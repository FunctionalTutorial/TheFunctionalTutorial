using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Content.IntegrationTests.Fixtures;
using Content.Server._Functional.TutorialServer;
using Content.Server.NPC.HTN;
using Content.Shared._Functional.TutorialServer;
using Content.Shared.Holopad;
using Content.Shared.Humanoid.Prototypes;
using Content.Shared.Movement.Components;
using NUnit.Framework;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._Functional.TutorialServer;

/// <summary>
/// Guards the Items and Survival curriculum. Shared checks live in
/// <see cref="TutorialCurriculumAssertions"/>; here are the cues, the species gate and picker order.
/// </summary>
[TestFixture]
[TestOf(typeof(TutorialCueSystem))]
public sealed class TutorialItemsTests : GameTest
{
    private const string RoleId = "TutorialItems";
    private const string MentorId = "TutorialHoloMentorItems";
    private const string BasicsRoleId = "TutorialBasics";

    [Test]
    public async Task TutorialItems_CurriculumResolvesEveryLocaleString()
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
    public async Task TutorialItems_ControlHintsUseKeybindMarkupNotLiteralKeys()
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
    public async Task TutorialItems_CoachHasAnInCharacterLineForEverySubGoal()
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

    /// <summary>A drill naming a tag, prototype or component that does not exist never completes.</summary>
    [Test]
    public async Task TutorialItems_EverySensorReferenceResolves()
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

    [Test]
    public async Task TutorialItems_MentorIsProjectedRatherThanWalked()
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
    /// A cue on an unknown sub-goal never fires at all; a cue keyed past the end of her lines
    /// quietly falls back to its backstop and goes off on a stopwatch instead of on the beat.
    /// </summary>
    [Test]
    public async Task TutorialItems_EveryStagedCueNamesARealSubGoal()
    {
        var server = Pair.Server;
        var protos = server.ProtoMan;

        await server.WaitAssertion(() =>
        {
            // Game-wide now that a second curriculum stages cues of its own; see the assertion.
            TutorialCurriculumAssertions.EveryStagedCueNamesARealSubGoal(protos);
        });
    }

    /// <summary>
    /// Props that exist only so a drill can name one copy of an ordinary object, mapped to what
    /// they copy. Renaming the furniture would teach a word for a thing that is not on the station.
    /// </summary>
    private static readonly (string Tutorial, string Upstream)[] ReskinnedProps =
    [
        ("TutorialWehPlushie", "PlushieLizard"),
        ("TutorialTrainingLantern", "Lantern"),
        ("TutorialBreachGate", "AirlockGlass"),
    ];

