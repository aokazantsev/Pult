using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;

namespace Pult.Tests
{
    internal static class Tests
    {
        private const string Session = "s-1";
        private static readonly string Sandbox = Path.Combine(Path.GetTempPath(), "PultTests-" + Guid.NewGuid().ToString("N"));
        private static int failed;
        private static int passed;

        private static int Main()
        {
            Isolate();
            Run("vectors: keys", VectorTests.Keys);
            Run("vectors: pairing", VectorTests.Pairing);
            Run("vectors: envelope valid", VectorTests.EnvelopeValid);
            Run("vectors: envelope reject", VectorTests.EnvelopeReject);
            Run("vectors: auth", VectorTests.Auth);
            Run("strict json and base64url", VectorTests.StrictParsing);
            Run("mode pc releases at once", ModePcReleasesAtOnce);
            Run("relay not configured releases at once", OfflineReleasesAtOnce);
            Run("phone allow reaches the hook", PhoneAllow);
            Run("phone deny carries the reason", PhoneDeny);
            Run("deny without reason gets default text", PhoneDenyDefault);
            Run("wait expires", WaitExpires);
            Run("hook limit shortens the wait", HookLimitShortensWait);
            Run("hook refusal means expired", HookRefusalMeansExpired);
            Run("lost hook means unknown", LostHookMeansUnknown);
            Run("hook gone while waiting", HookGoneWhileWaiting);
            Run("allow on pc by post tool use", AllowOnPcByPostToolUse);
            Run("other tool input does not resolve", OtherInputDoesNotResolve);
            Run("deny on pc by transcript", DenyOnPcByTranscript);
            Run("switch to pc releases waiting", SwitchToPcReleases);
            Run("relay refusal releases without resolved", RelayRefusal);
            Run("answer for unknown request is expired", UnknownAnswer);
            Run("late answer gets recorded outcome", LateAnswer);
            Run("text window needs open phone stream", TextNeedsPhone);
            Run("text answer continues the turn", TextAnswer);
            Run("text window closes with phone stream", TextClosesWithPhone);
            Run("queued input on pc releases text window", TextEnqueueReleases);
            Run("continuation cap keeps window closed", ContinuationCap);
            Run("continuation cap from environment", ContinuationCapFromEnvironment);
            Run("hello sends a snapshot with the open request", HelloSendsSnapshotWithOpenRequest);
            Run("transcript cursor keeps partial line", CursorPartialLine);
            Run("transcript signals", SignalsScan);
            Run("hook channel line protocol", ChannelLineProtocol);
            Run("processed ids survive restart", RelayTests.ProcessedIdsSurviveRestart);
            Run("processed ids are bounded", RelayTests.ProcessedIdsAreBounded);
            Run("processed ids tolerate broken input", RelayTests.ProcessedIdsTolerateBrokenInput);
            Run("transcript cursor tolerates empty and bad paths", RelayTests.CursorToleratesEmptyAndBadPaths);
            Run("request without transcript path", RelayTests.PendingRequestWithoutTranscript);
            Run("request waits for the server and is delivered", RelayTests.WaitsForServerAndDeliversQueuedMessage);
            Run("phone message not repeated after restart", RelayTests.PhoneMessageIsNotRepeatedAfterRestart);
            Run("forged envelopes are dropped", RelayTests.ForgedEnvelopesAreDropped);
            Run("hook port taken at once", ListenerTests.BindsAtOnce);
            Run("hook port retried until free", ListenerTests.RetriesUntilFree);
            Run("hook port busy for good fails", ListenerTests.FailsWhenStayingBusy);
            Run("hook port held by live listener is not shared", ListenerTests.BusyPortIsNotShared);
            Run("pairing accepted", RelayTests.PairingAccepted);
            Run("pairing rejected", RelayTests.PairingRejected);
            Run("stale stream logs in again, replay retried", RelayTests.StaleStreamLogsInAgain);
            Run("pair deleted ends the session", RelayTests.PairDeletedEndsSession);
            Run("unpair sends a signed delete", RelayTests.UnpairSendsSignedDelete);
            Run("vectors: messages valid and reject", MessageTests.ValidAndReject);
            Run("vectors: choice answers", MessageTests.ChoiceVectors);
            Run("vectors: tails", MessageTests.TailVectors);
            Run("built messages equal the examples", MessageTests.BuiltMessagesEqualExamples);
            Run("D1 transcript fixtures", MessageTests.TranscriptFixtures);
            Run("D1 limits of entries", MessageTests.LimitsOfEntries);
            Run("D2 statuses, error code, title", ThreadTests.StatusesAndTitle);
            Run("D2 title from the first message", ThreadTests.TitleFromFirstMessage);
            Run("D2 thread, feed, request order", ThreadTests.ThreadBeforeFeedBeforeRequest);
            Run("D2 thread outside the folders stays on the PC", ThreadTests.ThreadOutsideFoldersStaysOnPc);
            Run("D2 child shows its parent as unknown", ThreadTests.ChildThreadShowsParentAsUnknown);
            Run("D2 parent outside the folders is not shown", ThreadTests.ParentOutsideFoldersIsNotShown);
            Run("D2 continuation, fork, separate items", ThreadTests.ContinuationKeepsThreadForkAndUnknownMakeNew);
            Run("D2 fork of a thread outside the session", ThreadTests.ForkOfOutsideSession);
            Run("D2 pc dialog mark: desktop app", ThreadTests.PcDialogMarkDesktopApp);
            Run("D2 pc dialog mark: phone answer clears", ThreadTests.PcDialogMarkDesktopAppPhoneAnswerClears);
            Run("D2 pc dialog mark: CLI", ThreadTests.PcDialogMarkCli);
            Run("D2 pc dialog mark: notification keeps it", ThreadTests.PcDialogNotificationKeepsMark);
            Run("D2 turn note on the cap", ThreadTests.TurnNoteOnCap);
            Run("D2 hundred threads limit", ThreadTests.ThreadLimit);
            Run("D2 folder matching", ThreadTests.FolderMatching);
            Run("D2 folder from startup cwd and transcript directory", ThreadTests.FolderFromStartupCwdAndTranscriptDirectory);
            Run("D2 startup cwd remembered before pairing", ThreadTests.StartupCwdRememberedBeforePairing);
            Run("D3 restart closes open requests", ThreadTests.RestartClosesOpenRequests);
            Run("D3 restart keeps the feed, no repeated seq", ThreadTests.RestartKeepsFeedWithoutRepeatedSeq);
            Run("D3 restart with lost state starts over", ThreadTests.RestartWithLostStateStartsOver);
            Run("D3 end of session erases the state", ThreadTests.EndSessionErasesState);
            Run("D3 pair record keeps folders and time", ThreadTests.StateOnDiskIsAtomicAndSessionRecordKeepsFolders);
            Run("D4 snapshot race and order", ThreadTests.SnapshotRaceAndOrder);
            Run("D4 snapshot parts", ThreadTests.SnapshotSplitsIntoParts);
            Run("D4 tails follow hello and accepted feeds", ThreadTests.TailsFollowHelloAndAcceptedFeeds);
            Run("D4 hello burst makes one snapshot", ThreadTests.HelloWithinOneSecondsMakesOneSnapshot);
            Run("D5 question answered from the phone", ThreadTests.ChoiceQuestionIsAnsweredFromPhone);
            Run("D5 selected options map to labels", ThreadTests.ChoiceSelectedOptionsMapToLabels);
            Run("D5 question without the capability stays on the PC", ThreadTests.ChoiceWithoutCapabilityStaysOnPc);
            Run("D5 not answerable question is a card without actions", ThreadTests.ChoiceNotAnswerableIsAGhostCard);
            Run("D6 ttl by type", ThreadTests.TtlByType);
            Run("D6 phone text shows in the feed", ThreadTests.PhoneTextShowsInFeedWithRequestId);
            Run("D6 posts keep the queue order", RelayTests.MessagesArePostedInQueueOrder);
            Run("D7 invalid phone messages are dropped and acked", RelayTests.InvalidPhoneMessagesAreDroppedAndAcked);
            Run("cursor offset stops before a partial line", RegressionTests.P01CursorOffsetExcludesPartial);
            Run("cursor from the pairing time", RegressionTests.P02FromTimeSkipsOldFileAndSeeks);
            Run("large permission request fits", RegressionTests.P03LargePermissionRequestFits);
            Run("fit keeps identifiers and flags", RegressionTests.P04FitKeepsIdentifiersAndFlags);
            Run("card without actions closed by PostToolUse with answers", RegressionTests.P05GhostClosedByPostToolUseWithAnswers);
            Run("card without actions closed by denial on tick", RegressionTests.P06GhostClosedByDenialOnTick);
            Run("long labels answered in full", RegressionTests.P07LongLabelsAnsweredInFull);
            Run("working after the phone answer", RegressionTests.P08WorkingAfterAnswer);
            Run("accepted feeds need contiguity", RegressionTests.P09PhoneSentNeedsContiguity);
            Run("parent announced before the restart notice", RegressionTests.P10ParentAnnouncedBeforeRestartNotice);
            Run("tails within 1 MiB by real size", RegressionTests.P11TailsKeepBudgetByActualSize);
            Run("steady hello stream still gets a snapshot", RegressionTests.P12HelloStreamStillGetsSnapshot);
            Run("silent records of the agent", RegressionTests.P13AdapterSilentRecords);
            Run("cut tool input marks the call truncated", RegressionTests.P14ToolCallTruncatedFlag);
            Run("unmarked sibling folder is not taken", RegressionTests.P16SiblingFolderClaimsTranscript);
            Run("long transcript read across syncs", RegressionTests.P17LongTranscriptReadAcrossSyncs);
            Run("cut tail moves the phone base", RegressionTests.P18TailCutByKnownLimitAdvancesPhoneSent);
            Run("tail of a thread absent in hello is not repeated", RegressionTests.P19TailOfThreadAbsentInHelloIsNotRepeated);
            Run("changed desktop meta is read again", RegressionTests.P20DesktopSessionsRereadChangedFile);
            Run("prior cli ids find desktop meta", RegressionTests.P21DesktopSessionsFindsByPriorCliId);
            Run("deleted desktop meta leaves the index", RegressionTests.P22DesktopSessionsDropsDeletedFile);
            Run("locked and partial desktop meta are retried", RegressionTests.P23DesktopSessionsRetriesLockedAndPartialFile);
            Run("warm scan indexes desktop meta", RegressionTests.P24DesktopSessionsWarmIndexesBeforeFind);
            Console.WriteLine();
            Console.WriteLine("passed " + passed + ", failed " + failed);
            try
            {
                Directory.Delete(Sandbox, true);
            }
            catch (IOException)
            {
            }
            return failed == 0 ? 0 : 1;
        }

