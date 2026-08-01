using Content.Server.Popups;
using Content.Shared._Functional.TutorialServer;
using Content.Shared.Popups;
using Content.Shared.UserInterface;
using Robust.Server.GameObjects;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;

namespace Content.Server._Functional.TutorialServer;

/// <summary>
/// Bound UI for the handheld Tutorial prompt: shows the current stage with Back/Next.
/// </summary>
public sealed class TutorialGuideSystem : EntitySystem
{
    [Dependency] private readonly TutorialServerRuleSystem _tutorial = default!;
    [Dependency] private readonly UserInterfaceSystem _ui = default!;
    [Dependency] private readonly PopupSystem _popup = default!;
    [Dependency] private readonly IPrototypeManager _protos = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<TutorialGuideComponent, BeforeActivatableUIOpenEvent>(OnBeforeOpen);
        SubscribeLocalEvent<TutorialGuideComponent, ActivatableUIOpenAttemptEvent>(OnOpenAttempt);
        SubscribeLocalEvent<TutorialParticipantComponent, TutorialParticipantProgressChangedEvent>(OnProgressChanged);

        Subs.BuiEvents<TutorialGuideComponent>(TutorialPromptUiKey.Key, subs =>
        {
            subs.Event<BoundUIOpenedEvent>(OnOpened);
            subs.Event<TutorialPromptBackBuiMsg>(OnBack);
            subs.Event<TutorialPromptNextBuiMsg>(OnNext);
            subs.Event<TutorialPromptHintBuiMsg>(OnHint);
        });
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

