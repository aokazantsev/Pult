using System;

namespace Pult
{
    internal sealed class RelaySession
    {
        public readonly string Server;
        public readonly string Pair;
        public readonly PairKeys Keys;
        private byte[] secret;
        private volatile int idleSeconds;

        private RelaySession(string server, string pair, PairKeys keys, int idleSeconds, byte[] secret)
        {
            Server = server.TrimEnd('/');
            Pair = pair;
            Keys = keys;
            this.idleSeconds = idleSeconds;
            this.secret = secret;
        }

        public int IdleSeconds
        {
            get { return idleSeconds; }
            set
            {
                if (!IsActive) idleSeconds = Math.Max(QrPayload.MinIdleSeconds, Math.Min(QrPayload.MaxIdleSeconds, value));
            }
        }

        public System.Collections.Generic.List<string> Folders = new System.Collections.Generic.List<string>();

        public DateTime PairedAtUtc = DateTime.MinValue;

        public string PhoneSign { get; private set; }

        public string PhoneAgree { get; private set; }

        public DirectionKeys Directions { get; private set; }

        public string Fingerprint { get; private set; }

        public bool IsActive
        {
            get { return Directions != null; }
        }

        public bool AwaitsPhone
        {
            get { return Directions == null && secret != null; }
        }

        public static RelaySession Pending(string server, string pair, PairKeys keys, byte[] secret, int idleSeconds)
        {
            return new RelaySession(server, pair, keys, idleSeconds, secret);
        }

        public static RelaySession Active(string server, string pair, PairKeys keys, string phoneSign, string phoneAgree, int idleSeconds)
        {
            var session = new RelaySession(server, pair, keys, idleSeconds, null);
            byte[] agreePoint = P256.ParsePoint(phoneAgree);
            if (agreePoint == null || P256.ParsePoint(phoneSign) == null) return null;
            session.Complete(phoneSign, phoneAgree, agreePoint);
            return session;
        }

        public static RelaySession Restore(PairRecord record)
        {
            if (record == null || !record.IsActive) return null;
            PairKeys keys = PairKeys.Open(record.SignKey, record.AgreeKey);
            if (keys == null)
            {
                AppLog.Append("pair " + record.Pair + ": keys are missing in the key store");
                return null;
            }
            int idle = record.IdleSeconds >= QrPayload.MinIdleSeconds && record.IdleSeconds <= QrPayload.MaxIdleSeconds ? record.IdleSeconds : QrPayload.DefaultIdleSeconds;
            RelaySession restored = Active(record.Server, record.Pair, keys, record.PhoneSign, record.PhoneAgree, idle);
            if (restored != null)
            {
                restored.Folders = new System.Collections.Generic.List<string>(record.Folders);
                restored.PairedAtUtc = record.PairedAtUnix > 0 ? new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(record.PairedAtUnix) : DateTime.UtcNow;
            }
            return restored;
        }

        public bool Verify(string phoneSign, string phoneAgree, string tag)
        {
            byte[] key = secret;
            secret = null;
            if (key == null) return false;
            try
            {
                byte[] agreePoint = P256.ParsePoint(phoneAgree);
                byte[] tagBytes = Base64Url.Decode(tag, 32);
                if (agreePoint == null || P256.ParsePoint(phoneSign) == null || tagBytes == null) return false;
                string transcript = Transcript(phoneSign, phoneAgree);
                if (!PairCrypto.FixedTimeEquals(PairCrypto.ClaimTag(key, transcript), tagBytes)) return false;
                Complete(phoneSign, phoneAgree, agreePoint);
                return true;
            }
            finally
            {
                Array.Clear(key, 0, key.Length);
            }
        }

        public bool SamePhone(string phoneSign, string phoneAgree)
        {
            return phoneSign == PhoneSign && phoneAgree == PhoneAgree;
        }

        public byte[] SecretCopy()
        {
            byte[] key = secret;
            return key == null ? null : (byte[])key.Clone();
        }

        public void ForgetSecret()
        {
            byte[] key = secret;
            secret = null;
            if (key != null) Array.Clear(key, 0, key.Length);
        }

        public PairRecord ToRecord()
        {
            return new PairRecord
            {
                Server = Server,
                Pair = Pair,
                SignKey = Keys.SignName,
                AgreeKey = Keys.AgreeName,
                PhoneSign = PhoneSign,
                PhoneAgree = PhoneAgree,
                IdleSeconds = IdleSeconds,
                Folders = new System.Collections.Generic.List<string>(Folders),
                PairedAtUnix = PairedAtUtc == DateTime.MinValue ? 0 : (long)(PairedAtUtc - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds
            };
        }

        private void Complete(string phoneSign, string phoneAgree, byte[] agreePoint)
        {
            string transcript = Transcript(phoneSign, phoneAgree);
            Directions = PairCrypto.DeriveKeys(Keys.Agree, agreePoint, transcript);
            Fingerprint = PairCrypto.Fingerprint(transcript);
            PhoneSign = phoneSign;
            PhoneAgree = phoneAgree;
        }

        private string Transcript(string phoneSign, string phoneAgree)
        {
            return PairCrypto.Transcript(Pair, Keys.SignPublic, Keys.AgreePublic, phoneSign, phoneAgree);
        }
    }
}
