
namespace Engine.Core;

using System;
using System.Buffers;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;




/// <summary>
/// A base for anything manually reference counted, inherited from <see cref="Freeable"/>. <br />
/// On construction, <see cref="UserCount"/> will be 0.
/// </summary>
public abstract class RefCounted : Freeable
{
    public uint UserCount { get; private set; }


    public void AddUser()
    {
        lock (this)
        {
            UserCount++;
        }
    }


    public void RemoveUser()
    {
        lock (this)
        {
            if (Valid)
            {
                UserCount--;
                if (UserCount <= 0) Free();
            }
        }
    }

}

/// <summary>
/// A base for any object with a lifetime.
/// <br /> <b>You should ALWAYS call <see cref="Free"/> to destroy an object. Each freeable claims a <see cref="System.Runtime.InteropServices.GCHandle"/> (<see cref="GCHandle"/>) for itself automatically for use in unmanaged memory and only releases that handle during <see cref="Free"/>. </b>
/// </summary>
public abstract class Freeable : IGCHandleOwner
{

    public bool Valid { get; private set; } = true;
    

    public void Free()
    {
        lock (this)
        {
            if (Valid)
            {
                Valid = false;
                if (OnFree != null) OnFreeEvent.Invoke();
                OnFree();

                GCHandle.Free();
            }
        }
    }

    protected abstract void OnFree();



    public readonly ThreadSafeEventAction OnFreeEvent = new();


    /// <summary>
    /// A GC handle owned by the object. Freed on <see cref="Free"/>.
    /// </summary>
    public GCHandle GCHandle => _handle;

    private readonly GCHandle _handle;



    public Freeable()
    {
        _handle = GCHandle.Alloc(this, GCHandleType.Normal);
    }

}



/// <summary>
/// An interface for objects that own their own GC handles.
/// </summary>
public interface IGCHandleOwner
{
    public GCHandle GCHandle { get; }
}


public static class MiscExtensions
{

    /// <summary>
    /// Returns <typeparamref name="T"/>'s <see cref="IGCHandleOwner.GCHandle"/> cast to <see cref="GCHandle{T}"/>.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="target"></param>
    /// <returns></returns>
    public static GCHandle<T> GetGenericGCHandle<T>(this T target) where T : class, IGCHandleOwner
        => (GCHandle<T>)target.GCHandle;

}






/// <summary>
/// A small unmanaged dictionary-style collection that uses supplied <see cref="GCHandle"/>s for keys and values to allow stack allocation.
/// <br/> Given <see cref="GCHandle"/>s must be allocated and freed manually. This collection doesn't own anything - see <seealso cref="UnmanagedKeyValueHandleCollectionOwner{KeyType, ValueType}"/> for a wrapper that does.
/// </summary>
public unsafe struct UnmanagedKeyValueHandleCollection<KeyT, ValueT> where KeyT : class where ValueT : class
{

    ////////////////////////////////////////////////
    private const int SizeLimit = 16;
    ////////////////////////////////////////////////



    [InlineArray(SizeLimit)]
    private struct KVPairs { public KeyValuePair<GCHandle<KeyT>, GCHandle<ValueT>> value; }

    private KVPairs KVs;
    private int _count;


    public UnmanagedKeyValueHandleCollection(ReadOnlySpan<KeyValuePair<GCHandle<KeyT>, GCHandle<ValueT>>> entries)
    {
        if (entries.Length > SizeLimit)
            throw new IndexOutOfRangeException();

        _count = entries.Length;
        KVs = default;

        for (int i = 0; i < _count; i++)
            KVs[i] = entries[i];
    }


    public readonly int Count => _count;



    public readonly KeyValuePair<GCHandle<KeyT>, GCHandle<ValueT>> this[int i] => KVs[i];



    //because of the small key counts, iterating over all keys is fine

    public readonly GCHandle<ValueT> this[KeyT key]
    {
        get
        {
            for (int i = 0; i < _count; i++)
            {
                var kv = KVs[i];

                if (KeyEquals(kv.Key.Target, key))
                    return kv.Value;

            }
            return default;
        }
    }

