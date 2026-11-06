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
        public const int ProtocolVersion = 1;
        private static readonly TimeSpan Liveness = TimeSpan.FromSeconds(45);
        private static readonly TimeSpan PingInterval = TimeSpan.FromSeconds(15);
        private const int StreamReadTimeoutMs = 60000;
        private const int ConnectTimeoutMs = 15000;
        private const int PostTimeoutMs = 10000;
        private const int ProcessedLimit = 2000;
        private static readonly int[] ReconnectDelaysMs = { 1000, 2000, 5000 };

        private readonly string baseUrl;
        private readonly string pair;
        private readonly string token;
        private readonly object queueGate = new object();
        private readonly List<OutgoingMessage> queue = new List<OutgoingMessage>();
        private readonly AutoResetEvent queueSignal = new AutoResetEvent(false);
        private readonly HashSet<string> processed = new HashSet<string>();
        private readonly Queue<string> processedOrder = new Queue<string>();
        private readonly Thread streamThread;
        private readonly Thread senderThread;
        private IRelayListener listener;
        private HttpWebRequest streamRequest;
        private volatile bool stopped;
        private volatile bool connected;
        private volatile bool phoneOpen;
        private long disconnectedAtTicks = DateTime.UtcNow.Ticks;
        private DateTime lastPost = DateTime.MinValue;
        private string lastProblem = "not connected yet";

        public RelayClient(string server, string pair, string token)
        {
            baseUrl = server.TrimEnd('/') + "/v1/pairs/" + Uri.EscapeDataString(pair) + "/";
            this.pair = pair;
            this.token = token;
            streamThread = new Thread(StreamLoop) { IsBackground = true, Name = "relay stream" };
            senderThread = new Thread(SendLoop) { IsBackground = true, Name = "relay sender" };
        }

        public bool Configured
        {
            get { return true; }
        }

        public bool Connected
        {
            get { return connected; }
        }

        public bool PhoneStreamOpen
        {
            get
            {
                if (!phoneOpen) return false;
                if (connected) return true;
                return DateTime.UtcNow - new DateTime(Interlocked.Read(ref disconnectedAtTicks), DateTimeKind.Utc) < Liveness;
            }
        }

        public void Start(IRelayListener relayListener)
        {
            listener = relayListener;
            AppLog.Append("relay: " + baseUrl.Substring(0, baseUrl.Length - 1));
            streamThread.Start();
            senderThread.Start();
        }

        public void Dispose()
        {
            stopped = true;
            queueSignal.Set();
            HttpWebRequest request = streamRequest;
            if (request != null) request.Abort();
        }

        public void Send(Dictionary<string, object> inner, DateTime expiresAtUtc, Action rejected)
        {
            Enqueue(OutgoingMessage.Queued(inner, expiresAtUtc, rejected));
        }

        public void SendLive(Dictionary<string, object> inner)
        {
            if (!connected) return;
            Enqueue(OutgoingMessage.Live(inner));
        }

        private void Enqueue(OutgoingMessage message)
        {
            lock (queueGate) queue.Add(message);
            queueSignal.Set();
        }

        private void StreamLoop()
        {
            int failures = 0;
            while (!stopped)
            {
                bool opened = false;
                try
                {
                    var request = (HttpWebRequest)WebRequest.Create(baseUrl + "stream");
                    Prepare(request);
                    request.Method = "GET";
                    request.Accept = "text/event-stream";
                    request.Timeout = ConnectTimeoutMs;
                    request.ReadWriteTimeout = StreamReadTimeoutMs;
                    request.AllowReadStreamBuffering = false;
                    streamRequest = request;
                    using (var response = (HttpWebResponse)request.GetResponse())
                    using (var reader = new StreamReader(response.GetResponseStream(), new UTF8Encoding(false)))
                    {
                        opened = true;
                        failures = 0;
                        connected = true;
                        Problem(null);
                        queueSignal.Set();
                        listener.OnRelayConnected();
                        ReadEvents(reader);
                    }
                    Problem("stream ended");
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
                    if (connected) Interlocked.Exchange(ref disconnectedAtTicks, DateTime.UtcNow.Ticks);
                    connected = false;
                    listener.OnPhoneStreamChanged();
                }
                if (stopped) break;
                if (!opened) failures++;
                Thread.Sleep(ReconnectDelaysMs[Math.Min(failures, ReconnectDelaysMs.Length - 1)]);
            }
        }

        private void ReadEvents(StreamReader reader)
        {
            string eventName = null;
            var data = new StringBuilder();
            while (!stopped)
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
            Dictionary<string, object> payload = Json.TryParseObject(data);
            if (payload == null)
            {
                AppLog.Append("relay: unreadable " + eventName + " event");
                return;
            }
            if (eventName == "server")
            {
                if (Json.String(payload, "type") != "phone_stream") return;
                bool open = Json.String(payload, "state") == "open";
                if (open != phoneOpen) AppLog.Append("relay: phone stream " + (open ? "open" : "closed"));
                phoneOpen = open;
                listener.OnPhoneStreamChanged();
                return;
            }
            if (eventName != "message") return;
            string id = Json.String(payload, "id");
            if (string.IsNullOrEmpty(id) || Json.String(payload, "from") != "phone") return;
            if (processed.Add(id))
            {
                processedOrder.Enqueue(id);
                while (processedOrder.Count > ProcessedLimit) processed.Remove(processedOrder.Dequeue());
                Dictionary<string, object> inner = Json.Object(payload, "inner");
                if (inner != null)
                {
                    AppLog.Append("relay: phone " + Json.String(inner, "type") + " " + (Json.String(inner, "request_id") ?? ""));
                    listener.OnPhoneMessage(inner);
                }
                else
                {
                    AppLog.Append("relay: phone envelope without inner skipped");
                }
            }
            Enqueue(OutgoingMessage.Ack(new List<string> { id }));
        }

        private void SendLoop()
        {
            while (!stopped)
            {
                queueSignal.WaitOne(1000);
                if (stopped) return;
                DropStale();
                if (!connected) continue;
                if (DateTime.UtcNow - lastPost >= PingInterval) Post("ping", "{}");
                while (connected && !stopped)
                {
                    OutgoingMessage next;
                    lock (queueGate)
                    {
                        if (queue.Count == 0) break;
                        next = queue[0];
                    }
                    int status = Deliver(next);
                    if (status < 0) break;
                    lock (queueGate) queue.Remove(next);
                    if (status >= 400)
                    {
                        AppLog.Append("relay: " + Kind(next) + " refused with " + status);
                        if (next.Rejected != null) next.Rejected();
                    }
                }
            }
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
                if (!message.IsLive) AppLog.Append("relay: " + Kind(message) + " expired before sending");
            }
        }

        private int Deliver(OutgoingMessage message)
        {
            if (message.AckIds != null) return Post("acks", Json.Write(new Dictionary<string, object> { { "ids", message.AckIds } }));
            long ttl = message.IsLive ? 0 : (long)Math.Ceiling((message.ExpiresAtUtc - DateTime.UtcNow).TotalSeconds);
            if (!message.IsLive && ttl < 1) return 0;
            var envelope = new Dictionary<string, object>
            {
                { "v", ProtocolVersion },
                { "pair", pair },
                { "from", "pc" },
                { "id", "pc-" + Guid.NewGuid().ToString("N") },
                { "ts", (long)(DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds },
                { "ttl", ttl },
                { "inner", message.Inner }
            };
            return Post("messages", Json.Write(envelope));
        }

        private int Post(string path, string body)
        {
            try
            {
                var request = (HttpWebRequest)WebRequest.Create(baseUrl + path);
                Prepare(request);
                request.Method = "POST";
                request.ContentType = "application/json; charset=utf-8";
                request.Timeout = PostTimeoutMs;
                request.ReadWriteTimeout = PostTimeoutMs;
                byte[] bytes = new UTF8Encoding(false).GetBytes(body);
                request.ContentLength = bytes.Length;
                using (Stream stream = request.GetRequestStream()) stream.Write(bytes, 0, bytes.Length);
                using (var response = (HttpWebResponse)request.GetResponse())
                {
                    lastPost = DateTime.UtcNow;
                    return (int)response.StatusCode;
                }
            }
            catch (WebException error)
            {
                var response = error.Response as HttpWebResponse;
                if (response != null)
                {
                    using (response)
                    {
                        lastPost = DateTime.UtcNow;
                        return (int)response.StatusCode;
                    }
                }
                Problem("post " + path + ": " + Describe(error));
                return -1;
            }
            catch (IOException error)
            {
                Problem("post " + path + ": " + error.Message);
                return -1;
            }
        }

        private void Prepare(HttpWebRequest request)
        {
            request.Headers[HttpRequestHeader.Authorization] = "Bearer " + token;
            request.KeepAlive = true;
            if (request.RequestUri.IsLoopback) request.Proxy = null;
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
