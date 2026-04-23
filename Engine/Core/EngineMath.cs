


using System.Diagnostics;
using System.Numerics;



namespace Engine.Core;




public static class EngineMath
{

    /// <summary>
    /// Area aligned bounding box. Represented as min-max in memory but can be used as center-extent based via properties.
    /// </summary>
    public unsafe struct AABB : IEquatable<AABB>
    {
        public static readonly AABB MaxValue = new AABB(Vector3.Zero, new Vector3(float.MaxValue));




        public Vector3 Min;
        public Vector3 Max;



        [Conditional("DEBUG")]
        public void Validate()
        {
            if (float.IsNaN(Min.X) || float.IsNaN(Min.Y) || float.IsNaN(Min.Z) ||
                float.IsNaN(Max.X) || float.IsNaN(Max.Y) || float.IsNaN(Max.Z))
                throw new Exception($"AABB contains NaN | Min={Min} Max={Max}");

            if (Min.X > Max.X || Min.Y > Max.Y || Min.Z > Max.Z)
                throw new Exception($"AABB Min > Max | Min={Min} Max={Max}");
        }




        private AABB(Vector3 min, Vector3 max)
        {
            Min = min;
            Max = max;
        }



        public static AABB FromMinMax(Vector3 min, Vector3 max)
            => new AABB(min, max);


        public static AABB FromCenterExtent(Vector3 center, Vector3 extents)
            => new AABB(center - extents, center + extents);




        public Vector3 Center
        {
            readonly get => (Min + Max) * 0.5f;
            set
            {
                Vector3 extents = Extents;
                Min = value - extents;
                Max = value + extents;
            }
        }

        public Vector3 Extents
        {
            readonly get => (Max - Min) * 0.5f;
            set
            {
                Vector3 center = Center;
                Min = center - value;
                Max = center + value;
            }
        }



        public override readonly string ToString() => $"AABB(Min: {Min}, Max: {Max})";






        public override readonly int GetHashCode() => HashCode.Combine(Min, Max, Center, Extents);
        public readonly bool Equals(AABB other) => Min.Equals(other.Min) && Max.Equals(other.Max);

        public override readonly bool Equals(object? obj) => obj is AABB other && Equals(other);

        public static bool operator ==(AABB left, AABB right) => left.Equals(right);

        public static bool operator !=(AABB left, AABB right) => !(left == right);



        public static AABB operator *(AABB a, Matrix4x4 m)
        {
            Vector3 min = new(float.MaxValue);
            Vector3 max = new(float.MinValue);

            Span<Vector3> corners =
            [
                new(a.Min.X, a.Min.Y, a.Min.Z),
                new(a.Min.X, a.Min.Y, a.Max.Z),
                new(a.Min.X, a.Max.Y, a.Min.Z),
                new(a.Min.X, a.Max.Y, a.Max.Z),
                new(a.Max.X, a.Min.Y, a.Min.Z),
                new(a.Max.X, a.Min.Y, a.Max.Z),
                new(a.Max.X, a.Max.Y, a.Min.Z),
                new(a.Max.X, a.Max.Y, a.Max.Z),
            ];

            foreach (var c in corners)
            {
                var t = m.Transform(c);
                min = Vector3.Min(min, t);
                max = Vector3.Max(max, t);
            }

            return new AABB(min, max);
        }



        public readonly bool Overlaps(AABB other) =>
            Min.X <= other.Min.X || Max.X >= other.Max.X ||
            Min.Y <= other.Min.Y || Max.Y >= other.Max.Y ||
            Min.Z <= other.Min.Z || Max.Z >= other.Max.Z;


        public readonly bool Encompasses(AABB other) =>
            Min.X <= other.Min.X && Max.X >= other.Max.X &&
            Min.Y <= other.Min.Y && Max.Y >= other.Max.Y &&
            Min.Z <= other.Min.Z && Max.Z >= other.Max.Z;


        public readonly AABB Union(AABB other) =>
            new AABB(Vector3.Min(Min, other.Min), Vector3.Max(Max, other.Max));

