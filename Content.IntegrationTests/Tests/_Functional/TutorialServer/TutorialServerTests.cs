using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.IntegrationTests.Fixtures;
using Content.Server._Functional.TutorialServer;
using Content.Server.GameTicking;
using Content.Server.GameTicking.Rules.Components;
using Content.Server.Ghost;
using Content.Server.Medical;
using Content.Server.Nuke;
using Content.Server.Vocalization.Components;
using Content.Server._Functional.TutorialServer.CyberMedSurgery;
using Content.Server._Functional.TutorialServer.StarlightSurgery;
using Content.Shared._Functional.TutorialServer;
using Content.Shared._Functional.TutorialServer.CyberMedSurgery;
using Content.Shared._Functional.TutorialServer.StarlightSurgery;
using Content.Shared.Actions;
using Content.Shared.Actions.Components;
using Content.Shared.Anomaly;
using Content.Shared.Anomaly.Components;
using Content.Server.Power.Components;
using Content.Shared.Atmos.Components;
using Content.Shared.CCVar;
using Content.Shared.Cuffs.Components;
using Content.Shared.Damage;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Damage.Systems;
using Content.Shared.FixedPoint;
using Content.Shared.Fluids.Components;
using Content.Shared.GameTicking;
using Content.Shared.Ghost;
using Content.Shared.Gravity;
using Content.Shared.Guidebook;
using Content.Shared.Hands.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Humanoid.Prototypes;
using Content.Shared.Interaction;
using Content.Shared.Interaction.Components;
using Content.Shared.Interaction.Events;
using Content.Shared.Inventory;
using Content.Shared.Item;
using Content.Shared.Item.ItemToggle;
using Content.Shared.Medical;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Maps;
using Content.Shared.Mind;
using Content.Shared.Objectives.Systems;
using Content.Shared.Nutrition.EntitySystems;
using Content.Shared.Paper;
using Content.Shared.Physics;
using Content.Shared.Preferences.Loadouts;
using Content.Shared.Random;
using Content.Shared.Clothing.Components;
using Content.Shared.Roles;
using Content.Shared.Silicons.Borgs;
using Content.Shared.Silicons.Borgs.Components;
using Content.Shared.Storage;
using Content.Shared.Tag;
using Content.Server.Silicons.Borgs;
using Content.Shared.UserInterface;
using Content.Server.Shuttles.Components;
using Content.Server.Shuttles.Systems;
using Robust.Server.GameObjects;
using Robust.Shared.Configuration;
using Robust.Shared.Console;
using Robust.Shared.ContentPack;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Localization;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.IntegrationTests.Tests._Functional.TutorialServer;

[TestFixture]
[TestOf(typeof(TutorialServerRuleSystem))]
public sealed class TutorialServerTests : GameTest
{
    public override PoolSettings PoolSettings => new()
    {
        Dirty = true,
        InLobby = true,
        DummyTicker = false,
        Connected = true,
    };

    private static readonly EntProtoId TutorialRule = "TutorialServer";

