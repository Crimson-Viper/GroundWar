using System.Numerics;

namespace GroundWar.Sim.Core;

/// <summary>2D geometry helpers. The simulation runs on the ground plane: X east, Y south (Godot Z).</summary>
public static class Geo
{
    public const float Epsilon = 1e-6f;

    public static float Cross(Vector2 a, Vector2 b) => a.X * b.Y - a.Y * b.X;

    public static Vector2 SafeNormalize(Vector2 v, Vector2 fallback)
    {
        float len = v.Length();
        return len > Epsilon ? v / len : fallback;
    }

    public static Vector2 Perp(Vector2 v) => new(-v.Y, v.X);

    public static Vector2 Rotate(Vector2 v, float radians)
    {
        float c = MathF.Cos(radians), s = MathF.Sin(radians);
        return new Vector2(v.X * c - v.Y * s, v.X * s + v.Y * c);
    }

    /// <summary>Squared distance from point p to segment ab; t is the position along ab (0..1).</summary>
    public static float PointSegmentDistSq(Vector2 p, Vector2 a, Vector2 b, out float t)
    {
        Vector2 ab = b - a;
        float len2 = ab.LengthSquared();
        t = len2 > Epsilon ? Math.Clamp(Vector2.Dot(p - a, ab) / len2, 0f, 1f) : 0f;
        return Vector2.DistanceSquared(p, a + ab * t);
    }

    /// <summary>Squared distance between segments p1q1 and p2q2 (Ericson, Real-Time Collision Detection 5.1.9).
    /// s and t are the closest-point parameters along each segment.</summary>
    public static float SegmentSegmentDistSq(Vector2 p1, Vector2 q1, Vector2 p2, Vector2 q2, out float s, out float t)
    {
        Vector2 d1 = q1 - p1, d2 = q2 - p2, r = p1 - p2;
        float a = Vector2.Dot(d1, d1), e = Vector2.Dot(d2, d2), f = Vector2.Dot(d2, r);
        if (a <= Epsilon && e <= Epsilon)
        {
            s = t = 0f;
            return Vector2.DistanceSquared(p1, p2);
        }
        if (a <= Epsilon)
        {
            s = 0f;
            t = Math.Clamp(f / e, 0f, 1f);
        }
        else
        {
            float c = Vector2.Dot(d1, r);
            if (e <= Epsilon)
            {
                t = 0f;
                s = Math.Clamp(-c / a, 0f, 1f);
            }
            else
            {
                float b = Vector2.Dot(d1, d2);
                float denom = a * e - b * b;
                s = denom > Epsilon ? Math.Clamp((b * f - c * e) / denom, 0f, 1f) : 0f;
                t = (b * s + f) / e;
                if (t < 0f) { t = 0f; s = Math.Clamp(-c / a, 0f, 1f); }
                else if (t > 1f) { t = 1f; s = Math.Clamp((b - c) / a, 0f, 1f); }
            }
        }
        return Vector2.DistanceSquared(p1 + d1 * s, p2 + d2 * t);
    }
}
