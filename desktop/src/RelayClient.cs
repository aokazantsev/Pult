using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;

namespace Pult
{
    internal sealed class RelayClient : IRelay, IDisposable
    {
        public const string IntrusionText = "От сервера: кто-то пытался войти в пару Pult";
        private static readonly TimeSpan Liveness = TimeSpan.FromSeconds(45);
        private static readonly TimeSpan PingInterval = TimeSpan.FromSeconds(15);
        private static readonly TimeSpan NoticeRepeat = TimeSpan.FromMinutes(1);
        private const int HelloTtlSeconds = 120;
        private const int StreamReadTimeoutMs = 60000;
        private const int ConnectTimeoutMs = 15000;
        private const int PostTimeoutMs = 10000;
        private const int ProcessedLimit = 2000;
        private const int ReplayRetries = 3;
        private static readonly int[] ReconnectDelaysMs = { 1000, 2000, 5000 };
        private const int IncompatibleDelayMs = 60000;
        private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false);

        private readonly RelaySession session;
        private readonly ISessionListener sessionListener;
        private readonly string pairUrl;
        private readonly object queueGate = new object();
        private readonly List<OutgoingMessage> queue = new List<OutgoingMessage>();
        private readonly AutoResetEvent queueSignal = new AutoResetEvent(false);
        private readonly ProcessedIds processed;
        private readonly HashSet<string> droppedIds = new HashSet<string>();
        private readonly Thread streamThread;
        private readonly Thread senderThread;
        private IRelayListener listener;
        private HttpWebRequest streamRequest;
        private volatile string streamId;
        private long counter;
        private volatile bool stopped;
        private volatile bool ended;
        private int endNotified;
        private volatile bool connected;
        private volatile bool phoneOpen;
        private volatile bool phoneCompatible = true;
        private volatile string serverProblem;
        private long disconnectedAtTicks = DateTime.UtcNow.Ticks;
        private DateTime lastPost = DateTime.MinValue;
        private string lastProblem = "not connected yet";
        private string lastNotice;
        private DateTime lastNoticeAt = DateTime.MinValue;

        public RelayClient(RelaySession session, ISessionListener sessionListener)
            : this(session, sessionListener, ProcessedIdsPath(session.Pair))
        {
        }

        public RelayClient(RelaySession session, ISessionListener sessionListener, string processedIdsPath)
        {
            this.session = session;
            this.sessionListener = sessionListener;
            processed = new ProcessedIds(processedIdsPath, ProcessedLimit);
            pairUrl = session.Server + "/v2/pairs/" + session.Pair + "/";
            streamThread = new Thread(StreamLoop) { IsBackground = true, Name = "relay stream" };
            senderThread = new Thread(SendLoop) { IsBackground = true, Name = "relay sender" };
        }

        public static string ProcessedIdsPath(string pair)
        {
            return UserDataPaths.File("processed-" + pair + ".txt");
        }

        public RelaySession Session
        {
            get { return session; }
        }

        public bool Configured
        {
            get { return session.IsActive && !ended; }
        }

        public bool Connected
        {
            get { return connected; }
        }

        public bool Ended
        {
            get { return ended; }
        }

        public string ServerProblem
        {
            get { return serverProblem; }
        }

        public bool PhoneCompatible
        {
            get { return phoneCompatible; }
        }

        public bool PhoneStreamOpen
        {
            get
            {
                if (!phoneOpen || !session.IsActive) return false;
                if (connected) return true;
                return DateTime.UtcNow - new DateTime(Interlocked.Read(ref disconnectedAtTicks), DateTimeKind.Utc) < Liveness;
            }
        }

        public void Start(IRelayListener relayListener)
        {
            listener = relayListener;
            AppLog.Append("relay: " + session.Server + " pair " + session.Pair + (session.IsActive ? "" : " (waiting for the phone)"));
            streamThread.Start();
            senderThread.Start();
        }

        public void Dispose()
        {
            stopped = true;
            queueSignal.Set();
            AbortStream();
        }

        public void Send(Dictionary<string, object> inner, DateTime expiresAtUtc, Action rejected)
        {
            if (ended)
            {
                if (rejected != null) rejected();
                return;
            }
            Enqueue(OutgoingMessage.Queued(inner, expiresAtUtc, rejected), false);
        }

