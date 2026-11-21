using System;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;

namespace FOMS.PerfTest;

// Engine-agnostic battle simulation for the M0 performance test.
// Uses only System.Numerics so it can move to another engine unchanged.
// Ground plane coordinates: Vector2(x, z). Yaw follows Godot's convention:
// a model facing -Z, rotated by yaw around +Y.

public enum SoldierState : byte { Idle = 0, Walk = 1, Attack = 2, Dead = 3, Gone = 4 }

public sealed class Regiment
{
    public int Team;
    public int First;      // index of first soldier
    public int Count;      // starting head count
    public int Alive;
    public int Cols;
    public int Rows;
    public float Spacing = 1.1f;
    public Vector2 Center; // front-centre of the formation
    public Vector2 Forward;
    public bool Engaged;
    public bool Routing;
    public int TargetRegiment = -1;

    public Vector2 Right => new(-Forward.Y, Forward.X);

    public Vector2 Slot(int local)
    {
        int col = local % Cols;
        int row = local / Cols;
        float x = (col - (Cols - 1) * 0.5f) * Spacing;
        float back = row * Spacing;
        return Center + Right * x - Forward * back;
    }
}

public sealed class BattleSim
{
    // Tuning
    public const float MarchSpeed = 2.2f;
    public const float ChargeSpeed = 3.6f;
    public const float RunSpeed = 4.0f;
    public const float RoutSpeed = 5.0f;
    public const float EngageDistance = 18f;  // regiment front-to-front
    public const float StopDistance = 2.5f;
    public const float SearchRadius = 8f;
    public const float Reach = 1.3f;
    public const float SeparationRadius = 0.75f;
    public const float RoutThreshold = 0.45f; // rout below 45% strength
    public const float MapRadius = 170f;
    public const int MaxHp = 100;

    public readonly int Count;
    public readonly Regiment[] Regiments;

    public Vector2[] Pos, PrevPos, NextPos, Vel;
    public float[] Yaw, PrevYaw;
    public int[] Hp;
    public SoldierState[] State;
    public float[] StateTime, AttackCd, Retarget, AnimPhase;
    public int[] RegimentOf, Target;
    public byte[] Team;

    public int Tick;
    public int Winner = -1;
    public int[] AlivePerTeam = new int[2];

    // Spatial hash (counting sort into a uniform grid)
    const float CellSize = 2f;
    const int GridDim = 256;               // covers -256..+256 m
    const float GridOrigin = -GridDim * CellSize * 0.5f;
    readonly int[] _cellStart = new int[GridDim * GridDim + 1];
    readonly int[] _cellOf;
    readonly int[] _sorted;

