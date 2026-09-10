using System;

namespace ProbeRoulette.Core;

internal enum ShotTarget
{
    Normal,
    Player,
    Ship
}

internal readonly struct Intercept
{
    public readonly Vector Direction;
    public readonly double Time;

    public Intercept(Vector direction, double time)
    {
        Direction = direction;
        Time      = time;
    }
}

internal static class Targeting
{
    public static ShotTarget Select(int loop, int firstLoop, double chancePercent, double roll, double targetRoll, string selection)
    {
        if (loop < firstLoop || double.IsNaN(chancePercent) || roll >= Math.Max(0, Math.Min(100, chancePercent)) / 100.0)
        {
            return ShotTarget.Normal;
        }

        if (selection == "Player")
        {
            return ShotTarget.Player;
        }

        if (selection == "Ship")
        {
            return ShotTarget.Ship;
        }

        return targetRoll < 0.5 ? ShotTarget.Player : ShotTarget.Ship;
    }

    // Everything here uses the cannon's launch position as zero, with no rotating reference frame.
    public static bool TryIntercept(Vector[] positions, double step, Vector origin, Vector inheritedVelocity, double boost, out Intercept intercept)
    {
        intercept = default;
        if (positions == null          || positions.Length < 2            || step <= 0              || boost <= 0 ||
            double.IsNaN(step + boost) || double.IsInfinity(step + boost) || !positions[0].IsFinite ||
            !origin.IsFinite           || !inheritedVelocity.IsFinite)
        {
            return false;
        }

        for (var i = 1; i < positions.Length; i++)
        {
            var time         = i * step;
            var displacement = positions[i] - origin - inheritedVelocity * time;
            if (!displacement.IsFinite)
            {
                return false;
            }

            var reach = boost * time;
            // Squared distances tell us the same thing without a square root on every sample.
            if (Vector.Dot(displacement, displacement) > reach * reach)
            {
                continue;
            }

            var lower = (i - 1) * step;
            var upper = time;
            // We just stepped past an intercept. Narrow it down within that physics frame.
            for (var iteration = 0; iteration < 40; iteration++)
            {
                var middle      = (lower  + upper)          * 0.5;
                var fraction    = (middle - (i - 1) * step) / step;
                var point       = positions[i - 1] + (positions[i] - positions[i - 1]) * fraction;
                var required    = point            - origin - inheritedVelocity        * middle;
                var middleReach = boost * middle;
                if (Vector.Dot(required, required) > middleReach * middleReach)
                {
                    lower = middle;
                }
                else
                {
                    upper = middle;
                }
            }

            var arrival  = (lower   + upper)          * 0.5;
            var weight   = (arrival - (i - 1) * step) / step;
            var target   = positions[i - 1] + (positions[i] - positions[i - 1]) * weight;
            var impulse  = target           - origin - inheritedVelocity        * arrival;
            var distance = impulse.Length;
            if (arrival < 0.0001 || distance < 0.0001)
            {
                return false;
            }

            intercept = new Intercept(impulse / distance, arrival);

            return true;
        }

        return false;
    }
}
