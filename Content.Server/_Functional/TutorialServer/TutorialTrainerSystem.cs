using Content.Server.Chat.Systems;
using Content.Shared._Functional.TutorialServer;
using Content.Shared.Chat;
using Content.Shared.Interaction;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
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
    [Dependency] private readonly TutorialServerRuleSystem _tutorial = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<TutorialTrainerComponent, InteractHandEvent>(OnInteractHand);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

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

            // Only speak when the sub-goal changes — no timed reminders.
            if (trainer.LastSpokenSubGoal == subGoalId)
                continue;

            // Don't pile IC speech on top of an open tutorial prompt.
            if (_tutorial.IsGuideUiOpen(playerUid))
            {
                trainer.LastSpokenSubGoal = subGoalId;
                Dirty(trainerUid, trainer);
                continue;
            }

            Speak(trainerUid, trainer, playerUid, subGoalId, dialogue);
        }
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
        Speak(ent, ent.Comp, args.User, subGoalId, dialogue);

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

    private void Speak(
        EntityUid trainerUid,
        TutorialTrainerComponent trainer,
        EntityUid playerUid,
        string subGoalId,
        string dialogue)
    {
        SpeakAsCoach(trainerUid, playerUid, subGoalId, dialogue, id =>
        {
            trainer.LastSpokenSubGoal = id;
            Dirty(trainerUid, trainer);
        });
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
