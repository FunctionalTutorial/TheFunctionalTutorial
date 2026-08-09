using Content.Server.Popups;
using Content.Shared._Functional.TutorialServer;
using Content.Shared.Popups;
using Content.Shared.UserInterface;
using Robust.Server.GameObjects;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._Functional.TutorialServer;

/// <summary>
/// Bound UI for the handheld Tutorial prompt (travel roles): checklist with Next/Hint, plus IC speech.
/// </summary>
public sealed class TutorialGuideSystem : EntitySystem
{
    [Dependency] private readonly TutorialServerRuleSystem _tutorial = default!;
    [Dependency] private readonly TutorialTrainerSystem _coach = default!;
    [Dependency] private readonly UserInterfaceSystem _ui = default!;
    [Dependency] private readonly PopupSystem _popup = default!;
    [Dependency] private readonly IPrototypeManager _protos = default!;
    [Dependency] private readonly IGameTiming _timing = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<TutorialGuideComponent, BeforeActivatableUIOpenEvent>(OnBeforeOpen);
        SubscribeLocalEvent<TutorialGuideComponent, ActivatableUIOpenAttemptEvent>(OnOpenAttempt);
        SubscribeLocalEvent<TutorialParticipantComponent, TutorialParticipantProgressChangedEvent>(OnProgressChanged);

