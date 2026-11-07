using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;

namespace Pult
{
    internal sealed class Broker : IRelayListener
    {
        public static readonly TimeSpan HookMargin = TimeSpan.FromSeconds(5);
        public const int RecentOutcomesLimit = 50;
        public const int ResolvedTtlSeconds = 900;
        public const int DefaultSnapshotDelayMs = 1000;
        public const int DefaultTickMs = 1000;
        private const int ClosedOutcomesLimit = 500;
        private const int LastMessageLimit = 4000;
        private const int OutcomeSummaryLimit = 120;
        private const string DeniedFromPhone = "Запрещено с телефона";
        private const string ChoiceTool = "AskUserQuestion";

        private readonly object gate = new object();
        private readonly IRelay relay;
        private readonly TimeSpan wait;
        private readonly int pollMs;
        private readonly int snapshotDelayMs;
        private readonly ThreadHub hub;
        private readonly List<PendingRequest> pending = new List<PendingRequest>();
        private readonly Dictionary<string, string> closedOutcomes = new Dictionary<string, string>();
        private readonly Queue<string> closedOrder = new Queue<string>();
        private readonly List<Dictionary<string, object>> recentOutcomes = new List<Dictionary<string, object>>();
        private readonly Dictionary<string, int> stopStreaks = new Dictionary<string, int>();
        private readonly Dictionary<string, long> helloFeeds = new Dictionary<string, long>();
        private readonly List<string> helloCapabilities = new List<string>();
        private readonly Timer tick;
        private Timer snapshotTimer;
        private bool snapshotPending;
        private ControlMode mode = ControlMode.Phone;
        private bool stopped;
        private bool choiceDelivery = true;
        private long lastHookTicks = DateTime.UtcNow.Ticks;

        public Broker(IRelay relay, TimeSpan wait, int pollMs)
            : this(relay, wait, pollMs, null, UserDataPaths.Directory, DefaultTickMs, DefaultSnapshotDelayMs)
        {
        }

        public Broker(IRelay relay, TimeSpan wait, int pollMs, DesktopSessions sessions, string stateRoot, int tickMs, int snapshotDelayMs)
        {
            this.relay = relay;
            this.wait = wait;
            this.pollMs = pollMs;
            this.snapshotDelayMs = snapshotDelayMs;
            hub = new ThreadHub(gate, relay, sessions ?? new DesktopSessions(null), stateRoot);
            hub.HasOpenRequest = HasOpenRequestFor;
            if (tickMs > 0) tick = new Timer(state => Tick(), null, tickMs, tickMs);
        }

        public ThreadHub Hub
        {
            get { return hub; }
        }

        public bool ChoiceDelivery
        {
            get { return choiceDelivery; }
            set { choiceDelivery = value; }
        }

        public ControlMode Mode
        {
            get
            {
                lock (gate) return mode;
            }
        }

        public int OpenRequests
        {
            get
            {
                lock (gate) return pending.Count(r => !r.Released);
            }
        }

        public DateTime LastHookUtc
        {
            get { return new DateTime(Interlocked.Read(ref lastHookTicks), DateTimeKind.Utc); }
        }

        public void ResetIdle()
        {
            Interlocked.Exchange(ref lastHookTicks, DateTime.UtcNow.Ticks);
        }

        public void BeginSession(string pair, DateTime pairedAtUtc, IList<string> folders, bool restored)
        {
            lock (gate)
            {
                hub.Begin(pair, pairedAtUtc, folders, restored);
                closedOutcomes.Clear();
                closedOrder.Clear();
                recentOutcomes.Clear();
                helloFeeds.Clear();
                helloCapabilities.Clear();
                stopStreaks.Clear();
                if (restored) LoadSession();
                SaveSession();
            }
            ResetIdle();
        }

