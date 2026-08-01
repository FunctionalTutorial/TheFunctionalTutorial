using System.Linq;
using Content.Server._Functional.TutorialServer.UI;
using Content.Server.Chat.Managers;
using Content.Server.EUI;
using Content.Server.GameTicking;
using Content.Server.GameTicking.Rules;
using Content.Server.GameTicking.Rules.Components;
using Content.Server.Humanoid;
using Content.Server.Mind;
using Content.Server.Popups;
using Content.Server.Power.EntitySystems;
using Content.Server.Roles.Jobs;
using Content.Server.Shuttles.Components;
using Content.Server.Station.Events;
using Content.Server.Station.Systems;
using Content.Shared._Functional.TutorialServer;
using Content.Shared.Actions;
using Content.Shared.Bed.Sleep;
using Content.Shared.Popups;
using Content.Shared.Body;
using Content.Shared.CCVar;
using Content.Shared.Damage;
using Content.Shared.Damage.Systems;
using Content.Shared.DetailExaminable;
using Content.Shared.FixedPoint;
using Content.Shared.GameTicking;
using Content.Shared.GameTicking.Components;
using Content.Shared.Ghost;
using Content.Shared.Humanoid;
using Content.Shared.Humanoid.Prototypes;
using Content.Shared.IdentityManagement;
using Content.Shared.Hands.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction.Components;
using Content.Shared.Mind;
using Content.Shared.Mind.Components;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Preferences;
using Content.Shared.Preferences.Loadouts;
using Content.Shared.Roles;
using Content.Shared.SSDIndicator;
using Content.Shared.StatusEffectNew;
using Content.Shared.Verbs;
using Robust.Server.GameObjects;
using Robust.Server.Player;
using Robust.Shared.Configuration;
using Robust.Shared.Enums;
using Robust.Shared.Map;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Server._Functional.TutorialServer;

/// <summary>
/// Orchestrates the Functional Tutorial Server: picker, private maps, sessions, respawn loop.
/// </summary>
public sealed class TutorialServerRuleSystem : GameRuleSystem<TutorialServerRuleComponent>
{
    [Dependency] private readonly IChatManager _chat = default!;
    [Dependency] private readonly IConfigurationManager _cfg = default!;
    [Dependency] private readonly IPlayerManager _players = default!;
    [Dependency] private readonly IPrototypeManager _protos = default!;
    [Dependency] private readonly EuiManager _eui = default!;
    [Dependency] private readonly HumanoidProfileSystem _humanoidProfile = default!;
    [Dependency] private readonly IdentitySystem _identity = default!;
    [Dependency] private readonly JobSystem _jobs = default!;
    [Dependency] private readonly MetaDataSystem _meta = default!;
    [Dependency] private readonly MindSystem _mind = default!;
    [Dependency] private readonly DamageableSystem _damageable = default!;
    [Dependency] private readonly PowerReceiverSystem _power = default!;
    [Dependency] private readonly RespawnRuleSystem _respawn = default!;
    [Dependency] private readonly SharedVisualBodySystem _visualBody = default!;
    [Dependency] private readonly StationSpawningSystem _stationSpawning = default!;
    [Dependency] private readonly StationSystem _station = default!;
    [Dependency] private readonly TutorialMapSystem _tutorialMaps = default!;
    [Dependency] private readonly TutorialPracticeRoomSystem _tutorialRooms = default!;
    [Dependency] private readonly TutorialTegBootstrapSystem _tegBootstrap = default!;
    [Dependency] private readonly TutorialResearchBootstrapSystem _researchBootstrap = default!;
    [Dependency] private readonly TutorialCargoBootstrapSystem _cargoBootstrap = default!;
    [Dependency] private readonly TutorialCommandBootstrapSystem _commandBootstrap = default!;
    [Dependency] private readonly TutorialAntagBootstrapSystem _antagBootstrap = default!;
    [Dependency] private readonly SharedActionsSystem _actions = default!;
    [Dependency] private readonly SharedHandsSystem _hands = default!;
    [Dependency] private readonly PopupSystem _popup = default!;
    [Dependency] private readonly StatusEffectsSystem _statusEffects = default!;
    [Dependency] private readonly UserInterfaceSystem _ui = default!;
    [Dependency] private readonly IGameTiming _timing = default!;

    private static readonly EntProtoId TutorialGuideProto = "TutorialGuide";
    private static readonly EntProtoId TutorialChooseRoleActionProto = "ActionTutorialChooseRole";
    private static readonly EntProtoId StatusEffectSsdSleepingProto = "StatusEffectSSDSleeping";
    private static readonly TimeSpan ProgressPopupCooldown = TimeSpan.FromSeconds(0.75);

