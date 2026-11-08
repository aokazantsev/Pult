using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace Pult
{
    internal sealed class ControlCommand
    {
        public const string Status = "status";
        public const string Start = "start";
        public const string Exit = "exit";
        public const string Pair = "pair";
        public const string Pairing = "pairing";
        public const string NewQr = "new-qr";
        public const string ClosePairing = "close-pairing";
        public const string Unpair = "unpair";
        public const string Mode = "mode";
        public const string Settings = "settings";
        public const string Log = "log";
        public const string Help = "help";
        public const int MaxWaitSeconds = 600;
        public const int DefaultLogLines = 50;
        public const int MaxLogLines = 2000;
        private const int MaxFolders = 64;

        public string Name;
        public readonly List<string> Folders = new List<string>();
        public string Server;
        public int IdleSeconds;
        public int WaitSeconds;
        public ControlMode TargetMode;
        public string SettingKey;
        public string SettingValue;
        public int LogLines = DefaultLogLines;
        public Action AfterReply;

        public bool IsLocal
        {
            get { return Name == Start || Name == Settings || Name == Log || Name == Help; }
        }

        public static ControlCommand Parse(IList<string> args, string workingDirectory, out string problem)
        {
            problem = null;
            if (args == null || args.Count == 0)
            {
                problem = "no command";
                return null;
            }
            var command = new ControlCommand { Name = args[0] };
            var rest = new Queue<string>();
            for (int i = 1; i < args.Count; i++) rest.Enqueue(args[i] ?? "");
            switch (command.Name)
            {
                case Status:
                case Start:
                case Exit:
                case NewQr:
                case ClosePairing:
                case Unpair:
                case Help:
                    break;
                case Pair:
                    problem = command.ParsePair(rest, workingDirectory);
                    break;
                case Pairing:
                    problem = command.ParseOption(rest, "--wait", () => ReadNumber(rest, 0, MaxWaitSeconds, "--wait", out command.WaitSeconds));
                    break;
                case Mode:
                    problem = command.ParseMode(rest);
                    break;
                case Settings:
                    problem = command.ParseSettings(rest);
                    break;
                case Log:
                    problem = command.ParseOption(rest, "--lines", () => ReadNumber(rest, 1, MaxLogLines, "--lines", out command.LogLines));
                    break;
                default:
                    problem = "unknown command: " + command.Name;
                    return null;
            }
            if (problem == null && rest.Count > 0) problem = "unexpected argument: " + rest.Peek();
            return problem == null ? command : null;
        }

        private string ParseOption(Queue<string> rest, string option, Func<string> read)
        {
            while (rest.Count > 0)
            {
                if (rest.Peek() != option) return "unexpected argument: " + rest.Peek();
                rest.Dequeue();
                string problem = read();
                if (problem != null) return problem;
            }
            return null;
        }

        private string ParsePair(Queue<string> rest, string workingDirectory)
        {
            while (rest.Count > 0)
            {
                string option = rest.Dequeue();
                if (rest.Count == 0) return option + " needs a value";
                string value = rest.Dequeue();
                switch (option)
                {
                    case "--folder":
                        string folder = ResolveFolder(value, workingDirectory);
                        if (folder == null) return "--folder is not a folder path: " + value;
                        if (Folders.Count >= MaxFolders) return "too many folders, at most " + MaxFolders;
                        Folders.Add(folder);
                        break;
                    case "--server":
                        Server = value.Trim().TrimEnd('/');
                        if (Server.Length == 0) return "--server is empty";
                        break;
                    case "--idle-hours":
                        decimal hours;
                        if (!decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out hours)) return "--idle-hours is not a number: " + value;
                        decimal seconds = Math.Round(hours * 3600m);
                        if (seconds < QrPayload.MinIdleSeconds || seconds > QrPayload.MaxIdleSeconds) return "--idle-hours must be 0.25-36";
                        IdleSeconds = (int)seconds;
                        break;
                    default:
                        return "unexpected argument: " + option;
                }
            }
            return null;
        }

        private string ParseMode(Queue<string> rest)
        {
            if (rest.Count == 0) return "mode needs phone or pc";
            string value = rest.Dequeue();
            if (value == "phone") TargetMode = ControlMode.Phone;
            else if (value == "pc") TargetMode = ControlMode.Pc;
            else return "mode needs phone or pc, not " + value;
            return null;
        }

        private string ParseSettings(Queue<string> rest)
        {
            if (rest.Count == 0) return null;
            if (rest.Dequeue() != "set" || rest.Count < 2) return "settings set needs a key and a value";
            SettingKey = rest.Dequeue();
            SettingValue = rest.Dequeue();
            return null;
        }

        private static string ReadNumber(Queue<string> rest, int min, int max, string option, out int number)
        {
            number = 0;
            if (rest.Count == 0) return option + " needs a value";
            return ReadNumber(rest.Dequeue(), min, max, option, out number);
        }

        private static string ReadNumber(string value, int min, int max, string option, out int number)
        {
            if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out number) || number < min || number > max)
            {
                return option + " must be a whole number " + min + "-" + max;
            }
            return null;
        }

        private static string ResolveFolder(string value, string workingDirectory)
        {
            string trimmed = value.Trim();
            if (trimmed.Length == 0) return null;
            try
            {
                if (!Path.IsPathRooted(trimmed))
                {
                    if (string.IsNullOrEmpty(workingDirectory) || !Path.IsPathRooted(workingDirectory)) return null;
                    trimmed = Path.Combine(workingDirectory, trimmed);
                }
                string full = Path.GetFullPath(trimmed);
                string root = Path.GetPathRoot(full);
                return full.Length > root.Length ? full.TrimEnd('\\') : full;
            }
            catch (ArgumentException)
            {
                return null;
            }
            catch (NotSupportedException)
            {
                return null;
            }
            catch (PathTooLongException)
            {
                return null;
            }
        }
    }
}
