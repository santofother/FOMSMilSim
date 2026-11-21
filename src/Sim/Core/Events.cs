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
    // TODO(S06): ChargeImpact. TODO(S07): ProjectileFired. TODO(S05): RegimentRallied.
}

/// <summary>One event. Fields not used by a kind are -1 / zero.</summary>
public readonly record struct BattleEvent(EventKind Kind, int Soldier, int Regiment, int Other, Vector2 Pos);
