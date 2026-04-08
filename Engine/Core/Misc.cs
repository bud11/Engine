
namespace Engine.Core;

using Engine.Stripped;
using System;
using System.Buffers;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using static Engine.Core.References;




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
/// <br /> <b><see cref="Free"/>, or <see cref="IDisposable.Dispose"/>, should always be called to destroy a <see cref="Freeable"/>.</b>
/// </summary>
public abstract class Freeable : IDisposable
{

    public bool Valid { get; private set; } = true;
    

    public void Free()
    {
        lock (this)
        {
            if (Valid)
            {
                Valid = false;
                OnFreeEvent.Invoke();
                OnFree();


                // debug finalizer can catch memory leaks
#if !DEBUG
                GC.SuppressFinalize(this);
#endif

            }
        }
    }


    protected abstract void OnFree();



#pragma warning disable CA1816  // Dispose methods should call SuppressFinalize

    public void Dispose() => Free();

#pragma warning restore CA1816 





    public readonly ThreadSafeEventAction OnFreeEvent = new();





    /// <summary>
    /// A reference to this. Used by <see cref="References.GetRef{T}(T, bool)"/>.
    /// </summary>
    public readonly WeakObjRef SelfRef;

    public Freeable()
    {
        SelfRef = this.GetRef(false);

#if DEBUG
        if (EngineDebug.FreeableConstructorStackTraceStorage) 
            ConstructorStackTrace = new();
#endif
    }



#if DEBUG

    private readonly StackTrace ConstructorStackTrace;

    ~Freeable()
    {
        if (Valid) 
            throw new Exception($"Memory leak: this {nameof(Freeable)} was not freed, but is now being garbage collected. \n --- CREATION STACKTRACE --- \n {ConstructorStackTrace.ToClickableSrcLinesString()}");
    }

#endif


}



public static class MiscExtensions
{



    public static bool HasFlags<T>(this T self, ReadOnlySpan<T> vals) where T : Enum
    {
        for (int i = 0; i < vals.Length; i++)
            if (!self.HasFlag(vals[i])) return false;

        return true;
    }



    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector4 ToVector4(this Color Col)
    {
        return new Vector4(Col.R, Col.G, Col.B, Col.A)/255f;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Color ToColor(this Vector4 Col)
    {
        return Color.FromArgb((int)(Col.W * 255), (int)(Col.X * 255), (int)(Col.Y * 255), (int)(Col.Z * 255));
    }


}











/// <summary>
/// A small unmanaged dictionary-style collection that can be stack allocated. Optimized for being frequently copied and permutated. See <see cref="SizeLimit"/>.
/// </summary>
public unsafe struct UnmanagedKeyValueCollection<TKey, TValue> : IEnumerable<KeyValuePair<TKey, TValue>> where TKey : unmanaged where TValue : unmanaged
{



    public const int SizeLimit = 16;



    [InlineArray(SizeLimit)]
    public struct KVPairs { public KeyValuePair<TKey, TValue> value; }


    /// <summary>
    /// The actual internal keys/values laid out within struct memory. Shouldn't be directly modified.
    /// </summary>
    public KVPairs KeyValuePairs;

    public int Count;


    public TValue this[in TKey key]
    {
        readonly get => Get(key);
        set => Set(key, value);
    }




    public UnmanagedKeyValueCollection(IDictionary<TKey, TValue> from)
    {

#if DEBUG
        if (from.Count > SizeLimit)
            throw new InvalidOperationException();
#endif

        // we can trust that all keys are unique 

        foreach (var kv in from)
            KeyValuePairs[Count++] = new(kv.Key, kv.Value);

    }









    public bool TryAdd(in TKey key, in TValue value)
    {
#if DEBUG
        if (Count >= SizeLimit)
            throw new InvalidOperationException();
#endif

        for (int i = 0; i < Count; i++)
        {
            ref var kv = ref KeyValuePairs[i];
            if (KeyEquals(kv.Key, key)) return false;
        }

        KeyValuePairs[Count++] = new(key, value);
        return true;
    }

    public void Add(in TKey key, in TValue value)
    {
        if (!TryAdd(key, value)) 
            throw new Exception();
    }




