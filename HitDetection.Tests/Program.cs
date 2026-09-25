using ProbeRoulette;
using System;
using System.Reflection;
using UnityEngine;
using Object = UnityEngine.Object;

internal static class Program
{
    private sealed class Scene
    {
        public readonly ProbeRouletteMod Mod    = new();
        public readonly DeathManager     Death  = new();
        public readonly OWRigidbody      Player = new GameObject().AddComponent<OWRigidbody>();
        public readonly OWRigidbody      Probe  = new GameObject().AddComponent<OWRigidbody>();
        public readonly CapsuleCollider  Capsule;

        public Scene(Vector3 probePosition)
        {
            Capsule = Player.gameObject.AddComponent<CapsuleCollider>();
            Probe.transform.position = probePosition;
            Probe.SetVelocity(new Vector3(0, 0, 500));
            Locator.Player     = Player;
            Locator.Controller = new PlayerCharacterController
            {
                _bodyCollider = Capsule
            };
            Locator.DeathManager = Death;
            Object.Clones.Clear();
        }

        public ProbePlayerHitDetector ArmCenter()
        {
            var detector = Probe.gameObject.AddComponent<ProbePlayerHitDetector>();
            detector.Initialize(Probe, Mod);
            return detector;
        }

        public void FireShotgun()
        {
            Probe.SetVelocity(default);
            ShotgunShot.Fire(Probe, Vector3.forward, Mod);
            Probe.SetVelocity(new Vector3(0, 0, 500));
            Require(Mod.Errors.Count == 0, string.Join("\n", Mod.Errors));
            Require(Object.Clones.Count == Mod.ShotgunProbeCount - 1, "Wrong extra probe count.");
            foreach (var clone in Object.Clones)
            {
                Require(clone.activeInHierarchy, "An extra probe failed initialization.");
                Require(clone.GetComponentsInChildren<ProbePlayerHitDetector>(true).Length == 1, "Extra probe must have one detector.");
                Require(clone.GetComponent<ProbePlayerHitDetector>().enabled, "Extra probe detector is disabled.");
            }
        }
    }

    private static readonly MethodInfo FixedUpdate = typeof(ProbePlayerHitDetector).GetMethod("FixedUpdate", BindingFlags.NonPublic | BindingFlags.Instance);

    private static int Main()
    {
        var tests = new (string Name, Action Run)[]
        {
            ("Turning cannot make a 40 m near miss lethal", TurningMiss),
            ("Turning still detects a fast crossing at a small hit radius", TurningHit),
            ("Player movement and a world-origin shift preserve the sweep", MovementAndOriginShift),
            ("Player and probe teleports do not create collision paths", Teleports),
            ("Disabling and re-enabling hit detection resets the sweep", DisabledHits),
            ("Every shotgun probe can kill independently with every death type", ShotgunHits),
            ("Shotgun probes reject turning near misses", ShotgunMisses),
            ("Shotgun probes use the configured hit radius", ShotgunRadius),
            ("Shotgun probes respect disabled player kills and targeting", ShotgunDisabled),
            ("A one-probe shotgun still leaves center hit detection working", SingleProbe)
        };

        var failures = 0;
        foreach (var test in tests)
        {
            try
            {
                test.Run();
                Console.WriteLine("PASS " + test.Name);
            }
            catch (Exception exception)
            {
                failures++;
                Console.Error.WriteLine($"FAIL {test.Name}: {exception}");
            }
        }

        Console.WriteLine($"{tests.Length - failures}/{tests.Length} hit detection tests passed (engine doubles; no live Unity physics).");
        return failures == 0 ? 0 : 1;
    }

    private static void TurningMiss()
    {
        foreach (var yaw in new[] { 0f, 30f, 45f, 60f, 90f, 180f })
        {
            var scene    = new Scene(new Vector3(40, 0, -5));
            // Keep the radius from the original false-hit reproduction.
            scene.Mod.ProbeHitRadius = 35.5f;
            var detector = scene.ArmCenter();
            scene.Player.transform.rotation = Quaternion.AngleAxis(yaw, Vector3.up);
            scene.Probe.transform.position = new Vector3(40, 0, 5);
            Tick(detector);
            Require(scene.Death.KillCount == 0, $"A {yaw} degree turn made a 40 m miss lethal.");
        }
    }

    private static void TurningHit()
    {
        foreach (var yaw in new[] { 0f, 45f, 90f, 180f })
        {
            var scene = new Scene(new Vector3(-5, 0, 0));
            scene.Mod.ProbeHitRadius = 0.1f;
            var detector = scene.ArmCenter();
            scene.Player.transform.rotation = Quaternion.AngleAxis(yaw, Vector3.up);
            scene.Probe.transform.position = new Vector3(5, 0, 0);
            Tick(detector);
            Require(scene.Death.KillCount == 1, $"A {yaw} degree turn hid a crossing probe.");
        }
    }

    private static void MovementAndOriginShift()
    {
        foreach (var miss in new[] { false, true })
        {
            var x        = miss ? 40 : 0;
            var scene    = new Scene(new Vector3(x, 0, -100));
            var detector = scene.ArmCenter();
            var shift    = new Vector3(100000, -30000, 8000);
            scene.Player.transform.position = shift + new Vector3(2, 0, 0);
            scene.Player.transform.rotation = Quaternion.AngleAxis(90, Vector3.up);
            scene.Probe.transform.position = shift + new Vector3(x, 0, 100);
            Tick(detector);
            Require(scene.Death.KillCount == (miss ? 0 : 1), "Movement or origin shift changed the collision result.");
        }
    }

