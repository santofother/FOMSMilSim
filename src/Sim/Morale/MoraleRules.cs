// Morale rules: the numbers behind breaking, routing and rallying (GameDesign §3.4, §3.5). Owner: S05
// Public API: MoraleRules (constants, StateOf, IsGeneral), MoraleReason
using System;
using FOMS.Sim.Data;

namespace FOMS.Sim;

/// <summary>One line of a regiment's morale tooltip: what pushes its morale and by how much.</summary>
public readonly record struct MoraleReason(string Label, float Value);

/// <summary>
/// Morale model. Every tick a regiment's situation gives a <b>target</b> morale = unit morale + the modifiers below
/// (listed in <see cref="Regiment.MoraleReasons"/>). Morale falls toward the target fast and rises slowly, so a
/// sudden blow hurts more than the same losses taken slowly. Below <see cref="BreakAt"/> the regiment breaks.
/// </summary>
public static class MoraleRules
{
    // ---- states ----
    public const float EagerAt = 80f, WaverAt = 40f, BreakAt = 15f;
    /// <summary>A wavering regiment counts as steady again only this far above <see cref="WaverAt"/> (no flicker).</summary>
    public const float Hysteresis = 5f;
    /// <summary>Morale points per second toward the target: falling, rising.</summary>
    public const float FallRate = 8f, RiseRate = 1.5f;

    // ---- losses ----
    /// <summary>Target −(this × % of the regiment lost so far).</summary>
    public const float LossWeight = 0.5f;
    /// <summary>Target −(this × % lost recently); "recently" fades with <see cref="RecentLossTau"/>.</summary>
    public const float RateWeight = 1.6f, RateCap = 40f, RecentLossTau = 8f;

    // ---- melee ----
    /// <summary>Full penalty when this share of the blows landing on the regiment come from that side.</summary>
    public const float FlankPenalty = 20f, RearPenalty = 30f, SideShareFull = 0.4f;
    /// <summary>Landed blows fade with this time constant; fewer than <see cref="MinSideHits"/> don't count.</summary>
    public const float HitTau = 3f, MinSideHits = 2f;
    public const float WinningBonus = 8f, LosingPenalty = 10f, MeleeTau = 6f;
    /// <summary>Kills vs losses ratio (recent) that counts as winning or losing.</summary>
    public const float WinRatio = 1.5f;

    // ---- friends ----
    public const float RoutSeeRadius = 40f;
    /// <summary>Per routing friend within <see cref="RoutSeeRadius"/>, capped.</summary>
    public const float FriendRoutingPenalty = 10f, FriendRoutingCap = 20f;
    /// <summary>Shock when a friend within <see cref="RoutSeeRadius"/> breaks; fades over <see cref="ShockFade"/> s.</summary>
    public const float FriendBrokeShock = 12f, ShockCap = 16f, ShockFade = 15f;
    /// <summary>Target −(this × share of the army's men dead, routing or gone): a beaten army gives up.</summary>
    public const float ArmyLossWeight = 30f;

    // ---- the general ----
    public const float GeneralNearRadius = 60f, GeneralNearBonus = 12f;
    public const float GeneralSightRadius = 150f, GeneralSightBonus = 5f;
    /// <summary>Army-wide when the general falls: a shock that fades to a lasting penalty.</summary>
    public const float GeneralKilledShock = 20f, GeneralKilledLasting = 8f, GeneralKilledFade = 40f;
    /// <summary>Rally ability: range, cooldown, instant boost (if not broken), lasting boost fading over its time.</summary>
    public const float RallyRange = 70f, RallyCooldown = 45f, RallyInstant = 15f, RallyBoost = 30f, RallyBoostTime = 30f;
    /// <summary>A rally order lets a broken regiment rally as if the general were near, for this long.</summary>
    public const float RallyCallTime = 15f;

    // ---- fatigue ----
    public const float FreshBonus = 5f, TiredPenalty = 5f, ExhaustedPenalty = 15f;

    // ---- rallying ----
    /// <summary>A broken regiment with no enemy within this radius for <see cref="RallyClearTime"/> s, near its general, rallies.</summary>
    public const float ClearRadius = 80f, RallyClearTime = 6f;
    /// <summary>Morale right after rallying, the boost that keeps it from breaking again at once, and the re-form time.</summary>
    public const float RallyMorale = 25f, ReformBoost = 15f, ReformTime = 4f;

    public const string AbilityRally = "rally";

    /// <summary>State with hysteresis: a band is left only once morale is <see cref="Hysteresis"/> past its edge.</summary>
    public static MoraleState StateOf(float morale, MoraleState current)
    {
        var s = StateOf(morale);
        if (current == MoraleState.Wavering && s == MoraleState.Steady && morale < WaverAt + Hysteresis) return current;
        if (current == MoraleState.Eager && s == MoraleState.Steady && morale >= EagerAt - Hysteresis) return current;
        return s;
    }

    public static MoraleState StateOf(float morale) =>
        morale >= EagerAt ? MoraleState.Eager
        : morale >= WaverAt ? MoraleState.Steady
        : morale >= BreakAt ? MoraleState.Wavering
        : MoraleState.Broken;

    /// <summary>A general's regiment: the trait "general" (or the unit id gb_general).</summary>
    public static bool IsGeneral(UnitDef def) =>
        def.Id == "gb_general" || Array.IndexOf(def.Traits ?? Array.Empty<string>(), "general") >= 0;

    // Reason labels (constants: the list is rebuilt every tick without allocating).
    public const string RBase = "Unit morale", RLosses = "Losses", RRate = "Heavy casualties",
        RFlank = "Attacked in flank", RRear = "Attacked in rear", RWinning = "Winning the melee",
        RLosing = "Losing the melee", RFriendsRouting = "Friends routing nearby", RFriendBroke = "Saw friends break",
        RGeneralNear = "General nearby", RGeneralSight = "General in sight", RGeneralKilled = "General killed", RArmy = "The army is beaten",
        RRallied = "Rallied", RFresh = "Fresh", RTired = "Tired", RExhausted = "Exhausted",
        RBroken = "Broken", RShattered = "Shattered";
}
