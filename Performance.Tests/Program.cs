using System;
using System.Diagnostics;
using Current = ProbeRoulette.Core;
using Old = ProbeRoulette.Baseline;

internal static class Program
{
    private static          double               _sink;
    private static readonly Func<double, double> Gravity = radius => 80000000 / (radius * radius);

    private static int Main(string[] args)
    {
        ValidateEquivalence();
        Console.WriteLine($"Runtime: {Environment.Version}; 64 bit: {Environment.Is64BitProcess}; processor count: {Environment.ProcessorCount}");
        Console.WriteLine("Baseline: unmodified 0.1.2 core. Median of 7 alternating paired trials; Release build; microseconds per operation.");
        var surfaceGain = Measure("Surface prediction (2251 samples)", OldSurface, NewSurface, 1000);
        var aimGain     = Measure("Full aim (prediction + 6 solves)", OldAim, NewAim, 800);
        var missGain    = Measure("Distant probe sweep", OldMiss, NewMiss, 1000000);
        Measure("Nearby crossing sweep", OldHit, NewHit, 1000000);
        Console.WriteLine("Correctness: 200 randomized orbital cases, 1200 intercepts, 100000 sweep comparisons passed.");
        Console.WriteLine($"Sink: {_sink:F3}");

        return args.Length > 0 && args[0] == "--require-gain" && (surfaceGain < 10 || aimGain < 10 || missGain < 10) ? 1 : 0;
    }

    private static double Measure(string name, Action baseline, Action current, int operations)
    {
        for (var i = 0; i < 150; i++)
        {
            baseline();
            current();
        }

        var before = new double[7];
        var after  = new double[7];
        for (var trial = 0; trial < 7; trial++)
            if (trial % 2 == 0)
            {
                before[trial] = Time(baseline, operations);
                after[trial]  = Time(current, operations);
            }
            else
            {
                after[trial]  = Time(current, operations);
                before[trial] = Time(baseline, operations);
            }

        Array.Sort(before);
        Array.Sort(after);
        var gain = (1 - after[3] / before[3]) * 100;
        Console.WriteLine($"{name}: baseline={before[3]:F4} us, current={after[3]:F4} us, improvement={gain:F1}%; " +
                          $"ranges {before[0]:F4}-{before[6]:F4} / {after[0]:F4}-{after[6]:F4}");

        return gain;
    }

    private static double Time(Action action, int operations)
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        var timer = Stopwatch.StartNew();
        for (var i = 0; i < operations; i++) action();

        timer.Stop();

