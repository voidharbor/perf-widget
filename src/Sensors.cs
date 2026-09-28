using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;

namespace PerfWidget
{
    // One sample of everything the widget shows.
    public class Reading
    {
        public DateTime At;

        public bool GpuOk;
        public string GpuName = "GPU";
        public int GpuTemp;            // C
        public int GpuLoad;            // %
        public int GpuMemLoad;         // % (memory controller busy)
        public double VramUsedGB, VramTotalGB;
        public double PowerW, PowerLimitW;
        public int ClockMHz, ClockMaxMHz;
        public int FanPct;             // -1 when the driver reports no fan
        public int PState;
        public ulong Reasons;
        public string GpuError = "";

        public string CpuName = "CPU";
        public double CpuUtility;      // % (time x performance, clamped 0..100 for display)
        public double CpuTime;         // % Processor Time, raw
        public double CpuPerf;         // % of base clock
        public double CpuBaseGHz;
        public double CpuEffectiveGHz;

        public double MemUsedGB, MemTotalGB;
        public double MemFraction;

        public bool NetOk;
        public double NetDown, NetUp;   // bytes per second over the physical adapters
        public int PingMs = -2;         // -2 not measured yet, -1 timeout
        public Level PingLevel;

        public Level GpuLevel;
        public Level CpuLevel;
        public Level MemLevel;
        public Level Overall;
    }

    // Ring buffers of the last three minutes at one sample per two seconds, for the panel's sparklines.
    public class History
    {
        public const int Cap = 90;
        public readonly float[] GpuLoad = new float[Cap], GpuTemp = new float[Cap], Cpu = new float[Cap], Mem = new float[Cap], Down = new float[Cap], Up = new float[Cap];
        int head, count;

        public int Count { get { return count; } }

        public void Add(Reading r)
        {
            GpuLoad[head] = r.GpuOk ? r.GpuLoad : 0;
            GpuTemp[head] = r.GpuOk ? r.GpuTemp : 0;
            Cpu[head] = (float)r.CpuUtility;
            Mem[head] = (float)(r.MemFraction * 100);
            Down[head] = (float)r.NetDown;
            Up[head] = (float)r.NetUp;
            head = (head + 1) % Cap;
            if (count < Cap) count++;
        }

        // Oldest first.
        public float[] Ordered(float[] ring)
        {
            var o = new float[count];
            int start = (head - count + Cap) % Cap;
            for (int i = 0; i < count; i++) o[i] = ring[(start + i) % Cap];
            return o;
        }
    }

    // One ICMP echo to 1.1.1.1 every four seconds on a pool thread; the sampler reads the latest answer.
    class PingWorker : IDisposable
    {
        volatile int lastMs = -2;
        volatile bool busy;
        readonly Ping ping = new Ping();
        readonly System.Threading.Timer timer;

        public int LastMs { get { return lastMs; } }

        public PingWorker() { timer = new System.Threading.Timer(Tick, null, 300, 4000); }

        void Tick(object state)
        {
            if (busy) return;
            busy = true;
            try
            {
                PingReply reply = ping.Send("1.1.1.1", 1500);
                lastMs = reply.Status == IPStatus.Success ? (int)reply.RoundtripTime : -1;
            }
            catch { lastMs = -1; }
            finally { busy = false; }
        }

        public void Dispose() { timer.Dispose(); ping.Dispose(); }
    }

    // NVML P/Invoke. nvml.dll ships with the display driver in System32; no SDK needed.
    static class Nvml
    {
        [StructLayout(LayoutKind.Sequential)] public struct Utilization { public uint gpu; public uint memory; }
        [StructLayout(LayoutKind.Sequential)] public struct Memory { public ulong total; public ulong free; public ulong used; }

