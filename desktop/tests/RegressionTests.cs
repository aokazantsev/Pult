using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

namespace Pult.Tests
{
    internal static class RegressionTests
    {
        private static void Expect(bool c, string m) { MessageTests.Expect(c, m); }

        private static string Temp() { return Path.Combine(Path.GetTempPath(), "PultRegression-" + Guid.NewGuid().ToString("N")); }

        public static void P01CursorOffsetExcludesPartial()
        {
            string dir = Temp();
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, "c.jsonl");
            File.WriteAllText(path, "{\"a\":1}\n{\"b\":", new UTF8Encoding(false));
            TranscriptCursor cursor = TranscriptCursor.FromOffset(path, 0);
            cursor.ReadNew();
            Expect(cursor.Offset == 8, "offset stops before the partial line: " + cursor.Offset);
        }

        public static void P02FromTimeSkipsOldFileAndSeeks()
        {
            string dir = Temp();
            DateTime since = new DateTime(2026, 11, 7, 10, 0, 0, DateTimeKind.Utc);
            string old = Rig.Transcript(dir, Rig.UserLine("o1", "старое", since.AddHours(-2)));
            File.SetLastWriteTimeUtc(old, since.AddHours(-1));
            TranscriptCursor skipped = TranscriptCursor.FromTime(old, since);
            Expect(skipped.ReadNew().Count == 0, "file not written after pairing is skipped");
            var lines = new List<string>();
            string pad = new string('x', 1000);
            for (int i = 0; i < 6000; i++) lines.Add(Rig.UserLine("u" + i, pad, i < 4000 ? since.AddMinutes(-60 + i * 0.001) : since.AddSeconds(i)));
            string big = Rig.Transcript(dir, lines.ToArray());
            long firstAfter = 0;
            for (int i = 0; i < 4000; i++) firstAfter += Encoding.UTF8.GetByteCount(lines[i]) + 1;
            TranscriptCursor cursor = TranscriptCursor.FromTime(big, since);
            long offset = cursor.Offset;
            Expect(offset > 0 && offset <= firstAfter, "seek lands before the first new record and skips old ones: " + offset + " / " + firstAfter);
            byte[] bytes = File.ReadAllBytes(big);
            Expect(bytes[offset - 1] == (byte)'\n', "seek lands on a line start");
        }

        public static void P03LargePermissionRequestFits()
        {
            var rig = new Rig(5);
            rig.Start("s-big", "C:\\work\\demo", null);
            var input = new Dictionary<string, object>();
            for (int i = 0; i < 20; i++) input["k" + i] = new string('ж', 1000);
            Thread thread = rig.Hook(rig.Event("PermissionRequest", "s-big", "C:\\work\\demo", null, null, "tool_name", "Edit", "tool_input", input), new FakeChannel(HookAck.Taken));
            rig.WaitRequest("permission_request");
            rig.NoProblems();
            rig.Broker.SetMode(ControlMode.Pc);
            thread.Join(3000);
        }

        public static void P04FitKeepsIdentifiersAndFlags()
        {
            var input = new Dictionary<string, object>();
            for (int i = 0; i < 40; i++) input["k" + i] = new string('v', 500);
            string call = "c" + new string('a', 700);
            var entry = new Dictionary<string, object> { { "kind", "tool_call" }, { "at", 1L }, { "call", call }, { "tool", "Bash" }, { "input", input } };
            bool cut = FeedLimits.Fit(entry);
            Expect(cut, "cut reported");
            Expect(MessageRules.Size(entry) <= MessageRules.ItemBytes - 64, "fits with reserve: " + MessageRules.Size(entry));
            Expect((string)entry["call"] == call && (string)entry["tool"] == "Bash", "identifiers intact");
            Expect(entry.ContainsKey("truncated") && (bool)entry["truncated"], "truncated flag");
            Expect(input.Values.Cast<string>().Any(v => MessageRules.Length(v) > 2), "values halved, not wiped");
        }

        private static HookEvent Question(Rig rig, string session, string transcript, string questionText, string label)
        {
            var input = new Dictionary<string, object>
            {
                { "questions", new object[]
                    {
                        new Dictionary<string, object>
                        {
                            { "question", questionText }, { "header", "Ф" }, { "multiSelect", false },
                            { "options", new object[] { new Dictionary<string, object> { { "label", label }, { "description", "a" } }, new Dictionary<string, object> { { "label", "B" }, { "description", "b" } } } }
                        }
                    }
                }
            };
            return rig.Event("PermissionRequest", session, "C:\\work\\demo", null, transcript, "tool_name", "AskUserQuestion", "tool_input", input);
        }

