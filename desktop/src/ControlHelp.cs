using System.Collections.Generic;

namespace Pult
{
    internal static class ControlHelp
    {
        private static readonly string[][] Commands =
        {
            new[] { "status", "state of Pult: hooks, server, pair, session, threads, open requests, mode, pairing window, recent notices" },
            new[] { "start", "start Pult if it is not running and wait for it, then status" },
            new[] { "exit", "tray menu Exit; waits until Pult is gone" },
            new[] { "pair [--folder <path>]... [--server <url>] [--idle-hours <0.25-36>]", "tray menu Connect phone: reopens the pairing window with these fields and answers with its state and the QR text" },
            new[] { "pairing [--wait <s>]", "state of the pairing window; --wait waits while it is creating a claim or showing a QR" },
            new[] { "new-qr", "pairing window button New QR, in the window opened by pair" },
            new[] { "close-pairing", "pairing window button Close (Done), for the window opened by pair" },
            new[] { "unpair", "tray menu Break the session, without the confirmation box; waits for the attempt to delete the pair on the server" },
            new[] { "mode phone|pc", "tray menu Control from the phone / from the PC" },
            new[] { "settings [set <key> <value>]", "tray menu Settings: the values of settings.txt, or one of them changed" },
            new[] { "log [--lines <n>]", "tray menu Log: the last lines of log.txt" },
            new[] { "help", "this list" }
        };

        public static Dictionary<string, object> Describe()
        {
            var list = new List<Dictionary<string, object>>();
            foreach (string[] command in Commands) list.Add(new Dictionary<string, object> { { "usage", "Pult.exe " + command[0] }, { "does", command[1] } });
            Dictionary<string, object> result = ControlResult.Ok();
            result["commands"] = list;
            result["exit_codes"] = new Dictionary<string, object> { { "0", "done" }, { "1", "failed or refused" }, { "2", "usage" }, { "3", "Pult is not running" } };
            return result;
        }
    }
}
