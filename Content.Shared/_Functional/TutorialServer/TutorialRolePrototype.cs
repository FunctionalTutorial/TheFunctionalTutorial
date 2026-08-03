using System.Numerics;
using Content.Shared.AlertLevel;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.FixedPoint;
using Content.Shared.Preferences.Loadouts;
using Content.Shared.Research.Prototypes;
using Content.Shared.Roles;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Shared._Functional.TutorialServer;

/// <summary>
/// Defines a selectable tutorial package for the Functional Tutorial Server.
/// </summary>
[Prototype] //Wizden: drop redundant type (RA0042)
public sealed partial class TutorialRolePrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    /// <summary>
    /// Crew job to outfit as, if any.
    /// </summary>
    [DataField]
    public ProtoId<JobPrototype>? Job;

    /// <summary>
    /// Antagonist package id (matches <see cref="AntagPrototype"/>), if any.
    /// </summary>
    [DataField]
    public ProtoId<AntagPrototype>? Antag;

    /// <summary>
    /// When true, picker shows the role greyed with an incomplete marker.
    /// </summary>
    [DataField]
    public bool Stub = true;

    /// <summary>
    /// When true, after the private map loads: force APC receivers to not need power and
    /// freeze grid atmospherics (fill-once, no LINDA/pipe/device ticks). Leave false for
    /// engineering/cargo tutorials that teach live power, spacing, or EVA.
    /// </summary>
    [DataField]
    public bool SimplifiedEnvironment;

    /// <summary>
    /// Map/grid path to load when <see cref="Room"/> is unset (stubs / legacy).
    /// </summary>
    [DataField]
    public ResPath Map = new("/Maps/_Functional/TutorialServer/StubPractice.yml");

    /// <summary>
    /// When set, loads/builds a single-room template and stamps N identical copies
    /// (goal-driven) with gated doors between them. Takes priority over <see cref="Room"/>.
    /// </summary>
    [DataField]
    public ProtoId<TutorialRoomTemplatePrototype>? RoomTemplate;

    /// <summary>
    /// Last-resort procedural room style when <see cref="RoomTemplate"/> is unset
    /// (or its map crop is missing). Builds one chamber then stamps copies the same way.
    /// </summary>
    [DataField]
    public ProtoId<TutorialRoomPrototype>? Room;

    /// <summary>
    /// When set, builds a shuttle + dock platform arena (takes priority over <see cref="Room"/>).
    /// </summary>
    [DataField]
    public ProtoId<TutorialShuttleArenaPrototype>? ShuttleArena;

    /// <summary>
    /// When set, builds a salvage bay + debris arena (takes priority over <see cref="Room"/>).
    /// </summary>
    [DataField]
    public ProtoId<TutorialSalvageArenaPrototype>? SalvageArena;

    /// <summary>
    /// When true, builds the floating Syndicate outpost spawn lounge + chem lab fragment
    /// (takes priority over <see cref="Room"/> / <see cref="Map"/>, after shuttle/salvage arenas).
    /// </summary>
    [DataField]
    public bool NukeopsOutpost;

    /// <summary>
    /// When set, spawns this entity prototype as the player body instead of a humanoid job spawn
    /// (e.g. <c>XenoborgEngi</c>, <c>MothershipCore</c>).
    /// </summary>
    [DataField]
    public EntProtoId? SpawnEntity;

    /// <summary>
    /// Optional antag/job starting gear equipped after a gearless humanoid spawn.
    /// When set, Passenger/job loadouts are skipped in favor of this gear plus <see cref="RoleLoadout"/>.
    /// </summary>
    [DataField]
    public ProtoId<StartingGearPrototype>? StartingGear;

    /// <summary>
    /// Optional role loadout applied with <see cref="StartingGear"/> (e.g. <c>RoleSurvivalNukie</c>).
    /// </summary>
    [DataField]
    public ProtoId<RoleLoadoutPrototype>? RoleLoadout;

    /// <summary>
    /// Optional guidebook entry id for cross-linking.
    /// </summary>
    [DataField]
    public string? Guidebook;

    /// <summary>
    /// Plain-text objective prototype ids to add for Character UI (e.g. Traitor placeholders).
    /// </summary>
    [DataField]
    public List<EntProtoId> PlaceholderObjectives = new();

    /// <summary>
    /// Legacy flat steps (used when <see cref="Goals"/> is empty, mainly stubs).
    /// </summary>
    [DataField]
    public List<TutorialStepData> Steps = new();

    /// <summary>
    /// Multi-goal curriculum. When non-empty, replaces <see cref="Steps"/>.
    /// </summary>
    [DataField]
    public List<TutorialGoalData> Goals = new();

    /// <summary>
    /// Entities spawned on the private map after load (vendors, machines, props, markers).
    /// </summary>
    [DataField]
    public List<TutorialPracticeSpawn> PracticeSpawns = new();

    /// <summary>
    /// Optional offset from the chamber / zone-origin spawn point for the player body.
    /// Use when the crop center is outside the practice room (e.g. Command crop centers on Cap).
    /// </summary>
    [DataField]
    public Vector2 SpawnOffset;

    /// <summary>
    /// Display name override locale id. Falls back to job/antag name.
    /// </summary>
    [DataField]
    public string? Name;

    /// <summary>
    /// Department grouping key for the picker UI.
    /// </summary>
    [DataField]
    public string Category = "Misc";

    /// <summary>
    /// Optional indented sub-heading under <see cref="Category"/> (e.g. BPL14 / Starlight).
    /// </summary>
    [DataField]
    public string? SubCategory;

    /// <summary>
    /// When true, the tutorial guide Bound UI opens as soon as the tablet is given.
    /// When false, open is deferred until chamber-pad check-in after the opening goal
    /// (never synchronously from a UseInHand that ends that goal — e.g. Passenger drink).
    /// </summary>
    [DataField]
    public bool AutoOpenGuide = true;
}

