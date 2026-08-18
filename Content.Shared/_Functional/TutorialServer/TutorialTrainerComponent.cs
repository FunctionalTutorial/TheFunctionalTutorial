using Robust.Shared.GameStates;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Shared._Functional.TutorialServer;

/// <summary>
/// Scripted tutorial coach that speaks lines keyed by the player's current sub-goal id.
/// </summary>
/// <remarks>
/// Multiple <see cref="TutorialTrainerLine"/> entries may share a sub-goal id; they queue and are
/// spoken one at a time. Keep each one to a single sentence — a wall of text in a speech bubble
/// scrolls away before a new player has read it.
/// </remarks>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentPause]
public sealed partial class TutorialTrainerComponent : Component
{
    /// <summary>
    /// Dialogue lines spoken when the matching sub-goal is current, in author order.
    /// </summary>
    [DataField]
    public List<TutorialTrainerLine> Lines = new();

    /// <summary>
    /// Sub-goal id whose lines were last queued (change detection; no timed reminders).
    /// </summary>
    [DataField]
    public string? LastSpokenSubGoal;

    /// <summary>
    /// Lines still waiting to be spoken for <see cref="LastSpokenSubGoal"/>.
    /// </summary>
    [ViewVariables]
    public Queue<TutorialPendingLine> PendingLines = new();

    /// <summary>
    /// When the next queued line may be spoken. Null while waiting on the player to come close.
    /// </summary>
    [ViewVariables]
    public TimeSpan? NextLineAt;

    /// <summary>
    /// Lines of <see cref="LastSpokenSubGoal"/> said out loud, reset when the segment changes, so a
    /// staged effect can land on a line of the script rather than on a stopwatch.
    /// </summary>
    [ViewVariables]
    public int LinesSpoken;

    /// <summary>
    /// How close the player must get before the coach starts a queued segment. Null speaks as soon
    /// as the sub-goal becomes current, which is what walking mentors want since they follow you.
    /// A holopad coach uses a small radius so the player walks up to them rather than catching
    /// dialogue through a doorway.
    /// </summary>
    [DataField]
    public float? SpeakRange;

    /// <summary>
    /// Pause after the player comes in range before the first line of a segment. Gives them a beat
    /// to arrive and look at the coach instead of reading mid-stride.
    /// </summary>
    [DataField]
    public TimeSpan StartDelay = TimeSpan.Zero;

    /// <summary>
    /// Extra pause before the very first thing this coach ever says, on top of
    /// <see cref="StartDelay"/>. The player has just been dropped into a body and is still
    /// finding the screen; opening on them instantly reads as a bug.
    /// </summary>
    [DataField]
    public TimeSpan SessionStartDelay = TimeSpan.Zero;

    /// <summary>
    /// True once this coach has spoken at least once, so <see cref="SessionStartDelay"/> only
    /// applies to the opening line.
    /// </summary>
    [ViewVariables]
    public bool HasSpoken;

    /// <summary>
    /// True once the player has come within <see cref="SpeakRange"/> since this coach arrived where
    /// they now are. Cleared when a holopad coach re-projects into a different chamber.
    /// </summary>
    /// <remarks>
    /// The range check is a "have you got here yet" gate, not a leash. Applying it to every segment
    /// meant a drill that deliberately sends the player down a lane silenced her for the rest of the
    /// chamber; once they have walked up to her, she keeps talking however far they wander.
    /// </remarks>
    [ViewVariables]
    public bool PlayerArrived;

    /// <summary>
    /// Rate limit for one-off corrections (see <see cref="TutorialSubGoalData.RetryLine"/>).
    /// </summary>
    [ViewVariables]
    public TimeSpan? NextInterjectionAt;

    /// <summary>
    /// Minimum gap between one-off corrections, so a player who keeps sprinting is told once
    /// rather than every tick.
    /// </summary>
    [DataField]
    public TimeSpan InterjectionCooldown = TimeSpan.FromSeconds(8);

    /// <summary>
    /// Floor on the gap between consecutive lines. Zero keeps the original speak-everything-at-once
    /// behaviour for coaches that author a single line per sub-goal.
    /// </summary>
    [DataField]
    public TimeSpan MinLineDelay = TimeSpan.Zero;

    /// <summary>
    /// Ceiling on the gap between consecutive lines, however long the line is.
    /// </summary>
    [DataField]
    public TimeSpan MaxLineDelay = TimeSpan.FromSeconds(9);

    /// <summary>
    /// Added per character of the line that is coming next, approximating someone typing it out.
    /// The long pause belongs in front of the long message, not behind it.
    /// </summary>
    [DataField]
    public float SecondsPerCharacter;

    /// <summary>
    /// Unused legacy field kept for map/component compatibility.
    /// </summary>
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField]
    public TimeSpan NextReminderAt;
}

/// <summary>
/// One trainer speech line tied to a curriculum sub-goal id.
/// </summary>
[DataDefinition]
public sealed partial class TutorialTrainerLine
{
    [DataField(required: true)]
    public string SubGoalId = string.Empty;

    [DataField(required: true)]
    public LocId Dialogue = default!;

    /// <summary>
    /// Release the sub-goal's control hint as this line is spoken, rather than waiting for the
    /// whole segment to finish. For the line that actually asks the player to do the thing.
    /// </summary>
    [DataField]
    public bool ShowControlHint;
}

/// <summary>
/// One queued line of coach dialogue: resolved text plus whether speaking it should reveal the
/// control hint.
/// </summary>
public readonly record struct TutorialPendingLine(string Text, bool ShowControlHint);
