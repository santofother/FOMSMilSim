// Morale, routing, rallying and the general (S05). Battle calls Update once per tick, after deaths.
// Owner: S05
// Public API: MoraleSystem (Battle.Morale): Commander, GeneralOf, GeneralDead, Cooldown, Breaks, FleeDir, Accepts
using System;
using System.Collections.Generic;
using System.Numerics;

namespace FOMS.Sim;

/// <summary>Per-regiment morale state and the rules that move it. Sequential and deterministic (C3).</summary>
public sealed class MoraleSystem
{
    readonly Battle _b;
    readonly bool[] _autoAttack;
    readonly Vector2[] _fleeDir;
    /// <summary>The commander's soldier index per general regiment, -1 = not a general.</summary>
    readonly int[] _commander;
    /// <summary>First general regiment per team, -1 = none.</summary>
    readonly int[] _general;
    readonly bool[] _generalDead;
    readonly float[] _generalShock;

    // Per regiment.
    readonly int[] _prevAlive, _breaks, _onField;
    readonly float[] _recentLoss, _recentKills, _recentLost, _hits, _flankHits, _rearHits;
    readonly float[] _shock, _boost, _boostRate, _clear, _rallyCall, _reform, _cooldown;
    readonly Vector2[] _centroid;
    readonly List<int> _brokeNow = new();
    /// <summary>Per team: starting men, and men dead, routing or gone (this tick).</summary>
    readonly int[] _armyStart, _armyOut;

    internal MoraleSystem(Battle battle, bool[] autoAttack)
    {
        _b = battle;
        var regs = battle.Regiments;
        int r = regs.Length, teams = battle.TeamCount;
        _autoAttack = autoAttack;
        _prevAlive = new int[r]; _breaks = new int[r]; _onField = new int[r];
        _recentLoss = new float[r]; _recentKills = new float[r]; _recentLost = new float[r];
        _hits = new float[r]; _flankHits = new float[r]; _rearHits = new float[r];
        _shock = new float[r]; _boost = new float[r]; _boostRate = new float[r]; _clear = new float[r];
        _rallyCall = new float[r]; _reform = new float[r]; _cooldown = new float[r];
        _centroid = new Vector2[r];
        _commander = new int[r];
        _general = new int[teams];
        _generalDead = new bool[teams];
        _generalShock = new float[teams];
        _armyStart = new int[teams];
        _armyOut = new int[teams];
        foreach (var reg in regs) _armyStart[reg.Team] += reg.Count;
        Array.Fill(_general, -1);

        // WHY: "own side's map edge" = behind the army as it deployed (scenarios have no edges yet, see handoff).
        _fleeDir = new Vector2[teams];
        var centre = new Vector2[teams];
        var count = new int[teams];
        foreach (var reg in regs)
        {
            _prevAlive[reg.Id] = reg.Alive;
            _fleeDir[reg.Team] -= reg.Facing;
            centre[reg.Team] += reg.Centre;
            count[reg.Team]++;
            _commander[reg.Id] = -1;
            if (MoraleRules.IsGeneral(reg.Def))
            {
                // The commander stands in the middle of the second rank (protected until the front gives way).
                _commander[reg.Id] = reg.First + Math.Min(reg.Count - 1, reg.Cols + reg.Cols / 2);
                if (_general[reg.Team] < 0) _general[reg.Team] = reg.Id;
            }
        }
        for (int t = 0; t < teams; t++)
        {
            if (_fleeDir[t].LengthSquared() > 1e-4f) _fleeDir[t] = Vector2.Normalize(_fleeDir[t]);
            else if (count[t] > 0 && centre[t].LengthSquared() > 1e-4f) _fleeDir[t] = Vector2.Normalize(centre[t]);
            else _fleeDir[t] = new Vector2(0, t == 0 ? -1 : 1);
        }
        foreach (var reg in regs) RefreshState(reg);
    }

    // ---- read API ----

