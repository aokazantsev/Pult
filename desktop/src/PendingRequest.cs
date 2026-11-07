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
        public readonly object ToolInput;
        public readonly DateTime DeadlineUtc;
        public readonly TranscriptCursor Transcript;
        public readonly Dictionary<string, object> Message;
        public readonly ThreadState Thread;

        public bool Released;
        public bool Handed;
        public bool Ghost;
        public bool NotifyPhone = true;
        public string Output;
        public string Outcome;
        public string Cause;
        public string Summary;

        public PendingRequest(string id, RequestKind kind, HookEvent hookEvent, DateTime deadlineUtc, Dictionary<string, object> message, ThreadState thread)
        {
            Id = id;
            Kind = kind;
            SessionId = hookEvent.SessionId;
            ToolName = hookEvent.ToolName;
            ToolInputKey = kind != RequestKind.Text ? hookEvent.ToolInputKey : null;
            ToolInput = hookEvent.ToolInput;
            DeadlineUtc = deadlineUtc;
            Transcript = TranscriptCursor.AtEnd(hookEvent.TranscriptPath);
            Message = message;
            Thread = thread;
        }

        public string KindName
        {
            get { return Kind == RequestKind.Permission ? "permission" : Kind == RequestKind.Choice ? "choice" : "text"; }
        }
    }
}
