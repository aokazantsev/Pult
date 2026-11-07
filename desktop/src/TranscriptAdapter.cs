using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Text;

namespace Pult
{
    internal sealed class PhoneText
    {
        public string Text;
        public string RequestId;
    }

    internal sealed class AdapterContext
    {
        public DateTime PairedAtUtc = DateTime.MinValue;
        public HashSet<string> Issued = new HashSet<string>();
        public Dictionary<string, string> CallIds = new Dictionary<string, string>();
        public List<PhoneText> PhoneTexts = new List<PhoneText>();
        public List<Dictionary<string, object>> Entries = new List<Dictionary<string, object>>();
        public List<string> NewUuids = new List<string>();
        public string CustomTitle;
        public string FirstUserText;
        public int Candidates;
        public Func<string, string[]> PeerResolver = id => new string[] { "…", null };
        internal int PendingUnrecognized;
        internal long PendingAt;
    }

    internal static class TranscriptAdapter
    {
        public const string StopFeedbackPrefix = "Stop hook feedback:";
        public const string InterruptedForToolUse = "[Request interrupted by user for tool use]";
        public const string Interrupted = "[Request interrupted by user]";
        private static readonly Regex CrossSession = new Regex(@"^\s*<cross-session-message\s+from=""([^""]+)""[^>]*>(.*?)</cross-session-message>\s*$", RegexOptions.Singleline | RegexOptions.CultureInvariant);
        public const string AttachmentNotice = "вложение не показано";
        private static readonly HashSet<string> Silent = new HashSet<string>(StringComparer.Ordinal) { "attachment", "system", "progress", "summary" };

