using System.Collections.Generic;

namespace Pult
{
    internal static class ToolInputs
    {
        public const int SummaryLimit = 300;
        public const int ValueLimit = 1000;

        public static string Summary(string toolName, object input)
        {
            var map = input as Dictionary<string, object>;
            foreach (string key in new[] { "command", "file_path", "path", "url", "pattern", "description", "prompt" })
            {
                string value = Json.String(map, key);
                if (!string.IsNullOrEmpty(value)) return MessageRules.Cut(value, SummaryLimit);
            }
            return MessageRules.Cut(toolName, SummaryLimit);
        }

        public static Dictionary<string, object> Trim(object input)
        {
            var map = input as Dictionary<string, object>;
            if (map == null) return null;
            var trimmed = new Dictionary<string, object>();
            foreach (KeyValuePair<string, object> pair in map)
            {
                object value = pair.Value;
                if (value == null) continue;
                var text = value as string;
                if (text != null) trimmed[pair.Key] = MessageRules.Cut(text, ValueLimit);
                else if (value is Dictionary<string, object> || value is object[] || value is System.Collections.IList) trimmed[pair.Key] = MessageRules.Cut(Json.Write(value), ValueLimit);
                else trimmed[pair.Key] = value;
            }
            return trimmed;
        }
    }
}
