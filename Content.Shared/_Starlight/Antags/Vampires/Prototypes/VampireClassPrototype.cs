using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Shared._Starlight.Antags.Vampires.Prototypes;

[Prototype] //Tutorial: drop redundant type (RA0042)
public sealed partial class VampireClassPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField(required: true)]
    public string Tooltip { get; private set; } = default!;

    [DataField(required: true)]
    public SpriteSpecifier Icon { get; private set; } = default!;

    [DataField(required: true)]
    public string ClassComponent { get; private set; } = default!;

    [DataField]
    public List<EntProtoId> Actions { get; private set; } = new();
}