        public void EndSession()
        {
            lock (gate)
            {
                foreach (PendingRequest request in pending.ToList())
                {
                    request.NotifyPhone = false;
                    if (request.Ghost) pending.Remove(request);
                    Release(request, null, null, "session ended");
                }
                hub.End();
                closedOutcomes.Clear();
                closedOrder.Clear();
                recentOutcomes.Clear();
                helloFeeds.Clear();
                helloCapabilities.Clear();
            }
        }

        public void SetMode(ControlMode value)
        {
            lock (gate)
            {
                if (mode == value) return;
                mode = value;
                AppLog.Append("mode: " + ModeName(value));
                if (value == ControlMode.Pc)
                {
                    foreach (PendingRequest request in pending.ToList())
                    {
                        if (request.Ghost) CloseGhost(request, Outcome.ResolvedOnPc, "mode pc");
                        else Release(request, null, Outcome.ResolvedOnPc, "mode pc");
                    }
                }
            }
            relay.SendLive(ModeMessage(value));
        }

        public void Stop()
        {
            lock (gate)
            {
                stopped = true;
                foreach (PendingRequest request in pending.ToList())
                {
                    if (request.Ghost) pending.Remove(request);
                    else Release(request, null, Outcome.Expired, "desktop exit");
                }
                if (tick != null) tick.Dispose();
                if (snapshotTimer != null) snapshotTimer.Dispose();
            }
        }

        public void Tick()
        {
            lock (gate)
            {
                if (stopped) return;
                hub.SyncAll();
                foreach (PendingRequest request in pending.Where(r => r.Ghost).ToList())
                {
                    TranscriptSignals signals = request.Transcript == null ? new TranscriptSignals() : TranscriptSignals.Scan(request.Transcript.ReadNew());
                    if (signals.DeniedOnPc) CloseGhost(request, Outcome.ResolvedOnPc, "denied on pc");
                    else if (DateTime.UtcNow >= request.DeadlineUtc) CloseGhost(request, Outcome.Expired, "expired");
                }
            }
        }

        public void Handle(HookEvent hookEvent, TimeSpan hookLimit, IHookChannel channel)
        {
            hub.RememberStart(hookEvent);
            ThreadState thread;
            lock (gate)
            {
                thread = hub.Admit(hookEvent);
                if (thread != null)
                {
                    hub.ApplyEvent(thread, hookEvent);
                    ResetIdle();
                }
                if (hookEvent.Name == "SessionStart") AppLog.Append("thread " + Short(hookEvent.SessionId) + " " + (hookEvent.Source ?? "started") + (thread == null ? " (not in the session)" : ""));
            }
            switch (hookEvent.Name)
            {
                case "PermissionRequest":
                    HandlePermission(hookEvent, hookLimit, channel, thread);
                    return;
                case "Stop":
                    HandleStop(hookEvent, hookLimit, channel, thread);
                    return;
                case "PostToolUse":
                case "PostToolUseFailure":
                    channel.Deliver(null);
                    ResolveByToolUse(hookEvent);
                    return;
                case "StopFailure":
                    channel.Deliver(null);
                    AppLog.Append("thread " + Short(hookEvent.SessionId) + " stopped with error " + Json.String(hookEvent.Data, "error"));
                    return;
                default:
                    channel.Deliver(null);
                    return;
            }
        }

        public void OnRelayConnected()
        {
            relay.SendLive(ModeMessage(Mode));
            QueueSnapshot();
        }

        public void OnPhoneStreamChanged()
        {
            lock (gate) Monitor.PulseAll(gate);
        }

        public void OnPhoneMessage(Dictionary<string, object> inner)
        {
            string type = Json.String(inner, "type");
            string requestId = Json.String(inner, "request_id");
            switch (type)
            {
                case "hello":
                    OnHello(inner);
                    return;
                case "permission_response":
                    Answer(requestId, RequestKind.Permission, delegate(PendingRequest request) { return PermissionOutput(inner); });
                    return;
                case "choice_response":
                    Answer(requestId, RequestKind.Choice, delegate(PendingRequest request) { return ChoiceOutput(request, inner); });
                    return;
                case "text_response":
                    Answer(requestId, RequestKind.Text, delegate(PendingRequest request)
                    {
                        string output = TextOutput(Json.String(inner, "text"));
                        if (output != null && request.Thread != null) hub.NotePhoneText(request.Thread, Json.String(inner, "text"), request.Id);
                        return output;
                    });
                    return;
                default:
                    if (requestId != null) Answer(requestId, null, null);
                    return;
            }
        }