        private static void Isolate()
        {
            Directory.CreateDirectory(Sandbox);
            typeof(UserDataPaths).GetField("Directory", BindingFlags.Static | BindingFlags.Public).SetValue(null, Sandbox);
        }

        private static void Run(string name, Action test)
        {
            try
            {
                test();
                passed++;
                Console.WriteLine("ok    " + name);
            }
            catch (Exception error)
            {
                failed++;
                Console.WriteLine("FAIL  " + name + ": " + error.Message);
            }
        }

        private static void Check(bool condition, string message)
        {
            if (!condition) throw new Exception(message);
        }

        private static void Equal(object expected, object actual, string what)
        {
            if (!Equals(expected, actual)) throw new Exception(what + ": expected <" + expected + ">, got <" + actual + ">");
        }

        private static Broker NewBroker(IRelay relay, double waitSeconds)
        {
            var broker = new Broker(relay, TimeSpan.FromSeconds(waitSeconds), 20, new DesktopSessions(null), Sandbox, 0, 0);
            broker.BeginSession("pair-" + Guid.NewGuid().ToString("N").Substring(0, 8), DateTime.UtcNow.AddHours(-1), new List<string> { "C:\\work" }, false);
            broker.Hub.RememberStart(Event("SessionStart", null, "source", "startup"));
            return broker;
        }