[DataDefinition]
public sealed partial class TutorialGoalData
{
    [DataField(required: true)]
    public string Id = string.Empty;

    /// <summary>
    /// Locale id for the goal title shown in the HUD.
    /// </summary>
    [DataField(required: true)]
    public string Title = string.Empty;

    /// <summary>
    /// When set, advancing into this goal unlocks the gate into that chamber and may
    /// inject a glowing-pad check-in. Prefer keeping early goals in chamber 0 and only
    /// setting this when a new room is actually required (hazard isolation, pry exit).
    /// </summary>
    [DataField]
    public int? EnterRoom;

    [DataField(required: true)]
    public List<TutorialSubGoalData> SubGoals = new();
}

[DataDefinition]
public sealed partial class TutorialSubGoalData
{
    [DataField(required: true)]
    public string Id = string.Empty;

    /// <summary>
    /// Locale id for sub-goal prompt text.
    /// </summary>
    [DataField(required: true)]
    public string Text = string.Empty;

    /// <summary>
    /// Optional locale id for a short actionable hint while waiting on a sensor.
    /// </summary>
    [DataField]
    public string? Hint;

    /// <summary>
    /// Optional locale id for an extra stuck tip shown via the prompt Hint button.
    /// </summary>
    [DataField]
    public string? StuckHint;

    [DataField]
    public TutorialStepComplete Complete = TutorialStepComplete.Acknowledge;

    /// <summary>
    /// Tag used for InteractTag / InteractTargetTag / HoldTag.
    /// </summary>
    [DataField]
    public string? Tag;

    /// <summary>
    /// Entity prototype for HoldItem / ObtainItem / UseInHand / HasAction /
    /// ActionUsed / MapHasEntity matching.
    /// </summary>
    [DataField]
    public EntProtoId? Entity;

    /// <summary>
    /// Marker id for ReachMarker (matches <see cref="TutorialStepMarkerComponent.MarkerId"/>),
    /// or dock-station id for DockShuttle / UndockShuttle (matches <see cref="TutorialDockStationComponent.StationId"/>),
    /// or puddle marker id for <see cref="TutorialStepComplete.PuddleCleared"/>.
    /// </summary>
    [DataField]
    public string? Marker;

    /// <summary>
    /// Reagent prototype for <see cref="TutorialStepComplete.SolutionContains"/>.
    /// </summary>
    [DataField]
    public ProtoId<ReagentPrototype>? Reagent;

    /// <summary>
    /// Minimum reagent units for <see cref="TutorialStepComplete.SolutionContains"/> (default 1).
    /// </summary>
    [DataField]
    public FixedPoint2 MinAmount = 1;

