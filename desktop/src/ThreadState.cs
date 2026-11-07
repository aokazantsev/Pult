using System;
using System.Collections.Generic;

namespace Pult
{
    internal sealed class ThreadState
    {
        public const int RingSize = 200;
        public const int MaxTranscripts = 4;

        public string Id;
        public string Title = "…";
        public string Folder = "…";
        public string Status = "unknown";
        public bool PcDialog;
        public string ErrorCode;
        public string ParentId;
        public string ForkTitle;
        public string ForkThreadId;
        public string TurnNote;
        public string HostId;
        public string EventTitle;
        public string TranscriptTitle;
        public string MetaTitle;
        public string FirstUser;
        public bool DesktopApp;
        public long NextSeq = 1;
        public long LastEventTicks;
        public readonly List<string> SessionIds = new List<string>();
        public readonly List<Dictionary<string, object>> Ring = new List<Dictionary<string, object>>();
        public readonly List<TranscriptCursor> Cursors = new List<TranscriptCursor>();
        public readonly List<PhoneText> PhoneTexts = new List<PhoneText>();
        public readonly Dictionary<string, string> CallIds = new Dictionary<string, string>();
        public long PhoneSent;
        public string LastSentDescription;

        public long LastSeq
        {
            get { return NextSeq - 1; }
        }

        public string PickTitle()
        {
            foreach (string candidate in new[] { TranscriptTitle, EventTitle, MetaTitle, FirstUser })
            {
                if (candidate == null) continue;
                string line = candidate.Replace("\r\n", " ").Replace('\r', ' ').Replace('\n', ' ').Trim();
                if (line.Length == 0 || MessageRules.IsBlank(line)) continue;
                return MessageRules.Cut(line, 120);
            }
            return "…";
        }

        public Dictionary<string, object> ToStorage()
        {
            var transcripts = new Dictionary<string, object>();
            foreach (TranscriptCursor cursor in Cursors) transcripts[cursor.Path] = cursor.Offset;
            var map = new Dictionary<string, object>
            {
                { "id", Id },
                { "title", Title },
                { "folder", Folder },
                { "status", Status },
                { "pc_dialog", PcDialog },
                { "next_seq", NextSeq },
                { "last_event", LastEventTicks },
                { "desktop_app", DesktopApp },
                { "sessions", new List<object>(SessionIds) },
                { "transcripts", transcripts },
                { "ring", new List<object>(Ring) }
            };
            Put(map, "error", ErrorCode);
            Put(map, "parent", ParentId);
            Put(map, "fork_title", ForkTitle);
            Put(map, "fork_thread", ForkThreadId);
            Put(map, "turn_note", TurnNote);
            Put(map, "host", HostId);
            Put(map, "event_title", EventTitle);
            Put(map, "transcript_title", TranscriptTitle);
            Put(map, "meta_title", MetaTitle);
            Put(map, "first_user", FirstUser);
            return map;
        }

        public static ThreadState FromStorage(Dictionary<string, object> map)
        {
            string id = Json.String(map, "id");
            if (!StrictJson.IsIdentifier(id)) return null;
            var state = new ThreadState
            {
                Id = id,
                Title = Json.String(map, "title") ?? "…",
                Folder = Json.String(map, "folder") ?? "…",
                Status = Json.String(map, "status") ?? "unknown",
                PcDialog = Json.Bool(map, "pc_dialog"),
                ErrorCode = Json.String(map, "error"),
                ParentId = Json.String(map, "parent"),
                ForkTitle = Json.String(map, "fork_title"),
                ForkThreadId = Json.String(map, "fork_thread"),
                TurnNote = Json.String(map, "turn_note"),
                HostId = Json.String(map, "host"),
                EventTitle = Json.String(map, "event_title"),
                TranscriptTitle = Json.String(map, "transcript_title"),
                MetaTitle = Json.String(map, "meta_title"),
                FirstUser = Json.String(map, "first_user"),
                DesktopApp = Json.Bool(map, "desktop_app"),
                NextSeq = Math.Max(1, Json.Long(map, "next_seq", 1)),
                LastEventTicks = Json.Long(map, "last_event", 0)
            };
            foreach (object item in List(Json.Value(map, "sessions")))
            {
                var text = item as string;
                if (text != null) state.SessionIds.Add(text);
            }
            Dictionary<string, object> transcripts = Json.Object(map, "transcripts");
            if (transcripts != null)
            {
                foreach (KeyValuePair<string, object> pair in transcripts)
                {
                    TranscriptCursor cursor = TranscriptCursor.FromOffset(pair.Key, Convert.ToInt64(pair.Value));
                    if (cursor != null) state.Cursors.Add(cursor);
                }
            }
            foreach (object item in List(Json.Value(map, "ring")))
            {
                var entry = item as Dictionary<string, object>;
                if (entry != null) state.Ring.Add(entry);
            }
            return state;
        }

        private static IEnumerable<object> List(object value)
        {
            var array = value as object[];
            if (array != null) return array;
            var list = value as System.Collections.IList;
            var result = new List<object>();
            if (list != null)
            {
                foreach (object item in list) result.Add(item);
            }
            return result;
        }

        private static void Put(Dictionary<string, object> map, string key, string value)
        {
            if (value != null) map[key] = value;
        }
    }
}
