using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace Pult
{
    internal sealed class AppSettings
    {
        public const int DefaultHookPort = 47652;
        private const string HookPortKey = "hook_port";

        public int HookPort = DefaultHookPort;

        public static string FilePath
        {
            get { return UserDataPaths.File("settings.txt"); }
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
                    string key = line.Substring(0, separator).Trim();
                    string value = line.Substring(separator + 1).Trim();
                    int port;
                    if (key == HookPortKey && int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out port) && port > 0 && port < 65536)
                    {
                        settings.HookPort = port;
                    }
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

        private void Save()
        {
            try
            {
                File.WriteAllText(FilePath, HookPortKey + "=" + HookPort.ToString(CultureInfo.InvariantCulture) + Environment.NewLine, new UTF8Encoding(false));
            }
            catch (IOException error)
            {
                AppLog.Append("settings not saved: " + error.Message);
            }
            catch (UnauthorizedAccessException error)
            {
                AppLog.Append("settings not saved: " + error.Message);
            }
        }
    }
}
