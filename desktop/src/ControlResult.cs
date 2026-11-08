using System.Collections.Generic;

namespace Pult
{
    internal static class ControlResult
    {
        public const string Usage = "usage";
        public const string Failed = "failed";
        public const string NotRunning = "not_running";
        public const string Refused = "refused";
        public const int ExitOk = 0;
        public const int ExitFailed = 1;
        public const int ExitUsage = 2;
        public const int ExitNotRunning = 3;

        public static Dictionary<string, object> Ok()
        {
            return new Dictionary<string, object> { { "ok", true } };
        }

        public static Dictionary<string, object> Fail(string error, string message)
        {
            return new Dictionary<string, object> { { "ok", false }, { "error", error }, { "message", message } };
        }

        public static int ExitCode(Dictionary<string, object> result)
        {
            if (Json.Bool(result, "ok")) return ExitOk;
            switch (Json.String(result, "error"))
            {
                case Usage: return ExitUsage;
                case NotRunning: return ExitNotRunning;
                default: return ExitFailed;
            }
        }
    }
}
