using Robust.Shared.Serialization;

namespace Content.Shared._Starlight.Antags.Vampires;

[Serializable, NetSerializable]
public sealed partial class VampireClassClosedBuiMsg : BoundUserInterfaceMessage;

[Serializable, NetSerializable]
public enum VampireClassUiKey : byte
{
    Key,
}

[Serializable, NetSerializable]
public sealed class VampireClassChosenBuiMsg : BoundUserInterfaceMessage
{
    public string Choice { get; init; } = string.Empty;
}
