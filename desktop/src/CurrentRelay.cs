using System;
using System.Collections.Generic;

namespace Pult
{
    internal sealed class CurrentRelay : IRelay
    {
        private static readonly IRelay Offline = new OfflineRelay();
        private volatile IRelay current = Offline;

        public IRelay Current
        {
            get { return current; }
            set { current = value ?? Offline; }
        }

        public bool Configured
        {
            get { return current.Configured; }
        }

        public bool PhoneStreamOpen
        {
            get { return current.PhoneStreamOpen; }
        }

        public bool PhoneCompatible
        {
            get { return current.PhoneCompatible; }
        }

        public void Send(Dictionary<string, object> inner, DateTime expiresAtUtc, Action rejected)
        {
            current.Send(inner, expiresAtUtc, rejected);
        }

        public void SendLive(Dictionary<string, object> inner)
        {
            current.SendLive(inner);
        }

        public void SendLive(Dictionary<string, object> inner, Action accepted)
        {
            current.SendLive(inner, accepted);
        }
    }
}