        private void OnHello(Dictionary<string, object> inner)
        {
            lock (gate)
            {
                helloCapabilities.Clear();
                foreach (object capability in Items(Json.Value(inner, "capabilities"))) helloCapabilities.Add((string)capability);
                helloFeeds.Clear();
                foreach (object item in Items(Json.Value(inner, "feeds")))
                {
                    var feed = (Dictionary<string, object>)item;
                    helloFeeds[Json.String(feed, "thread")] = Convert.ToInt64(feed["seq"], CultureInfo.InvariantCulture);
                }
                foreach (ThreadState state in hub.All())
                {
                    long seq;
                    state.PhoneSent = helloFeeds.TryGetValue(state.Id, out seq) ? seq : 0;
                }
                SaveSession();
            }
            ScheduleSnapshot();
        }

        private void ScheduleSnapshot()
        {
            if (snapshotDelayMs <= 0)
            {
                QueueSnapshot();
                return;
            }
            lock (gate)
            {
                if (stopped || snapshotPending) return;
                snapshotPending = true;
                if (snapshotTimer == null) snapshotTimer = new Timer(state => SnapshotDue(), null, Timeout.Infinite, Timeout.Infinite);
                snapshotTimer.Change(snapshotDelayMs, Timeout.Infinite);
            }
        }

        private void SnapshotDue()
        {
            lock (gate) snapshotPending = false;
            QueueSnapshot();
        }

        public void QueueSnapshot()
        {
            lock (gate)
            {
                if (stopped || !hub.Active) return;
                hub.SyncAll();
                var requests = new List<Dictionary<string, object>>();
                foreach (PendingRequest request in pending)
                {
                    if (!request.Released) requests.Add(new Dictionary<string, object>(request.Message));
                }
                hub.QueueSnapshot(ModeName(mode), requests, new List<Dictionary<string, object>>(recentOutcomes), new Dictionary<string, long>(helloFeeds));
            }
        }

        private bool PhoneHasChoice
        {
            get
            {
                lock (gate) return helloCapabilities.Contains("choice");
            }
        }

        private bool HasOpenRequestFor(string threadId)
        {
            lock (gate) return pending.Any(r => !r.Released && r.Thread != null && r.Thread.Id == threadId);
        }

        private void HandlePermission(HookEvent hookEvent, TimeSpan hookLimit, IHookChannel channel, ThreadState thread)
        {
            bool choice = hookEvent.ToolName == ChoiceTool;
            string refusal = thread == null ? "thread not in the session" : RouteRefusal();
            if (refusal == null && choice && !PhoneHasChoice) refusal = "phone cannot answer questions";
            Dictionary<string, object> question = null;
            if (refusal == null && choice)
            {
                question = ChoiceMessage(hookEvent.ToolInput);
                if (question == null) refusal = "question is not readable";
            }
            if (refusal != null)
            {
                AppLog.Append((choice ? "question in " : "permission " + hookEvent.ToolName + " in ") + Short(hookEvent.SessionId) + " stays on pc: " + refusal);
                if (thread != null) hub.SetPcDialog(thread, true);
                channel.Deliver(null);
                return;
            }
            string id = NewRequestId();
            Dictionary<string, object> message;
            if (choice)
            {
                message = question;
                message["type"] = "choice_request";
                message["request_id"] = id;
                message["thread"] = thread.Id;
                message["title"] = thread.Title;
                message["answerable"] = choiceDelivery;
            }
            else
            {
                message = new Dictionary<string, object>
                {
                    { "type", "permission_request" },
                    { "request_id", id },
                    { "thread", thread.Id },
                    { "title", thread.Title },
                    { "tool", MessageRules.Cut(hookEvent.ToolName, 64) },
                    { "summary", ToolInputs.Summary(hookEvent.ToolName, hookEvent.ToolInput) }
                };
                Dictionary<string, object> input = ToolInputs.Trim(hookEvent.ToolInput);
                if (input != null) message["input"] = input;
            }
            FeedLimits.Fit(message);
            var request = new PendingRequest(id, choice ? RequestKind.Choice : RequestKind.Permission, hookEvent, Deadline(hookLimit), message, thread);
            request.Summary = choice ? ChoiceSummary(message) : MessageRules.Cut(hookEvent.ToolName + ": " + (string)message["summary"], OutcomeSummaryLimit);
            if (choice && !choiceDelivery)
            {
                RegisterGhost(request);
                channel.Deliver(null);
                return;
            }
            if (Wait(request, channel) == Outcome.Answered) hub.SetStatus(thread, "working");
        }

