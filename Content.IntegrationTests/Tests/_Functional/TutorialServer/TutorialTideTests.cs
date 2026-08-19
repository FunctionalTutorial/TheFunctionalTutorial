using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Content.IntegrationTests.Fixtures;
using Content.Server._Functional.TutorialServer;
using Content.Server.NPC.HTN;
using Content.Server.NPC;
using Content.Shared._Functional.TutorialServer;
using Content.Shared.Access.Components;
using Content.Shared.Containers;
using Content.Shared.Tag;
using NUnit.Framework;
using Robust.Shared.GameObjects;
using Robust.Shared.Localization;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._Functional.TutorialServer;

/// <summary>
/// Guards the Passenger (greytide) curriculum. Shared checks live in
/// <see cref="TutorialCurriculumAssertions"/>; here are the things specific to this one — the
/// coach who leads instead of follows, the props that must keep the station's own names, the
/// welder that must not exist, and the beats that complete on being refused.
/// </summary>
[TestFixture]
[TestOf(typeof(TutorialGoalSensorSystem))]
public sealed class TutorialTideTests : GameTest
{
    private const string RoleId = "TutorialTide";
    private const string MentorId = "TutorialTideMentor";
    private const string NanciId = "TutorialTideNanci";
    private const string OfficerId = "TutorialTideSecOfficer";
    private const string HolopadId = "TutorialTideHolopad";
    private const string FarewellId = "farewell";

    /// <summary>
    /// Props that are allowed their own name: markers and cues are editor furniture the player
    /// never sees, and the two bystanders are people rather than items.
    /// </summary>
    private static readonly HashSet<string> MayBeNamed =
    [
        "TutorialTideMarker",
        "TutorialTideCueGloves",
        "TutorialTideCueRefusal",
        "TutorialTideCueBoots",
        "TutorialTideCueArrest",
        "TutorialTideMentor",
        "TutorialTideHoP",
        "TutorialTideSecOfficer",
    ];

    [Test]
    public async Task TutorialTide_CurriculumResolvesEveryLocaleString()
    {
        var server = Pair.Server;
        var protos = server.ProtoMan;

        await server.WaitAssertion(() =>
        {
            Assert.That(protos.TryIndex<TutorialRolePrototype>(RoleId, out var role), Is.True);
            TutorialCurriculumAssertions.EveryLocaleStringResolves(role!);
        });
    }

    [Test]
    public async Task TutorialTide_ControlHintsUseKeybindMarkupNotLiteralKeys()
    {
        var server = Pair.Server;
        var protos = server.ProtoMan;

        await server.WaitAssertion(() =>
        {
            Assert.That(protos.TryIndex<TutorialRolePrototype>(RoleId, out var role), Is.True);
            TutorialCurriculumAssertions.ControlHintsUseKeybindMarkup(role!);
        });
    }

    [Test]
    public async Task TutorialTide_MentorHasAnInCharacterLineForEverySubGoal()
    {
        var server = Pair.Server;
        var protos = server.ProtoMan;

        await server.WaitAssertion(() =>
        {
            Assert.That(protos.TryIndex<TutorialRolePrototype>(RoleId, out var role), Is.True);
            Assert.That(protos.TryIndex<EntityPrototype>(MentorId, out var mentor), Is.True);
            TutorialCurriculumAssertions.CoachSpeaksForEverySubGoal(role!, mentor!);
        });
    }

    /// <summary>A drill naming a tag, prototype or marker that does not exist never completes.</summary>
    [Test]
    public async Task TutorialTide_EverySensorReferenceResolves()
    {
        var server = Pair.Server;
        var protos = server.ProtoMan;
        var compFactory = server.ResolveDependency<IComponentFactory>();

        await server.WaitAssertion(() =>
        {
            Assert.That(protos.TryIndex<TutorialRolePrototype>(RoleId, out var role), Is.True);
            TutorialCurriculumAssertions.EverySensorReferenceResolves(role!, protos, compFactory);
        });
    }

