using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;

namespace Pult
{
    internal sealed class HookChannel : IHookChannel
    {
        private const int ReplyTimeoutMs = 5000;
        private const int AckTimeoutMs = 3000;

        private readonly LineSocket line;
        private bool delivered;

        public HookChannel(LineSocket line)
        {
            this.line = line;
        }

        public bool IsGone
        {
            get { return line.IsClosedByPeer; }
        }

        public HookAck Deliver(string output)
        {
            if (delivered) return HookAck.Lost;
            delivered = true;
            bool needsAck = !string.IsNullOrEmpty(output);
            try
            {
                line.WriteLine(Json.Write(new Dictionary<string, object> { { "output", output ?? "" }, { "ack", needsAck } }), ReplyTimeoutMs);
                if (!needsAck) return HookAck.Taken;
                Dictionary<string, object> reply = Json.TryParseObject(line.ReadLine(AckTimeoutMs, 4096) ?? "");
                if (reply == null) return HookAck.Lost;
                return Json.Bool(reply, "taken") ? HookAck.Taken : HookAck.Refused;
            }
            catch (IOException)
            {
                return HookAck.Lost;
            }
            catch (SocketException)
            {
                return HookAck.Lost;
            }
            catch (ObjectDisposedException)
            {
                return HookAck.Lost;
            }
        }
    }
}
