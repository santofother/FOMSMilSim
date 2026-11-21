// Terrain queries for the sim. Owner: S01 (interface + FlatTerrain); heightmap and water: wave B.
// Public API: ITerrain, FlatTerrain
using System.Numerics;

namespace FOMS.Sim.Terrain;

/// <summary>What the sim needs to know about the ground.</summary>
public interface ITerrain
{
    /// <summary>Ground height in metres at (x, z).</summary>
    float Height(Vector2 p);
    /// <summary>Can soldiers stand here?</summary>
    bool Walkable(Vector2 p);
}

/// <summary>Flat plain at height 0, walkable everywhere.</summary>
public sealed class FlatTerrain : ITerrain
{
    public float Height(Vector2 p) => 0f;
    public bool Walkable(Vector2 p) => true;
}
