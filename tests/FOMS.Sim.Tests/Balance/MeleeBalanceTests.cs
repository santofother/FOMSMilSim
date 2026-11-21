// Balance tests for melee v2 + fatigue (S04). Wide ranges, several seeds. Owner: S04
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text.Json;
using FOMS.Sim;
using FOMS.Sim.Data;
using Xunit;
using Xunit.Abstractions;

namespace FOMS.Sim.Tests.Balance;

public class MeleeBalanceTests(ITestOutputHelper output)
{
    const int MaxTicks = (int)(300f / Battle.TickDt);

    [Fact]
    public void EqualRegimentsAreEven()
    {
        int[] wins = new int[3];
        for (uint seed = 1; seed <= 20; seed++)
        {
            var b = MeleeSetup.Duel("gb_spearmen", "gb_spearmen", seed);
            wins[Math.Min(2, FightToEnd(b))]++;
        }
        output.WriteLine($"[Balance] equal spearmen: {wins[0]} / {wins[1]} / {wins[2]} (team 0 / team 1 / both)");
        Assert.InRange(wins[0], 7, 13);
        Assert.InRange(wins[1], 7, 13);
    }

    [Fact]
    public void FlankedRegimentLosesBadly()
    {
        float frontal = 0, flank = 0;
        for (uint seed = 1; seed <= 3; seed++)
        {
            frontal += ExchangeRatio(flanked: false, seed);
            flank += ExchangeRatio(flanked: true, seed);
        }
        frontal /= 3; flank /= 3;
        output.WriteLine($"[Balance] defender losses per attacker loss: frontal {frontal:F2}, flank {flank:F2}");
        Assert.InRange(frontal, 0.6f, 1.6f);
        Assert.True(flank >= 2f, $"flanked defender lost {flank:F2} men per attacker lost (frontal {frontal:F2})");
        Assert.True(flank >= 1.6f * frontal, $"flank {flank:F2} vs frontal {frontal:F2}");
    }

    [Fact]
    public void ArmouredBeatUnarmouredOfSameCost()
    {
        // Same unit, but one spent its budget on armour and the other on a better weapon arm.
        var units = TestData.Units();
        var armoured = MeleeSetup.Variant(units["gb_spearmen"], "t_armoured", d => d.Armour = 45);
        var unarmoured = MeleeSetup.Variant(units["gb_spearmen"], "t_unarmoured", d =>
        {
            d.Armour = 10;
            d.MeleeAttack = 0.45f;
        });
        units[armoured.Id] = armoured;
        units[unarmoured.Id] = unarmoured;

        int armouredWins = 0;
        for (uint seed = 1; seed <= 10; seed++)
        {
            // Alternate sides so neither team index is favoured.
            bool flip = seed % 2 == 0;
            var b = MeleeSetup.Duel(flip ? unarmoured.Id : armoured.Id, flip ? armoured.Id : unarmoured.Id, seed, units);
            if (FightToEnd(b) == (flip ? 1 : 0)) armouredWins++;
        }
        output.WriteLine($"[Balance] armoured won {armouredWins}/10");
        Assert.True(armouredWins >= 8, $"armoured won {armouredWins}/10");
    }

    [Fact]
    public void TiredRegimentFightsWorse()
    {
        int tiredWins = 0;
        for (uint seed = 1; seed <= 10; seed++)
        {
            var b = MeleeSetup.Duel("gb_spearmen", "gb_spearmen", seed);
            int tired = (int)(seed % 2);
            b.Regiments[tired].Fatigue = 0.8f;
            if (FightToEnd(b) == tired) tiredWins++;
        }
        output.WriteLine($"[Balance] exhausted won {tiredWins}/10");
        Assert.True(tiredWins <= 2, $"exhausted regiment won {tiredWins}/10");
    }

    static int FightToEnd(Battle b)
    {
        while (b.Winner < 0 && b.Tick < MaxTicks) b.Step();
        Assert.True(b.Winner >= 0, $"no winner after {b.Tick} ticks");
        return b.Winner;
    }

    /// <summary>Defender soldiers lost per attacker soldier lost, until the defender routs.</summary>
    static float ExchangeRatio(bool flanked, uint seed)
    {
        // Defender (team 1) stands still facing +Z; the attacker (team 0) comes from in front or from its right.
        var at = flanked ? new[] { 30f, -2.2f } : new[] { 0f, 25f };
        var face = flanked ? new[] { -1f, 0f } : new[] { 0f, -1f };
        var sc = MeleeSetup.Scenario(seed,
            new RegimentPlacement { Unit = "gb_spearmen", Position = at, Facing = face },
            new RegimentPlacement { Unit = "gb_spearmen", Position = new[] { 0f, 0f }, Facing = new[] { 0f, 1f } },
            autoAttack: false);
        var b = new Battle(sc, TestData.Units()) { Log = _ => { } };
        b.Issue(new Attack(0, 1, Run: false));
        while (!b.Regiments[1].Routing && b.Tick < MaxTicks) b.Step();
        Assert.True(b.Regiments[1].Routing || b.Regiments[0].Routing, "fight never ended");
        int lostA = b.Regiments[0].Count - b.Regiments[0].Alive;
        int lostD = b.Regiments[1].Count - b.Regiments[1].Alive;
        return lostD / (float)Math.Max(1, lostA);
    }
}

/// <summary>Builders shared by the melee tests.</summary>
public static class MeleeSetup
{
    /// <summary>Two regiments 24 m apart facing each other; both auto-attack.</summary>
    public static Battle Duel(string unit0, string unit1, uint seed, Dictionary<string, UnitDef> units = null)
    {
        var sc = Scenario(seed,
            new RegimentPlacement { Unit = unit0, Position = new[] { 0f, -12f }, Facing = new[] { 0f, 1f } },
            new RegimentPlacement { Unit = unit1, Position = new[] { 0f, 12f }, Facing = new[] { 0f, -1f } });
        return new Battle(sc, units ?? TestData.Units()) { Log = _ => { } };
    }

    public static ScenarioDef Scenario(uint seed, RegimentPlacement team0, RegimentPlacement team1,
                                       bool autoAttack = true) => new()
    {
        Id = "melee_test",
        Seed = seed,
        Sides =
        {
            new SideDef { AutoAttack = autoAttack, Regiments = { team0 } },
            new SideDef { AutoAttack = autoAttack, Regiments = { team1 } },
        },
    };

    static readonly JsonSerializerOptions CloneOptions = new() { IncludeFields = true };

    /// <summary>A copy of <paramref name="def"/> with a new id and some stats changed.</summary>
    public static UnitDef Variant(UnitDef def, string id, Action<UnitDef> change)
    {
        var copy = JsonSerializer.Deserialize<UnitDef>(JsonSerializer.Serialize(def, CloneOptions), CloneOptions);
        copy.Id = id;
        change(copy);
        return copy;
    }

    /// <summary>Spread of a regiment's living soldiers along its facing (std dev, metres): its depth.</summary>
    public static float Depth(Battle b, Regiment r)
    {
        var s = b.Soldiers;
        double sum = 0, sum2 = 0;
        int n = 0;
        for (int i = r.First; i < r.First + r.Count; i++)
        {
            if (!s.IsAlive(i)) continue;
            double d = Vector2.Dot(s.Pos[i], r.Facing);
            sum += d; sum2 += d * d; n++;
        }
        if (n < 2) return 0;
        double mean = sum / n;
        return (float)Math.Sqrt(Math.Max(0, sum2 / n - mean * mean));
    }
}
