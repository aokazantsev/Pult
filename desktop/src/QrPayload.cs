using System.Globalization;
using System.Text;

namespace Pult
{
    internal static class QrPayload
    {
        public const int MinIdleSeconds = 900;
        public const int MaxIdleSeconds = 129600;
        public const int DefaultIdleSeconds = 14400;

        public static string Build(string server, string pair, byte[] secret, string pcSign, string pcAgree, int idleSeconds)
        {
            var text = new StringBuilder();
            text.Append("{\"v\":[").Append(Envelope.Version.ToString(CultureInfo.InvariantCulture)).Append("]");
            text.Append(",\"s\":").Append(Quote(server));
            text.Append(",\"c\":").Append(Quote(pair));
            text.Append(",\"k\":").Append(Quote(Base64Url.Encode(secret)));
            text.Append(",\"ps\":").Append(Quote(pcSign));
            text.Append(",\"pa\":").Append(Quote(pcAgree));
            text.Append(",\"n\":").Append(idleSeconds.ToString(CultureInfo.InvariantCulture)).Append('}');
            return text.ToString();
        }

        public static string Quote(string value)
        {
            var text = new StringBuilder("\"");
            foreach (char c in value)
            {
                if (c == '"' || c == '\\') text.Append('\\').Append(c);
                else if (c < 0x20) text.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                else text.Append(c);
            }
            return text.Append('"').ToString();
        }
    }
}
