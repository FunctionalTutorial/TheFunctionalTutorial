using Content.Client.UserInterface.Systems.Chat;
using Content.Shared._Functional.TutorialServer;
using Content.Shared.Chat;
using Robust.Client.UserInterface;
using Robust.Shared.Utility;

namespace Content.Client._Functional.TutorialServer;

/// <summary>
/// Receives tutorial tip LocIds from the server, resolves against the client culture, and posts to chat.
/// </summary>
public sealed class TutorialTipChatSystem : EntitySystem
{
    [Dependency] private readonly IUserInterfaceManager _ui = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeNetworkEvent<TutorialTipChatEvent>(OnTipChat);
    }

    private void OnTipChat(TutorialTipChatEvent ev)
    {
        if (string.IsNullOrWhiteSpace(ev.LocId))
            return;

        var markup = string.IsNullOrEmpty(ev.TextArgLocId)
            ? Loc.GetString(ev.LocId)
            : Loc.GetString(ev.LocId, ("text", TutorialLoc.Get(ev.TextArgLocId)));

        if (string.IsNullOrWhiteSpace(markup))
            return;

        // Reparsed rather than passed through, so a malformed tag reaches the chat box as text
        // instead of throwing inside its markup parser.
        var parsed = FormattedMessage.FromMarkupPermissive(markup);

        // Plain text for filters and highlights; markup for the line that gets drawn, because a
        // stripped [keybind] leaves a hole where the key should be.
        var plain = parsed.ToString();
        if (string.IsNullOrWhiteSpace(plain))
            return;

        var wrapped = Loc.GetString("chat-manager-server-wrap-message", ("message", parsed.ToMarkup()));

        var msg = new ChatMessage(
            ChatChannel.Server,
            plain,
            wrapped,
            NetEntity.Invalid,
            senderKey: null);

        _ui.GetUIController<ChatUIController>().ProcessChatMessage(msg, speechBubble: false);
    }
}
