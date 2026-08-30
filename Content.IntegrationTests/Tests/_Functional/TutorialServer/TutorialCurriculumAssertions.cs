using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Content.Server._Functional.TutorialServer;
using Content.Shared._Functional.TutorialServer;
using Content.Shared.Tag;
using NUnit.Framework;
using Robust.Client.GameObjects;
using Robust.Shared.GameObjects;
using Robust.Shared.Localization;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._Functional.TutorialServer;

/// <summary>
/// Checks that hold for any N.A.N.C.I. curriculum. They guard the three-channel contract: banner
/// names a control, checklist names the objective, everything else is spoken in character.
/// </summary>
public static class TutorialCurriculumAssertions
{
    /// <summary>Bindings that must never be baked into a locale string; players rebind them.</summary>
    private static readonly string[] HardcodedKeyNames =
    [
        "WASD", "W A S D", "Shift", "Numpad", "numpad key",
    ];

    public static void EveryLocaleStringResolves(TutorialRolePrototype role)
    {
        Assert.That(role.Goals, Is.Not.Empty);

        Assert.Multiple(() =>
        {
            Assert.That(Loc.TryGetString(role.Name!, out _), Is.True, $"missing {role.Name}");

            foreach (var goal in role.Goals)
            {
                Assert.That(Loc.TryGetString(goal.Title, out _), Is.True, $"missing {goal.Title}");

                foreach (var sub in goal.SubGoals)
                {
                    Assert.That(Loc.TryGetString(sub.Text, out _), Is.True, $"missing {sub.Text}");

                    if (!string.IsNullOrEmpty(sub.ControlHint))
                        Assert.That(Loc.TryGetString(sub.ControlHint, out _), Is.True, $"missing {sub.ControlHint}");

                    if (!string.IsNullOrEmpty(sub.StuckHint))
                        Assert.That(Loc.TryGetString(sub.StuckHint, out _), Is.True, $"missing {sub.StuckHint}");

                    if (sub.RetryLine is { } retry)
                        Assert.That(Loc.TryGetString(retry, out _), Is.True, $"missing {retry}");
                }
            }
        });
    }

    /// <summary>Control hints may name keys, but only through markup that resolves real bindings.</summary>
    public static void ControlHintsUseKeybindMarkup(TutorialRolePrototype role)
    {
        var checkedAny = false;

        Assert.Multiple(() =>
        {
            foreach (var sub in role.Goals.SelectMany(g => g.SubGoals))
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

                foreach (Match match in Regex.Matches(text, @"\[keybind[^\]]*\]"))
                {
                    Assert.That(match.Value, Does.Match("^\\[keybind=\"[A-Za-z0-9]+\"\\]$"),
                        $"{sub.ControlHint} has malformed keybind markup: {match.Value}");
                }
            }
        });

