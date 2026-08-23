using Content.Server.Chat.Systems;
using Content.Shared._Functional.TutorialServer;
using Content.Shared.Chat;
using Content.Shared.Interaction;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Server._Functional.TutorialServer;

/// <summary>
/// Speaks coach lines for mentors (and shared dialogue resolution), handles click-to-repeat /
/// Acknowledge advance / stuck hints when there is no handheld guide.
/// Speaks once per sub-goal change (not on a timer); muted while the guide UI is open.
/// </summary>
public sealed class TutorialTrainerSystem : EntitySystem
{
    [Dependency] private readonly ChatSystem _chat = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly TutorialServerRuleSystem _tutorial = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<TutorialTrainerComponent, InteractHandEvent>(OnInteractHand);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        var trainers = EntityQueryEnumerator<TutorialTrainerComponent, TransformComponent>();
        while (trainers.MoveNext(out var trainerUid, out var trainer, out var trainerXform))
        {
            if (TryComp<MobStateComponent>(trainerUid, out var mobState) &&
                mobState.CurrentState is MobState.Dead or MobState.Critical)
                continue;

            if (!TryResolvePlayer(trainerUid, trainerXform, out var playerUid, out var part))
                continue;

            if (!TryResolveDialogue(trainerUid, trainer, playerUid, part, out var subGoalId, out var dialogue))
                continue;

            // Only queue when the sub-goal changes — no timed reminders.
            if (trainer.LastSpokenSubGoal != subGoalId)
            {
                trainer.LastSpokenSubGoal = subGoalId;
                trainer.PendingLines.Clear();
                trainer.NextLineAt = null;
                trainer.LinesSpoken = 0;

                // Don't pile IC speech on top of an open tutorial prompt.
                if (!_tutorial.IsGuideUiOpen(playerUid))
                {
                    foreach (var line in ResolveSegment(trainer, subGoalId, dialogue))
                        trainer.PendingLines.Enqueue(line);
                }

                Dirty(trainerUid, trainer);
            }

            if (trainer.PendingLines.Count == 0)
                continue;

            // Coaches with a speak range wait for the player to walk up to them once, and then talk
            // for as long as they are stationed there. Re-checking the range every segment silenced
            // her for the rest of a chamber the moment a drill sent the player down a lane; a
            // holopad coach only has to be arrived at, and that is what re-projecting into the next
            // chamber resets.
            if (trainer.NextLineAt == null)
            {
                if (!trainer.PlayerArrived)
                {
                    if (!IsPlayerInSpeakRange(trainer, trainerXform, playerUid))
                        continue;

                    trainer.PlayerArrived = true;
                }

                var opening = trainer.HasSpoken ? trainer.StartDelay : trainer.StartDelay + trainer.SessionStartDelay;
                trainer.NextLineAt = now + opening;
                Dirty(trainerUid, trainer);
            }

            if (now < trainer.NextLineAt.Value)
                continue;

            var next = trainer.PendingLines.Dequeue();
            trainer.NextLineAt = now + ResolveNextLineDelay(trainer);
            trainer.HasSpoken = true;
            trainer.LinesSpoken++;
            Dirty(trainerUid, trainer);

            SpeakLine(trainerUid, playerUid, subGoalId, next.Text);

            if (next.ShowControlHint)
                _tutorial.ShowPendingControlHint(playerUid);
        }
    }

    /// <summary>
    /// How many lines of <paramref name="subGoalId"/> this player's coach has spoken. False when no
    /// coach is on that segment, which callers must read as "no cue is coming", not as zero.
    /// </summary>
    public bool TryGetLinesSpoken(EntityUid player, string subGoalId, out int spoken)
    {
        spoken = 0;

        var coaches = EntityQueryEnumerator<TutorialTrainerComponent, TutorialMentorComponent>();
        while (coaches.MoveNext(out _, out var trainer, out var mentor))
        {
            if (mentor.PlayerUid != player)
                continue;

            // A different segment means her count belongs to another beat.
            if (!string.Equals(trainer.LastSpokenSubGoal, subGoalId, StringComparison.Ordinal))
                return false;

            spoken = trainer.LinesSpoken;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Where a coach is in <paramref name="subGoalId"/>'s script. Callers that hold something back
    /// until she is done need <see cref="TutorialCoachSpeech.Waiting"/> separated from
    /// <see cref="TutorialCoachSpeech.Speaking"/>: only the former can go on forever, so only the
    /// former may be timed out.
    /// </summary>
    public TutorialCoachSpeech ResolveSegmentState(EntityUid mentor, string subGoalId)
    {
        if (!TryComp<TutorialTrainerComponent>(mentor, out var trainer))
            return TutorialCoachSpeech.Done;

        // The segment is enqueued by this system's own Update, which may not have run since the
        // sub-goal changed. Until it has, she is about to start rather than finished.
        if (!string.Equals(trainer.LastSpokenSubGoal, subGoalId, StringComparison.Ordinal))
            return TutorialCoachSpeech.Waiting;

        // Lines queued with no clock running means nobody has walked into earshot yet.
        if (trainer.PendingLines.Count > 0 && trainer.NextLineAt == null)
            return TutorialCoachSpeech.Waiting;

        if (trainer.PendingLines.Count > 0 ||
            (trainer.NextLineAt is { } next && _timing.CurTime < next))
        {
            return TutorialCoachSpeech.Speaking;
        }

        return TutorialCoachSpeech.Done;
    }

    /// <summary>
    /// Speaks a one-off correction outside the sub-goal script, rate limited so a player who keeps
    /// breaking a drill is corrected once rather than continuously.
    /// </summary>
    public void TrySpeakInterjection(EntityUid mentor, EntityUid player, LocId line)
    {
        if (!TryComp<TutorialTrainerComponent>(mentor, out var trainer))
            return;

        var now = _timing.CurTime;
        if (trainer.NextInterjectionAt is { } ready && now < ready)
            return;

        var text = Loc.GetString(line);
        if (string.IsNullOrWhiteSpace(text))
            return;

        trainer.NextInterjectionAt = now + trainer.InterjectionCooldown;
        Dirty(mentor, trainer);
        SpeakAsCoach(mentor, player, string.Empty, text, null);
    }

    private void OnInteractHand(Entity<TutorialTrainerComponent> ent, ref InteractHandEvent args)
    {
        if (args.Handled)
            return;

        if (!TryComp<TutorialParticipantComponent>(args.User, out var part))
            return;

        // Mentors only coach their bound player.
        if (TryComp<TutorialMentorComponent>(ent, out var mentor) &&
            mentor.PlayerUid != EntityUid.Invalid &&
            mentor.PlayerUid != args.User)
            return;

        if (!TryResolveDialogue(ent, ent.Comp, args.User, part, out var subGoalId, out var dialogue))
            return;

        args.Handled = true;

        // Mid-segment: clicking pulls the next line forward for players who read faster than the
        // coach talks, rather than repeating what they just heard.
        if (ent.Comp.PendingLines.Count > 0)
        {
            var next = ent.Comp.PendingLines.Dequeue();
            ent.Comp.NextLineAt = _timing.CurTime + ResolveNextLineDelay(ent.Comp);
            Dirty(ent.Owner, ent.Comp);
            SpeakLine(ent, args.User, subGoalId, next.Text);

            if (next.ShowControlHint)
                _tutorial.ShowPendingControlHint(args.User);
            return;
        }

        SpeakLine(ent, args.User, subGoalId, dialogue);

        if (part.StepComplete == TutorialStepComplete.Acknowledge)
        {
            _tutorial.AdvanceSubGoal(args.User);
            return;
        }

        // Waiting on a sensor: click shows the stuck hint when authored.
        if (!string.IsNullOrEmpty(part.StuckHintText))
            _tutorial.SendTipChat(args.User, part.StuckHintText);
    }

    /// <summary>
    /// Resolves coach dialogue: trainer line override, else live sub-goal text.
    /// </summary>
    public bool TryResolveDialogue(
        EntityUid coachUid,
        TutorialTrainerComponent? trainer,
        EntityUid playerUid,
        TutorialParticipantComponent part,
        out string subGoalId,
        out string dialogue)
    {
        subGoalId = string.Empty;
        dialogue = string.Empty;

        if (_tutorial.TryGetCurrentSubGoal(playerUid, part, out var sub))
        {
            subGoalId = sub.Id;
            if (trainer != null && TryGetOverrideLine(trainer, sub.Id, out var overrideLoc))
            {
                dialogue = Loc.GetString(overrideLoc);
                return !string.IsNullOrWhiteSpace(dialogue);
            }

            dialogue = Loc.GetString(sub.Text);
            return !string.IsNullOrWhiteSpace(dialogue);
        }

        // Legacy flat steps.
        if (part.StepCount <= 0 || string.IsNullOrEmpty(part.StepText))
            return false;

        subGoalId = $"legacy:{part.StepIndex}";
        dialogue = part.StepText;
        return true;
    }

    /// <summary>
    /// Guide / mentor shared speak helper. Always speaks IC (speech bubble).
    /// Keybind markup is stripped for the spoken line; resolved binds stay available via
    /// stuck-hint tip chat / the guide UI, not a duplicate grey progress toast.
    /// </summary>
    public void SpeakAsCoach(
        EntityUid speakerUid,
        EntityUid playerUid,
        string subGoalId,
        string dialogue,
        Action<string>? markSpoken)
    {
        // playerUid reserved for future per-player coach delivery (e.g. whisper range).
        _ = playerUid;

        var spoken = FormattedMessage.RemoveMarkupPermissive(dialogue);
        if (!string.IsNullOrWhiteSpace(spoken))
        {
            _chat.TrySendInGameICMessage(
                speakerUid,
                spoken,
                InGameICChatType.Speak,
                hideChat: false,
                hideLog: true,
                ignoreActionBlocker: true);
        }

        markSpoken?.Invoke(subGoalId);
    }

    private void SpeakLine(
        EntityUid trainerUid,
        EntityUid playerUid,
        string subGoalId,
        string dialogue)
    {
        SpeakAsCoach(trainerUid, playerUid, subGoalId, dialogue, null);
    }

    /// <summary>
    /// All authored lines for a sub-goal in order, or the resolved fallback when none are authored.
    /// </summary>
    private List<TutorialPendingLine> ResolveSegment(
        TutorialTrainerComponent trainer,
        string subGoalId,
        string fallback)
    {
        var lines = new List<TutorialPendingLine>();
        foreach (var line in trainer.Lines)
        {
            if (!string.Equals(line.SubGoalId, subGoalId, StringComparison.Ordinal))
                continue;

            var text = Loc.GetString(line.Dialogue);
            if (!string.IsNullOrWhiteSpace(text))
                lines.Add(new TutorialPendingLine(text, line.ShowControlHint));
        }

        if (lines.Count == 0 && !string.IsNullOrWhiteSpace(fallback))
            lines.Add(new TutorialPendingLine(fallback, false));

        return lines;
    }

    private bool IsPlayerInSpeakRange(
        TutorialTrainerComponent trainer,
        TransformComponent trainerXform,
        EntityUid playerUid)
    {
        if (trainer.SpeakRange is not { } range)
            return true;

        if (!TryComp<TransformComponent>(playerUid, out var playerXform) ||
            playerXform.MapID != trainerXform.MapID)
            return false;

        var delta = _transform.GetWorldPosition(playerXform) - _transform.GetWorldPosition(trainerXform);
        return delta.Length() <= range;
    }

    /// <summary>
    /// Gap before the line at the head of the queue, scaled by how long *that* line is.
    /// </summary>
    /// <remarks>
    /// Scaling the pause to the line just spoken had it backwards: someone typing spends the long
    /// silence composing the long message, not recovering from it. With nothing left to say the gap
    /// collapses to the floor, since it only has to let the closing line be read.
    /// </remarks>
    private static TimeSpan ResolveNextLineDelay(TutorialTrainerComponent trainer)
    {
        if (!trainer.PendingLines.TryPeek(out var upcoming))
            return trainer.MinLineDelay;

        var typed = TimeSpan.FromSeconds(upcoming.Text.Length * trainer.SecondsPerCharacter);
        if (typed < trainer.MinLineDelay)
            typed = trainer.MinLineDelay;

        return typed > trainer.MaxLineDelay ? trainer.MaxLineDelay : typed;
    }

    /// <summary>
    /// Clears the arrival gate, so this coach waits for the player to walk up to her again.
    /// </summary>
    public void ResetArrival(EntityUid trainerUid)
    {
        if (!TryComp<TutorialTrainerComponent>(trainerUid, out var trainer) || !trainer.PlayerArrived)
            return;

        trainer.PlayerArrived = false;
        Dirty(trainerUid, trainer);
    }

    private bool TryResolvePlayer(
        EntityUid trainerUid,
        TransformComponent trainerXform,
        out EntityUid playerUid,
        out TutorialParticipantComponent part)
    {
        if (TryComp<TutorialMentorComponent>(trainerUid, out var mentor) &&
            mentor.PlayerUid != EntityUid.Invalid &&
            !TerminatingOrDeleted(mentor.PlayerUid) &&
            TryComp(mentor.PlayerUid, out part!))
        {
            playerUid = mentor.PlayerUid;
            return true;
        }

        var mapUid = trainerXform.MapUid;
        if (mapUid == null)
        {
            playerUid = default;
            part = default!;
            return false;
        }

        var participants = EntityQueryEnumerator<TutorialParticipantComponent, TransformComponent>();
        while (participants.MoveNext(out playerUid, out part!, out var playerXform))
        {
            if (playerXform.MapUid == mapUid)
                return true;
        }

        playerUid = default;
        part = default!;
        return false;
    }

    private static bool TryGetOverrideLine(TutorialTrainerComponent trainer, string subGoalId, out LocId dialogue)
    {
        foreach (var line in trainer.Lines)
        {
            if (!string.Equals(line.SubGoalId, subGoalId, StringComparison.Ordinal))
                continue;

            dialogue = line.Dialogue;
            return true;
        }

        dialogue = default;
        return false;
    }
}
