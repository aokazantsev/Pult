using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace Pult
{
    internal sealed class TrayApplication : ApplicationContext, ISessionListener
    {
        private const int BrokerPollMs = 200;
        private static readonly TimeSpan DeletePatience = TimeSpan.FromSeconds(10);

        private readonly NotifyIcon trayIcon = new NotifyIcon();
        private readonly ToolStripMenuItem listenerItem = new ToolStripMenuItem();
        private readonly ToolStripMenuItem relayItem = new ToolStripMenuItem();
        private readonly ToolStripMenuItem pairItem = new ToolStripMenuItem("Подключить телефон…");
        private readonly ToolStripMenuItem unpairItem = new ToolStripMenuItem("Разорвать");
        private readonly ToolStripMenuItem phoneModeItem = new ToolStripMenuItem("Управление с телефона");
        private readonly ToolStripMenuItem pcModeItem = new ToolStripMenuItem("Управление с ПК");
        private readonly System.Windows.Forms.Timer refreshTimer = new System.Windows.Forms.Timer { Interval = 1000 };
        private readonly Control ui = new Control();
        private readonly AppSettings settings;
        private readonly CurrentRelay relay = new CurrentRelay();
        private readonly Broker broker;
        private readonly HookListener listener;
        private readonly Icon icon;
        private volatile RelayClient active;
        private PairingForm pairingForm;
        private bool unpairing;

        public TrayApplication(AppSettings settings)
        {
            this.settings = settings;
            AppLog.Append("settings: " + settings.Describe());
            GC.KeepAlive(ui.Handle);
            ServicePointManager.DefaultConnectionLimit = 16;
            ServicePointManager.Expect100Continue = false;
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            broker = new Broker(relay, TimeSpan.FromSeconds(settings.WaitSeconds), BrokerPollMs);
            PairRecord.Delete(UserDataPaths.File("processed-ids.txt"));
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
            RestorePair();
            ThreadPool.QueueUserWorkItem(state => PairingFlow.CleanLeftoverClaim(null));
            listenerItem.Enabled = false;
            relayItem.Enabled = false;
            pairItem.Click += (sender, e) => OnPair();
            unpairItem.Click += (sender, e) => OnUnpair();
            phoneModeItem.Click += (sender, e) => broker.SetMode(ControlMode.Phone);
            pcModeItem.Click += (sender, e) => broker.SetMode(ControlMode.Pc);
            var menu = new ContextMenuStrip();
            menu.Items.Add(listenerItem);
            menu.Items.Add(relayItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(pairItem);
            menu.Items.Add(unpairItem);
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

        public void OnPaired(RelayClient client)
        {
            if (!client.Session.ToRecord().Save(UserDataPaths.File(PairRecord.PairFile))) AppLog.Append("pair " + client.Session.Pair + " not saved: it will be lost after restart");
            PairRecord.Delete(UserDataPaths.File(PairRecord.ClaimFile));
            RelayClient previous = active;
            active = client;
            relay.Current = client;
            broker.ResetIdle();
            if (previous != null && previous != client)
            {
                ThreadPool.QueueUserWorkItem(state =>
                {
                    previous.DeletePair("replaced", DeletePatience);
                    previous.Dispose();
                    previous.Session.Keys.Delete();
                    previous.Session.Keys.Dispose();
                    PairRecord.Delete(RelayClient.ProcessedIdsPath(previous.Session.Pair));
                });
            }
            Post(() =>
            {
                if (pairingForm != null) pairingForm.OnPaired(client);
                Notify("Телефон подключён, отпечаток " + client.Session.Fingerprint);
            });
        }

        public void OnPairingRejected(RelayClient client)
        {
            Post(() =>
            {
                if (pairingForm != null && pairingForm.Pending == client) pairingForm.OnRejected(client);
                else ThreadPool.QueueUserWorkItem(state => PairingFlow.Forget(client));
                Notify("Попытка сопряжения не прошла проверку");
            });
        }

        public void OnPairEnded(RelayClient client, string reason, string by)
        {
            Post(() =>
            {
                if (client == active)
                {
                    Detach(client);
                    ThreadPool.QueueUserWorkItem(state => PairingFlow.Forget(client));
                    Notify(EndText(reason, by));
                    return;
                }
                if (pairingForm != null && pairingForm.Pending == client) pairingForm.OnEnded(client);
            });
        }

        public void OnNotice(RelayClient client, string text)
        {
            Post(() => Notify(text));
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

        private void RestorePair()
        {
            string path = UserDataPaths.File(PairRecord.PairFile);
            PairRecord record = PairRecord.Load(path);
            if (record == null) return;
            RelaySession session = RelaySession.Restore(record);
            if (session == null)
            {
                AppLog.Append("pair " + record.Pair + " cannot be restored, removed");
                PairRecord.Delete(path);
                PairKeys.DeleteByName(record.SignKey);
                PairKeys.DeleteByName(record.AgreeKey);
                return;
            }
            active = StartClient(session);
            relay.Current = active;
        }

        private RelayClient StartClient(RelaySession session)
        {
            var client = new RelayClient(session, this);
            client.Start(broker);
            return client;
        }

        private void OnPair()
        {
            if (pairingForm != null && !pairingForm.IsDisposed)
            {
                pairingForm.Activate();
                return;
            }
            pairingForm = new PairingForm(settings, StartClient);
            pairingForm.FormClosed += (sender, e) => pairingForm = null;
            pairingForm.Show();
        }

        private void OnUnpair()
        {
            RelayClient client = active;
            if (client == null || unpairing) return;
            if (MessageBox.Show("Разорвать сессию с телефоном? Для новой понадобится новый QR.", AppIdentity.Name, MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
            EndActive(client, "user", "Пара разорвана");
        }

        private void EndActive(RelayClient client, string reason, string text)
        {
            if (client != active) return;
            unpairing = true;
            Detach(client);
            ThreadPool.QueueUserWorkItem(state =>
            {
                PairingFlow.Abandon(client, reason);
                Post(() =>
                {
                    unpairing = false;
                    Notify(text);
                });
            });
        }

        private void Detach(RelayClient client)
        {
            if (client != active) return;
            active = null;
            relay.Current = null;
            AppLog.Append("pair " + client.Session.Pair + " detached");
        }

        private void CheckIdle()
        {
            RelayClient client = active;
            if (client == null || unpairing) return;
            int idle = client.Session.IdleSeconds;
            if (DateTime.UtcNow - broker.LastHookUtc < TimeSpan.FromSeconds(idle)) return;
            AppLog.Append("pair " + client.Session.Pair + ": no thread events for " + idle + " s");
            EndActive(client, "idle", "Сессия закончилась по простою");
        }

        private static string EndText(string reason, string by)
        {
            switch (reason)
            {
                case "user": return by == "phone" ? "Телефон отвязан: сессия закончена" : "Пара разорвана на ПК";
                case "idle": return "Сессия закончилась по простою";
                case "replaced": return "Телефон подключён к другой сессии: эта закончена";
                case "no_hello": return "Телефон не дождался подтверждения ПК: сопряжение не завершено";
                case "pairing_rejected": return "Сопряжение отклонено";
                case "pc_offline": return "ПК был без связи с сервером дольше 5 минут: сессия закончена";
                case "not_found": return "Пары больше нет на сервере: сессия закончена";
                default: return "Сессия закончена (" + reason + ")";
            }
        }

        private void Notify(string text)
        {
            trayIcon.ShowBalloonTip(8000, AppIdentity.Name, text, ToolTipIcon.Info);
        }

        private void Post(Action action)
        {
            try
            {
                ui.BeginInvoke(action);
            }
            catch (InvalidOperationException)
            {
            }
        }

        private void Refresh()
        {
            CheckIdle();
            ControlMode mode = broker.Mode;
            phoneModeItem.Checked = mode == ControlMode.Phone;
            pcModeItem.Checked = mode == ControlMode.Pc;
            relayItem.Text = RelayText();
            unpairItem.Enabled = active != null && !unpairing;
            int open = broker.OpenRequests;
            string tip = AppIdentity.Name + ": " + (mode == ControlMode.Phone ? "с телефона" : "с ПК") + (open > 0 ? ", ждут ответа: " + open : "");
            trayIcon.Text = tip.Length > 63 ? tip.Substring(0, 63) : tip;
        }

        private string RelayText()
        {
            RelayClient client = active;
            if (client == null) return unpairing ? "Разрываю пару…" : "Телефон не подключён";
            if (client.ServerProblem != null) return client.ServerProblem;
            if (!client.Connected) return "Сервер: нет связи";
            if (!client.PhoneCompatible) return "Телефон несовместим: запросы остаются на ПК";
            return client.PhoneStreamOpen ? "Сервер: связь есть, телефон на экране" : "Сервер: связь есть, телефон не на экране";
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
            if (pairingForm != null) pairingForm.Close();
            if (active != null) active.Dispose();
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
