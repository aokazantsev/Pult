using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace Pult
{
    internal sealed class Broker : IRelayListener
    {
        public static readonly TimeSpan HookMargin = TimeSpan.FromSeconds(5);
        private const int ClosedOutcomesLimit = 500;
        private const int SummaryLimit = 300;
        private const int InputValueLimit = 1000;
        private const int LastMessageLimit = 4000;
        private const string DeniedFromPhone = "Запрещено с телефона";

        private readonly object gate = new object();
        private readonly IRelay relay;
        private readonly TimeSpan wait;
        private readonly int pollMs;
        private readonly List<PendingRequest> pending = new List<PendingRequest>();
        private readonly Dictionary<string, string> closedOutcomes = new Dictionary<string, string>();
        private readonly Queue<string> closedOrder = new Queue<string>();
        private readonly Dictionary<string, int> stopStreaks = new Dictionary<string, int>();
        private readonly Dictionary<string, string> titles = new Dictionary<string, string>();
        private ControlMode mode = ControlMode.Phone;
        private bool stopped;
        private long lastHookTicks = DateTime.UtcNow.Ticks;

        public Broker(IRelay relay, TimeSpan wait, int pollMs)
        {
            this.relay = relay;
            this.wait = wait;
            this.pollMs = pollMs;
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

        public void SetMode(ControlMode value)
        {
            lock (gate)
            {
                if (mode == value) return;
                mode = value;
                AppLog.Append("mode: " + ModeName(value));
                if (value == ControlMode.Pc)
                {
                    foreach (PendingRequest request in pending) Release(request, null, Outcome.ResolvedOnPc, "mode pc");
                }
            }
            relay.SendLive(ModeMessage(value));
        }

        public void Stop()
        {
            lock (gate)
            {
                stopped = true;
                foreach (PendingRequest request in pending) Release(request, null, Outcome.Expired, "desktop exit");
            }
        }

        public void Handle(HookEvent hookEvent, TimeSpan hookLimit, IHookChannel channel)
        {
            Remember(hookEvent);
            ResetIdle();
            switch (hookEvent.Name)
            {
                case "PermissionRequest":
                    HandlePermission(hookEvent, hookLimit, channel);
                    return;
                case "Stop":
                    HandleStop(hookEvent, hookLimit, channel);
                    return;
                case "PostToolUse":
                case "PostToolUseFailure":
                    channel.Deliver(null);
                    ResolveByToolUse(hookEvent);
                    relay.SendLive(StatusMessage(hookEvent.SessionId, "working"));
                    return;
                case "StopFailure":
                    channel.Deliver(null);
                    AppLog.Append("thread " + Short(hookEvent.SessionId) + " stopped with error " + Json.String(hookEvent.Data, "error"));
                    relay.SendLive(StatusMessage(hookEvent.SessionId, "error"));
                    return;
                case "SessionStart":
                    channel.Deliver(null);
                    relay.SendLive(StatusMessage(hookEvent.SessionId, "working"));
                    return;
                default:
                    channel.Deliver(null);
                    return;
            }
        }

        public void OnRelayConnected()
        {
            relay.SendLive(ModeMessage(Mode));
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
                    ResendOpen();
                    return;
                case "permission_response":
                    Answer(requestId, RequestKind.Permission, PermissionOutput(inner));
                    return;
                case "text_response":
                    Answer(requestId, RequestKind.Text, TextOutput(Json.String(inner, "text")));
                    return;
                default:
                    if (requestId != null) Answer(requestId, null, null);
                    return;
            }
        }

        private void HandlePermission(HookEvent hookEvent, TimeSpan hookLimit, IHookChannel channel)
        {
            string refusal = RouteRefusal();
            if (refusal != null)
            {
                AppLog.Append("permission " + hookEvent.ToolName + " in " + Short(hookEvent.SessionId) + " stays on pc: " + refusal);
                channel.Deliver(null);
                return;
            }
            string id = NewRequestId();
            var message = new Dictionary<string, object>
            {
                { "type", "permission_request" },
                { "request_id", id },
                { "thread", hookEvent.SessionId },
                { "title", TitleOf(hookEvent) },
                { "tool", hookEvent.ToolName },
                { "summary", Summary(hookEvent) },
                { "input", TrimmedInput(hookEvent.ToolInput) }
            };
            Wait(new PendingRequest(id, RequestKind.Permission, hookEvent, Deadline(hookLimit), message), channel);
        }

        private void HandleStop(HookEvent hookEvent, TimeSpan hookLimit, IHookChannel channel)
        {
            int streak;
            lock (gate)
            {
                stopStreaks.TryGetValue(hookEvent.SessionId, out streak);
                streak = hookEvent.StopHookActive ? streak + 1 : 0;
                stopStreaks[hookEvent.SessionId] = streak;
            }
            string refusal = RouteRefusal();
            if (refusal == null && !relay.PhoneStreamOpen) refusal = "phone stream closed";
            if (refusal == null && streak >= hookEvent.StopBlockCap) refusal = "continuation cap " + hookEvent.StopBlockCap + " reached";
            if (refusal != null)
            {
                AppLog.Append("text window for " + Short(hookEvent.SessionId) + " not opened: " + refusal);
                channel.Deliver(null);
                relay.SendLive(StatusMessage(hookEvent.SessionId, "done"));
                return;
            }
            string id = NewRequestId();
            var message = new Dictionary<string, object>
            {
                { "type", "text_request" },
                { "request_id", id },
                { "thread", hookEvent.SessionId },
                { "title", TitleOf(hookEvent) },
                { "last_message", Cut(hookEvent.LastAssistantMessage ?? "", LastMessageLimit) }
            };
            string outcome = Wait(new PendingRequest(id, RequestKind.Text, hookEvent, Deadline(hookLimit), message), channel);
            if (outcome != Outcome.Answered) relay.SendLive(StatusMessage(hookEvent.SessionId, "done"));
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

        private string Wait(PendingRequest request, IHookChannel channel)
        {
            lock (gate) pending.Add(request);
            AppLog.Append("request " + request.Id + " " + Describe(request) + ": sent, wait until " + request.DeadlineUtc.ToLocalTime().ToString("HH:mm:ss"));
            relay.Send(request.Message, request.DeadlineUtc, delegate
            {
                lock (gate)
                {
                    if (Release(request, null, null, "relay refused")) request.NotifyPhone = false;
                }
            });
            while (true)
            {
                TranscriptSignals signals = request.Transcript == null ? new TranscriptSignals() : TranscriptSignals.Scan(request.Transcript.ReadNew());
                bool gone = channel.IsGone;
                lock (gate)
                {
                    if (!request.Released)
                    {
                        if (request.Kind == RequestKind.Permission && signals.DeniedOnPc) Release(request, null, Outcome.ResolvedOnPc, "denied on pc");
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
                if (outcome != null) RememberOutcome(request.Id, outcome);
            }
            AppLog.Append("request " + request.Id + ": " + (outcome ?? "not delivered") + " (" + request.Cause + ")");
            if (request.NotifyPhone && outcome != null) relay.Send(ResolvedMessage(request.Id, outcome), DateTime.UtcNow.AddMinutes(5), delegate { });
            return outcome;
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

        private void Answer(string requestId, RequestKind? kind, string output)
        {
            string closed = null;
            lock (gate)
            {
                PendingRequest request = pending.FirstOrDefault(r => r.Id == requestId);
                if (request != null && !request.Released && kind.HasValue && request.Kind == kind.Value && output != null)
                {
                    Release(request, output, Outcome.Answered, "phone answer");
                    return;
                }
                if (request != null) return;
                if (requestId == null || !closedOutcomes.TryGetValue(requestId, out closed)) closed = Outcome.Expired;
            }
            AppLog.Append("phone answer for closed request " + requestId + ": " + closed);
            if (requestId != null) relay.Send(ResolvedMessage(requestId, closed), DateTime.UtcNow.AddMinutes(5), delegate { });
        }

        private void ResolveByToolUse(HookEvent hookEvent)
        {
            string key = hookEvent.ToolInputKey;
            lock (gate)
            {
                PendingRequest request = pending.FirstOrDefault(r => !r.Released && r.Kind == RequestKind.Permission
                    && r.SessionId == hookEvent.SessionId && r.ToolName == hookEvent.ToolName && r.ToolInputKey == key);
                if (request != null) Release(request, null, Outcome.ResolvedOnPc, "allowed on pc");
            }
        }

        private void ResendOpen()
        {
            List<PendingRequest> open;
            lock (gate) open = pending.Where(r => !r.Released).ToList();
            relay.SendLive(ModeMessage(Mode));
            foreach (PendingRequest request in open) relay.Send(request.Message, request.DeadlineUtc, delegate { });
        }

        private void Remember(HookEvent hookEvent)
        {
            if (hookEvent.SessionId.Length == 0) return;
            lock (gate)
            {
                string title = hookEvent.SessionTitle;
                if (!string.IsNullOrEmpty(title)) titles[hookEvent.SessionId] = title;
                if (hookEvent.Name == "SessionStart") AppLog.Append("thread " + Short(hookEvent.SessionId) + " " + (hookEvent.Source ?? "started"));
            }
        }

        private void RememberOutcome(string id, string outcome)
        {
            closedOutcomes[id] = outcome;
            closedOrder.Enqueue(id);
            while (closedOrder.Count > ClosedOutcomesLimit) closedOutcomes.Remove(closedOrder.Dequeue());
        }

        private DateTime Deadline(TimeSpan hookLimit)
        {
            TimeSpan limit = hookLimit - HookMargin;
            TimeSpan span = limit < wait ? limit : wait;
            if (span < TimeSpan.Zero) span = TimeSpan.Zero;
            return DateTime.UtcNow + span;
        }

        private string TitleOf(HookEvent hookEvent)
        {
            lock (gate)
            {
                string title;
                if (titles.TryGetValue(hookEvent.SessionId, out title)) return title;
            }
            string cwd = hookEvent.Cwd;
            if (string.IsNullOrEmpty(cwd)) return Short(hookEvent.SessionId);
            try
            {
                return Path.GetFileName(cwd.TrimEnd('\\', '/'));
            }
            catch (ArgumentException)
            {
                return cwd;
            }
        }

        private static string Summary(HookEvent hookEvent)
        {
            var input = hookEvent.ToolInput as Dictionary<string, object>;
            foreach (string key in new[] { "command", "file_path", "path", "url", "pattern", "description", "prompt" })
            {
                string value = Json.String(input, key);
                if (!string.IsNullOrEmpty(value)) return Cut(value, SummaryLimit);
            }
            return hookEvent.ToolName;
        }

        private static object TrimmedInput(object input)
        {
            var map = input as Dictionary<string, object>;
            if (map == null) return input;
            var trimmed = new Dictionary<string, object>();
            foreach (KeyValuePair<string, object> pair in map)
            {
                var text = pair.Value as string;
                if (text != null) trimmed[pair.Key] = Cut(text, InputValueLimit);
                else if (pair.Value is Dictionary<string, object> || pair.Value is object[]) trimmed[pair.Key] = Cut(Json.Write(pair.Value), InputValueLimit);
                else trimmed[pair.Key] = pair.Value;
            }
            return trimmed;
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

        private static string TextOutput(string text)
        {
            if (string.IsNullOrEmpty(text) || text.Trim().Length == 0) return null;
            return Json.Write(new Dictionary<string, object> { { "decision", "block" }, { "reason", text } });
        }

        private static Dictionary<string, object> ResolvedMessage(string id, string outcome)
        {
            return new Dictionary<string, object> { { "type", "resolved" }, { "request_id", id }, { "outcome", outcome } };
        }

        private static Dictionary<string, object> StatusMessage(string thread, string status)
        {
            return new Dictionary<string, object> { { "type", "status" }, { "thread", thread }, { "status", status } };
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
            return (request.Kind == RequestKind.Permission ? "permission " + request.ToolName : "text window") + " in " + Short(request.SessionId);
        }

        private static string NewRequestId()
        {
            return "r-" + Guid.NewGuid().ToString("N").Substring(0, 12);
        }

        private static string Short(string id)
        {
            return string.IsNullOrEmpty(id) ? "-" : id.Length > 8 ? id.Substring(0, 8) : id;
        }

        private static string Cut(string text, int limit)
        {
            return text.Length <= limit ? text : text.Substring(0, limit) + "…";
        }
    }
}