    public GCHandle<ValueT> this[GCHandle<KeyT> key]
    {
        readonly get
        {
            for (int i = 0; i < _count; i++)
            {
                var kv = KVs[i];

                if (KeyEquals(kv.Key.Target, key.Target))
                    return kv.Value;

            }

            return default;
        }

        set
        {

            for (int i = 0; i < _count; i++)
            {
                ref var kv = ref KVs[i];

                if (KeyEquals(kv.Key.Target, key.Target))
                {
                    if (!key.IsAllocated)
                    {
                        int tailCount = _count - i - 1;
                        if (tailCount > 0)
                        {
                            var src = MemoryMarshal.CreateSpan(ref KVs[i + 1], tailCount);
                            var dst = MemoryMarshal.CreateSpan(ref KVs[i], tailCount);
                            src.CopyTo(dst);
                        }

                        _count--;
                        return;
                    }

                    kv = new(key, value);
                    return;
                }
            }

            if (_count >= SizeLimit)
                throw new IndexOutOfRangeException();

            if (!key.IsAllocated) return;

            KVs[_count++] = new(key, value);

        }
    }


    public void Add(GCHandle<KeyT> key, GCHandle<ValueT> value)
        => this[key] = value;


    public readonly Enumerator GetEnumerator()
        => new(this);


    public ref struct Enumerator : IEnumerator
    {
        private readonly UnmanagedKeyValueHandleCollection<KeyT, ValueT> _owner;
        private int _index;

        public Enumerator(UnmanagedKeyValueHandleCollection<KeyT, ValueT> owner)
        {
            _owner = owner;
            _index = -1;
        }

        public bool MoveNext()
            => ++_index < _owner._count;


        public readonly KeyValuePair<GCHandle<KeyT>, GCHandle<ValueT>> Current => _owner.KVs[_index];


        readonly object IEnumerator.Current => Current;
        public void Reset() => _index = -1;
    }




    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool KeyEquals(KeyT a, KeyT b) => EqualityComparer<KeyT>.Default.Equals(a, b);


    public readonly UnmanagedKeyValueHandleCollection<KeyT, ValueT> Combine(in UnmanagedKeyValueHandleCollection<KeyT, ValueT> b)
    {
        var result = this;

        for (int i = 0; i < b._count; i++)
        {
            var key = b.KVs[i].Key;
            result[key] = b[key];
        }


        return result;
    }
}



/// <summary>
/// Wraps an <see cref="UnmanagedKeyValueHandleCollection{KeyT, ValueT}"/> and creates and releases normal <see cref="GCHandle"/>s when keys/values are added/removed.
/// <br/> Handles will be released on object destruction.
/// <br/> The intention is that logical semi-permanent collections can still exist while temporary transient permutations and combinations can be quickly created and passed downstream without constant further allocation.
/// </summary>
public class UnmanagedKeyValueHandleCollectionOwner<KeyT, ValueT> : IEnumerable<KeyValuePair<KeyT, ValueT>> where KeyT : class where ValueT : class
{

    public UnmanagedKeyValueHandleCollectionOwner()
    {
        _enumerator = new(this);
    }


    private UnmanagedKeyValueHandleCollection<KeyT, ValueT> Collection;

    private readonly Dictionary<KeyT, GCHandle> keyhandles = new();
    private readonly Dictionary<ValueT, GCHandle> valuehandles = new();


    public ref UnmanagedKeyValueHandleCollection<KeyT, ValueT> GetUnderlyingCollection() => ref Collection;



    public ValueT this[KeyT key]
    {
        get => Collection[key].Target;
        set
        {
            var khandle = (GCHandle<KeyT>)AcquireGCHandleFor(key, keyhandles);

            if (value == null)
            {
                Collection[khandle] = default;
                khandle.Free();
                keyhandles.Remove(key);
                return;
            }

            Collection[khandle] = (GCHandle<ValueT>)AcquireGCHandleFor(value, valuehandles);
        }
    }



    private static GCHandle AcquireGCHandleFor<T>(T key, Dictionary<T, GCHandle> handles) where T : class
    {
        if (handles.TryGetValue(key, out var kget)) return kget;
        else kget = handles[key] = GCHandle.Alloc(key, GCHandleType.Normal);

        return kget;
    }



    ~UnmanagedKeyValueHandleCollectionOwner()
    {
        foreach (var kv in keyhandles) kv.Value.Free();
        foreach (var kv in valuehandles) kv.Value.Free();
    }





    public int Count => Collection.Count;



    public void Add(KeyT key, ValueT value)
        => this[key] = value;