        private static HookEvent Event(string name, string transcript, params object[] pairs)
        {
            var data = new Dictionary<string, object> { { "hook_event_name", name }, { "session_id", Session }, { "cwd", "C:\\work\\demo" }, { "session_title", "Demo thread" } };
            if (transcript != null) data["transcript_path"] = transcript;
            var env = new Dictionary<string, object>();
            for (int i = 0; i + 1 < pairs.Length; i += 2)
            {
                string key = (string)pairs[i];
                if (key.StartsWith("CLAUDE_", StringComparison.Ordinal)) env[key] = pairs[i + 1];
                else data[key] = pairs[i + 1];
            }
            return new HookEvent(data, env);
        }

        private static HookEvent Permission(string command, string transcript)
        {
            return Event("PermissionRequest", transcript, "tool_name", "Bash", "tool_input", new Dictionary<string, object> { { "command", command } });
        }

        private static Thread Start(Broker broker, HookEvent hookEvent, IHookChannel channel, double limitSeconds)
        {
            var thread = new Thread(() => broker.Handle(hookEvent, TimeSpan.FromSeconds(limitSeconds), channel)) { IsBackground = true };
            thread.Start();
            return thread;
        }

        private static string WaitRequest(FakeRelay relay, string type)
        {
            DateTime until = DateTime.UtcNow.AddSeconds(3);
            while (DateTime.UtcNow < until)
            {
                List<Dictionary<string, object>> sent = relay.Sent(type);
                if (sent.Count > 0) return Json.String(sent[sent.Count - 1], "request_id");
                Thread.Sleep(10);
            }
            throw new Exception(type + " not sent");
        }

