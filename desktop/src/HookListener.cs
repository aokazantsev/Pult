using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace Pult
{
    internal sealed class HookListener : IDisposable
    {
        private const int MaxRequestBytes = 2 * 1024 * 1024;
        private const int RequestTimeoutMs = 5000;
        private static readonly TimeSpan DefaultHookLimit = TimeSpan.FromSeconds(60);

        private readonly TcpListener listener;
        private readonly string secret;
        private readonly Broker broker;
        private volatile bool stopped;

        public HookListener(int port, string secret, Broker broker)
        {
            listener = new TcpListener(IPAddress.Loopback, port);
            listener.ExclusiveAddressUse = true;
            this.secret = secret;
            this.broker = broker;
        }

        public void Start()
        {
            listener.Start();
            BeginAccept();
        }

        public void Dispose()
        {
            stopped = true;
            listener.Stop();
        }

        private void BeginAccept()
        {
            try
            {
                listener.BeginAcceptSocket(OnAccept, null);
            }
            catch (ObjectDisposedException)
            {
            }
            catch (SocketException)
            {
            }
        }

        private void OnAccept(IAsyncResult result)
        {
            Socket socket;
            try
            {
                socket = listener.EndAcceptSocket(result);
            }
            catch (ObjectDisposedException)
            {
                return;
            }
            catch (SocketException)
            {
                if (!stopped) BeginAccept();
                return;
            }
            if (stopped)
            {
                socket.Close();
                return;
            }
            BeginAccept();
            var thread = new Thread(() => Serve(socket)) { IsBackground = true, Name = "hook" };
            thread.Start();
        }

        private void Serve(Socket socket)
        {
            using (socket)
            {
                try
                {
                    var line = new LineSocket(socket);
                    string text = line.ReadLine(RequestTimeoutMs, MaxRequestBytes);
                    if (text == null) return;
                    Dictionary<string, object> request = Json.TryParseObject(text);
                    if (request == null)
                    {
                        AppLog.Append("hook request rejected: not json");
                        return;
                    }
                    if (!HookSecret.Matches(secret, Json.String(request, "secret")))
                    {
                        AppLog.Append("hook request rejected: wrong secret");
                        return;
                    }
                    var hookEvent = new HookEvent(Json.Object(request, "event"), Json.Object(request, "env"));
                    long limitMs = Json.Long(request, "limit_ms", (long)DefaultHookLimit.TotalMilliseconds);
                    broker.Handle(hookEvent, TimeSpan.FromMilliseconds(limitMs), new HookChannel(line));
                }
                catch (IOException)
                {
                }
                catch (SocketException)
                {
                }
                catch (ObjectDisposedException)
                {
                }
                catch (Exception error)
                {
                    AppLog.Append("hook request failed: " + error);
                }
            }
        }
    }
}