        private void HandleStop(HookEvent hookEvent, TimeSpan hookLimit, IHookChannel channel, ThreadState thread)
        {
            int streak;
            lock (gate)
            {
                stopStreaks.TryGetValue(hookEvent.SessionId, out streak);
                streak = hookEvent.StopHookActive ? streak + 1 : 0;
                stopStreaks[hookEvent.SessionId] = streak;
            }
            string refusal = thread == null ? "thread not in the session" : RouteRefusal();
            if (refusal == null && !relay.PhoneStreamOpen) refusal = "phone stream closed";
            bool capped = false;
            if (refusal == null && streak >= hookEvent.StopBlockCap)
            {
                refusal = "continuation cap " + hookEvent.StopBlockCap + " reached";
                capped = true;
            }
            if (refusal != null)
            {
                AppLog.Append("text window for " + Short(hookEvent.SessionId) + " not opened: " + refusal);
                if (capped) hub.SetTurnNote(thread, "continuation_cap");
                channel.Deliver(null);
                return;
            }
            string id = NewRequestId();
            var message = new Dictionary<string, object>
            {
                { "type", "text_request" },
                { "request_id", id },
                { "thread", thread.Id },
                { "title", thread.Title },
                { "last_message", MessageRules.Cut(hookEvent.LastAssistantMessage ?? "", LastMessageLimit) }
            };
            var request = new PendingRequest(id, RequestKind.Text, hookEvent, Deadline(hookLimit), message, thread);
            request.Summary = "Окно текста";
            string outcome = Wait(request, channel);
            if (outcome == Outcome.Answered) hub.SetStatus(thread, "working");
        }

        private string RouteRefusal()
        {
            lock (gate)
            {
                if (stopped) return "desktop exiting";
                if (mode == ControlMode.Pc) return "mode pc";
            }
            if (!relay.Configured) return "relay not configured";
            if (!relay.PhoneCompatible) return "phone incompatible";
            return null;
        }

        private void RegisterGhost(PendingRequest request)
        {
            request.Ghost = true;
            lock (gate)
            {
                pending.Add(request);
                hub.Sync(request.Thread);
                request.Message["after_seq"] = request.Thread.LastSeq;
                hub.SetPcDialog(request.Thread, true);
                SaveSession();
                relay.Send(request.Message, request.DeadlineUtc, delegate
                {
                    lock (gate)
                    {
                        if (pending.Remove(request))
                        {
                            request.NotifyPhone = false;
                            request.Released = true;
                            SaveSession();
                        }
                    }
                });
            }
            AppLog.Append("request " + request.Id + " " + Describe(request) + ": shown without actions");
        }

        private void CloseGhost(PendingRequest request, string outcome, string cause)
        {
            if (request.Released) return;
            request.Released = true;
            request.Outcome = outcome;
            request.Cause = cause;
            pending.Remove(request);
            RememberClosed(request);
            AdjustDialogMark(request, outcome);
            SaveSession();
            AppLog.Append("request " + request.Id + ": " + outcome + " (" + cause + ")");
            if (request.NotifyPhone) SendResolved(request.Id, outcome);
        }

