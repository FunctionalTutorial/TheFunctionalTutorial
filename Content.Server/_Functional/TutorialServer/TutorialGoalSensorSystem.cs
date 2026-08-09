using Content.Server.Ame.Components;
using Content.Server.Cargo.Components;
using Content.Server.Cargo.Systems;
using Content.Server.DeviceLinking.Components;
using Content.Shared.Labels.EntitySystems;
using Content.Server.Fluids.EntitySystems;
using Content.Server.Nuke;
using Content.Server.Power.Components;
using Content.Server.Power.Generation.Teg;
using Content.Server.Research.Systems;
using Content.Server.Shuttles.Components;
using Content.Server.Shuttles.Events;
using Content.Server.Shuttles.Systems;
using Content.Server.Wires;
using Content.Shared._Functional.TutorialServer;
using Content.Shared.Nuke;
using Content.Shared.Objectives.Components;
using Content.Shared.Thief.Components;
using Content.Shared._Functional.TutorialServer.CyberMedSurgery;
using Content.Shared._Functional.TutorialServer.StarlightSurgery;
using Content.Shared.AlertLevel;
using Content.Shared.Ame.Components;
using Content.Shared.Access.Components;
using Content.Shared.Anomaly.Components;
using Content.Shared.Buckle.Components;
using Content.Shared.Chemistry.Components.SolutionManager;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Cuffs;
using Content.Shared.Cuffs.Components;
using Content.Shared.Damage;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Systems;
using Content.Shared.Doors.Components;
using Content.Shared.FixedPoint;
using Content.Shared.Fluids.Components;
using Content.Shared.Hands;
using Content.Shared.Hands.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Implants;
using Content.Shared.Implants.Components;
using Content.Shared.Interaction;
using Content.Shared.Interaction.Events;
using Content.Shared.Inventory;
using Content.Shared.Inventory.Events;
using Content.Shared.Item;
using Content.Shared.Lathe;
using Content.Shared.Movement.Systems;
using Content.Shared.Nutrition.Components;
using Content.Shared.Nutrition.EntitySystems;
using Content.Shared.PDA;
using Content.Shared.Power;
using Content.Shared.Research.Components;
using Content.Shared.Research.Prototypes;
using Content.Shared.Roles;
using Content.Shared.Shuttles.Components;
using Content.Shared.Actions;
using Content.Shared.Actions.Components;
using Content.Shared.Actions.Events;
using Content.Shared.UserInterface;
using Content.Shared.Changeling.Components;
using Content.Shared.Changeling.Systems;
using Content.Shared.Devour;
using Content.Shared.Emag.Systems;
using Content.Shared.Silicons.Borgs.Components;
using Content.Shared.Humanoid;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Revolutionary.Components;
using Content.Shared.Slippery;
using Content.Shared.Station.Components;
using Content.Shared.Storage;
using Content.Shared.Store;
using Content.Shared.Store.Components;
using Content.Shared.Stunnable;
using Content.Shared.Tag;
using Content.Shared.Wires;
using Content.Shared.Zombies;
using Robust.Server.GameObjects;
using Robust.Shared.Containers;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.Server._Functional.TutorialServer;

/// <summary>
/// Watches tutorial participants and advances sub-goals when sensor conditions match.
/// </summary>
public sealed partial class TutorialGoalSensorSystem : EntitySystem
{
    [Dependency] private readonly DamageableSystem _damageable = default!;
    [Dependency] private readonly InventorySystem _inventory = default!;
    [Dependency] private readonly PuddleSystem _puddle = default!;
    [Dependency] private readonly ResearchSystem _research = default!;
    [Dependency] private readonly SharedHandsSystem _hands = default!;
    [Dependency] private readonly SharedSolutionContainerSystem _solutions = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly SharedWiresSystem _wires = default!;
    [Dependency] private readonly WiresSystem _wiresServer = default!;
    [Dependency] private readonly SharedContainerSystem _containers = default!;
    [Dependency] private readonly TagSystem _tags = default!;
    [Dependency] private readonly EmagSystem _emag = default!;
    [Dependency] private readonly OpenableSystem _openable = default!;
    [Dependency] private readonly SharedActionsSystem _actions = default!;
    [Dependency] private readonly UserInterfaceSystem _ui = default!;
    [Dependency] private readonly DockingSystem _docking = default!;
    [Dependency] private TutorialServerRuleSystem _tutorial = default!;

    private readonly HashSet<EntityUid> _changelingDevoured = new();
    private readonly HashSet<EntityUid> _changelingStung = new();
    private readonly HashSet<EntityUid> _dragonDevoured = new();
    private readonly Dictionary<EntityUid, HashSet<EntProtoId>> _actionsUsed = new();

    /// <summary>
    /// Suppress nested <see cref="OnUndock"/> while force-clearing dual docks.
    /// </summary>
    private bool _undockCascade;

    private const float MarkerReachRange = 1.5f;
    private static readonly ProtoId<TagPrototype> TutorialRecyclerTag = "TutorialRecycler";
    private static readonly ProtoId<TagPrototype> TutorialDebrisLockerTag = "TutorialDebrisLocker";
    private static readonly ProtoId<TagPrototype> TutorialLatheTag = "TutorialLathe";

    /// <summary>
    /// Stability at or below this counts as "stabilized" for the science tutorial.
    /// </summary>
    private const float StabilizeStabilityThreshold = 0.4f;

