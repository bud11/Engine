

using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace Engine.Core;




public static class RefCountCollections
{

    [CollectionBuilder(typeof(RefCountCollections), nameof(CreateRefCountedArray))]
    public class RefCountedArray<T>(int Length) : RefCounted, IEnumerable<T>
        where T : RefCounted
    {
        private T[] array = new T[Length];

        public T this[int idx]
        {
            get => array[idx];
            set
            {
                var ret = ReferenceReplaceLogic(array[idx], value);

                if (ret.changed)
                {
                    array[idx] = value;
                    OnValueChanged.Invoke((idx, value));
                }
            }
        }

        public readonly ThreadSafeEventAction<(int idx, T newvalue)> OnValueChanged = new();

        public IEnumerator<T> GetEnumerator()
            => ((IEnumerable<T>)array).GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        protected override void OnFree()
        {
            for (int i = 0; i < Length; i++)
                array[i]?.RemoveUser();

            array = null!;
        }
    }

    public static RefCountedArray<T> CreateRefCountedArray<T>(ReadOnlySpan<T> items)
            where T : RefCounted
    {
        var arr = new RefCountedArray<T>(items.Length);

        for (int i = 0; i < items.Length; i++)
            arr[i] = items[i];

        return arr;
    }



    public class RefCountedDictionary<TKey, TValue> : RefCounted, IDictionary<TKey, TValue> where TKey : notnull where TValue : RefCounted
    {
        public RefCountedDictionary(int Length) 
            => dict = new(Length);

        public RefCountedDictionary()
            => dict = new();


        private readonly Dictionary<TKey, TValue> dict;


        public TValue this[TKey idx]
        {
            get => dict[idx];
            set
            {
                dict.TryGetValue(idx, out var get);

                var (res, changed) = ReferenceReplaceLogic(get, value);

                if (changed)
                {
                    dict[idx] = value;
                    OnValueChanged.Invoke((idx, value));
                }
            }
        }

        public readonly ThreadSafeEventAction<(TKey idx, TValue newvalue)> OnValueChanged = new();

        protected override void OnFree()
        {
            foreach (var val in dict.Values) 
                val.RemoveUser();
        }




        public ICollection<TKey> Keys => ((IDictionary<TKey, TValue>)dict).Keys;

        public ICollection<TValue> Values => ((IDictionary<TKey, TValue>)dict).Values;

        public int Count => ((ICollection<KeyValuePair<TKey, TValue>>)dict).Count;

        public bool IsReadOnly => ((ICollection<KeyValuePair<TKey, TValue>>)dict).IsReadOnly;



        public void Add(TKey key, TValue value) 
            => this[key] = value;



        public bool ContainsKey(TKey key)
        {
            return ((IDictionary<TKey, TValue>)dict).ContainsKey(key);
        }

        public bool Remove(TKey key)
        {
            if (dict.TryGetValue(key, out var get))
            {
                get.RemoveUser();
                return ((IDictionary<TKey, TValue>)dict).Remove(key);
            }
            return false;
        }




        public bool TryGetValue(TKey key, [MaybeNullWhen(false)] out TValue value)
        {
            return ((IDictionary<TKey, TValue>)dict).TryGetValue(key, out value);
        }

        public void Add(KeyValuePair<TKey, TValue> item) 
            => Add(item.Key, item.Value);



        public void Clear()
        {
            foreach (var k in dict.Keys) 
                dict.Remove(k);
        }

        public bool Contains(KeyValuePair<TKey, TValue> item)
        {
            return ((ICollection<KeyValuePair<TKey, TValue>>)dict).Contains(item);
        }

        public void CopyTo(KeyValuePair<TKey, TValue>[] array, int arrayIndex)
        {
            ((ICollection<KeyValuePair<TKey, TValue>>)dict).CopyTo(array, arrayIndex);
        }

        public bool Remove(KeyValuePair<TKey, TValue> item) 
            => Remove(item.Key);


        public IEnumerator<KeyValuePair<TKey, TValue>> GetEnumerator()
        {
            return ((IEnumerable<KeyValuePair<TKey, TValue>>)dict).GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return ((IEnumerable)dict).GetEnumerator();
        }
    }





    private static (RefCounted res, bool changed) ReferenceReplaceLogic(RefCounted originalValue, RefCounted newValue)
    {
        bool changed = false;

        if (originalValue != newValue)
        {
            if (newValue != null) newValue.AddUser();
            if (originalValue != null) originalValue.RemoveUser();

            changed = true;
        }
        return (newValue, changed);
    }


}









