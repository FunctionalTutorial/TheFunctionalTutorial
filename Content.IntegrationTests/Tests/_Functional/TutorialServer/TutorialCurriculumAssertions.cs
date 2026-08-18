using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Content.Shared._Functional.TutorialServer;
using Content.Shared.Tag;
using NUnit.Framework;
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
    /// override, which would break the in-character rule.
    /// </summary>
    public static void CoachSpeaksForEverySubGoal(TutorialRolePrototype role, EntityPrototype mentor)
    {
        Assert.That(mentor.TryGetComponent<TutorialTrainerComponent>(out var trainer), Is.True);

        var lines = trainer!.Lines.ToLookup(l => l.SubGoalId, l => l.Dialogue);
        var subGoalIds = role.Goals.SelectMany(g => g.SubGoals).Select(s => s.Id).ToHashSet();

        Assert.Multiple(() =>
        {
            foreach (var sub in role.Goals.SelectMany(g => g.SubGoals))
            {
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
