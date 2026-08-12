using Content.Client.UserInterface.Systems.Chat;
using Content.Shared._Functional.TutorialServer;
using Content.Shared.Chat;
using Robust.Client.UserInterface;
using Robust.Shared.Utility;

namespace Content.Client._Functional.TutorialServer;

/// <summary>
/// Receives tutorial tip markup from the server, resolves keybind tags, and posts to chat.
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
        if (string.IsNullOrWhiteSpace(ev.Markup))
            return;

        var resolved = FormattedMessage.FromMarkupPermissive(ev.Markup).ToString();
        if (string.IsNullOrWhiteSpace(resolved))
            return;

        var wrapped = Loc.GetString("chat-manager-server-wrap-message",
            ("message", FormattedMessage.EscapeText(resolved)));

        var msg = new ChatMessage(
            ChatChannel.Server,
            resolved,
            wrapped,
            NetEntity.Invalid,
            senderKey: null);

        _ui.GetUIController<ChatUIController>().ProcessChatMessage(msg, speechBubble: false);
    }
}
