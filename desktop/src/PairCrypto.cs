using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Pult
{
    internal static class PairCrypto
    {
        public const int SecretLength = 32;
        private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false);

        public static string Transcript(string pair, string pcSign, string pcAgree, string phoneSign, string phoneAgree)
        {
            return string.Join("\n", pair, pcSign, pcAgree, phoneSign, phoneAgree);
        }

        public static byte[] ClaimTag(byte[] secret, string transcript)
        {
            using (var hmac = new HMACSHA256(secret))
            {
                return hmac.ComputeHash(Utf8.GetBytes("pult-v2-claim\n" + transcript));
            }
        }

        public static bool FixedTimeEquals(byte[] left, byte[] right)
        {
            if (left == null || right == null || left.Length != right.Length) return false;
            int difference = 0;
            for (int i = 0; i < left.Length; i++) difference |= left[i] ^ right[i];
            return difference == 0;
        }

        public static string Fingerprint(string transcript)
        {
            byte[] hash = Sha256(Utf8.GetBytes("pult-v2-fingerprint\n" + transcript));
            ulong head = 0;
            for (int i = 0; i < 8; i++) head = (head << 8) | hash[i];
            string digits = (head % 10000000000000000UL).ToString("D16", CultureInfo.InvariantCulture);
            return digits.Substring(0, 4) + " " + digits.Substring(4, 4) + " " + digits.Substring(8, 4) + " " + digits.Substring(12, 4);
        }

        public static byte[] Salt(string transcript)
        {
            return Sha256(Utf8.GetBytes("pult-v2-salt\n" + transcript));
        }

        public static byte[] PseudoRandomKey(CngKey ownAgree, byte[] peerAgreePoint, byte[] salt)
        {
            using (ECDiffieHellmanPublicKey peer = P256.AgreePublicKey(peerAgreePoint))
            using (var ecdh = new ECDiffieHellmanCng(ownAgree))
            {
                ecdh.KeyDerivationFunction = ECDiffieHellmanKeyDerivationFunction.Hmac;
                ecdh.HashAlgorithm = CngAlgorithm.Sha256;
                ecdh.HmacKey = salt;
                return ecdh.DeriveKeyMaterial(peer);
            }
        }

        public static byte[] Expand(byte[] prk, string label)
        {
            byte[] info = Utf8.GetBytes(label);
            var input = new byte[info.Length + 1];
            Buffer.BlockCopy(info, 0, input, 0, info.Length);
            input[info.Length] = 1;
            using (var hmac = new HMACSHA256(prk))
            {
                return hmac.ComputeHash(input);
            }
        }

        public static DirectionKeys DeriveKeys(CngKey ownAgree, byte[] peerAgreePoint, string transcript)
        {
            byte[] prk = PseudoRandomKey(ownAgree, peerAgreePoint, Salt(transcript));
            var keys = new DirectionKeys(Expand(prk, "pult-v2-key-pc-phone"), Expand(prk, "pult-v2-key-phone-pc"));
            Array.Clear(prk, 0, prk.Length);
            return keys;
        }

        public static byte[] Sha256(byte[] data)
        {
            using (var sha = SHA256.Create())
            {
                return sha.ComputeHash(data);
            }
        }

        public static byte[] RandomBytes(int length)
        {
            var bytes = new byte[length];
            using (var random = RandomNumberGenerator.Create()) random.GetBytes(bytes);
            return bytes;
        }
    }
}
