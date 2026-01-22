

namespace Engine.Core;




public static class RefCountedSetters
{

    /// <summary>
    /// Adds a user to all <see cref="RefCounted"/>s in this collection.
    /// </summary>
    /// <typeparam name="CollectionType"></typeparam>
    /// <param name="arr"></param>
    public static void AddUserToAll<CollectionType>(this CollectionType arr) where CollectionType : notnull, IList<RefCounted>
    {
        for (int i = 0; i < arr.Count; i++)
            arr[i]?.AddUser();
    }


    /// <summary>
    /// Adds a user to all <see cref="RefCounted"/>s in this collection.
    /// </summary>
    /// <typeparam name="KeyType"></typeparam>
    /// <typeparam name="ResType"></typeparam>
    /// <param name="dict"></param>
    public static void AddUserToAll<KeyType, ResType>(this IDictionary<KeyType, ResType> dict) where KeyType : notnull where ResType : RefCounted
    {
        foreach (var v in dict)
            v.Value?.AddUser();
    }


    /// <summary>
    /// Removes a user from all <see cref="RefCounted"/>s in this collection.
    /// </summary>
    /// <typeparam name="CollectionType"></typeparam>
    /// <param name="arr"></param>
    public static void RemoveUserFromAll<CollectionType>(this CollectionType arr) where CollectionType : notnull, IList<RefCounted>
    {
        for (int i = 0; i < arr.Count; i++) 
            arr[i]?.RemoveUser();
    }


    /// <summary>
    /// Removes a user from all <see cref="RefCounted"/>s in this collection.
    /// </summary>
    /// <typeparam name="KeyType"></typeparam>
    /// <typeparam name="ResType"></typeparam>
    /// <param name="dict"></param>
    public static void RemoveUserFromAll<KeyType, ResType>(this IDictionary<KeyType, ResType> dict) where KeyType : notnull where ResType : RefCounted
    {
        foreach (var v in dict)
            v.Value?.RemoveUser();
    }



    /// <summary>
    /// Frees all <see cref="RefCounted"/>s in this collection.
    /// </summary>
    /// <typeparam name="CollectionType"></typeparam>
    /// <param name="arr"></param>
    public static void FreeAll<CollectionType>(this CollectionType arr) where CollectionType : notnull, IList<RefCounted>
    {
        for (int i = 0; i < arr.Count; i++)
            arr[i]?.Free();
    }


    /// <summary>
    /// Frees all <see cref="RefCounted"/>s in this collection.
    /// </summary>
    /// <typeparam name="KeyType"></typeparam>
    /// <typeparam name="ResType"></typeparam>
    /// <param name="dict"></param>
    public static void FreeAll<KeyType, ResType>(this IDictionary<KeyType, ResType> dict) where KeyType : notnull where ResType : RefCounted
    {
        foreach (var v in dict)
            v.Value?.Free();
    }




    /// <summary>
    /// Frees all <see cref="RefCounted"/>s in this collection.
    /// </summary>
    /// <typeparam name="CollectionType"></typeparam>
    /// <param name="arr"></param>
    public static void FreeDeferredAll<CollectionType>(this CollectionType arr) where CollectionType : notnull, IList<RefCounted>
    {
        for (int i = 0; i < arr.Count; i++)
            arr[i]?.FreeDeferred();
    }


    /// <summary>
    /// Frees all <see cref="RefCounted"/>s in this collection.
    /// </summary>
    /// <typeparam name="KeyType"></typeparam>
    /// <typeparam name="ResType"></typeparam>
    /// <param name="dict"></param>
    public static void FreeDeferredAll<KeyType, ResType>(this IDictionary<KeyType, ResType> dict) where KeyType : notnull where ResType : RefCounted
    {
        foreach (var v in dict)
            v.Value?.FreeDeferred();
    }









    /// <summary>
    /// Sets a <see cref="RefCounted"/> value in this collection, calling <see cref="RefCounted.AddUser"/> on the new value and <see cref="RefCounted.RemoveUser"/> on the existing value where applicable.
    /// <br/> Returns true if the value changed.
    /// </summary>
    /// <typeparam name="CollectionType"></typeparam>
    /// <param name="arr"></param>
    /// <param name="idx"></param>
    /// <param name="res"></param>
    /// <returns></returns>
    public static bool AddRefCountedReference<CollectionType>(this CollectionType arr, int idx, RefCounted res) where CollectionType : notnull, IList<RefCounted>
    {
        var ret = ReferenceReplaceLogic(arr[idx], res);
        arr[idx] = ret.res;
        return ret.changed;
    }


    /// <summary>
    /// <inheritdoc cref="AddRefCountedReference{Collection}(Collection, int, RefCounted)"/>
    /// </summary>
    /// <typeparam name="KeyType"></typeparam>
    /// <typeparam name="ResType"></typeparam>
    /// <param name="dict"></param>
    /// <param name="name"></param>
    /// <param name="res"></param>
    /// <returns></returns>
    public static bool AddRefCountedReference<KeyType, ResType>(this IDictionary<KeyType, ResType> dict, KeyType name, ResType res, bool exclusivelySet = false) where KeyType : notnull where ResType : RefCounted
    {
        if (!dict.ContainsKey(name))
        {
            if (exclusivelySet) return false;
            dict[name] = null;
        }

        var ret = ReferenceReplaceLogic(dict[name], res);
        dict[name] = (ResType)ret.res;
        return ret.changed;
    }



    /// <summary>
    /// A <see cref="RefCounted"/> setter that, where applicable, calls <see cref="RefCounted.AddUser"/> on the new value and <see cref="RefCounted.RemoveUser"/> on the existing value.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="originalValue"></param>
    /// <param name="newValue"></param>
    /// <returns></returns>
    public static bool RefCountedSetter<T>(ref T originalValue, T newValue) where T : RefCounted
    {
        var ret = ReferenceReplaceLogic(originalValue, newValue);
        originalValue = (T)ret.res;
        return ret.changed;
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