        private string Wait(PendingRequest request, IHookChannel channel)
        {
            lock (gate)
            {
                pending.Add(request);
                if (request.Thread != null)
                {
                    hub.Sync(request.Thread);
                    request.Message["after_seq"] = request.Thread.LastSeq;
                    if (request.Kind != RequestKind.Text && request.Thread.DesktopApp) hub.SetPcDialog(request.Thread, true);
                }
                SaveSession();
                AppLog.Append("request " + request.Id + " " + Describe(request) + ": sent, wait until " + request.DeadlineUtc.ToLocalTime().ToString("HH:mm:ss"));
                relay.Send(request.Message, request.DeadlineUtc, delegate
                {
                    lock (gate)
                    {
                        if (Release(request, null, null, "relay refused")) request.NotifyPhone = false;
                    }
                });
            }
            while (true)
            {
                TranscriptSignals signals = request.Transcript == null ? new TranscriptSignals() : TranscriptSignals.Scan(request.Transcript.ReadNew());
                bool gone = channel.IsGone;
                lock (gate)
                {
                    if (!request.Released)
                    {
                        if (request.Kind != RequestKind.Text && signals.DeniedOnPc) Release(request, null, Outcome.ResolvedOnPc, "denied on pc");
                        else if (request.Kind == RequestKind.Text && signals.Enqueued) Release(request, null, Outcome.ResolvedOnPc, "input queued on pc");
                        else if (request.Kind == RequestKind.Text && !relay.PhoneStreamOpen) Release(request, null, Outcome.Expired, "phone stream closed");
                        else if (gone) Release(request, null, Outcome.Expired, "hook gone");
                        else if (DateTime.UtcNow >= request.DeadlineUtc) Release(request, null, Outcome.Expired, "expired");
                    }
                    if (request.Released) break;
                    Monitor.Wait(gate, pollMs);
                }
            }
            string outcome = request.Outcome;
            if (!string.IsNullOrEmpty(request.Output))
            {
                lock (gate)
                {
                    request.Handed = true;
                    SaveSession();
                }
                HookAck ack = channel.Deliver(request.Output);
                if (ack == HookAck.Refused) outcome = Outcome.Expired;
                else if (ack == HookAck.Lost) outcome = Outcome.Unknown;
                AppLog.Append("request " + request.Id + ": hook " + ack.ToString().ToLowerInvariant());
            }
            else
            {
                channel.Deliver(null);
            }
            lock (gate)
            {
                pending.Remove(request);
                request.Outcome = outcome;
                if (outcome != null && request.NotifyPhone) RememberClosed(request);
                else if (outcome != null) closedOutcomes[request.Id] = outcome;
                AdjustDialogMark(request, outcome);
                SaveSession();
            }
            AppLog.Append("request " + request.Id + ": " + (outcome ?? "not delivered") + " (" + request.Cause + ")");
            if (request.NotifyPhone && outcome != null) SendResolved(request.Id, outcome);
            return outcome;
        }

        private void AdjustDialogMark(PendingRequest request, string outcome)
        {
            if (request.Thread == null || request.Kind == RequestKind.Text) return;
            if (outcome == Outcome.Answered || request.Cause == "allowed on pc" || request.Cause == "denied on pc") hub.SetPcDialog(request.Thread, false);
            else if (outcome == Outcome.Expired || request.Cause == "mode pc" || request.Cause == "relay refused" || request.Cause == "hook gone") hub.SetPcDialog(request.Thread, true);
        }

        private bool Release(PendingRequest request, string output, string outcome, string cause)
        {
            if (request.Released) return false;
            request.Released = true;
            request.Output = output;
            request.Outcome = outcome;
            request.Cause = cause;
            Monitor.PulseAll(gate);
            return true;
        }

