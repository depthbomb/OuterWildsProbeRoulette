using System;

namespace ProbeRoulette.Core;

internal readonly struct EulerRotation
{
    private readonly double _sinX;
    private readonly double _cosX;
    private readonly double _sinY;
    private readonly double _cosY;
    private readonly double _sinZ;
    private readonly double _cosZ;

    public EulerRotation(Vector radians)
    {
        _sinX = Math.Sin(radians.X);
        _cosX = Math.Cos(radians.X);
        _sinY = Math.Sin(radians.Y);
        _cosY = Math.Cos(radians.Y);
        _sinZ = Math.Sin(radians.Z);
        _cosZ = Math.Cos(radians.Z);
    }

    public Vector Apply(Vector value)
    {
        // Unity applies Euler angles in Z, X, Y order, even though the input is called XYZ.
        var z = new Vector(value.X  * _cosZ - value.Y * _sinZ, value.X * _sinZ + value.Y * _cosZ, value.Z);
        var x = new Vector(z.X, z.Y * _cosX - z.Z     * _sinX, z.Y     * _sinX + z.Z     * _cosX);

        return new Vector(x.X * _cosY + x.Z * _sinY, x.Y, -x.X * _sinY + x.Z * _cosY);
    }
}
