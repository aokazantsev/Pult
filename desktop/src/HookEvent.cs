using System.Collections.Generic;
using System.Globalization;

namespace Pult
{
    internal sealed class HookEvent
    {
        public const int DefaultStopBlockCap = 8;

        public readonly Dictionary<string, object> Data;
        public readonly Dictionary<string, object> Environment;

        public HookEvent(Dictionary<string, object> data, Dictionary<string, object> environment)
        {
            Data = data ?? new Dictionary<string, object>();
            Environment = environment ?? new Dictionary<string, object>();
        }

        public string Name
        {
            get { return Json.String(Data, "hook_event_name") ?? ""; }
        }

        public string SessionId
        {
            get { return Json.String(Data, "session_id") ?? ""; }
        }

        public string TranscriptPath
        {
            get { return Json.String(Data, "transcript_path"); }
        }

        public string HostSessionId
        {
            get
            {
                string id = Json.String(Environment, "CLAUDE_CODE_HOST_SESSION_ID");
                return string.IsNullOrWhiteSpace(id) ? null : id.Trim();
            }
        }

        public string Cwd
        {
            get { return Json.String(Data, "cwd"); }
        }

        public string ToolName
        {
            get { return Json.String(Data, "tool_name") ?? ""; }
        }

        public object ToolInput
        {
            get { return Json.Value(Data, "tool_input"); }
        }

        public string ToolInputKey
        {
            get { return Json.Write(ToolInput); }
        }

        public string Source
        {
            get { return Json.String(Data, "source"); }
        }

        public string SessionTitle
        {
            get { return Json.String(Data, "session_title"); }
        }

        public string LastAssistantMessage
        {
            get { return Json.String(Data, "last_assistant_message"); }
        }

        public bool StopHookActive
        {
            get { return Json.Bool(Data, "stop_hook_active"); }
        }

        public int StopBlockCap
        {
            get
            {
                int cap;
                string text = Json.String(Environment, "CLAUDE_CODE_STOP_HOOK_BLOCK_CAP");
                if (text != null && int.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out cap) && cap > 0) return cap;
                return DefaultStopBlockCap;
            }
        }
    }
}