        public static void P05GhostClosedByPostToolUseWithAnswers()
        {
            var rig = new Rig(5);
            rig.Broker.ChoiceDelivery = false;
            rig.Start("s-g", "C:\\work\\demo", null);
            rig.Hello("choice");
            HookEvent question = Question(rig, "s-g", null, "Как?", "A");
            rig.Broker.Handle(question, TimeSpan.FromSeconds(10), new FakeChannel(HookAck.Taken));
            var withAnswers = new Dictionary<string, object>((Dictionary<string, object>)question.ToolInput);
            withAnswers["answers"] = new Dictionary<string, object> { { "Как?", "A" } };
            rig.Broker.Handle(rig.Event("PostToolUse", "s-g", "C:\\work\\demo", null, null, "tool_name", "AskUserQuestion", "tool_input", withAnswers), TimeSpan.FromSeconds(1), new FakeChannel(HookAck.Taken));
            Expect(rig.Relay.Sent("resolved").Count == 1 && Json.String(rig.Relay.Sent("resolved")[0], "outcome") == Outcome.ResolvedOnPc, "PostToolUse with answers closes the card");
        }

        public static void P06GhostClosedByDenialOnTick()
        {
            var rig = new Rig(5);
            rig.Broker.ChoiceDelivery = false;
            string dir = Temp();
            string transcript = Rig.Transcript(dir, Rig.UserLine("a1", "x", DateTime.UtcNow));
            rig.Start("s-g", "C:\\work\\demo", null);
            rig.Hello("choice");
            rig.Broker.Handle(Question(rig, "s-g", transcript, "Как?", "A"), TimeSpan.FromSeconds(10), new FakeChannel(HookAck.Taken));
            Rig.Append(transcript, "{\"type\":\"user\",\"uuid\":\"d3\",\"timestamp\":\"" + DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ") + "\",\"message\":{\"role\":\"user\",\"content\":[{\"type\":\"text\",\"text\":\"[Request interrupted by user for tool use]\"}]}}");
            rig.Broker.Tick();
            Expect(rig.Relay.Sent("resolved").Count == 1 && Json.String(rig.Relay.Sent("resolved")[0], "outcome") == Outcome.ResolvedOnPc, "denial on the PC closes the card on tick");
        }

        public static void P07LongLabelsAnsweredInFull()
        {
            var rig = new Rig(5);
            rig.Start("s-q", "C:\\work\\demo", null);
            rig.Hello("choice");
            var channel = new FakeChannel(HookAck.Taken);
            string text = new string('в', 600) + "?";
            string label = new string('м', 150);
            Thread thread = rig.Hook(Question(rig, "s-q", null, text, label), channel);
            string id = rig.WaitRequest("choice_request");
            rig.Broker.OnPhoneMessage(new Dictionary<string, object> { { "type", "choice_response" }, { "request_id", id }, { "answers", new object[] { new Dictionary<string, object> { { "question", "q1" }, { "selected", new object[] { "o1" } } } } } });
            thread.Join(3000);
            Dictionary<string, object> answers = Json.Object(Json.Object(Json.Object(Json.Object(Json.ParseObject(channel.Output), "hookSpecificOutput"), "decision"), "updatedInput"), "answers");
            Expect(Json.String(answers, text) == label, "full question text and full label: " + Json.Write(answers).Length);
        }

        public static void P08WorkingAfterAnswer()
        {
            var rig = new Rig(5);
            rig.WriteMeta("local_W", "s-w", "C:\\work\\demo", "Десктоп", null, null);
            Thread thread = rig.Hook(rig.Event("PermissionRequest", "s-w", "C:\\work\\demo", "local_W", null, "tool_name", "Bash", "tool_input", new Dictionary<string, object> { { "command", "ls" } }), new FakeChannel(HookAck.Taken));
            string id = rig.WaitRequest("permission_request");
            string before = Json.String(rig.Relay.Sent("thread").Last(), "status");
            rig.Broker.OnPhoneMessage(new Dictionary<string, object> { { "type", "permission_response" }, { "request_id", id }, { "decision", "allow" } });
            thread.Join(3000);
            Expect(Json.String(rig.Relay.Sent("thread").Last(), "status") == "working", "allowed from the phone: working (was " + before + ")");
        }

