using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace Pult.Tests
{
    internal static class RelayTests
    {
        public static void ProcessedIdsSurviveRestart()
        {
            string path = TempFile();
            var first = new ProcessedIds(path, 10);
            Expect(first.Add("a"), "first add");
            Expect(first.Add("b"), "second add");
            Expect(!first.Add("a"), "duplicate in the same run");
            var second = new ProcessedIds(path, 10);
            Expect(!second.Add("a"), "a remembered after restart");
            Expect(!second.Add("b"), "b remembered after restart");
            Expect(second.Add("c"), "new id accepted");
        }

        public static void ProcessedIdsAreBounded()
        {
            string path = TempFile();
            var ids = new ProcessedIds(path, 3);
            for (int i = 0; i < 20; i++) Expect(ids.Add("id" + i), "add " + i);
            Expect(new FileInfo(path).Length < 20 * 6, "file compacted");
            var reloaded = new ProcessedIds(path, 3);
            Expect(!reloaded.Add("id19"), "recent id kept");
            Expect(!reloaded.Add("id17"), "third recent id kept");
            Expect(reloaded.Add("id0"), "old id forgotten");
        }

        public static void ProcessedIdsTolerateBrokenInput()
        {
            string path = TempFile();
            File.WriteAllText(path, "x\n\n  \ny\n");
            var ids = new ProcessedIds(path, 10);
            Expect(!ids.Add("x") && !ids.Add("y"), "ids from file");
            Expect(!ids.Add(""), "empty id refused");
            Expect(!ids.Add("a\nb"), "multiline id refused");
            Expect(new ProcessedIds(Path.Combine(Path.GetTempPath(), "no-such-dir-" + Guid.NewGuid().ToString("N"), "x.txt"), 10).Add("z"), "unwritable path still works in memory");
        }

        public static void CursorToleratesEmptyAndBadPaths()
        {
            Expect(TranscriptCursor.AtEnd(null) == null, "null");
            Expect(TranscriptCursor.AtEnd("") == null, "empty");
            TranscriptCursor blank = TranscriptCursor.AtEnd("   ");
            Expect(blank == null || blank.ReadNew().Count == 0, "blank");
            TranscriptCursor invalid = TranscriptCursor.AtEnd("C:\\bad|name\\x?.jsonl");
            Expect(invalid == null || invalid.ReadNew().Count == 0, "invalid characters");
            TranscriptCursor missing = TranscriptCursor.AtEnd(Path.Combine(Path.GetTempPath(), "missing-" + Guid.NewGuid().ToString("N") + ".jsonl"));
            Expect(missing != null && missing.ReadNew().Count == 0, "missing file");
        }

        public static void PendingRequestWithoutTranscript()
        {
            foreach (string transcript in new[] { null, "", "   " })
            {
                var data = new Dictionary<string, object>
                {
                    { "hook_event_name", "PermissionRequest" },
                    { "session_id", "s-1" },
                    { "tool_name", "Bash" },
                    { "tool_input", new Dictionary<string, object>() }
                };
                if (transcript != null) data["transcript_path"] = transcript;
                var request = new PendingRequest("r", RequestKind.Permission, new HookEvent(data, new Dictionary<string, object>()), DateTime.UtcNow.AddMinutes(1), new Dictionary<string, object>());
                Expect(request.Transcript == null || request.Transcript.ReadNew().Count == 0, "transcript " + (transcript ?? "absent"));
            }
        }

        public static void WaitsForServerAndDeliversQueuedMessage()
        {
            int port = FreePort();
            var listener = new RecordingListener();
            using (var client = new RelayClient("http://127.0.0.1:" + port, "p1", "tok", TempFile()))
            {
                client.Start(listener);
                client.Send(new Dictionary<string, object> { { "type", "permission_request" }, { "request_id", "r1" } }, DateTime.UtcNow.AddMinutes(2), delegate { });
                Thread.Sleep(2500);
                Expect(!client.Connected, "not connected while the server is down");
                Expect(listener.Connected == 0, "no connect callback yet");
                using (var server = new FakeServer(port, new string[0]))
                {
                    WaitFor(() => server.PostCount("/v1/pairs/p1/messages") > 0, 15, "queued message delivered after the server came back");
                    Expect(client.Connected, "connected");
                    Expect(listener.Connected > 0, "connect callback");
                    Expect(server.PostBody("/v1/pairs/p1/messages").Contains("\"request_id\":\"r1\""), "body carries the request");
                }
            }
        }

        public static void PhoneMessageIsNotRepeatedAfterRestart()
        {
            string idsPath = TempFile();
            string message = "event: message\ndata: {\"id\":\"ph-1\",\"from\":\"phone\",\"inner\":{\"type\":\"hello\"}}\n\n";
            var listener = new RecordingListener();
            int port = FreePort();
            using (var server = new FakeServer(port, new[] { message }))
            {
                using (var client = new RelayClient("http://127.0.0.1:" + port, "p1", "tok", idsPath))
                {
                    client.Start(listener);
                    WaitFor(() => listener.Messages == 1, 10, "first delivery");
                    WaitFor(() => server.PostCount("/v1/pairs/p1/acks") > 0, 10, "ack sent");
                }
                using (var client = new RelayClient("http://127.0.0.1:" + port, "p1", "tok", idsPath))
                {
                    client.Start(listener);
                    WaitFor(() => server.PostCount("/v1/pairs/p1/acks") > 1, 10, "ack for the repeated message");
                    Expect(listener.Messages == 1, "message not handled twice after restart");
                }
            }
        }

        private static string TempFile()
        {
            return Path.Combine(Path.GetTempPath(), "PultRelayTests-" + Guid.NewGuid().ToString("N") + ".txt");
        }

        private static int FreePort()
        {
            var probe = new TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            int port = ((IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();
            return port;
        }

        private static void Expect(bool condition, string message)
        {
            if (!condition) throw new Exception(message);
        }

        private static void WaitFor(Func<bool> condition, int seconds, string what)
        {
            DateTime until = DateTime.UtcNow.AddSeconds(seconds);
            while (DateTime.UtcNow < until)
            {
                if (condition()) return;
                Thread.Sleep(50);
            }
            throw new Exception("timeout: " + what);
        }

        private sealed class RecordingListener : IRelayListener
        {
            private int connected;
            private int messages;

            public int Connected
            {
                get { return Thread.VolatileRead(ref connected); }
            }

            public int Messages
            {
                get { return Thread.VolatileRead(ref messages); }
            }

            public void OnRelayConnected()
            {
                Interlocked.Increment(ref connected);
            }

            public void OnPhoneStreamChanged()
            {
            }

            public void OnPhoneMessage(Dictionary<string, object> inner)
            {
                Interlocked.Increment(ref messages);
            }
        }

        private sealed class FakeServer : IDisposable
        {
            private readonly TcpListener listener;
            private readonly string[] streamEvents;
            private readonly object gate = new object();
            private readonly List<KeyValuePair<string, string>> posts = new List<KeyValuePair<string, string>>();
            private volatile bool stopped;

            public FakeServer(int port, string[] streamEvents)
            {
                this.streamEvents = streamEvents;
                listener = new TcpListener(IPAddress.Loopback, port);
                listener.Start();
                new Thread(AcceptLoop) { IsBackground = true }.Start();
            }

            public int PostCount(string path)
            {
                lock (gate)
                {
                    int count = 0;
                    foreach (KeyValuePair<string, string> post in posts) if (post.Key == path) count++;
                    return count;
                }
            }

            public string PostBody(string path)
            {
                lock (gate)
                {
                    foreach (KeyValuePair<string, string> post in posts) if (post.Key == path) return post.Value;
                    return "";
                }
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
                        string[] first = head.Split(new[] { "\r\n" }, StringSplitOptions.None)[0].Split(' ');
                        string method = first[0];
                        string path = first[1];
                        if (method == "GET")
                        {
                            Write(stream, "HTTP/1.1 200 OK\r\nContent-Type: text/event-stream\r\nConnection: close\r\n\r\n: open\r\n\r\n");
                            foreach (string item in streamEvents) Write(stream, item);
                            while (!stopped && client.Connected)
                            {
                                Thread.Sleep(100);
                                Write(stream, ": keepalive\n\n");
                            }
                            return;
                        }
                        int length = 0;
                        foreach (string line in head.Split(new[] { "\r\n" }, StringSplitOptions.None))
                        {
                            if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase)) length = int.Parse(line.Substring(15).Trim());
                        }
                        var body = new byte[length];
                        int total = 0;
                        while (total < length)
                        {
                            int read = stream.Read(body, total, length - total);
                            if (read <= 0) break;
                            total += read;
                        }
                        lock (gate) posts.Add(new KeyValuePair<string, string>(path, Encoding.UTF8.GetString(body)));
                        Write(stream, "HTTP/1.1 202 Accepted\r\nContent-Type: application/json\r\nContent-Length: 2\r\nConnection: close\r\n\r\n{}");
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
}
