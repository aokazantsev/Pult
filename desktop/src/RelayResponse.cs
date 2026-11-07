using System.Collections.Generic;

namespace Pult
{
    internal sealed class RelayResponse
    {
        public readonly int Status;
        public readonly string Body;
        public readonly string NetworkProblem;

        public RelayResponse(int status, string body, string networkProblem)
        {
            Status = status;
            Body = body;
            NetworkProblem = networkProblem;
        }

        public Dictionary<string, object> Json
        {
            get { return StrictJson.TryParseObject(Body); }
        }

        public string Error
        {
            get { return Pult.Json.String(Json, "error"); }
        }

        public string Describe()
        {
            if (Status < 0) return NetworkProblem;
            string error = Error;
            return "http " + Status + (error != null ? " " + error : "");
        }
    }
}
