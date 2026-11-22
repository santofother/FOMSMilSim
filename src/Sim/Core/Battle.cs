// Battle: the fixed-tick simulation loop (C2), seeded RNG (C3), orders queue (C5) and events (C6).
// Owner: S01.
// Public API: Battle (ctor, TickDt, Tick, Time, Seed, Soldiers, Regiments, Events, Winner, AlivePerTeam,
//             Terrain, Log, Issue, Step, ApplyDefs, Rand, RandAt, YawOf, LerpAngle)
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using FOMS.Sim.Data;
using FOMS.Sim.Terrain;

namespace FOMS.Sim;

/// <summary>One battle. Create from a scenario, call Issue() for orders and Step() once per tick.</summary>
public sealed class Battle
{
    /// <summary>CONTRACT (C2): every Step advances exactly this many seconds.</summary>
    public const float TickDt = 1f / 30f;

    const float MapRadius = 170f;

    public readonly uint Seed;
    public readonly SoldierStore Soldiers = new();
    public readonly Regiment[] Regiments;
    /// <summary>Events of the last Step (C6). Cleared at the start of the next Step.</summary>
    public readonly List<BattleEvent> Events = new();
    public readonly ITerrain Terrain;
    /// <summary>Living (or fled but not dead) soldiers per team.</summary>
    public readonly int[] AlivePerTeam;
    public int TeamCount => AlivePerTeam.Length;

    /// <summary>Number of Steps done.</summary>
    public int Tick { get; private set; }
    public float Time => Tick * TickDt;
    /// <summary>-1 = still fighting, team index = winner, TeamCount = mutual collapse.</summary>
    public int Winner { get; private set; } = -1;

    /// <summary>Log sink (the game points it at GD.Print). Prefix lines by system, e.g. [Orders].</summary>
    public Action<string> Log = Console.WriteLine;

    readonly List<Order> _pending = new();
    // FIX(S02): invoke the order system from the fixed tick and feed its formation state to soldier movement.
    readonly RegimentOrders _orders;
    readonly SpatialGrid _grid = new();
    bool[] _aliveSnap = Array.Empty<bool>();
    readonly Action<int> _updateSoldier;
    // FIX(S04): melee duels (Combat/Melee.cs) and melee spacing (Movement/MeleeSpacing.cs) plug into the tick.
    readonly MeleeSystem _melee;
    readonly MeleeSpacing _spacing;

    public Battle(ScenarioDef scenario, IReadOnlyDictionary<string, UnitDef> units, ITerrain terrain = null)
    {
        Seed = scenario.Seed;
        Terrain = terrain ?? new FlatTerrain();
        _updateSoldier = UpdateSoldier;
        _melee = new MeleeSystem(this);
        _spacing = new MeleeSpacing(this, _grid);

        var regs = new List<Regiment>();
        for (int team = 0; team < scenario.Sides.Count; team++)
        {
            foreach (var p in scenario.Sides[team].Regiments)
            {
                if (!units.TryGetValue(p.Unit, out var def))
                    throw new KeyNotFoundException($"[Sim] scenario '{scenario.Id}': unknown unit '{p.Unit}'");
                regs.Add(Spawn(regs.Count, team, def, p));
            }
        }
        Regiments = regs.ToArray();
        AlivePerTeam = new int[Math.Max(2, scenario.Sides.Count)];
        var autoAttack = new bool[scenario.Sides.Count];
        for (int team = 0; team < autoAttack.Length; team++) autoAttack[team] = scenario.Sides[team].AutoAttack;
        _orders = new RegimentOrders(Regiments, autoAttack, Terrain);
        foreach (var r in Regiments) AlivePerTeam[r.Team] += r.Count;
        Log($"[Sim] battle '{scenario.Id}' seed {Seed}: {Regiments.Length} regiments, {Soldiers.Count} soldiers");
    }

