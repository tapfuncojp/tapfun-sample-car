#nullable enable
using System;
using System.Collections.Generic;

namespace TapFun
{
    /// <summary>Decodes the query strings that TapFun sends over UDP (<c>key=value&amp;key=value</c>, UTF-8, percent-encoded).</summary>
    internal static class QueryString
    {
        public static Dictionary<string, string> Decode(string query)
        {
            var result = new Dictionary<string, string>();
            foreach (string pair in query.Split(new[] { '&' }, StringSplitOptions.RemoveEmptyEntries))
            {
                int eq = pair.IndexOf('=');
                string key = eq < 0 ? pair : pair.Substring(0, eq);
                string value = eq < 0 ? "" : pair.Substring(eq + 1);
                result[Unescape(key)] = Unescape(value);
            }
            return result;
        }

        private static string Unescape(string s) => Uri.UnescapeDataString(s.Replace('+', ' '));
    }
}