        public readonly float GetSurfaceArea()
        {
            Vector3 size = Max - Min; 
            return 2f * (size.X * size.Y + size.X * size.Z + size.Y * size.Z);
        }


    }





    public static Matrix4x4 GetBasis(this in Matrix4x4 m) => m with { M41 = 0, M42 = 0, M43 = 0, M44 = 1, M14 = 0, M24 = 0, M34 = 0 };

    public static Vector3 GetOrientationX(this in Matrix4x4 m)
        => new(m.M11, m.M12, m.M13);

    public static void SetOrientationX(this ref Matrix4x4 m, Vector3 v)
    {
        m.M11 = v.X; m.M12 = v.Y; m.M13 = v.Z;
    }

    public static Vector3 GetOrientationY(this in Matrix4x4 m)
        => new(m.M21, m.M22, m.M23);

    public static void SetOrientationY(this ref Matrix4x4 m, Vector3 v)
    {
        m.M21 = v.X; m.M22 = v.Y; m.M23 = v.Z;
    }

    public static Vector3 GetOrientationZ(this in Matrix4x4 m)
        => new(m.M31, m.M32, m.M33);

    public static void SetOrientationZ(this ref Matrix4x4 m, Vector3 v)
    {
        m.M31 = v.X; m.M32 = v.Y; m.M33 = v.Z;
    }





    public struct PosRotScale
    {
        public Vector3 Origin;
        public Vector3 Scale;
        public Quaternion Rotation;

        public readonly Matrix4x4 Compose() => Matrix4x4.CreateFromQuaternion(Rotation).Scaled(Scale) with { Translation = Origin };
    }



    public static PosRotScale Decompose(this in Matrix4x4 m)
    {
        if (!Matrix4x4.Decompose(m, out var scale, out var rotation, out var origin))
            throw new InvalidOperationException();
        return new PosRotScale { Origin = origin, Rotation = rotation, Scale = scale };
    }




    public static Matrix4x4 Inverse(this in Matrix4x4 m)
    {
        Matrix4x4.Invert(m, out var inv);
        return inv;
    }

    public static Matrix4x4 Rotated(this in Matrix4x4 m, Vector3 axis, float radians)
        => m * Matrix4x4.CreateFromAxisAngle(Vector3.Normalize(axis), radians);


    public static Matrix4x4 FromEuler(Vector3 euler)
    {

        var rotX = Matrix4x4.CreateRotationX(euler.X);
        var rotY = Matrix4x4.CreateRotationY(euler.Y);
        var rotZ = Matrix4x4.CreateRotationZ(euler.Z);
        return rotZ * rotX * rotY;
    }

    public static Vector3 GetEuler(this in Matrix4x4 m)
    {
        float sy = -m.M32;
        bool singular = MathF.Abs(sy) >= 0.999999f;

        float pitch, yaw, roll;
        if (!singular)
        {
            pitch = MathF.Asin(sy);
            yaw = MathF.Atan2(m.M31, m.M33);
            roll = MathF.Atan2(m.M12, m.M22);
        }
        else
        {
            pitch = MathF.Asin(sy);
            yaw = MathF.Atan2(-m.M13, m.M11);
            roll = 0;
        }

        return new Vector3(pitch, yaw, roll);
    }


    public static Matrix4x4 Scaled(this in Matrix4x4 m, Vector3 scale)
        => Matrix4x4.CreateScale(scale) * m;


    public static Matrix4x4 Orthonormalized(this in Matrix4x4 m)
    {
        Vector3 x = new(m.M11, m.M12, m.M13);
        Vector3 y = new(m.M21, m.M22, m.M23);
        Vector3 z = new(m.M31, m.M32, m.M33);

        x = Vector3.Normalize(x);
        y = Vector3.Normalize(y - Vector3.Dot(y, x) * x);
        z = Vector3.Cross(x, y);

        var mat = m;
        mat.M11 = x.X; mat.M12 = x.Y; mat.M13 = x.Z;
        mat.M21 = y.X; mat.M22 = y.Y; mat.M23 = y.Z;
        mat.M31 = z.X; mat.M32 = z.Y; mat.M33 = z.Z;

        return mat;
    }

