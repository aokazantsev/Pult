using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace Pult.Tests
{
    internal static class ListenerTests
    {
        private static int FreePort()
        {
            var probe = new TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            int port = ((IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();
            return port;
        }

        private static Broker NewBroker()
        {
            return new Broker(new OfflineRelay(), TimeSpan.FromSeconds(1), 20);
        }

        private static void Expect(bool condition, string message)
        {
            if (!condition) throw new Exception(message);
        }

        private static void WaitFor(Func<bool> condition, int milliseconds, string what)
        {
            DateTime limit = DateTime.UtcNow.AddMilliseconds(milliseconds);
            while (DateTime.UtcNow < limit)
            {
                if (condition()) return;
                Thread.Sleep(10);
            }
            throw new Exception("timeout: " + what);
        }

        private static bool Accepts(int port)
        {
            try
            {
                using (var client = new TcpClient())
                {
                    client.Connect(IPAddress.Loopback, port);
                    return true;
                }
            }
            catch (SocketException)
            {
                return false;
            }
        }

        public static void BindsAtOnce()
        {
            int port = FreePort();
            using (var listener = new HookListener(port, "s", NewBroker(), 20, 1000))
            {
                Expect(listener.Start() == HookListenerState.Listening, "listening at once");
                Expect(Accepts(port), "accepts connections");
            }
        }

        public static void RetriesUntilFree()
        {
            int port = FreePort();
            var occupier = new TcpListener(IPAddress.Loopback, port);
            occupier.ExclusiveAddressUse = true;
            occupier.Start();
            using (var listener = new HookListener(port, "s", NewBroker(), 20, 10000))
            {
                Expect(listener.Start() == HookListenerState.Retrying, "retrying while busy");
                Thread.Sleep(150);
                Expect(listener.State == HookListenerState.Retrying, "still retrying");
                occupier.Stop();
                WaitFor(() => listener.State == HookListenerState.Listening, 5000, "bound after release");
                Expect(Accepts(port), "accepts connections after retries");
            }
        }

        public static void FailsWhenStayingBusy()
        {
            int port = FreePort();
            var occupier = new TcpListener(IPAddress.Loopback, port);
            occupier.ExclusiveAddressUse = true;
            occupier.Start();
            try
            {
                using (var listener = new HookListener(port, "s", NewBroker(), 20, 300))
                {
                    listener.Start();
                    WaitFor(() => listener.State == HookListenerState.Failed, 5000, "failed after the limit");
                }
            }
            finally
            {
                occupier.Stop();
            }
        }

        public static void BusyPortIsNotShared()
        {
            int port = FreePort();
            using (var first = new HookListener(port, "s", NewBroker(), 20, 1000))
            using (var second = new HookListener(port, "s", NewBroker(), 20, 200))
            {
                Expect(first.Start() == HookListenerState.Listening, "first listens");
                second.Start();
                WaitFor(() => second.State == HookListenerState.Failed, 5000, "second does not take the port");
                Expect(first.State == HookListenerState.Listening, "first keeps the port");
            }
        }
    }
}
