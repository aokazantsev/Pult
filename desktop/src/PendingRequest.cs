using System;
using System.Collections.Generic;

namespace Pult
{
    internal sealed class PendingRequest
    {
        public readonly string Id;
        public readonly RequestKind Kind;
        public readonly string SessionId;
        public readonly string ToolName;
        public readonly string ToolInputKey;
        public readonly DateTime DeadlineUtc;
        public readonly TranscriptCursor Transcript;
        public readonly Dictionary<string, object> Message;

        public bool Released;
        public bool NotifyPhone = true;
        public string Output;
        public string Outcome;
        public string Cause;

        public PendingRequest(string id, RequestKind kind, HookEvent hookEvent, DateTime deadlineUtc, Dictionary<string, object> message)
        {
            Id = id;
            Kind = kind;
            SessionId = hookEvent.SessionId;
            ToolName = hookEvent.ToolName;
            ToolInputKey = kind == RequestKind.Permission ? hookEvent.ToolInputKey : null;
            DeadlineUtc = deadlineUtc;
            Transcript = TranscriptCursor.AtEnd(hookEvent.TranscriptPath);
            Message = message;
        }
    }
}
