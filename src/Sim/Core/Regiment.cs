// Regiment: the unit of command and the read model UI / AI use (contract C4b).
// Owner: S01 (fields). Other cards fill in Morale, Fatigue, Ammo, Order execution.
// Public API: Regiment, RegimentActivity, MoraleState
using System.Numerics;
using FOMS.Sim.Data;

namespace FOMS.Sim;

/// <summary>What a regiment is doing right now (for UI and AI).</summary>
public enum RegimentActivity { Idle, Moving, Engaging, Fighting, Routing, Rallying, Shattered }

/// <summary>Coarse morale band. TODO(S05): morale system.</summary>
public enum MoraleState { Eager, Steady, Wavering, Routing, Shattered }

/// <summary>A block of soldiers sharing orders, formation and morale.</summary>
public sealed class Regiment
{
    // ---- C4b read model ----
    public int Id;
    public int Team;
    public string UnitId;
    /// <summary>Starting head count. CONTRACT: soldiers are [First, First + Count) in SoldierStore.</summary>
    public int Count;
    public int Alive;
    /// <summary>Front centre of the formation (ground x, z).</summary>
    public Vector2 Centre;
    /// <summary>Unit vector the formation faces.</summary>
    public Vector2 Facing = new(0, 1);
    /// <summary>Frontage in metres.</summary>
    public float Width;
    /// <summary>Current order, null = none. TODO(S02): executed by src/Sim/Orders/.</summary>
    public Order Order;
    public RegimentActivity Activity = RegimentActivity.Idle;
    /// <summary>0–100. TODO(S05).</summary>
    public float Morale = 100f;
    public MoraleState MoraleState = MoraleState.Steady;
    /// <summary>0 (fresh) – 1 (exhausted). TODO(S04).</summary>
    public float Fatigue;
    /// <summary>Missile volleys left; 0 for melee units. TODO(S07).</summary>
    public int Ammo;

    // ---- internals other sim modules may use ----
    /// <summary>Index of the first soldier in SoldierStore.</summary>
    public int First;
    public int Cols, Rows;
    /// <summary>Live stats (replaced by Battle.ApplyDefs, C7).</summary>
    public UnitDef Def;
    /// <summary>Regiment this one is heading for, -1 = none.</summary>
    public int TargetRegiment = -1;

    public bool Routing => Activity is RegimentActivity.Routing or RegimentActivity.Shattered;
    public bool Engaged => Activity == RegimentActivity.Fighting;
    public Vector2 Right => new(-Facing.Y, Facing.X);

    /// <summary>World position of formation slot <paramref name="local"/> (0 = front-left).</summary>
    public Vector2 Slot(int local)
    {
        int col = local % Cols;
        int row = local / Cols;
        float s = Def.Spacing;
        float x = (col - (Cols - 1) * 0.5f) * s;
        return Centre + Right * x - Facing * (row * s);
    }
}
