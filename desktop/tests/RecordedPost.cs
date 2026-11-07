namespace Pult.Tests
{
    internal sealed class RecordedPost
    {
        public readonly string Op;
        public readonly string Stream;
        public readonly long N;
        public readonly bool SignatureValid;
        public readonly string Body;

        public RecordedPost(string op, string stream, long n, bool signatureValid, string body)
        {
            Op = op;
            Stream = stream;
            N = n;
            SignatureValid = signatureValid;
            Body = body;
        }
    }
}
