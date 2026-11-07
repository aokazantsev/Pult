using System;
using System.Collections.Generic;

namespace Pult
{
    internal sealed class OfflineRelay : IRelay
    {
        public bool Configured
        {
            get { return false; }
        }

        public bool PhoneCompatible
        {
            get { return true; }
        }

        public bool PhoneStreamOpen
        {
            get { return false; }
        }

        public void Send(Dictionary<string, object> inner, DateTime expiresAtUtc, Action rejected)
        {
            rejected();
        }

        public void SendLive(Dictionary<string, object> inner)
        {
        }
    }
}
