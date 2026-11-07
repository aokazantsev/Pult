using System;
using System.Collections.Generic;
using System.Linq;

namespace Pult
{
    internal sealed class TailThread
    {
        public string Thread;
        public long LastSeq;
        public bool OpenRequest;
        public long LastEvent;
        public long EntryBytes;
    }

    internal static class TailPlan
    {
        public const int KnownLimit = 200;
        public const int NewLimit = 50;
        public const long Budget = 1024 * 1024;

        public static Dictionary<string, long[]> Compute(IDictionary<string, long> known, IList<TailThread> threads)
        {
            return Compute(known, threads, (thread, first, last) => (last - first + 1) * thread.EntryBytes);
        }

        public static Dictionary<string, long[]> Compute(IDictionary<string, long> known, IList<TailThread> threads, Func<TailThread, long, long, long> cost)
        {
            var order = new List<TailThread>(threads.OrderBy(t => t.OpenRequest ? 0 : 1).ThenByDescending(t => t.LastEvent));
            long left = Budget;
            var result = new Dictionary<string, long[]>();
            foreach (TailThread thread in order)
            {
                long last = thread.LastSeq;
                long first;
                long seen;
                if (known.TryGetValue(thread.Thread, out seen)) first = Math.Max(seen + 1, last - KnownLimit + 1);
                else first = Math.Max(1, last - NewLimit + 1);
                if (first > last)
                {
                    result[thread.Thread] = null;
                    continue;
                }
                long price = cost(thread, first, last);
                if (price > left)
                {
                    result[thread.Thread] = null;
                    continue;
                }
                left -= price;
                result[thread.Thread] = new[] { first, last };
            }
            return result;
        }
    }
}
