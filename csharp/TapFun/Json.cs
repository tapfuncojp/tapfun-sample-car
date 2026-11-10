#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace TapFun
{
    /// <summary>
    /// Small JSON reader for the Web API responses, so the code needs no JSON package.
    /// Objects become <c>Dictionary&lt;string, object?&gt;</c>, arrays <c>List&lt;object?&gt;</c>,
    /// integers <c>long</c>, other numbers <c>double</c>, and <c>true</c> / <c>false</c> / <c>null</c> bool and null.
    /// </summary>
    internal static class Json
    {
        /// <exception cref="FormatException">The text is not valid JSON.</exception>
        public static object? Parse(string json)
        {
            var reader = new Reader(json);
            object? value = reader.ReadValue();
            reader.SkipWhitespace();
            if (!reader.AtEnd)
            {
                throw reader.Error("unexpected text after the value");
            }
            return value;
        }

        private sealed class Reader
        {
            private readonly string _s;
            private int _i;

            public Reader(string s)
            {
                _s = s;
            }

            public bool AtEnd => _i >= _s.Length;

            public FormatException Error(string message) => new FormatException($"Invalid JSON at {_i}: {message}");

            public void SkipWhitespace()
            {
                while (!AtEnd && (_s[_i] == ' ' || _s[_i] == '\t' || _s[_i] == '\n' || _s[_i] == '\r'))
                {
                    _i++;
                }
            }

            public object? ReadValue()
            {
                SkipWhitespace();
                if (AtEnd)
                {
                    throw Error("unexpected end");
                }
                char c = _s[_i];
                switch (c)
                {
                    case '{': return ReadObject();
                    case '[': return ReadArray();
                    case '"': return ReadString();
                    case 't': return ReadWord("true", true);
                    case 'f': return ReadWord("false", false);
                    case 'n': return ReadWord("null", null);
                    default:
                        if (c == '-' || (c >= '0' && c <= '9'))
                        {
                            return ReadNumber();
                        }
                        throw Error($"unexpected '{c}'");
                }
            }

            private Dictionary<string, object?> ReadObject()
            {
                var result = new Dictionary<string, object?>();
                _i++; // {
                SkipWhitespace();
                if (!AtEnd && _s[_i] == '}')
                {
                    _i++;
                    return result;
                }
                while (true)
                {
                    SkipWhitespace();
                    if (AtEnd || _s[_i] != '"')
                    {
                        throw Error("expected a property name");
                    }
                    string key = ReadString();
                    SkipWhitespace();
                    Expect(':');
                    result[key] = ReadValue();
                    SkipWhitespace();
                    if (!AtEnd && _s[_i] == ',')
                    {
                        _i++;
                        continue;
                    }
                    Expect('}');
                    return result;
                }
            }

            private List<object?> ReadArray()
            {
                var result = new List<object?>();
                _i++; // [
                SkipWhitespace();
                if (!AtEnd && _s[_i] == ']')
                {
                    _i++;
                    return result;
                }
                while (true)
                {
                    result.Add(ReadValue());
                    SkipWhitespace();
                    if (!AtEnd && _s[_i] == ',')
                    {
                        _i++;
                        continue;
                    }
                    Expect(']');
                    return result;
                }
            }

            private string ReadString()
            {
                var result = new StringBuilder();
                _i++; // "
                while (true)
                {
                    if (AtEnd)
                    {
                        throw Error("unterminated string");
                    }
                    char c = _s[_i++];
                    if (c == '"')
                    {
                        return result.ToString();
                    }
                    if (c < ' ')
                    {
                        throw Error("control character in a string");
                    }
                    if (c != '\\')
                    {
                        result.Append(c);
                        continue;
                    }
                    if (AtEnd)
                    {
                        throw Error("unterminated string");
                    }
                    char e = _s[_i++];
                    switch (e)
                    {
                        case '"': result.Append('"'); break;
                        case '\\': result.Append('\\'); break;
                        case '/': result.Append('/'); break;
                        case 'b': result.Append('\b'); break;
                        case 'f': result.Append('\f'); break;
                        case 'n': result.Append('\n'); break;
                        case 'r': result.Append('\r'); break;
                        case 't': result.Append('\t'); break;
                        case 'u':
                            if (_i + 4 > _s.Length ||
                                !int.TryParse(_s.Substring(_i, 4), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out int code))
                            {
                                throw Error("invalid \\u escape");
                            }
                            result.Append((char)code);
                            _i += 4;
                            break;
                        default:
                            throw Error($"invalid escape '\\{e}'");
                    }
                }
            }

            private object ReadNumber()
            {
                int start = _i;
                bool integer = true;
                while (!AtEnd && "+-0123456789.eE".IndexOf(_s[_i]) >= 0)
                {
                    if (_s[_i] == '.' || _s[_i] == 'e' || _s[_i] == 'E')
                    {
                        integer = false;
                    }
                    _i++;
                }
                string text = _s.Substring(start, _i - start);
                if (integer && long.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out long l))
                {
                    return l;
                }
                if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double d))
                {
                    return d;
                }
                _i = start;
                throw Error($"invalid number '{text}'");
            }

            private object? ReadWord(string word, object? value)
            {
                if (string.CompareOrdinal(_s, _i, word, 0, word.Length) != 0)
                {
                    throw Error($"expected '{word}'");
                }
                _i += word.Length;
                return value;
            }

            private void Expect(char c)
            {
                if (AtEnd || _s[_i] != c)
                {
                    throw Error($"expected '{c}'");
                }
                _i++;
            }
        }
    }
}