    public void Set(in TKey key, in TValue value)
    {
        for (int i = 0; i < Count; i++)
        {
            ref var kv = ref KeyValuePairs[i];
            if (KeyEquals(kv.Key, key))
            {
                kv = new(kv.Key, value);
                return;
            }
        }

#if DEBUG
        if (Count >= SizeLimit)
            throw new InvalidOperationException();
#endif

        KeyValuePairs[Count++] = new(key, value);
    }



    public readonly bool TryGet(in TKey key, out TValue value)
    {
        for (int i = 0; i < Count; i++)
        {
            var kv = KeyValuePairs[i];
            if (KeyEquals(kv.Key, key))
            {
                value = kv.Value;
                return true;
            }
        }

        value = default;
        return false;
    }
    public readonly TValue Get(in TKey key)
    {
        if (!TryGet(key, out var get)) 
            throw new Exception();

        return get;
    }



    public bool TryRemove(in TKey key)
    {
        for (int i = 0; i < Count; i++)
        {
            var kv = KeyValuePairs[i];
            if (KeyEquals(kv.Key, key))
            {
                int tailCount = Count - i - 1;
                if (tailCount > 0)
                {
                    var src = MemoryMarshal.CreateSpan(ref KeyValuePairs[i + 1], tailCount);
                    var dst = MemoryMarshal.CreateSpan(ref KeyValuePairs[i], tailCount);
                    src.CopyTo(dst);
                }

                Count--;

                return true;
            }
        }

        return false;
    }
    public void Remove(in TKey key)
    {
        if (!TryRemove(key))
            throw new Exception();
    }



    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool KeyEquals(in TKey a, in TKey b) 
        => EqualityComparer<TKey>.Default.Equals(a, b);





    /// <summary>
    /// Combines two <see cref="UnmanagedKeyValueCollection{TKey, TValue}"/>s.
    /// </summary>
    /// <param name="b"></param>
    /// <returns></returns>
    public readonly UnmanagedKeyValueCollection<TKey, TValue> Combine(in UnmanagedKeyValueCollection<TKey, TValue> b)
    {
        if (Count == 0 && b.Count == 0) return default;
        if (Count == 0) return b;
        if (b.Count == 0) return this;


        var result = this;

        for (int i = 0; i < b.Count; i++)
        {
            var key = b.KeyValuePairs[i].Key;
            result[key] = b[key];
        }

        return result;
    }





    public struct Enumerator(UnmanagedKeyValueCollection<TKey, TValue> owner) : IEnumerator<KeyValuePair<TKey, TValue>>
    {
        private int _index = -1;

        public bool MoveNext()
            => ++_index < owner.Count;


        public readonly KeyValuePair<TKey, TValue> Current => owner.KeyValuePairs[_index];


        readonly object IEnumerator.Current => Current;
        public void Reset() => _index = -1;


        public readonly void Dispose() { }
    }


    public readonly Enumerator GetEnumerator() => new(this);

    readonly IEnumerator<KeyValuePair<TKey, TValue>> IEnumerable<KeyValuePair<TKey, TValue>>.GetEnumerator() => GetEnumerator();
    readonly IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();





#if DEBUG
    public override string ToString()
    {
        StringBuilder sb = new();
        foreach (var kv in this)
            sb.AppendLine($"{{ {{ {kv.Key.ToString()}, {{ {kv.Value.ToString()} }} }} }}");

        return sb.ToString();
    }

#endif


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

        public static implicit operator Span<T>(ArrayFromPool<T> a) => new(a.Ref, 0, a.Length);
        public static implicit operator ReadOnlySpan<T>(ArrayFromPool<T> a) => new(a.Ref, 0, a.Length);


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
public unsafe interface IDeferredCommand<TSelf>
    where TSelf : unmanaged, IDeferredCommand<TSelf>
{
    static abstract void Execute(TSelf* self);
}



public unsafe sealed class DeferredCommandBuffer
{



    [StructLayout(LayoutKind.Sequential)]
    private unsafe struct CommandHeader
    {
        public void* ExecuteFn;
        public ushort Size;

#if DEBUG
        public int DebugIndex;
#endif
    }





