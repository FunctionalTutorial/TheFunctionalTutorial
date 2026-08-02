using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.IntegrationTests.Fixtures;
using Content.Server._Functional.TutorialServer;
using Content.Server.GameTicking;
using Content.Server.GameTicking.Rules.Components;
using Content.Server.Ghost;
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
using Content.Shared.Damage.Systems;
using Content.Shared.Fluids.Components;
using Content.Shared.GameTicking;
using Content.Shared.Ghost;
using Content.Shared.Gravity;
using Content.Shared.Hands.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using Content.Shared.Interaction.Events;
using Content.Shared.Item;
using Content.Shared.Maps;
using Content.Shared.Mind;
using Content.Shared.Nutrition.EntitySystems;
using Content.Shared.Preferences.Loadouts;
using Content.Shared.Roles;
using Content.Server.Shuttles.Components;
using Content.Server.Shuttles.Systems;
using Robust.Server.GameObjects;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;
using Robust.Shared.Localization;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
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
            Assert.That(cfg.GetCVar(CCVars.OocEnabled), Is.False);
            Assert.That(cfg.GetCVar(CCVars.LoocEnabled), Is.False);
            Assert.That(cfg.GetCVar(CCVars.DeadChatEnabled), Is.False);
            Assert.That(server.EntMan.Count<Content.Server.Shuttles.Components.StationCentcommComponent>(), Is.EqualTo(0),
                "TutorialServer must not spawn CentComm");
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
            Assert.That(Sub(chemist, "inaprovaline").Complete, Is.EqualTo(TutorialStepComplete.SolutionContains));
            Assert.That(Sub(chemist, "inaprovaline").Reagent, Is.EqualTo(new ProtoId<Content.Shared.Chemistry.Reagent.ReagentPrototype>("Inaprovaline")));
            Assert.That(Sub(chemist, "dylovene").Complete, Is.EqualTo(TutorialStepComplete.SolutionContains));
            Assert.That(Sub(chemist, "dylovene").Reagent, Is.EqualTo(new ProtoId<Content.Shared.Chemistry.Reagent.ReagentPrototype>("Dylovene")));
            Assert.That(Sub(chemist, "pills").Complete, Is.EqualTo(TutorialStepComplete.ObtainItem));
            Assert.That(Sub(chemist, "pills").Entity, Is.EqualTo(new EntProtoId("PillCanister")));
            Assert.That(Sub(chemist, "pills").MinCount, Is.EqualTo(1));
            Assert.That(chemist.PracticeSpawns.Any(p => p.Id == "TutorialChemDispenser"));
            Assert.That(chemist.PracticeSpawns.Any(p => p.Id == "PillCanister"));

            var janitor = proto.Index<TutorialRolePrototype>("TutorialJanitor");
            Assert.That(Sub(janitor, "clear-puddle").Complete, Is.EqualTo(TutorialStepComplete.PuddleCleared));
            Assert.That(Sub(janitor, "clear-puddle").Marker, Is.EqualTo("blood-puddle"));

            var ta = proto.Index<TutorialRolePrototype>("TutorialTechnicalAssistant");
            Assert.That(ta.Stub, Is.False);
            Assert.That(Sub(ta, "open-panel").Complete, Is.EqualTo(TutorialStepComplete.WiresPanelOpen));
            Assert.That(Sub(ta, "place-lv").Complete, Is.EqualTo(TutorialStepComplete.MapHasEntity));
            Assert.That(Sub(ta, "place-lv").Entity, Is.EqualTo(new EntProtoId("CableApcExtension")));

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
            Assert.That(Sub(doctor, "heal-dummy").Complete, Is.EqualTo(TutorialStepComplete.PracticeMobDamageBelow));
            Assert.That(Sub(doctor, "use-epi").Complete, Is.EqualTo(TutorialStepComplete.UseInHand));
            Assert.That(Sub(doctor, "use-epi").Entity, Is.EqualTo(new EntProtoId("EmergencyMedipen")));
            Assert.That(doctor.PracticeSpawns.Any(p => p.Id == "TutorialPracticeMobPatient"));
            Assert.That(doctor.PracticeSpawns.Any(p => p.Id == "EmergencyMedipen"));

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
            Assert.That(ce.RoomTemplate, Is.EqualTo(new ProtoId<TutorialRoomTemplatePrototype>("TutorialSectionEngineering")));
            Assert.That(ce.PracticeSpawns.Any(p => p.Id == "TutorialComputerComms"));
            Assert.That(ce.Goals.SelectMany(g => g.SubGoals).Any(s => s.Id is "teg" or "singulo"), Is.False);

            var cmo = proto.Index<TutorialRolePrototype>("TutorialChiefMedicalOfficer");
            Assert.That(Sub(cmo, "heal-dummy").Complete, Is.EqualTo(TutorialStepComplete.PracticeMobDamageBelow));
            Assert.That(Sub(cmo, "use-crew-monitor").Complete, Is.EqualTo(TutorialStepComplete.UseInHand));
            Assert.That(Sub(cmo, "use-crew-monitor").Entity, Is.EqualTo(new EntProtoId("HandheldCrewMonitor")));
            Assert.That(cmo.PracticeSpawns.Any(p => p.Id == "HandheldCrewMonitor"));

            var rd = proto.Index<TutorialRolePrototype>("TutorialResearchDirector");
            Assert.That(rd.Goals.SelectMany(g => g.SubGoals).Any(s => s.Complete == TutorialStepComplete.SpawnAnomaly));
            Assert.That(rd.Goals.SelectMany(g => g.SubGoals).Any(s => s.Complete == TutorialStepComplete.RemoveAnomaly));

            var para = proto.Index<TutorialRolePrototype>("TutorialParamedic");
            Assert.That(para.Stub, Is.False);
            Assert.That(Sub(para, "heal-dummy").Complete, Is.EqualTo(TutorialStepComplete.PracticeMobDamageBelow));

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
            Assert.That(surgery.Stub, Is.True); //Wizden: temporarily greyed pending manual test
            Assert.That(surgery.Category, Is.EqualTo("Server specific"));
            Assert.That(surgery.SubCategory, Is.EqualTo("Starlight"));
            Assert.That(surgery.Name, Is.EqualTo("tutorial-job-surgery-starlight-name"));
            Assert.That(Loc.GetString(surgery.Name), Is.EqualTo("Surgery"));
            Assert.That(Sub(surgery, "open-ui").Complete, Is.EqualTo(TutorialStepComplete.StarlightSurgeryUiOpened));
            Assert.That(Sub(surgery, "implant").Complete, Is.EqualTo(TutorialStepComplete.StarlightSurgeryEyeImplanted));
            Assert.That(surgery.PracticeSpawns.Any(p => p.Id == "TutorialPracticeMobStarlightSurgery"));
            Assert.That(surgery.PracticeSpawns.Any(p => p.Id == "TutorialStarlightEyeImplantWelding"));

            var cyberMed = proto.Index<TutorialRolePrototype>("TutorialSurgeryCyberMed");
            Assert.That(cyberMed.Stub, Is.True); //Wizden: temporarily greyed pending manual test
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
    public async Task TutorialRoles_RoomChangesAreSparseAndPaced()
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

                // First room change (EnterRoom > 0) must come after several interactive steps.
                var stepsBefore = 0;
                foreach (var goal in role.Goals)
                {
                    if (goal.EnterRoom is > 0)
                    {
                        Assert.That(stepsBefore, Is.GreaterThanOrEqualTo(3),
                            $"{role.ID}: need a few steps in chamber 0 before EnterRoom {goal.EnterRoom} ({goal.Id})");
                        break;
                    }

                    stepsBefore += goal.SubGoals.Count;
                }
            }

            var ra = proto.Index<TutorialRolePrototype>("TutorialResearchAssistant");
            Assert.That(TutorialMapSystem.ResolveCopyCount(ra), Is.EqualTo(1));
            var console = ra.PracticeSpawns.First(p => p.Id == "TutorialResearchConsole");
            Assert.That(console.Offset.X, Is.LessThan(-3f),
                "R&D console should sit on the Saltern science console offset, not chamber center");
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
            Assert.That(passenger.PracticeSpawns.Any(p => p.Id == "TutorialPassengerTrainer" && p.Room == 0));
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
    public async Task TutorialRolePicker_OrdersPassengerThenDepartmentsThenAntagsAndOmitsErt()
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
            Assert.That(entries[0].RoleId, Is.EqualTo("TutorialPassenger"));
            Assert.That(entries.Any(e => e.RoleId.Contains("ERT", StringComparison.OrdinalIgnoreCase)), Is.False);

            var firstAntag = entries.FindIndex(e => e.Category == "Wizden antagonists");
            Assert.That(firstAntag, Is.GreaterThan(0));
            Assert.That(entries.Skip(firstAntag).All(e => e.Category == "Wizden antagonists"), Is.True,
                "Wizden antagonists must be grouped at the bottom");

            var lastNonAntag = entries.Take(firstAntag).Last();
            Assert.That(lastNonAntag.Category, Is.Not.EqualTo("Wizden antagonists"));
            Assert.That(entries.Take(firstAntag).Any(e => e.Category is "Command" or "Security" or "Medical"),
                Is.True,
                "Department roles should appear before antagonists");

            var serverSpecific = entries.Where(e => e.Category == "Server specific").ToList();
            Assert.That(serverSpecific, Is.Not.Empty);
            Assert.That(serverSpecific.Select(e => e.SubCategory), Is.EquivalentTo(new[] { "BPL14", "Starlight", "Starlight", "Starlight" }));
            Assert.That(serverSpecific.Single(e => e.RoleId == "TutorialSurgeryCyberMed").DisplayName, Is.EqualTo("Surgery"));
            Assert.That(serverSpecific.Single(e => e.RoleId == "TutorialSurgeryStarlight").DisplayName, Is.EqualTo("Surgery"));
            Assert.That(serverSpecific.Single(e => e.RoleId == "TutorialAntagVampire").SubCategory, Is.EqualTo("Starlight"));
            Assert.That(serverSpecific.Single(e => e.RoleId == "TutorialAntagChangeling").SubCategory, Is.EqualTo("Starlight"));
            Assert.That(entries.All(e => e.Category != "Antagonist"), Is.True);
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
            Assert.That(completes, Does.Contain(TutorialStepComplete.UndockShuttle));
            Assert.That(completes, Does.Contain(TutorialStepComplete.DockShuttle));

            var markers = cargo.Goals.SelectMany(g => g.SubGoals)
                .Where(s => s.Complete is TutorialStepComplete.DockShuttle or TutorialStepComplete.UndockShuttle)
                .Select(s => s.Marker)
                .ToArray();
            Assert.That(markers, Does.Contain("cargo-bay"));
            Assert.That(markers, Does.Contain("ats"));

            Assert.That(cargo.Goals.Any(g => g.Id == "sell"));
            Assert.That(cargo.Goals.SelectMany(g => g.SubGoals)
                .Any(s => s.Id == "sell-pallet" && s.Tag == "TutorialCargoSell"));
            Assert.That(cargo.Goals.SelectMany(g => g.SubGoals)
                .Any(s => s.Id == "sell-goods" && s.Complete == TutorialStepComplete.CargoSold));
            Assert.That(cargo.Goals.SelectMany(g => g.SubGoals)
                .Any(s => s.Id == "sell-bounty" && s.Complete == TutorialStepComplete.CargoBountyFulfilled));
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
            Assert.That(maps.TryLoadTutorialMap(cargo, out var mapUid, out var shuttleUid, out var spawn), Is.True);
            Assert.That(server.EntMan.HasComponent<Content.Server.Shuttles.Components.ShuttleComponent>(shuttleUid));
            Assert.That(server.EntMan.HasComponent<Content.Shared.Cargo.Components.CargoShuttleComponent>(shuttleUid));
            Assert.That(spawn.EntityId, Is.EqualTo(shuttleUid));

            var dockCount = 0;
            var consoleCount = 0;
            var thrusterCount = 0;
            var cargoBay = false;
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
                    consoleCount++;
                if (id is "Thruster" or "ThrusterLarge")
                    thrusterCount++;

                if (server.EntMan.TryGetComponent<TutorialDockStationComponent>(uid, out var station))
                {
                    if (station.StationId == TutorialShuttleArenaSystem.CargoBayStationId)
                        cargoBay = true;
                    if (station.StationId == TutorialShuttleArenaSystem.AtsStationId)
                        ats = true;
                }
            }

            Assert.That(dockCount, Is.GreaterThanOrEqualTo(6), "Need shuttle docks + bay + ATS");
            Assert.That(consoleCount, Is.GreaterThanOrEqualTo(1));
            Assert.That(thrusterCount, Is.GreaterThanOrEqualTo(1));
            Assert.That(cargoBay, Is.True, "Cargo bay mini-station missing");
            Assert.That(ats, Is.True, "ATS mini-station missing");

            var atsHasBank = false;
            var sellCrateUnanchored = false;
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

            var crateQuery = server.EntMan.AllEntityQueryEnumerator<MetaDataComponent, TransformComponent>();
            while (crateQuery.MoveNext(out var crateUid, out var meta, out var crateXform))
            {
                if (crateXform.MapUid != mapXform.MapUid)
                    continue;
                if (meta.EntityPrototype?.ID is not ("CrateGenericSteel" or "CrateHydroponics"))
                    continue;
                if (!crateXform.Anchored)
                {
                    sellCrateUnanchored = true;
                    break;
                }
            }

            Assert.That(sellCrateUnanchored, Is.True, "Sell crates must be unanchored on ATS sell pallets");

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
            Assert.That(chefAtmos!.Simulated, Is.False, "Simplified roles should freeze grid atmos after fill");

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
            maps.UnloadTutorialMap(atmosMap);
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
            Assert.That(borg.Category, Is.EqualTo("Science"));
            Assert.That(borg.Name, Is.EqualTo("tutorial-job-borg-name"));
            Assert.That(Loc.GetString(borg.Name!), Is.EqualTo("Cyborg"));
            Assert.That(Sub(borg, "panel-open").Complete, Is.EqualTo(TutorialStepComplete.PlayerWiresPanelOpen));
            Assert.That(Sub(borg, "emagged").Complete, Is.EqualTo(TutorialStepComplete.SiliconSubverted));
            Assert.That(borg.Goals.Any(g => g.Id == "modules"));
            Assert.That(borg.Goals.Any(g => g.Id == "subversion"));
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
            Assert.That(ling.Stub, Is.True); //Wizden: temporarily greyed pending manual test
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
            Assert.That(vamp.Stub, Is.True); //Wizden: temporarily greyed pending manual test
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
            Assert.That(dragon.RoomTemplate,
                Is.EqualTo(new ProtoId<TutorialRoomTemplatePrototype>("TutorialSectionMaintAntag")));
            Assert.That(Sub(dragon, "use-breath").Complete, Is.EqualTo(TutorialStepComplete.ActionUsed));
            Assert.That(Sub(dragon, "use-breath").Entity, Is.EqualTo(new EntProtoId("ActionDragonsBreath")));
            Assert.That(Sub(dragon, "kill-dummy").Complete, Is.EqualTo(TutorialStepComplete.PracticeMobDead));
            Assert.That(Sub(dragon, "devour-human").Complete, Is.EqualTo(TutorialStepComplete.DragonDevoured));
            Assert.That(Sub(dragon, "open-rift").Complete, Is.EqualTo(TutorialStepComplete.MapHasEntity));
            Assert.That(Sub(dragon, "open-rift").Entity, Is.EqualTo(new EntProtoId("CarpRift")));
            Assert.That(dragon.PracticeSpawns.Count(p => p.Id == "TutorialPracticeMobVictim"), Is.GreaterThanOrEqualTo(2));
            Assert.That(proto.HasIndex(new EntProtoId("ActionSpawnRift")));
            Assert.That(proto.HasIndex(new EntProtoId("ActionDevour")));
            Assert.That(proto.HasIndex(new EntProtoId("MindRoleDragon")));
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
                "Space Dragon maint arena must load");
            Assert.That(dragonSpawn != default, Is.True);
            maps.UnloadTutorialMap(dragonMap);
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
    public async Task TutorialGuide_GivesItemAndBackNextRespectsSensors()
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
            var tutorial = server.System<TutorialServerRuleSystem>();
            tutorial.TrySelectRole(pair.Player!, "TutorialPassenger", confirmedStub: false);
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
            Assert.That(part.GoalIndex, Is.EqualTo(0));
            Assert.That(part.SubGoalIndex, Is.EqualTo(0));

            TutorialSessionData? session = null;
            var ruleQuery = entMan.EntityQueryEnumerator<TutorialServerRuleComponent>();
            while (ruleQuery.MoveNext(out _, out var ruleComp))
            {
                if (ruleComp.Sessions.TryGetValue(player.UserId, out session))
                    break;
            }

            Assert.That(session, Is.Not.Null, "Active tutorial session should track the player");
            Assert.That(session!.GuideUid, Is.Not.EqualTo(EntityUid.Invalid));
            Assert.That(entMan.EntityExists(session.GuideUid));
            Assert.That(entMan.HasComponent<TutorialGuideComponent>(session.GuideUid));
            Assert.That(entMan.GetComponent<MetaDataComponent>(session.GuideUid).EntityName,
                Is.EqualTo("Tutorial"));

            var guideSys = server.System<TutorialGuideSystem>();
            var tutorialSys = server.System<TutorialServerRuleSystem>();
            var guideComp = entMan.GetComponent<TutorialGuideComponent>(session.GuideUid);
            var guide = new Entity<TutorialGuideComponent>(session.GuideUid, guideComp);

            var state = guideSys.GetUiState(guide, mob);
            Assert.That(state.HasTutorial, Is.True);
            Assert.That(state.CanGoBack, Is.False);
            Assert.That(state.WaitingOnSensor, Is.True);
            Assert.That(state.CanGoNext, Is.False, "Next greyed out while waiting on ReachMarker sensor");
            Assert.That(guideSys.TryGoNext(guide, mob), Is.False);

            // Force-advance through welcome (8 sub-goals) into crowbar-door's second tip (pry).
            for (var i = 0; i < 9; i++)
                tutorialSys.AdvanceSubGoal(mob);

            Assert.That(entMan.TryGetComponent(mob, out part));
            Assert.That(part!.GoalIndex, Is.EqualTo(1), "Passenger advances to crowbar-door goal");
            Assert.That(part.SubGoalIndex, Is.EqualTo(1), "Pry tip");
            Assert.That(part.StepComplete, Is.EqualTo(TutorialStepComplete.InteractTargetTag));

            guide.Comp.ViewGoalIndex = 1;
            guide.Comp.ViewIndex = 0;
            state = guideSys.GetUiState(guide, mob);
            Assert.That(state.CanGoBack, Is.True, "Back enabled when browsing a prior tip");
            Assert.That(state.CanGoNext, Is.True, "Next allowed when viewing a previously passed step");
            Assert.That(guideSys.TryGoNext(guide, mob), Is.True);
            Assert.That(guide.Comp.ViewIndex, Is.EqualTo(1));

            state = guideSys.GetUiState(guide, mob);
            Assert.That(state.WaitingOnSensor, Is.True);
            Assert.That(state.CanGoNext, Is.False);
            Assert.That(guideSys.TryGoBack(guide, mob), Is.True);
            Assert.That(guide.Comp.ViewIndex, Is.EqualTo(0));

            // Back across goals returns toward welcome (lands on the last welcome tip).
            Assert.That(guideSys.TryGoBack(guide, mob), Is.True);
            Assert.That(guide.Comp.ViewGoalIndex, Is.EqualTo(0));
            Assert.That(guide.Comp.ViewIndex, Is.EqualTo(7), "Crossing back into welcome opens its last tip");
            while (guide.Comp.ViewIndex > 0)
                Assert.That(guideSys.TryGoBack(guide, mob), Is.True);

            state = guideSys.GetUiState(guide, mob);
            Assert.That(state.CanGoBack, Is.False);
            Assert.That(state.CanGoNext, Is.True);
            Assert.That(guideSys.TryGoNext(guide, mob), Is.True);
            Assert.That(guide.Comp.ViewGoalIndex, Is.EqualTo(0));
            Assert.That(guide.Comp.ViewIndex, Is.EqualTo(1));
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

        // Chef still auto-opens; Passenger defers until after welcome.
        await server.WaitPost(() =>
        {
            server.System<TutorialServerRuleSystem>()
                .TrySelectRole(pair.Player!, "TutorialChef", confirmedStub: false);
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
            var ui = server.System<UserInterfaceSystem>();
            Assert.That(ui.IsUiOpen(session.GuideUid, TutorialPromptUiKey.Key, mob), Is.True,
                "Prompt Bound UI should auto-open once at tutorial start for roles with AutoOpenGuide");
        });
    }

    [Test]
    public async Task TutorialPassenger_DefersGuideOpenUntilAfterWelcome()
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
            var ui = server.System<UserInterfaceSystem>();
            var tutorial = server.System<TutorialServerRuleSystem>();

            TutorialSessionData? session = null;
            var ruleQuery = entMan.EntityQueryEnumerator<TutorialServerRuleComponent>();
            while (ruleQuery.MoveNext(out _, out var ruleComp))
            {
                if (ruleComp.Sessions.TryGetValue(player.UserId, out session))
                    break;
            }

            Assert.That(session, Is.Not.Null);
            Assert.That(session!.GuideUid, Is.Not.EqualTo(EntityUid.Invalid));
            Assert.That(session.GuideAutoOpened, Is.False);
            Assert.That(ui.IsUiOpen(session.GuideUid, TutorialPromptUiKey.Key, mob), Is.False,
                "Passenger guide should stay closed during the NPC opening block");

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
            Assert.That(hands.GetHeldItem((mob, handsComp), leftHand), Is.EqualTo(session.GuideUid),
                "Tutorial tablet should occupy the left/off hand");
            Assert.That(rightHand, Is.Not.Null);
            Assert.That(hands.HandIsEmpty((mob, handsComp), rightHand!), Is.True,
                "Active right hand should stay free for pickup practice");
            Assert.That(hands.GetActiveHand((mob, handsComp)), Is.EqualTo(rightHand));

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
            // Welcome ends on drink-water (UseInHand). Auto-opening the Bound UI in that same
            // AdvanceSubGoal steals focus from the bottle — Passenger has no chamber pad, so the
            // tablet stays closed until the player activates it.
            Assert.That(session.GuideAutoOpened, Is.False);
            Assert.That(ui.IsUiOpen(session.GuideUid, TutorialPromptUiKey.Key, mob), Is.False,
                "Guide must not auto-open when welcome ends on a UseInHand (drink)");
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
            var tutorial = server.System<TutorialServerRuleSystem>();
            var hands = server.System<SharedHandsSystem>();
            var timing = server.ResolveDependency<IGameTiming>();

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
            var firstReminder = trainer.NextReminderAt;

            var interact = new InteractHandEvent(mob, trainerUid.Value);
            entMan.EventBus.RaiseLocalEvent(trainerUid.Value, interact);
            trainer = entMan.GetComponent<TutorialTrainerComponent>(trainerUid.Value);
            Assert.That(trainer.LastSpokenSubGoal, Is.EqualTo("meet-trainer"));
            Assert.That(trainer.NextReminderAt, Is.GreaterThan(firstReminder),
                "Hug/interact should reset the reminder timer");

            trainer.NextReminderAt = timing.CurTime - TimeSpan.FromSeconds(1);
        });

        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            var entMan = server.EntMan;
            var timing = server.ResolveDependency<IGameTiming>();
            EntityUid? trainerUid = null;
            TutorialTrainerComponent? trainer = null;
            var trainers = entMan.EntityQueryEnumerator<TutorialTrainerComponent>();
            while (trainers.MoveNext(out var uid, out var comp))
            {
                trainerUid = uid;
                trainer = comp;
                break;
            }

            Assert.That(trainerUid, Is.Not.Null);
            Assert.That(trainer, Is.Not.Null);
            Assert.That(trainer!.NextReminderAt, Is.GreaterThan(timing.CurTime),
                "Trainer should re-speak and schedule the next 10s reminder");
        });

        await server.WaitAssertion(() =>
        {
            var player = pair.Player!;
            var mob = player.AttachedEntity!.Value;
            var entMan = server.EntMan;
            var tutorial = server.System<TutorialServerRuleSystem>();
            var hands = server.System<SharedHandsSystem>();

            // Advance to drop-crowbar (meet -> pick -> drop).
            tutorial.AdvanceSubGoal(mob);
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

        await server.WaitPost(() =>
        {
            var mob = pair.Player!.AttachedEntity!.Value;
            EntityUid? chooseAction = null;
            foreach (var (actionUid, _) in actions.GetActions(mob))
            {
                if (server.EntMan.GetComponent<MetaDataComponent>(actionUid).EntityPrototype?.ID ==
                    "ActionTutorialChooseRole")
                {
                    chooseAction = actionUid;
                    break;
                }
            }

            Assert.That(chooseAction, Is.Not.Null);
            actions.PerformAction(mob, (chooseAction.Value, server.EntMan.GetComponent<ActionComponent>(chooseAction.Value)));
        });
        await pair.RunTicksSync(20);

        await server.WaitAssertion(() =>
        {
            var player = pair.Player!;
            Assert.That(player.AttachedEntity, Is.Not.Null);
            Assert.That(server.EntMan.HasComponent<GhostComponent>(player.AttachedEntity!.Value), Is.True,
                "Choose a tutorial from a living body should leave an observer");
            Assert.That(tutorial.IsPickerOpen(player), Is.True,
                "Choose a tutorial from a living body must open the role picker");
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
            Assert.That(session.GuideAutoOpened, Is.False,
                "Deferred guide must not open on the first Z that only opens the bottle");
            Assert.That(
                server.System<UserInterfaceSystem>()
                    .IsUiOpen(session.GuideUid, TutorialPromptUiKey.Key, mob),
                Is.False,
                "Tutorial prompt must stay closed when only opening the bottle");
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
            Assert.That(session.GuideAutoOpened, Is.False,
                "Drinking must not open the tutorial Bound UI (deferred open is pad-gated)");
            Assert.That(
                server.System<UserInterfaceSystem>()
                    .IsUiOpen(session.GuideUid, TutorialPromptUiKey.Key, mob),
                Is.False,
                "Tutorial prompt must stay closed after drinking");
        });
    }

    [Test]
    public async Task TutorialGuide_StuckHintDoesNotAdvanceCurriculum()
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
            var guide = new Entity<TutorialGuideComponent>(
                session!.GuideUid,
                entMan.GetComponent<TutorialGuideComponent>(session.GuideUid));

            Assert.That(entMan.TryGetComponent<TutorialParticipantComponent>(mob, out var part));
            Assert.That(part!.StepComplete, Is.EqualTo(TutorialStepComplete.ReachMarker));
            Assert.That(part.StuckHintText, Is.Not.Empty, "Passenger purple-X step should author a stuckHint");
            Assert.That(part.HintText, Is.Not.Empty);
            Assert.That(guideSys.TryGoNext(guide, mob), Is.False, "Sensor tip cannot be skipped with Next");

            var goalBefore = part.GoalIndex;
            var subBefore = part.SubGoalIndex;
            Assert.That(guideSys.TryShowStuckHint(mob), Is.True);
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
                .TrySelectRole(pair.Player!, "TutorialSurgeryStarlight", confirmedStub: true); //Wizden: stub greyed pending manual test
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
                .TrySelectRole(pair.Player!, "TutorialPassenger", confirmedStub: false);
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
            ui.CloseUi(session!.GuideUid, TutorialPromptUiKey.Key, mob);

            var guide = new Entity<TutorialGuideComponent>(
                session.GuideUid,
                entMan.GetComponent<TutorialGuideComponent>(session.GuideUid));

            Assert.That(entMan.TryGetComponent<TutorialParticipantComponent>(mob, out var part));
            Assert.That(part!.StepComplete, Is.EqualTo(TutorialStepComplete.ReachMarker));
            Assert.That(guideSys.TryGoNext(guide, mob), Is.False, "Closed UI cannot skip a sensor tip");
            Assert.That(ui.IsUiOpen(session.GuideUid, TutorialPromptUiKey.Key, mob), Is.False);

            // Sensor advance while closed should still move curriculum and resync when reopened.
            server.System<TutorialServerRuleSystem>().AdvanceSubGoal(mob);
            Assert.That(entMan.TryGetComponent(mob, out part));
            Assert.That(part!.GoalIndex, Is.EqualTo(0));
            Assert.That(part.SubGoalIndex, Is.EqualTo(1), "Advance from meet-trainer into pick-crowbar");

            ui.OpenUi(session.GuideUid, TutorialPromptUiKey.Key, mob);
            var state = guideSys.GetUiState(guide, mob);
            Assert.That(state.ViewGoalIndex, Is.EqualTo(part.GoalIndex));
            Assert.That(state.ProgressIndex, Is.EqualTo(part.SubGoalIndex));
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
    public async Task TutorialAtmosStamp_KeepsEastAxis()
    {
        await AssertSectionStampAxisAndForbiddenDoors(
            "TutorialSectionAtmos",
            TutorialRoomDoorSide.East,
            forbiddenDoorProtos: Array.Empty<string>(),
            requireVaults: true);
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
        string? forbiddenDoorSubstring = null)
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

            maps.UnloadTutorialMap(mapUid);
        });
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

            // Advance through Acknowledge / PilotShuttle until UndockShuttle is current.
            for (var i = 0; i < 20; i++)
            {
                if (!tutorial.TryGetCurrentSubGoal(mob, part!, out var sub))
                    break;
                if (sub.Complete == TutorialStepComplete.UndockShuttle)
                    break;
                if (sub.Complete is TutorialStepComplete.Acknowledge or TutorialStepComplete.PilotShuttle)
                    tutorial.AdvanceSubGoal(mob);
                else
                    break;
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

            var shuttle = server.EntMan.GetComponent<TransformComponent>(mob).GridUid;
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

            var shuttle = server.EntMan.GetComponent<TransformComponent>(mob).GridUid!.Value;
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
}