    private void OnBack(Entity<TutorialGuideComponent> ent, ref TutorialPromptBackBuiMsg args)
    {
        TryGoBack(ent, args.Actor);
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
    /// Pages the prompt view backward through previously completed steps (including prior goals).
    /// </summary>
    public bool TryGoBack(Entity<TutorialGuideComponent> ent, EntityUid user)
    {
        if (!TryComp<TutorialParticipantComponent>(user, out var part))
            return false;

        if (!TryGetRole(user, out var role))
            return false;

        if (role.Goals.Count > 0)
        {
            if (ent.Comp.ViewIndex > 0)
            {
                ent.Comp.ViewIndex--;
                UpdateUi(ent, user);
                return true;
            }

            if (ent.Comp.ViewGoalIndex <= 0)
                return false;

            ent.Comp.ViewGoalIndex--;
            var prevGoal = role.Goals[ent.Comp.ViewGoalIndex];
            ent.Comp.ViewIndex = Math.Max(prevGoal.SubGoals.Count - 1, 0);
            UpdateUi(ent, user);
            return true;
        }

        if (ent.Comp.ViewIndex <= 0)
            return false;

        ent.Comp.ViewIndex--;
        UpdateUi(ent, user);
        return true;
    }

    /// <summary>
    /// Pages forward through passed steps, or advances an Acknowledge tip.
    /// Returns false when Next is blocked (waiting on a sensor at the live tip).
    /// </summary>
    public bool TryGoNext(Entity<TutorialGuideComponent> ent, EntityUid user)
    {
        if (!TryComp<TutorialParticipantComponent>(user, out var part))
            return false;

        if (!TryGetRole(user, out var role))
            return false;

        if (role.Goals.Count > 0)
        {
            var liveGoal = part.GoalIndex;
            var liveSub = part.SubGoalIndex;

            // Browsing history: step forward within / across goals until the live tip.
            if (ent.Comp.ViewGoalIndex < liveGoal ||
                (ent.Comp.ViewGoalIndex == liveGoal && ent.Comp.ViewIndex < liveSub))
            {
                var goal = role.Goals[ent.Comp.ViewGoalIndex];
                if (ent.Comp.ViewIndex + 1 < goal.SubGoals.Count)
                {
                    ent.Comp.ViewIndex++;
                }
                else if (ent.Comp.ViewGoalIndex + 1 <= liveGoal)
                {
                    ent.Comp.ViewGoalIndex++;
                    ent.Comp.ViewIndex = 0;
                }
                else
                {
                    return false;
                }

                UpdateUi(ent, user);
                return true;
            }

            if (ent.Comp.ViewGoalIndex != liveGoal || ent.Comp.ViewIndex != liveSub)
                return false;

            if (part.StepComplete != TutorialStepComplete.Acknowledge)
                return false;

            _tutorial.AdvanceSubGoal(user);

            if (TryComp(user, out part))
                SnapViewToProgress(ent, user);

            UpdateUi(ent, user);
            return true;
        }

        var progress = part.StepIndex;
        if (ent.Comp.ViewIndex < progress)
        {
            ent.Comp.ViewIndex++;
            UpdateUi(ent, user);
            return true;
        }

        if (ent.Comp.ViewIndex != progress)
            return false;

        if (part.StepComplete != TutorialStepComplete.Acknowledge)
            return false;

        var oldStep = progress;
        _tutorial.AdvanceSubGoal(user);

        if (!TryComp(user, out part))
            return true;

        if (part.StepIndex != oldStep)
            ent.Comp.ViewIndex = part.StepIndex;

        UpdateUi(ent, user);
        return true;
    }

    public TutorialPromptBuiState GetUiState(Entity<TutorialGuideComponent> ent, EntityUid user)
    {
        return BuildState(ent, user);
    }

    /// <summary>
    /// Keeps an open guide UI in sync when curriculum progress changes (sensors, etc.).
    /// </summary>
    public void OnParticipantProgressChanged(
        EntityUid mob,
        EntityUid guideUid,
        int oldGoalIndex,
        int oldProgressIndex)
    {
        if (!TryComp<TutorialGuideComponent>(guideUid, out var guide) ||
            !TryComp<TutorialParticipantComponent>(mob, out var part))
            return;

        // Follow the player to the new tip when they were watching the previous live tip
        // (or when the goal advanced). Do not yank them forward if they deliberately browsed back.
        var viewingLive = guide.ViewGoalIndex == oldGoalIndex && guide.ViewIndex >= oldProgressIndex;
        if (part.GoalIndex != oldGoalIndex || viewingLive)
            SnapViewToProgress((guideUid, guide), mob);

        if (_ui.IsUiOpen(guideUid, TutorialPromptUiKey.Key))
        {
            UpdateUi((guideUid, guide), mob);
            return;
        }

        // Closed prompt: toast the next step so sensor advances are not silent.
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

        if (role.Goals.Count > 0)
        {
            var liveGoal = Math.Clamp(part.GoalIndex, 0, Math.Max(role.Goals.Count - 1, 0));
            var liveSub = Math.Clamp(part.SubGoalIndex, 0, Math.Max(part.SubGoalCount - 1, 0));
            var viewGoal = Math.Clamp(ent.Comp.ViewGoalIndex, 0, liveGoal);
            ent.Comp.ViewGoalIndex = viewGoal;

            var viewedGoal = role.Goals[viewGoal];
            var stepCount = Math.Max(viewedGoal.SubGoals.Count, 1);
            var maxViewSub = viewGoal == liveGoal ? liveSub : stepCount - 1;
            var view = Math.Clamp(ent.Comp.ViewIndex, 0, Math.Max(maxViewSub, 0));
            ent.Comp.ViewIndex = view;

            var subStates = new List<TutorialHudSubGoalState>(viewedGoal.SubGoals.Count);
            for (var i = 0; i < viewedGoal.SubGoals.Count; i++)
            {
                var completed = viewGoal < liveGoal || (viewGoal == liveGoal && i < liveSub);
                subStates.Add(new TutorialHudSubGoalState
                {
                    Text = Loc.GetString(viewedGoal.SubGoals[i].Text),
                    Completed = completed,
                });
            }

            var atLive = viewGoal == liveGoal && view == liveSub;
            var stepText = view >= 0 && view < viewedGoal.SubGoals.Count
                ? Loc.GetString(viewedGoal.SubGoals[view].Text)
                : part.StepText;
            var viewComplete = atLive
                ? part.StepComplete
                : TutorialStepComplete.Acknowledge;

            return new TutorialPromptBuiState
            {
                HasTutorial = true,
                GoalTitle = Loc.GetString(viewedGoal.Title),
                GoalIndex = part.GoalIndex,
                GoalCount = role.Goals.Count,
                ViewGoalIndex = viewGoal,
                ViewIndex = view,
                ProgressIndex = liveSub,
                StepCount = stepCount,
                StepText = stepText,
                ViewComplete = viewComplete,
                SubGoalStates = subStates,
                HintText = atLive ? part.HintText : string.Empty,
                StuckHintText = atLive ? part.StuckHintText : string.Empty,
                CanGoBack = viewGoal > 0 || view > 0,
                WaitingOnSensor = atLive && part.StepComplete != TutorialStepComplete.Acknowledge,
                CanGoNext = !atLive || part.StepComplete == TutorialStepComplete.Acknowledge,
            };
        }

        var progress = part.StepIndex;
        var legacyCount = Math.Max(part.StepCount, 1);
        var legacyView = Math.Clamp(ent.Comp.ViewIndex, 0, Math.Max(legacyCount - 1, 0));
        ent.Comp.ViewIndex = legacyView;
        ent.Comp.ViewGoalIndex = 0;

        return new TutorialPromptBuiState
        {
            HasTutorial = true,
            GoalTitle = string.Empty,
            GoalIndex = 0,
            GoalCount = 0,
            ViewGoalIndex = 0,
            ViewIndex = legacyView,
            ProgressIndex = progress,
            StepCount = legacyCount,
            StepText = legacyView == progress
                ? part.StepText
                : Loc.GetString("tutorial-server-prompt-past-step", ("index", legacyView + 1)),
            ViewComplete = legacyView == progress
                ? part.StepComplete
                : TutorialStepComplete.Acknowledge,
            CanGoBack = legacyView > 0,
            WaitingOnSensor = legacyView == progress && part.StepComplete != TutorialStepComplete.Acknowledge,
            CanGoNext = legacyView < progress ||
                        (legacyView == progress && part.StepComplete == TutorialStepComplete.Acknowledge),
            HintText = legacyView == progress ? part.HintText : string.Empty,
            StuckHintText = legacyView == progress ? part.StuckHintText : string.Empty,
        };
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