        public void SendLive(Dictionary<string, object> inner)
        {
            if (!connected || !session.IsActive) return;
            Enqueue(OutgoingMessage.Live(inner), false);
        }

        public bool DeletePair(string reason, TimeSpan patience)
        {
            if (ended) return false;
            Interlocked.Exchange(ref endNotified, 1);
            OutgoingMessage request = OutgoingMessage.Delete(reason, patience);
            Enqueue(request, true);
            bool done = request.Done.WaitOne(patience + TimeSpan.FromSeconds(2));
            AppLog.Append("relay: delete " + session.Pair + " (" + reason + ") " + (done && ended ? "accepted" : "not confirmed by the server"));
            return done && ended;
        }

        private void Enqueue(OutgoingMessage message, bool first)
        {
            lock (queueGate)
            {
                if (first) queue.Insert(0, message);
                else queue.Add(message);
            }
            queueSignal.Set();
        }

        private void StreamLoop()
        {
            int failures = 0;
            while (!stopped && !ended)
            {
                bool opened = false;
                int delay = 0;
                try
                {
                    bool reachable;
                    string incompatible = ServerInfo.Check(session.Server, ConnectTimeoutMs, out reachable);
                    if (incompatible != null && reachable)
                    {
                        serverProblem = incompatible;
                        Problem(incompatible);
                        Notice(incompatible);
                        delay = IncompatibleDelayMs;
                    }
                    else if (incompatible != null)
                    {
                        Problem(incompatible);
                    }
                    else
                    {
                        serverProblem = null;
                        opened = OpenStream();
                    }
                }
                catch (WebException error)
                {
                    Problem(Describe(error));
                }
                catch (IOException error)
                {
                    Problem("stream broken: " + error.Message);
                }
                catch (Exception error)
                {
                    Problem("stream failed: " + error.GetType().Name + ": " + error.Message);
                }
                finally
                {
                    streamRequest = null;
                    streamId = null;
                    if (connected) Interlocked.Exchange(ref disconnectedAtTicks, DateTime.UtcNow.Ticks);
                    connected = false;
                    if (listener != null) listener.OnPhoneStreamChanged();
                }
                if (stopped || ended) break;
                if (opened) failures = 0;
                else failures++;
                Thread.Sleep(delay > 0 ? delay : ReconnectDelaysMs[Math.Min(failures, ReconnectDelaysMs.Length - 1)]);
            }
        }

        private bool OpenStream()
        {
            RelayResponse challengeResponse = RelayHttp.PostJson(pairUrl + "challenge", "{\"from\":\"pc\"}", PostTimeoutMs);
            if (EndsPair(challengeResponse)) return false;
            string challenge = Json.String(challengeResponse.Json, "challenge");
            if (challengeResponse.Status != 200 || !StrictJson.IsIdentifier(challenge))
            {
                Problem("challenge: " + challengeResponse.Describe());
                return false;
            }
            string signature = Base64Url.Encode(session.Keys.SignData(Utf8.GetBytes(SignedStrings.Login(session.Pair, "pc", challenge))));
            var headers = new Dictionary<string, string> { { "Pult-From", "pc" }, { "Pult-Challenge", challenge }, { "Pult-Sig", signature } };
            HttpWebRequest request = RelayHttp.Create(pairUrl + "stream", "GET", headers, ConnectTimeoutMs);
            request.Accept = "text/event-stream";
            request.ReadWriteTimeout = StreamReadTimeoutMs;
            request.AllowReadStreamBuffering = false;
            streamRequest = request;
            if (stopped) return false;
            try
            {
                using (var response = (HttpWebResponse)request.GetResponse())
                using (var reader = new StreamReader(response.GetResponseStream(), Utf8))
                {
                    ReadEvents(reader);
                }
            }
            catch (WebException error)
            {
                var response = error.Response as HttpWebResponse;
                if (response == null) throw;
                using (response)
                {
                    var answer = new RelayResponse((int)response.StatusCode, RelayHttp.ReadBody(response), null);
                    if (!EndsPair(answer)) Problem("stream: " + answer.Describe());
                }
                return false;
            }
            bool hadSession = streamId != null;
            Problem("stream ended");
            return hadSession;
        }

        private bool EndsPair(RelayResponse response)
        {
            if (response.Status == 410)
            {
                Dictionary<string, object> body = response.Json;
                End(Json.String(body, "reason") ?? "unknown", Json.String(body, "by") ?? "server");
                return true;
            }
            if (response.Status == 404)
            {
                End("not_found", "server");
                return true;
            }
            return false;
        }

