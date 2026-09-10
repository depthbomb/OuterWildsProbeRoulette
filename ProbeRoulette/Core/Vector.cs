using System;

namespace ProbeRoulette.Core;

internal readonly struct Vector
{
    public readonly double X;
    public readonly double Y;
    public readonly double Z;

    public double Length   => Math.Sqrt(Dot(this, this));
    public bool   IsFinite => !double.IsNaN(X + Y + Z) && !double.IsInfinity(X + Y + Z);

    public Vector(double x, double y, double z)
    {
        X = x;
        Y = y;
        Z = z;
    }

    public static Vector operator +(Vector a, Vector b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);

    public static Vector operator -(Vector a, Vector b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);

    public static Vector operator *(Vector a, double scale) => new(a.X * scale, a.Y * scale, a.Z * scale);

    public static Vector operator /(Vector a, double scale) => a * (1.0 / scale);

    public static double Dot(Vector a, Vector b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;

    // Unity's Quaternion.Euler applies Z, then X, then Y.
    public static Vector RotateEuler(Vector value, Vector radians) => new EulerRotation(radians).Apply(value);
}
