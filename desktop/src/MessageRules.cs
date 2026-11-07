using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Pult
{
    internal static class MessageRules
    {
        public const int MessageBytes = 48000;
        public const int ItemBytes = 16384;
        private const string WhiteSpace = "\u0009\u000A\u000B\u000C\u000D\u0020\u0085\u00A0\u1680\u2000\u2001\u2002\u2003\u2004\u2005\u2006\u2007\u2008\u2009\u200A\u2028\u2029\u202F\u205F\u3000";
        private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false);

        private sealed class Rejected : Exception
        {
            public Rejected(string reason)
                : base(reason)
            {
            }
        }

        public static string Check(Dictionary<string, object> message, string to)
        {
            try
            {
                Validate(message, to);
                return null;
            }
            catch (Rejected error)
            {
                return error.Message;
            }
        }

        public static bool IsBlank(string text)
        {
            foreach (char c in text)
            {
                if (WhiteSpace.IndexOf(c) < 0) return false;
            }
            return true;
        }

        public static int Length(string text)
        {
            int count = 0;
            for (int i = 0; i < text.Length; i++)
            {
                if (char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1])) i++;
                count++;
            }
            return count;
        }

        public static string Cut(string text, int limit)
        {
            if (text == null) return "";
            if (Length(text) <= limit) return text;
            var result = new StringBuilder();
            int count = 0;
            for (int i = 0; i < text.Length && count < limit - 1; i++)
            {
                result.Append(text[i]);
                if (char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1])) result.Append(text[++i]);
                count++;
            }
            return result.Append('…').ToString();
        }

        public static int Size(object value)
        {
            return Utf8.GetByteCount(Json.Write(value));
        }

        public static bool IsInteger(object value)
        {
            if (value is long) return (long)value >= 0 && (long)value <= StrictJson.MaxSafeInteger;
            if (value is int) return (int)value >= 0;
            return false;
        }

        private static void Validate(Dictionary<string, object> message, string to)
        {
            if (message == null) throw new Rejected("not an object");
            NoNulls(message);
            if (Size(message) > MessageBytes) throw new Rejected("message too large");
            object typeValue = Need(message, "type", true);
            var type = typeValue as string;
            if (type == null) throw new Rejected("bad type");
            bool known = Array.IndexOf(ToPhone, type) >= 0 || Array.IndexOf(ToPc, type) >= 0;
            if (!known) return;
            if (Array.IndexOf(to == "phone" ? ToPhone : ToPc, type) < 0) throw new Rejected("type not for " + to);
            switch (type)
            {
                case "thread":
                    CheckThread(message, false);
                    break;
                case "feed":
                    CheckFeed(message);
                    break;
                case "permission_request":
                case "choice_request":
                case "text_request":
                    CheckRequest(message);
                    break;
                case "resolved":
                    Ident(message, "request_id", true);
                    Text(message, "outcome", 64, true, false);
                    break;
                case "mode":
                    Text(message, "mode", 64, true, false);
                    break;
                case "snapshot":
                    CheckSnapshot(message);
                    break;
                case "hello":
                    CheckHello(message, to);
                    break;
                case "permission_response":
                    Ident(message, "request_id", true);
                    string decision = Need(message, "decision", true) as string;
                    if (decision != "allow" && decision != "deny") throw new Rejected("bad decision");
                    Text(message, "reason", 1000, false, false);
                    break;
                case "choice_response":
                    CheckChoiceResponse(message);
                    break;
                case "text_response":
                    Ident(message, "request_id", true);
                    Text(message, "text", 8000, true, true);
                    break;
            }
        }

        private static readonly string[] ToPhone = { "thread", "feed", "permission_request", "choice_request", "text_request", "resolved", "mode", "snapshot", "hello" };
        private static readonly string[] ToPc = { "permission_response", "choice_response", "text_response", "hello" };

        private static void CheckThread(Dictionary<string, object> desc, bool inSnapshot)
        {
            ObjectOf(desc, "thread");
            Ident(desc, "thread", true);
            Text(desc, "title", 120, true, true);
            Text(desc, "folder", 120, true, false);
            Text(desc, "status", 64, true, false);
            Bool(desc, "pc_dialog", true);
            Text(desc, "error", 64, false, false);
            Ident(desc, "parent", false);
            object fork = Need(desc, "fork_of", false);
            if (fork != null)
            {
                Dictionary<string, object> forkMap = ObjectOf(fork, "fork_of");
                Text(forkMap, "title", 120, true, false);
                Ident(forkMap, "thread", false);
            }
            Text(desc, "turn_note", 64, false, false);
            Int(desc, "last_seq", inSnapshot, 0);
        }

        private static void CheckInput(object value)
        {
            var map = value as Dictionary<string, object>;
            if (map == null) throw new Rejected("input not an object");
            foreach (object item in map.Values)
            {
                var text = item as string;
                if (text != null)
                {
                    if (Length(text) > 1000) throw new Rejected("input value too long");
                }
                else if (item == null || item is IDictionary || item is Array) throw new Rejected("input value not flat");
            }
        }

        private static void CheckFeed(Dictionary<string, object> message)
        {
            Ident(message, "thread", true);
            object[] entries = ArrayOf(message, "entries", true, 1, -1);
            long previous = -1;
            foreach (object item in entries)
            {
                Dictionary<string, object> entry = ObjectOf(item, "entry");
                CheckEntry(entry);
                long seq = Convert.ToInt64(entry["seq"], CultureInfo.InvariantCulture);
                if (previous >= 0 && seq != previous + 1) throw new Rejected("seq not contiguous");
                previous = seq;
            }
        }

        private static void CheckEntry(Dictionary<string, object> entry)
        {
            Int(entry, "seq", true, 1);
            string kind = Text(entry, "kind", 64, true, false);
            Int(entry, "at", false, 0);
            Bool(entry, "truncated", false);
            switch (kind)
            {
                case "user":
                    Text(entry, "text", 8000, true, false);
                    Text(entry, "via", 64, false, false);
                    Ident(entry, "request_id", false);
                    break;
                case "assistant":
                    Text(entry, "text", 8000, true, false);
                    break;
                case "tool_call":
                    Ident(entry, "call", true);
                    Text(entry, "tool", 64, true, false);
                    Text(entry, "summary", 300, true, false);
                    if (entry.ContainsKey("input")) CheckInput(entry["input"]);
                    break;
                case "tool_result":
                    Ident(entry, "call", true);
                    Text(entry, "output", 2000, true, false);
                    Bool(entry, "error", true);
                    break;
                case "peer":
                    Text(entry, "direction", 64, true, false);
                    Text(entry, "peer_title", 120, true, false);
                    Text(entry, "text", 8000, true, false);
                    Ident(entry, "peer", false);
                    break;
                case "notice":
                    Text(entry, "text", 300, true, false);
                    break;
                case "unrecognized":
                    Int(entry, "count", true, 1);
                    break;
            }
        }

        private static void CheckRequest(Dictionary<string, object> message)
        {
            ObjectOf(message, "request");
            Ident(message, "request_id", true);
            Ident(message, "thread", true);
            Text(message, "title", 120, true, false);
            Int(message, "after_seq", true, 0);
            switch ((string)message["type"])
            {
                case "permission_request":
                    Text(message, "tool", 64, true, false);
                    Text(message, "summary", 300, true, false);
                    if (message.ContainsKey("input")) CheckInput(message["input"]);
                    break;
                case "choice_request":
                    CheckChoiceRequest(message);
                    break;
                case "text_request":
                    Text(message, "last_message", 4000, true, false);
                    break;
                default:
                    throw new Rejected("not a request");
            }
        }

        private static void CheckChoiceRequest(Dictionary<string, object> message)
        {
            Bool(message, "answerable", true);
            object[] questions = ArrayOf(message, "questions", true, 1, 4);
            var seen = new HashSet<string>();
            foreach (object item in questions)
            {
                Dictionary<string, object> question = ObjectOf(item, "question");
                if (!seen.Add(Ident(question, "id", true))) throw new Rejected("duplicate question id");
                Text(question, "header", 50, false, false);
                Text(question, "question", 500, true, false);
                Bool(question, "multi", true);
                Bool(question, "other", true);
                var optionIds = new HashSet<string>();
                foreach (object optionItem in ArrayOf(question, "options", true, 1, 8))
                {
                    Dictionary<string, object> option = ObjectOf(optionItem, "option");
                    if (!optionIds.Add(Ident(option, "id", true))) throw new Rejected("duplicate option id");
                    Text(option, "label", 100, true, false);
                    Text(option, "description", 300, false, false);
                }
            }
        }

        private static void CheckSnapshot(Dictionary<string, object> message)
        {
            Ident(message, "snapshot", true);
            long part = Int(message, "part", true, 1);
            Bool(message, "last", true);
            Text(message, "mode", 64, part == 1, false);
            foreach (object item in ArrayOf(message, "threads", false, 0, -1) ?? new object[0]) CheckThread(ObjectOf(item, "thread"), true);
            foreach (object item in ArrayOf(message, "requests", false, 0, -1) ?? new object[0])
            {
                Dictionary<string, object> request = ObjectOf(item, "request");
                CheckRequest(request);
            }
            foreach (object item in ArrayOf(message, "outcomes", false, 0, -1) ?? new object[0])
            {
                Dictionary<string, object> outcome = ObjectOf(item, "outcome");
                Ident(outcome, "request_id", true);
                Text(outcome, "kind", 64, true, false);
                Ident(outcome, "thread", true);
                Int(outcome, "after_seq", true, 0);
                Text(outcome, "summary", 120, true, false);
                Text(outcome, "outcome", 64, true, false);
            }
        }

        private static void CheckHello(Dictionary<string, object> message, string to)
        {
            foreach (object version in ArrayOf(message, "protocol", true, 1, -1))
            {
                if (!(version is long) && !(version is int)) throw new Rejected("bad protocol");
            }
            foreach (object capability in ArrayOf(message, "capabilities", true, 0, -1))
            {
                if (!(capability is string)) throw new Rejected("bad capability");
            }
            object[] feeds = ArrayOf(message, "feeds", false, 0, -1);
            if (feeds == null) return;
            if (to != "pc") throw new Rejected("feeds only from phone");
            var threads = new HashSet<string>();
            foreach (object item in feeds)
            {
                Dictionary<string, object> feed = ObjectOf(item, "feeds");
                if (!threads.Add(Ident(feed, "thread", true))) throw new Rejected("duplicate feeds thread");
                Int(feed, "seq", true, 0);
            }
        }

        private static void CheckChoiceResponse(Dictionary<string, object> message)
        {
            Ident(message, "request_id", true);
            foreach (object item in ArrayOf(message, "answers", true, 1, -1))
            {
                Dictionary<string, object> answer = ObjectOf(item, "answer");
                Ident(answer, "question", true);
                foreach (object selected in ArrayOf(answer, "selected", true, 0, -1))
                {
                    if (!StrictJson.IsIdentifier(selected)) throw new Rejected("bad selected");
                }
                Text(answer, "other", 2000, false, false);
            }
        }

        private static void NoNulls(object value)
        {
            if (value == null) throw new Rejected("null");
            var map = value as IDictionary<string, object>;
            if (map != null)
            {
                foreach (object item in map.Values) NoNulls(item);
                return;
            }
            var list = value as IEnumerable;
            if (list != null && !(value is string))
            {
                foreach (object item in list) NoNulls(item);
            }
        }

        private static object Need(Dictionary<string, object> map, string key, bool required)
        {
            if (!map.ContainsKey(key))
            {
                if (required) throw new Rejected("missing " + key);
                return null;
            }
            return map[key];
        }

        private static string Ident(Dictionary<string, object> map, string key, bool required)
        {
            object value = Need(map, key, required);
            if (value == null) return null;
            if (!StrictJson.IsIdentifier(value)) throw new Rejected("bad id " + key);
            return (string)value;
        }

        private static string Text(Dictionary<string, object> map, string key, int limit, bool required, bool nonEmpty)
        {
            object value = Need(map, key, required);
            if (value == null) return null;
            var text = value as string;
            if (text == null) throw new Rejected("not a string " + key);
            if (Length(text) > limit) throw new Rejected("too long " + key);
            if (nonEmpty && IsBlank(text)) throw new Rejected("empty " + key);
            return text;
        }

        private static long Int(Dictionary<string, object> map, string key, bool required, long minimum)
        {
            object value = Need(map, key, required);
            if (value == null) return 0;
            if (!IsInteger(value)) throw new Rejected("bad int " + key);
            long number = Convert.ToInt64(value, CultureInfo.InvariantCulture);
            if (number < minimum) throw new Rejected("bad int " + key);
            return number;
        }

        private static bool Bool(Dictionary<string, object> map, string key, bool required)
        {
            object value = Need(map, key, required);
            if (value == null) return false;
            if (!(value is bool)) throw new Rejected("not bool " + key);
            return (bool)value;
        }

        private static object[] ArrayOf(Dictionary<string, object> map, string key, bool required, int minimum, int maximum)
        {
            object value = Need(map, key, required);
            if (value == null) return null;
            object[] list = AsArray(value);
            if (list == null || list.Length < minimum || (maximum >= 0 && list.Length > maximum)) throw new Rejected("bad array " + key);
            return list;
        }

        private static object[] AsArray(object value)
        {
            var array = value as object[];
            if (array != null) return array;
            var list = value as IList;
            if (list == null || value is string) return null;
            var result = new object[list.Count];
            list.CopyTo(result, 0);
            return result;
        }

        private static Dictionary<string, object> ObjectOf(object value, string label)
        {
            var map = value as Dictionary<string, object>;
            if (map == null) throw new Rejected("not an object " + label);
            if (Size(map) > ItemBytes) throw new Rejected("item too large " + label);
            return map;
        }
    }
}
