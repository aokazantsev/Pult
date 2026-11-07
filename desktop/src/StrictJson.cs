using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Pult
{
    internal sealed class StrictJson
    {
        public const long MaxSafeInteger = 9007199254740991L;
        private const int MaxDepth = 64;

        private readonly string text;
        private int position;

        private StrictJson(string text)
        {
            this.text = text;
        }

        public static Dictionary<string, object> TryParseObject(string text)
        {
            if (text == null) return null;
            try
            {
                var parser = new StrictJson(text);
                parser.SkipSpace();
                object value = parser.ReadValue(0);
                parser.SkipSpace();
                if (parser.position != text.Length) return null;
                return value as Dictionary<string, object>;
            }
            catch (FormatException)
            {
                return null;
            }
        }

        public static bool IsSafeInteger(object value)
        {
            return value is long && (long)value >= 0 && (long)value <= MaxSafeInteger;
        }

        public static bool IsIdentifier(object value)
        {
            var text = value as string;
            if (text == null || text.Length < 1 || text.Length > 64) return false;
            foreach (char c in text)
            {
                bool allowed = (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '-' || c == '_';
                if (!allowed) return false;
            }
            return true;
        }

        private object ReadValue(int depth)
        {
            if (depth > MaxDepth) throw new FormatException("too deep");
            if (position >= text.Length) throw new FormatException("unexpected end");
            char c = text[position];
            if (c == '{') return ReadObject(depth);
            if (c == '[') return ReadArray(depth);
            if (c == '"') return ReadString();
            if (c == '-' || (c >= '0' && c <= '9')) return ReadNumber();
            if (Take("true")) return true;
            if (Take("false")) return false;
            if (Take("null")) return null;
            throw new FormatException("unexpected character");
        }

        private Dictionary<string, object> ReadObject(int depth)
        {
            var result = new Dictionary<string, object>(StringComparer.Ordinal);
            position++;
            SkipSpace();
            if (Peek() == '}')
            {
                position++;
                return result;
            }
            while (true)
            {
                SkipSpace();
                if (Peek() != '"') throw new FormatException("key expected");
                string key = ReadString();
                SkipSpace();
                Expect(':');
                SkipSpace();
                object value = ReadValue(depth + 1);
                if (result.ContainsKey(key)) throw new FormatException("duplicate key");
                result.Add(key, value);
                SkipSpace();
                char next = Peek();
                position++;
                if (next == '}') return result;
                if (next != ',') throw new FormatException("comma expected");
            }
        }

        private object[] ReadArray(int depth)
        {
            var result = new List<object>();
            position++;
            SkipSpace();
            if (Peek() == ']')
            {
                position++;
                return result.ToArray();
            }
            while (true)
            {
                SkipSpace();
                result.Add(ReadValue(depth + 1));
                SkipSpace();
                char next = Peek();
                position++;
                if (next == ']') return result.ToArray();
                if (next != ',') throw new FormatException("comma expected");
            }
        }

        private string ReadString()
        {
            Expect('"');
            var result = new StringBuilder();
            while (true)
            {
                if (position >= text.Length) throw new FormatException("unterminated string");
                char c = text[position++];
                if (c == '"') return result.ToString();
                if (c < 0x20) throw new FormatException("control character in string");
                if (c != '\\')
                {
                    result.Append(c);
                    continue;
                }
                if (position >= text.Length) throw new FormatException("unterminated escape");
                char escape = text[position++];
                switch (escape)
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
                        if (position + 4 > text.Length) throw new FormatException("short unicode escape");
                        int code;
                        if (!int.TryParse(text.Substring(position, 4), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out code)) throw new FormatException("bad unicode escape");
                        position += 4;
                        result.Append((char)code);
                        break;
                    default:
                        throw new FormatException("bad escape");
                }
            }
        }

        private object ReadNumber()
        {
            int start = position;
            if (Peek() == '-') position++;
            if (Peek() == '0') position++;
            else if (IsDigit(Peek())) SkipDigits();
            else throw new FormatException("digit expected");
            bool integer = true;
            if (Peek() == '.')
            {
                integer = false;
                position++;
                if (!IsDigit(Peek())) throw new FormatException("digit expected");
                SkipDigits();
            }
            if (Peek() == 'e' || Peek() == 'E')
            {
                integer = false;
                position++;
                if (Peek() == '+' || Peek() == '-') position++;
                if (!IsDigit(Peek())) throw new FormatException("digit expected");
                SkipDigits();
            }
            string literal = text.Substring(start, position - start);
            long whole;
            if (integer && long.TryParse(literal, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out whole)) return whole;
            double fraction;
            if (!double.TryParse(literal, NumberStyles.Float, CultureInfo.InvariantCulture, out fraction)) throw new FormatException("number out of range");
            return fraction;
        }

        private void SkipDigits()
        {
            while (IsDigit(Peek())) position++;
        }

        private static bool IsDigit(char c)
        {
            return c >= '0' && c <= '9';
        }

        private bool Take(string literal)
        {
            if (string.CompareOrdinal(text, position, literal, 0, literal.Length) != 0) return false;
            position += literal.Length;
            return true;
        }

        private char Peek()
        {
            return position < text.Length ? text[position] : '\0';
        }

        private void Expect(char c)
        {
            if (Peek() != c) throw new FormatException("'" + c + "' expected");
            position++;
        }

        private void SkipSpace()
        {
            while (position < text.Length)
            {
                char c = text[position];
                if (c != ' ' && c != '\t' && c != '\n' && c != '\r') return;
                position++;
            }
        }
    }
}
