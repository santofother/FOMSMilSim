// Balance tests for morale (S05): flanks and the general change when a regiment breaks. Owner: S05
using System;
using System.Collections.Generic;
using System.Linq;
using FOMS.Sim;
using FOMS.Sim.Data;
using Xunit;
using Xunit.Abstractions;

namespace FOMS.Sim.Tests.Balance;

public class MoraleBalanceTests(ITestOutputHelper output)
{
    const float Cap = 240f;

    [Fact]
    public void FlankAttackCausesEarlierRout()
    {
        float frontT = 0, flankT = 0, frontLeft = 0, flankLeft = 0;
        for (uint seed = 1; seed <= 3; seed++)
        {
            var (t0, a0) = DefenderBreak(flanked: false, general: false, seed);
            var (t1, a1) = DefenderBreak(flanked: true, general: false, seed);
            frontT += t0 / 3; flankT += t1 / 3; frontLeft += a0 / 3f; flankLeft += a1 / 3f;
        }
        output.WriteLine($"[Balance] defender breaks after {frontT:F0} s of melee ({frontLeft:F0} men left) frontally, " +
                         $"{flankT:F0} s ({flankLeft:F0} left) flanked");
        Assert.True(flankT < 0.75f * frontT, $"flanked {flankT:F0} s vs frontal {frontT:F0} s");
        Assert.True(flankLeft > frontLeft + 10, $"flanked broke with {flankLeft:F0} men vs {frontLeft:F0}");
    }

    [Fact]
    public void GeneralNearbyDelaysRout()
    {
        float without = 0, with = 0;
        for (uint seed = 1; seed <= 3; seed++)
        {
            without += DefenderBreak(flanked: false, general: false, seed).seconds / 3;
            with += DefenderBreak(flanked: false, general: true, seed).seconds / 3;
        }
        output.WriteLine($"[Balance] frontal defender breaks after {without:F0} s alone, {with:F0} s with the general behind it");
        Assert.True(with >= 1.25f * without, $"with general {with:F0} s vs alone {without:F0} s");
    }

    /// <summary>
    /// A halted spearmen regiment (team 1) is attacked by swordsmen from the front or the flank. Returns seconds of
    /// melee until it breaks (the cap if it never does) and the men it still has then.
    /// </summary>
    static (float seconds, int alive) DefenderBreak(bool flanked, bool general, uint seed)
    {
        var at = flanked ? new[] { 30f, -2.2f } : new[] { 0f, 25f };
        var face = flanked ? new[] { -1f, 0f } : new[] { 0f, -1f };
        var sc = new ScenarioDef
        {
            Id = "morale_balance", Seed = seed,
            Sides =
            {
                new SideDef { Regiments = { new RegimentPlacement { Unit = "gb_swordsmen", Position = at, Facing = face } } },
                new SideDef { Regiments = { new RegimentPlacement { Unit = "gb_spearmen", Position = new[] { 0f, 0f }, Facing = new[] { 0f, 1f } } } },
            },
        };
        if (general)
            sc.Sides[1].Regiments.Add(new RegimentPlacement { Unit = "gb_general", Position = new[] { 0f, -35f }, Facing = new[] { 0f, 1f } });
        var b = new Battle(sc, TestData.Units()) { Log = _ => { } };
        b.Issue(new Attack(0, 1, Run: false));
        float contact = -1;
        var def = b.Regiments[1];
        while (!def.Routing && b.Time < Cap)
        {
            b.Step();
            if (contact < 0 && b.Events.Any(e => e.Kind == EventKind.MeleeHit)) contact = b.Time;
            if (b.Regiments[0].Routing) return (Cap - Math.Max(0, contact), def.Alive); // the attacker broke first
        }
        Assert.True(contact >= 0, "never met");
        return (b.Time - contact, def.Alive);
    }
}
