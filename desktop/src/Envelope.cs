using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Pult
{
    internal static class Envelope
    {
        public const int Version = 2;
        private static readonly string[] Fields = { "v", "pair", "from", "id", "ts", "ttl", "ct" };
        private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false);

        public static string Aad(long version, string pair, string from, string id, long ts, long ttl)
        {
            return string.Join("\n", "pult-v2-env", Number(version), pair, from, id, Number(ts), Number(ttl));
        }

        public static string Seal(DirectionKeys keys, string pair, string from, string id, long ts, long ttl, string plaintext, byte[] nonce)
        {
            byte[] aad = Utf8.GetBytes(Aad(Version, pair, from, id, ts, ttl));
            byte[] sealedBytes = AesGcm.Encrypt(keys.For(from), nonce, aad, Utf8.GetBytes(plaintext));
            var ct = new byte[nonce.Length + sealedBytes.Length];
            System.Buffer.BlockCopy(nonce, 0, ct, 0, nonce.Length);
            System.Buffer.BlockCopy(sealedBytes, 0, ct, nonce.Length, sealedBytes.Length);
            return "{\"v\":" + Number(Version) + ",\"pair\":\"" + pair + "\",\"from\":\"" + from + "\",\"id\":\"" + id
                + "\",\"ts\":" + Number(ts) + ",\"ttl\":" + Number(ttl) + ",\"ct\":\"" + Base64Url.Encode(ct) + "\"}";
        }

        public static string Seal(DirectionKeys keys, string pair, string from, string id, long ts, long ttl, string plaintext)
        {
            return Seal(keys, pair, from, id, ts, ttl, plaintext, PairCrypto.RandomBytes(AesGcm.NonceLength));
        }

        public static OpenedEnvelope Open(DirectionKeys keys, string ownPair, string ownSide, string text)
        {
            return Open(keys, ownPair, ownSide, StrictJson.TryParseObject(text));
        }

        public static OpenedEnvelope Open(DirectionKeys keys, string ownPair, string ownSide, Dictionary<string, object> envelope)
        {
            if (envelope == null) return OpenedEnvelope.Invalid("not a strict json object");
            if (envelope.Count != Fields.Length) return OpenedEnvelope.Invalid("wrong field set");
            foreach (string field in Fields)
            {
                if (!envelope.ContainsKey(field)) return OpenedEnvelope.Invalid("missing " + field);
            }
            object version = envelope["v"];
            if (!StrictJson.IsSafeInteger(version)) return OpenedEnvelope.Invalid("v is not an integer");
            string id = envelope["id"] as string;
            if (!StrictJson.IsIdentifier(id)) return OpenedEnvelope.Invalid("bad id");
            if ((long)version != Version) return OpenedEnvelope.Unsupported(id, (long)version);
            string pair = envelope["pair"] as string;
            string from = envelope["from"] as string;
            if (!StrictJson.IsIdentifier(pair) || pair != ownPair) return OpenedEnvelope.Invalid("foreign pair");
            if (from != "pc" && from != "phone") return OpenedEnvelope.Invalid("bad from");
            if (from == ownSide) return OpenedEnvelope.Invalid("own direction");
            object ts = envelope["ts"];
            object ttl = envelope["ttl"];
            if (!StrictJson.IsSafeInteger(ts) || !StrictJson.IsSafeInteger(ttl)) return OpenedEnvelope.Invalid("ts or ttl is not an integer");
            byte[] ct = Base64Url.Decode(envelope["ct"] as string);
            if (ct == null || ct.Length < AesGcm.NonceLength + AesGcm.TagLength) return OpenedEnvelope.Invalid("bad ct");
            var nonce = new byte[AesGcm.NonceLength];
            var rest = new byte[ct.Length - AesGcm.NonceLength];
            System.Buffer.BlockCopy(ct, 0, nonce, 0, nonce.Length);
            System.Buffer.BlockCopy(ct, nonce.Length, rest, 0, rest.Length);
            byte[] aad = Utf8.GetBytes(Aad(Version, pair, from, id, (long)ts, (long)ttl));
            byte[] plain = AesGcm.Decrypt(keys.For(from), nonce, aad, rest);
            if (plain == null) return OpenedEnvelope.Invalid("authentication failed");
            string plaintext;
            try
            {
                plaintext = new UTF8Encoding(false, true).GetString(plain);
            }
            catch (System.ArgumentException)
            {
                return OpenedEnvelope.Invalid("plaintext is not utf-8");
            }
            Dictionary<string, object> inner = StrictJson.TryParseObject(plaintext);
            if (inner == null || !(Json.Value(inner, "type") is string)) return OpenedEnvelope.Invalid("plaintext is not a typed object");
            return OpenedEnvelope.Ok(id, from, plaintext, inner);
        }

        private static string Number(long value)
        {
            return value.ToString(CultureInfo.InvariantCulture);
        }
    }
}
