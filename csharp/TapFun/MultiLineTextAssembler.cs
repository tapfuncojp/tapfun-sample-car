#nullable enable
using System.Collections.Generic;
using System.Text;

namespace TapFun
{
    /// <summary>
    /// Joins the answer of inputTextMultiLine. TapFun sends the entered text in 128-character chunks,
    /// one UDP message per chunk (<c>seq</c> 1 to <c>end</c>). UDP does not guarantee the order, so the
    /// chunks are put together by <c>seq</c>.
    /// </summary>
    internal sealed class MultiLineTextAssembler
    {
        private readonly Dictionary<string, Dictionary<int, string>> _chunks = new Dictionary<string, Dictionary<int, string>>();

        /// <summary>
        /// Pass every message with <c>from=inputTextMultiLine</c>. Returns <c>true</c> when the answer is complete:
        /// <paramref name="result"/> is <c>submit</c> with the whole <paramref name="text"/>, or <c>cancel</c>.
        /// </summary>
        public bool TryAdd(IReadOnlyDictionary<string, string> message, out string label, out string result, out string text)
        {
            label = Value(message, "label");
            result = Value(message, "result");
            text = "";
            if (result != "submit")
            {
                _chunks.Remove(label);
                return true;
            }
            if (!int.TryParse(Value(message, "seq"), out int seq) || !int.TryParse(Value(message, "end"), out int end) ||
                seq < 1 || seq > end)
            {
                return false;
            }
            if (!_chunks.TryGetValue(label, out Dictionary<int, string>? parts))
            {
                parts = new Dictionary<int, string>();
                _chunks[label] = parts;
            }
            parts[seq] = Value(message, "text");
            var joined = new StringBuilder();
            for (int i = 1; i <= end; i++)
            {
                if (!parts.TryGetValue(i, out string? part))
                {
                    return false;
                }
                joined.Append(part);
            }
            text = joined.ToString();
            _chunks.Remove(label);
            return true;
        }

        private static string Value(IReadOnlyDictionary<string, string> message, string key) =>
            message.TryGetValue(key, out string? value) ? value : "";
    }
}