    private const int BufferSize = 1_000_000;
    private readonly byte[] _buffer = new byte[BufferSize];
    private int _offset;




#if DEBUG
    private readonly List<StackTrace> _debugOrigins = new();
#endif




    public unsafe void PushCommand(delegate*<void> fn)
    {
        lock (this)
        {
            CommandHeader header = new CommandHeader
            {
                ExecuteFn = fn,
                Size = (ushort)sizeof(CommandHeader)
            };


            fixed (byte* dst = &_buffer[_offset])
            {
                Unsafe.WriteUnaligned(dst, header);

                PushTrace(dst);
            }

            _offset += header.Size;
        }

    }



    public void PushCommand<TData>(TData data, delegate*<TData*, void> fn)
        where TData : unmanaged
    {
        lock (this)
        {
            CommandHeader header = new CommandHeader
            {
                ExecuteFn = (void*)(delegate*<void*, void>)&Invoke,
                Size = (ushort)(
                    sizeof(CommandHeader) +
                    sizeof(void*) +   
                    sizeof(TData))
            };


            fixed (byte* dst = &_buffer[_offset])
            {
                byte* ptr = dst;

                Unsafe.WriteUnaligned(ptr, header);
                ptr += sizeof(CommandHeader);

                *(void**)ptr = (void*)fn;
                ptr += sizeof(void*);

                Unsafe.WriteUnaligned(ptr, data);

                PushTrace(dst);
            }

            _offset += header.Size;
        }


        static void Invoke(void* ptr)
        {
            byte* p = (byte*)ptr;

            var fn = (delegate*<TData*, void>)(*(void**)p);

            var dataPtr = (TData*)(p + sizeof(void*));

            fn(dataPtr);
        }
    }


    public void PushCommand<T>(T cmd)
        where T : unmanaged, IDeferredCommand<T>
    {
        lock (this)
        {
            CommandHeader header = new CommandHeader
            {
                ExecuteFn = (void*)(delegate*<void*, void>)&Execute<T>,
                Size = (ushort)(sizeof(CommandHeader) + sizeof(T))
            };

            fixed (byte* dst = &_buffer[_offset])
            {
                Unsafe.WriteUnaligned(dst, header);
                Unsafe.WriteUnaligned(dst + sizeof(CommandHeader), cmd);

                PushTrace(dst);
            }

            _offset += header.Size;
        }

        static void Execute<TCmd>(void* ptr)
            where TCmd : unmanaged, IDeferredCommand<TCmd>
        {
            TCmd.Execute((TCmd*)ptr);
        }
    }







    [Conditional("DEBUG")]
    private unsafe void PushTrace(byte* dst)
    {
        int debugidx = -1;

        if (EngineDebug.DeferredCommandStackTraceStorage)
        {
            _debugOrigins.Add(new StackTrace(true));
            debugidx = _debugOrigins.Count - 1;
        }

        ((CommandHeader*)dst)->DebugIndex = debugidx;

    }








    public bool ContainsCommands()
    {
        lock (this)
            return _offset != 0;
    }





    /// <summary>
    /// Executes all commands and resets.
    /// </summary>

    [DebuggerHidden]
    [StackTraceHidden]

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

                        if (header->Size == sizeof(CommandHeader))
                            ((delegate*<void>)(header->ExecuteFn))();
                        else
                            ((delegate*<void*, void>)(header->ExecuteFn))(cmdPtr);
#if DEBUG
                    }
                    catch (Exception ex)
                    {
                        Throw(header, ex);
                    }
#endif


                    ptr += header->Size;
                }
            }

            Reset();
        }

    }


    private void Throw(CommandHeader* header, Exception ex)
    {
        if (header->DebugIndex != -1)
        {
            var originTrace = _debugOrigins[header->DebugIndex];
            throw new Exception(
                "Deferred command originally submitted from:\n" +
                originTrace.ToClickableSrcLinesString() + "\n\nDeferred command failure:\n" +
                ex,
                ex);
        }

        throw ex;
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