        private static void Finish(Thread thread)
        {
            Check(thread.Join(5000), "hook handling did not finish");
        }

        private static string Resolved(FakeRelay relay, string id)
        {
            foreach (Dictionary<string, object> message in relay.Sent("resolved"))
            {
                if (Json.String(message, "request_id") == id) return Json.String(message, "outcome");
            }
            return null;
        }

        private static Dictionary<string, object> Answer(string type, string id, params string[] pairs)
        {
            var inner = new Dictionary<string, object> { { "type", type }, { "request_id", id } };
            for (int i = 0; i + 1 < pairs.Length; i += 2) inner[pairs[i]] = pairs[i + 1];
            return inner;
        }

        private static string NewTranscript()
        {
            string path = Path.Combine(Sandbox, Guid.NewGuid().ToString("N") + ".jsonl");
            File.WriteAllText(path, "{\"type\":\"user\",\"message\":{\"role\":\"user\",\"content\":\"old [Request interrupted by user for tool use]\"}}\n");
            return path;
        }

        private static void Append(string path, string text)
        {
            File.AppendAllText(path, text, new UTF8Encoding(false));
        }

        private static void ModePcReleasesAtOnce()
        {
            var relay = new FakeRelay();
            Broker broker = NewBroker(relay, 5);
            broker.SetMode(ControlMode.Pc);
            var channel = new FakeChannel(HookAck.Taken);
            broker.Handle(Permission("ls", null), TimeSpan.FromSeconds(60), channel);
            Equal(1, channel.Deliveries, "deliveries");
            Check(string.IsNullOrEmpty(channel.Output), "no decision expected");
            Equal(0, relay.Sent("permission_request").Count, "requests");
            Equal("pc", Json.String(relay.Sent("mode")[0], "mode"), "mode message");
        }

        private static void OfflineReleasesAtOnce()
        {
            Broker broker = NewBroker(new OfflineRelay(), 5);
            var channel = new FakeChannel(HookAck.Taken);
            broker.Handle(Permission("ls", null), TimeSpan.FromSeconds(60), channel);
            Check(string.IsNullOrEmpty(channel.Output), "no decision expected");
        }

        private static void PhoneAllow()
        {
            var relay = new FakeRelay();
            Broker broker = NewBroker(relay, 5);
            var channel = new FakeChannel(HookAck.Taken);
            Thread thread = Start(broker, Permission("ls", null), channel, 60);
            string id = WaitRequest(relay, "permission_request");
            Dictionary<string, object> request = relay.Sent("permission_request")[0];
            Equal("Bash", Json.String(request, "tool"), "tool");
            Equal("ls", Json.String(request, "summary"), "summary");
            Equal("Demo thread", Json.String(request, "title"), "title");
            broker.OnPhoneMessage(Answer("permission_response", id, "decision", "allow"));
            Finish(thread);
            Dictionary<string, object> output = Json.ParseObject(channel.Output);
            Equal("allow", Json.String(Json.Object(Json.Object(output, "hookSpecificOutput"), "decision"), "behavior"), "behavior");
            Equal("PermissionRequest", Json.String(Json.Object(output, "hookSpecificOutput"), "hookEventName"), "event name");
            Equal(Outcome.Answered, Resolved(relay, id), "outcome");
        }

        private static void PhoneDeny()
        {
            var relay = new FakeRelay();
            Broker broker = NewBroker(relay, 5);
            var channel = new FakeChannel(HookAck.Taken);
            Thread thread = Start(broker, Permission("rm x", null), channel, 60);
            string id = WaitRequest(relay, "permission_request");
            broker.OnPhoneMessage(Answer("permission_response", id, "decision", "deny", "reason", "не сейчас"));
            Finish(thread);
            Dictionary<string, object> decision = Json.Object(Json.Object(Json.ParseObject(channel.Output), "hookSpecificOutput"), "decision");
            Equal("deny", Json.String(decision, "behavior"), "behavior");
            Equal("не сейчас", Json.String(decision, "message"), "message");
        }

