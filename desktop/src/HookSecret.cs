using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Pult
{
    internal static class HookSecret
    {
        public const string FileName = "hook-secret.txt";

        public static string FilePath
        {
            get { return UserDataPaths.File(FileName); }
        }

        public static string LoadOrCreate()
        {
            try
            {
                if (File.Exists(FilePath))
                {
                    string existing = File.ReadAllText(FilePath, Encoding.ASCII).Trim();
                    if (existing.Length >= 32) return existing;
                }
                var bytes = new byte[32];
                using (var random = RandomNumberGenerator.Create()) random.GetBytes(bytes);
                string secret = BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant();
                File.WriteAllText(FilePath, secret, Encoding.ASCII);
                AppLog.Append("hook secret created");
                return secret;
            }
            catch (IOException error)
            {
                AppLog.Append("hook secret not available: " + error.Message);
                return null;
            }
            catch (UnauthorizedAccessException error)
            {
                AppLog.Append("hook secret not available: " + error.Message);
                return null;
            }
        }

        public static bool Matches(string expected, string given)
        {
            if (expected == null || given == null) return false;
            int difference = expected.Length ^ given.Length;
            for (int i = 0; i < expected.Length && i < given.Length; i++) difference |= expected[i] ^ given[i];
            return difference == 0;
        }
    }
}
