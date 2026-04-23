
namespace Engine.Core;

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


#if DEBUG
using Engine.Stripped;
#endif






/// <summary>
/// A base for any object with a lifetime.
/// </summary>
public abstract class Freeable
{


    /// <summary>
    /// A self weak reference, stored as an optimization. Used by <see cref="References.GetWeakRef{T}(T, bool)"/>.
    /// </summary>
    public readonly WeakObjRef SelfRef;

    public Freeable()
    {
        SelfRef = this.GetWeakRef(false);
    }

    public bool Valid { get; private set; } = true;
    



    public void Free()
    {
        lock (this)
        {
            if (Valid)
            {
                GC.SuppressFinalize(this);

                Valid = false;
                OnFreeEvent.Invoke();
                OnFree();
            }
        }
    }

    ~Freeable() => Free();



    protected abstract void OnFree();

    public readonly ThreadSafeEventAction OnFreeEvent = new();

}



/// <summary>
/// Faciliates obtaining a canonical substitute/fallback/placeholder instance of <typeparamref name="TSelf"/>, should a required instance be invalid or missing.
/// </summary>
/// <typeparam name="TSelf"></typeparam>
public interface IPlaceholderProvider<TSelf> where TSelf : class, IPlaceholderProvider<TSelf>
{
    public static abstract TSelf GetPlaceholder();
    public bool IsPlaceholder { get; }
}


/// <summary>
/// Faciliates obtaining a substitute/fallback/placeholder instance of <typeparamref name="TSelf"/>, according to a specification/minimum requirement encoded into <typeparamref name="TSpec"/>, should a required instance be invalid or missing.
/// </summary>
/// <typeparam name="TSelf"></typeparam>
public interface IPlaceholderProvider<TSelf, TSpec> where TSelf : class, IPlaceholderProvider<TSelf, TSpec>
{
    public static abstract TSelf GetPlaceholder(TSpec? requirement);
    public bool IsPlaceholder { get; }
}








/// <summary>
/// A disposable wrapper over <see cref="byte[]"/> (simply sets own reference to <paramref name="byteArray"/> to null upon disposal to allow GC).
/// </summary>
public sealed class Bytes(byte[] byteArray) : IDisposable
{
    public byte[] ByteArray = byteArray;
    public void Dispose() => ByteArray = null;
}








public static class MiscExtensions
{







    /// <summary>
    /// Performant alternative to <see cref="Enum.HasFlag(Enum)"/>.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="self"></param>
    /// <param name="value"></param>
    /// <returns></returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool EnumHasValue<T>(this T self, in T value) where T : struct, Enum
    {
        ulong a = EnumToUInt64(self);
        ulong b = EnumToUInt64(value);
        return (a & b) != 0;
    }

    /// <summary>
    /// Equivalent to multiple calls to <see cref="EnumHasValue{T}(T, in T)"/>.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="self"></param>
    /// <param name="values"></param>
    /// <returns></returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool EnumHasValues<T>(this T self, ReadOnlySpan<T> values) where T : struct, Enum
    {
        for (int i = 0; i < values.Length; i++)
            if (!EnumHasValue(self, in values[i])) return false;

        return true;
    }



    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ulong EnumToUInt64<T>(T value) where T : struct, Enum
    {
        TypeCode typeCode = Type.GetTypeCode(Enum.GetUnderlyingType(typeof(T)));
        return typeCode switch
        {
            TypeCode.Byte => Unsafe.As<T, byte>(ref value),
            TypeCode.SByte => (ulong)Unsafe.As<T, sbyte>(ref value),
            TypeCode.Int16 => (ulong)Unsafe.As<T, short>(ref value),
            TypeCode.UInt16 => Unsafe.As<T, ushort>(ref value),
            TypeCode.Int32 => (ulong)Unsafe.As<T, int>(ref value),
            TypeCode.UInt32 => Unsafe.As<T, uint>(ref value),
            TypeCode.Int64 => (ulong)Unsafe.As<T, long>(ref value),
            TypeCode.UInt64 => Unsafe.As<T, ulong>(ref value),
            _ => throw new InvalidOperationException()
        };
    }






