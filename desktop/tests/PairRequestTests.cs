using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Windows.Forms;

namespace Pult.Tests
{
    internal static class PairRequestTests
    {
        private static void Expect(bool c, string m) { MessageTests.Expect(c, m); }

        private static string RequestPath { get { return UserDataPaths.File(PairRequest.RequestFile); } }

        private static string QrPath { get { return UserDataPaths.File(PairRequest.QrFile); } }

        public static void NoRequestFile()
        {
            File.Delete(RequestPath);
            Expect(PairRequest.Take() == null, "no file means no request");
        }

        public static void RequestFoldersAreRead()
        {
            File.WriteAllText(RequestPath, "\uFEFFfolder=D:\\work\\lab\r\n# note\nfolder=  \nother=1\n  folder=C:\\two  \n", new UTF8Encoding(false));
            PairRequest request = PairRequest.Take();
            Expect(request != null, "request is taken");
            Expect(request.Folders.SequenceEqual(new[] { "D:\\work\\lab", "C:\\two" }), "only non-empty folder lines: " + string.Join("|", request.Folders));
            Expect(!File.Exists(RequestPath), "request file is consumed");
            Expect(PairRequest.Take() == null, "request is taken once");
        }

        public static void EmptyRequestMeansRecentFolders()
        {
            File.WriteAllText(RequestPath, "");
            PairRequest request = PairRequest.Take();
            Expect(request != null && request.Folders.Count == 0, "empty file is a request without folders");
        }

        public static void RequestedFoldersReplaceRecent()
        {
            var rig = new Rig(5);
            rig.WriteMeta("local_F", "s-f", "C:\\work\\demo", "Тред", null, null);
            var request = new PairRequest();
            request.Folders.Add("D:\\lab");
            request.Folders.Add("D:\\lab\\");
            request.Folders.Add("C:\\work\\demo");
            using (var form = new PairingForm(new AppSettings(), null, new DesktopSessions(rig.Meta, TimeSpan.Zero), request))
            {
                var list = form.Controls.OfType<CheckedListBox>().Single();
                Expect(list.Items.Count == 2 && list.CheckedItems.Count == 2, "requested folders only, duplicates merged: " + list.Items.Count);
                Expect((string)list.Items[0] == "D:\\lab" && (string)list.Items[1] == "C:\\work\\demo", "requested order kept");
            }
        }

        public static void RequestedFormPublishesQrUntilClosed()
        {
            PairRequest.WithdrawQr();
            using (var form = new PairingForm(new AppSettings(), null, new DesktopSessions(Path.GetTempPath(), TimeSpan.Zero), new PairRequest()))
            {
                PairingOffer offer = Offer(form);
                Expect(File.Exists(QrPath), "QR is published");
                Expect(File.ReadAllText(QrPath, Encoding.UTF8) == offer.QrText, "published text equals the QR");
                Invoke(form, "OnClosing");
                Expect(!File.Exists(QrPath), "QR is withdrawn when the window closes");
            }
        }

        public static void ClickedFormDoesNotPublishQr()
        {
            PairRequest.WithdrawQr();
            using (var form = new PairingForm(new AppSettings(), null, new DesktopSessions(Path.GetTempPath(), TimeSpan.Zero), null))
            {
                Offer(form);
                Expect(!File.Exists(QrPath), "QR stays off the disk without a request");
            }
        }

        public static void PairingWithdrawsQr()
        {
            using (var form = RequestedForm())
            {
                RelayClient client = Pending(form, Offer(form));
                form.OnPaired(Stranger());
                Expect(File.Exists(QrPath), "an event of another client keeps the QR");
                form.OnPaired(client);
                Expect(!File.Exists(QrPath), "QR is withdrawn on pairing");
            }
        }

        public static void RejectionWithdrawsQr()
        {
            using (var form = RequestedForm())
            {
                RelayClient client = Pending(form, Offer(form));
                form.OnRejected(Stranger());
                Expect(File.Exists(QrPath), "a rejection of another client keeps the QR");
                form.OnRejected(client);
                Expect(!File.Exists(QrPath), "QR is withdrawn on rejection");
            }
        }

        public static void EndedClaimWithdrawsQr()
        {
            using (var form = RequestedForm())
            {
                RelayClient client = Pending(form, Offer(form));
                form.OnEnded(Stranger());
                Expect(File.Exists(QrPath), "an end of another claim keeps the QR");
                form.OnEnded(client);
                Expect(!File.Exists(QrPath), "QR of the ended claim is withdrawn");
            }
        }

        public static void NewQrWithdrawsTheOldOne()
        {
            using (var form = RequestedForm())
            {
                Offer(form);
                Invoke(form, "StartOffer");
                Expect(!File.Exists(QrPath), "the old QR is withdrawn before a new claim");
            }
        }