        private void ReadEvents(StreamReader reader)
        {
            string eventName = null;
            var data = new StringBuilder();
            while (!stopped && !ended)
            {
                string line = reader.ReadLine();
                if (line == null) return;
                if (line.Length == 0)
                {
                    if (data.Length > 0) Dispatch(eventName ?? "message", data.ToString());
                    eventName = null;
                    data.Length = 0;
                    continue;
                }
                if (line.StartsWith(":", StringComparison.Ordinal)) continue;
                int colon = line.IndexOf(':');
                string field = colon < 0 ? line : line.Substring(0, colon);
                string value = colon < 0 ? "" : line.Substring(colon + 1);
                if (value.StartsWith(" ", StringComparison.Ordinal)) value = value.Substring(1);
                if (field == "event") eventName = value;
                else if (field == "data")
                {
                    if (data.Length > 0) data.Append('\n');
                    data.Append(value);
                }
            }
        }

        private void Dispatch(string eventName, string data)
        {
            Dictionary<string, object> payload = StrictJson.TryParseObject(data);
            if (payload == null)
            {
                AppLog.Append("relay: unreadable " + eventName + " event");
                return;
            }
            if (eventName == "server")
            {
                HandleSignal(payload);
                return;
            }
            if (eventName != "message" || streamId == null) return;
            if (!session.IsActive) return;
            HandleEnvelope(payload);
        }

        private void HandleSignal(Dictionary<string, object> payload)
        {
            string type = Json.String(payload, "type");
            if (type == "session")
            {
                string id = Json.String(payload, "stream");
                if (!StrictJson.IsIdentifier(id))
                {
                    AppLog.Append("relay: session signal without a valid stream id");
                    return;
                }
                Interlocked.Exchange(ref counter, 0);
                streamId = id;
                connected = true;
                Problem(null);
                if (session.IsActive) Greet();
                queueSignal.Set();
                return;
            }
            if (streamId == null) return;
            switch (type)
            {
                case "pair":
                    HandlePairSignal(payload);
                    return;
                case "phone_stream":
                    if (!session.IsActive) return;
                    bool open = Json.String(payload, "state") == "open";
                    if (open != phoneOpen) AppLog.Append("relay: phone stream " + (open ? "open" : "closed"));
                    phoneOpen = open;
                    if (listener != null) listener.OnPhoneStreamChanged();
                    return;
                case "phone_incompatible":
                    phoneCompatible = false;
                    Notice("Телефон несовместим: сервер отверг его конверт версии " + Convert.ToString(Json.Value(payload, "v"), CultureInfo.InvariantCulture) + " — обновите приложение на телефоне или Pult на ПК");
                    if (listener != null) listener.OnPhoneStreamChanged();
                    return;
                case "intrusion":
                    Notice(IntrusionText);
                    return;
                case "pair_deleted":
                    End(Json.String(payload, "reason") ?? "unknown", Json.String(payload, "by") ?? "server");
                    return;
            }
        }

        private void HandlePairSignal(Dictionary<string, object> payload)
        {
            string phoneSign = Json.String(payload, "phone_sign");
            string phoneAgree = Json.String(payload, "phone_agree");
            string tag = Json.String(payload, "tag");
            if (session.IsActive)
            {
                if (!session.SamePhone(phoneSign, phoneAgree)) Notice(IntrusionText);
                return;
            }
            if (!session.AwaitsPhone)
            {
                AppLog.Append("relay: phone keys for a claim without its secret, claim deleted");
                SendDeleteNow("user");
                return;
            }
            if (!session.Verify(phoneSign, phoneAgree, tag))
            {
                AppLog.Append("relay: pairing " + session.Pair + " rejected: tag does not match");
                SendDeleteNow("pairing_rejected");
                if (sessionListener != null) sessionListener.OnPairingRejected(this);
                return;
            }
            AppLog.Append("relay: paired " + session.Pair + ", fingerprint " + session.Fingerprint);
            if (sessionListener != null) sessionListener.OnPaired(this);
            Greet();
            queueSignal.Set();
        }

