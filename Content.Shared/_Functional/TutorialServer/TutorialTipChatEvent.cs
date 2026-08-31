using Robust.Shared.Serialization;

namespace Content.Shared._Functional.TutorialServer;

/// <summary>
/// Server → client tip for the chat box. Resolved on the client against the player's culture.
/// Markup in the Fluent value may include <c>[keybind]</c> tags resolved against local bindings.
/// </summary>
[Serializable, NetSerializable]
public sealed class TutorialTipChatEvent : EntityEventArgs
{
    /// <summary>Fluent message id for the tip body.</summary>
    public string LocId = string.Empty;

    /// <summary>
    /// When set, resolved with the client culture and passed to Fluent as the <c>text</c> argument
    /// (e.g. progress toast wrapping a sub-goal LocId).
    /// </summary>
    public string? TextArgLocId;
}
