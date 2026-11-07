using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Pult.Tests
{
    internal static class MessageTests
    {
        private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false);

        public static string Root()
        {
            string directory = AppDomain.CurrentDomain.BaseDirectory;
            for (int i = 0; i < 6 && !Directory.Exists(Path.Combine(directory, "protocol")); i++) directory = Path.GetDirectoryName(directory.TrimEnd('\\'));
            return directory;
        }

        public static string Canon(object value)
        {
            var text = new StringBuilder();
            Write(text, value);
            return text.ToString();
        }

        private static void Write(StringBuilder text, object value)
        {
            var map = value as IDictionary<string, object>;
            if (map != null)
            {
                text.Append('{');
                bool first = true;
                foreach (string key in map.Keys.OrderBy(k => k, StringComparer.Ordinal))
                {
                    if (!first) text.Append(',');
                    first = false;
                    text.Append(Json.Write(key)).Append(':');
                    Write(text, map[key]);
                }
                text.Append('}');
                return;
            }
            var list = value as IList;
            if (list != null && !(value is string))
            {
                text.Append('[');
                for (int i = 0; i < list.Count; i++)
                {
                    if (i > 0) text.Append(',');
                    Write(text, list[i]);
                }
                text.Append(']');
                return;
            }
            if (value is int || value is long) text.Append(Convert.ToInt64(value));
            else text.Append(Json.Write(value));
        }

        public static void Expect(bool condition, string message)
        {
            if (!condition) throw new Exception(message);
        }

        public static void ValidAndReject()
        {
            Dictionary<string, object> data = VectorTests.Load("messages.json");
            var failures = new List<string>();
            foreach (object item in (object[])data["valid"])
            {
                var vector = (Dictionary<string, object>)item;
                string problem = MessageRules.Check((Dictionary<string, object>)vector["message"], (string)vector["to"]);
                if (problem != null) failures.Add("valid " + vector["name"] + ": " + problem);
            }
            foreach (object item in (object[])data["reject"])
            {
                var vector = (Dictionary<string, object>)item;
                Dictionary<string, object> message = vector.ContainsKey("text") ? StrictJson.TryParseObject((string)vector["text"]) : (Dictionary<string, object>)vector["message"];
                if (message != null && MessageRules.Check(message, (string)vector["to"]) == null) failures.Add("reject accepted: " + vector["name"]);
            }
            Expect(failures.Count == 0, string.Join("; ", failures.ToArray()));
        }

        public static void ChoiceVectors()
        {
            Dictionary<string, object> data = (Dictionary<string, object>)VectorTests.Load("messages.json")["choice_answers"];
            var request = (Dictionary<string, object>)data["request"];
            Expect(MessageRules.Check(request, "phone") == null, "request is valid");
            var failures = new List<string>();
            foreach (object item in (object[])data["accept"])
            {
                var response = (Dictionary<string, object>)item;
                string problem = MessageRules.Check(response, "pc") ?? ChoiceAnswers.Validate(request, response);
                if (problem != null) failures.Add("accept: " + problem + " " + Json.Write(response));
            }
            foreach (object item in (object[])data["reject"])
            {
                var response = (Dictionary<string, object>)item;
                string problem = MessageRules.Check(response, "pc") ?? ChoiceAnswers.Validate(request, response);
                if (problem == null) failures.Add("reject accepted: " + Json.Write(response));
            }
            Expect(failures.Count == 0, string.Join("; ", failures.ToArray()));
        }

        public static void TailVectors()
        {
            foreach (object item in (object[])VectorTests.Load("messages.json")["tails"])
            {
                var vector = (Dictionary<string, object>)item;
                var known = new Dictionary<string, long>();
                foreach (KeyValuePair<string, object> pair in (Dictionary<string, object>)vector["known"]) known[pair.Key] = Convert.ToInt64(pair.Value);
                var threads = new List<TailThread>();
                foreach (object threadItem in (object[])vector["threads"])
                {
                    var thread = (Dictionary<string, object>)threadItem;
                    threads.Add(new TailThread
                    {
                        Thread = (string)thread["thread"],
                        LastSeq = Convert.ToInt64(thread["last_seq"]),
                        OpenRequest = (bool)thread["open_request"],
                        LastEvent = Convert.ToInt64(thread["last_event"]),
                        EntryBytes = Convert.ToInt64(thread["entry_bytes"])
                    });
                }
                Dictionary<string, long[]> got = TailPlan.Compute(known, threads);
                var expected = (Dictionary<string, object>)vector["expected"];
                foreach (KeyValuePair<string, object> pair in expected)
                {
                    long[] range = got[pair.Key];
                    var want = pair.Value as object[];
                    if (want == null) Expect(range == null, "tails " + vector["name"] + ": " + pair.Key + " expected nothing");
                    else Expect(range != null && range[0] == Convert.ToInt64(want[0]) && range[1] == Convert.ToInt64(want[1]), "tails " + vector["name"] + ": " + pair.Key);
                }
                Expect(got.Count == expected.Count, "tails " + vector["name"] + ": thread count");
            }
        }

        private static Dictionary<string, object> Example(string name)
        {
            foreach (object item in (object[])VectorTests.Load("messages.json")["valid"])
            {
                var vector = (Dictionary<string, object>)item;
                if ((string)vector["name"] == name) return (Dictionary<string, object>)vector["message"];
            }
            throw new Exception("no example " + name);
        }

        private static void SameAsExample(string name, Dictionary<string, object> built, params string[] overridden)
        {
            Dictionary<string, object> example = Example(name);
            var copy = new Dictionary<string, object>(built);
            foreach (string key in overridden)
            {
                if (example.ContainsKey(key)) copy[key] = example[key];
                else copy.Remove(key);
            }
            Expect(Canon(copy) == Canon(example), "built message differs from example '" + name + "':\n" + Canon(copy) + "\n" + Canon(example));
        }

        public static void BuiltMessagesEqualExamples()
        {
            var hub = new ThreadHub(new object(), new FakeRelay(), new DesktopSessions(null), Path.GetTempPath());
            Func<string, string, string, bool, ThreadState> state = (title, folder, status, dialog) => new ThreadState { Id = "t1", Title = title, Folder = folder, Status = status, PcDialog = dialog };
            Func<ThreadState, Dictionary<string, object>> thread = s =>
            {
                Dictionary<string, object> m = hub.Describe(s, false);
                m["type"] = "thread";
                return m;
            };
            SameAsExample("thread new", thread(state("Правка README", "Pult", "working", false)));
            ThreadState failed = state("Правка README", "Pult", "error", false);
            failed.ErrorCode = "authentication_failed";
            SameAsExample("thread error", thread(failed));
            ThreadState child = state("Проверка ссылок", "Pult", "working", true);
            child.Id = "t2";
            child.ParentId = "t1";
            SameAsExample("thread child", thread(child));
            ThreadState fork = state("Правка README", "Pult", "done", false);
            fork.Id = "t3";
            fork.ForkTitle = "Правка README";
            SameAsExample("thread fork outside session", thread(fork));
            ThreadState capped = state("Правка README", "Pult", "done", false);
            capped.TurnNote = "continuation_cap";
            SameAsExample("thread continuation cap", thread(capped));
            ThreadState placeholder = state("Оркестратор", "Pult", "unknown", false);
            placeholder.Id = "t0";
            SameAsExample("thread unknown parent placeholder", thread(placeholder));
            SameAsExample("hello pc", RelayClient.HelloMessage());
            SameAsExample("mode", new Dictionary<string, object> { { "type", "mode" }, { "mode", "phone" } });
            SameAsExample("resolved", new Dictionary<string, object> { { "type", "resolved" }, { "request_id", "r1" }, { "outcome", "answered" } });

            var relay = new FakeRelay();
            var broker = new Broker(relay, TimeSpan.FromSeconds(5), 20, new DesktopSessions(null), Path.GetTempPath(), 0, 0);
            broker.BeginSession("pair-" + Guid.NewGuid().ToString("N").Substring(0, 8), DateTime.UtcNow.AddHours(-1), new List<string> { "C:\\work" }, false);
            broker.OnPhoneMessage(new Dictionary<string, object> { { "type", "hello" }, { "protocol", new object[] { 2L } }, { "capabilities", new object[] { "choice" } } });
            broker.Hub.RememberStart(Event("SessionStart", "startup"));
            var permissionChannel = new FakeChannel(HookAck.Taken);
            var permission = Event("PermissionRequest", null);
            permission.Data["tool_name"] = "Bash";
            permission.Data["tool_input"] = new Dictionary<string, object> { { "command", "mkdir t1" } };
            RunHook(broker, permission, permissionChannel);
            Expect(WaitSent(relay, "permission_request"), "permission request sent");
            SameAsExample("permission request", relay.Sent("permission_request")[0], "request_id", "thread", "title", "after_seq");

            var choice = Event("PermissionRequest", null);
            choice.Data["tool_name"] = "AskUserQuestion";
            choice.Data["tool_input"] = new Dictionary<string, object>
            {
                { "questions", new object[]
                    {
                        new Dictionary<string, object>
                        {
                            { "question", "В каком формате?" },
                            { "header", "Формат" },
                            { "multiSelect", false },
                            { "options", new object[]
                                {
                                    new Dictionary<string, object> { { "label", "Markdown" }, { "description", "как сейчас" } },
                                    new Dictionary<string, object> { { "label", "HTML" } }
                                }
                            }
                        }
                    }
                }
            };
            RunHook(broker, choice, new FakeChannel(HookAck.Taken));
            Expect(WaitSent(relay, "choice_request"), "choice request sent");
            SameAsExample("choice request", relay.Sent("choice_request")[0], "request_id", "thread", "title", "after_seq");
            broker.SetMode(ControlMode.Pc);
            Expect(relay.Problems.Count == 0, "everything sent is valid: " + string.Join("; ", relay.Problems.ToArray()));
        }

        public static HookEvent Event(string name, string source)
        {
            var data = new Dictionary<string, object>
            {
                { "hook_event_name", name },
                { "session_id", "s-ex" },
                { "cwd", "C:\\work\\demo" },
                { "session_title", "Правка README" }
            };
            if (source != null) data["source"] = source;
            return new HookEvent(data, new Dictionary<string, object>());
        }

        public static void RunHook(Broker broker, HookEvent hookEvent, IHookChannel channel)
        {
            var thread = new System.Threading.Thread(() => broker.Handle(hookEvent, TimeSpan.FromSeconds(60), channel)) { IsBackground = true };
            thread.Start();
        }

        public static bool WaitSent(FakeRelay relay, string type)
        {
            DateTime until = DateTime.UtcNow.AddSeconds(3);
            while (DateTime.UtcNow < until)
            {
                if (relay.Sent(type).Count > 0) return true;
                System.Threading.Thread.Sleep(10);
            }
            return false;
        }

        public static void TranscriptFixtures()
        {
            string directory = Path.Combine(Root(), "desktop", "tests", "fixtures");
            string[] names = { "plain-turn", "tools", "denial", "continuation", "unknown", "phone-text", "peer" };
            foreach (string name in names)
            {
                Dictionary<string, object> expected = StrictJson.TryParseObject(File.ReadAllText(Path.Combine(directory, name + ".expected.json"), Utf8));
                string[] lines = File.ReadAllLines(Path.Combine(directory, name + ".jsonl"), Utf8);
                var context = new AdapterContext { PairedAtUtc = new DateTime(2026, 11, 7, 10, 0, 0, DateTimeKind.Utc) };
                if (expected.ContainsKey("issued_before")) foreach (object id in (object[])expected["issued_before"]) context.Issued.Add((string)id);
                if (expected.ContainsKey("phone_texts"))
                {
                    foreach (object item in (object[])expected["phone_texts"])
                    {
                        var phone = (Dictionary<string, object>)item;
                        context.PhoneTexts.Add(new PhoneText { Text = (string)phone["text"], RequestId = (string)phone["request_id"] });
                    }
                }
                TranscriptAdapter.Parse(lines, context);
                var wanted = (object[])expected["entries"];
                Expect(context.Entries.Count == wanted.Length, name + ": entries " + context.Entries.Count + ", expected " + wanted.Length + "\n" + Json.Write(context.Entries));
                string generated = null;
                for (int i = 0; i < wanted.Length; i++)
                {
                    var want = new Dictionary<string, object>((Dictionary<string, object>)wanted[i]);
                    Dictionary<string, object> got = context.Entries[i];
                    got["seq"] = (long)(i + 1);
                    if (want.ContainsKey("call") && ((string)want["call"]).StartsWith("@", StringComparison.Ordinal))
                    {
                        string actual = (string)got["call"];
                        Expect(StrictJson.IsIdentifier(actual) && actual.StartsWith("c", StringComparison.Ordinal), name + ": generated call id is a valid identifier");
                        if (generated == null) generated = actual;
                        Expect(actual == generated, name + ": the result keeps the id of its call");
                        want["call"] = actual;
                    }
                    if (want.ContainsKey("output") && (string)want["output"] == "@cut2000")
                    {
                        string output = string.Concat(Enumerable.Repeat("строка\n", 600).ToArray());
                        want["output"] = MessageRules.Cut(output, 2000);
                    }
                    Expect(Canon(got) == Canon(want), name + " entry " + (i + 1) + ":\n" + Canon(got) + "\n" + Canon(want));
                }
                var feed = new Dictionary<string, object> { { "type", "feed" }, { "thread", "t1" }, { "entries", context.Entries.Cast<object>().ToList() } };
                string problem = MessageRules.Check(feed, "phone");
                Expect(problem == null, name + ": feed is valid: " + problem);
                if (expected.ContainsKey("title")) Expect(context.CustomTitle == (string)expected["title"], name + ": title");
                if (expected.ContainsKey("candidates")) Expect(context.Candidates == Convert.ToInt32(expected["candidates"]), name + ": candidates " + context.Candidates);
                var again = new AdapterContext { PairedAtUtc = context.PairedAtUtc, Issued = context.Issued };
                TranscriptAdapter.Parse(lines, again);
                Expect(again.Entries.Count == 0, name + ": a second pass gives nothing, uuids are issued");
            }
        }

        public static void LimitsOfEntries()
        {
            var context = new AdapterContext();
            string huge = new string('я', 20000);
            string line = "{\"type\":\"assistant\",\"uuid\":\"h1\",\"timestamp\":\"2026-11-07T10:00:00.000Z\",\"message\":{\"role\":\"assistant\",\"content\":[{\"type\":\"text\",\"text\":\"" + huge + "\"},"
                + "{\"type\":\"tool_use\",\"id\":\"toolu_1\",\"name\":\"Write\",\"input\":{\"content\":\"" + new string('x', 5000) + "\",\"b\":\"" + new string('y', 5000) + "\",\"c\":\"" + new string('z', 5000) + "\"}}]}}";
            TranscriptAdapter.Parse(new[] { line }, context);
            Expect(context.Entries.Count == 2, "two entries");
            Dictionary<string, object> text = context.Entries[0];
            Expect(MessageRules.Length((string)text["text"]) == 8000 && ((string)text["text"]).EndsWith("…", StringComparison.Ordinal) && (bool)text["truncated"], "text cut to 8000 with an ellipsis and a flag");
            Dictionary<string, object> call = context.Entries[1];
            int input = ((Dictionary<string, object>)call["input"]).Values.Cast<string>().Sum(v => MessageRules.Length(v));
            Expect(input <= 3000, "input values cut to 1000 each");
            Expect(MessageRules.Size(call) <= MessageRules.ItemBytes, "item fits 16384 bytes");
            Expect(MessageRules.Size(text) <= MessageRules.ItemBytes, "text item fits 16384 bytes: " + MessageRules.Size(text));
        }
    }
}
