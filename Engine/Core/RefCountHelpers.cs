

using System.Collections;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using static Engine.Core.References;

namespace Engine.Core;




public static class RefCountCollections
{


    /// <summary>
    /// Wraps an array of <typeparamref name="T"/> and calls <see cref="RefCounted.AddUser"/> or <see cref="RefCounted.RemoveUser"/> respectively when values are changed.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    [CollectionBuilder(typeof(RefCountCollections), nameof(CreateRefCountedArray))]
    public class RefCountedArray<T> : Freeable, IEnumerable<T>
        where T : RefCounted
    {

        public RefCountedArray(int Length)
        {
            _arr = new T[Length];
        }

        public RefCountedArray(T[] arr)
        {
            for (int i = 0; i < arr.Length; i++)
                arr[i]?.AddUser();

            _arr = arr.ToArray();
        }



        private T[] _arr;

        public ReadOnlySpan<T> AsSpan() => _arr.AsSpan();


        public T this[int idx]
        {
            get => _arr[idx];
            set
            {
                if (ReferenceReplaceLogic(_arr[idx], value))
                {
                    _arr[idx] = value;
                    OnValueChanged.Invoke((idx, value));
                }
            }
        }

        public readonly ThreadSafeEventAction<(int idx, T newvalue)> OnValueChanged = new();

        public IEnumerator<T> GetEnumerator()
            => ((IEnumerable<T>)_arr).GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        protected override void OnFree()
        {
            for (int i = 0; i < _arr.Length; i++)
                _arr[i]?.RemoveUser();

            _arr = null!;
        }


        public static explicit operator RefCountedArray<T>(T[] arr)
            => new(arr);

    }



    public static RefCountedArray<T> CreateRefCountedArray<T>(ReadOnlySpan<T> items)
            where T : RefCounted
    {
        var arr = new RefCountedArray<T>(items.Length);

        for (int i = 0; i < items.Length; i++)
            arr[i] = items[i];

        return arr;
    }




    /// <summary>
    /// Wraps a dictionary of <typeparamref name="TValue"/> values and calls <see cref="RefCounted.AddUser"/> or <see cref="RefCounted.RemoveUser"/> respectively when values are changed.
    /// </summary>
    /// <typeparam name="TKey"></typeparam>
    /// <typeparam name="TValue"></typeparam>
    public class RefCountedDictionary<TKey, TValue> : Freeable, IDictionary<TKey, TValue> where TKey : class where TValue : RefCounted
    {
        public RefCountedDictionary(int Length) 
            => _dict = new(Length);

        public RefCountedDictionary()
            => _dict = new();

        public RefCountedDictionary(IDictionary<TKey, TValue> from)
        {
            _dict = new(from.Count);

            foreach (var kv in from)
            {
                _dict.Add(kv.Key, kv.Value);

                if (_dict.Count <= UnmanagedKeyValueCollection<byte, byte>.SizeLimit)
                    _unmanaged[kv.Key.GetRef()] = kv.Value.GetRef();

                kv.Value?.AddUser();
            }

        }



        public static explicit operator RefCountedDictionary<TKey, TValue>(Dictionary<TKey, TValue> from) 
            => new(from);




        private Dictionary<TKey, TValue> _dict;


        public TValue this[TKey idx]
        {
            get => _dict[idx];
            set
            {
                if (!_dict.TryGetValue(idx, out var get))  //not present
                {
                    _unmanaged.Add(idx.GetRef(), value.GetRef());

                    _dict[idx] = value;
                    OnValueChanged.Invoke((idx, value));
                }


                else if (ReferenceReplaceLogic(get, value))  //present but changed
                {
                    _unmanaged.Set(idx.GetRef(), value.GetRef(), addNew: false);

                    _dict[idx] = value;
                    OnValueChanged.Invoke((idx, value));
                }
            }
        }


        public readonly ThreadSafeEventAction<(TKey idx, TValue newvalue)> OnValueChanged = new();

        protected override void OnFree()
        {
            foreach (var val in _dict.Values) 
                val.RemoveUser();

            _dict = null;
            _unmanaged = default;
        }



        private UnmanagedKeyValueCollection<WeakObjRef<TKey>, WeakObjRef<TValue>> _unmanaged = new();
        public ref UnmanagedKeyValueCollection<WeakObjRef<TKey>, WeakObjRef<TValue>> AsUnmanaged()
        {
#if DEBUG
            if (_dict.Count > UnmanagedKeyValueCollection<byte, byte>.SizeLimit) throw new InvalidCastException($"Collection is too big to be represented by {typeof(UnmanagedKeyValueCollection<WeakObjRef<TKey>, WeakObjRef<TValue>>).Name}");
#endif

            return ref _unmanaged;
        }




        public ICollection<TKey> Keys => ((IDictionary<TKey, TValue>)_dict).Keys;

        public ICollection<TValue> Values => ((IDictionary<TKey, TValue>)_dict).Values;

        public int Count => ((ICollection<KeyValuePair<TKey, TValue>>)_dict).Count;

        public bool IsReadOnly => ((ICollection<KeyValuePair<TKey, TValue>>)_dict).IsReadOnly;



        public void Add(TKey key, TValue value) 
            => this[key] = value;



        public bool ContainsKey(TKey key)
        {
            return ((IDictionary<TKey, TValue>)_dict).ContainsKey(key);
        }

        public bool Remove(TKey key)
        {
            if (_dict.TryGetValue(key, out var get))
            {
                _unmanaged.TryRemove(key.GetRef());

                get.RemoveUser();
                return ((IDictionary<TKey, TValue>)_dict).Remove(key);
            }
            return false;
        }




        public bool TryGetValue(TKey key, [MaybeNullWhen(false)] out TValue value)
        {
            return ((IDictionary<TKey, TValue>)_dict).TryGetValue(key, out value);
        }

        public void Add(KeyValuePair<TKey, TValue> item) 
            => Add(item.Key, item.Value);



        public void Clear()
        {
            foreach (var k in _dict.Keys) 
                Remove(k);
        }


        public bool Contains(KeyValuePair<TKey, TValue> item)
        {
            return ((ICollection<KeyValuePair<TKey, TValue>>)_dict).Contains(item);
        }

        public void CopyTo(KeyValuePair<TKey, TValue>[] array, int arrayIndex)
        {
            ((ICollection<KeyValuePair<TKey, TValue>>)_dict).CopyTo(array, arrayIndex);
        }

        public bool Remove(KeyValuePair<TKey, TValue> item) 
            => Remove(item.Key);


        public IEnumerator<KeyValuePair<TKey, TValue>> GetEnumerator()
        {
            return ((IEnumerable<KeyValuePair<TKey, TValue>>)_dict).GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return ((IEnumerable)_dict).GetEnumerator();
        }
    }










    private static bool ReferenceReplaceLogic<T>(T? originalValue, T? newValue) where T : RefCounted
    {
        bool changed = false;

        if (originalValue != newValue)
        {
            newValue?.AddUser();
            originalValue?.RemoveUser();

            changed = true;
        }

        return changed;
    }






}









