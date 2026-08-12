using Robust.Shared.GameStates;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Shared._Functional.TutorialServer;

/// <summary>
/// Scripted tutorial coach that speaks lines keyed by the player's current sub-goal id.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentPause]
public sealed partial class TutorialTrainerComponent : Component
{
    /// <summary>
    /// Dialogue lines spoken when the matching sub-goal is current.
    /// </summary>
    [DataField]
    public List<TutorialTrainerLine> Lines = new();

    /// <summary>
    /// Sub-goal id of the last line spoken (change detection; no timed reminders).
    /// </summary>
    [DataField]
    public string? LastSpokenSubGoal;

    /// <summary>
    /// Unused legacy field kept for map/component compatibility.
    /// </summary>
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField]
    public TimeSpan NextReminderAt;
}

/// <summary>
/// One trainer speech line tied to a curriculum sub-goal id.
/// </summary>
[DataDefinition]
public sealed partial class TutorialTrainerLine
{
    [DataField(required: true)]
    public string SubGoalId = string.Empty;

    [DataField(required: true)]
    public LocId Dialogue = default!;
}