    /// <summary>Soldier index of the general himself in a general's regiment, -1 for other regiments.</summary>
    public int Commander(int regiment) => _commander[regiment];
    /// <summary>The team's general regiment, -1 = none.</summary>
    public int GeneralOf(int team) => _general[team];
    public bool GeneralDead(int team) => _generalDead[team];
    /// <summary>Seconds until the regiment can use an ability again (0 = ready).</summary>
    public float Cooldown(int regiment) => _cooldown[regiment];
    /// <summary>Times the regiment has broken (2 = shattered).</summary>
    public int Breaks(int regiment) => _breaks[regiment];
    /// <summary>Unit direction a team's routers run in (toward its own map edge).</summary>
    public Vector2 FleeDir(int team) => _fleeDir[team];
    /// <summary>Mean position of the regiment's soldiers still on the field (last tick).</summary>
    public Vector2 Centroid(int regiment) => _centroid[regiment];

    /// <summary>Broken, shattered and re-forming regiments ignore orders.</summary>
    public bool Accepts(Regiment reg) => !reg.Routing && !reg.Fled && _reform[reg.Id] <= 0;

    // ---- the general's abilities ----

    /// <summary>UseAbility order (C5). "rally": boosts a wavering or broken friend in range; cooldown.</summary>
    internal bool UseAbility(UseAbility order)
    {
        var regs = _b.Regiments;
        var gen = regs[order.RegimentId];
        if (order.Ability != MoraleRules.AbilityRally) return false;
        if (_commander[gen.Id] < 0 || !_b.Soldiers.IsAlive(_commander[gen.Id])) return false;
        if (!Accepts(gen) || _cooldown[gen.Id] > 0) return false;
        if ((uint)order.Target >= (uint)regs.Length) return false;
        var t = regs[order.Target];
        if (t.Team != gen.Team || t.Alive <= 0 || t.Fled) return false;
        if (t.MoraleState is not (MoraleState.Wavering or MoraleState.Broken)) return false;
        if (Vector2.Distance(_centroid[gen.Id], _centroid[t.Id]) > MoraleRules.RallyRange) return false;

        _cooldown[gen.Id] = MoraleRules.RallyCooldown;
        GiveBoost(t.Id, MoraleRules.RallyBoost, MoraleRules.RallyBoostTime);
        if (t.Routing) _rallyCall[t.Id] = MoraleRules.RallyCallTime;
        else t.Morale = MathF.Min(100f, t.Morale + MoraleRules.RallyInstant);
        return true;
    }

    void GiveBoost(int id, float amount, float time)
    {
        if (amount <= _boost[id]) return;
        _boost[id] = amount;
        _boostRate[id] = amount / time;
    }

    // ---- per tick ----

