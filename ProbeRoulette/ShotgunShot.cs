using OWML.Common;
using System;
using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;
using Random = System.Random;

namespace ProbeRoulette;

internal static class ShotgunShot
{
    private const float LaunchSpeed     = 500f;
    private const float LifetimeSeconds = 20f;

    public static void Fire(OWRigidbody mainProbe, Vector3 forward, ProbeRouletteMod mod)
    {
        // Called before the vanilla launch impulse, while the source has only inherited velocity.
        var velocity        = mainProbe.GetVelocity();
        var angularVelocity = mainProbe.GetAngularVelocity();
        var orientation     = Quaternion.LookRotation(forward);
        var colliders       = new List<Collider>(mainProbe.GetComponentsInChildren<Collider>(true));
        var minimumCosine   = Math.Cos(mod.ShotgunSpread * Mathf.Deg2Rad);
        var random          = new Random(Guid.NewGuid().GetHashCode());
        for (var index = 0; index < mod.ShotgunProbeCount - 1; index++)
        {
            GameObject clone = null;
            try
            {
                // Sample the whole cone evenly without changing the game's random sequence.
                var phase     = random.NextDouble() * 2 * Math.PI;
                var cosine    = 1 - random.NextDouble() * (1 - minimumCosine);
                var sine      = Math.Sqrt(1                  - cosine * cosine);
                var direction = orientation * new Vector3((float)(Math.Cos(phase) * sine), (float)(Math.Sin(phase) * sine), (float)cosine);
                var rotation  = Quaternion.FromToRotation(forward, direction);
                clone = Object.Instantiate(mainProbe.gameObject, mainProbe.GetPosition(), rotation * mainProbe.transform.rotation);
                // Schedule cleanup immediately, even if a later initialization step fails.
                Object.Destroy(clone, LifetimeSeconds);
                clone.name = "ShotgunProbe_" + index;
                var body = clone.GetComponent<OWRigidbody>();
                body.SetVelocity(velocity + direction * LaunchSpeed);
                body.SetAngularVelocity(rotation      * angularVelocity + body.transform.right * 0.1f);

                var extraColliders = clone.GetComponentsInChildren<Collider>(true);
                foreach (var extra in extraColliders)
                {
                    foreach (var existing in colliders)
                    {
                        Physics.IgnoreCollision(extra, existing);
                    }
                }

                colliders.AddRange(extraColliders);
                clone.AddComponent<ProbePlayerHitDetector>().Initialize(body, mod);
            }
            catch (Exception exception)
            {
                if (clone != null)
                {
                    Object.Destroy(clone);
                }

                mod.Log("Could not launch an extra shotgun probe. " + exception, MessageType.Error);
            }
        }
    }
}