    /// <summary>
    /// He leads and the player follows, which is the opposite of every other walking coach. If this
    /// ever flips back to Walk the chambers still work, but the lesson stops being "keep up with
    /// him" and the walk points become scenery nothing uses.
    /// </summary>
    [Test]
    public async Task TutorialTide_MentorLeadsRatherThanFollows()
    {
        var server = Pair.Server;
        var protos = server.ProtoMan;

        await server.WaitAssertion(() =>
        {
            Assert.That(protos.TryIndex<TutorialRolePrototype>(RoleId, out var role), Is.True);

            Assert.Multiple(() =>
            {
                Assert.That(role!.MentorMode, Is.EqualTo(TutorialMentorMode.Lead));
                Assert.That(role.MentorFollows, Is.False,
                    "a leading coach must not also be handed to the follow system");

                Assert.That(protos.TryIndex<EntityPrototype>(MentorId, out var mentor), Is.True);
                Assert.That(mentor!.TryGetComponent<TutorialTrainerComponent>(out var trainer), Is.True);

                // Without this he talks to an empty room the moment he walks ahead, and "follow
                // him" stops being enforced by anything.
                Assert.That(trainer!.SpeakRange, Is.Not.Null,
                    "a leading coach needs a speak range or the player never has to catch up");
            });
        });
    }

    /// <summary>
    /// Nothing in this curriculum renames a real item. A player who learns "mechanical toolbox"
    /// here has to be able to find "mechanical toolbox" on the station tomorrow, so a tutorial prop
    /// is its parent plus sensors and nothing else.
    /// </summary>
    [Test]
    public async Task TutorialTide_PropsKeepTheNamesTheStationGivesThem()
    {
        var server = Pair.Server;
        var protos = server.ProtoMan;

        await server.WaitAssertion(() =>
        {
            var checkedAny = false;

            Assert.Multiple(() =>
            {
                foreach (var proto in protos.EnumeratePrototypes<EntityPrototype>())
                {
                    if (!proto.ID.StartsWith("TutorialTide") || MayBeNamed.Contains(proto.ID))
                        continue;

                    var parentId = proto.Parents?.FirstOrDefault();
                    if (parentId == null || !protos.TryIndex<EntityPrototype>(parentId, out var parent))
                        continue;

                    checkedAny = true;
                    Assert.That(proto.Name, Is.EqualTo(parent!.Name),
                        $"{proto.ID} renames {parentId}");
                    Assert.That(proto.Description, Is.EqualTo(parent.Description),
                        $"{proto.ID} re-describes {parentId}");
                }
            });

            Assert.That(checkedAny, Is.True, "found no tide props to check");
        });
    }

    /// <summary>
    /// The vault door's panel is plated over and there is no welder in the curriculum, which is the
    /// only reason the disposal pipe is the way in. A welder turning up in the toolbox or the belt
    /// would silently make chamber 5 a second hacking drill.
    /// </summary>
    [Test]
    public async Task TutorialTide_NoWelderAnywhereInTheCurriculum()
    {
        var server = Pair.Server;
        var protos = server.ProtoMan;

        await server.WaitAssertion(() =>
        {
            Assert.That(protos.TryIndex<EntityPrototype>("TutorialTideBelt", out var belt), Is.True);

            // No StorageFill at all is the intended state: the player loads it from the toolbox.
            Assert.That(belt!.Components.ContainsKey("StorageFill"), Is.False,
                "the tide belt must start empty; the player fills it from the toolbox");

            Assert.That(protos.TryIndex<EntityPrototype>("TutorialTideToolbox", out var toolbox), Is.True);
            Assert.That(toolbox!.TryGetComponent<ContainerFillComponent>(out var fill),
                Is.True, "the tide toolbox needs a fixed fill; the vanilla one rolls for its tools");

            var contents = fill!.Containers.Values.SelectMany(v => v).ToList();
            Assert.Multiple(() =>
            {
                Assert.That(contents, Does.Not.Contain("Welder"));
                foreach (var tool in new[] { "Screwdriver", "Wirecutter", "Crowbar", "Multitool" })
                    Assert.That(contents, Does.Contain(tool), $"the curriculum needs a {tool}");
            });
        });
    }

