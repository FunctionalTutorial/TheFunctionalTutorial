using Content.Server.Chat.Managers;
using Content.Server.Radio;
using Content.Shared._Functional.TutorialServer;
using Content.Shared.Chat;
using Content.Shared.GameTicking.Components;

namespace Content.Server._Functional.TutorialServer;

/// <summary>
/// Chat isolation while TutorialServer is active: no radio, and ghosts cannot use dead chat
/// (CVars also disable OOC/LOOC/dead; this hard-cancels dead-chat attempts as a backstop).
/// </summary>
public sealed class TutorialChatIsolationSystem : EntitySystem
{
    [Dependency] private readonly IChatManager _chat = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<RadioReceiveAttemptEvent>(OnRadioReceiveAttempt);
        SubscribeLocalEvent<RadioSendAttemptEvent>(OnRadioSendAttempt);
        SubscribeLocalEvent<InGameOocMessageAttemptEvent>(OnInGameOocAttempt);
    }

    private bool TutorialActive()
    {
        var query = EntityQueryEnumerator<TutorialServerRuleComponent, ActiveGameRuleComponent, GameRuleComponent>();
        return query.MoveNext(out _, out _, out _, out _);
    }

    private void OnRadioReceiveAttempt(ref RadioReceiveAttemptEvent args)
    {
        if (TutorialActive())
            args.Cancelled = true;
    }

    private void OnRadioSendAttempt(ref RadioSendAttemptEvent args)
    {
        if (TutorialActive())
            args.Cancelled = true;
    }

    private void OnInGameOocAttempt(ref InGameOocMessageAttemptEvent args)
    {
        if (args.Cancelled || !TutorialActive())
            return;

        if (args.Type != InGameOOCChatType.Dead)
            return;

        args.Cancelled = true;
        _chat.DispatchServerMessage(args.Session, Loc.GetString("tutorial-server-dead-chat-disabled"), suppressLog: true);
    }
}