    public BattleSim(int soldiers, int regiments)
    {
        regiments = Math.Max(2, regiments - regiments % 2);
        int perReg = Math.Max(1, soldiers / regiments);
        Count = perReg * regiments;
        Regiments = new Regiment[regiments];

        Pos = new Vector2[Count]; PrevPos = new Vector2[Count]; NextPos = new Vector2[Count]; Vel = new Vector2[Count];
        Yaw = new float[Count]; PrevYaw = new float[Count];
        Hp = new int[Count]; State = new SoldierState[Count];
        StateTime = new float[Count]; AttackCd = new float[Count]; Retarget = new float[Count]; AnimPhase = new float[Count];
        RegimentOf = new int[Count]; Target = new int[Count]; Team = new byte[Count];
        _cellOf = new int[Count]; _sorted = new int[Count];

        int perSide = regiments / 2;
        int perLine = Math.Min(5, perSide);
        int cols = Math.Max(4, (int)MathF.Round(MathF.Sqrt(perReg * 5f)));
        int rows = (perReg + cols - 1) / cols;
        float width = cols * 1.1f;
        float pitch = width + 6f;

        int idx = 0;
        for (int r = 0; r < regiments; r++)
        {
            int team = r < perSide ? 0 : 1;
            int k = r % perSide;
            int line = k / perLine;
            int inLine = k % perLine;
            int lineCount = Math.Min(perLine, perSide - line * perLine);
            float x = (inLine - (lineCount - 1) * 0.5f) * pitch;
            float zFront = 50f + line * (rows * 1.1f + 20f);
            var fwd = team == 0 ? new Vector2(0, 1) : new Vector2(0, -1);
            var reg = new Regiment
            {
                Team = team, First = idx, Count = perReg, Alive = perReg, Cols = cols, Rows = rows,
                Center = new Vector2(x, team == 0 ? -zFront : zFront), Forward = fwd,
            };
            Regiments[r] = reg;
            float yaw = YawOf(fwd);
            for (int s = 0; s < perReg; s++, idx++)
            {
                var p = reg.Slot(s) + Jitter(idx) * 0.15f;
                Pos[idx] = PrevPos[idx] = p;
                Yaw[idx] = PrevYaw[idx] = yaw;
                Hp[idx] = MaxHp;
                State[idx] = SoldierState.Idle;
                RegimentOf[idx] = r;
                Target[idx] = -1;
                Team[idx] = (byte)team;
                AnimPhase[idx] = Rand01((uint)idx, 0, 7) * 6.28f;
                Retarget[idx] = Rand01((uint)idx, 0, 3) * 0.5f;
            }
        }
        AlivePerTeam[0] = AlivePerTeam[1] = perReg * perSide;
    }

    public void Step(float dt)
    {
        Tick++;
        BuildGrid();
        UpdateRegiments(dt);
        Array.Copy(Yaw, PrevYaw, Count);

        Parallel.For(0, Count, i => UpdateSoldier(i, dt));

        // swap position buffers: Prev <- Pos <- Next
        var tmp = PrevPos; PrevPos = Pos; Pos = NextPos; NextPos = tmp;

        // resolve deaths sequentially
        for (int i = 0; i < Count; i++)
        {
            var st = State[i];
            if (st == SoldierState.Dead || st == SoldierState.Gone) continue;
            if (Hp[i] <= 0)
            {
                State[i] = SoldierState.Dead;
                StateTime[i] = 0;
                Vel[i] = Vector2.Zero;
                var reg = Regiments[RegimentOf[i]];
                reg.Alive--;
                AlivePerTeam[Team[i]]--;
            }
        }
        CheckVictory();
    }

    void UpdateRegiments(float dt)
    {
        foreach (var reg in Regiments)
        {
            if (!reg.Routing && reg.Alive < reg.Count * RoutThreshold) reg.Routing = true;
            if (reg.Routing) { reg.Engaged = false; continue; }

            // nearest non-routing enemy regiment
            Regiment best = null; float bestD = float.MaxValue; int bestIdx = -1;
            for (int j = 0; j < Regiments.Length; j++)
            {
                var o = Regiments[j];
                if (o.Team == reg.Team || o.Routing || o.Alive == 0) continue;
                float d = Vector2.Distance(o.Center, reg.Center);
                if (d < bestD) { bestD = d; best = o; bestIdx = j; }
            }
            reg.TargetRegiment = bestIdx;
            if (best == null) { reg.Engaged = false; continue; }

            reg.Engaged = bestD < EngageDistance;
            var dir = Vector2.Normalize(best.Center - reg.Center);
            if (!reg.Engaged)
            {
                // turn the formation gradually toward the target
                reg.Forward = Vector2.Normalize(Vector2.Lerp(reg.Forward, dir, 1f - MathF.Exp(-0.8f * dt)));
            }
            if (bestD > StopDistance)
            {
                float speed = bestD < 40f ? ChargeSpeed : MarchSpeed;
                if (reg.Engaged) speed = 0.6f; // press slowly once in contact
                reg.Center += dir * MathF.Min(speed * dt, bestD - StopDistance);
            }
        }
    }