    /// <summary>
    /// Two beats complete when a door <i>refuses</i> the player. If either is ever changed to a
    /// success condition the chamber stops teaching what it is for.
    /// </summary>
    [Test]
    public async Task TutorialTide_TheDoorsThatSayNoCompleteOnRefusal()
    {
        var server = Pair.Server;
        var protos = server.ProtoMan;

        await server.WaitAssertion(() =>
        {
            Assert.That(protos.TryIndex<TutorialRolePrototype>(RoleId, out var role), Is.True);

            var denied = role!.Goals
                .SelectMany(g => g.SubGoals)
                .Where(s => s.Complete == TutorialStepComplete.DoorAccessDenied)
                .Select(s => s.Id)
                .ToList();

            Assert.That(denied, Is.EquivalentTo(new[] { "try-cargo-door", "try-hat-door" }));
        });
    }

    /// <summary>
    /// Chamber 4 takes the power off the door before it asks for the bolts, and that order is the
    /// whole lesson: <c>TrySetBoltDown</c> refuses on an unpowered door, so the player has to work
    /// out that the bolts need feeding, mend a power wire, raise them live, and only then kill it
    /// again for the crowbar. Flip these two beats round and the chamber teaches nothing but the
    /// order of four tools.
    /// </summary>
    [Test]
    public async Task TutorialTide_TheHackTeachesPowerBeforeBolts()
    {
        var server = Pair.Server;
        var protos = server.ProtoMan;

        await server.WaitAssertion(() =>
        {
            Assert.That(protos.TryIndex<TutorialRolePrototype>(RoleId, out var role), Is.True);

            var storage = role!.Goals.FirstOrDefault(g => g.Id == "storage");
            Assert.That(storage, Is.Not.Null, "the materials storage chamber is gone");

            var beats = storage!.SubGoals.Select(s => s.Id).ToList();
            Assert.Multiple(() =>
            {
                Assert.That(beats.IndexOf("cut-power"), Is.LessThan(beats.IndexOf("unbolt-door")),
                    "the power has to come off first or there is no moment to learn from");
                Assert.That(beats.IndexOf("unbolt-door"), Is.LessThan(beats.IndexOf("cut-power-again")),
                    "the door has to be live for the bolts and dead for the crowbar, in that order");
                Assert.That(beats.IndexOf("cut-power-again"), Is.LessThan(beats.IndexOf("pry-door")),
                    "a powered airlock does not yield to a crowbar");

                // Every player arrives at this beat with the power already off, because the beat
                // before it told them to cut it. That makes the explanation the lesson rather than
                // a correction, so it belongs in his ordinary script and there is no failure state
                // to detect: a retry line here would fire for everyone, every run.
                var unbolt = storage.SubGoals.First(s => s.Id == "unbolt-door");
                Assert.That(beats.IndexOf("cut-power"), Is.LessThan(beats.IndexOf("restore-power")),
                    "there is nothing to reconnect until the power has been cut");
                Assert.That(beats.IndexOf("restore-power"), Is.LessThan(beats.IndexOf("unbolt-door")),
                    "the multitool is useless on a dead door, so mending comes first");

                // Not the bolt lesson any more, which restore-power now teaches outright. This is
                // for the player who pulses a power wire mid-beat and is left looking at a door
                // that has quietly stopped answering.
                Assert.That(unbolt.RetryLine, Is.Not.Null,
                    "nothing on screen explains a door that went dark under the multitool");
            });
        });
    }

    /// <summary>
    /// A placed marker has to be visible to the person placing it. Checked on the client, which is
    /// the only side that has sprites, and game-wide rather than tide-only because the trap is in
    /// MarkerBase and catches every curriculum that inherits from it.
    /// </summary>
    [Test]
    public async Task TutorialTide_EveryPlaceableMarkerDrawsSomething()
    {
        await Client.WaitAssertion(() =>
        {
            TutorialCurriculumAssertions.EveryMapperMarkerDrawsSomething(CProtoMan);
        });
    }

