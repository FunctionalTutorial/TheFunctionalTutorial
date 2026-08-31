using Content.Client.UserInterface.Systems.Chat;
using Content.Shared._Functional.TutorialServer;
using Content.Shared.Chat;
using Robust.Client.UserInterface;
using Robust.Shared.Utility;

namespace Content.Client._Functional.TutorialServer;

/// <summary>
/// Renders coach/mentor speech in the local player's language (solo tutorial instances).
/// </summary>
public sealed class TutorialCoachSpeechSystem : EntitySystem
{
    [Dependency] private readonly IUserInterfaceManager _ui = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeNetworkEvent<TutorialCoachSpeechEvent>(OnCoachSpeech);
    }

    private void OnCoachSpeech(TutorialCoachSpeechEvent ev)
    {
        if (string.IsNullOrWhiteSpace(ev.LocId))
            return;

        var spoken = FormattedMessage.RemoveMarkupPermissive(TutorialLoc.Get(ev.LocId));
        if (string.IsNullOrWhiteSpace(spoken))
            return;

        var name = Name(GetEntity(ev.Speaker));
        var wrapped = Loc.GetString(
            "chat-manager-entity-say-wrap-message",
            ("entityName", name),
            ("verb", Loc.GetString("chat-manager-entity-say-verb-default")),
            ("fontType", "Default"),
            ("fontSize", 12),
            ("message", FormattedMessage.EscapeText(spoken)));

        var msg = new ChatMessage(
            ChatChannel.Local,
            spoken,
            wrapped,
            ev.Speaker,
            senderKey: null);

        _ui.GetUIController<ChatUIController>().ProcessChatMessage(msg, speechBubble: true);
    }
}
