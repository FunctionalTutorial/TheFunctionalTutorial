using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Content.IntegrationTests.Fixtures;
using Content.Server._Functional.TutorialServer;
using Content.Server.GameTicking;
using Content.Server.Interaction;
using Content.Server.Preferences.Managers;
using Content.Shared._Functional.TutorialServer;
using Content.Shared.Preferences;
using Content.Shared.Tag;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Hands.Components;
using NUnit.Framework;
using Robust.Shared.GameObjects;
using Robust.Shared.Localization;
using Robust.Shared.Utility;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._Functional.TutorialServer;

/// <summary>
/// Runs a real Items and Survival session far enough to prove the suite the player lands in carries
/// every drill target. A live session, not a map load: the props are spawned when the tutorial
/// starts. Its own fixture so <see cref="TutorialItemsTests"/> does not pay for the ticker.
/// </summary>
[TestFixture]
[TestOf(typeof(TutorialServerRuleSystem))]
public sealed class TutorialItemsSessionTests : GameTest
{
    public override PoolSettings PoolSettings => new()
    {
        Dirty = true,
        InLobby = true,
        DummyTicker = false,
        Connected = true,
    };

    private const string RoleId = "TutorialItems";

    /// <summary>Prototypes a drill names directly, so the suite is useless without them.</summary>
    private static readonly string[] RequiredProtos =
    [
        "TutorialWrench", "TutorialScrewdriver", "TutorialTrainingLantern", "TutorialTrainingDrink",
        "TutorialWehPlushie", "ClothingBeltUtility", "ClothingOuterSuitEmergency",
        "TutorialBreachCue", "TutorialBreachGate",
    ];

    /// <summary>
    /// Destinations meant to be invisible in play, so the lit-marker check skips them. Listed and
    /// not inferred, so hiding a marker stays a deliberate act.
    /// </summary>
    private static readonly string[] HiddenMarkers =
    [
        // "Cross the vacuum" already names the destination.
        "items-pad-vacuum",
        "items-pad-3",
    ];

    /// <summary>Tags a drill matches on, kept separate because they let the map swap the prop.</summary>
    private static readonly string[] RequiredTags =
    [
        "TutorialExamineTarget", "TutorialGearLocker", "TutorialDisposal", "TutorialPullCrate",
        "TutorialFirelock", "TutorialGirderTool", "TutorialGirder",
        // Tag rather than prototype: the belt drill accepts any crowbar, red one included.
        "Crowbar",
    ];

    /// <summary>
    /// Regression: watching <c>UserActivateInWorldEvent</c> on the player only fires when nothing
    /// consumed the keypress, and an opening locker consumes it, so chamber 2 dead-ended.
    /// </summary>
    [Test]
    public async Task TutorialItems_ActivatingTheLockerAdvancesTheDrill()
    {
        var pair = Pair;
        var server = pair.Server;
        var tutorial = server.System<TutorialServerRuleSystem>();
        var interaction = server.System<InteractionSystem>();
        var tags = server.System<TagSystem>();
        var entMan = server.EntMan;

        await StartSession();

        await server.WaitAssertion(() =>
        {
            var mob = pair.Player!.AttachedEntity!.Value;
            Assert.That(entMan.TryGetComponent<TutorialParticipantComponent>(mob, out var part), Is.True);

            // Skip to the drill rather than playing eleven sub-goals to reach it.
            for (var i = 0; i < 200; i++)
            {
                if (tutorial.TryGetCurrentSubGoal(mob, part!, out var current) && current.Id == "open-locker")
                    break;

                tutorial.AdvanceSubGoal(mob);
            }

            Assert.That(tutorial.TryGetCurrentSubGoal(mob, part!, out var sub), Is.True);
            Assert.That(sub.Id, Is.EqualTo("open-locker"), "never reached the activate drill");

            var locker = EntityUid.Invalid;
            var query = entMan.EntityQueryEnumerator<TransformComponent>();
            while (query.MoveNext(out var uid, out _))
            {
                if (!tags.HasTag(uid, "TutorialGearLocker"))
                    continue;

                locker = uid;
                break;
            }

            Assert.That(locker, Is.Not.EqualTo(EntityUid.Invalid), "no gear locker in the suite");

            // checkAccess:false stands in for walking the player over to it.
            interaction.InteractionActivate(mob, locker, checkAccess: false);

            Assert.That(tutorial.TryGetCurrentSubGoal(mob, part!, out var after), Is.True);
            Assert.That(after.Id, Is.EqualTo("take-can"),
                "opening the locker did not advance the drill");
        });
    }

