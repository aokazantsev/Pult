using System;
using System.Collections.Generic;

namespace Pult
{
    internal interface IRelay
    {
        bool Configured { get; }

        bool PhoneStreamOpen { get; }

        bool PhoneCompatible { get; }

        void Send(Dictionary<string, object> inner, DateTime expiresAtUtc, Action rejected);

        void SendLive(Dictionary<string, object> inner);

        void SendLive(Dictionary<string, object> inner, Action accepted);
    }
}
