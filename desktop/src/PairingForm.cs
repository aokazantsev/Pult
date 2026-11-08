using System;
using System.Drawing;
using System.Globalization;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace Pult
{
    internal sealed class PairingForm : Form
    {
        private static readonly TimeSpan ReissueMargin = TimeSpan.FromSeconds(20);

        private readonly AppSettings settings;
        private readonly Func<RelaySession, RelayClient> startClient;
        private readonly TextBox serverBox = new TextBox();
        private readonly NumericUpDown idleBox = new NumericUpDown();
        private readonly CheckedListBox folderList = new CheckedListBox();
        private readonly Button addFolderButton = new Button();
        private readonly PictureBox qrBox = new PictureBox();
        private readonly Label statusLabel = new Label();
        private readonly Label fingerprintLabel = new Label();
        private readonly Button newQrButton = new Button();
        private readonly Button closeButton = new Button();
        private readonly System.Windows.Forms.Timer clock = new System.Windows.Forms.Timer { Interval = 1000 };
        private PairingOffer offer;
        private RelayClient pending;
        private bool creating;
        private bool paired;

        public PairingForm(AppSettings settings, Func<RelaySession, RelayClient> startClient, DesktopSessions sessions)
        {
            this.settings = settings;
            this.startClient = startClient;
            Text = "Pult — подключить телефон";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ShowInTaskbar = true;
            ClientSize = new Size(440, 760);
            Font = new Font("Segoe UI", 9f);

            var serverLabel = new Label { Text = "Сервер:", Location = new Point(16, 18), AutoSize = true };
            serverBox.Location = new Point(16, 38);
            serverBox.Width = 408;
            serverBox.Text = settings.Server;
            var idleLabel = new Label { Text = "Разорвать сессию после простоя, часов (0,25–36):", Location = new Point(16, 72), AutoSize = true };
            idleBox.Location = new Point(16, 92);
            idleBox.Width = 90;
            idleBox.DecimalPlaces = 2;
            idleBox.Increment = 0.25m;
            idleBox.Minimum = QrPayload.MinIdleSeconds / 3600m;
            idleBox.Maximum = QrPayload.MaxIdleSeconds / 3600m;
            idleBox.Value = QrPayload.DefaultIdleSeconds / 3600m;
            idleBox.ValueChanged += (sender, e) => OnIdleChanged();
            var folderLabel = new Label { Text = "Папки проектов, чьи треды увидит телефон:", Location = new Point(16, 124), AutoSize = true };
            folderList.Location = new Point(16, 144);
            folderList.Size = new Size(408, 74);
            folderList.CheckOnClick = true;
            folderList.HorizontalScrollbar = true;
            foreach (DesktopMeta meta in sessions.Recent(8)) folderList.Items.Add(meta.OriginCwd ?? meta.Cwd, true);
            folderList.ItemCheck += (sender, e) => BeginInvoke(new Action(ApplyFolders));
            addFolderButton.Text = "Добавить папку…";
            addFolderButton.Location = new Point(16, 220);
            addFolderButton.Size = new Size(140, 24);
            addFolderButton.Click += (sender, e) => OnAddFolder();
            qrBox.Location = new Point(40, 248);
            qrBox.Size = new Size(360, 360);
            qrBox.SizeMode = PictureBoxSizeMode.Zoom;
            qrBox.BackColor = Color.White;
            statusLabel.Location = new Point(16, 616);
            statusLabel.Size = new Size(408, 54);
            fingerprintLabel.Location = new Point(16, 670);
            fingerprintLabel.Size = new Size(408, 34);
            fingerprintLabel.Font = new Font("Consolas", 16f, FontStyle.Bold);
            fingerprintLabel.TextAlign = ContentAlignment.MiddleCenter;
            newQrButton.Text = "Новый QR";
            newQrButton.Location = new Point(220, 716);
            newQrButton.Size = new Size(100, 30);
            newQrButton.Click += (sender, e) => StartOffer();
            closeButton.Text = "Закрыть";
            closeButton.Location = new Point(324, 716);
            closeButton.Size = new Size(100, 30);
            closeButton.Click += (sender, e) => Close();
            CancelButton = closeButton;
            Controls.AddRange(new Control[] { serverLabel, serverBox, idleLabel, idleBox, folderLabel, folderList, addFolderButton, qrBox, statusLabel, fingerprintLabel, newQrButton, closeButton });
            clock.Tick += (sender, e) => Tick();
            Shown += (sender, e) =>
            {
                closeButton.Focus();
                StartOffer();
            };
            FormClosing += (sender, e) => OnClosing();
        }

        public RelayClient Pending
        {
            get { return pending; }
        }

        public void OnPaired(RelayClient client)
        {
            if (client != pending) return;
            pending = null;
            offer = null;
            paired = true;
            ShowQr(null);
            qrBox.Visible = false;
            statusLabel.Text = "Телефон подключён. Сверьте отпечаток с экраном телефона — цифры должны совпасть:";
            fingerprintLabel.Text = client.Session.Fingerprint;
            newQrButton.Enabled = false;
            closeButton.Text = "Готово";
            idleBox.Enabled = false;
            serverBox.Enabled = false;
            folderList.Enabled = false;
            addFolderButton.Enabled = false;
        }

        public void OnRejected(RelayClient client)
        {
            if (client != pending) return;
            pending = null;
            offer = null;
            ShowQr(null);
            statusLabel.Text = "Попытка сопряжения не прошла проверку: ключи телефона не заверены секретом из QR. Покажите новый QR.";
            newQrButton.Enabled = true;
            ThreadPool.QueueUserWorkItem(state => PairingFlow.Forget(client));
        }

        public void OnEnded(RelayClient client)
        {
            if (client != pending) return;
            pending = null;
            offer = null;
            ShowQr(null);
            ThreadPool.QueueUserWorkItem(state => PairingFlow.Forget(client));
            if (!paired) StartOffer();
        }

        private void StartOffer()
        {
            if (creating || paired) return;
            creating = true;
            newQrButton.Enabled = false;
            ShowQr(null);
            fingerprintLabel.Text = "";
            statusLabel.Text = "Создаю заявку на сервере…";
            string server = serverBox.Text.Trim().TrimEnd('/');
            if (server != settings.Server)
            {
                settings.Server = server;
                settings.Save();
            }
            int idle = IdleSeconds();
            RelayClient previous = pending;
            pending = null;
            offer = null;
            ThreadPool.QueueUserWorkItem(state =>
            {
                if (previous != null) PairingFlow.Abandon(previous, "user");
                string problem;
                PairingOffer created = PairingFlow.CreateOffer(server, idle, out problem);
                Deliver(created, problem);
            });
        }

        private void Deliver(PairingOffer created, string problem)
        {
            try
            {
                BeginInvoke((Action)(() => ShowOffer(created, problem)));
            }
            catch (InvalidOperationException)
            {
                if (created != null) Discard(created);
            }
        }

        private void ShowOffer(PairingOffer created, string problem)
        {
            creating = false;
            newQrButton.Enabled = true;
            if (IsDisposed || paired)
            {
                if (created != null) Discard(created);
                return;
            }
            if (created == null)
            {
                statusLabel.Text = problem;
                return;
            }
            created.Session.IdleSeconds = IdleSeconds();
            created.Session.Folders = SelectedFolders();
            offer = created;
            pending = startClient(created.Session);
            RenderQr();
            clock.Start();
            Tick();
        }

        private static void Discard(PairingOffer created)
        {
            var client = new RelayClient(created.Session, null);
            ThreadPool.QueueUserWorkItem(state =>
            {
                client.Start(null);
                PairingFlow.Abandon(client, "user");
            });
        }

        private void Tick()
        {
            if (offer == null) return;
            TimeSpan left = offer.ExpiresAtUtc - DateTime.UtcNow;
            if (left <= ReissueMargin)
            {
                AppLog.Append("pairing: claim " + offer.Session.Pair + " is about to expire, issuing a new QR");
                StartOffer();
                return;
            }
            TimeSpan shown = left - ReissueMargin;
            statusLabel.Text = "Отсканируйте QR приложением Pult на телефоне. QR действует ещё "
                + ((int)shown.TotalMinutes).ToString(CultureInfo.InvariantCulture) + ":" + shown.Seconds.ToString("00", CultureInfo.InvariantCulture)
                + ", потом сменится сам.";
        }

        private void OnIdleChanged()
        {
            if (offer == null) return;
            offer.Session.IdleSeconds = IdleSeconds();
            RenderQr();
        }

        private System.Collections.Generic.List<string> SelectedFolders()
        {
            var folders = new System.Collections.Generic.List<string>();
            foreach (object item in folderList.CheckedItems) folders.Add((string)item);
            return folders;
        }

        private void ApplyFolders()
        {
            if (offer != null) offer.Session.Folders = SelectedFolders();
        }

        private void OnAddFolder()
        {
            using (var dialog = new FolderBrowserDialog())
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                foreach (object item in folderList.Items)
                {
                    if (string.Equals(FolderMembership.Normalize((string)item), FolderMembership.Normalize(dialog.SelectedPath), StringComparison.OrdinalIgnoreCase)) return;
                }
                folderList.Items.Add(dialog.SelectedPath, true);
                ApplyFolders();
            }
        }

        private int IdleSeconds()
        {
            return (int)Math.Round(idleBox.Value * 3600m);
        }

        private void RenderQr()
        {
            if (offer == null || !offer.Session.AwaitsPhone) return;
            QrCode code = QrCode.EncodeBytes(new UTF8Encoding(false).GetBytes(offer.QrText));
            ShowQr(code.Render(8, 4));
        }

        private void ShowQr(Image image)
        {
            Image old = qrBox.Image;
            qrBox.Image = image;
            if (old != null) old.Dispose();
        }

        private void OnClosing()
        {
            clock.Stop();
            RelayClient client = pending;
            pending = null;
            offer = null;
            ShowQr(null);
            if (client != null) ThreadPool.QueueUserWorkItem(state => PairingFlow.Abandon(client, "user"));
        }
    }
}
