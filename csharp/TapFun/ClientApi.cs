#nullable enable
using System;
using System.Collections.Generic;

namespace TapFun
{
    /// <summary>
    /// Builds the requests of the TapFun client API. Each method returns the form fields to send;
    /// <c>req</c> is the API name. Send them with <see cref="TapFunClient.SendAsync"/>.
    /// The data passes through the player's browser, so do not put secrets in it.
    /// Options left empty (or 0) are not sent, and TapFun uses its default.
    /// </summary>
    public static class ClientApi
    {
        /// <summary>Length of a chunk of inputTextMultiLine text, in both directions.</summary>
        public const int ChunkLength = 128;

        /// <summary>The largest <c>seq</c> of inputTextMultiLineChunkText.</summary>
        public const int MaxChunks = 100;

        /// <summary>
        /// Answers to requests sent sooner than this after the game starts can be lost.
        /// Wait this long after the game starts before sending a request whose answer you need.
        /// </summary>
        public static readonly TimeSpan StartupWait = TimeSpan.FromSeconds(5);

        /// <summary>
        /// How long to wait for an answer before sending the request again. Send again only requests that show
        /// nothing to the player, such as getPlayerEnvironment (<see cref="TapFunClient.GetPlayerEnvironmentAsync"/>).
        /// </summary>
        public static readonly TimeSpan RetryInterval = TimeSpan.FromSeconds(3);

        /// <summary>Tells TapFun that the game has started. Send it once, when the game is ready to play.</summary>
        public static Dictionary<string, string> LaunchSuccessNotice() => Request("launchSuccessNotice");

        /// <summary>
        /// Asks for the player's environment. The answer has
        /// <c>from=getPlayerEnvironment</c>, <c>userAgent</c>, <c>isSP</c>, <c>authenticated</c>, <c>isPaidTimer</c>,
        /// <c>language</c> and <c>country</c> (the same values as <see cref="LaunchArgs.Language"/> and <see cref="LaunchArgs.Country"/>).
        /// </summary>
        public static Dictionary<string, string> GetPlayerEnvironment() => Request("getPlayerEnvironment");

        /// <summary>Opens a URL in a new tab of the player's browser. TapFun asks the player to confirm first.</summary>
        public static Dictionary<string, string> OpenUrl(string url)
        {
            Dictionary<string, string> request = Request("openUrl");
            request["url"] = url;
            return request;
        }

        /// <summary>
        /// Shows a single-line text input. The answer has <c>from=inputTextSingleLine</c>, <c>label</c>,
        /// <c>result</c> (<c>submit</c> or <c>cancel</c>) and <c>text</c>.
        /// </summary>
        /// <param name="label">Returned in the answer, to tell inputs apart.</param>
        /// <param name="title">Title of the input.</param>
        /// <param name="text">Initial text (up to 128 characters).</param>
        /// <param name="placeholder">Placeholder text.</param>
        /// <param name="submit">Label of the submit button.</param>
        /// <param name="cancel">Label of the cancel button.</param>
        /// <param name="maxLength">Max characters (up to 128). 0 means the default (128).</param>
        /// <param name="position"><c>bottom</c> to show it at the bottom. Empty means the center.</param>
        /// <param name="pattern">HTML <c>pattern</c> attribute that the text has to match.</param>
        /// <param name="invalid">Message shown when the text does not match <paramref name="pattern"/>.</param>
        public static Dictionary<string, string> InputTextSingleLine(
            string label, string title = "", string text = "", string placeholder = "",
            string submit = "", string cancel = "", int maxLength = 0, string position = "",
            string pattern = "", string invalid = "")
        {
            Dictionary<string, string> request = Request("inputTextSingleLine");
            request["label"] = label;
            AddIfNotEmpty(request, "title", title);
            AddIfNotEmpty(request, "text", text);
            AddIfNotEmpty(request, "placeholder", placeholder);
            AddIfNotEmpty(request, "submit", submit);
            AddIfNotEmpty(request, "cancel", cancel);
            AddIfNotEmpty(request, "maxlength", maxLength > 0 ? maxLength.ToString() : "");
            AddIfNotEmpty(request, "position", position);
            AddIfNotEmpty(request, "pattern", pattern);
            AddIfNotEmpty(request, "invalid", invalid);
            return request;
        }