    /// <summary>
    /// Maximum total damage for <see cref="TutorialStepComplete.PracticeMobDamageBelow"/> (default 0).
    /// </summary>
    [DataField]
    public float MaxDamage;

    /// <summary>
    /// Minimum count of matching entities for <see cref="TutorialStepComplete.MapHasEntity"/>
    /// or items inside a tagged container for <see cref="TutorialStepComplete.ContainerHasEntityCount"/> (default 1).
    /// </summary>
    [DataField]
    public int MinCount = 1;

    /// <summary>
    /// Job prototype for <see cref="TutorialStepComplete.IdCardHasJob"/>.
    /// </summary>
    [DataField]
    public ProtoId<JobPrototype>? Job;

    /// <summary>
    /// Technology prototype for <see cref="TutorialStepComplete.ResearchUnlocked"/>.
    /// </summary>
    [DataField]
    public ProtoId<TechnologyPrototype>? Technology;

    /// <summary>
    /// Alert level prototype id for <see cref="TutorialStepComplete.AlertLevelChanged"/>
    /// (e.g. <c>blue</c>). Defaults to blue when unset.
    /// </summary>
    [DataField]
    public ProtoId<AlertLevelPrototype>? AlertLevel;
}

/// <summary>
/// Legacy flat step (stubs / backward compatibility).
/// </summary>
[DataDefinition]
public sealed partial class TutorialStepData
{
    [DataField(required: true)]
    public string Id = string.Empty;

    [DataField(required: true)]
    public string Text = string.Empty;

    /// <summary>
    /// Optional locale id for a short actionable hint while waiting on a sensor.
    /// </summary>
    [DataField]
    public string? Hint;

    /// <summary>
    /// Optional locale id for an extra stuck tip shown via the prompt Hint button.
    /// </summary>
    [DataField]
    public string? StuckHint;

    [DataField]
    public TutorialStepComplete Complete = TutorialStepComplete.Acknowledge;

    [DataField]
    public string? Tag;

    [DataField]
    public EntProtoId? Entity;

    [DataField]
    public string? Marker;
}

[DataDefinition]
public sealed partial class TutorialPracticeSpawn
{
    [DataField(required: true)]
    public EntProtoId Id;

    /// <summary>
    /// Offset from the chamber center (see <see cref="Room"/>).
    /// </summary>
    [DataField]
    public Vector2 Offset = Vector2.Zero;

    /// <summary>
    /// Which chamber to place this entity in (0 = spawn / first goal room).
    /// Out-of-range values clamp to the last built chamber.
    /// </summary>
    [DataField]
    public int Room;

    /// <summary>
    /// If set, attaches a step marker with this id after spawning.
    /// </summary>
    [DataField]
    public string? Marker;

    /// <summary>
    /// Force ApcPowerReceiver.NeedsPower = false so machines work on isolated grids.
    /// </summary>
    [DataField]
    public bool AlwaysPowered = true;
}

public enum TutorialStepComplete : byte
{
    /// <summary>Player presses Continue on the HUD.</summary>
    Acknowledge,

    /// <summary>Player collides with / reaches a marker entity.</summary>
    ReachMarker,

    /// <summary>Player interacts using a held item that has <see cref="TutorialSubGoalData.Tag"/>.</summary>
    InteractTag,

    /// <summary>Player interacts with a world target that has <see cref="TutorialSubGoalData.Tag"/>.</summary>
    InteractTargetTag,

    /// <summary>Player holds an item matching <see cref="TutorialSubGoalData.Entity"/>.</summary>
    HoldItem,

    /// <summary>Player holds an item with <see cref="TutorialSubGoalData.Tag"/>.</summary>
    HoldTag,

    /// <summary>Player has the entity in hands or inventory.</summary>
    ObtainItem,

    /// <summary>Player uses the matching held item in-hand.</summary>
    UseInHand,

    /// <summary>
    /// Player dropped a matching <see cref="TutorialSubGoalData.Entity"/> to the world
    /// (not stowed into inventory).
    /// </summary>
    DropItem,

    /// <summary>
    /// Player has the entity in an inventory/storage slot (not currently held in hands).
    /// </summary>
    StowItem,

    /// <summary>Player is piloting a shuttle (has PilotComponent).</summary>
    PilotShuttle,

    /// <summary>Player is providing shuttle throttle / strafe / rotate input while piloting.</summary>
    ShuttleThrottle,

