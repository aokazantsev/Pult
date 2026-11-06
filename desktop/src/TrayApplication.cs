using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Pult
{
    internal sealed class TrayApplication : ApplicationContext
    {
        private const int BrokerPollMs = 200;

        private readonly NotifyIcon trayIcon = new NotifyIcon();
        private readonly ToolStripMenuItem listenerItem = new ToolStripMenuItem();
        private readonly ToolStripMenuItem relayItem = new ToolStripMenuItem();
        private readonly ToolStripMenuItem phoneModeItem = new ToolStripMenuItem("Управление с телефона");
        private readonly ToolStripMenuItem pcModeItem = new ToolStripMenuItem("Управление с ПК");
        private readonly Timer refreshTimer = new Timer { Interval = 1000 };
        private readonly RelayClient relayClient;
        private readonly Broker broker;
        private readonly HookListener listener;
        private readonly Icon icon;

        public TrayApplication(AppSettings settings)
        {
            AppLog.Append("settings: " + settings.Describe());
            ServicePointManager.DefaultConnectionLimit = 16;
            ServicePointManager.Expect100Continue = false;
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            IRelay relay;
            if (settings.RelayConfigured)
            {
                relayClient = new RelayClient(settings.Server, settings.Pair, settings.Token);
                relay = relayClient;
            }
            else
            {
                relay = new OfflineRelay();
            }
            broker = new Broker(relay, TimeSpan.FromSeconds(settings.WaitSeconds), BrokerPollMs);
            string secret = HookSecret.LoadOrCreate();
            if (secret != null)
            {
                listener = new HookListener(settings.HookPort, secret, broker);
                listenerItem.Text = StartListener(settings.HookPort);
            }
            else
            {
                listenerItem.Text = "Нет секрета хуков — хуки не принимаются";
            }
            if (relayClient != null) relayClient.Start(broker);
            listenerItem.Enabled = false;
            relayItem.Enabled = false;
            phoneModeItem.Click += (sender, e) => broker.SetMode(ControlMode.Phone);
            pcModeItem.Click += (sender, e) => broker.SetMode(ControlMode.Pc);
            var menu = new ContextMenuStrip();
            menu.Items.Add(listenerItem);
            menu.Items.Add(relayItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(phoneModeItem);
            menu.Items.Add(pcModeItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Настройки (settings.txt)", null, OnOpenSettings);
            menu.Items.Add("Журнал", null, OnOpenLog);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Выход", null, OnExit);
            menu.Opening += (sender, e) => Refresh();
            icon = PaintIcon();
            trayIcon.Icon = icon;
            trayIcon.ContextMenuStrip = menu;
            trayIcon.Visible = true;
            refreshTimer.Tick += (sender, e) => Refresh();
            refreshTimer.Start();
            Refresh();
            AppLog.Append("tray ready: " + listenerItem.Text);
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

        private void Refresh()
        {
            ControlMode mode = broker.Mode;
            phoneModeItem.Checked = mode == ControlMode.Phone;
            pcModeItem.Checked = mode == ControlMode.Pc;
            relayItem.Text = RelayText();
            int open = broker.OpenRequests;
            string tip = AppIdentity.Name + ": " + (mode == ControlMode.Phone ? "с телефона" : "с ПК") + (open > 0 ? ", ждут ответа: " + open : "");
            trayIcon.Text = tip.Length > 63 ? tip.Substring(0, 63) : tip;
        }

        private string RelayText()
        {
            if (relayClient == null) return "Сервер не настроен (settings.txt)";
            if (!relayClient.Connected) return "Сервер: нет связи";
            return relayClient.PhoneStreamOpen ? "Сервер: связь есть, телефон на экране" : "Сервер: связь есть, телефон не на экране";
        }

        private void OnOpenSettings(object sender, EventArgs e)
        {
            OpenInNotepad(AppSettings.FilePath);
        }

        private void OnOpenLog(object sender, EventArgs e)
        {
            OpenInNotepad(AppLog.FilePath);
        }

        private static void OpenInNotepad(string path)
        {
            string notepad = Path.Combine(Environment.SystemDirectory, "notepad.exe");
            if (!File.Exists(path)) return;
            Process.Start(notepad, "\"" + path + "\"");
        }

        private void OnExit(object sender, EventArgs e)
        {
            refreshTimer.Stop();
            trayIcon.Visible = false;
            trayIcon.Dispose();
            if (listener != null) listener.Dispose();
            broker.Stop();
            if (relayClient != null) relayClient.Dispose();
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