        Subs.BuiEvents<TutorialGuideComponent>(TutorialPromptUiKey.Key, subs =>
        {
            subs.Event<BoundUIOpenedEvent>(OnOpened);
            subs.Event<TutorialPromptNextBuiMsg>(OnNext);
            subs.Event<TutorialPromptHintBuiMsg>(OnHint);
        });
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var guides = EntityQueryEnumerator<TutorialGuideComponent, TransformComponent>();
        while (guides.MoveNext(out var guideUid, out var guide, out _))
        {
            if (!TryGetHolderParticipant(guideUid, out var playerUid, out var part))
                continue;

            if (!_coach.TryResolveDialogue(guideUid, trainer: null, playerUid, part, out var subGoalId, out var dialogue))
                continue;

            if (guide.LastSpokenSubGoal != subGoalId)
            {
                SpeakGuide(guideUid, guide, subGoalId, dialogue);
                continue;
            }

            if (_timing.CurTime >= guide.NextReminderAt)
                SpeakGuide(guideUid, guide, subGoalId, dialogue);
        }
    }

    private void OnProgressChanged(
        Entity<TutorialParticipantComponent> ent,
        ref TutorialParticipantProgressChangedEvent args)
    {
        OnParticipantProgressChanged(ent.Owner, args.GuideUid, args.OldGoalIndex, args.OldProgressIndex);
    }

    private void OnOpenAttempt(Entity<TutorialGuideComponent> ent, ref ActivatableUIOpenAttemptEvent args)
    {
        if (!HasComp<TutorialParticipantComponent>(args.User))
            args.Cancel();
    }

    private void OnBeforeOpen(Entity<TutorialGuideComponent> ent, ref BeforeActivatableUIOpenEvent args)
    {
        SnapViewToProgress(ent, args.User);
        UpdateUi(ent, args.User);
    }

    private void OnOpened(Entity<TutorialGuideComponent> ent, ref BoundUIOpenedEvent args)
    {
        SnapViewToProgress(ent, args.Actor);
        UpdateUi(ent, args.Actor);
    }

    private void OnNext(Entity<TutorialGuideComponent> ent, ref TutorialPromptNextBuiMsg args)
    {
        TryGoNext(ent, args.Actor);
    }

    private void OnHint(Entity<TutorialGuideComponent> ent, ref TutorialPromptHintBuiMsg args)
    {
        TryShowStuckHint(args.Actor);
    }

    /// <summary>
    /// Shows the authored stuck hint popup without advancing curriculum.
    /// </summary>
    public bool TryShowStuckHint(EntityUid user)
    {
        if (!TryComp<TutorialParticipantComponent>(user, out var part))
            return false;

        if (string.IsNullOrEmpty(part.StuckHintText))
            return false;

        _popup.PopupEntity(part.StuckHintText, user, user, PopupType.Medium);
        return true;
    }

    /// <summary>
    /// Advances an Acknowledge tip. View is always snapped to live progress (no history paging).
    /// </summary>
    public bool TryGoNext(Entity<TutorialGuideComponent> ent, EntityUid user)
    {
        if (!TryComp<TutorialParticipantComponent>(user, out var part))
            return false;

        if (part.StepComplete != TutorialStepComplete.Acknowledge)
            return false;

        _tutorial.AdvanceSubGoal(user);
        SnapViewToProgress(ent, user);
        UpdateUi(ent, user);
        return true;
    }

    public TutorialPromptBuiState GetUiState(Entity<TutorialGuideComponent> ent, EntityUid user)
    {
        return BuildState(ent, user);
    }

    /// <summary>
    /// Keeps an open guide UI in sync when curriculum progress changes (sensors, etc.).
    /// Also toasts when the prompt is closed, or when there is no handheld guide (mentor roles).
    /// </summary>
    public void OnParticipantProgressChanged(
        EntityUid mob,
        EntityUid guideUid,
        int oldGoalIndex,
        int oldProgressIndex)
    {
        if (!TryComp<TutorialParticipantComponent>(mob, out var part))
            return;

        if (guideUid != EntityUid.Invalid &&
            !TerminatingOrDeleted(guideUid) &&
            TryComp<TutorialGuideComponent>(guideUid, out var guide))
        {
            SnapViewToProgress((guideUid, guide), mob);

            if (_ui.IsUiOpen(guideUid, TutorialPromptUiKey.Key))
            {
                UpdateUi((guideUid, guide), mob);
                return;
            }
        }

        if (!TryComp<ActorComponent>(mob, out var actor))
            return;

        if (!_tutorial.TryConsumeProgressPopup(actor.PlayerSession))
            return;

        var toast = Loc.GetString("tutorial-server-progress-toast", ("text", part.StepText));
        _popup.PopupEntity(toast, mob, actor.PlayerSession, PopupType.Medium);
    }

    private void SnapViewToProgress(Entity<TutorialGuideComponent> ent, EntityUid user)
    {
        if (!TryComp<TutorialParticipantComponent>(user, out var part))
            return;

        if (part.GoalCount > 0)
        {
            ent.Comp.ViewGoalIndex = part.GoalIndex;
            ent.Comp.ViewIndex = part.SubGoalIndex;
        }
        else
        {
            ent.Comp.ViewGoalIndex = 0;
            ent.Comp.ViewIndex = part.StepIndex;
        }
    }

    private void UpdateUi(Entity<TutorialGuideComponent> ent, EntityUid user)
    {
        _ui.SetUiState(ent.Owner, TutorialPromptUiKey.Key, BuildState(ent, user));
    }

    private TutorialPromptBuiState BuildState(Entity<TutorialGuideComponent> ent, EntityUid user)
    {
        if (!TryComp<TutorialParticipantComponent>(user, out var part))
        {
            return new TutorialPromptBuiState { HasTutorial = false };
        }

        if (!TryGetRole(user, out var role))
        {
            return new TutorialPromptBuiState { HasTutorial = false };
        }

        SnapViewToProgress(ent, user);

        if (role.Goals.Count > 0)
        {
            var liveGoal = Math.Clamp(part.GoalIndex, 0, Math.Max(role.Goals.Count - 1, 0));
            var liveSub = Math.Clamp(part.SubGoalIndex, 0, Math.Max(part.SubGoalCount - 1, 0));
            var viewedGoal = role.Goals[liveGoal];
            var stepCount = Math.Max(viewedGoal.SubGoals.Count, 1);

            var subStates = new List<TutorialHudSubGoalState>(viewedGoal.SubGoals.Count);
            for (var i = 0; i < viewedGoal.SubGoals.Count; i++)
            {
                subStates.Add(new TutorialHudSubGoalState
                {
                    Text = Loc.GetString(viewedGoal.SubGoals[i].Text),
                    Completed = i < liveSub,
                });
            }

            var stepText = liveSub >= 0 && liveSub < viewedGoal.SubGoals.Count
                ? Loc.GetString(viewedGoal.SubGoals[liveSub].Text)
                : part.StepText;

            return new TutorialPromptBuiState
            {
                HasTutorial = true,
                GoalTitle = Loc.GetString(viewedGoal.Title),
                GoalIndex = part.GoalIndex,
                GoalCount = role.Goals.Count,
                ViewGoalIndex = liveGoal,
                ViewIndex = liveSub,
                ProgressIndex = liveSub,
                StepCount = stepCount,
                StepText = stepText,
                ViewComplete = part.StepComplete,
                SubGoalStates = subStates,
                HintText = part.HintText,
                StuckHintText = part.StuckHintText,
                CanGoBack = false,
                WaitingOnSensor = part.StepComplete != TutorialStepComplete.Acknowledge,
                CanGoNext = part.StepComplete == TutorialStepComplete.Acknowledge,
            };
        }

        var progress = part.StepIndex;
        var legacyCount = Math.Max(part.StepCount, 1);

        return new TutorialPromptBuiState
        {
            HasTutorial = true,
            GoalTitle = string.Empty,
            GoalIndex = 0,
            GoalCount = 0,
            ViewGoalIndex = 0,
            ViewIndex = progress,
            ProgressIndex = progress,
            StepCount = legacyCount,
            StepText = part.StepText,
            ViewComplete = part.StepComplete,
            CanGoBack = false,
            WaitingOnSensor = part.StepComplete != TutorialStepComplete.Acknowledge,
            CanGoNext = part.StepComplete == TutorialStepComplete.Acknowledge,
            HintText = part.HintText,
            StuckHintText = part.StuckHintText,
        };
    }

    private void SpeakGuide(EntityUid guideUid, TutorialGuideComponent guide, string subGoalId, string dialogue)
    {
        _coach.SpeakAsCoach(guideUid, subGoalId, dialogue, (id, next) =>
        {
            guide.LastSpokenSubGoal = id;
            guide.NextReminderAt = next;
            Dirty(guideUid, guide);
        });
    }

    private bool TryGetHolderParticipant(
        EntityUid guideUid,
        out EntityUid playerUid,
        out TutorialParticipantComponent part)
    {
        playerUid = default;
        part = default!;

        if (!TryComp<TransformComponent>(guideUid, out var xform) ||
            xform.ParentUid == EntityUid.Invalid)
            return false;

        // Held in a hand container → parent is the mob.
        var holder = xform.ParentUid;
        if (!TryComp(holder, out part!))
        {
            // Nested container (e.g. inventory) — walk up one more level.
            if (!TryComp<TransformComponent>(holder, out var holderXform) ||
                !TryComp(holderXform.ParentUid, out part!))
                return false;
            holder = holderXform.ParentUid;
        }

        playerUid = holder;
        return true;
    }

    private bool TryGetRole(EntityUid user, out TutorialRolePrototype role)
    {
        role = default!;

        if (!TryComp<ActorComponent>(user, out var actor))
            return false;

        var query = EntityQueryEnumerator<TutorialServerRuleComponent>();
        while (query.MoveNext(out _, out var rule))
        {
            if (!rule.Sessions.TryGetValue(actor.PlayerSession.UserId, out var session) ||
                session.SelectedRoleId == null ||
                !_protos.TryIndex(session.SelectedRoleId, out role!))
                continue;

            return true;
        }

        return false;
    }
}