    /// <summary>Player's grid undocks from another grid.</summary>
    UndockShuttle,

    /// <summary>Player's grid docks to another grid.</summary>
    DockShuttle,

    /// <summary>Player spawned a tutorial anomaly via the spawn pad.</summary>
    SpawnAnomaly,

    /// <summary>Player scanned an anomaly (held/inventory scanner has ScannedAnomaly set).</summary>
    ScanAnomaly,

    /// <summary>Scanned anomaly stability is at or below the tutorial stabilize threshold.</summary>
    StabilizeAnomaly,

    /// <summary>Anomaly on the player's map shut down without going supercritical.</summary>
    RemoveAnomaly,

    /// <summary>Held/inventory solution contains <see cref="TutorialSubGoalData.Reagent"/> ≥ MinAmount.</summary>
    SolutionContains,

    /// <summary>Practice puddle with matching MarkerId is gone or empty on the player's map.</summary>
    PuddleCleared,

    /// <summary>Player cuffed a <see cref="TutorialPracticeMobComponent"/> on their map.</summary>
    PracticeMobCuffed,

    /// <summary>Practice mob total damage is at or below MaxDamage.</summary>
    PracticeMobDamageBelow,

    /// <summary>AME controller on the map has fuel and is injecting.</summary>
    AmeInjecting,

    /// <summary>Player harvested produce from a tutorial hydro tray (optionally matching Entity).</summary>
    HydroHarvest,

    /// <summary>
    /// At least <see cref="TutorialSubGoalData.MinCount"/> entities matching
    /// <see cref="TutorialSubGoalData.Entity"/> exist on the player's map
    /// (used for placed cables, built SMES, etc.).
    /// </summary>
    MapHasEntity,

    /// <summary>
    /// A tagged practice entity on the map has its wires panel open
    /// (<see cref="TutorialSubGoalData.Tag"/>).
    /// </summary>
    WiresPanelOpen,

    /// <summary>A practice mob on the map was hit by a cream pie.</summary>
    PracticeMobCreamPied,

    /// <summary>
    /// A practice mob on the map is buckled to a rollerbed tagged
    /// <c>TutorialRollerBed</c>.
    /// </summary>
    PracticeMobBuckled,

    /// <summary>
    /// Player opened the Starlight surgery Bound UI on a tutorial patient.
    /// </summary>
    StarlightSurgeryUiOpened,

    /// <summary>
    /// A tutorial Starlight surgery patient on the map has an implanted eye cybernetic.
    /// </summary>
    StarlightSurgeryEyeImplanted,

    /// <summary>
    /// Player opened the CyberMed analyzer Bound UI on a tutorial patient.
    /// </summary>
    CyberMedSurgeryUiOpened,

    /// <summary>
    /// A tutorial BPL CyberMed surgery patient finished the example implant + close path.
    /// </summary>
    CyberMedSurgeryComplete,

    /// <summary>
    /// An ID card on the player's map has <see cref="TutorialSubGoalData.Job"/> written.
    /// </summary>
    IdCardHasJob,

    /// <summary>
    /// A tagged storage/locker on the map contains at least <see cref="TutorialSubGoalData.MinCount"/> items.
    /// </summary>
    ContainerHasEntityCount,

    /// <summary>
    /// Player fed an item into a tagged tutorial recycler on their map.
    /// </summary>
    RecyclerProcessed,

    /// <summary>A practice mob on the map was slipped (soap/peel).</summary>
    PracticeMobSlipped,

    /// <summary>
    /// At least <see cref="TutorialSubGoalData.MinCount"/> entities were sold via cargo pallet sale
    /// on the player's map.
    /// </summary>
    CargoSold,

    /// <summary>
    /// A thermo-electric generator on the player's map has <c>LastGeneration &gt; 0</c>.
    /// Curriculum should require a prior TEG interact sub-goal so this cannot idle-complete.
    /// </summary>
    TegProducingPower,

    /// <summary>
    /// A technology database on the player's map has unlocked
    /// <see cref="TutorialSubGoalData.Technology"/>.
    /// </summary>
    ResearchUnlocked,

    /// <summary>
    /// A tagged tutorial lathe on the player's map started printing a recipe whose result
    /// matches <see cref="TutorialSubGoalData.Entity"/>.
    /// </summary>
    LathePrinted,