    /// <summary>
    /// The mask-prep drill waits for an empty active hand, not a swap itself. A player who still
    /// holds something (a lantern, a leftover tool) must switch; a swap onto a full hand must not
    /// count.
    /// </summary>
    [Test]
    public async Task TutorialItems_EmptyActiveHandAdvancesTheMaskPrepDrill()
    {
        var pair = Pair;
        var server = pair.Server;
        var tutorial = server.System<TutorialServerRuleSystem>();
        var hands = server.System<SharedHandsSystem>();
        var entMan = server.EntMan;

        await StartSession();

        var mob = EntityUid.Invalid;
        var wrench = EntityUid.Invalid;

        await server.WaitAssertion(() =>
        {
            mob = pair.Player!.AttachedEntity!.Value;
            Assert.That(entMan.TryGetComponent<TutorialParticipantComponent>(mob, out var part), Is.True);

            for (var i = 0; i < 200; i++)
            {
                if (tutorial.TryGetCurrentSubGoal(mob, part!, out var current) && current.Id == "take-tool")
                    break;

                tutorial.AdvanceSubGoal(mob);
            }

            Assert.That(CurrentSubGoal(mob), Is.EqualTo("take-tool"), "never reached the first tool drill");

            var query = entMan.EntityQueryEnumerator<TransformComponent>();
            while (query.MoveNext(out var uid, out _))
            {
                if (entMan.GetComponentOrNull<MetaDataComponent>(uid)?.EntityPrototype?.ID != "TutorialWrench")
                    continue;

                wrench = uid;
                break;
            }

            Assert.That(wrench, Is.Not.EqualTo(EntityUid.Invalid), "no wrench in the suite");
        });

        await server.WaitPost(() => hands.TryPickupAnyHand(mob, wrench));
        await pair.RunTicksSync(5);
        Assert.That(CurrentSubGoal(mob), Is.EqualTo("swap-hands"));

        await server.WaitPost(() =>
        {
            if (!entMan.TryGetComponent<TutorialParticipantComponent>(mob, out var part))
                return;

            for (var i = 0; i < 200; i++)
            {
                if (tutorial.TryGetCurrentSubGoal(mob, part, out var current) &&
                    current.Id == "empty-hand-for-mask")
                    break;

                tutorial.AdvanceSubGoal(mob);
            }
        });
        await pair.RunTicksSync(5);

        Assert.That(CurrentSubGoal(mob), Is.EqualTo("empty-hand-for-mask"),
            "holding an item should keep the empty-hand drill from auto-completing");

        await server.WaitPost(() => hands.SwapHands(mob));
        await pair.RunTicksSync(5);
        Assert.That(CurrentSubGoal(mob), Is.EqualTo("take-mask"),
            "switching to an empty hand did not advance the mask-prep drill");
    }

