using System.Diagnostics;
using System.Net;
using System.Runtime.InteropServices;
using InternetMeter.Models;

namespace InternetMeter.Services;

public static class ConnectionViewerService
{
    private const int AddressFamilyInterNetwork = 2;
    private const int InsufficientBuffer = 122;

    public static IReadOnlyList<ConnectionInfo> GetTcpConnections()
    {
        var size = 0;
        var result = GetExtendedTcpTable(IntPtr.Zero, ref size, true, AddressFamilyInterNetwork, TcpTableClass.OwnerPidAll, 0);
        if (result != InsufficientBuffer || size <= 0) return [];

        var buffer = Marshal.AllocHGlobal(size);
        try
        {
            result = GetExtendedTcpTable(buffer, ref size, true, AddressFamilyInterNetwork, TcpTableClass.OwnerPidAll, 0);
            if (result != 0) return [];
            var count = Marshal.ReadInt32(buffer);
            var rowSize = Marshal.SizeOf<TcpRowOwnerPid>();
            var current = IntPtr.Add(buffer, sizeof(int));
            var connections = new List<ConnectionInfo>(count);
            var processNames = new Dictionary<int, string>();

            for (var index = 0; index < count; index++)
            {
                var row = Marshal.PtrToStructure<TcpRowOwnerPid>(current);
                var processId = unchecked((int)row.OwningPid);
                if (!processNames.TryGetValue(processId, out var processName))
                {
                    processName = GetProcessName(processId);
                    processNames[processId] = processName;
                }

                connections.Add(new ConnectionInfo(
                    processName,
                    processId,
                    "TCP",
                    FormatEndpoint(row.LocalAddress, row.LocalPort),
                    FormatEndpoint(row.RemoteAddress, row.RemotePort),
                    ((TcpState)row.State).ToString()));
                current = IntPtr.Add(current, rowSize);
            }

            return connections
                .OrderBy(connection => connection.Process, StringComparer.OrdinalIgnoreCase)
                .ThenBy(connection => connection.LocalEndpoint)
                .ToArray();
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static string GetProcessName(int processId)
    {
        if (processId == 0) return "System Idle";
        if (processId == 4) return "System";
        try { return Process.GetProcessById(processId).ProcessName; }
        catch { return "Unavailable"; }
    }

    private static string FormatEndpoint(uint address, uint port)
    {
        var ip = new IPAddress(BitConverter.GetBytes(address));
        var hostPort = unchecked((ushort)IPAddress.NetworkToHostOrder(unchecked((short)(port & 0xffff))));
        return $"{ip}:{hostPort}";
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TcpRowOwnerPid
    {
        public uint State;
        public uint LocalAddress;
        public uint LocalPort;
        public uint RemoteAddress;
        public uint RemotePort;
        public uint OwningPid;
    }

    private enum TcpTableClass
    {
        BasicListener,
        BasicConnections,
        BasicAll,
        OwnerPidListener,
        OwnerPidConnections,
        OwnerPidAll
    }

    private enum TcpState : uint
    {
        Closed = 1,
        Listen,
        SynSent,
        SynReceived,
        Established,
        FinWait1,
        FinWait2,
        CloseWait,
        Closing,
        LastAck,
        TimeWait,
        DeleteTcb
    }

    [DllImport("iphlpapi.dll", SetLastError = true)]
    private static extern int GetExtendedTcpTable(
        IntPtr table,
        ref int size,
        [MarshalAs(UnmanagedType.Bool)] bool order,
        int ipVersion,
        TcpTableClass tableClass,
        uint reserved);
}