    /// <summary>
    /// A nuclear bomb on the player's map is in the armed state.
    /// </summary>
    NukeArmed,

    /// <summary>
    /// Player successfully completed a tutorial war declaration (WarReady).
    /// </summary>
    WarDeclared,

    /// <summary>
    /// Player entity has <c>ZombieComponent</c> (e.g. after Turn Undead).
    /// </summary>
    PlayerIsZombie,

    /// <summary>
    /// At least <see cref="TutorialSubGoalData.MinCount"/> practice mobs on the map have
    /// <c>PendingZombieComponent</c> or <c>ZombieComponent</c>.
    /// </summary>
    PracticeMobInfected,

    /// <summary>
    /// At least <see cref="TutorialSubGoalData.MinCount"/> practice mobs on the map have
    /// <c>RevolutionaryComponent</c> (converted by a Head Revolutionary flash).
    /// </summary>
    PracticeMobConverted,

    /// <summary>
    /// The player's own wires panel is open (used for cyborg maintenance / emag setup).
    /// </summary>
    PlayerWiresPanelOpen,

    /// <summary>
    /// The player's <c>SiliconLawProviderComponent.Subverted</c> is true (emagged / ion-stormed).
    /// </summary>
    SiliconSubverted,

    /// <summary>
    /// A practice mob on the map is dead (<c>MobState.Dead</c>).
    /// </summary>
    PracticeMobDead,

    /// <summary>
    /// Player successfully finished a changeling devour (<c>ChangelingDevouredEvent</c>).
    /// </summary>
    ChangelingDevoured,

    /// <summary>
    /// Player used Extract DNA sting on a target.
    /// </summary>
    ChangelingStung,

    /// <summary>
    /// Player owns an action matching <see cref="TutorialSubGoalData.Entity"/> (e.g. ArmBlade).
    /// </summary>
    HasAction,

    /// <summary>
    /// Vampire <c>TotalBlood</c> is at least <see cref="TutorialSubGoalData.MinCount"/>.
    /// </summary>
    VampireBloodAbove,

    /// <summary>
    /// Vampire has chosen a class path (<c>ChosenClassId</c> set).
    /// </summary>
    VampireClassChosen,

    /// <summary>
    /// Vampire fangs are extended.
    /// </summary>
    VampireFangsExtended,

    /// <summary>
    /// A cargo order was approved on the player's station (approve console path).
    /// </summary>
    CargoOrderApproved,

    /// <summary>
    /// A bounty-labeled crate was sold and fulfilled on the player's map.
    /// </summary>
    CargoBountyFulfilled,

    /// <summary>
    /// A practice mob on the map was stunned/knocked down (or stun tool InteractUsing).
    /// </summary>
    PracticeMobStunned,

    /// <summary>
    /// A tagged tutorial brig timer on the map has an active signal timer.
    /// </summary>
    BrigTimerStarted,

    /// <summary>
    /// Participant spent Telecrystal (or bought something) on their PDA/implant uplink store.
    /// </summary>
    StorePurchased,

    /// <summary>
    /// Station alert level changed to <see cref="TutorialSubGoalData.AlertLevel"/> (default blue).
    /// </summary>
    AlertLevelChanged,

    /// <summary>
    /// A thieving beacon on the player's map is linked (StealArea OwnerCount &gt; 0).
    /// Unfolding the beacon as a thief auto-links it to their mind.
    /// </summary>
    ThiefBeaconLinked,

    /// <summary>
    /// Player successfully used an action matching <see cref="TutorialSubGoalData.Entity"/>
    /// (fires after the action event is handled).
    /// </summary>
    ActionUsed,

    /// <summary>
    /// Player finished devouring a humanoid (Devour do-after completed on a
    /// <c>HumanoidProfile</c> target — grants Ichor healing for space dragons).
    /// </summary>
    DragonDevoured,

    /// <summary>
    /// Player selected a cyborg chassis type. When <see cref="TutorialSubGoalData.Marker"/>
    /// is set, it must match the selected <c>borgType</c> prototype id (e.g. <c>generic</c>).
    /// </summary>
    BorgTypeSelected,

    /// <summary>
    /// Player's active borg module matches <see cref="TutorialSubGoalData.Entity"/>
    /// (must differ from the chassis's initially auto-selected module — use after a tip).
    /// </summary>
    BorgModuleSelected,
}
