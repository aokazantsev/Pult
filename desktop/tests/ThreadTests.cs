using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

namespace Pult.Tests
{
    internal sealed class BlockingChannel : IHookChannel
    {
        private readonly ManualResetEvent release = new ManualResetEvent(false);
        private readonly ManualResetEvent reached = new ManualResetEvent(false);
        public string Output;

        public bool IsGone
        {
            get { return false; }
        }

        public HookAck Deliver(string output)
        {
            Output = output;
            reached.Set();
            release.WaitOne(10000);
            return HookAck.Taken;
        }

        public bool WaitReached(int ms)
        {
            return reached.WaitOne(ms);
        }

        public void Release()
        {
            release.Set();
        }
    }

    internal sealed class Rig
    {
        public readonly FakeRelay Relay = new FakeRelay();
        public readonly string State = Path.Combine(Path.GetTempPath(), "PultThreads-" + Guid.NewGuid().ToString("N"));
        public readonly string Meta;
        public readonly string Pair = "pair-" + Guid.NewGuid().ToString("N").Substring(0, 10);
        public Broker Broker;
        public readonly DateTime PairedAt = DateTime.UtcNow.AddHours(-1);

        public Rig(double waitSeconds, params string[] folders)
        {
            Directory.CreateDirectory(State);
            Meta = Path.Combine(State, "meta");
            Directory.CreateDirectory(Meta);
            Wait = waitSeconds;
            Folders = folders.Length == 0 ? new List<string> { "C:\\work" } : folders.ToList();
            Broker = Create(false);
        }

        public readonly double Wait;
        public readonly List<string> Folders;

        public Broker Create(bool restored)
        {
            var broker = new Broker(Relay, TimeSpan.FromSeconds(Wait), 20, new DesktopSessions(Meta, TimeSpan.Zero), State, 0, 0);
            broker.BeginSession(Pair, PairedAt, Folders, restored);
            return broker;
        }

        public void WriteMeta(string local, string cli, string origin, string title, string spawnedFrom, string forkedFrom)
        {
            var map = new Dictionary<string, object> { { "sessionId", local }, { "cliSessionId", cli }, { "cwd", origin }, { "originCwd", origin }, { "title", title } };
            if (spawnedFrom != null) map["spawnedFrom"] = new Dictionary<string, object> { { "sessionId", spawnedFrom }, { "title", "родитель" } };
            if (forkedFrom != null) map["forkedFromSessionId"] = forkedFrom;
            File.WriteAllText(Path.Combine(Meta, local + ".json"), Json.Write(map), new UTF8Encoding(false));
        }

        public HookEvent Event(string name, string session, string cwd, string host, string transcript, params object[] pairs)
        {
            var data = new Dictionary<string, object> { { "hook_event_name", name }, { "session_id", session }, { "cwd", cwd } };
            if (transcript != null) data["transcript_path"] = transcript;
            var env = new Dictionary<string, object>();
            if (host != null) env["CLAUDE_CODE_HOST_SESSION_ID"] = host;
            for (int i = 0; i + 1 < pairs.Length; i += 2) data[(string)pairs[i]] = pairs[i + 1];
            return new HookEvent(data, env);
        }

        public void Start(string session, string cwd, string host)
        {
            Broker.Handle(Event("SessionStart", session, cwd, host, null, "source", "startup"), TimeSpan.FromSeconds(10), new FakeChannel(HookAck.Taken));
        }

        public Dictionary<string, object> LastThread(string title)
        {
            List<Dictionary<string, object>> all = Relay.Sent("thread");
            for (int i = all.Count - 1; i >= 0; i--)
            {
                if (Json.String(all[i], "title") == title) return all[i];
            }
            return null;
        }

        public List<string> Types()
        {
            return Relay.Log.Select(m => Json.String(m.Inner, "type")).ToList();
        }

        public void Hello(params string[] capabilities)
        {
            Broker.OnPhoneMessage(new Dictionary<string, object> { { "type", "hello" }, { "protocol", new object[] { 2L } }, { "capabilities", capabilities.Cast<object>().ToArray() } });
        }

        public static string Transcript(string dir, params string[] lines)
        {
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, Guid.NewGuid().ToString("N") + ".jsonl");
            File.WriteAllText(path, string.Concat(lines.Select(l => l + "\n").ToArray()), new UTF8Encoding(false));
            return path;
        }

        public static string UserLine(string uuid, string text, DateTime time)
        {
            return "{\"type\":\"user\",\"uuid\":\"" + uuid + "\",\"timestamp\":\"" + time.ToString("yyyy-MM-ddTHH:mm:ss.fffZ") + "\",\"message\":{\"role\":\"user\",\"content\":" + Json.Write(text) + "}}";
        }

        public static string AssistantLine(string uuid, string text, DateTime time)
        {
            return "{\"type\":\"assistant\",\"uuid\":\"" + uuid + "\",\"timestamp\":\"" + time.ToString("yyyy-MM-ddTHH:mm:ss.fffZ") + "\",\"message\":{\"role\":\"assistant\",\"content\":[{\"type\":\"text\",\"text\":" + Json.Write(text) + "}]}}";
        }

        public static void Append(string path, params string[] lines)
        {
            File.AppendAllText(path, string.Concat(lines.Select(l => l + "\n").ToArray()), new UTF8Encoding(false));
        }

        public Thread Hook(HookEvent hookEvent, IHookChannel channel)
        {
            var thread = new Thread(() => Broker.Handle(hookEvent, TimeSpan.FromSeconds(60), channel)) { IsBackground = true };
            thread.Start();
            return thread;
        }

        public string WaitRequest(string type)
        {
            DateTime until = DateTime.UtcNow.AddSeconds(3);
            while (DateTime.UtcNow < until)
            {
                List<Dictionary<string, object>> sent = Relay.Sent(type);
                if (sent.Count > 0) return Json.String(sent[sent.Count - 1], "request_id");
                Thread.Sleep(10);
            }
            throw new Exception(type + " not sent");
        }

