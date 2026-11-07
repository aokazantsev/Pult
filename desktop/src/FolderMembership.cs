using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Pult
{
    internal static class FolderMembership
    {
        public static string Normalize(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return null;
            string text = path.Trim().Replace('/', '\\');
            while (text.Length > 3 && text.EndsWith("\\", StringComparison.Ordinal)) text = text.Substring(0, text.Length - 1);
            return text;
        }

        public static string NameOf(string folder)
        {
            string normalized = Normalize(folder);
            if (normalized == null) return "…";
            string name = normalized.Substring(normalized.LastIndexOf('\\') + 1);
            if (name.Length == 0) name = normalized;
            return MessageRules.Cut(name, 120);
        }

        public static bool IsUnder(string path, string folder)
        {
            string a = Normalize(path);
            string b = Normalize(folder);
            if (a == null || b == null) return false;
            if (string.Equals(a, b, StringComparison.OrdinalIgnoreCase)) return true;
            string prefix = b.EndsWith("\\", StringComparison.Ordinal) ? b : b + "\\";
            return a.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
        }

        public static string MarkedFolderFor(string origin, IList<string> marked)
        {
            string best = null;
            foreach (string folder in marked)
            {
                if (!IsUnder(origin, folder)) continue;
                if (best == null || Normalize(folder).Length > Normalize(best).Length) best = folder;
            }
            return best;
        }

        public static string Distort(string path)
        {
            string normalized = Normalize(path);
            if (normalized == null) return "";
            var text = new StringBuilder();
            foreach (char c in normalized) text.Append((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') ? c : '-');
            return text.ToString();
        }

        public static string FromTranscriptDirectory(string transcriptPath, IList<string> marked)
        {
            if (string.IsNullOrEmpty(transcriptPath)) return null;
            string directory;
            try
            {
                directory = Path.GetFileName(Path.GetDirectoryName(transcriptPath));
            }
            catch (ArgumentException)
            {
                return null;
            }
            if (string.IsNullOrEmpty(directory)) return null;
            string match = null;
            foreach (string folder in marked)
            {
                string distorted = Distort(folder);
                bool hit = string.Equals(directory, distorted, StringComparison.OrdinalIgnoreCase)
                    || directory.StartsWith(distorted + "-", StringComparison.OrdinalIgnoreCase);
                if (hit && (match == null || distorted.Length > Distort(match).Length)) match = folder;
            }
            if (match == null) return null;
            foreach (string folder in marked)
            {
                if (Normalize(folder) != Normalize(match) && Distort(folder) == Distort(match)) return null;
            }
            return match;
        }
    }
}