    public IEnumerator<KeyValuePair<KeyT, ValueT>> GetEnumerator()
    {
        _enumerator.Reset();
        return _enumerator;
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }


    //class with single instance to prevent gc / boxing
    private readonly Enumerator _enumerator;



    public class Enumerator(UnmanagedKeyValueHandleCollectionOwner<KeyT, ValueT> owner) : IEnumerator<KeyValuePair<KeyT, ValueT>>
    {
        private int _index = -1;

        public bool MoveNext() => ++_index < owner.Collection.Count;

        public KeyValuePair<KeyT, ValueT> Current
        {
            get
            {
                var get = owner.Collection[_index];
                return new(get.Key.Target, get.Value.Target);
            }
        }


        object IEnumerator.Current => Current;

        public void Reset() => _index = -1;

        public void Dispose() { }
    }


}




















/// <summary>
/// An event-style <typeparamref name="delegateT"/> that locks during add, remove and invoke.
/// <br/> Uses a hashset internally, so identical actions aren't allowed.
/// </summary>
public abstract class ThreadSafeEventBase<delegateT> where delegateT : Delegate
{
    protected readonly HashSet<delegateT> _delegates = new();

    public void Add(delegateT a)
    {
        lock (this)
            _delegates.Add(a);
    }

    public void Remove(delegateT a)
    {
        lock (this)
            _delegates.Remove(a);
    }

    public static ThreadSafeEventBase<delegateT> operator +(ThreadSafeEventBase<delegateT> left, delegateT right)
    {
        left.Add(right);
        return left;
    }

    public static ThreadSafeEventBase<delegateT> operator -(ThreadSafeEventBase<delegateT> left, delegateT right)
    {
        left.Remove(right);
        return left;
    }
}



/// <summary>
/// <inheritdoc cref="ThreadSafeEventBase{delegateT}"/>
/// </summary>
public class ThreadSafeEventAction : ThreadSafeEventBase<Action>
{
    public void Invoke()
    {
        lock (this)
        {
            foreach (var d in _delegates)
                d.Invoke();
        }
    }
}


/// <summary>
/// <inheritdoc cref="ThreadSafeEventBase{delegateT}"/>
/// </summary>
public class ThreadSafeEventAction<ArgType> : ThreadSafeEventBase<Action<ArgType>>
{
    public void Invoke(ArgType arg)
    {
        lock (this)
        {
            foreach (var d in _delegates)
                d.Invoke(arg);
        }
    }
}










public static class Comparisons
{

    /// <summary>
    /// Creates a dictionary with a very fast unmanaged struct equality comparer.
    /// </summary>
    /// <typeparam name="Key"></typeparam>
    /// <typeparam name="Value"></typeparam>
    /// <returns></returns>
    public static Dictionary<Key, Value> CreateUnsafeStructKeyComparisonDictionary<Key, Value>() where Key : unmanaged 
        => new Dictionary<Key, Value>(new UnmanagedStructComparer<Key>());



    public unsafe sealed class UnmanagedStructComparer<T> : IEqualityComparer<T>
        where T : unmanaged
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Equals(T x, T y)
        {
            return new ReadOnlySpan<byte>(&x, sizeof(T))
                .SequenceEqual(new ReadOnlySpan<byte>(&y, sizeof(T)));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int GetHashCode(T value)
        {
            //FNV-1a hash
            ReadOnlySpan<byte> data = new(&value, sizeof(T));
            const uint FNV_OFFSET = 2166136261u;
            const uint FNV_PRIME = 16777619u;

            uint hash = FNV_OFFSET;
            foreach (byte b in data)
            {
                hash ^= b;
                hash *= FNV_PRIME;
            }

            return (int)hash;
        }
    }




    public class DictionaryEqualityComparer<TKey, TValue> : IEqualityComparer<IDictionary<TKey, TValue>>
    {
        private readonly IEqualityComparer<TValue> _valueComparer;

        public DictionaryEqualityComparer(IEqualityComparer<TValue> valueComparer = null)
        {
            _valueComparer = valueComparer ?? EqualityComparer<TValue>.Default;
        }

        public bool Equals(IDictionary<TKey, TValue> x, IDictionary<TKey, TValue> y)
        {
            if (ReferenceEquals(x, y)) return true;
            if (x is null || y is null || x.Count != y.Count) return false;

            foreach (var kvp in x)
            {
                if (!y.TryGetValue(kvp.Key, out var value)) return false;
                if (!_valueComparer.Equals(kvp.Value, value)) return false;
            }

            return true;
        }

