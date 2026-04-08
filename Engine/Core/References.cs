




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
        public object? Dereference()
        {
            if (ID == 0) return null;

            var slot = Volatile.Read(ref ReferenceTable[ID]);
            
            if (slot == null 
                || slot.Gen != Gen 
                || !slot.Obj.IsAllocated) return null;


            return slot.Obj.Target;
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
            var deref = Dereference();
            return $"ObjRef: {(deref == null ? "null" : deref.ToString())}";
        }
#endif
    }










    /// <summary>
    /// <see cref="WeakObjRef"/> wrapped with a generic.
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
            var deref = Dereference();
            return $"ObjRef<{typeof(T).Name}>: {(deref == null ? "null" : deref.ToString())}";
        }

#endif
    }










    public static UnmanagedKeyValueCollection<WeakObjRef<TKey>, TValue> DictToUnmanagedK<TKey, TValue>(this IDictionary<TKey, TValue> from) where TKey : class where TValue : unmanaged
    {

#if DEBUG
        if (from.Count > UnmanagedKeyValueCollection<byte,byte>.SizeLimit)
            throw new InvalidOperationException();
#endif

        var collection = new UnmanagedKeyValueCollection<WeakObjRef<TKey>, TValue>();


        //guaranteed unique items

        foreach (var kv in from)
            collection.KeyValuePairs[collection.Count++] = new(kv.Key.GetRef(), kv.Value);


        return collection;
    }


    public static UnmanagedKeyValueCollection<WeakObjRef<TKey>, WeakObjRef<TValue>> DictToUnmanagedKV<TKey, TValue>(this IDictionary<TKey, TValue> from) where TKey : class where TValue : class
    {

#if DEBUG
        if (from.Count > UnmanagedKeyValueCollection<byte, byte>.SizeLimit)
            throw new InvalidOperationException();
#endif

        var collection = new UnmanagedKeyValueCollection<WeakObjRef<TKey>, WeakObjRef<TValue>>();


        //guaranteed unique items

        foreach (var kv in from)
            collection.KeyValuePairs[collection.Count++] = new(kv.Key.GetRef(), kv.Value.GetRef());


        return collection;
    }


    public static UnmanagedKeyValueCollection<TKey, WeakObjRef<TValue>> DictToUnmanagedV<TKey, TValue>(this IDictionary<TKey, TValue> from) where TKey : unmanaged where TValue : class
    {

#if DEBUG
        if (from.Count > UnmanagedKeyValueCollection<byte, byte>.SizeLimit)
            throw new InvalidOperationException();
#endif

        var collection = new UnmanagedKeyValueCollection<TKey, WeakObjRef<TValue>>();


        //guaranteed unique items

        foreach (var kv in from)
            collection.KeyValuePairs[collection.Count++] = new(kv.Key, kv.Value.GetRef());


        return collection;
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






    // this is a class so that volatileread can attain atomically
    private sealed class RefSlot(int gen, GCHandle obj)
    {
        public int Gen = gen;
        public GCHandle Obj = obj;
    }




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
                    {
                        slot.Obj.Free();
                        slot.Obj = default;
                    }

                    Gaps.Add(ID);
                }
            }
        }
    }







    /// <summary>
    /// Fetches or creates a weak stable reference for an object, which is automatically cleaned up once the object is garbage collected.
    /// <br/> Each object can only ever have one ref at a time, assuming <paramref name="checkForExisting"/> is true. 
    /// <br/> <paramref name="checkForExisting"/> should never be false unless you absolutely know the object does not have a ref allocated, you know what you're doing, and you NEED the check not to happen.
    /// </summary>
    public static WeakObjRef<T> GetRef<T>([DisallowNull] this T obj, bool checkForExisting = true) where T : class
    {

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




        lock (RefLock)
        {

            if (checkForExisting)
            {
                // required second check in lock

                if (ReferenceWeakTable.TryGetValue(obj, out var existing2))
                    return new WeakObjRef<T>(new WeakObjRef(existing2.ID, existing2.Gen));
            }




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
            
            if (slotRef == null)
            {
                slotRef = new RefSlot(0, GCHandle.Alloc(obj, GCHandleType.Weak));
            }
            else
            {
                slotRef.Obj = GCHandle.Alloc(obj, GCHandleType.Weak);
                slotRef.Gen++;
            }



            var refId = new ReferenceID(id, slotRef.Gen);
            ReferenceWeakTable.Add(obj, refId);



            if (id == ReferenceTableCurrentMax)
                ReferenceTableCurrentMax++;



            return new WeakObjRef<T>(new(id, slotRef.Gen));
        }

    }

    

}
