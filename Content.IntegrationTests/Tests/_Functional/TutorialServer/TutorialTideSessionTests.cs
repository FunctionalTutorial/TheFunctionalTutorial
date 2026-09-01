using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Content.IntegrationTests.Fixtures;
using Content.Server._Functional.TutorialServer;
using Content.Shared.Access.Systems;
using Content.Shared.Access.Components;
using Content.Server.NPC;
using Content.Server.NPC.HTN;
using Content.Server.NPC.Components;
using Content.Server.NPC.Pathfinding;
using Content.Server.NPC.Systems;
using Content.Server.GameTicking;
using Content.Server.Preferences.Managers;
using Content.Shared._Functional.TutorialServer;
using Content.Shared.Disposal.Components;
using Content.Shared.Disposal.Unit;
using Content.Shared.Doors.Components;
using Content.Shared.Doors.Systems;
using Content.Shared.Preferences;
using Content.Shared.Tag;
using NUnit.Framework;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Content.Shared.SubFloor;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.IntegrationTests.Tests._Functional.TutorialServer;

/// <summary>
/// Runs a real Passenger session on the hand-authored map. Nothing in this curriculum is stamped at
/// runtime any more, so the map file is the only thing standing between the script and a drill with
/// no prop in front of it: these tests load it, count what the beats reach for, and put a body down
/// the disposal run to see where it comes out.
/// </summary>
[TestFixture]
[TestOf(typeof(TutorialServerRuleSystem))]
public sealed class TutorialTideSessionTests : GameTest
{
    public override PoolSettings PoolSettings => new()
    {
        Dirty = true,
        InLobby = true,
        DummyTicker = false,
        Connected = true,
    };

    private const string RoleId = "TutorialTide";

    /// <summary>Prototypes a drill names directly, so the map is useless without them.</summary>
    private static readonly string[] RequiredProtos =
    [
        "TutorialTideCargoDoor", "TutorialTideMaintDoor", "TutorialTideToolbox", "TutorialTideBelt",
        "TutorialTideGrille", "TutorialTideStorageDoor", "TutorialTideSteel", "TutorialTideVaultDoor",
        "TutorialTideDisposal", "DisposalTrunk", "TutorialTideVendor", "ClothingHeadHatCapcap",
        "TutorialTideCueGloves", "TutorialTideCueRefusal", "TutorialTideCueBoots", "TutorialTideCueArrest",
        "TutorialTideHoP",
    ];

    /// <summary>Markers the curriculum reaches for, which the map has to carry.</summary>
    private static readonly string[] RequiredMarkers =
    [
        // walk-N comes from the generic TutorialWalkPointN prototypes, which are not a greytide
        // idea: any curriculum with a coach who walks uses the same nine.
        "walk-0", "walk-1", "walk-2", "walk-3", "walk-4", "walk-5", "walk-6", "walk-7", "walk-8",
        "tide-pipe-1", "tide-pipe-2", "tide-pad-vault", "tide-counter",
    ];

    [Test]
    public async Task TutorialTide_MapCarriesEveryDrillTarget()
    {
        var pair = Pair;
        var server = pair.Server;
        var entMan = server.EntMan;

        await StartSession();

        await server.WaitAssertion(() =>
        {
            var mob = pair.Player!.AttachedEntity!.Value;
            var mapUid = entMan.GetComponent<TransformComponent>(mob).MapUid;
            Assert.That(mapUid, Is.Not.Null);

            var present = new HashSet<string>();
            var markers = new HashSet<string>();

            var query = entMan.EntityQueryEnumerator<TransformComponent>();
            while (query.MoveNext(out var uid, out var xform))
            {
                if (xform.MapUid != mapUid)
                    continue;

                if (entMan.GetComponent<MetaDataComponent>(uid).EntityPrototype?.ID is { } id)
                    present.Add(id);

                if (entMan.TryGetComponent<TutorialStepMarkerComponent>(uid, out var marker) &&
                    !string.IsNullOrEmpty(marker.MarkerId))
                {
                    markers.Add(marker.MarkerId);
                }
            }

            // Every section a goal sends the coach to needs somewhere for him to stand in it, or
            // he waits in the section before it and his next line never starts.
            var walkRooms = new HashSet<int>();
            var walkQuery = entMan.EntityQueryEnumerator<TutorialWalkPointComponent, TransformComponent>();
            while (walkQuery.MoveNext(out _, out var walk, out var walkXform))
            {
                if (walkXform.MapUid == mapUid)
                    walkRooms.Add(walk.Room);
            }

            var role = server.ProtoMan.Index<TutorialRolePrototype>(RoleId);

            Assert.Multiple(() =>
            {
                foreach (var proto in RequiredProtos)
                    Assert.That(present, Does.Contain(proto), $"the map is missing {proto}");

                foreach (var marker in RequiredMarkers)
                    Assert.That(markers, Does.Contain(marker), $"the map carries no '{marker}'");

                foreach (var room in role.Goals.Select(g => g.EnterRoom ?? 0).Distinct())
                {
                    Assert.That(walkRooms, Does.Contain(room),
                        $"section {room} has no walk point; the coach would stay in the section before it");
                }
            });
        });
    }

