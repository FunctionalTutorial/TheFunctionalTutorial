using Content.Shared.Silicons.Borgs;
using Content.Shared.Silicons.Borgs.Components;
using JetBrains.Annotations;
using Robust.Client.UserInterface;
using Robust.Shared.Prototypes;

namespace Content.Client.Silicons.Borgs;

/// <summary>
/// User interface used by borgs to select their type.
/// </summary>
/// <seealso cref="BorgSelectTypeMenu"/>
/// <seealso cref="BorgSwitchableTypeComponent"/>
/// <seealso cref="BorgSwitchableTypeUiKey"/>
[UsedImplicitly]
public sealed class BorgSelectTypeUserInterface : BoundUserInterface
{
    [ViewVariables]
    private BorgSelectTypeMenu? _menu;

    public BorgSelectTypeUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();

        _menu = this.CreateWindow<BorgSelectTypeMenu>();
        //Tutorial - Begin: filter chassis list when AvailableBorgTypes is set
        IReadOnlyList<ProtoId<BorgTypePrototype>>? available = null;
        if (EntMan.TryGetComponent(Owner, out BorgSwitchableTypeComponent? switchable))
            available = switchable.AvailableBorgTypes;
        _menu.Populate(available);
        //Tutorial - End
        _menu.ConfirmedBorgType += prototype => SendPredictedMessage(new BorgSelectTypeMessage(prototype));
    }
}

