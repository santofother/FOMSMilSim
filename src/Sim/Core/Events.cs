// Events: what happened during the last tick (contract C6). No C# events inside the sim.
// Owner: S01 (type + first kinds). Cards add kinds.
// Public API: EventKind, BattleEvent
using System.Numerics;

namespace FOMS.Sim;

/// <summary>Kinds of battle event. Cards append new kinds at the end.</summary>
public enum EventKind
{
    /// <summary>Soldier = victim, Regiment = victim's regiment, Pos = where.</summary>
    SoldierKilled,
    /// <summary>Regiment = the routing regiment, Pos = its centre.</summary>
    RegimentRouted,
    /// <summary>
    /// A landed melee blow (S04). Soldier = attacker, Regiment = attacker's regiment, Other = victim,
    /// Pos = victim position. MeleeRules.Killed(battle, e) tells whether it killed (valid until the next Step).
    /// </summary>
    MeleeHit,
    // FIX(S05): morale events. Regiment = the regiment, Pos = its centre.
    /// <summary>Morale fell from Steady/Eager to Wavering.</summary>
    RegimentWavering,
    /// <summary>A broken regiment re-forms.</summary>
    RegimentRallied,
    /// <summary>Broke a second time: it won't rally again (a RegimentRouted is sent the same tick).</summary>
    RegimentShattered,
    /// <summary>Soldier = the general, Regiment = his regiment, Pos = where he fell.</summary>
    GeneralKilled,
    // TODO(S06): ChargeImpact. TODO(S07): ProjectileFired.
}

/// <summary>One event. Fields not used by a kind are -1 / zero.</summary>
public readonly record struct BattleEvent(EventKind Kind, int Soldier, int Regiment, int Other, Vector2 Pos);
