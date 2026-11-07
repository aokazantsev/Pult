using System;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace Pult
{
    internal static class AesGcm
    {
        public const int NonceLength = 12;
        public const int TagLength = 16;
        private const uint AuthTagMismatch = 0xC000A002;

        public static byte[] Encrypt(byte[] key, byte[] nonce, byte[] aad, byte[] plaintext)
        {
            var ciphertext = new byte[plaintext.Length];
            var tag = new byte[TagLength];
            Run(true, key, nonce, aad, plaintext, ciphertext, tag);
            var result = new byte[ciphertext.Length + TagLength];
            Buffer.BlockCopy(ciphertext, 0, result, 0, ciphertext.Length);
            Buffer.BlockCopy(tag, 0, result, ciphertext.Length, TagLength);
            return result;
        }

        public static byte[] Decrypt(byte[] key, byte[] nonce, byte[] aad, byte[] ciphertextAndTag)
        {
            if (ciphertextAndTag.Length < TagLength) return null;
            int length = ciphertextAndTag.Length - TagLength;
            var ciphertext = new byte[length];
            var tag = new byte[TagLength];
            Buffer.BlockCopy(ciphertextAndTag, 0, ciphertext, 0, length);
            Buffer.BlockCopy(ciphertextAndTag, length, tag, 0, TagLength);
            var plaintext = new byte[length];
            return Run(false, key, nonce, aad, ciphertext, plaintext, tag) ? plaintext : null;
        }

        private static bool Run(bool encrypt, byte[] key, byte[] nonce, byte[] aad, byte[] input, byte[] output, byte[] tag)
        {
            if (key.Length != 32 || nonce.Length != NonceLength) throw new CryptographicException("bad AES-GCM key or nonce length");
            IntPtr algorithm = IntPtr.Zero;
            IntPtr keyHandle = IntPtr.Zero;
            GCHandle noncePin = GCHandle.Alloc(nonce, GCHandleType.Pinned);
            GCHandle aadPin = GCHandle.Alloc(aad, GCHandleType.Pinned);
            GCHandle tagPin = GCHandle.Alloc(tag, GCHandleType.Pinned);
            try
            {
                Check(BCryptOpenAlgorithmProvider(out algorithm, "AES", null, 0), "open AES");
                byte[] mode = Encoding.Unicode.GetBytes("ChainingModeGCM\0");
                Check(BCryptSetProperty(algorithm, "ChainingMode", mode, mode.Length, 0), "set GCM");
                Check(BCryptGenerateSymmetricKey(algorithm, out keyHandle, IntPtr.Zero, 0, key, key.Length, 0), "import key");
                var info = new AuthenticatedCipherModeInfo
                {
                    cbSize = Marshal.SizeOf(typeof(AuthenticatedCipherModeInfo)),
                    dwInfoVersion = 1,
                    pbNonce = noncePin.AddrOfPinnedObject(),
                    cbNonce = nonce.Length,
                    pbAuthData = aadPin.AddrOfPinnedObject(),
                    cbAuthData = aad.Length,
                    pbTag = tagPin.AddrOfPinnedObject(),
                    cbTag = tag.Length
                };
                int written;
                uint status = encrypt
                    ? BCryptEncrypt(keyHandle, input, input.Length, ref info, null, 0, output, output.Length, out written, 0)
                    : BCryptDecrypt(keyHandle, input, input.Length, ref info, null, 0, output, output.Length, out written, 0);
                if (!encrypt && status == AuthTagMismatch) return false;
                Check(status, encrypt ? "encrypt" : "decrypt");
                if (written != output.Length) throw new CryptographicException("AES-GCM wrote " + written + " of " + output.Length + " bytes");
                return true;
            }
            finally
            {
                if (keyHandle != IntPtr.Zero) BCryptDestroyKey(keyHandle);
                if (algorithm != IntPtr.Zero) BCryptCloseAlgorithmProvider(algorithm, 0);
                noncePin.Free();
                aadPin.Free();
                tagPin.Free();
            }
        }

        private static void Check(uint status, string step)
        {
            if (status != 0) throw new CryptographicException("bcrypt " + step + " failed: 0x" + status.ToString("X8"));
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct AuthenticatedCipherModeInfo
        {
            public int cbSize;
            public int dwInfoVersion;
            public IntPtr pbNonce;
            public int cbNonce;
            public IntPtr pbAuthData;
            public int cbAuthData;
            public IntPtr pbTag;
            public int cbTag;
            public IntPtr pbMacContext;
            public int cbMacContext;
            public int cbAAD;
            public long cbData;
            public int dwFlags;
        }

        [DllImport("bcrypt.dll", CharSet = CharSet.Unicode)]
        private static extern uint BCryptOpenAlgorithmProvider(out IntPtr algorithm, string algorithmId, string implementation, uint flags);

        [DllImport("bcrypt.dll", CharSet = CharSet.Unicode)]
        private static extern uint BCryptSetProperty(IntPtr handle, string property, byte[] input, int inputLength, uint flags);

        [DllImport("bcrypt.dll")]
        private static extern uint BCryptGenerateSymmetricKey(IntPtr algorithm, out IntPtr key, IntPtr keyObject, int keyObjectLength, byte[] secret, int secretLength, uint flags);

        [DllImport("bcrypt.dll")]
        private static extern uint BCryptEncrypt(IntPtr key, byte[] input, int inputLength, ref AuthenticatedCipherModeInfo info, byte[] iv, int ivLength, byte[] output, int outputLength, out int written, uint flags);

        [DllImport("bcrypt.dll")]
        private static extern uint BCryptDecrypt(IntPtr key, byte[] input, int inputLength, ref AuthenticatedCipherModeInfo info, byte[] iv, int ivLength, byte[] output, int outputLength, out int written, uint flags);

        [DllImport("bcrypt.dll")]
        private static extern uint BCryptDestroyKey(IntPtr key);

        [DllImport("bcrypt.dll")]
        private static extern uint BCryptCloseAlgorithmProvider(IntPtr algorithm, uint flags);
    }
}
