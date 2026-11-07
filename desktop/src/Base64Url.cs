using System;

namespace Pult
{
    internal static class Base64Url
    {
        public static string Encode(byte[] data)
        {
            return Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }

        public static byte[] Decode(string text)
        {
            if (text == null || text.Length % 4 == 1) return null;
            foreach (char c in text)
            {
                bool allowed = (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '-' || c == '_';
                if (!allowed) return null;
            }
            string padded = text.Replace('-', '+').Replace('_', '/');
            padded += new string('=', (4 - padded.Length % 4) % 4);
            byte[] data;
            try
            {
                data = Convert.FromBase64String(padded);
            }
            catch (FormatException)
            {
                return null;
            }
            return Encode(data) == text ? data : null;
        }

        public static byte[] Decode(string text, int length)
        {
            byte[] data = Decode(text);
            return data != null && data.Length == length ? data : null;
        }
    }
}