    Regiment Spawn(int id, int team, UnitDef def, RegimentPlacement p)
    {
        int n = p.Soldiers > 0 ? p.Soldiers : def.Soldiers;
        int cols = Math.Min(n, p.Cols > 0 ? p.Cols : Math.Max(4, (int)MathF.Round(MathF.Sqrt(n * 5f))));
        var reg = new Regiment
        {
            Id = id, Team = team, UnitId = def.Id, Def = def, Count = n, Alive = n,
            Cols = cols, Rows = (n + cols - 1) / cols, Width = cols * def.Spacing,
            Centre = p.PositionV, Facing = p.FacingV, Morale = def.Morale, Ammo = def.Ammo,
        };
        var s = Soldiers;
        reg.First = s.AddRange(n);
        float yaw = YawOf(reg.Facing);
        for (int k = 0; k < n; k++)
        {
            int i = reg.First + k;
            s.Pos[i] = s.PrevPos[i] = reg.Slot(k) + Jitter(i, 5) * 0.15f;
            s.Yaw[i] = s.PrevYaw[i] = yaw;
            s.Hp[i] = def.Hp;
            s.State[i] = SoldierState.Idle;
            s.RegimentId[i] = id;
            s.Team[i] = (byte)team;
            s.Target[i] = -1;
            s.AnimPhase[i] = Rand(i, 7) * MathF.Tau;
            s.Retarget[i] = Rand(i, 3) * 0.5f;
        }
        return reg;
    }

    /// <summary>Queues an order; it is applied at the start of the next Step (C5).</summary>
    public void Issue(Order order)
    {
        if (order == null || (uint)order.RegimentId >= (uint)Regiments.Length)
        {
            Log($"[Orders] rejected {order}: no such regiment");
            return;
        }
        _pending.Add(order);
    }

    /// <summary>Replaces live regiments' stats with new defs by unit id (hot reload, C7). Head counts unchanged.</summary>
    public void ApplyDefs(IReadOnlyDictionary<string, UnitDef> defs)
    {
        foreach (var r in Regiments)
        {
            if (!defs.TryGetValue(r.UnitId, out var d)) continue;
            r.Def = d;
            r.Width = r.Cols * r.SlotSpacing;
        }
    }

    /// <summary>Advances the battle by exactly TickDt.</summary>
    public void Step()
    {
        const float dt = TickDt;
        Events.Clear();
        Tick++;
        ApplyOrders();

        var s = Soldiers;
        int n = s.Count;
        if (_aliveSnap.Length < n) _aliveSnap = new bool[n];
        for (int i = 0; i < n; i++) _aliveSnap[i] = s.IsAlive(i);
        _grid.Build(s, _aliveSnap);
        _spacing.BeginTick(_aliveSnap); // FIX(S04)
        _melee.BeginTick(n); // FIX(S04)

        UpdateRouting();
        _orders.Step(dt, _melee.Contact); // FIX(S04c): last tick's contact tells attackers they've met the enemy
        Array.Copy(s.Yaw, s.PrevYaw, n);

        // WHY: each soldier writes only its own slots; damage to others is an order-independent Interlocked.Add (C3).
        Parallel.For(0, n, _updateSoldier);
        s.SwapPositions();
        _melee.EmitHits(Events); // FIX(S04)

        // Sequential phase: report local defensive melee and resolve deaths.
        for (int i = 0; i < n; i++)
        {
            var st = s.State[i];
            if (st == SoldierState.Dead || st == SoldierState.Gone) continue;
            var reg = Regiments[s.RegimentId[i]];
            if (reg.Activity == RegimentActivity.Idle && s.Target[i] >= 0)
                reg.Activity = RegimentActivity.Fighting;
            if (s.Hp[i] > 0) continue;
            s.State[i] = SoldierState.Dead;
            s.StateTime[i] = 0;
            s.Vel[i] = Vector2.Zero;
            reg.Alive--;
            AlivePerTeam[s.Team[i]]--;
            Events.Add(new BattleEvent(EventKind.SoldierKilled, i, reg.Id, -1, s.Pos[i]));
        }
        EndMelee(dt); // FIX(S04)
        CheckVictory();
    }

