using System.Collections.Generic;

namespace Pult
{
    internal sealed class OpenedEnvelope
    {
        public readonly bool Accepted;
        public readonly string Problem;
        public readonly string Id;
        public readonly string From;
        public readonly long UnsupportedVersion;
        public readonly string Plaintext;
        public readonly Dictionary<string, object> Inner;

        private OpenedEnvelope(bool accepted, string problem, string id, string from, long unsupportedVersion, string plaintext, Dictionary<string, object> inner)
        {
            Accepted = accepted;
            Problem = problem;
            Id = id;
            From = from;
            UnsupportedVersion = unsupportedVersion;
            Plaintext = plaintext;
            Inner = inner;
        }

        public bool IsUnsupportedVersion
        {
            get { return UnsupportedVersion >= 0; }
        }

        public static OpenedEnvelope Ok(string id, string from, string plaintext, Dictionary<string, object> inner)
        {
            return new OpenedEnvelope(true, null, id, from, -1, plaintext, inner);
        }

        public static OpenedEnvelope Invalid(string problem)
        {
            return new OpenedEnvelope(false, problem, null, null, -1, null, null);
        }

        public static OpenedEnvelope Unsupported(string id, long version)
        {
            return new OpenedEnvelope(false, "unsupported version " + version, id, null, version, null, null);
        }
    }
}
