using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Pult
{
    internal static class StateFiles
    {
        public static bool Write(string path, object value)
        {
            string temp = path + ".tmp";
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(temp, Json.Write(value), new UTF8Encoding(false));
                if (File.Exists(path)) File.Replace(temp, path, null);
                else File.Move(temp, path);
                return true;
            }
            catch (IOException error)
            {
                AppLog.Append(Path.GetFileName(path) + " not saved: " + error.Message);
                return false;
            }
            catch (UnauthorizedAccessException error)
            {
                AppLog.Append(Path.GetFileName(path) + " not saved: " + error.Message);
                return false;
            }
        }

        public static Dictionary<string, object> Read(string path)
        {
            try
            {
                if (!File.Exists(path)) return null;
                return Json.TryParseObject(File.ReadAllText(path, new UTF8Encoding(false)));
            }
            catch (IOException error)
            {
                AppLog.Append(Path.GetFileName(path) + " not read: " + error.Message);
                return null;
            }
            catch (UnauthorizedAccessException error)
            {
                AppLog.Append(Path.GetFileName(path) + " not read: " + error.Message);
                return null;
            }
        }

        public static void Append(string path, IEnumerable<string> lines)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.AppendAllText(path, string.Join("\n", new List<string>(lines).ToArray()) + "\n", new UTF8Encoding(false));
            }
            catch (IOException error)
            {
                AppLog.Append(Path.GetFileName(path) + " not appended: " + error.Message);
            }
            catch (UnauthorizedAccessException error)
            {
                AppLog.Append(Path.GetFileName(path) + " not appended: " + error.Message);
            }
        }

        public static List<string> ReadLines(string path)
        {
            var lines = new List<string>();
            try
            {
                if (!File.Exists(path)) return lines;
                foreach (string line in File.ReadAllLines(path, new UTF8Encoding(false)))
                {
                    if (line.Length > 0) lines.Add(line);
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
            return lines;
        }

        public static void DeleteDirectory(string path)
        {
            try
            {
                if (Directory.Exists(path)) Directory.Delete(path, true);
            }
            catch (IOException error)
            {
                AppLog.Append(Path.GetFileName(path) + " not deleted: " + error.Message);
            }
            catch (UnauthorizedAccessException error)
            {
                AppLog.Append(Path.GetFileName(path) + " not deleted: " + error.Message);
            }
        }
    }
}