    [Test]
    public async Task TutorialItems_SuiteProvidesEveryDrillProp()
    {
        var pair = Pair;
        var server = pair.Server;
        var tutorial = server.System<TutorialServerRuleSystem>();
        var tags = server.System<TagSystem>();
        var entMan = server.EntMan;

        await StartSession();

        await server.WaitAssertion(() =>
        {
            var player = pair.Player!;
            TutorialSessionData? session = null;
            var ruleQuery = entMan.EntityQueryEnumerator<TutorialServerRuleComponent>();
            while (ruleQuery.MoveNext(out _, out var ruleComp))
            {
                if (ruleComp.Sessions.TryGetValue(player.UserId, out session))
                    break;
            }

            Assert.That(session, Is.Not.Null);
            Assert.That(session!.SelectedRoleId, Is.EqualTo(RoleId));
            Assert.That(session.State, Is.EqualTo(TutorialSessionState.InTutorial));

            var gridUid = session.GridUid;
            Assert.That(gridUid, Is.Not.EqualTo(EntityUid.Invalid), "tutorial map never loaded");

            var protoIds = new HashSet<string>();
            var tagged = new HashSet<string>();
            var markers = new HashSet<string>();
            var cuedSubGoals = new HashSet<string>();
            var holoRooms = new List<int>();
            var sensorTargetTags = new HashSet<string>();
            var girderAnchored = false;
            var girderTools = 0;
            var unlitMarkers = new HashSet<string>();

            var query = entMan.EntityQueryEnumerator<TransformComponent>();
            while (query.MoveNext(out var uid, out var xform))
            {
                if (xform.GridUid != gridUid)
                    continue;

                if (entMan.GetComponentOrNull<MetaDataComponent>(uid)?.EntityPrototype?.ID is { } id)
                    protoIds.Add(id);

                var isSensorTarget = entMan.HasComponent<TutorialSensorTargetComponent>(uid);
                foreach (var tag in RequiredTags)
                {
                    if (!tags.HasTag(uid, tag))
                        continue;

                    tagged.Add(tag);
                    if (isSensorTarget)
                        sensorTargetTags.Add(tag);
                }

                if (tags.HasTag(uid, "TutorialGirder") && xform.Anchored)
                    girderAnchored = true;

                if (tags.HasTag(uid, "TutorialGirderTool"))
                    girderTools++;

                if (entMan.TryGetComponent<TutorialStepMarkerComponent>(uid, out var marker))
                {
                    markers.Add(marker.MarkerId);
                    if (!entMan.HasComponent<Robust.Server.GameObjects.PointLightComponent>(uid))
                        unlitMarkers.Add(marker.MarkerId);
                }

                if (entMan.TryGetComponent<TutorialCueComponent>(uid, out var cue))
                    cuedSubGoals.Add(cue.SubGoalId);

                if (entMan.TryGetComponent<TutorialHoloPointComponent>(uid, out var pad))
                    holoRooms.Add(pad.Room);
            }

            var role = server.ProtoMan.Index<TutorialRolePrototype>(RoleId);

            Assert.Multiple(() =>
            {
                foreach (var proto in RequiredProtos)
                    Assert.That(protoIds, Does.Contain(proto), $"nothing in the suite is a {proto}");

                // A loose girder satisfies `unbolt-girder` on its first poll and skips the lesson.
                Assert.That(girderAnchored, Is.True, "the girder is not anchored, so unbolting it teaches nothing");

                // `take-other-tool` is HoldTag with minCount 2 and cannot be completed otherwise.
                Assert.That(girderTools, Is.GreaterThanOrEqualTo(2), "fewer than two tagged tools in the suite");

                // chamberEntryPads is off for this role, so no chamber should carry one.
                Assert.That(markers.Where(m => m.StartsWith("chamber-")), Is.Empty,
                    "chamber entry pads were spawned despite the role opting out");

                // A ReachMarker the player cannot see is a drill with no destination: MarkerBase
                // hides its sprite outside mapping mode, leaving no square to drag the crate to.
                Assert.That(unlitMarkers.Except(HiddenMarkers), Is.Empty,
                    "these ReachMarker destinations have no light and may be invisible in play");

                foreach (var tag in RequiredTags)
                    Assert.That(tagged, Does.Contain(tag), $"nothing in the suite is tagged {tag}");

                foreach (var sub in role.Goals.SelectMany(g => g.SubGoals))
                {
                    if (!string.IsNullOrEmpty(sub.Marker))
                        Assert.That(markers, Does.Contain(sub.Marker), $"marker '{sub.Marker}' is not placed");
                }

                // Target-side sensors only see entities carrying TutorialSensorTarget, and
                // forgetting it is silent: the prop works, the objective just never ticks.
                foreach (var sub in role.Goals.SelectMany(g => g.SubGoals))
                {
                    if (sub.Complete is not (TutorialStepComplete.ExamineTag or TutorialStepComplete.ActivateInWorldTag))
                        continue;

                    Assert.That(sensorTargetTags, Does.Contain(sub.Tag),
                        $"{sub.Id}: nothing tagged '{sub.Tag}' carries TutorialSensorTarget, so the drill can never complete");
                }

                Assert.That(cuedSubGoals, Does.Contain("light-lantern"), "the blackout cue is not placed");
                Assert.That(cuedSubGoals, Does.Contain("breach-brief"), "the breach panel is not placed");

                // At least one pad per chamber, each knowing which chamber it is in, or she
                // projects into the room being left. Several is fine: TryFindHoloPoint takes the
                // nearest, so she moves up a long chamber as the player works through it.
                Assert.That(holoRooms.Distinct().OrderBy(r => r).ToList(),
                    Is.EqualTo(Enumerable.Range(0, role.Goals.Count).ToList()),
                    "every chamber needs a holopad, and no pad may name a chamber that does not exist");
            });
        });
    }

