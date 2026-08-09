using Content.Server.Chat.Systems;
using Content.Server.Popups;
using Content.Shared._Functional.TutorialServer;
using Content.Shared.Chat;
using Content.Shared.Interaction;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Popups;
using Robust.Shared.Timing;

namespace Content.Server._Functional.TutorialServer;

/// <summary>
/// Speaks coach lines for mentors (and shared dialogue resolution), handles click-to-repeat /
/// Acknowledge advance / stuck hints when there is no handheld guide.
/// </summary>
public sealed class TutorialTrainerSystem : EntitySystem
{
    [Dependency] private readonly ChatSystem _chat = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly PopupSystem _popup = default!;
    [Dependency] private readonly TutorialServerRuleSystem _tutorial = default!;

    private static readonly TimeSpan ReminderInterval = TimeSpan.FromSeconds(10);

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

            if (trainer.LastSpokenSubGoal != subGoalId)
            {
                Speak(trainerUid, trainer, subGoalId, dialogue);
                continue;
            }

            if (_timing.CurTime >= trainer.NextReminderAt)
                Speak(trainerUid, trainer, subGoalId, dialogue);
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

        Speak(ent, ent.Comp, subGoalId, dialogue);

        if (part.StepComplete == TutorialStepComplete.Acknowledge)
        {
            _tutorial.AdvanceSubGoal(args.User);
            return;
        }

        // Waiting on a sensor: click shows the stuck hint when authored.
        if (!string.IsNullOrEmpty(part.StuckHintText))
            _popup.PopupEntity(part.StuckHintText, args.User, args.User, PopupType.Medium);
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
    /// Guide / mentor shared speak helper.
    /// </summary>
    public void SpeakAsCoach(EntityUid speakerUid, string subGoalId, string dialogue, Action<string, TimeSpan>? markSpoken)
    {
        _chat.TrySendInGameICMessage(
            speakerUid,
            dialogue,
            InGameICChatType.Speak,
            hideChat: false,
            hideLog: true,
            ignoreActionBlocker: true);

        markSpoken?.Invoke(subGoalId, _timing.CurTime + ReminderInterval);
    }

    private void Speak(EntityUid trainerUid, TutorialTrainerComponent trainer, string subGoalId, string dialogue)
    {
        SpeakAsCoach(trainerUid, subGoalId, dialogue, (id, next) =>
        {
            trainer.LastSpokenSubGoal = id;
            trainer.NextReminderAt = next;
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
