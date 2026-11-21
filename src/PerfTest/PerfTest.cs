using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Godot;

namespace FOMS.PerfTest;

// Milestone 0 performance test: N soldiers in R regiments march, clash,
// fight and rout, rendered as a single GPU-instanced MultiMesh.
//
// Command line (after "--"):
//   --soldiers=2000  --regiments=20  --benchmark  --duration=60  --warmup=5
// Interactive keys: 1/2/3/4 restart with 1k/2k/5k/10k soldiers, R restart,
// WASD/QE/mouse camera, Space pause.
public partial class PerfTest : Node3D
{
    const float SimHz = 30f;

    int _soldiers = 2000;
    int _regiments = 20;
    bool _benchmark;
    float _duration = 60f, _warmup = 5f;

    BattleSim _sim;
    MultiMesh _mm;
    MultiMeshInstance3D _mmi;
    ArrayMesh _mesh;
    int _tris;
    float[] _buffer;
    RtsCamera _camera;
    Label _hud;
    bool _paused;
    readonly Queue<float> _shots = new();
    string _cameraPreset = "";
    float _clock;

    float _acc, _restartTimer = -1f, _hudTimer;
    readonly Stopwatch _sw = new();
    double _simMsAccum, _uploadMsAccum; int _simSamples, _uploadSamples, _framesSinceHud;
    double _lastSimMs, _lastUploadMs;

    // benchmark samples
    float _benchTime;
    readonly List<float> _frameMs = new();
    readonly List<double> _simMs = new();
    readonly List<double> _uploadMs = new();
    readonly List<int> _aliveSamples = new();

