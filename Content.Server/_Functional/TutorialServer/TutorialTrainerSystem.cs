using Content.Server.Chat.Systems;
using Content.Shared._Functional.TutorialServer;
using Content.Shared.Chat;
using Content.Shared.Interaction;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Robust.Shared.Timing;

namespace Content.Server._Functional.TutorialServer;

/// <summary>
/// Speaks trainer lines when a same-map participant's sub-goal matches, on hug/interact, and on a 10s reminder.
/// </summary>
public sealed class TutorialTrainerSystem : EntitySystem
{
    [Dependency] private readonly ChatSystem _chat = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
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

            var mapUid = trainerXform.MapUid;
            if (mapUid == null)
                continue;

            if (!TryFindParticipantOnMap(mapUid.Value, out var playerUid, out var part))
                continue;

            if (!_tutorial.TryGetCurrentSubGoal(playerUid, part, out var sub))
                continue;

            if (!TryGetLine(trainer, sub.Id, out var dialogue))
                continue;

            if (trainer.LastSpokenSubGoal != sub.Id)
            {
                Speak(trainerUid, trainer, sub.Id, dialogue);
                continue;
            }

            if (_timing.CurTime >= trainer.NextReminderAt)
                Speak(trainerUid, trainer, sub.Id, dialogue);
        }
    }

    private void OnInteractHand(Entity<TutorialTrainerComponent> ent, ref InteractHandEvent args)
    {
        if (args.Handled)
            return;

        if (!TryComp<TutorialParticipantComponent>(args.User, out var part))
            return;

        if (!_tutorial.TryGetCurrentSubGoal(args.User, part, out var sub))
            return;

        if (!TryGetLine(ent.Comp, sub.Id, out var dialogue))
            return;

        // Do not mark handled — practice mobs can still play hug / InteractionPopup.
        Speak(ent, ent.Comp, sub.Id, dialogue);
    }

    private bool TryFindParticipantOnMap(EntityUid mapUid, out EntityUid playerUid, out TutorialParticipantComponent part)
    {
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

    private static bool TryGetLine(TutorialTrainerComponent trainer, string subGoalId, out LocId dialogue)
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

    private void Speak(EntityUid trainerUid, TutorialTrainerComponent trainer, string subGoalId, LocId dialogue)
    {
        var message = Loc.GetString(dialogue);
        // ignoreActionBlocker: practice mobs can be SSD-slept; still coach the player in chat.
        _chat.TrySendInGameICMessage(
            trainerUid,
            message,
            InGameICChatType.Speak,
            hideChat: false,
            hideLog: true,
            ignoreActionBlocker: true);

        trainer.LastSpokenSubGoal = subGoalId;
        trainer.NextReminderAt = _timing.CurTime + ReminderInterval;
        Dirty(trainerUid, trainer);
    }
}
