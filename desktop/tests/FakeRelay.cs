using System;
using System.Collections.Generic;
using System.Linq;

namespace Pult.Tests
{
    internal sealed class FakeRelay : IRelay
    {
        private readonly object gate = new object();
        private readonly List<Dictionary<string, object>> sent = new List<Dictionary<string, object>>();
        private readonly List<Action> rejections = new List<Action>();

        public bool Configured
        {
            get { return true; }
        }

        public volatile bool PhoneOpen = true;
        public bool RejectAll;

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
            lock (gate) sent.Add(inner);
            if (RejectAll) rejected();
        }

        public void SendLive(Dictionary<string, object> inner)
        {
            lock (gate) sent.Add(inner);
        }

        public List<Dictionary<string, object>> Sent(string type)
        {
            lock (gate) return sent.Where(m => Json.String(m, "type") == type).ToList();
        }
    }
}
