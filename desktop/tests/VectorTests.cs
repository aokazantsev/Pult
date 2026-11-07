using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Pult.Tests
{
    internal static class VectorTests
    {
        private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false);

        public static void Keys()
        {
            Dictionary<string, object> keys = Json.Object(Load("keys.json"), "keys");
            foreach (string name in new[] { "pc_sign", "pc_agree", "phone_sign", "phone_agree" })
            {
                Dictionary<string, object> key = Json.Object(keys, name);
                byte[] point = P256.ParsePoint(Json.String(key, "public"));
                Expect(point != null, name + " public parses as a point on the curve");
                Expect(Hex(point, 1, 32) == Json.String(key, "x") && Hex(point, 33, 32) == Json.String(key, "y"), name + " x and y");
                using (CngKey imported = PrivateKey(name))
                {
                    Expect(P256.PublicText(imported) == Json.String(key, "public"), name + " exported public key");
                }
            }
            byte[] valid = P256.ParsePoint(Json.String(Json.Object(keys, "pc_sign"), "public"));
            byte[] offCurve = (byte[])valid.Clone();
            offCurve[64] ^= 1;
            Expect(!P256.IsOnCurve(offCurve), "point off the curve rejected");
            Expect(P256.ParsePoint(Json.String(Json.Object(keys, "pc_sign"), "public") + "A") == null, "wrong length rejected");
        }

        public static void Pairing()
        {
            Dictionary<string, object> v = Load("pairing.json");
            string pair = Json.String(v, "pair");
            byte[] secret = Base64Url.Decode(Json.String(v, "secret"), PairCrypto.SecretLength);
            string transcript = PairCrypto.Transcript(pair, Json.String(v, "pc_sign"), Json.String(v, "pc_agree"), Json.String(v, "phone_sign"), Json.String(v, "phone_agree"));
            Expect(transcript == Json.String(v, "transcript"), "transcript");
            byte[] tag = PairCrypto.ClaimTag(secret, transcript);
            Expect(Base64Url.Encode(tag) == Json.String(v, "tag"), "tag");
            Expect(PairCrypto.FixedTimeEquals(tag, Base64Url.Decode(Json.String(v, "tag"), 32)), "tag compares equal");
            foreach (object item in (object[])Json.Value(v, "reject"))
            {
                var reject = (Dictionary<string, object>)item;
                byte[] expected = PairCrypto.ClaimTag(secret, Json.String(reject, "transcript"));
                Expect(!PairCrypto.FixedTimeEquals(expected, Base64Url.Decode(Json.String(reject, "tag"))), "reject: " + Json.String(reject, "case"));
            }
            Expect(PairCrypto.Fingerprint(transcript) == Json.String(v, "fingerprint"), "fingerprint");
            byte[] salt = PairCrypto.Salt(transcript);
            Expect(Hex(salt) == Json.String(v, "salt_hex"), "salt");
            byte[] phoneAgree = P256.ParsePoint(Json.String(v, "phone_agree"));
            byte[] pcAgree = P256.ParsePoint(Json.String(v, "pc_agree"));
            using (CngKey pcKey = PrivateKey("pc_agree"))
            using (CngKey phoneKey = PrivateKey("phone_agree"))
            {
                Expect(Hex(PairCrypto.PseudoRandomKey(pcKey, phoneAgree, salt)) == Json.String(v, "prk_hex"), "prk on pc");
                Expect(Hex(PairCrypto.PseudoRandomKey(phoneKey, pcAgree, salt)) == Json.String(v, "prk_hex"), "prk on phone");
                DirectionKeys keys = PairCrypto.DeriveKeys(pcKey, phoneAgree, transcript);
                Expect(Hex(keys.PcToPhone) == Json.String(v, "key_pc_to_phone_hex"), "K_pc");
                Expect(Hex(keys.PhoneToPc) == Json.String(v, "key_phone_to_pc_hex"), "K_phone");
            }
            string qr = QrPayload.Build(Json.String(v, "server"), pair, secret, Json.String(v, "pc_sign"), Json.String(v, "pc_agree"), (int)Json.Long(v, "idle_seconds", 0));
            Expect(qr == Json.String(v, "qr"), "qr payload");
        }

        public static void EnvelopeValid()
        {
            Dictionary<string, object> v = Load("envelope.json");
            DirectionKeys keys = Keys(v);
            string pair = Json.String(v, "pair");
            foreach (object item in (object[])Json.Value(v, "valid"))
            {
                var vector = (Dictionary<string, object>)item;
                Dictionary<string, object> expected = Json.Object(vector, "envelope");
                string from = Json.String(vector, "from");
                string id = Json.String(expected, "id");
                long ts = Json.Long(expected, "ts", -1);
                long ttl = Json.Long(expected, "ttl", -1);
                Expect(Envelope.Aad(2, pair, from, id, ts, ttl) == Json.String(vector, "aad"), "aad " + id);
                Expect(Hex(keys.For(from)) == Json.String(vector, "key_hex"), "direction key " + id);
                string sealedText = Envelope.Seal(keys, pair, from, id, ts, ttl, Json.String(vector, "plaintext"), FromHex(Json.String(vector, "nonce_hex")));
                Dictionary<string, object> produced = StrictJson.TryParseObject(sealedText);
                Expect(produced != null && produced.Count == 7, "sealed envelope is strict json " + id);
                foreach (KeyValuePair<string, object> field in expected)
                {
                    Expect(Equals(produced[field.Key], field.Value), "sealed field " + field.Key + " of " + id);
                }
                OpenedEnvelope opened = Envelope.Open(keys, pair, Json.String(vector, "to"), expected);
                Expect(opened.Accepted, "opened " + id + ": " + opened.Problem);
                Expect(opened.Plaintext == Json.String(vector, "plaintext"), "plaintext " + id);
                Expect(!Envelope.Open(keys, pair, from, expected).Accepted, "sender does not accept its own envelope " + id);
            }
        }

        public static void EnvelopeReject()
        {
            Dictionary<string, object> v = Load("envelope.json");
            DirectionKeys keys = Keys(v);
            string pair = Json.String(v, "pair");
            foreach (object item in (object[])Json.Value(v, "reject"))
            {
                var vector = (Dictionary<string, object>)item;
                OpenedEnvelope opened = Envelope.Open(keys, pair, Json.String(vector, "to"), Json.Object(vector, "envelope"));
                Expect(!opened.Accepted, "reject: " + Json.String(vector, "case"));
            }
            foreach (object item in (object[])Json.Value(v, "reject_text"))
            {
                var vector = (Dictionary<string, object>)item;
                OpenedEnvelope opened = Envelope.Open(keys, pair, Json.String(vector, "to"), Json.String(vector, "text"));
                Expect(!opened.Accepted, "reject text: " + Json.String(vector, "case"));
            }
        }

        public static void Auth()
        {
            Dictionary<string, object> v = Load("auth.json");
            string pair = Json.String(v, "pair");
            foreach (object item in (object[])Json.Value(v, "login"))
            {
                var vector = (Dictionary<string, object>)item;
                string from = Json.String(vector, "from");
                string text = SignedStrings.Login(pair, from, Json.String(vector, "challenge"));
                Expect(text == Json.String(vector, "string"), "login string " + from);
                CheckOwnSignature(from + "_sign", Json.String(vector, "public"), text);
            }
            foreach (object item in (object[])Json.Value(v, "post"))
            {
                var vector = (Dictionary<string, object>)item;
                string from = Json.String(vector, "from");
                byte[] body = Utf8.GetBytes(Json.String(vector, "body"));
                Expect(SignedStrings.BodyHash(body) == Json.String(vector, "body_sha256"), "body hash " + Json.String(vector, "op"));
                string text = SignedStrings.Post(Json.String(vector, "op"), pair, from, Json.String(vector, "stream"), Json.Long(vector, "n", -1), body);
                Expect(text == Json.String(vector, "string"), "post string " + Json.String(vector, "op"));
                CheckOwnSignature(from + "_sign", Json.String(vector, "public"), text);
                Expect(P256.Verify(P256.ParsePoint(Json.String(vector, "public")), Utf8.GetBytes(text), Base64Url.Decode(Json.String(vector, "sig"))), "reference signature verifies " + Json.String(vector, "op"));
            }
            foreach (object item in (object[])Json.Value(v, "verify"))
            {
                var vector = (Dictionary<string, object>)item;
                bool valid = P256.Verify(P256.ParsePoint(Json.String(vector, "public")), Utf8.GetBytes(Json.String(vector, "string")), Base64Url.Decode(Json.String(vector, "sig")));
                Expect(valid == Json.Bool(vector, "valid"), "verify: " + Json.String(vector, "case"));
            }
        }

        public static void StrictParsing()
        {
            Expect(StrictJson.TryParseObject("{\"a\":1,\"a\":1}") == null, "duplicate key");
            Expect(StrictJson.TryParseObject("{\"a\":NaN}") == null, "NaN");
            Expect(StrictJson.TryParseObject("{\"a\":1} x") == null, "trailing text");
            Expect(StrictJson.TryParseObject("{\"a\":01}") == null, "leading zero");
            Expect(StrictJson.TryParseObject("{\"a\":1e999}") == null, "overflow");
            Dictionary<string, object> parsed = StrictJson.TryParseObject("{\"i\":600,\"f\":600.0,\"s\":\"\\u0422\\n\",\"a\":[1,true,null]}");
            Expect(parsed != null && parsed["i"] is long && parsed["f"] is double && (string)parsed["s"] == "Т\n" && ((object[])parsed["a"]).Length == 3, "values");
            Expect(Base64Url.Decode("AB") == null && Base64Url.Decode("AA") != null, "base64url canonical bits");
            Expect(Base64Url.Decode("AA==") == null && Base64Url.Decode("A+") == null && Base64Url.Decode("A") == null, "base64url padding, alphabet, length");
        }

        public static CngKey PrivateKey(string name)
        {
            Dictionary<string, object> key = Json.Object(Json.Object(Load("keys.json"), "keys"), name);
            return P256.ImportPrivate(P256.ParsePoint(Json.String(key, "public")), FromHex(Json.String(key, "d")), name.EndsWith("_agree", StringComparison.Ordinal));
        }

        public static Dictionary<string, object> Load(string file)
        {
            string directory = AppDomain.CurrentDomain.BaseDirectory;
            for (int i = 0; i < 5 && !Directory.Exists(Path.Combine(directory, "protocol")); i++) directory = Path.GetDirectoryName(directory.TrimEnd('\\'));
            Dictionary<string, object> parsed = StrictJson.TryParseObject(File.ReadAllText(Path.Combine(directory, "protocol", "vectors", file), Utf8));
            if (parsed == null) throw new Exception(file + " is not strict json");
            return parsed;
        }

        public static byte[] FromHex(string hex)
        {
            var bytes = new byte[hex.Length / 2];
            for (int i = 0; i < bytes.Length; i++) bytes[i] = byte.Parse(hex.Substring(2 * i, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            return bytes;
        }

        private static void CheckOwnSignature(string keyName, string publicText, string text)
        {
            using (CngKey key = PrivateKey(keyName))
            {
                byte[] signature = P256.Sign(key, Utf8.GetBytes(text));
                Expect(signature.Length == 64, "p1363 length");
                Expect(P256.Verify(P256.ParsePoint(publicText), Utf8.GetBytes(text), signature), "own signature verifies with " + keyName);
            }
        }

        private static DirectionKeys Keys(Dictionary<string, object> v)
        {
            return new DirectionKeys(FromHex(Json.String(v, "key_pc_to_phone_hex")), FromHex(Json.String(v, "key_phone_to_pc_hex")));
        }

        private static string Hex(byte[] data)
        {
            return Hex(data, 0, data.Length);
        }

        private static string Hex(byte[] data, int offset, int count)
        {
            var text = new StringBuilder();
            for (int i = offset; i < offset + count; i++) text.Append(data[i].ToString("x2", CultureInfo.InvariantCulture));
            return text.ToString();
        }

        private static void Expect(bool condition, string message)
        {
            if (!condition) throw new Exception(message);
        }
    }
}