    void UpdateSoldier(int i, float dt)
    {
        var st = State[i];
        if (st == SoldierState.Dead || st == SoldierState.Gone)
        {
            NextPos[i] = Pos[i];
            if (st == SoldierState.Dead) StateTime[i] += dt;
            return;
        }

        var reg = Regiments[RegimentOf[i]];
        var p = Pos[i];
        Vector2 desired;
        int faceTarget = -1;
        bool attacked = false;

        if (reg.Routing)
        {
            desired = -reg.Forward * RoutSpeed + Jitter(i) * 0.8f;
            if (p.LengthSquared() > MapRadius * MapRadius)
            {
                State[i] = SoldierState.Gone;
                NextPos[i] = p;
                return;
            }
        }
        else
        {
            int t = -1;
            if (reg.Engaged)
            {
                t = Target[i];
                Retarget[i] -= dt;
                if (t < 0 || !IsAlive(t) || Retarget[i] <= 0)
                {
                    t = FindNearestEnemy(i, p, SearchRadius);
                    Target[i] = t;
                    Retarget[i] = 0.5f;
                }
            }
            else Target[i] = -1;

            if (t >= 0)
            {
                var d = Pos[t] - p;
                float dist = d.Length();
                faceTarget = t;
                if (dist > Reach)
                {
                    desired = d / MathF.Max(dist, 1e-4f) * RunSpeed;
                }
                else
                {
                    desired = Vector2.Zero;
                    AttackCd[i] -= dt;
                    if (AttackCd[i] <= 0)
                    {
                        AttackCd[i] = 1.2f + Rand01((uint)i, (uint)Tick, 11) * 0.6f;
                        attacked = true;
                        if (Rand01((uint)i, (uint)Tick, 13) < 0.4f)
                        {
                            int dmg = 15 + (int)(Rand01((uint)i, (uint)Tick, 17) * 15f);
                            Interlocked.Add(ref Hp[t], -dmg);
                        }
                    }
                }
            }
            else
            {
                var slot = reg.Slot(i - reg.First);
                var delta = slot - p;
                float len = delta.Length();
                float speed = MathF.Min(len * 2f, reg.Engaged ? RunSpeed : ChargeSpeed + 0.6f);
                desired = len > 1e-3f ? delta / len * speed : Vector2.Zero;
            }
        }

        // separation from all living bodies nearby
        desired += Separation(i, p);

        var v = Vector2.Lerp(Vel[i], desired, 1f - MathF.Exp(-8f * dt));
        Vel[i] = v;
        NextPos[i] = p + v * dt;
        float speedNow = v.Length();

        // facing
        float targetYaw;
        if (faceTarget >= 0) targetYaw = YawOf(Pos[faceTarget] - p);
        else if (speedNow > 0.4f) targetYaw = YawOf(v);
        else targetYaw = YawOf(reg.Forward);
        Yaw[i] = LerpAngle(Yaw[i], targetYaw, 1f - MathF.Exp(-10f * dt));

        // animation state
        if (attacked) { State[i] = SoldierState.Attack; StateTime[i] = 0; }
        else
        {
            StateTime[i] += dt;
            if (st != SoldierState.Attack || StateTime[i] > 0.5f)
            {
                var ns = speedNow > 0.4f ? SoldierState.Walk : SoldierState.Idle;
                if (ns != st) { State[i] = ns; StateTime[i] = 0; }
            }
        }
        AnimPhase[i] += speedNow * dt * 3.2f;
    }

    Vector2 Separation(int i, Vector2 p)
    {
        Vector2 push = Vector2.Zero;
        CellCoords(p, out int cx, out int cz);
        for (int z = cz - 1; z <= cz + 1; z++)
        {
            if ((uint)z >= GridDim) continue;
            for (int x = cx - 1; x <= cx + 1; x++)
            {
                if ((uint)x >= GridDim) continue;
                int c = z * GridDim + x;
                for (int k = _cellStart[c]; k < _cellStart[c + 1]; k++)
                {
                    int j = _sorted[k];
                    if (j == i) continue;
                    var d = p - Pos[j];
                    float d2 = d.LengthSquared();
                    if (d2 >= SeparationRadius * SeparationRadius) continue;
                    if (d2 < 1e-6f) { d = Jitter(i + j); d2 = d.LengthSquared() + 1e-6f; }
                    float dist = MathF.Sqrt(d2);
                    push += d / dist * (SeparationRadius - dist) * 12f;
                }
            }
        }
        return push;
    }