        [DllImport("nvml.dll", EntryPoint = "nvmlInit_v2")] public static extern int Init();
        [DllImport("nvml.dll", EntryPoint = "nvmlShutdown")] public static extern int Shutdown();
        [DllImport("nvml.dll", EntryPoint = "nvmlDeviceGetHandleByIndex_v2")] public static extern int GetHandleByIndex(uint index, out IntPtr device);
        [DllImport("nvml.dll", EntryPoint = "nvmlDeviceGetName")] public static extern int GetName(IntPtr device, StringBuilder name, uint length);
        [DllImport("nvml.dll", EntryPoint = "nvmlDeviceGetTemperature")] public static extern int GetTemperature(IntPtr device, int sensor, out uint temp);
        [DllImport("nvml.dll", EntryPoint = "nvmlDeviceGetUtilizationRates")] public static extern int GetUtilizationRates(IntPtr device, out Utilization util);
        [DllImport("nvml.dll", EntryPoint = "nvmlDeviceGetMemoryInfo")] public static extern int GetMemoryInfo(IntPtr device, out Memory mem);
        [DllImport("nvml.dll", EntryPoint = "nvmlDeviceGetPowerUsage")] public static extern int GetPowerUsage(IntPtr device, out uint milliwatts);
        [DllImport("nvml.dll", EntryPoint = "nvmlDeviceGetEnforcedPowerLimit")] public static extern int GetEnforcedPowerLimit(IntPtr device, out uint milliwatts);
        [DllImport("nvml.dll", EntryPoint = "nvmlDeviceGetClockInfo")] public static extern int GetClockInfo(IntPtr device, int type, out uint mhz);
        [DllImport("nvml.dll", EntryPoint = "nvmlDeviceGetMaxClockInfo")] public static extern int GetMaxClockInfo(IntPtr device, int type, out uint mhz);
        [DllImport("nvml.dll", EntryPoint = "nvmlDeviceGetCurrentClocksEventReasons")] public static extern int GetClocksEventReasons(IntPtr device, out ulong reasons);
        [DllImport("nvml.dll", EntryPoint = "nvmlDeviceGetCurrentClocksThrottleReasons")] public static extern int GetClocksThrottleReasons(IntPtr device, out ulong reasons);
        [DllImport("nvml.dll", EntryPoint = "nvmlDeviceGetFanSpeed")] public static extern int GetFanSpeed(IntPtr device, out uint pct);
        [DllImport("nvml.dll", EntryPoint = "nvmlDeviceGetPerformanceState")] public static extern int GetPerformanceState(IntPtr device, out int pstate);

        public const int TEMPERATURE_GPU = 0;
        public const int CLOCK_GRAPHICS = 0;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys, ullAvailPhys, ullTotalPageFile, ullAvailPageFile, ullTotalVirtual, ullAvailVirtual, ullAvailExtendedVirtual;
    }

    public class Sampler : IDisposable
    {
        [DllImport("kernel32.dll", SetLastError = true)] static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX buf);

        IntPtr device = IntPtr.Zero;
        bool nvmlUp = false;
        bool reasonsNewApi = true;
        string gpuName = "GPU";
        string gpuError = "";

        // "% Processor Utility" is unusable on this box (it echoes % Processor Performance, 111 % at idle),
        // so load = % Processor Time scaled by performance, which is what Task Manager shows, clamped to 100.
        PerformanceCounter time, perf;
        string cpuName = "CPU";
        double cpuBaseGHz = 0;

        readonly PingWorker pinger = new PingWorker();
        NetworkInterface[] nics;
        DateTime nicsAt;
        long lastRx, lastTx;
        DateTime lastNetAt;
        bool netPrimed;

        Dictionary<int, TimeSpan> procTimes = new Dictionary<int, TimeSpan>();
        DateTime procAt;

        public readonly History History = new History();

        public Sampler()
        {
            InitGpu();
            InitCpu();
        }