    /// <param name="contact">Soldiers in melee contact per regiment, this tick (MeleeSystem.Contact).</param>
    internal void Update(float dt, List<BattleEvent> events, int[] contact)
    {
        var s = _b.Soldiers;
        var regs = _b.Regiments;
        int r = regs.Length;

        // Centroids of the men still on the field.
        Array.Clear(_onField);
        Array.Clear(_centroid);
        int n = s.Count;
        for (int i = 0; i < n; i++)
        {
            if (!s.IsAlive(i)) continue;
            int id = s.RegimentId[i];
            _onField[id]++;
            _centroid[id] += s.Pos[i];
        }
        foreach (var reg in regs)
        {
            if (_onField[reg.Id] > 0) _centroid[reg.Id] /= _onField[reg.Id];
            else _centroid[reg.Id] = reg.Centre;
            if (reg.Routing)
            {
                reg.Centre = _centroid[reg.Id];
                reg.Facing = _fleeDir[reg.Team];
                if (_onField[reg.Id] == 0 && !reg.Fled) reg.Fled = true;
            }
        }

        // Fade the recent tallies, then add this tick's blows.
        float hitDecay = MathF.Exp(-dt / MoraleRules.HitTau);
        float meleeDecay = MathF.Exp(-dt / MoraleRules.MeleeTau);
        float lossDecay = MathF.Exp(-dt / MoraleRules.RecentLossTau);
        for (int id = 0; id < r; id++)
        {
            _hits[id] *= hitDecay; _flankHits[id] *= hitDecay; _rearHits[id] *= hitDecay;
            _recentKills[id] *= meleeDecay; _recentLost[id] *= meleeDecay;
            _recentLoss[id] *= lossDecay;
        }
        foreach (var e in events)
        {
            if (e.Kind != EventKind.MeleeHit) continue;
            int v = s.RegimentId[e.Other];
            switch (MeleeRules.SideOf(regs[v].Facing, s.Pos[e.Other], s.Pos[e.Soldier]))
            {
                case MeleeSide.Flank: _flankHits[v]++; break;
                case MeleeSide.Rear: _rearHits[v]++; break;
            }
            _hits[v]++;
            if (MeleeRules.Killed(_b, e)) _recentKills[e.Regiment]++;
        }

        // The general.
        for (int t = 0; t < _general.Length; t++)
        {
            int g = _general[t];
            if (g < 0 || _generalDead[t] || s.State[_commander[g]] != SoldierState.Dead) continue;
            _generalDead[t] = true;
            _generalShock[t] = MoraleRules.GeneralKilledShock;
            int c = _commander[g];
            events.Add(new BattleEvent(EventKind.GeneralKilled, c, g, -1, s.Pos[c]));
            _b.Log($"[Morale] tick {_b.Tick}: the general of team {t} is killed");
        }
        for (int t = 0; t < _generalShock.Length; t++)
            _generalShock[t] = MathF.Max(0, _generalShock[t] - MoraleRules.GeneralKilledShock / MoraleRules.GeneralKilledFade * dt);

        Array.Clear(_armyOut);
        foreach (var reg in regs) _armyOut[reg.Team] += reg.Routing || reg.Fled ? reg.Count : reg.Count - reg.Alive;

        _brokeNow.Clear();
        foreach (var reg in regs)
        {
            int id = reg.Id;
            int lost = _prevAlive[id] - reg.Alive;
            _prevAlive[id] = reg.Alive;
            if (lost > 0)
            {
                _recentLoss[id] += lost / (float)reg.Count;
                _recentLost[id] += lost;
            }
            _cooldown[id] = MathF.Max(0, _cooldown[id] - dt);
            _shock[id] = MathF.Max(0, _shock[id] - MoraleRules.FriendBrokeShock / MoraleRules.ShockFade * dt);
            _boost[id] = MathF.Max(0, _boost[id] - _boostRate[id] * dt);
            _rallyCall[id] = MathF.Max(0, _rallyCall[id] - dt);
            if (reg.Alive <= 0 || reg.Fled) { reg.MoraleReasons.Clear(); continue; }

            if (reg.Routing) { UpdateBroken(reg, dt, events); continue; }

            float target = Target(reg, contact);
            float m = reg.Morale;
            m += Math.Clamp(target - m, -MoraleRules.FallRate * dt, MoraleRules.RiseRate * dt);
            reg.Morale = Math.Clamp(m, 0f, 100f);

            if (reg.Morale < MoraleRules.BreakAt) { Break(reg, events); continue; }
            var before = reg.MoraleState;
            RefreshState(reg);
            if (reg.MoraleState == MoraleState.Wavering && before is MoraleState.Eager or MoraleState.Steady)
            {
                events.Add(new BattleEvent(EventKind.RegimentWavering, -1, id, -1, reg.Centre));
                _b.Log($"[Morale] tick {_b.Tick}: regiment {id} (team {reg.Team}) wavers, morale {reg.Morale:F0}");
            }
            if (_reform[id] > 0)
            {
                _reform[id] -= dt;
                reg.Activity = RegimentActivity.Rallying;
                if (_reform[id] <= 0 && _autoAttack[reg.Team] && reg.Order is Halt) reg.Order = null;
            }
        }

        // Friends see each other break (after the loop, so the order of regiments doesn't matter).
        foreach (int id in _brokeNow)
        {
            var broke = regs[id];
            foreach (var f in regs)
            {
                if (f.Team != broke.Team || f == broke || f.Routing || f.Alive <= 0) continue;
                if (Vector2.Distance(_centroid[f.Id], _centroid[id]) > MoraleRules.RoutSeeRadius) continue;
                _shock[f.Id] = MathF.Min(MoraleRules.ShockCap, _shock[f.Id] + MoraleRules.FriendBrokeShock);
            }
        }
    }

