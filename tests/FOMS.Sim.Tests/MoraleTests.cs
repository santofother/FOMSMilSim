// Morale, routing, rallying and the general (S05). Owner: S05
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using FOMS.Sim;
using FOMS.Sim.Data;
using FOMS.Sim.Tests.Balance;
using Xunit;
using Xunit.Abstractions;

namespace FOMS.Sim.Tests;

public class MoraleTests(ITestOutputHelper output)
{
    static readonly float[] North = { 0f, 1f }, South = { 0f, -1f };

    [Fact]
    public void RoutCascadesToNeighbours()
    {
        // A line of seven; the middle three are shaky (unit morale 30). Break the centre one.
        var units = TestData.Units();
        units["t_shaky"] = MeleeSetup.Variant(units["gb_spearmen"], "t_shaky", d => d.Morale = 30);
        var line = new List<RegimentPlacement>();
        foreach (float x in new[] { -90f, -60f, -30f, 0f, 30f, 60f, 90f })
            line.Add(Place(MathF.Abs(x) <= 30 ? "t_shaky" : "gb_spearmen", x, 0, North));
        var b = Build(units, line, new() { Place("gb_spearmen", 0, 150, South) });
        b.Regiments[3].Morale = 0;
        Run(b, 15f);

        string states = string.Join(" ", b.Regiments.Take(7).Select(r => $"{r.MoraleState}:{r.Morale:F0}"));
        output.WriteLine($"[Morale] line after 15 s: {states}");
        Assert.True(b.Regiments[3].Routing);
        Assert.True(b.Regiments[2].Routing && b.Regiments[4].Routing, $"shaky neighbours should break: {states}");
        foreach (int id in new[] { 0, 1, 5, 6 }) Assert.False(b.Regiments[id].Routing, $"regiment {id} broke: {states}");
        // The next ones out felt it, the outermost much less.
        Assert.True(b.Regiments[1].Morale < b.Regiments[0].Morale - 3f, states);
    }

    [Fact]
    public void RallyWorksOnlyClearOfEnemies()
    {
        // Break a regiment in front of its general. Its men run back past him.
        static Battle Setup(bool enemyNear) => Build(TestData.Units(),
            new() { Place("gb_spearmen", 0, -20, North), Place("gb_general", 0, -60, North) },
            new() { enemyNear ? Place("gb_spearmen", 0, -78, North) : Place("gb_spearmen", 0, 150, South) });

        var clear = Setup(enemyNear: false);
        clear.Regiments[0].Morale = 0;
        float rallied = RunUntil(clear, 20f, b => b.Events.Any(e => e.Kind == EventKind.RegimentRallied));
        output.WriteLine($"[Morale] rallied after {rallied:F1} s with {clear.Regiments[0].Alive} men");
        Assert.True(rallied < 15f, "a broken regiment clear of enemies, near its general, should rally");
        var r = clear.Regiments[0];
        Assert.Equal(RegimentActivity.Rallying, r.Activity);
        Assert.Equal(MoraleState.Wavering, r.MoraleState);
        Assert.InRange(r.Morale, MoraleRules.BreakAt, MoraleRules.WaverAt);
        Run(clear, MoraleRules.ReformTime + 1f);
        clear.Issue(new Halt(0));
        clear.Step();
        Assert.IsType<Halt>(r.Order);
        Assert.False(r.Routing);

        var near = Setup(enemyNear: true);
        near.Regiments[0].Morale = 0;
        float never = RunUntil(near, 40f, b => b.Events.Any(e => e.Kind == EventKind.RegimentRallied));
        Assert.True(never >= 40f, "rallied with an enemy regiment close by");
    }