    // FIX(S04): contact tally, pushing by mass and fatigue, once per tick after deaths.
    void EndMelee(float dt)
    {
        _melee.TallyContact();
        _spacing.Push(_melee.Contact, _melee.Opponent, dt);
        _spacing.FollowUp(_melee.Contact, _melee.Reaching, _melee.Gap, _melee.EnemyFront, dt);
        foreach (var reg in Regiments)
        {
            if (reg.Alive <= 0) continue;
            bool moving = reg.Activity is RegimentActivity.Moving or RegimentActivity.Engaging;
            bool running = reg.Routing
                || (moving && RegimentOrders.Pace(reg) > reg.Def.Speed * FatigueRules.SpeedMul(reg.Fatigue) * 1.01f);
            FatigueRules.Update(reg, _melee.Contact[reg.Id] > 0, running, moving, dt);
        }
    }

    void ApplyOrders()
    {
        foreach (var o in _pending)
        {
            if (_orders.Apply(o)) Log($"[Orders] tick {Tick}: {o}");
            else Log($"[Orders] tick {Tick}: rejected {o}");
        }
        _pending.Clear();
    }

    void UpdateRouting()
    {
        foreach (var reg in Regiments)
        {
            var def = reg.Def;
            if (!reg.Routing && reg.Alive < reg.Count * def.RoutThreshold)
            {
                reg.Activity = RegimentActivity.Routing;
                reg.MoraleState = MoraleState.Routing;
                reg.Morale = 0;
                Events.Add(new BattleEvent(EventKind.RegimentRouted, -1, reg.Id, -1, reg.Centre));
                Log($"[Sim] tick {Tick}: regiment {reg.Id} (team {reg.Team}) routs, {reg.Alive}/{reg.Count} left");
            }
        }
    }

    void UpdateSoldier(int i)
    {
        const float dt = TickDt;
        var s = Soldiers;
        var st = s.State[i];
        var p = s.Pos[i];
        if (st == SoldierState.Dead || st == SoldierState.Gone)
        {
            s.NextPos[i] = p;
            if (st == SoldierState.Dead) s.StateTime[i] += dt;
            return;
        }

        var reg = Regiments[s.RegimentId[i]];
        var def = reg.Def;
        Vector2 desired;
        int faceTarget = -1;
        bool attacked = false;

        if (reg.Routing)
        {
            desired = -reg.Facing * def.RoutSpeed + Jitter(i, 9) * 0.8f;
            if (p.LengthSquared() > MapRadius * MapRadius)
            {
                s.State[i] = SoldierState.Gone;
                s.NextPos[i] = p;
                return;
            }
        }
        else
        {
            // FIX(S04): melee v2. Soldiers keep their (column-filled) slots and only step up to the enemy on a
            // leash; duels resolve by stats and the side the blow comes from.
            // FIX(S04c): men fight whoever is in reach whatever the regiment is doing (a marching block that meets
            // the enemy fights back instead of being squeezed through it). Withdrawing men don't.
            bool melee = reg.Order is not Withdraw;
            int t = _spacing.ChooseTarget(i, reg, p, melee, dt);
            s.Target[i] = t;
            desired = _spacing.Desired(i, reg, p, t, RegimentOrders.Pace(reg));
            if (t >= 0)
            {
                faceTarget = t;
                float dist = Vector2.Distance(s.Pos[t], p);
                if (dist > def.Reach) _melee.MarkGap(i, dist - def.Reach);
                else
                {
                    _melee.MarkContact(i);
                    s.AttackCd[i] -= dt;
                    if (s.AttackCd[i] <= 0)
                    {
                        s.AttackCd[i] = def.AttackInterval + Rand(i, 11) * def.AttackIntervalRandom;
                        attacked = true;
                        _melee.Swing(i, t, reg);
                    }
                }
            }
        }

        desired += _spacing.Separation(i, p); // FIX(S04)

        var v = Vector2.Lerp(s.Vel[i], desired, 1f - MathF.Exp(-8f * dt));
        // FIX(S02): soldiers also respect terrain while following their formation slots.
        var next = p + v * dt;
        bool walkable = Terrain.Walkable(next);
        s.Vel[i] = v = walkable ? v : Vector2.Zero;
        s.NextPos[i] = walkable ? next : p;
        float speedNow = v.Length();

        float targetYaw;
        if (faceTarget >= 0) targetYaw = YawOf(s.Pos[faceTarget] - p);
        else if (speedNow > 0.4f && reg.Order is not Withdraw) targetYaw = YawOf(v);
        else targetYaw = YawOf(reg.Facing);
        s.Yaw[i] = LerpAngle(s.Yaw[i], targetYaw, 1f - MathF.Exp(-10f * dt));

        if (attacked) { s.State[i] = SoldierState.Attack; s.StateTime[i] = 0; }
        else
        {
            s.StateTime[i] += dt;
            if (st != SoldierState.Attack || s.StateTime[i] > 0.5f)
            {
                var ns = speedNow > 0.4f ? SoldierState.Walk : SoldierState.Idle;
                if (ns != st) { s.State[i] = ns; s.StateTime[i] = 0; }
            }
        }
        s.AnimPhase[i] += speedNow * dt * 3.2f;
    }

