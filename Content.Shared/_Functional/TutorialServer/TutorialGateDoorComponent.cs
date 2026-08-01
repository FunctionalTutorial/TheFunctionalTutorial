using Robust.Shared.GameStates;

namespace Content.Shared._Functional.TutorialServer;

/// <summary>
/// Marks an inter-chamber tutorial door that stays bolted until a goal index is reached.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class TutorialGateDoorComponent : Component
{
    /// <summary>
    /// Unlock (unbolt + open) when the player's <c>GoalIndex</c> is greater than or equal to this.
    /// Door between chamber 0 and 1 uses 1, etc.
    /// </summary>
    [DataField]
    public int UnlockAtGoalIndex = 1;

    [DataField]
    public bool Unlocked;

    /// <summary>
    /// When true, the door stays closed for crowbar practice and is never auto-unbolted/opened
    /// by <c>UnlockGatesForGoal</c>.
    /// </summary>
    [DataField]
    public bool RequirePry;
}
