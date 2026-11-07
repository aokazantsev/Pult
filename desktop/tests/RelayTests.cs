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
            RelaySession session = ActiveSession(port);
            using (var client = new RelayClient(session, listener, TempFile()))
            {
                client.Start(listener);
                client.Send(new Dictionary<string, object> { { "type", "permission_request" }, { "request_id", "r1" } }, DateTime.UtcNow.AddMinutes(2), delegate { });
                Thread.Sleep(2500);
                Expect(!client.Connected, "not connected while the server is down");
                Expect(listener.Connected == 0, "no connect callback yet");
                using (var server = new FakeRelayServer(port, session.Pair, Point("pc_sign")))
                {
                    WaitFor(() => Inner(server, session, "permission_request") != null, 15, "queued message delivered after the server came back");
                    Expect(client.Connected, "connected");
                    Expect(listener.Connected > 0, "connect callback");
                    Expect(Json.String(Inner(server, session, "permission_request"), "request_id") == "r1", "body carries the request");
                    Expect(Inner(server, session, "hello") != null, "hello sent on connect");
                    Expect(server.Posts("messages").TrueForAll(p => p.SignatureValid && p.Stream == "st1"), "posts signed for the open stream");
                    var numbers = new HashSet<long>();
                    foreach (RecordedPost post in server.Posts("messages")) Expect(numbers.Add(post.N) && post.N >= 1, "n unique and from 1");
                    Expect(server.BadLogins == 0, "login signature accepted");
                }
            }
        }

        public static void PhoneMessageIsNotRepeatedAfterRestart()
        {
            string idsPath = TempFile();
            int port = FreePort();
            RelaySession session = ActiveSession(port);
            string envelope = Envelope.Seal(session.Directions, session.Pair, "phone", "ph-1", 1, 600, "{\"type\":\"permission_response\",\"request_id\":\"r9\",\"decision\":\"allow\"}");
            var listener = new RecordingListener();
            using (var server = new FakeRelayServer(port, session.Pair, Point("pc_sign")))
            {
                server.Queue("id: 1\nevent: message\ndata: " + envelope + "\n\n");
                using (var client = new RelayClient(session, listener, idsPath))
                {
                    client.Start(listener);
                    WaitFor(() => listener.Messages == 1, 10, "first delivery");
                    WaitFor(() => server.Posts("acks").Count > 0, 10, "ack sent");
                }
                using (var client = new RelayClient(ActiveSession(port), listener, idsPath))
                {
                    client.Start(listener);
                    WaitFor(() => server.Posts("acks").Count > 1, 10, "ack for the repeated message");
                    Expect(listener.Messages == 1, "message not handled twice after restart");
                    Expect(server.Posts("acks")[0].Body == "{\"ids\":[\"ph-1\"]}", "ack body");
                }
            }
        }

        public static void ForgedEnvelopesAreDropped()
        {
            int port = FreePort();
            RelaySession session = ActiveSession(port);
            string own = Envelope.Seal(session.Directions, session.Pair, "pc", "pc-x", 1, 600, "{\"type\":\"hello\"}");
            string good = Envelope.Seal(session.Directions, session.Pair, "phone", "ph-2", 1, 600, "{\"type\":\"permission_response\",\"request_id\":\"r2\",\"decision\":\"deny\"}");
            string tampered = good.Replace("\"ttl\":600", "\"ttl\":900");
            var listener = new RecordingListener();
            using (var server = new FakeRelayServer(port, session.Pair, Point("pc_sign")))
            using (var client = new RelayClient(session, listener, TempFile()))
            {
                server.Queue("event: message\ndata: " + own + "\n\n");
                server.Queue("event: message\ndata: " + tampered + "\n\n");
                server.Queue("event: message\ndata: " + good + "\n\n");
                client.Start(listener);
                WaitFor(() => listener.Messages == 1, 10, "good envelope delivered");
                WaitFor(() => server.Posts("acks").Count == 1, 10, "good envelope acked");
                Thread.Sleep(300);
                Expect(listener.Messages == 1, "own and tampered envelopes dropped");
                Expect(server.Posts("acks").Count == 1, "dropped envelopes are not acked");
            }
        }

        public static void PairingAccepted()
        {
            Dictionary<string, object> v = VectorTests.Load("pairing.json");
            int port = FreePort();
            RelaySession session = PendingSession(port, v);
            var listener = new RecordingListener();
            using (var server = new FakeRelayServer(port, session.Pair, Point("pc_sign")))
            using (var client = new RelayClient(session, listener, TempFile()))
            {
                client.Start(listener);
                WaitFor(() => client.Connected, 10, "pending pair logged in");
                Expect(!client.Configured, "pending pair does not take requests");
                server.Push(PairSignal(Json.String(v, "phone_sign"), Json.String(v, "phone_agree"), Json.String(v, "tag")));
                WaitFor(() => listener.Paired == 1, 10, "paired");
                Expect(session.Fingerprint == Json.String(v, "fingerprint"), "fingerprint");
                Expect(session.SecretCopy() == null, "secret wiped");
                Expect(client.Configured, "active pair takes requests");
                WaitFor(() => Inner(server, session, "hello") != null, 10, "hello after verification");
                object[] versions = Json.Value(Inner(server, session, "hello"), "protocol") as object[];
                Expect(versions != null && versions.Length == 1 && (long)versions[0] == 2, "hello protocol [2]");
                server.Push(PairSignal(Json.String(v, "phone_sign"), Json.String(v, "phone_agree"), Json.String(v, "tag")));
                Thread.Sleep(500);
                Expect(listener.Notices.Count == 0, "repeated pair signal with the same keys is ignored");
                server.Push(PairSignal(Json.String(v, "pc_agree"), Json.String(v, "phone_agree"), Json.String(v, "tag")));
                WaitFor(() => listener.Notices.Contains(RelayClient.IntrusionText), 10, "other keys reported as intrusion");
                Expect(session.SamePhone(Json.String(v, "phone_sign"), Json.String(v, "phone_agree")), "stored keys unchanged");
            }
        }

        public static void PairingRejected()
        {
            Dictionary<string, object> v = VectorTests.Load("pairing.json");
            int port = FreePort();
            RelaySession session = PendingSession(port, v);
            var listener = new RecordingListener();
            using (var server = new FakeRelayServer(port, session.Pair, Point("pc_sign")))
            using (var client = new RelayClient(session, listener, TempFile()))
            {
                client.Start(listener);
                WaitFor(() => client.Connected, 10, "pending pair logged in");
                var reject = (Dictionary<string, object>)((object[])Json.Value(v, "reject"))[0];
                server.Push(PairSignal(Json.String(v, "phone_sign"), Json.String(v, "pc_agree"), Json.String(reject, "tag")));
                WaitFor(() => listener.Rejected == 1, 10, "rejected");
                Expect(server.Posts("delete").Count == 1 && server.Posts("delete")[0].Body == "{\"reason\":\"pairing_rejected\"}", "delete pairing_rejected");
                Expect(server.Posts("delete")[0].SignatureValid, "delete signed");
                Expect(!session.IsActive && client.Ended, "pair not activated");
                Expect(Inner(server, session, "hello") == null && server.Posts("messages").Count == 0, "no hello");
            }
        }

        public static void StaleStreamLogsInAgain()
        {
            int port = FreePort();
            RelaySession session = ActiveSession(port);
            var listener = new RecordingListener();
            using (var server = new FakeRelayServer(port, session.Pair, Point("pc_sign")))
            {
                server.Plan("messages", 401, "{\"error\":\"stale_stream\"}");
                server.Plan("messages", 409, "{\"error\":\"replay\"}");
                using (var client = new RelayClient(session, listener, TempFile()))
                {
                    client.Start(listener);
                    WaitFor(() => server.Posts("messages").Count >= 3, 15, "message retried");
                    List<RecordedPost> posts = server.Posts("messages");
                    string firstId = Json.String(StrictJson.TryParseObject(posts[0].Body), "id");
                    Expect(posts[0].Stream == "st1" && posts[1].Stream == "st2", "second attempt on a new stream after stale_stream");
                    Expect(Json.String(StrictJson.TryParseObject(posts[1].Body), "id") == firstId, "same envelope id after relogin");
                    Expect(posts[2].N == posts[1].N + 1 && Json.String(StrictJson.TryParseObject(posts[2].Body), "id") == firstId, "replay retried with the next n");
                    Expect(server.Challenges >= 2, "new challenge for the new login");
                }
            }
        }

        public static void PairDeletedEndsSession()
        {
            int port = FreePort();
            RelaySession session = ActiveSession(port);
            var listener = new RecordingListener();
            using (var server = new FakeRelayServer(port, session.Pair, Point("pc_sign")))
            using (var client = new RelayClient(session, listener, TempFile()))
            {
                client.Start(listener);
                WaitFor(() => client.Connected, 10, "connected");
                server.Push("event: server\ndata: {\"type\":\"pair_deleted\",\"reason\":\"user\",\"by\":\"phone\"}\n\n");
                WaitFor(() => listener.Ended == "user/phone", 10, "pair_deleted signal ends the session");
                Expect(!client.Configured, "ended pair takes no requests");
                bool rejected = false;
                client.Send(new Dictionary<string, object> { { "type", "permission_request" } }, DateTime.UtcNow.AddMinutes(1), delegate { rejected = true; });
                Expect(rejected, "send after the end is refused at once");
            }
            int port2 = FreePort();
            RelaySession gone = ActiveSession(port2);
            var listener2 = new RecordingListener();
            using (var server = new FakeRelayServer(port2, gone.Pair, Point("pc_sign")))
            using (var client = new RelayClient(gone, listener2, TempFile()))
            {
                server.EndStreams("{\"error\":\"pair_deleted\",\"reason\":\"pc_offline\",\"by\":\"server\"}");
                client.Start(listener2);
                WaitFor(() => listener2.Ended == "pc_offline/server", 10, "410 on login ends the session");
            }
        }

        public static void UnpairSendsSignedDelete()
        {
            int port = FreePort();
            RelaySession session = ActiveSession(port);
            var listener = new RecordingListener();
            using (var server = new FakeRelayServer(port, session.Pair, Point("pc_sign")))
            using (var client = new RelayClient(session, listener, TempFile()))
            {
                client.Start(listener);
                Expect(client.DeletePair("user", TimeSpan.FromSeconds(10)), "delete confirmed");
                Expect(server.Posts("delete").Count == 1 && server.Posts("delete")[0].SignatureValid && server.Posts("delete")[0].Body == "{\"reason\":\"user\"}", "signed delete user");
                Expect(client.Ended && listener.Ended == null, "own delete is not reported as a remote end");
            }
        }

        private static string PairSignal(string phoneSign, string phoneAgree, string tag)
        {
            return "event: server\ndata: {\"type\":\"pair\",\"phone_sign\":\"" + phoneSign + "\",\"phone_agree\":\"" + phoneAgree + "\",\"tag\":\"" + tag + "\"}\n\n";
        }

        private static Dictionary<string, object> Inner(FakeRelayServer server, RelaySession session, string type)
        {
            if (session.Directions == null) return null;
            foreach (RecordedPost post in server.Posts("messages"))
            {
                OpenedEnvelope opened = Envelope.Open(session.Directions, session.Pair, "phone", post.Body);
                if (opened.Accepted && Json.String(opened.Inner, "type") == type) return opened.Inner;
            }
            return null;
        }

        private static RelaySession ActiveSession(int port)
        {
            Dictionary<string, object> v = VectorTests.Load("pairing.json");
            return RelaySession.Active("http://127.0.0.1:" + port, Json.String(v, "pair"), PcKeys(), Json.String(v, "phone_sign"), Json.String(v, "phone_agree"), QrPayload.DefaultIdleSeconds);
        }

        private static RelaySession PendingSession(int port, Dictionary<string, object> v)
        {
            return RelaySession.Pending("http://127.0.0.1:" + port, Json.String(v, "pair"), PcKeys(), Base64Url.Decode(Json.String(v, "secret")), QrPayload.DefaultIdleSeconds);
        }

        private static PairKeys PcKeys()
        {
            return new PairKeys(VectorTests.PrivateKey("pc_sign"), VectorTests.PrivateKey("pc_agree"), null, null);
        }

        private static byte[] Point(string name)
        {
            return P256.ParsePoint(Json.String(Json.Object(Json.Object(VectorTests.Load("keys.json"), "keys"), name), "public"));
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
    }
}
