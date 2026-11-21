// Tests for Combat/Melee.cs and Movement/ (duel rules, events, spacing, fatigue, performance). Owner: S04
using System;
using System.Diagnostics;
using System.Numerics;
using FOMS.Sim;
using FOMS.Sim.Data;
using FOMS.Sim.Tests.Balance;
using Xunit;
using Xunit.Abstractions;

namespace FOMS.Sim.Tests;

public class MeleeTests(ITestOutputHelper output)
{
    static UnitDef Spear => TestData.Units()["gb_spearmen"];

    [Fact]
    public void SideComesFromTheDefendersFacing()
    {
        var facing = new Vector2(0, 1);
        Assert.Equal(MeleeSide.Front, MeleeRules.SideOf(facing, Vector2.Zero, new Vector2(0.3f, 1f)));
        Assert.Equal(MeleeSide.Flank, MeleeRules.SideOf(facing, Vector2.Zero, new Vector2(1f, 0.1f)));
        Assert.Equal(MeleeSide.Rear, MeleeRules.SideOf(facing, Vector2.Zero, new Vector2(-0.2f, -1f)));
    }

    [Fact]
    public void FlankAndRearHitMoreOftenAndIgnoreTheShield()
    {
        var u = Spear;
        float front = MeleeRules.HitChance(u, 0, u, 0, MeleeSide.Front);
        float flank = MeleeRules.HitChance(u, 0, u, 0, MeleeSide.Flank);
        float rear = MeleeRules.HitChance(u, 0, u, 0, MeleeSide.Rear);
        Assert.True(front < flank && flank < rear);
        Assert.InRange(front, 0.4f, 0.6f);
        Assert.True(MeleeRules.ShieldBlock(u, MeleeSide.Front, u.Shield - 0.01f));
        Assert.False(MeleeRules.ShieldBlock(u, MeleeSide.Front, u.Shield + 0.01f));
        Assert.False(MeleeRules.ShieldBlock(u, MeleeSide.Flank, 0f));
        Assert.False(MeleeRules.ShieldBlock(u, MeleeSide.Rear, 0f));
    }

    [Fact]
    public void ArmourReducesNormalDamageButNotArmourPiercing()
    {
        var atk = MeleeSetup.Variant(Spear, "a", d => { d.WeaponDamage = 20; d.ArmourPiercing = 10; });
        var bare = MeleeSetup.Variant(Spear, "b", d => d.Armour = 0);
        var plate = MeleeSetup.Variant(Spear, "c", d => d.Armour = 80);
        Assert.Equal(30, MeleeRules.Damage(atk, bare, 1f, 0.5f));
        // Full armour roll: 20 × (1 − 0.8) + 10 = 14. Half roll: 20 × (1 − 0.4) + 10 = 22.
        Assert.Equal(14, MeleeRules.Damage(atk, plate, 1f, 0.5f));
        Assert.Equal(22, MeleeRules.Damage(atk, plate, 0f, 0.5f));
        var apOnly = MeleeSetup.Variant(atk, "d", d => d.WeaponDamage = 0);
        Assert.Equal(10, MeleeRules.Damage(apOnly, plate, 1f, 0.5f));
    }

    [Fact]
    public void FatigueBandsWeakenAttackDefenceAndSpeed()
    {
        Assert.Equal(FatigueBand.Fresh, FatigueRules.BandOf(0.1f));
        Assert.Equal(FatigueBand.Winded, FatigueRules.BandOf(0.3f));
        Assert.Equal(FatigueBand.Tired, FatigueRules.BandOf(0.6f));
        Assert.Equal(FatigueBand.Exhausted, FatigueRules.BandOf(0.9f));
        var u = Spear;
        Assert.True(MeleeRules.HitChance(u, 0.9f, u, 0f, MeleeSide.Front) < MeleeRules.HitChance(u, 0f, u, 0f, MeleeSide.Front));
        Assert.True(MeleeRules.HitChance(u, 0f, u, 0.9f, MeleeSide.Front) > MeleeRules.HitChance(u, 0f, u, 0f, MeleeSide.Front));
        Assert.True(FatigueRules.SpeedMul(0.9f) < FatigueRules.SpeedMul(0.6f));
        Assert.True(FatigueRules.SpeedMul(0.6f) < FatigueRules.SpeedMul(0f));
    }

