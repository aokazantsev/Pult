using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace Pult
{
    internal sealed class PairRecord
    {
        public const string PairFile = "pair.txt";
        public const string ClaimFile = "claim.txt";

        public string Server;
        public string Pair;
        public string SignKey;
        public string AgreeKey;
        public string PhoneSign;
        public string PhoneAgree;
        public int IdleSeconds;

        public static PairRecord Load(string path)
        {
            try
            {
                if (!File.Exists(path)) return null;
                var values = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (string line in File.ReadAllLines(path, new UTF8Encoding(false)))
                {
                    int separator = line.IndexOf('=');
                    if (separator > 0) values[line.Substring(0, separator)] = line.Substring(separator + 1).Trim();
                }
                var record = new PairRecord
                {
                    Server = Get(values, "server"),
                    Pair = Get(values, "pair"),
                    SignKey = Get(values, "sign_key"),
                    AgreeKey = Get(values, "agree_key"),
                    PhoneSign = Get(values, "phone_sign"),
                    PhoneAgree = Get(values, "phone_agree")
                };
                int idle;
                if (int.TryParse(Get(values, "idle_seconds"), NumberStyles.None, CultureInfo.InvariantCulture, out idle)) record.IdleSeconds = idle;
                if (string.IsNullOrEmpty(record.Server) || !StrictJson.IsIdentifier(record.Pair) || string.IsNullOrEmpty(record.SignKey) || string.IsNullOrEmpty(record.AgreeKey))
                {
                    AppLog.Append(Path.GetFileName(path) + " is incomplete, ignored");
                    return null;
                }
                return record;
            }
            catch (IOException error)
            {
                AppLog.Append(Path.GetFileName(path) + " not read: " + error.Message);
                return null;
            }
            catch (UnauthorizedAccessException error)
            {
                AppLog.Append(Path.GetFileName(path) + " not read: " + error.Message);
                return null;
            }
        }

        public bool IsActive
        {
            get { return P256.ParsePoint(PhoneSign) != null && P256.ParsePoint(PhoneAgree) != null; }
        }

        public bool Save(string path)
        {
            var text = new StringBuilder();
            Line(text, "server", Server);
            Line(text, "pair", Pair);
            Line(text, "sign_key", SignKey);
            Line(text, "agree_key", AgreeKey);
            if (PhoneSign != null) Line(text, "phone_sign", PhoneSign);
            if (PhoneAgree != null) Line(text, "phone_agree", PhoneAgree);
            if (IdleSeconds > 0) Line(text, "idle_seconds", IdleSeconds.ToString(CultureInfo.InvariantCulture));
            string temp = path + ".tmp";
            try
            {
                File.WriteAllText(temp, text.ToString(), new UTF8Encoding(false));
                if (File.Exists(path)) File.Replace(temp, path, null);
                else File.Move(temp, path);
                return true;
            }
            catch (IOException error)
            {
                AppLog.Append(Path.GetFileName(path) + " not saved: " + error.Message);
                return false;
            }
            catch (UnauthorizedAccessException error)
            {
                AppLog.Append(Path.GetFileName(path) + " not saved: " + error.Message);
                return false;
            }
        }

        public static void Delete(string path)
        {
            try
            {
                if (File.Exists(path)) File.Delete(path);
            }
            catch (IOException error)
            {
                AppLog.Append(Path.GetFileName(path) + " not deleted: " + error.Message);
            }
            catch (UnauthorizedAccessException error)
            {
                AppLog.Append(Path.GetFileName(path) + " not deleted: " + error.Message);
            }
        }

        private static string Get(Dictionary<string, string> values, string key)
        {
            string value;
            return values.TryGetValue(key, out value) ? value : null;
        }

        private static void Line(StringBuilder text, string key, string value)
        {
            text.Append(key).Append('=').Append(value).Append('\n');
        }
    }
}