        private void Answer(string requestId, RequestKind? kind, Func<PendingRequest, string> build)
        {
            string closed = null;
            lock (gate)
            {
                PendingRequest request = pending.FirstOrDefault(r => r.Id == requestId);
                if (request != null && request.Ghost)
                {
                    AppLog.Append("phone answer for request " + requestId + " without actions rejected");
                    return;
                }
                if (request != null && !request.Released && kind.HasValue && request.Kind == kind.Value && build != null)
                {
                    string output = build(request);
                    if (output == null) return;
                    Release(request, output, Outcome.Answered, "phone answer");
                    return;
                }
                if (request != null) return;
                if (requestId == null || !closedOutcomes.TryGetValue(requestId, out closed)) closed = Outcome.Expired;
            }
            AppLog.Append("phone answer for closed request " + requestId + ": " + closed);
            if (requestId != null) SendResolved(requestId, closed);
        }

        private void SendResolved(string id, string outcome)
        {
            relay.Send(ResolvedMessage(id, outcome), DateTime.UtcNow.AddSeconds(ResolvedTtlSeconds), delegate { });
        }

        private void ResolveByToolUse(HookEvent hookEvent)
        {
            string key = hookEvent.ToolInputKey;
            lock (gate)
            {
                PendingRequest request = pending.FirstOrDefault(r => !r.Released && r.Kind != RequestKind.Text
                    && r.SessionId == hookEvent.SessionId && r.ToolName == hookEvent.ToolName && r.ToolInputKey == key);
                if (request == null) return;
                if (request.Ghost) CloseGhost(request, Outcome.ResolvedOnPc, "allowed on pc");
                else Release(request, null, Outcome.ResolvedOnPc, "allowed on pc");
            }
        }

        private void RememberClosed(PendingRequest request)
        {
            closedOutcomes[request.Id] = request.Outcome;
            closedOrder.Enqueue(request.Id);
            while (closedOrder.Count > ClosedOutcomesLimit) closedOutcomes.Remove(closedOrder.Dequeue());
            if (request.Thread == null) return;
            recentOutcomes.Add(new Dictionary<string, object>
            {
                { "request_id", request.Id },
                { "kind", request.KindName },
                { "thread", request.Thread.Id },
                { "after_seq", Convert.ToInt64(request.Message["after_seq"], CultureInfo.InvariantCulture) },
                { "summary", MessageRules.Cut(request.Summary ?? "", OutcomeSummaryLimit) },
                { "outcome", request.Outcome }
            });
            while (recentOutcomes.Count > RecentOutcomesLimit) recentOutcomes.RemoveAt(0);
        }

        private void SaveSession()
        {
            if (!hub.Active) return;
            var open = new List<object>();
            foreach (PendingRequest request in pending)
            {
                if (request.Thread == null) continue;
                open.Add(new Dictionary<string, object>
                {
                    { "id", request.Id },
                    { "kind", request.KindName },
                    { "thread", request.Thread.Id },
                    { "after_seq", Convert.ToInt64(request.Message["after_seq"], CultureInfo.InvariantCulture) },
                    { "summary", MessageRules.Cut(request.Summary ?? "", OutcomeSummaryLimit) },
                    { "handed", request.Handed }
                });
            }
            var closed = new Dictionary<string, object>();
            foreach (KeyValuePair<string, string> pair in closedOutcomes) closed[pair.Key] = pair.Value;
            var feeds = new Dictionary<string, object>();
            foreach (KeyValuePair<string, long> pair in helloFeeds) feeds[pair.Key] = pair.Value;
            var session = new Dictionary<string, object>
            {
                { "pair", hub.Pair },
                { "requests", open },
                { "outcomes", new List<object>(recentOutcomes) },
                { "closed", closed },
                { "hello", new Dictionary<string, object> { { "capabilities", new List<object>(helloCapabilities) }, { "feeds", feeds } } }
            };
            StateFiles.Write(hub.SessionFile, session);
        }