        public static void P09PhoneSentNeedsContiguity()
        {
            var rig = new Rig(5);
            string dir = Temp();
            DateTime now = DateTime.UtcNow;
            var lines = new List<string>();
            for (int i = 1; i <= 30; i++) lines.Add(Rig.UserLine("u" + i, "с " + i, now));
            string transcript = Rig.Transcript(dir, lines.ToArray());
            rig.Start("s-c", "C:\\work\\demo", null);
            rig.Broker.Handle(rig.Event("UserPromptSubmit", "s-c", "C:\\work\\demo", null, transcript), TimeSpan.FromSeconds(1), new FakeChannel(HookAck.Taken));
            string threadId = Json.String(rig.Relay.Sent("thread")[0], "thread");
            rig.Relay.PhoneOpen = false;
            rig.Broker.OnPhoneMessage(new Dictionary<string, object>
            {
                { "type", "hello" }, { "protocol", new object[] { 2L } }, { "capabilities", new object[0] },
                { "feeds", new object[] { new Dictionary<string, object> { { "thread", threadId }, { "seq", 10L } } } }
            });
            rig.Relay.PhoneOpen = true;
            Rig.Append(transcript, Rig.UserLine("u31", "новое", now));
            rig.Broker.Handle(rig.Event("PostToolUse", "s-c", "C:\\work\\demo", null, transcript), TimeSpan.FromSeconds(1), new FakeChannel(HookAck.Taken));
            rig.Relay.Clear();
            rig.Broker.OnRelayConnected();
            var seqs = rig.Relay.Sent("feed").SelectMany(f => ((List<object>)f["entries"]).Cast<Dictionary<string, object>>()).Select(e => Convert.ToInt64(e["seq"])).ToList();
            Expect(seqs.Contains(11) && seqs.Contains(30), "a gap is not covered by a later accepted feed: " + string.Join(",", seqs.Select(s => s.ToString()).ToArray()));
        }

        public static void P10ParentAnnouncedBeforeRestartNotice()
        {
            var rig = new Rig(5);
            rig.Start("s-x", "C:\\work\\demo", null);
            rig.Broker.Stop();
            Directory.Delete(Path.Combine(rig.State, "state-" + rig.Pair), true);
            rig.WriteMeta("local_P", "cli-p", "C:\\work\\proj", "Оркестратор", null, null);
            rig.WriteMeta("local_C", "cli-c", "C:\\work\\proj", "Дочерний", "local_P", null);
            var fresh = new FakeRelay();
            var restarted = new Broker(fresh, TimeSpan.FromSeconds(5), 20, new DesktopSessions(rig.Meta, TimeSpan.Zero), rig.State, 0, 0);
            restarted.BeginSession(rig.Pair, rig.PairedAt, rig.Folders, true);
            restarted.Handle(rig.Event("SessionStart", "cli-c", "C:\\work\\proj", "local_C", null, "source", "startup"), TimeSpan.FromSeconds(1), new FakeChannel(HookAck.Taken));
            string parent = Json.String(fresh.Sent("thread").First(t => Json.String(t, "title") == "Оркестратор"), "thread");
            List<SentMessage> log = fresh.Log;
            int threadAt = log.FindIndex(m => Json.String(m.Inner, "type") == "thread" && Json.String(m.Inner, "thread") == parent);
            int feedAt = log.FindIndex(m => Json.String(m.Inner, "type") == "feed" && Json.String(m.Inner, "thread") == parent);
            Expect(feedAt >= 0 && threadAt < feedAt, "parent thread before its restart notice: " + threadAt + " " + feedAt);
            restarted.Stop();
        }

        public static void P11TailsKeepBudgetByActualSize()
        {
            var rig = new Rig(5);
            string dir = Temp();
            DateTime now = DateTime.UtcNow;
            for (int t = 0; t < 3; t++)
            {
                var lines = new List<string>();
                for (int i = 1; i <= 50; i++) lines.Add(Rig.UserLine("u" + t + "-" + i, new string('ж', 7000), now));
                string transcript = Rig.Transcript(dir, lines.ToArray());
                rig.Start("s-b" + t, "C:\\work\\b" + t, null);
                rig.Broker.Handle(rig.Event("UserPromptSubmit", "s-b" + t, "C:\\work\\b" + t, null, transcript), TimeSpan.FromSeconds(1), new FakeChannel(HookAck.Taken));
            }
            rig.Relay.Clear();
            rig.Hello();
            long bytes = rig.Relay.Sent("feed").Sum(f => (long)MessageRules.Size(f));
            Expect(bytes > 0 && bytes <= TailPlan.Budget, "tails within 1 MiB by real size: " + bytes);
        }

        public static void P12HelloStreamStillGetsSnapshot()
        {
            var relay = new FakeRelay();
            var broker = new Broker(relay, TimeSpan.FromSeconds(5), 20, new DesktopSessions(null), Path.GetTempPath(), 0, 300);
            broker.BeginSession("pair-" + Guid.NewGuid().ToString("N").Substring(0, 8), DateTime.UtcNow.AddHours(-1), new List<string> { "C:\\work" }, false);
            for (int i = 0; i < 15; i++)
            {
                broker.OnPhoneMessage(new Dictionary<string, object> { { "type", "hello" }, { "protocol", new object[] { 2L } }, { "capabilities", new object[0] } });
                Thread.Sleep(100);
            }
            int count = relay.Sent("snapshot").Count;
            broker.Stop();
            Expect(count >= 1, "a steady hello stream does not postpone the snapshot forever: " + count);
        }

