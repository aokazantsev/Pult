using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace Pult
{
    internal sealed class AppSettings
    {
        public const int DefaultHookPort = 47652;
        public const int DefaultWaitSeconds = 300;
        public const int MinWaitSeconds = 10;
        public const int MaxWaitSeconds = 540;
        private const string HookPortKey = "hook_port";
        private const string ServerKey = "server";
        private const string LegacyPairKey = "pair";
        private const string LegacyTokenKey = "pc_token";
        private const string WaitKey = "wait_seconds";
        private const string ChoiceKey = "choice_delivery";

        public int HookPort = DefaultHookPort;
        public string Server = "";
        public int WaitSeconds = DefaultWaitSeconds;
        public bool ChoiceDelivery = true;

        public static string FilePath
        {
            get { return UserDataPaths.File("settings.txt"); }
        }

        private bool hadLegacyPair;

        public bool ServerConfigured
        {
            get { return Server.Length > 0; }
        }

        public static AppSettings Load()
        {
            var settings = new AppSettings();
            try
            {
                if (!File.Exists(FilePath))
                {
                    settings.Save();
                    return settings;
                }
                foreach (string line in File.ReadAllLines(FilePath, new UTF8Encoding(false)))
                {
                    int separator = line.IndexOf('=');
                    if (separator <= 0) continue;
                    settings.Apply(line.Substring(0, separator).Trim(), line.Substring(separator + 1).Trim());
                }
                if (settings.hadLegacyPair)
                {
                    AppLog.Append("settings: stage 1 pair (pair, pc_token) removed, pairing is done by QR now");
                    settings.Save();
                }
            }
            catch (IOException error)
            {
                AppLog.Append("settings not read, defaults used: " + error.Message);
            }
            catch (UnauthorizedAccessException error)
            {
                AppLog.Append("settings not read, defaults used: " + error.Message);
            }
            return settings;
        }

        public string Describe()
        {
            return "hook port=" + HookPort + ", wait=" + WaitSeconds + " s, choice delivery=" + (ChoiceDelivery ? "on" : "off") + ", server=" + (ServerConfigured ? Server : "not configured");
        }

        private void Apply(string key, string value)
        {
            int number;
            bool isNumber = int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out number);
            switch (key)
            {
                case HookPortKey:
                    if (isNumber && number > 0 && number < 65536) HookPort = number;
                    return;
                case WaitKey:
                    if (isNumber) WaitSeconds = Math.Max(MinWaitSeconds, Math.Min(MaxWaitSeconds, number));
                    return;
                case ChoiceKey:
                    if (isNumber) ChoiceDelivery = number != 0;
                    return;
                case ServerKey:
                    Server = value;
                    return;
                case LegacyPairKey:
                case LegacyTokenKey:
                    hadLegacyPair = true;
                    return;
            }
        }

        public bool Save()
        {
            var text = new StringBuilder();
            text.Append(HookPortKey).Append('=').Append(HookPort.ToString(CultureInfo.InvariantCulture)).Append(Environment.NewLine);
            text.Append(WaitKey).Append('=').Append(WaitSeconds.ToString(CultureInfo.InvariantCulture)).Append(Environment.NewLine);
            text.Append(ChoiceKey).Append('=').Append(ChoiceDelivery ? "1" : "0").Append(Environment.NewLine);
            text.Append(ServerKey).Append('=').Append(Server).Append(Environment.NewLine);
            try
            {
                File.WriteAllText(FilePath, text.ToString(), new UTF8Encoding(false));
                return true;
            }
            catch (IOException error)
            {
                AppLog.Append("settings not saved: " + error.Message);
                return false;
            }
            catch (UnauthorizedAccessException error)
            {
                AppLog.Append("settings not saved: " + error.Message);
                return false;
            }
        }
    }
}