        void InitGpu()
        {
            try
            {
                int rc = Nvml.Init();
                if (rc != 0) { gpuError = "nvmlInit failed (" + rc + ")"; return; }
                rc = Nvml.GetHandleByIndex(0, out device);
                if (rc != 0) { gpuError = "no GPU handle (" + rc + ")"; return; }
                var sb = new StringBuilder(96);
                if (Nvml.GetName(device, sb, 96) == 0) gpuName = sb.ToString();
                nvmlUp = true;
            }
            catch (Exception ex)
            {
                gpuError = "nvml.dll: " + ex.GetType().Name;
            }
        }

        void InitCpu()
        {
            try
            {
                using (var key = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0"))
                {
                    if (key != null)
                    {
                        object n = key.GetValue("ProcessorNameString");
                        if (n != null) cpuName = n.ToString().Trim();
                        object mhz = key.GetValue("~MHz");
                        if (mhz != null) cpuBaseGHz = Convert.ToDouble(mhz) / 1000.0;
                    }
                }
            }
            catch { }
            try
            {
                time = new PerformanceCounter("Processor Information", "% Processor Time", "_Total", true);
                perf = new PerformanceCounter("Processor Information", "% Processor Performance", "_Total", true);
                time.NextValue(); perf.NextValue();   // first read of a rate counter is always 0
            }
            catch { time = null; perf = null; }
        }

        public Reading Sample()
        {
            var r = new Reading();
            r.At = DateTime.Now;
            SampleGpu(r);
            SampleCpu(r);
            SampleMemory(r);
            SampleNet(r);
            r.PingMs = pinger.LastMs;
            r.GpuLevel = r.GpuOk ? Severity.Gpu(r.GpuTemp, r.Reasons) : Level.Normal;
            r.CpuLevel = Severity.Cpu(r.CpuUtility, r.CpuEffectiveGHz, r.CpuBaseGHz);
            r.MemLevel = Severity.Memory(r.MemFraction);
            r.PingLevel = r.PingMs == -2 ? Level.Normal : Severity.Ping(r.PingMs);
            r.Overall = Severity.Max(r.GpuLevel, r.CpuLevel, r.MemLevel);   // network is its own signal, not thermal health
            History.Add(r);
            return r;
        }

        void SampleGpu(Reading r)
        {
            r.GpuName = gpuName;
            r.GpuError = gpuError;
            r.FanPct = -1;
            if (!nvmlUp) return;
            try
            {
                uint u; int i;
                if (Nvml.GetTemperature(device, Nvml.TEMPERATURE_GPU, out u) == 0) r.GpuTemp = (int)u;
                Nvml.Utilization util;
                if (Nvml.GetUtilizationRates(device, out util) == 0) { r.GpuLoad = (int)util.gpu; r.GpuMemLoad = (int)util.memory; }
                Nvml.Memory mem;
                if (Nvml.GetMemoryInfo(device, out mem) == 0) { r.VramUsedGB = mem.used / 1073741824.0; r.VramTotalGB = mem.total / 1073741824.0; }
                if (Nvml.GetPowerUsage(device, out u) == 0) r.PowerW = u / 1000.0;
                if (Nvml.GetEnforcedPowerLimit(device, out u) == 0) r.PowerLimitW = u / 1000.0;
                if (Nvml.GetClockInfo(device, Nvml.CLOCK_GRAPHICS, out u) == 0) r.ClockMHz = (int)u;
                if (Nvml.GetMaxClockInfo(device, Nvml.CLOCK_GRAPHICS, out u) == 0) r.ClockMaxMHz = (int)u;
                if (Nvml.GetFanSpeed(device, out u) == 0) r.FanPct = (int)u;
                if (Nvml.GetPerformanceState(device, out i) == 0) r.PState = i;
                r.Reasons = ReadReasons();
                r.GpuOk = true;
            }
            catch (Exception ex)
            {
                r.GpuOk = false;
                r.GpuError = ex.GetType().Name;
            }
        }

        ulong ReadReasons()
        {
            ulong l = 0;
            if (reasonsNewApi)
            {
                try { if (Nvml.GetClocksEventReasons(device, out l) == 0) return l; }
                catch (EntryPointNotFoundException) { reasonsNewApi = false; }
            }
            if (!reasonsNewApi)
            {
                try { if (Nvml.GetClocksThrottleReasons(device, out l) == 0) return l; }
                catch (EntryPointNotFoundException) { }
            }
            return 0;
        }

        void SampleCpu(Reading r)
        {
            r.CpuName = cpuName;
            r.CpuBaseGHz = cpuBaseGHz;
            if (time == null) return;
            try
            {
                double t = time.NextValue();
                double p = perf.NextValue();
                double u = p > 0 ? t * p / 100.0 : t;
                r.CpuTime = t;
                r.CpuUtility = Math.Max(0, Math.Min(100, u));
                r.CpuPerf = p;
                r.CpuEffectiveGHz = cpuBaseGHz * p / 100.0;
            }
            catch { }
        }

        void SampleMemory(Reading r)
        {
            var m = new MEMORYSTATUSEX();
            m.dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX));
            if (GlobalMemoryStatusEx(ref m))
            {
                r.MemTotalGB = m.ullTotalPhys / 1073741824.0;
                r.MemUsedGB = (m.ullTotalPhys - m.ullAvailPhys) / 1073741824.0;
                r.MemFraction = m.ullTotalPhys == 0 ? 0 : (double)(m.ullTotalPhys - m.ullAvailPhys) / m.ullTotalPhys;
            }
        }

        static readonly string[] VirtualMarks = { "virtual", "hyper-v", "tailscale", "vmware", "virtualbox", "wsl", "bluetooth", "loopback", "tap-", "wireguard", "npcap", "vethernet", "vpn" };

        static bool IsPhysical(NetworkInterface n)
        {
            if (n.OperationalStatus != OperationalStatus.Up) return false;
            var t = n.NetworkInterfaceType;
            if (t != NetworkInterfaceType.Ethernet && t != NetworkInterfaceType.GigabitEthernet && t != NetworkInterfaceType.FastEthernetT && t != NetworkInterfaceType.Wireless80211) return false;
            string d = (n.Description + " " + n.Name).ToLowerInvariant();
            foreach (string m in VirtualMarks) if (d.Contains(m)) return false;
            return true;
        }

        // Sum of the physical adapters, so tunnel and virtual-switch traffic is not counted twice.
        void SampleNet(Reading r)
        {
            try
            {
                DateTime now = DateTime.Now;
                if (nics == null || (now - nicsAt).TotalSeconds > 30)
                {
                    nics = NetworkInterface.GetAllNetworkInterfaces().Where(IsPhysical).ToArray();
                    nicsAt = now;
                }
                long rx = 0, tx = 0;
                foreach (var n in nics)
                {
                    var st = n.GetIPv4Statistics();
                    rx += st.BytesReceived; tx += st.BytesSent;
                }
                if (netPrimed)
                {
                    double dt = (now - lastNetAt).TotalSeconds;
                    if (dt > 0.2)
                    {
                        r.NetDown = Math.Max(0, rx - lastRx) / dt;
                        r.NetUp = Math.Max(0, tx - lastTx) / dt;
                        r.NetOk = true;
                    }
                }
                lastRx = rx; lastTx = tx; lastNetAt = now; netPrimed = true;
            }
            catch { r.NetOk = false; }
        }

        // Top three processes by CPU since the previous call, grouped by name. Only called while the
        // panel is open: it walks every process. The first call primes and returns "".
        public string TopCpuText()
        {
            DateTime now = DateTime.Now;
            double dt = (now - procAt).TotalSeconds;
            var next = new Dictionary<int, TimeSpan>();
            var byName = new Dictionary<string, double>();
            foreach (var p in Process.GetProcesses())
            {
                try
                {
                    if (p.Id == 0) continue;
                    TimeSpan t = p.TotalProcessorTime;
                    next[p.Id] = t;
                    TimeSpan prev;
                    if (procAt != DateTime.MinValue && dt > 0 && procTimes.TryGetValue(p.Id, out prev))
                    {
                        double pct = (t - prev).TotalSeconds / dt / Environment.ProcessorCount * 100;
                        if (pct > 0.05)
                        {
                            string n = p.ProcessName;
                            byName[n] = (byName.ContainsKey(n) ? byName[n] : 0) + pct;
                        }
                    }
                }
                catch { }
                finally { p.Dispose(); }
            }
            procTimes = next; procAt = now;
            if (byName.Count == 0) return "";
            var top = byName.OrderByDescending(kv => kv.Value).Take(3).Select(kv => kv.Key + " " + (kv.Value < 9.95 ? kv.Value.ToString("0.0") : kv.Value.ToString("0")) + "%");
            return string.Join("   ", top.ToArray());
        }

        public void ForgetProcs() { procTimes = new Dictionary<int, TimeSpan>(); procAt = DateTime.MinValue; }

        public void Dispose()
        {
            pinger.Dispose();
            if (time != null) time.Dispose();
            if (perf != null) perf.Dispose();
            if (nvmlUp) { try { Nvml.Shutdown(); } catch { } nvmlUp = false; }
        }

        // Text dump for --probe and for the report.
        public static string Dump(Reading r)
        {
            var sb = new StringBuilder();
            sb.AppendLine("at:            " + r.At.ToString("yyyy-MM-dd HH:mm:ss"));
            sb.AppendLine("gpu:           " + r.GpuName + (r.GpuOk ? "" : "  (" + r.GpuError + ")"));
            sb.AppendLine("gpu temp:      " + r.GpuTemp + " C");
            sb.AppendLine("gpu load:      " + r.GpuLoad + " %  (memory bus " + r.GpuMemLoad + " %)");
            sb.AppendLine("gpu power:     " + r.PowerW.ToString("0") + " / " + r.PowerLimitW.ToString("0") + " W");
            sb.AppendLine("gpu clock:     " + r.ClockMHz + " / " + r.ClockMaxMHz + " MHz");
            sb.AppendLine("vram:          " + r.VramUsedGB.ToString("0.0") + " / " + r.VramTotalGB.ToString("0.0") + " GB");
            sb.AppendLine("fan:           " + (r.FanPct < 0 ? "n/a" : r.FanPct + " %"));
            sb.AppendLine("pstate:        P" + r.PState);
            sb.AppendLine("reasons:       0x" + r.Reasons.ToString("x") + " = " + Severity.DescribeReasons(r.Reasons));
            sb.AppendLine("cpu:           " + r.CpuName);
            sb.AppendLine("cpu load:      " + r.CpuUtility.ToString("0") + " %  (processor time " + r.CpuTime.ToString("0") + " % x performance)");
            sb.AppendLine("cpu perf:      " + r.CpuPerf.ToString("0") + " % of base " + r.CpuBaseGHz.ToString("0.0") + " GHz = " + r.CpuEffectiveGHz.ToString("0.00") + " GHz");
            sb.AppendLine("cpu temp:      unavailable (needs an admin-installed driver)");
            sb.AppendLine("memory:        " + r.MemUsedGB.ToString("0.0") + " / " + r.MemTotalGB.ToString("0.0") + " GB (" + (r.MemFraction * 100).ToString("0") + " %)");
            sb.AppendLine("network:       down " + Fmt.Rate(r.NetDown) + "B/s  up " + Fmt.Rate(r.NetUp) + "B/s" + (r.NetOk ? "" : "  (not primed)"));
            sb.AppendLine("ping:          " + (r.PingMs >= 0 ? r.PingMs + " ms" : r.PingMs == -1 ? "timeout" : "pending") + " to 1.1.1.1");
            sb.AppendLine("levels:        gpu=" + r.GpuLevel + " cpu=" + r.CpuLevel + " mem=" + r.MemLevel + " overall=" + r.Overall + " (" + Severity.Describe(r.Overall) + ")");
            return sb.ToString();
        }
    }
}
