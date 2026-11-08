using System.Collections.Generic;

namespace Pult
{
    internal sealed class PairRequest
    {
        public readonly List<string> Folders = new List<string>();
        public string Server;
        public int IdleSeconds;

        public static PairRequest From(ControlCommand command)
        {
            var request = new PairRequest { Server = command.Server, IdleSeconds = command.IdleSeconds };
            request.Folders.AddRange(command.Folders);
            return request;
        }
    }
}
