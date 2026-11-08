using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Pult
{
    internal sealed class PairRequest
    {
        public const string RequestFile = "pair-request.txt";
        public const string QrFile = "pair-qr.txt";
        private const string FolderKey = "folder=";

        public readonly List<string> Folders = new List<string>();

        public static PairRequest Take()
        {
            string path = UserDataPaths.File(RequestFile);
            if (!File.Exists(path)) return null;
            string[] lines;
            try
            {
                lines = File.ReadAllLines(path, new UTF8Encoding(false));
                File.Delete(path);
            }
            catch (IOException error)
            {
                AppLog.Append("pair request: not read yet: " + error.Message);
                return null;
            }
            catch (UnauthorizedAccessException error)
            {
                AppLog.Append("pair request: not read: " + error.Message);
                return null;
            }
            var request = new PairRequest();
            foreach (string raw in lines)
            {
                string line = raw.Trim().TrimStart('﻿');
                if (!line.StartsWith(FolderKey, StringComparison.Ordinal)) continue;
                string folder = line.Substring(FolderKey.Length).Trim();
                if (folder.Length > 0) request.Folders.Add(folder);
            }
            AppLog.Append("pair request: opening the pairing window, folders: " + (request.Folders.Count == 0 ? "recent" : string.Join("; ", request.Folders)));
            return request;
        }

        public static void PublishQr(string text)
        {
            string path = UserDataPaths.File(QrFile);
            string temporary = path + ".tmp";
            try
            {
                File.WriteAllText(temporary, text, new UTF8Encoding(false));
                if (File.Exists(path)) File.Replace(temporary, path, null);
                else File.Move(temporary, path);
            }
            catch (IOException error)
            {
                AppLog.Append("pair request: QR not written: " + error.Message);
                WithdrawQr();
            }
            catch (UnauthorizedAccessException error)
            {
                AppLog.Append("pair request: QR not written: " + error.Message);
                WithdrawQr();
            }
        }

        public static void WithdrawQr()
        {
            foreach (string name in new[] { QrFile, QrFile + ".tmp" })
            {
                try
                {
                    File.Delete(UserDataPaths.File(name));
                }
                catch (IOException error)
                {
                    AppLog.Append("pair request: " + name + " not deleted: " + error.Message);
                }
                catch (UnauthorizedAccessException error)
                {
                    AppLog.Append("pair request: " + name + " not deleted: " + error.Message);
                }
            }
        }
    }
}
