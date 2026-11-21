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
/// Defaults support programmatic definitions. JSON unit files supply their own stats.
/// </summary>
public sealed class UnitDef
{
    public string Id = "";
    public string Name = "";
    public bool Placeholder;
    /// <summary>Soldier model name for the renderer registry (C9), e.g. "infantry".</summary>
    public string Model = "infantry";
    /// <summary>Head count of a fresh regiment.</summary>
    public int Soldiers = 100;
    public float MeleeAttack = 0.4f;
    public float MeleeDefence;
    public int WeaponDamage = 15;
    public int ArmourPiercing;
    public int Armour;
    public float Shield;
    public float ChargeBonus;
    public float Mass = 1f;
    public float Morale = 100f;
    public float Stamina = 100f;
    public int Ammo;
    public float Range;
    public float Accuracy;
    public string[] Traits = Array.Empty<string>();

    public int Hp = 100;
    // M0 melee. TODO(S04): replace with melee_attack / melee_defence / armour.
    /// <summary>Chance a swing hits (0–1).</summary>
    public float MeleeHitChance = 0.4f;
    /// <summary>Damage = weapon_damage + random(0..weapon_damage_random).</summary>
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
    public bool AutoAttack;
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

    static readonly string[] RequiredUnitFields =
    {
        "id", "name", "model", "soldiers", "melee_attack", "melee_defence", "weapon_damage",
        "armour_piercing", "armour", "shield", "charge_bonus", "mass", "speed", "run_speed",
        "morale", "stamina", "ammo", "range", "accuracy", "traits",
        "hp", "melee_hit_chance", "weapon_damage_random", "attack_interval",
        "attack_interval_random", "reach", "rout_speed", "rout_threshold", "spacing",
    };

    public static UnitDef ParseUnit(string json) => ParseUnit(json, "unit JSON");

    static UnitDef ParseUnit(string json, string source)
    {
        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions
            {
                CommentHandling = Options.ReadCommentHandling,
                AllowTrailingCommas = Options.AllowTrailingCommas,
            });
            var root = document.RootElement;
            Require(root.ValueKind == JsonValueKind.Object, source, "expected a JSON object");
            foreach (string key in RequiredUnitFields)
                Require(root.TryGetProperty(key, out _), source, $"missing {key}");

            var def = root.Deserialize<UnitDef>(Options);
            Require(!string.IsNullOrWhiteSpace(def.Id), source, "id must be non-empty");
            Require(!string.IsNullOrWhiteSpace(def.Name), source, "name must be non-empty");
            Require(!string.IsNullOrWhiteSpace(def.Model), source, "model must be non-empty");
            Require(def.Traits != null && Array.TrueForAll(def.Traits, t => !string.IsNullOrWhiteSpace(t)),
                source, "traits must be an array of non-empty strings");
            Require(def.Soldiers > 0, source, "soldiers must be positive");
            Require(def.Hp > 0, source, "hp must be positive");
            Require(def.WeaponDamage >= 0, source, "weapon_damage must be non-negative");
            Require(def.ArmourPiercing >= 0, source, "armour_piercing must be non-negative");
            Require(def.Armour >= 0, source, "armour must be non-negative");
            Require(def.Ammo >= 0, source, "ammo must be non-negative");
            Require(def.WeaponDamageRandom >= 0, source, "weapon_damage_random must be non-negative");

            Number(def.MeleeAttack, "melee_attack", source, 0, 1);
            Number(def.MeleeDefence, "melee_defence", source, 0, 1);
            Number(def.Shield, "shield", source, 0, 1);
            Number(def.Accuracy, "accuracy", source, 0, 1);
            Number(def.Morale, "morale", source, 0, 100);
            Number(def.ChargeBonus, "charge_bonus", source, 0);
            Number(def.Mass, "mass", source, float.Epsilon);
            Number(def.Speed, "speed", source, float.Epsilon);
            Number(def.RunSpeed, "run_speed", source, def.Speed);
            Number(def.Stamina, "stamina", source, float.Epsilon);
            Number(def.Range, "range", source, 0);
            Number(def.MeleeHitChance, "melee_hit_chance", source, 0, 1);
            Number(def.AttackInterval, "attack_interval", source, float.Epsilon);
            Number(def.AttackIntervalRandom, "attack_interval_random", source, 0);
            Number(def.Reach, "reach", source, float.Epsilon);
            Number(def.RoutSpeed, "rout_speed", source, float.Epsilon);
            Number(def.RoutThreshold, "rout_threshold", source, 0, 1);
            Number(def.Spacing, "spacing", source, float.Epsilon);
            return def;
        }
        catch (JsonException e)
        {
            throw new InvalidDataException($"[Data] {source}: {e.Message}", e);
        }
    }

    static void Number(float value, string field, string source, float min, float max = float.MaxValue)
    {
        if (float.IsFinite(value) && value >= min && value <= max) return;
        string range = (min, max) switch
        {
            (float.Epsilon, float.MaxValue) => "positive",
            (0f, float.MaxValue) => "non-negative",
            (_, float.MaxValue) => FormattableString.Invariant($"at least {min}"),
            _ => FormattableString.Invariant($"between {min} and {max}"),
        };
        throw new InvalidDataException($"[Data] {source}: {field} must be finite and {range}");
    }

    static void Require(bool condition, string source, string message)
    {
        if (!condition) throw new InvalidDataException($"[Data] {source}: {message}");
    }

    public static ScenarioDef ParseScenario(string json) => ParseScenario(json, "scenario JSON");

    static ScenarioDef ParseScenario(string json, string source)
    {
        try
        {
            return JsonSerializer.Deserialize<ScenarioDef>(json, Options)
                ?? throw new InvalidDataException($"[Data] {source}: expected a JSON object");
        }
        catch (JsonException e)
        {
            throw new InvalidDataException($"[Data] {source}: {e.Message}", e);
        }
    }

    /// <summary>All data/units/*.json under <paramref name="dataDir"/>, keyed by id.</summary>
    public static Dictionary<string, UnitDef> LoadUnits(string dataDir)
    {
        var result = new Dictionary<string, UnitDef>();
        var files = Directory.GetFiles(Path.Combine(dataDir, "units"), "*.json");
        Array.Sort(files, StringComparer.Ordinal);
        foreach (var file in files)
        {
            string source = Path.GetFileName(file);
            var def = ParseUnit(File.ReadAllText(file), source);
            Require(def.Id == Path.GetFileNameWithoutExtension(file), source, $"id '{def.Id}' must match the file name");
            result.Add(def.Id, def);
        }
        return result;
    }

    /// <summary>data/scenarios/&lt;id&gt;.json under <paramref name="dataDir"/>.</summary>
    public static ScenarioDef LoadScenario(string dataDir, string id)
    {
        var path = Path.Combine(dataDir, "scenarios", id + ".json");
        string source = Path.GetFileName(path);
        var def = ParseScenario(File.ReadAllText(path), source);
        Require(def.Id == id, source, $"id '{def.Id}' must match the file name");
        return def;
    }
}
