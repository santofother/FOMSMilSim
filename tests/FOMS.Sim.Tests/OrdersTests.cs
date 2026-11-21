using System;
using System.Numerics;
using FOMS.Sim.Data;
using FOMS.Sim.Terrain;
using Xunit;

namespace FOMS.Sim.Tests;

public class OrdersTests
{
    [Fact]
    public void MoveWheelsAndReachesItsFrontCentreWidthAndFacing()
    {
        var battle = Drill();
        var reg = battle.Regiments[0];
        var destination = new Vector2(8f, 0f);
        var facing = -Vector2.UnitY;
        var firstSoldier = battle.Soldiers.Pos[reg.First];

        battle.Issue(new Move(0, destination, facing * 2f, 4.4f, Run: false));
        battle.Step();

        Assert.Equal(RegimentActivity.Moving, reg.Activity);
        Assert.Equal(Vector2.Zero, reg.Centre);
        Assert.InRange(Vector2.Distance(Vector2.UnitY, reg.Facing), 0.001f, 0.1f);
        Assert.InRange(Vector2.Distance(firstSoldier, battle.Soldiers.Pos[reg.First]), 0f, 0.1f);
        Assert.Equal(4, reg.Cols);
        Assert.Equal(3, reg.Rows);

        Step(battle, 330);
        Assert.Equal(RegimentActivity.Idle, reg.Activity);
        Assert.InRange(Vector2.Distance(destination, reg.Centre), 0f, 0.01f);
        Assert.InRange(Vector2.Distance(facing, reg.Facing), 0f, 0.001f);
        Assert.Equal(4.4f, reg.Width, 3);
        for (int i = 0; i < reg.Count; i++)
            Assert.InRange(Vector2.Distance(reg.Slot(i), battle.Soldiers.Pos[reg.First + i]), 0f, 0.25f);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MoveUsesTheOrderedPaceAndHaltHoldsThatFormation(bool run)
    {
        var battle = Drill();
        var reg = battle.Regiments[0];
        var enemy = battle.Regiments[1];
        var idleSoldier = battle.Soldiers.Pos[enemy.First];
        battle.Issue(new Move(0, new Vector2(0f, 20f), Vector2.UnitY, 4.4f, run));
        Step(battle, 30);

        Assert.Equal(run ? reg.Def.RunSpeed : reg.Def.Speed, reg.Centre.Y, 3);
        var haltedCentre = reg.Centre;
        var haltedWidth = reg.Width;
        battle.Issue(new Halt(0));
        Step(battle, 90);

        Assert.Equal(RegimentActivity.Idle, reg.Activity);
        Assert.Equal(haltedCentre, reg.Centre);
        Assert.Equal(Vector2.UnitY, reg.Facing);
        Assert.Equal(haltedWidth, reg.Width);
        Assert.Equal(new Vector2(0f, 35f), enemy.Centre);
        Assert.Equal(idleSoldier, battle.Soldiers.Pos[enemy.First]);
    }

    [Fact]
    public void AttackFollowsAMovingTargetAndReachesMelee()
    {
        var battle = Drill();
        var reg = battle.Regiments[0];
        var enemy = battle.Regiments[1];
        battle.Issue(new Attack(0, 1, Run: true));
        battle.Issue(new Move(1, new Vector2(0f, 42f), Vector2.UnitY, enemy.Width, Run: false));
        Step(battle, 150);

        Assert.Equal(RegimentActivity.Engaging, reg.Activity);
        Assert.Equal(enemy.Id, reg.TargetRegiment);
        Assert.True(enemy.Centre.Y > 35f);

        bool fought = false;
        for (int i = 0; i < 450 && !fought; i++)
        {
            battle.Step();
            for (int j = enemy.First; j < enemy.First + enemy.Count; j++)
                fought |= battle.Soldiers.Hp[j] < enemy.Def.Hp;
        }

        Assert.True(fought, "The attack should deal damage to the moving target.");
        Assert.Equal(RegimentActivity.Fighting, reg.Activity);
        Assert.InRange(Vector2.Distance(reg.Centre, enemy.Centre), 0f, enemy.Rows * enemy.Def.Spacing + reg.Def.Reach);
    }

    [Fact]
    public void WithdrawBacksAwayAtWalkingPaceAndKeepsFacingTheEnemy()
    {
        var battle = Drill();
        var reg = battle.Regiments[0];
        battle.Issue(new Withdraw(0));
        Step(battle, 30);

        Assert.Equal(RegimentActivity.Moving, reg.Activity);
        Assert.Equal(-reg.Def.Speed, reg.Centre.Y, 3);
        Assert.Equal(Vector2.UnitY, reg.Facing);
        Assert.InRange(MathF.Abs(Battle.LerpAngle(Battle.YawOf(reg.Facing), battle.Soldiers.Yaw[reg.First], 1f)
                                - Battle.YawOf(reg.Facing)), 0f, 0.01f);
    }

    [Fact]
    public void FormationChangesSpacingWithoutCancellingMovement()
    {
        var battle = Drill();
        var reg = battle.Regiments[0];
        var order = new Move(0, new Vector2(0f, 15f), Vector2.UnitY, 4.4f, Run: false);
        battle.Issue(order);
        battle.Step();

        var initialSpacing = Vector2.Distance(reg.Slot(0), reg.Slot(1));
        battle.Issue(new SetFormation(0, FormationKind.Loose));
        Step(battle, 30);

        Assert.Equal(FormationKind.Loose, reg.Formation);
        Assert.Equal(initialSpacing * 1.5f, Vector2.Distance(reg.Slot(0), reg.Slot(1)), 3);
        Assert.Equal(6.6f, reg.Width, 3);
        Assert.Same(order, reg.Order);
        Assert.True(reg.Centre.Y > 2f);

        battle.Issue(new SetFormation(0, FormationKind.Line));
        battle.Step();
        Assert.Equal(FormationKind.Line, reg.Formation);
        Assert.Equal(4.4f, reg.Width, 3);
    }

    [Fact]
    public void MoveStopsAtUnwalkableGround()
    {
        var battle = Drill(terrain: new Barrier());
        var reg = battle.Regiments[0];
        battle.Issue(new Move(0, new Vector2(0f, 8f), Vector2.UnitY, 4.4f, Run: true));
        Step(battle, 120);

        Assert.InRange(reg.Centre.Y, 2.8f, 3f);
        for (int i = reg.First; i < reg.First + reg.Count; i++)
            Assert.True(battle.Soldiers.Pos[i].Y < 3f);
    }

    [Fact]
    public void AutoAttackIsPerSideAndAnExplicitHaltOverridesIt()
    {
        var battle = Drill(autoAttack: true);
        var attacker = battle.Regiments[0];
        var defender = battle.Regiments[1];

        Step(battle, 30);
        Assert.Null(attacker.Order);
        Assert.True(attacker.Centre.Y > 0f);
        Assert.Equal(new Vector2(0f, 35f), defender.Centre);

        var centre = attacker.Centre;
        battle.Issue(new Halt(0));
        Step(battle, 30);
        Assert.Equal(centre, attacker.Centre);
        Assert.Equal(RegimentActivity.Idle, attacker.Activity);
    }

    static Battle Drill(bool autoAttack = false, ITerrain terrain = null)
    {
        var scenario = new ScenarioDef
        {
            Id = "orders_test",
            Sides =
            {
                new SideDef
                {
                    AutoAttack = autoAttack,
                    Regiments = { new RegimentPlacement { Unit = "gb_spearmen", Soldiers = 12, Cols = 4 } },
                },
                new SideDef
                {
                    Regiments =
                    {
                        new RegimentPlacement
                        {
                            Unit = "gb_spearmen", Soldiers = 12, Cols = 4,
                            Position = new[] { 0f, 35f }, Facing = new[] { 0f, -1f },
                        },
                    },
                },
            },
        };
        return new Battle(scenario, TestData.Units(), terrain) { Log = _ => { } };
    }

    static void Step(Battle battle, int ticks)
    {
        for (int i = 0; i < ticks; i++) battle.Step();
    }

    sealed class Barrier : ITerrain
    {
        public float Height(Vector2 p) => 0f;
        public bool Walkable(Vector2 p) => p.Y < 3f;
    }
}