    /// <summary>
    /// He walks off to the chamber's own waypoint instead of trailing the player. If the lead
    /// system ever stops running, the mentor still stands somewhere sensible and this is the only
    /// thing that would notice.
    /// </summary>
    [Test]
    public async Task TutorialTide_MentorWalksToTheChambersOwnWaypoint()
    {
        var pair = Pair;
        var server = pair.Server;
        var entMan = server.EntMan;
        var tutorial = server.System<TutorialServerRuleSystem>();

        await StartSession();
        await pair.RunTicksSync(60);

        await server.WaitAssertion(() =>
        {
            var mob = pair.Player!.AttachedEntity!.Value;
            Assert.That(tutorial.TryGetSession(mob, out var session), Is.True);
            Assert.That(tutorial.TryGetRole(mob, out var role), Is.True);

            Assert.That(session.MentorUid, Is.Not.EqualTo(EntityUid.Invalid), "no mentor spawned");
            Assert.That(entMan.TryGetComponent<TutorialMentorComponent>(session.MentorUid, out var mentor),
                Is.True);
            Assert.That(mentor!.Leads, Is.True, "the mentor was not marked as leading");

            // Arrivals pins him in front of the player, so there is nothing to resolve until that
            // beat is over.
            Assert.That(session.MentorWalkPoint, Is.EqualTo(EntityUid.Invalid),
                "he set off during the beat that is meant to hold him still");

            tutorial.AdvanceSubGoal(mob);
        });

        // He does not turn to go while a line of his is still on screen, so give the gap after it
        // time to run out before asking where he was sent.
        await pair.RunTicksSync(300);

        await server.WaitAssertion(() =>
        {
            var mob = pair.Player!.AttachedEntity!.Value;
            Assert.That(tutorial.TryGetSession(mob, out var session), Is.True);

            Assert.Multiple(() =>
            {
                Assert.That(entMan.TryGetComponent<TutorialWalkPointComponent>(session.MentorWalkPoint, out var point),
                    Is.True);
                Assert.That(point!.Room, Is.EqualTo(0), "he was sent to the wrong chamber's waypoint");
            });
        });
    }

    /// <summary>
    /// The whole of chamber 5 rests on this: two lengths of pipe close a gap, and the bin then
    /// carries the player to the far end of the run. Every part of that is map geometry the
    /// curriculum cannot check for itself, so it is checked here.
    /// </summary>
    /// <summary>
    /// The next section starts where it happens. Narrating it over his shoulder on the way there
    /// describes a room the player cannot see yet, and stops the walk being the thing that carries
    /// them into it.
    /// </summary>
    /// <remarks>
    /// The opening walk is the deliberate exception and is checked here too: the line that tells
    /// the player to come with him is in that segment, so holding it would leave a player who has
    /// been given no reason to move alone in a corridor.
    /// </remarks>
    [Test]
    public async Task TutorialTide_TheCoachIsSilentForTheLengthOfTheWalk()
    {
        var pair = Pair;
        var server = pair.Server;
        var entMan = server.EntMan;
        var tutorial = server.System<TutorialServerRuleSystem>();
        var lead = server.System<TutorialLeadMentorSystem>();
        var transform = server.System<SharedTransformSystem>();

        await StartSession();
        await pair.RunTicksSync(60);

        await server.WaitAssertion(() =>
        {
            var mob = pair.Player!.AttachedEntity!.Value;
            Assert.That(tutorial.TryGetSession(mob, out var session), Is.True);
            Assert.That(tutorial.TryGetRole(mob, out var role), Is.True);

            var mentor = session.MentorUid;
            Assert.That(entMan.TryGetComponent<TutorialTrainerComponent>(mentor, out var trainer), Is.True);
            Assert.That(trainer!.SpeechHeld, Is.False,
                "the opening walk is not held; his first line is what asks the player to follow");

            // Push the curriculum to the Cargo door, which is somewhere he is not standing. Two
            // goals along rather than one: arrivals holds him in place on purpose, and the dorms
            // section he walks to next is where the introduction happens.
            for (var i = 0; i < 40 && session.GoalIndex < 2; i++)
                tutorial.AdvanceSubGoal(mob);

            Assert.That(session.GoalIndex, Is.EqualTo(2), "the curriculum never reached the Cargo door");
        });

        // He finishes the line he is on before setting off, so the destination is not resolved on
        // the same tick the beat changed. Sampled rather than waited out, because the hold is
        // released the moment he arrives and a fixed wait races the walk.
        var held = false;
        var arrived = false;

        for (var i = 0; i < 200 && !arrived; i++)
        {
            await pair.RunTicksSync(2);
            await server.WaitPost(() =>
            {
                var mob = pair.Player!.AttachedEntity!.Value;
                if (!tutorial.TryGetSession(mob, out var session))
                    return;

                var comp = entMan.GetComponent<TutorialTrainerComponent>(session.MentorUid);
                held |= comp.SpeechHeld;

                arrived = entMan.TryGetComponent<TutorialWalkPointComponent>(session.MentorWalkPoint, out var p)
                          && p.Room == 1 && !comp.SpeechHeld;
            });
        }

        await server.WaitAssertion(() =>
        {
            var mob = pair.Player!.AttachedEntity!.Value;
            Assert.That(tutorial.TryGetSession(mob, out var session), Is.True);

            Assert.That(entMan.TryGetComponent<TutorialWalkPointComponent>(session.MentorWalkPoint, out var point),
                Is.True);
            Assert.That(point!.Room, Is.EqualTo(1), "he was never sent to the Cargo door");

            Assert.That(held, Is.True,
                "he started the next section's script without ever being held for the walk to it");
            Assert.That(arrived, Is.True, "he never arrived, so the hold was never released");
        });
    }