    private static readonly ProtoId<TagPrototype> TutorialRollerBedTag = "TutorialRollerBed";
    private static readonly ProtoId<TagPrototype> TutorialBrigTimerTag = "TutorialBrigTimer";
    private static readonly ProtoId<CurrencyPrototype> TelecrystalCurrency = "Telecrystal";
    private static readonly ProtoId<AlertLevelPrototype> DefaultAlertLevel = "Blue";

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<TutorialParticipantComponent, DidEquipHandEvent>(OnDidEquipHand);
        SubscribeLocalEvent<TutorialParticipantComponent, DidUnequipHandEvent>(OnDidUnequipHand);
        SubscribeLocalEvent<TutorialParticipantComponent, DidEquipEvent>(OnDidEquip);
        SubscribeLocalEvent<TutorialParticipantComponent, UserInteractUsingEvent>(OnInteractUsing);
        SubscribeLocalEvent<TutorialParticipantComponent, UserInteractHandEvent>(OnInteractHand);
        SubscribeLocalEvent<TutorialParticipantComponent, UserActivateInWorldEvent>(OnActivateInWorld);
        // ActivatableUI (chem dispenser / ChemMaster) handles ActivateInWorld; complete after open.
        SubscribeLocalEvent<ActivatableUIComponent, AfterActivatableUIOpenEvent>(OnAfterActivatableUIOpen);
        // After OpenableSystem: opening a closed drink sets Handled, and we must not treat that
        // press as the UseInHand goal (deferred guide would pop open on bottle-open).
        SubscribeLocalEvent<TutorialSensorTargetComponent, UseInHandEvent>(
            OnUseInHand,
            after: [typeof(OpenableSystem)],
            before: [typeof(IngestionSystem)]);
        SubscribeLocalEvent<ItemComponent, DroppedEvent>(OnItemDropped);
        // Do not subscribe PilotComponent.ComponentStartup — ShuttleConsoleSystem already owns it.
        SubscribeLocalEvent<DockEvent>(OnDock);
        SubscribeLocalEvent<UndockEvent>(OnUndock);
        SubscribeLocalEvent<AnomalyShutdownEvent>(OnAnomalyShutdown);
        SubscribeLocalEvent<EntitySoldEvent>(OnEntitySold);
        SubscribeLocalEvent<FulfillCargoOrderEvent>(OnFulfillCargoOrder);
        SubscribeLocalEvent<AlertLevelChangedEvent>(OnAlertLevelChanged);
        SubscribeLocalEvent<LatheComponent, LatheStartPrintingEvent>(OnLatheStartPrinting);
        SubscribeLocalEvent<TutorialPracticeMobComponent, TargetHandcuffedEvent>(OnPracticeMobCuffed);
        SubscribeLocalEvent<TutorialPracticeMobComponent, DamageChangedEvent>(OnPracticeMobDamaged);
        SubscribeLocalEvent<TutorialPracticeMobComponent, BuckledEvent>(OnPracticeMobBuckled);
        SubscribeLocalEvent<TutorialPracticeMobComponent, SlipEvent>(OnPracticeMobSlipped);
        SubscribeLocalEvent<TutorialPracticeMobComponent, InteractUsingEvent>(OnPracticeMobSlipInteract);
        SubscribeLocalEvent<TutorialPracticeMobComponent, StunnedEvent>(OnPracticeMobStunned);
        SubscribeLocalEvent<TutorialPracticeMobComponent, KnockedDownEvent>(OnPracticeMobKnockedDown);
        SubscribeLocalEvent<TutorialPracticeMobComponent, MobStateChangedEvent>(OnPracticeMobStateChanged);
        SubscribeLocalEvent<TutorialParticipantComponent, ChangelingDevouredEvent>(OnChangelingDevoured);
        SubscribeLocalEvent<TutorialParticipantComponent, ChangelingStingDnaEvent>(OnChangelingStung);
        SubscribeLocalEvent<ActionComponent, ActionPerformedEvent>(OnActionPerformed);
        SubscribeLocalEvent<TutorialParticipantComponent, DevourDoAfterEvent>(
            OnDragonDevoured,
            after: [typeof(DevourSystem)]);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<TutorialParticipantComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var part, out var xform))
        {
            if (!_tutorial.TryGetCurrentSubGoal(uid, part, out var sub))
                continue;

            switch (sub.Complete)
            {
                case TutorialStepComplete.HoldItem:
                case TutorialStepComplete.HoldTag:
                case TutorialStepComplete.ObtainItem:
                case TutorialStepComplete.StowItem:
                    TryCompleteFromPossession(uid, sub);
                    break;
                case TutorialStepComplete.ReachMarker:
                    TryCompleteReachMarker(uid, xform, sub);
                    break;
                case TutorialStepComplete.PilotShuttle:
                    if (HasComp<PilotComponent>(uid))
                        _tutorial.AdvanceSubGoal(uid);
                    break;
                case TutorialStepComplete.UndockShuttle:
                    // Covers early undock (before this goal was current) once dual docks are cleared.
                    TryCompleteUndockShuttle(uid, xform, sub);
                    break;
                case TutorialStepComplete.ShuttleThrottle:
                    if (TryComp<PilotComponent>(uid, out var pilot) && pilot.HeldButtons != ShuttleButtons.None)
                        _tutorial.AdvanceSubGoal(uid);
                    break;
                case TutorialStepComplete.SpawnAnomaly:
                    if (HasSpawnedTutorialAnomaly(xform.MapUid))
                        _tutorial.AdvanceSubGoal(uid);
                    break;
                case TutorialStepComplete.ScanAnomaly:
                    if (TryGetScannedAnomaly(uid, out _))
                        _tutorial.AdvanceSubGoal(uid);
                    break;
                case TutorialStepComplete.StabilizeAnomaly:
                    if (TryGetScannedAnomaly(uid, out var scanned) &&
                        TryComp<AnomalyComponent>(scanned, out var anomaly) &&
                        anomaly.Stability <= StabilizeStabilityThreshold)
                        _tutorial.AdvanceSubGoal(uid);
                    break;
                case TutorialStepComplete.SolutionContains:
                    if (TryCompleteSolutionContains(uid, sub))
                        _tutorial.AdvanceSubGoal(uid);
                    break;
                case TutorialStepComplete.PuddleCleared:
                    if (IsPuddleCleared(xform.MapUid, sub.Marker))
                        _tutorial.AdvanceSubGoal(uid);
                    break;
                case TutorialStepComplete.PracticeMobDamageBelow:
                    if (IsPracticeMobDamageBelow(xform.MapUid, sub.MaxDamage))
                        _tutorial.AdvanceSubGoal(uid);
                    break;
                case TutorialStepComplete.AmeInjecting:
                    if (IsAmeInjecting(xform.MapUid))
                        _tutorial.AdvanceSubGoal(uid);
                    break;
                case TutorialStepComplete.MapHasEntity:
                    if (IsMapHasEntity(xform.MapUid, sub))
                        _tutorial.AdvanceSubGoal(uid);
                    break;
                case TutorialStepComplete.WiresPanelOpen:
                    if (IsWiresPanelOpen(xform.MapUid, sub.Tag))
                        _tutorial.AdvanceSubGoal(uid);
                    break;
                case TutorialStepComplete.WearItem:
                    if (sub.Entity != null && IsWearingProto(uid, sub.Entity.Value))
                        _tutorial.AdvanceSubGoal(uid);
                    break;
                case TutorialStepComplete.TargetPowerDisabled:
                    if (IsTargetPowerDisabled(xform.MapUid, sub.Tag))
                        _tutorial.AdvanceSubGoal(uid);
                    break;
                case TutorialStepComplete.TargetDoorOpen:
                    if (IsTargetDoorOpen(xform.MapUid, sub.Tag))
                        _tutorial.AdvanceSubGoal(uid);
                    break;
                case TutorialStepComplete.PowerWiresCut:
                    if (IsPowerWiresCut(xform.MapUid, sub.Tag))
                        _tutorial.AdvanceSubGoal(uid);
                    break;
                case TutorialStepComplete.PracticeMobCreamPied:
                    if (IsPracticeMobCreamPied(xform.MapUid))
                        _tutorial.AdvanceSubGoal(uid);
                    break;
                case TutorialStepComplete.PracticeMobBuckled:
                    if (IsPracticeMobBuckled(xform.MapUid))
                        _tutorial.AdvanceSubGoal(uid);
                    break;
                case TutorialStepComplete.StarlightSurgeryUiOpened:
                    if (IsStarlightSurgeryUiOpen(uid, xform.MapUid))
                        _tutorial.AdvanceSubGoal(uid);
                    break;
                case TutorialStepComplete.StarlightSurgeryEyeImplanted:
                    if (IsStarlightSurgeryEyeImplanted(xform.MapUid))
                        _tutorial.AdvanceSubGoal(uid);
                    break;
                case TutorialStepComplete.CyberMedSurgeryUiOpened:
                    if (IsCyberMedSurgeryUiOpen(uid, xform.MapUid))
                        _tutorial.AdvanceSubGoal(uid);
                    break;
                case TutorialStepComplete.CyberMedSurgeryComplete:
                    if (IsCyberMedSurgeryComplete(xform.MapUid))
                        _tutorial.AdvanceSubGoal(uid);
                    break;
                case TutorialStepComplete.IdCardHasJob:
                    if (IsIdCardHasJob(xform.MapUid, sub.Job))
                        _tutorial.AdvanceSubGoal(uid);
                    break;
                case TutorialStepComplete.ContainerHasEntityCount:
                    if (IsContainerHasEntityCount(xform.MapUid, sub))
                        _tutorial.AdvanceSubGoal(uid);
                    break;
                case TutorialStepComplete.TegProducingPower:
                    if (IsTegProducingPower(xform.MapUid))
                        _tutorial.AdvanceSubGoal(uid);
                    break;
                case TutorialStepComplete.ResearchUnlocked:
                    if (IsResearchUnlocked(xform.MapUid, sub.Technology))
                        _tutorial.AdvanceSubGoal(uid);
                    break;
                case TutorialStepComplete.NukeArmed:
                    if (IsNukeArmed(xform.MapUid))
                        _tutorial.AdvanceSubGoal(uid);
                    break;
                case TutorialStepComplete.WarDeclared:
                    if (HasComp<TutorialWarDeclaredComponent>(uid))
                        _tutorial.AdvanceSubGoal(uid);
                    break;
                case TutorialStepComplete.PlayerIsZombie:
                    if (HasComp<ZombieComponent>(uid))
                        _tutorial.AdvanceSubGoal(uid);
                    break;
                case TutorialStepComplete.PracticeMobInfected:
                    if (CountInfectedPracticeMobs(xform.MapUid) >= Math.Max(1, sub.MinCount))
                        _tutorial.AdvanceSubGoal(uid);
                    break;
                case TutorialStepComplete.PracticeMobConverted:
                    if (CountConvertedPracticeMobs(xform.MapUid) >= Math.Max(1, sub.MinCount))
                        _tutorial.AdvanceSubGoal(uid);
                    break;
                case TutorialStepComplete.PlayerWiresPanelOpen:
                    if (TryComp<WiresPanelComponent>(uid, out var panel) && panel.Open)
                        _tutorial.AdvanceSubGoal(uid);
                    break;
                case TutorialStepComplete.SiliconSubverted:
                    if (_emag.CheckFlag(uid, EmagType.Interaction))
                        _tutorial.AdvanceSubGoal(uid);
                    break;
                case TutorialStepComplete.PracticeMobDead:
                    if (HasDeadPracticeMob(xform.MapUid))
                        _tutorial.AdvanceSubGoal(uid);
                    break;
                case TutorialStepComplete.PracticeMobRevived:
                    if (HasRevivedPracticeCorpse(xform.MapUid))
                        _tutorial.AdvanceSubGoal(uid);
                    break;
                case TutorialStepComplete.ChangelingDevoured:
                    if (_changelingDevoured.Contains(uid))
                        _tutorial.AdvanceSubGoal(uid);
                    break;
                case TutorialStepComplete.ChangelingStung:
                    if (_changelingStung.Contains(uid))
                        _tutorial.AdvanceSubGoal(uid);
                    break;
                case TutorialStepComplete.HasAction:
                    if (PlayerHasAction(uid, sub.Entity))
                        _tutorial.AdvanceSubGoal(uid);
                    break;
                case TutorialStepComplete.VampireBloodAbove:
                case TutorialStepComplete.VampireClassChosen:
                case TutorialStepComplete.VampireFangsExtended:
                    // Handled by TutorialVampireSensorSystem once Vampire components exist.
                    break;
                case TutorialStepComplete.BrigTimerStarted:
                    if (IsBrigTimerStarted(xform.MapUid))
                        _tutorial.AdvanceSubGoal(uid);
                    break;
                case TutorialStepComplete.StorePurchased:
                    if (HasStorePurchase(uid, sub))
                        _tutorial.AdvanceSubGoal(uid);
                    break;
                case TutorialStepComplete.PracticeMobStunned:
                    if (IsPracticeMobStunned(xform.MapUid))
                        _tutorial.AdvanceSubGoal(uid);
                    break;
                case TutorialStepComplete.ThiefBeaconLinked:
                    if (IsThiefBeaconLinked(xform.MapUid))
                        _tutorial.AdvanceSubGoal(uid);
                    break;
                case TutorialStepComplete.ActionUsed:
                    if (sub.Entity != null &&
                        _actionsUsed.TryGetValue(uid, out var used) &&
                        used.Contains(sub.Entity.Value))
                        _tutorial.AdvanceSubGoal(uid);
                    break;
                case TutorialStepComplete.DragonDevoured:
                    if (_dragonDevoured.Contains(uid))
                        _tutorial.AdvanceSubGoal(uid);
                    break;
                case TutorialStepComplete.BorgTypeSelected:
                    if (IsBorgTypeSelected(uid, sub))
                        _tutorial.AdvanceSubGoal(uid);
                    break;
                case TutorialStepComplete.BorgModuleSelected:
                    if (IsBorgModuleSelected(uid, sub))
                        _tutorial.AdvanceSubGoal(uid);
                    break;
            }
        }
    }

    private bool IsBorgTypeSelected(EntityUid uid, TutorialSubGoalData sub)
    {
        if (!TryComp<BorgSwitchableTypeComponent>(uid, out var switchable) ||
            switchable.SelectedBorgType == null)
            return false;

        if (string.IsNullOrEmpty(sub.Marker))
            return true;

        return switchable.SelectedBorgType.Value.Id == sub.Marker;
    }

    private bool IsBorgModuleSelected(EntityUid uid, TutorialSubGoalData sub)
    {
        if (sub.Entity == null)
            return false;

        if (!TryComp<BorgChassisComponent>(uid, out var chassis) ||
            chassis.SelectedModule is not { } module ||
            Deleted(module))
            return false;

        return MetaData(module).EntityPrototype?.ID == sub.Entity.Value.Id;
    }

    private void OnActionPerformed(Entity<ActionComponent> ent, ref ActionPerformedEvent args)
    {
        if (!TryComp<TutorialParticipantComponent>(args.Performer, out var part))
            return;

        var protoId = MetaData(ent).EntityPrototype?.ID;
        if (protoId == null)
            return;

        var actionProto = new EntProtoId(protoId);
        if (!_actionsUsed.TryGetValue(args.Performer, out var used))
        {
            used = new HashSet<EntProtoId>();
            _actionsUsed[args.Performer] = used;
        }

        used.Add(actionProto);

        if (!_tutorial.TryGetCurrentSubGoal(args.Performer, part, out var sub) ||
            sub.Complete != TutorialStepComplete.ActionUsed ||
            sub.Entity != actionProto)
            return;

        _tutorial.AdvanceSubGoal(args.Performer);
    }

    private void OnDragonDevoured(Entity<TutorialParticipantComponent> ent, ref DevourDoAfterEvent args)
    {
        if (args.Cancelled || args.Args.Target is not { } target)
            return;

        // Ichor heal only applies to humanoid preference targets.
        if (!HasComp<HumanoidProfileComponent>(target))
            return;

        _dragonDevoured.Add(ent.Owner);

        if (!_tutorial.TryGetCurrentSubGoal(ent.Owner, ent.Comp, out var sub) ||
            sub.Complete != TutorialStepComplete.DragonDevoured)
            return;

        _tutorial.AdvanceSubGoal(ent.Owner);
    }

    private void OnChangelingDevoured(Entity<TutorialParticipantComponent> ent, ref ChangelingDevouredEvent args)
    {
        if (args.Changeling != ent.Owner)
            return;

        _changelingDevoured.Add(ent.Owner);

        if (!_tutorial.TryGetCurrentSubGoal(ent.Owner, ent.Comp, out var sub) ||
            sub.Complete != TutorialStepComplete.ChangelingDevoured)
            return;

        _tutorial.AdvanceSubGoal(ent.Owner);
    }

    private void OnChangelingStung(Entity<TutorialParticipantComponent> ent, ref ChangelingStingDnaEvent args)
    {
        if (args.Handled)
            _changelingStung.Add(ent.Owner);

        // Sting events set Handled after success in ChangelingAbilitySystem — also accept Performer match.
        _changelingStung.Add(ent.Owner);

        if (!_tutorial.TryGetCurrentSubGoal(ent.Owner, ent.Comp, out var sub) ||
            sub.Complete != TutorialStepComplete.ChangelingStung)
            return;

        _tutorial.AdvanceSubGoal(ent.Owner);
    }

    private void OnEntitySold(ref EntitySoldEvent args)
    {
        if (args.Sold.Count == 0)
            return;

        EntityUid? mapUid = null;
        foreach (var sold in args.Sold)
        {
            if (!Exists(sold))
                continue;
            mapUid = Transform(sold).MapUid;
            break;
        }

        // Entities may already be queued for delete; fall back to station grids.
        if (mapUid == null && Exists(args.Station))
        {
            var query = EntityQueryEnumerator<StationMemberComponent, TransformComponent>();
            while (query.MoveNext(out _, out var member, out var xform))
            {
                if (member.Station != args.Station)
                    continue;
                mapUid = xform.MapUid;
                break;
            }
        }

        if (mapUid == null)
            return;

        var partQuery = EntityQueryEnumerator<TutorialParticipantComponent, TransformComponent>();
        while (partQuery.MoveNext(out var uid, out var part, out var xform))
        {
            if (xform.MapUid != mapUid)
                continue;

            if (!_tutorial.TryGetCurrentSubGoal(uid, part, out var sub))
                continue;

            if (sub.Complete == TutorialStepComplete.CargoSold)
            {
                var needed = sub.MinCount > 0 ? sub.MinCount : 1;
                if (args.Sold.Count < needed)
                    continue;

                _tutorial.AdvanceSubGoal(uid);
                continue;
            }

            if (sub.Complete != TutorialStepComplete.CargoBountyFulfilled)
                continue;

            if (!AnySoldBountyFulfilled(args.Sold))
                continue;

            _tutorial.AdvanceSubGoal(uid);
        }
    }

    private void OnFulfillCargoOrder(ref FulfillCargoOrderEvent args)
    {
        // Approve path raises this before order.Approved is set; order is then removed from the DB.
        EntityUid? mapUid = null;
        if (TryComp<TransformComponent>(args.OrderConsole.Owner, out var consoleXform))
            mapUid = consoleXform.MapUid;

        if (mapUid == null && TryComp<TransformComponent>(args.Station.Owner, out var stationXform))
            mapUid = stationXform.MapUid;

        if (mapUid == null)
            return;

        var query = EntityQueryEnumerator<TutorialParticipantComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var part, out var xform))
        {
            if (xform.MapUid != mapUid)
                continue;

            if (!_tutorial.TryGetCurrentSubGoal(uid, part, out var sub))
                continue;

            if (sub.Complete != TutorialStepComplete.CargoOrderApproved)
                continue;

            _tutorial.AdvanceSubGoal(uid);
        }
    }

    private void OnAlertLevelChanged(ref AlertLevelChangedEvent args)
    {
        EntityUid? mapUid = null;
        var memberQuery = EntityQueryEnumerator<StationMemberComponent, TransformComponent>();
        while (memberQuery.MoveNext(out _, out var member, out var xform))
        {
            if (member.Station != args.Station)
                continue;
            mapUid = xform.MapUid;
            break;
        }

        if (mapUid == null)
            return;

        var wanted = args.AlertLevel;
        var query = EntityQueryEnumerator<TutorialParticipantComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var part, out var xform))
        {
            if (xform.MapUid != mapUid)
                continue;

            if (!_tutorial.TryGetCurrentSubGoal(uid, part, out var sub))
                continue;

            if (sub.Complete != TutorialStepComplete.AlertLevelChanged)
                continue;

            var expected = sub.AlertLevel ?? DefaultAlertLevel;
            if (wanted != expected)
                continue;

            _tutorial.AdvanceSubGoal(uid);
        }
    }

    private void OnLatheStartPrinting(Entity<LatheComponent> ent, ref LatheStartPrintingEvent args)
    {
        if (!_tags.HasTag(ent.Owner, TutorialLatheTag))
            return;

        var mapUid = Transform(ent).MapUid;
        var query = EntityQueryEnumerator<TutorialParticipantComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var part, out var xform))
        {
            if (xform.MapUid != mapUid)
                continue;

            if (!_tutorial.TryGetCurrentSubGoal(uid, part, out var sub))
                continue;

            if (sub.Complete != TutorialStepComplete.LathePrinted)
                continue;

            if (sub.Entity != null && args.Recipe.Result != sub.Entity)
                continue;

            _tutorial.AdvanceSubGoal(uid);
        }
    }

    private void OnPracticeMobCuffed(Entity<TutorialPracticeMobComponent> ent, ref TargetHandcuffedEvent args)
    {
        var mapUid = Transform(ent).MapUid;
        var query = EntityQueryEnumerator<TutorialParticipantComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var part, out var xform))
        {
            if (xform.MapUid != mapUid)
                continue;

            if (!_tutorial.TryGetCurrentSubGoal(uid, part, out var sub))
                continue;

            if (sub.Complete != TutorialStepComplete.PracticeMobCuffed)
                continue;

            _tutorial.AdvanceSubGoal(uid);
        }
    }

    private void OnPracticeMobDamaged(Entity<TutorialPracticeMobComponent> ent, ref DamageChangedEvent args)
    {
        var mapUid = Transform(ent).MapUid;
        var query = EntityQueryEnumerator<TutorialParticipantComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var part, out var xform))
        {
            if (xform.MapUid != mapUid)
                continue;

            if (!_tutorial.TryGetCurrentSubGoal(uid, part, out var sub))
                continue;

            if (sub.Complete != TutorialStepComplete.PracticeMobDamageBelow)
                continue;

            if (!IsPracticeMobDamageBelow(mapUid, sub.MaxDamage))
                continue;

            _tutorial.AdvanceSubGoal(uid);
        }
    }

    private void OnPracticeMobBuckled(Entity<TutorialPracticeMobComponent> ent, ref BuckledEvent args)
    {
        if (!_tags.HasTag(args.Strap, TutorialRollerBedTag))
            return;

        var mapUid = Transform(ent).MapUid;
        var query = EntityQueryEnumerator<TutorialParticipantComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var part, out var xform))
        {
            if (xform.MapUid != mapUid)
                continue;

            if (!_tutorial.TryGetCurrentSubGoal(uid, part, out var sub))
                continue;

            if (sub.Complete != TutorialStepComplete.PracticeMobBuckled)
                continue;

            _tutorial.AdvanceSubGoal(uid);
        }
    }

    private void OnPracticeMobSlipped(Entity<TutorialPracticeMobComponent> ent, ref SlipEvent args)
    {
        AdvancePracticeMobSlipped(Transform(ent).MapUid);
    }

    /// <summary>
    /// Practice mobs do not walk onto soap; allow soap/peel InteractUsing to count as a slip bit.
    /// </summary>
    private void OnPracticeMobSlipInteract(Entity<TutorialPracticeMobComponent> ent, ref InteractUsingEvent args)
    {
        if (args.Handled)
            return;

        var used = MetaData(args.Used).EntityPrototype?.ID;
        if (used is ("Soap" or "SoapHomemade" or "SoapNT" or "SoapDeluxe" or "SoapSyndie" or "TrashBananaPeel"))
        {
            AdvancePracticeMobSlipped(Transform(ent).MapUid, args.User);
            args.Handled = true;
            return;
        }

        // Practice mobs may not take real stun hits; stun tools count via InteractUsing.
        if (IsStunToolProto(used))
        {
            AdvancePracticeMobStunned(Transform(ent).MapUid, args.User);
            args.Handled = true;
        }
    }

    private void OnPracticeMobStunned(Entity<TutorialPracticeMobComponent> ent, ref StunnedEvent args)
    {
        AdvancePracticeMobStunned(Transform(ent).MapUid);
    }

    private void OnPracticeMobKnockedDown(Entity<TutorialPracticeMobComponent> ent, ref KnockedDownEvent args)
    {
        AdvancePracticeMobStunned(Transform(ent).MapUid);
    }

    private void OnPracticeMobStateChanged(EntityUid uid, TutorialPracticeMobComponent component, MobStateChangedEvent args)
    {
        if (args.OldMobState != MobState.Dead)
            return;

        if (args.NewMobState is not (MobState.Critical or MobState.Alive))
            return;

        var mapUid = Transform(uid).MapUid;
        var query = EntityQueryEnumerator<TutorialParticipantComponent, TransformComponent>();
        while (query.MoveNext(out var participant, out var part, out var xform))
        {
            if (xform.MapUid != mapUid)
                continue;

            if (!_tutorial.TryGetCurrentSubGoal(participant, part, out var sub))
                continue;

            if (sub.Complete != TutorialStepComplete.PracticeMobRevived)
                continue;

            _tutorial.AdvanceSubGoal(participant);
        }
    }

    private static bool IsStunToolProto(string? protoId)
    {
        return protoId is "Stunbaton" or "WeaponDisabler" or "Flash" or "FlashMilitary";
    }

    private void AdvancePracticeMobStunned(EntityUid? mapUid, EntityUid? user = null)
    {
        var query = EntityQueryEnumerator<TutorialParticipantComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var part, out var xform))
        {
            if (xform.MapUid != mapUid)
                continue;

            if (user != null && uid != user)
                continue;

            if (!_tutorial.TryGetCurrentSubGoal(uid, part, out var sub))
                continue;

            if (sub.Complete != TutorialStepComplete.PracticeMobStunned)
                continue;

            _tutorial.AdvanceSubGoal(uid);
        }
    }

    private void AdvancePracticeMobSlipped(EntityUid? mapUid, EntityUid? user = null)
    {
        var query = EntityQueryEnumerator<TutorialParticipantComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var part, out var xform))
        {
            if (xform.MapUid != mapUid)
                continue;

            if (user != null && uid != user)
                continue;

            if (!_tutorial.TryGetCurrentSubGoal(uid, part, out var sub))
                continue;

            if (sub.Complete != TutorialStepComplete.PracticeMobSlipped)
                continue;

            _tutorial.AdvanceSubGoal(uid);
        }
    }

    private void OnAnomalyShutdown(ref AnomalyShutdownEvent args)
    {
        if (args.Supercritical)
            return;

        var mapUid = Transform(args.Anomaly).MapUid;
        var query = EntityQueryEnumerator<TutorialParticipantComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var part, out var xform))
        {
            if (!_tutorial.TryGetCurrentSubGoal(uid, part, out var sub))
                continue;

            if (sub.Complete != TutorialStepComplete.RemoveAnomaly)
                continue;

            if (xform.MapUid != mapUid)
                continue;

            _tutorial.AdvanceSubGoal(uid);
        }
    }

    private bool HasSpawnedTutorialAnomaly(EntityUid? mapUid)
    {
        if (mapUid == null)
            return false;

        var spawners = EntityQueryEnumerator<TutorialAnomalySpawnerComponent, TransformComponent>();
        while (spawners.MoveNext(out _, out var spawner, out var xform))
        {
            if (xform.MapUid != mapUid)
                continue;

            if (spawner.Spawned &&
                spawner.SpawnedAnomaly is { } anomaly &&
                Exists(anomaly) &&
                !TerminatingOrDeleted(anomaly))
                return true;
        }

        return false;
    }

    private bool TryGetScannedAnomaly(EntityUid mob, out EntityUid anomaly)
    {
        anomaly = default;

        foreach (var hand in _hands.EnumerateHeld(mob))
        {
            if (TryComp<AnomalyScannerComponent>(hand, out var scanner) &&
                scanner.ScannedAnomaly is { } heldScan &&
                Exists(heldScan))
            {
                anomaly = heldScan;
                return true;
            }
        }

        if (!_inventory.TryGetContainerSlotEnumerator(mob, out var slots))
            return false;

        while (slots.MoveNext(out var slot))
        {
            if (slot.ContainedEntity is not { } item)
                continue;

            if (TryComp<AnomalyScannerComponent>(item, out var scanner) &&
                scanner.ScannedAnomaly is { } invScan &&
                Exists(invScan))
            {
                anomaly = invScan;
                return true;
            }
        }

        return false;
    }

    private void OnDock(DockEvent ev)
    {
        AdvanceDockGoal(ev.GridAUid, ev.GridBUid, TutorialStepComplete.DockShuttle);
    }

    private void OnUndock(UndockEvent ev)
    {
        // Nested UndockEvents from UndockAllBetween must not re-enter or double-advance.
        if (_undockCascade)
            return;

        // Tutorial arenas often dual-dock. Undocking one port leaves a weld that pins the
        // shuttle so thrusters cannot move it — always clear the remaining pair docks.
        if (IsTutorialDockPair(ev.GridAUid, ev.GridBUid))
        {
            _undockCascade = true;
            try
            {
                UndockAllBetween(ev.GridAUid, ev.GridBUid);
            }
            finally
            {
                _undockCascade = false;
            }
        }

        AdvanceDockGoal(ev.GridAUid, ev.GridBUid, TutorialStepComplete.UndockShuttle);
    }

    private bool IsTutorialDockPair(EntityUid gridA, EntityUid gridB)
    {
        return HasComp<TutorialDockStationComponent>(gridA) ||
               HasComp<TutorialDockStationComponent>(gridB);
    }

    private void UndockAllBetween(EntityUid gridA, EntityUid gridB)
    {
        foreach (var dock in _docking.GetDocks(gridA))
        {
            if (dock.Comp.DockedWith is not { } other)
                continue;

            if (Transform(other).GridUid != gridB)
                continue;

            _docking.Undock(dock);
        }
    }

    private void TryCompleteUndockShuttle(EntityUid uid, TransformComponent xform, TutorialSubGoalData sub)
    {
        if (xform.GridUid is not { } shuttle)
            return;

        if (IsDockedToTutorialStation(shuttle, sub.Marker))
            return;

        _tutorial.AdvanceSubGoal(uid);
    }

    /// <summary>
    /// True when the shuttle still has a dock weld to a tutorial station matching <paramref name="marker"/>
    /// (or any tutorial dock station when marker is null/empty).
    /// </summary>
    private bool IsDockedToTutorialStation(EntityUid shuttle, string? marker)
    {
        foreach (var dock in _docking.GetDocks(shuttle))
        {
            if (dock.Comp.DockedWith is not { } other)
                continue;

            if (Transform(other).GridUid is not { } otherGrid)
                continue;

            if (!TryComp<TutorialDockStationComponent>(otherGrid, out var station))
                continue;

            if (!string.IsNullOrEmpty(marker) &&
                !string.Equals(station.StationId, marker, StringComparison.Ordinal))
                continue;

            return true;
        }

        return false;
    }

    private void AdvanceDockGoal(EntityUid gridA, EntityUid gridB, TutorialStepComplete complete)
    {
        var query = EntityQueryEnumerator<TutorialParticipantComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var part, out var xform))
        {
            if (!_tutorial.TryGetCurrentSubGoal(uid, part, out var sub))
                continue;

            if (sub.Complete != complete)
                continue;

            var grid = xform.GridUid;
            if (grid == null || (grid != gridA && grid != gridB))
                continue;

            // Optional station filter via Marker (e.g. cargo-bay / ats).
            if (!string.IsNullOrEmpty(sub.Marker))
            {
                var other = grid == gridA ? gridB : gridA;
                if (!TryComp<TutorialDockStationComponent>(other, out var station) ||
                    !string.Equals(station.StationId, sub.Marker, StringComparison.Ordinal))
                    continue;
            }

            // For undock goals, require the shuttle to be fully free of the station.
            if (complete == TutorialStepComplete.UndockShuttle &&
                GridsStillDocked(gridA, gridB))
                continue;

            _tutorial.AdvanceSubGoal(uid);
        }
    }

    private bool GridsStillDocked(EntityUid gridA, EntityUid gridB)
    {
        foreach (var dock in _docking.GetDocks(gridA))
        {
            if (dock.Comp.DockedWith is not { } other)
                continue;

            if (Transform(other).GridUid == gridB)
                return true;
        }

        return false;
    }

    private void OnDidEquip(Entity<TutorialParticipantComponent> ent, ref DidEquipEvent args)
    {
        if (!_tutorial.TryGetCurrentSubGoal(ent, ent.Comp, out var sub))
            return;

        if (sub.Complete != TutorialStepComplete.WearItem || sub.Entity == null)
            return;

        if (!IsWearingProto(ent, sub.Entity.Value))
            return;

        _tutorial.AdvanceSubGoal(ent);
    }

    private void OnDidEquipHand(Entity<TutorialParticipantComponent> ent, ref DidEquipHandEvent args)
    {
        if (!_tutorial.TryGetCurrentSubGoal(ent, ent.Comp, out var sub))
            return;

        switch (sub.Complete)
        {
            case TutorialStepComplete.HoldItem:
            case TutorialStepComplete.HoldTag:
            case TutorialStepComplete.ObtainItem:
            case TutorialStepComplete.StowItem:
                TryCompleteFromPossession(ent, sub);
                break;
            case TutorialStepComplete.SolutionContains:
                if (TryCompleteSolutionContains(ent, sub))
                    _tutorial.AdvanceSubGoal(ent);
                break;
        }
    }

    private void OnDidUnequipHand(Entity<TutorialParticipantComponent> ent, ref DidUnequipHandEvent args)
    {
        if (!_tutorial.TryGetCurrentSubGoal(ent, ent.Comp, out var sub))
            return;

        if (sub.Complete is TutorialStepComplete.StowItem or TutorialStepComplete.ObtainItem)
            TryCompleteFromPossession(ent, sub);
    }

    private void OnInteractUsing(Entity<TutorialParticipantComponent> ent, ref UserInteractUsingEvent args)
    {
        if (args.Handled)
            return;

        if (!_tutorial.TryGetCurrentSubGoal(ent, ent.Comp, out var sub))
            return;

        if (sub.Complete == TutorialStepComplete.InteractTag)
        {
            if (string.IsNullOrEmpty(sub.Tag) || !_tags.HasTag(args.Used, (ProtoId<TagPrototype>) sub.Tag))
                return;

            args.Handled = true;
            _tutorial.AdvanceSubGoal(ent);
            return;
        }

        if (sub.Complete == TutorialStepComplete.InteractTargetTag)
        {
            if (string.IsNullOrEmpty(sub.Tag) || !_tags.HasTag(args.Target, (ProtoId<TagPrototype>) sub.Tag))
                return;

            // Let ActivatableUI / insert logic run; Bound-UI machines complete on open instead.
            if (HasComp<ActivatableUIComponent>(args.Target))
                return;

            args.Handled = true;
            _tutorial.AdvanceSubGoal(ent);
            return;
        }

        if (sub.Complete == TutorialStepComplete.InteractTargetHolding)
        {
            if (string.IsNullOrEmpty(sub.Tag) ||
                sub.Entity == null ||
                !_tags.HasTag(args.Target, (ProtoId<TagPrototype>) sub.Tag) ||
                !IsProto(args.Used, sub.Entity.Value))
                return;

            if (HasComp<ActivatableUIComponent>(args.Target))
                return;

            args.Handled = true;
            _tutorial.AdvanceSubGoal(ent);
            return;
        }

        // Feed scrap into the practice recycler (avoid dual MaterialReclaimer InteractUsing subscriptions).
        if (sub.Complete == TutorialStepComplete.RecyclerProcessed &&
            _tags.HasTag(args.Target, TutorialRecyclerTag))
        {
            args.Handled = true;
            _tutorial.AdvanceSubGoal(ent);
        }
    }

    private void OnInteractHand(Entity<TutorialParticipantComponent> ent, ref UserInteractHandEvent args)
    {
        if (args.Handled)
            return;

        if (!_tutorial.TryGetCurrentSubGoal(ent, ent.Comp, out var sub))
            return;

        if (sub.Complete != TutorialStepComplete.InteractTargetTag)
            return;

        if (string.IsNullOrEmpty(sub.Tag) || !_tags.HasTag(args.Target, (ProtoId<TagPrototype>) sub.Tag))
            return;

        // Do not handle — ActivatableUI opens on the subsequent ActivateInWorld.
        if (HasComp<ActivatableUIComponent>(args.Target))
            return;

        args.Handled = true;
        _tutorial.AdvanceSubGoal(ent);
    }

    private void OnActivateInWorld(Entity<TutorialParticipantComponent> ent, ref UserActivateInWorldEvent args)
    {
        if (args.Handled)
            return;

        if (!_tutorial.TryGetCurrentSubGoal(ent, ent.Comp, out var sub))
            return;

        if (sub.Complete != TutorialStepComplete.InteractTargetTag)
            return;

        if (string.IsNullOrEmpty(sub.Tag) || !_tags.HasTag(args.Target, (ProtoId<TagPrototype>) sub.Tag))
            return;

        if (HasComp<ActivatableUIComponent>(args.Target))
            return;

        args.Handled = true;
        _tutorial.AdvanceSubGoal(ent);
    }

    private void OnAfterActivatableUIOpen(Entity<ActivatableUIComponent> machine, ref AfterActivatableUIOpenEvent args)
    {
        if (!TryComp<TutorialParticipantComponent>(args.User, out var part))
            return;

        if (!_tutorial.TryGetCurrentSubGoal(args.User, part, out var sub))
            return;

        if (sub.Complete != TutorialStepComplete.InteractTargetTag)
            return;

        if (string.IsNullOrEmpty(sub.Tag) || !_tags.HasTag(machine.Owner, (ProtoId<TagPrototype>) sub.Tag))
            return;

        _tutorial.AdvanceSubGoal(args.User);
    }

    private void OnUseInHand(Entity<TutorialSensorTargetComponent> used, ref UseInHandEvent args)
    {
        var user = args.User;
        if (!TryComp<TutorialParticipantComponent>(user, out var part))
            return;

        if (!_tutorial.TryGetCurrentSubGoal(user, part, out var sub))
            return;

        if (sub.Complete != TutorialStepComplete.UseInHand)
            return;

        if (sub.Entity != null)
        {
            if (!IsProto(used, sub.Entity.Value))
                return;
        }
        else if (!string.IsNullOrEmpty(sub.Tag))
        {
            if (!_tags.HasTag(used, (ProtoId<TagPrototype>) sub.Tag))
                return;
        }
        else
        {
            return;
        }

        // Opening a sealed drink/container is the first Z press (Openable sets Handled).
        // Require a later Use once open so deferred-guide open does not fire on open alone.
        if (args.Handled && HasComp<OpenableComponent>(used.Owner))
            return;

        if (_openable.IsClosed(used.Owner))
            return;

        _tutorial.AdvanceSubGoal(user);
    }

    private void OnItemDropped(Entity<ItemComponent> item, ref DroppedEvent args)
    {
        var user = args.User;
        if (!TryComp<TutorialParticipantComponent>(user, out var part))
            return;

        if (!_tutorial.TryGetCurrentSubGoal(user, part, out var sub))
            return;

        if (sub.Complete != TutorialStepComplete.DropItem)
            return;

        if (sub.Entity == null || !IsProto(item.Owner, sub.Entity.Value))
            return;

        _tutorial.AdvanceSubGoal(user);
    }

    private void TryCompleteReachMarker(EntityUid mob, TransformComponent xform, TutorialSubGoalData sub)
    {
        if (string.IsNullOrEmpty(sub.Marker))
            return;

        var mapId = xform.MapID;
        if (mapId == MapId.Nullspace)
            return;

        var mobPos = _transform.GetWorldPosition(xform);
        var markerQuery = EntityQueryEnumerator<TutorialStepMarkerComponent, TransformComponent>();
        while (markerQuery.MoveNext(out _, out var marker, out var markerXform))
        {
            if (marker.MarkerId != sub.Marker)
                continue;

            if (markerXform.MapID != mapId)
                continue;

            var markerPos = _transform.GetWorldPosition(markerXform);
            if ((markerPos - mobPos).Length() > MarkerReachRange)
                continue;

            _tutorial.AdvanceSubGoal(mob);
            return;
        }
    }

    private void TryCompleteFromPossession(EntityUid mob, TutorialSubGoalData sub)
    {
        switch (sub.Complete)
        {
            case TutorialStepComplete.HoldItem:
                if (sub.Entity == null)
                    return;
                if (!IsHoldingProto(mob, sub.Entity.Value))
                    return;
                break;
            case TutorialStepComplete.HoldTag:
                if (string.IsNullOrEmpty(sub.Tag))
                    return;
                if (!IsHoldingTag(mob, sub.Tag))
                    return;
                break;
            case TutorialStepComplete.ObtainItem:
                if (sub.Entity == null)
                    return;
                // PillCanister + MinCount: require that many items inside the canister (filled pills).
                int? storageCount = sub.Entity.Value.Id == "PillCanister" && sub.MinCount > 0
                    ? sub.MinCount
                    : null;
                if (!HasProto(mob, sub.Entity.Value, requireStorageCount: storageCount))
                    return;
                break;
            case TutorialStepComplete.StowItem:
                if (sub.Entity == null)
                    return;
                if (IsHoldingProto(mob, sub.Entity.Value))
                    return;
                if (!HasStowedProto(mob, sub.Entity.Value))
                    return;
                break;
            default:
                return;
        }

        _tutorial.AdvanceSubGoal(mob);
    }

    private bool IsHoldingProto(EntityUid mob, EntProtoId proto)
    {
        foreach (var held in _hands.EnumerateHeld(mob))
        {
            if (IsProto(held, proto))
                return true;
        }

        return false;
    }

    private bool IsHoldingTag(EntityUid mob, string tag)
    {
        foreach (var held in _hands.EnumerateHeld(mob))
        {
            if (_tags.HasTag(held, (ProtoId<TagPrototype>) tag))
                return true;
        }

        return false;
    }

    private bool HasProto(EntityUid mob, EntProtoId proto, int? requireStorageCount = null)
    {
        foreach (var held in _hands.EnumerateHeld(mob))
        {
            if (IsProto(held, proto) && MatchesStorageCount(held, requireStorageCount))
                return true;
        }

        foreach (var item in _inventory.GetHandOrInventoryEntities(mob))
        {
            if (IsProto(item, proto) && MatchesStorageCount(item, requireStorageCount))
                return true;
        }

        return false;
    }

    private bool MatchesStorageCount(EntityUid item, int? requireStorageCount)
    {
        if (requireStorageCount is null or <= 0)
            return true;

        if (!TryComp<StorageComponent>(item, out var storage))
            return false;

        return storage.Container.ContainedEntities.Count >= requireStorageCount.Value;
    }

    /// <summary>
    /// True when the proto is equipped in an inventory slot or inside equipped storage (not held).
    /// </summary>
    private bool HasStowedProto(EntityUid mob, EntProtoId proto)
    {
        var enumerator = _inventory.GetSlotEnumerator(mob);
        while (enumerator.NextItem(out var item))
        {
            if (IsProto(item, proto))
                return true;

            if (!TryComp<Content.Shared.Storage.StorageComponent>(item, out var storage))
                continue;

            foreach (var stored in storage.Container.ContainedEntities)
            {
                if (IsProto(stored, proto))
                    return true;
            }
        }

        return false;
    }

    private bool IsProto(EntityUid uid, EntProtoId proto)
    {
        var meta = MetaData(uid);
        return meta.EntityPrototype?.ID == proto.Id;
    }

    private bool TryCompleteSolutionContains(EntityUid mob, TutorialSubGoalData sub)
    {
        if (sub.Reagent == null)
            return false;

        var min = sub.MinAmount > 0 ? sub.MinAmount : FixedPoint2.New(1);
        var reagent = sub.Reagent.Value;

        foreach (var held in _hands.EnumerateHeld(mob))
        {
            if (SolutionHasReagent(held, reagent, min))
                return true;
        }

        foreach (var item in _inventory.GetHandOrInventoryEntities(mob))
        {
            if (SolutionHasReagent(item, reagent, min))
                return true;
        }

        return false;
    }

    private bool SolutionHasReagent(EntityUid uid, ProtoId<Content.Shared.Chemistry.Reagent.ReagentPrototype> reagent, FixedPoint2 min)
    {
        foreach (var (_, soln) in _solutions.EnumerateSolutions(uid))
        {
            if (soln.Comp.Solution.GetTotalPrototypeQuantity(reagent) >= min)
                return true;
        }

        return false;
    }

    private bool IsPuddleCleared(EntityUid? mapUid, string? markerId)
    {
        if (mapUid == null || string.IsNullOrEmpty(markerId))
            return false;

        var query = EntityQueryEnumerator<TutorialStepMarkerComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var marker, out var xform))
        {
            if (marker.MarkerId != markerId || xform.MapUid != mapUid)
                continue;

            if (HasComp<PuddleComponent>(uid) && _puddle.CurrentVolume(uid) > FixedPoint2.Zero)
                return false;
        }

        // No filled puddle remains with this marker (entity deleted or emptied).
        return true;
    }

    private bool IsPracticeMobDamageBelow(EntityUid? mapUid, float maxDamage)
    {
        if (mapUid == null)
            return false;

        var found = false;
        var query = EntityQueryEnumerator<TutorialPracticeMobComponent, DamageableComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out _, out var damageable, out var xform))
        {
            if (xform.MapUid != mapUid)
                continue;

            // Dead corpses coexist with heal drills; only living/crit patients gate completion.
            if (TryComp<MobStateComponent>(uid, out var mobState) &&
                mobState.CurrentState == MobState.Dead)
                continue;

            found = true;
            if (_damageable.GetTotalDamage((uid, damageable)).Float() > maxDamage)
                return false;
        }

        return found;
    }

    private bool IsAmeInjecting(EntityUid? mapUid)
    {
        if (mapUid == null)
            return false;

        var query = EntityQueryEnumerator<AmeControllerComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var controller, out var xform))
        {
            if (xform.MapUid != mapUid)
                continue;

            if (!controller.Injecting || controller.InjectionAmount <= 0)
                continue;

            if (controller.FuelSlot.Item is not { } jar ||
                !TryComp<AmeFuelContainerComponent>(jar, out var fuel) ||
                fuel.FuelAmount <= 0)
                continue;

            return true;
        }

        return false;
    }

    private bool IsMapHasEntity(EntityUid? mapUid, TutorialSubGoalData sub)
    {
        if (mapUid == null || sub.Entity == null)
            return false;

        var needed = sub.MinCount > 0 ? sub.MinCount : 1;
        var count = 0;
        var query = EntityQueryEnumerator<MetaDataComponent, TransformComponent>();
        while (query.MoveNext(out _, out var meta, out var xform))
        {
            if (xform.MapUid != mapUid)
                continue;

            if (meta.EntityPrototype?.ID != sub.Entity.Value.Id)
                continue;

            count++;
            if (count >= needed)
                return true;
        }

        return false;
    }

    private bool IsWiresPanelOpen(EntityUid? mapUid, string? tag)
    {
        if (mapUid == null || string.IsNullOrEmpty(tag))
            return false;

        var tagId = (ProtoId<TagPrototype>) tag;
        var query = EntityQueryEnumerator<WiresPanelComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var panel, out var xform))
        {
            if (xform.MapUid != mapUid)
                continue;

            if (!_wires.IsPanelOpen((uid, panel)))
                continue;

            if (!_tags.HasTag(uid, tagId))
                continue;

            return true;
        }

        return false;
    }

    private bool IsWearingProto(EntityUid mob, EntProtoId proto)
    {
        var enumerator = _inventory.GetSlotEnumerator(mob);
        while (enumerator.NextItem(out var item))
        {
            if (IsProto(item, proto))
                return true;
        }

        return false;
    }

    private bool IsTargetPowerDisabled(EntityUid? mapUid, string? tag)
    {
        if (mapUid == null || string.IsNullOrEmpty(tag))
            return false;

        var tagId = (ProtoId<TagPrototype>) tag;
        var query = EntityQueryEnumerator<ApcPowerReceiverComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var power, out var xform))
        {
            if (xform.MapUid != mapUid)
                continue;

            if (!_tags.HasTag(uid, tagId))
                continue;

            if (power.PowerDisabled || !power.Powered)
                return true;
        }

        return false;
    }

    private bool IsTargetDoorOpen(EntityUid? mapUid, string? tag)
    {
        if (mapUid == null || string.IsNullOrEmpty(tag))
            return false;

        var tagId = (ProtoId<TagPrototype>) tag;
        var query = EntityQueryEnumerator<DoorComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var door, out var xform))
        {
            if (xform.MapUid != mapUid)
                continue;

            if (!_tags.HasTag(uid, tagId))
                continue;

            if (door.State is DoorState.Open or DoorState.Opening)
                return true;
        }

        return false;
    }

    private bool IsPowerWiresCut(EntityUid? mapUid, string? tag)
    {
        if (mapUid == null || string.IsNullOrEmpty(tag))
            return false;

        var tagId = (ProtoId<TagPrototype>) tag;
        var query = EntityQueryEnumerator<WiresComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var wires, out var xform))
        {
            if (xform.MapUid != mapUid)
                continue;

            if (!_tags.HasTag(uid, tagId))
                continue;

            if (!_wiresServer.TryGetData<int?>(uid, PowerWireActionKey.CutWires, out var cut, wires) ||
                !_wiresServer.TryGetData<int?>(uid, PowerWireActionKey.WireCount, out var count, wires))
                continue;

            if (cut is > 0 && count is > 0 && cut == count)
                return true;
        }

        return false;
    }

    private bool IsPracticeMobCreamPied(EntityUid? mapUid)
    {
        if (mapUid == null)
            return false;

        var query = EntityQueryEnumerator<TutorialPracticeMobComponent, CreamPiedComponent, TransformComponent>();
        while (query.MoveNext(out _, out _, out var creamPied, out var xform))
        {
            if (xform.MapUid != mapUid)
                continue;

            if (creamPied.CreamPied)
                return true;
        }

        return false;
    }

    private bool IsPracticeMobBuckled(EntityUid? mapUid)
    {
        if (mapUid == null)
            return false;

        var query = EntityQueryEnumerator<TutorialPracticeMobComponent, BuckleComponent, TransformComponent>();
        while (query.MoveNext(out _, out _, out var buckle, out var xform))
        {
            if (xform.MapUid != mapUid)
                continue;

            if (buckle.BuckledTo is not { } strap)
                continue;

            if (_tags.HasTag(strap, TutorialRollerBedTag))
                return true;
        }

        return false;
    }

    private bool IsStarlightSurgeryUiOpen(EntityUid user, EntityUid? mapUid)
    {
        if (mapUid == null)
            return false;

        var query = EntityQueryEnumerator<TutorialStarlightSurgeryTargetComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out _, out var xform))
        {
            if (xform.MapUid != mapUid)
                continue;

            if (_ui.IsUiOpen(uid, TutorialStarlightSurgeryUiKey.Key, user))
                return true;
        }

        return false;
    }

    private bool IsStarlightSurgeryEyeImplanted(EntityUid? mapUid)
    {
        if (mapUid == null)
            return false;

        var query = EntityQueryEnumerator<TutorialStarlightSurgeryTargetComponent, TransformComponent>();
        while (query.MoveNext(out _, out var target, out var xform))
        {
            if (xform.MapUid != mapUid)
                continue;

            if (target.ExampleSurgeryComplete)
                return true;
        }

        return false;
    }

    private bool IsCyberMedSurgeryUiOpen(EntityUid user, EntityUid? mapUid)
    {
        if (mapUid == null)
            return false;

        var query = EntityQueryEnumerator<TutorialCyberMedAnalyzerComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out _, out var xform))
        {
            if (xform.MapUid != mapUid)
                continue;

            if (_ui.IsUiOpen(uid, TutorialCyberMedUiKey.Key, user))
                return true;
        }

        return false;
    }

    private bool IsCyberMedSurgeryComplete(EntityUid? mapUid)
    {
        if (mapUid == null)
            return false;

        var query = EntityQueryEnumerator<TutorialCyberMedSurgeryTargetComponent, TransformComponent>();
        while (query.MoveNext(out _, out var target, out var xform))
        {
            if (xform.MapUid != mapUid)
                continue;

            if (target.ExampleSurgeryComplete)
                return true;
        }

        return false;
    }

    private bool IsIdCardHasJob(EntityUid? mapUid, ProtoId<JobPrototype>? job)
    {
        if (mapUid == null || job == null)
            return false;

        var query = EntityQueryEnumerator<IdCardComponent, TransformComponent>();
        while (query.MoveNext(out _, out var idCard, out var xform))
        {
            if (xform.MapUid != mapUid)
                continue;

            if (idCard.JobPrototype == job)
                return true;
        }

        return false;
    }

    private bool IsContainerHasEntityCount(EntityUid? mapUid, TutorialSubGoalData sub)
    {
        if (mapUid == null || string.IsNullOrEmpty(sub.Tag))
            return false;

        var needed = sub.MinCount > 0 ? sub.MinCount : 1;
        var tagId = (ProtoId<TagPrototype>) sub.Tag;
        var query = EntityQueryEnumerator<TransformComponent>();
        while (query.MoveNext(out var uid, out var xform))
        {
            if (xform.MapUid != mapUid)
                continue;

            if (!_tags.HasTag(uid, tagId))
                continue;

            var count = 0;
            if (TryComp<StorageComponent>(uid, out var storage))
            {
                foreach (var _ in storage.Container.ContainedEntities)
                    count++;
            }
            else if (_containers.TryGetContainer(uid, "entity_storage", out var entityStorage))
            {
                count = entityStorage.ContainedEntities.Count;
            }
            else
            {
                continue;
            }

            if (count >= needed)
                return true;
        }

        return false;
    }

    private bool IsTegProducingPower(EntityUid? mapUid)
    {
        if (mapUid == null)
            return false;

        var query = EntityQueryEnumerator<TegGeneratorComponent, TransformComponent>();
        while (query.MoveNext(out _, out var teg, out var xform))
        {
            if (xform.MapUid != mapUid)
                continue;

            if (teg.LastGeneration > 0f)
                return true;
        }

        return false;
    }

    private bool IsResearchUnlocked(EntityUid? mapUid, ProtoId<TechnologyPrototype>? technology)
    {
        if (mapUid == null || technology == null)
            return false;

        var query = EntityQueryEnumerator<TechnologyDatabaseComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out _, out var xform))
        {
            if (xform.MapUid != mapUid)
                continue;

            if (_research.IsTechnologyUnlocked(uid, technology.Value.Id))
                return true;
        }

        return false;
    }

    private bool IsNukeArmed(EntityUid? mapUid)
    {
        if (mapUid == null)
            return false;

        var query = EntityQueryEnumerator<NukeComponent, TransformComponent>();
        while (query.MoveNext(out _, out var nuke, out var xform))
        {
            if (xform.MapUid != mapUid)
                continue;

            if (nuke.Status == NukeStatus.ARMED)
                return true;
        }

        return false;
    }

    private int CountInfectedPracticeMobs(EntityUid? mapUid)
    {
        if (mapUid == null)
            return 0;

        var count = 0;
        var query = EntityQueryEnumerator<TutorialPracticeMobComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out _, out var xform))
        {
            if (xform.MapUid != mapUid)
                continue;

            if (HasComp<PendingZombieComponent>(uid) || HasComp<ZombieComponent>(uid))
                count++;
        }

        return count;
    }

    private int CountConvertedPracticeMobs(EntityUid? mapUid)
    {
        if (mapUid == null)
            return 0;

        var count = 0;
        var query = EntityQueryEnumerator<TutorialPracticeMobComponent, RevolutionaryComponent, TransformComponent>();
        while (query.MoveNext(out _, out _, out _, out var xform))
        {
            if (xform.MapUid != mapUid)
                continue;

            count++;
        }

        return count;
    }

    private bool HasDeadPracticeMob(EntityUid? mapUid)
    {
        if (mapUid == null)
            return false;

        var query = EntityQueryEnumerator<TutorialPracticeMobComponent, MobStateComponent, TransformComponent>();
        while (query.MoveNext(out _, out _, out var mobState, out var xform))
        {
            if (xform.MapUid != mapUid)
                continue;

            if (mobState.CurrentState == MobState.Dead)
                return true;
        }

        return false;
    }

    private static readonly ProtoId<TagPrototype> TutorialPracticeCorpseTag = "TutorialPracticeCorpse";

    /// <summary>
    /// A practice corpse finished a revive (left Dead for Critical/Alive).
    /// </summary>
    private bool HasRevivedPracticeCorpse(EntityUid? mapUid)
    {
        if (mapUid == null)
            return false;

        var query = EntityQueryEnumerator<TutorialPracticeMobComponent, MobStateComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out _, out var mobState, out var xform))
        {
            if (xform.MapUid != mapUid)
                continue;

            if (!_tags.HasTag(uid, TutorialPracticeCorpseTag))
                continue;

            if (mobState.CurrentState is MobState.Critical or MobState.Alive)
                return true;
        }

        return false;
    }

    private bool PlayerHasAction(EntityUid uid, EntProtoId? actionProto)
    {
        if (actionProto == null)
            return false;

        foreach (var (actionUid, _) in _actions.GetActions(uid))
        {
            if (MetaData(actionUid).EntityPrototype?.ID == actionProto.Value.Id)
                return true;
        }

        return false;
    }

    private bool IsBrigTimerStarted(EntityUid? mapUid)
    {
        if (mapUid == null)
            return false;

        var query = EntityQueryEnumerator<ActiveSignalTimerComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out _, out var xform))
        {
            if (xform.MapUid != mapUid)
                continue;

            if (_tags.HasTag(uid, TutorialBrigTimerTag))
                return true;
        }

        return false;
    }

    private bool IsPracticeMobStunned(EntityUid? mapUid)
    {
        if (mapUid == null)
            return false;

        var query = EntityQueryEnumerator<TutorialPracticeMobComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out _, out var xform))
        {
            if (xform.MapUid != mapUid)
                continue;

            if (HasComp<StunnedComponent>(uid) || HasComp<KnockedDownComponent>(uid))
                return true;
        }

        return false;
    }

    private bool HasStorePurchase(EntityUid mob, TutorialSubGoalData sub)
    {
        var needed = sub.MinCount > 0 ? sub.MinCount : 1;

        foreach (var store in EnumerateParticipantStores(mob))
        {
            if (store.BoughtEntities.Count >= needed)
                return true;

            if (store.BalanceSpent.TryGetValue(TelecrystalCurrency, out var spent) && spent > 0)
                return true;
        }

        return false;
    }

    private bool IsThiefBeaconLinked(EntityUid? mapUid)
    {
        if (mapUid == null)
            return false;

        var query = EntityQueryEnumerator<ThiefBeaconComponent, StealAreaComponent, TransformComponent>();
        while (query.MoveNext(out _, out _, out var area, out var xform))
        {
            if (xform.MapUid != mapUid)
                continue;

            if (area.OwnerCount > 0)
                return true;
        }

        return false;
    }

    private IEnumerable<StoreComponent> EnumerateParticipantStores(EntityUid mob)
    {
        foreach (var item in _inventory.GetHandOrInventoryEntities(mob))
        {
            if (TryComp<StoreComponent>(item, out var direct))
                yield return direct;

            if (TryComp<RemoteStoreComponent>(item, out var remote) &&
                remote.Store is { } storeUid &&
                TryComp<StoreComponent>(storeUid, out var remoteStore))
            {
                yield return remoteStore;
            }
        }

        foreach (var held in _hands.EnumerateHeld(mob))
        {
            if (TryComp<StoreComponent>(held, out var direct))
                yield return direct;

            if (TryComp<RemoteStoreComponent>(held, out var remote) &&
                remote.Store is { } storeUid &&
                TryComp<StoreComponent>(storeUid, out var remoteStore))
            {
                yield return remoteStore;
            }
        }

        if (_containers.TryGetContainer(mob, ImplanterComponent.ImplantSlotId, out var implants))
        {
            foreach (var implant in implants.ContainedEntities)
            {
                if (TryComp<StoreComponent>(implant, out var implantStore))
                    yield return implantStore;

                if (TryComp<RemoteStoreComponent>(implant, out var remote) &&
                    remote.Store is { } storeUid &&
                    TryComp<StoreComponent>(storeUid, out var remoteStore))
                {
                    yield return remoteStore;
                }
            }
        }
    }

    private bool AnySoldBountyFulfilled(HashSet<EntityUid> sold)
    {
        // Check label presence (CargoSystem may already have removed the bounty DB entry).
        foreach (var ent in sold)
        {
            if (!Exists(ent))
                continue;

            if (!_containers.TryGetContainer(ent, LabelSystem.ContainerName, out var labelContainer))
                continue;

            foreach (var label in labelContainer.ContainedEntities)
            {
                if (HasComp<CargoBountyLabelComponent>(label))
                    return true;
            }
        }

        return false;
    }
}
