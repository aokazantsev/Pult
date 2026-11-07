using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace Pult.Tests
{
    internal sealed class FakeRelayServer : IDisposable
    {
        private readonly TcpListener listener;
        private readonly string pair;
        private readonly byte[] pcSign;
        private readonly object gate = new object();
        private readonly List<string> queued = new List<string>();
        private readonly List<string> live = new List<string>();
        private readonly List<RecordedPost> posts = new List<RecordedPost>();
        private readonly Dictionary<string, Queue<KeyValuePair<int, string>>> planned = new Dictionary<string, Queue<KeyValuePair<int, string>>>();
        private volatile bool stopped;
        private int challenges;
        private int streams;
        private int badLogins;
        private string streamGone;

        public FakeRelayServer(int port, string pair, byte[] pcSign)
        {
            this.pair = pair;
            this.pcSign = pcSign;
            listener = new TcpListener(IPAddress.Loopback, port);
            listener.Start();
            new Thread(AcceptLoop) { IsBackground = true }.Start();
        }

        public int Challenges
        {
            get { return Thread.VolatileRead(ref challenges); }
        }

        public int Streams
        {
            get { return Thread.VolatileRead(ref streams); }
        }

        public int BadLogins
        {
            get { return Thread.VolatileRead(ref badLogins); }
        }

        public void Queue(string sseEvent)
        {
            lock (gate) queued.Add(sseEvent);
        }

        public void Push(string sseEvent)
        {
            lock (gate) live.Add(sseEvent);
        }

        public void Plan(string op, int status, string body)
        {
            lock (gate)
            {
                if (!planned.ContainsKey(op)) planned[op] = new Queue<KeyValuePair<int, string>>();
                planned[op].Enqueue(new KeyValuePair<int, string>(status, body));
            }
        }

        public void EndStreams(string goneBody)
        {
            streamGone = goneBody;
        }

        public List<RecordedPost> Posts(string op)
        {
            lock (gate) return posts.FindAll(p => p.Op == op);
        }

        public void Dispose()
        {
            stopped = true;
            listener.Stop();
        }

        private void AcceptLoop()
        {
            while (!stopped)
            {
                TcpClient client;
                try
                {
                    client = listener.AcceptTcpClient();
                }
                catch (SocketException)
                {
                    return;
                }
                catch (InvalidOperationException)
                {
                    return;
                }
                new Thread(() => Serve(client)) { IsBackground = true }.Start();
            }
        }

        private void Serve(TcpClient client)
        {
            try
            {
                using (client)
                {
                    NetworkStream stream = client.GetStream();
                    string head = ReadHead(stream);
                    if (head == null) return;
                    string[] lines = head.Split(new[] { "\r\n" }, StringSplitOptions.None);
                    string[] first = lines[0].Split(' ');
                    string method = first[0];
                    string path = first[1];
                    var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    foreach (string line in lines)
                    {
                        int colon = line.IndexOf(':');
                        if (colon > 0) headers[line.Substring(0, colon).Trim()] = line.Substring(colon + 1).Trim();
                    }
                    string prefix = "/v2/pairs/" + pair + "/";
                    if (method == "GET" && path == "/info")
                    {
                        Answer(stream, 200, "{\"name\":\"pult-relay\",\"protocol\":[2]}");
                        return;
                    }
                    byte[] body = ReadBody(stream, headers);
                    if (method == "POST" && path == prefix + "challenge")
                    {
                        int number = Interlocked.Increment(ref challenges);
                        Answer(stream, 200, "{\"challenge\":\"ch" + number + "\",\"ttl\":60}");
                        return;
                    }
                    if (method == "GET" && path == prefix + "stream")
                    {
                        ServeStream(client, stream, headers);
                        return;
                    }
                    if (method == "POST" && path.StartsWith(prefix, StringComparison.Ordinal))
                    {
                        string op = path.Substring(prefix.Length);
                        long n = long.Parse(Header(headers, "Pult-N") ?? "0");
                        string signed = SignedStrings.Post(op, pair, Header(headers, "Pult-From"), Header(headers, "Pult-Stream"), n, body);
                        bool valid = P256.Verify(pcSign, Encoding.UTF8.GetBytes(signed), Base64Url.Decode(Header(headers, "Pult-Sig")));
                        KeyValuePair<int, string> reply = new KeyValuePair<int, string>(op == "messages" ? 202 : 200, "{}");
                        lock (gate)
                        {
                            posts.Add(new RecordedPost(op, Header(headers, "Pult-Stream"), n, valid, Encoding.UTF8.GetString(body)));
                            Queue<KeyValuePair<int, string>> plan;
                            if (planned.TryGetValue(op, out plan) && plan.Count > 0) reply = plan.Dequeue();
                        }
                        Answer(stream, valid ? reply.Key : 401, valid ? reply.Value : "{\"error\":\"bad_signature\"}");
                        return;
                    }
                    Answer(stream, 404, "{\"error\":\"not_found\"}");
                }
            }
            catch (IOException)
            {
            }
            catch (SocketException)
            {
            }
            catch (ObjectDisposedException)
            {
            }
        }

        private void ServeStream(TcpClient client, NetworkStream stream, Dictionary<string, string> headers)
        {
            if (streamGone != null)
            {
                Answer(stream, 410, streamGone);
                return;
            }
            string challenge = Header(headers, "Pult-Challenge");
            string login = SignedStrings.Login(pair, Header(headers, "Pult-From"), challenge ?? "");
            if (challenge == null || !P256.Verify(pcSign, Encoding.UTF8.GetBytes(login), Base64Url.Decode(Header(headers, "Pult-Sig"))))
            {
                Interlocked.Increment(ref badLogins);
                Answer(stream, 401, "{\"error\":\"bad_signature\"}");
                return;
            }
            int number = Interlocked.Increment(ref streams);
            Write(stream, "HTTP/1.1 200 OK\r\nContent-Type: text/event-stream\r\nConnection: close\r\n\r\n");
            Write(stream, "event: server\ndata: {\"type\":\"session\",\"stream\":\"st" + number + "\"}\n\n");
            List<string> backlog;
            lock (gate) backlog = new List<string>(queued);
            foreach (string item in backlog) Write(stream, item);
            int sentLive;
            lock (gate) sentLive = live.Count;
            while (!stopped && client.Connected && Streams == number && streamGone == null)
            {
                Thread.Sleep(50);
                List<string> fresh = null;
                lock (gate)
                {
                    if (live.Count > sentLive)
                    {
                        fresh = live.GetRange(sentLive, live.Count - sentLive);
                        sentLive = live.Count;
                    }
                }
                if (fresh != null)
                {
                    foreach (string item in fresh) Write(stream, item);
                }
                else Write(stream, ": keepalive\n\n");
            }
        }

        private static string Header(Dictionary<string, string> headers, string name)
        {
            string value;
            return headers.TryGetValue(name, out value) ? value : null;
        }

        private static byte[] ReadBody(NetworkStream stream, Dictionary<string, string> headers)
        {
            int length = 0;
            string text = Header(headers, "Content-Length");
            if (text != null) length = int.Parse(text);
            var body = new byte[length];
            int total = 0;
            while (total < length)
            {
                int read = stream.Read(body, total, length - total);
                if (read <= 0) break;
                total += read;
            }
            return body;
        }

        private static void Answer(NetworkStream stream, int status, string body)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(body);
            Write(stream, "HTTP/1.1 " + status + " X\r\nContent-Type: application/json\r\nContent-Length: " + bytes.Length + "\r\nConnection: close\r\n\r\n" + body);
        }

        private static string ReadHead(NetworkStream stream)
        {
            var collected = new StringBuilder();
            var one = new byte[1];
            while (!collected.ToString().EndsWith("\r\n\r\n", StringComparison.Ordinal))
            {
                if (stream.Read(one, 0, 1) <= 0) return null;
                collected.Append((char)one[0]);
            }
            return collected.ToString();
        }

        private static void Write(NetworkStream stream, string text)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(text);
            stream.Write(bytes, 0, bytes.Length);
            stream.Flush();
        }
    }
}