    [Fact]
    public void FightingTiresAndRestingRecovers()
    {
        var b = MeleeSetup.Duel("gb_spearmen", "gb_spearmen", 1);
        var r = b.Regiments[0];
        while (b.Regiments[0].Activity != RegimentActivity.Fighting && b.Tick < 900) b.Step();
        float atContact = r.Fatigue;
        for (int t = 0; t < 600; t++) b.Step();
        Assert.True(r.Fatigue > atContact + 0.03f, $"fatigue {atContact:F3} -> {r.Fatigue:F3} after 20 s of melee");

        var rest = MeleeSetup.Duel("gb_spearmen", "gb_spearmen", 1);
        rest.Issue(new Halt(0));
        rest.Issue(new Halt(1));
        rest.Regiments[0].Fatigue = 0.5f;
        for (int t = 0; t < 300; t++) rest.Step();
        Assert.True(rest.Regiments[0].Fatigue < 0.47f, $"rested fatigue {rest.Regiments[0].Fatigue:F3}");

        // Exhaustion slows the march.
        var slow = MeleeSetup.Duel("gb_spearmen", "gb_spearmen", 1);
        slow.Regiments[0].Fatigue = 0.95f;
        Assert.True(slow.Regiments[0].Fatigue >= FatigueRules.ExhaustedAt);
        float fresh = Pace(MeleeSetup.Duel("gb_spearmen", "gb_spearmen", 1));
        Assert.True(Pace(slow) < 0.8f * fresh);
    }

    static float Pace(Battle b)
    {
        var r = b.Regiments[0];
        b.Issue(new Move(0, r.Centre + new Vector2(0, 30), new Vector2(0, 1), r.Width, Run: false));
        var start = r.Centre;
        for (int t = 0; t < 30; t++) b.Step();
        return Vector2.Distance(start, r.Centre);
    }

    [Fact]
    public void MeleeHitEventsNameAttackerAndVictimAndReportKills()
    {
        var b = MeleeSetup.Duel("gb_spearmen", "gb_spearmen", 3);
        int hits = 0, kills = 0;
        while (b.Winner < 0 && b.Tick < 9000)
        {
            b.Step();
            foreach (var e in b.Events)
            {
                if (e.Kind != EventKind.MeleeHit) continue;
                hits++;
                Assert.NotEqual(b.Soldiers.Team[e.Soldier], b.Soldiers.Team[e.Other]);
                Assert.Equal(b.Soldiers.RegimentId[e.Soldier], e.Regiment);
                if (!MeleeRules.Killed(b, e)) continue;
                kills++;
                var victim = e.Other;
                Assert.Contains(b.Events, k => k.Kind == EventKind.SoldierKilled && k.Soldier == victim);
            }
        }
        Assert.True(hits > 100 && kills > 30, $"hits {hits}, kills {kills}");
    }

    [Fact]
    public void LinesKeepDepthInMelee()
    {
        float ratio = 0;
        for (uint seed = 1; seed <= 3; seed++)
        {
            var b = MeleeSetup.Duel("gb_spearmen", "gb_spearmen", seed);
            float start = (MeleeSetup.Depth(b, b.Regiments[0]) + MeleeSetup.Depth(b, b.Regiments[1])) / 2;
            int contact = RunToContact(b);
            for (int t = 0; t < 900 && b.Winner < 0; t++) b.Step();
            Assert.True(b.Tick - contact >= 900, "battle ended before 30 s of melee");
            float now = (MeleeSetup.Depth(b, b.Regiments[0]) + MeleeSetup.Depth(b, b.Regiments[1])) / 2;
            ratio += now / start / 3;
        }
        output.WriteLine($"[Balance] depth after 30 s of melee: {ratio:P0}");
        Assert.True(ratio > 0.6f, $"depth after 30 s is {ratio:P0} of the starting depth");
    }

