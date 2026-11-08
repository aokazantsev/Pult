using System;
using System.Collections.Generic;
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

        public static void RequestedFieldsFillTheWindow()
        {
            var request = new PairRequest { Server = "https://example.com/other", IdleSeconds = 1800 };
            using (var form = new PairingForm(new AppSettings { Server = "https://example.com/pult" }, null, Sessions(), request))
            {
                Dictionary<string, object> state = form.Describe();
                Expect((string)state["server"] == "https://example.com/other", "requested server");
                Expect((int)state["idle_seconds"] == 1800, "requested idle");
            }
            using (var form = new PairingForm(new AppSettings { Server = "https://example.com/pult" }, null, Sessions(), new PairRequest()))
            {
                Dictionary<string, object> state = form.Describe();
                Expect((string)state["server"] == "https://example.com/pult" && (int)state["idle_seconds"] == QrPayload.DefaultIdleSeconds, "defaults without fields");
            }
        }

        public static void DescribeFollowsTheWindow()
        {
            using (var form = new PairingForm(new AppSettings(), null, Sessions(), null))
            {
                Expect((string)form.Describe()["state"] == PairingForm.Creating && !form.Describe().ContainsKey("qr"), "a new window is creating a claim");
                PairingOffer offer = Offer(form);
                RelayClient client = Pending(form, offer);
                Dictionary<string, object> state = form.Describe();
                Expect((string)state["state"] == PairingForm.Showing, "showing");
                Expect((string)state["qr"] == offer.QrText && (string)state["pair"] == offer.Session.Pair, "QR and claim");
                Expect(new UTF8Encoding(false).GetString(Base64Url.Decode((string)state["qr_b64url"])) == offer.QrText, "base64url of the QR");
                Expect((int)state["qr_valid_seconds"] > 500, "validity is shown as on the screen");
                form.OnPaired(Stranger());
                Expect((string)form.Describe()["state"] == PairingForm.Showing, "a stranger does not pair the window");
                form.OnPaired(client);
                state = form.Describe();
                Expect((string)state["state"] == PairingForm.Paired && (string)state["pair"] == offer.Session.Pair && !state.ContainsKey("qr"), "paired: " + Json.Write(state));
            }
        }

        public static void RejectionAndFailureAreStates()
        {
            using (var form = new PairingForm(new AppSettings(), null, Sessions(), null))
            {
                RelayClient client = Pending(form, Offer(form));
                form.OnRejected(client);
                Dictionary<string, object> state = form.Describe();
                Expect((string)state["state"] == PairingForm.Rejected && state.ContainsKey("problem") && !state.ContainsKey("qr"), "rejected: " + Json.Write(state));
                typeof(PairingForm).GetMethod("ShowOffer", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(form, new object[] { null, "Сервер недоступен" });
                state = form.Describe();
                Expect((string)state["state"] == PairingForm.Failed && (string)state["problem"] == "Сервер недоступен", "failed: " + Json.Write(state));
            }
        }

        public static void HiddenWindowFailsWhenTheServerEndsItsClaim()
        {
            using (var form = new PairingForm(new AppSettings(), null, Sessions(), new PairRequest()))
            {
                GC.KeepAlive(form.Handle);
                RelayClient client = Pending(form, Offer(form));
                form.OnEnded(Stranger());
                Expect((string)form.Describe()["state"] == PairingForm.Showing, "an end of another claim changes nothing");
                form.OnEnded(client);
                Dictionary<string, object> state = form.Describe();
                Expect((string)state["state"] == PairingForm.Failed && state.ContainsKey("problem") && !state.ContainsKey("qr"), "the hidden window reports the ended claim and issues no new QR: " + Json.Write(state));
            }
        }

        public static void VisibleWindowReissuesWhenTheServerEndsItsClaim()
        {
            var form = new PairingForm(new AppSettings(), null, Sessions(), new PairRequest { Server = "ftp://example.com" });
            try
            {
                form.Opacity = 0;
                form.ShowInTaskbar = false;
                form.Show();
                DateTime until = DateTime.UtcNow.AddSeconds(10);
                while ((string)form.Describe()["state"] != PairingForm.Failed && DateTime.UtcNow < until)
                {
                    Application.DoEvents();
                    System.Threading.Thread.Sleep(10);
                }
                RelayClient client = Pending(form, Offer(form));
                form.OnEnded(client);
                Dictionary<string, object> state = form.Describe();
                Expect((string)state["state"] == PairingForm.Creating && !state.ContainsKey("problem"), "a window on the screen issues a new claim: " + Json.Write(state));
            }
            finally
            {
                form.Dispose();
            }
        }

        public static void HiddenWindowClosesWhenItsQrExpires()
        {
            var form = new PairingForm(new AppSettings(), null, Sessions(), new PairRequest());
            GC.KeepAlive(form.Handle);
            PairingOffer offer = NewOffer();
            typeof(PairingForm).GetField("offer", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(form, new PairingOffer(offer.Session, 10));
            Invoke(form, "Tick");
            Expect(form.IsDisposed, "a hidden window does not reissue an expiring QR, it closes");
        }

        public static void VisibleWindowReissuesAnExpiringQr()
        {
            var form = new PairingForm(new AppSettings(), null, Sessions(), new PairRequest { Server = "ftp://example.com" });
            try
            {
                form.Opacity = 0;
                form.ShowInTaskbar = false;
                form.Show();
                DateTime until = DateTime.UtcNow.AddSeconds(10);
                while ((string)form.Describe()["state"] != PairingForm.Failed && DateTime.UtcNow < until)
                {
                    Application.DoEvents();
                    System.Threading.Thread.Sleep(10);
                }
                typeof(PairingForm).GetField("offer", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(form, new PairingOffer(NewOffer().Session, 10));
                Invoke(form, "Tick");
                Expect(!form.IsDisposed && form.Visible, "a window on the screen stays open");
                Expect((string)form.Describe()["state"] == PairingForm.Creating, "a window on the screen issues a new claim");
            }
            finally
            {
                form.Dispose();
            }
        }

        public static void HiddenWindowWorksAndStartsOnce()
        {
            using (var form = new PairingForm(new AppSettings(), null, Sessions(), new PairRequest { Server = "ftp://example.com" }))
            {
                form.Begin();
                DateTime until = DateTime.UtcNow.AddSeconds(10);
                while ((string)form.Describe()["state"] == PairingForm.Creating && DateTime.UtcNow < until)
                {
                    Application.DoEvents();
                    System.Threading.Thread.Sleep(10);
                }
                Expect((string)form.Describe()["state"] == PairingForm.Failed, "the hidden window runs the claim: " + Json.Write(form.Describe()));
                Expect(!form.Visible, "the window stays hidden");
                form.Begin();
                Expect((string)form.Describe()["state"] == PairingForm.Failed, "a second start, as on showing the window, does not issue a new claim");
            }
        }

        public static void RequestedFoldersGoToTheClaim()
        {
            var request = new PairRequest();
            request.Folders.Add("D:\\lab");
            request.Folders.Add("C:\\two");
            using (var form = new PairingForm(new AppSettings(), session => new RelayClient(session, null), Sessions(), request))
            {
                PairingOffer offer = NewOffer();
                typeof(PairingForm).GetMethod("ShowOffer", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(form, new object[] { offer, null });
                Expect(offer.Session.Folders.SequenceEqual(new[] { "D:\\lab", "C:\\two" }), "requested folders go to the claim");
                Expect(form.Pending != null && (string)form.Describe()["qr"] == offer.QrText, "the claim QR is shown");
            }
        }

        public static void ClaimNotCreatedIsLogged()
        {
            string problem;
            Expect(PairingFlow.CreateOffer("ftp://example.com", 3600, out problem) == null && problem != null, "bad address is refused");
            Expect(File.ReadAllLines(AppLog.FilePath).Last().EndsWith("pairing: claim not created: bad server address"), "bad address is logged");
            Expect(PairingFlow.CreateOffer("http://127.0.0.1:1", 3600, out problem) == null && problem != null, "unreachable server is refused");
            Expect(File.ReadAllLines(AppLog.FilePath).Last().EndsWith("pairing: claim not created: " + problem), "unreachable server is logged with the reason");
        }

        private static DesktopSessions Sessions()
        {
            return new DesktopSessions(Path.Combine(UserDataPaths.Directory, "no-desktop-sessions"), TimeSpan.Zero);
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
