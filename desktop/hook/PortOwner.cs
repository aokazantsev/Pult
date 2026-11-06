using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace PultHook
{
    internal static class PortOwner
    {
        private const int AfInet = 2;
        private const int TcpTableOwnerPidListener = 3;
        private const uint LoopbackAddress = 0x0100007F;
        private const uint ProcessQueryLimitedInformation = 0x1000;
        private const int RowFields = 6;

        [DllImport("iphlpapi.dll", SetLastError = true)]
        private static extern uint GetExtendedTcpTable(IntPtr table, ref int size, bool sorted, int addressFamily, int tableClass, uint reserved);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(uint access, bool inherit, uint processId);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool QueryFullProcessImageName(IntPtr process, uint flags, StringBuilder name, ref int size);

        [DllImport("kernel32.dll")]
        private static extern bool CloseHandle(IntPtr handle);

        public static string ImagePath(int port)
        {
            uint pid = ListenerPid(port);
            if (pid == 0) return null;
            IntPtr process = OpenProcess(ProcessQueryLimitedInformation, false, pid);
            if (process == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error(), "port " + port + " owner " + pid + " not readable");
            try
            {
                var name = new StringBuilder(1024);
                int size = name.Capacity;
                if (!QueryFullProcessImageName(process, 0, name, ref size)) throw new Win32Exception(Marshal.GetLastWin32Error());
                return name.ToString(0, size);
            }
            finally
            {
                CloseHandle(process);
            }
        }

        private static uint ListenerPid(int port)
        {
            int size = 0;
            GetExtendedTcpTable(IntPtr.Zero, ref size, false, AfInet, TcpTableOwnerPidListener, 0);
            for (int attempt = 0; attempt < 3; attempt++)
            {
                IntPtr table = Marshal.AllocHGlobal(size);
                try
                {
                    uint result = GetExtendedTcpTable(table, ref size, false, AfInet, TcpTableOwnerPidListener, 0);
                    if (result == 122) continue;
                    if (result != 0) throw new Win32Exception((int)result);
                    int rows = Marshal.ReadInt32(table);
                    uint found = 0;
                    for (int i = 0; i < rows; i++)
                    {
                        int row = 4 + i * RowFields * 4;
                        uint localAddress = (uint)Marshal.ReadInt32(table, row + 4);
                        uint localPort = (uint)Marshal.ReadInt32(table, row + 8);
                        uint owner = (uint)Marshal.ReadInt32(table, row + 20);
                        int hostPort = (int)(((localPort & 0xFF) << 8) | ((localPort >> 8) & 0xFF));
                        if (hostPort != port || (localAddress != LoopbackAddress && localAddress != 0)) continue;
                        if (found != 0 && found != owner) throw new InvalidOperationException("port " + port + " is shared by processes " + found + " and " + owner);
                        found = owner;
                    }
                    return found;
                }
                finally
                {
                    Marshal.FreeHGlobal(table);
                }
            }
            return 0;
        }
    }
}
