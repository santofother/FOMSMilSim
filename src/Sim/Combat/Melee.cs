// Melee v2: per-soldier duels (GameDesign §3.3), contact bookkeeping and MeleeHit events (C6). Owner: S04.
// Public API: MeleeSide, MeleeRules (SideOf, HitChance, ShieldBlock, Damage, Killed)
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading;
using FOMS.Sim.Data;

namespace FOMS.Sim;

/// <summary>Where a blow lands relative to the defender's regiment facing.</summary>
public enum MeleeSide { Front, Flank, Rear }

/// <summary>Pure duel maths. Deterministic: callers pass the random rolls (C3).</summary>
public static class MeleeRules
{
    /// <summary>Hit chance before attack and defence: two equal average soldiers hit about half the time.</summary>
    public const float BaseHit = 0.4f;
    public const float MinHit = 0.05f, MaxHit = 0.95f;
    /// <summary>Hit-chance bonus for blows into the flank / rear. Both ignore the shield.</summary>
    public const float FlankBonus = 0.2f, RearBonus = 0.3f;
    /// <summary>Cosine of the half-angle of the front arc (±60°) and of the rear arc (±60° behind).</summary>
    public const float FrontCos = 0.5f, RearCos = -0.5f;

    /// <summary>
    /// Which side of the defender a blow comes from: the angle between the defender regiment's facing and the
    /// direction from the victim to the attacker.
    /// </summary>
    public static MeleeSide SideOf(Vector2 defenderFacing, Vector2 victimPos, Vector2 attackerPos)
    {
        var d = attackerPos - victimPos;
        float len = d.Length();
        if (len < 1e-5f) return MeleeSide.Front;
        float cos = Vector2.Dot(d / len, defenderFacing);
        return cos >= FrontCos ? MeleeSide.Front : cos > RearCos ? MeleeSide.Flank : MeleeSide.Rear;
    }

    /// <summary>Chance that a swing lands: base + attack − defence (both scaled by fatigue) + side bonus.</summary>
    public static float HitChance(UnitDef attacker, float attackerFatigue, UnitDef defender, float defenderFatigue,
                                  MeleeSide side)
    {
        float atk = attacker.MeleeAttack * FatigueRules.CombatMul(attackerFatigue);
        float def = defender.MeleeDefence * FatigueRules.CombatMul(defenderFatigue);
        float bonus = side switch { MeleeSide.Flank => FlankBonus, MeleeSide.Rear => RearBonus, _ => 0f };
        return Math.Clamp(BaseHit + atk - def + bonus, MinHit, MaxHit);
    }

    /// <summary>A landed frontal hit is stopped by the shield with probability <c>shield</c>.</summary>
    public static bool ShieldBlock(UnitDef defender, MeleeSide side, float roll) =>
        side == MeleeSide.Front && roll < defender.Shield;

    /// <summary>
    /// Damage of a landed, unblocked hit. Normal damage (<c>weapon_damage</c>) is reduced by armour: between half
    /// and all of <c>armour</c> percent, by <paramref name="armourRoll"/>. Armour-piercing damage is added in full.
    /// The total varies ±20% by <paramref name="spreadRoll"/>. Always at least 1.
    /// </summary>
    public static int Damage(UnitDef attacker, UnitDef defender, float armourRoll, float spreadRoll)
    {
        float armour = Math.Clamp(defender.Armour / 100f, 0f, 0.95f) * (0.5f + 0.5f * armourRoll);
        float total = attacker.WeaponDamage * (1f - armour) + attacker.ArmourPiercing;
        return Math.Max(1, (int)MathF.Round(total * (0.8f + 0.4f * spreadRoll)));
    }

    /// <summary>
    /// For a <see cref="EventKind.MeleeHit"/> of the last Step: true if the blow's victim died that tick.
    /// Only valid until the next Step.
    /// </summary>
    public static bool Killed(Battle battle, in BattleEvent hit) =>
        hit.Kind == EventKind.MeleeHit && battle.Soldiers.State[hit.Other] == SoldierState.Dead
        && battle.Soldiers.StateTime[hit.Other] == 0f;
}

/// <summary>Per-tick melee state: who swung at whom, who is in contact, and which regiments are locked together.</summary>
internal sealed class MeleeSystem
{
    const uint SaltHit = 101, SaltShield = 103, SaltArmour = 107, SaltSpread = 109;

    readonly Battle _b;
    /// <summary>Victim of this tick's landed blow per attacker, -1 = none. Written only by the attacker (C3).</summary>
    int[] _hit = Array.Empty<int>();
    /// <summary>True when the soldier has an enemy within reach this tick. Written only by that soldier.</summary>
    bool[] _contact = Array.Empty<bool>();
    /// <summary>How far the soldier's target is beyond his reach (0 when in contact). Written only by him.</summary>
    float[] _gap = Array.Empty<float>();
    int[] _pairs = Array.Empty<int>();

