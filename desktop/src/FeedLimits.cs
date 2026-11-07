using System;
using System.Collections;
using System.Collections.Generic;

namespace Pult
{
    internal static class FeedLimits
    {
        public const int TextLimit = 8000;
        public const int OutputLimit = 2000;
        public const int NoticeLimit = 300;
        private const int Reserve = 64;

        private static readonly HashSet<string> Fixed = new HashSet<string>(StringComparer.Ordinal)
        {
            "type", "kind", "seq", "at", "call", "tool", "request_id", "thread", "peer", "via", "direction", "id", "error", "status", "mode"
        };

        public static bool Fit(Dictionary<string, object> item)
        {
            bool cut = false;
            for (int guard = 0; guard < 256 && MessageRules.Size(item) > MessageRules.ItemBytes - Reserve; guard++)
            {
                IDictionary holder = null;
                IList list = null;
                object key = null;
                int longest = 1;
                Find(item, ref holder, ref list, ref key, ref longest);
                if (key == null) break;
                string text = holder != null ? (string)holder[key] : (string)list[(int)key];
                string shorter = MessageRules.Cut(text, longest / 2);
                if (holder != null) holder[key] = shorter;
                else list[(int)key] = shorter;
                cut = true;
            }
            if (cut && item.ContainsKey("kind")) item["truncated"] = true;
            return cut;
        }

        private static void Find(object value, ref IDictionary holder, ref IList list, ref object key, ref int longest)
        {
            var map = value as IDictionary;
            if (map != null)
            {
                foreach (DictionaryEntry pair in map)
                {
                    var name = pair.Key as string;
                    if (name != null && Fixed.Contains(name)) continue;
                    var text = pair.Value as string;
                    if (text != null)
                    {
                        int length = MessageRules.Length(text);
                        if (length > longest)
                        {
                            longest = length;
                            holder = map;
                            list = null;
                            key = pair.Key;
                        }
                    }
                    else
                    {
                        Find(pair.Value, ref holder, ref list, ref key, ref longest);
                    }
                }
                return;
            }
            var items = value as IList;
            if (items == null) return;
            for (int i = 0; i < items.Count; i++)
            {
                var text = items[i] as string;
                if (text != null)
                {
                    int length = MessageRules.Length(text);
                    if (length > longest && !items.IsFixedSize)
                    {
                        longest = length;
                        holder = null;
                        list = items;
                        key = i;
                    }
                }
                else
                {
                    Find(items[i], ref holder, ref list, ref key, ref longest);
                }
            }
        }
    }
}