        private static void PhoneDenyDefault()
        {
            var relay = new FakeRelay();
            Broker broker = NewBroker(relay, 5);
            var channel = new FakeChannel(HookAck.Taken);
            Thread thread = Start(broker, Permission("rm y", null), channel, 60);
            string id = WaitRequest(relay, "permission_request");
            broker.OnPhoneMessage(Answer("permission_response", id, "decision", "deny"));
            Finish(thread);
            Dictionary<string, object> decision = Json.Object(Json.Object(Json.ParseObject(channel.Output), "hookSpecificOutput"), "decision");
            Equal("Запрещено с телефона", Json.String(decision, "message"), "message");
        }

        private static void WaitExpires()
        {
            var relay = new FakeRelay();
            Broker broker = NewBroker(relay, 0.3);
            var channel = new FakeChannel(HookAck.Taken);
            Thread thread = Start(broker, Permission("ls", null), channel, 60);
            string id = WaitRequest(relay, "permission_request");
            Finish(thread);
            Check(string.IsNullOrEmpty(channel.Output), "no decision expected");
            Equal(Outcome.Expired, Resolved(relay, id), "outcome");
        }

        private static void HookLimitShortensWait()
        {
            var relay = new FakeRelay();
            Broker broker = NewBroker(relay, 60);
            var channel = new FakeChannel(HookAck.Taken);
            DateTime started = DateTime.UtcNow;
            Thread thread = Start(broker, Permission("ls", null), channel, Broker.HookMargin.TotalSeconds + 0.3);
            Finish(thread);
            Check(DateTime.UtcNow - started < TimeSpan.FromSeconds(3), "wait not shortened by hook limit");
        }

        private static void HookRefusalMeansExpired()
        {
            var relay = new FakeRelay();
            Broker broker = NewBroker(relay, 5);
            var channel = new FakeChannel(HookAck.Refused);
            Thread thread = Start(broker, Permission("ls", null), channel, 60);
            string id = WaitRequest(relay, "permission_request");
            broker.OnPhoneMessage(Answer("permission_response", id, "decision", "allow"));
            Finish(thread);
            Equal(Outcome.Expired, Resolved(relay, id), "outcome");
        }

        private static void LostHookMeansUnknown()
        {
            var relay = new FakeRelay();
            Broker broker = NewBroker(relay, 5);
            var channel = new FakeChannel(HookAck.Lost);
            Thread thread = Start(broker, Permission("ls", null), channel, 60);
            string id = WaitRequest(relay, "permission_request");
            broker.OnPhoneMessage(Answer("permission_response", id, "decision", "allow"));
            Finish(thread);
            Equal(Outcome.Unknown, Resolved(relay, id), "outcome");
        }

        private static void HookGoneWhileWaiting()
        {
            var relay = new FakeRelay();
            Broker broker = NewBroker(relay, 5);
            var channel = new FakeChannel(HookAck.Taken);
            Thread thread = Start(broker, Permission("ls", null), channel, 60);
            string id = WaitRequest(relay, "permission_request");
            channel.Gone = true;
            Finish(thread);
            Equal(Outcome.Expired, Resolved(relay, id), "outcome");
        }

        private static void AllowOnPcByPostToolUse()
        {
            var relay = new FakeRelay();
            Broker broker = NewBroker(relay, 5);
            var channel = new FakeChannel(HookAck.Taken);
            Thread thread = Start(broker, Permission("echo a", null), channel, 60);
            string id = WaitRequest(relay, "permission_request");
            var post = new FakeChannel(HookAck.Taken);
            broker.Handle(Event("PostToolUse", null, "tool_name", "Bash", "tool_input", new Dictionary<string, object> { { "command", "echo a" } }), TimeSpan.FromSeconds(10), post);
            Finish(thread);
            Check(string.IsNullOrEmpty(channel.Output), "no decision expected");
            Check(string.IsNullOrEmpty(post.Output), "post tool use gets no output");
            Equal(Outcome.ResolvedOnPc, Resolved(relay, id), "outcome");
            broker.OnPhoneMessage(Answer("permission_response", id, "decision", "allow"));
            Equal(2, relay.Sent("resolved").Count, "late answer gets its own resolved");
        }

