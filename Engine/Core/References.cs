




using Engine.GameObjects;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;


namespace Engine.Core;


public static class References
{




    /// <summary>
    /// A stable, fast, unmanaged weak reference to an object.
    /// </summary>
    public readonly struct WeakObjRef : IEquatable<WeakObjRef>
    {

        private readonly ulong _value;


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public WeakObjRef(int id, int gen)
            => _value = ((ulong)(uint)id << 32) | (uint)gen;


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private WeakObjRef(ulong value)
           => _value = value;



        public readonly int ID
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => (int)(_value >> 32);
        }


        public readonly int Gen
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => (int)_value;
        }


        public readonly bool Equals(WeakObjRef other)
        {
            // same ref
            if (_value == other._value) return true;

            // same instance
            var a = Dereference();
            var b = other.Dereference();


            if (ReferenceEquals(a,b)) return true;

            return object.Equals(a, b);
        }




        public override readonly bool Equals(object? b)
        {
            if (b is WeakObjRef r)
                return Equals(r);


            var a = Dereference();

            if (ReferenceEquals(a, b)) return true;

            return object.Equals(a, b);
        }



        
        public override readonly int GetHashCode()
            => _value.GetHashCode();

        
        public static bool operator ==(WeakObjRef left, WeakObjRef right)
            => left._value == right._value;


        public static bool operator !=(WeakObjRef left, WeakObjRef right)
            => left._value != right._value;





        /// <summary>
        /// Resolves the underlying instance. Returns null if it no longer exists.
        /// </summary>
        /// <returns></returns>
        public object? Dereference() => Dereference(out var _);


        public enum Status
        {
            NullDefault,
            Alive,
            Dead
        }


        public object? Dereference(out Status status)
        {
            if (ID == 0)
            {
                status = Status.NullDefault;
                return null;
            }



            var slot = Volatile.Read(ref ReferenceTable[ID]);

            status = Status.Dead;

            if (slot == null
                || slot.Gen != Gen
                || !slot.Obj.IsAllocated)
            {
                status = Status.Dead;
                return null;
            }



            var target = slot.Obj.Target;

            if (target == null) return null;

            status = Status.Alive;
            return target;
        }




        /// <summary>
        /// Converts this <see cref="WeakObjRef"/> to a <see cref="nint"/>. Internally, <see cref="WeakObjRef"/> is already a <see cref="ulong"/>, so this is a simple cast, assuming this is a 64 bit platform.
        /// </summary>
        /// <returns></returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public nint ToIntPtr()
        {
            Validate64Bit();
            return (nint)_value;
        }


        /// <summary>
        /// Converts a <see cref="nint"/> to an <see cref="WeakObjRef"/>. This is unsafe and performs no validation. Internally, <see cref="WeakObjRef"/> is already a <see cref="ulong"/>, so this is a simple cast, assuming this is a 64 bit platform.
        /// </summary>
        /// <returns></returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static WeakObjRef FromIntPtr(nint ptr)
        {
            Validate64Bit();
            return new WeakObjRef((ulong)ptr);
        }



        [Conditional("DEBUG")]
        private static void Validate64Bit()
        {
            if (IntPtr.Size != 8)
                throw new PlatformNotSupportedException("ObjRef nint-interop requires 64-bit platform for 64-bit nint");
        }


#if DEBUG
        public override string ToString()
        {
            var deref = Dereference(out var status);
            return $"ObjRef: {(deref == null ? status.ToString() : deref.ToString())}";
        }
