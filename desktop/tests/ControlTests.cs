using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace Pult.Tests
{
    internal static class ControlTests
    {
        private static void Expect(bool c, string m) { MessageTests.Expect(c, m); }

        private static ControlCommand Parse(params string[] args)
        {
            string problem;
            ControlCommand command = ControlCommand.Parse(args, "D:\\work", out problem);
            Expect(command != null, "parsed: " + string.Join(" ", args) + ": " + problem);
            return command;
        }

        private static string Refusal(params string[] args)
        {
            string problem;
            ControlCommand command = ControlCommand.Parse(args, "D:\\work", out problem);
            Expect(command == null && problem != null, "refused: " + string.Join(" ", args));
            return problem;
        }

        public static void PlainCommands()
        {
            foreach (string name in new[] { "status", "start", "exit", "new-qr", "close-pairing", "unpair", "help", "settings", "log", "pairing" })
            {
                Expect(Parse(name).Name == name, name);
            }
            Expect(Parse("start").IsLocal && Parse("settings").IsLocal && Parse("log").IsLocal && Parse("help").IsLocal, "local commands");
            Expect(!Parse("status").IsLocal && !Parse("exit").IsLocal && !Parse("pair").IsLocal, "commands of the running Pult");
            Expect(Refusal().Contains("no command"), "no command");
            Expect(Refusal("bogus").Contains("unknown command"), "unknown command");
            Expect(Refusal("status", "x").Contains("unexpected argument"), "extra argument");
        }

        public static void PairOptions()
        {
            ControlCommand command = Parse("pair", "--folder", "C:\\lab\\", "--folder", "sub\\x", "--server", "https://example.com/pult/", "--idle-hours", "0.5");
            Expect(command.Folders.SequenceEqual(new[] { "C:\\lab", "D:\\work\\sub\\x" }), "folders are full paths: " + string.Join("|", command.Folders));
            Expect(command.Server == "https://example.com/pult", "server without the trailing slash");
            Expect(command.IdleSeconds == 1800, "idle in seconds");
            Expect(Parse("pair").Folders.Count == 0 && Parse("pair").IdleSeconds == 0 && Parse("pair").Server == null, "pair without options");
            Expect(Parse("pair", "--folder", "C:\\").Folders[0] == "C:\\", "drive root stays a root");
            foreach (string partial in new[] { "\\work\\lab", "/work/lab", "D:lab", "D:" })
            {
                Expect(Refusal("pair", "--folder", partial).Contains("not a folder path"), "a path relative to a drive or its root is refused: " + partial);
            }
            Expect(Parse("pair", "--folder", "\\\\server\\share\\lab").Folders[0] == "\\\\server\\share\\lab", "a network path is a full path");
            Expect(Refusal("pair", "--idle-hours", "0.2").Contains("0.25-36"), "idle below the minimum");
            Expect(Refusal("pair", "--idle-hours", "37").Contains("0.25-36"), "idle above the maximum");
            Expect(Refusal("pair", "--idle-hours", "x").Contains("not a number"), "idle not a number");
            Expect(Parse("pair", "--idle-hours", "0,5").IdleSeconds == 1800 && Parse("pair", "--idle-hours", "1,25").IdleSeconds == 4500, "a comma is the decimal separator, not thousands");
            Expect(Refusal("pair", "--idle-hours", "1,000.5") != null, "thousands are refused");
            Expect(Refusal("pair", "--idle-hours", "99999999999999999999999999").Contains("0.25-36"), "a huge number is a usage error, not an overflow");
            Expect(Parse("pair", "--idle-hours", "0.333").IdleSeconds == 1188, "hours are rounded to the window grid of 0.01 h");
            Expect(Parse("pair", "--idle-hours", "0.25").IdleSeconds == 900 && Parse("pair", "--idle-hours", "36").IdleSeconds == 129600, "bounds are accepted");
            Expect(Refusal("pair", "--server", "ftp://example.com").Contains("https://"), "server scheme is checked like settings set");
            Expect(Refusal("pair", "--server", "example.com").Contains("https://"), "server without a scheme is refused");
            Expect(Refusal("pair", "--server", "https://example.com/x\nhook_port=1").Contains("control characters"), "a line break in the server is refused");
            Expect(Refusal("pair", "--folder").Contains("needs a value"), "folder without value");
            Expect(Refusal("pair", "--folder", " ").Contains("not a folder"), "empty folder");
            Expect(Refusal("pair", "--server", " ").Contains("empty"), "empty server");
            Expect(Refusal("pair", "--wait", "3").Contains("unexpected argument"), "pair has no wait");
            string problem;
            Expect(ControlCommand.Parse(new[] { "pair", "--folder", "rel" }, null, out problem) == null, "relative folder needs a working folder");
        }

        public static void OtherOptions()
        {
            Expect(Parse("mode", "pc").TargetMode == ControlMode.Pc && Parse("mode", "phone").TargetMode == ControlMode.Phone, "mode");
            Expect(Refusal("mode").Contains("phone or pc") && Refusal("mode", "tv").Contains("phone or pc"), "bad mode");
            Expect(Parse("pairing", "--wait", "30").WaitSeconds == 30 && Parse("pairing").WaitSeconds == 0, "pairing wait");
            Expect(Refusal("pairing", "--wait", "-1").Contains("whole number") && Refusal("pairing", "--wait", "601").Contains("0-600"), "wait range");
            Expect(Parse("log", "--lines", "7").LogLines == 7 && Parse("log").LogLines == ControlCommand.DefaultLogLines, "log lines");
            Expect(Refusal("log", "--lines", "0").Contains("1-2000"), "log lines range");
            ControlCommand set = Parse("settings", "set", "wait_seconds", "60");
            Expect(set.SettingKey == "wait_seconds" && set.SettingValue == "60", "settings set");
            Expect(Refusal("settings", "get").Contains("settings set"), "settings without set");
            Expect(Refusal("settings", "set", "x").Contains("settings set"), "settings set without a value");
        }

        public static void SettingsAreValidated()
        {
            var settings = new AppSettings();
            Expect(settings.TrySet("hook_port", "0") != null && settings.TrySet("hook_port", "70000") != null && settings.TrySet("hook_port", "x") != null, "bad port");
            Expect(settings.TrySet("hook_port", "47000") == null && settings.HookPort == 47000, "port set");
            Expect(settings.TrySet("wait_seconds", "9") != null && settings.TrySet("wait_seconds", "541") != null, "wait out of range is refused, not clamped");
            Expect(settings.TrySet("wait_seconds", "60") == null && settings.WaitSeconds == 60, "wait set");
            Expect(settings.TrySet("choice_delivery", "2") != null && settings.TrySet("choice_delivery", "0") == null && !settings.ChoiceDelivery, "choice delivery");
            Expect(settings.TrySet("server", "ftp://example.com") != null, "server scheme");
            Expect(settings.TrySet("server", "https://example.com/pult/") == null && settings.Server == "https://example.com/pult", "server set");
            Expect(settings.TrySet("server", "") == null && settings.Server == "", "server cleared");
            Expect(settings.TrySet("server", "https://example.com/x\nhook_port=1").Contains("control characters") && settings.Server == "", "a line break cannot add a key to the file");
            Expect(settings.TrySet("pair", "x").Contains("unknown setting"), "unknown key");
        }

        public static void SettingsCommandWritesTheFile()
        {
            File.WriteAllText(AppSettings.FilePath, "hook_port=47652\r\nwait_seconds=120\r\nchoice_delivery=1\r\nserver=https://example.com/a\r\n", new UTF8Encoding(false));
            Dictionary<string, object> result = ControlClient.Execute(Parse("settings", "set", "wait_seconds", "200"), null, "unused", null);
            Expect(Json.Bool(result, "ok"), "set is done");
            AppSettings saved = AppSettings.Load();
            Expect(saved.WaitSeconds == 200 && saved.Server == "https://example.com/a" && saved.HookPort == 47652, "only the key changes");
            result = ControlClient.Execute(Parse("settings", "set", "wait_seconds", "1"), null, "unused", null);
            Expect(ControlResult.ExitCode(result) == ControlResult.ExitUsage && AppSettings.Load().WaitSeconds == 200, "bad value is a usage error and is not written");
            result = ControlClient.Execute(Parse("settings"), null, "unused", null);
            Expect((int)Json.Object(result, "settings")["wait_seconds"] == 200, "settings shows the file");
        }

        public static void SaveServerKeepsOtherKeys()
        {
            File.WriteAllText(AppSettings.FilePath, "hook_port=47652\r\nwait_seconds=77\r\nchoice_delivery=0\r\nserver=https://example.com/a\r\n", new UTF8Encoding(false));
            Expect(AppSettings.SaveServer("https://example.com/b"), "server saved");
            AppSettings saved = AppSettings.Load();
            Expect(saved.Server == "https://example.com/b" && saved.WaitSeconds == 77 && !saved.ChoiceDelivery, "a key changed on disk since start survives");
        }

        public static void LogTail()
        {
            AppLog.Append("tail one");
            AppLog.Append("tail two");
            Dictionary<string, object> result = ControlClient.Execute(Parse("log", "--lines", "2"), null, "unused", null);
            var lines = (List<string>)result["lines"];
            Expect(lines.Count == 2 && lines[0].EndsWith("tail one") && lines[1].EndsWith("tail two"), "last lines: " + string.Join("|", lines));
        }

        public static void HelpListsEveryCommand()
        {
            string help = Json.Write(ControlHelp.Describe());
            foreach (string name in new[] { "status", "start", "exit", "pair", "pairing", "new-qr", "close-pairing", "unpair", "mode", "settings", "log", "help" })
            {
                Expect(help.Contains("Pult.exe " + name), "help names " + name);
            }
        }

        public static void ExitCodes()
        {
            Expect(ControlResult.ExitCode(ControlResult.Ok()) == 0, "ok");
            Expect(ControlResult.ExitCode(ControlResult.Fail(ControlResult.Failed, "x")) == 1, "failed");
            Expect(ControlResult.ExitCode(ControlResult.Fail(ControlResult.Refused, "x")) == 1, "refused");
            Expect(ControlResult.ExitCode(ControlResult.Fail(ControlResult.Usage, "x")) == 2, "usage");
            Expect(ControlResult.ExitCode(ControlResult.Fail(ControlResult.NotRunning, "x")) == 3, "not running");
        }

        public static void PipeIsForTheUserOnly()
        {
            PipeSecurity security = ControlChannel.Security();
            Expect((SecurityIdentifier)security.GetOwner(typeof(SecurityIdentifier)) == ControlChannel.CurrentUser, "owner is the user");
            Expect(security.AreAccessRulesProtected, "no inherited rules");
            var rules = security.GetAccessRules(true, true, typeof(SecurityIdentifier)).Cast<PipeAccessRule>().ToList();
            Expect(rules.Count == 2, "two rules: " + rules.Count);
            Expect(rules.Any(r => r.AccessControlType == AccessControlType.Allow && (SecurityIdentifier)r.IdentityReference == ControlChannel.CurrentUser), "the user is allowed");
            Expect(rules.Any(r => r.AccessControlType == AccessControlType.Deny && ((SecurityIdentifier)r.IdentityReference).IsWellKnown(WellKnownSidType.NetworkSid)), "network logons are denied");
            Expect(rules.All(r => r.AccessControlType == AccessControlType.Deny || (SecurityIdentifier)r.IdentityReference == ControlChannel.CurrentUser), "nobody else is allowed");
            Expect(ControlChannel.PipeName.Contains(ControlChannel.CurrentUser.Value), "the name carries the user");
        }

        public static void RoundTrip()
        {
            string name = TestPipe();
            ControlCommand seen = null;
            using (var server = new ControlServer(name, command =>
            {
                seen = command;
                Dictionary<string, object> result = ControlResult.Ok();
                result["text"] = "привет";
                return result;
            }))
            {
                Expect(server.Start(), "server starts");
                Dictionary<string, object> answer = ControlClient.Send(name, new[] { "pair", "--folder", "lab" }, 10);
                Expect(Json.Bool(answer, "ok") && Json.String(answer, "text") == "привет", "answer comes back in UTF-8: " + Json.Write(answer));
                Expect(seen != null && seen.Name == "pair" && seen.Folders.Single() == Path.Combine(Environment.CurrentDirectory, "lab"), "relative folder resolves against the client folder");
                answer = ControlClient.Send(name, new[] { "status" }, 10);
                Expect(Json.Bool(answer, "ok") && seen.Name == "status", "the next request is served too");
            }
            Dictionary<string, object> after = ControlClient.Send(name, new[] { "status" }, 10);
            Expect(ControlResult.ExitCode(after) == ControlResult.ExitNotRunning, "a stopped server means not running: " + Json.Write(after));
        }

        public static void ServerRefusesBadRequests()
        {
            string name = TestPipe();
            int calls = 0;
            using (var server = new ControlServer(name, command =>
            {
                calls++;
                return ControlResult.Ok();
            }))
            {
                Expect(server.Start(), "server starts");
                Expect(Json.String(ControlClient.Send(name, new[] { "settings" }, 10), "error") == ControlResult.Usage, "a local command is not run by the server");
                Expect(Json.String(ControlClient.Send(name, new[] { "bogus" }, 10), "error") == ControlResult.Usage, "unknown command");
                Expect(Json.String(Raw(name, "not json"), "error") == ControlResult.Usage, "not json");
                Expect(Json.String(Raw(name, "{\"args\":[1]}"), "error") == ControlResult.Usage, "arguments must be strings");
                Expect(calls == 0, "the handler is not called");
            }
        }

        public static void HandlerFailureIsAnAnswer()
        {
            string name = TestPipe();
            foreach (Exception failure in new Exception[] { new InvalidDataException("boom"), new InvalidOperationException("boom"), new NullReferenceException("boom") })
            {
                Exception thrown = failure;
                using (var server = new ControlServer(name, command => { throw thrown; }))
                {
                    server.Start();
                    Dictionary<string, object> answer = ControlClient.Send(name, new[] { "status" }, 10);
                    Expect(Json.String(answer, "error") == ControlResult.Failed && Json.String(answer, "message") == "boom", thrown.GetType().Name + " is an answer: " + Json.Write(answer));
                    Expect(Json.String(ControlClient.Send(name, new[] { "status" }, 10), "error") == ControlResult.Failed, thrown.GetType().Name + ": the next request is answered too");
                }
            }
        }

        public static void AfterReplyRunsAfterTheAnswer()
        {
            string name = TestPipe();
            var ran = new ManualResetEvent(false);
            using (var server = new ControlServer(name, command =>
            {
                command.AfterReply = () => ran.Set();
                return ControlResult.Ok();
            }))
            {
                server.Start();
                Dictionary<string, object> answer = ControlClient.Send(name, new[] { "exit" }, 10);
                Expect(Json.Bool(answer, "ok"), "exit answered");
                Expect(ran.WaitOne(3000), "the action after the answer runs");
            }
        }

        public static void SlowAnswerTimesOut()
        {
            string name = TestPipe();
            var release = new ManualResetEvent(false);
            using (var server = new ControlServer(name, command =>
            {
                release.WaitOne(5000);
                return ControlResult.Ok();
            }))
            {
                server.Start();
                Dictionary<string, object> answer = ControlClient.Send(name, new[] { "status" }, 1);
                release.Set();
                Expect(Json.String(answer, "error") == ControlResult.Failed && Json.String(answer, "message").Contains("did not answer"), "timeout: " + Json.Write(answer));
            }
        }

        public static void ServerStopsOnForeignOwner()
        {
            string name = TestPipe();
            Expect(!new ControlServer(name, command => ControlResult.Ok(), pipe => false).Start(), "a pipe of another owner is not served");
            int checks = 0;
            using (var server = new ControlServer(name, command => ControlResult.Ok(), pipe => ++checks == 1))
            {
                Expect(server.Start(), "the first instance is ours");
                Expect(Json.Bool(ControlClient.Send(name, new[] { "status" }, 10), "ok"), "the connected request is served");
                Expect(ControlResult.ExitCode(ControlClient.Send(name, new[] { "status" }, 10)) == ControlResult.ExitNotRunning, "the next instance of another owner stops the channel");
                Thread.Sleep(2500);
                Expect(checks == 2, "a foreign owner is not retried: " + checks);
            }
        }

        public static void ServerRetriesATransientFailure()
        {
            string name = TestPipe();
            using (var server = new ControlServer(name, command => ControlResult.Ok()))
            {
                int creations = 0;
                server.CreateInstance = () =>
                {
                    if (++creations == 2) throw new IOException("busy");
                    return server.Create();
                };
                Expect(server.Start(), "server starts");
                Expect(Json.Bool(ControlClient.Send(name, new[] { "status" }, 10), "ok"), "first request");
                Thread.Sleep(1500);
                Expect(Json.Bool(ControlClient.Send(name, new[] { "status" }, 10), "ok"), "the channel comes back after a transient failure");
            }
        }

        public static void ServerRetriesAFailedFirstInstance()
        {
            string name = TestPipe();
            using (var server = new ControlServer(name, command => ControlResult.Ok()))
            {
                int creations = 0;
                server.CreateInstance = () =>
                {
                    if (++creations <= 3) throw new IOException("busy");
                    return server.Create();
                };
                Expect(server.Start(), "a transient failure of the first instance is retried, not given up");
                Thread.Sleep(3500);
                Expect(Json.Bool(ControlClient.Send(name, new[] { "status" }, 10), "ok"), "the channel comes up after the retry");
            }
        }

        public static void ServerStopsRetryingOnForeignOwner()
        {
            string name = TestPipe();
            int checks = 0;
            using (var server = new ControlServer(name, command => ControlResult.Ok(), pipe =>
            {
                checks++;
                return false;
            }))
            {
                int creations = 0;
                server.CreateInstance = () =>
                {
                    if (++creations == 1) throw new IOException("busy");
                    return server.Create();
                };
                Expect(server.Start(), "a transient failure is retried");
                Thread.Sleep(3500);
                Expect(checks == 1, "a foreign owner met while retrying stops the retries: " + checks);
                Expect(ControlResult.ExitCode(ControlClient.Send(name, new[] { "status" }, 10)) == ControlResult.ExitNotRunning, "the channel is down");
            }
        }

        public static void ClientRefusesForeignOwner()
        {
            string name = TestPipe();
            int calls = 0;
            using (var server = new ControlServer(name, command =>
            {
                calls++;
                return ControlResult.Ok();
            }))
            {
                server.Start();
                Dictionary<string, object> answer = ControlClient.Send(name, new[] { "status" }, 10, pipe => false);
                Expect(Json.String(answer, "error") == ControlResult.Refused && calls == 0, "the client does not talk to a pipe of another owner: " + Json.Write(answer));
            }
        }

        public static void RequestsAreServedInParallel()
        {
            string name = TestPipe();
            var release = new ManualResetEvent(false);
            using (var server = new ControlServer(name, command =>
            {
                if (command.Name == ControlCommand.Pairing) release.WaitOne(10000);
                return ControlResult.Ok();
            }))
            {
                server.Start();
                Dictionary<string, object> waiting = null;
                var slow = new Thread(() => waiting = ControlClient.Send(name, new[] { "pairing", "--wait", "5" }, 20));
                slow.Start();
                Thread.Sleep(300);
                var clock = System.Diagnostics.Stopwatch.StartNew();
                Dictionary<string, object> quick = ControlClient.Send(name, new[] { "status" }, 3);
                Expect(Json.Bool(quick, "ok") && clock.ElapsedMilliseconds < 3000, "status is answered while pairing waits: " + Json.Write(quick));
                release.Set();
                slow.Join(10000);
                Expect(Json.Bool(waiting, "ok"), "the waiting request is answered too");
                for (int i = 0; i < 20; i++)
                {
                    Expect(Json.Bool(ControlClient.Send(name, new[] { "status" }, 5), "ok"), "request " + i + " in a row is served");
                }
            }
        }

        public static void AfterReplyWaitsForTheAnswer()
        {
            string name = TestPipe();
            var answered = new ManualResetEvent(false);
            var done = new ManualResetEvent(false);
            bool sawAnswer = false;
            using (var server = new ControlServer(name, command =>
            {
                command.AfterReply = () =>
                {
                    sawAnswer = answered.WaitOne(3000);
                    done.Set();
                };
                return ControlResult.Ok();
            }))
            {
                server.Start();
                Expect(Json.Bool(ControlClient.Send(name, new[] { "exit" }, 10), "ok"), "exit answered");
                answered.Set();
                Expect(done.WaitOne(5000) && sawAnswer, "the action runs after the client has the answer");
            }
        }

        public static void ExitWaitsForTheProcess()
        {
            string name = TestPipe();
            var start = new System.Diagnostics.ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "PING.EXE"), "-n 3 127.0.0.1") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true };
            using (System.Diagnostics.Process child = System.Diagnostics.Process.Start(start))
            using (var server = new ControlServer(name, command =>
            {
                Dictionary<string, object> result = ControlResult.Ok();
                result["pid"] = child.Id;
                return result;
            }))
            {
                server.Start();
                Dictionary<string, object> answer = ControlClient.Execute(Parse("exit"), new[] { "exit" }, name, null);
                Expect(Json.Bool(answer, "ok") && child.HasExited, "exit returns after the process is gone: " + Json.Write(answer));
                var clock = System.Diagnostics.Stopwatch.StartNew();
                Expect(Json.Bool(ControlClient.Execute(Parse("exit"), new[] { "exit" }, name, null), "ok") && clock.ElapsedMilliseconds < 3000, "a process already gone is done at once");
            }
        }

        public static void StartLeavesARunningPult()
        {
            string name = TestPipe();
            using (var server = new ControlServer(name, command => ControlResult.Ok()))
            {
                server.Start();
                Dictionary<string, object> answer = ControlClient.Execute(Parse("start"), null, name, "C:\\no-such-folder\\Pult.exe");
                Expect(Json.Bool(answer, "ok") && Json.Value(answer, "started") is bool && !(bool)answer["started"], "a running Pult is not started again: " + Json.Write(answer));
            }
            Dictionary<string, object> missing = ControlClient.Execute(Parse("start"), null, name, "C:\\no-such-folder\\Pult.exe");
            Expect(Json.String(missing, "error") == ControlResult.Failed && Json.String(missing, "message").Contains("not started"), "a missing exe is reported: " + Json.Write(missing));
        }

        public static void ClientWaitsLongerThanTheServer()
        {
            Expect(ControlClient.AnswerSeconds(Parse("pairing", "--wait", "600")) > 600, "pairing --wait");
            Expect(ControlClient.AnswerSeconds(Parse("pair")) > TrayApplication.PairPatienceSeconds, "pair");
            Expect(ControlClient.AnswerSeconds(Parse("new-qr")) > TrayApplication.PairPatienceSeconds, "new-qr");
            Expect(ControlClient.AnswerSeconds(Parse("unpair")) > TrayApplication.UnpairPatienceSeconds, "unpair");
        }

        public static void SaveServerKeepsAnUnreadableFile()
        {
            File.WriteAllText(AppSettings.FilePath, "hook_port=47652\r\nwait_seconds=77\r\nchoice_delivery=1\r\nserver=https://example.com/a\r\n", new UTF8Encoding(false));
            var deny = new FileSystemAccessRule(ControlChannel.CurrentUser, FileSystemRights.ReadData, AccessControlType.Deny);
            FileSecurity security = File.GetAccessControl(AppSettings.FilePath);
            security.AddAccessRule(deny);
            File.SetAccessControl(AppSettings.FilePath, security);
            bool saved;
            try
            {
                saved = AppSettings.SaveServer("https://example.com/b");
            }
            finally
            {
                security = File.GetAccessControl(AppSettings.FilePath);
                security.RemoveAccessRule(deny);
                File.SetAccessControl(AppSettings.FilePath, security);
            }
            AppSettings after = AppSettings.Load();
            Expect(!saved && after.WaitSeconds == 77 && after.Server == "https://example.com/a", "an unreadable file is not overwritten with defaults");
        }

        public static void CommandWindowsAreNotTheUsers()
        {
            Expect(!TrayApplication.OpenedByUser(null) && TrayApplication.CommandWindowRefusal(null) != null, "no window");
            var form = new PairingForm(new AppSettings(), null, new DesktopSessions(Path.Combine(UserDataPaths.Directory, "no-desktop-sessions"), TimeSpan.Zero), new PairRequest());
            Expect(!TrayApplication.OpenedByUser(form) && TrayApplication.CommandWindowRefusal(form) == null, "a hidden window belongs to commands");
            form.Opacity = 0;
            form.ShowInTaskbar = false;
            form.Show();
            Expect(TrayApplication.OpenedByUser(form) && Json.String(TrayApplication.CommandWindowRefusal(form), "message").Contains("opened by the user"), "a window on the screen is the user's");
            form.Dispose();
            Expect(!TrayApplication.OpenedByUser(form) && Json.String(TrayApplication.CommandWindowRefusal(form), "message").Contains("no pairing window"), "a closed window is no window");
        }

        public static void UnpairIsRefusedWhileExiting()
        {
            object pair = new object();
            Expect(TrayApplication.CanUnpair(pair, pair, false, false), "a pair can be broken");
            Expect(!TrayApplication.CanUnpair(null, null, false, false), "no pair");
            Expect(!TrayApplication.CanUnpair(pair, new object(), false, false), "the pair asked about was replaced while the question was open");
            Expect(!TrayApplication.CanUnpair(pair, pair, true, false), "already deleting");
            Expect(!TrayApplication.CanUnpair(pair, pair, false, true), "Pult is exiting");
        }

        public static void PairCommandFillsTheWindow()
        {
            PairRequest request = PairRequest.From(Parse("pair", "--server", "https://example.com/x", "--idle-hours", "0.5", "--folder", "C:\\a"));
            using (var form = new PairingForm(new AppSettings(), null, new DesktopSessions(Path.Combine(UserDataPaths.Directory, "no-desktop-sessions"), TimeSpan.Zero), request))
            {
                Dictionary<string, object> state = form.Describe();
                Expect((int)state["idle_seconds"] == 1800 && (string)state["server"] == "https://example.com/x", "idle and server reach the window");
                Expect(((List<string>)state["folders"]).SequenceEqual(new[] { "C:\\a" }), "folders reach the window");
            }
        }

        public static void QrAnswerFollowsTheWindow()
        {
            var showing = new Dictionary<string, object> { { "state", PairingForm.Showing } };
            Expect(Json.Bool(TrayApplication.QrAnswer(showing), "ok") && TrayApplication.QrAnswer(showing)["pairing"] == showing, "showing is the QR");
            var failed = new Dictionary<string, object> { { "state", PairingForm.Failed }, { "problem", "Сервер недоступен" } };
            Dictionary<string, object> answer = TrayApplication.QrAnswer(failed);
            Expect(Json.String(answer, "error") == ControlResult.Failed && Json.String(answer, "message") == "Сервер недоступен" && answer["pairing"] == failed, "failed carries the problem");
            Expect(Json.String(TrayApplication.QrAnswer(new Dictionary<string, object> { { "state", PairingForm.Creating } }), "message").Contains("no QR in 60 s"), "still creating");
            Expect(Json.String(TrayApplication.QrAnswer(null), "message") == "the pairing window is closed", "closed");
            Expect(Json.String(TrayApplication.QrAnswer(new Dictionary<string, object> { { "state", PairingForm.Paired } }), "message") == "the pairing window is paired", "paired");
        }

        public static void PipeHeldByStrangerIsRefused()
        {
            string name = TestPipe();
            var security = new PipeSecurity();
            security.AddAccessRule(new PipeAccessRule(ControlChannel.CurrentUser, PipeAccessRights.FullControl, AccessControlType.Allow));
            security.SetOwner(new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null));
            NamedPipeServerStream stranger;
            try
            {
                stranger = new NamedPipeServerStream(name, PipeDirection.InOut, 4, PipeTransmissionMode.Byte, PipeOptions.None, 4096, 4096, security);
            }
            catch (Exception)
            {
                Console.WriteLine("      (skipped: the owner cannot be another principal here)");
                return;
            }
            using (stranger)
            {
                Expect(!new ControlServer(name, command => ControlResult.Ok()).Start(), "the server does not join a pipe owned by another principal");
                Dictionary<string, object> answer = null;
                var client = new Thread(() => answer = ControlClient.Send(name, new[] { "status" }, 2));
                client.Start();
                stranger.WaitForConnection();
                client.Join(5000);
                Expect(Json.String(answer, "error") == ControlResult.Refused, "the client refuses a pipe owned by another principal: " + Json.Write(answer));
            }
        }

        private static Dictionary<string, object> Raw(string name, string line)
        {
            using (var pipe = new NamedPipeClientStream(".", name, PipeDirection.InOut))
            {
                pipe.Connect(2000);
                ControlChannel.WriteLine(pipe, line);
                return Json.TryParseObject(ControlChannel.ReadLine(pipe));
            }
        }

        private static string TestPipe()
        {
            return "Pult.Test." + Guid.NewGuid().ToString("N");
        }
    }
}