        private void LoadSession()
        {
            Dictionary<string, object> session = StateFiles.Read(hub.SessionFile);
            if (session == null) return;
            Dictionary<string, object> closed = Json.Object(session, "closed");
            if (closed != null)
            {
                foreach (KeyValuePair<string, object> pair in closed)
                {
                    var outcome = pair.Value as string;
                    if (outcome == null) continue;
                    closedOutcomes[pair.Key] = outcome;
                    closedOrder.Enqueue(pair.Key);
                }
            }
            foreach (object item in Items(Json.Value(session, "outcomes")))
            {
                var record = item as Dictionary<string, object>;
                if (record != null) recentOutcomes.Add(record);
            }
            Dictionary<string, object> hello = Json.Object(session, "hello");
            foreach (object capability in Items(Json.Value(hello, "capabilities")))
            {
                var text = capability as string;
                if (text != null) helloCapabilities.Add(text);
            }
            Dictionary<string, object> feeds = Json.Object(hello, "feeds");
            if (feeds != null)
            {
                foreach (KeyValuePair<string, object> pair in feeds) helloFeeds[pair.Key] = Convert.ToInt64(pair.Value, CultureInfo.InvariantCulture);
            }
            foreach (object item in Items(Json.Value(session, "requests")))
            {
                var saved = item as Dictionary<string, object>;
                if (saved == null) continue;
                string id = Json.String(saved, "id");
                string outcome = Json.Bool(saved, "handed") ? Outcome.Unknown : Outcome.Expired;
                string kind = Json.String(saved, "kind");
                ThreadState thread = hub.Find(Json.String(saved, "thread"));
                if (thread == null || !StrictJson.IsIdentifier(id)) continue;
                closedOutcomes[id] = outcome;
                closedOrder.Enqueue(id);
                recentOutcomes.Add(new Dictionary<string, object>
                {
                    { "request_id", id },
                    { "kind", kind },
                    { "thread", thread.Id },
                    { "after_seq", Json.Long(saved, "after_seq", 0) },
                    { "summary", Json.String(saved, "summary") ?? "" },
                    { "outcome", outcome }
                });
                if (outcome == Outcome.Expired && kind != "text") hub.SetPcDialog(thread, true);
                SendResolved(id, outcome);
                AppLog.Append("request " + id + " closed after restart: " + outcome);
            }
            while (recentOutcomes.Count > RecentOutcomesLimit) recentOutcomes.RemoveAt(0);
            while (closedOrder.Count > ClosedOutcomesLimit) closedOutcomes.Remove(closedOrder.Dequeue());
        }

        private DateTime Deadline(TimeSpan hookLimit)
        {
            TimeSpan limit = hookLimit - HookMargin;
            TimeSpan span = limit < wait ? limit : wait;
            if (span < TimeSpan.Zero) span = TimeSpan.Zero;
            return DateTime.UtcNow + span;
        }

        private static IEnumerable<object> Items(object value)
        {
            var array = value as object[];
            if (array != null) return array;
            var list = value as IList;
            var result = new List<object>();
            if (list != null)
            {
                foreach (object item in list) result.Add(item);
            }
            return result;
        }

        public static Dictionary<string, object> ChoiceMessage(object toolInput)
        {
            var input = toolInput as Dictionary<string, object>;
            IList questions = Json.Value(input, "questions") as IList;
            if (questions == null || questions.Count < 1 || questions.Count > 4) return null;
            var list = new List<object>();
            int number = 0;
            foreach (object item in questions)
            {
                var question = item as Dictionary<string, object>;
                string text = Json.String(question, "question");
                IList options = Json.Value(question, "options") as IList;
                if (string.IsNullOrWhiteSpace(text) || options == null || options.Count < 1 || options.Count > 8) return null;
                number++;
                var entry = new Dictionary<string, object> { { "id", "q" + number.ToString(CultureInfo.InvariantCulture) } };
                string header = Json.String(question, "header");
                if (!string.IsNullOrWhiteSpace(header)) entry["header"] = MessageRules.Cut(header, 50);
                entry["question"] = MessageRules.Cut(text, 500);
                entry["multi"] = Json.Bool(question, "multiSelect");
                entry["other"] = true;
                var optionList = new List<object>();
                int optionNumber = 0;
                foreach (object optionItem in options)
                {
                    var option = optionItem as Dictionary<string, object>;
                    string label = Json.String(option, "label");
                    if (string.IsNullOrWhiteSpace(label)) return null;
                    optionNumber++;
                    var optionEntry = new Dictionary<string, object>
                    {
                        { "id", "o" + optionNumber.ToString(CultureInfo.InvariantCulture) },
                        { "label", MessageRules.Cut(label, 100) }
                    };
                    string description = Json.String(option, "description");
                    if (!string.IsNullOrWhiteSpace(description)) optionEntry["description"] = MessageRules.Cut(description, 300);
                    optionList.Add(optionEntry);
                }
                entry["options"] = optionList;
                list.Add(entry);
            }
            return new Dictionary<string, object> { { "questions", list } };
        }

