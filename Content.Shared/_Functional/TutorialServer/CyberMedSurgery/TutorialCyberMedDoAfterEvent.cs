using Content.Shared.DoAfter;
using Robust.Shared.Serialization;

namespace Content.Shared._Functional.TutorialServer.CyberMedSurgery;

[Serializable, NetSerializable]
public sealed partial class TutorialCyberMedDoAfterEvent : SimpleDoAfterEvent
{
    public string StepId = string.Empty;
}
