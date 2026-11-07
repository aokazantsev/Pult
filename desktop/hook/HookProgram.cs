using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Web.Script.Serialization;

namespace PultHook
{
    internal static class HookProgram
    {
        private const int DefaultHookPort = 47652;
        private const int DefaultLimitSeconds = 570;
        private const int ConnectTimeoutMs = 2000;
        private static readonly string[] ForwardedEnvironment = { "CLAUDE_CODE_STOP_HOOK_BLOCK_CAP", "CLAUDE_CODE_HOST_SESSION_ID" };

        private static int Main(string[] args)
        {
            try
            {
                Run(args);
            }
            catch (Exception error)
            {
                Note("hook failed: " + error.GetType().Name);
            }
            return 0;
        }

        private static void Run(string[] args)
        {
            DateTime started = DateTime.UtcNow;
            DateTime deadline = started.AddSeconds(LimitSeconds(args));
            string input = ReadStdin();
            var serializer = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
            input = input.TrimStart('﻿');
            if (string.IsNullOrWhiteSpace(input)) return;
            Dictionary<string, object> hookEvent;
            try
            {
                hookEvent = serializer.DeserializeObject(input) as Dictionary<string, object>;
            }
            catch (ArgumentException)
            {
                Note("hook input is not json");
                return;
            }
            if (hookEvent == null) return;
            string dataDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Pult");
            string secretPath = Path.Combine(dataDirectory, "hook-secret.txt");
            if (!File.Exists(secretPath)) return;
            string secret = File.ReadAllText(secretPath, Encoding.ASCII).Trim();
            int port = ReadPort(Path.Combine(dataDirectory, "settings.txt"));
            string expected = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Pult.exe");
            string owner = PortOwner.ImagePath(port);
            if (owner == null) return;
            if (!string.Equals(Path.GetFullPath(owner), Path.GetFullPath(expected), StringComparison.OrdinalIgnoreCase))
            {
                Note("port " + port + " is held by " + owner + ", event not sent");
                return;
            }
            var environment = new Dictionary<string, object>();
            foreach (string name in ForwardedEnvironment)
            {
                string value = Environment.GetEnvironmentVariable(name);
                if (value != null) environment[name] = value;
            }
            using (var client = new TcpClient(AddressFamily.InterNetwork))
            {
                IAsyncResult connecting = client.BeginConnect(IPAddress.Loopback, port, null, null);
                if (!connecting.AsyncWaitHandle.WaitOne(ConnectTimeoutMs)) return;
                client.EndConnect(connecting);
                NetworkStream stream = client.GetStream();
                var request = new Dictionary<string, object>
                {
                    { "secret", secret },
                    { "event", hookEvent },
                    { "env", environment },
                    { "limit_ms", (long)(deadline - DateTime.UtcNow).TotalMilliseconds }
                };
                WriteLine(stream, serializer.Serialize(request));
                int remaining = (int)Math.Max(1000, (deadline - DateTime.UtcNow).TotalMilliseconds);
                client.ReceiveTimeout = remaining;
                string replyLine = ReadLine(stream);
                if (replyLine == null) return;
                var reply = serializer.DeserializeObject(replyLine) as Dictionary<string, object>;
                if (reply == null) return;
                object outputValue;
                string output = reply.TryGetValue("output", out outputValue) ? outputValue as string : null;
                if (string.IsNullOrEmpty(output)) return;
                bool taken = DateTime.UtcNow < deadline;
                WriteLine(stream, taken ? "{\"taken\":true}" : "{\"taken\":false}");
                if (!taken) return;
                byte[] bytes = new UTF8Encoding(false).GetBytes(output);
                using (Stream stdout = Console.OpenStandardOutput())
                {
                    stdout.Write(bytes, 0, bytes.Length);
                    stdout.Flush();
                }
            }
        }

        private static int LimitSeconds(string[] args)
        {
            for (int i = 0; i + 1 < args.Length; i++)
            {
                int value;
                if (args[i] == "--limit" && int.TryParse(args[i + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out value) && value > 0) return value;
            }
            return DefaultLimitSeconds;
        }

        private static string ReadStdin()
        {
            using (Stream stdin = Console.OpenStandardInput())
            using (var buffer = new MemoryStream())
            {
                stdin.CopyTo(buffer);
                return new UTF8Encoding(false).GetString(buffer.ToArray());
            }
        }

        private static int ReadPort(string settingsPath)
        {
            if (!File.Exists(settingsPath)) return DefaultHookPort;
            foreach (string line in File.ReadAllLines(settingsPath, new UTF8Encoding(false)))
            {
                int separator = line.IndexOf('=');
                if (separator <= 0 || line.Substring(0, separator).Trim() != "hook_port") continue;
                int port;
                if (int.TryParse(line.Substring(separator + 1).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out port) && port > 0 && port < 65536) return port;
            }
            return DefaultHookPort;
        }

        private static void WriteLine(NetworkStream stream, string line)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(line + "\n");
            stream.Write(bytes, 0, bytes.Length);
            stream.Flush();
        }

        private static string ReadLine(NetworkStream stream)
        {
            var collected = new MemoryStream();
            var one = new byte[1];
            while (true)
            {
                int read = stream.Read(one, 0, 1);
                if (read <= 0) return null;
                if (one[0] == (byte)'\n') break;
                collected.WriteByte(one[0]);
            }
            return Encoding.UTF8.GetString(collected.ToArray()).TrimEnd('\r');
        }

        private static void Note(string text)
        {
            try
            {
                string directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Pult");
                if (!Directory.Exists(directory)) return;
                File.AppendAllText(Path.Combine(directory, "hook-log.txt"),
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + "  " + text + Environment.NewLine, new UTF8Encoding(false));
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
