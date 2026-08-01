using Content.Shared.Actions;
using Content.Shared.DoAfter;
using Robust.Shared.Serialization;

namespace Content.Shared._Starlight.Antags.Vampires;

public sealed partial class VampireClassSelectActionEvent : InstantActionEvent;

public sealed partial class VampireToggleFangsActionEvent : InstantActionEvent;

[Serializable, NetSerializable]
public sealed partial class VampireDrinkBloodDoAfterEvent : SimpleDoAfterEvent;
