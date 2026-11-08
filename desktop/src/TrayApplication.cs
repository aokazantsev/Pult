using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Drawing;
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
        private const int CommandPollMs = 200;
        public const int PairPatienceSeconds = 60;
        public const int UnpairPatienceSeconds = 60;
        private const int NoticesKept = 20;
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
        private bool exiting;
        private readonly ControlServer control;
        private readonly List<Dictionary<string, object>> notices = new List<Dictionary<string, object>>();
        private readonly DesktopSessions sessions = new DesktopSessions(DesktopSessions.DefaultRoot);

        public TrayApplication(AppSettings settings)
        {
            this.settings = settings;
            AppLog.Append("settings: " + settings.Describe());
            GC.KeepAlive(ui.Handle);
            ServicePointManager.DefaultConnectionLimit = 16;
            ServicePointManager.Expect100Continue = false;
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            ThreadPool.QueueUserWorkItem(state => sessions.Warm());
            broker = new Broker(relay, TimeSpan.FromSeconds(settings.WaitSeconds), BrokerPollMs, sessions, UserDataPaths.Directory, Broker.DefaultTickMs, Broker.DefaultSnapshotDelayMs);
            broker.ChoiceDelivery = settings.ChoiceDelivery;
            PairRecord.Delete(UserDataPaths.File("processed-ids.txt"));
            string secret = HookSecret.LoadOrCreate();
            if (secret != null)
            {
                listener = new HookListener(settings.HookPort, secret, broker);
                listener.StateChanged += () => Post(() => listenerItem.Text = ListenerText(settings.HookPort));
                listener.Start();
                listenerItem.Text = ListenerText(settings.HookPort);
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
            icon = LoadTrayIcon();
            trayIcon.Icon = icon;
            trayIcon.ContextMenuStrip = menu;
            trayIcon.Visible = true;
            refreshTimer.Tick += (sender, e) => Refresh();
            refreshTimer.Start();
            Refresh();
            control = new ControlServer(ControlChannel.PipeName, Execute);
            control.Start();
            AppLog.Append("tray ready: " + listenerItem.Text);
        }

        public void OnPaired(RelayClient client)
        {
            client.Session.PairedAtUtc = DateTime.UtcNow;
            if (!client.Session.ToRecord().Save(UserDataPaths.File(PairRecord.PairFile))) AppLog.Append("pair " + client.Session.Pair + " not saved: it will be lost after restart");
            PairRecord.Delete(UserDataPaths.File(PairRecord.ClaimFile));
            RelayClient previous = active;
            if (previous != null && previous != client) broker.EndSession();
            active = client;
            relay.Current = client;
            broker.BeginSession(client.Session.Pair, client.Session.PairedAtUtc, client.Session.Folders, false);
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
                    Notify(EndText(reason, by, client.Session.IdleSeconds));
                    return;
                }
                if (pairingForm != null && pairingForm.Pending == client) pairingForm.OnEnded(client);
            });
        }

        public void OnNotice(RelayClient client, string text)
        {
            Post(() => Notify(text));
        }

        private string ListenerText(int port)
        {
            switch (listener.State)
            {
                case HookListenerState.Listening: return "Приём хуков: 127.0.0.1:" + port;
                case HookListenerState.Retrying: return "Порт " + port + " занят — повторяю, хуки пока не принимаются";
                default: return "Порт " + port + " занят — хуки не принимаются";
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
            var client = new RelayClient(session, this);
            active = client;
            relay.Current = client;
            broker.BeginSession(session.Pair, session.PairedAtUtc, session.Folders, true);
            client.Start(broker);
        }

        private RelayClient StartClient(RelaySession session)
        {
            var client = new RelayClient(session, this);
            client.Start(broker);
            return client;
        }

        private void OnPair()
        {
            if (OpenedByUser(pairingForm))
            {
                pairingForm.Activate();
                return;
            }
            if (pairingForm != null && !pairingForm.IsDisposed) pairingForm.Close();
            OpenPairingForm(null).Show();
        }

        private PairingForm ReopenPairingForm(PairRequest request)
        {
            if (pairingForm != null && !pairingForm.IsDisposed) pairingForm.Close();
            PairingForm form = OpenPairingForm(request);
            form.Begin();
            return form;
        }

        private PairingForm OpenPairingForm(PairRequest request)
        {
            AppSettings current = AppSettings.Load();
            if (!current.ReadFailed) settings.Server = current.Server;
            var form = new PairingForm(settings, StartClient, sessions, request);
            pairingForm = form;
            form.FormClosed += (sender, e) =>
            {
                if (pairingForm == form) pairingForm = null;
            };
            return form;
        }

        private void OnUnpair()
        {
            RelayClient client = active;
            if (client == null || unpairing) return;
            if (MessageBox.Show("Разорвать сессию с телефоном? Для новой понадобится новый QR.", AppIdentity.Name, MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
            Unpair(client);
        }

        public static bool CanUnpair(object asked, object current, bool deleting, bool leaving)
        {
            return asked != null && asked == current && !deleting && !leaving;
        }

        private bool Unpair(RelayClient client)
        {
            if (!CanUnpair(client, active, unpairing, exiting)) return false;
            EndActive(client, "user", EndText("user", "pc", client.Session.IdleSeconds));
            return true;
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
            broker.EndSession();
            AppLog.Append("pair " + client.Session.Pair + " detached");
        }

        private void CheckIdle()
        {
            RelayClient client = active;
            if (client == null || unpairing) return;
            int idle = client.Session.IdleSeconds;
            if (DateTime.UtcNow - broker.LastHookUtc < TimeSpan.FromSeconds(idle)) return;
            AppLog.Append("pair " + client.Session.Pair + ": no thread events for " + idle + " s");
            EndActive(client, "idle", EndText("idle", "pc", idle));
        }

        private static string IdleWords(int seconds)
        {
            int minutes = seconds / 60;
            int hours = minutes / 60;
            minutes %= 60;
            if (hours == 0) return minutes + " мин";
            return minutes == 0 ? hours + " ч" : hours + " ч " + minutes + " мин";
        }

        private static string EndText(string reason, string by, int idleSeconds)
        {
            switch (reason)
            {
                case "user": return by == "phone" ? "Телефон отвязан с телефона" : "Сессия разорвана";
                case "idle": return "Сессия закрыта: нет событий " + IdleWords(idleSeconds);
                case "replaced": return "Телефон сопряжён заново";
                case "no_hello": return "Телефон не дождался подтверждения сопряжения";
                case "pairing_rejected": return "Попытка сопряжения не прошла проверку";
                case "pc_offline": return "Не было связи с сервером дольше 5 минут";
                case "not_found": return "Сессии больше нет: её разорвала другая сторона";
                default: return "Сессия разорвана (" + reason + ")";
            }
        }

        private void Notify(string text)
        {
            notices.Add(new Dictionary<string, object> { { "time", DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture) }, { "text", text } });
            if (notices.Count > NoticesKept) notices.RemoveAt(0);
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
            if (exiting) return;
            exiting = true;
            refreshTimer.Stop();
            if (control != null) control.Dispose();
            trayIcon.Visible = false;
            trayIcon.Dispose();
            if (listener != null) listener.Dispose();
            broker.Stop();
            if (pairingForm != null) pairingForm.Close();
            if (active != null) active.Dispose();
            AppLog.Append("exit");
            ExitThread();
        }

        private Dictionary<string, object> Execute(ControlCommand command)
        {
            try
            {
                switch (command.Name)
                {
                    case ControlCommand.Status:
                        return OnUi(StatusReport);
                    case ControlCommand.Exit:
                        command.AfterReply = () => Post(() => OnExit(null, EventArgs.Empty));
                        Dictionary<string, object> leaving = ControlResult.Ok();
                        leaving["pid"] = Process.GetCurrentProcess().Id;
                        return leaving;
                    case ControlCommand.Mode:
                        return OnUi(() => SetMode(command.TargetMode));
                    case ControlCommand.Pair:
                        return AwaitQr(OnUi(() => ReopenPairingForm(PairRequest.From(command))));
                    case ControlCommand.NewQr:
                        PairingForm renewed = null;
                        Dictionary<string, object> refusal = OnUi(() => RequestNewQr(out renewed));
                        return refusal ?? AwaitQr(renewed);
                    case ControlCommand.Pairing:
                        return AwaitPairing(OnUi(() => pairingForm), command.WaitSeconds, state => state == PairingForm.Creating || state == PairingForm.Showing);
                    case ControlCommand.ClosePairing:
                        return OnUi(ClosePairing);
                    case ControlCommand.Unpair:
                        return UnpairAndWait();
                    default:
                        return ControlResult.Fail(ControlResult.Usage, "not a command of the running Pult: " + command.Name);
                }
            }
            catch (InvalidOperationException)
            {
                if (!exiting && ui.IsHandleCreated) throw;
                return ControlResult.Fail(ControlResult.Failed, "Pult is exiting");
            }
        }

        private T OnUi<T>(Func<T> action)
        {
            return (T)ui.Invoke(action);
        }

        private Dictionary<string, object> SetMode(ControlMode mode)
        {
            broker.SetMode(mode);
            Refresh();
            Dictionary<string, object> result = ControlResult.Ok();
            result["mode"] = mode == ControlMode.Pc ? "pc" : "phone";
            return result;
        }

        private Dictionary<string, object> RequestNewQr(out PairingForm form)
        {
            form = pairingForm;
            Dictionary<string, object> refusal = CommandWindowRefusal(form);
            if (refusal != null) return refusal;
            form.NewQr();
            return null;
        }

        private Dictionary<string, object> ClosePairing()
        {
            if (pairingForm == null || pairingForm.IsDisposed)
            {
                Dictionary<string, object> result = ControlResult.Ok();
                result["closed"] = false;
                return result;
            }
            Dictionary<string, object> refusal = CommandWindowRefusal(pairingForm);
            if (refusal != null) return refusal;
            pairingForm.Close();
            Dictionary<string, object> done = ControlResult.Ok();
            done["closed"] = true;
            return done;
        }

        public static bool OpenedByUser(PairingForm form)
        {
            return form != null && !form.IsDisposed && form.Visible;
        }

        public static Dictionary<string, object> CommandWindowRefusal(PairingForm form)
        {
            if (form == null || form.IsDisposed) return ControlResult.Fail(ControlResult.Refused, "no pairing window: pair opens it");
            if (OpenedByUser(form)) return ControlResult.Fail(ControlResult.Refused, "the pairing window on the screen was opened by the user: pair opens a window of its own");
            return null;
        }

        private Dictionary<string, object> AwaitQr(PairingForm form)
        {
            return QrAnswer(AwaitPairing(form, PairPatienceSeconds, shown => shown == PairingForm.Creating)["pairing"] as Dictionary<string, object>);
        }

        public static Dictionary<string, object> QrAnswer(Dictionary<string, object> pairing)
        {
            string state = pairing == null ? "closed" : (string)pairing["state"];
            if (state == PairingForm.Showing)
            {
                Dictionary<string, object> result = ControlResult.Ok();
                result["pairing"] = pairing;
                return result;
            }
            string message = state == PairingForm.Creating ? "no QR in " + PairPatienceSeconds + " s"
                : pairing != null && pairing.ContainsKey("problem") ? (string)pairing["problem"]
                : "the pairing window is " + state;
            Dictionary<string, object> failure = ControlResult.Fail(ControlResult.Failed, message);
            failure["pairing"] = pairing;
            return failure;
        }

        private Dictionary<string, object> AwaitPairing(PairingForm form, int seconds, Func<string, bool> waitWhile)
        {
            var clock = Stopwatch.StartNew();
            while (true)
            {
                Dictionary<string, object> pairing = OnUi(() => form == null || form != pairingForm || form.IsDisposed ? null : form.Describe());
                if (pairing == null || !waitWhile((string)pairing["state"]) || clock.Elapsed.TotalSeconds >= seconds)
                {
                    Dictionary<string, object> result = ControlResult.Ok();
                    result["pairing"] = pairing;
                    return result;
                }
                Thread.Sleep(CommandPollMs);
            }
        }

        private Dictionary<string, object> UnpairAndWait()
        {
            Dictionary<string, object> refusal = OnUi(() => Unpair(active) ? null : ControlResult.Fail(ControlResult.Refused, exiting ? "Pult is exiting" : unpairing ? "the pair is being deleted already" : "no pair to break"));
            if (refusal != null) return refusal;
            var clock = Stopwatch.StartNew();
            while (OnUi(() => unpairing))
            {
                if (clock.Elapsed.TotalSeconds >= UnpairPatienceSeconds) return ControlResult.Fail(ControlResult.Failed, "the pair is still being deleted after " + UnpairPatienceSeconds + " s");
                Thread.Sleep(CommandPollMs);
            }
            return ControlResult.Ok();
        }

        private Dictionary<string, object> StatusReport()
        {
            Dictionary<string, object> result = ControlResult.Ok();
            result["version"] = AppIdentity.Version;
            result["pid"] = Process.GetCurrentProcess().Id;
            result["exe"] = Application.ExecutablePath;
            result["data"] = UserDataPaths.Directory;
            result["hooks"] = new Dictionary<string, object>
            {
                { "state", listener == null ? "no_secret" : listener.State.ToString().ToLowerInvariant() },
                { "port", settings.HookPort },
                { "text", listenerItem.Text }
            };
            result["relay"] = RelayReport();
            foreach (KeyValuePair<string, object> part in broker.StatusView()) result[part.Key] = part.Value;
            result["tray"] = trayIcon.Text;
            result["pairing"] = pairingForm == null || pairingForm.IsDisposed ? null : pairingForm.Describe();
            result["notices"] = new List<Dictionary<string, object>>(notices);
            return result;
        }

        private Dictionary<string, object> RelayReport()
        {
            RelayClient client = active;
            var report = new Dictionary<string, object> { { "text", RelayText() }, { "paired", client != null }, { "unpairing", unpairing } };
            if (client == null) return report;
            RelaySession session = client.Session;
            report["server"] = session.Server;
            report["pair"] = session.Pair;
            report["fingerprint"] = session.Fingerprint;
            report["paired_at"] = session.PairedAtUtc == DateTime.MinValue ? null : session.PairedAtUtc.ToLocalTime().ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture);
            report["folders"] = new List<string>(session.Folders);
            report["idle_seconds"] = session.IdleSeconds;
            report["idle_left_seconds"] = Math.Max(0, session.IdleSeconds - (int)(DateTime.UtcNow - broker.LastHookUtc).TotalSeconds);
            report["connected"] = client.Connected;
            report["server_problem"] = client.ServerProblem;
            report["phone_compatible"] = client.PhoneCompatible;
            report["phone_on_screen"] = client.PhoneStreamOpen;
            return report;
        }

        [DllImport("user32.dll")]
        private static extern bool DestroyIcon(IntPtr handle);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern uint ExtractIconEx(string file, int index, IntPtr[] large, IntPtr[] small, uint count);

        private static Icon LoadTrayIcon()
        {
            var small = new IntPtr[1];
            if (ExtractIconEx(Application.ExecutablePath, 0, null, small, 1) == 0 || small[0] == IntPtr.Zero)
            {
                return (Icon)SystemIcons.Application.Clone();
            }
            try
            {
                using (Icon fromHandle = Icon.FromHandle(small[0]))
                {
                    return (Icon)fromHandle.Clone();
                }
            }
            finally
            {
                DestroyIcon(small[0]);
            }
        }
    }
}
