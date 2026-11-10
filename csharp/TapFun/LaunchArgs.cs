#nullable enable
using System;
using System.Collections.Generic;

namespace TapFun
{
    /// <summary>
    /// Values that TapFun passes to the exe as command-line arguments.
    /// TapFun replaces the placeholders written in "exe launch parameters" in the Publisher Console,
    /// for example <c>--tapfun-user-id {userId} --api-host {apiHost} --bearer-token {bearerToken} --port {port}</c>.
    /// The option names below are the ones in the default launch parameters. If you renamed them,
    /// read your names with <see cref="Get"/>.
    /// </summary>
    public sealed class LaunchArgs
    {
        /// <summary>The UDP port used when <c>--port</c> is not passed.</summary>
        public const int DefaultPort = 10100;

        private readonly Dictionary<string, string> _values;

        private LaunchArgs(Dictionary<string, string> values)
        {
            _values = values;
        }

        /// <summary>TapFun user ID (<c>--tapfun-user-id</c>). Not secret, but do not show it on screen.</summary>
        public string UserId => Get("--tapfun-user-id");

        /// <summary>Host name of the TapFun API (<c>--api-host</c>), for example <c>tapfun.co.jp</c>.</summary>
        public string ApiHost => Get("--api-host");

        /// <summary>Bearer token for the client API and the Web API (<c>--bearer-token</c>). Do not show it on screen.</summary>
        public string BearerToken => Get("--bearer-token");

        /// <summary>UDP port to receive messages from TapFun (<c>--port</c>). Usually 10100.</summary>
        public int Port => int.TryParse(Get("--port"), out int port) && port > 0 && port <= 65535 ? port : DefaultPort;

        /// <summary>Player name (<c>--username</c>). TapFun passes it URL-encoded; this returns the decoded value.</summary>
        public string Username => Uri.UnescapeDataString(Get("--username"));

        /// <summary><c>true</c> for a paid-timer player (<c>--is-paid-timer 1</c>).</summary>
        public bool IsPaidTimer => Get("--is-paid-timer") == "1";

        /// <summary>Same value as <c>isSP</c> of getPlayerEnvironment (<c>--is-sp 1</c>).</summary>
        public bool IsSp => Get("--is-sp") == "1";

        /// <summary>Codes of the DLCs the player owns (<c>--dlcs</c>). Empty when the player owns none.</summary>
        public IReadOnlyList<string> Dlcs
        {
            get
            {
                var codes = new List<string>();
                foreach (string code in Get("--dlcs").Split(','))
                {
                    if (code.Trim() != "")
                    {
                        codes.Add(code.Trim());
                    }
                }
                return codes;
            }
        }

        /// <summary><c>true</c> when the values needed to call the API are present.</summary>
        public bool HasClientApiValues => UserId != "" && ApiHost != "" && BearerToken != "";

        /// <summary>Returns the value of an option, or an empty string if it was not passed.</summary>
        public string Get(string name) => _values.TryGetValue(name, out string? value) ? value : "";

        /// <summary>
        /// Reads the arguments of this process (<see cref="Environment.GetCommandLineArgs"/>).
        /// Works in Unity players and in .NET apps.
        /// </summary>
        public static LaunchArgs FromCommandLine() => Parse(Environment.GetCommandLineArgs());

        /// <summary>
        /// Parses <c>--name value</c> and <c>--name=value</c>. Arguments that are not options, such as the exe path, are skipped.
        /// When an option is empty, TapFun drops it together with its name, so a missing option means "no value".
        /// </summary>
        public static LaunchArgs Parse(IReadOnlyList<string> args)
        {
            var values = new Dictionary<string, string>();
            for (int i = 0; i < args.Count; i++)
            {
                string arg = args[i];
                if (!arg.StartsWith("-", StringComparison.Ordinal))
                {
                    continue;
                }
                int eq = arg.IndexOf('=');
                if (eq > 0)
                {
                    values[arg.Substring(0, eq)] = arg.Substring(eq + 1);
                }
                else if (i + 1 < args.Count && !args[i + 1].StartsWith("--", StringComparison.Ordinal))
                {
                    values[arg] = args[i + 1];
                    i++;
                }
                else
                {
                    values[arg] = "";
                }
            }
            return new LaunchArgs(values);
        }
    }
}
