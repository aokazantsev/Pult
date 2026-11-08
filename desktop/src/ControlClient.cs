using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;

namespace Pult
{
    internal static class ControlClient
    {
        private const int ConnectTimeoutMs = 1000;
        private const int DefaultAnswerSeconds = 30;
        private const int PairAnswerSeconds = 90;
        private const int StartPatienceMs = 20000;
        private const int StartPollMs = 300;
        private const int ExitPatienceMs = 15000;

        public static int Run(string[] args, Stream output, string executable)
        {
            string problem;
            ControlCommand command = ControlCommand.Parse(args, Environment.CurrentDirectory, out problem);
            Dictionary<string, object> result = command == null
                ? ControlResult.Fail(ControlResult.Usage, problem + "; Pult.exe help lists the commands")
                : Execute(command, args, ControlChannel.PipeName, executable);
            ControlChannel.WriteLine(output, Json.Write(result));
            return ControlResult.ExitCode(result);
        }

        public static Dictionary<string, object> Execute(ControlCommand command, IList<string> args, string pipeName, string executable)
        {
            switch (command.Name)
            {
                case ControlCommand.Help: return ControlHelp.Describe();
                case ControlCommand.Settings: return SettingsCommand(command);
                case ControlCommand.Log: return LogTail(command.LogLines);
                case ControlCommand.Start: return StartPult(pipeName, executable);
                case ControlCommand.Exit: return ExitPult(args, pipeName);
                default: return Send(pipeName, args, AnswerSeconds(command));
            }
        }

        public static Dictionary<string, object> Send(string pipeName, IList<string> args, int answerSeconds)
        {
            return Send(pipeName, args, answerSeconds, ControlChannel.OwnedByCurrentUser);
        }

        public static Dictionary<string, object> Send(string pipeName, IList<string> args, int answerSeconds, Func<PipeStream, bool> ownedByUser)
        {
            var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut);
            try
            {
                try
                {
                    pipe.Connect(ConnectTimeoutMs);
                }
                catch (TimeoutException)
                {
                    return ControlResult.Fail(ControlResult.NotRunning, "Pult is not running: Pult.exe start launches it");
                }
                catch (IOException error)
                {
                    return ControlResult.Fail(ControlResult.NotRunning, "Pult does not answer: " + error.Message);
                }
                catch (UnauthorizedAccessException)
                {
                    return ControlResult.Fail(ControlResult.Refused, "the control channel denies access: it belongs to another user or to an elevated Pult");
                }
                if (!ownedByUser(pipe)) return ControlResult.Fail(ControlResult.Refused, "the control channel belongs to another user");
                var request = new Dictionary<string, object> { { "args", new List<string>(args) }, { "cwd", Environment.CurrentDirectory } };
                string answer = null;
                Exception failure = null;
                var reader = new Thread(() =>
                {
                    try
                    {
                        ControlChannel.WriteLine(pipe, Json.Write(request));
                        answer = ControlChannel.ReadLine(pipe);
                    }
                    catch (Exception error)
                    {
                        failure = error;
                    }
                }) { IsBackground = true };
                reader.Start();
                if (!reader.Join(TimeSpan.FromSeconds(answerSeconds)))
                {
                    pipe.Dispose();
                    return ControlResult.Fail(ControlResult.Failed, "Pult did not answer in " + answerSeconds + " s");
                }
                if (failure != null) return ControlResult.Fail(ControlResult.Failed, "control channel broke: " + failure.Message);
                Dictionary<string, object> result = answer == null ? null : Json.TryParseObject(answer);
                return result ?? ControlResult.Fail(ControlResult.Failed, "Pult closed the channel without an answer");
            }
            finally
            {
                pipe.Dispose();
            }
        }

        public static int AnswerSeconds(ControlCommand command)
        {
            switch (command.Name)
            {
                case ControlCommand.Pair:
                case ControlCommand.NewQr:
                case ControlCommand.Unpair:
                    return PairAnswerSeconds;
                case ControlCommand.Pairing:
                    return command.WaitSeconds + DefaultAnswerSeconds;
                default:
                    return DefaultAnswerSeconds;
            }
        }

