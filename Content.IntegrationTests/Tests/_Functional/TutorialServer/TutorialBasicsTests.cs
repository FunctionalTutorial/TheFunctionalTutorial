using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Content.IntegrationTests.Fixtures;
using Content.Server._Functional.TutorialServer;
using Content.Server.GameTicking;
using Content.Server.NPC.HTN;
using Content.Server.Power.Components;
using Content.Shared._Functional.TutorialServer;
using Content.Shared.Holopad;
using Content.Shared.Doors.Components;
using Content.Shared.Movement.Components;
using Content.Shared.Tag;
using Robust.Shared.GameObjects;
using Robust.Shared.Localization;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._Functional.TutorialServer;

/// <summary>
/// Guards the Basic Controls curriculum's three-channel contract: the banner names the control
/// and nothing else, the checklist names the objective, and the coach says everything else in
/// character. Also pins the "keys come from the player's bindings" rule.
/// </summary>
[TestFixture]
[TestOf(typeof(TutorialGoalSensorSystem))]
public sealed class TutorialBasicsTests : GameTest
{
    /// <summary>
    /// Session-based tests below need a real ticker and a connected player, not the dummy ticker.
    /// </summary>
    public override PoolSettings PoolSettings => new()
    {
        Dirty = true,
        InLobby = true,
        DummyTicker = false,
        Connected = true,
    };

    private const string RoleId = "TutorialBasics";
    private const string MentorId = "TutorialHoloMentor";

    /// <summary>
    /// Default bindings we must never bake into a locale string, because players rebind them.
    /// </summary>
    private static readonly string[] HardcodedKeyNames =
    [
        "WASD", "W A S D", "Shift", "Numpad", "numpad key",
    ];

    [Test]
    public async Task TutorialBasics_CurriculumResolvesEveryLocaleString()
    {
        var server = Pair.Server;
        var protos = server.ProtoMan;

        await server.WaitAssertion(() =>
        {
            Assert.That(protos.TryIndex<TutorialRolePrototype>(RoleId, out var role), Is.True);
            Assert.That(role!.Goals, Is.Not.Empty);

            Assert.Multiple(() =>
            {
                Assert.That(Loc.TryGetString(role.Name!, out _), Is.True, $"missing {role.Name}");

                foreach (var goal in role.Goals)
                {
                    Assert.That(Loc.TryGetString(goal.Title, out _), Is.True, $"missing {goal.Title}");

                    foreach (var sub in goal.SubGoals)
                    {
                        Assert.That(Loc.TryGetString(sub.Text, out _), Is.True, $"missing {sub.Text}");

                        if (!string.IsNullOrEmpty(sub.Hint))
                            Assert.That(Loc.TryGetString(sub.Hint, out _), Is.True, $"missing {sub.Hint}");

                        if (!string.IsNullOrEmpty(sub.StuckHint))
                            Assert.That(Loc.TryGetString(sub.StuckHint, out _), Is.True, $"missing {sub.StuckHint}");

                        if (!string.IsNullOrEmpty(sub.ControlHint))
                            Assert.That(Loc.TryGetString(sub.ControlHint, out _), Is.True, $"missing {sub.ControlHint}");
                    }
                }
            });
        });
    }

    /// <summary>
    /// Control hints are the only channel allowed to mention keys, and they must resolve the
    /// player's real bindings rather than assuming a default layout.
    /// </summary>
    [Test]
    public async Task TutorialBasics_ControlHintsUseKeybindMarkupNotLiteralKeys()
    {
        var server = Pair.Server;
        var protos = server.ProtoMan;

        await server.WaitAssertion(() =>
        {
            Assert.That(protos.TryIndex<TutorialRolePrototype>(RoleId, out var role), Is.True);

            var checkedAny = false;

            Assert.Multiple(() =>
            {
                foreach (var sub in role!.Goals.SelectMany(g => g.SubGoals))
                {
                    if (string.IsNullOrEmpty(sub.ControlHint))
                        continue;

                    checkedAny = true;
                    var text = Loc.GetString(sub.ControlHint);

                    foreach (var literal in HardcodedKeyNames)
                    {
                        Assert.That(text, Does.Not.Contain(literal),
                            $"{sub.ControlHint} hardcodes '{literal}'; use [keybind=\"...\"] markup instead");
                    }

                    // Every keybind tag must name a function, i.e. [keybind="Something"].
                    foreach (Match match in Regex.Matches(text, @"\[keybind[^\]]*\]"))
                    {
                        Assert.That(match.Value, Does.Match("^\\[keybind=\"[A-Za-z0-9]+\"\\]$"),
                            $"{sub.ControlHint} has malformed keybind markup: {match.Value}");
                    }
                }
            });

            Assert.That(checkedAny, Is.True, "curriculum authored no control hints at all");
        });
    }

