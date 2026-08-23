using Content.Shared._Starlight.Antags.Vampires;
using Content.Shared._Starlight.Antags.Vampires.Components;
using Content.Shared._Starlight.Antags.Vampires.Prototypes;
using Content.Shared.Actions;
using Content.Shared.DoAfter;
using Content.Shared.Interaction;
using Content.Shared.Mobs.Components;
using Content.Shared.Popups;
using Robust.Server.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.Server._Starlight.Antags.Vampires;

/// <summary>
/// Minimal vampire gameplay: fangs, tutorial blood-drink shim, class picker.
/// </summary>
public sealed class VampireSystem : EntitySystem
{
    [Dependency] private readonly SharedActionsSystem _actions = default!;
    [Dependency] private readonly SharedDoAfterSystem _doAfter = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly UserInterfaceSystem _ui = default!;
    [Dependency] private readonly IComponentFactory _factory = default!;
    [Dependency] private readonly IPrototypeManager _protos = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<VampireComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<VampireComponent, VampireToggleFangsActionEvent>(OnToggleFangs);
        SubscribeLocalEvent<VampireComponent, VampireClassSelectActionEvent>(OnClassSelectAction);
        SubscribeLocalEvent<VampireComponent, AfterInteractEvent>(OnAfterInteract);
        SubscribeLocalEvent<VampireComponent, VampireDrinkBloodDoAfterEvent>(OnDrinkDoAfter);
        SubscribeLocalEvent<VampireComponent, VampireClassChosenBuiMsg>(OnClassChosen);
        SubscribeLocalEvent<VampireComponent, VampireClassClosedBuiMsg>(OnClassClosed);
    }

    /// <summary>Tutorial bootstrap: ensure vampire + lowered class threshold.</summary>
    public void MakeTutorialVampire(EntityUid mob, int classSelectThreshold = 40)
    {
        var vamp = EnsureComp<VampireComponent>(mob);
        vamp.ClassSelectThreshold = classSelectThreshold;
        vamp.BaseVampireActions = new List<EntProtoId> { "ActionVampireToggleFangs" };
        GrantBaseActions(mob, vamp);
        Dirty(mob, vamp);
    }

    private void OnMapInit(Entity<VampireComponent> ent, ref MapInitEvent args)
    {
        GrantBaseActions(ent, ent.Comp);
    }

    private void GrantBaseActions(EntityUid mob, VampireComponent vamp)
    {
        foreach (var action in vamp.BaseVampireActions)
            _actions.AddAction(mob, action);
    }

    private void OnToggleFangs(Entity<VampireComponent> ent, ref VampireToggleFangsActionEvent args)
    {
        if (args.Handled)
            return;

        ent.Comp.FangsExtended = !ent.Comp.FangsExtended;
        Dirty(ent);
        args.Handled = true;

        _popup.PopupEntity(
            Loc.GetString(ent.Comp.FangsExtended
                ? "vampire-fangs-extended"
                : "vampire-fangs-retracted"),
            ent,
            ent);
    }

    private void OnAfterInteract(Entity<VampireComponent> ent, ref AfterInteractEvent args)
    {
        if (args.Handled || !args.CanReach || args.Target == null)
            return;

        if (!ent.Comp.FangsExtended || ent.Comp.IsDrinking)
            return;

        if (!HasComp<MobStateComponent>(args.Target.Value))
            return;

        if (!_transform.InRange(ent.Owner, args.Target.Value, ent.Comp.BiteDistanceThreshold))
            return;

        ent.Comp.IsDrinking = true;
        _doAfter.TryStartDoAfter(new DoAfterArgs(EntityManager, ent, ent.Comp.DrinkDoAfter, new VampireDrinkBloodDoAfterEvent(), ent, target: args.Target, used: ent)
        {
            BreakOnMove = true,
            BreakOnDamage = true,
            NeedHand = false,
        });
        args.Handled = true;
    }

    private void OnDrinkDoAfter(Entity<VampireComponent> ent, ref VampireDrinkBloodDoAfterEvent args)
    {
        ent.Comp.IsDrinking = false;

        if (args.Cancelled || args.Target == null)
            return;

        // Tutorial blood shim — no Starlight Bloodstream dependency.
        ent.Comp.TotalBlood += ent.Comp.TutorialSipBlood;
        ent.Comp.DrunkBlood += ent.Comp.TutorialSipBlood;
        Dirty(ent);

        _popup.PopupEntity(Loc.GetString("vampire-drink-success", ("amount", ent.Comp.TutorialSipBlood)), ent, ent);

        TryGrantClassSelect(ent);
    }

    private void TryGrantClassSelect(Entity<VampireComponent> ent)
    {
        if (ent.Comp.ChosenClassId != null)
            return;

        if (ent.Comp.TotalBlood < ent.Comp.ClassSelectThreshold)
            return;

        _actions.AddAction(ent, ent.Comp.ClassSelectActionId);
        _popup.PopupEntity(Loc.GetString("vampire-class-select-unlocked"), ent, ent);
    }

    private void OnClassSelectAction(Entity<VampireComponent> ent, ref VampireClassSelectActionEvent args)
    {
        if (args.Handled || ent.Comp.ChosenClassId != null)
            return;

        _ui.TryToggleUi(ent.Owner, VampireClassUiKey.Key, ent.Owner);
        args.Handled = true;
    }

    private void OnClassChosen(Entity<VampireComponent> ent, ref VampireClassChosenBuiMsg args)
    {
        if (ent.Comp.ChosenClassId != null)
            return;

        if (!_protos.TryIndex<VampireClassPrototype>(args.Choice, out var classProto))
            return;

        ent.Comp.ChosenClassId = classProto.ID;
        Dirty(ent);

        // Add empty class marker component (abilities intentionally unwired).
        if (_factory.TryGetRegistration(classProto.ClassComponent, out var reg))
        {
            var marker = (Component)_factory.GetComponent(reg);
            EntityManager.AddComponent(ent.Owner, marker, overwrite: true);
        }

        _popup.PopupEntity(Loc.GetString("vampire-class-chosen", ("class", classProto.ID)), ent, ent);
        _ui.CloseUi(ent.Owner, VampireClassUiKey.Key);
    }

    private void OnClassClosed(Entity<VampireComponent> ent, ref VampireClassClosedBuiMsg args)
    {
        // no-op — player can reopen via action
    }
}
