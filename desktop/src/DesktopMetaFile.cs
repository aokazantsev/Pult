using System;
using System.Collections.Generic;

namespace Pult
{
    internal sealed class DesktopMetaFile
    {
        public DateTime Stamp;
        public long Length;
        public DesktopMeta Meta;
        public List<string> CliIds;
    }
}