        private static void OtherInputDoesNotResolve()
        {
            var relay = new FakeRelay();
            Broker broker = NewBroker(relay, 0.6);
            var channel = new FakeChannel(HookAck.Taken);
            Thread thread = Start(broker, Permission("echo a", null), channel, 60);
            string id = WaitRequest(relay, "permission_request");
            broker.Handle(Event("PostToolUse", null, "tool_name", "Bash", "tool_input", new Dictionary<string, object> { { "command", "echo b" } }), TimeSpan.FromSeconds(10), new FakeChannel(HookAck.Taken));
            Finish(thread);
            Equal(Outcome.Expired, Resolved(relay, id), "outcome");
        }

        private static void DenyOnPcByTranscript()
        {
            string transcript = NewTranscript();
            var relay = new FakeRelay();
            Broker broker = NewBroker(relay, 5);
            var channel = new FakeChannel(HookAck.Taken);
            Thread thread = Start(broker, Permission("rm z", transcript), channel, 60);
            string id = WaitRequest(relay, "permission_request");
            Thread.Sleep(100);
            Check(thread.IsAlive, "old transcript lines must not release");
            Append(transcript, "{\"type\":\"user\",\"message\":{\"role\":\"user\",\"content\":[{\"type\":\"tool_result\",\"content\":\"The user doesn't want to proceed\",\"is_error\":true}]},\"toolUseResult\":\"User rejected tool use\",\"toolDenialKind\":\"user-rejected\"}\n");
            Finish(thread);
            Equal(Outcome.ResolvedOnPc, Resolved(relay, id), "outcome");
        }

        private static void SwitchToPcReleases()
        {
            var relay = new FakeRelay();
            Broker broker = NewBroker(relay, 5);
            var channel = new FakeChannel(HookAck.Taken);
            Thread thread = Start(broker, Permission("ls", null), channel, 60);
            string id = WaitRequest(relay, "permission_request");
            broker.SetMode(ControlMode.Pc);
            Finish(thread);
            Equal(Outcome.ResolvedOnPc, Resolved(relay, id), "outcome");
        }

        private static void RelayRefusal()
        {
            var relay = new FakeRelay { RejectAll = true };
            Broker broker = NewBroker(relay, 5);
            var channel = new FakeChannel(HookAck.Taken);
            Thread thread = Start(broker, Permission("ls", null), channel, 60);
            Finish(thread);
            Check(string.IsNullOrEmpty(channel.Output), "no decision expected");
            Equal(0, relay.Sent("resolved").Count, "resolved messages");
        }

        private static void UnknownAnswer()
        {
            var relay = new FakeRelay();
            Broker broker = NewBroker(relay, 5);
            broker.OnPhoneMessage(Answer("permission_response", "r-missing", "decision", "allow"));
            Equal(Outcome.Expired, Resolved(relay, "r-missing"), "outcome");
        }

        private static void LateAnswer()
        {
            var relay = new FakeRelay();
            Broker broker = NewBroker(relay, 5);
            var channel = new FakeChannel(HookAck.Taken);
            Thread thread = Start(broker, Permission("ls", null), channel, 60);
            string id = WaitRequest(relay, "permission_request");
            broker.OnPhoneMessage(Answer("permission_response", id, "decision", "allow"));
            Finish(thread);
            broker.OnPhoneMessage(Answer("permission_response", id, "decision", "deny"));
            List<Dictionary<string, object>> resolved = relay.Sent("resolved");
            Equal(2, resolved.Count, "resolved messages");
            Equal(Outcome.Answered, Json.String(resolved[1], "outcome"), "repeated outcome");
            Equal(1, channel.Deliveries, "deliveries");
        }

        private static void TextNeedsPhone()
        {
            var relay = new FakeRelay { PhoneOpen = false };
            Broker broker = NewBroker(relay, 5);
            var channel = new FakeChannel(HookAck.Taken);
            broker.Handle(Event("Stop", null, "stop_hook_active", false), TimeSpan.FromSeconds(60), channel);
            Check(string.IsNullOrEmpty(channel.Output), "no decision expected");
            Equal(0, relay.Sent("text_request").Count, "text requests");
        }

