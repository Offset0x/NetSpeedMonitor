using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;

namespace NetSpeedMonitor
{
    internal enum AdapterMode { Auto, All, Manual }

    internal sealed class NetworkSampler : IDisposable
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct MIB_IPFORWARDROW
        {
            public uint dwForwardDest;
            public uint dwForwardMask;
            public uint dwForwardPolicy;
            public uint dwForwardNextHop;
            public uint dwForwardIfIndex;
            public uint dwForwardType;
            public uint dwForwardProto;
            public uint dwForwardAge;
            public uint dwForwardNextHopAS;
            public uint dwForwardMetric1;
            public uint dwForwardMetric2;
            public uint dwForwardMetric3;
            public uint dwForwardMetric4;
            public uint dwForwardMetric5;
        }

        [DllImport("iphlpapi.dll", SetLastError = true)]
        private static extern int GetBestRoute(uint dwDestAddr, uint dwSourceAddr, ref MIB_IPFORWARDROW pBestRoute);

        [DllImport("iphlpapi.dll", SetLastError = true)]
        private static extern int GetBestInterface(uint dwDestAddr, out uint pdwBestIfIndex);

        private const uint ERROR_SUCCESS = 0;

        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private double _smoothedUp;
        private double _smoothedDown;
        private long _lastBytesSent;
        private long _lastBytesRecv;
        private long _lastTicks;
        private bool _primed;

        private AdapterMode _mode = AdapterMode.Auto;
        private string _manualAdapterName = "";
        private NetworkInterface[] _activeAdapters;
        private long _lastRouteCheckTicks;
        private readonly TimeSpan _routeRecheckInterval = TimeSpan.FromSeconds(10);

        public double SmoothedUpload => _smoothedUp;
        public double SmoothedDownload => _smoothedDown;

        public void Configure(AdapterMode mode, string manualAdapterName)
        {
            _mode = mode;
            _manualAdapterName = manualAdapterName ?? "";
            _activeAdapters = null;
            _lastRouteCheckTicks = 0;
            _primed = false;
        }

        public static List<NetworkInterface> GetCandidateAdapters()
        {
            try
            {
                return NetworkInterface.GetAllNetworkInterfaces()
                    .Where(n => n.OperationalStatus == OperationalStatus.Up &&
                                n.NetworkInterfaceType != NetworkInterfaceType.Loopback &&
                                n.NetworkInterfaceType != NetworkInterfaceType.Tunnel &&
                                !n.Description.Contains("Loopback", StringComparison.OrdinalIgnoreCase))
                    .ToList();
            }
            catch { return new List<NetworkInterface>(); }
        }

        public static string Describe(NetworkInterface n)
        {
            string type = n.NetworkInterfaceType.ToString();
            return $"{n.Name}  [{type}]";
        }

        private NetworkInterface[] ResolveAdapters()
        {
            var candidates = GetCandidateAdapters();
            if (candidates.Count == 0) return Array.Empty<NetworkInterface>();

            switch (_mode)
            {
                case AdapterMode.Manual:
                    {
                        var match = candidates.FirstOrDefault(n =>
                            string.Equals(n.Name, _manualAdapterName, StringComparison.OrdinalIgnoreCase));
                        if (match != null) return new[] { match };
                        return Array.Empty<NetworkInterface>();
                    }
                case AdapterMode.All:
                    return candidates.ToArray();
                case AdapterMode.Auto:
                default:
                    {
                        var def = FindDefaultRouteAdapter(candidates);
                        if (def != null) return new[] { def };
                        return candidates.ToArray();
                    }
            }
        }