    /// <summary>
    /// Aligns <paramref name="value"/> to <paramref name="alignment"/>, where <paramref name="alignment"/> must be a power of 2.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="value"></param>
    /// <param name="alignment"></param>
    /// <returns></returns>
    /// <exception cref="ArgumentException"></exception>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static T Align<T>(this T value, T alignment)
        where T : IBinaryInteger<T>
    {
#if DEBUG
        if (!T.IsPow2(alignment))
            throw new ArgumentException("Alignment must be a power of two");
#endif

        return (value + (alignment - T.One)) & ~(alignment - T.One);
    }









    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector4 ToVector4(this Color Col)
        => new Vector4(Col.R, Col.G, Col.B, Col.A)/255f;



    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Color ToColor(this Vector4 Col) 
        => Color.FromArgb((int)(Col.W * 255), (int)(Col.X * 255), (int)(Col.Y * 255), (int)(Col.Z * 255));


}












/// <summary>
/// A small unmanaged dictionary-style collection that can be stack allocated. Optimized for being frequently copied and mutated. See <see cref="SizeLimit"/>.
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




    public void Set(in TKey key, in TValue value, bool addNew = true)
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

        if (!addNew) return;

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
    public readonly UnmanagedKeyValueCollection<TKey, TValue> Combine(UnmanagedKeyValueCollection<TKey, TValue> b)
    {
        if (Count == 0 && b.Count == 0) return default;
        if (Count == 0) return b;
        if (b.Count == 0) return this;


        var result = this;

        for (int i = 0; i < b.Count; i++)
        {
            ref var kv = ref b.KeyValuePairs[i];
            result.Set(kv.Key, kv.Value);
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
    /// Creates a dictionary optimized for unmanaged comparisons. Relies on keys being exactly equal in memory.
    /// </summary>
    /// <typeparam name="Key"></typeparam>
    /// <typeparam name="Value"></typeparam>
    /// <returns></returns>
    public static Dictionary<Key, Value> CreateUnsafeStructKeyComparisonDictionary<Key, Value>() where Key : unmanaged 
        => new(new UnmanagedStructComparer<Key>());



    public unsafe sealed class UnmanagedStructComparer<T> : IEqualityComparer<T>
        where T : unmanaged
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public unsafe bool Equals(T x, T y)
        {
            byte* a = (byte*)&x;
            byte* b = (byte*)&y;

            int size = sizeof(T);

            for (int i = 0; i < size; i++)
            {
                if (a[i] != b[i])
                    return false;
            }

            return true;
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public unsafe int GetHashCode(T value)
        {
            byte* data = (byte*)&value;

            uint hash = 2166136261u;
            int len = sizeof(T);

            int i = 0;
            for (; i + 8 <= len; i += 8)
            {
                ulong chunk = *(ulong*)(data + i);
                hash ^= (uint)chunk;
                hash *= 16777619u;

                hash ^= (uint)(chunk >> 32);
                hash *= 16777619u;
            }

            for (; i < len; i++)
            {
                hash ^= data[i];
                hash *= 16777619u;
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
/// Wraps a rented array originating from <see cref="ArrayPool{T}.Rent(int)"/>.
/// </summary>
/// <typeparam name="T"></typeparam>
public struct ArrayFromPool<T> : IDisposable
{

    /// <param name="array"></param>
    /// <param name="length"></param>
    private ArrayFromPool(T[] array, int length)
    {
        Array = array;
        Length = length;
    }



    public static ArrayFromPool<T> Rent(int length)
    {
        var arr = ArrayPool<T>.Shared.Rent(length);
        System.Array.Clear(arr, 0, length);

        return new ArrayFromPool<T>(arr, length);
    }

    public static ArrayFromPool<T> RentClone(ReadOnlySpan<T> span)
    {
        var arr = ArrayPool<T>.Shared.Rent(span.Length);
        span.CopyTo(arr);

        return new ArrayFromPool<T>(arr, span.Length);
    }




    /// <summary>
    /// The actual underlying array, rented from the pool.
    /// </summary>
    public readonly T[] Array;

    /// <summary>
    /// The logical length. May be smaller than <see cref="Array"/>'s actual length.
    /// </summary>
    public int Length;


    public readonly T this[int idx]
    {
        get
        {
#if DEBUG
            if ((uint)idx >= (uint)Length)
                throw new IndexOutOfRangeException();
#endif

            return Array[idx];
        }
        set
        {
#if DEBUG
            if ((uint)idx >= (uint)Length)
                throw new IndexOutOfRangeException();
#endif

            Array[idx] = value;
        }
    }


    public static implicit operator Span<T>(ArrayFromPool<T> a) => new(a.Array, 0, a.Length);
    public static implicit operator ReadOnlySpan<T>(ArrayFromPool<T> a) => new(a.Array, 0, a.Length);


#if DEBUG
    private bool Returned = false;

#endif

    public void Return()
    {

#if DEBUG
        if (Returned) throw new Exception("already returned");
#endif

        ArrayPool<T>.Shared.Return(Array);

#if DEBUG
        Returned = true;
#endif
    }

    public void Dispose() => Return();
}







/// <summary>
/// A generic temporary unmanaged arena heap allocator. Grows in blocks. Content is invalidated on calls to <see cref="Reset"/> or <see cref="Shrink"/>.
/// </summary>
public unsafe class DynamicUnmanagedHeapAllocator
{
    public unsafe readonly struct Block(byte* ptr, int size, int offset)
    {
        public readonly byte* Ptr = ptr;
        public readonly int Size = size;
        public readonly int Offset = offset;
    }

    private readonly List<Block> _blocks = new();
    private readonly int _unitSize;
    private readonly object _lock = new();


    public DynamicUnmanagedHeapAllocator(int minUnitSize = 512_000)
    {
        if (minUnitSize <= 0) throw new ArgumentException(nameof(minUnitSize));
        _unitSize = minUnitSize;

        _blocks.Add(new Block
        (
            (byte*)Marshal.AllocHGlobal(minUnitSize),
            minUnitSize,
            0
        ));
    }



    public byte* Alloc(int bytes)
    {
        if (bytes <= 0) throw new ArgumentException(nameof(bytes));

        lock (_lock)
        {
            int lastIndex = _blocks.Count - 1;
            var current = _blocks[lastIndex]; 

            int alignedOffset = current.Offset.Align(16);


            if (alignedOffset + bytes > current.Size)
            {
                int newSize = Math.Max(bytes, _unitSize);
                _blocks.Add(new Block
                (
                    (byte*)Marshal.AllocHGlobal(newSize),
                    newSize,
                    0
                ));
                lastIndex = _blocks.Count - 1;
                alignedOffset = 0;
                current = _blocks[lastIndex];
            }

            byte* ptr = current.Ptr + alignedOffset;


            _blocks[lastIndex] = new Block
            (
                current.Ptr,
                current.Size,
                alignedOffset + bytes
            );

            return ptr;
        }
    }



    public void Reset()
    {
        lock (_lock)
        {
            for (int i = 1; i < _blocks.Count; i++)
                Marshal.FreeHGlobal((nint)_blocks[i].Ptr);


            _blocks.RemoveRange(1, _blocks.Count - 1);
            _blocks[0] = new Block
            (
                _blocks[0].Ptr,
                _blocks[0].Size,
                0
            );
        }
    }



    public void Shrink()
    {
        lock (_lock)
        {
            Reset();

            var first = _blocks[0];
            if (first.Size == _unitSize) return;


            Marshal.FreeHGlobal((nint)first.Ptr);
            first = new Block
            (
                (byte*)Marshal.AllocHGlobal(_unitSize),
                _unitSize,
                0
            );
        }
    }


    public void Free()
    {
        lock (_lock)
        {
            foreach (var b in _blocks)
                Marshal.FreeHGlobal((nint)b.Ptr);
            _blocks.Clear();
        }
    }



    /// <summary>
    /// Returns a span of the current blocks.
    /// </summary>
    /// <returns></returns>
    public ReadOnlySpan<Block> GetCurrentBlocks()
    {
        lock (_lock)
            return CollectionsMarshal.AsSpan(_blocks);
    }


    /// <summary>
    /// Returns true if this contains anything.
    /// </summary>
    /// <returns></returns>
    public bool HasContent()
    {
        lock (_lock)
            return _blocks[0].Offset != 0;
    }
}







/// <summary>
/// Represents a range to be written. Doesn't own or directly store data.
/// </summary>
public unsafe readonly struct WriteRange(uint offsetIntoDst, uint lengthOfSrc, void* src)
{
    public readonly uint OffsetIntoDst = offsetIntoDst;
    public readonly uint LengthOfSrc = lengthOfSrc;
    public readonly byte* Src = (byte*)src;
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
        public uint Size;

#if DEBUG
        public int DebugIndex;
#endif
    }




    private readonly DynamicUnmanagedHeapAllocator allocator = new DynamicUnmanagedHeapAllocator(); 



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
                Size = (uint)sizeof(CommandHeader).Align(16)
            };


            var dst = allocator.Alloc((int)header.Size);
            Unsafe.WriteUnaligned(dst, header);
            PushTrace(dst);
        }

    }



    public void PushCommand<TData>(TData data, delegate*<TData*, void> fn)
        where TData : unmanaged
    {
        lock (this)
        {
            CommandHeader header = new CommandHeader
            {
                ExecuteFn = (delegate*<void*, void>)&Invoke,
                Size = (uint)(
                    sizeof(CommandHeader) +
                    sizeof(void*) +   
                    sizeof(TData)).Align(16)
            };



            var dst = allocator.Alloc((int)header.Size);

            byte* ptr = dst;

            Unsafe.WriteUnaligned(ptr, header);
            ptr += sizeof(CommandHeader);

            *(void**)ptr = fn;
            ptr += sizeof(void*);

            Unsafe.WriteUnaligned(ptr, data);

            PushTrace(dst);
        }


        [DebuggerHidden]
        [StackTraceHidden]
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
                ExecuteFn = (delegate*<void*, void>)&Execute<T>,
                Size = (uint)(sizeof(CommandHeader) + sizeof(T)).Align(16)
            };
            

            var dst = allocator.Alloc((int)header.Size);

            Unsafe.WriteUnaligned(dst, header);
            Unsafe.WriteUnaligned(dst + sizeof(CommandHeader), cmd);

            PushTrace(dst);
        }


        [DebuggerHidden]
        [StackTraceHidden]
        static void Execute<TCmd>(void* ptr)
            where TCmd : unmanaged, IDeferredCommand<TCmd>
        {
            TCmd.Execute((TCmd*)ptr);
        }
    }










    [Conditional("DEBUG")]
    private unsafe void PushTrace(byte* dst)
    {
#if DEBUG

        int debugidx = -1;

        if (EngineDebug.DeferredCommandStackTraceStorage)
        {
            _debugOrigins.Add(new StackTrace(true));
            debugidx = _debugOrigins.Count - 1;
        }

        ((CommandHeader*)dst)->DebugIndex = debugidx;
#endif
    }








    public bool ContainsCommands()
    {
        lock (this)
            return allocator.HasContent();
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

            var blocks = allocator.GetCurrentBlocks();


            for (int b = 0; b < blocks.Length; b++)
            {
                var block = blocks[b];
                byte* ptr = block.Ptr;
                byte* end = block.Ptr + block.Offset;

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


    [Conditional("DEBUG")]
    private void Throw(CommandHeader* header, Exception ex)
    {
#if DEBUG

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

#endif
    }






    /// <summary>
    /// Resets the offset, effectively discarding all commands.
    /// </summary>
    public void Reset()
    {
        lock (this)
        {
            allocator.Reset();
#if DEBUG
            _debugOrigins.Clear();
#endif
        }

    }

}