    [Fact]
    public void ShatteredNeverReturns()
    {
        var b = Build(TestData.Units(),
            new() { Place("gb_spearmen", 0, -20, North), Place("gb_general", 0, -60, North) },
            new() { Place("gb_spearmen", 0, 150, South) });
        b.Regiments[0].Morale = 0;
        RunUntil(b, 20f, x => x.Events.Any(e => e.Kind == EventKind.RegimentRallied));
        Run(b, MoraleRules.ReformTime + 1f);
        Assert.False(b.Regiments[0].Routing);

        b.Regiments[0].Morale = 0;
        b.Step();
        Assert.Contains(b.Events, e => e.Kind == EventKind.RegimentShattered && e.Regiment == 0);
        Assert.Equal(MoraleState.Shattered, b.Regiments[0].MoraleState);
        Assert.Equal(2, b.Morale.Breaks(0));
        b.Issue(new UseAbility(1, MoraleRules.AbilityRally, 0));
        float back = RunUntil(b, 40f, x => x.Events.Any(e => e.Kind == EventKind.RegimentRallied));
        Assert.True(back >= 40f, "a shattered regiment rallied");
        Assert.Equal(0f, b.Morale.Cooldown(1)); // the rally order was refused
        Assert.True(b.Regiments[0].Fled, "a shattered regiment runs off the field");
    }

    [Fact]
    public void BrokenRegimentIgnoresOrdersAndRunsToItsOwnEdge()
    {
        var b = Build(TestData.Units(), new() { Place("gb_spearmen", 0, -20, North) },
                      new() { Place("gb_spearmen", 0, 150, South) });
        b.Regiments[0].Morale = 0;
        b.Step();
        Assert.Contains(b.Events, e => e.Kind == EventKind.RegimentRouted && e.Regiment == 0);
        Assert.Equal(MoraleState.Broken, b.Regiments[0].MoraleState);
        b.Issue(new Move(0, new Vector2(0, 100), new Vector2(0, 1), 20, false));
        b.Step();
        Assert.Null(b.Regiments[0].Order);
        float start = b.Morale.Centroid(0).Y;
        Run(b, 5f);
        Assert.True(b.Morale.Centroid(0).Y < start - 15f, "routers should run back toward their own side");
        Run(b, 40f);
        Assert.True(b.Regiments[0].Fled);
        Assert.True(b.Regiments[0].Alive > 0, "nobody chased them: they left the field alive");
    }

    [Fact]
    public void GeneralKilledShocksTheArmy()
    {
        static Battle Setup() => Build(TestData.Units(),
            new() { Place("gb_general", 0, -40, North), Place("gb_spearmen", -30, 0, North), Place("gb_spearmen", 30, 0, North) },
            new() { Place("gb_spearmen", 0, 150, South) });
        var control = Setup();
        var b = Setup();
        Run(control, 3f);
        Run(b, 3f);
        int general = b.Morale.Commander(0);
        Assert.True(general >= 0);
        b.Soldiers.Hp[general] = 0;
        int events = 0;
        for (int t = 0; t < 150; t++)
        {
            b.Step();
            control.Step();
            events += b.Events.Count(e => e.Kind == EventKind.GeneralKilled);
        }
        Assert.Equal(1, events);
        Assert.True(b.Morale.GeneralDead(0));
        for (int id = 0; id < 3; id++)
        {
            output.WriteLine($"[Morale] regiment {id}: {control.Regiments[id].Morale:F0} → {b.Regiments[id].Morale:F0}");
            Assert.True(b.Regiments[id].Morale < control.Regiments[id].Morale - 15f, $"regiment {id} barely noticed");
            Assert.Contains(b.Regiments[id].MoraleReasons, m => m.Label == MoraleRules.RGeneralKilled);
        }
        b.Regiments[1].Morale = 30;
        b.Issue(new UseAbility(0, MoraleRules.AbilityRally, 1));
        b.Step();
        Assert.Equal(0f, b.Morale.Cooldown(0)); // a dead general rallies nobody
    }

