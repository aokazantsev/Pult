using System;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace Pult
{
    internal sealed class HookListener : IDisposable
    {
        private const int MaxHeaderBytes = 16 * 1024;
        private const int MaxBodyBytes = 1024 * 1024;
        private const int SocketTimeoutMs = 3000;
        private const string ContentLengthHeader = "content-length:";
        private const string OriginHeader = "origin:";

        private static readonly byte[] DefaultResponse = Encoding.ASCII.GetBytes(
            "HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: 2\r\nConnection: close\r\n\r\n{}");
        private static readonly byte[] BadRequestResponse =
            Encoding.ASCII.GetBytes("HTTP/1.1 400 Bad Request\r\nContent-Length: 0\r\nConnection: close\r\n\r\n");
        private static readonly byte[] ForbiddenResponse =
            Encoding.ASCII.GetBytes("HTTP/1.1 403 Forbidden\r\nContent-Length: 0\r\nConnection: close\r\n\r\n");

        private readonly TcpListener listener;
        private readonly int port;
        private volatile bool stopped;

        public HookListener(int port)
        {
            listener = new TcpListener(IPAddress.Loopback, port);
            this.port = port;
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
                listener.BeginAcceptTcpClient(OnAccept, null);
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
            TcpClient client;
            try
            {
                client = listener.EndAcceptTcpClient(result);
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
                client.Close();
                return;
            }
            BeginAccept();
            using (client)
            {
                try
                {
                    Receive(client);
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

        private void Receive(TcpClient client)
        {
            client.ReceiveTimeout = SocketTimeoutMs;
            client.SendTimeout = SocketTimeoutMs;
            NetworkStream stream = client.GetStream();
            string route;
            int bodyLength;
            byte[] response = ReadRequest(stream, out route, out bodyLength);
            if (response == DefaultResponse) AppLog.Append("hook " + route + ": " + bodyLength + " bytes, default reply");
            stream.Write(response, 0, response.Length);
            stream.Flush();
            client.Client.Shutdown(SocketShutdown.Send);
        }

        private byte[] ReadRequest(NetworkStream stream, out string route, out int bodyLength)
        {
            route = null;
            bodyLength = 0;
            var buffer = new byte[8192];
            var received = new MemoryStream();
            int headerEnd = -1;
            while (headerEnd < 0)
            {
                int read = stream.Read(buffer, 0, buffer.Length);
                if (read <= 0) return BadRequestResponse;
                received.Write(buffer, 0, read);
                headerEnd = FindHeaderEnd(received.GetBuffer(), (int)received.Length);
                if (headerEnd < 0 && received.Length > MaxHeaderBytes) return BadRequestResponse;
            }
            string headers = Encoding.ASCII.GetString(received.GetBuffer(), 0, headerEnd).ToLowerInvariant();
            string[] lines = headers.Split(new[] { "\r\n" }, StringSplitOptions.None);
            string[] request = lines[0].Split(' ');
            if (request.Length < 2 || request[0] != "post" || !request[1].StartsWith("/hook", StringComparison.Ordinal)) return BadRequestResponse;
            route = request[1];
            if (!HasAllowedHost(lines) || HasHeader(lines, OriginHeader)) return ForbiddenResponse;
            int contentLength = ParseContentLength(lines);
            if (contentLength < 0 || contentLength > MaxBodyBytes) return BadRequestResponse;
            int have = (int)received.Length - (headerEnd + 4);
            while (have < contentLength)
            {
                int read = stream.Read(buffer, 0, buffer.Length);
                if (read <= 0) return BadRequestResponse;
                have += read;
            }
            bodyLength = contentLength;
            return DefaultResponse;
        }

        private bool HasAllowedHost(string[] lines)
        {
            foreach (string line in lines)
            {
                if (!line.StartsWith("host:", StringComparison.Ordinal)) continue;
                string host = line.Substring("host:".Length).Trim();
                return host == "127.0.0.1:" + port || host == "localhost:" + port;
            }
            return false;
        }

        private static bool HasHeader(string[] lines, string name)
        {
            foreach (string line in lines)
            {
                if (line.StartsWith(name, StringComparison.Ordinal)) return true;
            }
            return false;
        }

        private static int ParseContentLength(string[] lines)
        {
            foreach (string line in lines)
            {
                if (!line.StartsWith(ContentLengthHeader, StringComparison.Ordinal)) continue;
                int length;
                string value = line.Substring(ContentLengthHeader.Length).Trim();
                return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out length) ? length : -1;
            }
            return 0;
        }

        private static int FindHeaderEnd(byte[] data, int length)
        {
            for (int i = 0; i + 3 < length; i++)
            {
                if (data[i] == '\r' && data[i + 1] == '\n' && data[i + 2] == '\r' && data[i + 3] == '\n') return i;
            }
            return -1;
        }
    }
}