        private NetworkInterface FindDefaultRouteAdapter(List<NetworkInterface> candidates)
        {
            try
            {
                uint dest = BitConverter.ToUInt32(IPAddress.Parse("8.8.8.8").GetAddressBytes(), 0);
                if (GetBestInterface(dest, out uint ifIndex) == ERROR_SUCCESS && ifIndex != 0)
                {
                    var match = candidates.FirstOrDefault(n =>
                    {
                        try { return n.GetIPProperties().GetIPv4Properties().Index == ifIndex; }
                        catch { return false; }
                    });
                    if (match != null) return match;
                }

                var row = new MIB_IPFORWARDROW();
                if (GetBestRoute(dest, 0, ref row) == ERROR_SUCCESS && row.dwForwardIfIndex != 0)
                {
                    var match = candidates.FirstOrDefault(n =>
                    {
                        try { return n.GetIPProperties().GetIPv4Properties().Index == row.dwForwardIfIndex; }
                        catch { return false; }
                    });
                    if (match != null) return match;
                }
            }
            catch { }

            return candidates.FirstOrDefault(n =>
                n.NetworkInterfaceType == NetworkInterfaceType.Ethernet ||
                n.NetworkInterfaceType == NetworkInterfaceType.Wireless80211) ?? candidates[0];
        }

        public void Sample(out double upBytesPerSec, out double downBytesPerSec, out long deltaUpBytes, out long deltaDownBytes)
        {
            upBytesPerSec = 0;
            downBytesPerSec = 0;
            deltaUpBytes = 0;
            deltaDownBytes = 0;

            long nowTicks = _clock.ElapsedTicks;
            if (_activeAdapters == null || (nowTicks - _lastRouteCheckTicks) > _routeRecheckInterval.Ticks)
            {
                var resolved = ResolveAdapters();
                _lastRouteCheckTicks = nowTicks;
                if (_activeAdapters == null || !SameAdapters(_activeAdapters, resolved))
                {
                    _activeAdapters = resolved;
                    _primed = false;
                }
                else
                {
                    _activeAdapters = resolved;
                }
            }

            if (_activeAdapters == null || _activeAdapters.Length == 0)
            {
                _smoothedUp = 0;
                _smoothedDown = 0;
                return;
            }

            long totalSent = 0, totalRecv = 0;
            foreach (var n in _activeAdapters)
            {
                try
                {
                    var stats = n.GetIPStatistics();
                    totalSent += stats.BytesSent;
                    totalRecv += stats.BytesReceived;
                }
                catch { }
            }

            if (!_primed)
            {
                _lastBytesSent = totalSent;
                _lastBytesRecv = totalRecv;
                _lastTicks = nowTicks;
                _primed = true;
                return;
            }

            long dSent = totalSent - _lastBytesSent;
            long dRecv = totalRecv - _lastBytesRecv;
            if (dSent < 0) dSent = 0;
            if (dRecv < 0) dRecv = 0;

            double elapsedSec = (nowTicks - _lastTicks) / (double)Stopwatch.Frequency;
            if (elapsedSec <= 0) elapsedSec = 1e-6;

            upBytesPerSec = dSent / elapsedSec;
            downBytesPerSec = dRecv / elapsedSec;
            deltaUpBytes = dSent;
            deltaDownBytes = dRecv;

            _lastBytesSent = totalSent;
            _lastBytesRecv = totalRecv;
            _lastTicks = nowTicks;
        }

        private static bool SameAdapters(NetworkInterface[] a, NetworkInterface[] b)
        {
            if (a == null || b == null) return false;
            if (a.Length != b.Length) return false;
            var namesA = a.Select(x => x.Id).OrderBy(x => x).ToArray();
            var namesB = b.Select(x => x.Id).OrderBy(x => x).ToArray();
            for (int i = 0; i < namesA.Length; i++)
                if (namesA[i] != namesB[i]) return false;
            return true;
        }

        public void ApplySmoothing(double instantUp, double instantDown, double alpha)
        {
            if (alpha <= 0)
            {
                _smoothedUp = instantUp;
                _smoothedDown = instantDown;
            }
            else if (alpha >= 1)
            {
                _smoothedUp = instantUp;
                _smoothedDown = instantDown;
            }
            else
            {
                _smoothedUp = alpha * instantUp + (1 - alpha) * _smoothedUp;
                _smoothedDown = alpha * instantDown + (1 - alpha) * _smoothedDown;
            }
        }

        public void ResetSmoothing()
        {
            _smoothedUp = 0;
            _smoothedDown = 0;
        }

        public void Dispose() { }
    }
}
