// Tests for src/Sim/Core (determinism C3, ranges C4, orders C5, events C6, data C7, M0 outcome). Owner: S01
using System;
using System.Collections.Generic;
using System.Numerics;
using FOMS.Sim;
using FOMS.Sim.Data;
using Xunit;

namespace FOMS.Sim.Tests;

public class CoreTests
{
    static ulong PositionHash(Battle b)
    {
        ulong h = 1469598103934665603UL;
        var s = b.Soldiers;
        for (int i = 0; i < s.Count; i++)
        {
            h = (h ^ BitConverter.SingleToUInt32Bits(s.Pos[i].X)) * 1099511628211UL;
            h = (h ^ BitConverter.SingleToUInt32Bits(s.Pos[i].Y)) * 1099511628211UL;
            h = (h ^ (uint)s.Hp[i]) * 1099511628211UL;
        }
        return h;
    }

    static ulong Run(uint seed, int ticks)
    {
        var b = TestData.Battle("m0_clash", seed);
        for (int t = 0; t < ticks; t++) b.Step();
        return PositionHash(b);
    }

    [Fact]
    public void SameSeedGivesIdenticalBattle()
    {
        Assert.Equal(Run(1, 900), Run(1, 900));
    }

    [Fact]
    public void DifferentSeedGivesDifferentBattle()
    {
        Assert.NotEqual(Run(1, 900), Run(2, 900));
    }

    [Fact]
    public void RegimentsOwnContiguousRanges()
    {
        var b = TestData.Battle("m0_clash");
        int next = 0;
        foreach (var r in b.Regiments)
        {
            Assert.Equal(next, r.First);
            for (int i = r.First; i < r.First + r.Count; i++) Assert.Equal(r.Id, b.Soldiers.RegimentId[i]);
            next += r.Count;
        }
        Assert.Equal(b.Soldiers.Count, next);
    }

    [Fact]
    public void M0ClashLoadsAs20x100()
    {
        var b = TestData.Battle("m0_clash");
        Assert.Equal(20, b.Regiments.Length);
        Assert.Equal(2000, b.Soldiers.Count);
        Assert.Equal(1000, b.AlivePerTeam[0]);
        Assert.Equal(1000, b.AlivePerTeam[1]);
    }

    [Fact]
    public void M0BattleProducesWinnerWithinThreeMinutes()
    {
        var b = TestData.Battle("m0_clash");
        int maxTicks = (int)(180f / Battle.TickDt);
        int kills = 0, routs = 0;
        while (b.Winner < 0 && b.Tick < maxTicks)
        {
            b.Step();
            foreach (var e in b.Events)
            {
                if (e.Kind == EventKind.SoldierKilled) kills++;
                if (e.Kind == EventKind.RegimentRouted) routs++;
            }
        }
        Assert.True(b.Winner >= 0, $"no winner after {b.Tick} ticks");
        Assert.True(kills > 0 && routs > 0, $"kills {kills}, routs {routs}");
    }

    [Fact]
    public void IssuedOrderIsAppliedNextTickAndLogged()
    {
        var log = new List<string>();
        var sc = DataLoader.LoadScenario(TestData.DataDir, "m0_clash");
        var b = new Battle(sc, TestData.Units()) { Log = log.Add };
        var order = new Move(3, new Vector2(10, 0), new Vector2(0, 1), 20f, false);
        b.Issue(order);
        Assert.Null(b.Regiments[3].Order);
        b.Step();
        Assert.Same(order, b.Regiments[3].Order);
        Assert.Contains(log, l => l.StartsWith("[Orders]"));
    }

    [Fact]
    public void ApplyDefsChangesStatsNotHeadCount()
    {
        var b = TestData.Battle("m0_clash");
        var units = TestData.Units();
        units["gb_spearmen"].Speed = 9f;
        units["gb_spearmen"].Soldiers = 5;
        b.ApplyDefs(units);
        Assert.Equal(9f, b.Regiments[0].Def.Speed);
        Assert.Equal(100, b.Regiments[0].Count);
    }

    [Fact]
    public void RandIsInUnitRangeAndSeeded()
    {
        var a = TestData.Battle("m0_clash", 1);
        var c = TestData.Battle("m0_clash", 2);
        for (int i = 0; i < 1000; i++)
        {
            float r = a.Rand(i, 1);
            Assert.InRange(r, 0f, 0.99999994f);
        }
        Assert.NotEqual(a.Rand(5, 1), c.Rand(5, 1));
        Assert.Equal(a.Rand(5, 1), TestData.Battle("m0_clash", 1).Rand(5, 1));
    }
}
