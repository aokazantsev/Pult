using System.Collections.Generic;

namespace Pult
{
    internal static class ServerInfo
    {
        public static readonly long[] Supported = { Envelope.Version };

        public static string Check(string server, int timeoutMs, out bool reachable)
        {
            RelayResponse response = RelayHttp.Send(server.TrimEnd('/') + "/info", "GET", null, null, timeoutMs);
            reachable = response.Status == 200;
            if (response.Status != 200) return "сервер не ответил: " + response.Describe();
            Dictionary<string, object> info = response.Json;
            object[] versions = Json.Value(info, "protocol") as object[];
            if (Json.String(info, "name") != "pult-relay" || versions == null) return "по адресу не сервер Pult";
            return Incompatibility("Сервер", versions);
        }

        public static string Incompatibility(string side, object[] versions)
        {
            long highest = -1;
            foreach (object version in versions)
            {
                if (!StrictJson.IsSafeInteger(version)) continue;
                foreach (long own in Supported)
                {
                    if ((long)version == own) return null;
                }
                if ((long)version > highest) highest = (long)version;
            }
            long ownHighest = Supported[Supported.Length - 1];
            string update = highest < ownHighest ? "обновите " + (side == "Сервер" ? "сервер" : "приложение на телефоне") : "обновите Pult на ПК";
            return side + " несовместим (версии протокола " + Describe(versions) + ", у Pult — " + ownHighest + "): " + update;
        }

        private static string Describe(object[] versions)
        {
            var parts = new List<string>();
            foreach (object version in versions) parts.Add(System.Convert.ToString(version, System.Globalization.CultureInfo.InvariantCulture));
            return parts.Count == 0 ? "нет" : string.Join(", ", parts);
        }
    }
}