    /// <summary>
    /// The coach falls back to reading the objectives checklist aloud when a sub-goal has no
    /// override, which would break the in-character rule. Every sub-goal needs a line.
    /// </summary>
    [Test]
    public async Task TutorialBasics_CoachHasAnInCharacterLineForEverySubGoal()
    {
        var server = Pair.Server;
        var protos = server.ProtoMan;

        await server.WaitAssertion(() =>
        {
            Assert.That(protos.TryIndex<TutorialRolePrototype>(RoleId, out var role), Is.True);
            Assert.That(protos.TryIndex<EntityPrototype>(MentorId, out var mentor), Is.True);
            Assert.That(mentor!.TryGetComponent<TutorialTrainerComponent>(out var trainer), Is.True);

            var lines = trainer!.Lines.ToLookup(l => l.SubGoalId, l => l.Dialogue);

            Assert.Multiple(() =>
            {
                foreach (var sub in role!.Goals.SelectMany(g => g.SubGoals))
                {
                    Assert.That(lines.Contains(sub.Id), Is.True,
                        $"sub-goal '{sub.Id}' has no coach line; N.A.N.C.I. would read the checklist aloud");

                    foreach (var dialogue in lines[sub.Id])
                    {
                        var text = Loc.GetString(dialogue);
                        Assert.That(string.IsNullOrWhiteSpace(text), Is.False, $"missing {dialogue}");
                        // One sentence per bubble, and no em dashes.
                        Assert.That(text, Does.Not.Contain("\u2014"), $"{dialogue} uses an em dash");
                    }
                }
            });
        });
    }

    /// <summary>
    /// The coach is a projection, not a walker: no HTN, no mover components, and a role that
    /// routes through <see cref="TutorialHoloMentorSystem"/> instead of the follow system.
    /// </summary>
    [Test]
    public async Task TutorialBasics_MentorIsProjectedRatherThanWalked()
    {
        var server = Pair.Server;
        var protos = server.ProtoMan;

        await server.WaitAssertion(() =>
        {
            Assert.That(protos.TryIndex<TutorialRolePrototype>(RoleId, out var role), Is.True);
            Assert.That(protos.TryIndex<EntityPrototype>(MentorId, out var mentor), Is.True);

            Assert.Multiple(() =>
            {
                Assert.That(role!.MentorMode, Is.EqualTo(TutorialMentorMode.Holopad));
                Assert.That(role.MentorFollows, Is.False);
                Assert.That(role.MentorEntity?.Id, Is.EqualTo(MentorId));
                Assert.That(role.AutoOpenGuide, Is.False, "the banner and coach replace the tablet here");

                Assert.That(mentor!.TryGetComponent<HolopadHologramComponent>(out _), Is.True);
                Assert.That(mentor.TryGetComponent<TutorialTrainerComponent>(out _), Is.True);
                Assert.That(mentor.TryGetComponent<HTNComponent>(out _), Is.False, "hologram must not pathfind");
                Assert.That(mentor.TryGetComponent<InputMoverComponent>(out _), Is.False, "hologram must not walk");
            });
        });
    }

    /// <summary>
    /// A player who has never played should land on the controls tutorial first.
    /// </summary>
    [Test]
    public async Task TutorialBasics_SortsFirstInRolePicker()
    {
        var server = Pair.Server;
        var tutorial = server.System<TutorialServerRuleSystem>();

        await server.WaitAssertion(() =>
        {
            var entries = tutorial.BuildPickerEntries();
            Assert.That(entries, Is.Not.Empty);
            Assert.That(entries[0].RoleId, Is.EqualTo(RoleId));
            Assert.That(entries[0].Stub, Is.False);
        });
    }

