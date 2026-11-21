// Orders: every command the player or the AI can give a regiment (contract C5).
// Owner: S01 (types). Execution: S02 (src/Sim/Orders/).
// Public API: Order and its records, FormationKind
using System.Numerics;

namespace FOMS.Sim;

/// <summary>Formation shapes a regiment can take. TODO(S02): execution.</summary>
public enum FormationKind { Line, Column, Square, Wedge, Loose }

/// <summary>Base of all orders. Queued with Battle.Issue and applied at the start of the next tick.</summary>
public abstract record Order(int RegimentId);

/// <summary>Move so the formation's front centre ends at <paramref name="FrontCentre"/>, facing <paramref name="Facing"/>, <paramref name="Width"/> metres wide.</summary>
public sealed record Move(int RegimentId, Vector2 FrontCentre, Vector2 Facing, float Width, bool Run) : Order(RegimentId);

/// <summary>Close with and fight another regiment.</summary>
public sealed record Attack(int RegimentId, int TargetRegiment, bool Run) : Order(RegimentId);

/// <summary>Stop where you are.</summary>
public sealed record Halt(int RegimentId) : Order(RegimentId);

public sealed record SetFormation(int RegimentId, FormationKind Formation) : Order(RegimentId);

/// <summary>Missile units: shoot at targets of their own choosing.</summary>
public sealed record SetFireAtWill(int RegimentId, bool On) : Order(RegimentId);

/// <summary>Pull back out of melee, facing the enemy.</summary>
public sealed record Withdraw(int RegimentId) : Order(RegimentId);

/// <summary>Use a named ability. <paramref name="Target"/> is a regiment id, or -1 for none.</summary>
public sealed record UseAbility(int RegimentId, string Ability, int Target) : Order(RegimentId);
