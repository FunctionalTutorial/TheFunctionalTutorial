using System.IO;
using Robust.Shared.Configuration;
using Robust.Shared.Log;
using Content.Shared.CCVar;

namespace Content.Server.Entry;

public sealed partial class EntryPoint
{
    private static void LoadLocalServerConfig(IConfigurationManager cfg, ISawmill sawmill)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "server_config.local.toml");
        if (!File.Exists(path))
        {
            sawmill.Info("No local server config at {Path}", path);
            return;
        }

        string? loginHostUser = null;
        bool? loginLocal = null;

        foreach (var raw in File.ReadAllLines(path))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#') || line.StartsWith('['))
                continue;

            var eq = line.IndexOf('=');
            if (eq <= 0)
                continue;

            var key = line[..eq].Trim();
            var value = line[(eq + 1)..].Trim().Trim('"');

            switch (key)
            {
                case "login_host_user":
                    loginHostUser = value;
                    break;
                case "loginlocal":
                    if (bool.TryParse(value, out var parsed))
                        loginLocal = parsed;
                    break;
            }
        }

        if (loginHostUser != null)
        {
            cfg.SetCVar(CCVars.ConsoleLoginHostUser, loginHostUser);
            sawmill.Info("Local config: console.login_host_user={User} from {Path}", loginHostUser, path);
        }

        if (loginLocal != null)
        {
            cfg.SetCVar(CCVars.ConsoleLoginLocal, loginLocal.Value);
            sawmill.Info("Local config: console.loginlocal={Value} from {Path}", loginLocal.Value, path);
        }

        if (loginHostUser == null && loginLocal == null)
            sawmill.Warning("Local server config {Path} had no usable keys", path);
    }
}
