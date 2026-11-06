using System.Collections.Generic;

namespace Pult
{
    internal interface IRelayListener
    {
        void OnRelayConnected();

        void OnPhoneStreamChanged();

        void OnPhoneMessage(Dictionary<string, object> inner);
    }
}