    /// <summary>
    /// Every marker a beat reaches for has a prototype carrying that id already, so placing the
    /// map is placing entities rather than typing thirteen strings into thirteen components and
    /// finding out which one was mistyped by playing the whole tutorial.
    /// </summary>
    [Test]
    public async Task TutorialTide_EveryMarkerHasAPrototypeThatBakesItIn()
    {
        var server = Pair.Server;
        var protos = server.ProtoMan;

        await server.WaitAssertion(() =>
        {
            Assert.That(protos.TryIndex<TutorialRolePrototype>(RoleId, out var role), Is.True);

            var baked = new Dictionary<string, string>();
            foreach (var proto in protos.EnumeratePrototypes<EntityPrototype>())
            {
                if (proto.Abstract || proto.HideSpawnMenu)
                    continue;

                if (proto.TryGetComponent<TutorialStepMarkerComponent>(out var marker) &&
                    !string.IsNullOrEmpty(marker.MarkerId))
                {
                    baked[marker.MarkerId] = proto.ID;
                }
            }

            var wanted = role!.Goals
                .SelectMany(g => g.SubGoals)
                .Select(s => s.Marker)
                .Where(m => !string.IsNullOrEmpty(m))
                .Distinct();

            Assert.Multiple(() =>
            {
                foreach (var marker in wanted)
                {
                    Assert.That(baked.ContainsKey(marker!), Is.True,
                        $"no placeable prototype carries marker '{marker}'; a mapper would have to type it in");
                }
            });
        });
    }

    /// <summary>
    /// Every coach that leads can get itself through a door, all three ways at once.
    /// </summary>
    /// <remarks>
    /// Three separate things have to line up and none of them says anything when it is missing.
    /// <c>NavDoors</c> stops the pathfinder planning around every locked door; <c>AllAccess</c> is
    /// what makes that routing honest, since the door still runs its own check when he arrives;
    /// and <c>DoorBumpOpener</c> is what actually pushes an airlock open, because an airlock is
    /// bump-open and answers to that tag and nothing else. Have the first without the other two
    /// and he walks up to a door and paces in front of it until a player opens it for him.
    /// <para>
    /// Scoped to leading coaches. A following coach works gated chamber maps where a sealed door
    /// really does mean the player is unreachable, and its follow system is right to give up and
    /// teleport instead of walking at something that will not open.
    /// </para>
    /// <para>
    /// The tag is the easy one to lose: a child prototype's <c>Tag</c> list replaces its parent's
    /// rather than adding to it, so declaring any tag of your own silently drops the one
    /// <c>species_base</c> gave you.
    /// </para>
    /// </remarks>
    [Test]
    public async Task TutorialTide_EveryLeadingMentorCanGetThroughADoor()
    {
        var server = Pair.Server;
        var protos = server.ProtoMan;
        var entMan = server.EntMan;

        await server.WaitAssertion(() =>
        {
            var leaders = protos.EnumeratePrototypes<TutorialRolePrototype>()
                .Where(r => r.MentorMode == TutorialMentorMode.Lead && r.MentorEntity != null)
                .Select(r => r.MentorEntity!.Value.Id)
                .Distinct()
                .ToList();

            Assert.That(leaders, Is.Not.Empty, "no curriculum has a coach who leads");

            Assert.Multiple(() =>
            {
                foreach (var id in leaders)
                {
                    Assert.That(protos.TryIndex<EntityPrototype>(id, out var proto), Is.True,
                        $"a role names mentor '{id}', which does not exist");

                    Assert.That(proto!.TryGetComponent<TagComponent>(out var tags), Is.True,
                        $"{id} has no tags at all");
                    Assert.That(tags!.Tags, Does.Contain("DoorBumpOpener"),
                        $"{id} cannot push a door open, so it will pace in front of one");

                    Assert.That(proto.TryGetComponent<AccessComponent>(out var acc), Is.True,
                        $"{id} has no access, so every locked door it is routed through refuses it");
                    Assert.That(acc!.Groups, Does.Contain("AllAccess"), $"{id} is not all-access");

                    Assert.That(proto.TryGetComponent<HTNComponent>(out var htn), Is.True,
                        $"{id} leads but has no HTN to walk with");
                    Assert.That(
                        htn!.Blackboard.TryGetValue<bool>(NPCBlackboard.NavDoors, out var nav, entMan)
                        && nav,
                        Is.True,
                        $"{id} plans around doors instead of through them; set NavDoors");
                }
            });
        });
    }