    /// <summary>Soldiers per regiment with an enemy in reach (last tick).</summary>
    public int[] Contact { get; private set; } = Array.Empty<int>();
    /// <summary>Enemy regiment most of this regiment's fighters face, -1 = none (last tick).</summary>
    public int[] Opponent { get; private set; } = Array.Empty<int>();
    /// <summary>Soldiers per regiment who have a target but can't reach it (last tick).</summary>
    public int[] Reaching { get; private set; } = Array.Empty<int>();
    /// <summary>Mean distance those soldiers are short of their reach, metres (last tick).</summary>
    public float[] Gap { get; private set; } = Array.Empty<float>();
    /// <summary>
    /// Nearest targeted enemy along the regiment's facing (min of dot(enemy pos, facing)); +∞ = none (last tick).
    /// </summary>
    public float[] EnemyFront { get; private set; } = Array.Empty<float>();

    public MeleeSystem(Battle battle) => _b = battle;

    /// <summary>Sequential, before the soldier phase.</summary>
    public void BeginTick(int soldiers)
    {
        if (_hit.Length < soldiers)
        {
            _hit = new int[soldiers];
            _contact = new bool[soldiers];
            _gap = new float[soldiers];
        }
        Array.Fill(_hit, -1, 0, soldiers);
        Array.Clear(_contact, 0, soldiers);
        Array.Clear(_gap, 0, soldiers);
        int r = _b.Regiments.Length;
        if (Contact.Length != r)
        {
            Contact = new int[r];
            Opponent = new int[r];
            Reaching = new int[r];
            Gap = new float[r];
            EnemyFront = new float[r];
            _pairs = new int[r * r];
        }
    }

    /// <summary>Soldier phase (parallel): soldier <paramref name="i"/> stands within reach of <paramref name="t"/>.</summary>
    public void MarkContact(int i) => _contact[i] = true;

    /// <summary>Soldier phase (parallel): <paramref name="i"/> has a target <paramref name="gap"/> m beyond reach.</summary>
    public void MarkGap(int i, float gap) => _gap[i] = gap;

    /// <summary>
    /// Soldier phase (parallel): <paramref name="i"/> swings at <paramref name="t"/>. Damage lands through an
    /// order-independent Interlocked.Add (C3).
    /// </summary>
    public void Swing(int i, int t, Regiment attacker)
    {
        var s = _b.Soldiers;
        var victim = _b.Regiments[s.RegimentId[t]];
        var side = MeleeRules.SideOf(victim.Facing, s.Pos[t], s.Pos[i]);
        float chance = MeleeRules.HitChance(attacker.Def, attacker.Fatigue, victim.Def, victim.Fatigue, side);
        if (_b.Rand(i, SaltHit) >= chance) return;
        if (MeleeRules.ShieldBlock(victim.Def, side, _b.Rand(i, SaltShield))) return;
        int dmg = MeleeRules.Damage(attacker.Def, victim.Def, _b.Rand(i, SaltArmour), _b.Rand(i, SaltSpread));
        Interlocked.Add(ref s.Hp[t], -dmg);
        _hit[i] = t;
    }

    /// <summary>Sequential, after the soldier phase: one MeleeHit per landed blow, in soldier order (C3, C6).</summary>
    public void EmitHits(List<BattleEvent> events)
    {
        var s = _b.Soldiers;
        int n = s.Count;
        for (int i = 0; i < n; i++)
        {
            int t = _hit[i];
            if (t >= 0) events.Add(new BattleEvent(EventKind.MeleeHit, i, s.RegimentId[i], t, s.Pos[t]));
        }
    }

    /// <summary>Sequential: tallies contact per regiment and finds each regiment's main opponent.</summary>
    public void TallyContact()
    {
        var s = _b.Soldiers;
        int r = _b.Regiments.Length, n = s.Count;
        Array.Clear(Contact);
        Array.Clear(Reaching);
        Array.Clear(Gap);
        Array.Fill(EnemyFront, float.PositiveInfinity);
        Array.Clear(_pairs);
        var regs = _b.Regiments;
        for (int i = 0; i < n; i++)
        {
            int t = s.Target[i];
            if (t < 0 || !s.IsAlive(i)) continue;
            int a = s.RegimentId[i];
            EnemyFront[a] = MathF.Min(EnemyFront[a], Vector2.Dot(s.Pos[t], regs[a].Facing));
            if (!_contact[i])
            {
                Reaching[a]++;
                Gap[a] += _gap[i];
                continue;
            }
            Contact[a]++;
            _pairs[a * r + s.RegimentId[t]]++;
        }
        for (int a = 0; a < r; a++)
        {
            if (Reaching[a] > 0) Gap[a] /= Reaching[a];
            int best = -1, bestCount = 0;
            for (int b = 0; b < r; b++)
            {
                int c = _pairs[a * r + b];
                if (c > bestCount) { bestCount = c; best = b; }
            }
            Opponent[a] = best;
        }
    }
}