        public int GetHashCode(IDictionary<TKey, TValue> obj)
        {
            int hash = 17;
            foreach (var kvp in obj.OrderBy(k => k.Key))
            {
                hash = hash * 31 + (kvp.Key?.GetHashCode() ?? 0);
                hash = hash * 31 + (_valueComparer.GetHashCode(kvp.Value));
            }
            return hash;
        }
    }

}



/// <summary>
/// Wraps <see cref="GCHandle"/> with a strongly typed generic.
/// </summary>
/// <typeparam name="T"></typeparam>
public unsafe struct GCHandle<T>  where T : class 
{

    private GCHandle Handle;

    public GCHandle(GCHandle handle)
    {
        Handle = handle;
    }

    /// <summary>
    /// Gets the object this handle represents.
    /// </summary>
    public readonly T Target => (T)Handle.Target;

    public void Free()
    {
        Handle.Free();
        Handle = default;
    }

    public readonly bool IsAllocated => Handle.IsAllocated;


    public static GCHandle<T> Alloc(T reference, GCHandleType type) => new(GCHandle.Alloc(reference, type));
    public static GCHandle<T> Alloc(T reference) => new(GCHandle.Alloc(reference));


    public static explicit operator GCHandle<T> (GCHandle handle) => new(handle);
    public static explicit operator GCHandle(GCHandle<T> handle) => handle.Handle;

}









public static class Sorting
{

    /// <summary>
    /// Sorts a span of (T, float) by the floats.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="source"></param>
    /// <param name="largestFirst"></param>
    /// <returns></returns>
    /// <exception cref="Exception"></exception>
    public static unsafe void RadixSort<T>(this Span<(T item, float key)> source, bool largestFirst = false)
    {
        int n = source.Length;
        if (n == 0) return;

        var bufA = source; // input span
        var tempArr = ArrayPools.RentArrayFromPool<(T, float)>(n); // temporary buffer for radix passes
        var bufB = tempArr.Ref;

        (T, float)* a = ((T, float)*)Unsafe.AsPointer(ref bufA[0]);
        (T, float)* b = ((T, float)*)Unsafe.AsPointer(ref bufB[0]);


        uint* keys = stackalloc uint[n];
        int* count = stackalloc int[256];
        int* offset = stackalloc int[256];


        // --------------------------
        // Build sortable keys
        // --------------------------
        int i = 0;
        int unrolled = n & ~3;
        for (; i < unrolled; i += 4)
        {
            float f0 = a[i + 0].Item2;
            float f1 = a[i + 1].Item2;
            float f2 = a[i + 2].Item2;
            float f3 = a[i + 3].Item2;

            uint b0 = *(uint*)&f0 ^ (uint)((int)*(uint*)&f0 >> 31 | 0x80000000);
            uint b1 = *(uint*)&f1 ^ (uint)((int)*(uint*)&f1 >> 31 | 0x80000000);
            uint b2 = *(uint*)&f2 ^ (uint)((int)*(uint*)&f2 >> 31 | 0x80000000);
            uint b3 = *(uint*)&f3 ^ (uint)((int)*(uint*)&f3 >> 31 | 0x80000000);

            keys[i + 0] = b0;
            keys[i + 1] = b1;
            keys[i + 2] = b2;
            keys[i + 3] = b3;
        }
        for (; i < n; i++)
        {
            float f = a[i].Item2;
            keys[i] = *(uint*)&f ^ (uint)((int)*(uint*)&f >> 31 | 0x80000000);
        }

        const int PASSES = 4;
        for (int pass = 0; pass < PASSES; pass++)
        {
            int shift = pass * 8;

            // zero counts
            for (int j = 0; j < 256; j += 8)
            {
                count[j + 0] = 0; count[j + 1] = 0;
                count[j + 2] = 0; count[j + 3] = 0;
                count[j + 4] = 0; count[j + 5] = 0;
                count[j + 6] = 0; count[j + 7] = 0;
            }

            // COUNT
            for (i = 0; i < n; i++)
            {
                count[(keys[i] >> shift) & 0xFF]++;
            }

            // prefix
            int running = 0;
            for (i = 0; i < 256; i++)
            {
                offset[i] = running;
                running += count[i];
            }

            // SCATTER
            for (i = 0; i < n; i++)
            {
                int pos = offset[(int)((keys[i] >> shift) & 0xFF)]++;
                b[pos] = a[i];
            }

            // swap buffers
            var tmp = a;
            a = b;
            b = tmp;
        }

        // Copy back if the sorted data isn't already in the input
        if (a != ((T, float)*)Unsafe.AsPointer(ref source[0]))
        {
            Buffer.MemoryCopy(a, Unsafe.AsPointer(ref source[0]), n * sizeof((T, float)), n * sizeof((T, float)));
        }


        // reverse if needed
        if (largestFirst)
        {
            i = 0;
            int j = n - 1;
            while (i < j)
            {
                var tmp = source[i];
                source[i] = source[j];
                source[j] = tmp;
                i++; j--;
            }
        }

        tempArr.Return();
    }


}





