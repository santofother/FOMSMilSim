// S04b formation movement: wheeling on the march, facing about, re-forming at the destination. Owner: S04b
using System;
using System.Numerics;
using FOMS.Sim.Data;
using Xunit;

namespace FOMS.Sim.Tests;

public class FormationTests
{
    const float Dt = Battle.TickDt;

    [Fact]
    public void SmallTurnWheelsWithoutStopping()
    {
        var (battle, reg) = Line();
        var dir = Dir(30f);
        var target = reg.Centre + dir * 40f;
        battle.Issue(new Move(reg.Id, target, dir, reg.Width, Run: false));

        float minStep = 0.5f * reg.Def.Speed * Dt;
        int ticks = 0;
        while (Vector2.Distance(reg.Centre, target) > 0.1f && ticks < 900)
        {
            var before = reg.Centre;
            battle.Step();
            ticks++;
            if (Vector2.Distance(reg.Centre, target) > 0.1f)
                Assert.True(Vector2.Distance(before, reg.Centre) >= minStep, $"stopped at tick {ticks}");
        }
        Step(battle, 60);
        Assert.Equal(RegimentActivity.Idle, reg.Activity);
        Assert.InRange(Vector2.Distance(target, reg.Centre), 0f, 0.01f);
        Assert.InRange(Vector2.Distance(dir, reg.Facing), 0f, 0.001f);
    }

    [Fact]
    public void NoSoldierWalksBackwardsWhileWheeling()
    {
        var (battle, reg) = Line();
        var dir = Dir(-40f);
        battle.Issue(new Move(reg.Id, reg.Centre + dir * 45f, dir, reg.Width, Run: false));
        var s = battle.Soldiers;
        float worst = 0f;
        for (int t = 0; t < 450; t++)
        {
            battle.Step();
            for (int i = reg.First; i < reg.First + reg.Count; i++)
                worst = MathF.Min(worst, Vector2.Dot(s.Vel[i], reg.Facing));
        }
        Assert.InRange(worst, -0.3f, 0f);
    }

    [Fact]
    public void AboutFaceSwapsRanks()
    {
        var (battle, reg) = Line();
        float spacing = Spacing(reg);
        var start = Positions(battle, reg);
        var rearFront = reg.Centre - reg.Facing * ((reg.Rows - 1) * spacing);
        battle.Issue(new Move(reg.Id, rearFront, -reg.Facing, reg.Width, Run: false));
        Step(battle, 90);

        Assert.Equal(RegimentActivity.Idle, reg.Activity);
        Assert.InRange(Vector2.Distance(-Vector2.UnitY, reg.Facing), 0f, 0.001f);
        for (int k = 0; k < reg.Count; k++)
            Assert.InRange(Vector2.Distance(start[k], battle.Soldiers.Pos[reg.First + k]), 0f, 1.5f * spacing);
        AssertFormedUp(battle, reg, 0.3f);
    }

    [Fact]
    public void LateralMoveReformsAtDestination()
    {
        var (battle, reg) = Line();
        var target = reg.Centre + Vector2.UnitX * 30f;
        battle.Issue(new Move(reg.Id, target, Vector2.UnitX, reg.Width, Run: false));
        int limit = (int)((30f / reg.Def.Speed + 4f) / Dt);
        int ticks = 0;
        do
        {
            battle.Step();
            ticks++;
        } while (ticks < limit && !(reg.Activity == RegimentActivity.Idle && FormedUp(battle, reg, 0.3f)));
        Assert.True(ticks < limit, $"not formed up after {ticks * Dt:F1} s");
        Assert.InRange(Vector2.Distance(target, reg.Centre), 0f, 0.01f);
        Assert.InRange(Vector2.Distance(Vector2.UnitX, reg.Facing), 0f, 0.001f);
    }

    [Fact]
    public void FlankMarchKeepsItsFacing()
    {
        var (battle, reg) = Line();
        var target = reg.Centre + Vector2.UnitX * 20f;
        battle.Issue(new Move(reg.Id, target, Vector2.UnitY, reg.Width, Run: false));
        for (int t = 0; t < 300; t++)
        {
            battle.Step();
            Assert.Equal(Vector2.UnitY, reg.Facing);
        }
        Assert.InRange(Vector2.Distance(target, reg.Centre), 0f, 0.01f);
        AssertFormedUp(battle, reg, 0.3f);
    }

