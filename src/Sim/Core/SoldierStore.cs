// SoldierStore: struct-of-arrays storage for every soldier in a battle (contract C4).
// Owner: S01
// Public API: SoldierState, SoldierStore (Count, arrays, AddRange, IsAlive)
using System;
using System.Numerics;

namespace FOMS.Sim;

/// <summary>Per-soldier animation / life state. Stored as a byte; also sent to the shader (C9).</summary>
public enum SoldierState : byte { Idle = 0, Walk = 1, Attack = 2, Dead = 3, Gone = 4 }

/// <summary>
/// All soldiers as parallel arrays. Index i is one soldier for the whole battle.
/// CONTRACT: each regiment owns the contiguous range [First, First + Count). Arrays only grow between
/// ticks (AddRange). Dead soldiers keep their slot (State = Dead); soldiers who leave the map become Gone.
/// CONTRACT: Pos / PrevPos are swapped every tick, so never cache the array references across ticks.
/// </summary>
public sealed class SoldierStore
{
    /// <summary>Number of soldiers in use.</summary>
    public int Count { get; private set; }

    /// <summary>Ground position (x, z) at the current tick.</summary>
    public Vector2[] Pos = Array.Empty<Vector2>();
    /// <summary>Ground position at the previous tick (for interpolation, C2).</summary>
    public Vector2[] PrevPos = Array.Empty<Vector2>();
    /// <summary>Velocity in m/s.</summary>
    public Vector2[] Vel = Array.Empty<Vector2>();
    /// <summary>Facing yaw (C8) now and at the previous tick.</summary>
    public float[] Yaw = Array.Empty<float>(), PrevYaw = Array.Empty<float>();
    /// <summary>Hit points. Damage may land from other threads via Interlocked.Add (C3).</summary>
    public int[] Hp = Array.Empty<int>();
    public SoldierState[] State = Array.Empty<SoldierState>();
    /// <summary>Seconds since the current State began.</summary>
    public float[] StateTime = Array.Empty<float>();
    /// <summary>Walk-cycle phase for the shader.</summary>
    public float[] AnimPhase = Array.Empty<float>();
    public int[] RegimentId = Array.Empty<int>();
    public byte[] Team = Array.Empty<byte>();
    /// <summary>Current melee target soldier, -1 = none.</summary>
    public int[] Target = Array.Empty<int>();
    /// <summary>Seconds until next swing / next target search.</summary>
    public float[] AttackCd = Array.Empty<float>(), Retarget = Array.Empty<float>();

    // scratch buffer for the next-tick positions (internal to Battle)
    internal Vector2[] NextPos = Array.Empty<Vector2>();

    /// <summary>Appends <paramref name="n"/> soldiers and returns the index of the first one.</summary>
    public int AddRange(int n)
    {
        int first = Count;
        int size = Count + n;
        Grow(ref Pos, size); Grow(ref PrevPos, size); Grow(ref NextPos, size); Grow(ref Vel, size);
        Grow(ref Yaw, size); Grow(ref PrevYaw, size); Grow(ref Hp, size); Grow(ref State, size);
        Grow(ref StateTime, size); Grow(ref AnimPhase, size); Grow(ref RegimentId, size); Grow(ref Team, size);
        Grow(ref Target, size); Grow(ref AttackCd, size); Grow(ref Retarget, size);
        Count = size;
        return first;
    }

    /// <summary>True while the soldier is on the field and fighting fit.</summary>
    public bool IsAlive(int i) => State[i] != SoldierState.Dead && State[i] != SoldierState.Gone && Hp[i] > 0;

    internal void SwapPositions()
    {
        var tmp = PrevPos; PrevPos = Pos; Pos = NextPos; NextPos = tmp;
    }

    static void Grow<T>(ref T[] a, int size)
    {
        if (a.Length < size) Array.Resize(ref a, size);
    }
}
