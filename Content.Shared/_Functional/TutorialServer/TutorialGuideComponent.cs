namespace Content.Shared._Functional.TutorialServer;

/// <summary>
/// Handheld tutorial prompt device. Activating it opens the Bound UI for the
/// holder's current tutorial stage.
/// </summary>
[RegisterComponent]
public sealed partial class TutorialGuideComponent : Component
{
    /// <summary>
    /// Goal currently displayed in the Bound UI (may be behind live progress).
    /// </summary>
    [ViewVariables]
    public int ViewGoalIndex;

    /// <summary>
    /// Sub-goal / legacy step currently displayed in the Bound UI.
    /// Independent of authoritative progress so players can page back through completed steps.
    /// </summary>
    [ViewVariables]
    public int ViewIndex;
}