        private void Greet()
        {
            var hello = new Dictionary<string, object>
            {
                { "type", "hello" },
                { "protocol", new List<object> { (long)Envelope.Version } },
                { "capabilities", new List<object>() }
            };
            bool queued;
            lock (queueGate) queued = queue.Exists(m => m.Inner != null && Json.String(m.Inner, "type") == "hello");
            if (!queued) Enqueue(OutgoingMessage.Queued(hello, DateTime.UtcNow.AddSeconds(HelloTtlSeconds), null), true);
            if (listener != null) listener.OnRelayConnected();
        }

        private void HandleEnvelope(Dictionary<string, object> payload)
        {
            OpenedEnvelope opened = Envelope.Open(session.Directions, session.Pair, "pc", payload);
            if (!opened.Accepted)
            {
                string key = opened.Id ?? Json.String(payload, "id") ?? "?";
                bool first;
                lock (droppedIds) first = droppedIds.Count < 1000 && droppedIds.Add(key);
                if (first) AppLog.Append("relay: envelope " + key + " dropped: " + opened.Problem);
                if (opened.IsUnsupportedVersion)
                {
                    phoneCompatible = false;
                    Notice(ServerInfo.Incompatibility("Телефон", new object[] { opened.UnsupportedVersion }));
                }
                return;
            }
            if (processed.Add(opened.Id))
            {
                Dictionary<string, object> inner = opened.Inner;
                string type = Json.String(inner, "type");
                AppLog.Append("relay: phone " + type + " " + (Json.String(inner, "request_id") ?? ""));
                if (type == "hello") CheckPhoneVersions(inner);
                if (listener != null) listener.OnPhoneMessage(inner);
            }
            Enqueue(OutgoingMessage.Ack(new List<string> { opened.Id }), false);
        }

        private void CheckPhoneVersions(Dictionary<string, object> hello)
        {
            object[] versions = Json.Value(hello, "protocol") as object[];
            string problem = versions == null ? "Телефон не сообщил версии протокола" : ServerInfo.Incompatibility("Телефон", versions);
            phoneCompatible = problem == null;
            if (problem != null) Notice(problem);
        }

        private void End(string reason, string by)
        {
            ended = true;
            queueSignal.Set();
            AbortStream();
            if (Interlocked.Exchange(ref endNotified, 1) != 0) return;
            AppLog.Append("relay: pair " + session.Pair + " ended: " + reason + " by " + by);
            if (sessionListener != null) sessionListener.OnPairEnded(this, reason, by);
        }

        private void SendDeleteNow(string reason)
        {
            Interlocked.Exchange(ref endNotified, 1);
            RelayResponse response = SignedPost("delete", Utf8.GetBytes("{\"reason\":\"" + reason + "\"}"));
            AppLog.Append("relay: delete " + session.Pair + " (" + reason + "): " + response.Describe());
            ended = true;
            queueSignal.Set();
            AbortStream();
        }

        private void SendLoop()
        {
            while (!stopped)
            {
                queueSignal.WaitOne(1000);
                if (stopped) break;
                DropStale();
                if (ended)
                {
                    FinishDeletes();
                    continue;
                }
                if (!connected || streamId == null) continue;
                if (DateTime.UtcNow - lastPost >= PingInterval)
                {
                    RelayResponse ping = SignedPost("ping", new byte[0]);
                    if (ping.Status == 401) Reconnect("ping: " + ping.Describe());
                    else EndsPair(ping);
                }
                DeliverQueue();
            }
            FinishDeletes();
        }

        private void DeliverQueue()
        {
            int replays = 0;
            while (connected && !stopped && !ended)
            {
                OutgoingMessage next = null;
                lock (queueGate)
                {
                    foreach (OutgoingMessage message in queue)
                    {
                        if (session.IsActive || message.DeleteReason != null)
                        {
                            next = message;
                            break;
                        }
                    }
                }
                if (next == null) return;
                RelayResponse response = Deliver(next);
                if (response == null)
                {
                    Remove(next);
                    continue;
                }
                if (response.Status < 0)
                {
                    Problem("post " + next.Operation + ": " + response.Describe());
                    return;
                }
                if (response.Status == 401)
                {
                    Reconnect(next.Operation + ": " + response.Describe());
                    return;
                }
                if (response.Status == 409 && response.Error == "replay" && replays++ < ReplayRetries) continue;
                if (next.DeleteReason == null && EndsPair(response)) return;
                Remove(next);
                if (next.DeleteReason != null)
                {
                    if (response.Status == 200 || response.Status == 404 || response.Status == 410)
                    {
                        Interlocked.Exchange(ref endNotified, 1);
                        ended = true;
                        AbortStream();
                    }
                    next.Done.Set();
                    continue;
                }
                if (response.Status >= 400)
                {
                    AppLog.Append("relay: " + Kind(next) + " refused: " + response.Describe());
                    if (next.Rejected != null) next.Rejected();
                }
            }
        }