    /// <summary>
    /// The sign-off is N.A.N.C.I.'s, from the holopad in the room, and Kowalski has to be silent
    /// under it. He is face down in cuffs by then, and a coach with no authored line for a beat
    /// reads the objectives checklist aloud instead, so "he has no lines" is not enough on its own.
    /// </summary>
    [Test]
    public async Task TutorialTide_TheLastWordIsSpokenFromTheHolopad()
    {
        var server = Pair.Server;
        var protos = server.ProtoMan;

        await server.WaitAssertion(() =>
        {
            Assert.That(protos.TryIndex<EntityPrototype>(MentorId, out var mentor), Is.True);
            Assert.That(mentor!.TryGetComponent<TutorialTrainerComponent>(out var his), Is.True);

            Assert.That(protos.TryIndex<EntityPrototype>(NanciId, out var nanci), Is.True);
            Assert.That(nanci!.TryGetComponent<TutorialTrainerComponent>(out var hers), Is.True);

            Assert.Multiple(() =>
            {
                Assert.That(his!.SilentSubGoals, Does.Contain(FarewellId),
                    "Kowalski would read the checklist aloud over her");
                Assert.That(his.Lines.Any(l => l.SubGoalId == FarewellId), Is.False,
                    "Kowalski still has the farewell lines");

                var farewell = hers!.Lines.Where(l => l.SubGoalId == FarewellId).ToList();
                Assert.That(farewell, Is.Not.Empty, "nobody speaks the farewell");
                foreach (var line in farewell)
                    Assert.That(Loc.TryGetString(line.Dialogue, out _), Is.True, $"missing {line.Dialogue}");

                // Lead coaches never light a pad — TutorialHoloMentorSystem stands down for them —
                // so the pad in the graduation room has to switch itself on.
                Assert.That(protos.TryIndex<EntityPrototype>(HolopadId, out var pad), Is.True);
                Assert.That(pad!.TryGetComponent<TutorialCueComponent>(out var cue), Is.True,
                    "the graduation holopad never lights up");
                Assert.That(cue!.Effect, Is.EqualTo(TutorialCueEffect.Project));
                Assert.That(cue.SubGoalId, Is.EqualTo(FarewellId));
                Assert.That(cue.Spawn?.Id, Is.EqualTo(NanciId));
                Assert.That(pad.TryGetComponent<TutorialHoloPointComponent>(out _), Is.True,
                    "the pad has to be a holopad, or it lights nothing and looks like nothing");
            });
        });
    }

    /// <summary>
    /// Security comes for him rather than appearing near him. The cue only puts the officer on the
    /// map; the walk is the officer's own, which needs the mover pair a mob only gets from being
    /// played and an HTN to drive them.
    /// </summary>
    [Test]
    public async Task TutorialTide_TheOfficerWalksOverToMakeTheArrest()
    {
        var server = Pair.Server;
        var protos = server.ProtoMan;

        await server.WaitAssertion(() =>
        {
            Assert.That(protos.TryIndex<EntityPrototype>("TutorialTideCueBoots", out var cueProto), Is.True);
            Assert.That(cueProto!.TryGetComponent<TutorialCueComponent>(out var cue), Is.True);

            Assert.That(protos.TryIndex<EntityPrototype>(OfficerId, out var officer), Is.True);
            Assert.That(officer!.TryGetComponent<TagComponent>(out var tags), Is.True);

            Assert.Multiple(() =>
            {
                Assert.That(cue!.Spawn?.Id, Is.EqualTo(OfficerId));
                Assert.That(cue.SpawnFollowTag, Is.EqualTo("TutorialTideTider"),
                    "the officer is spawned and then left standing there");

                // Whoever the cue sends him after has to be findable, and it is the mentor.
                Assert.That(protos.TryIndex<EntityPrototype>(MentorId, out var mentor), Is.True);
                Assert.That(mentor!.TryGetComponent<TagComponent>(out var mentorTags), Is.True);
                Assert.That(mentorTags!.Tags, Does.Contain(cue.SpawnFollowTag!));

                Assert.That(officer.TryGetComponent<HTNComponent>(out _), Is.True,
                    "the officer has no HTN, so nothing walks him anywhere");
                Assert.That(officer.Components.ContainsKey("InputMover"), Is.True,
                    "an NPC with no InputMover has its steering thrown away");
                Assert.That(officer.Components.ContainsKey("MobMover"), Is.True);
                Assert.That(tags!.Tags, Does.Contain("DoorBumpOpener"),
                    "he would stop at the first airlock between him and the arrest");
                Assert.That(officer.TryGetComponent<AccessComponent>(out var access), Is.True);
                Assert.That(access!.Groups, Does.Contain("AllAccess"));
            });
        });
    }

