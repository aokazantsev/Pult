using System.Threading;

namespace Pult.Tests
{
    internal sealed class FakeChannel : IHookChannel
    {
        private readonly HookAck ack;
        private readonly ManualResetEvent done = new ManualResetEvent(false);

        public FakeChannel(HookAck ack)
        {
            this.ack = ack;
        }

        public volatile bool Gone;
        public string Output;
        public int Deliveries;

        public bool IsGone
        {
            get { return Gone; }
        }

        public HookAck Deliver(string output)
        {
            Output = output;
            Interlocked.Increment(ref Deliveries);
            done.Set();
            return ack;
        }

        public bool WaitDelivered(int timeoutMs)
        {
            return done.WaitOne(timeoutMs);
        }
    }
}
