using Robust.Shared.Serialization;

namespace Content.Shared._Functional.TutorialServer;

/// <summary>
/// Server → client control hint. <see cref="LocId"/> is resolved on the client so the player's
/// language applies; markup may include <c>[keybind="MoveUp"]</c> tags resolved against local bindings.
/// </summary>
[Serializable, NetSerializable]
public sealed class TutorialControlHintEvent : EntityEventArgs
{
    /// <summary>
    /// Fluent message id to display. Ignored when <see cref="Show"/> is false.
    /// </summary>
    public string LocId = string.Empty;

    /// <summary>
    /// False hides the banner (the current sub-goal has no control to teach).
    /// </summary>
    public bool Show;
}