    public override void _Ready()
    {
        ParseArgs();
        DisplayServer.WindowSetVsyncMode(DisplayServer.VSyncMode.Disabled);
        Engine.MaxFps = 0;

        BuildWorld();
        _mesh = SoldierMesh.Build(12, out _tris);
        var mat = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/soldier.gdshader") };
        _mesh.SurfaceSetMaterial(0, mat);

        _mmi = new MultiMeshInstance3D
        {
            CustomAabb = new Aabb(new Vector3(-400, -5, -400), new Vector3(800, 20, 800)),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.On,
        };
        AddChild(_mmi);

        _camera = new RtsCamera { InputEnabled = !_benchmark };
        if (_cameraPreset == "close")
        {
            _camera.Distance = 28f;
            _camera.Pitch = Mathf.DegToRad(-22);
            _camera.Yaw = Mathf.DegToRad(70);
        }
        AddChild(_camera);

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
                case "soldiers": int.TryParse(val, out _soldiers); break;
                case "regiments": int.TryParse(val, out _regiments); break;
                case "benchmark": _benchmark = true; break;
                case "duration": float.TryParse(val, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out _duration); break;
                case "warmup": float.TryParse(val, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out _warmup); break;
                case "shots": // e.g. --shots=10,25,40  (seconds since start)
                    foreach (var t in val.Split(','))
                        if (float.TryParse(t, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var f)) _shots.Enqueue(f);
                    break;
                case "camera": // e.g. --camera=close
                    _cameraPreset = val; break;
            }
        }
        _soldiers = Math.Clamp(_soldiers, 20, 100_000);
        _regiments = Math.Clamp(_regiments, 2, 400);
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

        var sun = new DirectionalLight3D
        {
            ShadowEnabled = true,
            DirectionalShadowMaxDistance = 300f,
            Rotation = new Vector3(Mathf.DegToRad(-50), Mathf.DegToRad(30), 0),
        };
        AddChild(sun);

        var ground = new MeshInstance3D
        {
            Mesh = new PlaneMesh { Size = new Vector2(800, 800) },
            MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.36f, 0.42f, 0.22f), Roughness = 1f },
        };
        AddChild(ground);
    }

    void StartBattle()
    {
        _sim = new BattleSim(_soldiers, _regiments);
        _mm = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            UseCustomData = true,
            Mesh = _mesh,
        };
        _mm.InstanceCount = _sim.Count;
        _mmi.Multimesh = _mm;
        _buffer = new float[_sim.Count * 16];
        _acc = 0; _restartTimer = -1f;
        UploadInstances(1f);
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;

        if (!_paused)
        {
            _acc += dt;
            int steps = 0;
            const float step = 1f / SimHz;
            while (_acc >= step && steps < 4)
            {
                _sw.Restart();
                _sim.Step(step);
                _sw.Stop();
                _lastSimMs = _sw.Elapsed.TotalMilliseconds;
                _simMsAccum += _lastSimMs; _simSamples++;
                if (_benchmark && _benchTime >= _warmup) _simMs.Add(_lastSimMs);
                _acc -= step; steps++;
            }
            if (steps == 4) _acc = 0; // fell behind; drop time rather than spiral

            if (_sim.Winner >= 0 && _restartTimer < 0) _restartTimer = 6f;
            if (_restartTimer >= 0) { _restartTimer -= dt; if (_restartTimer < 0) StartBattle(); }
        }

        _sw.Restart();
        UploadInstances(_acc * SimHz);
        _sw.Stop();
        _lastUploadMs = _sw.Elapsed.TotalMilliseconds;
        _uploadMsAccum += _lastUploadMs; _uploadSamples++;

        if (_benchmark) Benchmark(dt);
        UpdateHud(dt);

        _clock += dt;
        if (_shots.Count > 0 && _clock >= _shots.Peek())
        {
            float t = _shots.Dequeue();
            var dir = ProjectSettings.GlobalizePath("res://perf_results");
            System.IO.Directory.CreateDirectory(dir);
            GetViewport().GetTexture().GetImage().SavePng(System.IO.Path.Combine(dir, $"shot_{_cameraPreset}{t:0}s.png"));
        }
    }

    void UploadInstances(float alpha)
    {
        var sim = _sim;
        var buf = _buffer;
        Parallel.For(0, sim.Count, i =>
        {
            int o = i * 16;
            var st = sim.State[i];
            if (st == SoldierState.Gone)
            {
                Array.Clear(buf, o, 16);
                buf[o + 7] = -1000f;
                return;
            }
            var p = System.Numerics.Vector2.Lerp(sim.PrevPos[i], sim.Pos[i], alpha);
            float yaw = BattleSim.LerpAngle(sim.PrevYaw[i], sim.Yaw[i], alpha);
            float c = MathF.Cos(yaw), s = MathF.Sin(yaw);
            buf[o + 0] = c; buf[o + 1] = 0; buf[o + 2] = s; buf[o + 3] = p.X;
            buf[o + 4] = 0; buf[o + 5] = 1; buf[o + 6] = 0; buf[o + 7] = 0;
            buf[o + 8] = -s; buf[o + 9] = 0; buf[o + 10] = c; buf[o + 11] = p.Y;
            buf[o + 12] = sim.AnimPhase[i];
            buf[o + 13] = (float)(byte)(st == SoldierState.Dead ? SoldierState.Dead : st);
            buf[o + 14] = sim.StateTime[i];
            buf[o + 15] = sim.Team[i];
        });
        RenderingServer.MultimeshSetBuffer(_mm.GetRid(), buf);
    }

    void Benchmark(float dt)
    {
        _benchTime += dt;
        if (_benchTime < _warmup) return;
        _frameMs.Add(dt * 1000f);
        _uploadMs.Add(_lastUploadMs);
        _aliveSamples.Add(_sim.AlivePerTeam[0] + _sim.AlivePerTeam[1]);
        if (_benchTime >= _warmup + _duration) FinishBenchmark();
    }

    void FinishBenchmark()
    {
        var sorted = _frameMs.OrderByDescending(x => x).ToList();
        int onePct = Math.Max(1, sorted.Count / 100);
        double avgFrame = _frameMs.Average();
        double worst1Pct = sorted.Take(onePct).Average();
        var size = DisplayServer.WindowGetSize();
        var scale = DisplayServer.ScreenGetScale();
        var result = new
        {
            soldiers = _sim.Count,
            regiments = _regiments,
            tris_per_soldier = _tris,
            duration_s = _duration,
            frames = _frameMs.Count,
            avg_fps = Math.Round(1000.0 / avgFrame, 1),
            one_percent_low_fps = Math.Round(1000.0 / worst1Pct, 1),
            avg_frame_ms = Math.Round(avgFrame, 2),
            worst_frame_ms = Math.Round(sorted[0], 2),
            avg_sim_ms_per_tick = Math.Round(_simMs.DefaultIfEmpty(0).Average(), 3),
            max_sim_ms_per_tick = Math.Round(_simMs.DefaultIfEmpty(0).Max(), 3),
            sim_hz = SimHz,
            avg_upload_ms = Math.Round(_uploadMs.Average(), 3),
            avg_alive = Math.Round(_aliveSamples.Average(), 0),
            min_alive = _aliveSamples.Min(),
            window = $"{size.X}x{size.Y}",
            screen_scale = scale,
            renderer = RenderingServer.GetVideoAdapterName(),
            godot = Engine.GetVersionInfo()["string"].AsString(),
            cpu_threads = System.Environment.ProcessorCount,
            timestamp = DateTime.Now.ToString("s"),
        };
        string json = JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true });
        GD.Print("BENCHMARK_RESULT " + json);
        var dir = ProjectSettings.GlobalizePath("res://perf_results");
        System.IO.Directory.CreateDirectory(dir);
        System.IO.File.WriteAllText(System.IO.Path.Combine(dir, $"m0_{_sim.Count}_{DateTime.Now:yyyyMMdd_HHmmss}.json"), json);
        GetTree().Quit();
    }

    void UpdateHud(float dt)
    {
        _hudTimer += dt; _framesSinceHud++;
        if (_hudTimer < 0.5f) return;
        double fps = _framesSinceHud / _hudTimer;
        double sim = _simSamples > 0 ? _simMsAccum / _simSamples : 0;
        double up = _uploadSamples > 0 ? _uploadMsAccum / _uploadSamples : 0;
        ulong draws = RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.TotalDrawCallsInFrame);
        ulong prims = RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.TotalPrimitivesInFrame);
        string status = _sim.Winner switch { 0 => "Blue wins, restarting", 1 => "Red wins, restarting", 2 => "Both broke, restarting", _ => "" };
        _hud.Text =
            $"FOMS M0 performance test{(_benchmark ? $"  [BENCHMARK {Math.Max(0, _benchTime - _warmup):0}/{_duration:0}s]" : "")}\n" +
            $"FPS {fps:0}  ({1000.0 / fps:0.0} ms)\n" +
            $"Sim {sim:0.00} ms/tick @ {SimHz:0} Hz   Upload {up:0.00} ms/frame\n" +
            $"Soldiers {_sim.Count}  alive blue {_sim.AlivePerTeam[0]} / red {_sim.AlivePerTeam[1]}   {_tris} tris each\n" +
            $"Draw calls {draws}   Primitives {prims:N0}\n" +
            $"{status}\n" +
            (_benchmark ? "" : "[1] 1k  [2] 2k  [3] 5k  [4] 10k  [R] restart  [Space] pause  WASD/QE/wheel/RMB camera");
        _hudTimer = 0; _framesSinceHud = 0; _simMsAccum = 0; _simSamples = 0; _uploadMsAccum = 0; _uploadSamples = 0;
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (_benchmark || e is not InputEventKey { Pressed: true, Echo: false } k) return;
        switch (k.Keycode)
        {
            case Key.Key1: _soldiers = 1000; StartBattle(); break;
            case Key.Key2: _soldiers = 2000; StartBattle(); break;
            case Key.Key3: _soldiers = 5000; StartBattle(); break;
            case Key.Key4: _soldiers = 10000; StartBattle(); break;
            case Key.R: StartBattle(); break;
            case Key.Space: _paused = !_paused; break;
            case Key.Escape: GetTree().Quit(); break;
        }
    }
}
