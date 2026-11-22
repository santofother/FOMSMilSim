// S04c regiment contact: lines meet square, nobody walks through anybody, lines keep their shape. Owner: S04c
using System;
using System.Numerics;
using FOMS.Sim.Data;
using FOMS.Sim.Tests.Balance;
using Xunit;
using Xunit.Abstractions;

namespace FOMS.Sim.Tests;

public class ContactTests(ITestOutputHelper output)
{
    const float Dt = Battle.TickDt;
    static readonly float Cos15 = MathF.Cos(15f * MathF.PI / 180f);

    [Fact]
    public void AttackFromAngleMeetsSquare()
    {
        var b = AngledAttack(seed: 1);
        var att = b.Regiments[0];
        var def = b.Regiments[1];
        MeleeTests.RunToContact(b);
        Step(b, 90);

        float square = Vector2.Dot(att.Facing, -def.Facing);
        float lateral = MathF.Abs(Vector2.Dot(att.Centre - def.Centre, def.Right));
        int fighting = InReach(b, att, def);
        output.WriteLine($"[Contact] 45° attack: facing cos {square:F3}, lateral offset {lateral:F1} m, " +
                         $"{fighting}/{att.Cols} front men in reach");
        Assert.True(square >= Cos15, $"lines meet at {MathF.Acos(Math.Clamp(square, -1f, 1f)) * 180f / MathF.PI:F0}°");
        Assert.True(lateral <= 0.25f * def.Width, $"attacker is {lateral:F1} m off the enemy's centre");
        Assert.True(fighting >= att.Cols * 0.6f, $"only {fighting}/{att.Cols} men in reach");

        Step(b, 600);
        float inShape = InShape(b, att) + InShape(b, def);
        output.WriteLine($"[Contact] after 20 s: {inShape / 2:P0} of living men inside their formation");
        Assert.True(inShape / 2 >= 0.8f, $"only {inShape / 2:P0} of men inside their formation");
    }

    [Fact]
    public void AttackFromSideHitsFlankSquare()
    {
        // Defender faces +Z at the origin; the attacker stands off its right-hand end, turned a little away.
        var sc = MeleeSetup.Scenario(2,
            new RegimentPlacement { Unit = "gb_spearmen", Position = new[] { 35f, 6f }, Facing = new[] { -1f, -0.3f } },
            new RegimentPlacement { Unit = "gb_spearmen", Position = new[] { 0f, 0f }, Facing = new[] { 0f, 1f } },
            autoAttack: false);
        var b = new Battle(sc, TestData.Units()) { Log = _ => { } };
        var att = b.Regiments[0];
        var def = b.Regiments[1];
        b.Issue(new Attack(0, 1, Run: false));
        MeleeTests.RunToContact(b);
        Step(b, 90);

        float square = MathF.Abs(Vector2.Dot(att.Facing, def.Right));
        int flankHits = 0, hits = 0;
        for (int t = 0; t < 450; t++)
        {
            b.Step();
            foreach (var e in b.Events)
            {
                if (e.Kind != EventKind.MeleeHit || e.Regiment != att.Id) continue;
                hits++;
                if (MeleeRules.SideOf(def.Facing, b.Soldiers.Pos[e.Other], b.Soldiers.Pos[e.Soldier]) == MeleeSide.Flank)
                    flankHits++;
            }
        }
        output.WriteLine($"[Contact] flank attack: facing cos {square:F3}, {flankHits}/{hits} blows on the flank");
        Assert.True(square >= Cos15, "the attacker should face square onto the flank");
        Assert.True(hits > 0 && flankHits >= hits * 0.7f, $"{flankHits}/{hits} blows landed on the flank");
    }

