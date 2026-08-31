using Robust.Shared.GameStates;
using Robust.Shared.Serialization;

namespace Content.Shared._Functional.TutorialServer;

/// <summary>
/// Present on a mob currently running a tutorial; mirrors curriculum progress for the prompt UI.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class TutorialParticipantComponent : Component
{
    [DataField, AutoNetworkedField]
    public string RoleId = string.Empty;

    [DataField, AutoNetworkedField]
    public int GoalIndex;

    [DataField, AutoNetworkedField]
    public int GoalCount;

    /// <summary>
    /// Locale id for the current goal title (resolved on the client).
    /// </summary>
    [DataField, AutoNetworkedField]
    public string GoalTitle = string.Empty;

    [DataField, AutoNetworkedField]
    public int SubGoalIndex;

    [DataField, AutoNetworkedField]
    public int SubGoalCount;

    /// <summary>
    /// Locale id for the current sub-goal prompt (resolved on the client).
    /// </summary>
    [DataField, AutoNetworkedField]
    public string StepText = string.Empty;

    [DataField, AutoNetworkedField]
    public TutorialStepComplete StepComplete = TutorialStepComplete.Acknowledge;

    /// <summary>
    /// Locale id for the live tip hint (empty when unset; resolved on the client).
    /// </summary>
    [DataField, AutoNetworkedField]
    public string HintText = string.Empty;

    /// <summary>
    /// Locale id for the stuck hint (empty when unset; resolved on the client).
    /// </summary>
    [DataField, AutoNetworkedField]
    public string StuckHintText = string.Empty;

    /// <summary>
    /// Checklist for the active goal (completed + remaining).
    /// </summary>
    [DataField, AutoNetworkedField]
    public List<TutorialHudSubGoalState> SubGoalStates = new();

    /// <summary>
    /// Legacy flat-step index (when role uses <see cref="TutorialRolePrototype.Steps"/> only).
    /// </summary>
    [DataField, AutoNetworkedField]
    public int StepIndex;

    [DataField, AutoNetworkedField]
    public int StepCount;
}

[DataDefinition, Serializable, NetSerializable]
public sealed partial class TutorialHudSubGoalState
{
    /// <summary>Locale id for checklist line text (resolved on the client).</summary>
    [DataField]
    public string Text = string.Empty;

    [DataField]
    public bool Completed;
}

[Serializable, NetSerializable]
public sealed class TutorialAcknowledgeStepEvent : EntityEventArgs;

/// <summary>
/// Raised on the participant after curriculum HUD fields are refreshed.
/// Used by the handheld prompt UI to follow progress.
/// </summary>
[ByRefEvent]
public readonly record struct TutorialParticipantProgressChangedEvent(
    EntityUid GuideUid,
    int OldGoalIndex,
    int OldProgressIndex);
