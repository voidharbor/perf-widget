using System;
using PerfWidget;

// Plain console test runner: csc has no test framework and none is needed for a ladder this size.
static class SeverityTests
{
    static int failures = 0;

    static void Check(string name, bool ok)
    {
        Console.WriteLine((ok ? "PASS " : "FAIL ") + name);
        if (!ok) failures++;
    }

    static int Main()
    {
        // GPU temperature ladder
        Check("gpu 40C normal",            Severity.Gpu(40, 0) == Level.Normal);
        Check("gpu 74C normal",            Severity.Gpu(74, 0) == Level.Normal);
        Check("gpu 75C warm",              Severity.Gpu(75, 0) == Level.Warm);
        Check("gpu 81C warm",              Severity.Gpu(81, 0) == Level.Warm);
        Check("gpu 82C hot",               Severity.Gpu(82, 0) == Level.Hot);
        Check("gpu 86C hot",               Severity.Gpu(86, 0) == Level.Hot);
        Check("gpu 87C critical",          Severity.Gpu(87, 0) == Level.Critical);
        // Reasons
        Check("power cap alone is normal", Severity.Gpu(70, Throttle.SwPowerCap) == Level.Normal);
        Check("idle reason is normal",     Severity.Gpu(35, Throttle.GpuIdle) == Level.Normal);
        Check("sw thermal is critical",    Severity.Gpu(60, Throttle.SwThermal) == Level.Critical);
        Check("hw thermal is critical",    Severity.Gpu(60, Throttle.HwThermal) == Level.Critical);
        Check("hw slowdown is critical",   Severity.Gpu(60, Throttle.HwSlowdown) == Level.Critical);
        Check("power brake is critical",   Severity.Gpu(60, Throttle.HwPowerBrake) == Level.Critical);
        Check("cap + thermal is critical", Severity.Gpu(84, Throttle.SwPowerCap | Throttle.SwThermal) == Level.Critical);

        // Memory ladder
        Check("mem 50% normal",            Severity.Memory(0.50) == Level.Normal);
        Check("mem 84% normal",            Severity.Memory(0.849) == Level.Normal);
        Check("mem 85% warm",              Severity.Memory(0.85) == Level.Warm);
        Check("mem 92% hot",               Severity.Memory(0.92) == Level.Hot);
        Check("mem 97% critical",          Severity.Memory(0.97) == Level.Critical);

        // CPU ladder (base 4.7 GHz)
        Check("cpu idle low clock normal", Severity.Cpu(5, 3.0, 4.7) == Level.Normal);
        Check("cpu busy boosting normal",  Severity.Cpu(100, 5.2, 4.7) == Level.Normal);
        Check("cpu busy at base normal",   Severity.Cpu(95, 4.7, 4.7) == Level.Normal);
        Check("cpu busy under base warm",  Severity.Cpu(95, 4.5, 4.7) == Level.Warm);
        Check("cpu busy far under hot",    Severity.Cpu(95, 3.9, 4.7) == Level.Hot);
        Check("cpu 89% never flags",       Severity.Cpu(89, 2.0, 4.7) == Level.Normal);
        Check("cpu unknown base normal",   Severity.Cpu(100, 2.0, 0) == Level.Normal);

        // Ping ladder
        Check("ping 12ms normal",          Severity.Ping(12) == Level.Normal);
        Check("ping 80ms warm",            Severity.Ping(80) == Level.Warm);
        Check("ping 150ms hot",            Severity.Ping(150) == Level.Hot);
        Check("ping timeout hot not red",  Severity.Ping(-1) == Level.Hot);

        // Rate formatting
        Check("rate 0",                    Fmt.Rate(0) == "0K");
        Check("rate 512K",                 Fmt.Rate(512e3) == "512K");
        Check("rate 999K rounds to 1.0M",  Fmt.Rate(999.6e3) == "1.0M");
        Check("rate 12.3M",                Fmt.Rate(12.34e6) == "12.3M");
        Check("rate 1.50G",                Fmt.Rate(1.5e9) == "1.50G");
        Check("ratebar idle empty",        Fmt.RateBar(0) == 0);
        Check("ratebar 10K empty",         Fmt.RateBar(1e4) == 0);
        Check("ratebar gigabit full",      Fmt.RateBar(1.25e8) == 1);
        Check("ratebar 1.25M mid",         Math.Abs(Fmt.RateBar(1.25e6) - 0.512) < 0.01);
        Check("ratebar above full clamps", Fmt.RateBar(1e12) == 1);

        // Helpers
        Check("max picks highest",         Severity.Max(Level.Normal, Level.Hot, Level.Warm) == Level.Hot);
        Check("max of nothing is normal",  Severity.Max() == Level.Normal);
        Check("describe critical",         Severity.Describe(Level.Critical) == "Throttling");
        Check("reasons none",              Severity.DescribeReasons(0) == "none");
        Check("reasons cap",               Severity.DescribeReasons(Throttle.SwPowerCap) == "power cap");
        Check("reasons two",               Severity.DescribeReasons(Throttle.SwThermal | Throttle.SwPowerCap) == "thermal slowdown, power cap");

        Console.WriteLine(failures == 0 ? "ALL PASS" : failures + " FAILED");
        return failures == 0 ? 0 : 1;
    }
}