public static class ArrayPools
{

    public static ArrayFromPool<T> RentArrayFromPool<T>(int length)
    {
        var arr = ArrayPool<T>.Shared.Rent(length);
        Array.Clear(arr, 0, length);

        return new ArrayFromPool<T>(arr, length);
    }


    public static ArrayFromPool<T> CreateBorrowedArrayPoolClone<T>(
        this ReadOnlySpan<T> span)
    {
        var arr = ArrayPool<T>.Shared.Rent(span.Length);
        span.CopyTo(arr);

        return new ArrayFromPool<T>(arr, span.Length);
    }




    public struct ArrayFromPool<T> : IDisposable
    {
        //dont use unless you know what you're doing
        public T[] Ref;

        public int Length;  //could be different from actual Ref.Length

        public ArrayFromPool(T[] @ref, int length)
        {
            Ref = @ref;
            Length = length;
        }

        public readonly T this[int idx]
        {
            get => Ref[idx];
            set => Ref[idx] = value;
        }

        public static implicit operator Span<T>(ArrayFromPool<T> a) => new Span<T>(a.Ref, 0, a.Length);
        public static implicit operator ReadOnlySpan<T>(ArrayFromPool<T> a) => new ReadOnlySpan<T>(a.Ref, 0, a.Length);


#if DEBUG
        private bool Returned = false;
#endif

        public void Return()
        {

#if DEBUG
            if (Returned) throw new Exception("already returned");
#endif

            ArrayPool<T>.Shared.Return(Ref);

#if DEBUG
            Returned = true;
#endif
        }

        public void Dispose() => Return();
    }

}


/// <summary>
/// A generic temporary unmanaged arena heap allocator. Grows as needed. Content is invalidated on calls to <see cref="Reset"/> or <see cref="Shrink"/>.
/// </summary>
public unsafe class DynamicUnmanagedHAllocator
{
    private unsafe struct Block
    {
        public byte* Ptr;
        public int Size;
        public int Offset;
    }


    private Block _current;
    private readonly List<Block> _oldBlocks = new();

    private readonly int _unitSize;
    private readonly object _lock = new();

    public DynamicUnmanagedHAllocator(int unitSize = 64 * 1024)
    {
#if DEBUG
        if (unitSize <= 0)
            throw new ArgumentException(nameof(unitSize));
#endif

        _unitSize = unitSize;

        _current = new Block
        {
            Ptr = (byte*)Marshal.AllocHGlobal(unitSize),
            Size = unitSize,
            Offset = 0
        };
    }

    public byte* Alloc(int bytes, int alignment = 16)
    {
#if DEBUG
        if (bytes <= 0)
            throw new ArgumentException(nameof(bytes));
        if ((alignment & (alignment - 1)) != 0)
            throw new ArgumentException("Alignment must be power of two");
#endif

        lock (_lock)
        {
            int alignedOffset = (_current.Offset + (alignment - 1)) & ~(alignment - 1);

            if (alignedOffset + bytes > _current.Size)
            {
                int units = (bytes + _unitSize - 1) / _unitSize;
                int newSize = units * _unitSize;

                _oldBlocks.Add(_current);

                _current = new Block
                {
                    Ptr = (byte*)Marshal.AllocHGlobal(newSize),
                    Size = newSize,
                    Offset = 0
                };


                alignedOffset = 0;
            }

            byte* ptr = _current.Ptr + alignedOffset;
            _current.Offset = alignedOffset + bytes;
            return ptr;
        }
    }

