using System;
using System.Globalization;
using System.Numerics;
using System.Security.Cryptography;

namespace Pult
{
    internal static class P256
    {
        public const int PointLength = 65;
        public const int SignatureLength = 64;
        private const int CoordinateLength = 32;
        private const int EcdhPublicMagic = 0x314B4345;
        private const int EcdhPrivateMagic = 0x324B4345;
        private const int EcdsaPublicMagic = 0x31534345;
        private const int EcdsaPrivateMagic = 0x32534345;

        private static readonly BigInteger Prime = Hex("ffffffff00000001000000000000000000000000ffffffffffffffffffffffff");
        private static readonly BigInteger CurveB = Hex("5ac635d8aa3a93e7b3ebbd55769886bc651d06b0cc53b0f63bce3c3e27d2604b");

        public static byte[] PublicPoint(CngKey key)
        {
            byte[] blob = key.Export(CngKeyBlobFormat.EccPublicBlob);
            if (blob.Length != 8 + 2 * CoordinateLength || BitConverter.ToInt32(blob, 4) != CoordinateLength) throw new CryptographicException("unexpected public key blob");
            var point = new byte[PointLength];
            point[0] = 4;
            Buffer.BlockCopy(blob, 8, point, 1, 2 * CoordinateLength);
            return point;
        }

        public static string PublicText(CngKey key)
        {
            return Base64Url.Encode(PublicPoint(key));
        }

        public static byte[] ParsePoint(string text)
        {
            byte[] point = Base64Url.Decode(text, PointLength);
            return point != null && IsOnCurve(point) ? point : null;
        }

        public static bool IsOnCurve(byte[] point)
        {
            if (point == null || point.Length != PointLength || point[0] != 4) return false;
            BigInteger x = Unsigned(point, 1);
            BigInteger y = Unsigned(point, 1 + CoordinateLength);
            if (x >= Prime || y >= Prime) return false;
            BigInteger left = BigInteger.ModPow(y, 2, Prime);
            BigInteger right = (BigInteger.ModPow(x, 3, Prime) - 3 * x + CurveB) % Prime;
            if (right < 0) right += Prime;
            return left == right;
        }

        public static ECDiffieHellmanPublicKey AgreePublicKey(byte[] point)
        {
            return ECDiffieHellmanCngPublicKey.FromByteArray(Blob(EcdhPublicMagic, point, null), CngKeyBlobFormat.EccPublicBlob);
        }

        public static CngKey SignPublicKey(byte[] point)
        {
            return CngKey.Import(Blob(EcdsaPublicMagic, point, null), CngKeyBlobFormat.EccPublicBlob);
        }

        public static CngKey ImportPrivate(byte[] point, byte[] scalar, bool agree)
        {
            return CngKey.Import(Blob(agree ? EcdhPrivateMagic : EcdsaPrivateMagic, point, scalar), CngKeyBlobFormat.EccPrivateBlob);
        }

        public static byte[] Sign(CngKey key, byte[] data)
        {
            using (var ecdsa = new ECDsaCng(key))
            {
                return ecdsa.SignData(data, HashAlgorithmName.SHA256);
            }
        }

        public static bool Verify(byte[] point, byte[] data, byte[] signature)
        {
            if (signature == null || signature.Length != SignatureLength || !IsOnCurve(point)) return false;
            using (CngKey key = SignPublicKey(point))
            using (var ecdsa = new ECDsaCng(key))
            {
                return ecdsa.VerifyData(data, signature, HashAlgorithmName.SHA256);
            }
        }

        private static byte[] Blob(int magic, byte[] point, byte[] scalar)
        {
            if (point == null || point.Length != PointLength || point[0] != 4) throw new CryptographicException("not an uncompressed P-256 point");
            var blob = new byte[8 + 2 * CoordinateLength + (scalar == null ? 0 : CoordinateLength)];
            Buffer.BlockCopy(BitConverter.GetBytes(magic), 0, blob, 0, 4);
            Buffer.BlockCopy(BitConverter.GetBytes(CoordinateLength), 0, blob, 4, 4);
            Buffer.BlockCopy(point, 1, blob, 8, 2 * CoordinateLength);
            if (scalar != null) Buffer.BlockCopy(scalar, 0, blob, 8 + 2 * CoordinateLength, CoordinateLength);
            return blob;
        }

        private static BigInteger Unsigned(byte[] source, int offset)
        {
            var littleEndian = new byte[CoordinateLength + 1];
            for (int i = 0; i < CoordinateLength; i++) littleEndian[i] = source[offset + CoordinateLength - 1 - i];
            return new BigInteger(littleEndian);
        }

        private static BigInteger Hex(string hex)
        {
            return BigInteger.Parse("0" + hex, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture);
        }
    }
}
