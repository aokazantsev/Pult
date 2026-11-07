using System.Globalization;

namespace Pult
{
    internal static class SignedStrings
    {
        public static string Login(string pair, string from, string challenge)
        {
            return string.Join("\n", "pult-v2-login", pair, from, challenge);
        }

        public static string Post(string op, string pair, string from, string stream, long n, byte[] body)
        {
            return string.Join("\n", "pult-v2-post", op, pair, from, stream, n.ToString(CultureInfo.InvariantCulture), BodyHash(body));
        }

        public static string BodyHash(byte[] body)
        {
            return Base64Url.Encode(PairCrypto.Sha256(body));
        }
    }
}
