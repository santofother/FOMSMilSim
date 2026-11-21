// SpatialGrid: uniform grid of living soldiers, rebuilt each tick with a counting sort.
// Owner: S01
// Public API: SpatialGrid (Build, CellCoords, CellRange, Sorted, Dim)
using System;
using System.Numerics;

namespace FOMS.Sim;

/// <summary>
/// Buckets living soldiers into square cells. Build is sequential; queries are read-only and thread safe.
/// Within a cell, soldiers are in ascending index order (deterministic, C3).
/// </summary>
public sealed class SpatialGrid
{
    /// <summary>Cell edge in metres.</summary>
    public const float CellSize = 2f;
    /// <summary>Cells per side; covers -256..+256 m.</summary>
    public const int Dim = 256;
    const float Origin = -Dim * CellSize * 0.5f;

    readonly int[] _cellStart = new int[Dim * Dim + 1];
    readonly int[] _cursor = new int[Dim * Dim];
    int[] _cellOf = Array.Empty<int>();
    int[] _sorted = Array.Empty<int>();

    /// <summary>Soldier indices sorted by cell. Use with CellRange.</summary>
    public int[] Sorted => _sorted;

    /// <summary>Rebuilds the grid from soldiers for which <paramref name="alive"/> is true.</summary>
    public void Build(SoldierStore s, bool[] alive)
    {
        int n = s.Count;
        if (_cellOf.Length < n) { _cellOf = new int[n]; _sorted = new int[n]; }
        Array.Clear(_cellStart);
        for (int i = 0; i < n; i++)
        {
            if (!alive[i]) { _cellOf[i] = -1; continue; }
            CellCoords(s.Pos[i], out int x, out int z);
            x = Math.Clamp(x, 0, Dim - 1); z = Math.Clamp(z, 0, Dim - 1);
            int c = z * Dim + x;
            _cellOf[i] = c;
            _cellStart[c + 1]++;
        }
        for (int c = 0; c < Dim * Dim; c++) _cellStart[c + 1] += _cellStart[c];
        Array.Copy(_cellStart, _cursor, Dim * Dim);
        for (int i = 0; i < n; i++)
        {
            int c = _cellOf[i];
            if (c >= 0) _sorted[_cursor[c]++] = i;
        }
    }

    /// <summary>Range [start, end) into Sorted for cell (x, z). Out-of-range cells are empty.</summary>
    public void CellRange(int x, int z, out int start, out int end)
    {
        if ((uint)x >= Dim || (uint)z >= Dim) { start = end = 0; return; }
        int c = z * Dim + x;
        start = _cellStart[c]; end = _cellStart[c + 1];
    }

    public static void CellCoords(Vector2 p, out int x, out int z)
    {
        x = (int)MathF.Floor((p.X - Origin) / CellSize);
        z = (int)MathF.Floor((p.Y - Origin) / CellSize);
    }
}
