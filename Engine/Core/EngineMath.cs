
namespace Engine.Core;



using System.Numerics;
using System.Runtime.InteropServices;




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





        // Equality implementation
        public readonly bool Equals(AABB other) => Min.Equals(other.Min) && Max.Equals(other.Max);

        public override readonly bool Equals(object? obj) => obj is AABB other && Equals(other);

        public override readonly int GetHashCode()
        {
            unchecked
            {
                int hash = 17;
                hash = hash * 31 + Min.GetHashCode();
                hash = hash * 31 + Max.GetHashCode();
                return hash;
            }
        }

        public static bool operator ==(AABB left, AABB right) => left.Equals(right);

        public static bool operator !=(AABB left, AABB right) => !(left == right);


        public static AABB operator *(AABB left, Transform transform) 
            => new AABB(transform * left.Min, transform * left.Max);




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







    /// <summary>
    /// A wrapper over System.Numerics's <see cref="Matrix4x4"/>. Column-major.
    /// </summary>
    public unsafe struct Transform : IEquatable<Transform>, IEquatable<Matrix4x4>
    {
        public static readonly Transform Identity = new(Matrix4x4.Identity);

        public Matrix4x4 Matrix;

        public Transform(Matrix4x4 matrix)
        {
            Matrix = matrix;
        }



        public Vector3 Origin
        {
            readonly get => Matrix.Translation;
            set => Matrix.Translation = value;
        }



        public Vector3 OrientationX
        {
            readonly get => new(Matrix.M11, Matrix.M21, Matrix.M31);
            set => Matrix = Matrix with { M11 = value.X, M21 = value.Y, M31 = value.Z };
        }

        public Vector3 OrientationY
        {
            readonly get => new(Matrix.M12, Matrix.M22, Matrix.M32);
            set => Matrix = Matrix with { M12 = value.X, M22 = value.Y, M32 = value.Z };
        }

        public Vector3 OrientationZ
        {
            readonly get => new(Matrix.M13, Matrix.M23, Matrix.M33);
            set => Matrix = Matrix with { M13 = value.X, M23 = value.Y, M33 = value.Z };
        }





        public struct PosRotScale
        {
            public Vector3 Origin;
            public Vector3 Scale;
            public Quaternion Rotation;
        }


        public readonly PosRotScale Decompose()
        {
            Vector3 scale;
            Quaternion rotation;
            Vector3 origin;
            if (!Matrix4x4.Decompose(Matrix, out scale, out rotation, out origin))
                throw new Exception("Failed to decompose matrix");
            return new PosRotScale { Origin = origin, Rotation = rotation, Scale = scale };
        }

        // ---------------------- Inverse ----------------------
        public readonly Transform Inverse()
        {
            Matrix4x4.Invert(Matrix, out var inv);
            return new Transform(inv);
        }



        public readonly Transform AffineInverse() => Inverse();




        public readonly Transform Rotated(Vector3 axis, float radians)
            => new(Matrix * Matrix4x4.CreateFromAxisAngle(Vector3.Normalize(axis), radians));

        public static Transform Rotated(Transform t, Vector3 axis, float radians)
            => t.Rotated(axis, radians);

        public static Transform FromRotation(Vector3 axis, float angle)
            => new(Matrix4x4.CreateFromAxisAngle(Vector3.Normalize(axis), angle));

        public static Transform FromRotation(Quaternion q)
            => new(Matrix4x4.CreateFromQuaternion(q));



        public static Transform FromEuler(Vector3 euler)
        {
            var rotX = Matrix4x4.CreateRotationX(euler.X);
            var rotY = Matrix4x4.CreateRotationY(euler.Y);
            var rotZ = Matrix4x4.CreateRotationZ(euler.Z);
            return new Transform(rotZ * rotX * rotY);
        }

        public readonly Vector3 GetEuler()
        {
            var m = Matrix;
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



        public readonly Transform Scaled(Vector3 scale)
            => new(Matrix4x4.CreateScale(scale) * Matrix);

        public static Transform FromScale(Vector3 scale)
            => new(Matrix4x4.CreateScale(scale));




        public readonly Transform Orthonormalized()
        {
            Vector3 x = new(Matrix.M11, Matrix.M12, Matrix.M13);
            Vector3 y = new(Matrix.M21, Matrix.M22, Matrix.M23);
            Vector3 z = new(Matrix.M31, Matrix.M32, Matrix.M33);

            x = Vector3.Normalize(x);
            y = Vector3.Normalize(y - Vector3.Dot(y, x) * x);
            z = Vector3.Cross(x, y);

            var mat = Matrix;
            mat.M11 = x.X; mat.M12 = x.Y; mat.M13 = x.Z;
            mat.M21 = y.X; mat.M22 = y.Y; mat.M23 = y.Z;
            mat.M31 = z.X; mat.M32 = z.Y; mat.M33 = z.Z;

            return new Transform(mat);
        }




        public static Transform LookingAt(Vector3 target, Vector3 up, Vector3 origin = default)
        {
            Vector3 z = Vector3.Normalize(target - origin);
            Vector3 x = Vector3.Normalize(Vector3.Cross(up, z));
            Vector3 y = Vector3.Cross(z, x);

            var mat = new Matrix4x4
            {
                M11 = x.X,
                M12 = x.Y,
                M13 = x.Z,
                M21 = y.X,
                M22 = y.Y,
                M23 = y.Z,
                M31 = z.X,
                M32 = z.Y,
                M33 = z.Z,
                Translation = origin
            };

            return new Transform(mat);
        }


        public readonly Transform LookingAt(Vector3 target, Vector3 up) => LookingAt(target, up, Origin);




        public static Transform operator *(Transform a, Transform b) => new(a.Matrix * b.Matrix);

        public static Transform operator *(Transform a, Matrix4x4 b) => new(a.Matrix * b);
        public static Transform operator *(Matrix4x4 a, Transform b) => new(a * b.Matrix);

        public static Vector3 operator *(Transform t, Vector3 v) => Vector3.Transform(v, t.Matrix);


        public static bool operator ==(Transform a, Transform b) => a.Matrix == b.Matrix;
        public static bool operator !=(Transform a, Transform b) => !(a == b);


        public static bool operator ==(Transform a, Matrix4x4 b) => a.Matrix == b;
        public static bool operator !=(Transform a, Matrix4x4 b) => !(a == b);


        public static implicit operator Transform(Matrix4x4 mat) => new(mat);
        public static implicit operator Matrix4x4(Transform tr) => tr.Matrix;


        public override readonly bool Equals(object? obj) => obj is Transform t && this == t;

        public override readonly int GetHashCode() => Matrix.GetHashCode();

        public override readonly string ToString() => Matrix.ToString();

        public bool Equals(Transform other) => Matrix == other.Matrix;

        public bool Equals(Matrix4x4 other) => Matrix == other;
    }





    /// <summary>
    /// Represents a 3x3 matrix of single-precision floating-point values.
    /// </summary>
    public unsafe struct Matrix3x3 : IEquatable<Matrix3x3>
    {
        public float M11, M12, M13;
        public float M21, M22, M23;
        public float M31, M32, M33;

        /// <summary>Creates a 3x3 matrix from 9 float values in row-major order.</summary>
        public Matrix3x3(
            float m11, float m12, float m13,
            float m21, float m22, float m23,
            float m31, float m32, float m33)
        {
            M11 = m11; M12 = m12; M13 = m13;
            M21 = m21; M22 = m22; M23 = m23;
            M31 = m31; M32 = m32; M33 = m33;
        }

        /// <summary>Creates a 3x3 matrix with all diagonal values set, others zero.</summary>
        public Matrix3x3(float diagonal)
        {
            M11 = M22 = M33 = diagonal;
            M12 = M13 = M21 = M23 = M31 = M32 = 0f;
        }

        public static readonly Matrix3x3 Identity = new(1f);

        /// <summary>Returns a new matrix that is the transpose of this matrix.</summary>
        public Matrix3x3 Transpose() => new(
            M11, M21, M31,
            M12, M22, M32,
            M13, M23, M33
        );

        /// <summary>Adds two matrices element-wise.</summary>
        public static Matrix3x3 operator +(Matrix3x3 a, Matrix3x3 b) => new(
            a.M11 + b.M11, a.M12 + b.M12, a.M13 + b.M13,
            a.M21 + b.M21, a.M22 + b.M22, a.M23 + b.M23,
            a.M31 + b.M31, a.M32 + b.M32, a.M33 + b.M33
        );

        /// <summary>Subtracts two matrices element-wise.</summary>
        public static Matrix3x3 operator -(Matrix3x3 a, Matrix3x3 b) => new(
            a.M11 - b.M11, a.M12 - b.M12, a.M13 - b.M13,
            a.M21 - b.M21, a.M22 - b.M22, a.M23 - b.M23,
            a.M31 - b.M31, a.M32 - b.M32, a.M33 - b.M33
        );

        /// <summary>Multiplies all elements by a scalar.</summary>
        public static Matrix3x3 operator *(Matrix3x3 m, float s) => new(
            m.M11 * s, m.M12 * s, m.M13 * s,
            m.M21 * s, m.M22 * s, m.M23 * s,
            m.M31 * s, m.M32 * s, m.M33 * s
        );

        /// <summary>Multiplies two 3x3 matrices together (matrix product).</summary>
        public static Matrix3x3 operator *(Matrix3x3 a, Matrix3x3 b) => new(
            a.M11 * b.M11 + a.M12 * b.M21 + a.M13 * b.M31,
            a.M11 * b.M12 + a.M12 * b.M22 + a.M13 * b.M32,
            a.M11 * b.M13 + a.M12 * b.M23 + a.M13 * b.M33,

            a.M21 * b.M11 + a.M22 * b.M21 + a.M23 * b.M31,
            a.M21 * b.M12 + a.M22 * b.M22 + a.M23 * b.M32,
            a.M21 * b.M13 + a.M22 * b.M23 + a.M23 * b.M33,

            a.M31 * b.M11 + a.M32 * b.M21 + a.M33 * b.M31,
            a.M31 * b.M12 + a.M32 * b.M22 + a.M33 * b.M32,
            a.M31 * b.M13 + a.M32 * b.M23 + a.M33 * b.M33
        );

        /// <summary>Multiplies a matrix by a vector3 (matrix * column vector).</summary>
        public static Vector3 operator *(Matrix3x3 m, Vector3 v) => new(
            m.M11 * v.X + m.M12 * v.Y + m.M13 * v.Z,
            m.M21 * v.X + m.M22 * v.Y + m.M23 * v.Z,
            m.M31 * v.X + m.M32 * v.Y + m.M33 * v.Z
        );

        public override bool Equals(object? obj) => obj is Matrix3x3 other && Equals(other);
        public bool Equals(Matrix3x3 other) =>
            M11 == other.M11 && M12 == other.M12 && M13 == other.M13 &&
            M21 == other.M21 && M22 == other.M22 && M23 == other.M23 &&
            M31 == other.M31 && M32 == other.M32 && M33 == other.M33;

        public override int GetHashCode()
        {
            HashCode hash = new HashCode();
            hash.Add(M11);
            hash.Add(M12);
            hash.Add(M13);
            hash.Add(M21);
            hash.Add(M22);
            hash.Add(M23);
            hash.Add(M31);
            hash.Add(M32);
            hash.Add(M33);
            return hash.ToHashCode();
        }

        public override string ToString() =>
            $"Matrix3x3(\n  {M11}, {M12}, {M13}\n  {M21}, {M22}, {M23}\n  {M31}, {M32}, {M33}\n)";
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
            get => index == 0 ? X : index == 1 ? Y : throw new IndexOutOfRangeException();
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

        public T Dot(Vector2<T> other) => X * other.X + Y * other.Y;

        public override bool Equals(object? obj) => obj is Vector2<T> other && Equals(other);
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
            get => index == 0 ? X : index == 1 ? Y : index == 2 ? Z : throw new IndexOutOfRangeException();
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

        public T Dot(Vector3<T> other) => X * other.X + Y * other.Y + Z * other.Z;
        public Vector3<T> Cross(Vector3<T> other) => new(
            Y * other.Z - Z * other.Y,
            Z * other.X - X * other.Z,
            X * other.Y - Y * other.X
        );

        public override bool Equals(object? obj) => obj is Vector3<T> other && Equals(other);
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
            get => index == 0 ? X : index == 1 ? Y : index == 2 ? Z : index == 3 ? W : throw new IndexOutOfRangeException();
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

        public T Dot(Vector4<T> other) => X * other.X + Y * other.Y + Z * other.Z + W * other.W;

        public override bool Equals(object? obj) => obj is Vector4<T> other && Equals(other);
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