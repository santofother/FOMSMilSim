// Data definitions loaded from data/*.json (contract C7) and the JSON loader.
// Owner: S01 (skeleton), S02 completes.
// Public API: UnitDef, ScenarioDef, SideDef, RegimentPlacement, DataLoader
using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Text.Json;

namespace FOMS.Sim.Data;

/// <summary>
/// One unit type: data/units/&lt;id&gt;.json. JSON keys are snake_case of these names.
/// Defaults are the M0 numbers. // TUNE: real values live in the JSON files.
/// </summary>
public sealed class UnitDef
{
    public string Id = "";
    public string Name = "";
    /// <summary>Soldier model name for the renderer registry (C9), e.g. "infantry".</summary>
    public string Model = "infantry";
    /// <summary>Head count of a fresh regiment.</summary>
    public int Soldiers = 100;
    public int Hp = 100;
    // M0 melee. TODO(S04): replace with melee_attack / melee_defence / armour.
    /// <summary>Chance a swing hits (0–1).</summary>
    public float MeleeHitChance = 0.4f;
    /// <summary>Damage = weapon_damage + random(0..weapon_damage_random).</summary>
    public int WeaponDamage = 15;
    public int WeaponDamageRandom = 15;
    /// <summary>Seconds between swings = attack_interval + random(0..attack_interval_random).</summary>
    public float AttackInterval = 1.2f;
    public float AttackIntervalRandom = 0.6f;
    /// <summary>Melee reach in metres.</summary>
    public float Reach = 1.3f;
    /// <summary>Marching speed, m/s.</summary>
    public float Speed = 2.2f;
    /// <summary>Speed when closing on the enemy, m/s.</summary>
    public float ChargeSpeed = 3.6f;
    public float RunSpeed = 4.0f;
    public float RoutSpeed = 5.0f;
    /// <summary>Regiment routs below this fraction of its starting strength. TODO(S05): morale.</summary>
    public float RoutThreshold = 0.45f;
    /// <summary>Distance between soldiers in formation, metres.</summary>
    public float Spacing = 1.1f;
}

/// <summary>A battle setup: data/scenarios/&lt;id&gt;.json.</summary>
public sealed class ScenarioDef
{
    public string Id = "";
    public string Name = "";
    public uint Seed = 1;
    public List<SideDef> Sides = new();
}

/// <summary>One army. Team index = position in ScenarioDef.Sides.</summary>
public sealed class SideDef
{
    public string Name = "";
    public List<RegimentPlacement> Regiments = new();
}

/// <summary>Where a regiment starts.</summary>
public sealed class RegimentPlacement
{
    /// <summary>UnitDef id.</summary>
    public string Unit = "";
    /// <summary>Front centre [x, z] in metres.</summary>
    public float[] Position = { 0, 0 };
    /// <summary>Facing direction [x, z]; normalised on load.</summary>
    public float[] Facing = { 0, 1 };
    /// <summary>Files (soldiers per rank). 0 = auto, round(sqrt(soldiers * 5)).</summary>
    public int Cols;
    /// <summary>Head count override. 0 = UnitDef.Soldiers.</summary>
    public int Soldiers;

    public Vector2 PositionV => new(Position[0], Position[1]);
    public Vector2 FacingV => Vector2.Normalize(new Vector2(Facing[0], Facing[1]));
}

/// <summary>Loads defs from JSON (snake_case keys, every file has an "id" matching its file name).</summary>
public static class DataLoader
{
    static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        IncludeFields = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static UnitDef ParseUnit(string json) => JsonSerializer.Deserialize<UnitDef>(json, Options);
    public static ScenarioDef ParseScenario(string json) => JsonSerializer.Deserialize<ScenarioDef>(json, Options);

    /// <summary>All data/units/*.json under <paramref name="dataDir"/>, keyed by id.</summary>
    public static Dictionary<string, UnitDef> LoadUnits(string dataDir)
    {
        var result = new Dictionary<string, UnitDef>();
        var files = Directory.GetFiles(Path.Combine(dataDir, "units"), "*.json");
        Array.Sort(files, StringComparer.Ordinal);
        foreach (var f in files)
        {
            var def = ParseUnit(File.ReadAllText(f));
            var expected = Path.GetFileNameWithoutExtension(f);
            if (def.Id != expected) throw new InvalidDataException($"[Sim] {f}: id '{def.Id}' != file name '{expected}'");
            result[def.Id] = def;
        }
        return result;
    }

    /// <summary>data/scenarios/&lt;id&gt;.json under <paramref name="dataDir"/>.</summary>
    public static ScenarioDef LoadScenario(string dataDir, string id)
    {
        var path = Path.Combine(dataDir, "scenarios", id + ".json");
        var def = ParseScenario(File.ReadAllText(path));
        if (def.Id != id) throw new InvalidDataException($"[Sim] {path}: id '{def.Id}' != file name '{id}'");
        return def;
    }
}