    private static void Teleports()
    {
        foreach (var warpPlayer in new[] { false, true })
        {
            var scene    = new Scene(new Vector3(0, 0, -100));
            var detector = scene.ArmCenter();
            if (warpPlayer)
            {
                scene.Player.Warp(new Vector3(0, 0, -200));
            }
            else
            {
                scene.Probe.Warp(new Vector3(0, 0, 100));
            }

            Tick(detector);
            Require(scene.Death.KillCount == 0, "Teleport was treated as a collision path.");
            scene.Probe.transform.position = scene.Player.transform.position + new Vector3(0, 0, -100);
            Tick(detector);
            Require(scene.Death.KillCount == 1, "Hit detection did not resume after teleporting.");
        }
    }

    private static void DisabledHits()
    {
        var scene    = new Scene(new Vector3(0, 0, -100));
        var detector = scene.ArmCenter();
        scene.Mod.PlayerHitsKill = false;
        scene.Probe.transform.position = new Vector3(0, 0, 100);
        Tick(detector);
        scene.Mod.PlayerHitsKill = true;
        Tick(detector);
        Require(scene.Death.KillCount == 0, "Re-enabling hits swept through the disabled interval.");
        scene.Probe.transform.position = new Vector3(0, 0, -100);
        Tick(detector);
        Require(scene.Death.KillCount == 1, "Hit detection did not resume.");
    }

    private static void ShotgunHits()
    {
        foreach (var type in Enum.GetValues<DeathType>())
        {
            for (var probeIndex = 0; probeIndex < 9; probeIndex++)
            {
                var scene = new Scene(new Vector3(0, 0, -100));
                scene.Mod.PlayerHitDeathType = type;
                scene.FireShotgun();
                var center   = scene.ArmCenter();
                var body     = probeIndex == 0 ? scene.Probe : Object.Clones[probeIndex - 1].GetComponent<OWRigidbody>();
                var detector = body.GetComponent<ProbePlayerHitDetector>();
                body.transform.position = new Vector3(0, 0, 100);
                Tick(detector);
                Require(scene.Death.KillCount == 1 && scene.Death.LastDeath == type, $"Probe {probeIndex} failed to request {type} death.");
                if (type == DeathType.Impact)
                {
                    Require(MathF.Abs(scene.Death.ImpactSpeed - 500) < 0.01f, "Impact death lost the probe's speed.");
                }

                scene.Probe.transform.position = new Vector3(0, 0, 100);
                Tick(center);
                foreach (var clone in Object.Clones)
                {
                    clone.transform.position = new Vector3(0, 0, 100);
                    Tick(clone.GetComponent<ProbePlayerHitDetector>());
                }

                Require(scene.Death.KillCount == 1, "The spread requested multiple deaths.");
            }
        }
    }

    private static void ShotgunMisses()
    {
        var scene = new Scene(new Vector3(40, 0, -5));
        scene.Mod.ProbeHitRadius = 35.5f;
        scene.FireShotgun();
        scene.Player.transform.rotation = Quaternion.AngleAxis(45, Vector3.up);
        foreach (var clone in Object.Clones)
        {
            clone.transform.position = new Vector3(40, 0, 5);
            Tick(clone.GetComponent<ProbePlayerHitDetector>());
        }

        Require(scene.Death.KillCount == 0, "An extra probe killed on a turning near miss.");
    }

    private static void ShotgunDisabled()
    {
        foreach (var disableTargeting in new[] { false, true })
        {
            var scene = new Scene(new Vector3(0, 0, -100));
            scene.FireShotgun();
            scene.Mod.TargetingEnabled = !disableTargeting;
            scene.Mod.PlayerHitsKill   = disableTargeting;
            foreach (var clone in Object.Clones)
            {
                clone.transform.position = new Vector3(0, 0, 100);
                Tick(clone.GetComponent<ProbePlayerHitDetector>());
            }

            Require(scene.Death.KillCount == 0, "An extra probe ignored disabled hits.");
        }
    }

    private static void ShotgunRadius()
    {
        foreach (var radius in new[] { 1f, 20f })
        {
            for (var probeIndex = 0; probeIndex < 8; probeIndex++)
            {
                var scene = new Scene(new Vector3(10, 0, -100));
                scene.Mod.ProbeHitRadius = radius;
                scene.FireShotgun();
                var clone = Object.Clones[probeIndex];
                clone.transform.position = new Vector3(10, 0, 100);
                Tick(clone.GetComponent<ProbePlayerHitDetector>());
                Require(scene.Death.KillCount == (radius > 10 ? 1 : 0), $"Extra probe {probeIndex} ignored the {radius} m hit radius.");
            }
        }
    }

    private static void SingleProbe()
    {
        var scene = new Scene(new Vector3(0, 0, -100));
        scene.Mod.ShotgunProbeCount = 1;
        scene.FireShotgun();
        var detector = scene.ArmCenter();
        scene.Probe.transform.position = new Vector3(0, 0, 100);
        Tick(detector);
        Require(scene.Death.KillCount == 1, "Center probe lost hit detection.");
    }

    private static void Tick(ProbePlayerHitDetector detector)
    {
        if (detector.enabled)
        {
            FixedUpdate.Invoke(detector, null);
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
