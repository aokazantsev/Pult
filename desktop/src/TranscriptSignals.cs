using System;
using System.Collections;
using System.Collections.Generic;

namespace Pult
{
    internal sealed class TranscriptSignals
    {
        private const string InterruptedForToolUse = "[Request interrupted by user for tool use]";
        private const string UserRejected = "user-rejected";
        private const string QueueOperation = "queue-operation";

        public bool DeniedOnPc;
        public bool Enqueued;

        public static TranscriptSignals Scan(IEnumerable<string> lines)
        {
            var signals = new TranscriptSignals();
            foreach (string line in lines)
            {
                bool maybeQueue = line.IndexOf(QueueOperation, StringComparison.Ordinal) >= 0;
                bool maybeDenial = line.IndexOf(UserRejected, StringComparison.Ordinal) >= 0
                    || line.IndexOf(InterruptedForToolUse, StringComparison.Ordinal) >= 0;
                if (!maybeQueue && !maybeDenial) continue;
                Dictionary<string, object> record = Json.TryParseObject(line);
                if (record == null) continue;
                string type = Json.String(record, "type");
                if (type == QueueOperation && Json.String(record, "operation") == "enqueue") signals.Enqueued = true;
                if (type == "user" && IsDenial(record)) signals.DeniedOnPc = true;
            }
            return signals;
        }

        private static bool IsDenial(Dictionary<string, object> record)
        {
            if (Json.String(record, "toolDenialKind") == UserRejected) return true;
            var content = Json.Value(Json.Object(record, "message"), "content") as IList;
            if (content == null) return false;
            foreach (object item in content)
            {
                var block = item as Dictionary<string, object>;
                if (block != null && Json.String(block, "type") == "text" && Json.String(block, "text") == InterruptedForToolUse) return true;
            }
            return false;
        }
    }
}