    /// <summary>
    /// Every marker and tag a drill names has to be somewhere on the map the player lands in, or
    /// that drill sits there with nothing in the world able to complete it.
    /// </summary>
    [Test]
    public async Task TutorialBasics_MapCarriesEveryDrillTarget()
    {
        var server = Pair.Server;
        var maps = server.System<TutorialMapSystem>();
        var tags = server.System<TagSystem>();
        var entMan = server.EntMan;
        var protos = server.ProtoMan;

        await server.WaitAssertion(() =>
        {
            var role = protos.Index<TutorialRolePrototype>(RoleId);
            Assert.That(maps.TryLoadTutorialMap(role, out var mapUid, out var gridUid, out _), Is.True);

            var markersOnMap = new HashSet<string>();
            var markerQuery = entMan.EntityQueryEnumerator<TutorialStepMarkerComponent, TransformComponent>();
            while (markerQuery.MoveNext(out _, out var marker, out var xform))
            {
                if (xform.GridUid == gridUid)
                    markersOnMap.Add(marker.MarkerId);
            }

            var onMap = new List<EntityUid>();
            var all = entMan.EntityQueryEnumerator<TransformComponent>();
            while (all.MoveNext(out var uid, out var xform))
            {
                if (xform.GridUid == gridUid)
                    onMap.Add(uid);
            }

            var subs = role.Goals.SelectMany(g => g.SubGoals).ToList();

            Assert.Multiple(() =>
            {
                foreach (var wanted in subs
                             .SelectMany(s => new[] { s.Marker, s.RetryMarker, s.RetryReturnMarker })
                             .Where(m => !string.IsNullOrEmpty(m))
                             .Distinct())
                {
                    Assert.That(markersOnMap, Does.Contain(wanted),
                        $"no TutorialStepMarker for '{wanted}' on the map");
                }

                foreach (var tag in subs.Select(s => s.Tag).Where(t => !string.IsNullOrEmpty(t)).Distinct())
                {
                    Assert.That(onMap.Any(uid => tags.HasTag(uid, tag!)), Is.True,
                        $"nothing on the map carries '{tag}', so the drill that wants it cannot finish");
                }
            });

            maps.UnloadTutorialMap(mapUid);
        });
    }

