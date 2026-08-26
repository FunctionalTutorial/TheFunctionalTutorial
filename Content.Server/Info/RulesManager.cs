using System.Net;
using Content.Server.Administration.Logs;
using Content.Server.Database;
using Content.Shared.CCVar;
using Content.Shared.Database;
using Content.Shared.Info;
using Robust.Server.Player;
using Robust.Shared.Configuration;
using Robust.Shared.Network;

namespace Content.Server.Info;

public sealed partial class RulesManager
{
    [Dependency] private IServerDbManager _dbManager = default!;
    [Dependency] private INetManager _netManager = default!;
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private IAdminLogManager _adminLog = default!;
    [Dependency] private IPlayerManager _player = default!;

    public void Initialize()
    {
        _netManager.Connected += OnConnected;
        _netManager.RegisterNetMessage<SendRulesInformationMessage>();
        _netManager.RegisterNetMessage<RulesAcceptedMessage>(OnRulesAccepted);
    }

    private async void OnConnected(object? sender, NetChannelArgs e)
    {
        var isLocalhost = IPAddress.IsLoopback(e.Channel.RemoteEndPoint.Address) &&
                            _cfg.GetCVar(CCVars.RulesExemptLocal);

        var lastRead = await _dbManager.GetLastReadRules(e.Channel.UserId);
        //Tutorial - Begin: rules.enabled gates the popup; validity_days=0 re-shows after a rules rewrite
        var validityDays = Math.Max(0, _cfg.GetCVar(CCVars.RulesValidityDays));
        var lastValid = DateTime.UtcNow - TimeSpan.FromDays(validityDays);
        var hasCooldown = lastRead > lastValid;

        var rulesEnabled = _cfg.GetCVar(CCVars.RulesEnabled);
        var showRulesMessage = new SendRulesInformationMessage
        {
            PopupTime = _cfg.GetCVar(CCVars.RulesWaitTime),
            CoreRules = _cfg.GetCVar(CCVars.RulesFile),
            ShouldShowRules = rulesEnabled && !isLocalhost && !hasCooldown,
        };
        //Tutorial - End
        _netManager.ServerSendMessage(showRulesMessage, e.Channel);
    }

    private async void OnRulesAccepted(RulesAcceptedMessage message)
    {
        var date = DateTime.UtcNow;
        await _dbManager.SetLastReadRules(message.MsgChannel.UserId, date);
        if (message.FuckRules && _player.TryGetSessionById(message.MsgChannel.UserId, out var session))
            _adminLog.Add(LogType.Connection, LogImpact.Extreme, $"Player {session} used the fuckrules command.");
    }
}
