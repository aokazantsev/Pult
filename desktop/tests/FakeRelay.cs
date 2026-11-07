using System;
using System.Collections.Generic;
using System.Linq;

namespace Pult.Tests
{
    internal sealed class SentMessage
    {
        public Dictionary<string, object> Inner;
        public bool Live;
        public DateTime ExpiresAtUtc;
        public Action Accepted;
    }

    internal sealed class FakeRelay : IRelay
    {
        private readonly object gate = new object();
        private readonly List<SentMessage> log = new List<SentMessage>();
        private readonly List<string> problems = new List<string>();

        public bool Configured
        {
            get { return true; }
        }

        public volatile bool PhoneOpen = true;
        public bool RejectAll;
        public bool AcceptAll = true;

        public bool PhoneCompatible
        {
            get { return true; }
        }

        public bool PhoneStreamOpen
        {
            get { return PhoneOpen; }
        }

        public void Send(Dictionary<string, object> inner, DateTime expiresAtUtc, Action rejected)
        {
            Record(inner, false, expiresAtUtc, null);
            if (RejectAll) rejected();
        }

        public void SendLive(Dictionary<string, object> inner)
        {
            SendLive(inner, null);
        }

        public void SendLive(Dictionary<string, object> inner, Action accepted)
        {
            Record(inner, true, DateTime.UtcNow, accepted);
            if (accepted != null && AcceptAll) accepted();
        }

        public List<Dictionary<string, object>> Sent(string type)
        {
            lock (gate) return log.Where(m => Json.String(m.Inner, "type") == type).Select(m => m.Inner).ToList();
        }

        public List<SentMessage> Log
        {
            get
            {
                lock (gate) return new List<SentMessage>(log);
            }
        }

        public List<string> Problems
        {
            get
            {
                lock (gate) return new List<string>(problems);
            }
        }

        public void Clear()
        {
            lock (gate) log.Clear();
        }

        private void Record(Dictionary<string, object> inner, bool live, DateTime expires, Action accepted)
        {
            string problem = MessageRules.Check(inner, "phone");
            lock (gate)
            {
                log.Add(new SentMessage { Inner = inner, Live = live, ExpiresAtUtc = expires, Accepted = accepted });
                if (problem != null) problems.Add(Json.String(inner, "type") + ": " + problem);
            }
        }
    }
}