    [Fact]
    public void RallyAbilityBoostsAWaveringRegimentThenCoolsDown()
    {
        var units = TestData.Units();
        units["t_shaky"] = MeleeSetup.Variant(units["gb_spearmen"], "t_shaky", d => d.Morale = 20);
        var b = Build(units,
            new() { Place("gb_general", 0, -40, North), Place("t_shaky", 0, 0, North), Place("t_shaky", 150, 0, North) },
            new() { Place("gb_spearmen", 0, 160, South) });
        Run(b, 2f);
        Assert.Equal(MoraleState.Wavering, b.Regiments[1].MoraleState);

        b.Issue(new UseAbility(0, MoraleRules.AbilityRally, 2)); // out of range
        b.Step();
        Assert.Equal(0f, b.Morale.Cooldown(0));

        float before = b.Regiments[1].Morale;
        b.Issue(new UseAbility(0, MoraleRules.AbilityRally, 1));
        b.Step();
        Assert.True(b.Regiments[1].Morale >= before + MoraleRules.RallyInstant - 1f);
        Assert.Contains(b.Regiments[1].MoraleReasons, m => m.Label == MoraleRules.RRallied);
        Assert.True(b.Morale.Cooldown(0) > MoraleRules.RallyCooldown - 1f);

        Run(b, 10f);
        float mid = b.Regiments[1].Morale;
        b.Regiments[1].Morale = 25;
        b.Issue(new UseAbility(0, MoraleRules.AbilityRally, 1)); // still cooling down
        b.Step();
        Assert.True(b.Regiments[1].Morale < 26f, $"second rally inside the cooldown worked (was {mid:F0})");
    }

    [Fact]
    public void FlankAttackShowsInMoraleReasons()
    {
        var sc = MeleeSetup.Scenario(1,
            new RegimentPlacement { Unit = "gb_spearmen", Position = new[] { 30f, -2.2f }, Facing = new[] { -1f, 0f } },
            new RegimentPlacement { Unit = "gb_spearmen", Position = new[] { 0f, 0f }, Facing = North },
            autoAttack: false);
        var b = new Battle(sc, TestData.Units()) { Log = _ => { } };
        b.Issue(new Attack(0, 1, Run: false));
        bool flanked = false;
        while (!b.Regiments[1].Routing && b.Time < 120f)
        {
            b.Step();
            flanked |= b.Regiments[1].MoraleReasons.Any(m => m.Label == MoraleRules.RFlank && m.Value < -5f);
        }
        Assert.True(flanked);
        Assert.True(b.Regiments[1].Routing, "the flanked regiment should break");
    }

    [Fact]
    public void PursuersCanCutDownRouters()
    {
        var b = Build(TestData.Units(), new() { Place("gb_spearmen", 0, 0, North) },
                      new() { Place("gb_light_cav", 0, 40, South) });
        b.Regiments[0].Morale = 0;
        b.Step();
        b.Issue(new Attack(1, 0, Run: true));
        b.Step();
        Assert.IsType<Attack>(b.Regiments[1].Order);
        Run(b, 20f);
        int lost = b.Regiments[0].Count - b.Regiments[0].Alive;
        output.WriteLine($"[Morale] cavalry cut down {lost} routers in 20 s");
        Assert.True(lost >= 5, $"pursuing cavalry killed only {lost}");
    }

    [Fact]
    public void MoraleIsDeterministic()
    {
        static string Run1()
        {
            var b = TestData.Battle("sandbox_vs_ai", 3);
            for (int t = 0; t < 2400; t++) b.Step();
            return string.Join(",", b.Regiments.Select(r => $"{r.Morale:F3}{r.MoraleState}"));
        }
        Assert.Equal(Run1(), Run1());
    }

    // ---- helpers ----

    internal static RegimentPlacement Place(string unit, float x, float z, float[] facing) =>
        new() { Unit = unit, Position = new[] { x, z }, Facing = facing };

    internal static Battle Build(Dictionary<string, UnitDef> units, List<RegimentPlacement> team0,
                                 List<RegimentPlacement> team1, bool autoAttack = false)
    {
        var sc = new ScenarioDef
        {
            Id = "morale_test", Seed = 1,
            Sides = { new SideDef { AutoAttack = autoAttack }, new SideDef { AutoAttack = autoAttack } },
        };
        sc.Sides[0].Regiments.AddRange(team0);
        sc.Sides[1].Regiments.AddRange(team1);
        return new Battle(sc, units) { Log = _ => { } };
    }

    internal static void Run(Battle b, float seconds)
    {
        int ticks = (int)(seconds / Battle.TickDt);
        for (int t = 0; t < ticks; t++) b.Step();
    }

    /// <summary>Steps until <paramref name="done"/> (checked after each Step); returns the seconds taken, or the cap.</summary>
    internal static float RunUntil(Battle b, float cap, Func<Battle, bool> done)
    {
        float start = b.Time;
        while (b.Time - start < cap)
        {
            b.Step();
            if (done(b)) return b.Time - start;
        }
        return cap;
    }
}
