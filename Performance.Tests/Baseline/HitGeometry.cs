using System;

namespace ProbeRoulette.Baseline;

internal static class HitGeometry
{
    public static bool SweptSphereHitsCapsule(Vector start, Vector end, Vector capsuleStart, Vector capsuleEnd, double combinedRadius)
    {
        if (!start.IsFinite              || !end.IsFinite                     || !capsuleStart.IsFinite || !capsuleEnd.IsFinite ||
            double.IsNaN(combinedRadius) || double.IsInfinity(combinedRadius) || combinedRadius < 0)
        {
            return false;
        }

        var first         = end        - start;
        var second        = capsuleEnd - capsuleStart;
        var offset        = start      - capsuleStart;
        var a             = Vector.Dot(first, first);
        var b             = Vector.Dot(first, second);
        var c             = Vector.Dot(first, offset);
        var e             = Vector.Dot(second, second);
        var f             = Vector.Dot(second, offset);
        var radiusSquared = combinedRadius * combinedRadius;
        var denominator   = a * e - b * b;
        if (denominator > 1e-12 * a * e)
        {
            var s = (b * f - c * e) / denominator;
            var t = (a * f - b * c) / denominator;
            if (s >= 0 && s <= 1 && t >= 0 && t <= 1)
            {
                var gap = offset + first * s - second * t;
                if (Vector.Dot(gap, gap) <= radiusSquared)
                {
                    return true;
                }
            }
        }

        return PointSegmentDistanceSquared(start, capsuleStart, capsuleEnd) <= radiusSquared ||
               PointSegmentDistanceSquared(end, capsuleStart, capsuleEnd)   <= radiusSquared ||
               PointSegmentDistanceSquared(capsuleStart, start, end)        <= radiusSquared ||
               PointSegmentDistanceSquared(capsuleEnd, start, end)          <= radiusSquared;
    }

    private static double PointSegmentDistanceSquared(Vector point, Vector start, Vector end)
    {
        var direction = end - start;
        var length    = Vector.Dot(direction, direction);
        var fraction  = length > 1e-12 ? Math.Max(0, Math.Min(1, Vector.Dot(point - start, direction) / length)) : 0;
        var gap       = point - start - direction * fraction;

        return Vector.Dot(gap, gap);
    }
}
