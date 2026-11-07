using System;
using System.Security.Cryptography;

namespace Pult
{
    internal sealed class PairKeys : IDisposable
    {
        public readonly CngKey Sign;
        public readonly CngKey Agree;
        public readonly string SignName;
        public readonly string AgreeName;
        public readonly string SignPublic;
        public readonly string AgreePublic;

        public PairKeys(CngKey sign, CngKey agree, string signName, string agreeName)
        {
            Sign = sign;
            Agree = agree;
            SignName = signName;
            AgreeName = agreeName;
            SignPublic = P256.PublicText(sign);
            AgreePublic = P256.PublicText(agree);
        }

        public static PairKeys Create()
        {
            string prefix = "Pult-" + Guid.NewGuid().ToString("N");
            CngKey sign = CreateKey(CngAlgorithm.ECDsaP256, prefix + "-sign");
            try
            {
                CngKey agree = CreateKey(CngAlgorithm.ECDiffieHellmanP256, prefix + "-agree");
                return new PairKeys(sign, agree, prefix + "-sign", prefix + "-agree");
            }
            catch
            {
                sign.Delete();
                throw;
            }
        }

        public static PairKeys Open(string signName, string agreeName)
        {
            if (!IsOurName(signName) || !IsOurName(agreeName)) return null;
            if (!CngKey.Exists(signName) || !CngKey.Exists(agreeName)) return null;
            return new PairKeys(CngKey.Open(signName), CngKey.Open(agreeName), signName, agreeName);
        }

        public static void DeleteByName(string name)
        {
            if (!IsOurName(name)) return;
            try
            {
                if (!CngKey.Exists(name)) return;
                using (CngKey key = CngKey.Open(name)) key.Delete();
            }
            catch (CryptographicException error)
            {
                AppLog.Append("key " + name + " not deleted: " + error.Message);
            }
        }

        public byte[] SignData(byte[] data)
        {
            return P256.Sign(Sign, data);
        }

        public void Delete()
        {
            DeleteKey(Sign, SignName);
            DeleteKey(Agree, AgreeName);
        }

        public void Dispose()
        {
            Sign.Dispose();
            Agree.Dispose();
        }

        private static void DeleteKey(CngKey key, string name)
        {
            try
            {
                if (name != null) key.Delete();
                else key.Dispose();
            }
            catch (CryptographicException error)
            {
                AppLog.Append("key " + name + " not deleted: " + error.Message);
            }
            catch (ObjectDisposedException)
            {
                DeleteByName(name);
            }
        }

        private static CngKey CreateKey(CngAlgorithm algorithm, string name)
        {
            var parameters = new CngKeyCreationParameters
            {
                ExportPolicy = CngExportPolicies.None,
                Provider = CngProvider.MicrosoftSoftwareKeyStorageProvider,
                KeyCreationOptions = CngKeyCreationOptions.None
            };
            return CngKey.Create(algorithm, name, parameters);
        }

        private static bool IsOurName(string name)
        {
            return name != null && name.StartsWith("Pult-", StringComparison.Ordinal) && name.Length < 80;
        }
    }
}
