// Formation slots: which soldier stands in which home slot. When a regiment faces about or changes frontage, men
// take the slot nearest where they stand (front-most men form the front rank, ordered left to right) instead of
// keeping their old slot and walking across the block. Owner: S04b.
// Public API: none (internal to the sim; MeleeSpacing calls it).
using System;
using System.Numerics;

namespace FOMS.Sim;

internal sealed class FormationSlots
{
    /// <summary>A facing jump bigger than this in one tick is a re-form (wheeling turns ≤ ~0.05 rad per tick).</summary>
    const float ReslotAngle = 0.35f;

    /// <summary>Home slot h of regiment r is held by soldier local index Holder[r.First + h].</summary>
    public int[] Holder = Array.Empty<int>();
    Vector2[] _lastFacing = Array.Empty<Vector2>();
    int[] _lastCols = Array.Empty<int>();
    float[] _keys = Array.Empty<float>();
    int[] _items = Array.Empty<int>();

    /// <summary>Sequential, start of tick: set up new regiments and re-form those whose layout jumped.</summary>
    public void Update(Regiment[] regs, SoldierStore s, bool[] alive)
    {
        if (Holder.Length < s.Count)
        {
            int old = Holder.Length;
            Array.Resize(ref Holder, s.Count);
            foreach (var reg in regs)
                if (reg.First + reg.Count > old)
                    for (int k = 0; k < reg.Count; k++) Holder[reg.First + k] = k;
        }
        if (_lastCols.Length < regs.Length)
        {
            int old = _lastCols.Length;
            Array.Resize(ref _lastCols, regs.Length);
            Array.Resize(ref _lastFacing, regs.Length);
            for (int r = old; r < regs.Length; r++) { _lastCols[r] = regs[r].Cols; _lastFacing[r] = regs[r].Facing; }
        }

        foreach (var reg in regs)
        {
            bool jumped = reg.Cols != _lastCols[reg.Id]
                          || MathF.Abs(FormationSteering.SignedAngle(_lastFacing[reg.Id], reg.Facing)) > ReslotAngle;
            if (jumped && !reg.Routing) Reslot(reg, s, alive);
            _lastCols[reg.Id] = reg.Cols;
            _lastFacing[reg.Id] = reg.Facing;
        }
    }

    void Reslot(Regiment reg, SoldierStore s, bool[] alive)
    {
        int n = reg.Count, cols = Math.Max(1, reg.Cols);
        if (_keys.Length < n) { _keys = new float[n]; _items = new int[n]; }
        var right = reg.Right;
        int a = 0;
        for (int k = 0; k < n; k++)
        {
            int i = reg.First + k;
            if (!alive[i]) continue;
            _items[a] = k;
            _keys[a] = -Vector2.Dot(s.Pos[i] - reg.Centre, reg.Facing); // most forward first
            a++;
        }
        Array.Sort(_keys, _items, 0, a);
        for (int r = 0; r < a; r += cols)
        {
            int m = Math.Min(cols, a - r);
            for (int j = r; j < r + m; j++) _keys[j] = Vector2.Dot(s.Pos[reg.First + _items[j]] - reg.Centre, right);
            Array.Sort(_keys, _items, r, m); // left to right within the rank
        }
        for (int h = 0; h < a; h++) Holder[reg.First + h] = _items[h];
        // The dead keep the rear homes; the column fill skips them.
        int next = a;
        for (int k = 0; k < n; k++)
            if (!alive[reg.First + k]) Holder[reg.First + next++] = k;
    }
}