    [Fact]
    public void LinesMeetAtAContactZoneWithoutInterpenetrating()
    {
        var b = MeleeSetup.Duel("gb_spearmen", "gb_spearmen", 2);
        RunToContact(b);
        for (int t = 0; t < 450; t++) b.Step();
        // Blue faces +Z, red faces −Z. The contact line is half-way between the two front centres.
        float mid = (b.Regiments[0].Centre.Y + b.Regiments[1].Centre.Y) / 2;
        int wrong = 0, alive = 0;
        var s = b.Soldiers;
        for (int i = 0; i < s.Count; i++)
        {
            if (!s.IsAlive(i)) continue;
            alive++;
            float ahead = (s.Pos[i].Y - mid) * (s.Team[i] == 0 ? 1 : -1);
            if (ahead > 1f) wrong++;
        }
        Assert.True(wrong < alive * 0.05f, $"{wrong}/{alive} soldiers are more than 1 m past the contact line");
    }

    [Fact]
    public void HeavierRegimentGainsGround()
    {
        var units = TestData.Units();
        var heavy = MeleeSetup.Variant(units["gb_spearmen"], "t_heavy", d => d.Mass = 2.5f);
        units[heavy.Id] = heavy;
        var b = MeleeSetup.Duel(heavy.Id, "gb_spearmen", 1, units);
        RunToContact(b);
        float y0 = b.Regiments[1].Centre.Y;
        for (int t = 0; t < 300; t++) b.Step();
        Assert.True(b.Regiments[1].Centre.Y > y0 + 1f, $"light regiment moved {b.Regiments[1].Centre.Y - y0:F2} m");

        var even = MeleeSetup.Duel("gb_spearmen", "gb_spearmen", 1);
        RunToContact(even);
        float e0 = even.Regiments[1].Centre.Y;
        for (int t = 0; t < 300; t++) even.Step();
        Assert.InRange(even.Regiments[1].Centre.Y - e0, -0.3f, 0.3f);
    }

    /// <summary>Steps until the first melee blow lands; returns that tick.</summary>
    internal static int RunToContact(Battle b)
    {
        while (b.Tick < 1800)
        {
            b.Step();
            foreach (var e in b.Events)
                if (e.Kind == EventKind.MeleeHit) return b.Tick;
        }
        throw new Exception("no melee contact within 60 s");
    }
}

[CollectionDefinition("MeleePerf", DisableParallelization = true)]
public class MeleePerfCollection { }

[Collection("MeleePerf")]
public class MeleePerfTests(ITestOutputHelper output)
{
    [Fact]
    public void FourThousandSoldiersInMeleeStayWithinBudget()
    {
        // 20 v 20 regiments of 100 (10 files × 10 ranks), every pair meeting head-on.
        var sc = new ScenarioDef { Id = "melee_perf", Seed = 1 };
        for (int team = 0; team < 2; team++)
        {
            var side = new SideDef { AutoAttack = true };
            for (int k = 0; k < 20; k++)
                side.Regiments.Add(new RegimentPlacement
                {
                    Unit = "gb_spearmen", Cols = 10,
                    Position = new[] { -118.75f + k * 12.5f, team == 0 ? -4f : 4f },
                    Facing = new[] { 0f, team == 0 ? 1f : -1f },
                });
            sc.Sides.Add(side);
        }
        var b = new Battle(sc, TestData.Units()) { Log = _ => { } };
        Assert.Equal(4000, b.Soldiers.Count);
        MeleeTests.RunToContact(b);
        for (int t = 0; t < 90; t++) b.Step(); // everyone closes up and the JIT warms

        const int ticks = 300;
        var sw = Stopwatch.StartNew();
        for (int t = 0; t < ticks; t++) b.Step();
        double ms = sw.Elapsed.TotalMilliseconds / ticks;
        int fighting = 0;
        foreach (var r in b.Regiments) if (r.Engaged) fighting++;
        output.WriteLine($"[Perf] melee 4000 soldiers: {ms:F2} ms/tick, {fighting}/40 regiments in melee, " +
                         $"{b.AlivePerTeam[0] + b.AlivePerTeam[1]} alive");
        Console.WriteLine($"[Perf] melee 4000 soldiers: {ms:F2} ms/tick");
        Assert.True(fighting >= 30, $"only {fighting} regiments fighting");
        // Budget is 4 ms (ARCHITECTURE §4); allow 2× head-room for a busy test machine.
        Assert.True(ms < 8.0, $"{ms:F2} ms per tick");
    }
}
