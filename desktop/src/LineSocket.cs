using System.IO;
using System.Net.Sockets;
using System.Text;

namespace Pult
{
    internal sealed class LineSocket
    {
        private readonly Socket socket;
        private readonly NetworkStream stream;
        private readonly MemoryStream pending = new MemoryStream();
        private readonly byte[] buffer = new byte[8192];

        public LineSocket(Socket socket)
        {
            this.socket = socket;
            stream = new NetworkStream(socket, false);
        }

        public string ReadLine(int timeoutMs, int maxBytes)
        {
            socket.ReceiveTimeout = timeoutMs;
            while (true)
            {
                byte[] data = pending.GetBuffer();
                int length = (int)pending.Length;
                for (int i = 0; i < length; i++)
                {
                    if (data[i] != (byte)'\n') continue;
                    string line = Encoding.UTF8.GetString(data, 0, i).TrimEnd('\r');
                    var rest = new byte[length - i - 1];
                    System.Array.Copy(data, i + 1, rest, 0, rest.Length);
                    pending.SetLength(0);
                    pending.Write(rest, 0, rest.Length);
                    return line;
                }
                if (length > maxBytes) return null;
                int read = stream.Read(buffer, 0, buffer.Length);
                if (read <= 0) return null;
                pending.Write(buffer, 0, read);
            }
        }

        public void WriteLine(string line, int timeoutMs)
        {
            socket.SendTimeout = timeoutMs;
            byte[] bytes = Encoding.UTF8.GetBytes(line + "\n");
            stream.Write(bytes, 0, bytes.Length);
            stream.Flush();
        }

        public bool IsClosedByPeer
        {
            get
            {
                try
                {
                    return pending.Length == 0 && socket.Poll(0, SelectMode.SelectRead) && socket.Available == 0;
                }
                catch (SocketException)
                {
                    return true;
                }
                catch (System.ObjectDisposedException)
                {
                    return true;
                }
            }
        }
    }
}
