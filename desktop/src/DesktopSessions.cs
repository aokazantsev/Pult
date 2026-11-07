using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Pult
{
    internal sealed class DesktopMeta
    {
        public string LocalId;
        public string CliSessionId;
        public string Cwd;
        public string OriginCwd;
        public string Title;
        public string SpawnedFromLocalId;
        public string SpawnedFromTitle;
        public string ForkedFromLocalId;
        public long LastActivity;
    }

    internal sealed class DesktopSessions
    {
        public static readonly TimeSpan DefaultRescanInterval = TimeSpan.FromSeconds(1);

        private readonly string root;
        private readonly TimeSpan rescanInterval;
        private readonly object gate = new object();
        private Dictionary<string, DesktopMeta> byLocal = new Dictionary<string, DesktopMeta>(StringComparer.Ordinal);
        private Dictionary<string, DesktopMeta> byCli = new Dictionary<string, DesktopMeta>(StringComparer.Ordinal);
        private DateTime scannedAt = DateTime.MinValue;

        public DesktopSessions(string root)
            : this(root, DefaultRescanInterval)
        {
        }

        public DesktopSessions(string root, TimeSpan rescanInterval)
        {
            this.root = root;
            this.rescanInterval = rescanInterval;
        }

        public static string DefaultRoot
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Claude", "claude-code-sessions"); }
        }

        public DesktopMeta ByLocalId(string localId)
        {
            return Find(localId, null);
        }

        public DesktopMeta Find(string localId, string cliSessionId)
        {
            lock (gate)
            {
                DesktopMeta found = Lookup(localId, cliSessionId);
                if (found == null && DateTime.UtcNow - scannedAt >= rescanInterval)
                {
                    Scan();
                    found = Lookup(localId, cliSessionId);
                }
                return found;
            }
        }

        public List<DesktopMeta> Recent(int limit)
        {
            lock (gate)
            {
                Scan();
                var folders = new Dictionary<string, DesktopMeta>(StringComparer.OrdinalIgnoreCase);
                foreach (DesktopMeta meta in byLocal.Values.OrderByDescending(m => m.LastActivity))
                {
                    string folder = meta.OriginCwd ?? meta.Cwd;
                    if (string.IsNullOrEmpty(folder) || folders.ContainsKey(folder)) continue;
                    folders[folder] = meta;
                    if (folders.Count >= limit) break;
                }
                return folders.Values.ToList();
            }
        }

        private DesktopMeta Lookup(string localId, string cliSessionId)
        {
            DesktopMeta found;
            if (localId != null && byLocal.TryGetValue(localId, out found)) return found;
            if (cliSessionId != null && byCli.TryGetValue(cliSessionId, out found)) return found;
            return null;
        }

        private void Scan()
        {
            scannedAt = DateTime.UtcNow;
            var local = new Dictionary<string, DesktopMeta>(StringComparer.Ordinal);
            var cli = new Dictionary<string, DesktopMeta>(StringComparer.Ordinal);
            try
            {
                if (!string.IsNullOrEmpty(root) && Directory.Exists(root))
                {
                    foreach (string file in Directory.EnumerateFiles(root, "local_*.json", SearchOption.AllDirectories))
                    {
                        DesktopMeta meta = Read(file);
                        if (meta == null) continue;
                        local[meta.LocalId] = meta;
                        foreach (string id in AllCliIds(file, meta)) cli[id] = meta;
                    }
                }
            }
            catch (IOException error)
            {
                AppLog.Append("desktop sessions not scanned: " + error.Message);
            }
            catch (UnauthorizedAccessException error)
            {
                AppLog.Append("desktop sessions not scanned: " + error.Message);
            }
            byLocal = local;
            byCli = cli;
        }

        private static IEnumerable<string> AllCliIds(string file, DesktopMeta meta)
        {
            var ids = new List<string>();
            if (!string.IsNullOrEmpty(meta.CliSessionId)) ids.Add(meta.CliSessionId);
            try
            {
                Dictionary<string, object> map = Json.TryParseObject(File.ReadAllText(file, new UTF8Encoding(false)));
                IList prior = Json.Value(map, "priorCliSessionIds") as IList;
                if (prior != null)
                {
                    foreach (object id in prior)
                    {
                        var text = id as string;
                        if (!string.IsNullOrEmpty(text)) ids.Add(text);
                    }
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
            return ids;
        }

        private static DesktopMeta Read(string file)
        {
            try
            {
                Dictionary<string, object> map = Json.TryParseObject(File.ReadAllText(file, new UTF8Encoding(false)));
                string local = Json.String(map, "sessionId");
                if (string.IsNullOrEmpty(local)) return null;
                Dictionary<string, object> spawned = Json.Object(map, "spawnedFrom");
                return new DesktopMeta
                {
                    LocalId = local,
                    CliSessionId = Json.String(map, "cliSessionId"),
                    Cwd = Json.String(map, "cwd"),
                    OriginCwd = Json.String(map, "originCwd"),
                    Title = Json.String(map, "title"),
                    SpawnedFromLocalId = Json.String(spawned, "sessionId"),
                    SpawnedFromTitle = Json.String(spawned, "title"),
                    ForkedFromLocalId = Json.String(map, "forkedFromSessionId"),
                    LastActivity = Json.Long(map, "lastActivityAt", 0)
                };
            }
            catch (IOException)
            {
                return null;
            }
            catch (UnauthorizedAccessException)
            {
                return null;
            }
        }
    }
}
