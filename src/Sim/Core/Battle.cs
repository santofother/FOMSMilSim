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

    const float SearchRadius = 8f;
    const float SeparationRadius = 0.75f;
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

    public Battle(ScenarioDef scenario, IReadOnlyDictionary<string, UnitDef> units, ITerrain terrain = null)
    {
        Seed = scenario.Seed;
        Terrain = terrain ?? new FlatTerrain();
        _updateSoldier = UpdateSoldier;

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

        UpdateRouting();
        _orders.Step(dt);
        Array.Copy(s.Yaw, s.PrevYaw, n);

        // WHY: each soldier writes only its own slots; damage to others is an order-independent Interlocked.Add (C3).
        Parallel.For(0, n, _updateSoldier);
        s.SwapPositions();

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
        CheckVictory();
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
            int t = -1;
            bool pressingAttack = reg.Engaged && reg.TargetRegiment >= 0;
            if (pressingAttack)
            {
                t = s.Target[i];
                s.Retarget[i] -= dt;
                // WHY: read the tick-start alive snapshot, not Hp, which other threads change mid-phase (C3).
                if (t < 0 || !_aliveSnap[t] || s.RegimentId[t] != reg.TargetRegiment || s.Retarget[i] <= 0)
                {
                    t = FindNearestEnemy(i, p, SearchRadius, reg.TargetRegiment);
                    s.Retarget[i] = 0.5f;
                }
            }
            else if (reg.Activity == RegimentActivity.Idle && reg.Order is not Withdraw)
            {
                t = FindNearestEnemy(i, p, def.Reach);
            }
            s.Target[i] = t;

            if (t >= 0)
            {
                var d = s.Pos[t] - p;
                float dist = d.Length();
                faceTarget = t;
                if (dist > def.Reach)
                {
                    desired = d / dist * RegimentOrders.Pace(reg);
                }
                else
                {
                    desired = Vector2.Zero;
                    s.AttackCd[i] -= dt;
                    if (s.AttackCd[i] <= 0)
                    {
                        s.AttackCd[i] = def.AttackInterval + Rand(i, 11) * def.AttackIntervalRandom;
                        attacked = true;
                        if (Rand(i, 13) < def.MeleeHitChance)
                        {
                            int dmg = def.WeaponDamage + (int)(Rand(i, 17) * def.WeaponDamageRandom);
                            Interlocked.Add(ref s.Hp[t], -dmg);
                        }
                    }
                }
            }
            else
            {
                var delta = reg.Slot(i - reg.First) - p;
                float len = delta.Length();
                float speed = MathF.Min(len * 2f, RegimentOrders.Pace(reg));
                desired = len > 0.22f ? delta / len * speed : Vector2.Zero;
            }
        }

        desired += Separation(i, p);

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

    Vector2 Separation(int i, Vector2 p)
    {
        var s = Soldiers;
        Vector2 push = Vector2.Zero;
        var sorted = _grid.Sorted;
        SpatialGrid.CellCoords(p, out int cx, out int cz);
        for (int z = cz - 1; z <= cz + 1; z++)
        for (int x = cx - 1; x <= cx + 1; x++)
        {
            _grid.CellRange(x, z, out int a, out int b);
            for (int k = a; k < b; k++)
            {
                int j = sorted[k];
                if (j == i) continue;
                var d = p - s.Pos[j];
                float d2 = d.LengthSquared();
                if (d2 >= SeparationRadius * SeparationRadius) continue;
                if (d2 < 1e-6f) { d = Jitter(i + j, 19); d2 = d.LengthSquared() + 1e-6f; }
                float dist = MathF.Sqrt(d2);
                push += d / dist * (SeparationRadius - dist) * 12f;
            }
        }
        return push;
    }

    int FindNearestEnemy(int i, Vector2 p, float radius, int targetRegiment = -1)
    {
        var s = Soldiers;
        byte team = s.Team[i];
        var sorted = _grid.Sorted;
        int r = (int)MathF.Ceiling(radius / SpatialGrid.CellSize);
        SpatialGrid.CellCoords(p, out int cx, out int cz);
        int best = -1; float bestD2 = radius * radius;
        for (int z = cz - r; z <= cz + r; z++)
        for (int x = cx - r; x <= cx + r; x++)
        {
            _grid.CellRange(x, z, out int a, out int b);
            for (int k = a; k < b; k++)
            {
                int j = sorted[k];
                if (s.Team[j] == team || (targetRegiment >= 0 && s.RegimentId[j] != targetRegiment)) continue;
                float d2 = Vector2.DistanceSquared(p, s.Pos[j]);
                if (d2 < bestD2) { bestD2 = d2; best = j; }
            }
        }
        return best;
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