        public static void IdleChangeRepublishesQr()
        {
            using (var form = RequestedForm())
            {
                PairingOffer offer = Offer(form);
                string before = File.ReadAllText(QrPath, Encoding.UTF8);
                form.Controls.OfType<NumericUpDown>().Single().Value = 1m;
                Expect(offer.QrText != before && File.ReadAllText(QrPath, Encoding.UTF8) == offer.QrText, "idle change rewrites the published QR");
                Expect(!File.Exists(QrPath + ".tmp"), "no temporary file is left");
            }
        }

        public static void BusyRequestIsTakenLater()
        {
            File.WriteAllText(RequestPath, "folder=D:\\lab\n");
            using (new FileStream(RequestPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                Expect(PairRequest.Take() == null && File.Exists(RequestPath), "a busy request waits for the next tick");
            }
            PairRequest request = PairRequest.Take();
            Expect(request != null && request.Folders.Count == 1, "the request is taken once free");
        }

        public static void RequestedFoldersGoToTheClaim()
        {
            PairRequest.WithdrawQr();
            var request = new PairRequest();
            request.Folders.Add("D:\\lab");
            request.Folders.Add("C:\\two");
            using (var form = new PairingForm(new AppSettings(), session => new RelayClient(session, null), Sessions(), request))
            {
                PairingOffer offer = NewOffer();
                typeof(PairingForm).GetMethod("ShowOffer", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(form, new object[] { offer, null });
                Expect(offer.Session.Folders.SequenceEqual(new[] { "D:\\lab", "C:\\two" }), "requested folders go to the claim");
                Expect(form.Pending != null && File.ReadAllText(QrPath, Encoding.UTF8) == offer.QrText, "the first QR of the claim is published");
            }
        }

        public static void FailedPublishLeavesNoStaleQr()
        {
            PairRequest.WithdrawQr();
            PairRequest.PublishQr("old");
            File.WriteAllText(QrPath + ".tmp", "");
            using (new FileStream(QrPath + ".tmp", FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                PairRequest.PublishQr("new");
                Expect(!File.Exists(QrPath), "a failed publish leaves no stale QR");
            }
            PairRequest.WithdrawQr();
            Expect(!File.Exists(QrPath + ".tmp"), "the temporary file is withdrawn too");
        }

        public static void ClaimNotCreatedIsLogged()
        {
            string problem;
            Expect(PairingFlow.CreateOffer("ftp://example.com", 3600, out problem) == null && problem != null, "bad address is refused");
            Expect(File.ReadAllLines(AppLog.FilePath).Last().EndsWith("pairing: claim not created: bad server address"), "bad address is logged");
            Expect(PairingFlow.CreateOffer("http://127.0.0.1:1", 3600, out problem) == null && problem != null, "unreachable server is refused");
            Expect(File.ReadAllLines(AppLog.FilePath).Last().EndsWith("pairing: claim not created: " + problem), "unreachable server is logged with the reason");
        }

        public static void WithdrawWithoutFileIsQuiet()
        {
            PairRequest.WithdrawQr();
            PairRequest.WithdrawQr();
            Expect(!File.Exists(QrPath), "nothing to withdraw");
        }

        private static DesktopSessions Sessions()
        {
            return new DesktopSessions(Path.GetTempPath(), TimeSpan.Zero);
        }

        private static PairingForm RequestedForm()
        {
            PairRequest.WithdrawQr();
            return new PairingForm(new AppSettings(), null, Sessions(), new PairRequest());
        }

        private static RelayClient Pending(PairingForm form, PairingOffer offer)
        {
            var client = new RelayClient(offer.Session, null);
            typeof(PairingForm).GetField("pending", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(form, client);
            return client;
        }

        private static RelayClient Stranger()
        {
            return new RelayClient(NewOffer().Session, null);
        }

        private static PairingOffer NewOffer()
        {
            var v = VectorTests.Load("pairing.json");
            var keys = new PairKeys(VectorTests.PrivateKey("pc_sign"), VectorTests.PrivateKey("pc_agree"), null, null);
            RelaySession session = RelaySession.Pending("https://example.com/pult", Json.String(v, "pair"), keys, Base64Url.Decode(Json.String(v, "secret")), QrPayload.DefaultIdleSeconds);
            return new PairingOffer(session, 600);
        }

        private static PairingOffer Offer(PairingForm form)
        {
            PairingOffer offer = NewOffer();
            typeof(PairingForm).GetField("offer", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(form, offer);
            Invoke(form, "RenderQr");
            return offer;
        }

        private static void Invoke(PairingForm form, string method)
        {
            typeof(PairingForm).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic, null, Type.EmptyTypes, null).Invoke(form, null);
        }
    }
}
