using System.IO;
using System.Text.Json.Nodes;
using FOMS.Sim.Data;
using Xunit;

namespace FOMS.Sim.Tests;

public class DataTests
{
    [Fact]
    public void GreyBoxRosterLoadsWithPlaceholderStats()
    {
        var units = TestData.Units();
        string[] greyBoxIds =
        {
            "gb_spearmen", "gb_swordsmen", "gb_pikemen", "gb_archers",
            "gb_light_cav", "gb_heavy_cav", "gb_general",
        };
        Assert.All(greyBoxIds, id => Assert.True(units[id].Placeholder));
        Assert.Contains("Pike", units["gb_pikemen"].Traits);
        Assert.True(units["gb_archers"].Ammo > 0);
        Assert.True(units["gb_archers"].Range > 0);
        Assert.Equal(0, units["gb_swordsmen"].Ammo);
        Assert.True(units["gb_light_cav"].RunSpeed > units["gb_swordsmen"].RunSpeed);
        Assert.True(units["gb_heavy_cav"].Mass > units["gb_light_cav"].Mass);
        Assert.True(units["gb_general"].Morale > units["gb_spearmen"].Morale);
    }

    [Fact]
    public void MissingStatReportsTheUnitFileAndField()
    {
        var temp = Directory.CreateTempSubdirectory();
        try
        {
            var units = temp.CreateSubdirectory("units");
            var unit = JsonNode.Parse(SpearmenJson()).AsObject();
            unit.Remove("melee_attack");
            File.WriteAllText(Path.Combine(units.FullName, "gb_spearmen.json"), unit.ToJsonString());

            var error = Assert.Throws<InvalidDataException>(() => DataLoader.LoadUnits(temp.FullName));
            Assert.Equal("[Data] gb_spearmen.json: missing melee_attack", error.Message);
        }
        finally
        {
            temp.Delete(recursive: true);
        }
    }

    [Fact]
    public void InvalidStatReportsTheFieldAndAcceptedRange()
    {
        var unit = JsonNode.Parse(SpearmenJson()).AsObject();
        unit["accuracy"] = 2.0;

        var error = Assert.Throws<InvalidDataException>(() => DataLoader.ParseUnit(unit.ToJsonString()));
        Assert.Contains("accuracy must be finite and between 0 and 1", error.Message);
    }

    [Theory]
    [InlineData("{")]
    [InlineData("null")]
    [InlineData("[]")]
    public void MalformedUnitHasAReadableDataError(string json)
    {
        var error = Assert.Throws<InvalidDataException>(() => DataLoader.ParseUnit(json));
        Assert.StartsWith("[Data] unit JSON:", error.Message);
    }

    static string SpearmenJson() => File.ReadAllText(Path.Combine(TestData.DataDir, "units", "gb_spearmen.json"));
}