        public void NoProblems()
        {
            MessageTests.Expect(Relay.Problems.Count == 0, "everything sent is valid: " + string.Join("; ", Relay.Problems.ToArray()));
        }
    }

    internal static class ThreadTests
    {
        private static void Expect(bool condition, string message)
        {
            MessageTests.Expect(condition, message);
        }

        private static string Temp()
        {
            return Path.Combine(Path.GetTempPath(), "PultTranscripts-" + Guid.NewGuid().ToString("N"));
        }

        private static HookEvent PermissionEvent(Rig rig, string session, string host, string transcript, string command)
        {
            return rig.Event("PermissionRequest", session, "C:\\work\\demo", host, transcript, "tool_name", "Bash", "tool_input", new Dictionary<string, object> { { "command", command } });
        }

        public static void StatusesAndTitle()
        {
            var rig = new Rig(5);
            rig.Start("s-a", "C:\\work\\demo", null);
            Dictionary<string, object> first = rig.Relay.Sent("thread")[0];
            Expect(Json.String(first, "status") == "unknown" || Json.String(first, "status") == "working", "thread message on the first event");
            Action<string, string> step = (name, expected) =>
            {
                rig.Broker.Handle(rig.Event(name, "s-a", "C:\\work\\demo", null, null, "error", "authentication_failed", "session_title", "Правка README", "stop_hook_active", false), TimeSpan.FromSeconds(1), new FakeChannel(HookAck.Taken));
                Dictionary<string, object> last = rig.Relay.Sent("thread").Last();
                Expect(Json.String(last, "status") == expected, name + " gives " + expected + ", got " + Json.String(last, "status"));
            };
            step("SessionStart", "working");
            step("UserPromptSubmit", "working");
            step("PostToolUse", "working");
            step("PostToolUseFailure", "working");
            step("StopFailure", "error");
            Expect(Json.String(rig.Relay.Sent("thread").Last(), "error") == "authentication_failed", "error code");
            step("SessionStart", "working");
            Expect(!rig.Relay.Sent("thread").Last().ContainsKey("error"), "error code goes with the status");
            Expect(Json.String(rig.Relay.Sent("thread").Last(), "title") == "Правка README", "title from agent data");
            step("SessionEnd", "ended");
            Expect(Json.String(rig.Relay.Sent("thread").Last(), "folder") == "work", "folder is the marked folder name");
            rig.NoProblems();
        }

        public static void TitleFromFirstMessage()
        {
            var rig = new Rig(5);
            string dir = Temp();
            string transcript = Rig.Transcript(dir, Rig.UserLine("a1", "Сделай красиво\nи быстро", DateTime.UtcNow));
            rig.Start("s-t", "C:\\work\\demo", null);
            rig.Broker.Handle(rig.Event("UserPromptSubmit", "s-t", "C:\\work\\demo", null, transcript), TimeSpan.FromSeconds(1), new FakeChannel(HookAck.Taken));
            Expect(rig.Relay.Sent("thread").Last()["title"].Equals("Сделай красиво и быстро"), "first user message as the title, one line");
        }

        public static void ThreadBeforeFeedBeforeRequest()
        {
            var rig = new Rig(5);
            string dir = Temp();
            string transcript = Rig.Transcript(dir, Rig.UserLine("a1", "Создай папку", DateTime.UtcNow), Rig.AssistantLine("a2", "Создаю", DateTime.UtcNow));
            rig.Start("s-o", "C:\\work\\demo", null);
            Thread thread = rig.Hook(PermissionEvent(rig, "s-o", null, transcript, "mkdir t1"), new FakeChannel(HookAck.Taken));
            rig.WaitRequest("permission_request");
            List<string> types = rig.Types();
            int firstThread = types.IndexOf("thread");
            int firstFeed = types.IndexOf("feed");
            int request = types.IndexOf("permission_request");
            Expect(firstThread >= 0 && firstThread < firstFeed && firstFeed < request, "thread, feed, request in that order: " + string.Join(",", types.ToArray()));
            Dictionary<string, object> message = rig.Relay.Sent("permission_request")[0];
            Expect(Convert.ToInt64(message["after_seq"]) == 2, "after_seq is the last entry sent before the request");
            Expect(Json.String(message, "thread") == Json.String(rig.Relay.Sent("thread")[0], "thread"), "request carries the thread of the item");
            rig.Broker.SetMode(ControlMode.Pc);
            thread.Join(3000);
            rig.NoProblems();
        }

        public static void ThreadOutsideFoldersStaysOnPc()
        {
            var rig = new Rig(5);
            DateTime before = rig.Broker.LastHookUtc;
            Thread.Sleep(30);
            var channel = new FakeChannel(HookAck.Taken);
            rig.Broker.Handle(rig.Event("PermissionRequest", "s-x", "D:\\other\\place", null, null, "tool_name", "Bash", "tool_input", new Dictionary<string, object> { { "command", "ls" } }), TimeSpan.FromSeconds(10), channel);
            Expect(string.IsNullOrEmpty(channel.Output) && channel.Deliveries == 1, "released at once");
            Expect(rig.Relay.Log.Count == 0, "nothing about a thread outside the marked folders reaches the phone");
            Expect(rig.Broker.LastHookUtc == before, "events of other threads do not move the idle clock");
        }

        public static void ChildThreadShowsParentAsUnknown()
        {
            var rig = new Rig(5);
            rig.WriteMeta("local_P", "cli-p", "C:\\work\\proj", "Оркестратор", null, null);
            rig.WriteMeta("local_C", "cli-c", "C:\\work\\proj", "Проверка ссылок", "local_P", null);
            rig.Broker.Handle(rig.Event("SessionStart", "cli-c", "C:\\work\\proj", "local_C", null, "source", "startup"), TimeSpan.FromSeconds(1), new FakeChannel(HookAck.Taken));
            List<Dictionary<string, object>> threads = rig.Relay.Sent("thread");
            Dictionary<string, object> parent = threads.First(t => Json.String(t, "title") == "Оркестратор");
            Dictionary<string, object> child = rig.LastThread("Проверка ссылок");
            Expect(Json.String(parent, "status") == "unknown", "parent shown only because of its child, without events of its own");
            Expect(Json.String(child, "parent") == Json.String(parent, "thread"), "child stands under its parent");
            Expect(threads.IndexOf(parent) < threads.IndexOf(child), "parent announced first");
            Expect(rig.Relay.Sent("feed").Count == 0, "no feed for the parent without own events");
            rig.Broker.Handle(rig.Event("SessionStart", "cli-p", "C:\\work\\proj", "local_P", null, "source", "startup"), TimeSpan.FromSeconds(1), new FakeChannel(HookAck.Taken));
            Expect(rig.Relay.Sent("thread").Select(t => Json.String(t, "thread")).Distinct().Count() == 2, "the parent's own first event keeps its thread");
            Expect(Json.String(rig.LastThread("Оркестратор"), "status") == "working", "parent's own event sets its status");
            rig.NoProblems();
        }

        public static void ParentOutsideFoldersIsNotShown()
        {
            var rig = new Rig(5);
            rig.WriteMeta("local_P", "cli-p", "D:\\elsewhere", "Оркестратор", null, null);
            rig.WriteMeta("local_C", "cli-c", "C:\\work\\proj", "Дочерний", "local_P", null);
            rig.Broker.Handle(rig.Event("SessionStart", "cli-c", "C:\\work\\proj", "local_C", null, "source", "startup"), TimeSpan.FromSeconds(1), new FakeChannel(HookAck.Taken));
            Expect(rig.Relay.Sent("thread").Count == 1 && !rig.Relay.Sent("thread")[0].ContainsKey("parent"), "child alone, a parent outside the session is not announced");
        }

        public static void ContinuationKeepsThreadForkAndUnknownMakeNew()
        {
            var rig = new Rig(5);
            rig.WriteMeta("local_H", "cli-1", "C:\\work\\proj", "Правка", null, null);
            rig.Broker.Handle(rig.Event("SessionStart", "cli-1", "C:\\work\\proj", "local_H", null, "source", "startup"), TimeSpan.FromSeconds(1), new FakeChannel(HookAck.Taken));
            string original = Json.String(rig.Relay.Sent("thread")[0], "thread");
            rig.Broker.Handle(rig.Event("SessionStart", "cli-2", "C:\\work\\proj", "local_H", null, "source", "resume"), TimeSpan.FromSeconds(1), new FakeChannel(HookAck.Taken));
            Expect(rig.Relay.Sent("thread").All(t => Json.String(t, "thread") == original), "new agent session of the same desktop thread is the same item");
            Expect(rig.Broker.Hub.Find(original).SessionIds.Contains("cli-2"), "resumed agent session joins the item");
            rig.Broker.Handle(rig.Event("PostToolUse", "cli-2", "C:\\work\\proj", "local_H", null), TimeSpan.FromSeconds(1), new FakeChannel(HookAck.Taken));
            rig.Broker.Handle(rig.Event("PostToolUse", "cli-1", "C:\\work\\proj", "local_H", null), TimeSpan.FromSeconds(1), new FakeChannel(HookAck.Taken));
            Expect(rig.Relay.Sent("thread").All(t => Json.String(t, "thread") == original), "old agent id still maps to the item");

            rig.WriteMeta("local_F", "cli-f", "C:\\work\\proj", "Правка (форк)", null, "local_H");
            rig.Broker.Handle(rig.Event("SessionStart", "cli-f", "C:\\work\\proj", "local_F", null, "source", "fork"), TimeSpan.FromSeconds(1), new FakeChannel(HookAck.Taken));
            Dictionary<string, object> fork = rig.LastThread("Правка (форк)");
            Expect(Json.String(fork, "thread") != original, "a fork is a new item");
            var forkOf = (Dictionary<string, object>)fork["fork_of"];
            Expect(Json.String(forkOf, "title") == "Правка" && Json.String(forkOf, "thread") == original, "fork_of names the source and its thread when it is in the session");

            rig.Start("cli-solo", "C:\\work\\proj", null);
            rig.Start("cli-solo2", "C:\\work\\proj", null);
            Expect(rig.Broker.Hub.All().Count == 4, "agent sessions without a desktop thread and without kinship are separate items, got " + rig.Broker.Hub.All().Count);
            rig.NoProblems();
        }

        public static void ForkOfOutsideSession()
        {
            var rig = new Rig(5);
            rig.WriteMeta("local_S", "cli-s", "D:\\elsewhere", "Источник", null, null);
            rig.WriteMeta("local_F", "cli-f", "C:\\work\\proj", "Форк", null, "local_S");
            rig.Broker.Handle(rig.Event("SessionStart", "cli-f", "C:\\work\\proj", "local_F", null, "source", "fork"), TimeSpan.FromSeconds(1), new FakeChannel(HookAck.Taken));
            var forkOf = (Dictionary<string, object>)rig.Relay.Sent("thread")[0]["fork_of"];
            Expect(Json.String(forkOf, "title") == "Источник" && !forkOf.ContainsKey("thread"), "source outside the session: title only");
        }

        public static void PcDialogMarkDesktopApp()
        {
            var rig = new Rig(5);
            rig.WriteMeta("local_D", "cli-d", "C:\\work\\proj", "Десктоп", null, null);
            rig.Broker.Handle(rig.Event("SessionStart", "cli-d", "C:\\work\\proj", "local_D", null, "source", "startup"), TimeSpan.FromSeconds(1), new FakeChannel(HookAck.Taken));
            Expect(!(bool)rig.LastThread("Десктоп")["pc_dialog"], "no mark without a request");
            HookEvent request = rig.Event("PermissionRequest", "cli-d", "C:\\work\\proj", "local_D", null, "tool_name", "Bash", "tool_input", new Dictionary<string, object> { { "command", "ls" } });
            Thread thread = rig.Hook(request, new FakeChannel(HookAck.Taken));
            rig.WaitRequest("permission_request");
            Expect((bool)rig.LastThread("Десктоп")["pc_dialog"], "desktop app thread: the dialog is on the PC from the first second");
            List<string> types = rig.Types();
            Expect(types.LastIndexOf("thread") < types.IndexOf("permission_request"), "mark sent before the request");
            rig.Broker.Handle(rig.Event("PostToolUse", "cli-d", "C:\\work\\proj", "local_D", null, "tool_name", "Bash", "tool_input", new Dictionary<string, object> { { "command", "ls" } }), TimeSpan.FromSeconds(1), new FakeChannel(HookAck.Taken));
            thread.Join(3000);
            Expect(!(bool)rig.LastThread("Десктоп")["pc_dialog"], "answer on the PC (the next event) removes the mark");
            Expect(Json.String(rig.Relay.Sent("resolved").Last(), "outcome") == Outcome.ResolvedOnPc, "closed as resolved on the PC");
            rig.NoProblems();
        }

        public static void PcDialogMarkDesktopAppPhoneAnswerClears()
        {
            var rig = new Rig(5);
            rig.WriteMeta("local_D", "cli-d", "C:\\work\\proj", "Десктоп", null, null);
            HookEvent request = rig.Event("PermissionRequest", "cli-d", "C:\\work\\proj", "local_D", null, "tool_name", "Bash", "tool_input", new Dictionary<string, object> { { "command", "ls" } });
            Thread thread = rig.Hook(request, new FakeChannel(HookAck.Taken));
            string id = rig.WaitRequest("permission_request");
            Expect((bool)rig.LastThread("Десктоп")["pc_dialog"], "marked");
            rig.Broker.OnPhoneMessage(new Dictionary<string, object> { { "type", "permission_response" }, { "request_id", id }, { "decision", "allow" } });
            thread.Join(3000);
            Expect(!(bool)rig.LastThread("Десктоп")["pc_dialog"], "the answer taken by the hook closes the desktop dialog and removes the mark");
        }

        public static void PcDialogMarkCli()
        {
            var rig = new Rig(0.3);
            rig.Start("s-cli", "C:\\work\\demo", null);
            HookEvent request = PermissionEvent(rig, "s-cli", null, null, "ls");
            Thread thread = rig.Hook(request, new FakeChannel(HookAck.Taken));
            rig.WaitRequest("permission_request");
            Expect(!(bool)rig.LastThread("…")["pc_dialog"], "CLI: no dialog while the hook waits");
            thread.Join(5000);
            Expect((bool)rig.LastThread("…")["pc_dialog"], "CLI: the hook released the request to the dialog: expired sets the mark");
            rig.Broker.Handle(rig.Event("PostToolUse", "s-cli", "C:\\work\\demo", null, null, "tool_name", "Bash", "tool_input", new Dictionary<string, object> { { "command", "ls" } }), TimeSpan.FromSeconds(1), new FakeChannel(HookAck.Taken));
            Expect(!(bool)rig.LastThread("…")["pc_dialog"], "continuation removes the mark");
            var pc = new Rig(5);
            pc.Start("s-cli", "C:\\work\\demo", null);
            pc.Broker.SetMode(ControlMode.Pc);
            var channel = new FakeChannel(HookAck.Taken);
            pc.Broker.Handle(PermissionEvent(pc, "s-cli", null, null, "ls"), TimeSpan.FromSeconds(5), channel);
            Expect((bool)pc.LastThread("…")["pc_dialog"], "route to the dialog at once (mode pc) sets the mark");
        }

        public static void PcDialogNotificationKeepsMark()
        {
            var rig = new Rig(5);
            rig.WriteMeta("local_D", "cli-d", "C:\\work\\proj", "Десктоп", null, null);
            HookEvent request = rig.Event("PermissionRequest", "cli-d", "C:\\work\\proj", "local_D", null, "tool_name", "Bash", "tool_input", new Dictionary<string, object> { { "command", "ls" } });
            Thread thread = rig.Hook(request, new FakeChannel(HookAck.Taken));
            rig.WaitRequest("permission_request");
            rig.Broker.Handle(rig.Event("Notification", "cli-d", "C:\\work\\proj", "local_D", null, "notification_type", "permission_prompt"), TimeSpan.FromSeconds(1), new FakeChannel(HookAck.Taken));
            Expect((bool)rig.LastThread("Десктоп")["pc_dialog"], "the notification about the dialog does not remove the mark");
            rig.Broker.SetMode(ControlMode.Pc);
            thread.Join(3000);
        }

        public static void TurnNoteOnCap()
        {
            var rig = new Rig(0.05);
            rig.Start("s-n", "C:\\work\\demo", null);
            rig.Hello();
            rig.Broker.Handle(rig.Event("Stop", "s-n", "C:\\work\\demo", null, null, "stop_hook_active", false, "CLAUDE_CODE_STOP_HOOK_BLOCK_CAP", "1"), TimeSpan.FromSeconds(10), new FakeChannel(HookAck.Taken));
            var env = new Dictionary<string, object> { { "CLAUDE_CODE_STOP_HOOK_BLOCK_CAP", "1" } };
            var data = new Dictionary<string, object> { { "hook_event_name", "Stop" }, { "session_id", "s-n" }, { "cwd", "C:\\work\\demo" }, { "stop_hook_active", true } };
            rig.Broker.Handle(new HookEvent(data, env), TimeSpan.FromSeconds(10), new FakeChannel(HookAck.Taken));
            Expect(Json.String(rig.Relay.Sent("thread").Last(), "turn_note") == "continuation_cap", "cap reached: the thread carries turn_note");
            rig.Broker.Handle(rig.Event("UserPromptSubmit", "s-n", "C:\\work\\demo", null, null), TimeSpan.FromSeconds(1), new FakeChannel(HookAck.Taken));
            Expect(!rig.Relay.Sent("thread").Last().ContainsKey("turn_note"), "the next working status removes the note");
            rig.NoProblems();
        }

        public static void ThreadLimit()
        {
            var rig = new Rig(5);
            for (int i = 0; i < ThreadHub.MaxThreads; i++) rig.Start("s-" + i, "C:\\work\\p" + i, null);
            Expect(rig.Broker.Hub.All().Count == ThreadHub.MaxThreads, "hundred threads");
            rig.Broker.Handle(rig.Event("SessionEnd", "s-5", "C:\\work\\p5", null, null), TimeSpan.FromSeconds(1), new FakeChannel(HookAck.Taken));
            string ended = rig.Broker.Hub.All().First(t => t.Status == "ended").Id;
            rig.Start("s-new", "C:\\work\\pnew", null);
            Expect(rig.Broker.Hub.All().Count == ThreadHub.MaxThreads, "still a hundred");
            Expect(rig.Broker.Hub.Find(ended) == null, "the ended thread is the one dropped");
            string quiet = rig.Broker.Hub.All().OrderBy(t => t.LastEventTicks).First().Id;
            rig.Start("s-new2", "C:\\work\\pnew2", null);
            Expect(rig.Broker.Hub.Find(quiet) == null, "without ended threads the one with the oldest event is dropped");
            Expect(rig.Broker.Hub.All().Count == ThreadHub.MaxThreads, "a hundred after the second drop");
        }

        public static void FolderMatching()
        {
            var marked = new List<string> { "C:\\a\\b", "C:\\work" };
            Expect(FolderMembership.MarkedFolderFor("C:\\work\\repo\\.claude\\worktrees\\x", marked) == "C:\\work", "nested folder belongs to the marked one");
            Expect(FolderMembership.MarkedFolderFor("c:/WORK/repo", marked) == "C:\\work", "case and separators do not matter");
            Expect(FolderMembership.MarkedFolderFor("C:\\workshop", marked) == null, "prefix of a name is not nesting");
            Expect(FolderMembership.FromTranscriptDirectory("C:\\u\\.claude\\projects\\C--work-repo\\x.jsonl", marked) == "C:\\work", "folder from the distorted transcript directory");
            Expect(FolderMembership.FromTranscriptDirectory("C:\\u\\.claude\\projects\\C--other\\x.jsonl", marked) == null, "unknown directory");
            var ambiguous = new List<string> { "C:\\a\\b", "C:\\a-b" };
            Expect(FolderMembership.FromTranscriptDirectory("C:\\u\\.claude\\projects\\C--a-b\\x.jsonl", ambiguous) == null, "two marked folders with the same distorted path: the thread stays on the PC");
            Expect(FolderMembership.FromTranscriptDirectory("C:\\u\\.claude\\projects\\C--a-b\\x.jsonl", new List<string> { "C:\\a\\b" }) == "C:\\a\\b", "a unique distorted path is accepted");
        }

        public static void FolderFromStartupCwdAndTranscriptDirectory()
        {
            var rig = new Rig(5);
            rig.Broker.Handle(rig.Event("PostToolUse", "s-dir", "C:\\somewhere\\else", null, "C:\\u\\.claude\\projects\\C--work-demo\\s-dir.jsonl"), TimeSpan.FromSeconds(1), new FakeChannel(HookAck.Taken));
            Expect(rig.Relay.Sent("thread").Count == 1, "folder from the transcript directory (cwd of the event is not the folder where the thread began)");
            rig.Broker.Handle(rig.Event("SessionStart", "s-st", "C:\\work\\x", null, null, "source", "startup"), TimeSpan.FromSeconds(1), new FakeChannel(HookAck.Taken));
            rig.Broker.Handle(rig.Event("PostToolUse", "s-st", "D:\\moved", null, null), TimeSpan.FromSeconds(1), new FakeChannel(HookAck.Taken));
            Expect(rig.Relay.Sent("thread").Select(t => Json.String(t, "thread")).Distinct().Count() == 2, "folder from SessionStart (startup) even when the later event has another cwd");
            Expect(Json.String(rig.Relay.Sent("thread").Last(), "folder") == "work", "folder name");
        }

        public static void StartupCwdRememberedBeforePairing()
        {
            var rig = new Rig(5);
            rig.Broker.EndSession();
            rig.Broker.Handle(rig.Event("SessionStart", "s-early", "C:\\work\\early", null, null, "source", "startup"), TimeSpan.FromSeconds(1), new FakeChannel(HookAck.Taken));
            rig.Relay.Clear();
            rig.Broker.BeginSession(rig.Pair, rig.PairedAt, rig.Folders, false);
            rig.Broker.Handle(rig.Event("PostToolUse", "s-early", "D:\\anywhere", null, null), TimeSpan.FromSeconds(1), new FakeChannel(HookAck.Taken));
            Expect(rig.Relay.Sent("thread").Count == 1, "a thread started before the pairing is recognised by the remembered startup cwd");
        }

        public static void RestartClosesOpenRequests()
        {
            var rig = new Rig(30);
            string dir = Temp();
            string transcript = Rig.Transcript(dir, Rig.UserLine("a1", "Старт", DateTime.UtcNow));
            rig.Start("s-r", "C:\\work\\demo", null);
            Thread open = rig.Hook(PermissionEvent(rig, "s-r", null, transcript, "ls"), new FakeChannel(HookAck.Taken));
            string openId = rig.WaitRequest("permission_request");
            var handedChannel = new BlockingChannel();
            Thread handed = rig.Hook(rig.Event("PermissionRequest", "s-r", "C:\\work\\demo", null, transcript, "tool_name", "Bash", "tool_input", new Dictionary<string, object> { { "command", "pwd" } }), handedChannel);
            DateTime until = DateTime.UtcNow.AddSeconds(3);
            while (rig.Relay.Sent("permission_request").Count < 2 && DateTime.UtcNow < until) Thread.Sleep(10);
            string handedId = Json.String(rig.Relay.Sent("permission_request")[1], "request_id");
            rig.Broker.OnPhoneMessage(new Dictionary<string, object> { { "type", "permission_response" }, { "request_id", handedId }, { "decision", "allow" } });
            Expect(handedChannel.WaitReached(3000), "the decision reached the hook channel");
            var fresh = new FakeRelay();
            var restarted = new Broker(fresh, TimeSpan.FromSeconds(30), 20, new DesktopSessions(rig.Meta, TimeSpan.Zero), rig.State, 0, 0);
            restarted.BeginSession(rig.Pair, rig.PairedAt, rig.Folders, true);
            Dictionary<string, string> outcomes = fresh.Sent("resolved").ToDictionary(m => Json.String(m, "request_id"), m => Json.String(m, "outcome"));
            Expect(outcomes[openId] == Outcome.Expired, "decision not handed to the hook: expired");
            Expect(outcomes[handedId] == Outcome.Unknown, "decision handed without confirmation: unknown");
            foreach (SentMessage message in fresh.Log.Where(m => Json.String(m.Inner, "type") == "resolved")) Expect(!message.Live && (message.ExpiresAtUtc - DateTime.UtcNow).TotalSeconds > 890, "resolved lives 900 seconds");
            Expect(restarted.Hub.All().Count == 1, "thread restored");
            Expect((bool)fresh.Sent("thread").Last()["pc_dialog"], "expired permission was handed to the dialog: the mark is set");
            restarted.OnRelayConnected();
            Dictionary<string, object> snapshot = fresh.Sent("snapshot")[0];
            Expect(((List<object>)snapshot["outcomes"]).Count == 2, "outcomes of both requests in the snapshot");
            Expect(!snapshot.ContainsKey("requests"), "no open requests after the restart");
            Expect(fresh.Problems.Count == 0, "valid: " + string.Join("; ", fresh.Problems.ToArray()));
            rig.Broker.SetMode(ControlMode.Pc);
            handedChannel.Release();
            open.Join(3000);
            handed.Join(3000);
        }

        public static void RestartKeepsFeedWithoutRepeatedSeq()
        {
            var rig = new Rig(5);
            string dir = Temp();
            DateTime now = DateTime.UtcNow;
            string transcript = Rig.Transcript(dir, Rig.UserLine("a1", "Первое", now), Rig.AssistantLine("a2", "Ответ", now));
            rig.Start("s-f", "C:\\work\\demo", null);
            rig.Broker.Handle(rig.Event("UserPromptSubmit", "s-f", "C:\\work\\demo", null, transcript), TimeSpan.FromSeconds(1), new FakeChannel(HookAck.Taken));
            string threadId = Json.String(rig.Relay.Sent("thread")[0], "thread");
            Expect(Convert.ToInt64(((List<object>)rig.Relay.Sent("feed")[0]["entries"]).Cast<Dictionary<string, object>>().Last()["seq"]) == 2, "two entries");
            rig.Broker.Stop();

            var fresh = new FakeRelay();
            var restarted = new Broker(fresh, TimeSpan.FromSeconds(5), 20, new DesktopSessions(rig.Meta, TimeSpan.Zero), rig.State, 0, 0);
            restarted.BeginSession(rig.Pair, rig.PairedAt, rig.Folders, true);
            Rig.Append(transcript, Rig.UserLine("a3", "Третье", now));
            restarted.Handle(rig.Event("PostToolUse", "s-f", "C:\\work\\demo", null, transcript), TimeSpan.FromSeconds(1), new FakeChannel(HookAck.Taken));
            List<Dictionary<string, object>> feeds = fresh.Sent("feed");
            Expect(feeds.Count == 1, "only the new entry after the restart: " + feeds.Count);
            var entry = (Dictionary<string, object>)((List<object>)feeds[0]["entries"])[0];
            Expect(Convert.ToInt64(entry["seq"]) == 3 && Json.String(entry, "text") == "Третье", "seq continues without repeats");
            Expect(restarted.Hub.All().Count == 1 && restarted.Hub.All()[0].Id == threadId, "same item after the restart");
            string copy = Rig.Transcript(dir, Rig.UserLine("a1", "Первое", now), Rig.AssistantLine("a2", "Ответ", now), Rig.UserLine("a3", "Третье", now), Rig.UserLine("a4", "Четвёртое", DateTime.UtcNow));
            restarted.Handle(rig.Event("SessionStart", "s-f2", "C:\\work\\demo", null, copy, "source", "startup"), TimeSpan.FromSeconds(1), new FakeChannel(HookAck.Taken));
            Expect(restarted.Hub.All().Count == 2, "a session id without kinship is a new item (CLI): its copy of history is not shown again");
            List<ThreadState> all = restarted.Hub.All();
            ThreadState continued = all.First(t => t.Id != threadId);
            Expect(continued.LastSeq == 1 && Json.String(continued.Ring[0], "text") == "Четвёртое", "only the new record of the copy is emitted, the copy of the history is not: " + continued.LastSeq);
            restarted.Stop();
        }

        public static void RestartWithLostStateStartsOver()
        {
            var rig = new Rig(5);
            rig.Start("s-l", "C:\\work\\demo", null);
            string oldId = Json.String(rig.Relay.Sent("thread")[0], "thread");
            rig.Broker.Stop();
            Directory.Delete(Path.Combine(rig.State, "state-" + rig.Pair), true);
            var fresh = new FakeRelay();
            var restarted = new Broker(fresh, TimeSpan.FromSeconds(5), 20, new DesktopSessions(rig.Meta, TimeSpan.Zero), rig.State, 0, 0);
            restarted.BeginSession(rig.Pair, rig.PairedAt, rig.Folders, true);
            restarted.Hub.RememberStart(rig.Event("SessionStart", "s-l", "C:\\work\\demo", null, null, "source", "startup"));
            restarted.Handle(rig.Event("PostToolUse", "s-l", "C:\\work\\demo", null, null), TimeSpan.FromSeconds(1), new FakeChannel(HookAck.Taken));
            string newId = Json.String(fresh.Sent("thread")[0], "thread");
            Expect(newId != oldId, "a thread whose state is lost gets a new identifier");
            var entries = (List<object>)fresh.Sent("feed")[0]["entries"];
            var notice = (Dictionary<string, object>)entries[0];
            Expect(Json.String(notice, "kind") == "notice" && Json.String(notice, "text") == ThreadHub.RestartNotice && Convert.ToInt64(notice["seq"]) == 1, "the new thread starts with the restart notice");
            Expect(fresh.Problems.Count == 0, "valid: " + string.Join("; ", fresh.Problems.ToArray()));
            restarted.Stop();
        }

        public static void EndSessionErasesState()
        {
            var rig = new Rig(5);
            rig.Start("s-e", "C:\\work\\demo", null);
            string directory = Path.Combine(rig.State, "state-" + rig.Pair);
            Expect(Directory.Exists(directory) && Directory.GetFiles(directory).Length > 0, "state on disk");
            rig.Broker.EndSession();
            Expect(!Directory.Exists(directory), "state erased with the session");
            Expect(rig.Broker.Hub.All().Count == 0, "threads dropped");
        }

        public static void SnapshotRaceAndOrder()
        {
            var rig = new Rig(10);
            rig.Start("s-s", "C:\\work\\demo", null);
            rig.Hello();
            rig.Relay.Clear();
            const int hooks = 12;
            var threads = new List<Thread>();
            var done = new ManualResetEvent(false);
            for (int i = 0; i < hooks; i++)
            {
                int n = i;
                var thread = new Thread(() =>
                {
                    done.WaitOne();
                    rig.Broker.Handle(PermissionEvent(rig, "s-s", null, null, "cmd" + n), TimeSpan.FromSeconds(60), new FakeChannel(HookAck.Taken));
                }) { IsBackground = true };
                thread.Start();
                threads.Add(thread);
            }
            var snapshots = new Thread(() =>
            {
                done.WaitOne();
                for (int i = 0; i < 20; i++)
                {
                    rig.Hello();
                    Thread.Sleep(2);
                }
            }) { IsBackground = true };
            snapshots.Start();
            done.Set();
            snapshots.Join(10000);
            DateTime until = DateTime.UtcNow.AddSeconds(5);
            while (rig.Relay.Sent("permission_request").Count < hooks && DateTime.UtcNow < until) Thread.Sleep(10);
            Expect(rig.Relay.Sent("permission_request").Count == hooks, "all requests sent");
            List<SentMessage> log = rig.Relay.Log;
            var sentBefore = new HashSet<string>();
            int snapshotsChecked = 0;
            for (int i = 0; i < log.Count; i++)
            {
                string type = Json.String(log[i].Inner, "type");
                if (type == "permission_request")
                {
                    sentBefore.Add(Json.String(log[i].Inner, "request_id"));
                    continue;
                }
                if (type != "snapshot" || Convert.ToInt64(log[i].Inner["part"]) != 1) continue;
                var listed = new HashSet<string>();
                int j = i;
                while (true)
                {
                    Dictionary<string, object> part = log[j].Inner;
                    if (part.ContainsKey("requests")) foreach (object r in (List<object>)part["requests"]) listed.Add(Json.String((Dictionary<string, object>)r, "request_id"));
                    if ((bool)part["last"]) break;
                    j++;
                }
                foreach (string id in sentBefore) Expect(listed.Contains(id), "request sent before the snapshot is inside it: " + id);
                for (int k = j + 1; k < log.Count; k++)
                {
                    if (Json.String(log[k].Inner, "type") == "permission_request") Expect(!listed.Contains(Json.String(log[k].Inner, "request_id")) || sentBefore.Contains(Json.String(log[k].Inner, "request_id")), "request queued after the snapshot is not inside it");
                }
                snapshotsChecked++;
            }
            Expect(snapshotsChecked > 0, "snapshots were checked");
            rig.NoProblems();
            rig.Broker.SetMode(ControlMode.Pc);
            foreach (Thread thread in threads) thread.Join(3000);
        }

        public static void SnapshotSplitsIntoParts()
        {
            var rig = new Rig(30);
            rig.Start("s-p", "C:\\work\\demo", null);
            rig.Hello();
            var threads = new List<Thread>();
            string filler = new string('ж', 900);
            for (int i = 0; i < 8; i++)
            {
                var input = new Dictionary<string, object>();
                for (int k = 0; k < 8; k++) input["k" + k] = filler + i;
                threads.Add(rig.Hook(rig.Event("PermissionRequest", "s-p", "C:\\work\\demo", null, null, "tool_name", "Edit", "tool_input", input), new FakeChannel(HookAck.Taken)));
            }
            DateTime until = DateTime.UtcNow.AddSeconds(5);
            while (rig.Relay.Sent("permission_request").Count < 8 && DateTime.UtcNow < until) Thread.Sleep(10);
            rig.Relay.Clear();
            rig.Hello();
            List<Dictionary<string, object>> parts = rig.Relay.Sent("snapshot");
            Expect(parts.Count >= 2, "the snapshot is cut into parts, got " + parts.Count);
            Expect(parts[0].ContainsKey("mode") && parts.Skip(1).All(p => !p.ContainsKey("mode")), "mode only in the first part");
            Expect(parts.Take(parts.Count - 1).All(p => !(bool)p["last"]) && (bool)parts.Last()["last"], "last only on the final part");
            for (int i = 0; i < parts.Count; i++)
            {
                Expect(Convert.ToInt64(parts[i]["part"]) == i + 1, "part numbers go on from 1");
                Expect(MessageRules.Size(parts[i]) <= MessageRules.MessageBytes, "part fits 48000 bytes");
                Expect(Json.String(parts[i], "snapshot") == Json.String(parts[0], "snapshot"), "one snapshot id");
            }
            Expect(parts.Sum(p => p.ContainsKey("requests") ? ((List<object>)p["requests"]).Count : 0) == 8, "all requests carried");
            Expect(parts[0].ContainsKey("threads"), "threads come first");
            rig.NoProblems();
            rig.Broker.SetMode(ControlMode.Pc);
            foreach (Thread thread in threads) thread.Join(3000);
        }

        public static void TailsFollowHelloAndAcceptedFeeds()
        {
            var rig = new Rig(5);
            string dir = Temp();
            DateTime now = DateTime.UtcNow;
            var lines = new List<string>();
            for (int i = 1; i <= 30; i++) lines.Add(Rig.UserLine("u" + i, "сообщение " + i, now));
            string transcript = Rig.Transcript(dir, lines.ToArray());
            rig.Start("s-k", "C:\\work\\demo", null);
            rig.Broker.Handle(rig.Event("UserPromptSubmit", "s-k", "C:\\work\\demo", null, transcript), TimeSpan.FromSeconds(1), new FakeChannel(HookAck.Taken));
            string threadId = Json.String(rig.Relay.Sent("thread")[0], "thread");
            rig.Relay.Clear();
            rig.Broker.OnPhoneMessage(new Dictionary<string, object>
            {
                { "type", "hello" }, { "protocol", new object[] { 2L } }, { "capabilities", new object[0] },
                { "feeds", new object[] { new Dictionary<string, object> { { "thread", threadId }, { "seq", 10L } } } }
            });
            List<Dictionary<string, object>> feeds = rig.Relay.Sent("feed");
            var first = feeds.SelectMany(f => ((List<object>)f["entries"]).Cast<Dictionary<string, object>>()).Select(e => Convert.ToInt64(e["seq"])).ToList();
            Expect(first.First() == 11 && first.Last() == 30 && first.Count == 20, "tail from the entry after the one the phone has: 11..30, got " + first.Count);
            var types = rig.Types();
            Expect(types.IndexOf("snapshot") < types.IndexOf("feed"), "tails go after the last part of the snapshot");
            Expect(((List<object>)rig.Relay.Sent("snapshot")[0]["threads"]).Cast<Dictionary<string, object>>().First()["last_seq"].Equals(30L), "last_seq in the snapshot");

            Rig.Append(transcript, Rig.UserLine("u31", "ещё", now), Rig.UserLine("u32", "ещё", now));
            rig.Broker.Handle(rig.Event("PostToolUse", "s-k", "C:\\work\\demo", null, transcript), TimeSpan.FromSeconds(1), new FakeChannel(HookAck.Taken));
            rig.Relay.Clear();
            rig.Broker.OnRelayConnected();
            Expect(rig.Relay.Sent("feed").Count == 0, "entries the server accepted while the phone stream was open are not repeated after PC reconnect");
            rig.Relay.PhoneOpen = false;
            Rig.Append(transcript, Rig.UserLine("u33", "потеряно", now));
            rig.Broker.Handle(rig.Event("PostToolUse", "s-k", "C:\\work\\demo", null, transcript), TimeSpan.FromSeconds(1), new FakeChannel(HookAck.Taken));
            rig.Relay.Clear();
            rig.Broker.OnRelayConnected();
            var again = rig.Relay.Sent("feed").SelectMany(f => ((List<object>)f["entries"]).Cast<Dictionary<string, object>>()).Select(e => Convert.ToInt64(e["seq"])).ToList();
            Expect(again.Count == 1 && again[0] == 33, "an entry accepted while the stream was closed is sent again");

            rig.Relay.Clear();
            rig.Broker.OnPhoneMessage(new Dictionary<string, object> { { "type", "hello" }, { "protocol", new object[] { 2L } }, { "capabilities", new object[0] }, { "feeds", new object[0] } });
            int count = rig.Relay.Sent("feed").SelectMany(f => (List<object>)f["entries"]).Count();
            Expect(count == 33, "an unknown thread gets the last 50 entries, all 33 here: " + count);
            rig.NoProblems();
        }

        public static void HelloWithinOneSecondsMakesOneSnapshot()
        {
            var relay = new FakeRelay();
            var broker = new Broker(relay, TimeSpan.FromSeconds(5), 20, new DesktopSessions(null), Path.GetTempPath(), 0, 150);
            broker.BeginSession("pair-" + Guid.NewGuid().ToString("N").Substring(0, 8), DateTime.UtcNow.AddHours(-1), new List<string> { "C:\\work" }, false);
            for (int i = 0; i < 4; i++)
            {
                broker.OnPhoneMessage(new Dictionary<string, object> { { "type", "hello" }, { "protocol", new object[] { 2L } }, { "capabilities", new object[0] } });
                Thread.Sleep(20);
            }
            Thread.Sleep(600);
            Expect(relay.Sent("snapshot").Count == 1, "four hello in a burst, one snapshot: " + relay.Sent("snapshot").Count);
            broker.OnPhoneMessage(new Dictionary<string, object> { { "type", "hello" }, { "protocol", new object[] { 2L } }, { "capabilities", new object[0] } });
            Thread.Sleep(600);
            Expect(relay.Sent("snapshot").Count == 2, "a later hello gets its own snapshot");
            broker.Stop();
        }

        private static HookEvent Question(Rig rig, string session, string host, string transcript)
        {
            var input = new Dictionary<string, object>
            {
                { "questions", new object[]
                    {
                        new Dictionary<string, object>
                        {
                            { "question", "В каком формате?" }, { "header", "Формат" }, { "multiSelect", false },
                            { "options", new object[] { new Dictionary<string, object> { { "label", "Markdown" }, { "description", "как сейчас" } }, new Dictionary<string, object> { { "label", "HTML" }, { "description", "html" } } } }
                        },
                        new Dictionary<string, object>
                        {
                            { "question", "Что проверить?" }, { "header", "Проверки" }, { "multiSelect", true },
                            { "options", new object[] { new Dictionary<string, object> { { "label", "Ссылки" }, { "description", "a" } }, new Dictionary<string, object> { { "label", "Код" }, { "description", "b" } } } }
                        }
                    }
                }
            };
            return rig.Event("PermissionRequest", session, "C:\\work\\demo", host, transcript, "tool_name", "AskUserQuestion", "tool_input", input);
        }

        public static void ChoiceQuestionIsAnsweredFromPhone()
        {
            var rig = new Rig(5);
            rig.Start("s-q", "C:\\work\\demo", null);
            rig.Hello("choice");
            var channel = new FakeChannel(HookAck.Taken);
            Thread thread = rig.Hook(Question(rig, "s-q", null, null), channel);
            string id = rig.WaitRequest("choice_request");
            Dictionary<string, object> request = rig.Relay.Sent("choice_request")[0];
            Expect((bool)request["answerable"], "answerable");
            Expect(Json.String(((Dictionary<string, object>)((List<object>)request["questions"])[0]), "id") == "q1", "positional question ids");
            var bad = new Dictionary<string, object> { { "type", "choice_response" }, { "request_id", id }, { "answers", new object[] { new Dictionary<string, object> { { "question", "q1" }, { "selected", new object[] { "o1" } } } } } };
            rig.Broker.OnPhoneMessage(bad);
            Thread.Sleep(100);
            Expect(thread.IsAlive && channel.Deliveries == 0, "an answer that does not cover every question is dropped, the request stays open");
            var good = new Dictionary<string, object>
            {
                { "type", "choice_response" }, { "request_id", id },
                { "answers", new object[]
                    {
                        new Dictionary<string, object> { { "question", "q1" }, { "selected", new object[0] }, { "other", "AsciiDoc" } },
                        new Dictionary<string, object> { { "question", "q2" }, { "selected", new object[] { "o1", "o2" } }, { "other", "ещё одно" } }
                    }
                }
            };
            rig.Broker.OnPhoneMessage(good);
            Expect(thread.Join(3000), "hook finished");
            Dictionary<string, object> decision = Json.Object(Json.Object(Json.ParseObject(channel.Output), "hookSpecificOutput"), "decision");
            Expect(Json.String(decision, "behavior") == "allow", "allow");
            Dictionary<string, object> updated = Json.Object(decision, "updatedInput");
            Expect(updated.ContainsKey("questions"), "updatedInput keeps the questions");
            Dictionary<string, object> answers = Json.Object(updated, "answers");
            Expect(Json.String(answers, "В каком формате?") == "AsciiDoc", "other text for a single question: " + Json.Write(answers));
            Expect(Json.String(answers, "Что проверить?") == "Ссылки, Код, ещё одно", "labels of a multi question and other text, joined: " + Json.Write(answers));
            Expect(Json.String(rig.Relay.Sent("resolved").Last(), "outcome") == Outcome.Answered, "answered");
            rig.NoProblems();
        }

        public static void ChoiceSelectedOptionsMapToLabels()
        {
            var rig = new Rig(5);
            rig.Start("s-q", "C:\\work\\demo", null);
            rig.Hello("choice");
            var channel = new FakeChannel(HookAck.Taken);
            Thread thread = rig.Hook(Question(rig, "s-q", null, null), channel);
            string id = rig.WaitRequest("choice_request");
            var answer = new Dictionary<string, object>
            {
                { "type", "choice_response" }, { "request_id", id },
                { "answers", new object[]
                    {
                        new Dictionary<string, object> { { "question", "q2" }, { "selected", new object[] { "o2" } } },
                        new Dictionary<string, object> { { "question", "q1" }, { "selected", new object[] { "o2" } } }
                    }
                }
            };
            rig.Broker.OnPhoneMessage(answer);
            thread.Join(3000);
            Dictionary<string, object> answers = Json.Object(Json.Object(Json.Object(Json.Object(Json.ParseObject(channel.Output), "hookSpecificOutput"), "decision"), "updatedInput"), "answers");
            Expect(Json.String(answers, "В каком формате?") == "HTML" && Json.String(answers, "Что проверить?") == "Код", "single options by label: " + Json.Write(answers));
        }

        public static void ChoiceWithoutCapabilityStaysOnPc()
        {
            var rig = new Rig(5);
            rig.Start("s-q", "C:\\work\\demo", null);
            rig.Hello();
            var channel = new FakeChannel(HookAck.Taken);
            rig.Broker.Handle(Question(rig, "s-q", null, null), TimeSpan.FromSeconds(10), channel);
            Expect(rig.Relay.Sent("choice_request").Count == 0 && rig.Relay.Sent("permission_request").Count == 0, "no question goes to a phone without the choice capability");
            Expect(string.IsNullOrEmpty(channel.Output) && channel.Deliveries == 1, "the hook is released to the dialog at once");
            Expect((bool)rig.LastThread("…")["pc_dialog"], "the question stands in the PC dialog");
        }

        public static void ChoiceNotAnswerableIsAGhostCard()
        {
            var rig = new Rig(0.4);
            rig.Broker.ChoiceDelivery = false;
            rig.Start("s-g", "C:\\work\\demo", null);
            rig.Hello("choice");
            var channel = new FakeChannel(HookAck.Taken);
            HookEvent question = Question(rig, "s-g", null, null);
            rig.Broker.Handle(question, TimeSpan.FromSeconds(10), channel);
            Dictionary<string, object> request = rig.Relay.Sent("choice_request")[0];
            Expect(!(bool)request["answerable"], "answerable false");
            Expect(string.IsNullOrEmpty(channel.Output) && channel.Deliveries == 1, "the hook returned no decision at once");
            Expect((bool)rig.LastThread("…")["pc_dialog"], "the mark is set");
            string id = Json.String(request, "request_id");
            rig.Broker.OnPhoneMessage(new Dictionary<string, object> { { "type", "choice_response" }, { "request_id", id }, { "answers", new object[] { new Dictionary<string, object> { { "question", "q1" }, { "selected", new object[] { "o1" } } } } } });
            Expect(rig.Relay.Sent("resolved").Count == 0, "a response to a card without actions is dropped");
            rig.Broker.Handle(rig.Event("PostToolUse", "s-g", "C:\\work\\demo", null, null, "tool_name", "AskUserQuestion", "tool_input", question.ToolInput), TimeSpan.FromSeconds(1), new FakeChannel(HookAck.Taken));
            Expect(Json.String(rig.Relay.Sent("resolved")[0], "outcome") == Outcome.ResolvedOnPc && Json.String(rig.Relay.Sent("resolved")[0], "request_id") == id, "answered on the PC: resolved_on_pc");
            Expect(!(bool)rig.LastThread("…")["pc_dialog"], "the mark is removed after the answer on the PC");

            var expiring = new Rig(0.3);
            expiring.Broker.ChoiceDelivery = false;
            expiring.Start("s-g", "C:\\work\\demo", null);
            expiring.Hello("choice");
            expiring.Broker.Handle(Question(expiring, "s-g", null, null), TimeSpan.FromSeconds(10), new FakeChannel(HookAck.Taken));
            Thread.Sleep(500);
            expiring.Broker.Tick();
            Expect(Json.String(expiring.Relay.Sent("resolved")[0], "outcome") == Outcome.Expired, "no answer for the whole wait: expired");
            var modePc = new Rig(5);
            modePc.Broker.ChoiceDelivery = false;
            modePc.Start("s-g", "C:\\work\\demo", null);
            modePc.Hello("choice");
            modePc.Broker.Handle(Question(modePc, "s-g", null, null), TimeSpan.FromSeconds(10), new FakeChannel(HookAck.Taken));
            modePc.Broker.SetMode(ControlMode.Pc);
            Expect(Json.String(modePc.Relay.Sent("resolved")[0], "outcome") == Outcome.ResolvedOnPc, "switch to the PC mode closes the card");
            rig.NoProblems();
            expiring.NoProblems();
            modePc.NoProblems();
        }

        public static void TtlByType()
        {
            var rig = new Rig(7);
            string dir = Temp();
            string transcript = Rig.Transcript(dir, Rig.UserLine("a1", "x", DateTime.UtcNow));
            rig.Start("s-ttl", "C:\\work\\demo", null);
            rig.Hello("choice");
            Thread permission = rig.Hook(PermissionEvent(rig, "s-ttl", null, transcript, "ls"), new FakeChannel(HookAck.Taken));
            string id = rig.WaitRequest("permission_request");
            Thread question = rig.Hook(Question(rig, "s-ttl", null, null), new FakeChannel(HookAck.Taken));
            rig.WaitRequest("choice_request");
            Thread text = rig.Hook(rig.Event("Stop", "s-ttl", "C:\\work\\demo", null, null, "stop_hook_active", false), new FakeChannel(HookAck.Taken));
            rig.WaitRequest("text_request");
            rig.Broker.OnPhoneMessage(new Dictionary<string, object> { { "type", "permission_response" }, { "request_id", id }, { "decision", "allow" } });
            permission.Join(3000);
            DateTime now = DateTime.UtcNow;
            foreach (SentMessage message in rig.Relay.Log)
            {
                string type = Json.String(message.Inner, "type");
                double left = (message.ExpiresAtUtc - now).TotalSeconds;
                if (type == "thread" || type == "feed" || type == "mode" || type == "snapshot") Expect(message.Live, type + " goes with ttl 0");
                else if (type == "permission_request" || type == "choice_request" || type == "text_request") Expect(!message.Live && left > 0 && left <= 7.5, type + " lives the rest of the wait: " + left);
                else if (type == "resolved") Expect(!message.Live && left > 890 && left <= 900.5, "resolved lives 900 s: " + left);
            }
            rig.Broker.SetMode(ControlMode.Pc);
            question.Join(3000);
            text.Join(3000);
            rig.NoProblems();
        }

        public static void PhoneTextShowsInFeedWithRequestId()
        {
            var rig = new Rig(5);
            string dir = Temp();
            DateTime now = DateTime.UtcNow;
            string transcript = Rig.Transcript(dir, Rig.AssistantLine("f1", "Готово. Что дальше?", now));
            rig.Start("s-pt", "C:\\work\\demo", null);
            rig.Hello();
            var channel = new FakeChannel(HookAck.Taken);
            Thread thread = rig.Hook(rig.Event("Stop", "s-pt", "C:\\work\\demo", null, transcript, "stop_hook_active", false, "last_assistant_message", "Готово. Что дальше?"), channel);
            string id = rig.WaitRequest("text_request");
            Dictionary<string, object> request = rig.Relay.Sent("text_request")[0];
            Expect(Convert.ToInt64(request["after_seq"]) == 1, "the card goes after the last message of the agent");
            Expect(Json.String(rig.Relay.Sent("thread").Last(), "status") == "done", "done while the window is open");
            rig.Broker.OnPhoneMessage(new Dictionary<string, object> { { "type", "text_response" }, { "request_id", id }, { "text", "Да, оставь как есть" } });
            thread.Join(3000);
            Expect(Json.String(rig.Relay.Sent("thread").Last(), "status") == "working", "text taken: working");
            Rig.Append(transcript, "{\"type\":\"user\",\"uuid\":\"f2\",\"timestamp\":\"" + DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ") + "\",\"message\":{\"role\":\"user\",\"content\":\"Stop hook feedback:\\nДа, оставь как есть\"},\"isMeta\":true}");
            rig.Broker.Tick();
            rig.Broker.Handle(rig.Event("PostToolUse", "s-pt", "C:\\work\\demo", null, transcript), TimeSpan.FromSeconds(1), new FakeChannel(HookAck.Taken));
            Dictionary<string, object> entry = rig.Relay.Sent("feed").SelectMany(f => ((List<object>)f["entries"]).Cast<Dictionary<string, object>>()).First(e => Json.String(e, "kind") == "user");
            Expect(Json.String(entry, "via") == "phone" && Json.String(entry, "request_id") == id && Json.String(entry, "text") == "Да, оставь как есть", "phone text in the feed: " + Json.Write(entry));
            rig.NoProblems();
        }

        public static void StateOnDiskIsAtomicAndSessionRecordKeepsFolders()
        {
            string path = Path.Combine(Path.GetTempPath(), "PultPair-" + Guid.NewGuid().ToString("N") + ".txt");
            var record = new PairRecord { Server = "https://example.com/pult", Pair = "p-1", SignKey = "a", AgreeKey = "b", IdleSeconds = 900, PairedAtUnix = 1793989146, Folders = new List<string> { "C:\\work\\one", "D:\\dev\\two=three" } };
            Expect(record.Save(path), "saved");
            PairRecord loaded = PairRecord.Load(path);
            Expect(loaded.PairedAtUnix == 1793989146 && loaded.Folders.Count == 2 && loaded.Folders[1] == "D:\\dev\\two=three", "folders and pairing time survive the record");
            Expect(!File.Exists(path + ".tmp"), "no temp file left");
        }
    }
}
