using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;

namespace Pult
{
    internal static class ControlChannel
    {
        public const int MaxMessageBytes = 256 * 1024;

        public static string PipeName
        {
            get { return "Pult.Control." + CurrentUser.Value + "." + Process.GetCurrentProcess().SessionId; }
        }

        public static SecurityIdentifier CurrentUser
        {
            get
            {
                using (WindowsIdentity identity = WindowsIdentity.GetCurrent()) return identity.User;
            }
        }

        public static PipeSecurity Security()
        {
            var security = new PipeSecurity();
            SecurityIdentifier user = CurrentUser;
            security.SetOwner(user);
            security.SetAccessRuleProtection(true, false);
            security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.NetworkSid, null), PipeAccessRights.FullControl, AccessControlType.Deny));
            security.AddAccessRule(new PipeAccessRule(user, PipeAccessRights.FullControl, AccessControlType.Allow));
            return security;
        }

        public static bool OwnedByCurrentUser(PipeStream pipe)
        {
            try
            {
                var owner = pipe.GetAccessControl().GetOwner(typeof(SecurityIdentifier)) as SecurityIdentifier;
                return owner != null && owner == CurrentUser;
            }
            catch (UnauthorizedAccessException)
            {
                return false;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
            catch (IOException)
            {
                return false;
            }
        }

        public static string ReadLine(Stream stream)
        {
            var bytes = new MemoryStream();
            while (true)
            {
                int value = stream.ReadByte();
                if (value < 0) return bytes.Length == 0 ? null : Decode(bytes);
                if (value == '\n') return Decode(bytes);
                if (bytes.Length >= MaxMessageBytes) throw new InvalidDataException("control message is longer than " + MaxMessageBytes + " bytes");
                bytes.WriteByte((byte)value);
            }
        }

        public static void WriteLine(Stream stream, string text)
        {
            byte[] bytes = new UTF8Encoding(false).GetBytes(text + "\n");
            stream.Write(bytes, 0, bytes.Length);
            stream.Flush();
        }

        private static string Decode(MemoryStream bytes)
        {
            return new UTF8Encoding(false, true).GetString(bytes.GetBuffer(), 0, (int)bytes.Length).TrimEnd('\r');
        }
    }
}