    [Test]
    public async Task TutorialTide_TheFinishedPipeCarriesThePlayerToTheVault()
    {
        var pair = Pair;
        var server = pair.Server;
        var entMan = server.EntMan;
        var tags = server.System<TagSystem>();
        var disposal = server.System<SharedDisposalUnitSystem>();
        var xforms = server.System<SharedTransformSystem>();
        var containers = server.System<SharedContainerSystem>();

        await StartSession();

        var unit = EntityUid.Invalid;
        var vault = Vector2.Zero;

        // Stand in for the player working the construction menu: the two gaps get their pipe.
        await server.WaitPost(() =>
        {
            var mob = pair.Player!.AttachedEntity!.Value;
            var mapUid = entMan.GetComponent<TransformComponent>(mob).MapUid;

            // A straight pipe only connects along its own axis, and the construction menu places it
            // facing whichever way the player is building. Copying the rotation off the length next
            // to the gap is that, and it keeps the test honest if the run is ever re-laid on a
            // different heading.
            var run = new List<(Vector2 Pos, Angle Rot)>();
            var pipes = entMan.EntityQueryEnumerator<TransformComponent>();
            while (pipes.MoveNext(out var uid, out var pipeXform))
            {
                if (pipeXform.MapUid == mapUid && tags.HasTag(uid, "TutorialTidePipe"))
                    run.Add((xforms.GetWorldPosition(pipeXform), pipeXform.LocalRotation));
            }

            Assert.That(run, Is.Not.Empty, "the map has no tutorial disposal pipe to match");

            var gaps = new List<EntityCoordinates>();
            var query = entMan.EntityQueryEnumerator<TutorialStepMarkerComponent, TransformComponent>();
            while (query.MoveNext(out _, out var marker, out var xform))
            {
                if (xform.MapUid != mapUid)
                    continue;

                if (marker.MarkerId is "tide-pipe-1" or "tide-pipe-2")
                    gaps.Add(xform.Coordinates);
                else if (marker.MarkerId == "tide-pad-vault")
                    vault = xforms.GetWorldPosition(xform);
            }

            foreach (var gap in gaps)
            {
                var at = xforms.ToMapCoordinates(gap).Position;
                var nearest = run.MinBy(p => Vector2.DistanceSquared(p.Pos, at));
                var pipe = entMan.SpawnAtPosition("DisposalPipe", gap);
                xforms.SetLocalRotation(pipe, nearest.Rot);
            }

            var units = entMan.EntityQueryEnumerator<DisposalUnitComponent, TransformComponent>();
            while (units.MoveNext(out var uid, out _, out var unitXform))
            {
                if (unitXform.MapUid == mapUid && tags.HasTag(uid, "TutorialTideDisposal"))
                {
                    unit = uid;
                    break;
                }
            }
        });

        await pair.RunTicksSync(5);

        Assert.That(unit, Is.Not.EqualTo(EntityUid.Invalid), "no tutorial disposal unit on the map");
        Assert.That(vault, Is.Not.EqualTo(Vector2.Zero), "no tide-pad-vault marker on the map");

        // user: null skips the climb-in do-after; the beat before this one is what teaches that.
        await server.WaitPost(() =>
        {
            var mob = pair.Player!.AttachedEntity!.Value;
            var unitComp = entMan.GetComponent<DisposalUnitComponent>(unit);
            Assert.That(disposal.TryInsert((unit, unitComp), mob, null), Is.True,
                "the unit would not take the player");
        });

        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            var mob = pair.Player!.AttachedEntity!.Value;
            Assert.That(containers.IsEntityInContainer(mob), Is.True, "the player is not in the bin");
        });

        // Long enough for the six-second automatic engage and then the ride down the run.
        for (var i = 0; i < 40 && !await LeftTheBin(); i++)
            await pair.RunTicksSync(30);

        await server.WaitAssertion(() =>
        {
            var mob = pair.Player!.AttachedEntity!.Value;
            Assert.That(containers.IsEntityInContainer(mob), Is.False,
                "the unit never flushed, or the run never let go of the player");

            var pos = xforms.GetWorldPosition(entMan.GetComponent<TransformComponent>(mob));
            Assert.That(Vector2.Distance(pos, vault), Is.LessThanOrEqualTo(1.5f),
                $"the pipe run put the player at {pos}, not at the vault marker {vault}");
        });

        async Task<bool> LeftTheBin()
        {
            var left = false;
            await server.WaitPost(() =>
            {
                if (pair.Player?.AttachedEntity is { } mob)
                    left = !containers.IsEntityInContainer(mob);
            });
            return left;
        }
    }

    /// <summary>
    /// Nothing a section has to say is said on the way to it, including the first section.
    /// </summary>
    /// <remarks>
    /// The opening walk used to be exempt from the speech hold, back when the line asking the
    /// player to follow lived in that segment. It does not any more; arrivals pins him in front of
    /// them to say it. The exemption outlived its reason as a hole exactly one walk wide, and the
    /// walk it applied to was the one the player is most likely to be watching. Checked on the
    /// live session rather than by reading flags, since the bug was that the hold was never
    /// applied at all.
    /// </remarks>
    [Test]
    public async Task TutorialTide_HeSaysNothingOnTheWayToTheFirstSection()
    {
        var pair = Pair;
        var server = pair.Server;
        var entMan = server.EntMan;
        var tutorial = server.System<TutorialServerRuleSystem>();
        var xforms = server.System<SharedTransformSystem>();

        await StartSession();
        await pair.RunTicksSync(30);

        // Off the arrivals beat, which is the one that holds him in place to speak.
        await server.WaitPost(() =>
        {
            var mob = pair.Player!.AttachedEntity!.Value;
            var part = entMan.GetComponent<TutorialParticipantComponent>(mob);

            for (var i = 0; i < 20; i++)
            {
                if (tutorial.TryGetCurrentSubGoal(mob, part, out var sub) && sub.Id == "introductions")
                    break;

                tutorial.AdvanceSubGoal(mob);
            }
        });

        var spokeWhileAway = false;
        var arrived = false;

        for (var i = 0; i < 250 && !arrived; i++)
        {
            await pair.RunTicksSync(2);
            await server.WaitPost(() =>
            {
                var mob = pair.Player!.AttachedEntity!.Value;
                if (!tutorial.TryGetSession(mob, out var session) ||
                    session.MentorWalkPoint == EntityUid.Invalid)
                {
                    return;
                }

                var comp = entMan.GetComponent<TutorialTrainerComponent>(session.MentorUid);
                var here = xforms.GetWorldPosition(entMan.GetComponent<TransformComponent>(session.MentorUid));
                var spot = xforms.GetWorldPosition(entMan.GetComponent<TransformComponent>(session.MentorWalkPoint));
                var atSpot = Vector2.Distance(here, spot) <= 1.5f;

                if (!atSpot && comp.SpokenLineIndex > 0 && comp.LastSpokenSubGoal == "introductions")
                    spokeWhileAway = true;

                // Stand in for the player following him, or he waits at the spot for a listener
                // who never turns up and the segment never starts.
                if (atSpot)
                    xforms.SetCoordinates(mob, entMan.GetComponent<TransformComponent>(session.MentorUid).Coordinates);

                arrived = atSpot && comp.LastSpokenSubGoal == "introductions" && comp.SpokenLineIndex > 0;
            });
        }

        await server.WaitAssertion(() =>
        {
            Assert.That(spokeWhileAway, Is.False,
                "he started the section's script before reaching the spot it belongs to");
            Assert.That(arrived, Is.True, "he never got there and said it");
        });
    }

    /// <summary>
    /// Doors do not stop the coach, and both halves of that are true at once.
    /// </summary>
    /// <remarks>
    /// <c>PathFlags.Doors</c> only tells the pathfinder it may route through a locked door; the
    /// door still runs its own access check when he walks into it. With the flag and no access he
    /// walks to a door that says no and stands there, which is worse than planning around it,
    /// so the two are checked together on the live mentor rather than read back out of YAML.
    /// </remarks>
    [Test]
    public async Task TutorialTide_TheCoachIsNotStoppedByDoors()
    {
        var pair = Pair;
        var server = pair.Server;
        var entMan = server.EntMan;
        var tutorial = server.System<TutorialServerRuleSystem>();
        var pathfinding = server.System<PathfindingSystem>();
        var access = server.System<AccessReaderSystem>();
        var tags = server.System<TagSystem>();

        await StartSession();
        await pair.RunTicksSync(30);

        await server.WaitAssertion(() =>
        {
            var mob = pair.Player!.AttachedEntity!.Value;
            Assert.That(tutorial.TryGetSession(mob, out var session), Is.True);

            var mentor = session.MentorUid;
            Assert.That(mentor, Is.Not.EqualTo(EntityUid.Invalid), "no mentor spawned");

            var mapUid = entMan.GetComponent<TransformComponent>(mob).MapUid;
            var locked = new List<EntityUid>();
            var doors = entMan.EntityQueryEnumerator<DoorComponent, AccessReaderComponent, TransformComponent>();
            while (doors.MoveNext(out var uid, out _, out _, out var xform))
            {
                if (xform.MapUid == mapUid)
                    locked.Add(uid);
            }

            Assert.Multiple(() =>
            {
                Assert.That(pathfinding.GetFlags(mentor).HasFlag(PathFlags.Doors), Is.True,
                    "the mentor plans around doors; set NavDoors on its HTN blackboard");

                // Routing him at a door is not the same as getting him through one. An airlock is
                // bump-open, and bump-open answers to this tag and nothing else.
                Assert.That(tags.HasTag(mentor, "DoorBumpOpener"), Is.True,
                    "the mentor cannot open a door by walking into it, so he will pace in front of it");

                Assert.That(locked, Is.Not.Empty,
                    "no access-locked door on the map, so this test proves nothing");

                foreach (var door in locked)
                {
                    Assert.That(access.IsAllowed(mentor, door), Is.True,
                        $"{entMan.ToPrettyString(door)} would refuse the mentor he was routed through");
                }
            });
        });
    }

    /// <summary>
    /// Put a closed, access-locked door between the coach and where he is going, and he opens it.
    /// </summary>
    /// <remarks>
    /// The end-to-end version of the door work, because every part of it fails silently on its own
    /// and reading the prototypes back proves nothing. Routing him at the door is one thing; the
    /// obstacle handler that opens it only runs once the walk has actually finished, and static
    /// collision avoidance used to push him off the door before it ever could, which read in-game
    /// as pacing back and forth in front of an airlock.
    /// <para>
    /// Driven on a spare coach rather than the session's own. The lead system re-points the real
    /// one at its section's walk point every tick, so anything this test asked of him would be
    /// overwritten before he took a step.
    /// </para>
    /// </remarks>
    [Test]
    public async Task TutorialTide_TheCoachOpensALockedDoorInHisWay()
    {
        var pair = Pair;
        var server = pair.Server;
        var entMan = server.EntMan;
        var xforms = server.System<SharedTransformSystem>();
        var tags = server.System<TagSystem>();

        await StartSession();
        await pair.RunTicksSync(30);

        var door = EntityUid.Invalid;
        var coach = EntityUid.Invalid;
        var inside = default(EntityCoordinates);

        // The materials store is the only walled room on the map with exactly one usable door, so
        // it is the only place a route can be forced through one. Its door ships bolted for the
        // hacking drill; unbolted it is an ordinary locked airlock, which is the case that strands
        // him.
        await server.WaitPost(() =>
        {
            var mob = pair.Player!.AttachedEntity!.Value;
            var mapUid = entMan.GetComponent<TransformComponent>(mob).MapUid;

            var query = entMan.EntityQueryEnumerator<DoorComponent, TransformComponent>();
            while (query.MoveNext(out var uid, out _, out var xform))
            {
                if (xform.MapUid == mapUid && tags.HasTag(uid, "TutorialTideStorageDoor"))
                {
                    door = uid;
                    break;
                }
            }

            var steel = entMan.EntityQueryEnumerator<TransformComponent>();
            while (steel.MoveNext(out var uid, out var xform))
            {
                if (xform.MapUid == mapUid && tags.HasTag(uid, "TutorialTideSteel"))
                {
                    inside = xform.Coordinates;
                    break;
                }
            }
        });

        Assert.That(door, Is.Not.EqualTo(EntityUid.Invalid), "no storage door on the map");
        Assert.That(inside, Is.Not.EqualTo(default(EntityCoordinates)), "no steel inside the store");

        await server.WaitPost(() =>
        {
            var doors = server.System<SharedDoorSystem>();
            if (entMan.TryGetComponent<DoorBoltComponent>(door, out var bolt))
                doors.SetBoltsDown((door, bolt), false);

            var at = entMan.GetComponent<TransformComponent>(door).Coordinates;
            coach = entMan.SpawnAtPosition("TutorialTideMentor", at.Offset(new Vector2(2.5f, 0f)));

            var beyond = entMan.SpawnAtPosition("TutorialWalkPoint0", inside);

            var npc = server.System<NPCSystem>();
            var htnSys = server.System<HTNSystem>();
            var htn = entMan.GetComponent<HTNComponent>(coach);
            npc.SetBlackboard(coach, NPCBlackboard.FollowTarget,
                new EntityCoordinates(beyond, Vector2.Zero), htn);
            htnSys.Replan(htn);
        });

        var opened = false;
        for (var i = 0; i < 30 && !opened; i++)
        {
            await pair.RunTicksSync(15);
            await server.WaitPost(() =>
            {
                if (entMan.TryGetComponent<DoorComponent>(door, out var comp))
                    opened |= comp.State is DoorState.Open or DoorState.Opening;
            });
        }

        await server.WaitAssertion(() =>
        {
            Assert.That(opened, Is.True,
                "the coach never opened the door he was walking at; he is pacing in front of it");
        });
    }

    /// <summary>
    /// The disposal run is on bare subfloor, so the player can actually see it.
    /// </summary>
    /// <remarks>
    /// Disposal pipes carry <c>SubFloorHide</c>: lay a finished tile over one and it stops
    /// rendering and stops being clickable. That would take out three beats at once, and all three
    /// would fail by simply never completing, with the pipe sitting invisible under the floor the
    /// whole time. Checked on the gap tiles too, since a pipe built into a covered gap vanishes the
    /// moment it is placed.
    /// </remarks>
    [Test]
    public async Task TutorialTide_ThePipeRunIsNotBuriedUnderTheFloor()
    {
        var pair = Pair;
        var server = pair.Server;
        var entMan = server.EntMan;
        var tags = server.System<TagSystem>();
        var subfloor = server.System<SharedSubFloorHideSystem>();
        var maps = server.System<SharedMapSystem>();

        await StartSession();

        await server.WaitAssertion(() =>
        {
            var mob = pair.Player!.AttachedEntity!.Value;
            var mapUid = entMan.GetComponent<TransformComponent>(mob).MapUid;

            var covered = new List<string>();

            void CheckTile(EntityUid uid, TransformComponent xform, string what)
            {
                if (xform.GridUid is not { } gridUid ||
                    !entMan.TryGetComponent<MapGridComponent>(gridUid, out var grid))
                {
                    return;
                }

                var tile = maps.LocalToTile(gridUid, grid, xform.Coordinates);
                if (subfloor.HasFloorCover(gridUid, grid, tile))
                    covered.Add($"{what} at {tile}");
            }

            var pipes = entMan.EntityQueryEnumerator<TransformComponent>();
            while (pipes.MoveNext(out var uid, out var xform))
            {
                if (xform.MapUid != mapUid)
                    continue;

                if (tags.HasTag(uid, "TutorialTidePipe"))
                    CheckTile(uid, xform, "disposal pipe");
            }

            var markers = entMan.EntityQueryEnumerator<TutorialStepMarkerComponent, TransformComponent>();
            while (markers.MoveNext(out var uid, out var marker, out var xform))
            {
                if (xform.MapUid == mapUid && marker.MarkerId is "tide-pipe-1" or "tide-pipe-2")
                    CheckTile(uid, xform, $"pipe gap '{marker.MarkerId}'");
            }

            Assert.That(covered, Is.Empty,
                "these tiles have a finished floor over them, so the pipe under them is invisible "
                + "and unclickable: " + string.Join(", ", covered));
        });
    }

    /// <summary>
    /// A line marked afterComplete is held back until the player has done the thing, and the beat
    /// waits for him to say it.
    /// </summary>
    /// <remarks>
    /// Driven on a spare coach so the assertion is about the mechanism rather than about whichever
    /// beat happens to use it today. Checks both halves: silent while the objective is outstanding,
    /// and the beat still open at the moment the reaction starts, since a reaction that lands after
    /// the next objective has already appeared is the bug this is here to prevent.
    /// </remarks>
    [Test]
    public async Task TutorialTide_ReactionLinesWaitForThePlayer()
    {
        var pair = Pair;
        var server = pair.Server;
        var entMan = server.EntMan;
        var trainer = server.System<TutorialTrainerSystem>();
        var xforms = server.System<SharedTransformSystem>();

        await StartSession();
        await pair.RunTicksSync(30);

        await server.WaitAssertion(() =>
        {
            var mob = pair.Player!.AttachedEntity!.Value;
            Assert.That(server.System<TutorialServerRuleSystem>().TryGetSession(mob, out var session),
                Is.True);

            var coach = session.MentorUid;
            Assert.That(coach, Is.Not.EqualTo(EntityUid.Invalid), "no mentor spawned");

            var comp = entMan.GetComponent<TutorialTrainerComponent>(coach);
            comp.LastSpokenSubGoal = "probe";
            comp.PendingLines.Clear();
            comp.PendingAfterLines.Clear();
            comp.ReactingFor = null;
            comp.PendingLines.Enqueue(new TutorialPendingLine("instruction", false));
            comp.PendingAfterLines.Enqueue(new TutorialPendingLine("reaction", false, true));

            Assert.Multiple(() =>
            {
                Assert.That(trainer.TryStartReaction(mob, "somethingelse"), Is.False,
                    "a reaction fired for a beat the coach is not on");

                Assert.That(trainer.TryStartReaction(mob, "probe"), Is.True,
                    "the owed reaction did not start, so the beat would advance over it");
                Assert.That(comp.ReactingFor, Is.EqualTo("probe"));
                Assert.That(comp.PendingLines, Is.Empty,
                    "instruction lines outlived the thing they were instructing");

                // Still true while it runs, because the answer means "leave the beat alone", not
                // "one started just now". Polled sensors call in every tick for as long as their
                // condition holds, and answering false to the second call advanced the beat out
                // from under the reaction the first one had begun.
                Assert.That(trainer.TryStartReaction(mob, "probe"), Is.True,
                    "a second call mid-reaction released the beat, cutting the coach off");
                Assert.That(comp.ReactingFor, Is.EqualTo("probe"), "the reaction was restarted");

                Assert.That(trainer.TryStartReaction(mob, "somethingelse"), Is.False,
                    "a beat that is not the one being reacted to was held anyway");
            });
        });

        // Reaction lines are paced like every other line. They were not: the gap is sized off the
        // line coming next, which was read from the instruction queue, and that queue is empty
        // while a reaction runs. Every reaction line came out at the floor delay, so the long ones
        // scrolled away unread.
        //
        // Driven on a real beat rather than a made-up one, because the coach's own Update rebuilds
        // his queues whenever the sub-goal he is on stops matching the player's, which throws away
        // any reaction staged by hand.
        await server.WaitPost(() =>
        {
            var mob = pair.Player!.AttachedEntity!.Value;
            var part = entMan.GetComponent<TutorialParticipantComponent>(mob);
            var rule = server.System<TutorialServerRuleSystem>();

            for (var i = 0; i < 40; i++)
            {
                if (rule.TryGetCurrentSubGoal(mob, part, out var sub) && sub.Id == "try-cargo-door")
                    break;

                rule.AdvanceSubGoal(mob);
            }

            // Park him on top of the player so nothing about range or the walk holds his script.
            rule.TryGetSession(mob, out var session);
            xforms.SetCoordinates(session.MentorUid, entMan.GetComponent<TransformComponent>(mob).Coordinates);
        });

        await pair.RunTicksSync(120);

        // Two reaction lines of known length: a short one to speak, and a long one whose typed
        // delay must be visible in the gap the coach schedules after it.
        const string longLine = "A reaction line long enough that its typing delay clears the floor.";
        var started = false;

        await server.WaitPost(() =>
        {
            var mob = pair.Player!.AttachedEntity!.Value;
            var rule = server.System<TutorialServerRuleSystem>();
            rule.TryGetSession(mob, out var session);

            var comp = entMan.GetComponent<TutorialTrainerComponent>(session.MentorUid);
            comp.PendingAfterLines.Clear();
            comp.PendingAfterLines.Enqueue(new TutorialPendingLine("Short.", false, true));
            comp.PendingAfterLines.Enqueue(new TutorialPendingLine(longLine, false, true));

            started = trainer.TryStartReaction(mob, "try-cargo-door");
        });

        Assert.That(started, Is.True, "the Cargo door beat owes a reaction and did not start one");

        // Tick until the short one is out and the gap before the long one has been scheduled.
        var gap = TimeSpan.Zero;
        var min = TimeSpan.Zero;

        for (var i = 0; i < 60 && gap == TimeSpan.Zero; i++)
        {
            await pair.RunTicksSync(5);
            await server.WaitPost(() =>
            {
                var mob = pair.Player!.AttachedEntity!.Value;
                if (!server.System<TutorialServerRuleSystem>().TryGetSession(mob, out var session))
                    return;

                var comp = entMan.GetComponent<TutorialTrainerComponent>(session.MentorUid);
                min = comp.MinLineDelay;

                if (comp.PendingAfterLines.Count != 1 || comp.NextLineAt is not { } at)
                    return;

                gap = at - server.ResolveDependency<IGameTiming>().CurTime;
            });
        }

        await server.WaitAssertion(() =>
        {
            Assert.That(gap, Is.GreaterThan(TimeSpan.Zero), "the reaction never got past its first line");

            // secondsPerCharacter stretches the gap to fit the line coming next. Sized off the
            // instruction queue instead, that queue is empty during a reaction and the gap comes
            // back as exactly the floor, so every reaction line lands at the same 3s however long
            // it is and the long ones scroll away half-read.
            Assert.That(gap, Is.GreaterThan(min),
                $"the pause before a {longLine.Length}-character reaction line was {gap.TotalSeconds:F2}s, "
                + "which is minLineDelay: the delay is being sized off the wrong queue");
        });
    }

    /// <summary>
    /// Walking into a locked door counts, not just clicking it. Almost nobody's first locked door
    /// is one they clicked, so a beat that only watched for clicks would leave a player standing in
    /// a doorway listening to the thing beep at them with the objective refusing to tick.
    /// </summary>
    /// <remarks>
    /// Driven through <c>TryOpen</c> with the player as the user, which is the exact call
    /// <c>SharedDoorSystem.HandleCollide</c> makes when a body bumps a door, rather than through
    /// physics: the collision itself is the engine's business and is not what this guards.
    /// </remarks>
    [Test]
    public async Task TutorialTide_WalkingIntoALockedDoorCountsAsTryingIt()
    {
        var pair = Pair;
        var server = pair.Server;
        var entMan = server.EntMan;
        var tags = server.System<TagSystem>();
        var doors = server.System<SharedDoorSystem>();
        var tutorial = server.System<TutorialServerRuleSystem>();

        await StartSession();

        // Walk the session up to the beat that completes on being refused.
        await server.WaitPost(() =>
        {
            var mob = pair.Player!.AttachedEntity!.Value;
            for (var i = 0; i < 32; i++)
            {
                if (!tutorial.TryGetCurrentSubGoal(mob, entMan.GetComponent<TutorialParticipantComponent>(mob),
                        out var sub) || sub.Id == "try-cargo-door")
                {
                    break;
                }

                tutorial.AdvanceSubGoal(mob);
            }
        });

        await server.WaitAssertion(() =>
        {
            var mob = pair.Player!.AttachedEntity!.Value;
            Assert.That(tutorial.TryGetCurrentSubGoal(mob,
                entMan.GetComponent<TutorialParticipantComponent>(mob), out var sub), Is.True);
            Assert.That(sub!.Id, Is.EqualTo("try-cargo-door"), "never reached the refusal beat");
        });

        await server.WaitPost(() =>
        {
            var mob = pair.Player!.AttachedEntity!.Value;
            var mapUid = entMan.GetComponent<TransformComponent>(mob).MapUid;

            var query = entMan.EntityQueryEnumerator<DoorComponent, TransformComponent>();
            while (query.MoveNext(out var uid, out var door, out var xform))
            {
                if (xform.MapUid != mapUid || !tags.HasTag(uid, "TutorialTideCargoDoor"))
                    continue;

                Assert.That(doors.TryOpen(uid, door, mob), Is.False, "the Cargo door let a passenger in");
                break;
            }
        });

        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            var mob = pair.Player!.AttachedEntity!.Value;
            Assert.That(tutorial.TryGetCurrentSubGoal(mob,
                entMan.GetComponent<TutorialParticipantComponent>(mob), out var sub), Is.True);
            Assert.That(sub!.Id, Is.Not.EqualTo("try-cargo-door"),
                "the door refused the player and the beat did not notice");
        });
    }

    /// <summary>
    /// Walks the whole curriculum in a live session, so every beat is checked against the banner
    /// the player would actually be looking at rather than the one it was authored with.
    /// </summary>
    [Test]
    public async Task TutorialTide_BannerMatchesWhatTheBeatAsksFor()
    {
        var pair = Pair;
        var server = pair.Server;
        var tutorial = server.System<TutorialServerRuleSystem>();

        await StartSession();

        await server.WaitAssertion(() =>
        {
            var mob = pair.Player!.AttachedEntity!.Value;
            var role = server.ProtoMan.Index<TutorialRolePrototype>(RoleId);
            TutorialCurriculumAssertions.BannerMatchesWhatTheBeatAsksFor(tutorial, server.EntMan, mob, role);
        });
    }

    private async Task StartSession()
    {
        var pair = Pair;
        var server = pair.Server;
        var ticker = server.System<GameTicker>();
        var tutorial = server.System<TutorialServerRuleSystem>();

        // The pooled player's saved character is not ours to assume, and the spawn path reads it.
        await server.WaitPost(() => server
            .ResolveDependency<IServerPreferencesManager>()
            .SetProfile(pair.Player!.UserId, 0, new HumanoidCharacterProfile()).Wait());

        await server.WaitPost(() =>
        {
            ticker.SetGamePreset("TutorialServer");
            ticker.StartRound();
        });
        await pair.RunTicksSync(10);

        await server.WaitPost(() =>
        {
            var query = server.EntMan.EntityQueryEnumerator<TutorialServerRuleComponent>();
            if (!query.MoveNext(out _, out _))
                ticker.StartGameRule("TutorialServer", out _);
        });
        await pair.RunTicksSync(5);

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

        Assert.That(ready, Is.True, "the player never ended up attached to a tutorial participant");

        bool HasLiveSelection()
        {
            var query = server.EntMan.EntityQueryEnumerator<TutorialServerRuleComponent>();
            while (query.MoveNext(out _, out var rule))
            {
                if (pair.Player?.UserId is { } uid &&
                    rule.Sessions.TryGetValue(uid, out var session) &&
                    session.SelectedRoleId != null)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