    public static Matrix4x4 Multiply(this in Matrix4x4 a, Matrix4x4 b) => a * b;
    public static Vector3 Transform(this in Matrix4x4 m, Vector3 v) => Vector3.Transform(v, m);







    public record struct FrustumPlanes(
        Plane Left,
        Plane Right,
        Plane Up,
        Plane Down,
        Plane Back,
        Plane Front)
    {
        public Plane this[int idx]
        {
            readonly get
            {
                return idx switch
                {
                    0 => Left,
                    1 => Right,
                    2 => Up,
                    3 => Down,
                    4 => Back,
                    5 => Front,
                    _ => throw new ArgumentOutOfRangeException(nameof(idx)),
                };
            }
            set
            {
                switch (idx)
                {
                    case 0: Left = value; break;
                    case 1: Right = value; break;
                    case 2: Up = value; break;
                    case 3: Down = value; break;
                    case 4: Back = value; break;
                    case 5: Front = value; break;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(idx));
                }
            }
        }


        public readonly bool ContainsAABB(AABB bounds)
        {
            if (bounds == AABB.MaxValue) return true;


            var maxX = bounds.Center.X + bounds.Extents.X;
            var maxY = bounds.Center.Y + bounds.Extents.Y;
            var maxZ = bounds.Center.Z + bounds.Extents.Z;

            var minX = bounds.Center.X - bounds.Extents.X;
            var minY = bounds.Center.Y - bounds.Extents.Y;
            var minZ = bounds.Center.Z - bounds.Extents.Z;



            for (int i = 0; i < 6; i++)
            {
                var plane = this[i];

                Vector3 positive = new(
                    plane.Normal.X >= 0 ? maxX : minX,
                    plane.Normal.Y >= 0 ? maxY : minY,
                    plane.Normal.Z >= 0 ? maxZ : minZ
                );

                if (Vector3.Dot(plane.Normal, positive) + plane.D < 0)
                    return false;
            }

            return true;
        }
    }





    public static FrustumPlanes ExtractFrustumPlanes(this in Matrix4x4 ViewProjectionMatrix)
    {

        FrustumPlanes ret = default;


        ret[0] = new Plane(
            ViewProjectionMatrix.M14 + ViewProjectionMatrix.M11,
            ViewProjectionMatrix.M24 + ViewProjectionMatrix.M21,
            ViewProjectionMatrix.M34 + ViewProjectionMatrix.M31,
            ViewProjectionMatrix.M44 + ViewProjectionMatrix.M41
        );

        ret[1] = new Plane(
            ViewProjectionMatrix.M14 - ViewProjectionMatrix.M11,
            ViewProjectionMatrix.M24 - ViewProjectionMatrix.M21,
            ViewProjectionMatrix.M34 - ViewProjectionMatrix.M31,
            ViewProjectionMatrix.M44 - ViewProjectionMatrix.M41
        );

        ret[2] = new Plane(
            ViewProjectionMatrix.M14 + ViewProjectionMatrix.M12,
            ViewProjectionMatrix.M24 + ViewProjectionMatrix.M22,
            ViewProjectionMatrix.M34 + ViewProjectionMatrix.M32,
            ViewProjectionMatrix.M44 + ViewProjectionMatrix.M42
        );

        ret[3] = new Plane(
            ViewProjectionMatrix.M14 - ViewProjectionMatrix.M12,
            ViewProjectionMatrix.M24 - ViewProjectionMatrix.M22,
            ViewProjectionMatrix.M34 - ViewProjectionMatrix.M32,
            ViewProjectionMatrix.M44 - ViewProjectionMatrix.M42
        );

        ret[4] = new Plane(
            ViewProjectionMatrix.M14 + ViewProjectionMatrix.M13,
            ViewProjectionMatrix.M24 + ViewProjectionMatrix.M23,
            ViewProjectionMatrix.M34 + ViewProjectionMatrix.M33,
            ViewProjectionMatrix.M44 + ViewProjectionMatrix.M43
        );

        ret[5] = new Plane(
            ViewProjectionMatrix.M14 - ViewProjectionMatrix.M13,
            ViewProjectionMatrix.M24 - ViewProjectionMatrix.M23,
            ViewProjectionMatrix.M34 - ViewProjectionMatrix.M33,
            ViewProjectionMatrix.M44 - ViewProjectionMatrix.M43
        );


        for (int i = 0; i < 6; i++)
        {
            float length = ret[i].Normal.Length();
            ret[i] = new Plane(
                ret[i].Normal / length,
                ret[i].D / length
            );
        }

        return ret;
    }










    /// <summary>
    /// A generic 2D vector. For float values, use <see cref="Vector2"/>.
    /// </summary>
    public unsafe struct Vector2<T> : IEquatable<Vector2<T>> where T : INumber<T>
    {
        public static readonly Vector2<T> Zero = new(T.Zero, T.Zero);
        public static readonly Vector2<T> One = new(T.One, T.One);

        public T X;
        public T Y;

        public Vector2(T x, T y) { X = x; Y = y; }
        public Vector2(T value) { X = Y = value; }

        public T this[int index]
        {
            readonly get => index == 0 ? X : index == 1 ? Y : throw new IndexOutOfRangeException();
            set { if (index == 0) X = value; else if (index == 1) Y = value; else throw new IndexOutOfRangeException(); }
        }


        public static Vector2<T> operator +(Vector2<T> a, Vector2<T> b) => new(a.X + b.X, a.Y + b.Y);
        public static Vector2<T> operator -(Vector2<T> a, Vector2<T> b) => new(a.X - b.X, a.Y - b.Y);
        public static Vector2<T> operator *(Vector2<T> v, T scalar) => new(v.X * scalar, v.Y * scalar);
        public static Vector2<T> operator *(Vector2<T> a, Vector2<T> b) => new(a.X * b.X, a.Y * b.Y);
        public static Vector2<T> operator *(T scalar, Vector2<T> v) => v * scalar;
        public static Vector2<T> operator /(Vector2<T> v, T scalar) => new(v.X / scalar, v.Y / scalar);
        public static Vector2<T> operator /(Vector2<T> a, Vector2<T> b) => new(a.X / b.X, a.Y / b.Y);


        public static bool operator ==(Vector2<T> a, Vector2<T> b) => a.X == b.X && a.Y == b.Y;
        public static bool operator !=(Vector2<T> a, Vector2<T> b) => !(a == b);

        public static explicit operator Vector2<T>(Vector2 val) => new(T.CreateChecked(val.X), T.CreateChecked(val.Y));
        public static explicit operator Vector2(Vector2<T> val) => new(float.CreateChecked(val.X), float.CreateChecked(val.Y));


        public readonly T Dot(Vector2<T> other) => X * other.X + Y * other.Y;


        public override readonly bool Equals(object? obj) => obj is Vector2<T> other && Equals(other);
        public readonly bool Equals(Vector2<T> other) => X == other.X && Y == other.Y;
        public readonly bool Equals(Vector2 other) => float.CreateChecked(X) == other.X && float.CreateChecked(Y) == other.Y;
        public override readonly int GetHashCode() => HashCode.Combine(X, Y);
        public override readonly string ToString() => $"Vector2<{typeof(T).Name}>({X}, {Y})";
    }



    /// <summary>
    /// A generic 3D vector. For float values, use <see cref="Vector3"/>.
    /// </summary>
    public unsafe struct Vector3<T> : IEquatable<Vector3<T>> where T : INumber<T>
    {
        public static readonly Vector3<T> Zero = new(T.Zero, T.Zero, T.Zero);
        public static readonly Vector3<T> One = new(T.One, T.One, T.One);

        public T X;
        public T Y;
        public T Z;

        public Vector3(T x, T y, T z) { X = x; Y = y; Z = z; }
        public Vector3(T value) { X = Y = Z = value; }

        public T this[int index]
        {
            readonly get => index == 0 ? X : index == 1 ? Y : index == 2 ? Z : throw new IndexOutOfRangeException();
            set { if (index == 0) X = value; else if (index == 1) Y = value; else if (index == 2) Z = value; else throw new IndexOutOfRangeException(); }
        }

        public static Vector3<T> operator +(Vector3<T> a, Vector3<T> b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
        public static Vector3<T> operator -(Vector3<T> a, Vector3<T> b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
        public static Vector3<T> operator *(Vector3<T> v, T scalar) => new(v.X * scalar, v.Y * scalar, v.Z * scalar);
        public static Vector3<T> operator *(Vector3<T> a, Vector3<T> b) => new(a.X * b.X, a.Y * b.Y, a.Z * b.Z);
        public static Vector3<T> operator *(T scalar, Vector3<T> v) => v * scalar;
        public static Vector3<T> operator /(Vector3<T> v, T scalar) => new(v.X / scalar, v.Y / scalar, v.Z / scalar);
        public static Vector3<T> operator /(Vector3<T> a, Vector3<T> b) => new(a.X / b.X, a.Y / b.Y, a.Z / b.Z);


        public static bool operator ==(Vector3<T> a, Vector3<T> b) => a.X == b.X && a.Y == b.Y && a.Z == b.Z;
        public static bool operator !=(Vector3<T> a, Vector3<T> b) => !(a == b);

        public static explicit operator Vector3<T>(Vector3 val) => new(T.CreateChecked(val.X), T.CreateChecked(val.Y), T.CreateChecked(val.Z));
        public static explicit operator Vector3(Vector3<T> val) => new(float.CreateChecked(val.X), float.CreateChecked(val.Y), float.CreateChecked(val.Z));

        public readonly T Dot(Vector3<T> other) => X * other.X + Y * other.Y + Z * other.Z;


        public readonly Vector3<T> Cross(Vector3<T> other) => new(
            Y * other.Z - Z * other.Y,
            Z * other.X - X * other.Z,
            X * other.Y - Y * other.X
        );

        public override readonly bool Equals(object? obj) => obj is Vector3<T> other && Equals(other);
        public readonly bool Equals(Vector3<T> other) => X == other.X && Y == other.Y && Z == other.Z;
        public readonly bool Equals(Vector3 other) => float.CreateChecked(X) == other.X && float.CreateChecked(Y) == other.Y && float.CreateChecked(Z) == other.Z;
        public override readonly int GetHashCode() => HashCode.Combine(X, Y, Z);
        public override readonly string ToString() => $"Vector3<{typeof(T).Name}>({X}, {Y}, {Z})";
    }



    /// <summary>
    /// A generic 4D vector. For float values, use <see cref="Vector4"/>.
    /// </summary>
    public unsafe struct Vector4<T> : IEquatable<Vector4<T>> where T : INumber<T>
    {
        public static readonly Vector4<T> Zero = new(T.Zero, T.Zero, T.Zero, T.Zero);
        public static readonly Vector4<T> One = new(T.One, T.One, T.One, T.One);

        public T X;
        public T Y;
        public T Z;
        public T W;

        public Vector4(T x, T y, T z, T w) { X = x; Y = y; Z = z; W = w; }
        public Vector4(T value) { X = Y = Z = W = value; }

        public T this[int index]
        {
            readonly get => index == 0 ? X : index == 1 ? Y : index == 2 ? Z : index == 3 ? W : throw new IndexOutOfRangeException();
            set { if (index == 0) X = value; else if (index == 1) Y = value; else if (index == 2) Z = value; else if (index == 3) W = value; else throw new IndexOutOfRangeException(); }
        }

        public static Vector4<T> operator +(Vector4<T> a, Vector4<T> b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z, a.W + b.W);
        public static Vector4<T> operator -(Vector4<T> a, Vector4<T> b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z, a.W - b.W);
        public static Vector4<T> operator *(Vector4<T> v, T scalar) => new(v.X * scalar, v.Y * scalar, v.Z * scalar, v.W * scalar);
        public static Vector4<T> operator *(Vector4<T> a, Vector4<T> b) => new(a.X * b.X, a.Y * b.Y, a.Z * b.Z, a.W * b.W);
        public static Vector4<T> operator *(T scalar, Vector4<T> v) => v * scalar;
        public static Vector4<T> operator /(Vector4<T> v, T scalar) => new(v.X / scalar, v.Y / scalar, v.Z / scalar, v.W / scalar);
        public static Vector4<T> operator /(Vector4<T> a, Vector4<T> b) => new(a.X / b.X, a.Y / b.Y, a.Z / b.Z, a.W / b.W);


        public static bool operator ==(Vector4<T> a, Vector4<T> b) => a.X == b.X && a.Y == b.Y && a.Z == b.Z && a.W == b.W;
        public static bool operator !=(Vector4<T> a, Vector4<T> b) => !(a == b);

        public static explicit operator Vector4<T>(Vector4 val) => new(T.CreateChecked(val.X), T.CreateChecked(val.Y), T.CreateChecked(val.Z), T.CreateChecked(val.W));
        public static explicit operator Vector4(Vector4<T> val) => new(float.CreateChecked(val.X), float.CreateChecked(val.Y), float.CreateChecked(val.Z), float.CreateChecked(val.W));

        public readonly T Dot(Vector4<T> other) => X * other.X + Y * other.Y + Z * other.Z + W * other.W;


        public override readonly bool Equals(object? obj) => obj is Vector4<T> other && Equals(other);
        public readonly bool Equals(Vector4<T> other) => X == other.X && Y == other.Y && Z == other.Z && W == other.W;
        public readonly bool Equals(Vector4 other) => float.CreateChecked(X) == other.X && float.CreateChecked(Y) == other.Y && float.CreateChecked(Z) == other.Z && float.CreateChecked(W) == other.W;
        public override readonly int GetHashCode() => HashCode.Combine(X, Y, Z, W);
        public override readonly string ToString() => $"Vector4<{typeof(T).Name}>({X}, {Y}, {Z}, {W})";
    }






    /// <summary>
    /// Decrements this float (as a ref value) by <paramref name="delta"/>, until it reaches 0. Returns true when equal to 0.
    /// </summary>
    /// <param name="time"></param>
    /// <param name="delta"></param>
    /// <returns></returns>
    public static bool TimerDecrement(ref this float time, float delta)
    {
        time = float.Max(time - delta, 0);
        return time == 0;
    }

    /// <summary>
    /// Increments this float (as a ref value) by <paramref name="delta"/>, until it reaches <paramref name="max"/>. Returns true when equal to <paramref name="max"/>.
    /// </summary>
    /// <param name="time"></param>
    /// <param name="delta"></param>
    /// <param name="max"></param>
    /// <returns></returns>
    public static bool TimerIncrement(ref this float time, float delta, float max)
    {
        time = float.Min(time + delta, max);
        return time == max;
    }






    public static Quaternion Normalized(this Quaternion q) => Quaternion.Normalize(q);


    public static Vector3 Normalized(this Vector3 v)
    {
        if (v == Vector3.Zero) return Vector3.Zero;
        return Vector3.Normalize(v);
    }

    public static Vector2 Normalized(this Vector2 v)
    {
        if (v == Vector2.Zero) return Vector2.Zero;
        return Vector2.Normalize(v);
    }


    public static Vector3 LimitLength(this Vector3 v, float max)
    {
        float len = v.Length();
        if (len > max)
            return v * (max / len);
        return v;
    }



    public static float DistanceTo(this Vector3 a, Vector3 b) => Vector3.Distance(a, b);
    public static float DistanceToSquared(this Vector3 a, Vector3 b) => Vector3.DistanceSquared(a, b);


    public static Vector3 DirectionTo(this Vector3 a, Vector3 b) => (b - a).Normalized();




    public static void VectorThreshold(this Vector3 v, float thres = 0.05f)
    {
        if (v.Length() <= thres) v = Vector3.Zero;
    }


    public static float Dot(this Vector3 a, Vector3 b) => Vector3.Dot(a, b);



    public static Vector3 Cross(this Vector3 a, Vector3 b) => Vector3.Cross(a, b);


    public static Vector3 Clamp(this Vector3 a, Vector3 min, Vector3 max) => Vector3.Clamp(a, min, max);


    public static float Clamp(this float a, float min, float max) => float.Clamp(a, min, max);



    public static int CeilToInt(float val) => (int)MathF.Ceiling(val);
    public static int FloorToInt(float val) => (int)MathF.Floor(val);
    public static int RoundToInt(float val) => (int)MathF.Round(val);
    public static uint RoundToUInt(float val) => (uint)MathF.Round(val);



    public static float DegToRad(float angle) => MathF.PI / 180 * angle;

    public static float RadToDeg(float angle) => angle * (180 / MathF.PI);


    public static float InverseLerp(this float a, float b, float v)
    {
        if (a == b) return 0f;
        return Clamp((v - a) / (b - a), 0f, 1f);
    }


    public static float LerpExp(float current, float target, float smoothing)
    {
        float t = 1f - MathF.Exp(-smoothing * Logic.Delta);
        return float.Lerp(current, target, t);
    }






    //gives you a vector that describes how different one vector is to the other in terms of ratio;
    //for example, (1920,1080) and (640,480) would give (1.333, 1), so if you divided 1920,1080 by that, you'd get it in the same aspect ratio as 640,480, or vice versa with multiplicaiton
    public static Vector2 RatioConvert2D(Vector2 FromDimension, Vector2 ToDimension, bool Clamp = true)
    {
        float bufX = FromDimension.X / FromDimension.Y / (ToDimension.X / ToDimension.Y);
        float bufY = 1f;
        if (bufX < 1f || !Clamp)
        {
            if (Clamp) bufX = 1f;
            bufY = FromDimension.Y / FromDimension.X / (ToDimension.Y / ToDimension.X);
        }

        return new(bufX, bufY);
    }



    public static Vector3? Unproject(Vector3 worldpos, Matrix4x4 ViewMatrix, Matrix4x4 projectionMatrix)
    {
        var clipSpacePos = Vector4.Transform(new Vector4(worldpos, 1.0f), ViewMatrix);
        clipSpacePos = Vector4.Transform(clipSpacePos, projectionMatrix);

        if (clipSpacePos.W != 0.0f) clipSpacePos /= clipSpacePos.W;


        if (clipSpacePos.X < -1 || clipSpacePos.X > 1 ||
            clipSpacePos.Y < -1 || clipSpacePos.Y > 1 ||
            clipSpacePos.Z < 0 || clipSpacePos.Z > 1)
        {
            return null;
        }

        var screenX = (1.0f + clipSpacePos.X) * 0.5f;
        var screenY = (1f-(1.0f + clipSpacePos.Y) * 0.5f);
        var screenZ = (1.0f + clipSpacePos.Z) * 0.5f;

        return new Vector3(screenX, screenY, screenZ);
    }





    public static bool IsParallel(this Vector3 v1, Vector3 v2)
    {
        if (v1.Length() < 0.00001f || v2.Length() < 0.00001f) return false;

        float dot = MathF.Abs(Vector3.Normalize(v1).Dot(Vector3.Normalize(v2)));
        return dot >= 0.9999f;
    }




    /// <summary>
    /// Given a supply of Vector3 and a 0-1 float of progress, return a catmull-rom interpolated Vector3 point.
    /// </summary>
    /// <param name="vectors"></param>
    /// <param name="progress"></param>
    /// <returns></returns>
    public static Vector3 CatmullInterp(ReadOnlySpan<Vector3> vectors, float progress)
    {
        progress = float.Clamp(progress, 0, vectors.Length - 1);

        int floorIndex = FloorToInt(progress);
        int ceilIndex = CeilToInt(progress);

        int p0 = int.Clamp(floorIndex - 1, 0, vectors.Length - 1);
        int p1 = floorIndex;
        int p2 = ceilIndex;
        int p3 = int.Clamp(ceilIndex + 1, 0, vectors.Length - 1);

        float t = progress % 1;

        float t2 = t * t;
        float t3 = t * t2;

        Vector3 result =
            0.5f * ((2.0f * vectors[p1]) +
                    (-vectors[p0] + vectors[p2]) * t +
                    (2.0f * vectors[p0] - 5.0f * vectors[p1] + 4.0f * vectors[p2] - vectors[p3]) * t2 +
                    (-vectors[p0] + 3.0f * vectors[p1] - 3.0f * vectors[p2] + vectors[p3]) * t3);


        return result;
    }

}