    [Fact]
    public void MoveThroughEnemyStopsAndFights()
    {
        // Blue is ordered to march straight through red's line to the far side.
        var sc = MeleeSetup.Scenario(3,
            new RegimentPlacement { Unit = "gb_spearmen", Position = new[] { 0f, -30f }, Facing = new[] { 0f, 1f } },
            new RegimentPlacement { Unit = "gb_spearmen", Position = new[] { 0f, 0f }, Facing = new[] { 0f, -1f } },
            autoAttack: false);
        var b = new Battle(sc, TestData.Units()) { Log = _ => { } };
        var blue = b.Regiments[0];
        var red = b.Regiments[1];
        b.Issue(new Move(0, new Vector2(0f, 30f), Vector2.UnitY, blue.Width, Run: false));

        int blueHits = 0, worstInside = 0;
        var s = b.Soldiers;
        for (int t = 0; t < 900; t++)
        {
            b.Step();
            foreach (var e in b.Events)
                if (e.Kind == EventKind.MeleeHit && e.Regiment == blue.Id) blueHits++;
            // Red's front rank stands on y = red.Centre.Y and its ranks go back toward +Y.
            int inside = 0;
            for (int i = blue.First; i < blue.First + blue.Count; i++)
                if (s.IsAlive(i) && s.Pos[i].Y > red.Centre.Y + 0.3f) inside++;
            worstInside = Math.Max(worstInside, inside);
        }
        output.WriteLine($"[Contact] move into enemy: blue front at y {blue.Centre.Y:F2} (red front {red.Centre.Y:F2}), " +
                         $"{blueHits} blue blows, at worst {worstInside} blue men past red's front, activity {blue.Activity}");
        Assert.True(blue.Centre.Y < red.Centre.Y, "blue's line should stop in front of red's");
        Assert.True(blueHits > 20, $"blue only struck {blueHits} blows");
        Assert.True(worstInside <= 2, $"{worstInside} blue men got inside red's line");
    }

    [Fact]
    public void FriendlyRegimentsDontInterleave()
    {
        // Two friendly regiments whose marches cross at the origin.
        var sc = new ScenarioDef
        {
            Id = "contact_friends", Seed = 4,
            Sides =
            {
                new SideDef
                {
                    Regiments =
                    {
                        new RegimentPlacement { Unit = "gb_spearmen", Position = new[] { -30f, 0f }, Facing = new[] { 1f, 0f } },
                        new RegimentPlacement { Unit = "gb_spearmen", Position = new[] { 0f, -30f }, Facing = new[] { 0f, 1f } },
                    },
                },
                new SideDef
                {
                    Regiments = { new RegimentPlacement { Unit = "gb_spearmen", Position = new[] { 0f, 300f }, Facing = new[] { 0f, -1f } } },
                },
            },
        };
        var b = new Battle(sc, TestData.Units()) { Log = _ => { } };
        var a = b.Regiments[0];
        var c = b.Regiments[1];
        b.Issue(new Move(0, new Vector2(30f, 0f), Vector2.UnitX, a.Width, Run: false));
        b.Issue(new Move(1, new Vector2(0f, 30f), Vector2.UnitY, c.Width, Run: false));

        int worst = 0;
        int ticks = 0;
        for (; ticks < (int)(90f / Dt); ticks++)
        {
            b.Step();
            worst = Math.Max(worst, Math.Max(Inside(b, a, c), Inside(b, c, a)));
            if (ticks > 30 && a.Activity == RegimentActivity.Idle && c.Activity == RegimentActivity.Idle) break;
        }
        output.WriteLine($"[Contact] crossing friends: both arrived after {ticks * Dt:F0} s, " +
                         $"at worst {worst} men inside the other block");
        Assert.True(worst <= 1, $"{worst} men walked into the other regiment");
        Assert.Equal(RegimentActivity.Idle, a.Activity);
        Assert.Equal(RegimentActivity.Idle, c.Activity);
        Assert.InRange(Vector2.Distance(a.Centre, new Vector2(30f, 0f)), 0f, 0.05f);
        Assert.InRange(Vector2.Distance(c.Centre, new Vector2(0f, 30f)), 0f, 0.05f);
    }

