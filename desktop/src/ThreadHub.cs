using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Pult
{
    internal sealed class ThreadHub
    {
        public const int MaxThreads = 100;
        public const string RestartNotice = "Pult на ПК перезапущен, часть ленты потеряна";
        private const int NegativeLimit = 1000;
        private const int StartCwdLimit = 2000;
        private const int PhoneTextLimit = 20;
        private const int ChunksPerSync = 4;

        private readonly object gate;
        private readonly IRelay relay;
        private readonly DesktopSessions sessions;
        private readonly string stateRoot;
        private readonly Dictionary<string, ThreadState> byId = new Dictionary<string, ThreadState>(StringComparer.Ordinal);
        private readonly Dictionary<string, ThreadState> byHost = new Dictionary<string, ThreadState>(StringComparer.Ordinal);
        private readonly Dictionary<string, ThreadState> bySession = new Dictionary<string, ThreadState>(StringComparer.Ordinal);
        private readonly HashSet<string> negative = new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> issued = new HashSet<string>(StringComparer.Ordinal);
        private readonly Dictionary<string, string> startCwds = new Dictionary<string, string>(StringComparer.Ordinal);
        private bool active;
        private string pair;
        private DateTime pairedAtUtc;
        private List<string> folders = new List<string>();
        private bool stateLost;

        public ThreadHub(object gate, IRelay relay, DesktopSessions sessions, string stateRoot)
        {
            this.gate = gate;
            this.relay = relay;
            this.sessions = sessions;
            this.stateRoot = stateRoot;
        }

        public Func<string, bool> HasOpenRequest = id => false;

        public bool Active
        {
            get
            {
                lock (gate) return active;
            }
        }

        public string Pair
        {
            get
            {
                lock (gate) return pair;
            }
        }

        public string SessionDirectory
        {
            get { return pair == null ? null : Path.Combine(stateRoot, "state-" + pair); }
        }

        public string SessionFile
        {
            get { return Path.Combine(SessionDirectory, "session.json"); }
        }

        public void Begin(string pairId, DateTime pairedAt, IList<string> marked, bool restored)
        {
            lock (gate)
            {
                Clear();
                active = true;
                pair = pairId;
                pairedAtUtc = pairedAt;
                folders = new List<string>(marked);
                stateLost = false;
                if (!restored) return;
                Dictionary<string, object> session = StateFiles.Read(SessionFile);
                if (session == null)
                {
                    stateLost = true;
                    AppLog.Append("session state of " + pairId + " is missing: threads start over");
                    return;
                }
                foreach (string uuid in StateFiles.ReadLines(Path.Combine(SessionDirectory, "uuids.txt"))) issued.Add(uuid);
                foreach (string file in Directory.GetFiles(SessionDirectory, "thread-*.json"))
                {
                    ThreadState state = ThreadState.FromStorage(StateFiles.Read(file));
                    if (state != null) Register(state);
                }
            }
        }

        public void End()
        {
            lock (gate)
            {
                string directory = SessionDirectory;
                Clear();
                active = false;
                pair = null;
                if (directory != null) StateFiles.DeleteDirectory(directory);
            }
        }

        public void RememberStart(HookEvent hookEvent)
        {
            if (hookEvent.Name != "SessionStart" || hookEvent.Source != "startup" || hookEvent.SessionId.Length == 0 || string.IsNullOrEmpty(hookEvent.Cwd)) return;
            lock (gate)
            {
                if (startCwds.Count >= StartCwdLimit) startCwds.Clear();
                startCwds[hookEvent.SessionId] = hookEvent.Cwd;
            }
        }

        public ThreadState Find(string threadId)
        {
            lock (gate)
            {
                ThreadState state;
                return threadId != null && byId.TryGetValue(threadId, out state) ? state : null;
            }
        }

        public List<ThreadState> All()
        {
            lock (gate) return byId.Values.ToList();
        }

        public ThreadState Admit(HookEvent hookEvent)
        {
            lock (gate)
            {
                if (!active || hookEvent.SessionId.Length == 0) return null;
                string host = hookEvent.HostSessionId;
                ThreadState state = null;
                if (host != null) byHost.TryGetValue(host, out state);
                if (state == null) bySession.TryGetValue(hookEvent.SessionId, out state);
                if (state != null)
                {
                    if (!bySession.ContainsKey(hookEvent.SessionId))
                    {
                        bySession[hookEvent.SessionId] = state;
                        state.SessionIds.Add(hookEvent.SessionId);
                    }
                    if (host != null && state.HostId == null)
                    {
                        state.HostId = host;
                        byHost[host] = state;
                    }
                    return state;
                }
                if (negative.Contains(hookEvent.SessionId)) return null;
                DesktopMeta meta = sessions == null ? null : sessions.Find(host, hookEvent.SessionId);
                string marked = ResolveFolder(hookEvent, meta);
                if (marked == null)
                {
                    if (negative.Count >= NegativeLimit) negative.Clear();
                    negative.Add(hookEvent.SessionId);
                    return null;
                }
                if (!MakeRoom()) return null;
                state = new ThreadState
                {
                    Id = Base64Url.Encode(PairCrypto.RandomBytes(16)),
                    Folder = FolderMembership.NameOf(marked),
                    HostId = host ?? (meta == null ? null : meta.LocalId),
                    DesktopApp = host != null,
                    LastEventTicks = DateTime.UtcNow.Ticks
                };
                state.SessionIds.Add(hookEvent.SessionId);
                if (meta != null) state.MetaTitle = meta.Title;
                state.Title = state.PickTitle();
                Register(state);
                AttachRelations(state, meta);
                Persist(state);
                if (stateLost)
                {
                    PushThread(state);
                    AppendEntries(state, new List<Dictionary<string, object>> { Notice(RestartNotice) });
                }
                return state;
            }
        }

        public void ApplyEvent(ThreadState state, HookEvent hookEvent)
        {
            lock (gate)
            {
                if (!string.IsNullOrEmpty(hookEvent.SessionTitle)) state.EventTitle = hookEvent.SessionTitle;
                state.LastEventTicks = DateTime.UtcNow.Ticks;
                string before = Json.Write(state.ToStorage().Where(p => p.Key != "ring" && p.Key != "last_event" && p.Key != "next_seq").ToDictionary(p => p.Key, p => p.Value));
                string transcript = hookEvent.TranscriptPath;
                if (!string.IsNullOrEmpty(transcript) && !state.Cursors.Any(c => string.Equals(c.Path, transcript, StringComparison.OrdinalIgnoreCase)))
                {
                    state.Cursors.Add(TranscriptCursor.FromTime(transcript, pairedAtUtc));
                    while (state.Cursors.Count > ThreadState.MaxTranscripts) state.Cursors.RemoveAt(0);
                }
                string status = StatusFor(hookEvent.Name);
                if (status != null)
                {
                    state.Status = status;
                    state.ErrorCode = status == "error" ? MessageRules.Cut(Json.String(hookEvent.Data, "error") ?? "unknown", 64) : null;
                    if (status == "working" || status == "ended") state.TurnNote = null;
                }
                if (hookEvent.Name != "Notification" && hookEvent.Name != "PermissionRequest") state.PcDialog = false;
                state.Title = state.PickTitle();
                PushThread(state);
                SyncLocked(state);
                state.Title = state.PickTitle();
                PushThread(state);
                string after = Json.Write(state.ToStorage().Where(p => p.Key != "ring" && p.Key != "last_event" && p.Key != "next_seq").ToDictionary(p => p.Key, p => p.Value));
                if (after != before) Persist(state);
            }
        }

        public void Sync(ThreadState state)
        {
            lock (gate)
            {
                SyncLocked(state);
                state.Title = state.PickTitle();
                Persist(state);
                PushThread(state);
            }
        }

        public void SyncAll()
        {
            lock (gate)
            {
                foreach (ThreadState state in byId.Values.ToList())
                {
                    if (state.Cursors.Count == 0) continue;
                    bool changed = SyncLocked(state);
                    if (!changed) continue;
                    state.Title = state.PickTitle();
                    Persist(state);
                    PushThread(state);
                }
            }
        }

        public void SetPcDialog(ThreadState state, bool value)
        {
            lock (gate)
            {
                if (state.PcDialog == value) return;
                state.PcDialog = value;
                Persist(state);
                PushThread(state);
            }
        }

        public void SetStatus(ThreadState state, string status)
        {
            lock (gate)
            {
                if (state.Status == status) return;
                state.Status = status;
                if (status == "working") state.TurnNote = null;
                Persist(state);
                PushThread(state);
            }
        }

        public void SetTurnNote(ThreadState state, string note)
        {
            lock (gate)
            {
                if (state.TurnNote == note) return;
                state.TurnNote = note;
                Persist(state);
                PushThread(state);
            }
        }

        public void NotePhoneText(ThreadState state, string text, string requestId)
        {
            lock (gate)
            {
                state.PhoneTexts.Add(new PhoneText { Text = text, RequestId = requestId });
                while (state.PhoneTexts.Count > PhoneTextLimit) state.PhoneTexts.RemoveAt(0);
            }
        }

        public Dictionary<string, object> Describe(ThreadState state, bool withLastSeq)
        {
            var description = new Dictionary<string, object>
            {
                { "thread", state.Id },
                { "title", state.Title },
                { "folder", state.Folder },
                { "status", state.Status },
                { "pc_dialog", state.PcDialog }
            };
            if (state.Status == "error" && state.ErrorCode != null) description["error"] = state.ErrorCode;
            if (state.ParentId != null) description["parent"] = state.ParentId;
            if (state.ForkTitle != null)
            {
                var fork = new Dictionary<string, object> { { "title", state.ForkTitle } };
                if (state.ForkThreadId != null) fork["thread"] = state.ForkThreadId;
                description["fork_of"] = fork;
            }
            if (state.TurnNote != null) description["turn_note"] = state.TurnNote;
            if (withLastSeq) description["last_seq"] = state.LastSeq;
            return description;
        }

        public void QueueSnapshot(string mode, List<Dictionary<string, object>> requests, List<Dictionary<string, object>> outcomes, IDictionary<string, long> helloFeeds)
        {
            lock (gate)
            {
                if (!active) return;
                string snapshotId = "s-" + Guid.NewGuid().ToString("N").Substring(0, 12);
                var parts = new List<Dictionary<string, object>>();
                var current = NewPart(snapshotId, 1, mode);
                foreach (ThreadState state in byId.Values.OrderByDescending(t => t.LastEventTicks)) current = AddToPart(parts, current, snapshotId, "threads", Describe(state, true));
                foreach (Dictionary<string, object> request in requests) current = AddToPart(parts, current, snapshotId, "requests", request);
                foreach (Dictionary<string, object> outcome in outcomes) current = AddToPart(parts, current, snapshotId, "outcomes", outcome);
                parts.Add(current);
                for (int i = 0; i < parts.Count; i++) parts[i]["last"] = i == parts.Count - 1;
                foreach (Dictionary<string, object> part in parts) relay.SendLive(part);
                foreach (ThreadState state in byId.Values) state.LastSentDescription = Json.Write(Describe(state, false));
                QueueTails(helloFeeds);
            }
        }

        private void QueueTails(IDictionary<string, long> helloFeeds)
        {
            var known = new Dictionary<string, long>();
            foreach (KeyValuePair<string, long> feed in helloFeeds)
            {
                ThreadState state;
                if (!byId.TryGetValue(feed.Key, out state)) continue;
                known[feed.Key] = Math.Max(feed.Value, state.PhoneSent);
            }
            var plan = new List<TailThread>();
            foreach (ThreadState state in byId.Values) plan.Add(new TailThread { Thread = state.Id, LastSeq = state.LastSeq, OpenRequest = HasOpenRequest(state.Id), LastEvent = state.LastEventTicks });
            Dictionary<string, long[]> ranges = TailPlan.Compute(known, plan, (thread, first, last) => TailCost(byId[thread.Thread], first, last));
            foreach (TailThread thread in plan.OrderBy(t => t.OpenRequest ? 0 : 1).ThenByDescending(t => t.LastEvent))
            {
                long[] range = ranges[thread.Thread];
                if (range == null) continue;
                ThreadState state = byId[thread.Thread];
                var entries = state.Ring.Where(e => Seq(e) >= range[0] && Seq(e) <= range[1]).ToList();
                if (entries.Count > 0) SendFeed(state, entries);
            }
        }

        private static long TailCost(ThreadState state, long first, long last)
        {
            long bytes = 0;
            int entries = 0;
            foreach (Dictionary<string, object> entry in state.Ring)
            {
                long seq = Seq(entry);
                if (seq < first || seq > last) continue;
                bytes += MessageRules.Size(entry) + 1;
                entries++;
            }
            long missing = (last - first + 1) - entries;
            if (missing > 0) bytes += missing * MessageRules.ItemBytes;
            long messages = bytes / MessageRules.MessageBytes + 1;
            return bytes + messages * FeedHeaderBytes;
        }

        private const int FeedHeaderBytes = 200;

        private static Dictionary<string, object> NewPart(string snapshotId, int number, string mode)
        {
            var part = new Dictionary<string, object> { { "type", "snapshot" }, { "snapshot", snapshotId }, { "part", (long)number } };
            if (number == 1) part["mode"] = mode;
            part["last"] = false;
            return part;
        }

        private static Dictionary<string, object> AddToPart(List<Dictionary<string, object>> parts, Dictionary<string, object> current, string snapshotId, string field, Dictionary<string, object> item)
        {
            object existing;
            var list = current.TryGetValue(field, out existing) ? (List<object>)existing : null;
            if (list == null)
            {
                list = new List<object>();
                current[field] = list;
            }
            list.Add(item);
            if (MessageRules.Size(current) <= MessageRules.MessageBytes) return current;
            list.RemoveAt(list.Count - 1);
            if (list.Count == 0) current.Remove(field);
            bool onlyHeader = !current.ContainsKey("threads") && !current.ContainsKey("requests") && !current.ContainsKey("outcomes");
            if (onlyHeader)
            {
                list = new List<object>();
                current[field] = list;
                list.Add(item);
                return current;
            }
            parts.Add(current);
            Dictionary<string, object> next = NewPart(snapshotId, parts.Count + 1, null);
            next[field] = new List<object> { item };
            return next;
        }

        private static long Seq(Dictionary<string, object> entry)
        {
            return Convert.ToInt64(entry["seq"]);
        }

        private bool SyncLocked(ThreadState state)
        {
            if (state.Cursors.Count == 0) return false;
            var context = new AdapterContext { PairedAtUtc = pairedAtUtc, Issued = issued, CallIds = state.CallIds, PhoneTexts = state.PhoneTexts, PeerResolver = ResolvePeer };
            foreach (TranscriptCursor cursor in state.Cursors)
            {
                int guard = ChunksPerSync;
                List<string> lines;
                while (guard-- > 0 && (lines = cursor.ReadNew()).Count > 0) TranscriptAdapter.Parse(lines, context);
            }
            if (context.CustomTitle != null) state.TranscriptTitle = context.CustomTitle;
            if (state.FirstUser == null && context.FirstUserText != null) state.FirstUser = context.FirstUserText;
            if (context.NewUuids.Count > 0) StateFiles.Append(Path.Combine(SessionDirectory, "uuids.txt"), context.NewUuids);
            if (context.Entries.Count == 0) return context.CustomTitle != null;
            AppendEntries(state, context.Entries);
            return true;
        }

        private string[] ResolvePeer(string localId)
        {
            ThreadState other;
            DesktopMeta meta = sessions == null ? null : sessions.ByLocalId(localId);
            string title = meta == null ? null : meta.Title;
            if (byHost.TryGetValue(localId, out other)) return new[] { title ?? other.Title, other.Id };
            return new[] { title ?? "…", null };
        }

        private void AppendEntries(ThreadState state, List<Dictionary<string, object>> entries)
        {
            foreach (Dictionary<string, object> entry in entries)
            {
                entry["seq"] = state.NextSeq++;
                state.Ring.Add(entry);
            }
            while (state.Ring.Count > ThreadState.RingSize) state.Ring.RemoveAt(0);
            Persist(state);
            SendFeed(state, entries);
        }

        private void SendFeed(ThreadState state, List<Dictionary<string, object>> entries)
        {
            var batch = new List<object>();
            int bytes = 200;
            foreach (Dictionary<string, object> entry in entries)
            {
                int size = MessageRules.Size(entry) + 1;
                if (batch.Count > 0 && bytes + size > MessageRules.MessageBytes) bytes = Flush(state, batch);
                batch.Add(entry);
                bytes += size;
            }
            if (batch.Count > 0) Flush(state, batch);
        }

        private int Flush(ThreadState state, List<object> batch)
        {
            long first = Convert.ToInt64(((Dictionary<string, object>)batch[0])["seq"]);
            long last = Convert.ToInt64(((Dictionary<string, object>)batch[batch.Count - 1])["seq"]);
            var message = new Dictionary<string, object> { { "type", "feed" }, { "thread", state.Id }, { "entries", new List<object>(batch) } };
            ThreadState target = state;
            relay.SendLive(message, delegate
            {
                lock (gate)
                {
                    if (relay.PhoneStreamOpen && first <= target.PhoneSent + 1 && last > target.PhoneSent) target.PhoneSent = last;
                }
            });
            batch.Clear();
            return 200;
        }

        public void PushThread(ThreadState state)
        {
            string description = Json.Write(Describe(state, false));
            if (description == state.LastSentDescription) return;
            state.LastSentDescription = description;
            var message = Describe(state, false);
            message["type"] = "thread";
            relay.SendLive(message);
        }

        private static Dictionary<string, object> Notice(string text)
        {
            return new Dictionary<string, object>
            {
                { "kind", "notice" },
                { "at", (long)(DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds },
                { "text", MessageRules.Cut(text, FeedLimits.NoticeLimit) }
            };
        }

        public void Persist(ThreadState state)
        {
            if (!active || pair == null) return;
            StateFiles.Write(Path.Combine(SessionDirectory, "thread-" + state.Id + ".json"), state.ToStorage());
        }

        private void Register(ThreadState state)
        {
            byId[state.Id] = state;
            if (state.HostId != null) byHost[state.HostId] = state;
            foreach (string id in state.SessionIds) bySession[id] = state;
        }

        private void Clear()
        {
            byId.Clear();
            byHost.Clear();
            bySession.Clear();
            negative.Clear();
            issued.Clear();
        }

        private bool MakeRoom()
        {
            if (byId.Count < MaxThreads) return true;
            ThreadState victim = byId.Values.Where(t => t.Status == "ended").OrderBy(t => t.LastEventTicks).FirstOrDefault();
            if (victim == null) victim = byId.Values.Where(t => !HasOpenRequest(t.Id)).OrderBy(t => t.LastEventTicks).FirstOrDefault();
            if (victim == null) return false;
            Remove(victim);
            return true;
        }

        private void Remove(ThreadState victim)
        {
            byId.Remove(victim.Id);
            if (victim.HostId != null) byHost.Remove(victim.HostId);
            foreach (string id in victim.SessionIds) bySession.Remove(id);
            foreach (ThreadState other in byId.Values)
            {
                if (other.ParentId == victim.Id) other.ParentId = null;
                if (other.ForkThreadId == victim.Id) other.ForkThreadId = null;
            }
            try
            {
                File.Delete(Path.Combine(SessionDirectory, "thread-" + victim.Id + ".json"));
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
            AppLog.Append("thread " + victim.Id + " dropped: more than " + MaxThreads + " threads");
        }

        private string ResolveFolder(HookEvent hookEvent, DesktopMeta meta)
        {
            string origin = meta == null ? null : (meta.OriginCwd ?? meta.Cwd);
            if (origin == null) startCwds.TryGetValue(hookEvent.SessionId, out origin);
            if (origin != null) return FolderMembership.MarkedFolderFor(origin, folders);
            return FolderMembership.FromTranscriptDirectory(hookEvent.TranscriptPath, folders);
        }

        private void AttachRelations(ThreadState state, DesktopMeta meta)
        {
            if (meta == null) return;
            if (!string.IsNullOrEmpty(meta.SpawnedFromLocalId))
            {
                ThreadState parent = ParentFor(meta.SpawnedFromLocalId, meta.SpawnedFromTitle);
                if (parent != null) state.ParentId = parent.Id;
            }
            if (!string.IsNullOrEmpty(meta.ForkedFromLocalId))
            {
                ThreadState source;
                byHost.TryGetValue(meta.ForkedFromLocalId, out source);
                DesktopMeta sourceMeta = sessions.ByLocalId(meta.ForkedFromLocalId);
                string title = sourceMeta != null && !string.IsNullOrWhiteSpace(sourceMeta.Title) ? sourceMeta.Title : (source == null ? null : source.Title);
                if (title != null)
                {
                    state.ForkTitle = MessageRules.Cut(title.Replace('\n', ' ').Trim(), 120);
                    if (source != null) state.ForkThreadId = source.Id;
                }
            }
        }

        private ThreadState ParentFor(string localId, string fallbackTitle)
        {
            ThreadState parent;
            if (byHost.TryGetValue(localId, out parent)) return parent;
            DesktopMeta meta = sessions.ByLocalId(localId);
            if (meta == null) return null;
            string marked = FolderMembership.MarkedFolderFor(meta.OriginCwd ?? meta.Cwd, folders);
            if (marked == null || !MakeRoom()) return null;
            parent = new ThreadState
            {
                Id = Base64Url.Encode(PairCrypto.RandomBytes(16)),
                Folder = FolderMembership.NameOf(marked),
                HostId = localId,
                DesktopApp = true,
                MetaTitle = string.IsNullOrWhiteSpace(meta.Title) ? fallbackTitle : meta.Title,
                LastEventTicks = DateTime.UtcNow.Ticks
            };
            parent.Title = parent.PickTitle();
            Register(parent);
            Persist(parent);
            PushThread(parent);
            if (stateLost) AppendEntries(parent, new List<Dictionary<string, object>> { Notice(RestartNotice) });
            return parent;
        }

        public static string StatusFor(string eventName)
        {
            switch (eventName)
            {
                case "SessionStart":
                case "UserPromptSubmit":
                case "PostToolUse":
                case "PostToolUseFailure":
                    return "working";
                case "Stop":
                    return "done";
                case "StopFailure":
                    return "error";
                case "SessionEnd":
                    return "ended";
                default:
                    return null;
            }
        }
    }
}
