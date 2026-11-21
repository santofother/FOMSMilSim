// Fatigue (C4b Regiment.Fatigue): rates from stamina, bands and their stat penalties. Owner: S04.
// Public API: FatigueBand, FatigueRules (BandOf, CombatMul, SpeedMul, rates, Update)
using System;

namespace FOMS.Sim;

/// <summary>Coarse fatigue band shown to the player: fresh → winded → tired → exhausted.</summary>
public enum FatigueBand { Fresh, Winded, Tired, Exhausted }

/// <summary>How fatigue builds up, recovers and weakens a regiment. All rates are per second at stamina 100.</summary>
public static class FatigueRules
{
    public const float WindedAt = 0.25f, TiredAt = 0.5f, ExhaustedAt = 0.75f;

    /// <summary>Fatigue gained per second while running (also while routing).</summary>
    public const float RunRate = 0.010f;
    /// <summary>Fatigue gained per second while any soldier of the regiment is in melee.</summary>
    public const float FightRate = 0.006f;
    /// <summary>Fatigue gained per second while marching at walking pace.</summary>
    public const float WalkRate = 0.0005f;
    /// <summary>Fatigue recovered per second while standing still out of combat.</summary>
    public const float RestRate = 0.008f;

    public static FatigueBand BandOf(float fatigue) =>
        fatigue >= ExhaustedAt ? FatigueBand.Exhausted
        : fatigue >= TiredAt ? FatigueBand.Tired
        : fatigue >= WindedAt ? FatigueBand.Winded
        : FatigueBand.Fresh;

    /// <summary>Multiplier on melee attack and melee defence.</summary>
    public static float CombatMul(float fatigue) => BandOf(fatigue) switch
    {
        FatigueBand.Fresh => 1f,
        FatigueBand.Winded => 0.92f,
        FatigueBand.Tired => 0.8f,
        _ => 0.62f,
    };

    /// <summary>Multiplier on walk, run and push speed.</summary>
    public static float SpeedMul(float fatigue) => BandOf(fatigue) switch
    {
        FatigueBand.Fresh => 1f,
        FatigueBand.Winded => 0.95f,
        FatigueBand.Tired => 0.85f,
        _ => 0.7f,
    };

    /// <summary>
    /// Advances one regiment's fatigue by <paramref name="dt"/>. Gains scale with 100 / stamina, recovery with
    /// stamina / 100. Fighting outweighs running, running outweighs walking.
    /// </summary>
    public static void Update(Regiment reg, bool fighting, bool running, bool moving, float dt)
    {
        float stamina = MathF.Max(reg.Def.Stamina, 1f) / 100f;
        float rate;
        if (fighting) rate = (running ? RunRate : FightRate) / stamina;
        else if (running) rate = RunRate / stamina;
        else if (moving) rate = WalkRate / stamina;
        else rate = -RestRate * stamina;
        reg.Fatigue = Math.Clamp(reg.Fatigue + rate * dt, 0f, 1f);
    }
}
