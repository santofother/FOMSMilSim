// Shared helpers: find data/ from the test binaries and build battles quietly. Owner: S01
using System;
using System.Collections.Generic;
using System.IO;
using FOMS.Sim;
using FOMS.Sim.Data;

namespace FOMS.Sim.Tests;

public static class TestData
{
    static string _dataDir;

    /// <summary>The repo's data/ folder (walks up from the test binaries).</summary>
    public static string DataDir => _dataDir ??= Find();

    static string Find()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
        {
            var candidate = Path.Combine(d.FullName, "data");
            if (Directory.Exists(Path.Combine(candidate, "units"))) return candidate;
        }
        throw new DirectoryNotFoundException("[Test] data/ not found above " + AppContext.BaseDirectory);
    }

    public static Dictionary<string, UnitDef> Units() => DataLoader.LoadUnits(DataDir);

    /// <summary>Loads a scenario, optionally overriding its seed, with logging silenced.</summary>
    public static Battle Battle(string scenario, uint? seed = null)
    {
        var sc = DataLoader.LoadScenario(DataDir, scenario);
        if (seed.HasValue) sc.Seed = seed.Value;
        Action<string> quiet = _ => { };
        var b = new Battle(sc, Units()) { Log = quiet };
        return b;
    }
}
