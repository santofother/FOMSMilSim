// SoldierRenderer: draws every soldier with one MultiMesh per model type (contract C9).
// Owner: S01
// Public API: SoldierRenderer.Register(model, builder), Init(battle), Draw(alpha)
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;
using SimBattle = FOMS.Sim.Battle;
using SoldierState = FOMS.Sim.SoldierState;

namespace FOMS.Game.View;

/// <summary>
/// One MultiMeshInstance3D per soldier model name (UnitDef.model).
/// CONTRACT (C9): INSTANCE_CUSTOM = (anim phase, state, state time, team + variant).
/// </summary>
public partial class SoldierRenderer : Node3D
{
    static readonly Dictionary<string, Func<ArrayMesh>> Builders = new();

    /// <summary>Adds a soldier model. Cards call this (e.g. from a static constructor or _Ready) instead of editing the renderer.</summary>
    public static void Register(string model, Func<ArrayMesh> builder) => Builders[model] = builder;

    static SoldierRenderer()
    {
        Register("infantry", () =>
        {
            var mesh = SoldierMesh.Build(12, out _);
            mesh.SurfaceSetMaterial(0, new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/soldier.gdshader") });
            return mesh;
        });
    }

    sealed class Batch
    {
        public MultiMesh Mm;
        public MultiMeshInstance3D Node;
        public int[] Soldiers;
        public float[] Buffer;
    }

    readonly List<Batch> _batches = new();
    readonly Dictionary<string, ArrayMesh> _meshes = new();
    SimBattle _battle;

    /// <summary>(Re)builds the batches for a battle. Call again after soldiers are appended (reinforcements).</summary>
    public void Init(SimBattle battle)
    {
        foreach (var b in _batches) b.Node.QueueFree();
        _batches.Clear();
        _battle = battle;

        var byModel = new SortedDictionary<string, List<int>>(StringComparer.Ordinal);
        foreach (var reg in battle.Regiments)
        {
            var model = reg.Def.Model;
            if (!Builders.ContainsKey(model))
            {
                GD.PushWarning($"[View] no mesh registered for model '{model}', using 'infantry'");
                model = "infantry";
            }
            if (!byModel.TryGetValue(model, out var list)) byModel[model] = list = new List<int>();
            for (int k = 0; k < reg.Count; k++) list.Add(reg.First + k);
        }

        foreach (var (model, list) in byModel)
        {
            if (!_meshes.TryGetValue(model, out var mesh)) _meshes[model] = mesh = Builders[model]();
            var mm = new MultiMesh
            {
                TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
                UseCustomData = true,
                Mesh = mesh,
                InstanceCount = list.Count,
            };
            var node = new MultiMeshInstance3D
            {
                Multimesh = mm,
                CustomAabb = new Aabb(new Vector3(-400, -5, -400), new Vector3(800, 20, 800)),
                CastShadow = GeometryInstance3D.ShadowCastingSetting.On,
                Name = "Soldiers_" + model,
            };
            AddChild(node);
            _batches.Add(new Batch { Mm = mm, Node = node, Soldiers = list.ToArray(), Buffer = new float[list.Count * 16] });
        }
        Draw(1f);
        GD.Print($"[View] {battle.Soldiers.Count} soldiers in {_batches.Count} model batch(es)");
    }

    /// <summary>Uploads interpolated soldier transforms; alpha = accumulator / TickDt (C2).</summary>
    public void Draw(float alpha)
    {
        if (_battle == null) return;
        var s = _battle.Soldiers;
        // WHY: read the arrays fresh every frame; the sim swaps Pos/PrevPos each tick.
        var pos = s.Pos; var prev = s.PrevPos; var yaw = s.Yaw; var prevYaw = s.PrevYaw;
        var state = s.State; var phase = s.AnimPhase; var stTime = s.StateTime; var team = s.Team;
        foreach (var b in _batches)
        {
            var buf = b.Buffer; var ids = b.Soldiers;
            Parallel.For(0, ids.Length, n =>
            {
                int i = ids[n];
                int o = n * 16;
                var st = state[i];
                if (st == SoldierState.Gone)
                {
                    Array.Clear(buf, o, 16);
                    buf[o + 7] = -1000f;
                    return;
                }
                var p = System.Numerics.Vector2.Lerp(prev[i], pos[i], alpha);
                float y = SimBattle.LerpAngle(prevYaw[i], yaw[i], alpha);
                float c = MathF.Cos(y), sn = MathF.Sin(y);
                buf[o + 0] = c; buf[o + 1] = 0; buf[o + 2] = sn; buf[o + 3] = p.X;
                buf[o + 4] = 0; buf[o + 5] = 1; buf[o + 6] = 0; buf[o + 7] = 0;
                buf[o + 8] = -sn; buf[o + 9] = 0; buf[o + 10] = c; buf[o + 11] = p.Y;
                buf[o + 12] = phase[i];
                buf[o + 13] = (float)(byte)st;
                buf[o + 14] = stTime[i];
                buf[o + 15] = team[i]; // TODO: + variant once models have variants
            });
            RenderingServer.MultimeshSetBuffer(b.Mm.GetRid(), buf);
        }
    }
}
