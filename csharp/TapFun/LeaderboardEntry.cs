#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;

namespace TapFun
{
    /// <summary>One entry of <see cref="TapFunClient.GetLeaderboardEntriesAsync"/>.</summary>
    public sealed class LeaderboardEntry
    {
        /// <summary>Creates an entry.</summary>
        public LeaderboardEntry(int position, string userId, string userName, long score, string note)
        {
            Position = position;
            UserId = userId;
            UserName = userName;
            Score = score;
            Note = note;
        }

        /// <summary>Rank, starting from 1.</summary>
        public int Position { get; }

        /// <summary>TapFun user ID of the player. Compare it with <see cref="LaunchArgs.UserId"/>; do not show it on screen.</summary>
        public string UserId { get; }

        /// <summary>Player name to show.</summary>
        public string UserName { get; }

        /// <summary>Score.</summary>
        public long Score { get; }

        /// <summary>The note saved with the score, or an empty string.</summary>
        public string Note { get; }

        /// <summary>Reads the <c>entries</c> of a successful response.</summary>
        /// <exception cref="FormatException">The body is not a leaderboard response.</exception>
        public static List<LeaderboardEntry> ParseEntries(string json)
        {
            var entries = new List<LeaderboardEntry>();
            if (!(Json.Parse(json) is Dictionary<string, object?> root) || !root.TryGetValue("entries", out object? list) || list == null)
            {
                return entries;
            }
            if (!(list is List<object?> items))
            {
                throw new FormatException("entries is not an array");
            }
            foreach (object? item in items)
            {
                if (!(item is Dictionary<string, object?> e))
                {
                    throw new FormatException("an entry is not an object");
                }
                entries.Add(new LeaderboardEntry(
                    (int)ReadLong(e, "position"),
                    ReadString(e, "userId"),
                    ReadString(e, "userName"),
                    ReadLong(e, "score"),
                    ReadString(e, "note")));
            }
            return entries;
        }

        // Numbers can also come as strings ("123"), because the score is a 64-bit integer.
        private static long ReadLong(Dictionary<string, object?> e, string key)
        {
            e.TryGetValue(key, out object? value);
            switch (value)
            {
                case long l: return l;
                case double d: return (long)d;
                case string s when long.TryParse(s, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out long parsed): return parsed;
                default: throw new FormatException($"{key} is not a number");
            }
        }

        private static string ReadString(Dictionary<string, object?> e, string key) =>
            e.TryGetValue(key, out object? value) && value is string s ? s : "";
    }
}
