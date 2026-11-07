using System;
using System.Collections.Generic;
using System.Threading;

namespace Pult
{
    internal sealed class OutgoingMessage
    {
        public readonly string EnvelopeId = "pc-" + Guid.NewGuid().ToString("N");
        public readonly Dictionary<string, object> Inner;
        public readonly List<string> AckIds;
        public readonly string DeleteReason;
        public readonly DateTime ExpiresAtUtc;
        public readonly bool IsLive;
        public readonly Action Rejected;
        public readonly ManualResetEvent Done;

        private OutgoingMessage(Dictionary<string, object> inner, List<string> ackIds, string deleteReason, DateTime expiresAtUtc, bool live, Action rejected)
        {
            Inner = inner;
            AckIds = ackIds;
            DeleteReason = deleteReason;
            ExpiresAtUtc = expiresAtUtc;
            IsLive = live;
            Rejected = rejected;
            if (deleteReason != null) Done = new ManualResetEvent(false);
        }

        public string Operation
        {
            get
            {
                if (AckIds != null) return "acks";
                if (DeleteReason != null) return "delete";
                return "messages";
            }
        }

        public static OutgoingMessage Queued(Dictionary<string, object> inner, DateTime expiresAtUtc, Action rejected)
        {
            return new OutgoingMessage(inner, null, null, expiresAtUtc, false, rejected);
        }

        public static OutgoingMessage Live(Dictionary<string, object> inner)
        {
            return new OutgoingMessage(inner, null, null, DateTime.UtcNow.AddSeconds(10), true, null);
        }

        public static OutgoingMessage Ack(List<string> ids)
        {
            return new OutgoingMessage(null, ids, null, DateTime.UtcNow.AddMinutes(5), false, null);
        }

        public static OutgoingMessage Delete(string reason, TimeSpan patience)
        {
            return new OutgoingMessage(null, null, reason, DateTime.UtcNow.Add(patience), false, null);
        }
    }
}