        private static void TextAnswer()
        {
            var relay = new FakeRelay();
            Broker broker = NewBroker(relay, 5);
            var channel = new FakeChannel(HookAck.Taken);
            Thread thread = Start(broker, Event("Stop", null, "stop_hook_active", false, "last_assistant_message", "Готово"), channel, 60);
            string id = WaitRequest(relay, "text_request");
            Equal("Готово", Json.String(relay.Sent("text_request")[0], "last_message"), "last message");
            broker.OnPhoneMessage(Answer("text_response", id, "text", "продолжай"));
            Finish(thread);
            Dictionary<string, object> output = Json.ParseObject(channel.Output);
            Equal("block", Json.String(output, "decision"), "decision");
            Equal("продолжай", Json.String(output, "reason"), "reason");
            Equal(Outcome.Answered, Resolved(relay, id), "outcome");
        }

        private static void TextClosesWithPhone()
        {
            var relay = new FakeRelay();
            Broker broker = NewBroker(relay, 5);
            var channel = new FakeChannel(HookAck.Taken);
            Thread thread = Start(broker, Event("Stop", null, "stop_hook_active", false), channel, 60);
            string id = WaitRequest(relay, "text_request");
            relay.PhoneOpen = false;
            broker.OnPhoneStreamChanged();
            Finish(thread);
            Check(string.IsNullOrEmpty(channel.Output), "no decision expected");
            Equal(Outcome.Expired, Resolved(relay, id), "outcome");
        }

        private static void TextEnqueueReleases()
        {
            string transcript = NewTranscript();
            var relay = new FakeRelay();
            Broker broker = NewBroker(relay, 5);
            var channel = new FakeChannel(HookAck.Taken);
            Thread thread = Start(broker, Event("Stop", transcript, "stop_hook_active", false), channel, 60);
            string id = WaitRequest(relay, "text_request");
            Append(transcript, "{\"type\":\"queue-operation\",\"operation\":\"enqueue\",\"timestamp\":\"2026-11-06T18:29:58.279Z\",\"content\":\"ввод на ПК\"}\n");
            Finish(thread);
            Check(string.IsNullOrEmpty(channel.Output), "no decision expected");
            Equal(Outcome.ResolvedOnPc, Resolved(relay, id), "outcome");
            broker.OnPhoneMessage(Answer("text_response", id, "text", "поздно"));
            Equal(Outcome.ResolvedOnPc, Json.String(relay.Sent("resolved")[1], "outcome"), "late text outcome");
        }

        private static void ContinuationCap()
        {
            var relay = new FakeRelay();
            Broker broker = NewBroker(relay, 0.05);
            broker.Handle(Event("Stop", null, "stop_hook_active", false), TimeSpan.FromSeconds(60), new FakeChannel(HookAck.Taken));
            for (int i = 1; i < HookEvent.DefaultStopBlockCap; i++)
            {
                broker.Handle(Event("Stop", null, "stop_hook_active", true), TimeSpan.FromSeconds(60), new FakeChannel(HookAck.Taken));
            }
            Equal(HookEvent.DefaultStopBlockCap, relay.Sent("text_request").Count, "windows below cap");
            broker.Handle(Event("Stop", null, "stop_hook_active", true), TimeSpan.FromSeconds(60), new FakeChannel(HookAck.Taken));
            Equal(HookEvent.DefaultStopBlockCap, relay.Sent("text_request").Count, "window at cap");
            broker.Handle(Event("Stop", null, "stop_hook_active", false), TimeSpan.FromSeconds(60), new FakeChannel(HookAck.Taken));
            Equal(HookEvent.DefaultStopBlockCap + 1, relay.Sent("text_request").Count, "streak reset");
        }

        private static void ContinuationCapFromEnvironment()
        {
            var relay = new FakeRelay();
            Broker broker = NewBroker(relay, 0.05);
            broker.Handle(Event("Stop", null, "stop_hook_active", false, "CLAUDE_CODE_STOP_HOOK_BLOCK_CAP", "1"), TimeSpan.FromSeconds(60), new FakeChannel(HookAck.Taken));
            broker.Handle(Event("Stop", null, "stop_hook_active", true, "CLAUDE_CODE_STOP_HOOK_BLOCK_CAP", "1"), TimeSpan.FromSeconds(60), new FakeChannel(HookAck.Taken));
            Equal(1, relay.Sent("text_request").Count, "windows");
        }

