using Content.Shared._Starlight.Antags.Vampires.Prototypes;
using Robust.Shared.Prototypes;

namespace Content.Shared._Starlight.Antags.Vampires.Components;

[RegisterComponent]
public sealed partial class VampireActionComponent : Component
{
    [DataField]
    public int BloodToUnlock;

    [DataField]
    public float BloodCost;

    [DataField]
    public ProtoId<VampireClassPrototype>? RequiredClass;
}