        private static string ChoiceSummary(Dictionary<string, object> message)
        {
            var first = (Dictionary<string, object>)((IList)message["questions"])[0];
            return MessageRules.Cut((string)first["question"], OutcomeSummaryLimit);
        }

        private static string PermissionOutput(Dictionary<string, object> inner)
        {
            string decision = Json.String(inner, "decision");
            var verdict = new Dictionary<string, object>();
            if (decision == "allow")
            {
                verdict["behavior"] = "allow";
            }
            else if (decision == "deny")
            {
                string reason = Json.String(inner, "reason");
                verdict["behavior"] = "deny";
                verdict["message"] = string.IsNullOrEmpty(reason) ? DeniedFromPhone : reason;
            }
            else
            {
                return null;
            }
            return Json.Write(new Dictionary<string, object>
            {
                { "hookSpecificOutput", new Dictionary<string, object> { { "hookEventName", "PermissionRequest" }, { "decision", verdict } } }
            });
        }

        private static string ChoiceOutput(PendingRequest request, Dictionary<string, object> response)
        {
            string problem = ChoiceAnswers.Validate(request.Message, response);
            if (problem != null)
            {
                AppLog.Append("choice response for " + request.Id + " rejected: " + problem);
                return null;
            }
            var input = request.ToolInput as Dictionary<string, object>;
            if (input == null) return null;
            Dictionary<string, object> updated = ChoiceAnswers.UpdatedInput(input, request.Message, response);
            var verdict = new Dictionary<string, object> { { "behavior", "allow" }, { "updatedInput", updated } };
            return Json.Write(new Dictionary<string, object>
            {
                { "hookSpecificOutput", new Dictionary<string, object> { { "hookEventName", "PermissionRequest" }, { "decision", verdict } } }
            });
        }

        private static string TextOutput(string text)
        {
            if (string.IsNullOrEmpty(text) || MessageRules.IsBlank(text)) return null;
            return Json.Write(new Dictionary<string, object> { { "decision", "block" }, { "reason", text } });
        }

        private static Dictionary<string, object> ResolvedMessage(string id, string outcome)
        {
            return new Dictionary<string, object> { { "type", "resolved" }, { "request_id", id }, { "outcome", outcome } };
        }

        private static Dictionary<string, object> ModeMessage(ControlMode value)
        {
            return new Dictionary<string, object> { { "type", "mode" }, { "mode", ModeName(value) } };
        }

        private static string ModeName(ControlMode value)
        {
            return value == ControlMode.Pc ? "pc" : "phone";
        }

        private static string Describe(PendingRequest request)
        {
            return (request.Kind == RequestKind.Permission ? "permission " + request.ToolName : request.Kind == RequestKind.Choice ? "question" : "text window") + " in " + Short(request.SessionId);
        }

        private static string NewRequestId()
        {
            return "r-" + Guid.NewGuid().ToString("N").Substring(0, 12);
        }

        private static string Short(string id)
        {
            return string.IsNullOrEmpty(id) ? "-" : id.Length > 8 ? id.Substring(0, 8) : id;
        }
    }
}
