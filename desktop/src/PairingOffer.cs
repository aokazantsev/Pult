using System;

namespace Pult
{
    internal sealed class PairingOffer
    {
        public readonly RelaySession Session;
        public readonly DateTime ExpiresAtUtc;

        public PairingOffer(RelaySession session, int ttlSeconds)
        {
            Session = session;
            ExpiresAtUtc = DateTime.UtcNow.AddSeconds(ttlSeconds);
        }

        public string QrText
        {
            get { return QrPayload.Build(Session.Server, Session.Pair, Session.SecretCopy(), Session.Keys.SignPublic, Session.Keys.AgreePublic, Session.IdleSeconds); }
        }
    }
}
