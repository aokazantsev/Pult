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
            using (var form = new PairingForm(new AppSettings(), null, new DesktopSessions(Path.GetTempPath(), TimeSpan.Zero)))
            {
                Offer(form);
                Expect(!File.Exists(QrPath), "QR stays off the disk without a request");
            }
        }

        public static void WithdrawWithoutFileIsQuiet()
        {
            PairRequest.WithdrawQr();
            PairRequest.WithdrawQr();
            Expect(!File.Exists(QrPath), "nothing to withdraw");
        }

        private static PairingOffer Offer(PairingForm form)
        {
            var v = VectorTests.Load("pairing.json");
            var keys = new PairKeys(VectorTests.PrivateKey("pc_sign"), VectorTests.PrivateKey("pc_agree"), null, null);
            RelaySession session = RelaySession.Pending("https://example.com/pult", Json.String(v, "pair"), keys, Base64Url.Decode(Json.String(v, "secret")), QrPayload.DefaultIdleSeconds);
            var offer = new PairingOffer(session, 600);
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