    int FindNearestEnemy(int i, Vector2 p, float radius)
    {
        byte team = Team[i];
        int r = (int)MathF.Ceiling(radius / CellSize);
        CellCoords(p, out int cx, out int cz);
        int best = -1; float bestD2 = radius * radius;
        for (int z = cz - r; z <= cz + r; z++)
        {
            if ((uint)z >= GridDim) continue;
            for (int x = cx - r; x <= cx + r; x++)
            {
                if ((uint)x >= GridDim) continue;
                int c = z * GridDim + x;
                for (int k = _cellStart[c]; k < _cellStart[c + 1]; k++)
                {
                    int j = _sorted[k];
                    if (Team[j] == team) continue;
                    float d2 = Vector2.DistanceSquared(p, Pos[j]);
                    if (d2 < bestD2) { bestD2 = d2; best = j; }
                }
            }
        }
        return best;
    }

    void BuildGrid()
    {
        Array.Clear(_cellStart);
        for (int i = 0; i < Count; i++)
        {
            if (!IsAlive(i)) { _cellOf[i] = -1; continue; }
            CellCoords(Pos[i], out int x, out int z);
            x = Math.Clamp(x, 0, GridDim - 1); z = Math.Clamp(z, 0, GridDim - 1);
            int c = z * GridDim + x;
            _cellOf[i] = c;
            _cellStart[c + 1]++;
        }
        for (int c = 0; c < GridDim * GridDim; c++) _cellStart[c + 1] += _cellStart[c];
        // fill using a running cursor per cell; reuse a temporary copy of starts
        var cursor = _cursor ??= new int[GridDim * GridDim];
        Array.Copy(_cellStart, cursor, GridDim * GridDim);
        for (int i = 0; i < Count; i++)
        {
            int c = _cellOf[i];
            if (c >= 0) _sorted[cursor[c]++] = i;
        }
    }
    int[] _cursor;

    void CheckVictory()
    {
        if (Winner >= 0) return;
        bool[] beaten = { true, true };
        foreach (var reg in Regiments)
            if (!reg.Routing && reg.Alive > 0) beaten[reg.Team] = false;
        if (beaten[0] && !beaten[1]) Winner = 1;
        else if (beaten[1] && !beaten[0]) Winner = 0;
        else if (beaten[0] && beaten[1]) Winner = 2; // mutual collapse
    }

    public bool IsAlive(int i) => State[i] != SoldierState.Dead && State[i] != SoldierState.Gone && Hp[i] > 0;

    static void CellCoords(Vector2 p, out int x, out int z)
    {
        x = (int)MathF.Floor((p.X - GridOrigin) / CellSize);
        z = (int)MathF.Floor((p.Y - GridOrigin) / CellSize);
    }

    public static float YawOf(Vector2 dir) => MathF.Atan2(-dir.X, -dir.Y);

    public static float LerpAngle(float a, float b, float t)
    {
        float diff = MathF.IEEERemainder(b - a, MathF.Tau);
        return a + diff * t;
    }

    static uint Hash(uint x)
    {
        x ^= x >> 16; x *= 0x7feb352d; x ^= x >> 15; x *= 0x846ca68b; x ^= x >> 16;
        return x;
    }

    public static float Rand01(uint a, uint b, uint salt) =>
        (Hash(a * 0x9E3779B9u ^ Hash(b + salt * 0x85EBCA6Bu)) & 0xFFFFFF) / 16777216f;

    static Vector2 Jitter(int i) =>
        new(Rand01((uint)i, 1, 5) * 2f - 1f, Rand01((uint)i, 2, 5) * 2f - 1f);
}