    void CheckVictory()
    {
        if (Winner >= 0) return;
        int standing = 0, last = -1;
        for (int t = 0; t < TeamCount; t++)
        {
            bool up = false;
            foreach (var reg in Regiments)
                if (reg.Team == t && !reg.Routing && reg.Alive > 0) { up = true; break; }
            if (up) { standing++; last = t; }
        }
        if (standing == 1) Winner = last;
        else if (standing == 0) Winner = TeamCount;
        if (Winner >= 0) Log($"[Sim] tick {Tick}: battle over, winner {Winner}");
    }

    // ---- randomness (C3) ----

    /// <summary>
    /// CONTRACT (C3): the only source of randomness. Uniform [0, 1) from a hash of (seed, tick, soldier, salt).
    /// Use a distinct salt per call site.
    /// </summary>
    public float Rand(int soldier, uint salt) => RandAt(Tick, soldier, salt);

    /// <summary>Like Rand but for a fixed tick, e.g. tick 0 for a per-soldier constant.</summary>
    public float RandAt(int tick, int soldier, uint salt)
    {
        uint h = Hash(Seed * 0x9E3779B9u ^ Hash((uint)tick + 0x7F4A7C15u));
        h = Hash(h ^ (uint)soldier * 0x85EBCA6Bu);
        h = Hash(h ^ salt * 0xC2B2AE35u);
        return (h & 0xFFFFFF) / 16777216f;
    }

    /// <summary>Per-soldier constant offset in [-1, 1]² (same every tick).</summary>
    Vector2 Jitter(int i, uint salt) => new(RandAt(0, i, salt) * 2f - 1f, RandAt(0, i, salt + 1000) * 2f - 1f);

    static uint Hash(uint x)
    {
        x ^= x >> 16; x *= 0x7feb352d; x ^= x >> 15; x *= 0x846ca68b; x ^= x >> 16;
        return x;
    }

    // ---- maths helpers (C8) ----

    /// <summary>Yaw of a model facing -Z that should face <paramref name="dir"/> (C8).</summary>
    public static float YawOf(Vector2 dir) => MathF.Atan2(-dir.X, -dir.Y);

    /// <summary>Shortest-path angle lerp.</summary>
    public static float LerpAngle(float a, float b, float t)
    {
        float diff = MathF.IEEERemainder(b - a, MathF.Tau);
        return a + diff * t;
    }
}
