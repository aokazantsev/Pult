namespace Pult
{
    internal sealed class DirectionKeys
    {
        public readonly byte[] PcToPhone;
        public readonly byte[] PhoneToPc;

        public DirectionKeys(byte[] pcToPhone, byte[] phoneToPc)
        {
            PcToPhone = pcToPhone;
            PhoneToPc = phoneToPc;
        }

        public byte[] For(string from)
        {
            if (from == "pc") return PcToPhone;
            if (from == "phone") return PhoneToPc;
            return null;
        }
    }
}