    /// <summary>
    /// Every beat the player has to act on leaves something in the banner: the key where one is
    /// taught, the objective line otherwise. Beats that end themselves leave it blank.
    /// </summary>
    [Test]
    public async Task TutorialItems_BannerMatchesWhatEachBeatAsksFor()
    {
        var pair = Pair;
        var server = pair.Server;
        var tutorial = server.System<TutorialServerRuleSystem>();

        await StartSession();

        await server.WaitAssertion(() =>
        {
            TutorialCurriculumAssertions.BannerMatchesWhatTheBeatAsksFor(
                tutorial,
                server.EntMan,
                pair.Player!.AttachedEntity!.Value,
                server.ProtoMan.Index<TutorialRolePrototype>(RoleId));
        });
    }

    /// <summary>
    /// The banner waits her out however long her script is. The safety net that stops a blank
    /// corner only covers a coach who has not started.
    /// </summary>
    /// <remarks>
    /// Regression: the net was measured from the start of the beat, so every segment longer than
    /// it put the banner up in the middle of what she was saying.
    /// </remarks>
    [Test]
    public async Task TutorialItems_BannerWaitsOutASegmentLongerThanTheSafetyNet()
    {
        var pair = Pair;
        var server = pair.Server;
        var tutorial = server.System<TutorialServerRuleSystem>();
        var trainers = server.System<TutorialTrainerSystem>();

        await StartSession();

        var speaking = TutorialCoachSpeech.Done;
        for (var i = 0; i < 60 && speaking != TutorialCoachSpeech.Speaking; i++)
        {
            await pair.RunTicksSync(5);
            await server.WaitPost(() => speaking = ResolveCoachSpeech(tutorial, trainers));
        }

        Assert.That(speaking, Is.EqualTo(TutorialCoachSpeech.Speaking),
            "the coach never started her opening segment, so there is nothing to wait out");

        // Age the beat past the safety net rather than sitting through it, leaving her mid-script.
        await server.WaitPost(() =>
        {
            var mob = pair.Player!.AttachedEntity!.Value;
            Assert.That(tutorial.TryGetSession(mob, out var session), Is.True);
            session.SubGoalStartedAt -= TimeSpan.FromMinutes(1);
        });

        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            var mob = pair.Player!.AttachedEntity!.Value;

            Assert.That(ResolveCoachSpeech(tutorial, trainers), Is.EqualTo(TutorialCoachSpeech.Speaking),
                "she finished early, so the beat is no longer the one under test");
            Assert.That(tutorial.HasPendingControlHint(mob), Is.True,
                "the banner went up in the middle of her script");
        });
    }

    /// <summary>Where the player's coach is in the script for their current beat.</summary>
    private TutorialCoachSpeech ResolveCoachSpeech(
        TutorialServerRuleSystem tutorial,
        TutorialTrainerSystem trainers)
    {
        var mob = Pair.Player?.AttachedEntity;
        if (mob is not { } player ||
            !Server.EntMan.TryGetComponent<TutorialParticipantComponent>(player, out var part) ||
            !tutorial.TryGetSession(player, out var session) ||
            !tutorial.TryGetCurrentSubGoal(player, part, out var sub))
        {
            return TutorialCoachSpeech.Done;
        }

        return trainers.ResolveSegmentState(session.MentorUid, sub.Id);
    }

    /// <summary>
    /// Whatever the banner says goes into chat as well, so an instruction the player looked away
    /// from can still be scrolled back to. Only what the banner says: never twice running, never
    /// the sign-off that <c>CompleteTutorial</c> writes to chat itself, and never a beat that ends
    /// on its own and so never puts anything on screen to repeat.
    /// </summary>
    [Test]
    public async Task TutorialItems_TheBannerIsRepeatedInChat()
    {
        var pair = Pair;
        var server = pair.Server;
        var client = pair.Client;
        var tutorial = server.System<TutorialServerRuleSystem>();

        await StartSession();

        var recorder = client.System<TutorialTipChatRecorder>();
        await client.WaitPost(() => recorder.Received.Clear());

        // Release each beat's banner rather than sitting through her lines for all of them.
        for (var i = 0; i < 8; i++)
        {
            await server.WaitPost(() =>
            {
                var mob = pair.Player!.AttachedEntity!.Value;
                tutorial.ShowPendingControlHint(mob);
                tutorial.AdvanceSubGoal(mob);
            });

            await pair.RunTicksSync(5);
        }

        var chatted = new List<string>();
        await client.WaitPost(() => chatted.AddRange(recorder.Received));

        await server.WaitAssertion(() =>
        {
            // Only what a beat actually banners. A beat that ends itself shows nothing, so its
            // objective line reaching chat means chat is carrying what the player never saw.
            var role = server.ProtoMan.Index<TutorialRolePrototype>(RoleId);
            var banners = role.Goals
                .SelectMany(g => g.SubGoals)
                .Where(sub => sub.Complete != TutorialStepComplete.Acknowledge || sub.AutoAdvanceSeconds == null)
                .Select(sub => Loc.GetString(string.IsNullOrEmpty(sub.ControlHint) ? sub.Text : sub.ControlHint))
                .ToHashSet();

            Assert.That(chatted, Is.Not.Empty, "the banner was never repeated into chat");

            Assert.Multiple(() =>
            {
                foreach (var line in chatted)
                {
                    Assert.That(banners, Does.Contain(line),
                        $"chat carried '{line}', which no beat's banner says");

                    // The chat box parses what it is given with AddMarkupOrThrow, so a line that
                    // does not survive the round trip takes the whole box down with it.
                    Assert.DoesNotThrow(
                        () => FormattedMessage.FromMarkupOrThrow(
                            FormattedMessage.FromMarkupPermissive(line).ToMarkup()),
                        $"'{line}' does not survive the trip to chat as markup");
                }

                for (var i = 1; i < chatted.Count; i++)
                {
                    Assert.That(chatted[i], Is.Not.EqualTo(chatted[i - 1]),
                        $"chat said '{chatted[i]}' twice running");
                }
            });
        });
    }

    /// <summary>
    /// Puts the connected player into a running Items and Survival session.
    /// </summary>
    /// <summary>
    /// Walks chamber 1 end to end, since every sensor it leans on is new. Drives the states
    /// directly: under test is whether the sensors notice, not whether construction works.
    /// </summary>
    [Test]
    public async Task TutorialItems_GirderChamberAdvancesThroughEverySensor()
    {
        var pair = Pair;
        var server = pair.Server;
        var tutorial = server.System<TutorialServerRuleSystem>();
        var hands = server.System<SharedHandsSystem>();
        var xforms = server.System<SharedTransformSystem>();
        var tags = server.System<TagSystem>();
        var entMan = server.EntMan;

        await StartSession();

        var mob = EntityUid.Invalid;
        var wrench = EntityUid.Invalid;
        var driver = EntityUid.Invalid;
        var girder = EntityUid.Invalid;

        await server.WaitAssertion(() =>
        {
            mob = pair.Player!.AttachedEntity!.Value;
            Assert.That(entMan.TryGetComponent<TutorialParticipantComponent>(mob, out var part), Is.True);

            for (var i = 0; i < 200; i++)
            {
                if (tutorial.TryGetCurrentSubGoal(mob, part!, out var current) && current.Id == "take-tool")
                    break;

                tutorial.AdvanceSubGoal(mob);
            }

            Assert.That(CurrentSubGoal(mob), Is.EqualTo("take-tool"), "never reached the first tool drill");

            var query = entMan.EntityQueryEnumerator<TransformComponent>();
            while (query.MoveNext(out var uid, out _))
            {
                var id = entMan.GetComponentOrNull<MetaDataComponent>(uid)?.EntityPrototype?.ID;
                if (id == "TutorialWrench")
                    wrench = uid;
                else if (id == "TutorialScrewdriver")
                    driver = uid;
                else if (tags.HasTag(uid, "TutorialGirder"))
                    girder = uid;
            }

            Assert.Multiple(() =>
            {
                Assert.That(wrench, Is.Not.EqualTo(EntityUid.Invalid), "no wrench in the suite");
                Assert.That(driver, Is.Not.EqualTo(EntityUid.Invalid), "no screwdriver in the suite");
                Assert.That(girder, Is.Not.EqualTo(EntityUid.Invalid), "no girder in the suite");
            });
        });

        // Either tool satisfies the first drill; take the wrench.
        await server.WaitPost(() => hands.TryPickupAnyHand(mob, wrench));
        await pair.RunTicksSync(5);
        Assert.That(CurrentSubGoal(mob), Is.EqualTo("swap-hands"), "picking up a tool did not advance");

        // A tick has to land first so the swap sensor has a baseline to compare against.
        await pair.RunTicksSync(5);
        await server.WaitPost(() => hands.SwapHands(mob));
        await pair.RunTicksSync(5);
        Assert.That(CurrentSubGoal(mob), Is.EqualTo("take-other-tool"), "swapping hands did not advance");

        await server.WaitPost(() => hands.TryPickupAnyHand(mob, driver));
        await pair.RunTicksSync(5);
        Assert.That(CurrentSubGoal(mob), Is.EqualTo("brief-girder"),
            "holding both tools did not satisfy the minCount drill");

        // Narration beat, on a timer and gated on the coach; skip it rather than wait her out.
        await server.WaitPost(() => tutorial.AdvanceSubGoal(mob));
        await pair.RunTicksSync(5);
        Assert.That(CurrentSubGoal(mob), Is.EqualTo("wrench-active"));

        await server.WaitPost(() =>
        {
            var handId = FindHandHolding(mob, wrench);
            hands.SetActiveHand(mob, handId);
        });
        await pair.RunTicksSync(5);
        Assert.That(CurrentSubGoal(mob), Is.EqualTo("unbolt-girder"),
            "making the wrench active did not advance");

        await server.WaitPost(() => xforms.Unanchor(girder, entMan.GetComponent<TransformComponent>(girder)));
        await pair.RunTicksSync(5);
        Assert.That(CurrentSubGoal(mob), Is.EqualTo("driver-active"), "unbolting the girder did not advance");

        await server.WaitPost(() =>
        {
            var handId = FindHandHolding(mob, driver);
            hands.SetActiveHand(mob, handId);
        });
        await pair.RunTicksSync(5);
        Assert.That(CurrentSubGoal(mob), Is.EqualTo("strip-girder"),
            "making the screwdriver active did not advance");

        // Deconstruction deletes the girder, which is exactly what TargetAbsent watches for.
        await server.WaitPost(() => entMan.DeleteEntity(girder));
        await pair.RunTicksSync(5);
        Assert.That(CurrentSubGoal(mob), Is.EqualTo("drop-tools"), "removing the girder did not advance");

        await server.WaitPost(() =>
        {
            hands.TryDrop(mob, wrench);
            hands.TryDrop(mob, driver);
        });
        await pair.RunTicksSync(5);
        // Straight into chamber 2's first drill; an auto-inserted pad beat would show up here.
        Assert.That(CurrentSubGoal(mob), Is.EqualTo("examine-plaque"),
            "emptying both hands did not lead straight into the next chamber's own first drill");
    }

    private string? CurrentSubGoal(EntityUid mob)
    {
        var server = Server;
        var tutorial = server.System<TutorialServerRuleSystem>();
        if (!server.EntMan.TryGetComponent<TutorialParticipantComponent>(mob, out var part))
            return null;

        return tutorial.TryGetCurrentSubGoal(mob, part, out var sub) ? sub.Id : null;
    }

    private string? FindHandHolding(EntityUid mob, EntityUid item)
    {
        var server = Server;
        var hands = server.System<SharedHandsSystem>();
        if (!server.EntMan.TryGetComponent<HandsComponent>(mob, out var comp))
            return null;

        foreach (var handId in comp.Hands.Keys)
        {
            if (hands.TryGetHeldItem((mob, comp), handId, out var held) && held == item)
                return handId;
        }

        return null;
    }

    /// <summary>
    /// Takes the girder apart before being asked to, which a player with both tools can do.
    /// Regression: waiting for a tagged entity with Anchored false matched nothing once it was
    /// gone, and the drill sat there asking for something that no longer existed.
    /// </summary>
    [Test]
    public async Task TutorialItems_DeconstructingTheGirderEarlyDoesNotStrand()
    {
        var pair = Pair;
        var server = pair.Server;
        var tutorial = server.System<TutorialServerRuleSystem>();
        var hands = server.System<SharedHandsSystem>();
        var tags = server.System<TagSystem>();
        var entMan = server.EntMan;

        await StartSession();

        var mob = EntityUid.Invalid;
        var wrench = EntityUid.Invalid;
        var driver = EntityUid.Invalid;

        await server.WaitAssertion(() =>
        {
            mob = pair.Player!.AttachedEntity!.Value;
            Assert.That(entMan.TryGetComponent<TutorialParticipantComponent>(mob, out var part), Is.True);

            for (var i = 0; i < 200; i++)
            {
                if (tutorial.TryGetCurrentSubGoal(mob, part!, out var current) && current.Id == "take-tool")
                    break;

                tutorial.AdvanceSubGoal(mob);
            }

            Assert.That(CurrentSubGoal(mob), Is.EqualTo("take-tool"));

            var girder = EntityUid.Invalid;
            var query = entMan.EntityQueryEnumerator<TransformComponent>();
            while (query.MoveNext(out var uid, out _))
            {
                var id = entMan.GetComponentOrNull<MetaDataComponent>(uid)?.EntityPrototype?.ID;
                if (id == "TutorialWrench")
                    wrench = uid;
                else if (id == "TutorialScrewdriver")
                    driver = uid;
                else if (tags.HasTag(uid, "TutorialGirder"))
                    girder = uid;
            }

            // The whole job, done before she has finished asking for the first tool.
            entMan.DeleteEntity(girder);
        });

        await pair.RunTicksSync(5);

        await server.WaitPost(() =>
        {
            hands.TryPickupAnyHand(mob, wrench);
        });
        await pair.RunTicksSync(5);
        await server.WaitPost(() => hands.SwapHands(mob));
        await pair.RunTicksSync(5);
        await server.WaitPost(() => hands.TryPickupAnyHand(mob, driver));
        await pair.RunTicksSync(5);

        // brief-girder is a timed narration beat; skip it rather than wait her out.
        Assert.That(CurrentSubGoal(mob), Is.EqualTo("brief-girder"));
        await server.WaitPost(() => tutorial.AdvanceSubGoal(mob));
        await pair.RunTicksSync(5);

        await server.WaitPost(() =>
        {
            var handId = FindHandHolding(mob, wrench);
            hands.SetActiveHand(mob, handId);
        });
        await pair.RunTicksSync(10);

        // unbolt-girder has nothing left to unbolt and should fall through on its own.
        Assert.That(CurrentSubGoal(mob), Is.EqualTo("driver-active"),
            "unbolt-girder stranded the player after an early deconstruction");

        // Selecting the screwdriver is still real; strip-girder is what should let go.
        await server.WaitPost(() =>
        {
            var handId = FindHandHolding(mob, driver);
            hands.SetActiveHand(mob, handId);
        });
        await pair.RunTicksSync(10);

        Assert.That(CurrentSubGoal(mob), Is.EqualTo("drop-tools"),
            "strip-girder stranded the player after an early deconstruction");

        await server.WaitPost(() =>
        {
            hands.TryDrop(mob, wrench);
            hands.TryDrop(mob, driver);
        });
        await pair.RunTicksSync(5);
        Assert.That(CurrentSubGoal(mob), Is.EqualTo("examine-plaque"));
    }

    /// <summary>
    /// True once the rule has accepted a role for this player <i>in the current round</i>. The grid
    /// liveness check is the point: the pool reuses the user id, so a stale session still carries a
    /// role id and taking it at face value means never issuing the real selection.
    /// </summary>
    private bool HasLiveSelection()
    {
        var server = Server;
        if (Pair.Player?.UserId is not { } userId)
            return false;

        var query = server.EntMan.EntityQueryEnumerator<TutorialServerRuleComponent>();
        while (query.MoveNext(out _, out var rule))
        {
            if (!rule.Sessions.TryGetValue(userId, out var session) || session.SelectedRoleId == null)
                continue;

            if (session.GridUid != EntityUid.Invalid && !server.EntMan.Deleted(session.GridUid))
                return true;
        }

        return false;
    }

    private async Task StartSession()
    {
        var pair = Pair;
        var server = pair.Server;
        var ticker = server.System<GameTicker>();
        var tutorial = server.System<TutorialServerRuleSystem>();

        // This curriculum is species-gated, and the pooled player's saved character is not ours to
        // assume. A Vox one makes TrySelectRole refuse every call, and the loop below then burns
        // its whole budget on a role the rule was never going to accept.
        await server.WaitPost(() => server
            .ResolveDependency<IServerPreferencesManager>()
            .SetProfile(pair.Player!.UserId, 0, new HumanoidCharacterProfile()).Wait());

        await server.WaitPost(() =>
        {
            ticker.SetGamePreset("TutorialServer");
            ticker.StartRound();
        });
        await pair.RunTicksSync(10);

        // Only if starting the round did not already bring one: two TutorialServer rules leaves
        // the player registered in neither, which no amount of retrying recovers from.
        await server.WaitPost(() =>
        {
            var query = server.EntMan.EntityQueryEnumerator<TutorialServerRuleComponent>();
            if (!query.MoveNext(out _, out _))
                ticker.StartGameRule("TutorialServer", out _);
        });
        await pair.RunTicksSync(5);

        // Generous budget: the loop exits as soon as the player is in. Retry rather than fire
        // once, since the rule only accepts a selection after the session registers, which takes a
        // variable number of ticks. Guarded on the rule's state so it cannot reload the map.
        var ready = false;
        for (var i = 0; i < 150 && !ready; i++)
        {
            await server.WaitPost(() =>
            {
                if (pair.Player?.AttachedEntity is { } mob &&
                    server.EntMan.HasComponent<TutorialParticipantComponent>(mob))
                {
                    ready = true;
                    return;
                }

                if (!HasLiveSelection())
                    tutorial.TrySelectRole(pair.Player!, RoleId, confirmedStub: false);
            });

            await pair.RunTicksSync(10);
        }

        // Report what the rule thought: "never attached" has at least three different causes.
        var diagnosis = "no session for this player at all";
        await server.WaitPost(() =>
        {
            var attached = pair.Player?.AttachedEntity;
            var query = server.EntMan.EntityQueryEnumerator<TutorialServerRuleComponent>();
            var rules = 0;
            while (query.MoveNext(out _, out var rule))
            {
                rules++;
                if (pair.Player?.UserId is not { } uid || !rule.Sessions.TryGetValue(uid, out var session))
                    continue;

                diagnosis = $"role={session.SelectedRoleId ?? "<none>"} state={session.State} " +
                            $"grid={session.GridUid} gridDeleted={server.EntMan.Deleted(session.GridUid)} " +
                            $"attached={attached?.ToString() ?? "<none>"}";
                return;
            }

            var blocked = tutorial.IsRoleBlockedForPlayer(
                pair.Player!,
                server.ProtoMan.Index<TutorialRolePrototype>(RoleId));
            diagnosis += $" (rules={rules}, attached={attached?.ToString() ?? "<none>"}, " +
                         $"speciesBlocked={blocked})";
        });

        Assert.That(ready, Is.True, $"the player never ended up attached to a tutorial participant: {diagnosis}");
    }
}