    /// <summary>Target morale from the regiment's situation; fills <see cref="Regiment.MoraleReasons"/>.</summary>
    float Target(Regiment reg, int[] contact)
    {
        var why = reg.MoraleReasons;
        why.Clear();
        int id = reg.Id;
        var regs = _b.Regiments;
        float sum = reg.Def.Morale;
        why.Add(new MoraleReason(MoraleRules.RBase, reg.Def.Morale));

        void Add(string label, float v)
        {
            if (MathF.Abs(v) < 0.5f) return;
            why.Add(new MoraleReason(label, v));
            sum += v;
        }

        Add(MoraleRules.RLosses, -MoraleRules.LossWeight * 100f * (1f - reg.Alive / (float)reg.Count));
        Add(MoraleRules.RRate, -MathF.Min(MoraleRules.RateCap, MoraleRules.RateWeight * 100f * _recentLoss[id]));

        if (_hits[id] > 0.01f)
        {
            if (_flankHits[id] >= MoraleRules.MinSideHits)
                Add(MoraleRules.RFlank, -MoraleRules.FlankPenalty * MathF.Min(1f, _flankHits[id] / _hits[id] / MoraleRules.SideShareFull));
            if (_rearHits[id] >= MoraleRules.MinSideHits)
                Add(MoraleRules.RRear, -MoraleRules.RearPenalty * MathF.Min(1f, _rearHits[id] / _hits[id] / MoraleRules.SideShareFull));
        }
        if (id < contact.Length && contact[id] > 0)
        {
            float k = _recentKills[id], l = _recentLost[id];
            if (k > MoraleRules.WinRatio * l + 1f) Add(MoraleRules.RWinning, MoraleRules.WinningBonus);
            else if (l > MoraleRules.WinRatio * k + 1f) Add(MoraleRules.RLosing, -MoraleRules.LosingPenalty);
        }

        int routing = 0;
        foreach (var f in regs)
            if (f.Team == reg.Team && f != reg && f.Routing && !f.Fled && f.Alive > 0
                && Vector2.Distance(_centroid[f.Id], _centroid[id]) <= MoraleRules.RoutSeeRadius)
                routing++;
        Add(MoraleRules.RFriendsRouting, -MathF.Min(MoraleRules.FriendRoutingCap, routing * MoraleRules.FriendRoutingPenalty));
        Add(MoraleRules.RFriendBroke, -_shock[id]);
        Add(MoraleRules.RArmy, -MoraleRules.ArmyLossWeight * _armyOut[reg.Team] / MathF.Max(1, _armyStart[reg.Team]));

        if (_generalDead[reg.Team])
            Add(MoraleRules.RGeneralKilled, -(MoraleRules.GeneralKilledLasting + _generalShock[reg.Team]));
        else if (GeneralDistance(reg.Team, id) is float d)
        {
            if (d <= MoraleRules.GeneralNearRadius) Add(MoraleRules.RGeneralNear, MoraleRules.GeneralNearBonus);
            else if (d <= MoraleRules.GeneralSightRadius) Add(MoraleRules.RGeneralSight, MoraleRules.GeneralSightBonus);
        }
        Add(MoraleRules.RRallied, _boost[id]);

        switch (FatigueRules.BandOf(reg.Fatigue))
        {
            case FatigueBand.Fresh: Add(MoraleRules.RFresh, MoraleRules.FreshBonus); break;
            case FatigueBand.Tired: Add(MoraleRules.RTired, -MoraleRules.TiredPenalty); break;
            case FatigueBand.Exhausted: Add(MoraleRules.RExhausted, -MoraleRules.ExhaustedPenalty); break;
        }
        return sum;
    }

    /// <summary>Distance from the team's living, steady general to a regiment; null = no such general.</summary>
    float? GeneralDistance(int team, int id)
    {
        int g = _general[team];
        if (g < 0 || _generalDead[team]) return null;
        var gen = _b.Regiments[g];
        if (gen.Routing || gen.Fled || gen.Alive <= 0) return null;
        return Vector2.Distance(_centroid[g], _centroid[id]);
    }