    /// <summary>
    /// Regression: the coach used to trail a room behind, because every holopad kept the
    /// prototype's default room index. With no room ever matching, projection fell back to
    /// "nearest pad to the player", which is the room they are leaving rather than the one the
    /// next goal sends them to.
    /// </summary>
    [Test]
    public async Task TutorialBasics_EveryChamberHasItsOwnHolopad()
    {
        var pair = Pair;
        var server = pair.Server;
        var ticker = server.System<GameTicker>();
        var tutorial = server.System<TutorialServerRuleSystem>();

        await server.WaitPost(() =>
        {
            ticker.SetGamePreset("TutorialServer");
            ticker.StartRound();
        });

        await pair.RunTicksSync(30);
        await server.WaitPost(() => tutorial.TrySelectRole(pair.Player!, RoleId, confirmedStub: false));
        await pair.RunTicksSync(120);

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
            Assert.That(session!.SelectedRoleId, Is.EqualTo(RoleId));
            Assert.That(session.State, Is.EqualTo(TutorialSessionState.InTutorial));
            var gridUid = session.GridUid;
            Assert.That(gridUid, Is.Not.EqualTo(EntityUid.Invalid), "tutorial map never loaded");

            var rooms = new List<int>();
            var query = server.EntMan.EntityQueryEnumerator<TutorialHoloPointComponent, TransformComponent>();
            while (query.MoveNext(out _, out var point, out var xform))
            {
                if (xform.GridUid == gridUid)
                    rooms.Add(point.Room);
            }

            var role = server.ProtoMan.Index<TutorialRolePrototype>(RoleId);

            Assert.Multiple(() =>
            {
                for (var chamber = 0; chamber < role.Goals.Count; chamber++)
                {
                    Assert.That(rooms, Does.Contain(chamber),
                        $"chamber {chamber} has no holopad for the coach to project from");
                }

                // Several pads in one chamber is fine: projection picks the nearest, so she moves
                // up a long chamber as the player works through it. A pad advertising a chamber
                // that does not exist is not, since she would project into nothing.
                foreach (var room in rooms.Distinct())
                {
                    Assert.That(room, Is.LessThan(role.Goals.Count),
                        $"a holopad claims chamber {room}, which the curriculum does not have");
                }
            });
        });
    }

    /// <summary>
    /// Every beat the player has to act on leaves something in the banner: the key where one is
    /// taught, the objective line otherwise. Beats that end themselves leave it blank.
    /// </summary>
    [Test]
    public async Task TutorialBasics_BannerMatchesWhatEachBeatAsksFor()
    {
        var pair = Pair;
        var server = pair.Server;
        var ticker = server.System<GameTicker>();
        var tutorial = server.System<TutorialServerRuleSystem>();

        await server.WaitPost(() =>
        {
            ticker.SetGamePreset("TutorialServer");
            ticker.StartRound();
        });

        await pair.RunTicksSync(30);
        await server.WaitPost(() => tutorial.TrySelectRole(pair.Player!, RoleId, confirmedStub: false));
        await pair.RunTicksSync(120);

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
    /// Regression: the crowbar door used to spawn as an ordinary powered airlock, so it opened on
    /// its own and the pry sub-goal could never be satisfied.
    /// </summary>
    [Test]
    public async Task TutorialBasics_CrowbarDoorIsUnpoweredAndNeverAutoOpens()
    {
        var server = Pair.Server;
        var maps = server.System<TutorialMapSystem>();
        var rooms = server.System<TutorialPracticeRoomSystem>();
        var tags = server.System<TagSystem>();
        var entMan = server.EntMan;
        var protos = server.ProtoMan;

        await server.WaitAssertion(() =>
        {
            var role = protos.Index<TutorialRolePrototype>(RoleId);
            Assert.That(maps.TryLoadTutorialMap(role, out var mapUid, out var gridUid, out _), Is.True);

            // The pry sub-goal completes on InteractTargetTag with this tag, so that is the door.
            var pryDoors = new List<EntityUid>();
            var doorQuery = entMan.EntityQueryEnumerator<DoorComponent, TransformComponent>();
            while (doorQuery.MoveNext(out var uid, out _, out var xform))
            {
                if (xform.GridUid == gridUid && tags.HasTag(uid, "TutorialAirlock"))
                    pryDoors.Add(uid);
            }

            Assert.That(pryDoors, Has.Count.EqualTo(1), "exactly one door should be crowbar practice");
            var pryDoor = pryDoors[0];

            Assert.Multiple(() =>
            {
                Assert.That(entMan.TryGetComponent<ApcPowerReceiverComponent>(pryDoor, out var power), Is.True);
                Assert.That(power!.PowerDisabled, Is.True, "a powered door would just open on click");
                Assert.That(entMan.HasComponent<TutorialToolOnlyPryComponent>(pryDoor), Is.True,
                    "airlocks inherit PryUnpowered, so without this the drill can be skipped bare-handed");
            });

            // Advancing all the way to the last goal must still leave the pry door shut, while the
            // ordinary chamber gates open.
            var autoGates = new List<EntityUid>();
            var gateQuery = entMan.EntityQueryEnumerator<TutorialGateDoorComponent, TransformComponent>();
            while (gateQuery.MoveNext(out var uid, out _, out var xform))
            {
                if (xform.GridUid == gridUid)
                    autoGates.Add(uid);
            }

            Assert.That(autoGates, Is.Not.Empty, "the chambers are not gated at all");
            rooms.UnlockGatesForGoal(gridUid, role.Goals.Count);

            Assert.Multiple(() =>
            {
                Assert.That(entMan.GetComponent<DoorComponent>(pryDoor).State, Is.Not.EqualTo(DoorState.Open),
                    "crowbar door must not auto-open");
                foreach (var gate in autoGates)
                {
                    Assert.That(entMan.GetComponent<TutorialGateDoorComponent>(gate).Unlocked, Is.True,
                        "ordinary chamber gates should unlock as goals advance");
                }
            });

            maps.UnloadTutorialMap(mapUid);
        });
    }
}
