using System;
using System.IO;

namespace Pult
{
    internal static class AppIdentity
    {
        public const string Name = "Pult";
        public const string Version = "0.1";
        public const string ExecutableName = "Pult.exe";
        public const string SingleInstanceMutex = "Pult.SingleInstance";
        public const string GitHubUrl = "https://github.com/aokazantsev/Pult";

        public static string DataDirectory
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), Name); }
        }
    }
}