    /// <summary>
    /// Rotation is only ever written while a mob is moving, so a coach who walks somewhere and then
    /// talks keeps whatever direction his last step left him in. The overrides are the beats where
    /// facing the player is wrong; both of them name somebody who is really there.
    /// </summary>
    [Test]
    public async Task TutorialTide_TheMentorFacesWhoeverHeIsTalkingTo()
    {
        var server = Pair.Server;
        var protos = server.ProtoMan;

        await server.WaitAssertion(() =>
        {
            Assert.That(protos.TryIndex<TutorialRolePrototype>(RoleId, out var role), Is.True);
            Assert.That(protos.TryIndex<EntityPrototype>(MentorId, out var mentor), Is.True);
            Assert.That(mentor!.TryGetComponent<TutorialMentorComponent>(out var comp), Is.True);

            var subGoalIds = role!.Goals.SelectMany(g => g.SubGoals).Select(s => s.Id).ToHashSet();

            // Some prototype has to wear the tag: a facing override that resolves to nothing
            // silently falls back to the player, which is the bug it exists to fix.
            var wearers = new HashSet<string>();
            foreach (var proto in protos.EnumeratePrototypes<EntityPrototype>())
            {
                if (!proto.TryGetComponent<TagComponent>(out var worn))
                    continue;

                foreach (var tag in worn.Tags)
                    wearers.Add(tag.Id);
            }

            Assert.That(comp!.Facing, Is.Not.Empty, "the coach never turns to face anyone");

            Assert.Multiple(() =>
            {
                foreach (var facing in comp.Facing)
                {
                    Assert.That(subGoalIds, Does.Contain(facing.SubGoalId),
                        $"the coach faces something on '{facing.SubGoalId}', which is not a sub-goal");
                    Assert.That(protos.HasIndex<TagPrototype>(facing.Tag), Is.True,
                        $"facing tag '{facing.Tag}' is not a tag");
                    Assert.That(wearers, Does.Contain(facing.Tag),
                        $"nothing in the game carries '{facing.Tag}', so he would face the player instead");
                }
            });
        });
    }

    /// <summary>
    /// It is the Passenger tutorial, listed under Civilian, and it is playable. The stub flag is
    /// what greys a picker entry and puts an "incomplete" confirmation in front of it.
    /// </summary>
    [Test]
    public async Task TutorialTide_IsThePlayablePassengerEntryUnderCivilian()
    {
        var server = Pair.Server;
        var protos = server.ProtoMan;

        await server.WaitAssertion(() =>
        {
            Assert.That(protos.TryIndex<TutorialRolePrototype>(RoleId, out var role), Is.True);

            Assert.Multiple(() =>
            {
                Assert.That(role!.Stub, Is.False);
                Assert.That(role.Category, Is.EqualTo("Civilian"));
                Assert.That(role.Job?.Id, Is.EqualTo("Passenger"));

                // A hand-authored slice of station. A role with no map, room or template falls back
                // to StubPractice, which is one empty box, and every drill in here would be missing
                // its prop without anything saying so.
                Assert.That(role.Room, Is.Null, "the procedural suite was replaced by the map");
                Assert.That(role.Map.ToString(),
                    Is.EqualTo("/Maps/_Functional/TutorialServer/Roles/Tide.yml"));
                Assert.That(role.PracticeSpawns, Is.Empty,
                    "practiceSpawns land relative to the player on a loaded map, not per section");
            });
        });
    }
}
