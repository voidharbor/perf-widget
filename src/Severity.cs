using System;

namespace PerfWidget
{
    public enum Level { Normal = 0, Warm = 1, Hot = 2, Critical = 3 }

    // Clock event ("throttle") reason bits, straight from nvml.h.
    public static class Throttle
    {
        public const ulong GpuIdle      = 0x1;
        public const ulong AppClocks    = 0x2;
        public const ulong SwPowerCap   = 0x4;
        public const ulong HwSlowdown   = 0x8;
        public const ulong SyncBoost    = 0x10;
        public const ulong SwThermal    = 0x20;
        public const ulong HwThermal    = 0x40;
        public const ulong HwPowerBrake = 0x80;
        public const ulong DisplayClock = 0x100;

        public const ulong AnySlowdown = HwSlowdown | SwThermal | HwThermal | HwPowerBrake;
    }

    // The severity ladder. Pure functions so they can be unit tested without a GPU.
    // Number formatting that the strip and the tests share.
    public static class Fmt
    {
        public static string Rate(double bytesPerSec)
        {
            if (bytesPerSec < 0) bytesPerSec = 0;
            if (bytesPerSec < 999.5e3) return Math.Round(bytesPerSec / 1e3) + "K";
            if (bytesPerSec < 999.95e6) return (bytesPerSec / 1e6).ToString("0.0") + "M";
            return (bytesPerSec / 1e9).ToString("0.00") + "G";
        }

        // Log-scale activity meter: 10 KB/s is empty, 125 MB/s (a full gigabit) is full.
        public static double RateBar(double bytesPerSec)
        {
            if (bytesPerSec <= 1e4) return 0;
            double v = Math.Log10(bytesPerSec / 1e4) / Math.Log10(1.25e8 / 1e4);
            return Math.Max(0, Math.Min(1, v));
        }
    }

    public static class Severity
    {
        // GPU. The RTX 5070 Ti's maximum operating temperature is 90 C and the driver starts
        // slowing clocks a few degrees under that, so 87 is treated as critical even before the
        // driver admits it. Running at the power limit (SwPowerCap) is normal under full load and
        // is deliberately NOT a warning; the Mac widget's "text only when something is wrong"
        // rule would be broken by a badge that lit up during every game.
        public static Level Gpu(int tempC, ulong reasons)
        {
            if ((reasons & Throttle.AnySlowdown) != 0) return Level.Critical;
            if (tempC >= 87) return Level.Critical;
            if (tempC >= 82) return Level.Hot;
            if (tempC >= 75) return Level.Warm;
            return Level.Normal;
        }

        // System memory. 32 GB box with WSL allowed 24 GB of it, so real pressure is common.
        public static Level Memory(double usedFraction)
        {
            if (usedFraction >= 0.97) return Level.Critical;
            if (usedFraction >= 0.92) return Level.Hot;
            if (usedFraction >= 0.85) return Level.Warm;
            return Level.Normal;
        }

        // CPU. No temperature without an admin-installed driver, so the only visible sign of
        // throttling is a busy CPU whose effective clock has fallen under its base clock.
        // Idle clock drops are power saving, not throttling, hence the utilisation gate.
        public static Level Cpu(double utilityPct, double effectiveGHz, double baseGHz)
        {
            if (utilityPct < 90 || baseGHz <= 0) return Level.Normal;
            if (effectiveGHz < baseGHz * 0.85) return Level.Hot;
            if (effectiveGHz < baseGHz) return Level.Warm;
            return Level.Normal;
        }

        // Round-trip to 1.1.1.1. A timeout (-1) is shown orange, never red: red is for the thermal ladder.
        public static Level Ping(int ms)
        {
            if (ms < 0) return Level.Hot;
            if (ms >= 150) return Level.Hot;
            if (ms >= 80) return Level.Warm;
            return Level.Normal;
        }

        public static Level Max(params Level[] levels)
        {
            Level m = Level.Normal;
            foreach (Level l in levels) if (l > m) m = l;
            return m;
        }

        public static string Describe(Level l)
        {
            switch (l)
            {
                case Level.Warm: return "Warming up";
                case Level.Hot: return "Hot";
                case Level.Critical: return "Throttling";
                default: return "Nominal";
            }
        }

        public static string DescribeReasons(ulong reasons)
        {
            if (reasons == 0) return "none";
            var parts = new System.Collections.Generic.List<string>();
            if ((reasons & Throttle.HwThermal) != 0) parts.Add("hardware thermal slowdown");
            if ((reasons & Throttle.SwThermal) != 0) parts.Add("thermal slowdown");
            if ((reasons & Throttle.HwPowerBrake) != 0) parts.Add("power brake");
            if ((reasons & Throttle.HwSlowdown) != 0) parts.Add("hardware slowdown");
            if ((reasons & Throttle.SwPowerCap) != 0) parts.Add("power cap");
            if ((reasons & Throttle.AppClocks) != 0) parts.Add("app clock setting");
            if ((reasons & Throttle.SyncBoost) != 0) parts.Add("sync boost");
            if ((reasons & Throttle.DisplayClock) != 0) parts.Add("display clock");
            if ((reasons & Throttle.GpuIdle) != 0) parts.Add("idle");
            return parts.Count == 0 ? "none" : string.Join(", ", parts.ToArray());
        }
    }
}
