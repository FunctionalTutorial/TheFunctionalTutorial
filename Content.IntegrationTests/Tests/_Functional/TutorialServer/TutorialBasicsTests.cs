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
    /// The chambers the curriculum sends the player into must all exist as gated rooms, and the
    /// props each drill needs must be spawned into the right one.
    /// </summary>
    [Test]
    public async Task TutorialBasics_EveryDrillChamberHasItsProps()
    {
        var server = Pair.Server;
        var protos = server.ProtoMan;

        await server.WaitAssertion(() =>
        {
            Assert.That(protos.TryIndex<TutorialRolePrototype>(RoleId, out var role), Is.True);

            var spawnsByRoom = new Dictionary<int, List<string>>();
            foreach (var spawn in role!.PracticeSpawns)
            {
                if (!spawnsByRoom.TryGetValue(spawn.Room, out var list))
                    spawnsByRoom[spawn.Room] = list = new List<string>();

                list.Add(spawn.Id.Id);
            }

            Assert.Multiple(() =>
            {
                // One projector per chamber, or the coach cannot follow the player through.
                for (var room = 0; room < role.Goals.Count; room++)
                {
                    Assert.That(spawnsByRoom.TryGetValue(room, out var props) && props.Contains("TutorialHolopad"),
                        Is.True, $"chamber {room} has no TutorialHolopad for the coach to project from");
                }

                Assert.That(spawnsByRoom[3], Does.Contain("TutorialClimbTable"));
                Assert.That(spawnsByRoom[4], Does.Contain("TutorialTrainingChair"));
                Assert.That(spawnsByRoom[5], Does.Contain("TutorialPointCrate"));
                Assert.That(spawnsByRoom[6], Does.Contain("TutorialBasicsDoor"));
                Assert.That(spawnsByRoom[6], Does.Contain("Crowbar"));
            });
        });
    }

    /// <summary>
    /// Regression: the coach used to trail a room behind, because every stamped holopad kept the
    /// prototype's default room index. With no room ever matching, projection fell back to
    /// "nearest pad to the player", which is the room they are leaving rather than the one the
    /// next goal sends them to.
    /// </summary>
    [Test]
    public async Task TutorialBasics_StampedHolopadsKnowTheirChamber()
    {
        var pair = Pair;
        var server = pair.Server;
        var ticker = server.System<GameTicker>();
        var tutorial = server.System<TutorialServerRuleSystem>();

        await server.WaitPost(() =>
        {
            ticker.SetGamePreset("TutorialServer");
            ticker.StartGameRule("TutorialServer", out _);
            ticker.StartRound();
        });
        await pair.RunTicksSync(5);

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
            var expected = role.PracticeSpawns
                .Where(p => p.Id == "TutorialHolopad")
                .Select(p => p.Room)
                .OrderBy(r => r)
                .ToList();

            Assert.That(rooms.OrderBy(r => r), Is.EqualTo(expected),
                "each chamber's holopad must carry its own room index, or the coach cannot lead the player");
            Assert.That(rooms.Distinct().Count(), Is.EqualTo(rooms.Count),
                "two pads sharing a room index means one chamber has none");
        });
    }

    /// <summary>
    /// Regression: the crowbar gate used to spawn as an ordinary powered airlock, so it opened on
    /// its own and the pry sub-goal could never be satisfied.
    /// </summary>
    [Test]
    public async Task TutorialBasics_CrowbarGateIsUnpoweredAndNeverAutoOpens()
    {
        var server = Pair.Server;
        var maps = server.System<TutorialMapSystem>();
        var rooms = server.System<TutorialPracticeRoomSystem>();
        var entMan = server.EntMan;
        var protos = server.ProtoMan;

        await server.WaitAssertion(() =>
        {
            var role = protos.Index<TutorialRolePrototype>(RoleId);
            Assert.That(maps.TryLoadTutorialMap(role, out var mapUid, out var gridUid, out _), Is.True);

            var pryGates = new List<EntityUid>();
            var autoGates = new List<EntityUid>();
            var query = entMan.EntityQueryEnumerator<TutorialGateDoorComponent, TransformComponent>();
            while (query.MoveNext(out var uid, out var gate, out var xform))
            {
                if (xform.GridUid != gridUid)
                    continue;

                (gate.RequirePry ? pryGates : autoGates).Add(uid);
            }

            Assert.That(pryGates, Has.Count.EqualTo(1), "exactly one gate should be crowbar practice");
            var pryGate = pryGates[0];

            var tags = server.System<TagSystem>();
            Assert.Multiple(() =>
            {
                // The pry sub-goal completes on InteractTargetTag with this tag.
                Assert.That(tags.HasTag(pryGate, "TutorialAirlock"), Is.True);
                Assert.That(entMan.TryGetComponent<ApcPowerReceiverComponent>(pryGate, out var power), Is.True);
                Assert.That(power!.PowerDisabled, Is.True, "a powered gate would just open on click");
            });

            // Advancing all the way to the last goal must still leave the pry gate shut.
            rooms.UnlockGatesForGoal(gridUid, role.Goals.Count);

            Assert.Multiple(() =>
            {
                Assert.That(entMan.GetComponent<DoorComponent>(pryGate).State, Is.Not.EqualTo(DoorState.Open),
                    "crowbar gate must not auto-open");
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