    /// <summary>
    /// Resets allocation state and frees all old blocks.
    /// The current block is retained.
    /// </summary>
    public void Reset()
    {
        lock (_lock)
        {
            foreach (var b in _oldBlocks)
                Marshal.FreeHGlobal((nint)b.Ptr);

            _oldBlocks.Clear();
            _current.Offset = 0;
        }
    }

    /// <summary>
    /// Resets the allocator and shrinks back to a single unit-sized block.
    /// </summary>
    public void Shrink()
    {
        lock (_lock)
        {
            Reset();

            if (_current.Size == _unitSize)
                return;

            Marshal.FreeHGlobal((nint)_current.Ptr);

            _current = new Block
            {
                Ptr = (byte*)Marshal.AllocHGlobal(_unitSize),
                Size = _unitSize,
                Offset = 0
            };
        }
    }

    /// <summary>
    /// Frees all memory owned by the allocator.
    /// The allocator must not be used after calling this.
    /// </summary>
    public void Free()
    {
        lock (_lock)
        {
            Reset();
            Marshal.FreeHGlobal((nint)_current.Ptr);
            _current = default;
        }
    }
}







/// <summary>
/// Represents a range to be written. Doesn't own or directly store data.
/// </summary>
public unsafe readonly struct WriteRange(uint offset, uint length, void* content)
{
    public readonly uint Offset = offset;
    public readonly uint Length = length;
    public readonly void* Content = content;
}






/// <summary>
/// An interface to define unmanaged deferred command structs for consumption via <see cref="DeferredCommandBuffer"/>.
/// <br /> Commands should store/reference immutable data to work as intended.
/// </summary>
public unsafe interface IDeferredCommand
{
    public static abstract void Execute(void* self);
}



public unsafe sealed class DeferredCommandBuffer
{



    [StructLayout(LayoutKind.Sequential)]
    private unsafe struct CommandHeader
    {
        public delegate*<void*, void> ExecuteFn;
        public ushort Size;

#if DEBUG
        public int DebugIndex;
#endif
    }





    private const int BufferSize = 1_000_000;
    private readonly byte[] _buffer = new byte[BufferSize];
    private int _offset;




#if DEBUG
    private readonly List<string> _debugOrigins = new();
#endif




    public void PushCommand<T>(T cmd
#if DEBUG
        , string file,
          int line
#endif
    )
        where T : unmanaged, IDeferredCommand

    {

        lock (this)
        {
            CommandHeader header = new CommandHeader
            {
                ExecuteFn = &T.Execute,
                Size = (ushort)(sizeof(CommandHeader) + sizeof(T))
            };


            fixed (byte* dst = &_buffer[_offset])
            {

                Unsafe.WriteUnaligned(dst, header);
                Unsafe.WriteUnaligned(dst + sizeof(CommandHeader), cmd);

#if DEBUG
                _debugOrigins.Add($"\n{typeof(T)}: Called from: {file} ({line})");
                ((CommandHeader*)dst)->DebugIndex = _debugOrigins.Count - 1;
#endif
            }

            _offset += sizeof(CommandHeader) + sizeof(T);
        }

    }

    public bool ContainsCommands()
    {
        lock (this)
            return _offset != 0;
    }





    /// <summary>
    /// Executes all commands and resets.
    /// </summary>
    public void Execute()
    {
        lock (this)
        {

            if (!ContainsCommands()) return;



            fixed (byte* start = _buffer)
            {
                byte* ptr = start;
                byte* end = start + _offset;

                while (ptr < end)
                {
                    var header = (CommandHeader*)ptr;

#if DEBUG
                    try
                    {
#endif
                        var cmdPtr = ptr + sizeof(CommandHeader);
                        header->ExecuteFn(cmdPtr);
#if DEBUG
                    }
                    catch (Exception ex)
                    {
                        var origin = _debugOrigins[header->DebugIndex];
                        throw new Exception($"{origin}\n Deferred command failed: {ex.Message}", ex);
                    }
#endif

                    ptr += header->Size;
                }
            }

            Reset();
        }

    }


    /// <summary>
    /// Resets the offset, effectively discarding all commands.
    /// </summary>
    public void Reset()
    {
        lock (this)
        {
            _offset = 0;
#if DEBUG
            _debugOrigins.Clear();
#endif
        }

    }

}