    private readonly Dictionary<NetUserId, TutorialRolePickerEui> _openPickers = new();
    private readonly HashSet<EntityUid> _advancing = new();
    private bool _cvarsApplied;
    private bool _restartCleanup;
    private bool _prevOoc;
    private bool _prevLooc;
    private bool _prevDeadChat;
    private bool _prevDisallowLateJoin;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<PlayerBeforeSpawnEvent>(OnBeforeSpawn);
        SubscribeLocalEvent<MobStateChangedEvent>(OnMobStateChanged);
        // Alive /ghost uses TransferTo → MindRemovedMessage on the body. MindUnvisitedMessage only
        // fires when leaving a Visit, so it never sees living tutorial exits.
        SubscribeLocalEvent<TutorialParticipantComponent, MindRemovedMessage>(OnTutorialMindRemoved);
        SubscribeLocalEvent<TutorialPracticeMobComponent, MapInitEvent>(OnPracticeMobMapInit);
        // Do not subscribe GhostComponent.MapInit — GhostSystem already owns that directed event.
        SubscribeLocalEvent<GhostComponent, PlayerAttachedEvent>(OnGhostPlayerAttached);
        SubscribeLocalEvent<GhostComponent, GetVerbsEvent<AlternativeVerb>>(OnGhostGetVerbs);
        SubscribeLocalEvent<TutorialChooseRoleActionEvent>(OnTutorialChooseRoleAction);
        SubscribeLocalEvent<StationPostInitEvent>(OnStationPostInit);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestartCleanup);
        SubscribeNetworkEvent<TutorialAcknowledgeStepEvent>(OnAcknowledgeStep);
        _players.PlayerStatusChanged += OnPlayerStatusChanged;
    }

    /// <summary>
    /// Practice dummies are mindless — strip SSD ZZZ / forced sleep so they stay awake and can speak.
    /// </summary>
    private void OnPracticeMobMapInit(Entity<TutorialPracticeMobComponent> ent, ref MapInitEvent args)
    {
        RemComp<SSDIndicatorComponent>(ent);
        RemComp<SleepingComponent>(ent);
        _statusEffects.TryRemoveStatusEffect(ent.Owner, StatusEffectSsdSleepingProto);
    }

    private void OnGhostPlayerAttached(Entity<GhostComponent> ent, ref PlayerAttachedEvent args)
    {
        if (!TryGetActiveRule(out _, out _, out _))
            return;

        EnsureTutorialChooseAction(ent.Owner);
    }

    /// <summary>
    /// Grants the Choose a tutorial action if the entity does not already have one.
    /// </summary>
    private void EnsureTutorialChooseAction(EntityUid uid)
    {
        foreach (var (actionUid, _) in _actions.GetActions(uid))
        {
            if (MetaData(actionUid).EntityPrototype?.ID == TutorialChooseRoleActionProto.Id)
                return;
        }

        _actions.AddAction(uid, TutorialChooseRoleActionProto);
    }

    private void OnTutorialChooseRoleAction(TutorialChooseRoleActionEvent args)
    {
        if (args.Handled)
            return;

        if (!TryComp<ActorComponent>(args.Performer, out var actor))
            return;

        var player = actor.PlayerSession;

        // Living tutorial body: leave via observer transfer — MindRemoved opens the picker
        // (same path as /ghost). Avoids double EndTutorialSession / picker open.
        if (TryGetActiveRule(out _, out var rule, out _) &&
            rule.Sessions.TryGetValue(player.UserId, out var session) &&
            session.State == TutorialSessionState.InTutorial &&
            session.BodyUid == args.Performer &&
            !HasComp<GhostComponent>(args.Performer))
        {
            GameTicker.JoinAsObserver(player);
            args.Handled = true;
            return;
        }

        TryOpenRolePicker(player);
        args.Handled = true;
    }

    private void OnStationPostInit(ref StationPostInitEvent args)
    {
        if (!TryGetActiveRule(out _, out _, out _))
            return;

        StripStationCentcomm();
    }

    public override void Shutdown()
    {
        base.Shutdown();
        _players.PlayerStatusChanged -= OnPlayerStatusChanged;
    }

    protected override void Started(EntityUid uid, TutorialServerRuleComponent component, GameRuleComponent gameRule, GameRuleStartedEvent args)
    {
        base.Started(uid, component, gameRule, args);
        ApplyTutorialCVars();
        // Lobby must never pull CentComm — strip any leftover StationCentcomm from stations.
        StripStationCentcomm();
    }

    private void StripStationCentcomm()
    {
        var query = EntityQueryEnumerator<StationCentcommComponent>();
        while (query.MoveNext(out var stationUid, out var centcomm))
        {
            if (centcomm.MapEntity is { } mapUid && !TerminatingOrDeleted(mapUid))
                QueueDel(mapUid);
            RemCompDeferred<StationCentcommComponent>(stationUid);
        }
    }

    protected override void Ended(EntityUid uid, TutorialServerRuleComponent component, GameRuleComponent gameRule, GameRuleEndedEvent args)
    {
        base.Ended(uid, component, gameRule, args);

        foreach (var session in component.Sessions.Values)
        {
            if (session.MapUid != EntityUid.Invalid)
                _tutorialMaps.UnloadTutorialMap(session.MapUid);
        }

        component.Sessions.Clear();

        foreach (var eui in _openPickers.Values.ToList())
            eui.Close();
        _openPickers.Clear();

        RestoreTutorialCVars();
    }

    private void ApplyTutorialCVars()
    {
        if (_cvarsApplied)
            return;

        _prevOoc = _cfg.GetCVar(CCVars.OocEnabled);
        _prevLooc = _cfg.GetCVar(CCVars.LoocEnabled);
        _prevDeadChat = _cfg.GetCVar(CCVars.DeadChatEnabled);
        _prevDisallowLateJoin = _cfg.GetCVar(CCVars.GameDisallowLateJoins);

        _cfg.SetCVar(CCVars.OocEnabled, false);
        _cfg.SetCVar(CCVars.LoocEnabled, false);
        _cfg.SetCVar(CCVars.DeadChatEnabled, false);
        _cfg.SetCVar(CCVars.GameDisallowLateJoins, false);
        _cvarsApplied = true;
    }

    private void RestoreTutorialCVars()
    {
        if (!_cvarsApplied)
            return;

        _cfg.SetCVar(CCVars.OocEnabled, _prevOoc);
        _cfg.SetCVar(CCVars.LoocEnabled, _prevLooc);
        _cfg.SetCVar(CCVars.DeadChatEnabled, _prevDeadChat);
        _cfg.SetCVar(CCVars.GameDisallowLateJoins, _prevDisallowLateJoin);
        _cvarsApplied = false;
    }

    private bool TryGetActiveRule(out EntityUid ruleUid, out TutorialServerRuleComponent rule, out RespawnTrackerComponent tracker)
    {
        var query = EntityQueryEnumerator<TutorialServerRuleComponent, RespawnTrackerComponent, GameRuleComponent>();
        while (query.MoveNext(out ruleUid, out rule!, out tracker!, out var gameRule))
        {
            if (GameTicker.IsGameRuleActive(ruleUid, gameRule))
                return true;
        }

        ruleUid = default;
        rule = default!;
        tracker = default!;
        return false;
    }

    private void OnBeforeSpawn(PlayerBeforeSpawnEvent ev)
    {
        if (!TryGetActiveRule(out var ruleUid, out var rule, out var tracker))
            return;

        if (!rule.Sessions.TryGetValue(ev.Player.UserId, out var session))
            session = new TutorialSessionData();

        // E2E: optionally skip picker and jump straight into a configured tutorialRole.
        var autoRole = _cfg.GetCVar(TutorialCVars.E2EAutoRole);
        if (string.IsNullOrWhiteSpace(session.SelectedRoleId) && !string.IsNullOrWhiteSpace(autoRole))
        {
            session.SelectedRoleId = autoRole;
            Log.Info($"TUTORIAL_E2E: auto_role={autoRole} for {ev.Player.Name}");
        }

        if (session.SelectedRoleId != null &&
            ProtoMan.TryIndex<TutorialRolePrototype>(session.SelectedRoleId, out var roleProto))
        {
            if (TryStartTutorial(ev.Player, ev.Profile, ruleUid, rule, tracker, roleProto))
            {
                ev.Handled = true;
                return;
            }
        }

        session.State = TutorialSessionState.PendingSelect;
        session.SelectedRoleId = null;
        session.PickerQuit = false;
        session.GuideAutoOpened = false;
        session.MapUid = EntityUid.Invalid;
        session.GridUid = EntityUid.Invalid;
        session.BodyUid = EntityUid.Invalid;
        session.GuideUid = EntityUid.Invalid;
        session.StepIndex = 0;
        session.GoalIndex = 0;
        session.SubGoalIndex = 0;
        session.Completed = false;
        rule.Sessions[ev.Player.UserId] = session;

        // Handled spawn skips a body; attach an observer first so GameplayState has a valid map
        // (otherwise client input spams Map=0 transform errors and the picker can look broken).
        if (ev.Player.AttachedEntity is not { } existing || !HasComp<GhostComponent>(existing))
            GameTicker.JoinAsObserver(ev.Player);

        OpenPicker(ev.Player);
        Log.Info($"TUTORIAL_E2E: opened_role_picker for {ev.Player.Name}");
        // Claim spawn so default late-join does not place players on the lobby station.
        ev.Handled = true;
    }

    public void TrySelectRole(ICommonSession player, string roleId, bool confirmedStub)
    {
        if (string.IsNullOrEmpty(roleId))
            return;

        if (!TryGetActiveRule(out _, out var rule, out _))
            return;

        if (!ProtoMan.TryIndex<TutorialRolePrototype>(roleId, out var roleProto))
            return;

        if (roleProto.Stub && !confirmedStub)
        {
            _chat.DispatchServerMessage(player, Loc.GetString("tutorial-server-stub-confirm-needed"));
            return;
        }

        if (!rule.Sessions.TryGetValue(player.UserId, out var session))
            session = new TutorialSessionData();

        session.SelectedRoleId = roleId;
        session.PickerQuit = false;
        session.State = TutorialSessionState.PendingSelect;
        rule.Sessions[player.UserId] = session;

        if (_openPickers.Remove(player.UserId, out var eui))
            eui.Close();

        var station = GetAnyStation();
        GameTicker.MakeJoinGame(player, station, silent: true);
    }

    public void OnPickerClosed(ICommonSession player)
    {
        _openPickers.Remove(player.UserId);

        // Round restart closes EUIs while the old rule is still briefly active — do not re-open.
        if (_restartCleanup)
            return;

        if (!TryGetActiveRule(out _, out var rule, out _))
            return;

        if (!rule.Sessions.TryGetValue(player.UserId, out var session))
            return;

        // Dismiss (window X) must not force-reopen — that locked living players / observers into
        // the picker. They already have Choose a tutorial on spawn and as ghosts.
        if (session.PickerQuit || session.State != TutorialSessionState.PendingSelect)
            return;

        if (session.SelectedRoleId != null)
            return;

        session.PickerQuit = true;
        rule.Sessions[player.UserId] = session;

        // BeforeSpawn may have claimed spawn without a body; keep a ghost so the action works.
        if (player.AttachedEntity is not { } body || !HasComp<GhostComponent>(body))
            GameTicker.JoinAsObserver(player);

        _chat.DispatchServerMessage(player, Loc.GetString("tutorial-server-picker-quit-tip"));
    }

    public void OnPickerQuit(ICommonSession player)
    {
        if (!TryGetActiveRule(out _, out var rule, out _))
            return;

        if (!rule.Sessions.TryGetValue(player.UserId, out var session))
            session = new TutorialSessionData();

        session.PickerQuit = true;
        session.SelectedRoleId = null;
        session.State = TutorialSessionState.PendingSelect;
        rule.Sessions[player.UserId] = session;

        if (_openPickers.Remove(player.UserId, out var eui))
            eui.Close();

        GameTicker.JoinAsObserver(player);
        _chat.DispatchServerMessage(player, Loc.GetString("tutorial-server-picker-quit-tip"));
    }

    /// <summary>
    /// Opens the role picker for a ghost / observer (or after leaving a living tutorial body).
    /// </summary>
    public void TryOpenRolePicker(ICommonSession player)
    {
        if (!TryGetActiveRule(out _, out var rule, out _))
            return;

        if (player.AttachedEntity is not { } ent || !HasComp<GhostComponent>(ent))
            return;

        if (!rule.Sessions.TryGetValue(player.UserId, out var session))
            session = new TutorialSessionData();

        session.PickerQuit = false;
        session.SelectedRoleId = null;
        session.State = TutorialSessionState.PendingSelect;
        rule.Sessions[player.UserId] = session;

        OpenPicker(player);
    }

    /// <inheritdoc cref="TryOpenRolePicker"/>
    public void TryOpenPickerForGhost(ICommonSession player) => TryOpenRolePicker(player);

    public bool IsPickerOpen(ICommonSession player) => _openPickers.ContainsKey(player.UserId);

    private void OnGhostGetVerbs(Entity<GhostComponent> ent, ref GetVerbsEvent<AlternativeVerb> args)
    {
        if (!TryGetActiveRule(out _, out _, out _))
            return;

        if (args.User != ent.Owner)
            return;

        if (!TryComp<ActorComponent>(ent.Owner, out var actor))
            return;

        var player = actor.PlayerSession;
        args.Verbs.Add(new AlternativeVerb
        {
            Text = Loc.GetString("tutorial-server-ghost-choose"),
            Priority = 10,
            Act = () => TryOpenRolePicker(player),
        });
    }

    private void OpenPicker(ICommonSession player)
    {
        if (_openPickers.TryGetValue(player.UserId, out var existing))
        {
            if (!existing.IsShutDown)
            {
                existing.StateDirty();
                return;
            }

            _openPickers.Remove(player.UserId);
        }

        var entries = BuildPickerEntries();
        var eui = new TutorialRolePickerEui(this, entries);
        _openPickers[player.UserId] = eui;
        _eui.OpenEui(eui, player);
        eui.StateDirty();
    }

    private void OnRoundRestartCleanup(RoundRestartCleanupEvent args)
    {
        // Clear before FlushEntities / ClearGameRules so stale EUIs cannot block a later OpenPicker.
        _restartCleanup = true;
        try
        {
            foreach (var eui in _openPickers.Values.ToList())
                eui.Close();
            _openPickers.Clear();
            _advancing.Clear();
        }
        finally
        {
            _restartCleanup = false;
        }
    }

    /// <summary>
    /// Builds the role-picker list: Passenger first, then departments, server-specific,
    /// antagonists last. ERT packages are omitted.
    /// </summary>
    public List<TutorialRolePickerEntry> BuildPickerEntries()
    {
        var list = new List<TutorialRolePickerEntry>();
        foreach (var proto in ProtoMan.EnumeratePrototypes<TutorialRolePrototype>())
        {
            if (IsErtTutorialRole(proto))
                continue;

            list.Add(new TutorialRolePickerEntry
            {
                RoleId = proto.ID,
                DisplayName = GetRoleDisplayName(proto),
                Category = proto.Category,
                SubCategory = proto.SubCategory,
                Stub = proto.Stub,
            });
        }

        return list
            .OrderBy(e => GetPickerCategoryOrder(e))
            .ThenBy(e => e.Category, StringComparer.Ordinal)
            .ThenBy(e => e.SubCategory ?? string.Empty, StringComparer.Ordinal)
            .ThenBy(e => e.DisplayName, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// 0 = Passenger/Assistant basics, 1 = departments, 2 = server-specific, 3 = wizden antagonists.
    /// </summary>
    private static int GetPickerCategoryOrder(TutorialRolePickerEntry entry)
    {
        if (entry.RoleId == "TutorialPassenger")
            return 0;

        if (string.Equals(entry.Category, "Wizden antagonists", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(entry.Category, "Antagonist", StringComparison.OrdinalIgnoreCase))
            return 3;

        if (string.Equals(entry.Category, "Server specific", StringComparison.OrdinalIgnoreCase))
            return 2;

        return 1;
    }

    private static bool IsErtTutorialRole(TutorialRolePrototype proto)
    {
        if (proto.ID.Contains("ERT", StringComparison.OrdinalIgnoreCase))
            return true;

        return proto.Job is { } job &&
               job.Id.StartsWith("ERT", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Picker label: explicit <see cref="TutorialRolePrototype.Name"/>, else antag, else job, else id.
    /// Antag is preferred over job so packages that outfit as Passenger still show the antag name.
    /// </summary>
    public string GetRoleDisplayName(TutorialRolePrototype proto)
    {
        if (!string.IsNullOrEmpty(proto.Name))
            return Loc.GetString(proto.Name);

        if (proto.Antag != null && ProtoMan.TryIndex(proto.Antag.Value, out AntagPrototype? antag))
            return Loc.GetString(antag.Name);

        if (proto.Job != null && ProtoMan.TryIndex(proto.Job.Value, out JobPrototype? job))
            return job.LocalizedName;

        return proto.ID;
    }

    private bool TryStartTutorial(
        ICommonSession player,
        Content.Shared.Preferences.HumanoidCharacterProfile profile,
        EntityUid ruleUid,
        TutorialServerRuleComponent rule,
        RespawnTrackerComponent tracker,
        TutorialRolePrototype roleProto)
    {
        if (!_tutorialMaps.TryLoadTutorialMap(roleProto, out var mapUid, out var gridUid, out var spawnCoords))
        {
            _chat.DispatchServerMessage(player, Loc.GetString("tutorial-server-map-load-failed"));
            return false;
        }

        var mob = roleProto.SpawnEntity != null
            ? Spawn(roleProto.SpawnEntity.Value, spawnCoords)
            : roleProto.StartingGear != null
                ? SpawnAntagTutorialMob(spawnCoords, profile, player, roleProto)
                : _stationSpawning.SpawnPlayerMob(spawnCoords, roleProto.Job ?? "Passenger", profile, null);

        var mindId = _mind.CreateMind(player.UserId, profile.Name);
        _mind.SetUserId(mindId, player.UserId);
        _mind.TransferTo(mindId, mob);

        // Antag tutorials use StartingGear / SpawnEntity — do not attach Passenger job clothes/roles.
        if (roleProto.SpawnEntity == null && roleProto.StartingGear == null && roleProto.Job != null)
            _jobs.MindAddJob(mindId, roleProto.Job.Value);

        if (TryComp<MindComponent>(mindId, out var mindComp))
        {
            foreach (var objectiveId in roleProto.PlaceholderObjectives)
                _mind.TryAddObjective(mindId, mindComp, objectiveId);
        }

        _antagBootstrap.ApplyTutorialAntag(mob, mindId, roleProto.Antag);

        SpawnPracticeEntities(roleProto, gridUid, spawnCoords);

        if (roleProto.Antag is { } antagId && antagId.Id == "Thief")
            _antagBootstrap.PrepareThiefPracticeMobs(gridUid);

        var session = rule.Sessions.GetValueOrDefault(player.UserId) ?? new TutorialSessionData();
        session.State = TutorialSessionState.InTutorial;
        session.SelectedRoleId = roleProto.ID;
        session.MapUid = mapUid;
        session.GridUid = gridUid;
        session.BodyUid = mob;
        session.StepIndex = 0;
        session.GoalIndex = 0;
        session.SubGoalIndex = 0;
        session.Completed = false;
        session.AwaitingChamberEntryPad = false;
        rule.Sessions[player.UserId] = session;

        // Chamber 0 starts open. Later chambers unlock only when a goal sets EnterRoom
        // (or legacy room==goalIndex practice spawns).
        _tutorialRooms.UnlockGatesForGoal(gridUid, 0);

        EnsureComp<TutorialParticipantComponent>(mob);
        RefreshParticipantHud(mob, roleProto, session);
        GiveTutorialGuide(mob, session, spawnCoords, player, roleProto);
        EnsureTutorialChooseAction(mob);
        rule.Sessions[player.UserId] = session;

        _respawn.AddToTracker(player.UserId, (ruleUid, tracker));
        Log.Info($"TUTORIAL_E2E: private_map_loaded role={roleProto.ID} map={mapUid} body={mob} player={player.Name}");
        return true;
    }

    private void GiveTutorialGuide(
        EntityUid mob,
        TutorialSessionData session,
        EntityCoordinates spawnCoords,
        ICommonSession player,
        TutorialRolePrototype roleProto)
    {
        if (session.GuideUid != EntityUid.Invalid && !Deleted(session.GuideUid))
            QueueDel(session.GuideUid);

        var guide = Spawn(TutorialGuideProto, spawnCoords);
        EnsureComp<UnremoveableComponent>(guide);
        session.GuideUid = guide;
        // Off-hand (left) so the active right hand stays free for pickup practice.
        GiveTutorialGuideToOffHand(mob, guide);

        // One-shot discoverability: chat tip + highlight popup on the tablet.
        _chat.DispatchServerMessage(player, Loc.GetString("tutorial-server-guide-tip"));
        _popup.PopupEntity(Loc.GetString("tutorial-server-guide-highlight"), guide, player, PopupType.Medium);

        if (roleProto.AutoOpenGuide)
        {
            _ui.OpenUi(guide, TutorialPromptUiKey.Key, mob);
            session.GuideAutoOpened = true;
        }
    }

    /// <summary>
    /// Puts the unremoveable tutorial tablet in the left hand and keeps the right hand active/empty.
    /// </summary>
    private void GiveTutorialGuideToOffHand(EntityUid mob, EntityUid guide)
    {
        if (!TryComp<HandsComponent>(mob, out var hands))
        {
            _hands.PickupOrDrop(mob, guide, checkActionBlocker: false);
            return;
        }

        string? leftHand = null;
        string? rightHand = null;
        foreach (var handId in _hands.EnumerateHands((mob, hands)))
        {
            if (!_hands.TryGetHand((mob, hands), handId, out var hand))
                continue;

            if (hand.Value.Location == HandLocation.Left)
                leftHand = handId;
            else if (hand.Value.Location == HandLocation.Right)
                rightHand = handId;
        }

        if (leftHand != null &&
            _hands.TryPickup(mob, guide, leftHand, checkActionBlocker: false, handsComp: hands))
        {
            if (rightHand != null)
                _hands.TrySetActiveHand((mob, hands), rightHand);
            return;
        }

        _hands.PickupOrDrop(mob, guide, checkActionBlocker: false, handsComp: hands);
        if (rightHand != null && _hands.HandIsEmpty((mob, hands), rightHand))
            _hands.TrySetActiveHand((mob, hands), rightHand);
    }

    /// <summary>
    /// Opens the deferred guide Bound UI once. Must not run synchronously from a UseInHand /
    /// drink completion — that steals focus mid-interaction (Passenger water bottle).
    /// </summary>
    private void TryOpenDeferredGuide(ICommonSession player, EntityUid mob, ref TutorialSessionData session)
    {
        if (session.GuideAutoOpened)
            return;

        if (session.GuideUid == EntityUid.Invalid || Deleted(session.GuideUid))
            return;

        _ui.OpenUi(session.GuideUid, TutorialPromptUiKey.Key, mob);
        session.GuideAutoOpened = true;
    }

    /// <summary>
    /// Spawns a humanoid with antag starting gear + optional survival loadout (no job/Passenger gear).
    /// </summary>
    private EntityUid SpawnAntagTutorialMob(
        EntityCoordinates coordinates,
        HumanoidCharacterProfile profile,
        ICommonSession player,
        TutorialRolePrototype roleProto)
    {
        var speciesId = profile.Species;
        if (!_protos.TryIndex<SpeciesPrototype>(speciesId, out var species))
            throw new ArgumentException($"Invalid species prototype was used: {speciesId}");

        var entity = Spawn(species.Prototype, coordinates);
        _visualBody.ApplyProfileTo(entity, profile);
        _humanoidProfile.ApplyProfileTo(entity, profile);
        _meta.SetEntityName(entity, profile.Name);

        if (profile.FlavorText != "" && _cfg.GetCVar(CCVars.FlavorText))
            EnsureComp<DetailExaminableComponent>(entity).Content = profile.FlavorText;

        _stationSpawning.EquipStartingGear(entity, roleProto.StartingGear, raiseEvent: false);

        var loadoutId = roleProto.RoleLoadout ?? new ProtoId<RoleLoadoutPrototype>("RoleSurvivalNukie");
        if (_protos.TryIndex(loadoutId, out RoleLoadoutPrototype? loadoutProto))
        {
            var loadout = new RoleLoadout(loadoutId);
            loadout.SetDefault(profile, player, _protos);
            _stationSpawning.EquipRoleLoadout(entity, loadout, loadoutProto);
        }

        var gearEquippedEv = new StartingGearEquippedEvent(entity);
        RaiseLocalEvent(entity, ref gearEquippedEv);
        _identity.QueueIdentityUpdate(entity);
        return entity;
    }

    private void SpawnPracticeEntities(
        TutorialRolePrototype roleProto,
        EntityUid gridUid,
        EntityCoordinates spawnCoords)
    {
        foreach (var spawn in roleProto.PracticeSpawns)
        {
            var coords = HasComp<TutorialRoomLayoutComponent>(gridUid)
                ? _tutorialRooms.GetChamberCoords(gridUid, spawn.Room, spawn.Offset)
                : spawnCoords.Offset(spawn.Offset);

            var ent = Spawn(spawn.Id, coords);
            EnsureComp<TutorialSensorTargetComponent>(ent);

            if (!string.IsNullOrEmpty(spawn.Marker))
            {
                var marker = EnsureComp<TutorialStepMarkerComponent>(ent);
                marker.MarkerId = spawn.Marker;
                Dirty(ent, marker);
            }

            if (spawn.AlwaysPowered)
                _power.SetNeedsPower(ent, false);

            if (TryComp<TutorialPracticeMobComponent>(ent, out var practiceMob) &&
                !practiceMob.SpawnDamageApplied &&
                practiceMob.SpawnDamage.AnyPositive())
            {
                _damageable.TryChangeDamage(ent, practiceMob.SpawnDamage, ignoreResistances: true, interruptsDoAfters: false);
                practiceMob.SpawnDamageApplied = true;
            }
        }

        SpawnChamberEntryPads(gridUid, roleProto);

        _tegBootstrap.TryConfigureOnGrid(gridUid);
        _researchBootstrap.TryConfigureOnGrid(gridUid);
        _cargoBootstrap.TryConfigureOnGrid(gridUid, roleProto);
        _commandBootstrap.TryConfigureOnGrid(gridUid, roleProto);
    }

    /// <summary>
    /// Spawns a glowing pad marker in each chamber after the first so chamber-entry steps can target them.
    /// </summary>
    private void SpawnChamberEntryPads(EntityUid gridUid, TutorialRolePrototype roleProto)
    {
        if (!TryComp<TutorialRoomLayoutComponent>(gridUid, out var layout))
            return;

        var existing = new HashSet<string>();
        foreach (var spawn in roleProto.PracticeSpawns)
        {
            if (!string.IsNullOrEmpty(spawn.Marker))
                existing.Add(spawn.Marker);
        }

        // Marker offset beyond ReachMarker auto-complete range (1.5) from chamber center spawn.
        var padOffset = new System.Numerics.Vector2(0f, -1.5f);
        for (var i = 1; i < layout.ChamberCenters.Count; i++)
        {
            var markerId = TutorialRoomLayoutComponent.ChamberEntryMarkerId(i);
            if (!existing.Add(markerId))
                continue;

            var coords = _tutorialRooms.GetChamberCoords(gridUid, i, padOffset);
            var ent = Spawn("TutorialStepMarker", coords);
            EnsureComp<TutorialSensorTargetComponent>(ent);
            var marker = EnsureComp<TutorialStepMarkerComponent>(ent);
            marker.MarkerId = markerId;
            Dirty(ent, marker);
        }
    }

    private static TutorialSubGoalData CreateChamberEntryPadSubGoal(int chamberIndex)
    {
        return new TutorialSubGoalData
        {
            Id = $"chamber-entry-{chamberIndex}",
            Text = "tutorial-server-chamber-pad",
            Hint = "tutorial-server-chamber-pad-hint",
            StuckHint = "tutorial-server-chamber-pad-stuck",
            Complete = TutorialStepComplete.ReachMarker,
            Marker = TutorialRoomLayoutComponent.ChamberEntryMarkerId(chamberIndex),
        };
    }

    /// <summary>
    /// Chamber this goal unlocks/walks into, if any.
    /// Prefers explicit <see cref="TutorialGoalData.EnterRoom"/>; falls back to legacy
    /// room-index == goal-index when practice spawns still use that convention.
    /// </summary>
    private static int? ResolveGoalEnterRoom(TutorialRolePrototype role, int goalIndex)
    {
        if (goalIndex < 0 || goalIndex >= role.Goals.Count)
            return null;

        var goal = role.Goals[goalIndex];
        if (goal.EnterRoom is { } explicitRoom)
            return explicitRoom;

        if (goalIndex > 0 && role.PracticeSpawns.Any(s => s.Room == goalIndex))
            return goalIndex;

        return null;
    }

    /// <summary>
    /// True when this goal sends the player into a new chamber that needs a glowing-pad check-in.
    /// </summary>
    private bool ShouldAwaitChamberEntryPad(TutorialSessionData session, TutorialRolePrototype role)
    {
        var goalIndex = session.GoalIndex;
        if (goalIndex <= 0 || goalIndex >= role.Goals.Count)
            return false;

        if (!TryComp<TutorialRoomLayoutComponent>(session.GridUid, out var layout))
            return false;

        var enterRoom = ResolveGoalEnterRoom(role, goalIndex);
        if (enterRoom is not { } chamberIndex || chamberIndex <= 0)
            return false;

        if (chamberIndex >= layout.ChamberCenters.Count)
            return false;

        // Only when this stage has practice content in that chamber.
        if (!role.PracticeSpawns.Any(s => s.Room == chamberIndex))
            return false;

        // Passenger-style pry gates teach door mechanics instead of walking through.
        var gateIdx = chamberIndex - 1;
        if (gateIdx >= 0 && gateIdx < layout.GateDoors.Count)
        {
            var gate = layout.GateDoors[gateIdx];
            if (Exists(gate) &&
                TryComp<TutorialGateDoorComponent>(gate, out var gateComp) &&
                gateComp.RequirePry)
            {
                return false;
            }
        }

        var goal = role.Goals[goalIndex];

        // Goal already guides the player onto a marker in this chamber.
        foreach (var sub in goal.SubGoals)
        {
            if (sub.Complete != TutorialStepComplete.ReachMarker || string.IsNullOrEmpty(sub.Marker))
                continue;

            if (role.PracticeSpawns.Any(s => s.Marker == sub.Marker && s.Room == chamberIndex))
                return false;
        }

        return true;
    }

    /// <summary>
    /// Resolves the active sub-goal for a tutorial participant (goals curriculum or legacy steps).
    /// </summary>
    public bool TryGetCurrentSubGoal(EntityUid mob, TutorialParticipantComponent part, out TutorialSubGoalData subGoal)
    {
        subGoal = default!;

        if (!TryGetActiveRule(out _, out var rule, out _))
            return false;

        if (!TryComp<ActorComponent>(mob, out var actor))
            return false;

        if (!rule.Sessions.TryGetValue(actor.PlayerSession.UserId, out var session) ||
            session.SelectedRoleId == null ||
            !ProtoMan.TryIndex<TutorialRolePrototype>(session.SelectedRoleId, out var role))
            return false;

        if (role.Goals.Count > 0)
        {
            if (session.GoalIndex < 0 || session.GoalIndex >= role.Goals.Count)
                return false;

            if (session.AwaitingChamberEntryPad)
            {
                var padRoom = ResolveGoalEnterRoom(role, session.GoalIndex) ?? session.GoalIndex;
                subGoal = CreateChamberEntryPadSubGoal(padRoom);
                return true;
            }

            var goal = role.Goals[session.GoalIndex];
            if (session.SubGoalIndex < 0 || session.SubGoalIndex >= goal.SubGoals.Count)
                return false;

            subGoal = goal.SubGoals[session.SubGoalIndex];
            return true;
        }

        if (session.StepIndex < 0 || session.StepIndex >= role.Steps.Count)
            return false;

        var step = role.Steps[session.StepIndex];
        subGoal = new TutorialSubGoalData
        {
            Id = step.Id,
            Text = step.Text,
            Hint = step.Hint,
            StuckHint = step.StuckHint,
            Complete = step.Complete,
            Tag = step.Tag,
            Entity = step.Entity,
            Marker = step.Marker,
        };
        return true;
    }

    /// <summary>
    /// Returns true when a closed-UI progress toast may be shown (and records the timestamp).
    /// </summary>
    public bool TryConsumeProgressPopup(ICommonSession player)
    {
        if (!TryGetActiveRule(out _, out var rule, out _))
            return false;

        if (!rule.Sessions.TryGetValue(player.UserId, out var session))
            return false;

        if (session.Completed || session.State != TutorialSessionState.InTutorial)
            return false;

        var now = _timing.CurTime;
        if (now - session.LastProgressPopup < ProgressPopupCooldown)
            return false;

        session.LastProgressPopup = now;
        rule.Sessions[player.UserId] = session;
        return true;
    }

    public void AdvanceSubGoal(EntityUid mob)
    {
        if (!_advancing.Add(mob))
            return;

        try
        {
            if (!TryComp<ActorComponent>(mob, out var actor))
                return;

            AdvanceSubGoal(actor.PlayerSession, mob);
        }
        finally
        {
            _advancing.Remove(mob);
        }
    }

    private void RefreshParticipantHud(EntityUid mob, TutorialRolePrototype role, TutorialSessionData session)
    {
        var part = EnsureComp<TutorialParticipantComponent>(mob);
        var oldGoalIndex = part.GoalIndex;
        var oldProgress = role.Goals.Count > 0 ? part.SubGoalIndex : part.StepIndex;
        part.SubGoalStates.Clear();

        if (role.Goals.Count > 0)
        {
            part.GoalCount = role.Goals.Count;
            part.GoalIndex = session.GoalIndex;
            part.StepCount = 0;
            part.StepIndex = 0;

            if (session.GoalIndex >= 0 && session.GoalIndex < role.Goals.Count)
            {
                var goal = role.Goals[session.GoalIndex];
                part.GoalTitle = Loc.GetString(goal.Title);

                var needsPad = ShouldAwaitChamberEntryPad(session, role);
                var padActive = session.AwaitingChamberEntryPad;
                var padOffset = needsPad ? 1 : 0;
                part.SubGoalCount = goal.SubGoals.Count + padOffset;
                part.SubGoalIndex = padActive ? 0 : session.SubGoalIndex + padOffset;

                if (needsPad)
                {
                    part.SubGoalStates.Add(new TutorialHudSubGoalState
                    {
                        Text = Loc.GetString("tutorial-server-chamber-pad"),
                        Completed = !padActive,
                    });
                }

                for (var i = 0; i < goal.SubGoals.Count; i++)
                {
                    part.SubGoalStates.Add(new TutorialHudSubGoalState
                    {
                        Text = Loc.GetString(goal.SubGoals[i].Text),
                        Completed = !padActive && i < session.SubGoalIndex,
                    });
                }

                if (padActive)
                {
                    var padRoom = ResolveGoalEnterRoom(role, session.GoalIndex) ?? session.GoalIndex;
                    var pad = CreateChamberEntryPadSubGoal(padRoom);
                    part.StepText = Loc.GetString(pad.Text);
                    part.StepComplete = pad.Complete;
                    part.HintText = Loc.GetString(pad.Hint!);
                    part.StuckHintText = Loc.GetString(pad.StuckHint!);
                }
                else if (session.SubGoalIndex >= 0 && session.SubGoalIndex < goal.SubGoals.Count)
                {
                    var sub = goal.SubGoals[session.SubGoalIndex];
                    part.StepText = Loc.GetString(sub.Text);
                    part.StepComplete = sub.Complete;
                    part.HintText = string.IsNullOrEmpty(sub.Hint) ? string.Empty : Loc.GetString(sub.Hint);
                    part.StuckHintText = string.IsNullOrEmpty(sub.StuckHint)
                        ? string.Empty
                        : Loc.GetString(sub.StuckHint);
                }
                else
                {
                    part.StepText = Loc.GetString("tutorial-server-complete");
                    part.StepComplete = TutorialStepComplete.Acknowledge;
                    part.HintText = string.Empty;
                    part.StuckHintText = string.Empty;
                }
            }
            else
            {
                part.GoalTitle = Loc.GetString("tutorial-server-complete");
                part.StepText = Loc.GetString("tutorial-server-complete");
                part.StepComplete = TutorialStepComplete.Acknowledge;
                part.HintText = string.Empty;
                part.StuckHintText = string.Empty;
                part.SubGoalCount = 0;
                part.SubGoalIndex = 0;
            }
        }
        else
        {
            part.GoalCount = 0;
            part.GoalIndex = 0;
            part.GoalTitle = string.Empty;
            part.SubGoalCount = 0;
            part.SubGoalIndex = 0;
            part.StepIndex = session.StepIndex;
            part.StepCount = role.Steps.Count;

            if (session.StepIndex >= 0 && session.StepIndex < role.Steps.Count)
            {
                var step = role.Steps[session.StepIndex];
                part.StepText = Loc.GetString(step.Text);
                part.StepComplete = step.Complete;
                part.HintText = string.IsNullOrEmpty(step.Hint) ? string.Empty : Loc.GetString(step.Hint);
                part.StuckHintText = string.IsNullOrEmpty(step.StuckHint)
                    ? string.Empty
                    : Loc.GetString(step.StuckHint);
            }
            else
            {
                part.StepText = Loc.GetString("tutorial-server-complete");
                part.StepComplete = TutorialStepComplete.Acknowledge;
                part.HintText = string.Empty;
                part.StuckHintText = string.Empty;
            }
        }

        Dirty(mob, part);

        if (session.GuideUid != EntityUid.Invalid && !Deleted(session.GuideUid))
        {
            var ev = new TutorialParticipantProgressChangedEvent(session.GuideUid, oldGoalIndex, oldProgress);
            RaiseLocalEvent(mob, ref ev);
        }
    }

    private void OnAcknowledgeStep(TutorialAcknowledgeStepEvent ev, EntitySessionEventArgs args)
    {
        if (args.SenderSession.AttachedEntity is not { } mob)
            return;

        if (!TryComp<TutorialParticipantComponent>(mob, out var part))
            return;

        if (part.StepComplete != TutorialStepComplete.Acknowledge)
            return;

        AdvanceSubGoal(args.SenderSession, mob);
    }

    private void AdvanceSubGoal(ICommonSession player, EntityUid mob)
    {
        if (!TryGetActiveRule(out _, out var rule, out _))
            return;

        if (!rule.Sessions.TryGetValue(player.UserId, out var session) ||
            session.SelectedRoleId == null ||
            !ProtoMan.TryIndex<TutorialRolePrototype>(session.SelectedRoleId, out var role))
            return;

        if (role.Goals.Count > 0)
        {
            if (session.GoalIndex < 0 || session.GoalIndex >= role.Goals.Count)
                return;

            if (session.AwaitingChamberEntryPad)
            {
                session.AwaitingChamberEntryPad = false;
                // Safe moment: player finished walking onto the chamber pad (not mid-UseInHand).
                if (!role.AutoOpenGuide && session.GoalIndex >= 1)
                    TryOpenDeferredGuide(player, mob, ref session);

                rule.Sessions[player.UserId] = session;
                RefreshParticipantHud(mob, role, session);
                return;
            }

            var goal = role.Goals[session.GoalIndex];
            session.SubGoalIndex++;

            if (session.SubGoalIndex >= goal.SubGoals.Count)
            {
                session.GoalIndex++;
                session.SubGoalIndex = 0;

                if (session.GoalIndex >= role.Goals.Count)
                {
                    session.Completed = true;
                    rule.Sessions[player.UserId] = session;
                    CompleteTutorial(player);
                    return;
                }

                // Open the door into the chamber this goal enters (if any).
                if (session.GridUid != EntityUid.Invalid &&
                    ResolveGoalEnterRoom(role, session.GoalIndex) is { } enterRoom &&
                    enterRoom > 0)
                {
                    _tutorialRooms.UnlockGatesForGoal(session.GridUid, enterRoom);
                }

                session.AwaitingChamberEntryPad = ShouldAwaitChamberEntryPad(session, role);

                // Do NOT open the deferred guide here. Passenger welcome ends on drink-water
                // (UseInHand); opening the Bound UI in that same AdvanceSubGoal steals focus
                // from the bottle. Open on chamber-pad check-in instead (see above), or leave
                // the tablet for the player when there is no pad (e.g. pry-exit Passenger).
            }

            rule.Sessions[player.UserId] = session;
            RefreshParticipantHud(mob, role, session);
            return;
        }

        session.StepIndex++;
        if (session.StepIndex >= role.Steps.Count)
        {
            session.Completed = true;
            rule.Sessions[player.UserId] = session;
            CompleteTutorial(player);
            return;
        }

        rule.Sessions[player.UserId] = session;
        RefreshParticipantHud(mob, role, session);
    }

    private void CompleteTutorial(ICommonSession player)
    {
        // Stay on the practice map with Choose a tutorial — do not force-respawn to the picker.
        _chat.DispatchServerMessage(player, Loc.GetString("tutorial-server-tutorial-finished"));
    }

    private void OnMobStateChanged(MobStateChangedEvent args)
    {
        if (args.NewMobState != MobState.Dead)
            return;

        if (!TryComp<ActorComponent>(args.Target, out var actor))
            return;

        if (!TryGetActiveRule(out _, out var rule, out _))
            return;

        if (!rule.Sessions.TryGetValue(actor.PlayerSession.UserId, out var session))
            return;

        if (session.State != TutorialSessionState.InTutorial || session.BodyUid != args.Target)
            return;

        // RespawnRule will rejoin; unload the private map. Mind leaves via respawn delete.
        EndTutorialSession(actor.PlayerSession.UserId, queueRespawn: false, unloadMap: true, deleteBody: false);
    }

    private void OnTutorialMindRemoved(Entity<TutorialParticipantComponent> ent, ref MindRemovedMessage args)
    {
        if (!TryGetActiveRule(out _, out var rule, out _))
            return;

        NetUserId? userId = null;
        foreach (var (id, session) in rule.Sessions)
        {
            if (session.State == TutorialSessionState.InTutorial && session.BodyUid == ent.Owner)
            {
                userId = id;
                break;
            }
        }

        if (userId == null)
            return;

        // Death keeps the RespawnDeadRule delay → MakeJoinGame → picker. Alive /ghost needs
        // an immediate picker, and must leave the private map before unload deletes the ghost.
        var bodyDead = TryComp<MobStateComponent>(ent, out var mobState) &&
                       mobState.CurrentState == MobState.Dead;

        if (!bodyDead && _players.TryGetSessionById(userId.Value, out var playerSession))
            GameTicker.JoinAsObserver(playerSession);

        EndTutorialSession(userId.Value, queueRespawn: false, unloadMap: true, deleteBody: !bodyDead);

        if (!bodyDead && _players.TryGetSessionById(userId.Value, out playerSession))
            TryOpenPickerForGhost(playerSession);
    }

    private void OnPlayerStatusChanged(object? sender, SessionStatusEventArgs e)
    {
        if (e.NewStatus != SessionStatus.Disconnected)
            return;

        EndTutorialSession(e.Session.UserId, queueRespawn: false, unloadMap: true, deleteBody: false);
        if (_openPickers.Remove(e.Session.UserId, out var eui))
            eui.Close();
    }

    /// <summary>
    /// Ordered teardown: mark exiting, optional body delete, unload map, clear session, optional respawn.
    /// </summary>
    public void EndTutorialSession(
        NetUserId userId,
        bool queueRespawn,
        bool unloadMap,
        bool deleteBody)
    {
        if (!TryGetActiveRule(out var ruleUid, out var rule, out var tracker))
            return;

        if (!rule.Sessions.TryGetValue(userId, out var session))
            return;

        session.State = TutorialSessionState.Exiting;
        var mapUid = session.MapUid;
        var bodyUid = session.BodyUid;

        var guideUid = session.GuideUid;
        session.MapUid = EntityUid.Invalid;
        session.GridUid = EntityUid.Invalid;
        session.BodyUid = EntityUid.Invalid;
        session.GuideUid = EntityUid.Invalid;
        session.SelectedRoleId = null;
        session.StepIndex = 0;
        session.GoalIndex = 0;
        session.SubGoalIndex = 0;
        session.Completed = false;
        session.GuideAutoOpened = false;
        session.PickerQuit = false;
        session.State = TutorialSessionState.PendingSelect;
        rule.Sessions[userId] = session;

        if (guideUid != EntityUid.Invalid && !TerminatingOrDeleted(guideUid))
            QueueDel(guideUid);

        if (deleteBody && bodyUid != EntityUid.Invalid && !TerminatingOrDeleted(bodyUid))
            QueueDel(bodyUid);

        if (unloadMap && mapUid != EntityUid.Invalid)
            _tutorialMaps.UnloadTutorialMap(mapUid);

        if (queueRespawn && _players.TryGetSessionById(userId, out var playerSession))
        {
            _respawn.AddToTracker(userId, (ruleUid, tracker));
            GameTicker.MakeJoinGame(playerSession, GetAnyStation(), silent: true);
        }
    }

    private EntityUid GetAnyStation()
    {
        foreach (var station in _station.GetStations())
            return station;
        return EntityUid.Invalid;
    }
}