    [Fact]
    public void OverlappingDestinationsAreNudgedApart()
    {
        var sc = new ScenarioDef
        {
            Id = "contact_nudge", Seed = 5,
            Sides =
            {
                new SideDef
                {
                    Regiments =
                    {
                        new RegimentPlacement { Unit = "gb_spearmen", Position = new[] { -30f, 0f }, Facing = new[] { 0f, 1f } },
                        new RegimentPlacement { Unit = "gb_spearmen", Position = new[] { 30f, 0f }, Facing = new[] { 0f, 1f } },
                    },
                },
                new SideDef
                {
                    Regiments = { new RegimentPlacement { Unit = "gb_spearmen", Position = new[] { 0f, 300f }, Facing = new[] { 0f, -1f } } },
                },
            },
        };
        var b = new Battle(sc, TestData.Units()) { Log = _ => { } };
        var target = new Vector2(0f, 20f);
        b.Issue(new Move(0, target, Vector2.UnitY, b.Regiments[0].Width, Run: false));
        b.Issue(new Move(1, target, Vector2.UnitY, b.Regiments[1].Width, Run: false));
        Step(b, (int)(40f / Dt));

        var r0 = b.Regiments[0];
        var r1 = b.Regiments[1];
        // Gap between the outermost files of the two blocks.
        float gap = MathF.Abs(r0.Centre.X - r1.Centre.X) - (r0.Cols - 1) * r0.Def.Spacing;
        output.WriteLine($"[Contact] same destination: fronts at {r0.Centre} and {r1.Centre}, gap {gap:F2} m");
        Assert.Equal(RegimentActivity.Idle, r0.Activity);
        Assert.Equal(RegimentActivity.Idle, r1.Activity);
        Assert.InRange(r0.Centre.Y, 19.9f, 20.1f);
        Assert.InRange(r1.Centre.Y, 19.9f, 20.1f);
        Assert.True(gap >= 0.9f, $"blocks are only {gap:F2} m apart");
    }

    [Fact]
    public void LineHoldsShapeInFrontalMelee()
    {
        float sum = 0;
        int samples = 0;
        for (uint seed = 1; seed <= 2; seed++)
        {
            var b = MeleeSetup.Duel("gb_spearmen", "gb_spearmen", seed);
            MeleeTests.RunToContact(b);
            for (int k = 0; k < 12 && b.Winner < 0; k++)
            {
                Step(b, 150);
                sum += (InShape(b, b.Regiments[0]) + InShape(b, b.Regiments[1])) / 2;
                samples++;
            }
        }
        float avg = sum / samples;
        output.WriteLine($"[Contact] frontal melee: {avg:P0} of living men inside their formation ({samples} samples)");
        Assert.True(avg >= 0.8f, $"only {avg:P0} of men inside their formation");
    }

    [Fact]
    public void ArmyClashKeepsLinesAndDoesntInterpenetrate()
    {
        // m0_clash: 10 v 10 regiments, both sides auto-attack. Sample shape and mixing every 5 s for a minute.
        var b = TestData.Battle("m0_clash");
        float shape = 0;
        int samples = 0, worstMixed = 0;
        for (int k = 0; k < 12 && b.Winner < 0; k++)
        {
            Step(b, 150);
            foreach (var r in b.Regiments)
            {
                if (r.Routing || r.Alive <= 0) continue;
                shape += InShape(b, r);
                samples++;
                foreach (var e in b.Regiments)
                    if (e.Team != r.Team && !e.Routing && e.Alive > 0) worstMixed = Math.Max(worstMixed, Inside(b, r, e));
            }
        }
        shape /= Math.Max(1, samples);
        output.WriteLine($"[Contact] m0_clash: {shape:P0} of living men inside their formation, " +
                         $"at worst {worstMixed} men inside an enemy block");
        Assert.True(shape >= 0.75f, $"only {shape:P0} of men inside their formation");
        Assert.True(worstMixed <= 5, $"{worstMixed} men inside an enemy block");
    }

    [Fact]
    public void ArmyClashAlwaysEnds()
    {
        // Seeds that once stalled: two regiments attacking each other ran for each other's flank forever, or
        // deadlocked turning toward each other.
        foreach (uint seed in new uint[] { 6, 8, 13, 14 })
        {
            var b = TestData.Battle("m0_clash", seed);
            while (b.Winner < 0 && b.Time < 420f) b.Step();
            output.WriteLine($"[Contact] m0_clash seed {seed}: winner {b.Winner} after {b.Time:0} s");
            Assert.True(b.Winner >= 0, $"seed {seed}: no winner after {b.Time:0} s");
        }
    }