        private static void HelloSendsSnapshotWithOpenRequest()
        {
            var relay = new FakeRelay();
            Broker broker = NewBroker(relay, 5);
            var channel = new FakeChannel(HookAck.Taken);
            Thread thread = Start(broker, Permission("ls", null), channel, 60);
            string id = WaitRequest(relay, "permission_request");
            broker.OnPhoneMessage(new Dictionary<string, object> { { "type", "hello" }, { "protocol", new object[] { 2L } }, { "capabilities", new object[0] } });
            Equal(1, relay.Sent("permission_request").Count, "request is not repeated, the snapshot carries it");
            Dictionary<string, object> snapshot = relay.Sent("snapshot")[0];
            var requests = (List<object>)snapshot["requests"];
            Equal(id, Json.String((Dictionary<string, object>)requests[0], "request_id"), "request in snapshot");
            Equal("phone", Json.String(snapshot, "mode"), "mode in snapshot");
            broker.SetMode(ControlMode.Pc);
            Finish(thread);
            Equal(0, relay.Problems.Count, "messages against valid rules: " + string.Join("; ", relay.Problems.ToArray()));
        }

        private static void CursorPartialLine()
        {
            string path = Path.Combine(Sandbox, "cursor.jsonl");
            File.WriteAllText(path, "{\"a\":1}\n");
            TranscriptCursor cursor = TranscriptCursor.AtEnd(path);
            Equal(0, cursor.ReadNew().Count, "nothing new");
            Append(path, "{\"b\":2}\n{\"c\":");
            List<string> first = cursor.ReadNew();
            Equal(1, first.Count, "complete lines");
            Equal("{\"b\":2}", first[0], "line");
            Append(path, "3}\r\n");
            List<string> second = cursor.ReadNew();
            Equal(1, second.Count, "joined line");
            Equal("{\"c\":3}", second[0], "joined text");
        }

        private static void SignalsScan()
        {
            TranscriptSignals none = TranscriptSignals.Scan(new[]
            {
                "{\"type\":\"queue-operation\",\"operation\":\"dequeue\"}",
                "{\"type\":\"api-request-blob\",\"message\":{\"content\":[{\"type\":\"text\",\"text\":\"[Request interrupted by user for tool use]\"}]}}",
                "not json user-rejected"
            });
            Check(!none.DeniedOnPc && !none.Enqueued, "no signals expected");
            TranscriptSignals interrupted = TranscriptSignals.Scan(new[]
            {
                "{\"type\":\"user\",\"message\":{\"role\":\"user\",\"content\":[{\"type\":\"text\",\"text\":\"[Request interrupted by user for tool use]\"}]}}"
            });
            Check(interrupted.DeniedOnPc, "interruption is a denial");
            TranscriptSignals queued = TranscriptSignals.Scan(new[] { "{\"type\":\"queue-operation\",\"operation\":\"enqueue\",\"content\":\"x\"}" });
            Check(queued.Enqueued && !queued.DeniedOnPc, "enqueue expected");
        }

        private static void ChannelLineProtocol()
        {
            var server = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
            server.Start();
            int port = ((System.Net.IPEndPoint)server.LocalEndpoint).Port;
            var client = new System.Net.Sockets.TcpClient();
            client.Connect(System.Net.IPAddress.Loopback, port);
            System.Net.Sockets.Socket accepted = server.AcceptSocket();
            server.Stop();
            using (client)
            using (accepted)
            {
                var line = new LineSocket(accepted);
                var channel = new HookChannel(line);
                byte[] hello = Encoding.UTF8.GetBytes("{\"x\":\"привет\"}\n");
                client.GetStream().Write(hello, 0, hello.Length);
                Equal("{\"x\":\"привет\"}", line.ReadLine(1000, 1024), "request line");
                Check(!channel.IsGone, "client is still there");
                var reader = new StreamReader(client.GetStream(), new UTF8Encoding(false));
                var replyThread = new Thread(() =>
                {
                    string reply = reader.ReadLine();
                    Check(Json.String(Json.ParseObject(reply), "output") == "{\"decision\":\"block\"}", "reply output");
                    byte[] taken = Encoding.UTF8.GetBytes("{\"taken\":true}\n");
                    client.GetStream().Write(taken, 0, taken.Length);
                });
                replyThread.Start();
                Equal(HookAck.Taken, channel.Deliver("{\"decision\":\"block\"}"), "ack");
                Check(replyThread.Join(2000), "reply thread finished");
                client.Close();
                Thread.Sleep(50);
                Check(channel.IsGone, "closed client is gone");
            }
        }
    }
}