    void Break(Regiment reg, List<BattleEvent> events)
    {
        int id = reg.Id;
        _breaks[id]++;
        _reform[id] = 0;
        _clear[id] = 0;
        _rallyCall[id] = 0;
        bool shattered = _breaks[id] >= 2;
        reg.Morale = 0;
        reg.MoraleState = shattered ? MoraleState.Shattered : MoraleState.Broken;
        reg.Activity = shattered ? RegimentActivity.Shattered : RegimentActivity.Routing;
        reg.Order = null;
        reg.TargetRegiment = -1;
        reg.Centre = _centroid[id];
        reg.Facing = _fleeDir[reg.Team];
        _brokeNow.Add(id);
        events.Add(new BattleEvent(EventKind.RegimentRouted, -1, id, -1, reg.Centre));
        if (shattered) events.Add(new BattleEvent(EventKind.RegimentShattered, -1, id, -1, reg.Centre));
        SetBrokenReasons(reg);
        _b.Log($"[Morale] tick {_b.Tick}: regiment {id} (team {reg.Team}) {(shattered ? "shatters" : "routs")}, " +
               $"{reg.Alive}/{reg.Count} left");
    }

    void UpdateBroken(Regiment reg, float dt, List<BattleEvent> events)
    {
        int id = reg.Id;
        SetBrokenReasons(reg);
        if (reg.Activity == RegimentActivity.Shattered) return;

        bool clear = true;
        foreach (var e in _b.Regiments)
        {
            if (e.Team == reg.Team || e.Routing || e.Fled || e.Alive <= 0) continue;
            if (Vector2.Distance(_centroid[e.Id], _centroid[id]) <= MoraleRules.ClearRadius) { clear = false; break; }
        }
        _clear[id] = clear ? _clear[id] + dt : 0;
        bool general = _rallyCall[id] > 0
                       || GeneralDistance(reg.Team, id) is float d && d <= MoraleRules.GeneralNearRadius;
        if (_clear[id] < MoraleRules.RallyClearTime || !general) return;

        // Rally: face the nearest enemy (or away from home) and re-form around where the men are.
        Vector2 face = -_fleeDir[reg.Team];
        float best = float.MaxValue;
        foreach (var e in _b.Regiments)
        {
            if (e.Team == reg.Team || e.Routing || e.Fled || e.Alive <= 0) continue;
            var to = _centroid[e.Id] - _centroid[id];
            float dd = to.LengthSquared();
            if (dd < best && dd > 1e-4f) { best = dd; face = Vector2.Normalize(to); }
        }
        int rows = (_onField[id] + reg.Cols - 1) / Math.Max(1, reg.Cols);
        reg.Facing = face;
        reg.Centre = _centroid[id] + face * (rows * reg.SlotSpacing * 0.5f);
        reg.Morale = MoraleRules.RallyMorale;
        RefreshState(reg);
        reg.Activity = RegimentActivity.Rallying;
        reg.Order = new Halt(id);
        reg.TargetRegiment = -1;
        _reform[id] = MoraleRules.ReformTime;
        _clear[id] = 0;
        _rallyCall[id] = 0;
        GiveBoost(id, MoraleRules.ReformBoost, MoraleRules.RallyBoostTime);
        events.Add(new BattleEvent(EventKind.RegimentRallied, -1, id, -1, reg.Centre));
        _b.Log($"[Morale] tick {_b.Tick}: regiment {id} (team {reg.Team}) rallies, {_onField[id]} men");
    }

    static void SetBrokenReasons(Regiment reg)
    {
        reg.MoraleReasons.Clear();
        reg.MoraleReasons.Add(reg.Activity == RegimentActivity.Shattered
            ? new MoraleReason(MoraleRules.RShattered, 0)
            : new MoraleReason(MoraleRules.RBroken, 0));
    }

    static void RefreshState(Regiment reg) =>
        reg.MoraleState = reg.MoraleState is MoraleState.Broken or MoraleState.Shattered
            ? MoraleRules.StateOf(reg.Morale) : MoraleRules.StateOf(reg.Morale, reg.MoraleState);
}