        return timer.Elapsed.TotalMilliseconds * 1000 / operations;
    }

    private static Old.Vector[] OldPath() => Old.Trajectory.Surface(new Old.Vector(6000, 0, 2500), new Old.Vector(20, 0, 95),
        new Old.Vector(-2000, 0, 2500), default, new Old.Vector(0, 50, 240), new Old.Vector(0.01, 0.04, 0.005),
        default, Gravity, 0.02, 2251);

    private static Current.Vector[] NewPath() => Current.Trajectory.Surface(new Current.Vector(6000, 0, 2500), new Current.Vector(20, 0, 95),
        new Current.Vector(-2000, 0, 2500), default, new Current.Vector(0, 50, 240), new Current.Vector(0.01, 0.04, 0.005),
        default, Gravity, 0.02, 2251);

    private static void OldSurface()
    {
        _sink += OldPath()[2250].X;
    }

    private static void NewSurface()
    {
        _sink += NewPath()[2250].X;
    }

    private static void OldAim()
    {
        var path = OldPath();
        for (var i = 0; i < 6; i++)
        {
            Old.Targeting.TryIntercept(path, 0.02, new Old.Vector(i, 0, 49.59), new Old.Vector(-80, 0, 10), 500, out var result);
            _sink += result.Time;
        }
    }

    private static void NewAim()
    {
        var path = NewPath();
        for (var i = 0; i < 6; i++)
        {
            Current.Targeting.TryIntercept(path, 0.02, new Current.Vector(i, 0, 49.59), new Current.Vector(-80, 0, 10), 500, out var result);
            _sink += result.Time;
        }
    }

    private static void OldMiss()
    {
        _sink += Old.HitGeometry.SweptSphereHitsCapsule(new Old.Vector(4000, 20, 10), new Old.Vector(3990, 20, 10),
            new Old.Vector(0, -0.5, 0), new Old.Vector(0, 0.5, 0), 1.5)
            ? 1
            : 0;
    }

    private static void NewMiss()
    {
        _sink += Current.HitGeometry.SweptSphereHitsCapsule(new Current.Vector(4000, 20, 10), new Current.Vector(3990, 20, 10),
            new Current.Vector(0, -0.5, 0), new Current.Vector(0, 0.5, 0), 1.5)
            ? 1
            : 0;
    }

    private static void OldHit()
    {
        _sink += Old.HitGeometry.SweptSphereHitsCapsule(new Old.Vector(-10, 0, 0), new Old.Vector(10, 0, 0),
            new Old.Vector(0, -0.5, 0), new Old.Vector(0, 0.5, 0), 1.5)
            ? 1
            : 0;
    }

    private static void NewHit()
    {
        _sink += Current.HitGeometry.SweptSphereHitsCapsule(new Current.Vector(-10, 0, 0), new Current.Vector(10, 0, 0),
            new Current.Vector(0, -0.5, 0), new Current.Vector(0, 0.5, 0), 1.5)
            ? 1
            : 0;
    }

    private static void ValidateEquivalence()
    {
        var random = new Random(45891);
        for (var trial = 0; trial < 200; trial++)
        {
            var spin     = new Old.Vector(random.NextDouble() * 0.1, random.NextDouble() * 0.1, random.NextDouble() * 0.1);
            var velocity = new Old.Vector(random.NextDouble() * 3, random.NextDouble(), random.NextDouble());
            var old = Old.Trajectory.Surface(new Old.Vector(6000, 0, 2500), new Old.Vector(20, 0, 95),
                new Old.Vector(-2000, 0, 2500), default, new Old.Vector(0, 50, 240), spin, velocity, Gravity, 0.02, 2251);
            var current = Current.Trajectory.Surface(new Current.Vector(6000, 0, 2500), new Current.Vector(20, 0, 95),
                new Current.Vector(-2000, 0, 2500), default, new Current.Vector(0, 50, 240), Convert(spin), Convert(velocity), Gravity, 0.02, 2251);
            for (var i = 0; i < old.Length; i++) Require((Convert(old[i]) - current[i]).Length < 1e-8, "Trajectory changed.");

            for (var i = 0; i < 6; i++)
            {
                var oldFound = Old.Targeting.TryIntercept(old, 0.02, new Old.Vector(i, 0, 49.59), new Old.Vector(-80, 0, 10), 500, out var oldResult);
                var newFound = Current.Targeting.TryIntercept(current, 0.02, new Current.Vector(i, 0, 49.59), new Current.Vector(-80, 0, 10), 500, out var newResult);
                Require(oldFound                                                    == newFound && Math.Abs(oldResult.Time - newResult.Time) < 1e-8 &&
                        (Convert(oldResult.Direction) - newResult.Direction).Length < 1e-8, "Intercept changed.");
            }
        }

        for (var trial = 0; trial < 100000; trial++)
        {
            var start   = RandomVector(random);
            var end     = RandomVector(random);
            var first   = RandomVector(random);
            var second  = RandomVector(random);
            var radius  = random.NextDouble() * 5;
            var old     = Old.HitGeometry.SweptSphereHitsCapsule(start, end, first, second, radius);
            var current = Current.HitGeometry.SweptSphereHitsCapsule(Convert(start), Convert(end), Convert(first), Convert(second), radius);
            Require(old == current, "Sweep changed.");
        }
    }

    private static Old.Vector RandomVector(Random random) => new(random.NextDouble() * 40 - 20, random.NextDouble() * 40 - 20, random.NextDouble() * 40 - 20);

    private static Current.Vector Convert(Old.Vector value) => new(value.X, value.Y, value.Z);

    private static void Require(bool valid, string message)
    {
        if (!valid)
        {
            throw new InvalidOperationException(message);
        }
    }
}
