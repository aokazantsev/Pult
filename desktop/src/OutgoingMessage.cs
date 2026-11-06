using System;
using System.Collections.Generic;

namespace Pult
{
    internal sealed class OutgoingMessage
    {
        public readonly string Path;
        public readonly Dictionary<string, object> Inner;
        public readonly List<string> AckIds;
        public readonly DateTime ExpiresAtUtc;
        public readonly bool IsLive;
        public readonly Action Rejected;

        private OutgoingMessage(string path, Dictionary<string, object> inner, List<string> ackIds, DateTime expiresAtUtc, bool live, Action rejected)
        {
            Path = path;
            Inner = inner;
            AckIds = ackIds;
            ExpiresAtUtc = expiresAtUtc;
            IsLive = live;
            Rejected = rejected;
        }

        public static OutgoingMessage Queued(Dictionary<string, object> inner, DateTime expiresAtUtc, Action rejected)
        {
            return new OutgoingMessage("messages", inner, null, expiresAtUtc, false, rejected);
        }

        public static OutgoingMessage Live(Dictionary<string, object> inner)
        {
            return new OutgoingMessage("messages", inner, null, DateTime.UtcNow.AddSeconds(10), true, null);
        }

        public static OutgoingMessage Ack(List<string> ids)
        {
            return new OutgoingMessage("acks", null, ids, DateTime.UtcNow.AddMinutes(5), false, null);
        }
    }
}
