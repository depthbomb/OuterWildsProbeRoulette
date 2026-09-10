using Mono.Cecil;
using ProbeRoulette.Core;
using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

internal static class Program
{
    private const double Step    = 0.02;
    private const int    Samples = 2251;

    private static int Main(string[] args)
    {
        var tests = new (string Name, Action Run)[]
        {
            ("Loops 1-6 are untouched; loop 7 is eligible", LoopGate),
            ("Development and production probabilities", Probabilities),
            ("Forced targets and disabled targeting", ForcedTargets),
            ("Stationary target and muzzle offset", Stationary),
            ("Moving target and inherited probe velocity", Moving),
            ("World translation and shared velocity invariance", FrameInvariance),
            ("Unreachable and invalid trajectories", Unreachable),
            ("Planet rotation matches an analytical surface point", SurfaceRotation),
            ("Orbital prediction converges against circular motion", OrbitConvergence),
            ("Intercept of an orbiting and rotating surface target", SurfaceIntercept),
            ("Aim can be evaded after launch", Evasion),
            ("Swept player hit catches a probe crossing in one frame", SweptPlayerHit),
            ("Swept player hit rejects a near miss", SweptPlayerMiss),
            ("Capsule ends and grazing contacts", CapsuleEnds),
            ("Parallel and stationary hit paths", DegenerateHits),
            ("Player motion and world shifts preserve hit geometry", RelativeHits),
            ("Invalid hit geometry is rejected", InvalidHits),
            ("Public beta package defaults and version agree", () => ReleaseMetadata(args)),
            ("Patch methods and private fields bind to installed game", () => CheckBindings(args))
        };

        try
        {
            foreach (var test in tests)
            {
                test.Run();
                Console.WriteLine("PASS " + test.Name);
            }

            Console.WriteLine($"{tests.Length} tests passed.");

            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);

            return 1;
        }
    }

    private static void LoopGate()
    {
        for (var loop = 1; loop < 7; loop++) Require(Targeting.Select(loop, 7, 100, 0, 0, "Random (50/50)") == ShotTarget.Normal, "Early loop changed.");

        Require(Targeting.Select(7, 7, 100, 0.99, 0, "Random (50/50)")     == ShotTarget.Player, "Loop 7 excluded.");
        Require(Targeting.Select(700, 7, 100, 0.99, 0.5, "Random (50/50)") == ShotTarget.Ship, "Later loop excluded.");
    }

    private static void Probabilities()
    {
        foreach (var chance in new[] { 100, 10 })
        {
            var random = new Random(7942);
            var counts = new int[3];
            for (var i = 0; i < 100000; i++)
            {
                var target = Targeting.Select(7, 7, chance, random.NextDouble(), random.NextDouble(), "Random (50/50)");
                counts[(int)target]++;
            }

            var expected = 100000 * chance / 200.0;
            Near(counts[(int)ShotTarget.Player], expected, 600);
            Near(counts[(int)ShotTarget.Ship], expected, 600);
            Near(counts[0], 100000 * (1 - chance / 100.0), 600);
            Console.WriteLine($"  {chance}%: normal={counts[0]}, player={counts[1]}, ship={counts[2]}");
        }

        Require(Targeting.Select(7, 7, 10, 0.1, 0, "Random (50/50)") == ShotTarget.Normal, "Chance boundary wrong.");
    }

    private static void ForcedTargets()
    {
        Require(Targeting.Select(7, 7, 100, 0, 0.99, "Player")     == ShotTarget.Player, "Forced player failed.");
        Require(Targeting.Select(7, 7, 100, 0, 0, "Ship")          == ShotTarget.Ship, "Forced ship failed.");
        Require(Targeting.Select(7, 7, 0, 0, 0, "Player")          == ShotTarget.Normal, "Zero chance fired.");
        Require(Targeting.Select(7, 7, double.NaN, 0, 0, "Player") == ShotTarget.Normal, "NaN chance fired.");
    }

    private static void Stationary()
    {
        var target   = new Vector(0, 0, 5050);
        var origin   = new Vector(0, 0, 50);
        var path     = Trajectory.Linear(target, default, Step, Samples);
        var solution = Solve(path, origin, default);
        Near(solution.Time, 10, 0.000001);
        Near((origin + solution.Direction * (500 * solution.Time) - target).Length, 0, 0.000001);
    }

    private static void Moving()
    {
        var start     = new Vector(5000, 200, 1200);
        var velocity  = new Vector(-12, 7, 83);
        var inherited = new Vector(90, -3, 25);
        var origin    = new Vector(18, 0, 46);
        var path      = Trajectory.Linear(start, velocity, Step, Samples);
        var solution  = Solve(path, origin, inherited);
        var probe     = origin + (inherited + solution.Direction * 500) * solution.Time;
        var target    = start  + velocity                               * solution.Time;
        Near((probe - target).Length, 0, 0.000001);
    }

    private static void FrameInvariance()
    {
        var start       = new Vector(3500, 20, 6000);
        var velocity    = new Vector(20, -4, 30);
        var inherited   = new Vector(10, 0, 90);
        var origin      = new Vector(0, 0, 50);
        var translation = new Vector(1000000, -500000, 250000);
        var drift       = new Vector(500, -200, 700);
        var first       = Solve(Trajectory.Linear(start, velocity, Step, Samples), origin, inherited);
        var second      = Solve(Trajectory.Linear(start + translation, velocity + drift, Step, Samples), origin + translation, inherited + drift);
        Near(first.Time, second.Time, 0.000001);
        Near((first.Direction - second.Direction).Length, 0, 0.000001);
    }

    private static void Unreachable()
    {
        var path = Trajectory.Linear(new Vector(1000, 0, 0), new Vector(501, 0, 0), Step, Samples);
        Require(!Targeting.TryIntercept(path, Step, default, default, 500, out _), "Outrunning target intercepted.");
        Require(!Targeting.TryIntercept(path, 0, default, default, 500, out _), "Zero step accepted.");
        Require(!Targeting.TryIntercept(path, Step, default, default, 0, out _), "Zero speed accepted.");
        Require(!Targeting.TryIntercept(new[] { default(Vector), new Vector(double.NaN, 0, 0) }, Step, default, default, 500, out _), "NaN accepted.");
        Require(!Targeting.TryIntercept(path, double.NaN, default, default, 500, out _), "NaN step accepted.");
        Require(!Targeting.TryIntercept(path, Step, default, default, double.PositiveInfinity, out _), "Infinite speed accepted.");
        Require(!Targeting.TryIntercept(new[] { new Vector(double.NaN, 0, 0), default(Vector) }, Step, default, default, 500, out _), "Invalid first position accepted.");
    }

    private static void SurfaceRotation()
    {
        var path = Trajectory.Surface(default, default, new Vector(10000, 0, 0), default,
            new Vector(250, 0, 0), new Vector(0, 0.1, 0), default, _ => 0, Step, 501);
        var expected = new Vector(250 * Math.Cos(1), 0, -250 * Math.Sin(1));
        Near((path[500] - expected).Length, 0, 0.000001);
    }

    private static void OrbitConvergence()
    {
        const double radius = 8000;
        const double speed  = 100;
        const double mu     = radius * speed * speed;
        var coarse = Trajectory.Surface(new Vector(radius, 0, 0), new Vector(0, 0, speed), default, default,
            default, default, default, r => mu / (r * r), Step, 1001);
        var fine = Trajectory.Surface(new Vector(radius, 0, 0), new Vector(0, 0, speed), default, default,
            default, default, default, r => mu / (r * r), Step / 2, 2001);
        var expected    = new Vector(radius * Math.Cos(0.25), 0, radius * Math.Sin(0.25));
        var coarseError = (coarse[1000] - expected).Length;
        var fineError   = (fine[2000]   - expected).Length;
        Require(coarseError < 0.3 && fineError < coarseError * 0.6, "Orbital integration did not converge.");
    }

    private static void SurfaceIntercept()
    {
        var path = Trajectory.Surface(new Vector(6000, 0, 2500), new Vector(20, 0, 95),
            new Vector(-2000, 0, 2500), default, new Vector(0, 50, 240), new Vector(0, 0.04, 0),
            default, r => 80000000 / (r * r), Step, Samples);
        var inherited = new Vector(-80, 0, 10);
        var origin    = new Vector(0, 0, 49.59);
        var solution  = Solve(path, origin, inherited);
        var index     = (int)(solution.Time / Step);
        var fraction  = solution.Time / Step - index;
        var target    = path[index]          + (path[index + 1] - path[index])              * fraction;
        var probe     = origin               + (inherited       + solution.Direction * 500) * solution.Time;
        Near((target - probe).Length, 0, 0.00001);
        Require((solution.Direction - path[0] / path[0].Length).Length > 0.01, "No meaningful lead applied.");
    }

    private static void Evasion()
    {
        var start    = new Vector(0, 0, 5000);
        var solution = Solve(Trajectory.Linear(start, default, Step, Samples), default, default);
        var probe    = solution.Direction * (500 * solution.Time);
        var moved    = start + new Vector(10, 0, 0) * solution.Time;
        Require((probe - moved).Length > 99, "Post-launch movement failed to evade fixed aim.");
    }

    private static void CheckBindings(string[] args)
    {
        Require(args.Length == 2, "Pass the installed game assembly and built mod assembly paths.");
        using var game       = AssemblyDefinition.ReadAssembly(args[0]);
        using var mod        = AssemblyDefinition.ReadAssembly(args[1]);
        var       cannon     = game.MainModule.GetType("OrbitalProbeLaunchController");
        var       patches    = mod.MainModule.GetType("ProbeRoulette.CannonPatches");
        var       patchCount = 0;
        foreach (var patch in patches.Methods)
        {
            var attribute = patch.CustomAttributes.FirstOrDefault(a => a.AttributeType.Name == "HarmonyPatch");
            if (attribute == null)
            {
                continue;
            }

            patchCount++;
            var methodName = (string)attribute.ConstructorArguments[0].Value;
            var original   = cannon.Methods.Single(m => m.Name == methodName);
            foreach (var parameter in patch.Parameters)
            {
                if (parameter.Name.StartsWith("___", StringComparison.Ordinal))
                {
                    var field = cannon.Fields.Single(f => f.Name == parameter.Name.Substring(3));
                    Require(field.FieldType.FullName == parameter.ParameterType.FullName, "Field injection type mismatch.");
                }
                else if (parameter.Name == "__instance")
                {
                    Require(parameter.ParameterType.FullName == cannon.FullName, "Instance injection type mismatch.");
                }
                else
                {
                    Require(original.Parameters.Any(p => p.Name == parameter.Name && p.ParameterType.FullName == parameter.ParameterType.FullName), "Argument injection mismatch.");
                }
            }
        }

        Require(patchCount == 3, "Expected three cannon patches.");

        var controller = game.MainModule.GetType("PlayerCharacterController");
        Require(controller.Fields.Single(f => f.Name == "_bodyCollider").FieldType.FullName == "UnityEngine.Collider", "Player collider binding changed.");

        var manager = game.MainModule.GetType("DeathManager");
        Require(manager.Methods.Any(m => m.IsPublic && m.Name == "KillPlayer"          && m.Parameters.Count == 1 && m.Parameters[0].ParameterType.Name     == "DeathType"), "Death API missing.");
        Require(manager.Methods.Any(m => m.IsPublic && m.Name == "SetImpactDeathSpeed" && m.Parameters.Count == 1 && m.Parameters[0].ParameterType.FullName == "System.Single"), "Impact speed API missing.");

        var deathType = game.MainModule.GetType("DeathType");
        foreach (var choice in new[] { "Impact", "Crushed", "Asphyxiation", "Energy", "Lava", "Digestion" })
        {
            Require(deathType.Fields.Any(f => f.Name == choice && f.HasConstant), "Unsupported death type: " + choice);
        }
    }

    private static void ReleaseMetadata(string[] args)
    {
        Require(args.Length == 2, "Pass the game and built mod assembly paths.");
        var       directory = Path.GetDirectoryName(Path.GetFullPath(args[1]))!;
        using var manifest  = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "manifest.json")));
        using var config    = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "default-config.json")));
        using var assembly  = AssemblyDefinition.ReadAssembly(args[1]);
        var       metadata  = manifest.RootElement;
        var       settings  = config.RootElement.GetProperty("settings");
        var       version   = assembly.CustomAttributes.Single(a => a.AttributeType.Name == "AssemblyInformationalVersionAttribute");
        Require(metadata.GetProperty("version").GetString() == (string)version.ConstructorArguments[0].Value, "Manifest and assembly versions differ.");
        Require(Regex.IsMatch(metadata.GetProperty("version").GetString()!, @"^\d+\.\d+\.\d+$"), "OWML's manifest schema requires a three-part numeric version.");
        Require(metadata.GetProperty("filename").GetString()                                     == "ProbeRoulette.dll", "Manifest points at the wrong DLL.");
        Require(metadata.GetProperty("uniqueName").GetString()                                   == "Depthbomb.ProbeRoulette", "Mod identity changed.");
        Require(settings.GetProperty("First targeted loop").GetInt32()                           == 7, "Beta loop default changed.");
        Require(settings.GetProperty("Targeted shot chance (%)").GetInt32() == 10, "Beta shipped with development targeting chance.");
        Require(settings.GetProperty("Target selection").GetProperty("value").GetString()        == "Random (50/50)", "Beta shipped with forced targeting.");
        Require(!settings.GetProperty("Log closest approach").GetBoolean(), "Beta diagnostics should be opt-in.");
        Require(settings.GetProperty("Probe hits kill player").GetBoolean(), "Player hits should be enabled.");
    }

    private static void SweptPlayerHit()
    {
        Require(HitGeometry.SweptSphereHitsCapsule(new Vector(-10, 0, 0), new Vector(10, 0, 0),
            new Vector(0, -0.5, 0), new Vector(0, 0.5, 0), 1.5), "Fast probe tunneled through capsule.");
    }

    private static void SweptPlayerMiss()
    {
        Require(!HitGeometry.SweptSphereHitsCapsule(new Vector(-10, 0, 1.51), new Vector(10, 0, 1.51),
            new Vector(0, -0.5, 0), new Vector(0, 0.5, 0), 1.5), "Near miss incorrectly killed player.");
    }

    private static void CapsuleEnds()
    {
        Require(HitGeometry.SweptSphereHitsCapsule(new Vector(-10, 2, 0), new Vector(10, 2, 0),
            new Vector(0, -0.5, 0), new Vector(0, 0.5, 0), 1.5), "Tangent hit on capsule end missed.");
        Require(!HitGeometry.SweptSphereHitsCapsule(new Vector(-10, 2.01, 0), new Vector(10, 2.01, 0),
            new Vector(0, -0.5, 0), new Vector(0, 0.5, 0), 1.5), "Capsule end near miss killed player.");
    }

    private static void DegenerateHits()
    {
        Require(HitGeometry.SweptSphereHitsCapsule(new Vector(1, -10, 0), new Vector(1, 10, 0),
            new Vector(0, -0.5, 0), new Vector(0, 0.5, 0), 1.5), "Parallel hit missed.");
        Require(HitGeometry.SweptSphereHitsCapsule(default, default, default, default, 1), "Stationary sphere overlap missed.");
        Require(!HitGeometry.SweptSphereHitsCapsule(new Vector(2, 0, 0), new Vector(2, 0, 0), default, default, 1), "Separated stationary spheres hit.");
    }

    private static void RelativeHits()
    {
        var shift       = new Vector(100000, -30000, 8000);
        var probeStart  = new Vector(-10, 0, 0) + shift;
        var probeEnd    = new Vector(10, 0, 0)  + shift;
        var playerStart = new Vector(-2, 0, 0)  + shift;
        var playerEnd   = new Vector(2, 0, 0)   + shift;
        Require(HitGeometry.SweptSphereHitsCapsule(probeStart - playerStart, probeEnd - playerEnd,
            new Vector(0, -0.5, 0), new Vector(0, 0.5, 0), 1.5), "Moving player sweep failed.");
    }

    private static void InvalidHits()
    {
        Require(!HitGeometry.SweptSphereHitsCapsule(default, default, default, default, double.NaN), "NaN radius killed player.");
        Require(!HitGeometry.SweptSphereHitsCapsule(default, default, default, default, -1), "Negative radius killed player.");
        Require(!HitGeometry.SweptSphereHitsCapsule(new Vector(double.NaN, 0, 0), default, default, default, 1), "NaN path killed player.");
    }

    private static Intercept Solve(Vector[] path, Vector origin, Vector inherited)
    {
        Require(Targeting.TryIntercept(path, Step, origin, inherited, 500, out var solution), "No interception found.");
        Near(solution.Direction.Length, 1, 0.0000001);

        return solution;
    }

    private static void Near(double actual, double expected, double tolerance)
    {
        Require(double.IsFinite(actual) && Math.Abs(actual - expected) <= tolerance, $"Expected {expected} +/- {tolerance}; got {actual}.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
