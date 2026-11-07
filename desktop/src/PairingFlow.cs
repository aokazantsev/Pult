using System;
using System.Collections.Generic;
using System.Security.Cryptography;

namespace Pult
{
    internal static class PairingFlow
    {
        public const int DefaultClaimTtlSeconds = 600;
        private const int TimeoutMs = 15000;
        private static readonly TimeSpan DeletePatience = TimeSpan.FromSeconds(10);

        public static PairingOffer CreateOffer(string server, int idleSeconds, out string problem)
        {
            server = (server ?? "").Trim().TrimEnd('/');
            Uri address;
            if (!Uri.TryCreate(server, UriKind.Absolute, out address) || (address.Scheme != Uri.UriSchemeHttps && address.Scheme != Uri.UriSchemeHttp))
            {
                problem = "Адрес сервера должен начинаться с https:// (http:// — только для отладки)";
                return null;
            }
            bool reachable;
            problem = ServerInfo.Check(server, TimeoutMs, out reachable);
            if (problem != null) return null;
            PairKeys keys;
            try
            {
                keys = PairKeys.Create();
            }
            catch (CryptographicException error)
            {
                problem = "Не удалось создать ключи в хранилище Windows: " + error.Message;
                AppLog.Append("pairing: keys not created: " + error.Message);
                return null;
            }
            string body = "{\"pc_sign\":\"" + keys.SignPublic + "\",\"pc_agree\":\"" + keys.AgreePublic + "\"}";
            RelayResponse response = RelayHttp.PostJson(server + "/v2/claims", body, TimeoutMs);
            Dictionary<string, object> answer = response.Json;
            string pair = Json.String(answer, "pair");
            object ttl = Json.Value(answer, "ttl");
            if (response.Status != 201 || !StrictJson.IsIdentifier(pair) || !StrictJson.IsSafeInteger(ttl) || (long)ttl < 1)
            {
                keys.Delete();
                keys.Dispose();
                problem = "Сервер не принял заявку: " + response.Describe();
                AppLog.Append("pairing: claim refused: " + response.Describe());
                return null;
            }
            var record = new PairRecord { Server = server, Pair = pair, SignKey = keys.SignName, AgreeKey = keys.AgreeName };
            record.Save(UserDataPaths.File(PairRecord.ClaimFile));
            RelaySession session = RelaySession.Pending(server, pair, keys, PairCrypto.RandomBytes(PairCrypto.SecretLength), idleSeconds);
            AppLog.Append("pairing: claim " + pair + " created on " + server + ", ttl " + ttl + " s");
            return new PairingOffer(session, (int)Math.Min((long)ttl, DefaultClaimTtlSeconds));
        }

        public static void Abandon(RelayClient client, string reason)
        {
            if (client == null) return;
            if (!client.Ended) client.DeletePair(reason, DeletePatience);
            Forget(client);
        }

        public static void Forget(RelayClient client)
        {
            client.Dispose();
            RelaySession session = client.Session;
            session.ForgetSecret();
            session.Keys.Delete();
            session.Keys.Dispose();
            ForgetFile(PairRecord.ClaimFile, session.Pair);
            ForgetFile(PairRecord.PairFile, session.Pair);
            PairRecord.Delete(RelayClient.ProcessedIdsPath(session.Pair));
        }

        public static void CleanLeftoverClaim(IRelayListener listener)
        {
            string path = UserDataPaths.File(PairRecord.ClaimFile);
            PairRecord record = PairRecord.Load(path);
            if (record == null)
            {
                PairRecord.Delete(path);
                return;
            }
            PairKeys keys = PairKeys.Open(record.SignKey, record.AgreeKey);
            if (keys == null)
            {
                PairRecord.Delete(path);
                return;
            }
            AppLog.Append("pairing: claim " + record.Pair + " left from the previous run, its secret is lost: deleting it");
            var client = new RelayClient(RelaySession.Pending(record.Server, record.Pair, keys, null, QrPayload.DefaultIdleSeconds), null);
            client.Start(listener);
            Abandon(client, "user");
        }

        private static void ForgetFile(string name, string pair)
        {
            string path = UserDataPaths.File(name);
            PairRecord record = PairRecord.Load(path);
            if (record == null || record.Pair == pair) PairRecord.Delete(path);
        }
    }
}
