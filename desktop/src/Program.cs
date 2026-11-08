using System;
using System.IO;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Security;
using System.Threading;
using System.Windows.Forms;

namespace Pult
{
    internal static class Program
    {
        private const int StdOutput = -11;
        private const int AttachParentProcess = -1;

        [STAThread]
        private static int Main(string[] args)
        {
            if (args.Length > 0) return RunCommand(args);
            AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += OnThreadException;
            bool isFirstInstance;
            using (new Mutex(true, AppIdentity.SingleInstanceMutex, out isFirstInstance))
            {
                if (!isFirstInstance) return 0;
                UserDataPaths.Ensure();
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                AppLog.Append("start " + AppIdentity.Version + ", user=" + Environment.UserDomainName + "\\" + Environment.UserName
                    + ", os=" + Environment.OSVersion.VersionString + ", exe=" + Application.ExecutablePath);
                Application.Run(new TrayApplication(AppSettings.Load()));
            }
            return 0;
        }

        private static int RunCommand(string[] args)
        {
            Stream output = OpenOutput();
            try
            {
                return ControlClient.Run(args, output, Application.ExecutablePath);
            }
            catch (Exception error)
            {
                try
                {
                    ControlChannel.WriteLine(output, Json.Write(ControlResult.Fail(ControlResult.Failed, error.Message)));
                }
                catch (IOException)
                {
                }
                return ControlResult.ExitFailed;
            }
        }

        private static Stream OpenOutput()
        {
            IntPtr handle = GetStdHandle(StdOutput);
            if (handle == IntPtr.Zero || handle == new IntPtr(-1)) AttachConsole(AttachParentProcess);
            return Console.OpenStandardOutput();
        }

        [DllImport("kernel32.dll")]
        private static extern IntPtr GetStdHandle(int handle);

        [DllImport("kernel32.dll")]
        private static extern bool AttachConsole(int processId);

        private static void OnThreadException(object sender, ThreadExceptionEventArgs e)
        {
            CrashReport.Write("ui thread", e.Exception, false);
        }

        [HandleProcessCorruptedStateExceptions]
        [SecurityCritical]
        private static void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            CrashReport.Write("unhandled", e.ExceptionObject as Exception, e.IsTerminating);
        }
    }
}
