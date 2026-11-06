using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Pult
{
    internal sealed class TrayApplication : ApplicationContext
    {
        private readonly NotifyIcon trayIcon = new NotifyIcon();
        private readonly ToolStripMenuItem statusItem = new ToolStripMenuItem();
        private readonly HookListener listener;
        private readonly Icon icon;
        private readonly string status;

        public TrayApplication(AppSettings settings)
        {
            AppLog.Append("settings: hook port=" + settings.HookPort);
            listener = new HookListener(settings.HookPort);
            status = StartListener(settings.HookPort);
            statusItem.Text = status;
            statusItem.Enabled = false;
            var menu = new ContextMenuStrip();
            menu.Items.Add(statusItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Журнал", null, OnOpenLog);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Выход", null, OnExit);
            icon = PaintIcon();
            trayIcon.Icon = icon;
            trayIcon.Text = AppIdentity.Name + " " + AppIdentity.Version;
            trayIcon.ContextMenuStrip = menu;
            trayIcon.Visible = true;
            AppLog.Append("tray ready: " + status);
        }

        private string StartListener(int port)
        {
            try
            {
                listener.Start();
                return "Приём хуков: 127.0.0.1:" + port;
            }
            catch (SocketException error)
            {
                AppLog.Append("hook listener not started on port " + port + ": " + error.Message);
                return "Порт " + port + " занят — хуки не принимаются";
            }
        }

        private void OnOpenLog(object sender, EventArgs e)
        {
            string notepad = Path.Combine(Environment.SystemDirectory, "notepad.exe");
            if (!File.Exists(AppLog.FilePath)) return;
            Process.Start(notepad, "\"" + AppLog.FilePath + "\"");
        }

        private void OnExit(object sender, EventArgs e)
        {
            trayIcon.Visible = false;
            trayIcon.Dispose();
            listener.Dispose();
            AppLog.Append("exit");
            ExitThread();
        }

        [DllImport("user32.dll")]
        private static extern bool DestroyIcon(IntPtr handle);

        private static Icon PaintIcon()
        {
            using (var bitmap = new Bitmap(32, 32))
            {
                using (Graphics g = Graphics.FromImage(bitmap))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    using (var back = new SolidBrush(Color.FromArgb(47, 111, 235)))
                    using (var front = new SolidBrush(Color.White))
                    {
                        g.FillEllipse(back, 1, 1, 30, 30);
                        g.FillRectangle(front, 10, 7, 12, 18);
                        g.FillEllipse(back, 12, 10, 3, 3);
                        g.FillEllipse(back, 17, 10, 3, 3);
                        g.FillEllipse(back, 12, 16, 3, 3);
                        g.FillEllipse(back, 17, 16, 3, 3);
                    }
                }
                IntPtr handle = bitmap.GetHicon();
                try
                {
                    using (Icon fromHandle = Icon.FromHandle(handle))
                    {
                        return (Icon)fromHandle.Clone();
                    }
                }
                finally
                {
                    DestroyIcon(handle);
                }
            }
        }
    }
}
