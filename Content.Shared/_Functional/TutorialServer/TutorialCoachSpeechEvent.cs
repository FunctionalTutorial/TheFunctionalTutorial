using Robust.Shared.Serialization;

namespace Content.Shared._Functional.TutorialServer;

/// <summary>
/// Server → listening client only: coach/mentor line in the player's language (solo tutorial instances).
/// </summary>
[Serializable, NetSerializable]
public sealed class TutorialCoachSpeechEvent : EntityEventArgs
{
    public NetEntity Speaker;

    /// <summary>Fluent message id for the spoken line.</summary>
    public string LocId = string.Empty;
}
