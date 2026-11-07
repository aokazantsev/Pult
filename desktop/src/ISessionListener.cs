namespace Pult
{
    internal interface ISessionListener
    {
        void OnPaired(RelayClient client);

        void OnPairingRejected(RelayClient client);

        void OnPairEnded(RelayClient client, string reason, string by);

        void OnNotice(RelayClient client, string text);
    }
}
