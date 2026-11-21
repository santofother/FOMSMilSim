using System.Collections.Generic;
using Godot;

namespace FOMS.PerfTest;

// Builds a low-poly placeholder spearman (facing -Z, feet at origin) from
// primitive meshes. Each vertex carries:
//   COLOR  = base colour
//   UV.x   = body part id (used by the shader to animate limbs)
//   UV.y   = 1 if the part is team cloth (tinted per side in the shader)
public static class SoldierMesh
{
    public enum Part { Torso = 0, Head = 1, LegL = 2, LegR = 3, SpearArm = 4, ShieldArm = 5 }

    static readonly Color Skin = new(0.78f, 0.6f, 0.46f);
    static readonly Color Metal = new(0.55f, 0.56f, 0.6f);
    static readonly Color Leather = new(0.35f, 0.24f, 0.15f);
    static readonly Color Cloth = new(1f, 1f, 1f);
    static readonly Color Wood = new(0.45f, 0.32f, 0.2f);

    public static ArrayMesh Build(int segments, out int triangleCount)
    {
        var verts = new List<Vector3>();
        var norms = new List<Vector3>();
        var cols = new List<Color>();
        var uvs = new List<Vector2>();
        var idx = new List<int>();

        void Add(PrimitiveMesh m, Transform3D xf, Color c, Part part, bool cloth)
        {
            var a = m.GetMeshArrays();
            var v = a[(int)Mesh.ArrayType.Vertex].AsVector3Array();
            var n = a[(int)Mesh.ArrayType.Normal].AsVector3Array();
            var ix = a[(int)Mesh.ArrayType.Index].AsInt32Array();
            int baseIdx = verts.Count;
            for (int k = 0; k < v.Length; k++)
            {
                verts.Add(xf * v[k]);
                norms.Add((xf.Basis * n[k]).Normalized());
                cols.Add(c);
                uvs.Add(new Vector2((int)part, cloth ? 1 : 0));
            }
            foreach (var k in ix) idx.Add(baseIdx + k);
        }

        Transform3D At(float x, float y, float z) => new(Basis.Identity, new Vector3(x, y, z));
        Transform3D AtRotX(float x, float y, float z, float deg) =>
            new(new Basis(Vector3.Right, Mathf.DegToRad(deg)), new Vector3(x, y, z));

        int seg = segments;
        // legs
        Add(new CylinderMesh { TopRadius = 0.085f, BottomRadius = 0.07f, Height = 0.86f, RadialSegments = seg, Rings = 1 },
            At(-0.1f, 0.45f, 0), Leather, Part.LegL, false);
        Add(new CylinderMesh { TopRadius = 0.085f, BottomRadius = 0.07f, Height = 0.86f, RadialSegments = seg, Rings = 1 },
            At(0.1f, 0.45f, 0), Leather, Part.LegR, false);
        // tunic skirt + torso (team cloth) + mail shirt
        Add(new CylinderMesh { TopRadius = 0.21f, BottomRadius = 0.27f, Height = 0.3f, RadialSegments = seg, Rings = 1 },
            At(0, 0.88f, 0), Cloth, Part.Torso, true);
        Add(new CylinderMesh { TopRadius = 0.24f, BottomRadius = 0.2f, Height = 0.62f, RadialSegments = seg, Rings = 2 },
            At(0, 1.33f, 0), Metal, Part.Torso, false);
        Add(new CylinderMesh { TopRadius = 0.205f, BottomRadius = 0.205f, Height = 0.08f, RadialSegments = seg, Rings = 1 },
            At(0, 1.04f, 0), Leather, Part.Torso, false);
        // head + helmet + crest
        Add(new SphereMesh { Radius = 0.12f, Height = 0.26f, RadialSegments = seg, Rings = seg / 2 },
            At(0, 1.76f, 0), Skin, Part.Head, false);
        Add(new SphereMesh { Radius = 0.14f, Height = 0.2f, RadialSegments = seg, Rings = seg / 2, IsHemisphere = true },
            At(0, 1.8f, 0), Metal, Part.Head, false);
        Add(new BoxMesh { Size = new Vector3(0.03f, 0.08f, 0.22f) },
            At(0, 1.93f, 0.02f), Cloth, Part.Head, true);
        // spear arm + spear
        Add(new CylinderMesh { TopRadius = 0.06f, BottomRadius = 0.05f, Height = 0.58f, RadialSegments = seg / 2, Rings = 1 },
            AtRotX(0.3f, 1.32f, -0.12f, 35), Skin, Part.SpearArm, false);
        Add(new CylinderMesh { TopRadius = 0.022f, BottomRadius = 0.022f, Height = 2.5f, RadialSegments = seg / 2, Rings = 1 },
            AtRotX(0.32f, 1.2f, -0.35f, 80), Wood, Part.SpearArm, false);
        Add(new CylinderMesh { TopRadius = 0f, BottomRadius = 0.04f, Height = 0.22f, RadialSegments = seg / 2, Rings = 1 },
            AtRotX(0.32f, 1.41f, -1.6f, -100), Metal, Part.SpearArm, false);
        // shield arm + shield (team cloth face, metal boss)
        Add(new CylinderMesh { TopRadius = 0.06f, BottomRadius = 0.05f, Height = 0.55f, RadialSegments = seg / 2, Rings = 1 },
            AtRotX(-0.3f, 1.28f, -0.1f, 25), Skin, Part.ShieldArm, false);
        Add(new CylinderMesh { TopRadius = 0.36f, BottomRadius = 0.36f, Height = 0.05f, RadialSegments = seg + 4, Rings = 1 },
            new Transform3D(new Basis(Vector3.Right, Mathf.DegToRad(90)).Scaled(new Vector3(0.85f, 1f, 1.3f)), new Vector3(-0.32f, 1.18f, -0.32f)),
            Cloth, Part.ShieldArm, true);
        Add(new SphereMesh { Radius = 0.07f, Height = 0.07f, RadialSegments = seg / 2, Rings = 3, IsHemisphere = true },
            new Transform3D(new Basis(Vector3.Right, Mathf.DegToRad(-90)), new Vector3(-0.32f, 1.18f, -0.35f)), Metal, Part.ShieldArm, false);

        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = verts.ToArray();
        arrays[(int)Mesh.ArrayType.Normal] = norms.ToArray();
        arrays[(int)Mesh.ArrayType.Color] = cols.ToArray();
        arrays[(int)Mesh.ArrayType.TexUV] = uvs.ToArray();
        arrays[(int)Mesh.ArrayType.Index] = idx.ToArray();

        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        triangleCount = idx.Count / 3;
        return mesh;
    }
}
