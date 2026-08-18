using System.Collections.Generic;
using Content.Shared._Functional.TutorialServer;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._Functional.TutorialServer;

/// <summary>
/// Records the tutorial tip lines the client is sent, so a test can check what actually reached
/// chat rather than what the server meant to put there.
/// </summary>
/// <remarks>
/// A system rather than a subscription taken from the test body: the event bus locks broadcast
/// subscriptions once the entity manager has started, which is long before any test runs.
/// </remarks>
public sealed class TutorialTipChatRecorder : EntitySystem
{
    public readonly List<string> Received = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeNetworkEvent<TutorialTipChatEvent>(ev => Received.Add(ev.Markup));
    }
}
