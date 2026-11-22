// BattleScene: loads a scenario, steps the sim at a fixed tick (C2), interpolates, draws, HUD.
// Owner: S01
// Command line (after "--"): --scenario=<id> (default sandbox_vs_ai)  --quit-after=SEC  --shots=10,25
// Keys: R restart (Space pause, +/- speed live in BattleInput, S03). Camera: WASD/QE/wheel/MMB.
// Public API: BattleScene.Battle (current sim, read-only use, C1)
using System;
using System.Collections.Generic;
using System.Globalization;
using Godot;
using FOMS.Game.Camera;
using FOMS.Game.PlayerInput;
using FOMS.Game.UI.Hud;
using FOMS.Game.View;
using FOMS.Sim.Data;
using SimBattle = FOMS.Sim.Battle;

namespace FOMS.Game.Battle;

public partial class BattleScene : Node3D
{
    const int MaxTicksPerFrame = 4;
    const float RestartDelay = 6f;

    // FIX(triage S04b playtest): default to a scenario where only Red moves on its own; m0_clash is the perf/test clash.
    string _scenarioId = "sandbox_vs_ai";
    float _quitAfter = -1f;
    readonly Queue<float> _shots = new();

    /// <summary>The running battle. Read it; change it only via Issue (C1).</summary>
    public SimBattle Battle { get; private set; }

    SoldierRenderer _renderer;
    Label _hud;
    float _acc, _clock, _restartTimer = -1f, _hudTimer;
    int _frames;
    // FIX(S03): pause and speed are public so BattleInput can drive them.
    public bool Paused;
    public float Speed = 1f;

    public override void _Ready()
    {
        ParseArgs();
        BuildWorld();
        _renderer = new SoldierRenderer { Name = "SoldierRenderer" };
        AddChild(_renderer);
        AddChild(new RtsCamera { Name = "RtsCamera" });
        // FIX(S03): selection, markers and cards bar.
        var input = new BattleInput(this);
        AddChild(input);
        AddChild(new Markers(this, input));
        AddChild(new CardsBar(this, input));

        var layer = new CanvasLayer();
        AddChild(layer);
        _hud = new Label { Position = new Vector2(12, 10) };
        _hud.AddThemeFontSizeOverride("font_size", 16);
        _hud.AddThemeColorOverride("font_shadow_color", new Color(0, 0, 0, 0.9f));
        _hud.AddThemeConstantOverride("shadow_offset_x", 1);
        _hud.AddThemeConstantOverride("shadow_offset_y", 1);
        layer.AddChild(_hud);

        StartBattle();
    }

    void ParseArgs()
    {
        foreach (var a in OS.GetCmdlineUserArgs())
        {
            var kv = a.TrimStart('-').Split('=', 2);
            string key = kv[0], val = kv.Length > 1 ? kv[1] : "";
            switch (key)
            {
                case "scenario": _scenarioId = val; break;
                case "quit-after": float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out _quitAfter); break;
                case "shots":
                    foreach (var t in val.Split(','))
                        if (float.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out var f)) _shots.Enqueue(f);
                    break;
            }
        }
    }

    void StartBattle()
    {
        var dataDir = ProjectSettings.GlobalizePath("res://data");
        var units = DataLoader.LoadUnits(dataDir);
        var scenario = DataLoader.LoadScenario(dataDir, _scenarioId);
        Battle = new SimBattle(scenario, units) { Log = s => GD.Print(s) };
        _renderer.Init(Battle);
        _acc = 0; _restartTimer = -1f;
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        if (!Paused)
        {
            _acc += dt * Speed;
            int steps = 0;
            while (_acc >= SimBattle.TickDt && steps < MaxTicksPerFrame)
            {
                Battle.Step();
                _acc -= SimBattle.TickDt;
                steps++;
            }
            if (steps == MaxTicksPerFrame && _acc >= SimBattle.TickDt) _acc = 0; // fell behind: drop time, don't spiral

            if (Battle.Winner >= 0 && _restartTimer < 0) _restartTimer = RestartDelay;
            if (_restartTimer >= 0) { _restartTimer -= dt; if (_restartTimer < 0) StartBattle(); }
        }
        _renderer.Draw(_acc / SimBattle.TickDt);
        UpdateHud(dt);

        _clock += dt;
        if (_shots.Count > 0 && _clock >= _shots.Peek())
        {
            float t = _shots.Dequeue();
            var dir = ProjectSettings.GlobalizePath("res://perf_results");
            System.IO.Directory.CreateDirectory(dir);
            GetViewport().GetTexture().GetImage().SavePng(System.IO.Path.Combine(dir, $"battle_{t:0}s.png"));
        }
        if (_quitAfter >= 0 && _clock >= _quitAfter)
        {
            GD.Print($"[Sim] quit-after {_quitAfter:0.#}s: tick {Battle.Tick}, alive {Battle.AlivePerTeam[0]}/{Battle.AlivePerTeam[1]}, winner {Battle.Winner}");
            GetTree().Quit();
            _quitAfter = -1f;
        }
    }

    void UpdateHud(float dt)
    {
        _hudTimer += dt; _frames++;
        if (_hudTimer < 0.5f) return;
        double fps = _frames / _hudTimer;
        string status = Battle.Winner switch
        {
            -1 => "",
            0 => "Blue wins, restarting",
            1 => "Red wins, restarting",
            _ => "Both broke, restarting",
        };
        _hud.Text =
            $"FOMS Battle — {_scenarioId}   FPS {fps:0}\n" +
            $"Time {Battle.Time:0}s   Blue {Battle.AlivePerTeam[0]}  Red {Battle.AlivePerTeam[1]}\n" +
            $"{status}\n" +
            $"{(Paused ? "PAUSED  " : "")}Speed {Speed:0.0}x   [R] restart  [Space] pause  [+/-] speed  WASD/QE/wheel/MMB camera\n" +
            "LMB select  RMB move / drag line / attack  H halt  Backspace withdraw  Ctrl+1-9 groups";
        _hudTimer = 0; _frames = 0;
    }

    void BuildWorld()
    {
        var env = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Sky,
            Sky = new Sky { SkyMaterial = new ProceduralSkyMaterial() },
            AmbientLightSource = Godot.Environment.AmbientSource.Sky,
            TonemapMode = Godot.Environment.ToneMapper.Filmic,
        };
        AddChild(new WorldEnvironment { Environment = env });
        AddChild(new DirectionalLight3D
        {
            ShadowEnabled = true,
            DirectionalShadowMaxDistance = 300f,
            Rotation = new Vector3(Mathf.DegToRad(-50), Mathf.DegToRad(30), 0),
        });
        AddChild(new MeshInstance3D
        {
            Mesh = new PlaneMesh { Size = new Vector2(800, 800) },
            MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.36f, 0.42f, 0.22f), Roughness = 1f },
        });
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (e is not InputEventKey { Pressed: true, Echo: false } k) return;
        switch (k.Keycode)
        {
            case Key.R: StartBattle(); break;
        }
    }
}
