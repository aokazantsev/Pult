using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Pult
{
    internal sealed class ProcessedIds
    {
        private readonly object gate = new object();
        private readonly string path;
        private readonly int limit;
        private readonly HashSet<string> known = new HashSet<string>();
        private readonly Queue<string> order = new Queue<string>();
        private int fileLines;

        public ProcessedIds(string path, int limit)
        {
            this.path = path;
            this.limit = limit;
            Load();
        }

        public bool Add(string id)
        {
            if (string.IsNullOrEmpty(id) || id.IndexOf('\n') >= 0 || id.IndexOf('\r') >= 0) return false;
            lock (gate)
            {
                if (!known.Add(id)) return false;
                order.Enqueue(id);
                while (order.Count > limit) known.Remove(order.Dequeue());
                Persist(id);
                return true;
            }
        }

        private void Load()
        {
            if (path == null) return;
            try
            {
                if (!File.Exists(path)) return;
                foreach (string line in File.ReadAllLines(path, new UTF8Encoding(false)))
                {
                    fileLines++;
                    string id = line.Trim();
                    if (id.Length == 0 || !known.Add(id)) continue;
                    order.Enqueue(id);
                    while (order.Count > limit) known.Remove(order.Dequeue());
                }
            }
            catch (IOException)
            {
            }
            catch (System.UnauthorizedAccessException)
            {
            }
        }

        private void Persist(string id)
        {
            if (path == null) return;
            try
            {
                if (fileLines >= limit * 2)
                {
                    string temp = path + ".tmp";
                    File.WriteAllLines(temp, order.ToArray(), new UTF8Encoding(false));
                    if (File.Exists(path)) File.Delete(path);
                    File.Move(temp, path);
                    fileLines = order.Count;
                    return;
                }
                File.AppendAllText(path, id + "\n", new UTF8Encoding(false));
                fileLines++;
            }
            catch (IOException)
            {
            }
            catch (System.UnauthorizedAccessException)
            {
            }
        }
    }
}