        public static void Parse(IEnumerable<string> lines, AdapterContext context)
        {
            foreach (string line in lines)
            {
                Dictionary<string, object> record = Json.TryParseObject(line);
                if (record == null) continue;
                string type = Json.String(record, "type");
                if (type == "custom-title" || type == "agent-name")
                {
                    string title = Json.String(record, type == "custom-title" ? "customTitle" : "agentName");
                    if (!string.IsNullOrWhiteSpace(title) && (type == "custom-title" || context.CustomTitle == null)) context.CustomTitle = title;
                    continue;
                }
                string uuid = Json.String(record, "uuid");
                string stamp = Json.String(record, "timestamp");
                if (string.IsNullOrEmpty(uuid) || string.IsNullOrEmpty(stamp)) continue;
                DateTime time;
                if (!DateTime.TryParse(stamp, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out time)) continue;
                if (time < context.PairedAtUtc) continue;
                if (context.Issued.Contains(uuid)) continue;
                context.Issued.Add(uuid);
                context.NewUuids.Add(uuid);
                context.Candidates++;
                if (Json.Bool(record, "isSidechain")) continue;
                long at = (long)(time - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds;
                context.PendingAt = at;
                if (type == "user") ReadUser(record, at, context);
                else if (type == "assistant") ReadAssistant(record, at, context);
                else if (type == null || !Silent.Contains(type)) context.PendingUnrecognized++;
            }
            Flush(context);
        }

        private static void Flush(AdapterContext context)
        {
            if (context.PendingUnrecognized == 0) return;
            var entry = new Dictionary<string, object> { { "kind", "unrecognized" }, { "count", (long)context.PendingUnrecognized } };
            if (context.PendingAt > 0) entry["at"] = context.PendingAt;
            context.Entries.Add(entry);
            context.PendingUnrecognized = 0;
        }

        private static void Emit(AdapterContext context, Dictionary<string, object> entry)
        {
            Flush(context);
            context.Entries.Add(entry);
            if (context.FirstUserText == null && (string)entry["kind"] == "user" && !entry.ContainsKey("via")) context.FirstUserText = (string)entry["text"];
        }

        private static void ReadUser(Dictionary<string, object> record, long at, AdapterContext context)
        {
            Dictionary<string, object> message = Json.Object(record, "message");
            object content = Json.Value(message, "content");
            var text = content as string;
            bool meta = Json.Bool(record, "isMeta");
            if (text != null)
            {
                if (text.StartsWith(StopFeedbackPrefix, StringComparison.Ordinal))
                {
                    string body = text.Substring(StopFeedbackPrefix.Length);
                    if (body.StartsWith("\r\n", StringComparison.Ordinal)) body = body.Substring(2);
                    else if (body.StartsWith("\n", StringComparison.Ordinal)) body = body.Substring(1);
                    Emit(context, UserEntry(body, at, TakePhoneText(context, body)));
                    return;
                }
                if (meta) return;
                AddText(text, at, context);
                return;
            }
            if (meta) return;
            IList blocks = content as IList;
            if (blocks == null)
            {
                context.PendingUnrecognized++;
                return;
            }
            foreach (object item in blocks)
            {
                var block = item as Dictionary<string, object>;
                string blockType = Json.String(block, "type");
                if (blockType == "text") AddText(Json.String(block, "text") ?? "", at, context);
                else if (blockType == "tool_result") Emit(context, ToolResult(block, at, context));
                else if (blockType == "image" || blockType == "document") Emit(context, Notice(AttachmentNotice, at));
                else context.PendingUnrecognized++;
            }
        }

        private static void AddText(string text, long at, AdapterContext context)
        {
            string trimmed = text.Trim();
            if (trimmed.Length == 0) return;
            Match peer = CrossSession.Match(text);
            if (peer.Success)
            {
                Emit(context, PeerEntry(peer.Groups[1].Value, peer.Groups[2].Value.Trim(), at, context));
                return;
            }
            if (trimmed == InterruptedForToolUse) Emit(context, Notice("Запрос прерван на ПК", at));
            else if (trimmed == Interrupted) Emit(context, Notice("Ход прерван на ПК", at));
            else Emit(context, UserEntry(text, at, null));
        }

        private static PhoneText TakePhoneText(AdapterContext context, string body)
        {
            string wanted = body.Trim();
            for (int i = 0; i < context.PhoneTexts.Count; i++)
            {
                if (context.PhoneTexts[i].Text.Trim() == wanted)
                {
                    PhoneText found = context.PhoneTexts[i];
                    context.PhoneTexts.RemoveRange(0, i + 1);
                    return found;
                }
            }
            return null;
        }

        private static Dictionary<string, object> UserEntry(string text, long at, PhoneText phone)
        {
            var entry = new Dictionary<string, object> { { "kind", "user" }, { "at", at } };
            SetText(entry, "text", text, FeedLimits.TextLimit);
            if (phone != null)
            {
                entry["via"] = "phone";
                entry["request_id"] = phone.RequestId;
            }
            return Finish(entry);
        }

        private static Dictionary<string, object> PeerEntry(string from, string body, long at, AdapterContext context)
        {
            string[] who = context.PeerResolver(from);
            var entry = new Dictionary<string, object> { { "kind", "peer" }, { "at", at }, { "direction", "in" } };
            string title = who != null && who.Length > 0 && !string.IsNullOrWhiteSpace(who[0]) ? who[0] : "…";
            entry["peer_title"] = MessageRules.Cut(new string(title.Select(c => c == '\n' || c == '\r' ? ' ' : c).ToArray()).Trim(), 120);
            SetText(entry, "text", body, FeedLimits.TextLimit);
            if (who != null && who.Length > 1 && StrictJson.IsIdentifier(who[1])) entry["peer"] = who[1];
            return Finish(entry);
        }

        private static Dictionary<string, object> Notice(string text, long at)
        {
            var entry = new Dictionary<string, object> { { "kind", "notice" }, { "at", at } };
            SetText(entry, "text", text, FeedLimits.NoticeLimit);
            return entry;
        }

        private static void ReadAssistant(Dictionary<string, object> record, long at, AdapterContext context)
        {
            IList blocks = Json.Value(Json.Object(record, "message"), "content") as IList;
            if (blocks == null)
            {
                context.PendingUnrecognized++;
                return;
            }
            foreach (object item in blocks)
            {
                var block = item as Dictionary<string, object>;
                string blockType = Json.String(block, "type");
                if (blockType == "text")
                {
                    string text = Json.String(block, "text") ?? "";
                    if (text.Trim().Length == 0) continue;
                    var entry = new Dictionary<string, object> { { "kind", "assistant" }, { "at", at } };
                    SetText(entry, "text", text, FeedLimits.TextLimit);
                    Emit(context, Finish(entry));
                }
                else if (blockType == "thinking" || blockType == "redacted_thinking") continue;
                else if (blockType == "tool_use") Emit(context, ToolCall(block, at, context));
                else context.PendingUnrecognized++;
            }
        }

        private static Dictionary<string, object> ToolCall(Dictionary<string, object> block, long at, AdapterContext context)
        {
            string tool = Json.String(block, "name") ?? "";
            object input = Json.Value(block, "input");
            var entry = new Dictionary<string, object>
            {
                { "kind", "tool_call" },
                { "at", at },
                { "call", CallId(Json.String(block, "id"), context) },
                { "tool", MessageRules.Cut(tool, 64) }
            };
            SetText(entry, "summary", ToolInputs.Summary(tool, input), ToolInputs.SummaryLimit);
            Dictionary<string, object> trimmed = ToolInputs.Trim(input);
            if (trimmed != null) entry["input"] = trimmed;
            return Finish(entry);
        }

        private static Dictionary<string, object> ToolResult(Dictionary<string, object> block, long at, AdapterContext context)
        {
            var entry = new Dictionary<string, object>
            {
                { "kind", "tool_result" },
                { "at", at },
                { "call", CallId(Json.String(block, "tool_use_id"), context) },
                { "error", Json.Bool(block, "is_error") }
            };
            SetText(entry, "output", ResultText(Json.Value(block, "content")), FeedLimits.OutputLimit);
            return Finish(entry);
        }

        private static string ResultText(object content)
        {
            var text = content as string;
            if (text != null) return text;
            IList blocks = content as IList;
            if (blocks == null) return "";
            var result = new StringBuilder();
            foreach (object item in blocks)
            {
                var block = item as Dictionary<string, object>;
                if (Json.String(block, "type") != "text") continue;
                if (result.Length > 0) result.Append('\n');
                result.Append(Json.String(block, "text"));
            }
            return result.ToString();
        }

        private static string CallId(string original, AdapterContext context)
        {
            if (StrictJson.IsIdentifier(original)) return original;
            string key = original ?? "";
            string mapped;
            if (!context.CallIds.TryGetValue(key, out mapped))
            {
                mapped = "c" + (context.CallIds.Count + 1).ToString(CultureInfo.InvariantCulture) + "-" + Guid.NewGuid().ToString("N").Substring(0, 8);
                context.CallIds[key] = mapped;
            }
            return mapped;
        }

        private static void SetText(Dictionary<string, object> entry, string field, string text, int limit)
        {
            string cut = MessageRules.Cut(text, limit);
            entry[field] = cut;
            if (cut != text) entry["truncated"] = true;
        }

        private static Dictionary<string, object> Finish(Dictionary<string, object> entry)
        {
            FeedLimits.Fit(entry);
            return entry;
        }
    }
}
