using System.Collections.Generic;
using System.Linq;

namespace Pult
{
    internal static class FeedLimits
    {
        public const int TextLimit = 8000;
        public const int OutputLimit = 2000;
        public const int NoticeLimit = 300;

        public static void Fit(Dictionary<string, object> item)
        {
            int guard = 64;
            while (MessageRules.Size(item) > MessageRules.ItemBytes && guard-- > 0)
            {
                string longest = null;
                int length = 0;
                foreach (KeyValuePair<string, object> pair in item)
                {
                    var text = pair.Value as string;
                    if (text != null && MessageRules.Length(text) > length && pair.Key != "kind" && pair.Key != "type")
                    {
                        longest = pair.Key;
                        length = MessageRules.Length(text);
                    }
                }
                if (longest == null || length <= 1) return;
                item[longest] = MessageRules.Cut((string)item[longest], length / 2);
                if (item.ContainsKey("seq")) item["truncated"] = true;
            }
        }
    }
}
