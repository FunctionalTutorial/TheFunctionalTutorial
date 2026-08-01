using Robust.Shared.GameObjects;
using Robust.Shared.Network;

namespace Content.Shared._Functional.TutorialServer;

/// <summary>
/// Marks the TutorialServer game rule entity and holds per-player session state.
/// </summary>
[RegisterComponent]
public sealed partial class TutorialServerRuleComponent : Component
{
    /// <summary>
    /// Active / pending tutorial sessions keyed by player.
    /// </summary>
    [DataField]
    public Dictionary<NetUserId, TutorialSessionData> Sessions = new();
}

[DataDefinition, Serializable]
public sealed partial class TutorialSessionData
{
    [DataField]
    public TutorialSessionState State = TutorialSessionState.PendingSelect;

    [DataField]
    public string? SelectedRoleId;

    [DataField]
    public EntityUid MapUid;

    [DataField]
    public EntityUid GridUid;

    [DataField]
    public EntityUid BodyUid;

    [DataField]
    public int StepIndex;

    [DataField]
    public int GoalIndex;

    [DataField]
    public int SubGoalIndex;

    [DataField]
    public bool Completed;

    /// <summary>
    /// Handheld tutorial prompt device given at tutorial start.
    /// </summary>
    [DataField]
    public EntityUid GuideUid;

    /// <summary>
    /// True once the guide Bound UI has been auto-opened for this session
    /// (either at spawn or after the deferred first goal).
    /// </summary>
    [DataField]
    public bool GuideAutoOpened;

    /// <summary>
    /// Player chose Quit on the role picker; do not re-open until they rejoin spawn.
    /// </summary>
    [DataField]
    public bool PickerQuit;

    /// <summary>
    /// Rate-limit for closed-UI progress popups.
    /// </summary>
    [DataField]
    public TimeSpan LastProgressPopup;

    /// <summary>
    /// When true, the player must ReachMarker the chamber entry pad before the goal's YAML sub-goals.
    /// </summary>
    [DataField]
    public bool AwaitingChamberEntryPad;
}

public enum TutorialSessionState : byte
{
    PendingSelect,
    InTutorial,
    Exiting,
}