    [Test]
    public async Task TutorialItems_ReskinnedPropsKeepTheirRealNames()
    {
        var server = Pair.Server;
        var protos = server.ProtoMan;

        await server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                foreach (var (tutorial, upstream) in ReskinnedProps)
                {
                    Assert.That(protos.TryIndex<EntityPrototype>(tutorial, out var mine), Is.True, tutorial);
                    Assert.That(protos.TryIndex<EntityPrototype>(upstream, out var theirs), Is.True, upstream);

                    Assert.That(mine!.Name, Is.EqualTo(theirs!.Name),
                        $"{tutorial} calls itself something {upstream} does not");
                    Assert.That(mine.Description, Is.EqualTo(theirs.Description),
                        $"{tutorial} describes itself differently to {upstream}");
                }
            });
        });
    }

    /// <summary>
    /// A sub-goal gate whose id is not in the curriculum is an airlock nothing will ever unbolt.
    /// </summary>
    [Test]
    public async Task TutorialItems_EverySubGoalGateNamesARealSubGoal()
    {
        var server = Pair.Server;
        var protos = server.ProtoMan;

        await server.WaitAssertion(() =>
        {
            Assert.That(protos.TryIndex<TutorialRolePrototype>(RoleId, out var role), Is.True);

            var subGoalIds = role!.Goals.SelectMany(g => g.SubGoals).Select(s => s.Id).ToHashSet();
            var keyed = new List<string>();

            foreach (var proto in protos.EnumeratePrototypes<EntityPrototype>())
            {
                if (!proto.TryGetComponent<TutorialGateDoorComponent>(out var gate) ||
                    string.IsNullOrEmpty(gate.UnlockAtSubGoalId))
                    continue;

                keyed.Add(proto.ID);
                Assert.That(subGoalIds, Does.Contain(gate.UnlockAtSubGoalId),
                    $"{proto.ID} unlocks on sub-goal '{gate.UnlockAtSubGoalId}', which is not in {RoleId}");
            }

            Assert.That(keyed, Does.Contain("TutorialBreachGate"),
                "the breach gate is gone; nothing keeps the player out of the chamber the charge goes off in");
        });
    }

    /// <summary>
    /// The shutter opens on pry-firelock and the charge fires on a fuse lit by breach-brief. If
    /// that beat can end first, the shutter lifts on a chamber the explosion has not hit yet.
    /// </summary>
    [Test]
    public async Task TutorialItems_TheBlastShutterCannotOpenBeforeTheCharge()
    {
        var server = Pair.Server;
        var protos = server.ProtoMan;

        await server.WaitAssertion(() =>
        {
            Assert.That(protos.TryIndex<TutorialRolePrototype>(RoleId, out var role), Is.True);
            Assert.That(protos.TryIndex<EntityPrototype>("TutorialBreachGate", out var gateProto), Is.True);
            Assert.That(protos.TryIndex<EntityPrototype>("TutorialBreachCue", out var cueProto), Is.True);
            Assert.That(gateProto!.TryGetComponent<TutorialGateDoorComponent>(out var gate), Is.True);
            Assert.That(cueProto!.TryGetComponent<TutorialCueComponent>(out var cue), Is.True);

            var subs = role!.Goals.SelectMany(g => g.SubGoals).ToList();
            var cueIndex = subs.FindIndex(s => s.Id == cue!.SubGoalId);
            var gateIndex = subs.FindIndex(s => s.Id == gate!.UnlockAtSubGoalId);

            Assert.Multiple(() =>
            {
                Assert.That(cueIndex, Is.GreaterThanOrEqualTo(0));
                Assert.That(gateIndex, Is.GreaterThan(cueIndex),
                    "the shutter must open after the beat that lights the fuse, not on it");

                // Only the fuse beat is guaranteed to be running while the fuse burns.
                var fuseBeat = subs[cueIndex];
                Assert.That(fuseBeat.AutoAdvanceSeconds, Is.Not.Null,
                    "the fuse beat ends on a player action, so nothing bounds how early the shutter opens");
                Assert.That(fuseBeat.AutoAdvanceSeconds!.Value, Is.GreaterThan(cue!.Delay.TotalSeconds),
                    $"'{fuseBeat.Id}' can end after {fuseBeat.AutoAdvanceSeconds}s but the charge takes {cue.Delay.TotalSeconds}s");
            });
        });
    }

    /// <summary>Vox arrive wearing a tank harness and mask, pre-completing half of the chamber.</summary>
    [Test]
    public async Task TutorialItems_IsBlockedForVox()
    {
        var server = Pair.Server;
        var protos = server.ProtoMan;

        await server.WaitAssertion(() =>
        {
            Assert.That(protos.TryIndex<TutorialRolePrototype>(RoleId, out var role), Is.True);

            Assert.Multiple(() =>
            {
                foreach (var species in role!.BlockedSpecies)
                {
                    Assert.That(protos.HasIndex<SpeciesPrototype>(species.Id), Is.True,
                        $"blockedSpecies names unknown species '{species.Id}'");
                }

                Assert.That(role!.BlockedSpecies.Select(s => s.Id), Does.Contain("Vox"));
            });
        });
    }

    /// <summary>Start Here is read top to bottom, so its order must not be alphabetical luck.</summary>
    [Test]
    public async Task TutorialItems_SortsAfterBasicControls()
    {
        var server = Pair.Server;
        var protos = server.ProtoMan;
        var tutorial = server.System<TutorialServerRuleSystem>();

        await server.WaitAssertion(() =>
        {
            Assert.That(protos.TryIndex<TutorialRolePrototype>(RoleId, out var role), Is.True);
            Assert.That(protos.TryIndex<TutorialRolePrototype>(BasicsRoleId, out var basics), Is.True);
            Assert.That(role!.Category, Is.EqualTo(basics!.Category));
            Assert.That(role.PickerOrder, Is.GreaterThan(basics.PickerOrder));

            var entries = tutorial.BuildPickerEntries();
            var ids = entries.Select(e => e.RoleId).ToList();
            Assert.That(ids.IndexOf(BasicsRoleId), Is.LessThan(ids.IndexOf(RoleId)));
            Assert.That(ids[0], Is.EqualTo(BasicsRoleId));
        });
    }
}