        public static void P13AdapterSilentRecords()
        {
            string[] lines =
            {
                "{\"type\":\"user\",\"uuid\":\"h1\",\"timestamp\":\"2026-11-07T10:00:05.000Z\",\"message\":{\"role\":\"user\",\"content\":\"итог\"},\"isMeta\":true,\"origin\":{\"kind\":\"peer\",\"from\":\"local_a\",\"body\":\"итог\",\"handback\":true}}",
                "{\"type\":\"user\",\"uuid\":\"h2\",\"timestamp\":\"2026-11-07T10:00:06.000Z\",\"message\":{\"role\":\"user\",\"content\":\"задача\"},\"isMeta\":true,\"origin\":{\"kind\":\"peer\",\"from\":\"local_a\",\"body\":\"задача\",\"senderTaskId\":\"t1\"}}",
                "{\"type\":\"user\",\"uuid\":\"h3\",\"timestamp\":\"2026-11-07T10:00:07.000Z\",\"message\":{\"role\":\"user\",\"content\":\"только в транскрипте\"},\"isVisibleInTranscriptOnly\":true}"
            };
            var context = new AdapterContext { PairedAtUtc = new DateTime(2026, 11, 7, 10, 0, 0, DateTimeKind.Utc) };
            TranscriptAdapter.Parse(lines, context);
            Expect(context.Entries.Count == 0, "handback, task peer and transcript-only records are silent: " + Json.Write(context.Entries));
        }

        public static void P14ToolCallTruncatedFlag()
        {
            string line = "{\"type\":\"assistant\",\"uuid\":\"t1\",\"timestamp\":\"2026-11-07T10:00:05.000Z\",\"message\":{\"role\":\"assistant\",\"content\":[{\"type\":\"tool_use\",\"id\":\"toolu_1\",\"name\":\"Write\",\"input\":{\"content\":\"" + new string('x', 5000) + "\"}}]}}";
            var context = new AdapterContext { PairedAtUtc = new DateTime(2026, 11, 7, 10, 0, 0, DateTimeKind.Utc) };
            TranscriptAdapter.Parse(new[] { line }, context);
            Expect(context.Entries.Count == 1 && context.Entries[0].ContainsKey("truncated") && (bool)context.Entries[0]["truncated"], "cut input marks the call truncated");
        }

        public static void P16SiblingFolderClaimsTranscript()
        {
            string parent = Temp();
            Directory.CreateDirectory(Path.Combine(parent, "proj"));
            Directory.CreateDirectory(Path.Combine(parent, "proj-x"));
            var marked = new List<string> { Path.Combine(parent, "proj") };
            string transcript = "C:\\u\\.claude\\projects\\" + FolderMembership.Distort(Path.Combine(parent, "proj-x")) + "\\x.jsonl";
            Expect(FolderMembership.FromTranscriptDirectory(transcript, marked) == null, "transcript of an unmarked sibling with a longer name is not taken");
            string own = "C:\\u\\.claude\\projects\\" + FolderMembership.Distort(Path.Combine(parent, "proj")) + "-sub\\x.jsonl";
            Expect(FromTranscript(own, marked) != null, "a subfolder of the marked one is still taken");
        }

        public static void P17LongTranscriptReadAcrossSyncs()
        {
            var rig = new Rig(5);
            string dir = Temp();
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, "long.jsonl");
            string filler = "{\"type\":\"progress\",\"uuid\":\"p\",\"data\":\"" + new string('x', 1000) + "\"}\n";
            using (var writer = new StreamWriter(path, false, new UTF8Encoding(false)))
            {
                for (int i = 0; i < 34000; i++) writer.Write(filler);
                writer.Write(Rig.UserLine("end", "конец", DateTime.UtcNow) + "\n");
            }
            rig.Start("s-long", "C:\\work\\demo", null);
            rig.Broker.Handle(rig.Event("PostToolUse", "s-long", "C:\\work\\demo", null, path), TimeSpan.FromSeconds(1), new FakeChannel(HookAck.Taken));
            rig.Broker.Handle(rig.Event("PostToolUse", "s-long", "C:\\work\\demo", null, path), TimeSpan.FromSeconds(1), new FakeChannel(HookAck.Taken));
            bool found = rig.Relay.Sent("feed").SelectMany(f => ((List<object>)f["entries"]).Cast<Dictionary<string, object>>()).Any(e => Json.String(e, "text") == "конец");
            Expect(found, "the record after the per-sync limit is read by the next sync");
        }

        private static string FromTranscript(string path, List<string> marked) { return FolderMembership.FromTranscriptDirectory(path, marked); }
    }
}
