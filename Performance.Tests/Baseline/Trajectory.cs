using System;

namespace ProbeRoulette.Baseline;

internal static class Trajectory
{
    public static Vector[] Linear(Vector position, Vector velocity, double step, int count)
    {
        var samples                                = new Vector[count];
        for (var i = 0; i < count; i++) samples[i] = position + velocity * (i * step);

        return samples;
    }

    public static Vector[] Surface(Vector               planetPosition,
                                   Vector               planetVelocity,
                                   Vector               sunPosition,
                                   Vector               sunVelocity,
                                   Vector               surfaceOffset,
                                   Vector               angularVelocity,
                                   Vector               surfaceVelocity,
                                   Func<double, double> gravityMagnitude,
                                   double               step,
                                   int                  count)
    {
        var samples = new Vector[count];
        var spin    = angularVelocity * step;
        for (var i = 0; i < count; i++)
        {
            samples[i] = planetPosition + surfaceOffset + surfaceVelocity * (i * step);

            var toSun    = sunPosition - planetPosition;
            var distance = toSun.Length;
            if (distance > 0.001)
            {
                planetVelocity += toSun * (gravityMagnitude(distance) / distance * step);
            }

            // Match KinematicRigidbody: update velocity before position, then Euler rotation.
            planetPosition  += planetVelocity * step;
            sunPosition     += sunVelocity    * step;
            surfaceOffset   =  Vector.RotateEuler(surfaceOffset, spin);
            surfaceVelocity =  Vector.RotateEuler(surfaceVelocity, spin);
        }

        return samples;
    }
}
