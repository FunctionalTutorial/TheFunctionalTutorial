namespace Content.Shared._Functional.TutorialServer;

/// <summary>
/// Marks a soft-following tutorial mentor tied to one participant.
/// </summary>
[RegisterComponent]
public sealed partial class TutorialMentorComponent : Component
{
    /// <summary>
    /// Player body this mentor coaches and follows.
    /// </summary>
    [DataField]
    public EntityUid PlayerUid;

    /// <summary>
    /// When set, the mentor is in a catch-up grace window that ends at this time.
    /// After the deadline, pathfinding is checked before any teleport snap.
    /// </summary>
    [ViewVariables]
    public TimeSpan? CatchUpDeadline;

    /// <summary>
    /// True while an async path check for catch-up is in flight.
    /// </summary>
    [ViewVariables]
    public bool CatchUpPathCheckInFlight;

    /// <summary>
    /// Bumped when a new catch-up is requested so stale path results are ignored.
    /// </summary>
    [ViewVariables]
    public int CatchUpGeneration;
}