    [Test]
    public async Task TutorialPreset_AllowsMidRoundJoinAndRespawn()
    {
        var pair = Pair;
        var server = pair.Server;
        var ticker = server.System<GameTicker>();
        var cfg = server.ResolveDependency<IConfigurationManager>();

        await server.WaitPost(() =>
        {
            ticker.SetGamePreset("TutorialServer");
            ticker.StartGameRule(TutorialRule, out _);
            ticker.StartRound();
        });

        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            Assert.That(ticker.RunLevel, Is.EqualTo(GameRunLevel.InRound));
            Assert.That(server.EntMan.Count<TutorialServerRuleComponent>(), Is.GreaterThan(0));
            Assert.That(server.EntMan.Count<RespawnTrackerComponent>(), Is.GreaterThan(0));
            Assert.That(cfg.GetCVar(CCVars.GameDisallowLateJoins), Is.False);
            Assert.That(cfg.GetCVar(CCVars.GameRoleTimers), Is.False);
            Assert.That(cfg.GetCVar(CCVars.GameRoleWhitelist), Is.False);
            Assert.That(cfg.GetCVar(CCVars.OocEnabled), Is.False);
            Assert.That(cfg.GetCVar(CCVars.LoocEnabled), Is.False);
            Assert.That(cfg.GetCVar(CCVars.DeadChatEnabled), Is.False);
            Assert.That(cfg.GetCVar(CCVars.OocEnableDuringRound), Is.True,
                "ooc.enable_during_round must be true so ChatSystem does not turn OOC back on at round end");
            Assert.That(cfg.GetCVar(CCVars.EmergencyShuttleAutoCallTime), Is.EqualTo(0),
                "TutorialServer must disable emergency shuttle auto-call (lobby has no evac shuttle)");
            Assert.That(server.EntMan.Count<Content.Server.Shuttles.Components.StationCentcommComponent>(), Is.EqualTo(0),
                "TutorialServer must not spawn CentComm");
        });
    }

    [Test]
    public async Task TutorialServerRules_DocumentContainsHostRules()
    {
        var pair = Pair;
        var client = pair.Client;
        await client.WaitIdleAsync();
        var protoMan = client.ResolveDependency<IPrototypeManager>();
        var resMan = client.ResolveDependency<IResourceManager>();

        await client.WaitAssertion(() =>
        {
            Assert.That(protoMan.TryIndex<GuideEntryPrototype>("TutorialServerRules", out var proto), Is.True);
            Assert.That(proto!.RuleEntry, Is.True);

            using var reader = resMan.ContentFileReadText(proto.Text);
            var text = reader.ReadToEnd();
            Assert.That(text, Does.Contain("Please do not intentionally try to crash the server."));
            Assert.That(text, Does.Contain("The intent of this server is to provide a way for people to learn to play the game."));
            Assert.That(text, Does.Contain("When joining other servers please read their rules carefully, as each server has different expectations of their players."));
        });
    }

    [Test]
    public async Task TutorialLobbyMap_UsesLobbyStationWithCrewTutorials()
    {
        var pair = Pair;
        var server = pair.Server;
        var protos = server.ProtoMan;
        var tutorial = server.System<TutorialServerRuleSystem>();

        await server.WaitAssertion(() =>
        {
            Assert.That(protos.TryIndex<GameMapPrototype>("TutorialLobby", out var lobby), Is.True);
            var station = lobby!.Stations.Values.Single();
            Assert.That(station.StationPrototype.Id, Is.EqualTo("TutorialLobbyStation"));

            // Crew latejoin is wired through station jobs on TutorialLobby; Dev is Captain-only.
            Assert.That(protos.TryIndex<GameMapPrototype>("Dev", out var dev), Is.True);
            Assert.That(dev!.Stations.Values.Single().StationPrototype.Id,
                Is.EqualTo("StandardNanotrasenStation"));

            Assert.Multiple(() =>
            {
                Assert.That(tutorial.TryResolveTutorialRoleForJob("TechnicalAssistant", out var ta), Is.True);
                Assert.That(ta!.ID, Is.EqualTo("TutorialTechnicalAssistant"));
                Assert.That(tutorial.TryResolveTutorialRoleForJob("Passenger", out var passenger), Is.True);
                Assert.That(passenger!.ID, Is.EqualTo("TutorialPassenger"));
                Assert.That(tutorial.TryResolveTutorialRoleForJob("MedicalDoctor", out var md), Is.True);
                Assert.That(md!.ID, Is.EqualTo("TutorialMedicalDoctor"));
            });
        });
    }

    [Test]
    public async Task TutorialPreset_LateJoinJobStartsMatchingTutorial()
    {
        var pair = Pair;
        var server = pair.Server;
        var ticker = server.System<GameTicker>();
        var tutorial = server.System<TutorialServerRuleSystem>();
        var cfg = server.ResolveDependency<IConfigurationManager>();

        await server.WaitPost(() =>
        {
            ticker.SetGamePreset("TutorialServer");
            ticker.StartGameRule(TutorialRule, out _);
            ticker.StartRound();
        });
        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            Assert.That(cfg.GetCVar(CCVars.GameRoleTimers), Is.False);
            Assert.That(tutorial.TryResolveTutorialRoleForJob("Captain", out var captain));
            Assert.That(captain!.ID, Is.EqualTo("TutorialCaptain"));
            Assert.That(tutorial.TryResolveTutorialRoleForJob("HeadOfSecurity", out var hos));
            Assert.That(hos!.ID, Is.EqualTo("TutorialHeadOfSecurity"));
            Assert.That(tutorial.TryResolveTutorialRoleForJob("TechnicalAssistant", out var ta));
            Assert.That(ta!.ID, Is.EqualTo("TutorialTechnicalAssistant"));
        });

        await server.WaitPost(() =>
        {
            ticker.MakeJoinGame(pair.Player!, EntityUid.Invalid, "Captain", silent: true);
        });
        await pair.RunTicksSync(60);

        await server.WaitAssertion(() =>
        {
            var player = pair.Player!;
            Assert.That(player.AttachedEntity, Is.Not.Null);
            Assert.That(server.EntMan.HasComponent<TutorialParticipantComponent>(player.AttachedEntity!.Value), Is.True,
                "Late-joining as Captain should spawn into the Captain tutorial");
            Assert.That(tutorial.IsPickerOpen(player), Is.False);

            TutorialSessionData? session = null;
            var ruleQuery = server.EntMan.EntityQueryEnumerator<TutorialServerRuleComponent>();
            while (ruleQuery.MoveNext(out _, out var ruleComp))
            {
                if (ruleComp.Sessions.TryGetValue(player.UserId, out session))
                    break;
            }

            Assert.That(session, Is.Not.Null);
            Assert.That(session!.SelectedRoleId, Is.EqualTo("TutorialCaptain"));
            Assert.That(session.State, Is.EqualTo(TutorialSessionState.InTutorial));
        });
    }

    [Test]
    public async Task TutorialServer_BlocksGhostDeadChat()
    {
        var pair = Pair;
        var server = pair.Server;
        var ticker = server.System<GameTicker>();
        var cfg = server.ResolveDependency<IConfigurationManager>();

        await server.WaitPost(() =>
        {
            ticker.SetGamePreset("TutorialServer");
            ticker.StartGameRule(TutorialRule, out _);
            ticker.StartRound();
        });
        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            Assert.That(cfg.GetCVar(CCVars.DeadChatEnabled), Is.False);

            // Even if the CVar were flipped back on, isolation must still cancel dead chat.
            cfg.SetCVar(CCVars.DeadChatEnabled, true);

            var session = pair.Client.Session!;
            var ev = new Content.Shared.Chat.InGameOocMessageAttemptEvent(
                session,
                Content.Shared.Chat.InGameOOCChatType.Dead);
            server.EntMan.EventBus.RaiseEvent(EventSource.Local, ref ev);
            Assert.That(ev.Cancelled, Is.True, "Tutorial isolation must cancel ghost dead chat");

            cfg.SetCVar(CCVars.DeadChatEnabled, false);
        });
    }

    [Test]
    public async Task TutorialServer_BlocksGhostRoles()
    {
        var pair = Pair;
        var server = pair.Server;
        var ticker = server.System<GameTicker>();
        var cfg = server.ResolveDependency<IConfigurationManager>();
        var ghostRoles = server.System<Content.Server.Ghost.Roles.GhostRoleSystem>();

        await server.WaitPost(() =>
        {
            ticker.SetGamePreset("TutorialServer");
            ticker.StartGameRule(TutorialRule, out _);
            ticker.StartRound();
        });
        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            Assert.That(cfg.GetCVar(TutorialCVars.GhostRolesEnabled), Is.False,
                "TutorialServer must disable ghost role takeovers");
            Assert.That(ghostRoles.GetGhostRoleCount(), Is.EqualTo(0));
            Assert.That(ghostRoles.GetGhostRolesInfo(pair.Player), Is.Empty);

            // Even with a registered role id, Request/Takeover must no-op while disabled.
            Assert.That(ghostRoles.Takeover(pair.Player!, 0), Is.False);
        });
    }

    [Test]
    public async Task TutorialServer_BlocksVotes()
    {
        var pair = Pair;
        var server = pair.Server;
        var ticker = server.System<GameTicker>();
        var cfg = server.ResolveDependency<IConfigurationManager>();
        var votes = server.ResolveDependency<Content.Server.Voting.Managers.IVoteManager>();

        await server.WaitPost(() =>
        {
            ticker.SetGamePreset("TutorialServer");
            ticker.StartGameRule(TutorialRule, out _);
            ticker.StartRound();
        });
        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            Assert.That(cfg.GetCVar(CCVars.VoteEnabled), Is.False,
                "TutorialServer must disable player votes");
            Assert.That(votes.CanCallVote(pair.Player!, Content.Shared.Voting.StandardVoteType.Preset), Is.False);
        });
    }

    [Test]
    public async Task TutorialServer_BlocksLooc()
    {
        var pair = Pair;
        var server = pair.Server;
        var ticker = server.System<GameTicker>();
        var cfg = server.ResolveDependency<IConfigurationManager>();

        await server.WaitPost(() =>
        {
            ticker.SetGamePreset("TutorialServer");
            ticker.StartGameRule(TutorialRule, out _);
            ticker.StartRound();
        });
        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            Assert.That(cfg.GetCVar(CCVars.LoocEnabled), Is.False);

            cfg.SetCVar(CCVars.LoocEnabled, true);

            var session = pair.Client.Session!;
            var ev = new Content.Shared.Chat.InGameOocMessageAttemptEvent(
                session,
                Content.Shared.Chat.InGameOOCChatType.Looc);
            server.EntMan.EventBus.RaiseEvent(EventSource.Local, ref ev);
            Assert.That(ev.Cancelled, Is.True, "Tutorial isolation must cancel LOOC");

            cfg.SetCVar(CCVars.LoocEnabled, false);
        });
    }

    [Test]
    public async Task TutorialServer_PostRound_DoesNotEnableOoc()
    {
        var pair = Pair;
        var server = pair.Server;
        var ticker = server.System<GameTicker>();
        var cfg = server.ResolveDependency<IConfigurationManager>();

        await server.WaitPost(() =>
        {
            ticker.SetGamePreset("TutorialServer");
            ticker.StartGameRule(TutorialRule, out _);
            ticker.StartRound();
        });
        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            Assert.That(cfg.GetCVar(CCVars.OocEnabled), Is.False);
            Assert.That(cfg.GetCVar(CCVars.OocEnableDuringRound), Is.True);

            // ChatSystem turns OOC on for PostRound / PreRoundLobby unless ooc.enable_during_round is set.
            server.EntMan.EventBus.RaiseEvent(
                EventSource.Local,
                new GameRunLevelChangedEvent(GameRunLevel.InRound, GameRunLevel.PostRound));

            Assert.That(cfg.GetCVar(CCVars.OocEnabled), Is.False,
                "End-of-round must not re-enable OOC on TutorialServer");
            Assert.That(cfg.GetCVar(CCVars.LoocEnabled), Is.False);
            Assert.That(cfg.GetCVar(CCVars.DeadChatEnabled), Is.False);
        });
    }

    [Test]
    public async Task TutorialWikiJobs_HaveTutorialRolePrototypes()
    {
        var pair = Pair;
        var server = pair.Server;
        var proto = server.ProtoMan;

        var wikiJobs = new[]
        {
            "Passenger", "Bartender", "Botanist", "Chef", "Chaplain", "Clown", "Mime", "Janitor",
            "CargoTechnician", "Quartermaster", "SalvageSpecialist",
            "Chemist", "MedicalDoctor", "Paramedic", "ChiefMedicalOfficer",
            "Scientist", "ResearchDirector",
            "TechnicalAssistant", "StationEngineer", "AtmosphericTechnician", "ChiefEngineer",
            "SecurityOfficer", "Detective", "Warden", "HeadOfSecurity",
            "Captain", "HeadOfPersonnel",
        };

        await server.WaitAssertion(() =>
        {
            foreach (var job in wikiJobs)
            {
                Assert.That(proto.TryIndex<TutorialRolePrototype>($"Tutorial{job}", out var role),
                    $"Missing tutorialRole Tutorial{job}");
                Assert.That(role!.Job, Is.EqualTo(new ProtoId<JobPrototype>(job)));
                if (job == "TechnicalAssistant")
                    Assert.That(role.Stub, Is.True, $"{job} temporarily incomplete");
                else
                    Assert.That(role.Stub, Is.False, $"{job} should be a ready wiki tutorial");
                Assert.That(role.Goals.Count, Is.GreaterThanOrEqualTo(3), $"{job} should have multi-goal curriculum");
                Assert.That(role.Goals.Sum(g => g.SubGoals.Count), Is.GreaterThanOrEqualTo(5),
                    $"{job} should have enough sub-goals for a short practice session");
                if (role.ShuttleArena != null)
                {
                    Assert.That(role.Goals.SelectMany(g => g.SubGoals)
                        .Any(s => s.Complete is TutorialStepComplete.PilotShuttle
                            or TutorialStepComplete.DockShuttle
                            or TutorialStepComplete.UndockShuttle),
                        $"{job} shuttle arena should teach piloting/docking");
                }
                else if (role.SalvageArena != null)
                {
                    Assert.That(role.Goals.SelectMany(g => g.SubGoals)
                        .Any(s => s.Complete is TutorialStepComplete.RecyclerProcessed
                            or TutorialStepComplete.ContainerHasEntityCount),
                        $"{job} salvage arena should teach locker/recycler loops");
                }
                else
                {
                    Assert.That(role.PracticeSpawns.Count, Is.GreaterThan(0), $"{job} should stock a practice kit");
                    Assert.That(role.RoomTemplate != null || role.Room != null, $"{job} should use a department room template");
                }
            }

            Assert.That(proto.TryIndex<TutorialRolePrototype>("TutorialTraitor", out var traitor));
            Assert.That(traitor!.Stub, Is.False);
            Assert.That(traitor.Goals.Count, Is.GreaterThanOrEqualTo(3));
            Assert.That(traitor.PlaceholderObjectives.Count, Is.GreaterThanOrEqualTo(1));
            Assert.That(traitor.RoomTemplate, Is.EqualTo(new ProtoId<TutorialRoomTemplatePrototype>("TutorialSectionMaintAntag")));
        });
    }

    [Test]
    public async Task TutorialChef_HasCookingGoalsAndSensors()
    {
        var pair = Pair;
        var server = pair.Server;
        var proto = server.ProtoMan;

        await server.WaitAssertion(() =>
        {
            var chef = proto.Index<TutorialRolePrototype>("TutorialChef");
            Assert.That(chef.Stub, Is.False);
            Assert.That(chef.Goals.Any(g => g.Id == "burger"));
            Assert.That(chef.Goals.Any(g => g.Id == "cake"));

            var holdKnife = chef.Goals.SelectMany(g => g.SubGoals)
                .FirstOrDefault(s => s.Id == "knife");
            Assert.That(holdKnife, Is.Not.Null);
            Assert.That(holdKnife!.Complete, Is.EqualTo(TutorialStepComplete.HoldItem));
            Assert.That(holdKnife.Entity, Is.EqualTo(new EntProtoId("KitchenKnife")));

            Assert.That(chef.PracticeSpawns.Any(p => p.Id == "TutorialKitchenMicrowave"));
            Assert.That(chef.PracticeSpawns.Any(p => p.Id == "TutorialVendingMachineChefvend"));
        });
    }

    [Test]
    public async Task TutorialDeepCurricula_UseNewGoalSensors()
    {
        var pair = Pair;
        var server = pair.Server;
        var proto = server.ProtoMan;

        await server.WaitAssertion(() =>
        {
            static TutorialSubGoalData Sub(TutorialRolePrototype role, string id) =>
                role.Goals.SelectMany(g => g.SubGoals).First(s => s.Id == id);

            var chemist = proto.Index<TutorialRolePrototype>("TutorialChemist");
            Assert.That(chemist.RoomTemplate, Is.EqualTo(new ProtoId<TutorialRoomTemplatePrototype>("TutorialSectionChem")));
            Assert.That(Sub(chemist, "dispenser").Complete, Is.EqualTo(TutorialStepComplete.InteractTargetTag));
            Assert.That(Sub(chemist, "dispenser").Tag, Is.EqualTo("TutorialChemDispenser"));
            Assert.That(Sub(chemist, "inaprovaline").Complete, Is.EqualTo(TutorialStepComplete.SolutionContains));
            Assert.That(Sub(chemist, "inaprovaline").Reagent, Is.EqualTo(new ProtoId<Content.Shared.Chemistry.Reagent.ReagentPrototype>("Inaprovaline")));
            Assert.That(Sub(chemist, "dylovene").Complete, Is.EqualTo(TutorialStepComplete.SolutionContains));
            Assert.That(Sub(chemist, "dylovene").Reagent, Is.EqualTo(new ProtoId<Content.Shared.Chemistry.Reagent.ReagentPrototype>("Dylovene")));
            Assert.That(Sub(chemist, "pills").Complete, Is.EqualTo(TutorialStepComplete.ObtainItem));
            Assert.That(Sub(chemist, "pills").Entity, Is.EqualTo(new EntProtoId("PillCanister")));
            Assert.That(Sub(chemist, "pills").MinCount, Is.EqualTo(1));
            Assert.That(Sub(chemist, "table-salt").Complete, Is.EqualTo(TutorialStepComplete.SolutionContains));
            Assert.That(Sub(chemist, "table-salt").Reagent, Is.EqualTo(new ProtoId<Content.Shared.Chemistry.Reagent.ReagentPrototype>("TableSalt")));
            Assert.That(Sub(chemist, "make-saline").Complete, Is.EqualTo(TutorialStepComplete.SolutionContains));
            Assert.That(Sub(chemist, "make-saline").Reagent, Is.EqualTo(new ProtoId<Content.Shared.Chemistry.Reagent.ReagentPrototype>("Saline")));
            // Machines come from the Saltern crop (tagged by TutorialChemBootstrapSystem).
            Assert.That(chemist.PracticeSpawns.Any(p => p.Id == "TutorialChemDispenser"), Is.False);
            Assert.That(chemist.PracticeSpawns.Any(p => p.Id == "TutorialChemMaster"), Is.False);
            Assert.That(chemist.PracticeSpawns.Any(p => p.Id == "TutorialKitchenReagentGrinder"), Is.False);
            Assert.That(chemist.PracticeSpawns.Any(p => p.Id == "PillCanister"));
            Assert.That(chemist.PracticeSpawns.Any(p => p.Id == "JugWater"));
            // Glassware stays off the north machine row / drain tile.
            Assert.That(chemist.PracticeSpawns.All(p => p.Offset.Y < 0.75f),
                "Chemist practice spawns must stay south of the crop machine row (y+1)");

            var janitor = proto.Index<TutorialRolePrototype>("TutorialJanitor");
            Assert.That(Sub(janitor, "clear-puddle").Complete, Is.EqualTo(TutorialStepComplete.PuddleCleared));
            Assert.That(Sub(janitor, "clear-puddle").Marker, Is.EqualTo("blood-puddle"));

            var ta = proto.Index<TutorialRolePrototype>("TutorialTechnicalAssistant");
            Assert.That(ta.Stub, Is.True); // temporarily incomplete
            Assert.That(ta.MentorSpawnOffset, Is.EqualTo(new System.Numerics.Vector2(-2f, -3f)));
            Assert.That(Sub(ta, "wear-gloves").Complete, Is.EqualTo(TutorialStepComplete.WearItem));
            Assert.That(Sub(ta, "wear-gloves").Entity, Is.EqualTo(new EntProtoId("ClothingHandsGlovesColorYellow")));
            Assert.That(Sub(ta, "hold-screwdriver").Complete, Is.EqualTo(TutorialStepComplete.HoldTag));
            Assert.That(Sub(ta, "hold-screwdriver").Tag, Is.EqualTo("Screwdriver"));
            Assert.That(Sub(ta, "hold-multitool").Complete, Is.EqualTo(TutorialStepComplete.HoldTag));
            Assert.That(Sub(ta, "hold-multitool").Tag, Is.EqualTo("Multitool"));
            Assert.That(Sub(ta, "open-panel").Complete, Is.EqualTo(TutorialStepComplete.WiresPanelOpen));
            Assert.That(Sub(ta, "pulse-power").Complete, Is.EqualTo(TutorialStepComplete.TargetPowerDisabled));
            Assert.That(Sub(ta, "crowbar-door").Complete, Is.EqualTo(TutorialStepComplete.TargetDoorOpen));
            Assert.That(Sub(ta, "cut-power").Complete, Is.EqualTo(TutorialStepComplete.PowerWiresCut));
            Assert.That(Sub(ta, "place-lv").Complete, Is.EqualTo(TutorialStepComplete.MapHasEntity));
            Assert.That(Sub(ta, "place-lv").Entity, Is.EqualTo(new EntProtoId("CableApcExtension")));
            Assert.That(ta.PracticeSpawns.Any(p => p.Id == "TutorialHackAirlock" && p.Offset == new System.Numerics.Vector2(0f, 1f)));
            Assert.That(ta.PracticeSpawns.Any(p => p.Id == "ClothingHandsGlovesColorYellow"));

            var passenger = proto.Index<TutorialRolePrototype>("TutorialPassenger");
            Assert.That(passenger.MentorSpawnOffset, Is.EqualTo(new System.Numerics.Vector2(4f, -2f)));
            Assert.That(proto.Index<TutorialRoomTemplatePrototype>("TutorialSectionArrivals").LightFacingOffsetDegrees,
                Is.EqualTo(180f));
            Assert.That(proto.Index<TutorialRoomTemplatePrototype>("TutorialSectionEngineering").LightFacingOffsetDegrees,
                Is.EqualTo(0f));

            var eng = proto.Index<TutorialRolePrototype>("TutorialStationEngineer");
            Assert.That(Sub(eng, "place-mv").Complete, Is.EqualTo(TutorialStepComplete.MapHasEntity));
            Assert.That(Sub(eng, "place-mv").MinCount, Is.EqualTo(2));
            Assert.That(Sub(eng, "place-hv").Complete, Is.EqualTo(TutorialStepComplete.MapHasEntity));
            Assert.That(Sub(eng, "place-hv").MinCount, Is.EqualTo(2));
            Assert.That(Sub(eng, "power-monitor").Tag, Is.EqualTo("TutorialPowerMonitor"));
            Assert.That(eng.PracticeSpawns.Any(p => p.Id == "TutorialSingularityGenerator"));
            Assert.That(eng.PracticeSpawns.Any(p => p.Id == "TutorialTeslaGenerator"));
            Assert.That(eng.PracticeSpawns.Any(p => p.Id == "CableMV"));
            Assert.That(eng.PracticeSpawns.Any(p => p.Id == "CableHV"));

            var bar = proto.Index<TutorialRolePrototype>("TutorialBartender");
            Assert.That(Sub(bar, "screwdriver").Complete, Is.EqualTo(TutorialStepComplete.SolutionContains));
            Assert.That(Sub(bar, "screwdriver").Reagent, Is.EqualTo(new ProtoId<Content.Shared.Chemistry.Reagent.ReagentPrototype>("ScrewdriverCocktail")));

            var botany = proto.Index<TutorialRolePrototype>("TutorialBotanist");
            Assert.That(Sub(botany, "megaseed-vend").Complete, Is.EqualTo(TutorialStepComplete.InteractTargetTag));
            Assert.That(Sub(botany, "megaseed-vend").Tag, Is.EqualTo("TutorialVending"));
            Assert.That(Sub(botany, "hold-seeds").Complete, Is.EqualTo(TutorialStepComplete.HoldItem));
            Assert.That(Sub(botany, "hold-seeds").Entity, Is.EqualTo(new EntProtoId("WheatSeeds")));
            Assert.That(botany.PracticeSpawns.Any(p => p.Id == "TutorialVendingMachineSeeds"));

            var doctor = proto.Index<TutorialRolePrototype>("TutorialMedicalDoctor");
            Assert.That(Sub(doctor, "hug-mentor").Complete, Is.EqualTo(TutorialStepComplete.InteractMentor));
            Assert.That(Sub(doctor, "heal-dummy").Complete, Is.EqualTo(TutorialStepComplete.PracticeMobDamageBelow));
            Assert.That(Sub(doctor, "scan-patient").Complete, Is.EqualTo(TutorialStepComplete.InteractTargetHolding));
            Assert.That(Sub(doctor, "scan-patient").Entity, Is.EqualTo(new EntProtoId("HandheldHealthAnalyzer")));
            Assert.That(Sub(doctor, "scan-patient").Tag, Is.EqualTo("TutorialPracticePatient"));
            Assert.That(Sub(doctor, "use-epi").Complete, Is.EqualTo(TutorialStepComplete.InteractTargetHolding));
            Assert.That(Sub(doctor, "use-epi").Entity, Is.EqualTo(new EntProtoId("EmergencyMedipen")));
            Assert.That(Sub(doctor, "use-epi").Tag, Is.EqualTo("TutorialPracticePatient"));
            Assert.That(Sub(doctor, "revive-corpse").Complete, Is.EqualTo(TutorialStepComplete.PracticeMobRevived));
            Assert.That(doctor.MentorEntity, Is.EqualTo(new EntProtoId("TutorialMedicalMentor")));
            Assert.That(doctor.PracticeSpawns.Any(p => p.Id == "TutorialPracticeMobPatient"));
            Assert.That(doctor.PracticeSpawns.Any(p => p.Id == "TutorialPracticeMobCorpse"));
            Assert.That(doctor.PracticeSpawns.Any(p => p.Id == "DefibrillatorOneHandedUnpowered"));
            Assert.That(doctor.PracticeSpawns.Any(p => p.Id == "ClothingEyesHudMedical"));
            Assert.That(doctor.PracticeSpawns.Any(p => p.Id == "EmergencyMedipen"));
            Assert.That(doctor.PracticeSpawns.Any(p => p.Id == "TutorialStepMarker"), Is.False);

            var sec = proto.Index<TutorialRolePrototype>("TutorialSecurityOfficer");
            Assert.That(sec.RoomTemplate, Is.EqualTo(new ProtoId<TutorialRoomTemplatePrototype>("TutorialSectionSecurity")));
            Assert.That(Sub(sec, "stun-dummy").Complete, Is.EqualTo(TutorialStepComplete.PracticeMobStunned));
            Assert.That(Sub(sec, "cuff-dummy").Complete, Is.EqualTo(TutorialStepComplete.PracticeMobCuffed));
            var secSubs = sec.Goals.SelectMany(g => g.SubGoals).ToList();
            Assert.That(secSubs.FindIndex(s => s.Id == "stun-dummy"),
                Is.LessThan(secSubs.FindIndex(s => s.Id == "cuff-dummy")));

            var warden = proto.Index<TutorialRolePrototype>("TutorialWarden");
            Assert.That(warden.RoomTemplate, Is.EqualTo(new ProtoId<TutorialRoomTemplatePrototype>("TutorialSectionBrig")));
            Assert.That(Sub(warden, "start-timer").Complete, Is.EqualTo(TutorialStepComplete.BrigTimerStarted));
            Assert.That(Sub(warden, "cell-walk").Complete, Is.EqualTo(TutorialStepComplete.ReachMarker));

            var ce = proto.Index<TutorialRolePrototype>("TutorialChiefEngineer");
            Assert.That(Sub(ce, "ame-inject").Complete, Is.EqualTo(TutorialStepComplete.AmeInjecting));
            Assert.That(Sub(ce, "open-comms").Complete, Is.EqualTo(TutorialStepComplete.InteractTargetTag));
            Assert.That(Sub(ce, "open-comms").Tag, Is.EqualTo("TutorialCommsConsole"));
            Assert.That(Sub(ce, "lead-tip").Complete, Is.EqualTo(TutorialStepComplete.Acknowledge));
            // Magboots: Z/Use equips clothing; toggle is ActionToggleMagboots.
            Assert.That(Sub(ce, "use-magboots").Complete, Is.EqualTo(TutorialStepComplete.ActionUsed));
            Assert.That(Sub(ce, "use-magboots").Entity, Is.EqualTo(new EntProtoId("ActionToggleMagboots")));
            Assert.That(ce.RoomTemplate, Is.EqualTo(new ProtoId<TutorialRoomTemplatePrototype>("TutorialSectionEngineering")));
            Assert.That(ce.PracticeSpawns.Any(p => p.Id == "TutorialComputerComms"));
            Assert.That(ce.Goals.SelectMany(g => g.SubGoals).Any(s => s.Id is "teg" or "singulo"), Is.False);

            var cmo = proto.Index<TutorialRolePrototype>("TutorialChiefMedicalOfficer");
            Assert.That(Sub(cmo, "heal-dummy").Complete, Is.EqualTo(TutorialStepComplete.PracticeMobDamageBelow));
            Assert.That(Sub(cmo, "use-crew-monitor").Complete, Is.EqualTo(TutorialStepComplete.UseInHand));
            Assert.That(Sub(cmo, "use-crew-monitor").Entity, Is.EqualTo(new EntProtoId("HandheldCrewMonitor")));
            Assert.That(Sub(cmo, "revive-corpse").Complete, Is.EqualTo(TutorialStepComplete.PracticeMobRevived));
            Assert.That(Sub(cmo, "medhud-tip").Complete, Is.EqualTo(TutorialStepComplete.Acknowledge));
            Assert.That(cmo.PracticeSpawns.Any(p => p.Id == "HandheldCrewMonitor"));
            Assert.That(cmo.PracticeSpawns.Any(p => p.Id == "TutorialPracticeMobCorpse"));
            Assert.That(cmo.PracticeSpawns.Any(p => p.Id == "DefibrillatorOneHandedUnpowered"));

            var rd = proto.Index<TutorialRolePrototype>("TutorialResearchDirector");
            Assert.That(rd.Goals.SelectMany(g => g.SubGoals).Any(s => s.Complete == TutorialStepComplete.SpawnAnomaly));
            Assert.That(rd.Goals.SelectMany(g => g.SubGoals).Any(s => s.Complete == TutorialStepComplete.RemoveAnomaly));

            var para = proto.Index<TutorialRolePrototype>("TutorialParamedic");
            Assert.That(para.Stub, Is.False);
            Assert.That(Sub(para, "scan-patient").Complete, Is.EqualTo(TutorialStepComplete.InteractTargetHolding));
            Assert.That(Sub(para, "scan-patient").Entity, Is.EqualTo(new EntProtoId("HandheldHealthAnalyzer")));
            Assert.That(Sub(para, "heal-dummy").Complete, Is.EqualTo(TutorialStepComplete.PracticeMobDamageBelow));
            Assert.That(Sub(para, "heal-dummy").StuckHint, Is.EqualTo("tutorial-job-paramedic-sg-heal-stuck"));
            Assert.That(para.PracticeSpawns.Any(p => p.Id == "ClothingEyesHudMedical"));

            var atmos = proto.Index<TutorialRolePrototype>("TutorialAtmosphericTechnician");
            Assert.That(atmos.Stub, Is.False);
            Assert.That(atmos.RoomTemplate, Is.EqualTo(new ProtoId<TutorialRoomTemplatePrototype>("TutorialSectionAtmos")));
            Assert.That(Sub(atmos, "filter").Tag, Is.EqualTo("TutorialGasFilter"));
            Assert.That(Sub(atmos, "teg").Tag, Is.EqualTo("TutorialTeg"));
            Assert.That(Sub(atmos, "teg").Complete, Is.EqualTo(TutorialStepComplete.InteractTargetTag));
            Assert.That(Sub(atmos, "teg-power").Complete, Is.EqualTo(TutorialStepComplete.TegProducingPower));
            Assert.That(Sub(atmos, "place-pipes").Complete, Is.EqualTo(TutorialStepComplete.MapHasEntity));
            Assert.That(Sub(atmos, "place-pipes").Entity, Is.EqualTo(new EntProtoId("GasPipeStraight")));
            Assert.That(Sub(atmos, "place-pipes").MinCount, Is.EqualTo(2));
            Assert.That(Sub(atmos, "hold-suit").Complete, Is.EqualTo(TutorialStepComplete.ObtainItem),
                "Hardsuit Z/Use equips — ObtainItem must accept worn suits");
            Assert.That(Sub(atmos, "hold-suit").Entity, Is.EqualTo(new EntProtoId("ClothingOuterHardsuitAtmos")));
            Assert.That(Sub(atmos, "hold-magboots").Complete, Is.EqualTo(TutorialStepComplete.ObtainItem));
            Assert.That(Sub(atmos, "use-magboots").Complete, Is.EqualTo(TutorialStepComplete.ActionUsed));
            Assert.That(Sub(atmos, "use-magboots").Entity, Is.EqualTo(new EntProtoId("ActionToggleMagboots")));
            Assert.That(atmos.PracticeSpawns.Any(p => p.Id == "LockerAtmosphericsFilledHardsuit"),
                "Practice locker must include the atmos hardsuit fill");
            Assert.That(atmos.PracticeSpawns.Any(p => p.Id == "ClothingOuterHardsuitAtmos"), Is.False,
                "Floor suit spawn removed — suit comes from the hardsuit locker");
            Assert.That(atmos.PracticeSpawns.Any(p => p.Id == "TutorialTegCenter"));
            Assert.That(atmos.PracticeSpawns.Any(p => p.Id == "SheetSteel10"));
            // Interact gate must precede TegProducingPower in curriculum order.
            var atmosSubs = atmos.Goals.SelectMany(g => g.SubGoals).ToList();
            Assert.That(atmosSubs.FindIndex(s => s.Id == "teg"),
                Is.LessThan(atmosSubs.FindIndex(s => s.Id == "teg-power")));

            var salvage = proto.Index<TutorialRolePrototype>("TutorialSalvageSpecialist");
            Assert.That(salvage.Stub, Is.False);
            Assert.That(salvage.SalvageArena,
                Is.EqualTo(new ProtoId<TutorialSalvageArenaPrototype>("TutorialArenaSalvageBay")));
            Assert.That(salvage.Room, Is.Null);
            Assert.That(Sub(salvage, "use-magboots").Complete, Is.EqualTo(TutorialStepComplete.ActionUsed));
            Assert.That(Sub(salvage, "use-magboots").Entity, Is.EqualTo(new EntProtoId("ActionToggleMagboots")));
            Assert.That(Sub(salvage, "activate-magnet").Tag, Is.EqualTo("TutorialSalvageMagnet"));
            Assert.That(Sub(salvage, "stuff-locker").Complete, Is.EqualTo(TutorialStepComplete.ContainerHasEntityCount));
            Assert.That(Sub(salvage, "stuff-locker").MinCount, Is.EqualTo(3));
            Assert.That(Sub(salvage, "feed-recycler").Complete, Is.EqualTo(TutorialStepComplete.RecyclerProcessed));

            var hop = proto.Index<TutorialRolePrototype>("TutorialHeadOfPersonnel");
            Assert.That(Sub(hop, "write-botany").Complete, Is.EqualTo(TutorialStepComplete.IdCardHasJob));
            Assert.That(Sub(hop, "write-botany").Job, Is.EqualTo(new ProtoId<JobPrototype>("Botanist")));
            Assert.That(Sub(hop, "write-chef").Job, Is.EqualTo(new ProtoId<JobPrototype>("Chef")));
            Assert.That(Sub(hop, "write-janitor").Job, Is.EqualTo(new ProtoId<JobPrototype>("Janitor")));
            Assert.That(hop.PracticeSpawns.Any(p => p.Id == "TutorialHoPVisitorBotany"));
            Assert.That(hop.PracticeSpawns.Any(p => p.Id == "TutorialComputerId"));
            // Command crop zone origin is Cap quarters; ID console must sit on Saltern HoP desk (0,-5).
            var hopIdConsole = hop.PracticeSpawns.First(p => p.Id == "TutorialComputerId");
            Assert.That(hopIdConsole.Offset, Is.EqualTo(new Vector2(0f, -5f)));
            // Player must start inside HoP (desk/windoor is not a walk door).
            Assert.That(hop.SpawnOffset, Is.EqualTo(new Vector2(1f, -6f)));
            Assert.That((hopIdConsole.Offset - hop.SpawnOffset).Length(),
                Is.LessThanOrEqualTo(SharedInteractionSystem.InteractionRange),
                "HoP spawn must be within interact range of the ID console");

            var detective = proto.Index<TutorialRolePrototype>("TutorialDetective");
            Assert.That(detective.Stub, Is.False);
            Assert.That(Sub(detective, "scan-evidence").Tag, Is.EqualTo("TutorialForensicsEvidence"));
            Assert.That(Sub(detective, "pad-suspect").Complete, Is.EqualTo(TutorialStepComplete.InteractTargetTag));
            Assert.That(Sub(detective, "pad-suspect").Tag, Is.EqualTo("TutorialPracticeMob"));
            Assert.That(Sub(detective, "cuff-dummy").Complete, Is.EqualTo(TutorialStepComplete.PracticeMobCuffed));

            var chaplain = proto.Index<TutorialRolePrototype>("TutorialChaplain");
            Assert.That(Sub(chaplain, "heal-parishioner").Complete, Is.EqualTo(TutorialStepComplete.PracticeMobDamageBelow));
            Assert.That(chaplain.PracticeSpawns.Any(p => p.Id == "TutorialBible"));
            Assert.That(chaplain.PracticeSpawns.Any(p => p.Id == "TutorialPracticeMobParishioner"));

            var mime = proto.Index<TutorialRolePrototype>("TutorialMime");
            Assert.That(Sub(mime, "place-wall").Complete, Is.EqualTo(TutorialStepComplete.MapHasEntity));
            Assert.That(Sub(mime, "place-wall").Entity, Is.EqualTo(new EntProtoId("WallInvisible")));
            Assert.That(Sub(mime, "use-crayon").Complete, Is.EqualTo(TutorialStepComplete.UseInHand));
            Assert.That(Sub(mime, "use-crayon").Entity, Is.EqualTo(new EntProtoId("CrayonMime")));
            Assert.That(mime.RoomTemplate, Is.EqualTo(new ProtoId<TutorialRoomTemplatePrototype>("TutorialSectionTheatre")));

            Assert.That(proto.TryIndex<TutorialRolePrototype>("TutorialCentralCommandOfficial", out _), Is.False);

            var traitor = proto.Index<TutorialRolePrototype>("TutorialTraitor");
            Assert.That(Sub(traitor, "emag-door").Complete, Is.EqualTo(TutorialStepComplete.InteractTargetTag));
            Assert.That(Sub(traitor, "emag-door").Tag, Is.EqualTo("TutorialHackDoor"));
            Assert.That(Sub(traitor, "use-flash").Complete, Is.EqualTo(TutorialStepComplete.UseInHand));
            Assert.That(Sub(traitor, "stow-intel").Complete, Is.EqualTo(TutorialStepComplete.StowItem));
            Assert.That(Sub(traitor, "cuff-dummy").Complete, Is.EqualTo(TutorialStepComplete.PracticeMobCuffed));
            Assert.That(Sub(traitor, "uplink-buy").Complete, Is.EqualTo(TutorialStepComplete.StorePurchased));
            Assert.That(traitor.PracticeSpawns.Any(p => p.Id == "TutorialHackAirlock"));
            Assert.That(traitor.PracticeSpawns.Any(p => p.Id == "NukeDiskFake"));

            var captain = proto.Index<TutorialRolePrototype>("TutorialCaptain");
            Assert.That(Sub(captain, "stow-disk").Complete, Is.EqualTo(TutorialStepComplete.StowItem));
            Assert.That(Sub(captain, "stow-disk").Entity, Is.EqualTo(new EntProtoId("NukeDiskFake")));
            Assert.That(Sub(captain, "open-comms").Complete, Is.EqualTo(TutorialStepComplete.InteractTargetTag));
            Assert.That(Sub(captain, "open-comms").Tag, Is.EqualTo("TutorialCommsConsole"));
            Assert.That(Sub(captain, "set-alert").Complete, Is.EqualTo(TutorialStepComplete.AlertLevelChanged));
            Assert.That(Sub(captain, "set-alert").AlertLevel, Is.EqualTo(new ProtoId<Content.Shared.AlertLevel.AlertLevelPrototype>("Blue")));
            Assert.That(Sub(captain, "open-fax").Tag, Is.EqualTo("TutorialFax"));
            Assert.That(Sub(captain, "stamp-paper").Tag, Is.EqualTo("Paper"));
            Assert.That(captain.PracticeSpawns.Any(p => p.Id == "TutorialComputerComms"));
            Assert.That(captain.PracticeSpawns.Any(p => p.Id == "TutorialFaxCentcom"));

            var clown = proto.Index<TutorialRolePrototype>("TutorialClown");
            Assert.That(Sub(clown, "pie-dummy").Complete, Is.EqualTo(TutorialStepComplete.PracticeMobCreamPied));
            Assert.That(Sub(clown, "slip-dummy").Complete, Is.EqualTo(TutorialStepComplete.PracticeMobSlipped));
            Assert.That(clown.PracticeSpawns.Any(p => p.Id == "FoodPieBananaCream"));
            Assert.That(clown.PracticeSpawns.Any(p => p.Id == "TutorialPracticeMobAudience"));
            Assert.That(clown.RoomTemplate, Is.EqualTo(new ProtoId<TutorialRoomTemplatePrototype>("TutorialSectionTheatre")));

            var ra = proto.Index<TutorialRolePrototype>("TutorialResearchAssistant");
            Assert.That(ra.Stub, Is.False);
            Assert.That(ra.Category, Is.EqualTo("Science"));
            Assert.That(Sub(ra, "open-console").Tag, Is.EqualTo("TutorialResearchConsole"));
            Assert.That(Sub(ra, "unlock-tech").Complete, Is.EqualTo(TutorialStepComplete.ResearchUnlocked));
            Assert.That(Sub(ra, "unlock-tech").Technology,
                Is.EqualTo(new ProtoId<Content.Shared.Research.Prototypes.TechnologyPrototype>("BasicRobotics")));
            Assert.That(Sub(ra, "print-sensor").Complete, Is.EqualTo(TutorialStepComplete.LathePrinted));
            Assert.That(Sub(ra, "print-sensor").Entity, Is.EqualTo(new EntProtoId("ProximitySensor")));
            Assert.That(ra.PracticeSpawns.Any(p => p.Id == "TutorialResearchServer" && p.Room == 0));
            Assert.That(ra.PracticeSpawns.Any(p => p.Id == "TutorialResearchConsole" && p.Room == 0));
            Assert.That(ra.PracticeSpawns.Any(p => p.Id == "TutorialExosuitFabricator" && p.Room == 0));
            Assert.That(ra.PracticeSpawns.All(p => p.Room == 0),
                "Research Assistant stays in one science section — walk to the console layout spot");
            Assert.That(TutorialMapSystem.ResolveCopyCount(ra), Is.EqualTo(1));
            Assert.That(Sub(ra, "walk-console").Complete, Is.EqualTo(TutorialStepComplete.ReachMarker));
            Assert.That(Sub(ra, "walk-console").Marker, Is.EqualTo("ra-console"));
            Assert.That(ra.PracticeSpawns.Any(p => p.Id == "Multitool" && p.Room == 0),
                "Multitool must spawn in the kit chamber with the science locker");
            Assert.That(ra.PracticeSpawns.Any(p => p.Id == "NodeScanner" && p.Room == 0),
                "Node scanner must spawn in the kit chamber with the science locker");

            var scientist = proto.Index<TutorialRolePrototype>("TutorialScientist");
            Assert.That(scientist.Goals.SelectMany(g => g.SubGoals).Any(s => s.Complete == TutorialStepComplete.SpawnAnomaly));
            Assert.That(scientist.Goals.SelectMany(g => g.SubGoals).Any(s => s.Complete == TutorialStepComplete.RemoveAnomaly));
            Assert.That(scientist.Goals.SelectMany(g => g.SubGoals).Any(s => s.Complete == TutorialStepComplete.ResearchUnlocked), Is.False);
            Assert.That(scientist.Goals.SelectMany(g => g.SubGoals).Any(s => s.Complete == TutorialStepComplete.LathePrinted), Is.False);
            Assert.That(scientist.Goals.SelectMany(g => g.SubGoals).Any(s => s.Id == "tool-vend"), Is.False);
            Assert.That(Sub(scientist, "ra-tip").Complete, Is.EqualTo(TutorialStepComplete.Acknowledge));
            Assert.That(Sub(rd, "ra-tip").Complete, Is.EqualTo(TutorialStepComplete.Acknowledge));

            Assert.That(Sub(para, "buckle-patient").Complete, Is.EqualTo(TutorialStepComplete.PracticeMobBuckled));
            Assert.That(Sub(para, "heal-dummy").MaxDamage, Is.EqualTo(25f));
            Assert.That(Sub(para, "heal-dummy").Hint, Is.EqualTo("tutorial-job-paramedic-sg-heal-hint"));
            Assert.That(Sub(para, "rollerbed-tip").Complete, Is.EqualTo(TutorialStepComplete.Acknowledge));
            Assert.That(Sub(para, "medhud-tip").Complete, Is.EqualTo(TutorialStepComplete.Acknowledge));
            Assert.That(para.PracticeSpawns.Any(p => p.Id == "TutorialRollerBed"));

            var hos = proto.Index<TutorialRolePrototype>("TutorialHeadOfSecurity");
            Assert.That(Sub(hos, "open-comms").Complete, Is.EqualTo(TutorialStepComplete.InteractTargetTag));
            Assert.That(Sub(hos, "open-comms").Tag, Is.EqualTo("TutorialCommsConsole"));
            Assert.That(hos.PracticeSpawns.Any(p => p.Id == "TutorialComputerComms"));

            var qm = proto.Index<TutorialRolePrototype>("TutorialQuartermaster");
            Assert.That(Sub(qm, "orders").Tag, Is.EqualTo("TutorialCargoOrders"));
            Assert.That(Sub(qm, "approve-order").Complete, Is.EqualTo(TutorialStepComplete.CargoOrderApproved));
            Assert.That(Sub(qm, "sell-crate").Complete, Is.EqualTo(TutorialStepComplete.CargoSold));
            Assert.That(Sub(qm, "hold-package").Entity, Is.EqualTo(new EntProtoId("PackageDelivery")));
            Assert.That(Sub(qm, "cart").Complete, Is.EqualTo(TutorialStepComplete.InteractTargetTag));
            Assert.That(Sub(qm, "cart").Tag, Is.EqualTo("TutorialVending"));
            Assert.That(qm.PracticeSpawns.Any(p => p.Id == "TutorialCargoPalletSell"));

            Assert.That(Sub(botany, "get-wheat").Complete, Is.EqualTo(TutorialStepComplete.ObtainItem));
            Assert.That(Sub(botany, "get-wheat").Entity, Is.EqualTo(new EntProtoId("WheatBushel")));

            var surgery = proto.Index<TutorialRolePrototype>("TutorialSurgeryStarlight");
            Assert.That(surgery.Stub, Is.True); //Tutorial: temporarily greyed pending manual test
            Assert.That(surgery.Category, Is.EqualTo("Server specific"));
            Assert.That(surgery.SubCategory, Is.EqualTo("Starlight"));
            Assert.That(surgery.Name, Is.EqualTo("tutorial-job-surgery-starlight-name"));
            Assert.That(Loc.GetString(surgery.Name), Is.EqualTo("Surgery"));
            Assert.That(Sub(surgery, "open-ui").Complete, Is.EqualTo(TutorialStepComplete.StarlightSurgeryUiOpened));
            Assert.That(Sub(surgery, "implant").Complete, Is.EqualTo(TutorialStepComplete.StarlightSurgeryEyeImplanted));
            Assert.That(surgery.PracticeSpawns.Any(p => p.Id == "TutorialPracticeMobStarlightSurgery"));
            Assert.That(surgery.PracticeSpawns.Any(p => p.Id == "TutorialStarlightEyeImplantWelding"));

            var cyberMed = proto.Index<TutorialRolePrototype>("TutorialSurgeryCyberMed");
            Assert.That(cyberMed.Stub, Is.True); //Tutorial: temporarily greyed pending manual test
            Assert.That(cyberMed.Category, Is.EqualTo("Server specific"));
            Assert.That(cyberMed.SubCategory, Is.EqualTo("BPL14"));
            Assert.That(cyberMed.Name, Is.EqualTo("tutorial-job-surgery-cybermed-name"));
            Assert.That(Loc.GetString(cyberMed.Name), Is.EqualTo("Surgery"));
            Assert.That(Sub(cyberMed, "open-ui").Complete, Is.EqualTo(TutorialStepComplete.CyberMedSurgeryUiOpened));
            Assert.That(Sub(cyberMed, "implant").Complete, Is.EqualTo(TutorialStepComplete.CyberMedSurgeryComplete));
            Assert.That(cyberMed.PracticeSpawns.Any(p => p.Id == "TutorialPracticeMobCyberMedSurgery"));
            Assert.That(cyberMed.PracticeSpawns.Any(p => p.Id == "TutorialCyberMedAnalyzer"));
            Assert.That(cyberMed.PracticeSpawns.Any(p => p.Id == "TutorialCyberMedCyberHeart"));
        });
    }

    [Test]
    public async Task TutorialSurgeryStarlight_EyeImplantPathMarksComplete()
    {
        var pair = Pair;
        var server = pair.Server;
        var entMan = server.EntMan;
        var surgerySys = server.System<TutorialStarlightSurgerySystem>();

        await server.WaitAssertion(() =>
        {
            var patient = entMan.Spawn("TutorialPracticeMobStarlightSurgery");
            var surgeon = entMan.Spawn("MobHuman");
            Assert.That(entMan.HasComponent<TutorialStarlightSurgeryTargetComponent>(patient));

            bool Step(string surgery, string step) =>
                surgerySys.TryForceCompleteStep(patient, surgeon, "Head", surgery, step, skipToolCheck: true);

            Assert.That(Step("TutorialSurgeryOpenIncision", "open-scalpel"), Is.True);
            Assert.That(Step("TutorialSurgeryOpenIncision", "clamp-bleeders"), Is.True);
            Assert.That(Step("TutorialSurgeryOpenIncision", "retract-skin"), Is.True);

            Assert.That(Step("TutorialSurgeryImplantEyeImplant", "prepare-site"), Is.True);
            Assert.That(Step("TutorialSurgeryImplantEyeImplant", "insert-implant"), Is.True);
            Assert.That(Step("TutorialSurgeryImplantEyeImplant", "connect-nerve"), Is.True);

            Assert.That(Step("TutorialSurgeryCloseIncisionHead", "rejoin-vessels"), Is.True);
            Assert.That(Step("TutorialSurgeryCloseIncisionHead", "mend-skull"), Is.True);
            Assert.That(Step("TutorialSurgeryCloseIncisionHead", "close-incision"), Is.True);

            var target = entMan.GetComponent<TutorialStarlightSurgeryTargetComponent>(patient);
            Assert.That(target.HasEyeImplant, Is.True);
            Assert.That(target.ExampleSurgeryComplete, Is.True);
            Assert.That(entMan.HasComponent<Content.Shared.Eye.Blinding.Components.EyeProtectionComponent>(patient), Is.True);
        });
    }

    [Test]
    public async Task TutorialSurgeryCyberMed_HeartImplantPathMarksComplete()
    {
        var pair = Pair;
        var server = pair.Server;
        var entMan = server.EntMan;
        var surgerySys = server.System<TutorialCyberMedSurgerySystem>();

        await server.WaitAssertion(() =>
        {
            var patient = entMan.Spawn("TutorialPracticeMobCyberMedSurgery");
            var analyzer = entMan.Spawn("TutorialCyberMedAnalyzer");
            var surgeon = entMan.Spawn("MobHuman");

            var analyzerComp = entMan.GetComponent<TutorialCyberMedAnalyzerComponent>(analyzer);
            analyzerComp.ScannedPatient = patient;
            analyzerComp.SelectedPart = "Torso";
            entMan.Dirty(analyzer, analyzerComp);

            bool Step(string stepId) =>
                surgerySys.TryForceCompleteStep(analyzer, surgeon, stepId, skipToolCheck: true);

            Assert.That(Step("create-incision"), Is.True);
            Assert.That(Step("clamp-vessels"), Is.True);
            Assert.That(Step("retract-skin"), Is.True);
            Assert.That(Step("cut-bone"), Is.True);
            Assert.That(Step("marrow-bleeding"), Is.True);
            Assert.That(Step("retract-tissue"), Is.True);
            Assert.That(Step("insert-organ"), Is.True);
            Assert.That(Step("maintain-alignment"), Is.True);
            Assert.That(Step("seal-bleed-points"), Is.True);
            Assert.That(Step("repair-bone"), Is.True);
            Assert.That(Step("release-retractor"), Is.True);
            Assert.That(Step("reconnect-vessels"), Is.True);
            Assert.That(Step("seal-skin"), Is.True);

            var target = entMan.GetComponent<TutorialCyberMedSurgeryTargetComponent>(patient);
            Assert.That(target.HasCyberHeart, Is.True);
            Assert.That(target.ExampleSurgeryComplete, Is.True);
        });
    }

    [Test]
    public async Task TutorialSurgeryUis_AreRoleLocked()
    {
        var pair = Pair;
        var server = pair.Server;
        var entMan = server.EntMan;
        var proto = server.ProtoMan;

        await server.WaitAssertion(() =>
        {
            var starlight = proto.Index<TutorialRolePrototype>("TutorialSurgeryStarlight");
            var cyberMed = proto.Index<TutorialRolePrototype>("TutorialSurgeryCyberMed");
            Assert.That(starlight.ID, Is.EqualTo(TutorialSurgeryRoleLock.StarlightRoleId));
            Assert.That(cyberMed.ID, Is.EqualTo(TutorialSurgeryRoleLock.CyberMedRoleId));

            var starlightPatient = entMan.Spawn("TutorialPracticeMobStarlightSurgery");
            var cyberPatient = entMan.Spawn("TutorialPracticeMobCyberMedSurgery");
            Assert.That(entMan.GetComponent<TutorialStarlightSurgeryTargetComponent>(starlightPatient).RequiredRoleId,
                Is.EqualTo(TutorialSurgeryRoleLock.StarlightRoleId));
            Assert.That(entMan.GetComponent<TutorialCyberMedSurgeryTargetComponent>(cyberPatient).RequiredRoleId,
                Is.EqualTo(TutorialSurgeryRoleLock.CyberMedRoleId));

            // Cross-tutorial patients must not share the other surgery target component.
            Assert.That(entMan.HasComponent<TutorialCyberMedSurgeryTargetComponent>(starlightPatient), Is.False);
            Assert.That(entMan.HasComponent<TutorialStarlightSurgeryTargetComponent>(cyberPatient), Is.False);

            var analyzer = entMan.Spawn("TutorialCyberMedAnalyzer");
            Assert.That(entMan.GetComponent<TutorialCyberMedAnalyzerComponent>(analyzer).RequiredRoleId,
                Is.EqualTo(TutorialSurgeryRoleLock.CyberMedRoleId));
            // Standalone CyberMed analyzer must not expose the stock health analyzer UI key.
            Assert.That(entMan.HasComponent<Content.Server.Medical.Components.HealthAnalyzerComponent>(analyzer), Is.False);
        });
    }

    [Test]
    public async Task TutorialPracticeEntities_PuddleMarkerAndDamagedMobSpawn()
    {
        var pair = Pair;
        var server = pair.Server;
        var ticker = server.System<GameTicker>();
        var maps = server.System<TutorialMapSystem>();
        var proto = server.ProtoMan;
        var damageable = server.System<DamageableSystem>();

        await server.WaitPost(() =>
        {
            ticker.SetGamePreset("TutorialServer");
            ticker.StartGameRule(TutorialRule, out _);
            ticker.StartRound();
        });
        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            var doctor = proto.Index<TutorialRolePrototype>("TutorialMedicalDoctor");
            Assert.That(maps.TryLoadTutorialMap(doctor, out var mapUid, out _, out var spawn), Is.True);

            var puddle = server.EntMan.SpawnEntity("TutorialPracticePuddleBlood", spawn);
            var marker = server.EntMan.EnsureComponent<TutorialStepMarkerComponent>(puddle);
            marker.MarkerId = "blood-puddle";
            Assert.That(server.EntMan.HasComponent<PuddleComponent>(puddle));
            Assert.That(marker.MarkerId, Is.EqualTo("blood-puddle"));

            var dummy = server.EntMan.SpawnEntity("TutorialPracticeMobDamaged", spawn);
            Assert.That(server.EntMan.TryGetComponent<TutorialPracticeMobComponent>(dummy, out var practice));
            Assert.That(practice!.SpawnDamage.GetTotal().Float(), Is.GreaterThan(0f));
            damageable.TryChangeDamage(dummy, practice.SpawnDamage, ignoreResistances: true, interruptsDoAfters: false);
            Assert.That(damageable.GetTotalDamage(dummy).Float(), Is.GreaterThan(5f));
            Assert.That(server.EntMan.HasComponent<CuffableComponent>(dummy));

            maps.UnloadTutorialMap(mapUid);
        });
    }

    [Test]
    public async Task TutorialPracticeMobs_HaveRoleNamesAndVocalizerDatasets()
    {
        var pair = Pair;
        var server = pair.Server;
        var maps = server.System<TutorialMapSystem>();
        var proto = server.ProtoMan;
        var entMan = server.EntMan;

        await server.WaitAssertion(() =>
        {
            var doctor = proto.Index<TutorialRolePrototype>("TutorialMedicalDoctor");
            Assert.That(maps.TryLoadTutorialMap(doctor, out var mapUid, out _, out var spawn), Is.True);

            void AssertSpeakingMob(string protoId, string expectedName, string expectedDataset)
            {
                var uid = entMan.SpawnEntity(protoId, spawn);
                var meta = entMan.GetComponent<MetaDataComponent>(uid);
                Assert.That(meta.EntityName, Is.EqualTo(expectedName), protoId);
                Assert.That(entMan.HasComponent<TutorialPracticeMobComponent>(uid), Is.True, protoId);
                Assert.That(entMan.HasComponent<VocalizerComponent>(uid), Is.True, protoId);
                Assert.That(entMan.TryGetComponent<DatasetVocalizerComponent>(uid, out var vocal), Is.True, protoId);
                Assert.That(vocal!.Dataset.Id, Is.EqualTo(expectedDataset), protoId);
            }

            AssertSpeakingMob("TutorialPracticeMobCriminal", "Urist McCriminal", "TutorialPracticeMobCriminalAds");
            AssertSpeakingMob("TutorialPracticeMobSuspect", "Urist McSuspect", "TutorialPracticeMobSuspectAds");
            AssertSpeakingMob("TutorialPracticeMobPatient", "Urist McPatient", "TutorialPracticeMobPatientAds");
            AssertSpeakingMob("TutorialPracticeMobCasualty", "Urist McCasualty", "TutorialPracticeMobCasualtyAds");
            AssertSpeakingMob("TutorialPracticeMobCorpse", "Urist McCorpse", "TutorialPracticeMobPatientAds");
            AssertSpeakingMob("TutorialPracticeMobParishioner", "Urist McParishioner", "TutorialPracticeMobParishionerAds");
            AssertSpeakingMob("TutorialPracticeMobAudience", "Urist McAudience", "TutorialPracticeMobAudienceAds");
            AssertSpeakingMob("TutorialPracticeMobVictim", "Urist McVictim", "TutorialPracticeMobVictimAds");

            Assert.That(proto.Index<TutorialRolePrototype>("TutorialSecurityOfficer").PracticeSpawns
                .Any(p => p.Id == "TutorialPracticeMobCriminal"));
            Assert.That(proto.Index<TutorialRolePrototype>("TutorialTraitor").PracticeSpawns
                .Any(p => p.Id == "TutorialPracticeMobVictim"));
            Assert.That(proto.Index<TutorialRolePrototype>("TutorialParamedic").PracticeSpawns
                .Any(p => p.Id == "TutorialPracticeMobCasualty"));

            maps.UnloadTutorialMap(mapUid);
        });
    }

    [Test]
    public async Task TutorialRoles_ChamberCountFollowsPracticeSpawns()
    {
        var pair = Pair;
        var server = pair.Server;
        var proto = server.ProtoMan;

        await server.WaitAssertion(() =>
        {
            foreach (var role in proto.EnumeratePrototypes<TutorialRolePrototype>())
            {
                if (role.Stub || role.Goals.Count == 0)
                    continue;

                // Skip shuttle/salvage/nukeops/whole-map arenas — they do not stamp chambers.
                if (role.ShuttleArena != null || role.SalvageArena != null || role.NukeopsOutpost)
                    continue;

                if (role.RoomTemplate == null && role.Room == null)
                    continue;

                var copies = TutorialMapSystem.ResolveCopyCount(role);
                var maxSpawnRoom = role.PracticeSpawns.Count == 0
                    ? 0
                    : role.PracticeSpawns.Max(p => p.Room);
                Assert.That(copies, Is.EqualTo(Math.Max(1, maxSpawnRoom + 1)),
                    $"{role.ID}: copy count must follow practice chambers, not goal count");
            }
        });
    }

    [Test]
    public async Task TutorialPassenger_TeachesGeneralMechanics()
    {
        var pair = Pair;
        var server = pair.Server;
        var proto = server.ProtoMan;

        await server.WaitAssertion(() =>
        {
            var passenger = proto.Index<TutorialRolePrototype>("TutorialPassenger");
            Assert.That(passenger.Stub, Is.False);
            Assert.That(passenger.Name, Is.EqualTo("tutorial-job-passenger-name"));
            Assert.That(passenger.AutoOpenGuide, Is.False);

            var goalIds = passenger.Goals.Select(g => g.Id).ToArray();
            Assert.That(goalIds, Does.Contain("welcome"));
            Assert.That(goalIds, Does.Not.Contain("move"));
            Assert.That(goalIds, Does.Not.Contain("pickup"));
            Assert.That(goalIds, Does.Not.Contain("approach"));
            Assert.That(goalIds, Does.Not.Contain("inventory"));
            Assert.That(goalIds, Does.Not.Contain("drink"));
            Assert.That(goalIds, Does.Contain("crowbar-door"));
            Assert.That(TutorialMapSystem.ResolveCopyCount(passenger), Is.EqualTo(2),
                "Passenger tutorial should stamp exactly two chambers");

            var welcome = passenger.Goals[0];
            Assert.That(welcome.Id, Is.EqualTo("welcome"));
            Assert.That(welcome.SubGoals[0].Id, Is.EqualTo("meet-trainer"));
            Assert.That(welcome.SubGoals[0].Complete, Is.EqualTo(TutorialStepComplete.ReachMarker));
            Assert.That(welcome.SubGoals[0].Marker, Is.EqualTo("trainer-spot"));
            Assert.That(welcome.SubGoals.Any(s => s.Complete == TutorialStepComplete.DropItem));
            Assert.That(welcome.SubGoals.Any(s => s.Complete == TutorialStepComplete.StowItem),
                "Stow stays in welcome so flashlight/drink do not change rooms");
            Assert.That(welcome.SubGoals.Any(s => s.Entity == new EntProtoId("DrinkWaterBottleFull")),
                "Drinking is taught in the same welcome chamber");

            var crowbarDoor = passenger.Goals.First(g => g.Id == "crowbar-door");
            Assert.That(crowbarDoor.SubGoals.Any(s =>
                s.Complete == TutorialStepComplete.InteractTargetTag && s.Tag == "TutorialAirlock"));

            Assert.That(passenger.RoomTemplate, Is.EqualTo(new ProtoId<TutorialRoomTemplatePrototype>("TutorialSectionArrivals")));
            Assert.That(passenger.PracticeSpawns.Count(p => p.Id == "Crowbar"), Is.EqualTo(1));
            Assert.That(passenger.PracticeSpawns.Count(p => p.Id == "FlashlightLantern"), Is.EqualTo(1));
            Assert.That(passenger.PracticeSpawns.Count(p => p.Id == "DrinkWaterBottleFull"), Is.EqualTo(1));
            Assert.That(passenger.PracticeSpawns.Any(p => p.Id == "ClosetToolFilled"), Is.False);
            Assert.That(passenger.PracticeSpawns.Any(p => p.Id == "TutorialPassengerTrainer"), Is.False,
                "Passenger mentor is session-spawned, not a practiceSpawn");
            Assert.That(passenger.PracticeSpawns.Any(p => p.Id == "TutorialPassengerMentor"), Is.False);
            Assert.That(TutorialServerRuleSystem.UsesTravelingCoach(passenger), Is.False);
            Assert.That(passenger.MentorName, Is.EqualTo("Urist McGreentide"));
            Assert.That(proto.Index<TutorialRolePrototype>("TutorialMedicalDoctor").MentorName,
                Is.EqualTo("Urist McMalpractice"));
            Assert.That(proto.Index<TutorialRolePrototype>("TutorialBartender").MentorName,
                Is.EqualTo("Urist McDrunkard"));
            Assert.That(passenger.PracticeSpawns.Any(p => p.Id == "Crowbar" && p.Room == 0),
                "Crowbar must spawn in the trainer room");
            Assert.That(passenger.PracticeSpawns.Where(p => p.Marker != "passenger-exit").All(p => p.Room == 0),
                "Practice props belong in chamber 0; only the pry-exit marker is in chamber 1");
            Assert.That(passenger.PracticeSpawns.Any(p => p.Marker == "passenger-exit" && p.Room == 1),
                "Passenger needs a chamber-1 marker so the arrivals exit stamps a second room");
        });
    }

    [Test]
    public async Task TutorialRolePicker_StubsAreMarked()
    {
        var pair = Pair;
        var server = pair.Server;
        var proto = server.ProtoMan;

        await server.WaitAssertion(() =>
        {
            Assert.That(proto.TryIndex<TutorialRolePrototype>("TutorialLawyer", out var stub));
            Assert.That(stub!.Stub, Is.True);
            Assert.That(proto.TryIndex<TutorialRolePrototype>("TutorialSecurityCadet", out var cadet));
            Assert.That(cadet!.Stub, Is.False);
            Assert.That(proto.TryIndex<TutorialRolePrototype>("TutorialSalvageSpecialist", out var salvage));
            Assert.That(salvage!.Stub, Is.False);

            var stubs = proto.EnumeratePrototypes<TutorialRolePrototype>().Count(r => r.Stub);
            var ready = proto.EnumeratePrototypes<TutorialRolePrototype>().Count(r => !r.Stub);
            Assert.That(stubs, Is.GreaterThan(0));
            Assert.That(ready, Is.GreaterThan(0));
        });
    }

    [Test]
    public async Task TutorialRolePicker_OrdersStartHereThenDepartmentsThenAntagsAndOmitsErt()
    {
        var pair = Pair;
        var server = pair.Server;
        var proto = server.ProtoMan;
        var tutorial = server.System<TutorialServerRuleSystem>();

        await server.WaitAssertion(() =>
        {
            Assert.That(proto.HasIndex<TutorialRolePrototype>("TutorialERTLeader"), Is.False,
                "ERT tutorial stubs should be removed");

            var entries = tutorial.BuildPickerEntries();
            Assert.That(entries, Is.Not.Empty);
            // Start Here is in teaching order: movement first, then handling objects.
            Assert.That(entries[0].RoleId, Is.EqualTo("TutorialBasics"));
            Assert.That(entries[1].RoleId, Is.EqualTo("TutorialItems"));
            Assert.That(entries.Any(e => e.RoleId.Contains("ERT", StringComparison.OrdinalIgnoreCase)), Is.False);

            var stationJobs = entries.Where(e => e.Category == "Station Jobs").Select(e => e.RoleId).ToList();
            Assert.That(stationJobs, Is.EqualTo(new[]
            {
                "TutorialTide",
                "TutorialCargoTechnician",
                "TutorialMedicalDoctor",
                "TutorialChemist",
                "TutorialTechnicalAssistant",
            }));

            var firstAntag = entries.FindIndex(e => e.Category is "Antagonist" or "Wizden antagonists");
            Assert.That(firstAntag, Is.GreaterThan(0));
            Assert.That(entries.Skip(firstAntag).All(e => e.Category is "Antagonist" or "Wizden antagonists"), Is.True,
                "Antagonist tutorials must be grouped at the bottom");

            var lastNonAntag = entries.Take(firstAntag).Last();
            Assert.That(lastNonAntag.Category is not ("Antagonist" or "Wizden antagonists"));
            Assert.That(entries.Take(firstAntag).Any(e => e.Category is "Command" or "Security" or "Medical"),
                Is.True,
                "Department roles should appear before antagonists");

            Assert.That(entries.Single(e => e.RoleId == "TutorialAntagDragon").Category, Is.EqualTo("Antagonist"));
            Assert.That(entries.Single(e => e.RoleId == "TutorialAntagNukeops").Category, Is.EqualTo("Antagonist"));
            Assert.That(entries.Single(e => e.RoleId == "TutorialAntagNukeopsCommander").Category,
                Is.EqualTo("Wizden antagonists"));
            Assert.That(entries.Single(e => e.RoleId == "TutorialAntagNukeopsMedic").Category,
                Is.EqualTo("Wizden antagonists"));

            var serverSpecific = entries.Where(e => e.Category == "Server specific").ToList();
            Assert.That(serverSpecific, Is.Not.Empty);
            Assert.That(serverSpecific.Select(e => e.SubCategory), Is.EquivalentTo(new[] { "BPL14", "Starlight", "Starlight", "Starlight" }));
            Assert.That(serverSpecific.Single(e => e.RoleId == "TutorialSurgeryCyberMed").DisplayName, Is.EqualTo("Surgery"));
            Assert.That(serverSpecific.Single(e => e.RoleId == "TutorialSurgeryStarlight").DisplayName, Is.EqualTo("Surgery"));
            Assert.That(serverSpecific.Single(e => e.RoleId == "TutorialAntagVampire").SubCategory, Is.EqualTo("Starlight"));
            Assert.That(serverSpecific.Single(e => e.RoleId == "TutorialAntagChangeling").SubCategory, Is.EqualTo("Starlight"));
        });
    }

    [Test]
    public async Task TutorialRolePicker_LiveTutorials_ShowsOnlyAllowlistedRoles()
    {
        var pair = Pair;
        var server = pair.Server;
        var tutorial = server.System<TutorialServerRuleSystem>();
        var cfg = server.ResolveDependency<IConfigurationManager>();

        await server.WaitAssertion(() =>
        {
            cfg.SetCVar(TutorialCVars.LiveTutorials, true);
            try
            {
                var entries = tutorial.BuildPickerEntries();
                Assert.That(entries.Select(e => e.RoleId), Is.EqualTo(new[]
                {
                    "TutorialBasics",
                    "TutorialItems",
                    "TutorialTide",
                    "TutorialCargoTechnician",
                    "TutorialMedicalDoctor",
                    "TutorialChemist",
                    "TutorialAntagDragon",
                    "TutorialAntagNukeops",
                }));
                Assert.That(entries.Select(e => e.Category).Distinct(),
                    Is.EqualTo(new[] { "Start Here", "Station Jobs", "Antagonist" }));
                Assert.That(entries.All(e => !e.Stub), Is.True);
                Assert.That(entries.Any(e => e.RoleId is "TutorialAntagNukeopsCommander" or "TutorialAntagNukeopsMedic"),
                    Is.False);
            }
            finally
            {
                cfg.SetCVar(TutorialCVars.LiveTutorials, false);
            }
        });
    }

    [Test]
    public async Task TutorialRolePicker_Development_PrefixesNonLiveAsStubs()
    {
        var pair = Pair;
        var server = pair.Server;
        var tutorial = server.System<TutorialServerRuleSystem>();
        var cfg = server.ResolveDependency<IConfigurationManager>();

        await server.WaitAssertion(() =>
        {
            cfg.SetCVar(TutorialCVars.LiveTutorials, false);

            var entries = tutorial.BuildPickerEntries();
            Assert.That(entries.Single(e => e.RoleId == "TutorialBasics").Stub, Is.False);
            Assert.That(entries.Single(e => e.RoleId == "TutorialCargoTechnician").Stub, Is.False);
            Assert.That(entries.Single(e => e.RoleId == "TutorialAntagNukeops").Stub, Is.False);

            Assert.That(entries.Single(e => e.RoleId == "TutorialChef").Stub, Is.True,
                "Non-live tutorials must show as stubs in development");
            Assert.That(entries.Single(e => e.RoleId == "TutorialPassenger").Stub, Is.True);
            Assert.That(entries.Single(e => e.RoleId == "TutorialAntagNukeopsCommander").Stub, Is.True);
            Assert.That(entries.Single(e => e.RoleId == "TutorialLawyer").Stub, Is.True);
        });
    }

    [Test]
    public async Task TutorialServer_VoxIsNotRoundstartSpecies()
    {
        var pair = Pair;
        var server = pair.Server;
        var proto = server.ProtoMan;

        await server.WaitAssertion(() =>
        {
            Assert.That(proto.Index<SpeciesPrototype>("Vox").RoundStart, Is.False,
                "Vox nitrogen internals are too advanced for the tutorial server");

            var weights = proto.Index<WeightedRandomSpeciesPrototype>("SpeciesWeights");
            Assert.That(weights.Weights.ContainsKey("Vox"), Is.False,
                "Random species weights must not pick Vox");
        });
    }

    [Test]
    public async Task TutorialRolePicker_PrefersAntagNameOverJob()
    {
        var pair = Pair;
        var server = pair.Server;
        var proto = server.ProtoMan;
        var tutorial = server.System<TutorialServerRuleSystem>();

        await server.WaitAssertion(() =>
        {
            var traitor = proto.Index<TutorialRolePrototype>("TutorialTraitor");
            Assert.That(traitor.Job, Is.EqualTo(new ProtoId<JobPrototype>("Passenger")));
            Assert.That(traitor.Antag, Is.EqualTo(new ProtoId<AntagPrototype>("Traitor")));
            Assert.That(string.IsNullOrEmpty(traitor.Name), Is.True);

            var antagName = Loc.GetString(proto.Index(traitor.Antag.Value).Name);
            var jobName = proto.Index(traitor.Job.Value).LocalizedName;
            Assert.That(antagName, Is.Not.EqualTo(jobName));

            Assert.That(tutorial.GetRoleDisplayName(traitor), Is.EqualTo(antagName));
            Assert.That(tutorial.GetRoleDisplayName(traitor), Is.Not.EqualTo(jobName));

            var passenger = proto.Index<TutorialRolePrototype>("TutorialPassenger");
            Assert.That(tutorial.GetRoleDisplayName(passenger), Is.EqualTo(Loc.GetString(passenger.Name)));

            var bartender = proto.Index<TutorialRolePrototype>("TutorialBartender");
            Assert.That(tutorial.GetRoleDisplayName(bartender),
                Is.EqualTo(proto.Index(bartender.Job!.Value).LocalizedName));
        });
    }

    [Test]
    public async Task TutorialSpawn_CreatesPrivateMapPerPlayer()
    {
        var pair = Pair;
        var server = pair.Server;
        var ticker = server.System<GameTicker>();
        var maps = server.System<TutorialMapSystem>();
        var proto = server.ProtoMan;

        await server.WaitPost(() =>
        {
            ticker.SetGamePreset("TutorialServer");
            ticker.StartGameRule(TutorialRule, out _);
            ticker.StartRound();
        });
        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            var role = proto.Index<TutorialRolePrototype>("TutorialJanitor");
            Assert.That(maps.TryLoadTutorialMap(role, out var mapA, out var gridA, out _), Is.True);
            Assert.That(maps.TryLoadTutorialMap(role, out var mapB, out var gridB, out _), Is.True);
            Assert.That(mapA, Is.Not.EqualTo(mapB));
            Assert.That(gridA, Is.Not.EqualTo(gridB));

            var mapIdA = server.EntMan.GetComponent<MapComponent>(mapA).MapId;
            var mapIdB = server.EntMan.GetComponent<MapComponent>(mapB).MapId;
            Assert.That(mapIdA, Is.Not.EqualTo(mapIdB));

            maps.UnloadTutorialMap(mapA);
            maps.UnloadTutorialMap(mapB);
        });
    }

    [Test]
    public async Task TutorialScientist_TeachesAnomalySpawnScanStabilizeRemove()
    {
        var pair = Pair;
        var server = pair.Server;
        var ticker = server.System<GameTicker>();
        var maps = server.System<TutorialMapSystem>();
        var proto = server.ProtoMan;
        var anomalySys = server.System<Content.Server.Anomaly.AnomalySystem>();

        await server.WaitPost(() =>
        {
            ticker.SetGamePreset("TutorialServer");
            ticker.StartGameRule(TutorialRule, out _);
            ticker.StartRound();
        });
        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            var sci = proto.Index<TutorialRolePrototype>("TutorialScientist");
            var completes = sci.Goals.SelectMany(g => g.SubGoals).Select(s => s.Complete).ToArray();
            Assert.That(completes, Does.Contain(TutorialStepComplete.SpawnAnomaly));
            Assert.That(completes, Does.Contain(TutorialStepComplete.ScanAnomaly));
            Assert.That(completes, Does.Contain(TutorialStepComplete.StabilizeAnomaly));
            Assert.That(completes, Does.Contain(TutorialStepComplete.RemoveAnomaly));
            Assert.That(sci.PracticeSpawns.Any(p => p.Id == "TutorialAnomalySpawnPad"));
            Assert.That(sci.PracticeSpawns.Any(p => p.Id == "TutorialWeaponPistolCHIMP"));
            Assert.That(sci.PracticeSpawns.Any(p => p.Id == "TutorialMachineAPE"));

            Assert.That(maps.TryLoadTutorialMap(sci, out var mapUid, out var gridUid, out var spawn), Is.True);

            // Practice kits spawn on player enter; spawn the pad/anomaly directly here.
            var pad = server.EntMan.SpawnEntity("TutorialAnomalySpawnPad", spawn);
            Assert.That(server.EntMan.HasComponent<TutorialAnomalySpawnerComponent>(pad));

            var spawner = server.EntMan.GetComponent<TutorialAnomalySpawnerComponent>(pad);
            var coords = server.EntMan.GetComponent<TransformComponent>(pad).Coordinates.Offset(spawner.Offset);
            var anomaly = server.EntMan.SpawnEntity("TutorialAnomaly", coords);
            Assert.That(server.EntMan.HasComponent<TutorialAnomalyComponent>(anomaly));

            Assert.That(server.EntMan.TryGetComponent<AnomalyComponent>(anomaly, out var anom));
            Assert.That(anom!.LockParticles, Is.True);
            Assert.That(anom.WeakeningParticleType, Is.EqualTo(AnomalousParticleType.Zeta));
            Assert.That(anom.SeverityParticleType, Is.EqualTo(AnomalousParticleType.Delta));
            Assert.That(anom.DestabilizingParticleType, Is.EqualTo(AnomalousParticleType.Epsilon));
            Assert.That(anom.TransformationParticleType, Is.EqualTo(AnomalousParticleType.Sigma));
            Assert.That(anom.Stability, Is.EqualTo(0.55f).Within(0.01f));

            // Reshuffle must not change a locked tutorial anomaly's roles.
            anomalySys.ShuffleParticlesEffect((anomaly, anom));
            Assert.That(anom.WeakeningParticleType, Is.EqualTo(AnomalousParticleType.Zeta),
                "LockParticles should keep containment as Zeta even if ShuffleParticlesEffect is called");

            // Decay path: containment damage health until the anomaly ends.
            anomalySys.ChangeAnomalyHealth(anomaly, -1.1f, anom);
            Assert.That(
                !server.EntMan.EntityExists(anomaly) ||
                !server.EntMan.HasComponent<AnomalyComponent>(anomaly) ||
                server.EntMan.IsQueuedForDeletion(anomaly),
                "Draining health should end the practice anomaly");

            maps.UnloadTutorialMap(mapUid);
        });
    }

    [Test]
    public async Task TutorialCargoTechnician_TeachesShuttleFlightAndDocking()
    {
        var pair = Pair;
        var server = pair.Server;
        var proto = server.ProtoMan;

        await server.WaitAssertion(() =>
        {
            var cargo = proto.Index<TutorialRolePrototype>("TutorialCargoTechnician");
            Assert.That(cargo.ShuttleArena,
                Is.EqualTo(new ProtoId<TutorialShuttleArenaPrototype>("TutorialArenaCargoShuttle")));

            var arena = proto.Index<TutorialShuttleArenaPrototype>("TutorialArenaCargoShuttle");
            Assert.That(arena.ShuttleMap, Is.EqualTo(new Robust.Shared.Utility.ResPath("/Maps/Shuttles/cargo.yml")));

            var completes = cargo.Goals.SelectMany(g => g.SubGoals).Select(s => s.Complete).ToArray();
            Assert.That(completes, Does.Contain(TutorialStepComplete.PilotShuttle));
            Assert.That(completes, Does.Contain(TutorialStepComplete.ShuttleThrottle));
            Assert.That(completes, Does.Contain(TutorialStepComplete.NearDockStation));
            Assert.That(completes, Does.Contain(TutorialStepComplete.UndockShuttle));
            Assert.That(completes, Does.Contain(TutorialStepComplete.DockShuttle));

            // Dock welds pin the ship — undock must be taught before throttle/flight keys.
            var undockIdx = Array.IndexOf(completes, TutorialStepComplete.UndockShuttle);
            var throttleIdx = Array.IndexOf(completes, TutorialStepComplete.ShuttleThrottle);
            Assert.That(undockIdx, Is.GreaterThanOrEqualTo(0));
            Assert.That(throttleIdx, Is.GreaterThan(undockIdx),
                "Cargo tutorial must undock before teaching shuttle throttle");

            Assert.That(cargo.Goals.SelectMany(g => g.SubGoals)
                .Any(s => s.Id == "fly-ats" && s.Complete == TutorialStepComplete.NearDockStation
                          && s.Marker == "ats"));

            var markers = cargo.Goals.SelectMany(g => g.SubGoals)
                .Where(s => s.Complete is TutorialStepComplete.DockShuttle or TutorialStepComplete.UndockShuttle)
                .Select(s => s.Marker)
                .ToArray();
            Assert.That(markers, Does.Contain("cargo-bay"));
            Assert.That(markers, Does.Contain("ats"));

            Assert.That(cargo.AutoOpenGuide, Is.False);
            Assert.That(cargo.SpawnStationaryMentor, Is.True);
            Assert.That(cargo.MentorFollows, Is.False);
            Assert.That(cargo.MentorName, Is.EqualTo("Urist McQuartermaster"));

            Assert.That(cargo.Goals.Any(g => g.Id == "order"));
            Assert.That(cargo.Goals.SelectMany(g => g.SubGoals)
                .Any(s => s.Id == "orders" && s.Tag == "TutorialCargoOrders"));
            Assert.That(cargo.Goals.SelectMany(g => g.SubGoals)
                .Any(s => s.Id == "purchase" && s.Complete == TutorialStepComplete.CargoOrderAdded));
            Assert.That(cargo.Goals.SelectMany(g => g.SubGoals)
                .Any(s => s.Id == "confirm" && s.Complete == TutorialStepComplete.CargoOrderApproved));
            Assert.That(cargo.Goals.SelectMany(g => g.SubGoals)
                .Any(s => s.Id == "drag-crate" && s.Complete == TutorialStepComplete.PullTag
                          && s.Tag == "TutorialCargoBayCrate"));
            Assert.That(cargo.Goals.SelectMany(g => g.SubGoals)
                .Any(s => s.Id == "board-shuttle" && s.Complete == TutorialStepComplete.ReachMarker
                          && s.Marker == "cargo-shuttle"));
            Assert.That(cargo.Goals.SelectMany(g => g.SubGoals)
                .Any(s => s.Id == "controls" && s.Complete == TutorialStepComplete.Acknowledge));
            Assert.That(cargo.Goals.SelectMany(g => g.SubGoals)
                .Any(s => s.Id == "sell-crate" && s.Complete == TutorialStepComplete.CargoSold
                          && s.Tag == "TutorialCargoBayCrate"));
            Assert.That(cargo.Goals.SelectMany(g => g.SubGoals)
                .Any(s => s.Id == "retrieve" && s.Complete == TutorialStepComplete.PullTag
                          && s.Tag == "TutorialCargoPurchase"));

            var subIds = cargo.Goals.SelectMany(g => g.SubGoals).Select(s => s.Id).ToArray();
            var sellIdx = Array.IndexOf(subIds, "sell-crate");
            var retrieveIdx = Array.IndexOf(subIds, "retrieve");
            Assert.That(sellIdx, Is.GreaterThanOrEqualTo(0));
            Assert.That(retrieveIdx, Is.GreaterThan(sellIdx),
                "Sell the hauled crate before retrieving the purchase");

            // Trimmed curriculum: no long Acknowledge walls before sensors.
            Assert.That(subIds, Does.Not.Contain("undock-explain"));
            Assert.That(subIds, Does.Not.Contain("look-around"));
            Assert.That(subIds, Does.Not.Contain("sell-pallet"));
        });
    }

    [Test]
    public async Task TutorialCurriculumObjectives_AppearInMindAndTrackProgress()
    {
        var pair = Pair;
        var server = pair.Server;
        var ticker = server.System<GameTicker>();
        var tutorial = server.System<TutorialServerRuleSystem>();
        var minds = server.System<SharedMindSystem>();
        var objectives = server.System<SharedObjectivesSystem>();
        var proto = server.ProtoMan;

        await server.WaitPost(() =>
        {
            ticker.SetGamePreset("TutorialServer");
            ticker.StartGameRule(TutorialRule, out _);
            ticker.StartRound();
        });
        await pair.RunTicksSync(5);

        // Cargo starts on an Acknowledge tip so we can advance goal 0 without sensors.
        await server.WaitPost(() =>
        {
            tutorial.TrySelectRole(pair.Player!, "TutorialCargoTechnician", confirmedStub: false);
        });
        await pair.RunTicksSync(60);

        await server.WaitAssertion(() =>
        {
            var player = pair.Player!;
            Assert.That(player.AttachedEntity, Is.Not.Null);
            var mob = player.AttachedEntity!.Value;
            var role = proto.Index<TutorialRolePrototype>("TutorialCargoTechnician");

            Assert.That(minds.TryGetMind(mob, out var mindId, out var mind), Is.True);

            var goalObjectives = mind!.Objectives
                .Where(uid => server.EntMan.HasComponent<TutorialGoalConditionComponent>(uid))
                .ToList();
            Assert.That(goalObjectives.Count, Is.EqualTo(role.Goals.Count),
                "Each curriculum goal should be a Character objective");

            var first = goalObjectives
                .Select(uid => (uid, cond: server.EntMan.GetComponent<TutorialGoalConditionComponent>(uid)))
                .First(t => t.cond.GoalIndex == 0);

            var progressBefore = objectives.GetProgress(first.uid, (mindId, mind));
            Assert.That(progressBefore, Is.Not.Null);
            Assert.That(progressBefore!.Value, Is.LessThan(1f));

            Assert.That(server.EntMan.TryGetComponent<TutorialParticipantComponent>(mob, out var part));
            Assert.That(part!.StepComplete, Is.EqualTo(TutorialStepComplete.Acknowledge));
            tutorial.AdvanceSubGoal(mob);
        });
        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            var mob = pair.Player!.AttachedEntity!.Value;
            Assert.That(minds.TryGetMind(mob, out var mindId, out var mind), Is.True);
            Assert.That(server.EntMan.TryGetComponent<TutorialParticipantComponent>(mob, out var part));
            Assert.That(part!.GoalIndex, Is.GreaterThan(0), "Advancing cargo intro should complete welcome goal");

            var first = mind!.Objectives
                .Select(uid => (uid, cond: server.EntMan.GetComponentOrNull<TutorialGoalConditionComponent>(uid)))
                .First(t => t.cond != null && t.cond.GoalIndex == 0);

            var progress = objectives.GetProgress(first.uid, (mindId, mind));
            Assert.That(progress, Is.Not.Null);
            Assert.That(progress!.Value, Is.EqualTo(1f).Within(0.001f));
        });
    }

    [Test]
    public async Task TutorialTrainer_DoesNotAutoRemindOnTimer()
    {
        var pair = Pair;
        var server = pair.Server;
        var ticker = server.System<GameTicker>();
        var tutorial = server.System<TutorialServerRuleSystem>();
        var timing = server.ResolveDependency<IGameTiming>();

        await server.WaitPost(() =>
        {
            ticker.SetGamePreset("TutorialServer");
            ticker.StartGameRule(TutorialRule, out _);
            ticker.StartRound();
        });
        await pair.RunTicksSync(5);

        await server.WaitPost(() =>
        {
            tutorial.TrySelectRole(pair.Player!, "TutorialPassenger", confirmedStub: false);
        });
        await pair.RunTicksSync(60);

        string? spokenSubGoal = null;
        EntityUid mentorUid = default;

        await server.WaitAssertion(() =>
        {
            var player = pair.Player!;
            TutorialSessionData? session = null;
            var ruleQuery = server.EntMan.EntityQueryEnumerator<TutorialServerRuleComponent>();
            while (ruleQuery.MoveNext(out _, out var ruleComp))
            {
                if (ruleComp.Sessions.TryGetValue(player.UserId, out session))
                    break;
            }

            Assert.That(session, Is.Not.Null);
            mentorUid = session!.MentorUid;
            Assert.That(server.EntMan.TryGetComponent<TutorialTrainerComponent>(mentorUid, out var trainer));
            spokenSubGoal = trainer!.LastSpokenSubGoal;
            Assert.That(spokenSubGoal, Is.Not.Null.And.Not.Empty,
                "Mentor should speak once when the first sub-goal becomes current");

            // Old reminder cadence was 10s — advance time far past that.
            trainer.NextReminderAt = timing.CurTime - TimeSpan.FromSeconds(1);
            server.EntMan.Dirty(mentorUid, trainer);
        });

        await pair.RunTicksSync(30);

        await server.WaitAssertion(() =>
        {
            var trainer = server.EntMan.GetComponent<TutorialTrainerComponent>(mentorUid);
            Assert.That(trainer.LastSpokenSubGoal, Is.EqualTo(spokenSubGoal),
                "Mentor must not re-speak the same tip on a timer");
        });
    }

    [Test]
    public async Task TutorialSalvageArena_BuildsBayAndDebris()
    {
        var pair = Pair;
        var server = pair.Server;
        var ticker = server.System<GameTicker>();
        var maps = server.System<TutorialMapSystem>();
        var proto = server.ProtoMan;

        await server.WaitPost(() =>
        {
            ticker.SetGamePreset("TutorialServer");
            ticker.StartGameRule(TutorialRule, out _);
            ticker.StartRound();
        });
        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            var salvage = proto.Index<TutorialRolePrototype>("TutorialSalvageSpecialist");
            Assert.That(maps.TryLoadTutorialMap(salvage, out var mapUid, out var bayUid, out var spawn), Is.True);
            Assert.That(spawn.EntityId, Is.EqualTo(bayUid));

            var magnet = false;
            var recycler = false;
            var debrisLocker = false;
            var debrisMarker = false;
            var bayMarker = false;
            var mapXform = server.EntMan.GetComponent<TransformComponent>(mapUid);
            var query = server.EntMan.AllEntityQueryEnumerator<TransformComponent>();
            while (query.MoveNext(out var uid, out var xform))
            {
                if (xform.MapUid != mapXform.MapUid)
                    continue;

                var id = server.EntMan.GetComponent<MetaDataComponent>(uid).EntityPrototype?.ID;
                if (id == "TutorialSalvageMagnet")
                    magnet = true;
                if (id == "TutorialRecycler")
                    recycler = true;
                if (id == "TutorialDebrisLocker")
                    debrisLocker = true;

                if (server.EntMan.TryGetComponent<TutorialStepMarkerComponent>(uid, out var marker))
                {
                    if (marker.MarkerId == "debris-pass")
                        debrisMarker = true;
                    if (marker.MarkerId == "salvage-bay")
                        bayMarker = true;
                }
            }

            Assert.That(magnet, Is.True);
            Assert.That(recycler, Is.True);
            Assert.That(debrisLocker, Is.True);
            Assert.That(debrisMarker, Is.True);
            Assert.That(bayMarker, Is.True);

            // Suit-up foyer: spawn on steel (not lattice), minifan present.
            var minifan = false;
            var spawnTile = server.System<MapSystem>().GetTileRef(bayUid,
                server.EntMan.GetComponent<MapGridComponent>(bayUid),
                new Vector2i((int) MathF.Floor(spawn.Position.X), (int) MathF.Floor(spawn.Position.Y)));
            Assert.That(spawnTile.Tile.IsEmpty, Is.False);
            var spawnTileId = spawnTile.Tile.TypeId;
            var latticeId = ((ContentTileDefinition) server.ResolveDependency<ITileDefinitionManager>()["Lattice"]).TileId;
            Assert.That(spawnTileId, Is.Not.EqualTo(latticeId), "Salvage spawn must be in sealed foyer, not lattice");

            var foyerQuery = server.EntMan.AllEntityQueryEnumerator<TransformComponent>();
            while (foyerQuery.MoveNext(out var uid, out var xform))
            {
                if (xform.GridUid != bayUid)
                    continue;
                var id = server.EntMan.GetComponent<MetaDataComponent>(uid).EntityPrototype?.ID;
                if (id == "AtmosDeviceFanTiny")
                    minifan = true;
            }

            Assert.That(minifan, Is.True, "Salvage foyer doorway needs a minifan");

            maps.UnloadTutorialMap(mapUid);
        });
    }

    /// <summary>
    /// Magboots Z/Use quick-equips (boot swap) and must not finish the toggle step;
    /// ActionToggleMagboots does.
    /// </summary>
    [Test]
    public async Task TutorialSalvage_MagbootsToggleUsesActionNotUseInHand()
    {
        var pair = Pair;
        var server = pair.Server;
        var ticker = server.System<GameTicker>();
        var tutorial = server.System<TutorialServerRuleSystem>();
        var actions = server.System<SharedActionsSystem>();
        var hands = server.System<SharedHandsSystem>();
        var interaction = server.System<SharedInteractionSystem>();
        var entMan = server.EntMan;

        await server.WaitPost(() =>
        {
            ticker.SetGamePreset("TutorialServer");
            ticker.StartGameRule(TutorialRule, out _);
            ticker.StartRound();
        });
        await pair.RunTicksSync(5);

        await server.WaitPost(() =>
        {
            tutorial.TrySelectRole(pair.Player!, "TutorialSalvageSpecialist", confirmedStub: false);
        });
        await pair.RunTicksSync(60);

        EntityUid magboots = default;
        EntityUid mob = default;

        await server.WaitAssertion(() =>
        {
            mob = pair.Player!.AttachedEntity!.Value;

            // welcome.intro → eva.hold-magboots
            tutorial.AdvanceSubGoal(mob);

            Assert.That(entMan.TryGetComponent<TutorialParticipantComponent>(mob, out var part));
            Assert.That(part!.GoalIndex, Is.EqualTo(1));
            Assert.That(part.SubGoalIndex, Is.EqualTo(0));
            Assert.That(part.StepComplete, Is.EqualTo(TutorialStepComplete.HoldItem));

            var mapUid = entMan.GetComponent<TransformComponent>(mob).MapUid;
            var query = entMan.AllEntityQueryEnumerator<MetaDataComponent, TransformComponent>();
            while (query.MoveNext(out var uid, out var meta, out var xform))
            {
                if (xform.MapUid != mapUid)
                    continue;
                if (meta.EntityPrototype?.ID != "ClothingShoesBootsMag")
                    continue;
                magboots = uid;
                break;
            }

            Assert.That(magboots, Is.Not.EqualTo(EntityUid.Invalid), "Arena foyer must spawn magboots");
            Assert.That(hands.TryPickupAnyHand(mob, magboots, checkActionBlocker: false, animate: false), Is.True);
        });
        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.TryGetComponent<TutorialParticipantComponent>(mob, out var part));
            Assert.That(part!.GoalIndex, Is.EqualTo(1));
            Assert.That(part.SubGoalIndex, Is.EqualTo(1), "Holding magboots should advance to use-magboots");
            Assert.That(part.StepComplete, Is.EqualTo(TutorialStepComplete.ActionUsed));

            // Z/Use equips clothing — must not complete ActionUsed.
            Assert.That(interaction.UseInHandInteraction(mob, magboots), Is.True);
        });
        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.TryGetComponent<TutorialParticipantComponent>(mob, out var part));
            Assert.That(part!.SubGoalIndex, Is.EqualTo(1),
                "Use-in-hand (equip/swap boots) must not finish the magboots toggle step");
            Assert.That(part.StepComplete, Is.EqualTo(TutorialStepComplete.ActionUsed));

            // After equip, magboots may be worn; action still comes from the item.
            EntityUid? toggleAction = null;
            foreach (var (actionUid, _) in actions.GetActions(mob))
            {
                if (entMan.GetComponent<MetaDataComponent>(actionUid).EntityPrototype?.ID ==
                    "ActionToggleMagboots")
                {
                    toggleAction = actionUid;
                    break;
                }
            }

            Assert.That(toggleAction, Is.Not.Null, "Toggle Magboots action must be available when held or worn");
            actions.PerformAction(mob, (toggleAction.Value, entMan.GetComponent<ActionComponent>(toggleAction.Value)));
        });
        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.TryGetComponent<TutorialParticipantComponent>(mob, out var part));
            Assert.That(part!.GoalIndex, Is.EqualTo(1));
            Assert.That(part.SubGoalIndex, Is.EqualTo(2),
                "Performing ActionToggleMagboots must advance to hold-pka");
            Assert.That(part.StepComplete, Is.EqualTo(TutorialStepComplete.HoldItem));
        });
    }

    [Test]
    public async Task TutorialShuttleArena_BuildsFlyableDockableShuttle()
    {
        var pair = Pair;
        var server = pair.Server;
        var ticker = server.System<GameTicker>();
        var maps = server.System<TutorialMapSystem>();
        var proto = server.ProtoMan;

        await server.WaitPost(() =>
        {
            ticker.SetGamePreset("TutorialServer");
            ticker.StartGameRule(TutorialRule, out _);
            ticker.StartRound();
        });
        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            var cargo = proto.Index<TutorialRolePrototype>("TutorialCargoTechnician");
            Assert.That(cargo.SimplifiedEnvironment, Is.False,
                "Cargo keeps live atmos; shuttle arenas must force-power without SimplifiedEnvironment");
            Assert.That(maps.TryLoadTutorialMap(cargo, out var mapUid, out var shuttleUid, out var spawn), Is.True);
            Assert.That(server.EntMan.HasComponent<Content.Server.Shuttles.Components.ShuttleComponent>(shuttleUid));
            Assert.That(server.EntMan.HasComponent<Content.Shared.Cargo.Components.CargoShuttleComponent>(shuttleUid));

            EntityUid cargoBayUid = default;
            var bayQuery = server.EntMan.AllEntityQueryEnumerator<TutorialDockStationComponent, TransformComponent>();
            while (bayQuery.MoveNext(out var uid, out var station, out var xform))
            {
                if (xform.MapUid != server.EntMan.GetComponent<TransformComponent>(mapUid).MapUid)
                    continue;
                if (station.StationId == TutorialShuttleArenaSystem.CargoBayStationId)
                    cargoBayUid = uid;
            }

            Assert.That(cargoBayUid.IsValid(), Is.True, "Cargo bay mini-station missing before spawn assert");
            Assert.That(spawn.EntityId, Is.EqualTo(cargoBayUid), "Cargo Tech must spawn in the cargo bay");

            Assert.That(server.EntMan.TryGetComponent<PhysicsComponent>(shuttleUid, out var shuttlePhysics), Is.True);
            Assert.That(shuttlePhysics!.BodyType, Is.EqualTo(BodyType.Dynamic),
                "Cargo arena shuttle must be Dynamic or thrusters cannot move it after undock");

            Assert.That(server.EntMan.TryGetComponent<GridAtmosphereComponent>(shuttleUid, out var shuttleAtmos), Is.True);
            Assert.That(shuttleAtmos!.Simulated, Is.True,
                "Cargo shuttle should keep live atmos (not SimplifiedEnvironment freeze)");

            var dockCount = 0;
            var consoleCount = 0;
            var thrusterCount = 0;
            var poweredHelm = false;
            var hasCargoBay = false;
            var ats = false;
            var mapXform = server.EntMan.GetComponent<TransformComponent>(mapUid);
            var query = server.EntMan.AllEntityQueryEnumerator<TransformComponent>();
            while (query.MoveNext(out var uid, out var xform))
            {
                if (xform.MapUid != mapXform.MapUid)
                    continue;

                var id = server.EntMan.GetComponent<MetaDataComponent>(uid).EntityPrototype?.ID;
                if (id is "AirlockShuttle" or "AirlockGlassShuttle")
                    dockCount++;
                if (id == "ComputerShuttle")
                {
                    consoleCount++;
                    if (xform.GridUid == shuttleUid &&
                        server.EntMan.TryGetComponent<ApcPowerReceiverComponent>(uid, out var receiver) &&
                        !receiver.NeedsPower)
                    {
                        poweredHelm = true;
                    }
                }

                if (id is "Thruster" or "ThrusterLarge")
                    thrusterCount++;

                if (server.EntMan.TryGetComponent<TutorialDockStationComponent>(uid, out var station))
                {
                    if (station.StationId == TutorialShuttleArenaSystem.CargoBayStationId)
                        hasCargoBay = true;
                    if (station.StationId == TutorialShuttleArenaSystem.AtsStationId)
                        ats = true;
                }
            }

            Assert.That(dockCount, Is.GreaterThanOrEqualTo(6), "Need shuttle docks + bay + ATS");
            Assert.That(consoleCount, Is.GreaterThanOrEqualTo(1));
            Assert.That(poweredHelm, Is.True,
                "Cargo shuttle helm must be force-powered so TryPilot can grant control");
            Assert.That(thrusterCount, Is.GreaterThanOrEqualTo(1));
            Assert.That(hasCargoBay, Is.True, "Cargo bay mini-station missing");
            Assert.That(ats, Is.True, "ATS mini-station missing");

            var atsHasBank = false;
            var memberQuery = server.EntMan.AllEntityQueryEnumerator<Content.Shared.Station.Components.StationMemberComponent, TransformComponent>();
            while (memberQuery.MoveNext(out var gridEnt, out var member, out var memberXform))
            {
                if (memberXform.MapUid != mapXform.MapUid)
                    continue;
                if (!server.EntMan.TryGetComponent<TutorialDockStationComponent>(gridEnt, out var dock) ||
                    dock.StationId != TutorialShuttleArenaSystem.AtsStationId)
                    continue;

                Assert.That(server.EntMan.HasComponent<Content.Shared.Cargo.Components.StationBankAccountComponent>(member.Station),
                    Is.True,
                    "ATS owning station needs a bank account for pallet sales");
                atsHasBank = true;
            }

            Assert.That(atsHasBank, Is.True, "ATS grid should be a station member with a bank");

            var tags = server.System<TagSystem>();
            var bayHaulCrateCount = 0;
            var boardMarker = false;
            var sellPadTiles = new HashSet<Vector2i>();
            var markerQuery = server.EntMan.AllEntityQueryEnumerator<TutorialStepMarkerComponent, TransformComponent>();
            while (markerQuery.MoveNext(out _, out var stepMarker, out var markerXform))
            {
                if (markerXform.MapUid != mapXform.MapUid)
                    continue;
                if (stepMarker.MarkerId == TutorialShuttleArenaSystem.CargoShuttleBoardMarkerId &&
                    markerXform.GridUid == shuttleUid)
                    boardMarker = true;
            }

            var crateQuery = server.EntMan.AllEntityQueryEnumerator<MetaDataComponent, TransformComponent>();
            while (crateQuery.MoveNext(out var crateUid, out _, out var crateXform))
            {
                if (crateXform.MapUid != mapXform.MapUid)
                    continue;

                if (crateXform.GridUid == cargoBayUid &&
                    tags.HasTag(crateUid, "TutorialCargoBayCrate") &&
                    !crateXform.Anchored)
                {
                    bayHaulCrateCount++;
                }
            }

            var padQuery = server.EntMan.AllEntityQueryEnumerator<Content.Server.Cargo.Components.CargoPalletComponent, TransformComponent>();
            while (padQuery.MoveNext(out _, out var pallet, out var padXform))
            {
                if (padXform.MapUid != mapXform.MapUid || padXform.GridUid is not { } padGrid)
                    continue;
                if (!server.EntMan.TryGetComponent<TutorialDockStationComponent>(padGrid, out var dock) ||
                    dock.StationId != TutorialShuttleArenaSystem.AtsStationId)
                    continue;
                if ((pallet.PalletType & Content.Server.Cargo.Components.BuySellType.Sell) == 0)
                    continue;
                sellPadTiles.Add((Vector2i) padXform.LocalPosition);
            }

            var crateOnAtsSellPad = false;
            var atsCrateQuery = server.EntMan.AllEntityQueryEnumerator<MetaDataComponent, TransformComponent>();
            while (atsCrateQuery.MoveNext(out _, out var meta, out var crateXform))
            {
                if (crateXform.MapUid != mapXform.MapUid || crateXform.Anchored)
                    continue;
                if (meta.EntityPrototype?.ID is not ("CrateGenericSteel" or "CrateHydroponics" or "CrateJanitorialCleanerGrenades"))
                    continue;
                if (crateXform.GridUid is not { } crateGrid ||
                    !server.EntMan.TryGetComponent<TutorialDockStationComponent>(crateGrid, out var dock) ||
                    dock.StationId != TutorialShuttleArenaSystem.AtsStationId)
                    continue;
                if (sellPadTiles.Contains((Vector2i) crateXform.LocalPosition))
                    crateOnAtsSellPad = true;
            }

            Assert.That(bayHaulCrateCount, Is.GreaterThanOrEqualTo(3),
                "Bay needs three unanchored TutorialCargoBayCrate haul targets");
            Assert.That(boardMarker, Is.True, "Shuttle needs cargo-shuttle board marker");
            Assert.That(crateOnAtsSellPad, Is.False, "ATS sell pads must not preload sellable crates");

            var docked = false;
            var dockQuery = server.EntMan.AllEntityQueryEnumerator<Content.Server.Shuttles.Components.DockingComponent, TransformComponent>();
            while (dockQuery.MoveNext(out _, out var docking, out var dockXform))
            {
                if (dockXform.GridUid == shuttleUid && docking.Docked)
                {
                    docked = true;
                    break;
                }
            }

            Assert.That(docked, Is.True, "Cargo shuttle should start docked to the cargo bay");

            maps.UnloadTutorialMap(mapUid);
        });
    }

    [Test]
    public async Task TutorialRoom_BuildsEnclosedLitDepartmentRoom()
    {
        var pair = Pair;
        var server = pair.Server;
        var ticker = server.System<GameTicker>();
        var maps = server.System<TutorialMapSystem>();
        var rooms = server.System<TutorialPracticeRoomSystem>();
        var proto = server.ProtoMan;

        await server.WaitPost(() =>
        {
            ticker.SetGamePreset("TutorialServer");
            ticker.StartGameRule(TutorialRule, out _);
            ticker.StartRound();
        });
        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            var chef = proto.Index<TutorialRolePrototype>("TutorialChef");
            Assert.That(chef.RoomTemplate, Is.EqualTo(new ProtoId<TutorialRoomTemplatePrototype>("TutorialSectionKitchen")));
            Assert.That(maps.TryLoadTutorialMap(chef, out var mapUid, out var gridUid, out var spawn), Is.True);

            Assert.That(server.EntMan.HasComponent<MapGridComponent>(gridUid));
            Assert.That(server.EntMan.HasComponent<GridAtmosphereComponent>(gridUid));
            Assert.That(server.EntMan.HasComponent<GravityComponent>(gridUid));
            Assert.That(server.EntMan.TryGetComponent<TutorialRoomLayoutComponent>(gridUid, out var layout));
            // Chef practices entirely in one kitchen section (no stamped room changes).
            Assert.That(layout!.ChamberCenters.Count, Is.EqualTo(1));
            Assert.That(layout.GateDoors.Count, Is.EqualTo(0));
            Assert.That(TutorialMapSystem.ResolveCopyCount(chef), Is.EqualTo(1));
            Assert.That(chef.PracticeSpawns.All(p => p.Room == 0));

            var wallCount = 0;
            var query = server.EntMan.AllEntityQueryEnumerator<TransformComponent>();
            while (query.MoveNext(out var uid, out var xform))
            {
                if (xform.GridUid != gridUid)
                    continue;
                var meta = server.EntMan.GetComponent<MetaDataComponent>(uid);
                var id = meta.EntityPrototype?.ID;
                if (id != null &&
                    (id.Contains("Wall", StringComparison.Ordinal) ||
                     id.Contains("Window", StringComparison.Ordinal)))
                    wallCount++;
            }

            Assert.That(wallCount, Is.GreaterThan(10), "Kitchen suite should have walls from the stamped section");
            Assert.That(spawn.EntityId, Is.EqualTo(gridUid));

            var supportCount = 0;
            var apcCount = 0;
            var supportQuery = server.EntMan.AllEntityQueryEnumerator<TransformComponent>();
            while (supportQuery.MoveNext(out var uid, out var xform))
            {
                if (xform.GridUid != gridUid)
                    continue;
                var id = server.EntMan.GetComponent<MetaDataComponent>(uid).EntityPrototype?.ID;
                if (id == "TutorialInvisibleGridSupport")
                    supportCount++;
                if (id == "TutorialApcAlwaysOn")
                    apcCount++;
            }

            Assert.That(supportCount, Is.GreaterThan(0), "Practice rooms spawn invisible grid support");
            Assert.That(apcCount, Is.GreaterThanOrEqualTo(layout.GateDoors.Count),
                "Each gate door (and powered exterior doors) get an always-on APC");

            // Single-chamber kitchen has no inter-chamber gates; multi-copy suites do.
            if (layout.GateDoors.Count > 0)
            {
                Assert.That(server.EntMan.HasComponent<Content.Server.Power.Components.ApcPowerReceiverComponent>(layout.GateDoors[0]),
                    "Gate doors keep their power receiver (powered by APC)");

                var firstGate = layout.GateDoors[0];
                Assert.That(server.EntMan.TryGetComponent<TutorialGateDoorComponent>(firstGate, out var gate));
                Assert.That(gate!.Unlocked, Is.False);
                rooms.UnlockGatesForGoal(gridUid, 1);
                Assert.That(gate.Unlocked, Is.True);
            }

            maps.UnloadTutorialMap(mapUid);
        });
    }

    [Test]
    public async Task TutorialSimplifiedEnvironment_FreezesAtmosAndForcePowers()
    {
        var pair = Pair;
        var server = pair.Server;
        var ticker = server.System<GameTicker>();
        var maps = server.System<TutorialMapSystem>();
        var proto = server.ProtoMan;

        await server.WaitPost(() =>
        {
            ticker.SetGamePreset("TutorialServer");
            ticker.StartGameRule(TutorialRule, out _);
            ticker.StartRound();
        });
        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            var chef = proto.Index<TutorialRolePrototype>("TutorialChef");
            Assert.That(chef.SimplifiedEnvironment, Is.True);

            Assert.That(maps.TryLoadTutorialMap(chef, out var chefMap, out var chefGrid, out _), Is.True);
            Assert.That(server.EntMan.TryGetComponent<GridAtmosphereComponent>(chefGrid, out var chefAtmos), Is.True);
            // TEMPORARY: SimplifiedEnvironment atmos freeze is disabled (odd behavior); keep live sim.
            Assert.That(chefAtmos!.Simulated, Is.True,
                "TEMPORARY: SimplifiedEnvironment must leave atmos Simulated until freeze is safe again");

            var forcedReceiver = false;
            var receiverQuery = server.EntMan.AllEntityQueryEnumerator<ApcPowerReceiverComponent, TransformComponent>();
            while (receiverQuery.MoveNext(out _, out var receiver, out var xform))
            {
                if (xform.GridUid != chefGrid || receiver.PowerDisabled)
                    continue;
                Assert.That(receiver.NeedsPower, Is.False, "Simplified roles force APC receivers to not need power");
                forcedReceiver = true;
                break;
            }

            Assert.That(forcedReceiver, Is.True, "Chef practice map should have at least one APC power receiver");
            maps.UnloadTutorialMap(chefMap);

            var atmosRole = proto.Index<TutorialRolePrototype>("TutorialAtmosphericTechnician");
            Assert.That(atmosRole.SimplifiedEnvironment, Is.False);

            Assert.That(maps.TryLoadTutorialMap(atmosRole, out var atmosMap, out var atmosGrid, out _), Is.True);
            Assert.That(server.EntMan.TryGetComponent<GridAtmosphereComponent>(atmosGrid, out var liveAtmos), Is.True);
            Assert.That(liveAtmos!.Simulated, Is.True, "Engineering atmos tutorial must keep live simulation");

            // Section crops ship dark AP-powered fixtures; stamps add AlwaysPoweredWallLight.
            var wallLights = 0;
            var lightQuery = server.EntMan.AllEntityQueryEnumerator<MetaDataComponent, TransformComponent>();
            while (lightQuery.MoveNext(out _, out var meta, out var xform))
            {
                if (xform.GridUid != atmosGrid)
                    continue;
                if (meta.EntityPrototype?.ID == "AlwaysPoweredWallLight")
                    wallLights++;
            }

            Assert.That(wallLights, Is.GreaterThanOrEqualTo(6),
                "Atmos section stamp should place always-powered wall lights around chambers");
            maps.UnloadTutorialMap(atmosMap);
        });
    }

    /// <summary>
    /// Atmos hardsuit Z/Use quick-equips; ObtainItem must complete when the suit is worn
    /// (HoldItem would leave the player stuck).
    /// </summary>
    [Test]
    public async Task TutorialAtmos_HardsuitObtainAcceptsEquippedSuit()
    {
        var pair = Pair;
        var server = pair.Server;
        var ticker = server.System<GameTicker>();
        var tutorial = server.System<TutorialServerRuleSystem>();
        var inventory = server.System<InventorySystem>();
        var entMan = server.EntMan;

        await server.WaitPost(() =>
        {
            ticker.SetGamePreset("TutorialServer");
            ticker.StartGameRule(TutorialRule, out _);
            ticker.StartRound();
        });
        await pair.RunTicksSync(5);

        await server.WaitPost(() =>
        {
            tutorial.TrySelectRole(pair.Player!, "TutorialAtmosphericTechnician", confirmedStub: false);
        });
        await pair.RunTicksSync(60);

        await server.WaitAssertion(() =>
        {
            var mob = pair.Player!.AttachedEntity!.Value;
            tutorial.AdvanceSubGoal(mob); // welcome.intro → kit.hold-suit

            Assert.That(entMan.TryGetComponent<TutorialParticipantComponent>(mob, out var part));
            Assert.That(part!.StepComplete, Is.EqualTo(TutorialStepComplete.ObtainItem));
            Assert.That(part.SubGoalIndex, Is.EqualTo(0));

            // Equip without leaving the suit in-hand — mirrors Z/Use quick-equip.
            inventory.TryUnequip(mob, "outerClothing", force: true, silent: true);
            var suit = entMan.SpawnEntity("ClothingOuterHardsuitAtmos",
                entMan.GetComponent<TransformComponent>(mob).Coordinates);
            Assert.That(inventory.TryEquip(mob, suit, "outerClothing", force: true, silent: true), Is.True,
                "Forced equip of atmos hardsuit onto empty outerClothing slot");
        });
        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            var mob = pair.Player!.AttachedEntity!.Value;
            Assert.That(entMan.TryGetComponent<TutorialParticipantComponent>(mob, out var part));
            Assert.That(part!.GoalIndex, Is.EqualTo(1));
            Assert.That(part.SubGoalIndex, Is.EqualTo(1),
                "Wearing the atmos hardsuit must advance past hold-suit onto magboots");
        });
    }

    [Test]
    public async Task TutorialAtmosTeg_BootstrapProducesPower()
    {
        var pair = Pair;
        var server = pair.Server;
        var ticker = server.System<GameTicker>();
        var maps = server.System<TutorialMapSystem>();
        var tegBootstrap = server.System<TutorialTegBootstrapSystem>();
        var proto = server.ProtoMan;

        await server.WaitPost(() =>
        {
            ticker.SetGamePreset("TutorialServer");
            ticker.StartGameRule(TutorialRule, out _);
            ticker.StartRound();
        });
        await pair.RunTicksSync(5);

        EntityUid mapUid = default;
        float lastGeneration = 0f;
        await server.WaitPost(() =>
        {
            var atmos = proto.Index<TutorialRolePrototype>("TutorialAtmosphericTechnician");
            Assert.That(maps.TryLoadTutorialMap(atmos, out mapUid, out var gridUid, out _), Is.True);

            var centerCoords = new EntityCoordinates(gridUid, new System.Numerics.Vector2(3.5f, 3.5f));
            server.EntMan.SpawnEntity("TutorialTegCenter", centerCoords);
            server.EntMan.SpawnEntity("TutorialTegCirculator", centerCoords.Offset(new System.Numerics.Vector2(-1f, 0f)));
            server.EntMan.SpawnEntity("TutorialTegCirculator", centerCoords.Offset(new System.Numerics.Vector2(1f, 0f)));
            tegBootstrap.TryConfigureOnGrid(gridUid);

            var query = server.EntMan.AllEntityQueryEnumerator<Content.Server.Power.Generation.Teg.TegGeneratorComponent, TransformComponent>();
            while (query.MoveNext(out _, out var teg, out var xform))
            {
                if (xform.GridUid != gridUid)
                    continue;
                lastGeneration = teg.LastGeneration;
                break;
            }
        });

        await server.WaitAssertion(() =>
        {
            Assert.That(lastGeneration, Is.GreaterThan(0f), "Bootstrapped tutorial TEG should be marked as producing power");
            maps.UnloadTutorialMap(mapUid);
        });
    }

    [Test]
    public async Task TutorialDailyRestart_UsesMaxTimeRestartRule()
    {
        var pair = Pair;
        var server = pair.Server;
        var ticker = server.System<GameTicker>();
        var timing = server.ResolveDependency<Robust.Shared.Timing.IGameTiming>();

        MaxTimeRestartRuleComponent? maxTime = null;
        await server.WaitPost(() =>
        {
            ticker.SetGamePreset("TutorialServer");
            ticker.StartGameRule("MaxTimeRestartTutorial", out var ruleEntity);
            Assert.That(server.EntMan.TryGetComponent(ruleEntity, out maxTime));
            maxTime!.RoundMaxTime = TimeSpan.FromSeconds(3);
            maxTime.RoundEndDelay = TimeSpan.FromSeconds(1);
            ticker.StartRound();
        });

        await pair.RunTicksSync(5);
        await pair.RunTicksSync(timing.TickRate * 5);

        await server.WaitAssertion(() =>
        {
            Assert.That(
                ticker.RunLevel is GameRunLevel.PostRound or GameRunLevel.PreRoundLobby,
                $"Unexpected run level {ticker.RunLevel}");
        });
    }

    [Test]
    public async Task TutorialNukeopsRoles_HaveDeepCurriculaAndGear()
    {
        var pair = Pair;
        var server = pair.Server;
        var proto = server.ProtoMan;

        await server.WaitAssertion(() =>
        {
            static TutorialSubGoalData Sub(TutorialRolePrototype role, string id) =>
                role.Goals.SelectMany(g => g.SubGoals).First(s => s.Id == id);

            var op = proto.Index<TutorialRolePrototype>("TutorialAntagNukeops");
            Assert.That(op.Stub, Is.False);
            Assert.That(op.NukeopsOutpost, Is.True);
            Assert.That(op.StartingGear, Is.EqualTo(new ProtoId<StartingGearPrototype>("SyndicateOperativeGearFull")));
            Assert.That(op.RoleLoadout, Is.EqualTo(new ProtoId<RoleLoadoutPrototype>("RoleSurvivalNukie")));
            Assert.That(op.PracticeSpawns.Any(p => p.Id == "TutorialNuclearBomb"));
            Assert.That(op.PracticeSpawns.Any(p => p.Id == "NukeDisk"));
            Assert.That(op.PracticeSpawns.Any(p => p.Id == "BoxFolderNuclearCodes"));
            Assert.That(Sub(op, "arm-nuke").Complete, Is.EqualTo(TutorialStepComplete.NukeArmed));
            Assert.That(Sub(op, "hold-uplink").Entity, Is.EqualTo(new EntProtoId("BaseUplinkRadio40TC")));

            var medic = proto.Index<TutorialRolePrototype>("TutorialAntagNukeopsMedic");
            Assert.That(medic.Stub, Is.False);
            Assert.That(medic.NukeopsOutpost, Is.True);
            Assert.That(medic.StartingGear, Is.EqualTo(new ProtoId<StartingGearPrototype>("SyndicateOperativeMedicFull")));
            Assert.That(medic.PracticeSpawns.All(p => p.Id != "TutorialNuclearBomb"));
            Assert.That(Sub(medic, "mix-bicaridine").Reagent,
                Is.EqualTo(new ProtoId<Content.Shared.Chemistry.Reagent.ReagentPrototype>("Bicaridine")));
            Assert.That(Sub(medic, "mix-dermaline").Reagent,
                Is.EqualTo(new ProtoId<Content.Shared.Chemistry.Reagent.ReagentPrototype>("Dermaline")));
            Assert.That(Sub(medic, "mix-dexalinplus").Reagent,
                Is.EqualTo(new ProtoId<Content.Shared.Chemistry.Reagent.ReagentPrototype>("DexalinPlus")));
            Assert.That(Sub(medic, "mix-puncturase").Reagent,
                Is.EqualTo(new ProtoId<Content.Shared.Chemistry.Reagent.ReagentPrototype>("Puncturase")));

            var cmd = proto.Index<TutorialRolePrototype>("TutorialAntagNukeopsCommander");
            Assert.That(cmd.Stub, Is.False);
            Assert.That(cmd.StartingGear, Is.EqualTo(new ProtoId<StartingGearPrototype>("SyndicateCommanderGearFull")));
            Assert.That(cmd.ShuttleArena,
                Is.EqualTo(new ProtoId<TutorialShuttleArenaPrototype>("TutorialArenaNukeopsInfiltrator")));
            Assert.That(Sub(cmd, "declare").Complete, Is.EqualTo(TutorialStepComplete.WarDeclared));
            Assert.That(cmd.Goals.SelectMany(g => g.SubGoals)
                .Any(s => s.Complete is TutorialStepComplete.PilotShuttle
                    or TutorialStepComplete.DockShuttle
                    or TutorialStepComplete.UndockShuttle));

            var arena = proto.Index<TutorialShuttleArenaPrototype>("TutorialArenaNukeopsInfiltrator");
            Assert.That(arena.IncludeAtsSell, Is.False);
            Assert.That(arena.HomeStationId, Is.EqualTo("nukie-dock"));
            Assert.That(arena.DistantStationId, Is.EqualTo("nukie-rally"));
            Assert.That(arena.ShuttleMap, Is.EqualTo(new Robust.Shared.Utility.ResPath("/Maps/Shuttles/infiltrator.yml")));
        });
    }

    [Test]
    public async Task TutorialNukeopsOutpost_BuildsLoungeAndChem()
    {
        var pair = Pair;
        var server = pair.Server;
        var ticker = server.System<GameTicker>();
        var maps = server.System<TutorialMapSystem>();
        var proto = server.ProtoMan;

        await server.WaitPost(() =>
        {
            ticker.SetGamePreset("TutorialServer");
            ticker.StartGameRule(TutorialRule, out _);
            ticker.StartRound();
        });
        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            var op = proto.Index<TutorialRolePrototype>("TutorialAntagNukeops");
            Assert.That(maps.TryLoadTutorialMap(op, out var mapUid, out var gridUid, out _), Is.True);
            Assert.That(server.EntMan.HasComponent<TutorialForcePowerGridComponent>(gridUid));

            var hasDispenser = false;
            var hasMaster = false;
            var query = server.EntMan.AllEntityQueryEnumerator<MetaDataComponent, TransformComponent>();
            while (query.MoveNext(out var uid, out var meta, out var xform))
            {
                if (xform.GridUid != gridUid)
                    continue;
                if (meta.EntityPrototype?.ID == "TutorialChemDispenser")
                    hasDispenser = true;
                if (meta.EntityPrototype?.ID == "TutorialChemMaster")
                    hasMaster = true;
            }

            Assert.That(hasDispenser, Is.True, "Nukeops outpost should include a chem dispenser");
            Assert.That(hasMaster, Is.True, "Nukeops outpost should include a ChemMaster");
            maps.UnloadTutorialMap(mapUid);
        });
    }

    [Test]
    public async Task TutorialDummyNuke_ArmsWithoutExploding()
    {
        var pair = Pair;
        var server = pair.Server;
        var ticker = server.System<GameTicker>();
        var maps = server.System<TutorialMapSystem>();
        var nukeSys = server.System<Content.Server.Nuke.NukeSystem>();
        var proto = server.ProtoMan;

        await server.WaitPost(() =>
        {
            ticker.SetGamePreset("TutorialServer");
            ticker.StartGameRule(TutorialRule, out _);
            ticker.StartRound();
        });
        await pair.RunTicksSync(5);

        EntityUid mapUid = default;
        EntityUid nukeUid = default;
        await server.WaitAssertion(() =>
        {
            var op = proto.Index<TutorialRolePrototype>("TutorialAntagNukeops");
            Assert.That(maps.TryLoadTutorialMap(op, out mapUid, out var gridUid, out var spawn), Is.True);

            nukeUid = server.EntMan.SpawnEntity("TutorialNuclearBomb", spawn.Offset(new System.Numerics.Vector2(0f, -2f)));
            Assert.That(server.EntMan.HasComponent<TutorialDummyNukeComponent>(nukeUid));
            nukeSys.ArmBomb(nukeUid);
            nukeSys.SetRemainingTime(nukeUid, 0.05f);
        });

        await pair.RunTicksSync(server.ResolveDependency<Robust.Shared.Timing.IGameTiming>().TickRate);

        await server.WaitAssertion(() =>
        {
            Assert.That(server.EntMan.EntityExists(nukeUid), Is.True, "Dummy nuke must not delete on timer expiry");
            Assert.That(server.EntMan.TryGetComponent<Content.Server.Nuke.NukeComponent>(nukeUid, out var nuke));
            Assert.That(nuke!.Status, Is.EqualTo(Content.Shared.Nuke.NukeStatus.ARMED));
            Assert.That(nuke.RemainingTime, Is.GreaterThan(0f));
            Assert.That(nuke.Exploded, Is.False);
            maps.UnloadTutorialMap(mapUid);
        });
    }

    [Test]
    public async Task TutorialNukes_ShareAuthenticationCodesWithPapers()
    {
        var pair = Pair;
        var server = pair.Server;
        var ticker = server.System<GameTicker>();
        var maps = server.System<TutorialMapSystem>();
        var proto = server.ProtoMan;

        await server.WaitPost(() =>
        {
            ticker.SetGamePreset("TutorialServer");
            ticker.StartGameRule(TutorialRule, out _);
            ticker.StartRound();
        });
        await pair.RunTicksSync(5);

        EntityUid mapUid = default;
        await server.WaitAssertion(() =>
        {
            var op = proto.Index<TutorialRolePrototype>("TutorialAntagNukeops");
            Assert.That(maps.TryLoadTutorialMap(op, out mapUid, out _, out var spawn), Is.True);

            var nukeA = server.EntMan.SpawnEntity("TutorialNuclearBomb", spawn.Offset(new Vector2(0f, -2f)));
            var nukeB = server.EntMan.SpawnEntity("NuclearBombUnanchored", spawn.Offset(new Vector2(1f, -2f)));
            Assert.That(server.EntMan.TryGetComponent<NukeComponent>(nukeA, out var compA));
            Assert.That(server.EntMan.TryGetComponent<NukeComponent>(nukeB, out var compB));
            Assert.That(compA!.Code, Is.Not.Empty);
            Assert.That(compB!.Code, Is.EqualTo(compA.Code),
                "All nukes must share one auth code while TutorialServer is active");

            var paper = server.EntMan.SpawnEntity("NukeCodePaper", spawn.Offset(new Vector2(-1f, -2f)));
            Assert.That(server.EntMan.TryGetComponent<PaperComponent>(paper, out var paperComp));
            Assert.That(paperComp!.Content, Does.Contain(compA.Code),
                "Nuke code paper must print the shared tutorial auth code");

            maps.UnloadTutorialMap(mapUid);
        });
    }

    [Test]
    public async Task TutorialNukeopsCommander_ArenaDocksInfiltrator()
    {
        var pair = Pair;
        var server = pair.Server;
        var ticker = server.System<GameTicker>();
        var maps = server.System<TutorialMapSystem>();
        var proto = server.ProtoMan;

        await server.WaitPost(() =>
        {
            ticker.SetGamePreset("TutorialServer");
            ticker.StartGameRule(TutorialRule, out _);
            ticker.StartRound();
        });
        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            var cmd = proto.Index<TutorialRolePrototype>("TutorialAntagNukeopsCommander");
            Assert.That(maps.TryLoadTutorialMap(cmd, out var mapUid, out var shuttleUid, out _), Is.True,
                "Commander arena must load and start docked");

            var home = false;
            var rally = false;
            var query = server.EntMan.AllEntityQueryEnumerator<TutorialDockStationComponent, TransformComponent>();
            while (query.MoveNext(out _, out var station, out var xform))
            {
                if (xform.MapUid != server.EntMan.GetComponent<TransformComponent>(shuttleUid).MapUid)
                    continue;
                if (station.StationId == "nukie-dock")
                    home = true;
                if (station.StationId == "nukie-rally")
                    rally = true;
            }

            Assert.That(home, Is.True, "Expected nukie-dock station");
            Assert.That(rally, Is.True, "Expected nukie-rally station");
            maps.UnloadTutorialMap(mapUid);
        });
    }

    [Test]
    public async Task TutorialZombieRevBorg_HaveCombinedDeepCurricula()
    {
        var pair = Pair;
        var server = pair.Server;
        var proto = server.ProtoMan;

        await server.WaitAssertion(() =>
        {
            static TutorialSubGoalData Sub(TutorialRolePrototype role, string id) =>
                role.Goals.SelectMany(g => g.SubGoals).First(s => s.Id == id);

            Assert.That(proto.TryIndex<TutorialRolePrototype>("TutorialAntagInitialInfected", out _), Is.False,
                "Initial Infected should be merged into TutorialAntagZombie");
            Assert.That(proto.TryIndex<TutorialRolePrototype>("TutorialAntagRev", out _), Is.False,
                "Rev should be merged into TutorialAntagHeadRev");
            Assert.That(proto.TryIndex<TutorialRolePrototype>("TutorialAntagSubvertedSilicon", out _), Is.False,
                "Subverted Silicon should be merged into TutorialBorg");

            var zombie = proto.Index<TutorialRolePrototype>("TutorialAntagZombie");
            Assert.That(zombie.Stub, Is.False);
            Assert.That(zombie.Antag, Is.EqualTo(new ProtoId<AntagPrototype>("InitialInfected")));
            Assert.That(Sub(zombie, "turn-undead").Complete, Is.EqualTo(TutorialStepComplete.PlayerIsZombie));
            Assert.That(Sub(zombie, "bite").Complete, Is.EqualTo(TutorialStepComplete.PracticeMobInfected));
            Assert.That(zombie.PracticeSpawns.Count(p => p.Id == "TutorialPracticeMobVictim"), Is.GreaterThanOrEqualTo(2));

            var rev = proto.Index<TutorialRolePrototype>("TutorialAntagHeadRev");
            Assert.That(rev.Stub, Is.False);
            Assert.That(rev.Antag, Is.EqualTo(new ProtoId<AntagPrototype>("HeadRev")));
            Assert.That(Sub(rev, "convert-crew").Complete, Is.EqualTo(TutorialStepComplete.PracticeMobConverted));
            Assert.That(Sub(rev, "convert-crew").MinCount, Is.GreaterThanOrEqualTo(3));
            Assert.That(rev.PracticeSpawns.Count(p => p.Id == "TutorialPracticeMobConvertible"), Is.GreaterThanOrEqualTo(3));
            Assert.That(rev.PracticeSpawns.Any(p => p.Id == "Flash"));

            var borg = proto.Index<TutorialRolePrototype>("TutorialBorg");
            Assert.That(borg.Stub, Is.False);
            Assert.That(borg.Job, Is.EqualTo(new ProtoId<JobPrototype>("Borg")));
            Assert.That(borg.SpawnEntity, Is.EqualTo(new EntProtoId("TutorialPlayerBorg")));
            Assert.That(borg.Category, Is.EqualTo("Science"));
            Assert.That(borg.Name, Is.EqualTo("tutorial-job-borg-name"));
            Assert.That(Loc.GetString(borg.Name!), Is.EqualTo("Cyborg"));
            Assert.That(borg.Goals.Any(g => g.Id == "chassis"));
            Assert.That(Sub(borg, "select-chassis").Complete, Is.EqualTo(TutorialStepComplete.BorgTypeSelected));
            Assert.That(Sub(borg, "select-chassis").Marker, Is.EqualTo("generic"));
            Assert.That(Sub(borg, "select-tool-module").Complete, Is.EqualTo(TutorialStepComplete.BorgModuleSelected));
            Assert.That(Sub(borg, "select-tool-module").Entity, Is.EqualTo(new EntProtoId("BorgModuleTool")));
            Assert.That(Sub(borg, "select-inflatable-module").Complete, Is.EqualTo(TutorialStepComplete.BorgModuleSelected));
            Assert.That(Sub(borg, "select-inflatable-module").Entity, Is.EqualTo(new EntProtoId("BorgModuleInflatable")));
            Assert.That(Sub(borg, "panel-open").Complete, Is.EqualTo(TutorialStepComplete.PlayerWiresPanelOpen));
            Assert.That(Sub(borg, "emagged").Complete, Is.EqualTo(TutorialStepComplete.SiliconSubverted));
            Assert.That(borg.Goals.Any(g => g.Id == "modules"));
            Assert.That(borg.Goals.Any(g => g.Id == "subversion"));

            Assert.That(proto.TryIndex<EntityPrototype>("TutorialPlayerBorg", out var tutorialBorgBody), Is.True);
            var switchableName = server.ResolveDependency<IComponentFactory>().GetComponentName<BorgSwitchableTypeComponent>();
            Assert.That(tutorialBorgBody!.Components.TryGetComponent(switchableName, out var switchableComp), Is.True);
            var switchable = (BorgSwitchableTypeComponent)switchableComp!;
            Assert.That(switchable.AvailableBorgTypes, Is.Not.Null);
            Assert.That(switchable.AvailableBorgTypes!, Has.Count.EqualTo(1));
            Assert.That(switchable.AvailableBorgTypes![0].Id, Is.EqualTo("generic"));
        });
    }

    [Test]
    public async Task TutorialAntagExpansion_HaveDeepCurriculaAndRemovedStubs()
    {
        var pair = Pair;
        var server = pair.Server;
        var proto = server.ProtoMan;

        await server.WaitAssertion(() =>
        {
            static TutorialSubGoalData Sub(TutorialRolePrototype role, string id) =>
                role.Goals.SelectMany(g => g.SubGoals).First(s => s.Id == id);

            Assert.That(proto.TryIndex<TutorialRolePrototype>("TutorialAntagParadoxClone", out _), Is.False);
            Assert.That(proto.TryIndex<TutorialRolePrototype>("TutorialAntagSurvivor", out _), Is.False);

            var ling = proto.Index<TutorialRolePrototype>("TutorialAntagChangeling");
            Assert.That(ling.Stub, Is.True); //Tutorial: temporarily greyed pending manual test
            Assert.That(ling.Category, Is.EqualTo("Server specific"));
            Assert.That(ling.SubCategory, Is.EqualTo("Starlight"));
            Assert.That(ling.Antag, Is.EqualTo(new ProtoId<AntagPrototype>("Changeling")));
            Assert.That(Sub(ling, "kill-dummy").Complete, Is.EqualTo(TutorialStepComplete.PracticeMobDead));
            Assert.That(Sub(ling, "sting-dummy").Complete, Is.EqualTo(TutorialStepComplete.ChangelingStung));
            Assert.That(Sub(ling, "devour-corpse").Complete, Is.EqualTo(TutorialStepComplete.ChangelingDevoured));
            Assert.That(Sub(ling, "buy-armblade").Complete, Is.EqualTo(TutorialStepComplete.HasAction));
            Assert.That(Sub(ling, "buy-armblade").Entity, Is.EqualTo(new EntProtoId("ActionRetractableItemArmBlade")));

            var ninja = proto.Index<TutorialRolePrototype>("TutorialAntagSpaceNinja");
            Assert.That(ninja.Stub, Is.False);
            Assert.That(ninja.Antag, Is.EqualTo(new ProtoId<AntagPrototype>("SpaceNinja")));
            Assert.That(ninja.StartingGear, Is.EqualTo(new ProtoId<StartingGearPrototype>("SpaceNinjaGear")));
            Assert.That(Sub(ninja, "doorjack").Complete, Is.EqualTo(TutorialStepComplete.InteractTargetTag));
            Assert.That(Sub(ninja, "research").Tag, Is.EqualTo("TutorialResearchConsole"));
            Assert.That(Sub(ninja, "terror").Tag, Is.EqualTo("TutorialCommsConsole"));
            Assert.That(Sub(ninja, "records").Tag, Is.EqualTo("TutorialCriminalRecords"));

            var xeno = proto.Index<TutorialRolePrototype>("TutorialAntagXenoborg");
            Assert.That(xeno.Stub, Is.False);
            Assert.That(xeno.SpawnEntity, Is.EqualTo(new EntProtoId("XenoborgEngi")));
            Assert.That(xeno.ShuttleArena,
                Is.EqualTo(new ProtoId<TutorialShuttleArenaPrototype>("TutorialArenaMothership")));
            Assert.That(xeno.RoomTemplate, Is.Null);
            Assert.That(Sub(xeno, "crusher").Tag, Is.EqualTo("TutorialXenoborgCrusher"));
            Assert.That(Sub(xeno, "hold-brain").Entity, Is.EqualTo(new EntProtoId("OrganHumanBrain")));
            Assert.That(xeno.PracticeSpawns.Any(p => p.Id == "XenoborgEngiPrinted"));

            var core = proto.Index<TutorialRolePrototype>("TutorialAntagMothershipCore");
            Assert.That(core.Stub, Is.False);
            Assert.That(core.SpawnEntity, Is.EqualTo(new EntProtoId("MothershipCore")));
            Assert.That(core.ShuttleArena,
                Is.EqualTo(new ProtoId<TutorialShuttleArenaPrototype>("TutorialArenaMothership")));
            Assert.That(core.Goals.SelectMany(g => g.SubGoals)
                .Any(s => s.Complete is TutorialStepComplete.PilotShuttle
                    or TutorialStepComplete.DockShuttle
                    or TutorialStepComplete.UndockShuttle));

            var arena = proto.Index<TutorialShuttleArenaPrototype>("TutorialArenaMothership");
            Assert.That(arena.IncludeAtsSell, Is.False);
            Assert.That(arena.HomeStationId, Is.EqualTo("mothership-dock"));
            Assert.That(arena.DistantStationId, Is.EqualTo("ats"));
            Assert.That(arena.ShuttleMap, Is.EqualTo(new Robust.Shared.Utility.ResPath("/Maps/Shuttles/mothership.yml")));

            var vamp = proto.Index<TutorialRolePrototype>("TutorialAntagVampire");
            Assert.That(vamp.Stub, Is.True); //Tutorial: temporarily greyed pending manual test
            Assert.That(vamp.Category, Is.EqualTo("Server specific"));
            Assert.That(vamp.SubCategory, Is.EqualTo("Starlight"));
            Assert.That(vamp.Antag, Is.EqualTo(new ProtoId<AntagPrototype>("Vampire")));
            Assert.That(Sub(vamp, "extend-fangs").Complete, Is.EqualTo(TutorialStepComplete.VampireFangsExtended));
            Assert.That(Sub(vamp, "drink-blood").Complete, Is.EqualTo(TutorialStepComplete.VampireBloodAbove));
            Assert.That(Sub(vamp, "drink-blood").MinCount, Is.GreaterThanOrEqualTo(40));
            Assert.That(Sub(vamp, "choose-class").Complete, Is.EqualTo(TutorialStepComplete.VampireClassChosen));

            Assert.That(proto.HasIndex<Content.Shared._Starlight.Antags.Vampires.Prototypes.VampireClassPrototype>("Hemomancer"));
            Assert.That(proto.HasIndex(new EntProtoId("ActionVampireToggleFangs")));
            Assert.That(proto.HasIndex(new EntProtoId("MindRoleVampire")));

            Assert.That(proto.TryIndex<TutorialRolePrototype>("TutorialCBURN", out _), Is.False);
            Assert.That(proto.TryIndex<TutorialRolePrototype>("TutorialDeathSquad", out _), Is.False);
            Assert.That(proto.TryIndex<TutorialRolePrototype>("TutorialAntagGenericAntagonist", out _), Is.False);
            Assert.That(proto.TryIndex<TutorialRolePrototype>("TutorialAntagGenericFreeAgent", out _), Is.False);
            Assert.That(proto.TryIndex<TutorialRolePrototype>("TutorialAntagGenericSiliconAntagonist", out _), Is.False);
            Assert.That(proto.TryIndex<TutorialRolePrototype>("TutorialAntagGenericTeamAntagonist", out _), Is.False);

            var thief = proto.Index<TutorialRolePrototype>("TutorialAntagThief");
            Assert.That(thief.Stub, Is.False);
            Assert.That(thief.Antag, Is.EqualTo(new ProtoId<AntagPrototype>("Thief")));
            Assert.That(thief.StartingGear, Is.EqualTo(new ProtoId<StartingGearPrototype>("TutorialThiefGear")));
            Assert.That(thief.RoomTemplate, Is.EqualTo(new ProtoId<TutorialRoomTemplatePrototype>("TutorialSectionBar")));
            Assert.That(Sub(thief, "get-kit-bag").Entity, Is.EqualTo(new EntProtoId("ClothingBackpackSatchelSmugglerUnanchored")));
            Assert.That(Sub(thief, "steal-pen").Complete, Is.EqualTo(TutorialStepComplete.ObtainItem));
            Assert.That(Sub(thief, "steal-pen").Entity, Is.EqualTo(new EntProtoId("Pen")));
            Assert.That(thief.Goals.Select(g => g.Id), Does.Contain("beacon"));
            Assert.That(Sub(thief, "hold-beacon").Entity, Is.EqualTo(new EntProtoId("ThiefBeacon")));
            Assert.That(Sub(thief, "link-beacon").Complete, Is.EqualTo(TutorialStepComplete.ThiefBeaconLinked));
            Assert.That(thief.Goals.Select(g => g.Id), Does.Contain("secret-door"));
            Assert.That(Sub(thief, "build-door").Complete, Is.EqualTo(TutorialStepComplete.MapHasEntity));
            Assert.That(Sub(thief, "build-door").Entity, Is.EqualTo(new EntProtoId("SolidSecretDoor")));
            Assert.That(thief.PracticeSpawns.Any(p => p.Id == "SheetSteel10" && p.Room == 0),
                "Secret-door materials stay in the same bar section — no empty stamped chambers");
            Assert.That(TutorialMapSystem.ResolveCopyCount(thief), Is.EqualTo(1));
            Assert.That(thief.PracticeSpawns.Any(p => p.Id == "PartRodMetal10"));
            Assert.That(thief.PracticeSpawns.Any(p => p.Id == "CableApcStack10"));
            Assert.That(thief.PracticeSpawns.Any(p => p.Id == "PowerCellSmall"));
            Assert.That(thief.PracticeSpawns.Any(p => p.Id == "Screwdriver"));

            var wizard = proto.Index<TutorialRolePrototype>("TutorialAntagWizard");
            Assert.That(wizard.Stub, Is.False);
            Assert.That(wizard.Antag, Is.EqualTo(new ProtoId<AntagPrototype>("Wizard")));
            Assert.That(wizard.StartingGear, Is.EqualTo(new ProtoId<StartingGearPrototype>("TutorialWizardGear")));
            Assert.That(wizard.Map, Is.EqualTo(new Robust.Shared.Utility.ResPath("/Maps/Nonstations/wizardsden.yml")));
            Assert.That(wizard.RoomTemplate, Is.Null);
            Assert.That(Sub(wizard, "buy-smoke").Complete, Is.EqualTo(TutorialStepComplete.HasAction));
            Assert.That(Sub(wizard, "buy-smoke").Entity, Is.EqualTo(new EntProtoId("ActionSmoke")));
            Assert.That(Sub(wizard, "hold-suit").Entity, Is.EqualTo(new EntProtoId("ClothingOuterHardsuitWizard")));
            Assert.That(wizard.PracticeSpawns.Any(p => p.Id == "ClothingOuterHardsuitWizard"));

            var wizGear = proto.Index<StartingGearPrototype>("TutorialWizardGear");
            Assert.That(wizGear.Equipment.Values.Any(v => v == "WizardTeleportScroll"), Is.False,
                "Tutorial wizard gear must not include the teleport scroll");
            Assert.That(wizGear.Equipment.Values.Any(v => v == "WizardsGrimoire"), Is.True);

            var dragon = proto.Index<TutorialRolePrototype>("TutorialAntagDragon");
            Assert.That(dragon.Stub, Is.False);
            Assert.That(dragon.Antag, Is.EqualTo(new ProtoId<AntagPrototype>("Dragon")));
            Assert.That(dragon.SpawnEntity, Is.EqualTo(new EntProtoId("MobDragon")));
            Assert.That(dragon.RoomTemplate, Is.Null);
            Assert.That(dragon.DragonArena,
                Is.EqualTo(new ProtoId<TutorialDragonArenaPrototype>("TutorialDragonPrey")));
            Assert.That(dragon.Goals.Select(g => g.Id).ToArray(),
                Is.EqualTo(new[] { "welcome", "approach", "portal", "feast", "abilities", "finish" }));
            Assert.That(Sub(dragon, "reach-station").Complete, Is.EqualTo(TutorialStepComplete.ReachMarker));
            Assert.That(Sub(dragon, "reach-station").Marker, Is.EqualTo("dragon-station"));
            Assert.That(Sub(dragon, "open-rift").Complete, Is.EqualTo(TutorialStepComplete.MapHasEntity));
            Assert.That(Sub(dragon, "open-rift").Entity, Is.EqualTo(new EntProtoId("CarpRift")));
            Assert.That(Sub(dragon, "kill-dummy").Complete, Is.EqualTo(TutorialStepComplete.PracticeMobDead));
            Assert.That(Sub(dragon, "devour-human").Complete, Is.EqualTo(TutorialStepComplete.DragonDevoured));
            Assert.That(Sub(dragon, "use-breath").Complete, Is.EqualTo(TutorialStepComplete.ActionUsed));
            Assert.That(Sub(dragon, "use-breath").Entity, Is.EqualTo(new EntProtoId("ActionDragonsBreath")));
            Assert.That(dragon.PracticeSpawns.Count(p =>
                    p.Id.Id.StartsWith("TutorialPracticeMobIdle", StringComparison.Ordinal)),
                Is.GreaterThanOrEqualTo(6));
            Assert.That(proto.HasIndex(new EntProtoId("ActionSpawnRift")));
            Assert.That(proto.HasIndex(new EntProtoId("ActionDevour")));
            Assert.That(proto.HasIndex(new EntProtoId("MindRoleDragon")));
            Assert.That(proto.HasIndex(new EntProtoId("TutorialPinpointerDragonStation")));
        });
    }

    [Test]
    public async Task TutorialRoleExpansion_CadetServiceWorkerCurricula()
    {
        var pair = Pair;
        var server = pair.Server;
        var proto = server.ProtoMan;
        var maps = server.System<TutorialMapSystem>();

        await server.WaitAssertion(() =>
        {
            static TutorialSubGoalData Sub(TutorialRolePrototype role, string id) =>
                role.Goals.SelectMany(g => g.SubGoals).First(s => s.Id == id);

            var cadet = proto.Index<TutorialRolePrototype>("TutorialSecurityCadet");
            Assert.That(cadet.Stub, Is.False);
            Assert.That(cadet.Job, Is.EqualTo(new ProtoId<JobPrototype>("SecurityCadet")));
            Assert.That(cadet.RoomTemplate, Is.EqualTo(new ProtoId<TutorialRoomTemplatePrototype>("TutorialSectionSecurity")));
            Assert.That(cadet.Goals.Select(g => g.Id).ToArray(), Is.EqualTo(new[] { "welcome", "tools", "finish" }));
            Assert.That(Sub(cadet, "hold-baton").Entity, Is.EqualTo(new EntProtoId("Stunbaton")));
            Assert.That(Sub(cadet, "use-seclite").Complete, Is.EqualTo(TutorialStepComplete.UseInHand));

            var officer = proto.Index<TutorialRolePrototype>("TutorialSecurityOfficer");
            Assert.That(officer.Goals.Select(g => g.Id), Does.Not.Contain("tools"));
            Assert.That(officer.Goals.Select(g => g.Id), Does.Contain("arrest"));
            Assert.That(officer.PracticeSpawns.Any(p => p.Id == "Stunbaton"), Is.True,
                "Officer must keep stun tools for arrest practice");
            Assert.That(officer.PracticeSpawns.Any(p => p.Id == "WeaponDisabler"), Is.True);
            Assert.That(officer.PracticeSpawns.Any(p => p.Id == "Flash"), Is.True);

            var sw = proto.Index<TutorialRolePrototype>("TutorialServiceWorker");
            Assert.That(sw.Stub, Is.False);
            Assert.That(sw.Job, Is.EqualTo(new ProtoId<JobPrototype>("ServiceWorker")));
            Assert.That(sw.RoomTemplate, Is.EqualTo(new ProtoId<TutorialRoomTemplatePrototype>("TutorialSectionKitchen")));
            Assert.That(sw.Goals.Select(g => g.Id).ToArray(),
                Is.EqualTo(new[] { "welcome", "chef", "botanist", "bartender", "janitor", "finish" }));
            Assert.That(Sub(sw, "microwave").Tag, Is.EqualTo("TutorialMicrowave"));
            Assert.That(Sub(sw, "tray").Tag, Is.EqualTo("TutorialHydroTray"));
            Assert.That(Sub(sw, "hold-vodka").Complete, Is.EqualTo(TutorialStepComplete.HoldItem));
            Assert.That(Sub(sw, "clear-puddle").Complete, Is.EqualTo(TutorialStepComplete.PuddleCleared));

            Assert.That(maps.TryLoadTutorialMap(cadet, out var cadetMap, out _, out _), Is.True);
            maps.UnloadTutorialMap(cadetMap);
            Assert.That(maps.TryLoadTutorialMap(sw, out var swMap, out _, out _), Is.True);
            maps.UnloadTutorialMap(swMap);

            var thief = proto.Index<TutorialRolePrototype>("TutorialAntagThief");
            Assert.That(maps.TryLoadTutorialMap(thief, out var thiefMap, out _, out _), Is.True);
            maps.UnloadTutorialMap(thiefMap);

            var wizard = proto.Index<TutorialRolePrototype>("TutorialAntagWizard");
            Assert.That(maps.TryLoadTutorialMap(wizard, out var wizMap, out _, out var wizSpawn), Is.True,
                "Wizard den map must load");
            Assert.That(wizSpawn != default, Is.True);
            maps.UnloadTutorialMap(wizMap);

            var dragon = proto.Index<TutorialRolePrototype>("TutorialAntagDragon");
            Assert.That(maps.TryLoadTutorialMap(dragon, out var dragonMap, out _, out var dragonSpawn), Is.True,
                "Space Dragon prey arena must load");
            Assert.That(dragonSpawn != default, Is.True);
            maps.UnloadTutorialMap(dragonMap);
        });
    }

    [Test]
    public async Task TutorialDragonArena_SpawnsInSpaceWithPinpointerAndPacifiedPrey()
    {
        var pair = Pair;
        var server = pair.Server;
        var ticker = server.System<GameTicker>();
        var maps = server.System<TutorialMapSystem>();
        var proto = server.ProtoMan;

        await server.WaitPost(() =>
        {
            ticker.SetGamePreset("TutorialServer");
            ticker.StartGameRule(TutorialRule, out _);
            ticker.StartRound();
        });
        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            var dragon = proto.Index<TutorialRolePrototype>("TutorialAntagDragon");
            Assert.That(TutorialServerRuleSystem.UsesTravelingCoach(dragon), Is.False,
                "Dragons cannot hold Urist McTutorial; they get a soft-following mentor instead");
            Assert.That(maps.TryLoadTutorialMap(dragon, out var mapUid, out var stationUid, out var spawnCoords),
                Is.True);

            Assert.That(server.EntMan.HasComponent<TutorialDockStationComponent>(stationUid));
            Assert.That(server.EntMan.GetComponent<TutorialDockStationComponent>(stationUid).StationId,
                Is.EqualTo("dragon-prey"));
            Assert.That(server.EntMan.TryGetComponent<TutorialRoomLayoutComponent>(stationUid, out var layout));
            Assert.That(layout!.ChamberCenters.Count, Is.EqualTo(1));

            // Dragon spawn is map-parented (space), not on the station grid.
            var spawnXform = spawnCoords.EntityId;
            Assert.That(spawnXform, Is.EqualTo(mapUid),
                "Dragon must spawn on the map entity (open space), not the station grid");

            EntityUid? pin = null;
            EntityUid? beacon = null;
            var pinQuery = server.EntMan.AllEntityQueryEnumerator<Content.Shared.Pinpointer.PinpointerComponent, TransformComponent>();
            while (pinQuery.MoveNext(out var uid, out var pinComp, out var xform))
            {
                if (xform.MapUid != mapUid)
                    continue;
                if (server.EntMan.GetComponent<MetaDataComponent>(uid).EntityPrototype?.ID !=
                    "TutorialPinpointerDragonStation")
                    continue;
                pin = uid;
                Assert.That(pinComp.IsActive, Is.True, "Pinpointer should start active");
                Assert.That(pinComp.Target, Is.Not.Null, "Pinpointer should target the prey beacon");
                break;
            }

            Assert.That(pin, Is.Not.Null, "Expected TutorialPinpointerDragonStation near space spawn");

            var beaconQuery = server.EntMan.AllEntityQueryEnumerator<TutorialDragonPreyBeaconComponent, TransformComponent>();
            while (beaconQuery.MoveNext(out var uid, out _, out var xform))
            {
                if (xform.GridUid == stationUid)
                    beacon = uid;
            }

            Assert.That(beacon, Is.Not.Null, "Prey station needs a TutorialDragonPreyBeacon");
            Assert.That(server.EntMan.GetComponent<Content.Shared.Pinpointer.PinpointerComponent>(pin!.Value).Target,
                Is.EqualTo(beacon));

            var approach = false;
            var markerQuery = server.EntMan.AllEntityQueryEnumerator<TutorialStepMarkerComponent, TransformComponent>();
            while (markerQuery.MoveNext(out _, out var marker, out var xform))
            {
                if (xform.GridUid != stationUid)
                    continue;
                if (marker.MarkerId == TutorialDragonArenaSystem.StationApproachMarkerId)
                    approach = true;
            }

            Assert.That(approach, Is.True, "Station needs dragon-station approach marker");

            // Spawn practice prey on the station via role load path.
            maps.UnloadTutorialMap(mapUid);
        });
        await pair.RunTicksSync(2);

        // Full session: practice spawns + pacified idle prey on station.
        await server.WaitPost(() =>
        {
            var tutorial = server.System<TutorialServerRuleSystem>();
            tutorial.TrySelectRole(pair.Player!, "TutorialAntagDragon", confirmedStub: false);
        });
        await pair.RunTicksSync(15);

        await server.WaitAssertion(() =>
        {
            Assert.That(pair.Player!.AttachedEntity, Is.Not.Null);
            var mob = pair.Player.AttachedEntity!.Value;
            Assert.That(server.EntMan.HasComponent<TutorialParticipantComponent>(mob), Is.True);
            Assert.That(server.EntMan.HasComponent<Content.Server.Dragon.DragonComponent>(mob), Is.True);

            var mobXform = server.EntMan.GetComponent<TransformComponent>(mob);
            Assert.That(mobXform.GridUid, Is.Null,
                "Dragon body should start in space (no grid)");

            var tutorial = server.System<TutorialServerRuleSystem>();
            Assert.That(tutorial.TryGetSession(mob, out var session), Is.True);
            Assert.That(session!.GuideUid, Is.EqualTo(EntityUid.Invalid),
                "Dragon must not get the Urist McTutorial tablet");
            Assert.That(session.MentorUid, Is.Not.EqualTo(EntityUid.Invalid),
                "Dragon gets Urist McRift as a soft-following mentor");
            Assert.That(server.EntMan.GetComponent<MetaDataComponent>(session.MentorUid).EntityName,
                Is.EqualTo("Urist McRift"));
            Assert.That(server.EntMan.HasComponent<Content.Shared.Damage.Components.GodmodeComponent>(session.MentorUid),
                Is.True,
                "Dragon mentor starts in vacuum beside the player");

            ProtoId<Content.Shared.NPC.Prototypes.NpcFactionPrototype> nanoTrasenFaction = "NanoTrasen";
            var factions = server.System<Content.Shared.NPC.Systems.NpcFactionSystem>();
            var prey = 0;
            var pacified = 0;
            var nanoTrasen = 0;
            var query = server.EntMan.AllEntityQueryEnumerator<TutorialPracticeMobComponent, TransformComponent>();
            while (query.MoveNext(out var uid, out _, out var xform))
            {
                if (xform.MapUid != mobXform.MapUid)
                    continue;
                if (uid == mob)
                    continue;
                if (server.EntMan.HasComponent<TutorialMentorComponent>(uid))
                    continue;
                prey++;
                if (server.EntMan.HasComponent<Content.Shared.CombatMode.Pacification.PacifiedComponent>(uid))
                    pacified++;
                if (factions.IsMember(uid, nanoTrasenFaction))
                    nanoTrasen++;
                Assert.That(server.EntMan.HasComponent<Content.Server.NPC.HTN.HTNComponent>(uid), Is.True,
                    "Idle prey keep IdleCompound HTN");
                Assert.That(xform.GridUid, Is.Not.Null, "Prey must stand on the station grid");
            }

            Assert.That(prey, Is.GreaterThanOrEqualTo(6), "Expected idle prey variety on the bay");
            Assert.That(pacified, Is.EqualTo(prey), "All dragon prey must be Pacified");
            Assert.That(nanoTrasen, Is.EqualTo(prey),
                "Idle prey must be NanoTrasen so Dragon-faction carp attack them");
        });
    }

    [Test]
    public async Task TutorialMothershipCore_ArenaDocksShip()
    {
        var pair = Pair;
        var server = pair.Server;
        var ticker = server.System<GameTicker>();
        var maps = server.System<TutorialMapSystem>();
        var proto = server.ProtoMan;

        await server.WaitPost(() =>
        {
            ticker.SetGamePreset("TutorialServer");
            ticker.StartGameRule(TutorialRule, out _);
            ticker.StartRound();
        });
        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            var core = proto.Index<TutorialRolePrototype>("TutorialAntagMothershipCore");
            Assert.That(maps.TryLoadTutorialMap(core, out var mapUid, out var shuttleUid, out _), Is.True,
                "Mothership arena must load and start docked");

            var home = false;
            var ats = false;
            var query = server.EntMan.AllEntityQueryEnumerator<TutorialDockStationComponent, TransformComponent>();
            while (query.MoveNext(out _, out var station, out var xform))
            {
                if (xform.MapUid != server.EntMan.GetComponent<TransformComponent>(shuttleUid).MapUid)
                    continue;
                if (station.StationId == "mothership-dock")
                    home = true;
                if (station.StationId == "ats")
                    ats = true;
            }

            Assert.That(home, Is.True, "Expected mothership-dock station");
            Assert.That(ats, Is.True, "Expected ats station");
            maps.UnloadTutorialMap(mapUid);
        });
    }

    [Test]
    public async Task TutorialVampire_BloodShimAndClassSelect()
    {
        var pair = Pair;
        var server = pair.Server;
        var vampSys = server.System<Content.Server._Starlight.Antags.Vampires.VampireSystem>();
        var testMap = await pair.CreateTestMap();

        EntityUid vampUid = default;
        await server.WaitAssertion(() =>
        {
            vampUid = server.EntMan.SpawnEntity("MobHuman", testMap.GridCoords);
            vampSys.MakeTutorialVampire(vampUid, classSelectThreshold: 40);

            Assert.That(server.EntMan.TryGetComponent<Content.Shared._Starlight.Antags.Vampires.Components.VampireComponent>(vampUid, out var vamp));
            Assert.That(vamp!.ClassSelectThreshold, Is.EqualTo(40));
            Assert.That(vamp.TutorialSipBlood, Is.GreaterThanOrEqualTo(40));

            vamp.FangsExtended = true;
            vamp.TotalBlood += vamp.TutorialSipBlood;
            vamp.DrunkBlood += vamp.TutorialSipBlood;
            Assert.That(vamp.TotalBlood, Is.GreaterThanOrEqualTo(40));

            vamp.ChosenClassId = "Hemomancer";
            Assert.That(vamp.ChosenClassId, Is.EqualTo("Hemomancer"));
        });
    }

    [Test]
    public async Task TutorialMentor_PassengerSpawnsMentorWithoutGuide()
    {
        var pair = Pair;
        var server = pair.Server;
        var ticker = server.System<GameTicker>();

        await server.WaitPost(() =>
        {
            ticker.SetGamePreset("TutorialServer");
            ticker.StartGameRule(TutorialRule, out _);
            ticker.StartRound();
        });
        await pair.RunTicksSync(5);

        await server.WaitPost(() =>
        {
            server.System<TutorialServerRuleSystem>()
                .TrySelectRole(pair.Player!, "TutorialPassenger", confirmedStub: false);
        });
        await pair.RunTicksSync(60);

        await server.WaitAssertion(() =>
        {
            var player = pair.Player!;
            Assert.That(player.AttachedEntity, Is.Not.Null, "Player should spawn into the passenger tutorial");
            var mob = player.AttachedEntity!.Value;
            var entMan = server.EntMan;

            Assert.That(entMan.TryGetComponent<TutorialParticipantComponent>(mob, out var part));
            Assert.That(part!.StepComplete, Is.EqualTo(TutorialStepComplete.ReachMarker),
                "Passenger first tip is walk to the trainer marker");

            TutorialSessionData? session = null;
            var ruleQuery = entMan.EntityQueryEnumerator<TutorialServerRuleComponent>();
            while (ruleQuery.MoveNext(out _, out var ruleComp))
            {
                if (ruleComp.Sessions.TryGetValue(player.UserId, out session))
                    break;
            }

            Assert.That(session, Is.Not.Null, "Active tutorial session should track the player");
            Assert.That(session!.GuideUid, Is.EqualTo(EntityUid.Invalid), "Single-grid roles have no tablet");
            Assert.That(session.MentorUid, Is.Not.EqualTo(EntityUid.Invalid));
            Assert.That(entMan.EntityExists(session.MentorUid));
            Assert.That(entMan.HasComponent<TutorialMentorComponent>(session.MentorUid));
            Assert.That(entMan.HasComponent<TutorialTrainerComponent>(session.MentorUid));
            Assert.That(entMan.GetComponent<TutorialMentorComponent>(session.MentorUid).PlayerUid, Is.EqualTo(mob));
            Assert.That(entMan.GetComponent<MetaDataComponent>(session.MentorUid).EntityPrototype?.ID,
                Is.EqualTo("TutorialPassengerMentor"));
            Assert.That(entMan.GetComponent<MetaDataComponent>(session.MentorUid).EntityName,
                Is.EqualTo("Urist McGreentide"));

            var hands = server.System<SharedHandsSystem>();
            Assert.That(entMan.TryGetComponent<HandsComponent>(mob, out var handsComp));
            foreach (var handId in hands.EnumerateHands((mob, handsComp!)))
            {
                var held = hands.GetHeldItem((mob, handsComp), handId);
                if (held != null)
                    Assert.That(entMan.HasComponent<TutorialGuideComponent>(held.Value), Is.False);
            }
        });
    }

    [Test]
    public async Task TutorialMentor_CatchUpDelaysSnapUntilPathFails()
    {
        var pair = Pair;
        var server = pair.Server;
        var ticker = server.System<GameTicker>();
        var tutorial = server.System<TutorialServerRuleSystem>();
        var follow = server.System<TutorialMentorFollowSystem>();
        var timing = server.ResolveDependency<IGameTiming>();
        var xforms = server.System<SharedTransformSystem>();

        await server.WaitPost(() =>
        {
            ticker.SetGamePreset("TutorialServer");
            ticker.StartGameRule(TutorialRule, out _);
            ticker.StartRound();
        });
        await pair.RunTicksSync(5);

        await server.WaitPost(() =>
        {
            tutorial.TrySelectRole(pair.Player!, "TutorialPassenger", confirmedStub: false);
        });
        await pair.RunTicksSync(60);

        EntityUid mentor = default;
        EntityUid mob = default;
        EntityUid gridUid = default;

        await server.WaitAssertion(() =>
        {
            mob = pair.Player!.AttachedEntity!.Value;
            TutorialSessionData? session = null;
            var ruleQuery = server.EntMan.EntityQueryEnumerator<TutorialServerRuleComponent>();
            while (ruleQuery.MoveNext(out _, out var ruleComp))
            {
                if (ruleComp.Sessions.TryGetValue(pair.Player!.UserId, out session))
                    break;
            }

            Assert.That(session, Is.Not.Null);
            mentor = session!.MentorUid;
            gridUid = server.EntMan.GetComponent<TransformComponent>(mob).GridUid!.Value;
        });

        Vector2 mentorPosBefore = default;
        await server.WaitPost(() =>
        {
            var layout = server.EntMan.GetComponent<TutorialRoomLayoutComponent>(gridUid);
            Assert.That(layout.ChamberCenters.Count, Is.EqualTo(2));
            // Separate across the sealed pry gate so catch-up is needed.
            xforms.SetCoordinates(mentor, new EntityCoordinates(gridUid, layout.ChamberCenters[0]));
            xforms.SetCoordinates(mob, new EntityCoordinates(gridUid, layout.ChamberCenters[1]));
            mentorPosBefore = xforms.GetWorldPosition(mentor);
            follow.RequestCatchUp(mentor, restart: true);
        });
        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            var mentorComp = server.EntMan.GetComponent<TutorialMentorComponent>(mentor);
            Assert.That(mentorComp.CatchUpDeadline, Is.Not.Null,
                "Separated mentor should start a delayed catch-up window");
            Assert.That(mentorComp.CatchUpDeadline!.Value, Is.GreaterThan(timing.CurTime));
            Assert.That(xforms.GetWorldPosition(mentor), Is.EqualTo(mentorPosBefore),
                "Mentor must not teleport during the catch-up delay");
        });

        // Expire the grace window so Update queues a path check.
        await server.WaitPost(() =>
        {
            var mentorComp = server.EntMan.GetComponent<TutorialMentorComponent>(mentor);
            mentorComp.CatchUpDeadline = timing.CurTime - TimeSpan.FromSeconds(0.1);
        });

        // Enough ticks for pathfinding + continuation (sealed gate => NoPath => snap).
        await pair.RunTicksSync(90);

        await server.WaitAssertion(() =>
        {
            var mentorComp = server.EntMan.GetComponent<TutorialMentorComponent>(mentor);
            var dist = (xforms.GetWorldPosition(mentor) - xforms.GetWorldPosition(mob)).Length();
            Assert.That(dist, Is.LessThan(4f),
                "After grace + failed path across sealed chambers, mentor should snap near the player");
            Assert.That(mentorComp.CatchUpDeadline, Is.Null);
        });
    }

    [Test]
    public async Task TutorialGuide_CargoHybridGivesGuideAndStationaryQm()
    {
        var pair = Pair;
        var server = pair.Server;
        var ticker = server.System<GameTicker>();
        var proto = server.ProtoMan;
        var ui = server.System<UserInterfaceSystem>();

        await server.WaitAssertion(() =>
        {
            var cargo = proto.Index<TutorialRolePrototype>("TutorialCargoTechnician");
            Assert.That(TutorialServerRuleSystem.UsesTravelingCoach(cargo), Is.True);
            Assert.That(cargo.SpawnStationaryMentor, Is.True);
            var salvage = proto.Index<TutorialRolePrototype>("TutorialSalvageSpecialist");
            Assert.That(TutorialServerRuleSystem.UsesTravelingCoach(salvage), Is.True);
            Assert.That(salvage.SpawnStationaryMentor, Is.False);
        });

        await server.WaitPost(() =>
        {
            ticker.SetGamePreset("TutorialServer");
            ticker.StartGameRule(TutorialRule, out _);
            ticker.StartRound();
        });
        await pair.RunTicksSync(5);

        await server.WaitPost(() =>
        {
            server.System<TutorialServerRuleSystem>()
                .TrySelectRole(pair.Player!, "TutorialCargoTechnician", confirmedStub: false);
        });
        await pair.RunTicksSync(60);

        await server.WaitAssertion(() =>
        {
            var player = pair.Player!;
            Assert.That(player.AttachedEntity, Is.Not.Null);
            var mob = player.AttachedEntity!.Value;
            var entMan = server.EntMan;

            Assert.That(entMan.TryGetComponent<TutorialParticipantComponent>(mob, out var part));
            Assert.That(part!.StepComplete, Is.EqualTo(TutorialStepComplete.Acknowledge));

            TutorialSessionData? session = null;
            var ruleQuery = entMan.EntityQueryEnumerator<TutorialServerRuleComponent>();
            while (ruleQuery.MoveNext(out _, out var ruleComp))
            {
                if (ruleComp.Sessions.TryGetValue(player.UserId, out session))
                    break;
            }

            Assert.That(session, Is.Not.Null);
            Assert.That(session!.GuideUid, Is.Not.EqualTo(EntityUid.Invalid));
            Assert.That(entMan.HasComponent<TutorialGuideComponent>(session.GuideUid));
            Assert.That(entMan.GetComponent<MetaDataComponent>(session.GuideUid).EntityName,
                Is.EqualTo("Urist McTutorial"));
            Assert.That(ui.IsUiOpen(session.GuideUid, TutorialPromptUiKey.Key, mob), Is.False,
                "Cargo defers guide open so the bay QM can speak");

            Assert.That(session.MentorUid, Is.Not.EqualTo(EntityUid.Invalid), "Cargo hybrid spawns a bay QM");
            Assert.That(entMan.HasComponent<TutorialMentorComponent>(session.MentorUid));
            Assert.That(entMan.HasComponent<TutorialTrainerComponent>(session.MentorUid));
            Assert.That(entMan.HasComponent<Content.Server.NPC.HTN.HTNComponent>(session.MentorUid), Is.False,
                "Stationary QM must not HTN-follow");
            Assert.That(entMan.GetComponent<MetaDataComponent>(session.MentorUid).EntityName,
                Is.EqualTo("Urist McQuartermaster"));
            Assert.That(entMan.GetComponent<MetaDataComponent>(session.MentorUid).EntityPrototype?.ID,
                Is.EqualTo("TutorialCargoQmMentor"));
            Assert.That(entMan.TryGetComponent<LoadoutComponent>(session.MentorUid, out var qmLoadout), Is.True);
            Assert.That(qmLoadout!.StartingGear, Is.Not.Null);
            Assert.That(qmLoadout.StartingGear!, Does.Contain(new ProtoId<StartingGearPrototype>("VisitorQM")),
                "Cargo QM mentor must wear a full QM outfit, not accessory-only QuartermasterGear");

            var guideSys = server.System<TutorialGuideSystem>();
            var guide = new Entity<TutorialGuideComponent>(
                session.GuideUid,
                entMan.GetComponent<TutorialGuideComponent>(session.GuideUid));

            ui.OpenUi(session.GuideUid, TutorialPromptUiKey.Key, mob);
            var state = guideSys.GetUiState(guide, mob);
            Assert.That(state.HasTutorial, Is.True);
            Assert.That(state.CanGoBack, Is.False);
            Assert.That(state.CanGoNext, Is.True, "Acknowledge tip can advance via Next");
            Assert.That(state.WaitingOnSensor, Is.False);

            Assert.That(guideSys.TryGoNext(guide, mob), Is.True);
            Assert.That(entMan.TryGetComponent(mob, out part));
            // Welcome has a single Acknowledge tip; Next moves to goal "order" / orders console.
            Assert.That(part!.GoalIndex, Is.EqualTo(1));
            Assert.That(part.StepComplete, Is.EqualTo(TutorialStepComplete.InteractTargetTag));
            Assert.That(guideSys.TryGoNext(guide, mob), Is.False,
                "InteractTargetTag tip cannot be skipped with Next");
            state = guideSys.GetUiState(guide, mob);
            Assert.That(state.CanGoNext, Is.False);
            Assert.That(state.WaitingOnSensor, Is.True);

            // Force into pilot / open-console and confirm Next still cannot skip sensors.
            var tutorialSys = server.System<TutorialServerRuleSystem>();
            tutorialSys.AdvanceSubGoal(mob); // purchase
            tutorialSys.AdvanceSubGoal(mob); // confirm
            tutorialSys.AdvanceSubGoal(mob); // drag-crate
            tutorialSys.AdvanceSubGoal(mob); // board-shuttle
            ui.CloseUi(session.GuideUid, TutorialPromptUiKey.Key, mob);
            tutorialSys.AdvanceSubGoal(mob); // controls
            Assert.That(entMan.TryGetComponent(mob, out part));
            Assert.That(part!.StepComplete, Is.EqualTo(TutorialStepComplete.Acknowledge));
            Assert.That(tutorialSys.TryGetCurrentSubGoal(mob, part, out var controlsSub));
            Assert.That(controlsSub.Id, Is.EqualTo("controls"));
            Assert.That(controlsSub.SuppressControlHint, Is.True);
            Assert.That(session.GuideAutoOpened, Is.False);
            Assert.That(ui.IsUiOpen(session.GuideUid, TutorialPromptUiKey.Key, mob), Is.False,
                "Controls step should not force-open the guide tablet");

            tutorialSys.AdvanceSubGoal(mob); // open-console
            Assert.That(entMan.TryGetComponent(mob, out part));
            Assert.That(part!.StepComplete, Is.EqualTo(TutorialStepComplete.PilotShuttle));
            Assert.That(guideSys.TryGoNext(guide, mob), Is.False);
            state = guideSys.GetUiState(guide, mob);
            Assert.That(state.CanGoNext, Is.False);
            Assert.That(state.WaitingOnSensor, Is.True);
        });
    }

    [Test]
    public async Task TutorialGuide_AutoOpensPromptOnStart()
    {
        var pair = Pair;
        var server = pair.Server;
        var ticker = server.System<GameTicker>();

        await server.WaitPost(() =>
        {
            ticker.SetGamePreset("TutorialServer");
            ticker.StartGameRule(TutorialRule, out _);
            ticker.StartRound();
        });
        await pair.RunTicksSync(5);

        // Salvage keeps AutoOpenGuide on the talking tablet (Cargo defers open for the bay QM).
        await server.WaitPost(() =>
        {
            server.System<TutorialServerRuleSystem>()
                .TrySelectRole(pair.Player!, "TutorialSalvageSpecialist", confirmedStub: false);
        });
        await pair.RunTicksSync(60);

        await server.WaitAssertion(() =>
        {
            var player = pair.Player!;
            Assert.That(player.AttachedEntity, Is.Not.Null);
            var mob = player.AttachedEntity!.Value;
            var entMan = server.EntMan;

            TutorialSessionData? session = null;
            var ruleQuery = entMan.EntityQueryEnumerator<TutorialServerRuleComponent>();
            while (ruleQuery.MoveNext(out _, out var ruleComp))
            {
                if (ruleComp.Sessions.TryGetValue(player.UserId, out session))
                    break;
            }

            Assert.That(session, Is.Not.Null);
            Assert.That(session!.GuideUid, Is.Not.EqualTo(EntityUid.Invalid));
            Assert.That(session.MentorUid, Is.EqualTo(EntityUid.Invalid));
            var ui = server.System<UserInterfaceSystem>();
            Assert.That(ui.IsUiOpen(session.GuideUid, TutorialPromptUiKey.Key, mob), Is.True,
                "Prompt Bound UI should auto-open once at tutorial start for travel roles with AutoOpenGuide");
        });
    }

    [Test]
    public async Task TutorialPassenger_MentorKeepsHandsFreeAndMapGates()
    {
        var pair = Pair;
        var server = pair.Server;
        var ticker = server.System<GameTicker>();

        await server.WaitPost(() =>
        {
            ticker.SetGamePreset("TutorialServer");
            ticker.StartGameRule(TutorialRule, out _);
            ticker.StartRound();
        });
        await pair.RunTicksSync(5);

        await server.WaitPost(() =>
        {
            server.System<TutorialServerRuleSystem>()
                .TrySelectRole(pair.Player!, "TutorialPassenger", confirmedStub: false);
        });
        await pair.RunTicksSync(60);

        await server.WaitAssertion(() =>
        {
            var player = pair.Player!;
            Assert.That(player.AttachedEntity, Is.Not.Null);
            var mob = player.AttachedEntity!.Value;
            var entMan = server.EntMan;
            var tutorial = server.System<TutorialServerRuleSystem>();

            TutorialSessionData? session = null;
            var ruleQuery = entMan.EntityQueryEnumerator<TutorialServerRuleComponent>();
            while (ruleQuery.MoveNext(out _, out var ruleComp))
            {
                if (ruleComp.Sessions.TryGetValue(player.UserId, out session))
                    break;
            }

            Assert.That(session, Is.Not.Null);
            Assert.That(session!.GuideUid, Is.EqualTo(EntityUid.Invalid));
            Assert.That(session.MentorUid, Is.Not.EqualTo(EntityUid.Invalid));
            Assert.That(session.GuideAutoOpened, Is.False);

            var hands = server.System<SharedHandsSystem>();
            Assert.That(entMan.TryGetComponent<HandsComponent>(mob, out var handsComp));
            string? leftHand = null;
            string? rightHand = null;
            foreach (var handId in hands.EnumerateHands((mob, handsComp!)))
            {
                if (!hands.TryGetHand((mob, handsComp), handId, out var hand))
                    continue;
                if (hand.Value.Location == HandLocation.Left)
                    leftHand = handId;
                else if (hand.Value.Location == HandLocation.Right)
                    rightHand = handId;
            }

            Assert.That(leftHand, Is.Not.Null);
            Assert.That(hands.HandIsEmpty((mob, handsComp), leftHand!), Is.True,
                "No tablet — left hand stays free for pickup practice");
            Assert.That(rightHand, Is.Not.Null);
            Assert.That(hands.HandIsEmpty((mob, handsComp), rightHand!), Is.True,
                "Active right hand should stay free for pickup practice");

            Assert.That(entMan.TryGetComponent<GravityComponent>(session.GridUid, out var gravity));
            Assert.That(gravity!.Enabled, Is.True, "Passenger map must have gravity enabled");
            Assert.That(gravity.Inherent, Is.True);

            Assert.That(entMan.TryGetComponent<TutorialRoomLayoutComponent>(session.GridUid, out var layout));
            Assert.That(layout!.ChamberCenters.Count, Is.EqualTo(2));
            Assert.That(layout.GateDoors.Count, Is.EqualTo(1));
            var gate = layout.GateDoors[0];
            Assert.That(entMan.GetComponent<MetaDataComponent>(gate).EntityPrototype?.ID,
                Is.EqualTo("TutorialAirlockMaint"));
            Assert.That(entMan.GetComponent<TutorialGateDoorComponent>(gate).RequirePry, Is.True);
            Assert.That(entMan.GetComponent<TutorialGateDoorComponent>(gate).Unlocked, Is.False,
                "Pry exit must stay closed through welcome");

            // Complete all welcome sub-goals (walk + interact/storage in one chamber).
            for (var i = 0; i < 8; i++)
                tutorial.AdvanceSubGoal(mob);

            ruleQuery = entMan.EntityQueryEnumerator<TutorialServerRuleComponent>();
            while (ruleQuery.MoveNext(out _, out var ruleComp))
            {
                if (ruleComp.Sessions.TryGetValue(player.UserId, out session))
                    break;
            }

            Assert.That(session, Is.Not.Null);
            Assert.That(session!.GoalIndex, Is.EqualTo(1));
            Assert.That(session.MentorUid, Is.Not.EqualTo(EntityUid.Invalid));
            Assert.That(entMan.EntityExists(session.MentorUid), Is.True, "Mentor persists across goals");
            Assert.That(entMan.GetComponent<TutorialGateDoorComponent>(gate).Unlocked, Is.False,
                "Pry exit must remain closed after welcome unlock pass");
        });
    }

    [Test]
    public async Task TutorialPassenger_TrainerSpeaksRepeatsAndDropAdvances()
    {
        var pair = Pair;
        var server = pair.Server;
        var ticker = server.System<GameTicker>();

        await server.WaitPost(() =>
        {
            ticker.SetGamePreset("TutorialServer");
            ticker.StartGameRule(TutorialRule, out _);
            ticker.StartRound();
        });
        await pair.RunTicksSync(5);

        await server.WaitPost(() =>
        {
            server.System<TutorialServerRuleSystem>()
                .TrySelectRole(pair.Player!, "TutorialPassenger", confirmedStub: false);
        });
        await pair.RunTicksSync(60);

        await server.WaitAssertion(() =>
        {
            var player = pair.Player!;
            Assert.That(player.AttachedEntity, Is.Not.Null);
            var mob = player.AttachedEntity!.Value;
            var entMan = server.EntMan;
            var coach = server.System<TutorialTrainerSystem>();

            EntityUid? trainerUid = null;
            var trainers = entMan.EntityQueryEnumerator<TutorialTrainerComponent>();
            while (trainers.MoveNext(out var uid, out _))
            {
                trainerUid = uid;
                break;
            }

            Assert.That(trainerUid, Is.Not.Null, "Passenger map should spawn a trainer");
            var trainer = entMan.GetComponent<TutorialTrainerComponent>(trainerUid.Value);
            Assert.That(trainer.LastSpokenSubGoal, Is.EqualTo("meet-trainer"),
                "Trainer should speak the opening line after spawn");

            Assert.That(entMan.TryGetComponent<TutorialParticipantComponent>(mob, out var part));
            Assert.That(coach.TryResolveDialogue(trainerUid.Value, trainer, mob, part!, out var subGoalId, out var dialogue));
            Assert.That(subGoalId, Is.EqualTo("meet-trainer"));
            Assert.That(dialogue, Does.Contain("WASD (default)"),
                "Passenger opening line names the default move keys for new players");

            var interact = new InteractHandEvent(mob, trainerUid.Value);
            entMan.EventBus.RaiseLocalEvent(trainerUid.Value, interact);
            Assert.That(interact.Handled, Is.True,
                "Empty-hand click / hug on mentor should handle and re-speak");

            trainer = entMan.GetComponent<TutorialTrainerComponent>(trainerUid.Value);
            Assert.That(trainer.LastSpokenSubGoal, Is.EqualTo("meet-trainer"));
        });

        // Advance one tip and let the mentor speak the new line on the next Update.
        await server.WaitPost(() =>
        {
            var mob = pair.Player!.AttachedEntity!.Value;
            server.System<TutorialServerRuleSystem>().AdvanceSubGoal(mob);
        });
        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            var player = pair.Player!;
            var mob = player.AttachedEntity!.Value;
            var entMan = server.EntMan;
            var tutorial = server.System<TutorialServerRuleSystem>();
            var hands = server.System<SharedHandsSystem>();

            TutorialSessionData? session = null;
            var ruleQuery = entMan.EntityQueryEnumerator<TutorialServerRuleComponent>();
            while (ruleQuery.MoveNext(out _, out var ruleComp))
            {
                if (ruleComp.Sessions.TryGetValue(player.UserId, out session))
                    break;
            }

            Assert.That(session, Is.Not.Null);
            var trainer = entMan.GetComponent<TutorialTrainerComponent>(session!.MentorUid);
            Assert.That(trainer.LastSpokenSubGoal, Is.EqualTo("pick-crowbar"),
                "Mentor must IC-speak the next tip after curriculum advances");

            // Advance meet was already done; advance pick -> drop-crowbar.
            tutorial.AdvanceSubGoal(mob);
            Assert.That(entMan.GetComponent<TutorialParticipantComponent>(mob).StepComplete,
                Is.EqualTo(TutorialStepComplete.DropItem));

            var crowbar = entMan.SpawnEntity("Crowbar", entMan.GetComponent<TransformComponent>(mob).Coordinates);
            Assert.That(hands.TryPickupAnyHand(mob, crowbar, checkActionBlocker: false, animate: false));
            Assert.That(hands.TryDrop(mob, crowbar, checkActionBlocker: false));

            Assert.That(entMan.GetComponent<TutorialParticipantComponent>(mob).StepComplete,
                Is.EqualTo(TutorialStepComplete.HoldItem),
                "World-dropping a Crowbar should advance DropItem to hold-light");
        });
    }

    [Test]
    public async Task TutorialJoinCommand_OpensRolePicker()
    {
        var pair = Pair;
        var server = pair.Server;
        var ticker = server.System<GameTicker>();
        var tutorial = server.System<TutorialServerRuleSystem>();
        var console = server.ResolveDependency<IConsoleHost>();

        await server.WaitPost(() =>
        {
            ticker.SetGamePreset("TutorialServer");
            ticker.StartGameRule(TutorialRule, out _);
            ticker.StartRound();
        });
        await pair.RunTicksSync(5);

        await server.WaitPost(() => console.ExecuteCommand(pair.Player!, "jointutorial"));
        await pair.RunTicksSync(10);

        await server.WaitAssertion(() =>
        {
            Assert.That(tutorial.IsPickerOpen(pair.Player!), Is.True,
                "jointutorial should open the Choose a tutorial picker");
            var player = pair.Player!;
            Assert.That(player.AttachedEntity, Is.Not.Null);
            Assert.That(server.EntMan.HasComponent<GhostComponent>(player.AttachedEntity!.Value), Is.True,
                "jointutorial must attach an observer so the client has a valid map");
        });
    }

    [Test]
    public async Task TutorialPicker_DismissStaysClosedAndChooseReopens()
    {
        var pair = Pair;
        var server = pair.Server;
        var ticker = server.System<GameTicker>();
        var tutorial = server.System<TutorialServerRuleSystem>();

        await server.WaitPost(() =>
        {
            ticker.SetGamePreset("TutorialServer");
            ticker.StartGameRule(TutorialRule, out _);
            ticker.StartRound();
        });
        await pair.RunTicksSync(5);

        await server.WaitPost(() =>
        {
            // Join without a role so BeforeSpawn opens the picker.
            ticker.MakeJoinGame(pair.Player!, EntityUid.Invalid, silent: true);
        });
        await pair.RunTicksSync(10);

        await server.WaitAssertion(() =>
        {
            Assert.That(tutorial.IsPickerOpen(pair.Player!), Is.True, "Picker should open when joining without a role");
            var player = pair.Player!;
            Assert.That(player.AttachedEntity, Is.Not.Null);
            Assert.That(server.EntMan.HasComponent<GhostComponent>(player.AttachedEntity!.Value), Is.True,
                "Picker-only join must attach an observer so the client has a valid map");
        });

        await server.WaitPost(() => tutorial.OnPickerClosed(pair.Player!));
        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            Assert.That(tutorial.IsPickerOpen(pair.Player!), Is.False,
                "Dismiss must not force-reopen the picker (locked-open bug)");
            var player = pair.Player!;
            Assert.That(player.AttachedEntity, Is.Not.Null);
            Assert.That(server.EntMan.HasComponent<GhostComponent>(player.AttachedEntity!.Value), Is.True,
                "Dismiss should leave / create an observer");

            TutorialSessionData? session = null;
            var ruleQuery = server.EntMan.EntityQueryEnumerator<TutorialServerRuleComponent>();
            while (ruleQuery.MoveNext(out _, out var ruleComp))
            {
                if (ruleComp.Sessions.TryGetValue(player.UserId, out session))
                    break;
            }

            Assert.That(session, Is.Not.Null);
            Assert.That(session!.PickerQuit, Is.True);
        });

        await server.WaitPost(() => tutorial.TryOpenRolePicker(pair.Player!));
        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            Assert.That(tutorial.IsPickerOpen(pair.Player!), Is.True,
                "Choose a tutorial reopens the role picker after dismiss");
        });

        await server.WaitPost(() => tutorial.OnPickerQuit(pair.Player!));
        await pair.RunTicksSync(10);

        await server.WaitAssertion(() =>
        {
            Assert.That(tutorial.IsPickerOpen(pair.Player!), Is.False, "Quit must not leave the picker open");
        });

        await server.WaitPost(() => tutorial.OnPickerClosed(pair.Player!));
        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            Assert.That(tutorial.IsPickerOpen(pair.Player!), Is.False,
                "Dismiss after Quit must not re-open the picker");
        });

        await server.WaitPost(() => tutorial.TryOpenRolePicker(pair.Player!));
        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            Assert.That(tutorial.IsPickerOpen(pair.Player!), Is.True,
                "Ghosts can reopen the role picker via Choose a tutorial");
        });
    }

    [Test]
    public async Task TutorialGhost_AliveGhostOpensRolePicker()
    {
        var pair = Pair;
        var server = pair.Server;
        var ticker = server.System<GameTicker>();
        var tutorial = server.System<TutorialServerRuleSystem>();
        var ghosts = server.System<GhostSystem>();
        var minds = server.System<SharedMindSystem>();

        await server.WaitPost(() =>
        {
            ticker.SetGamePreset("TutorialServer");
            ticker.StartGameRule(TutorialRule, out _);
            ticker.StartRound();
        });
        await pair.RunTicksSync(5);

        await server.WaitPost(() =>
        {
            tutorial.TrySelectRole(pair.Player!, "TutorialPassenger", confirmedStub: false);
        });
        await pair.RunTicksSync(60);

        EntityUid mob = default;
        await server.WaitAssertion(() =>
        {
            Assert.That(pair.Player!.AttachedEntity, Is.Not.Null);
            mob = pair.Player.AttachedEntity!.Value;
            Assert.That(server.EntMan.HasComponent<TutorialParticipantComponent>(mob), Is.True);

            var actions = server.System<SharedActionsSystem>();
            var hasChoose = false;
            foreach (var (actionUid, _) in actions.GetActions(mob))
            {
                if (server.EntMan.GetComponent<MetaDataComponent>(actionUid).EntityPrototype?.ID ==
                    "ActionTutorialChooseRole")
                {
                    hasChoose = true;
                    break;
                }
            }

            Assert.That(hasChoose, Is.True, "Living tutorial spawn must get Choose a tutorial");
        });

        await server.WaitPost(() =>
        {
            Assert.That(minds.TryGetMind(pair.Player!, out var mindId, out var mind), Is.True);
            Assert.That(ghosts.OnGhostAttempt(mindId, canReturnGlobal: true, viaCommand: true, mind: mind), Is.True);
        });
        await pair.RunTicksSync(20);

        await server.WaitAssertion(() =>
        {
            var player = pair.Player!;
            Assert.That(player.AttachedEntity, Is.Not.Null);
            Assert.That(server.EntMan.HasComponent<GhostComponent>(player.AttachedEntity!.Value), Is.True,
                "/ghost should leave the player as an observer");
            Assert.That(tutorial.IsPickerOpen(player), Is.True,
                "/ghost from a living tutorial body must open the role picker");

            TutorialSessionData? session = null;
            var ruleQuery = server.EntMan.EntityQueryEnumerator<TutorialServerRuleComponent>();
            while (ruleQuery.MoveNext(out _, out var ruleComp))
            {
                if (ruleComp.Sessions.TryGetValue(player.UserId, out session))
                    break;
            }

            Assert.That(session, Is.Not.Null);
            Assert.That(session!.SelectedRoleId, Is.Null);
            Assert.That(session.State, Is.EqualTo(TutorialSessionState.PendingSelect));

            Assert.That(Loc.TryGetString("ent-ActionTutorialChooseRole", out var actionName), Is.True);
            Assert.That(actionName, Is.EqualTo("Choose a tutorial"));
        });
    }

    [Test]
    public async Task TutorialChooseAction_FromLivingBodyOpensRolePicker()
    {
        var pair = Pair;
        var server = pair.Server;
        var ticker = server.System<GameTicker>();
        var tutorial = server.System<TutorialServerRuleSystem>();
        var actions = server.System<SharedActionsSystem>();
        var entMan = server.EntMan;

        await server.WaitPost(() =>
        {
            ticker.SetGamePreset("TutorialServer");
            ticker.StartGameRule(TutorialRule, out _);
            ticker.StartRound();
        });
        await pair.RunTicksSync(5);

        await server.WaitPost(() =>
        {
            tutorial.TrySelectRole(pair.Player!, "TutorialPassenger", confirmedStub: false);
        });
        await pair.RunTicksSync(60);

        EntityUid body = default;
        await server.WaitPost(() =>
        {
            var mob = pair.Player!.AttachedEntity!.Value;
            body = mob;
            EntityUid? chooseAction = null;
            foreach (var (actionUid, _) in actions.GetActions(mob))
            {
                if (entMan.GetComponent<MetaDataComponent>(actionUid).EntityPrototype?.ID ==
                    "ActionTutorialChooseRole")
                {
                    chooseAction = actionUid;
                    break;
                }
            }

            Assert.That(chooseAction, Is.Not.Null);
            actions.PerformAction(mob, (chooseAction.Value, entMan.GetComponent<ActionComponent>(chooseAction.Value)));
        });
        await pair.RunTicksSync(20);

        await server.WaitAssertion(() =>
        {
            var player = pair.Player!;
            Assert.That(player.AttachedEntity, Is.EqualTo(body),
                "Choose a tutorial must not leave the current body until a role is selected");
            Assert.That(entMan.HasComponent<GhostComponent>(body), Is.False);
            Assert.That(tutorial.IsPickerOpen(player), Is.True,
                "Choose a tutorial from a living body must open the role picker");

            TutorialSessionData? session = null;
            var ruleQuery = entMan.EntityQueryEnumerator<TutorialServerRuleComponent>();
            while (ruleQuery.MoveNext(out _, out var ruleComp))
            {
                if (ruleComp.Sessions.TryGetValue(player.UserId, out session))
                    break;
            }

            Assert.That(session, Is.Not.Null);
            Assert.That(session!.State, Is.EqualTo(TutorialSessionState.InTutorial));
            Assert.That(session.BodyUid, Is.EqualTo(body));
        });
    }

    [Test]
    public async Task TutorialChooseAction_CancelKeepsCurrentTutorial()
    {
        var pair = Pair;
        var server = pair.Server;
        var ticker = server.System<GameTicker>();
        var tutorial = server.System<TutorialServerRuleSystem>();
        var actions = server.System<SharedActionsSystem>();
        var entMan = server.EntMan;

        await server.WaitPost(() =>
        {
            ticker.SetGamePreset("TutorialServer");
            ticker.StartGameRule(TutorialRule, out _);
            ticker.StartRound();
        });
        await pair.RunTicksSync(5);

        await server.WaitPost(() =>
        {
            tutorial.TrySelectRole(pair.Player!, "TutorialPassenger", confirmedStub: false);
        });
        await pair.RunTicksSync(60);

        EntityUid body = default;
        EntityUid mapUid = default;
        await server.WaitAssertion(() =>
        {
            Assert.That(pair.Player!.AttachedEntity, Is.Not.Null);
            body = pair.Player.AttachedEntity!.Value;

            TutorialSessionData? session = null;
            var ruleQuery = entMan.EntityQueryEnumerator<TutorialServerRuleComponent>();
            while (ruleQuery.MoveNext(out _, out var ruleComp))
            {
                if (ruleComp.Sessions.TryGetValue(pair.Player.UserId, out session))
                    break;
            }

            Assert.That(session, Is.Not.Null);
            mapUid = session!.MapUid;
        });

        await server.WaitPost(() =>
        {
            EntityUid? chooseAction = null;
            foreach (var (actionUid, _) in actions.GetActions(body))
            {
                if (entMan.GetComponent<MetaDataComponent>(actionUid).EntityPrototype?.ID ==
                    "ActionTutorialChooseRole")
                {
                    chooseAction = actionUid;
                    break;
                }
            }

            Assert.That(chooseAction, Is.Not.Null);
            actions.PerformAction(body, (chooseAction.Value, entMan.GetComponent<ActionComponent>(chooseAction.Value)));
        });
        await pair.RunTicksSync(10);

        await server.WaitAssertion(() =>
        {
            Assert.That(tutorial.IsPickerOpen(pair.Player!), Is.True);
        });

        // Quit must leave the living tutorial untouched.
        await server.WaitPost(() => tutorial.OnPickerQuit(pair.Player!));
        await pair.RunTicksSync(10);

        await server.WaitAssertion(() =>
        {
            var player = pair.Player!;
            Assert.That(tutorial.IsPickerOpen(player), Is.False);
            Assert.That(player.AttachedEntity, Is.EqualTo(body));
            Assert.That(entMan.HasComponent<GhostComponent>(body), Is.False);
            Assert.That(entMan.Deleted(body), Is.False);
            Assert.That(entMan.Deleted(mapUid), Is.False);

            TutorialSessionData? session = null;
            var ruleQuery = entMan.EntityQueryEnumerator<TutorialServerRuleComponent>();
            while (ruleQuery.MoveNext(out _, out var ruleComp))
            {
                if (ruleComp.Sessions.TryGetValue(player.UserId, out session))
                    break;
            }

            Assert.That(session, Is.Not.Null);
            Assert.That(session!.State, Is.EqualTo(TutorialSessionState.InTutorial));
            Assert.That(session.BodyUid, Is.EqualTo(body));
            Assert.That(session.MapUid, Is.EqualTo(mapUid));
            Assert.That(session.SelectedRoleId, Is.EqualTo("TutorialPassenger"));
            Assert.That(session.PickerQuit, Is.False);
        });

        // Re-open and dismiss with X — same no-op.
        await server.WaitPost(() => tutorial.TryOpenRolePicker(pair.Player!));
        await pair.RunTicksSync(5);
        await server.WaitPost(() => tutorial.OnPickerClosed(pair.Player!));
        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            var player = pair.Player!;
            Assert.That(tutorial.IsPickerOpen(player), Is.False);
            Assert.That(player.AttachedEntity, Is.EqualTo(body));

            TutorialSessionData? session = null;
            var ruleQuery = entMan.EntityQueryEnumerator<TutorialServerRuleComponent>();
            while (ruleQuery.MoveNext(out _, out var ruleComp))
            {
                if (ruleComp.Sessions.TryGetValue(player.UserId, out session))
                    break;
            }

            Assert.That(session, Is.Not.Null);
            Assert.That(session!.State, Is.EqualTo(TutorialSessionState.InTutorial));
            Assert.That(session.BodyUid, Is.EqualTo(body));
        });
    }

    [Test]
    public async Task TutorialChooseAction_ReselectPassengerSpawnsCleanly()
    {
        var pair = Pair;
        var server = pair.Server;
        var ticker = server.System<GameTicker>();
        var tutorial = server.System<TutorialServerRuleSystem>();
        var actions = server.System<SharedActionsSystem>();
        var entMan = server.EntMan;

        await server.WaitPost(() =>
        {
            ticker.SetGamePreset("TutorialServer");
            ticker.StartGameRule(TutorialRule, out _);
            ticker.StartRound();
        });
        await pair.RunTicksSync(5);

        await server.WaitPost(() =>
        {
            tutorial.TrySelectRole(pair.Player!, "TutorialPassenger", confirmedStub: false);
        });
        await pair.RunTicksSync(60);

        EntityUid firstBody = default;
        await server.WaitAssertion(() =>
        {
            Assert.That(pair.Player!.AttachedEntity, Is.Not.Null);
            firstBody = pair.Player.AttachedEntity!.Value;
            Assert.That(entMan.HasComponent<TutorialParticipantComponent>(firstBody), Is.True);
        });

        await server.WaitPost(() =>
        {
            var mob = pair.Player!.AttachedEntity!.Value;
            EntityUid? chooseAction = null;
            foreach (var (actionUid, _) in actions.GetActions(mob))
            {
                if (entMan.GetComponent<MetaDataComponent>(actionUid).EntityPrototype?.ID ==
                    "ActionTutorialChooseRole")
                {
                    chooseAction = actionUid;
                    break;
                }
            }

            Assert.That(chooseAction, Is.Not.Null);
            actions.PerformAction(mob, (chooseAction.Value, entMan.GetComponent<ActionComponent>(chooseAction.Value)));
        });
        await pair.RunTicksSync(20);

        await server.WaitAssertion(() =>
        {
            var player = pair.Player!;
            Assert.That(player.AttachedEntity, Is.EqualTo(firstBody),
                "Opening the picker must not tear down the current tutorial");
            Assert.That(entMan.HasComponent<GhostComponent>(firstBody), Is.False);
            Assert.That(tutorial.IsPickerOpen(player), Is.True);
        });

        // Selecting a role is what leaves the current tutorial and spawns cleanly.
        await server.WaitPost(() =>
        {
            tutorial.TrySelectRole(pair.Player!, "TutorialPassenger", confirmedStub: false);
        });
        await pair.RunTicksSync(60);

        await server.WaitAssertion(() =>
        {
            var player = pair.Player!;
            Assert.That(player.AttachedEntity, Is.Not.Null, "Passenger reselect must attach a living body");
            var mob = player.AttachedEntity!.Value;
            Assert.That(entMan.HasComponent<GhostComponent>(mob), Is.False);
            Assert.That(entMan.HasComponent<TutorialParticipantComponent>(mob), Is.True);
            Assert.That(mob, Is.Not.EqualTo(firstBody));
            Assert.That(entMan.Deleted(firstBody) || entMan.IsQueuedForDeletion(firstBody), Is.True,
                "Selecting a new role must tear down the previous tutorial body");

            TutorialSessionData? session = null;
            var ruleQuery = entMan.EntityQueryEnumerator<TutorialServerRuleComponent>();
            while (ruleQuery.MoveNext(out _, out var ruleComp))
            {
                if (ruleComp.Sessions.TryGetValue(player.UserId, out session))
                    break;
            }

            Assert.That(session, Is.Not.Null);
            Assert.That(session!.State, Is.EqualTo(TutorialSessionState.InTutorial));
            Assert.That(session.SelectedRoleId, Is.EqualTo("TutorialPassenger"));
            Assert.That(session.BodyUid, Is.EqualTo(mob));
            Assert.That(session.MentorUid, Is.Not.EqualTo(EntityUid.Invalid),
                "Passenger reselect must spawn a mentor coach");
            Assert.That(entMan.Deleted(session.MentorUid), Is.False);
            Assert.That(tutorial.IsPickerOpen(player), Is.False);

            var liveGhosts = 0;
            var ghostQuery = entMan.EntityQueryEnumerator<GhostComponent>();
            while (ghostQuery.MoveNext(out var ghostUid, out _))
            {
                if (!entMan.Deleted(ghostUid) && !entMan.IsQueuedForDeletion(ghostUid))
                    liveGhosts++;
            }

            Assert.That(liveGhosts, Is.EqualTo(0),
                "Starting a tutorial must WipeMind the observer so GhostOnShutdown cannot leave idle ghosts");
        });
    }

    [Test]
    public async Task TutorialPassenger_ClosedBottleUseDoesNotAdvanceDrinkGoal()
    {
        var pair = Pair;
        var server = pair.Server;
        var ticker = server.System<GameTicker>();
        var tutorial = server.System<TutorialServerRuleSystem>();
        var openable = server.System<OpenableSystem>();
        var entMan = server.EntMan;

        await server.WaitPost(() =>
        {
            ticker.SetGamePreset("TutorialServer");
            ticker.StartGameRule(TutorialRule, out _);
            ticker.StartRound();
        });
        await pair.RunTicksSync(5);

        await server.WaitPost(() =>
        {
            tutorial.TrySelectRole(pair.Player!, "TutorialPassenger", confirmedStub: false);
        });
        await pair.RunTicksSync(60);

        EntityUid bottle = default;
        EntityUid mob = default;

        await server.WaitAssertion(() =>
        {
            mob = pair.Player!.AttachedEntity!.Value;

            // Advance through welcome until drink-water (8th sub-goal, index 7).
            for (var i = 0; i < 7; i++)
                tutorial.AdvanceSubGoal(mob);

            Assert.That(entMan.TryGetComponent<TutorialParticipantComponent>(mob, out var part));
            Assert.That(part!.StepComplete, Is.EqualTo(TutorialStepComplete.UseInHand));

            TutorialSessionData? session = null;
            var ruleQuery = entMan.EntityQueryEnumerator<TutorialServerRuleComponent>();
            while (ruleQuery.MoveNext(out _, out var ruleComp))
            {
                if (ruleComp.Sessions.TryGetValue(pair.Player.UserId, out session))
                    break;
            }

            Assert.That(session, Is.Not.Null);
            Assert.That(session!.GoalIndex, Is.EqualTo(0));
            Assert.That(session.SubGoalIndex, Is.EqualTo(7));
            Assert.That(session.GuideAutoOpened, Is.False);

            var bottleQuery = entMan.EntityQueryEnumerator<TutorialSensorTargetComponent, MetaDataComponent>();
            while (bottleQuery.MoveNext(out var uid, out _, out var meta))
            {
                if (meta.EntityPrototype?.ID == "DrinkWaterBottleFull")
                {
                    bottle = uid;
                    break;
                }
            }

            Assert.That(bottle, Is.Not.EqualTo(EntityUid.Invalid));
            Assert.That(openable.IsClosed(bottle), Is.True, "Practice water bottle should start closed");

            var hands = server.System<SharedHandsSystem>();
            Assert.That(hands.TryPickupAnyHand(mob, bottle, checkActionBlocker: false, animate: false),
                Is.True,
                "Must hold the bottle before Use-in-hand");
        });

        var interaction = server.System<SharedInteractionSystem>();

        await server.WaitPost(() =>
        {
            // Full use path (same as player Z) so OpenableSystem opens the bottle before the sensor.
            Assert.That(interaction.UseInHandInteraction(mob, bottle), Is.True);
        });
        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            Assert.That(openable.IsClosed(bottle), Is.False, "First Z should open the bottle");

            TutorialSessionData? session = null;
            var ruleQuery = entMan.EntityQueryEnumerator<TutorialServerRuleComponent>();
            while (ruleQuery.MoveNext(out _, out var ruleComp))
            {
                if (ruleComp.Sessions.TryGetValue(pair.Player!.UserId, out session))
                    break;
            }

            Assert.That(session, Is.Not.Null);
            Assert.That(session!.GoalIndex, Is.EqualTo(0), "Opening a closed bottle must not finish welcome");
            Assert.That(session.SubGoalIndex, Is.EqualTo(7));
            Assert.That(session.GuideUid, Is.EqualTo(EntityUid.Invalid),
                "Passenger has no guide tablet that could steal focus");
            Assert.That(session.MentorUid, Is.Not.EqualTo(EntityUid.Invalid));
        });

        await server.WaitPost(() =>
        {
            Assert.That(interaction.UseInHandInteraction(mob, bottle), Is.True);
        });
        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            TutorialSessionData? session = null;
            var ruleQuery = entMan.EntityQueryEnumerator<TutorialServerRuleComponent>();
            while (ruleQuery.MoveNext(out _, out var ruleComp))
            {
                if (ruleComp.Sessions.TryGetValue(pair.Player!.UserId, out session))
                    break;
            }

            Assert.That(session, Is.Not.Null);
            Assert.That(session!.GoalIndex, Is.EqualTo(1), "Second Z (drink) should finish welcome");
            Assert.That(session.GuideUid, Is.EqualTo(EntityUid.Invalid));
            Assert.That(entMan.EntityExists(session.MentorUid), Is.True);
        });
    }

    [Test]
    public async Task TutorialMentor_StuckHintDoesNotAdvanceCurriculum()
    {
        var pair = Pair;
        var server = pair.Server;
        var ticker = server.System<GameTicker>();

        await server.WaitPost(() =>
        {
            ticker.SetGamePreset("TutorialServer");
            ticker.StartGameRule(TutorialRule, out _);
            ticker.StartRound();
        });
        await pair.RunTicksSync(5);

        await server.WaitPost(() =>
        {
            server.System<TutorialServerRuleSystem>()
                .TrySelectRole(pair.Player!, "TutorialPassenger", confirmedStub: false);
        });
        await pair.RunTicksSync(60);

        await server.WaitAssertion(() =>
        {
            var player = pair.Player!;
            var mob = player.AttachedEntity!.Value;
            var guideSys = server.System<TutorialGuideSystem>();
            var entMan = server.EntMan;

            TutorialSessionData? session = null;
            var ruleQuery = entMan.EntityQueryEnumerator<TutorialServerRuleComponent>();
            while (ruleQuery.MoveNext(out _, out var ruleComp))
            {
                if (ruleComp.Sessions.TryGetValue(player.UserId, out session))
                    break;
            }

            Assert.That(session, Is.Not.Null);
            Assert.That(session!.GuideUid, Is.EqualTo(EntityUid.Invalid));
            Assert.That(session.MentorUid, Is.Not.EqualTo(EntityUid.Invalid));

            Assert.That(entMan.TryGetComponent<TutorialParticipantComponent>(mob, out var part));
            Assert.That(part!.StepComplete, Is.EqualTo(TutorialStepComplete.ReachMarker));
            Assert.That(part.StuckHintText, Is.Not.Empty, "Passenger purple-X step should author a stuckHint");
            Assert.That(part.HintText, Is.Not.Empty);

            var goalBefore = part.GoalIndex;
            var subBefore = part.SubGoalIndex;
            Assert.That(guideSys.TryShowStuckHint(mob), Is.True);
            Assert.That(entMan.TryGetComponent(mob, out part));
            Assert.That(part!.GoalIndex, Is.EqualTo(goalBefore));
            Assert.That(part.SubGoalIndex, Is.EqualTo(subBefore));

            // Mentor click while on a sensor tip should not advance curriculum.
            var interact = new InteractHandEvent(mob, session.MentorUid);
            entMan.EventBus.RaiseLocalEvent(session.MentorUid, interact);
            Assert.That(entMan.TryGetComponent(mob, out part));
            Assert.That(part!.GoalIndex, Is.EqualTo(goalBefore));
            Assert.That(part.SubGoalIndex, Is.EqualTo(subBefore));
        });
    }

    [Test]
    public async Task TutorialSurgery_UiOpenedSensorAdvancesOpenUiStep()
    {
        var pair = Pair;
        var server = pair.Server;
        var ticker = server.System<GameTicker>();
        var ui = server.System<UserInterfaceSystem>();

        await server.WaitPost(() =>
        {
            ticker.SetGamePreset("TutorialServer");
            ticker.StartGameRule(TutorialRule, out _);
            ticker.StartRound();
        });
        await pair.RunTicksSync(5);

        await server.WaitPost(() =>
        {
            server.System<TutorialServerRuleSystem>()
                .TrySelectRole(pair.Player!, "TutorialSurgeryStarlight", confirmedStub: true); //Tutorial: stub greyed pending manual test
        });
        await pair.RunTicksSync(60);

        await server.WaitAssertion(() =>
        {
            var player = pair.Player!;
            var mob = player.AttachedEntity!.Value;
            var entMan = server.EntMan;
            var tutorial = server.System<TutorialServerRuleSystem>();

            TutorialServerRuleComponent? rule = null;
            TutorialSessionData? session = null;
            var ruleQuery = entMan.EntityQueryEnumerator<TutorialServerRuleComponent>();
            while (ruleQuery.MoveNext(out _, out rule))
            {
                if (rule.Sessions.TryGetValue(player.UserId, out session))
                    break;
            }

            Assert.That(session, Is.Not.Null);
            // Jump session to surgery / open-ui (goal index 2).
            session!.GoalIndex = 2;
            session.SubGoalIndex = 0;
            rule!.Sessions[player.UserId] = session;

            var part = entMan.GetComponent<TutorialParticipantComponent>(mob);
            part.GoalIndex = 2;
            part.SubGoalIndex = 0;
            part.StepComplete = TutorialStepComplete.StarlightSurgeryUiOpened;
            entMan.Dirty(mob, part);

            EntityUid? patient = null;
            var patients = entMan.EntityQueryEnumerator<TutorialStarlightSurgeryTargetComponent, TransformComponent>();
            while (patients.MoveNext(out var uid, out _, out var xform))
            {
                if (xform.MapUid == entMan.GetComponent<TransformComponent>(mob).MapUid)
                {
                    patient = uid;
                    break;
                }
            }

            Assert.That(patient, Is.Not.Null, "Starlight practice patient should exist on the private map");
            // OpenUi enforces interaction range — stand next to the patient first.
            var xforms = server.System<SharedTransformSystem>();
            xforms.SetCoordinates(mob, entMan.GetComponent<TransformComponent>(patient.Value).Coordinates);
            ui.OpenUi(patient.Value, TutorialStarlightSurgeryUiKey.Key, mob);
        });
        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            var player = pair.Player!;
            var mob = player.AttachedEntity!.Value;
            var part = server.EntMan.GetComponent<TutorialParticipantComponent>(mob);
            Assert.That(part.StepComplete, Is.Not.EqualTo(TutorialStepComplete.StarlightSurgeryUiOpened),
                "Opening surgery UI should advance past open-ui");
            Assert.That(part.GoalIndex, Is.EqualTo(2));
            Assert.That(part.SubGoalIndex, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task TutorialGuide_ClosedUiProgressStillAdvances()
    {
        var pair = Pair;
        var server = pair.Server;
        var ticker = server.System<GameTicker>();
        var ui = server.System<UserInterfaceSystem>();

        await server.WaitPost(() =>
        {
            ticker.SetGamePreset("TutorialServer");
            ticker.StartGameRule(TutorialRule, out _);
            ticker.StartRound();
        });
        await pair.RunTicksSync(5);

        await server.WaitPost(() =>
        {
            server.System<TutorialServerRuleSystem>()
                .TrySelectRole(pair.Player!, "TutorialCargoTechnician", confirmedStub: false);
        });
        await pair.RunTicksSync(60);

        await server.WaitAssertion(() =>
        {
            var player = pair.Player!;
            var mob = player.AttachedEntity!.Value;
            var entMan = server.EntMan;
            var guideSys = server.System<TutorialGuideSystem>();

            TutorialSessionData? session = null;
            var ruleQuery = entMan.EntityQueryEnumerator<TutorialServerRuleComponent>();
            while (ruleQuery.MoveNext(out _, out var ruleComp))
            {
                if (ruleComp.Sessions.TryGetValue(player.UserId, out session))
                    break;
            }

            Assert.That(session, Is.Not.Null);
            // Cargo starts with guide closed (AutoOpenGuide false); keep it closed for this assert.
            ui.CloseUi(session!.GuideUid, TutorialPromptUiKey.Key, mob);

            var guide = new Entity<TutorialGuideComponent>(
                session.GuideUid,
                entMan.GetComponent<TutorialGuideComponent>(session.GuideUid));

            Assert.That(entMan.TryGetComponent<TutorialParticipantComponent>(mob, out var part));
            Assert.That(part!.StepComplete, Is.EqualTo(TutorialStepComplete.Acknowledge));
            Assert.That(ui.IsUiOpen(session.GuideUid, TutorialPromptUiKey.Key, mob), Is.False);

            // Advance while closed should still move curriculum and resync when reopened.
            server.System<TutorialServerRuleSystem>().AdvanceSubGoal(mob);
            Assert.That(entMan.TryGetComponent(mob, out part));
            Assert.That(part!.GoalIndex, Is.EqualTo(1), "Intro Acknowledge advances into the order goal");
            Assert.That(part.SubGoalIndex, Is.EqualTo(0));

            ui.OpenUi(session.GuideUid, TutorialPromptUiKey.Key, mob);
            var state = guideSys.GetUiState(guide, mob);
            Assert.That(state.ViewGoalIndex, Is.EqualTo(part.GoalIndex));
            Assert.That(state.ProgressIndex, Is.EqualTo(part.SubGoalIndex));
        });
    }

    [Test]
    public async Task TutorialMentor_AcknowledgeAdvancesOnInteract()
    {
        var pair = Pair;
        var server = pair.Server;
        var ticker = server.System<GameTicker>();

        await server.WaitPost(() =>
        {
            ticker.SetGamePreset("TutorialServer");
            ticker.StartGameRule(TutorialRule, out _);
            ticker.StartRound();
        });
        await pair.RunTicksSync(5);

        await server.WaitPost(() =>
        {
            server.System<TutorialServerRuleSystem>()
                .TrySelectRole(pair.Player!, "TutorialChef", confirmedStub: false);
        });
        await pair.RunTicksSync(60);

        await server.WaitAssertion(() =>
        {
            var player = pair.Player!;
            var mob = player.AttachedEntity!.Value;
            var entMan = server.EntMan;

            TutorialSessionData? session = null;
            var ruleQuery = entMan.EntityQueryEnumerator<TutorialServerRuleComponent>();
            while (ruleQuery.MoveNext(out _, out var ruleComp))
            {
                if (ruleComp.Sessions.TryGetValue(player.UserId, out session))
                    break;
            }

            Assert.That(session, Is.Not.Null);
            Assert.That(session!.GuideUid, Is.EqualTo(EntityUid.Invalid));
            Assert.That(session.MentorUid, Is.Not.EqualTo(EntityUid.Invalid));

            Assert.That(entMan.TryGetComponent<TutorialParticipantComponent>(mob, out var part));
            Assert.That(part!.StepComplete, Is.EqualTo(TutorialStepComplete.Acknowledge));
            var subBefore = part.SubGoalIndex;

            var interact = new InteractHandEvent(mob, session.MentorUid);
            entMan.EventBus.RaiseLocalEvent(session.MentorUid, interact);

            Assert.That(entMan.TryGetComponent(mob, out part));
            Assert.That(part!.SubGoalIndex, Is.EqualTo(subBefore + 1),
                "Empty-hand click on mentor should Acknowledge-advance");
        });
    }

    [Test]
    public async Task TutorialMentor_ProgressDoesNotSendToast()
    {
        var pair = Pair;
        var server = pair.Server;
        var ticker = server.System<GameTicker>();

        await server.WaitPost(() =>
        {
            ticker.SetGamePreset("TutorialServer");
            ticker.StartGameRule(TutorialRule, out _);
            ticker.StartRound();
        });
        await pair.RunTicksSync(5);

        await server.WaitPost(() =>
        {
            server.System<TutorialServerRuleSystem>()
                .TrySelectRole(pair.Player!, "TutorialChef", confirmedStub: false);
        });
        await pair.RunTicksSync(60);

        await server.WaitAssertion(() =>
        {
            var player = pair.Player!;
            var mob = player.AttachedEntity!.Value;
            var entMan = server.EntMan;

            TutorialSessionData? session = null;
            TutorialServerRuleComponent? rule = null;
            var ruleQuery = entMan.EntityQueryEnumerator<TutorialServerRuleComponent>();
            while (ruleQuery.MoveNext(out _, out rule))
            {
                if (rule.Sessions.TryGetValue(player.UserId, out session))
                    break;
            }

            Assert.That(session, Is.Not.Null);
            Assert.That(session!.MentorUid, Is.Not.EqualTo(EntityUid.Invalid));
            Assert.That(rule, Is.Not.Null);

            // Clear any spawn-time bookkeeping so Advance is the only candidate to stamp the toast.
            session.LastProgressPopup = TimeSpan.Zero;
            rule!.Sessions[player.UserId] = session;

            server.System<TutorialServerRuleSystem>().AdvanceSubGoal(mob);

            Assert.That(rule.Sessions.TryGetValue(player.UserId, out session));
            Assert.That(session!.LastProgressPopup, Is.EqualTo(TimeSpan.Zero),
                "Mentor roles should not consume a closed-UI progress toast; the mentor speaks instead");
        });
    }

    [Test]
    public async Task TutorialRoomTemplate_StampsIdenticalCopiesWithGates()
    {
        var pair = Pair;
        var server = pair.Server;
        var ticker = server.System<GameTicker>();
        var maps = server.System<TutorialMapSystem>();
        var rooms = server.System<TutorialPracticeRoomSystem>();
        var templates = server.System<TutorialRoomTemplateSystem>();
        var proto = server.ProtoMan;

        await server.WaitPost(() =>
        {
            ticker.SetGamePreset("TutorialServer");
            ticker.StartGameRule(TutorialRule, out _);
            ticker.StartRound();
        });
        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            Assert.That(templates.TryStampFromRoomPrototype(
                "TutorialRoomKitchen",
                copyCount: 3,
                gateDoor: "Airlock",
                fillAtmosphere: true,
                out var mapUid,
                out var gridUid,
                out var spawn), Is.True);

            Assert.That(server.EntMan.TryGetComponent<TutorialRoomLayoutComponent>(gridUid, out var layout));
            Assert.That(layout!.ChamberCenters.Count, Is.EqualTo(3));
            Assert.That(layout.GateDoors.Count, Is.EqualTo(2));
            Assert.That(spawn.EntityId, Is.EqualTo(gridUid));

            rooms.UnlockGatesForGoal(gridUid, 1);
            Assert.That(server.EntMan.GetComponent<TutorialGateDoorComponent>(layout.GateDoors[0]).Unlocked, Is.True);
            Assert.That(server.EntMan.GetComponent<TutorialGateDoorComponent>(layout.GateDoors[1]).Unlocked, Is.False);

            maps.UnloadTutorialMap(mapUid);
        });

        await server.WaitAssertion(() =>
        {
            var doctor = proto.Index<TutorialRolePrototype>("TutorialMedicalDoctor");
            var cmo = proto.Index<TutorialRolePrototype>("TutorialChiefMedicalOfficer");
            Assert.That(doctor.RoomTemplate, Is.EqualTo(cmo.RoomTemplate));
            Assert.That(doctor.RoomTemplate, Is.EqualTo(new ProtoId<TutorialRoomTemplatePrototype>("TutorialSectionMedbay")));

            var chemist = proto.Index<TutorialRolePrototype>("TutorialChemist");
            Assert.That(chemist.RoomTemplate, Is.EqualTo(new ProtoId<TutorialRoomTemplatePrototype>("TutorialSectionChem")));
            Assert.That(chemist.RoomTemplate, Is.Not.EqualTo(doctor.RoomTemplate));

            var surgery = proto.Index<TutorialRolePrototype>("TutorialSurgeryCyberMed");
            Assert.That(surgery.RoomTemplate, Is.EqualTo(new ProtoId<TutorialRoomTemplatePrototype>("TutorialSectionSurgery")));

            Assert.That(maps.TryLoadTutorialMap(doctor, out var docMap, out var docGrid, out _), Is.True);
            Assert.That(server.EntMan.TryGetComponent<TutorialRoomLayoutComponent>(docGrid, out var docLayout));
            Assert.That(docLayout!.ChamberCenters.Count, Is.EqualTo(TutorialMapSystem.ResolveCopyCount(doctor)));
            Assert.That(docLayout.GateDoors.Count, Is.EqualTo(Math.Max(0, docLayout.ChamberCenters.Count - 1)));
            maps.UnloadTutorialMap(docMap);

            Assert.That(maps.TryLoadTutorialMap(chemist, out var chemMap, out _, out _), Is.True);
            maps.UnloadTutorialMap(chemMap);
        });
    }

    [Test]
    public async Task TutorialRoomTemplates_AllSectionPrototypesResolve()
    {
        var pair = Pair;
        var server = pair.Server;
        var ticker = server.System<GameTicker>();
        var templates = server.System<TutorialRoomTemplateSystem>();
        var maps = server.System<TutorialMapSystem>();
        var proto = server.ProtoMan;

        await server.WaitPost(() =>
        {
            ticker.SetGamePreset("TutorialServer");
            ticker.StartGameRule(TutorialRule, out _);
            ticker.StartRound();
        });
        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            foreach (var template in proto.EnumeratePrototypes<TutorialRoomTemplatePrototype>())
            {
                Assert.That(
                    templates.TryBuildFromTemplate(template.ID, copyCount: 2, out var mapUid, out var gridUid, out _),
                    Is.True,
                    $"Failed to build template {template.ID}");
                Assert.That(server.EntMan.TryGetComponent<TutorialRoomLayoutComponent>(gridUid, out var layout));
                Assert.That(layout!.ChamberCenters.Count, Is.EqualTo(2), template.ID);
                Assert.That(layout.GateDoors.Count, Is.EqualTo(1), template.ID);
                maps.UnloadTutorialMap(mapUid);
            }
        });
    }

    /// <summary>
    /// One-shot exporter: AABB-crops all tutorialSectionCrop recipes into Resources/Sections.
    /// Set env TUTORIAL_EXPORT_CROPS=1 to run (skipped in normal CI).
    /// </summary>
    [Test]
    public async Task ExportTutorialSectionCrops()
    {
        if (Environment.GetEnvironmentVariable("TUTORIAL_EXPORT_CROPS") != "1")
            Assert.Ignore("Set TUTORIAL_EXPORT_CROPS=1 to export section crops into Resources/");

        var pair = Pair;
        var server = pair.Server;
        var ticker = server.System<GameTicker>();
        var cropper = server.System<TutorialSectionCropSystem>();
        var templates = server.System<TutorialRoomTemplateSystem>();
        var maps = server.System<TutorialMapSystem>();
        var proto = server.ProtoMan;

        await server.WaitPost(() =>
        {
            ticker.SetGamePreset("TutorialServer");
            ticker.StartGameRule(TutorialRule, out _);
            ticker.StartRound();
        });
        await pair.RunTicksSync(5);

        var root = TutorialSectionCropSystem.FindResourcesDirectory();
        Assert.That(root, Is.Not.Null, "Resources/ directory not found");

        await server.WaitAssertion(() =>
        {
            var failures = new List<string>();
            foreach (var crop in proto.EnumeratePrototypes<TutorialSectionCropPrototype>())
            {
                if (!cropper.TryCrop(crop.ID, out var srcMap, out var srcGrid))
                {
                    failures.Add($"{crop.ID}: crop failed");
                    continue;
                }

                if (!templates.TryStampCopies(srcGrid, 2, crop.GateDoor, true, out var stampMap, out var stampGrid, out var spawn))
                {
                    failures.Add($"{crop.ID}: stamp failed");
                    server.EntMan.DeleteEntity(srcMap);
                    continue;
                }

                if (!server.EntMan.TryGetComponent<TutorialRoomLayoutComponent>(stampGrid, out var layout) ||
                    layout.ChamberCenters.Count != 2 ||
                    layout.GateDoors.Count != 1)
                {
                    failures.Add($"{crop.ID}: bad layout");
                    maps.UnloadTutorialMap(stampMap);
                    server.EntMan.DeleteEntity(srcMap);
                    continue;
                }

                // Spawn must sit on a non-empty floor tile.
                var gridComp = server.EntMan.GetComponent<MapGridComponent>(stampGrid);
                var mapSys = server.System<MapSystem>();
                var tile = mapSys.GetTileRef(stampGrid, gridComp, new Vector2i(
                    (int) MathF.Floor(spawn.Position.X),
                    (int) MathF.Floor(spawn.Position.Y)));
                if (tile.Tile.IsEmpty)
                    failures.Add($"{crop.ID}: spawn on empty tile");

                // Deny-list must be absent.
                var query = server.EntMan.AllEntityQueryEnumerator<TransformComponent>();
                while (query.MoveNext(out var uid, out var xform))
                {
                    if (xform.GridUid != stampGrid)
                        continue;
                    var id = server.EntMan.GetComponent<MetaDataComponent>(uid).EntityPrototype?.ID;
                    if (TutorialSectionCropSystem.IsDeniedPrototype(id))
                    {
                        failures.Add($"{crop.ID}: denied proto {id}");
                        break;
                    }
                }

                maps.UnloadTutorialMap(stampMap);

                if (!cropper.TryCropAndSave(crop.ID, root))
                    failures.Add($"{crop.ID}: save failed");

                server.EntMan.DeleteEntity(srcMap);
            }

            Assert.That(failures, Is.Empty, string.Join("\n", failures));
        });
    }

    [Test]
    public async Task TutorialSectionCrops_AllRecipesDefined()
    {
        var pair = Pair;
        var server = pair.Server;
        var proto = server.ProtoMan;

        await server.WaitAssertion(() =>
        {
            var crops = proto.EnumeratePrototypes<TutorialSectionCropPrototype>().Select(c => c.ID).ToHashSet();
            var expected = new[]
            {
                "CropMedbay", "CropChem", "CropScience", "CropCommand", "CropBar", "CropHydroponics",
                "CropArrivals", "CropEngineering", "CropKitchen", "CropCargoOffice",
                "CropSurgery", "CropAtmos", "CropJanitor", "CropTheatre",
            };
            foreach (var id in expected)
                Assert.That(crops.Contains(id), Is.True, $"Missing crop recipe {id}");
            Assert.That(crops.Count, Is.EqualTo(expected.Length));
        });
    }

    [Test]
    public async Task TutorialSectionCrops_WiredMapsExistAndStamp()
    {
        var pair = Pair;
        var server = pair.Server;
        var ticker = server.System<GameTicker>();
        var templates = server.System<TutorialRoomTemplateSystem>();
        var maps = server.System<TutorialMapSystem>();
        var proto = server.ProtoMan;
        var res = server.ResolveDependency<Robust.Shared.ContentPack.IResourceManager>();

        await server.WaitPost(() =>
        {
            ticker.SetGamePreset("TutorialServer");
            ticker.StartGameRule(TutorialRule, out _);
            ticker.StartRound();
        });
        await pair.RunTicksSync(5);

        var croppedTemplates = new[]
        {
            "TutorialSectionMedbay", "TutorialSectionChem", "TutorialSectionSurgery",
            "TutorialSectionAtmos", "TutorialSectionScience", "TutorialSectionCommand",
            "TutorialSectionBar", "TutorialSectionHydroponics", "TutorialSectionJanitor",
            "TutorialSectionTheatre", "TutorialSectionArrivals",
            "TutorialSectionEngineering", "TutorialSectionKitchen", "TutorialSectionCargoOffice",
        };

        await server.WaitAssertion(() =>
        {
            foreach (var id in croppedTemplates)
            {
                var template = proto.Index<TutorialRoomTemplatePrototype>(id);
                Assert.That(template.Map, Is.Not.Null, id);
                Assert.That(res.ContentFileExists(template.Map!.Value), Is.True, $"{id} map missing: {template.Map}");
                Assert.That(templates.TryBuildFromTemplate(id, 2, out var mapUid, out var gridUid, out var spawn),
                    Is.True, $"stamp {id}");
                Assert.That(server.EntMan.TryGetComponent<TutorialRoomLayoutComponent>(gridUid, out var layout));
                Assert.That(layout!.ChamberCenters.Count, Is.EqualTo(2), id);
                Assert.That(layout.GateDoors.Count, Is.EqualTo(1), id);

                var gridComp = server.EntMan.GetComponent<MapGridComponent>(gridUid);
                var tile = server.System<MapSystem>().GetTileRef(gridUid, gridComp, new Vector2i(
                    (int) MathF.Floor(spawn.Position.X),
                    (int) MathF.Floor(spawn.Position.Y)));
                Assert.That(tile.Tile.IsEmpty, Is.False, $"{id} spawn on empty tile");

                maps.UnloadTutorialMap(mapUid);
            }

            var doctor = proto.Index<TutorialRolePrototype>("TutorialMedicalDoctor");
            var cmo = proto.Index<TutorialRolePrototype>("TutorialChiefMedicalOfficer");
            var chemist = proto.Index<TutorialRolePrototype>("TutorialChemist");
            var surgery = proto.Index<TutorialRolePrototype>("TutorialSurgeryCyberMed");
            Assert.That(doctor.RoomTemplate, Is.EqualTo(cmo.RoomTemplate));
            Assert.That(chemist.RoomTemplate, Is.Not.EqualTo(doctor.RoomTemplate));
            Assert.That(surgery.RoomTemplate, Is.Not.EqualTo(doctor.RoomTemplate));
        });
    }

    [Test]
    public async Task TutorialSectionStamp_ReplacesCropDoorsWithVaultsAndKeepsOneGate()
    {
        var pair = Pair;
        var server = pair.Server;
        var ticker = server.System<GameTicker>();
        var templates = server.System<TutorialRoomTemplateSystem>();
        var maps = server.System<TutorialMapSystem>();
        var rooms = server.System<TutorialPracticeRoomSystem>();

        await server.WaitPost(() =>
        {
            ticker.SetGamePreset("TutorialServer");
            ticker.StartGameRule(TutorialRule, out _);
            ticker.StartRound();
        });
        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            // Bar crop has many interior airlocks; after stamp only the gate should be usable.
            Assert.That(templates.TryBuildFromTemplate("TutorialSectionBar", 2, out var mapUid, out var gridUid, out _),
                Is.True);

            Assert.That(server.EntMan.TryGetComponent<TutorialRoomLayoutComponent>(gridUid, out var layout));
            Assert.That(layout!.GateDoors.Count, Is.EqualTo(1));

            var gateSet = layout.GateDoors.ToHashSet();
            var vaultCount = 0;
            var doorQuery = server.EntMan.AllEntityQueryEnumerator<Content.Shared.Doors.Components.DoorComponent, TransformComponent>();
            while (doorQuery.MoveNext(out var uid, out _, out var xform))
            {
                if (xform.GridUid != gridUid)
                    continue;

                if (gateSet.Contains(uid))
                    continue;

                var id = server.EntMan.GetComponent<MetaDataComponent>(uid).EntityPrototype?.ID;
                if (id == "TutorialVaultDoor")
                    vaultCount++;
                else
                    Assert.Fail($"Leftover non-gate door {id}; critical-path doors are removed, others become vaults");
            }

            Assert.That(vaultCount, Is.GreaterThan(0), "Bar crop should produce sealed vault doors");

            rooms.UnlockGatesForGoal(gridUid, 1);
            Assert.That(server.EntMan.GetComponent<TutorialGateDoorComponent>(layout.GateDoors[0]).Unlocked, Is.True);
            Assert.That(server.EntMan.HasComponent<TutorialGateDoorComponent>(layout.GateDoors[0]));

            maps.UnloadTutorialMap(mapUid);
        });
    }

    [Test]
    public async Task TutorialScienceStamp_DoesNotCarveWindowsForGatePath()
    {
        var pair = Pair;
        var server = pair.Server;
        var ticker = server.System<GameTicker>();
        var templates = server.System<TutorialRoomTemplateSystem>();
        var maps = server.System<TutorialMapSystem>();
        var proto = server.ProtoMan;

        await server.WaitPost(() =>
        {
            ticker.SetGamePreset("TutorialServer");
            ticker.StartGameRule(TutorialRule, out _);
            ticker.StartRound();
        });
        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            var scienceTemplate = proto.Index<TutorialRoomTemplatePrototype>("TutorialSectionScience");
            Assert.That(scienceTemplate.StampDirection, Is.EqualTo(TutorialRoomDoorSide.South),
                "Science must stamp south — east mid-edge goes through the RD office");

            Assert.That(templates.TryBuildFromTemplate("TutorialSectionScience", 2, out var mapUid, out var gridUid, out _),
                Is.True);
            Assert.That(server.EntMan.TryGetComponent<TutorialRoomLayoutComponent>(gridUid, out var layout));
            Assert.That(layout!.ChamberCenters.Count, Is.EqualTo(2));
            Assert.That(layout.GateDoors.Count, Is.EqualTo(1));

            // Next chamber is south of chamber 0 (lower Y), not east.
            Assert.That(layout.ChamberCenters[1].Y, Is.LessThan(layout.ChamberCenters[0].Y));
            Assert.That(layout.ChamberCenters[1].X, Is.EqualTo(layout.ChamberCenters[0].X).Within(0.1f));

            var gateXform = server.EntMan.GetComponent<TransformComponent>(layout.GateDoors[0]);
            Assert.That(gateXform.LocalPosition.Y, Is.LessThan(layout.ChamberCenters[0].Y),
                "Gate must sit on the south divider between science chambers");

            // Perimeter windows from the crop must remain — pathing must not Bresenham-carve glass.
            var windowCount = 0;
            var rdVaults = 0;
            var query = server.EntMan.AllEntityQueryEnumerator<TransformComponent>();
            while (query.MoveNext(out var uid, out var xform))
            {
                if (xform.GridUid != gridUid)
                    continue;
                var id = server.EntMan.GetComponent<MetaDataComponent>(uid).EntityPrototype?.ID;
                if (id != null && id.Contains("Window", StringComparison.Ordinal))
                    windowCount++;
            }

            // RD office airlocks must not remain as usable crop doors on the stamp path.
            var doorQuery = server.EntMan.AllEntityQueryEnumerator<Content.Shared.Doors.Components.DoorComponent, TransformComponent>();
            while (doorQuery.MoveNext(out var uid, out _, out var xform))
            {
                if (xform.GridUid != gridUid)
                    continue;
                var id = server.EntMan.GetComponent<MetaDataComponent>(uid).EntityPrototype?.ID;
                Assert.That(id, Is.Not.EqualTo("AirlockResearchDirectorGlassLocked"),
                    "RD airlock must be vaulted or removed — it is not the tutorial path");
                if (id == "TutorialVaultDoor")
                    rdVaults++;
            }

            Assert.That(windowCount, Is.GreaterThan(4),
                "Science stamp must keep crop windows; gate path uses door openings only");
            Assert.That(rdVaults, Is.GreaterThan(0),
                "Off-path science doors (including RD) should become sealed vaults");

            maps.UnloadTutorialMap(mapUid);
        });
    }

    [Test]
    public async Task TutorialCommandStamp_StacksWestAwayFromCaptainOffice()
    {
        await AssertSectionStampAxisAndForbiddenDoors(
            "TutorialSectionCommand",
            TutorialRoomDoorSide.West,
            forbiddenDoorProtos: new[] { "AirlockCaptainLocked", "AirlockCaptainGlassLocked" });
    }

    [Test]
    public async Task TutorialMedbayStamp_StacksSouthAwayFromCmoOffice()
    {
        await AssertSectionStampAxisAndForbiddenDoors(
            "TutorialSectionMedbay",
            TutorialRoomDoorSide.South,
            forbiddenDoorProtos: new[] { "AirlockChiefMedicalOfficerGlassLocked" });
    }

    [Test]
    public async Task TutorialChemStamp_StacksSouthAwayFromMaintAirlock()
    {
        await AssertSectionStampAxisAndForbiddenDoors(
            "TutorialSectionChem",
            TutorialRoomDoorSide.South,
            forbiddenDoorProtos: Array.Empty<string>(),
            requireVaults: true);
    }

    [Test]
    public async Task TutorialPracticeSpawns_ScatterStackedFloorItems()
    {
        var pair = Pair;
        var server = pair.Server;
        var ticker = server.System<GameTicker>();
        var tutorial = server.System<TutorialServerRuleSystem>();
        var entMan = server.EntMan;

        await server.WaitPost(() =>
        {
            ticker.SetGamePreset("TutorialServer");
            ticker.StartGameRule(TutorialRule, out _);
            ticker.StartRound();
        });
        await pair.RunTicksSync(5);

        await server.WaitPost(() =>
        {
            tutorial.TrySelectRole(pair.Player!, "TutorialChemist", confirmedStub: false);
        });
        await pair.RunTicksSync(60);

        await server.WaitAssertion(() =>
        {
            var mob = pair.Player!.AttachedEntity!.Value;
            var mobXform = entMan.GetComponent<TransformComponent>(mob);
            var mapUid = mobXform.MapUid;
            var gridUid = mobXform.GridUid;
            var containers = server.System<SharedContainerSystem>();
            var factory = server.ResolveDependency<IComponentFactory>();
            Assert.That(server.ProtoMan.Index<EntityPrototype>("Beaker").TryGetComponent<ItemComponent>(out _, factory),
                Is.True,
                "Beaker prototype must expose Item for practice-pile scatter");

            var beakers = new List<(EntityUid Uid, Vector2 Local, EntityUid Parent)>();
            var query = entMan.AllEntityQueryEnumerator<MetaDataComponent, TransformComponent>();
            while (query.MoveNext(out var uid, out var meta, out var xform))
            {
                if (xform.MapUid != mapUid)
                    continue;
                if (meta.EntityPrototype?.ID != "Beaker")
                    continue;
                // Floor practice spawns only — skip loadout / storage (same transform as parent).
                if (containers.IsEntityInContainer(uid))
                    continue;
                // Prefer grid-parented loose items (tables/mobs reparent without always using containers).
                if (gridUid != null && xform.ParentUid != gridUid)
                    continue;
                beakers.Add((uid, xform.LocalPosition, xform.ParentUid));
            }

            Assert.That(beakers.Count, Is.GreaterThanOrEqualTo(2),
                $"Chemist practice kit includes two floor beakers (found {beakers.Count})");

            for (var i = 0; i < beakers.Count; i++)
            for (var j = i + 1; j < beakers.Count; j++)
            {
                var delta = beakers[i].Local - beakers[j].Local;
                Assert.That(delta.Length(), Is.GreaterThan(0.05f),
                    $"Practice beakers must be visually offset, not stacked exactly " +
                    $"(local {beakers[i].Local} vs {beakers[j].Local}, parents {beakers[i].Parent}/{beakers[j].Parent})");
            }
        });
    }

    [Test]
    public async Task TutorialChemist_TagsCropMachinesAndGlasswareSpawnsAreWalkable()
    {
        var pair = Pair;
        var server = pair.Server;
        var ticker = server.System<GameTicker>();
        var tutorial = server.System<TutorialServerRuleSystem>();
        var rooms = server.System<TutorialPracticeRoomSystem>();
        var tags = server.System<TagSystem>();
        var entMan = server.EntMan;
        var proto = server.ProtoMan;

        await server.WaitPost(() =>
        {
            ticker.SetGamePreset("TutorialServer");
            ticker.StartGameRule(TutorialRule, out _);
            ticker.StartRound();
        });
        await pair.RunTicksSync(5);

        await server.WaitPost(() =>
        {
            tutorial.TrySelectRole(pair.Player!, "TutorialChemist", confirmedStub: false);
        });
        await pair.RunTicksSync(60);

        await server.WaitAssertion(() =>
        {
            var mob = pair.Player!.AttachedEntity!.Value;
            var gridUid = entMan.GetComponent<TransformComponent>(mob).GridUid;
            Assert.That(gridUid, Is.Not.Null);

            var taggedDispenser = false;
            var taggedMaster = false;
            var taggedGrinder = false;
            var taggedHotplate = false;
            var query = entMan.AllEntityQueryEnumerator<MetaDataComponent, TransformComponent>();
            while (query.MoveNext(out var uid, out var meta, out var xform))
            {
                if (xform.GridUid != gridUid)
                    continue;

                if (meta.EntityPrototype?.ID == "ChemDispenser" &&
                    tags.HasTag(uid, new ProtoId<TagPrototype>("TutorialChemDispenser")))
                    taggedDispenser = true;
                if (meta.EntityPrototype?.ID == "ChemMaster" &&
                    tags.HasTag(uid, new ProtoId<TagPrototype>("TutorialChemMaster")))
                    taggedMaster = true;
                if (meta.EntityPrototype?.ID == "KitchenReagentGrinder" &&
                    tags.HasTag(uid, new ProtoId<TagPrototype>("TutorialGrinder")))
                    taggedGrinder = true;
                if (meta.EntityPrototype?.ID == "ChemistryHotplate" &&
                    tags.HasTag(uid, new ProtoId<TagPrototype>("TutorialHotplate")))
                    taggedHotplate = true;
            }

            Assert.That(taggedDispenser, Is.True, "Crop ChemDispenser must be tagged for the open-UI step");
            Assert.That(taggedMaster, Is.True, "Crop ChemMaster must be tagged");
            Assert.That(taggedGrinder, Is.True, "Crop reagent grinder must be tagged");
            Assert.That(taggedHotplate, Is.True, "Crop hotplate must be tagged/powered for table salt");

            EntityUid? dispenserUid = null;
            EntityUid? sinkUid = null;
            var fixtureQuery = entMan.AllEntityQueryEnumerator<MetaDataComponent, TransformComponent>();
            while (fixtureQuery.MoveNext(out var uid, out var meta, out var xform))
            {
                if (xform.GridUid != gridUid)
                    continue;
                if (meta.EntityPrototype?.ID == "ChemDispenser")
                    dispenserUid = uid;
                if (meta.EntityPrototype?.ID == "SinkStemlessWater")
                    sinkUid = uid;
            }

            Assert.That(dispenserUid, Is.Not.Null, "Chemist crop must include a ChemDispenser");
            Assert.That(sinkUid, Is.Not.Null, "Chemist crop must include a wall-mounted sink for water");
            var dispenserPos = entMan.GetComponent<TransformComponent>(dispenserUid!.Value).LocalPosition;
            var sinkXform = entMan.GetComponent<TransformComponent>(sinkUid!.Value);
            var sinkPos = sinkXform.LocalPosition;
            Assert.That((sinkPos - dispenserPos).Length(), Is.LessThanOrEqualTo(1.1f),
                "Sink must sit on the wall tile adjacent to the ChemDispenser");
            // East-wall mounts in this crop face west (same as the powered light on that wall).
            Assert.That(sinkXform.LocalRotation.Theta, Is.EqualTo(-Math.PI / 2).Within(0.01),
                "Sink rotation must match the east wall behind the dispenser");

            var chemist = proto.Index<TutorialRolePrototype>("TutorialChemist");
            Assert.That(entMan.TryGetComponent<TutorialRoomLayoutComponent>(gridUid!.Value, out var layout));
            Assert.That(layout!.ChamberCenters.Count, Is.GreaterThan(0));

            foreach (var spawn in chemist.PracticeSpawns)
            {
                var coords = rooms.GetChamberCoords(gridUid.Value, spawn.Room, spawn.Offset);
                var tile = new Vector2i((int) MathF.Floor(coords.Position.X), (int) MathF.Floor(coords.Position.Y));
                var wallOnTile = false;
                var wallQuery = entMan.AllEntityQueryEnumerator<TransformComponent, MetaDataComponent>();
                while (wallQuery.MoveNext(out _, out var xform, out var meta))
                {
                    if (xform.GridUid != gridUid)
                        continue;
                    if (meta.EntityPrototype?.ID is not ("WallReinforced" or "WallSolid" or "WallReinforcedDiagonal"))
                        continue;
                    var wallTile = new Vector2i(
                        (int) MathF.Floor(xform.LocalPosition.X),
                        (int) MathF.Floor(xform.LocalPosition.Y));
                    if (wallTile == tile)
                    {
                        wallOnTile = true;
                        break;
                    }
                }

                Assert.That(wallOnTile, Is.False,
                    $"Practice spawn {spawn.Id} at offset {spawn.Offset} lands in a wall tile {tile}");
            }
        });
    }

    [Test]
    public async Task TutorialChemist_OpeningDispenserAdvancesGoal()
    {
        var pair = Pair;
        var server = pair.Server;
        var ticker = server.System<GameTicker>();
        var tutorial = server.System<TutorialServerRuleSystem>();
        var tags = server.System<TagSystem>();
        var entMan = server.EntMan;

        await server.WaitPost(() =>
        {
            ticker.SetGamePreset("TutorialServer");
            ticker.StartGameRule(TutorialRule, out _);
            ticker.StartRound();
        });
        await pair.RunTicksSync(5);

        await server.WaitPost(() =>
        {
            tutorial.TrySelectRole(pair.Player!, "TutorialChemist", confirmedStub: false);
        });
        await pair.RunTicksSync(60);

        EntityUid dispenser = default;
        EntityUid mob = default;

        await server.WaitAssertion(() =>
        {
            mob = pair.Player!.AttachedEntity!.Value;

            // welcome → glassware hold-beaker → hold-large → mix.dispenser
            tutorial.AdvanceSubGoal(mob);
            tutorial.AdvanceSubGoal(mob);
            tutorial.AdvanceSubGoal(mob);

            Assert.That(entMan.TryGetComponent<TutorialParticipantComponent>(mob, out var part));
            Assert.That(part!.StepComplete, Is.EqualTo(TutorialStepComplete.InteractTargetTag));
            Assert.That(part.GoalIndex, Is.EqualTo(2));
            Assert.That(part.SubGoalIndex, Is.EqualTo(0));

            var mapUid = entMan.GetComponent<TransformComponent>(mob).MapUid;
            var query = entMan.AllEntityQueryEnumerator<MetaDataComponent, TransformComponent>();
            while (query.MoveNext(out var uid, out var meta, out var xform))
            {
                if (xform.MapUid != mapUid)
                    continue;
                if (meta.EntityPrototype?.ID != "ChemDispenser")
                    continue;
                if (!tags.HasTag(uid, new ProtoId<TagPrototype>("TutorialChemDispenser")))
                    continue;
                dispenser = uid;
                break;
            }

            Assert.That(dispenser, Is.Not.EqualTo(EntityUid.Invalid));
        });

        await server.WaitPost(() =>
        {
            // Raise the same event ActivatableUISystem fires after a successful open.
            // Full InteractionActivate hits Bound-UI range asserts in pooled tests.
            var ev = new AfterActivatableUIOpenEvent(mob);
            entMan.EventBus.RaiseLocalEvent(dispenser, ev);
        });
        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.TryGetComponent<TutorialParticipantComponent>(mob, out var part));
            Assert.That(part!.GoalIndex, Is.EqualTo(2));
            Assert.That(part.SubGoalIndex, Is.EqualTo(1),
                "Opening the chem dispenser Bound UI must advance past the dispenser step");
            Assert.That(part.StepComplete, Is.EqualTo(TutorialStepComplete.SolutionContains));
        });
    }

    [Test]
    public async Task TutorialEngineeringStamp_KeepsEastAndVaultsCeOffice()
    {
        await AssertSectionStampAxisAndForbiddenDoors(
            "TutorialSectionEngineering",
            TutorialRoomDoorSide.East,
            forbiddenDoorProtos: new[] { "AirlockChiefEngineerLocked" },
            requireVaults: true);
    }

    [Test]
    public async Task TutorialSecurityStamp_KeepsEastAndVaultsArmoryHighSec()
    {
        await AssertSectionStampAxisAndForbiddenDoors(
            "TutorialSectionSecurity",
            TutorialRoomDoorSide.East,
            forbiddenDoorProtos: new[] { "AirlockHighSecLocked", "WindoorSecureArmoryLocked" },
            requireVaults: true,
            forbiddenDoorSubstring: "HighSec");
    }

    [Test]
    public async Task TutorialBrigStamp_KeepsEastAndVaultsArmoryHighSec()
    {
        await AssertSectionStampAxisAndForbiddenDoors(
            "TutorialSectionBrig",
            TutorialRoomDoorSide.East,
            forbiddenDoorProtos: new[] { "WindoorSecureArmoryLocked" },
            requireVaults: true,
            forbiddenDoorSubstring: "HighSec");
    }

    [Test]
    public async Task TutorialAtmosStamp_StacksSouthThroughCenterCorridor()
    {
        await AssertSectionStampAxisAndForbiddenDoors(
            "TutorialSectionAtmos",
            TutorialRoomDoorSide.South,
            forbiddenDoorProtos: Array.Empty<string>(),
            requireVaults: true,
            requireWalkableCenterToNextChamber: true);
    }

    [Test]
    public async Task TutorialHydroponicsStamp_StacksWestAwayFromFreezer()
    {
        await AssertSectionStampAxisAndForbiddenDoors(
            "TutorialSectionHydroponics",
            TutorialRoomDoorSide.West,
            forbiddenDoorProtos: new[] { "AirlockFreezer" },
            requireVaults: true);
    }

    /// <summary>
    /// Shared stamp-axis assertions for section crops that redirect gates away from head offices.
    /// </summary>
    private async Task AssertSectionStampAxisAndForbiddenDoors(
        string templateId,
        TutorialRoomDoorSide expectedDirection,
        string[] forbiddenDoorProtos,
        bool requireVaults = false,
        string? forbiddenDoorSubstring = null,
        bool requireWalkableCenterToNextChamber = false)
    {
        var pair = Pair;
        var server = pair.Server;
        var ticker = server.System<GameTicker>();
        var templates = server.System<TutorialRoomTemplateSystem>();
        var maps = server.System<TutorialMapSystem>();
        var rooms = server.System<TutorialPracticeRoomSystem>();
        var turf = server.System<TurfSystem>();
        var mapSys = server.System<MapSystem>();
        var proto = server.ProtoMan;

        await server.WaitPost(() =>
        {
            ticker.SetGamePreset("TutorialServer");
            ticker.StartGameRule(TutorialRule, out _);
            ticker.StartRound();
        });
        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            var template = proto.Index<TutorialRoomTemplatePrototype>(templateId);
            Assert.That(template.StampDirection, Is.EqualTo(expectedDirection), templateId);

            Assert.That(templates.TryBuildFromTemplate(templateId, 2, out var mapUid, out var gridUid, out _),
                Is.True, templateId);
            Assert.That(server.EntMan.TryGetComponent<TutorialRoomLayoutComponent>(gridUid, out var layout));
            Assert.That(layout!.ChamberCenters.Count, Is.EqualTo(2), templateId);
            Assert.That(layout.GateDoors.Count, Is.EqualTo(1), templateId);

            var c0 = layout.ChamberCenters[0];
            var c1 = layout.ChamberCenters[1];
            switch (expectedDirection)
            {
                case TutorialRoomDoorSide.South:
                    Assert.That(c1.Y, Is.LessThan(c0.Y), $"{templateId}: next chamber must be south");
                    Assert.That(c1.X, Is.EqualTo(c0.X).Within(0.1f), templateId);
                    break;
                case TutorialRoomDoorSide.North:
                    Assert.That(c1.Y, Is.GreaterThan(c0.Y), $"{templateId}: next chamber must be north");
                    Assert.That(c1.X, Is.EqualTo(c0.X).Within(0.1f), templateId);
                    break;
                case TutorialRoomDoorSide.West:
                    Assert.That(c1.X, Is.LessThan(c0.X), $"{templateId}: next chamber must be west");
                    Assert.That(c1.Y, Is.EqualTo(c0.Y).Within(0.1f), templateId);
                    break;
                default:
                    Assert.That(c1.X, Is.GreaterThan(c0.X), $"{templateId}: next chamber must be east");
                    Assert.That(c1.Y, Is.EqualTo(c0.Y).Within(0.1f), templateId);
                    break;
            }

            var forbidden = forbiddenDoorProtos.ToHashSet(StringComparer.Ordinal);
            var vaultCount = 0;
            var doorQuery = server.EntMan.AllEntityQueryEnumerator<Content.Shared.Doors.Components.DoorComponent, TransformComponent>();
            while (doorQuery.MoveNext(out var uid, out _, out var xform))
            {
                if (xform.GridUid != gridUid)
                    continue;

                var id = server.EntMan.GetComponent<MetaDataComponent>(uid).EntityPrototype?.ID;
                if (id == null)
                    continue;

                Assert.That(forbidden.Contains(id), Is.False,
                    $"{templateId}: forbidden door {id} must be vaulted or removed");
                if (forbiddenDoorSubstring != null &&
                    id.Contains(forbiddenDoorSubstring, StringComparison.Ordinal) &&
                    id != "TutorialVaultDoor")
                {
                    Assert.Fail($"{templateId}: door {id} matches forbidden substring '{forbiddenDoorSubstring}'");
                }

                if (id == "TutorialVaultDoor")
                    vaultCount++;
            }

            if (requireVaults)
            {
                Assert.That(vaultCount, Is.GreaterThan(0),
                    $"{templateId}: off-path crop doors should become sealed vaults");
            }

            if (requireWalkableCenterToNextChamber)
            {
                rooms.UnlockGatesForGoal(gridUid, 1);
                var grid = server.EntMan.GetComponent<MapGridComponent>(gridUid);
                var start = new Vector2i((int) MathF.Floor(c0.X), (int) MathF.Floor(c0.Y));
                var goal = new Vector2i((int) MathF.Floor(c1.X), (int) MathF.Floor(c1.Y));
                Assert.That(
                    TryWalkMobPath(server.EntMan, mapSys, turf, gridUid, grid, start, goal),
                    Is.True,
                    $"{templateId}: chamber 0 center must walk to chamber 1 after gate unlock (no vault maze)");
            }

            maps.UnloadTutorialMap(mapUid);
        });
    }

    /// <summary>
    /// BFS on grid tiles: vault doors block; unlocked tutorial gates and open floor are walkable.
    /// </summary>
    private static bool TryWalkMobPath(
        IEntityManager entMan,
        MapSystem mapSys,
        TurfSystem turf,
        EntityUid gridUid,
        MapGridComponent grid,
        Vector2i start,
        Vector2i goal)
    {
        if (mapSys.GetTileRef(gridUid, grid, start).Tile.IsEmpty ||
            mapSys.GetTileRef(gridUid, grid, goal).Tile.IsEmpty)
            return false;

        bool Walkable(Vector2i tile)
        {
            if (mapSys.GetTileRef(gridUid, grid, tile).Tile.IsEmpty)
                return false;

            foreach (var ent in mapSys.GetAnchoredEntities(gridUid, grid, tile))
            {
                if (entMan.HasComponent<TutorialGateDoorComponent>(ent))
                    return true;

                var id = entMan.GetComponent<MetaDataComponent>(ent).EntityPrototype?.ID;
                if (id == "TutorialVaultDoor")
                    return false;

                if (entMan.HasComponent<Content.Shared.Doors.Components.DoorComponent>(ent))
                    return false;
            }

            return !turf.IsTileBlocked(gridUid, tile, CollisionGroup.MobMask);
        }

        if (!Walkable(start) || !Walkable(goal))
            return false;

        var cameFrom = new HashSet<Vector2i> { start };
        var queue = new Queue<Vector2i>();
        queue.Enqueue(start);
        var dirs = new[]
        {
            new Vector2i(1, 0),
            new Vector2i(-1, 0),
            new Vector2i(0, 1),
            new Vector2i(0, -1),
        };

        while (queue.Count > 0)
        {
            var cur = queue.Dequeue();
            if (cur == goal)
                return true;

            foreach (var dir in dirs)
            {
                var next = cur + dir;
                if (!cameFrom.Add(next))
                    continue;
                if (!Walkable(next))
                    continue;
                queue.Enqueue(next);
            }
        }

        return false;
    }

    [Test]
    public async Task TutorialBartender_SingleChamberStockWithoutChamberPad()
    {
        var pair = Pair;
        var server = pair.Server;
        var ticker = server.System<GameTicker>();
        var tutorial = server.System<TutorialServerRuleSystem>();
        var proto = server.ProtoMan;

        await server.WaitPost(() =>
        {
            ticker.SetGamePreset("TutorialServer");
            ticker.StartGameRule(TutorialRule, out _);
            ticker.StartRound();
        });
        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            var bartender = proto.Index<TutorialRolePrototype>("TutorialBartender");
            Assert.That(TutorialMapSystem.ResolveCopyCount(bartender), Is.EqualTo(1));
            Assert.That(bartender.PracticeSpawns.All(p => p.Room == 0));
        });

        await server.WaitPost(() =>
        {
            tutorial.TrySelectRole(pair.Player!, "TutorialBartender", confirmedStub: false);
        });
        await pair.RunTicksSync(10);

        await server.WaitAssertion(() =>
        {
            Assert.That(pair.Player!.AttachedEntity, Is.Not.Null);
            var mob = pair.Player.AttachedEntity!.Value;
            Assert.That(server.EntMan.TryGetComponent<TutorialParticipantComponent>(mob, out var part));
            Assert.That(part!.GoalIndex, Is.EqualTo(0));

            // Spawn must not sit on a vaulted crop airlock.
            var spawnTile = new Vector2i(
                (int) MathF.Floor(server.EntMan.GetComponent<TransformComponent>(mob).LocalPosition.X),
                (int) MathF.Floor(server.EntMan.GetComponent<TransformComponent>(mob).LocalPosition.Y));
            var grid = server.EntMan.GetComponent<TransformComponent>(mob).GridUid!.Value;
            foreach (var ent in server.System<MapSystem>()
                         .GetAnchoredEntities(grid, server.EntMan.GetComponent<MapGridComponent>(grid), spawnTile))
            {
                var id = server.EntMan.GetComponent<MetaDataComponent>(ent).EntityPrototype?.ID;
                Assert.That(id, Is.Not.EqualTo("TutorialVaultDoor"), "Bartender spawn clipped into vault door");
            }

            Assert.That(part.StepComplete, Is.EqualTo(TutorialStepComplete.Acknowledge));
            tutorial.AdvanceSubGoal(mob);
        });
        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            var mob = pair.Player!.AttachedEntity!.Value;
            Assert.That(server.EntMan.TryGetComponent<TutorialParticipantComponent>(mob, out var part));
            Assert.That(part!.GoalIndex, Is.EqualTo(1), "Stock goal should start after welcome");
            // Single-chamber bar: no chamber-entry pad — stock starts immediately.
            Assert.That(part.StepComplete, Is.EqualTo(TutorialStepComplete.InteractTargetTag));
            Assert.That(tutorial.TryGetCurrentSubGoal(mob, part, out var sub));
            Assert.That(sub.Tag, Is.EqualTo("TutorialVending"));
        });
    }

    [Test]
    public async Task TutorialSalternDeptSections_HaveNoSalvageHostiles()
    {
        var pair = Pair;
        var server = pair.Server;
        var ticker = server.System<GameTicker>();
        var maps = server.System<TutorialMapSystem>();
        var proto = server.ProtoMan;

        await server.WaitPost(() =>
        {
            ticker.SetGamePreset("TutorialServer");
            ticker.StartGameRule(TutorialRule, out _);
            ticker.StartRound();
        });
        await pair.RunTicksSync(5);

        var roles = new[]
        {
            "TutorialStationEngineer",
            "TutorialTechnicalAssistant",
            "TutorialServiceWorker",
            "TutorialQuartermaster",
        };

        await server.WaitAssertion(() =>
        {
            foreach (var roleId in roles)
            {
                var role = proto.Index<TutorialRolePrototype>(roleId);
                Assert.That(maps.TryLoadTutorialMap(role, out var mapUid, out var gridUid, out var spawn),
                    Is.True, roleId);

                var latticeId = ((ContentTileDefinition) server.ResolveDependency<ITileDefinitionManager>()["Lattice"]).TileId;
                var spawnTile = server.System<MapSystem>().GetTileRef(gridUid,
                    server.EntMan.GetComponent<MapGridComponent>(gridUid),
                    new Vector2i((int) MathF.Floor(spawn.Position.X), (int) MathF.Floor(spawn.Position.Y)));
                Assert.That(spawnTile.Tile.IsEmpty, Is.False, $"{roleId}: empty spawn tile");
                Assert.That(spawnTile.Tile.TypeId, Is.Not.EqualTo(latticeId), $"{roleId}: spawn on lattice");

                var query = server.EntMan.AllEntityQueryEnumerator<TransformComponent>();
                while (query.MoveNext(out var uid, out var xform))
                {
                    if (xform.GridUid != gridUid)
                        continue;

                    var id = server.EntMan.GetComponent<MetaDataComponent>(uid).EntityPrototype?.ID;
                    if (id == null)
                        continue;

                    Assert.That(id.StartsWith("SalvageMobSpawner", StringComparison.Ordinal), Is.False,
                        $"{roleId}: salvage mob spawner {id}");
                    Assert.That(id, Is.Not.EqualTo("SpaceTickSpawner"), $"{roleId}: tick spawner");
                    Assert.That(id is "SpawnMobKangarooSalvage" or "SpawnMobSpiderSalvage", Is.False,
                        $"{roleId}: salvage mob {id}");
                }

                maps.UnloadTutorialMap(mapUid);
            }
        });
    }

    [Test]
    public async Task TutorialResearchAssistant_PracticePathNotVaulted()
    {
        var pair = Pair;
        var server = pair.Server;
        var ticker = server.System<GameTicker>();
        var maps = server.System<TutorialMapSystem>();
        var proto = server.ProtoMan;

        await server.WaitPost(() =>
        {
            ticker.SetGamePreset("TutorialServer");
            ticker.StartGameRule(TutorialRule, out _);
            ticker.StartRound();
        });
        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            var ra = proto.Index<TutorialRolePrototype>("TutorialResearchAssistant");
            Assert.That(maps.TryLoadTutorialMap(ra, out var mapUid, out var gridUid, out var spawn), Is.True);

            // Practice path targets keep kit tiles unvaulted during single-chamber stamp.
            var spawnTile = new Vector2i(
                (int) MathF.Floor(spawn.Position.X),
                (int) MathF.Floor(spawn.Position.Y));
            foreach (var ent in server.System<MapSystem>()
                         .GetAnchoredEntities(gridUid, server.EntMan.GetComponent<MapGridComponent>(gridUid), spawnTile))
            {
                var id = server.EntMan.GetComponent<MetaDataComponent>(ent).EntityPrototype?.ID;
                Assert.That(id, Is.Not.EqualTo("TutorialVaultDoor"), "RA spawn must not sit on a vault door");
            }

            Assert.That(server.EntMan.TryGetComponent<TutorialRoomLayoutComponent>(gridUid, out var layout));
            var center = layout!.ChamberCenters[0];
            foreach (var practice in ra.PracticeSpawns)
            {
                var goal = center + practice.Offset;
                var tile = new Vector2i((int) MathF.Floor(goal.X), (int) MathF.Floor(goal.Y));
                foreach (var ent in server.System<MapSystem>()
                             .GetAnchoredEntities(gridUid, server.EntMan.GetComponent<MapGridComponent>(gridUid), tile))
                {
                    var id = server.EntMan.GetComponent<MetaDataComponent>(ent).EntityPrototype?.ID;
                    Assert.That(id, Is.Not.EqualTo("TutorialVaultDoor"),
                        $"Practice offset {practice.Offset} must not be vaulted");
                }
            }

            maps.UnloadTutorialMap(mapUid);
        });
    }

    [Test]
    public async Task TutorialHeadOfPersonnel_IdConsoleNotInWall()
    {
        var pair = Pair;
        var server = pair.Server;
        var ticker = server.System<GameTicker>();
        var maps = server.System<TutorialMapSystem>();
        var proto = server.ProtoMan;

        await server.WaitPost(() =>
        {
            ticker.SetGamePreset("TutorialServer");
            ticker.StartGameRule(TutorialRule, out _);
            ticker.StartRound();
        });
        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            var hop = proto.Index<TutorialRolePrototype>("TutorialHeadOfPersonnel");
            Assert.That(maps.TryLoadTutorialMap(hop, out var mapUid, out var gridUid, out _), Is.True);
            Assert.That(server.EntMan.TryGetComponent<TutorialRoomLayoutComponent>(gridUid, out var layout));
            var center = layout!.ChamberCenters[0];
            var grid = server.EntMan.GetComponent<MapGridComponent>(gridUid);
            var mapSys = server.System<MapSystem>();
            var turf = server.System<TurfSystem>();

            foreach (var practice in hop.PracticeSpawns)
            {
                var goal = center + practice.Offset;
                var tile = new Vector2i((int) MathF.Floor(goal.X), (int) MathF.Floor(goal.Y));
                foreach (var ent in mapSys.GetAnchoredEntities(gridUid, grid, tile))
                {
                    var id = server.EntMan.GetComponent<MetaDataComponent>(ent).EntityPrototype?.ID;
                    Assert.That(id is "WallSolid" or "WallReinforced" or "Girder", Is.False,
                        $"HoP practice {practice.Id} at {practice.Offset} must not land in {id}");
                    Assert.That(id, Is.Not.EqualTo("TutorialVaultDoor"),
                        $"HoP practice {practice.Id} at {practice.Offset} must not be vaulted");
                }
            }

            // Spawn tile (inside office) must be walkable so the player is not stuck in Cap.
            var spawnPos = center + hop.SpawnOffset;
            var spawnTile = new Vector2i((int) MathF.Floor(spawnPos.X), (int) MathF.Floor(spawnPos.Y));
            Assert.That(turf.IsTileBlocked(gridUid, spawnTile, CollisionGroup.MobMask), Is.False,
                "HoP spawnOffset must land on a walkable office tile");

            maps.UnloadTutorialMap(mapUid);
        });
    }

    /// <summary>
    /// Dual-dock welds pin the cargo shuttle. Undocking one port during an early Acknowledge
    /// step (before UndockShuttle is current) must still clear the remaining bay docks.
    /// </summary>
    [Test]
    public async Task TutorialCargoUndock_EarlyUndockClearsDualDocks()
    {
        var pair = Pair;
        var server = pair.Server;
        var ticker = server.System<GameTicker>();
        var docking = server.System<DockingSystem>();
        var tutorial = server.System<TutorialServerRuleSystem>();

        await server.WaitPost(() =>
        {
            ticker.SetGamePreset("TutorialServer");
            ticker.StartGameRule(TutorialRule, out _);
            ticker.StartRound();
        });
        await pair.RunTicksSync(5);

        await server.WaitPost(() =>
        {
            tutorial.TrySelectRole(pair.Player!, "TutorialCargoTechnician", confirmedStub: false);
        });
        await pair.RunTicksSync(15);

        await server.WaitPost(() =>
        {
            var mob = pair.Player!.AttachedEntity!.Value;
            Assert.That(server.EntMan.TryGetComponent<TutorialParticipantComponent>(mob, out var part));

            // Force through bay sensors until open-console (PilotShuttle) — before UndockShuttle.
            for (var i = 0; i < 40; i++)
            {
                if (!tutorial.TryGetCurrentSubGoal(mob, part!, out var sub))
                    break;
                if (sub.Complete == TutorialStepComplete.PilotShuttle &&
                    sub.Id == "open-console")
                    break;
                tutorial.AdvanceSubGoal(mob);
            }
        });
        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            var mob = pair.Player!.AttachedEntity!.Value;
            Assert.That(server.EntMan.TryGetComponent<TutorialParticipantComponent>(mob, out var part));
            Assert.That(tutorial.TryGetCurrentSubGoal(mob, part!, out var sub));
            Assert.That(sub.Complete, Is.EqualTo(TutorialStepComplete.PilotShuttle));
            Assert.That(sub.Id, Is.EqualTo("open-console"));

            var shuttle = FindCargoShuttleOnPlayerMap(server.EntMan, mob);
            Assert.That(shuttle, Is.Not.Null);

            EntityUid? oneDock = null;
            foreach (var dock in docking.GetDocks(shuttle.Value))
            {
                if (dock.Comp.DockedWith is not { } other)
                    continue;
                var otherGrid = server.EntMan.GetComponent<TransformComponent>(other).GridUid;
                if (otherGrid == null ||
                    !server.EntMan.TryGetComponent<TutorialDockStationComponent>(otherGrid.Value, out var station) ||
                    station.StationId != TutorialShuttleArenaSystem.CargoBayStationId)
                    continue;
                oneDock = dock.Owner;
                break;
            }

            Assert.That(oneDock, Is.Not.Null, "Expected a dock to the cargo bay before undock");
            var dockComp = server.EntMan.GetComponent<DockingComponent>(oneDock.Value);
            docking.Undock(new Entity<DockingComponent>(oneDock.Value, dockComp));
        });
        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            var mob = pair.Player!.AttachedEntity!.Value;
            var shuttle = FindCargoShuttleOnPlayerMap(server.EntMan, mob)!.Value;

            foreach (var dock in docking.GetDocks(shuttle))
            {
                if (dock.Comp.DockedWith is not { } other)
                    continue;
                var otherGrid = server.EntMan.GetComponent<TransformComponent>(other).GridUid;
                if (otherGrid != null &&
                    server.EntMan.TryGetComponent<TutorialDockStationComponent>(otherGrid.Value, out var station) &&
                    station.StationId == TutorialShuttleArenaSystem.CargoBayStationId)
                {
                    Assert.Fail("Early undock must cascade-clear remaining cargo-bay docks");
                }
            }
        });
    }

    [Test]
    public async Task TutorialCargoUndock_ClearsAllDocksToHomeBay()
    {
        var pair = Pair;
        var server = pair.Server;
        var ticker = server.System<GameTicker>();
        var maps = server.System<TutorialMapSystem>();
        var docking = server.System<DockingSystem>();
        var proto = server.ProtoMan;

        await server.WaitPost(() =>
        {
            ticker.SetGamePreset("TutorialServer");
            ticker.StartGameRule(TutorialRule, out _);
            ticker.StartRound();
        });
        await pair.RunTicksSync(5);

        EntityUid mapUid = default;
        EntityUid shuttleUid = default;
        EntityUid homeBay = default;

        await server.WaitAssertion(() =>
        {
            var cargo = proto.Index<TutorialRolePrototype>("TutorialCargoTechnician");
            Assert.That(maps.TryLoadTutorialMap(cargo, out mapUid, out shuttleUid, out _), Is.True);

            var mapXform = server.EntMan.GetComponent<TransformComponent>(mapUid);
            var query = server.EntMan.AllEntityQueryEnumerator<TutorialDockStationComponent, TransformComponent>();
            while (query.MoveNext(out var uid, out var station, out var xform))
            {
                if (xform.MapUid != mapXform.MapUid)
                    continue;
                if (station.StationId == TutorialShuttleArenaSystem.CargoBayStationId)
                    homeBay = uid;
            }

            Assert.That(homeBay.IsValid(), Is.True);

            var docked = 0;
            foreach (var dock in docking.GetDocks(shuttleUid))
            {
                if (dock.Comp.DockedWith is not { } other)
                    continue;
                if (server.EntMan.GetComponent<TransformComponent>(other).GridUid == homeBay)
                    docked++;
            }

            Assert.That(docked, Is.GreaterThanOrEqualTo(1), "Cargo shuttle should start docked to home bay");
        });

        // Start cargo tutorial so UndockShuttle goal can be active, then advance to undock step.
        await server.WaitPost(() =>
        {
            var tutorial = server.System<TutorialServerRuleSystem>();
            // Map already loaded above for inspection — start a fresh session on a new map via role select.
            maps.UnloadTutorialMap(mapUid);
            tutorial.TrySelectRole(pair.Player!, "TutorialCargoTechnician", confirmedStub: false);
        });
        await pair.RunTicksSync(15);

        await server.WaitPost(() =>
        {
            var tutorial = server.System<TutorialServerRuleSystem>();
            var mob = pair.Player!.AttachedEntity!.Value;
            Assert.That(server.EntMan.TryGetComponent<TutorialParticipantComponent>(mob, out var part));

            // Force through bay/board sensors until UndockShuttle is current.
            for (var i = 0; i < 40; i++)
            {
                if (!tutorial.TryGetCurrentSubGoal(mob, part!, out var sub))
                    break;
                if (sub.Complete == TutorialStepComplete.UndockShuttle)
                    break;
                tutorial.AdvanceSubGoal(mob);
            }
        });
        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            var tutorial = server.System<TutorialServerRuleSystem>();
            var mob = pair.Player!.AttachedEntity!.Value;
            Assert.That(server.EntMan.TryGetComponent<TutorialParticipantComponent>(mob, out var part));
            Assert.That(tutorial.TryGetCurrentSubGoal(mob, part!, out var sub));
            Assert.That(sub.Complete, Is.EqualTo(TutorialStepComplete.UndockShuttle));

            var shuttle = FindCargoShuttleOnPlayerMap(server.EntMan, mob);
            Assert.That(shuttle, Is.Not.Null);

            EntityUid bay = default;
            var mapUid2 = server.EntMan.GetComponent<TransformComponent>(shuttle.Value).MapUid;
            var q = server.EntMan.AllEntityQueryEnumerator<TutorialDockStationComponent, TransformComponent>();
            while (q.MoveNext(out var uid, out var station, out var xform))
            {
                if (xform.MapUid != mapUid2)
                    continue;
                if (station.StationId == sub.Marker)
                    bay = uid;
            }

            Assert.That(bay.IsValid(), Is.True);

            // Undock a single dock — sensor should clear the rest and advance.
            EntityUid? oneDock = null;
            foreach (var dock in docking.GetDocks(shuttle.Value))
            {
                if (dock.Comp.DockedWith is not { } other)
                    continue;
                if (server.EntMan.GetComponent<TransformComponent>(other).GridUid != bay)
                    continue;
                oneDock = dock.Owner;
                break;
            }

            Assert.That(oneDock, Is.Not.Null);
            Assert.That(CountJointsBetween(server.EntMan, shuttle.Value, bay), Is.GreaterThan(0),
                "Expected dock weld joints before undock");
            Assert.That(server.EntMan.TryGetComponent<PhysicsComponent>(shuttle.Value, out var preBody), Is.True);
            Assert.That(preBody!.BodyType, Is.EqualTo(BodyType.Dynamic),
                $"Shuttle must be Dynamic before undock (was {preBody.BodyType}); Static grids cannot fly after welds clear");
            Assert.That(server.EntMan.TryGetComponent<ShuttleComponent>(shuttle.Value, out var shuttleComp), Is.True);
            Assert.That(shuttleComp!.Enabled, Is.True);
            var dockComp = server.EntMan.GetComponent<DockingComponent>(oneDock.Value);
            docking.Undock(new Entity<DockingComponent>(oneDock.Value, dockComp));
        });
        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            var tutorial = server.System<TutorialServerRuleSystem>();
            var mob = pair.Player!.AttachedEntity!.Value;
            Assert.That(server.EntMan.TryGetComponent<TutorialParticipantComponent>(mob, out var part));
            Assert.That(tutorial.TryGetCurrentSubGoal(mob, part!, out var sub));
            Assert.That(sub.Complete, Is.Not.EqualTo(TutorialStepComplete.UndockShuttle),
                "Undock goal should advance after clearing dual docks");

            var shuttle = FindCargoShuttleOnPlayerMap(server.EntMan, mob)!.Value;
            foreach (var dock in docking.GetDocks(shuttle))
            {
                if (dock.Comp.DockedWith is not { } other)
                    continue;
                var otherGrid = server.EntMan.GetComponent<TransformComponent>(other).GridUid;
                if (otherGrid != null &&
                    server.EntMan.TryGetComponent<TutorialDockStationComponent>(otherGrid.Value, out var station) &&
                    station.StationId == TutorialShuttleArenaSystem.CargoBayStationId)
                {
                    Assert.Fail("Shuttle still docked to cargo-bay after undock cascade");
                }
            }

            EntityUid bayGrid = default;
            var mapUid3 = server.EntMan.GetComponent<TransformComponent>(shuttle).MapUid;
            var bayQuery = server.EntMan.AllEntityQueryEnumerator<TutorialDockStationComponent, TransformComponent>();
            while (bayQuery.MoveNext(out var uid, out var station, out var xform))
            {
                if (xform.MapUid != mapUid3)
                    continue;
                if (station.StationId == TutorialShuttleArenaSystem.CargoBayStationId)
                    bayGrid = uid;
            }

            Assert.That(bayGrid.IsValid(), Is.True);
            Assert.That(CountJointsBetween(server.EntMan, shuttle, bayGrid), Is.EqualTo(0),
                "Dock weld joints must be removed from physics when undocking; DockedWith-only cleanup leaves the shuttle pinned");

            Assert.That(server.EntMan.TryGetComponent<PhysicsComponent>(shuttle, out var shuttleBody), Is.True);
            Assert.That(shuttleBody!.BodyType, Is.EqualTo(BodyType.Dynamic),
                "Cargo shuttle must remain a dynamic body after undock so thrusters can move it");
        });
    }

    private static EntityUid? FindCargoShuttleOnPlayerMap(IEntityManager entMan, EntityUid mob)
    {
        var mapUid = entMan.GetComponent<TransformComponent>(mob).MapUid;
        if (mapUid == null)
            return null;

        var query = entMan.AllEntityQueryEnumerator<Content.Shared.Cargo.Components.CargoShuttleComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out _, out var xform))
        {
            if (xform.MapUid == mapUid)
                return uid;
        }

        return null;
    }

    private static int CountJointsBetween(IEntityManager entMan, EntityUid gridA, EntityUid gridB)
    {
        var count = 0;
        if (!entMan.TryGetComponent<JointComponent>(gridA, out var joints))
            return 0;

        foreach (var joint in joints.GetJoints.Values)
        {
            if ((joint.BodyAUid == gridA && joint.BodyBUid == gridB) ||
                (joint.BodyAUid == gridB && joint.BodyBUid == gridA))
                count++;
        }

        return count;
    }

    [Test]
    public async Task TutorialCargo_HybridSkipsToastWhenQmInRange()
    {
        var pair = Pair;
        var server = pair.Server;
        var ticker = server.System<GameTicker>();

        await server.WaitPost(() =>
        {
            ticker.SetGamePreset("TutorialServer");
            ticker.StartGameRule(TutorialRule, out _);
            ticker.StartRound();
        });
        await pair.RunTicksSync(5);

        await server.WaitPost(() =>
        {
            server.System<TutorialServerRuleSystem>()
                .TrySelectRole(pair.Player!, "TutorialCargoTechnician", confirmedStub: false);
        });
        await pair.RunTicksSync(60);

        await server.WaitAssertion(() =>
        {
            var player = pair.Player!;
            var mob = player.AttachedEntity!.Value;
            var tutorial = server.System<TutorialServerRuleSystem>();
            var entMan = server.EntMan;

            TutorialSessionData? session = null;
            TutorialServerRuleComponent? rule = null;
            var ruleQuery = entMan.EntityQueryEnumerator<TutorialServerRuleComponent>();
            while (ruleQuery.MoveNext(out _, out rule))
            {
                if (rule.Sessions.TryGetValue(player.UserId, out session))
                    break;
            }

            Assert.That(session, Is.Not.Null);
            Assert.That(session!.MentorUid, Is.Not.EqualTo(EntityUid.Invalid));
            Assert.That(tutorial.IsMentorCoachingInRange(mob), Is.True,
                "Bay QM should be in coach range at cargo spawn");

            session.LastProgressPopup = TimeSpan.Zero;
            rule!.Sessions[player.UserId] = session;

            tutorial.AdvanceSubGoal(mob);

            Assert.That(rule.Sessions.TryGetValue(player.UserId, out session));
            Assert.That(session!.LastProgressPopup, Is.EqualTo(TimeSpan.Zero),
                "Hybrid cargo must not tip-chat when the bay QM is already speaking");
        });
    }

    [Test]
    public async Task TutorialTechnicalAssistant_ChamberPadAfterHackDoor()
    {
        var pair = Pair;
        var server = pair.Server;
        var ticker = server.System<GameTicker>();
        var tutorial = server.System<TutorialServerRuleSystem>();
        var entMan = server.EntMan;

        await server.WaitPost(() =>
        {
            ticker.SetGamePreset("TutorialServer");
            ticker.StartGameRule(TutorialRule, out _);
            ticker.StartRound();
        });
        await pair.RunTicksSync(5);

        await server.WaitPost(() =>
        {
            tutorial.TrySelectRole(pair.Player!, "TutorialTechnicalAssistant", confirmedStub: true);
        });
        await pair.RunTicksSync(60);

        await server.WaitAssertion(() =>
        {
            var mob = pair.Player!.AttachedEntity!.Value;
            Assert.That(entMan.TryGetComponent<TutorialParticipantComponent>(mob, out _));

            // Advance through welcome + full hack curriculum to the spacing chamber-pad step.
            for (var i = 0; i < 32; i++)
            {
                Assert.That(tutorial.TryGetSession(mob, out var session));
                if (session.GoalIndex == 2 && session.AwaitingChamberEntryPad)
                    break;
                Assert.That(session.GoalIndex, Is.LessThanOrEqualTo(2),
                    "Should reach spacing pad before leaving the spacing goal");
                tutorial.AdvanceSubGoal(mob);
            }

            Assert.That(tutorial.TryGetSession(mob, out var live));
            Assert.That(live.GoalIndex, Is.EqualTo(2), "Spacing goal should be current");
            Assert.That(live.AwaitingChamberEntryPad, Is.True, "Spacing should require the purple chamber pad");
            Assert.That(entMan.TryGetComponent<TutorialParticipantComponent>(mob, out var part));
            Assert.That(part!.StepComplete, Is.EqualTo(TutorialStepComplete.ReachMarker));
            Assert.That(part.StepText, Does.Contain("purple").IgnoreCase);

            var mobMap = entMan.GetComponent<TransformComponent>(mob).MapUid;
            var foundPad = false;
            var markers = entMan.EntityQueryEnumerator<TutorialStepMarkerComponent, TransformComponent>();
            while (markers.MoveNext(out var uid, out var marker, out var xform))
            {
                if (xform.MapUid != mobMap)
                    continue;
                if (marker.MarkerId != "chamber-1")
                    continue;
                foundPad = true;
                Assert.That(entMan.HasComponent<PointLightComponent>(uid), Is.True,
                    "Chamber pad should glow so players can see the purple X");
                break;
            }

            Assert.That(foundPad, Is.True, "TA map must spawn chamber-1 purple X marker");
        });
    }

    [Test]
    public async Task TutorialTechnicalAssistant_ScrewdriverHold_BeltAndFloor()
    {
        var pair = Pair;
        var server = pair.Server;
        var ticker = server.System<GameTicker>();
        var tutorial = server.System<TutorialServerRuleSystem>();
        var hands = server.System<SharedHandsSystem>();
        var inventory = server.System<InventorySystem>();
        var tags = server.System<TagSystem>();
        var entMan = server.EntMan;

        await server.WaitPost(() =>
        {
            ticker.SetGamePreset("TutorialServer");
            ticker.StartGameRule(TutorialRule, out _);
            ticker.StartRound();
        });
        await pair.RunTicksSync(5);

        await server.WaitPost(() =>
        {
            tutorial.TrySelectRole(pair.Player!, "TutorialTechnicalAssistant", confirmedStub: true);
        });
        await pair.RunTicksSync(60);

        // Belt screwdriver: starting-gear tool must be sensor-tagged and complete HoldTag.
        await server.WaitAssertion(() =>
        {
            var mob = pair.Player!.AttachedEntity!.Value;
            Assert.That(entMan.TryGetComponent<TutorialParticipantComponent>(mob, out var part));
            Assert.That(part!.StepComplete, Is.EqualTo(TutorialStepComplete.Acknowledge));
            tutorial.AdvanceSubGoal(mob); // intro Acknowledge → wear gloves

            Assert.That(entMan.GetComponent<TutorialParticipantComponent>(mob).StepComplete,
                Is.EqualTo(TutorialStepComplete.WearItem));
            tutorial.AdvanceSubGoal(mob); // gloves → hold screwdriver

            Assert.That(entMan.GetComponent<TutorialParticipantComponent>(mob).StepComplete,
                Is.EqualTo(TutorialStepComplete.HoldTag));
            Assert.That(tutorial.TryGetCurrentSubGoal(mob, entMan.GetComponent<TutorialParticipantComponent>(mob), out var sub));
            Assert.That(sub.Tag, Is.EqualTo("Screwdriver"));

            Assert.That(inventory.TryGetSlotEntity(mob, "belt", out var belt), Is.True);
            Assert.That(entMan.TryGetComponent<StorageComponent>(belt!.Value, out var storage), Is.True);

            EntityUid? beltScrewdriver = null;
            foreach (var item in storage!.Container.ContainedEntities)
            {
                if (tags.HasTag(item, (ProtoId<TagPrototype>) "Screwdriver"))
                {
                    beltScrewdriver = item;
                    break;
                }
            }

            Assert.That(beltScrewdriver, Is.Not.Null, "TA belt should contain a screwdriver");
            Assert.That(entMan.HasComponent<TutorialSensorTargetComponent>(beltScrewdriver!.Value), Is.True,
                "Belt screwdriver must be tagged as a tutorial sensor target");
            Assert.That(hands.TryPickupAnyHand(mob, beltScrewdriver.Value, checkActionBlocker: false, animate: false),
                Is.True);
        });
        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            var mob = pair.Player!.AttachedEntity!.Value;
            Assert.That(entMan.GetComponent<TutorialParticipantComponent>(mob).StepComplete,
                Is.EqualTo(TutorialStepComplete.WiresPanelOpen),
                "Holding the belt screwdriver should advance hold-screwdriver");
        });

        // Floor screwdriver: practice-spawned tool on a fresh TA session.
        await server.WaitPost(() =>
        {
            tutorial.TrySelectRole(pair.Player!, "TutorialTechnicalAssistant", confirmedStub: true);
        });
        await pair.RunTicksSync(60);

        await server.WaitAssertion(() =>
        {
            var mob = pair.Player!.AttachedEntity!.Value;
            tutorial.AdvanceSubGoal(mob); // intro → gloves
            tutorial.AdvanceSubGoal(mob); // gloves → screwdriver
            Assert.That(entMan.GetComponent<TutorialParticipantComponent>(mob).StepComplete,
                Is.EqualTo(TutorialStepComplete.HoldTag));

            var containers = server.System<SharedContainerSystem>();
            var mobXform = entMan.GetComponent<TransformComponent>(mob);
            EntityUid? floorScrewdriver = null;
            var query = entMan.EntityQueryEnumerator<TutorialSensorTargetComponent, MetaDataComponent, TransformComponent>();
            while (query.MoveNext(out var uid, out _, out var meta, out var xform))
            {
                if (xform.MapUid != mobXform.MapUid)
                    continue;
                if (meta.EntityPrototype?.ID != "Screwdriver")
                    continue;
                // Practice-spawned floor tool only — skip belt / closet contents.
                if (containers.IsEntityInContainer(uid))
                    continue;
                floorScrewdriver = uid;
                break;
            }

            Assert.That(floorScrewdriver, Is.Not.Null, "TA map should practice-spawn a floor screwdriver");
            Assert.That(tags.HasTag(floorScrewdriver!.Value, (ProtoId<TagPrototype>) "Screwdriver"), Is.True);
            Assert.That(hands.TryPickupAnyHand(mob, floorScrewdriver.Value, checkActionBlocker: false, animate: false),
                Is.True);
        });
        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            var mob = pair.Player!.AttachedEntity!.Value;
            Assert.That(entMan.GetComponent<TutorialParticipantComponent>(mob).StepComplete,
                Is.EqualTo(TutorialStepComplete.WiresPanelOpen),
                "Holding the floor screwdriver should advance hold-screwdriver");
        });
    }

    [Test]
    public async Task TutorialXenoborg_SpawnsOnMothershipArena()
    {
        var pair = Pair;
        var server = pair.Server;
        var ticker = server.System<GameTicker>();
        var maps = server.System<TutorialMapSystem>();
        var proto = server.ProtoMan;

        await server.WaitPost(() =>
        {
            ticker.SetGamePreset("TutorialServer");
            ticker.StartGameRule(TutorialRule, out _);
            ticker.StartRound();
        });
        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            var xeno = proto.Index<TutorialRolePrototype>("TutorialAntagXenoborg");
            Assert.That(maps.TryLoadTutorialMap(xeno, out var mapUid, out var gridUid, out _), Is.True);
            Assert.That(server.EntMan.HasComponent<ShuttleComponent>(gridUid), Is.True,
                "Xenoborg tutorial must load the mothership shuttle, not a science crop");

            var hasConsole = false;
            var query = server.EntMan.AllEntityQueryEnumerator<TransformComponent>();
            while (query.MoveNext(out var uid, out var xform))
            {
                if (xform.GridUid != gridUid)
                    continue;
                var id = server.EntMan.GetComponent<MetaDataComponent>(uid).EntityPrototype?.ID;
                if (id == "ComputerShuttle")
                    hasConsole = true;
            }

            Assert.That(hasConsole, Is.True);
            maps.UnloadTutorialMap(mapUid);
        });
    }

    [Test]
    public async Task TutorialBorg_ForcesGenericChassisAndModuleSwap()
    {
        var pair = Pair;
        var server = pair.Server;
        var entMan = server.EntMan;
        var switchableSys = server.System<BorgSwitchableTypeSystem>();
        var borgSys = server.System<BorgSystem>();
        EntityUid borg = default;

        await server.WaitPost(() =>
        {
            borg = entMan.Spawn("TutorialPlayerBorg");
        });
        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.TryGetComponent<BorgSwitchableTypeComponent>(borg, out var switchable), Is.True);
            Assert.That(switchable!.AvailableBorgTypes, Is.Not.Null);
            Assert.That(switchable.AvailableBorgTypes!, Has.Count.EqualTo(1));
            Assert.That(switchable.AvailableBorgTypes![0].Id, Is.EqualTo("generic"));
            Assert.That(switchable.SelectedBorgType, Is.Null,
                "Tutorial borg must pick a chassis before modules/hands exist");

            Assert.That(switchableSys.TrySelectBorgType(borg, "engineering"), Is.False,
                "Non-generic chassis must be rejected during the tutorial");
            Assert.That(entMan.GetComponent<BorgSwitchableTypeComponent>(borg).SelectedBorgType, Is.Null);

            Assert.That(entMan.TryGetComponent<HandsComponent>(borg, out var handsBefore), Is.True);
            Assert.That(handsBefore!.Count, Is.EqualTo(0),
                "Cyborgs have no hand slots until a chassis type and module are active");

            Assert.That(switchableSys.TrySelectBorgType(borg, "generic"), Is.True);
            Assert.That(entMan.GetComponent<BorgSwitchableTypeComponent>(borg).SelectedBorgType?.Id,
                Is.EqualTo("generic"));

            Assert.That(entMan.TryGetComponent<BorgChassisComponent>(borg, out var chassis), Is.True);
            Assert.That(chassis!.ModuleContainer.ContainedEntities.Count, Is.GreaterThanOrEqualTo(2));

            // Modules only Install when the chassis is Active (brain/power path in a real round).
            borgSys.SetActive((borg, chassis), true);

            EntityUid? toolModule = null;
            EntityUid? inflatableModule = null;
            foreach (var moduleUid in chassis.ModuleContainer.ContainedEntities)
            {
                var id = entMan.GetComponent<MetaDataComponent>(moduleUid).EntityPrototype?.ID;
                if (id == "BorgModuleTool")
                    toolModule = moduleUid;
                else if (id == "BorgModuleInflatable")
                    inflatableModule = moduleUid;
            }

            Assert.That(toolModule, Is.Not.Null, "Generic chassis should install BorgModuleTool");
            Assert.That(inflatableModule, Is.Not.Null, "Generic chassis should install BorgModuleInflatable");
            Assert.That(entMan.GetComponent<BorgModuleComponent>(toolModule!.Value).Installed, Is.True);
            Assert.That(entMan.GetComponent<BorgModuleComponent>(inflatableModule!.Value).Installed, Is.True);

            // Activate installs modules; first selectable auto-selects. Swap between selectable modules.
            borgSys.SelectModule(borg, toolModule.Value);
            Assert.That(entMan.GetComponent<BorgChassisComponent>(borg).SelectedModule, Is.EqualTo(toolModule));

            Assert.That(entMan.TryGetComponent<HandsComponent>(borg, out var hands), Is.True);
            Assert.That(hands!.Count, Is.GreaterThan(0),
                "Selecting a module should provide hand slots with tools");

            borgSys.SelectModule(borg, inflatableModule.Value);
            Assert.That(entMan.GetComponent<BorgChassisComponent>(borg).SelectedModule, Is.EqualTo(inflatableModule));
        });

        await server.WaitPost(() => entMan.DeleteEntity(borg));
    }

    /// <summary>
    /// Empty-hand click on the medical mentor must complete InteractMentor (hug). The trainer
    /// must not swallow the interact before InteractionPopup raises InteractionSuccessEvent —
    /// otherwise the welcome step loops on its stuck hint forever.
    /// </summary>
    [Test]
    public async Task TutorialMedicalDoctor_HugMentorAdvancesWelcomeStep()
    {
        var pair = Pair;
        var server = pair.Server;
        var ticker = server.System<GameTicker>();
        var tutorial = server.System<TutorialServerRuleSystem>();
        var entMan = server.EntMan;

        await server.WaitPost(() =>
        {
            ticker.SetGamePreset("TutorialServer");
            ticker.StartGameRule(TutorialRule, out _);
            ticker.StartRound();
        });
        await pair.RunTicksSync(5);

        await server.WaitPost(() =>
        {
            tutorial.TrySelectRole(pair.Player!, "TutorialMedicalDoctor", confirmedStub: false);
        });
        await pair.RunTicksSync(60);

        await server.WaitAssertion(() =>
        {
            var player = pair.Player!;
            Assert.That(player.AttachedEntity, Is.Not.Null);
            var mob = player.AttachedEntity!.Value;

            Assert.That(tutorial.TryGetCurrentSubGoal(mob,
                entMan.GetComponent<TutorialParticipantComponent>(mob), out var sub));
            Assert.That(sub.Id, Is.EqualTo("hug-mentor"));
            Assert.That(sub.Complete, Is.EqualTo(TutorialStepComplete.InteractMentor));

            TutorialSessionData? session = null;
            var ruleQuery = entMan.EntityQueryEnumerator<TutorialServerRuleComponent>();
            while (ruleQuery.MoveNext(out _, out var ruleComp))
            {
                if (ruleComp.Sessions.TryGetValue(player.UserId, out session))
                    break;
            }

            Assert.That(session, Is.Not.Null);
            Assert.That(session!.MentorUid, Is.Not.EqualTo(EntityUid.Invalid));
            Assert.That(entMan.HasComponent<InteractionPopupComponent>(session.MentorUid), Is.True,
                "Mentor must keep species InteractionPopup so hugs raise InteractionSuccessEvent");

            // Drain any leftover coach segment so the click is not consumed as "next line".
            if (entMan.TryGetComponent<TutorialTrainerComponent>(session.MentorUid, out var trainer))
            {
                trainer.PendingLines.Clear();
                trainer.PendingAfterLines.Clear();
                trainer.ReactingFor = null;
            }

            var interact = new InteractHandEvent(mob, session.MentorUid);
            entMan.EventBus.RaiseLocalEvent(session.MentorUid, interact);

            Assert.That(tutorial.TryGetCurrentSubGoal(mob,
                entMan.GetComponent<TutorialParticipantComponent>(mob), out var after));
            Assert.That(after.Id, Is.Not.EqualTo("hug-mentor"),
                "Empty-hand mentor hug must advance past InteractMentor");
        });
    }

    [Test]
    public async Task TutorialMedicalDoctor_ScanRequiresHeldAnalyzer_HealIgnoresDead_DefibRevives()
    {
        var pair = Pair;
        var server = pair.Server;
        var ticker = server.System<GameTicker>();
        var tutorial = server.System<TutorialServerRuleSystem>();
        var hands = server.System<SharedHandsSystem>();
        var interaction = server.System<SharedInteractionSystem>();
        var damageable = server.System<DamageableSystem>();
        var itemToggle = server.System<ItemToggleSystem>();
        var defib = server.System<DefibrillatorSystem>();
        var tags = server.System<TagSystem>();
        var entMan = server.EntMan;

        await server.WaitPost(() =>
        {
            ticker.SetGamePreset("TutorialServer");
            ticker.StartGameRule(TutorialRule, out _);
            ticker.StartRound();
        });
        await pair.RunTicksSync(5);

        await server.WaitPost(() =>
        {
            tutorial.TrySelectRole(pair.Player!, "TutorialMedicalDoctor", confirmedStub: false);
        });
        await pair.RunTicksSync(60);

        EntityUid mob = default;
        EntityUid patient = default;
        EntityUid corpse = default;
        EntityUid analyzer = default;
        EntityUid defibrillator = default;

        void DropAllHeld()
        {
            foreach (var held in hands.EnumerateHeld(mob).ToArray())
                hands.TryDrop(mob, held, checkActionBlocker: false);
        }

        EntityUid FindOnMap(string protoId)
        {
            var mapUid = entMan.GetComponent<TransformComponent>(mob).MapUid;
            var itemQuery = entMan.EntityQueryEnumerator<MetaDataComponent, TransformComponent>();
            while (itemQuery.MoveNext(out var uid, out var meta, out var xform))
            {
                if (xform.MapUid != mapUid || meta.EntityPrototype?.ID != protoId)
                    continue;
                return uid;
            }

            return EntityUid.Invalid;
        }

        await server.WaitAssertion(() =>
        {
            mob = pair.Player!.AttachedEntity!.Value;
            Assert.That(entMan.TryGetComponent<TutorialParticipantComponent>(mob, out _), Is.True);

            var mapUid = entMan.GetComponent<TransformComponent>(mob).MapUid;
            var query = entMan.EntityQueryEnumerator<TutorialPracticeMobComponent, MetaDataComponent, TransformComponent>();
            while (query.MoveNext(out var uid, out _, out var meta, out var xform))
            {
                if (xform.MapUid != mapUid)
                    continue;

                switch (meta.EntityPrototype?.ID)
                {
                    case "TutorialPracticeMobPatient":
                        patient = uid;
                        break;
                    case "TutorialPracticeMobCorpse":
                        corpse = uid;
                        break;
                }
            }

            Assert.That(patient, Is.Not.EqualTo(EntityUid.Invalid), "Doctor arena must spawn a living patient");
            Assert.That(corpse, Is.Not.EqualTo(EntityUid.Invalid), "Doctor arena must spawn a practice corpse");
            Assert.That(tags.HasTag(patient, (ProtoId<TagPrototype>) "TutorialPracticePatient"), Is.True);
            Assert.That(tags.HasTag(corpse, (ProtoId<TagPrototype>) "TutorialPracticeCorpse"), Is.True);
            Assert.That(entMan.GetComponent<MobStateComponent>(corpse).CurrentState, Is.EqualTo(MobState.Dead),
                "Corpse spawnDamage should put the practice mob in Dead");

            // Advance into scan-patient (InteractTargetHolding).
            for (var i = 0; i < 20; i++)
            {
                if (!tutorial.TryGetCurrentSubGoal(mob, entMan.GetComponent<TutorialParticipantComponent>(mob), out var sub))
                    break;
                if (sub.Id == "scan-patient")
                    break;
                if (sub.Complete is TutorialStepComplete.Acknowledge or TutorialStepComplete.InteractMentor)
                    tutorial.AdvanceSubGoal(mob);
                else if (sub.Complete == TutorialStepComplete.HoldItem &&
                         sub.Entity == new EntProtoId("ClothingEyesHudMedical"))
                {
                    DropAllHeld();
                    var hud = FindOnMap("ClothingEyesHudMedical");
                    Assert.That(hud, Is.Not.EqualTo(EntityUid.Invalid));
                    Assert.That(hands.TryPickupAnyHand(mob, hud, checkActionBlocker: false, animate: false), Is.True);
                }
                else if (sub.Complete == TutorialStepComplete.HoldItem &&
                         sub.Entity == new EntProtoId("HandheldHealthAnalyzer"))
                {
                    DropAllHeld();
                    analyzer = FindOnMap("HandheldHealthAnalyzer");
                    Assert.That(analyzer, Is.Not.EqualTo(EntityUid.Invalid));
                    Assert.That(hands.TryPickupAnyHand(mob, analyzer, checkActionBlocker: false, animate: false), Is.True);
                }
                else
                {
                    Assert.Fail($"Unexpected sub-goal while seeking scan-patient: {sub.Id} ({sub.Complete})");
                    break;
                }
            }

            Assert.That(tutorial.TryGetCurrentSubGoal(mob, entMan.GetComponent<TutorialParticipantComponent>(mob), out var scanSub));
            Assert.That(scanSub.Id, Is.EqualTo("scan-patient"));
            Assert.That(scanSub.Complete, Is.EqualTo(TutorialStepComplete.InteractTargetHolding));

            // Empty-hand click must not advance — drop analyzer first.
            DropAllHeld();
            var emptyHand = new UserInteractHandEvent(mob, patient);
            entMan.EventBus.RaiseLocalEvent(mob, emptyHand, true);
        });
        await pair.RunTicksSync(2);

        await server.WaitAssertion(() =>
        {
            Assert.That(tutorial.TryGetCurrentSubGoal(mob, entMan.GetComponent<TutorialParticipantComponent>(mob), out var stillScan));
            Assert.That(stillScan.Id, Is.EqualTo("scan-patient"),
                "Empty-hand interact must not complete InteractTargetHolding");

            DropAllHeld();
            if (analyzer == EntityUid.Invalid || entMan.Deleted(analyzer))
                analyzer = FindOnMap("HandheldHealthAnalyzer");
            Assert.That(hands.TryPickupAnyHand(mob, analyzer, checkActionBlocker: false, animate: false), Is.True);
            var coords = entMan.GetComponent<TransformComponent>(patient).Coordinates;
            Assert.That(interaction.InteractUsing(mob, analyzer, patient, coords, checkCanInteract: false, checkCanUse: false),
                Is.True);
        });
        await pair.RunTicksSync(2);

        await server.WaitAssertion(() =>
        {
            Assert.That(tutorial.TryGetCurrentSubGoal(mob, entMan.GetComponent<TutorialParticipantComponent>(mob), out var afterScan));
            Assert.That(afterScan.Id, Is.EqualTo("med-vend"),
                "Held analyzer on TutorialPracticePatient should advance scan-patient");

            // Force-advance to heal-dummy, then prove Dead corpses are ignored.
            for (var i = 0; i < 20; i++)
            {
                if (!tutorial.TryGetCurrentSubGoal(mob, entMan.GetComponent<TutorialParticipantComponent>(mob), out var sub))
                    break;
                if (sub.Id == "heal-dummy")
                    break;
                tutorial.AdvanceSubGoal(mob);
            }

            Assert.That(tutorial.TryGetCurrentSubGoal(mob, entMan.GetComponent<TutorialParticipantComponent>(mob), out var healSub));
            Assert.That(healSub.Id, Is.EqualTo("heal-dummy"));

            // Clear living patient damage; corpse stays dead/high damage.
            Assert.That(entMan.TryGetComponent<DamageableComponent>(patient, out var patientDmg), Is.True);
            damageable.SetDamage((patient, patientDmg!), new DamageSpecifier());
        });
        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            Assert.That(tutorial.TryGetCurrentSubGoal(mob, entMan.GetComponent<TutorialParticipantComponent>(mob), out var afterHeal));
            Assert.That(afterHeal.Id, Is.Not.EqualTo("heal-dummy"),
                "PracticeMobDamageBelow must ignore Dead corpses and advance when the living patient is healed");
            Assert.That(entMan.GetComponent<MobStateComponent>(patient).CurrentState, Is.EqualTo(MobState.Critical),
                "Healed patient should drop into critical for medipen practice");

            // Force-advance past epi/crit tips into the revive goal, then pick up the defib.
            for (var i = 0; i < 30; i++)
            {
                if (!tutorial.TryGetCurrentSubGoal(mob, entMan.GetComponent<TutorialParticipantComponent>(mob), out var sub))
                    break;
                if (sub.Id is "hold-defib" or "revive-corpse")
                    break;
                tutorial.AdvanceSubGoal(mob);
            }

            Assert.That(tutorial.TryGetCurrentSubGoal(mob, entMan.GetComponent<TutorialParticipantComponent>(mob), out var holdDefib));
            Assert.That(holdDefib.Id, Is.EqualTo("hold-defib"),
                $"Expected hold-defib, at {holdDefib.Id} ({holdDefib.Complete})");

            DropAllHeld();
            defibrillator = FindOnMap("DefibrillatorOneHandedUnpowered");
            Assert.That(defibrillator, Is.Not.EqualTo(EntityUid.Invalid));
            Assert.That(hands.TryPickupAnyHand(mob, defibrillator, checkActionBlocker: false, animate: false), Is.True);
        });
        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            Assert.That(tutorial.TryGetCurrentSubGoal(mob, entMan.GetComponent<TutorialParticipantComponent>(mob), out var reviveSub));
            Assert.That(reviveSub.Id, Is.EqualTo("revive-corpse"),
                $"Holding defib should advance to revive-corpse, at {reviveSub.Id}");

            // Reset to the tutorial mix — asphyx can climb while the corpse waits mid-test.
            var proto = server.ProtoMan;
            var blunt = proto.Index<DamageTypePrototype>("Blunt");
            var asphyx = proto.Index<DamageTypePrototype>("Asphyxiation");
            var readyDamage = new DamageSpecifier(blunt, FixedPoint2.New(160)) +
                              new DamageSpecifier(asphyx, FixedPoint2.New(50));
            Assert.That(entMan.TryGetComponent<DamageableComponent>(corpse, out var corpseDmg), Is.True);
            damageable.SetDamage((corpse, corpseDmg!), readyDamage);
            Assert.That(entMan.GetComponent<MobStateComponent>(corpse).CurrentState, Is.EqualTo(MobState.Dead));

            Assert.That(itemToggle.TryActivate(defibrillator, mob), Is.True);
            defib.Zap(defibrillator, corpse, mob);
        });
        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.GetComponent<MobStateComponent>(corpse).CurrentState, Is.EqualTo(MobState.Critical),
                "210 damage corpse with 50 asphyx should revive after one zap (-40 asphyx + electrocution)");
            Assert.That(tutorial.TryGetCurrentSubGoal(mob, entMan.GetComponent<TutorialParticipantComponent>(mob), out var afterRevive));
            Assert.That(afterRevive.Id, Is.EqualTo("done"),
                $"PracticeMobRevived should advance revive-corpse to done (at {afterRevive.Id})");
            Assert.That(afterRevive.Complete, Is.Not.EqualTo(TutorialStepComplete.PracticeMobRevived));
        });
    }
}