    [Fact]
    public void ContactDeterministic()
    {
        static ulong Run()
        {
            var b = AngledAttack(seed: 7);
            Step(b, 900);
            ulong h = 1469598103934665603UL;
            for (int i = 0; i < b.Soldiers.Count; i++)
            {
                h = (h ^ BitConverter.SingleToUInt32Bits(b.Soldiers.Pos[i].X)) * 1099511628211UL;
                h = (h ^ BitConverter.SingleToUInt32Bits(b.Soldiers.Pos[i].Y)) * 1099511628211UL;
            }
            return h;
        }
        Assert.Equal(Run(), Run());
    }

    // ---- helpers ----

    /// <summary>Attacker 45° off the front of a standing defender, facing it, ordered to attack.</summary>
    static Battle AngledAttack(uint seed)
    {
        var sc = MeleeSetup.Scenario(seed,
            new RegimentPlacement { Unit = "gb_spearmen", Position = new[] { 30f, 30f }, Facing = new[] { -1f, -1f } },
            new RegimentPlacement { Unit = "gb_spearmen", Position = new[] { 0f, 0f }, Facing = new[] { 0f, 1f } },
            autoAttack: false);
        var b = new Battle(sc, TestData.Units()) { Log = _ => { } };
        b.Issue(new Attack(0, 1, Run: false));
        return b;
    }

    /// <summary>Living men of <paramref name="a"/> within reach of a living man of <paramref name="e"/>.</summary>
    static int InReach(Battle b, Regiment a, Regiment e)
    {
        var s = b.Soldiers;
        int n = 0;
        for (int i = a.First; i < a.First + a.Count; i++)
        {
            if (!s.IsAlive(i)) continue;
            for (int j = e.First; j < e.First + e.Count; j++)
                if (s.IsAlive(j) && Vector2.Distance(s.Pos[i], s.Pos[j]) <= a.Def.Reach + 0.1f) { n++; break; }
        }
        return n;
    }

    /// <summary>Fraction of living men within one spacing of their regiment's formation rectangle.</summary>
    static float InShape(Battle b, Regiment r)
    {
        var s = b.Soldiers;
        float sp = r.Def.Spacing;
        int rows = (Math.Max(1, r.Alive) + r.Cols - 1) / r.Cols;
        float hw = (r.Cols - 1) * sp * 0.5f + sp, depth = (rows - 1) * sp;
        int alive = 0, inside = 0;
        for (int i = r.First; i < r.First + r.Count; i++)
        {
            if (!s.IsAlive(i)) continue;
            alive++;
            var d = s.Pos[i] - r.Centre;
            float x = Vector2.Dot(d, r.Right), y = -Vector2.Dot(d, r.Facing);
            if (MathF.Abs(x) <= hw && y >= -sp && y <= depth + sp) inside++;
        }
        return alive == 0 ? 1f : inside / (float)alive;
    }

    /// <summary>Living men of <paramref name="a"/> standing inside <paramref name="other"/>'s ranks (0.3 m in).</summary>
    static int Inside(Battle b, Regiment a, Regiment other)
    {
        var s = b.Soldiers;
        float sp = other.Def.Spacing;
        float hw = (other.Cols - 1) * sp * 0.5f - 0.3f, depth = (other.Rows - 1) * sp - 0.3f;
        int n = 0;
        for (int i = a.First; i < a.First + a.Count; i++)
        {
            if (!s.IsAlive(i)) continue;
            var d = s.Pos[i] - other.Centre;
            float x = Vector2.Dot(d, other.Right), y = -Vector2.Dot(d, other.Facing);
            if (MathF.Abs(x) <= hw && y >= 0.3f && y <= depth) n++;
        }
        return n;
    }

    static void Step(Battle b, int ticks)
    {
        for (int i = 0; i < ticks; i++) b.Step();
    }
}