        private static Dictionary<string, object> StartPult(string pipeName, string executable)
        {
            var status = new List<string> { ControlCommand.Status };
            Dictionary<string, object> result = Send(pipeName, status, DefaultAnswerSeconds);
            if (Json.String(result, "error") != ControlResult.NotRunning)
            {
                result["started"] = false;
                return result;
            }
            try
            {
                Process.Start(new ProcessStartInfo(executable) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(executable) });
            }
            catch (Exception error)
            {
                return ControlResult.Fail(ControlResult.Failed, "Pult not started: " + error.Message);
            }
            var clock = Stopwatch.StartNew();
            while (clock.ElapsedMilliseconds < StartPatienceMs)
            {
                Thread.Sleep(StartPollMs);
                result = Send(pipeName, status, DefaultAnswerSeconds);
                if (Json.String(result, "error") == ControlResult.NotRunning) continue;
                result["started"] = true;
                return result;
            }
            return ControlResult.Fail(ControlResult.Failed, "Pult started, but its control channel did not answer in " + StartPatienceMs / 1000 + " s: see " + AppLog.FilePath);
        }

        private static Dictionary<string, object> ExitPult(IList<string> args, string pipeName)
        {
            Dictionary<string, object> result = Send(pipeName, args, DefaultAnswerSeconds);
            if (!Json.Bool(result, "ok")) return result;
            object pid = Json.Value(result, "pid");
            if (!(pid is int)) return result;
            try
            {
                using (Process pult = Process.GetProcessById((int)pid))
                {
                    if (pult.WaitForExit(ExitPatienceMs)) return result;
                }
            }
            catch (ArgumentException)
            {
                return result;
            }
            catch (InvalidOperationException)
            {
                return result;
            }
            catch (System.ComponentModel.Win32Exception error)
            {
                return ControlResult.Fail(ControlResult.Failed, "Pult accepted exit, its process is not watched: " + error.Message);
            }
            return ControlResult.Fail(ControlResult.Failed, "Pult accepted exit, but its process is still running after " + ExitPatienceMs / 1000 + " s");
        }

        private static Dictionary<string, object> SettingsCommand(ControlCommand command)
        {
            AppSettings settings = AppSettings.Load();
            if (settings.ReadFailed) return ControlResult.Fail(ControlResult.Failed, "settings not read: " + AppSettings.FilePath);
            if (command.SettingKey != null)
            {
                string problem = settings.TrySet(command.SettingKey, command.SettingValue);
                if (problem != null) return ControlResult.Fail(ControlResult.Usage, problem);
                if (!settings.Save()) return ControlResult.Fail(ControlResult.Failed, "settings not saved: " + AppSettings.FilePath);
            }
            Dictionary<string, object> result = ControlResult.Ok();
            result["path"] = AppSettings.FilePath;
            result["settings"] = settings.ToMap();
            result["applies"] = "server: the next pairing window; hook_port: hooks use it at once, Pult after a restart, so restart Pult right away (exit, start); the rest: after Pult restarts";
            return result;
        }

        private static Dictionary<string, object> LogTail(int count)
        {
            var lines = new Queue<string>();
            try
            {
                using (var stream = new FileStream(AppLog.FilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                using (var reader = new StreamReader(stream, new UTF8Encoding(false)))
                {
                    string line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        lines.Enqueue(line);
                        if (lines.Count > count) lines.Dequeue();
                    }
                }
            }
            catch (FileNotFoundException)
            {
            }
            catch (DirectoryNotFoundException)
            {
            }
            catch (IOException error)
            {
                return ControlResult.Fail(ControlResult.Failed, "log not read: " + error.Message);
            }
            catch (UnauthorizedAccessException error)
            {
                return ControlResult.Fail(ControlResult.Failed, "log not read: " + error.Message);
            }
            Dictionary<string, object> result = ControlResult.Ok();
            result["path"] = AppLog.FilePath;
            result["lines"] = new List<string>(lines);
            return result;
        }
    }
}