#endif

        [Conditional("DEBUG")]
        [DebuggerHidden]
        [StackTraceHidden]
        public readonly void ValidateNotNull()
        {
            if (Dereference() == null)
                throw new Exception("This reference is null");
        }

    }










    /// <summary>
    /// A stable, fast, unmanaged weak reference to an object instance of type <typeparamref name="T"/>.
    /// </summary>
    public readonly struct WeakObjRef<T> : IEquatable<WeakObjRef>, IEquatable<WeakObjRef<T>> where T : class
    {
        private readonly WeakObjRef Ref;


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public WeakObjRef(WeakObjRef id) => Ref = id;



        /// <summary>
        /// Resolves the underlying instance of <typeparamref name="T"/>. Returns null if it no longer exists.
        /// </summary>
        /// <returns></returns>
        public T Dereference() => (T)Ref.Dereference();

        public T Dereference(out WeakObjRef.Status status) => (T)Ref.Dereference(out status);



        /// <summary>
        /// <inheritdoc cref="WeakObjRef.ToIntPtr"/>
        /// </summary>
        /// <returns></returns>
        public nint ToIntPtr() => Ref.ToIntPtr();


        /// <summary>
        /// <inheritdoc cref="WeakObjRef.FromIntPtr(nint)"/>
        /// </summary>
        /// <returns></returns>
        public static WeakObjRef<T> FromIntPtr(nint ptr) => (WeakObjRef<T>)WeakObjRef.FromIntPtr(ptr);



        public static implicit operator WeakObjRef(WeakObjRef<T> id) => id.Ref;

        public static explicit operator WeakObjRef<T>(WeakObjRef id) => new(id);





        public readonly bool Equals(WeakObjRef other) => Ref.Equals(other);

        public override readonly bool Equals(object? obj) => Ref.Equals(obj);
        public override readonly int GetHashCode() => Ref.GetHashCode();
        public static bool operator ==(WeakObjRef<T> left, WeakObjRef right) => left.Equals(right);
        public static bool operator !=(WeakObjRef<T> left, WeakObjRef right) => !left.Equals(right);





        public readonly bool Equals(WeakObjRef<T> other) => Ref.Equals(other.Ref);

        public static bool operator ==(WeakObjRef<T> left, WeakObjRef<T> right) => left.Equals(right);
        public static bool operator !=(WeakObjRef<T> left, WeakObjRef<T> right) => !left.Equals(right);



#if DEBUG
        public override string ToString()
        {
            var deref = Dereference(out var status);
            return $"ObjRef<{typeof(T).Name}>: {(deref == null ? status.ToString() : deref.ToString())}";
        }
#endif

        [Conditional("DEBUG")]
        [DebuggerHidden]
        [StackTraceHidden]
        public readonly void ValidateNotNull() => Ref.ValidateNotNull();



    }














    public static bool TryGetUsingReference<TKey, TValue> (this ref UnmanagedKeyValueCollection<WeakObjRef<TKey>, TValue> collection, TKey key, out WeakObjRef<TKey> keyRef, out TValue value) where TKey : class where TValue : unmanaged
    {
        for (int i = 0; i < collection.Count; i++)
        {
            ref var get = ref collection.KeyValuePairs[i];

            if (get.Key.Equals(key))
            {
                keyRef = get.Key;
                value = get.Value;
                return true;
            }
        }

        keyRef = default;
        value = default;

        return false;
    }






    // this is a readonly class so that VolatileRead can attain in full atomically
    private sealed record class RefSlot(int Gen, GCHandle Obj);




    private static int ReferenceTableCurrentMax = 1;
    private static RefSlot[] ReferenceTable = new RefSlot[128];
    private static readonly List<int> Gaps = new();

    private static readonly ConditionalWeakTable<object, ReferenceID> ReferenceWeakTable = new();

    private static readonly object RefLock = new();




    private sealed class ReferenceID(int id, int gen)
    {
        public readonly int ID = id;
        public readonly int Gen = gen;

        ~ReferenceID()
        {
            lock (RefLock)
            {
                if (ID < ReferenceTable.Length)
                {
                    ref var slot = ref ReferenceTable[ID];

                    if (slot.Obj.IsAllocated)
                        slot.Obj.Free();

                    Gaps.Add(ID);
                }
            }
        }
    }







    /// <summary>
    /// Fetches or creates a stable weak reference for an <see cref="object"/>, meaning it gets automatically cleaned up once the object is garbage collected, and does NOT count as a reference that prevents said garbage collection.
    /// <br/> Each object can only ever have one <see cref="WeakObjRef"/>/<see cref="WeakObjRef{T}"/> at a time, assuming <paramref name="checkForExisting"/> is true. 
    /// <br/> <paramref name="checkForExisting"/> should never be false unless you absolutely know the object does not have a ref allocated and you have very good reason to prevent the check.
    /// </summary>
    public static WeakObjRef<T> GetWeakRef<T>(this T obj, bool checkForExisting = true) where T : class
    {

        if (obj == null) 
            return default;




        if (checkForExisting)
        {
            // fast specific paths

            if (obj is Freeable freeable)
            {
                if (freeable.Valid)
                    return (WeakObjRef<T>)freeable.SelfRef;

                return default;  
            }


            // semi fast path, weaktable is thread safe

            if (ReferenceWeakTable.TryGetValue(obj, out var existing))
                return new WeakObjRef<T>(new WeakObjRef(existing.ID, existing.Gen));

        }

#if DEBUG
        else
        {
            if (ReferenceWeakTable.TryGetValue(obj, out var existing))
                throw new InvalidOperationException("Reference for this object already exists");
        }
#endif



        lock (RefLock)
        {

            if (checkForExisting)
            {
                // required second check in lock

                if (ReferenceWeakTable.TryGetValue(obj, out var existing2))
                    return new WeakObjRef<T>(new WeakObjRef(existing2.ID, existing2.Gen));
            }


            
#if DEBUG
            if (ReferenceWeakTable.TryGetValue(obj, out var existing))
                throw new InvalidOperationException("Reference for this object already exists");
#endif




            int id;

            if (Gaps.Count == 0)
                id = ReferenceTableCurrentMax;

            else
            {
                id = Gaps[^1];
                Gaps.RemoveAt(Gaps.Count - 1);
            }



            if (id >= ReferenceTable.Length)
                Array.Resize(ref ReferenceTable, int.Max(id + 1, ReferenceTable.Length * 2));



            ref var slotRef = ref ReferenceTable[id];


            // refslot is immutable to prevent volatile read errors later

            var gen = (slotRef == null) ? 0 : slotRef.Gen+1;

            slotRef = new RefSlot(gen, GCHandle.Alloc(obj, GCHandleType.Weak));




            var refId = new ReferenceID(id, slotRef.Gen);
            ReferenceWeakTable.Add(obj, refId);



            if (id == ReferenceTableCurrentMax)
                ReferenceTableCurrentMax++;



            return new WeakObjRef<T>(new(id, slotRef.Gen));
        }

    }













    public static UnmanagedKeyValueCollection<WeakObjRef<TKey>, WeakObjRef<TValue>> ToUnmanagedKV<TKey, TValue>(this Dictionary<TKey, TValue> dict) where TKey : class where TValue : class
    {
        var ret = new UnmanagedKeyValueCollection<WeakObjRef<TKey>, WeakObjRef<TValue>>();

        ref var kvs = ref ret.KeyValuePairs;

        foreach (var kv in dict)
            kvs[ret.Count++] = new(kv.Key.GetWeakRef(), kv.Value.GetWeakRef());

        return ret;
    }

    public static UnmanagedKeyValueCollection<WeakObjRef<TKey>, TValue> ToUnmanagedK<TKey, TValue>(this Dictionary<TKey, TValue> dict) where TKey : class where TValue : unmanaged
    {
        var ret = new UnmanagedKeyValueCollection<WeakObjRef<TKey>, TValue>();

        ref var kvs = ref ret.KeyValuePairs;

        foreach (var kv in dict)
            kvs[ret.Count++] = new(kv.Key.GetWeakRef(), kv.Value);

        return ret;
    }

    public static UnmanagedKeyValueCollection<TKey, WeakObjRef<TValue>> ToUnmanagedV<TKey, TValue>(this Dictionary<TKey, TValue> dict) where TKey : unmanaged where TValue : class
    {
        var ret = new UnmanagedKeyValueCollection<TKey, WeakObjRef<TValue>>();

        ref var kvs = ref ret.KeyValuePairs;

        foreach (var kv in dict)
            kvs[ret.Count++] = new(kv.Key, kv.Value.GetWeakRef());

        return ret;
    }






}
