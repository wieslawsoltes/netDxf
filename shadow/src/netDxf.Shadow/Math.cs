namespace netDxf.Shadow;

/// <summary>Double-precision interchange math, independent of a geometry or rendering engine.</summary>
public readonly record struct Vec3(double X, double Y, double Z = 0)
{
    public static Vec3 Zero => default;
    public static Vec3 UnitX => new(1, 0, 0);
    public static Vec3 UnitY => new(0, 1, 0);
    public static Vec3 UnitZ => new(0, 0, 1);
    public double Length => Math.Sqrt(X * X + Y * Y + Z * Z);
    public bool IsFinite => double.IsFinite(X) && double.IsFinite(Y) && double.IsFinite(Z);
    [System.Text.Json.Serialization.JsonIgnore]
    public Vec3 Normalized => Length > 0 ? this / Length : UnitZ;
    public static Vec3 operator +(Vec3 a, Vec3 b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
    public static Vec3 operator -(Vec3 a, Vec3 b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
    public static Vec3 operator -(Vec3 a) => a * -1;
    public static Vec3 operator *(Vec3 a, double b) => new(a.X * b, a.Y * b, a.Z * b);
    public static Vec3 operator /(Vec3 a, double b) => a * (1 / b);
    public static double Dot(Vec3 a, Vec3 b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;
    public static Vec3 Cross(Vec3 a, Vec3 b) => new(a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);
    public static double Distance(Vec3 a, Vec3 b) => (a - b).Length;
}

/// <summary>Column-vector affine transform; (a * b)(p) applies b before a.</summary>
public readonly record struct Affine3(Vec3 X, Vec3 Y, Vec3 Z, Vec3 Translation)
{
    public bool IsFinite => X.IsFinite && Y.IsFinite && Z.IsFinite && Translation.IsFinite;
    public static Affine3 Identity => new(Vec3.UnitX, Vec3.UnitY, Vec3.UnitZ, Vec3.Zero);
    public Vec3 Point(Vec3 p) => Direction(p) + Translation;
    public Vec3 Direction(Vec3 p) => X * p.X + Y * p.Y + Z * p.Z;
    public static Affine3 operator *(Affine3 a, Affine3 b) => new(a.Direction(b.X), a.Direction(b.Y), a.Direction(b.Z), a.Point(b.Translation));
    public static Affine3 Move(Vec3 p) => Identity with { Translation = p };
    public static Affine3 Scale(Vec3 p) => new(Vec3.UnitX * p.X, Vec3.UnitY * p.Y, Vec3.UnitZ * p.Z, Vec3.Zero);
    public static Affine3 RotateZ(double radians) => new(new(Math.Cos(radians), Math.Sin(radians)), new(-Math.Sin(radians), Math.Cos(radians)), Vec3.UnitZ, Vec3.Zero);
    /// <summary>Scaled inversion avoids cubic overflow; numerically singular transforms fail explicitly.</summary>
    public bool TryInverse(out Affine3 inverse, double relativeTolerance = 1e-12)
    {
        inverse = default;
        if (!IsFinite || !double.IsFinite(relativeTolerance) || relativeTolerance < 0) return false;
        static double Max(Vec3 v) => Math.Max(Math.Abs(v.X), Math.Max(Math.Abs(v.Y), Math.Abs(v.Z)));
        var scale = Math.Max(Max(X), Math.Max(Max(Y), Max(Z)));
        if (!(scale > 0)) return false;
        var x = X / scale; var y = Y / scale; var z = Z / scale;
        var a = Vec3.Cross(y, z); var b = Vec3.Cross(z, x); var c = Vec3.Cross(x, y);
        var determinant = Vec3.Dot(x, a);
        if (!double.IsFinite(determinant) || Math.Abs(determinant) <= relativeTolerance * x.Length * y.Length * z.Length) return false;
        a = (a / determinant) / scale; b = (b / determinant) / scale; c = (c / determinant) / scale;
        var linear = new Affine3(new(a.X, b.X, c.X), new(a.Y, b.Y, c.Y), new(a.Z, b.Z, c.Z), Vec3.Zero);
        inverse = linear with { Translation = linear.Direction(-Translation) };
        return inverse.IsFinite;
    }

    public static Affine3 ObjectCoordinates(Vec3 normal)
    {
        var z = normal.Normalized;
        var x = Vec3.Cross(Math.Abs(z.X) < 1.0 / 64 && Math.Abs(z.Y) < 1.0 / 64 ? Vec3.UnitY : Vec3.UnitZ, z).Normalized;
        return new(x, Vec3.Cross(z, x), z, Vec3.Zero);
    }
}

public readonly record struct Bounds2(double MinX, double MinY, double MaxX, double MaxY)
{
    public static Bounds2 Empty => new(double.PositiveInfinity, double.PositiveInfinity, double.NegativeInfinity, double.NegativeInfinity);
    public bool IsEmpty => MinX > MaxX || MinY > MaxY;
    public double Width => IsEmpty ? 0 : MaxX - MinX;
    public double Height => IsEmpty ? 0 : MaxY - MinY;
    public Vec3 Center => new((MinX + MaxX) / 2, (MinY + MaxY) / 2);
    public Bounds2 Include(Vec3 p) => !p.IsFinite ? this : new(Math.Min(MinX, p.X), Math.Min(MinY, p.Y), Math.Max(MaxX, p.X), Math.Max(MaxY, p.Y));
    public Bounds2 Union(Bounds2 b) => b.IsEmpty ? this : IsEmpty ? b : new(Math.Min(MinX, b.MinX), Math.Min(MinY, b.MinY), Math.Max(MaxX, b.MaxX), Math.Max(MaxY, b.MaxY));
    public Bounds2 Inflate(double amount) => IsEmpty ? this : new(MinX - amount, MinY - amount, MaxX + amount, MaxY + amount);
    public bool Intersects(Bounds2 b) => !IsEmpty && !b.IsEmpty && MinX <= b.MaxX && MaxX >= b.MinX && MinY <= b.MaxY && MaxY >= b.MinY;
    public bool Contains(Vec3 p) => p.X >= MinX && p.X <= MaxX && p.Y >= MinY && p.Y <= MaxY;
}
