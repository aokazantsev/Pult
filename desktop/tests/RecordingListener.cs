using System.Collections.Generic;
using System.Threading;

namespace Pult.Tests
{
    internal sealed class RecordingListener : IRelayListener, ISessionListener
    {
        private readonly object gate = new object();
        private readonly List<Dictionary<string, object>> messages = new List<Dictionary<string, object>>();
        private readonly List<string> notices = new List<string>();
        private int connected;
        private int paired;
        private int rejected;
        private string ended;

        public int Connected
        {
            get { return Thread.VolatileRead(ref connected); }
        }

        public int Messages
        {
            get
            {
                lock (gate) return messages.Count;
            }
        }

        public int Paired
        {
            get { return Thread.VolatileRead(ref paired); }
        }

        public int Rejected
        {
            get { return Thread.VolatileRead(ref rejected); }
        }

        public string Ended
        {
            get
            {
                lock (gate) return ended;
            }
        }

        public List<string> Notices
        {
            get
            {
                lock (gate) return new List<string>(notices);
            }
        }

        public void OnRelayConnected()
        {
            Interlocked.Increment(ref connected);
        }

        public void OnPhoneStreamChanged()
        {
        }

        public void OnPhoneMessage(Dictionary<string, object> inner)
        {
            lock (gate) messages.Add(inner);
        }

        public void OnPaired(RelayClient client)
        {
            Interlocked.Increment(ref paired);
        }

        public void OnPairingRejected(RelayClient client)
        {
            Interlocked.Increment(ref rejected);
        }

        public void OnPairEnded(RelayClient client, string reason, string by)
        {
            lock (gate) ended = reason + "/" + by;
        }

        public void OnNotice(RelayClient client, string text)
        {
            lock (gate) notices.Add(text);
        }
    }
}