        private RelayResponse Deliver(OutgoingMessage message)
        {
            if (message.AckIds != null) return SignedPost("acks", Utf8.GetBytes(Json.Write(new Dictionary<string, object> { { "ids", message.AckIds } })));
            if (message.DeleteReason != null) return SignedPost("delete", Utf8.GetBytes("{\"reason\":\"" + message.DeleteReason + "\"}"));
            long ttl = message.IsLive ? 0 : (long)Math.Ceiling((message.ExpiresAtUtc - DateTime.UtcNow).TotalSeconds);
            if (!message.IsLive && ttl < 1) return null;
            long ts = (long)(DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds;
            string body = Envelope.Seal(session.Directions, session.Pair, "pc", message.EnvelopeId, ts, ttl, Json.Write(message.Inner));
            return SignedPost("messages", Utf8.GetBytes(body));
        }

        private RelayResponse SignedPost(string op, byte[] body)
        {
            string stream = streamId;
            if (stream == null) return new RelayResponse(-1, null, "no stream");
            long n = Interlocked.Increment(ref counter);
            string signature = Base64Url.Encode(session.Keys.SignData(Utf8.GetBytes(SignedStrings.Post(op, session.Pair, "pc", stream, n, body))));
            var headers = new Dictionary<string, string>
            {
                { "Pult-From", "pc" },
                { "Pult-Stream", stream },
                { "Pult-N", n.ToString(CultureInfo.InvariantCulture) },
                { "Pult-Sig", signature }
            };
            RelayResponse response = RelayHttp.Send(pairUrl + op, "POST", headers, body, PostTimeoutMs);
            if (response.Status > 0) lastPost = DateTime.UtcNow;
            return response;
        }

        private void Reconnect(string why)
        {
            Problem(why + ", logging in again");
            connected = false;
            AbortStream();
        }

        private void AbortStream()
        {
            HttpWebRequest request = streamRequest;
            if (request != null) request.Abort();
        }

        private void Remove(OutgoingMessage message)
        {
            lock (queueGate) queue.Remove(message);
        }

        private void DropStale()
        {
            var dropped = new List<OutgoingMessage>();
            lock (queueGate)
            {
                DateTime now = DateTime.UtcNow;
                foreach (OutgoingMessage message in queue)
                {
                    if (message.ExpiresAtUtc <= now || (message.IsLive && !connected)) dropped.Add(message);
                }
                foreach (OutgoingMessage message in dropped) queue.Remove(message);
            }
            foreach (OutgoingMessage message in dropped)
            {
                if (message.Done != null) message.Done.Set();
                if (!message.IsLive) AppLog.Append("relay: " + Kind(message) + " expired before sending");
            }
        }

        private void FinishDeletes()
        {
            lock (queueGate)
            {
                foreach (OutgoingMessage message in queue)
                {
                    if (message.Done != null) message.Done.Set();
                }
            }
        }

        private void Notice(string text)
        {
            lock (queueGate)
            {
                if (text == lastNotice && DateTime.UtcNow - lastNoticeAt < NoticeRepeat) return;
                lastNotice = text;
                lastNoticeAt = DateTime.UtcNow;
            }
            AppLog.Append("relay notice: " + text);
            if (sessionListener != null) sessionListener.OnNotice(this, text);
        }

        private void Problem(string problem)
        {
            if (problem == lastProblem) return;
            lastProblem = problem;
            AppLog.Append(problem == null ? "relay: connected" : "relay: " + problem);
        }

        private static string Kind(OutgoingMessage message)
        {
            if (message.AckIds != null) return "ack";
            if (message.DeleteReason != null) return "delete";
            return Json.String(message.Inner, "type") + " " + (Json.String(message.Inner, "request_id") ?? "");
        }

        private static string Describe(WebException error)
        {
            var response = error.Response as HttpWebResponse;
            if (response != null) return "http " + ((int)response.StatusCode).ToString(CultureInfo.InvariantCulture);
            return error.Status.ToString();
        }
    }
}