        /// <summary>
        /// Shows a multi-line text input. The answer arrives through <see cref="TapFunClient.InputTextMultiLineReceived"/>.
        /// For a long initial text, send <see cref="InputTextMultiLineChunks"/> first and pass their count as <paramref name="end"/>.
        /// </summary>
        /// <param name="label">Returned in the answer, to tell inputs apart.</param>
        /// <param name="title">Title of the input.</param>
        /// <param name="text">Initial text. Ignored when <paramref name="end"/> is set.</param>
        /// <param name="placeholder">Placeholder text.</param>
        /// <param name="submit">Label of the submit button.</param>
        /// <param name="cancel">Label of the cancel button.</param>
        /// <param name="maxLength">Max characters (up to 10000; a line break counts as 1). 0 means the default (128).</param>
        /// <param name="position"><c>bottom</c> to show it at the bottom. Empty means the center.</param>
        /// <param name="end">Number of chunks sent with <see cref="InputTextMultiLineChunks"/>.</param>
        public static Dictionary<string, string> InputTextMultiLine(
            string label, string title = "", string text = "", string placeholder = "",
            string submit = "", string cancel = "", int maxLength = 0, string position = "", int end = 0)
        {
            Dictionary<string, string> request = Request("inputTextMultiLine");
            request["label"] = label;
            AddIfNotEmpty(request, "title", title);
            AddIfNotEmpty(request, "text", text);
            AddIfNotEmpty(request, "placeholder", placeholder);
            AddIfNotEmpty(request, "submit", submit);
            AddIfNotEmpty(request, "cancel", cancel);
            AddIfNotEmpty(request, "maxlength", maxLength > 0 ? maxLength.ToString() : "");
            AddIfNotEmpty(request, "position", position);
            AddIfNotEmpty(request, "end", end > 0 ? end.ToString() : "");
            return request;
        }

        /// <summary>
        /// Splits a long initial text for <see cref="InputTextMultiLine"/> into <c>inputTextMultiLineChunkText</c>
        /// requests (<c>seq</c> 1, 2, ...). Send them all, then send InputTextMultiLine with <c>end</c> = the number of chunks.
        /// A chunk never ends in the middle of a surrogate pair, so it can be 127 characters.
        /// </summary>
        /// <exception cref="ArgumentException">The text needs more than 100 chunks.</exception>
        public static List<Dictionary<string, string>> InputTextMultiLineChunks(string text)
        {
            var chunks = new List<Dictionary<string, string>>();
            for (int start = 0; start < text.Length;)
            {
                int length = Math.Min(ChunkLength, text.Length - start);
                if (start + length < text.Length && char.IsHighSurrogate(text[start + length - 1]))
                {
                    length--;
                }
                Dictionary<string, string> chunk = Request("inputTextMultiLineChunkText");
                chunk["text"] = text.Substring(start, length);
                chunk["seq"] = (chunks.Count + 1).ToString();
                chunks.Add(chunk);
                start += length;
            }
            if (chunks.Count > MaxChunks)
            {
                throw new ArgumentException($"The text needs {chunks.Count} chunks; up to {MaxChunks} can be sent.", nameof(text));
            }
            return chunks;
        }

        /// <summary>Shows text in a modal.</summary>
        public static Dictionary<string, string> ShowText(string text)
        {
            Dictionary<string, string> request = Request("showText");
            request["text"] = text;
            return request;
        }

        /// <summary>Shows a modal that lets the player copy text to the clipboard.</summary>
        public static Dictionary<string, string> CopyText(string text, string title = "")
        {
            Dictionary<string, string> request = Request("copyText");
            request["text"] = text;
            AddIfNotEmpty(request, "title", title);
            return request;
        }

        /// <summary>
        /// Writes a log line in the player's browser. Open the game page with <c>?debugLog=1</c>
        /// to see it on screen, or <c>?verbose=1</c> to see it in the developer tools console.
        /// </summary>
        public static Dictionary<string, string> ConsoleLog(string data)
        {
            Dictionary<string, string> request = Request("consoleLog");
            request["data"] = data;
            return request;
        }

        private static Dictionary<string, string> Request(string req) => new Dictionary<string, string> { ["req"] = req };

        private static void AddIfNotEmpty(Dictionary<string, string> request, string key, string value)
        {
            if (value != "")
            {
                request[key] = value;
            }
        }
    }
}