        Assert.That(checkedAny, Is.True, "curriculum authored no control hints at all");
    }

    /// <summary>
    /// The coach falls back to reading the objectives checklist aloud when a sub-goal has no
    /// override, which would break the in-character rule. A beat handed to another voice is
    /// exempt, but only when the coach declares it: that is what turns the fallback off.
    /// </summary>
    public static void CoachSpeaksForEverySubGoal(TutorialRolePrototype role, EntityPrototype mentor)
    {
        Assert.That(mentor.TryGetComponent<TutorialTrainerComponent>(out var trainer), Is.True);

        var lines = trainer!.Lines.ToLookup(l => l.SubGoalId, l => l.Dialogue);
        var subGoalIds = role.Goals.SelectMany(g => g.SubGoals).Select(s => s.Id).ToHashSet();
        var silent = trainer.SilentSubGoals.ToHashSet();

        Assert.Multiple(() =>
        {
            foreach (var id in silent)
            {
                Assert.That(subGoalIds, Does.Contain(id),
                    $"coach is silent on '{id}', which is not a sub-goal of {role.ID}");
                Assert.That(lines.Contains(id), Is.False,
                    $"coach is both silent on '{id}' and has lines for it");
            }

            foreach (var sub in role.Goals.SelectMany(g => g.SubGoals))
            {
                if (silent.Contains(sub.Id))
                    continue;

                Assert.That(lines.Contains(sub.Id), Is.True,
                    $"sub-goal '{sub.Id}' has no coach line; N.A.N.C.I. would read the checklist aloud");

                foreach (var dialogue in lines[sub.Id])
                {
                    var text = Loc.GetString(dialogue);
                    Assert.That(string.IsNullOrWhiteSpace(text), Is.False, $"missing {dialogue}");
                    // One sentence per bubble, and no em dashes.
                    Assert.That(text, Does.Not.Contain("—"), $"{dialogue} uses an em dash");
                }
            }

            // A line keyed to a sub-goal that no longer exists is never spoken, and never reported.
            foreach (var group in lines)
            {
                Assert.That(subGoalIds, Does.Contain(group.Key),
                    $"coach has lines for '{group.Key}', which is not a sub-goal of {role.ID}");
            }
        });
    }

    /// <summary>
    /// Every beat the player has to act on leaves something in the banner: the key where one is
    /// taught, the objective line otherwise. Beats that play themselves out leave it blank, since
    /// the only thing they could say is "listen" and it would outlast her saying anything.
    /// </summary>
    /// <remarks>
    /// Walks the curriculum by advancing the live session, since the banner is chosen where the
    /// sub-goal is published rather than where it is authored.
    /// </remarks>
    public static void BannerMatchesWhatTheBeatAsksFor(
        TutorialServerRuleSystem tutorial,
        IEntityManager entMan,
        EntityUid mob,
        TutorialRolePrototype role)
    {
        Assert.That(entMan.TryGetComponent<TutorialParticipantComponent>(mob, out var part), Is.True,
            "player is not in a tutorial session");

        var expected = role.Goals.Sum(g => g.SubGoals.Count);
        var walked = 0;

        Assert.Multiple(() =>
        {
            while (walked <= expected && tutorial.TryGetCurrentSubGoal(mob, part!, out var sub))
            {
                Assert.That(tutorial.TryGetSession(mob, out var session), Is.True);
                var banner = session.PendingControlHint;

                if (sub.Complete == TutorialStepComplete.Acknowledge && sub.AutoAdvanceSeconds != null)
                {
                    Assert.That(banner, Is.Null.Or.Empty,
                        $"'{sub.Id}' ends on its own, so its banner would still be up after she stopped");
                }
                else if (string.IsNullOrEmpty(banner))
                {
                    Assert.Fail($"'{sub.Id}' leaves the banner blank once the coach stops talking");
                }
                else
                {
                    Assert.That(Loc.TryGetString(banner, out _), Is.True,
                        $"'{sub.Id}' banners '{banner}', which does not resolve");
                }

                walked++;
                tutorial.AdvanceSubGoal(mob);
            }
        });

        Assert.That(walked, Is.EqualTo(expected), "did not walk the whole curriculum");
    }

    /// <summary>
    /// Every mapper-facing marker, cue and walk point draws something.
    /// </summary>
    /// <remarks>
    /// <c>MarkerBase</c> gives its children an RSI but no layers, and a sprite with no layers is
    /// never added to the render tree at all. The failure is completely silent: the prototype is in
    /// the spawn list, the mapper places it, and an invisible entity lands on the tile. Wants the
    /// <i>client</i> prototype manager, since SpriteComponent is not registered on the server.
    /// </remarks>
    public static void EveryMapperMarkerDrawsSomething(IPrototypeManager clientProtos)
    {
        var checkedAny = false;

        Assert.Multiple(() =>
        {
            foreach (var proto in clientProtos.EnumeratePrototypes<EntityPrototype>())
            {
                if (proto.Abstract || proto.HideSpawnMenu)
                    continue;

                if (!proto.TryGetComponent<TutorialStepMarkerComponent>(out _) &&
                    !proto.TryGetComponent<TutorialCueComponent>(out _) &&
                    !proto.TryGetComponent<TutorialWalkPointComponent>(out _))
                {
                    continue;
                }

                checkedAny = true;

                Assert.That(proto.TryGetComponent<SpriteComponent>(out var sprite), Is.True,
                    $"{proto.ID} is placeable but has no sprite at all");

                if (sprite == null)
                    continue;

                Assert.That(sprite.AllLayers.Any(), Is.True,
                    $"{proto.ID} has a sprite with no layers, so it draws nothing when placed");
            }
        });

        Assert.That(checkedAny, Is.True, "found no placeable tutorial markers to check");
    }

    /// <summary>
    /// Every staged cue in the game names a sub-goal some curriculum actually has, and fires on a
    /// line that curriculum's coach actually reaches.
    /// </summary>
    /// <remarks>
    /// Game-wide rather than per-curriculum, because a cue prototype carries no reference back to
    /// the role it was written for: all it has is a sub-goal id, so the only honest check is that
    /// the id exists somewhere and is reachable there. Scoping it to one role was fine while only
    /// Items staged anything, and started failing the moment a second curriculum did.
    /// </remarks>
    public static void EveryStagedCueNamesARealSubGoal(IPrototypeManager protos)
    {
        // Sub-goal id -> the most lines any coach speaks for it. Ids are only unique within a
        // curriculum, and a cue only has to be reachable in the one it was authored against, so
        // the most generous count is the right bar.
        var reachableLines = new Dictionary<string, int>();

        foreach (var role in protos.EnumeratePrototypes<TutorialRolePrototype>())
        {
            if (role.MentorEntity is not { } mentorId ||
                !protos.TryIndex<EntityPrototype>(mentorId, out var mentor) ||
                !mentor.TryGetComponent<TutorialTrainerComponent>(out var trainer))
            {
                continue;
            }

            var spoken = trainer.Lines.ToLookup(l => l.SubGoalId);
            foreach (var sub in role.Goals.SelectMany(g => g.SubGoals))
            {
                var count = spoken[sub.Id].Count();
                if (!reachableLines.TryGetValue(sub.Id, out var best) || count > best)
                    reachableLines[sub.Id] = count;
            }
        }

        var cued = new List<string>();

        Assert.Multiple(() =>
        {
            foreach (var proto in protos.EnumeratePrototypes<EntityPrototype>())
            {
                if (!proto.TryGetComponent<TutorialCueComponent>(out var cue))
                    continue;

                cued.Add(proto.ID);
                Assert.That(reachableLines.ContainsKey(cue.SubGoalId), Is.True,
                    $"{proto.ID} cues sub-goal '{cue.SubGoalId}', which no curriculum has");

                if (cue.AfterLine is not { } afterLine)
                    continue;

                Assert.That(afterLine, Is.GreaterThan(0), $"{proto.ID} counts coach lines from one");
                Assert.That(reachableLines.GetValueOrDefault(cue.SubGoalId), Is.GreaterThanOrEqualTo(afterLine),
                    $"{proto.ID} fires after line {afterLine} of '{cue.SubGoalId}', which is never reached");
            }
        });

        Assert.That(cued, Is.Not.Empty, "no staged cues exist at all");
    }

    /// <summary>Everything a sub-goal names has to exist, or its drill silently never completes.</summary>
    public static void EverySensorReferenceResolves(
        TutorialRolePrototype role,
        IPrototypeManager protos,
        IComponentFactory compFactory)
    {
        var markers = new HashSet<string>();
        foreach (var proto in protos.EnumeratePrototypes<EntityPrototype>())
        {
            if (proto.TryGetComponent<TutorialStepMarkerComponent>(out var marker) &&
                !string.IsNullOrEmpty(marker.MarkerId))
            {
                markers.Add(marker.MarkerId);
            }
        }

        // A marker id may equally be stamped per placement from the role's own practiceSpawns,
        // which is how a curriculum reuses one marker prototype across every chamber instead of
        // carrying an entity per spot.
        foreach (var spawn in role.PracticeSpawns)
        {
            if (!string.IsNullOrEmpty(spawn.Marker))
                markers.Add(spawn.Marker);
        }

        Assert.Multiple(() =>
        {
            foreach (var sub in role.Goals.SelectMany(g => g.SubGoals))
            {
                if (sub.Entity is { } entity)
                {
                    Assert.That(protos.HasIndex<EntityPrototype>(entity.Id), Is.True,
                        $"{sub.Id}: unknown entity prototype '{entity.Id}'");
                }

                if (!string.IsNullOrEmpty(sub.Tag))
                {
                    Assert.That(protos.HasIndex<TagPrototype>(sub.Tag), Is.True,
                        $"{sub.Id}: unknown tag '{sub.Tag}'");
                }

                if (!string.IsNullOrEmpty(sub.Component))
                {
                    Assert.That(compFactory.TryGetRegistration(sub.Component, out _), Is.True,
                        $"{sub.Id}: unknown component '{sub.Component}'");
                }

                if (!string.IsNullOrEmpty(sub.Marker))
                {
                    Assert.That(markers, Does.Contain(sub.Marker),
                        $"{sub.Id}: no prototype stamps marker '{sub.Marker}'");
                }
            }
        });
    }
}