    [Fact]
    public void ArrivedMoveHoldsWhereItIsPushed()
    {
        var (battle, reg) = Line();
        var target = reg.Centre + Vector2.UnitY * 3f;
        battle.Issue(new Move(reg.Id, target, Vector2.UnitY, reg.Width, Run: false));
        Step(battle, 90);
        Assert.Equal(RegimentActivity.Idle, reg.Activity);

        var pushed = reg.Centre - Vector2.UnitY; // what MeleeSpacing.Push does to a lighter regiment
        reg.Centre = pushed;
        Step(battle, 60);
        Assert.Equal(pushed, reg.Centre);
    }

    [Fact]
    public void FormationMovementDeterministic()
    {
        var (a, ra) = Line();
        var (b, rb) = Line();
        foreach (var (battle, reg) in new[] { (a, ra), (b, rb) })
        {
            battle.Issue(new Move(reg.Id, reg.Centre + Dir(35f) * 25f, Dir(35f), reg.Width * 0.7f, Run: true));
            Step(battle, 200);
            battle.Issue(new Move(reg.Id, reg.Centre - Dir(35f) * 10f, -Dir(35f), reg.Width, Run: false));
            Step(battle, 200);
        }
        for (int i = 0; i < a.Soldiers.Count; i++)
        {
            Assert.Equal(a.Soldiers.Pos[i], b.Soldiers.Pos[i]);
            Assert.Equal(a.Soldiers.Yaw[i], b.Soldiers.Yaw[i]);
        }
    }

    // ---- helpers ----

    /// <summary>60 spearmen, 12 wide, at the origin facing +Y, settled for a second. A far enemy keeps the battle on.</summary>
    static (Battle, Regiment) Line()
    {
        var scenario = new ScenarioDef
        {
            Id = "formation_test",
            Sides =
            {
                new SideDef { Regiments = { new RegimentPlacement { Unit = "gb_spearmen", Soldiers = 60, Cols = 12 } } },
                new SideDef
                {
                    Regiments =
                    {
                        new RegimentPlacement
                        {
                            Unit = "gb_spearmen", Soldiers = 12, Cols = 4,
                            Position = new[] { 0f, 400f }, Facing = new[] { 0f, -1f },
                        },
                    },
                },
            },
        };
        var battle = new Battle(scenario, TestData.Units()) { Log = _ => { } };
        Step(battle, 30);
        return (battle, battle.Regiments[0]);
    }

    /// <summary>Unit vector <paramref name="degrees"/> clockwise-ish from +Y (sign only matters for variety).</summary>
    static Vector2 Dir(float degrees)
    {
        float r = degrees * MathF.PI / 180f;
        return new Vector2(MathF.Sin(r), MathF.Cos(r));
    }

    static float Spacing(Regiment reg) => Vector2.Distance(reg.Slot(0), reg.Slot(1));

    static Vector2[] Positions(Battle battle, Regiment reg)
    {
        var p = new Vector2[reg.Count];
        for (int k = 0; k < reg.Count; k++) p[k] = battle.Soldiers.Pos[reg.First + k];
        return p;
    }

    static bool FormedUp(Battle battle, Regiment reg, float tolerance)
    {
        for (int k = 0; k < reg.Count; k++)
        {
            float nearest = float.MaxValue;
            for (int i = reg.First; i < reg.First + reg.Count; i++)
                nearest = MathF.Min(nearest, Vector2.Distance(reg.Slot(k), battle.Soldiers.Pos[i]));
            if (nearest > tolerance) return false;
        }
        return true;
    }

    static void AssertFormedUp(Battle battle, Regiment reg, float tolerance)
        => Assert.True(FormedUp(battle, reg, tolerance), "every slot should have a man in it");

    static void Step(Battle battle, int ticks)
    {
        for (int i = 0; i < ticks; i++) battle.Step();
    }
}
