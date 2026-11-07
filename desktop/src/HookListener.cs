using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace Pult
{
    internal enum HookListenerState
    {
        Stopped,
        Listening,
        Retrying,
        Failed
    }

    internal sealed class HookListener : IDisposable
    {
        public const int DefaultRetryIntervalMs = 1000;
        public const int DefaultRetryLimitMs = 6 * 60 * 1000;

        private const int MaxRequestBytes = 2 * 1024 * 1024;
        private const int RequestTimeoutMs = 5000;
        private static readonly TimeSpan DefaultHookLimit = TimeSpan.FromSeconds(60);

        private readonly object gate = new object();
        private readonly int port;
        private readonly string secret;
        private readonly Broker broker;
        private readonly int retryIntervalMs;
        private readonly int retryLimitMs;
        private TcpListener listener;
        private volatile HookListenerState state = HookListenerState.Stopped;
        private volatile bool stopped;

        public HookListener(int port, string secret, Broker broker)
            : this(port, secret, broker, DefaultRetryIntervalMs, DefaultRetryLimitMs)
        {
        }

        public HookListener(int port, string secret, Broker broker, int retryIntervalMs, int retryLimitMs)
        {
            this.port = port;
            this.secret = secret;
            this.broker = broker;
            this.retryIntervalMs = retryIntervalMs;
            this.retryLimitMs = retryLimitMs;
        }

        public HookListenerState State
        {
            get { return state; }
        }

        public event Action StateChanged;

        public HookListenerState Start()
        {
            try
            {
                if (TryBind())
                {
                    SetState(HookListenerState.Listening);
                    return state;
                }
            }
            catch (SocketException error)
            {
                AppLog.Append("hook listener not started on port " + port + ": " + error.Message);
                SetState(HookListenerState.Failed);
                return state;
            }
            SetState(HookListenerState.Retrying);
            var thread = new Thread(RetryBinding) { IsBackground = true, Name = "hook-bind" };
            thread.Start();
            return state;
        }

        public void Dispose()
        {
            stopped = true;
            lock (gate)
            {
                if (listener != null) listener.Stop();
            }
        }

        private bool TryBind()
        {
            var candidate = new TcpListener(IPAddress.Loopback, port);
            candidate.ExclusiveAddressUse = true;
            try
            {
                candidate.Start();
            }
            catch (SocketException error)
            {
                candidate.Stop();
                if (error.SocketErrorCode != SocketError.AddressAlreadyInUse && error.SocketErrorCode != SocketError.AccessDenied) throw;
                AppLog.Append("hook listener: port " + port + " busy (" + error.SocketErrorCode + ")");
                return false;
            }
            lock (gate)
            {
                if (stopped)
                {
                    candidate.Stop();
                    return true;
                }
                listener = candidate;
            }
            BeginAccept();
            return true;
        }

        private void RetryBinding()
        {
            DateTime limit = DateTime.UtcNow.AddMilliseconds(retryLimitMs);
            while (!stopped && DateTime.UtcNow < limit)
            {
                Thread.Sleep(retryIntervalMs);
                if (stopped) return;
                bool bound;
                try
                {
                    bound = TryBind();
                }
                catch (SocketException error)
                {
                    AppLog.Append("hook listener not started on port " + port + ": " + error.Message);
                    SetState(HookListenerState.Failed);
                    return;
                }
                if (bound)
                {
                    AppLog.Append("hook listener: port " + port + " bound after retries");
                    SetState(HookListenerState.Listening);
                    return;
                }
            }
            if (!stopped) SetState(HookListenerState.Failed);
        }

        private void SetState(HookListenerState value)
        {
            state = value;
            Action handler = StateChanged;
            if (handler != null) handler();
        }

        private void BeginAccept()
        {
            try
            {
                TcpListener current;
                lock (gate) current = listener;
                current.BeginAcceptSocket(OnAccept, null);
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
                TcpListener current;
                lock (gate) current = listener;
                socket = current.EndAcceptSocket(result);
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
