


namespace Engine.GameObjects;


using Engine.Attributes;
using Engine.Core;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;


using static Engine.Core.EngineMath;





/// <summary>
/// An object with basic parameters within the game world heirarchy.
/// </summary>
public partial class GameObject : Freeable
{



    /// <summary>
    /// The name of this object. <br/> <b>! ! ! Names are not automatically made unique in any context. ! ! ! </b> 
    /// </summary>
    public string Name
    {
        get => _name;
        set
        {
            if (_name != value)
            {
                if (_name != null) NamedGameObjects.Remove(_name);
                _name = value;
                if (_name != null) NamedGameObjects[_name] = this;
            }
        }
    }

    private string _name;




    public GameObject Parent { get; private set; }


    private List<GameObject> Children = new();


    /// <summary>
    /// Returns an immutable wrapper over the current child list. Adding/removing children may invalidate the integrity of this (see <see cref="CollectionsMarshal.AsSpan{T}(List{T}?)"/>)
    /// </summary>
    /// <returns></returns>
    public ImmutableArray<GameObject> GetChildren() => ImmutableArray.Create(CollectionsMarshal.AsSpan(Children));




    private bool _toplevel = false;

    /// <summary>
    /// Whether the object should inherit transforms from its ancestors or not. <br />
    /// When setting, the existing global transform of the object will be maintained.
    /// </summary>
    public bool TopLevel
    {
        get => _toplevel;
        set
        {
            if (_toplevel != value)
            {
                var gtransform = GlobalTransform;
                _toplevel = value;
                GlobalTransform = gtransform;
            }
        }
    }

    private Transform _transform = Transform.Identity;


    /// <summary>
    /// The world-space transform of the object. This is cached where possible to reduce unnessecary matrix multiplications.
    /// </summary>
    public Transform GlobalTransform
    {
        get
        {
            if (TopLevel) return Transform;

            if (!GlobalTransformDirty) return GlobalTransformCached;

            Transform global = Transform;
            var p = Parent;

            while (p != null)
            {
                var parentTransform = p.Transform;

                global = global * parentTransform;

                if (p.TopLevel) break;
                p = p.Parent;
            }

            GlobalTransformDirty = false;
            GlobalTransformCached = global;

            return global;
        }

        set
        {
            Transform = value;
            if (Parent != null && !TopLevel) Transform *= Parent.GlobalTransform.AffineInverse();
        }
    }


    public Transform Transform
    {
        get => _transform;
        set
        {
            GlobalTransformChanged();
            _transform = value;
        }
    }



    private bool GlobalTransformDirty = true;
    private Transform GlobalTransformCached;



    /// <summary>
    /// Called on this object and its recursive children whenever this object or its ancestors' transform changes.
    /// </summary>
    protected virtual void GlobalTransformChanged()
    {
        GlobalTransformDirty = true;

        OnGlobalTransformChangedEvent.Invoke();

        for (int i = 0; i < Children.Count; i++)
            Children[i].GlobalTransformChanged();
    }


    public readonly ThreadSafeEventAction OnGlobalTransformChangedEvent = new();




    public Vector3 GlobalPosition
    {
        get => GlobalTransform.Origin;
        set => GlobalTransform = GlobalTransform with { Origin = value };
    }

    public Vector3 GlobalRotation
    {
        get => GlobalTransform.GetEuler();
        set
        {
            var gtransform = GlobalTransform;
            var scale = gtransform.Decompose().Scale;
            GlobalTransform = Transform.FromEuler(value).Scaled(scale) with { Origin = gtransform.Origin };
        }
    }

    public Vector3 GlobalRotationDegrees
    {
        get
        {
            var e = GlobalTransform.GetEuler();
            return new Vector3(RadToDeg(e.X), RadToDeg(e.Y), RadToDeg(e.Z));
        }
        set
        {
            var gtransform = GlobalTransform;
            var scale = gtransform.Decompose().Scale;
            GlobalTransform = Transform.FromEuler(new Vector3(DegToRad(value.X), DegToRad(value.Y), DegToRad(value.Z)))
                .Scaled(scale)
                with
            { Origin = gtransform.Origin };
        }
    }

    public Vector3 Position
    {
        get => Transform.Origin;
        set => Transform = Transform with { Origin = value };
    }

    public Vector3 Rotation
    {
        get => Transform.GetEuler();
        set
        {
            var t = Transform;
            var scale = t.Decompose().Scale;
            Transform = Transform.FromEuler(value).Scaled(scale) with { Origin = t.Origin };
        }
    }

    public Vector3 RotationDegrees
    {
        get
        {
            var e = Transform.GetEuler();
            return new Vector3(RadToDeg(e.X), RadToDeg(e.Y), RadToDeg(e.Z));
        }
        set
        {
            var t = Transform;
            var scale = t.Decompose().Scale;
            Transform = Transform.FromEuler(new Vector3(DegToRad(value.X), DegToRad(value.Y), DegToRad(value.Z)))
                .Scaled(scale)
                with
            { Origin = t.Origin };
        }
    }


    public bool IsSceneInstanceRoot;

    public bool Visible = true;



    /// <summary>
    /// Whether this object and all of its ancestors have <see cref="Visible"/> enabled.
    /// </summary>
    /// <returns></returns>
    public bool IsVisibleInTree()
    {
        return check(this);


        bool check(GameObject obj)
        {
            if (obj.Parent != null) return obj.Visible && check(obj.Parent);
            return obj.Visible;
        }
    }


    public bool EnableCameraCulling = true;

    /// <summary>
    /// Whether this object and all of its ancestors have <see cref="EnableCameraCulling"/> enabled.
    /// </summary>
    /// <returns></returns>
    public bool IsEnableCameraCullingInTree()
    {
        return check(this);


        bool check(GameObject obj)
        {
            if (obj.Parent != null) return obj.EnableCameraCulling && check(obj.Parent);
            return obj.EnableCameraCulling;
        }
    }




    public virtual void Loop() { }





    public virtual void Reparented() { }

    protected override void OnFree()
    {
        AllGameObjects.Remove(this);
        NamedGameObjects.Remove(Name);


        if (Parent != null) Parent.RemoveChild(this);

        GameObject[] array = [.. Children];

        for (int i1 = 0; i1 < array.Length; i1++)
            array[i1].Free();

    }






    public virtual void AddChild(GameObject child)
    {
        Children.Add(child);
        child.Parent = this;
        child.Reparented();
    }

    public virtual void RemoveChild(GameObject child)
    {
        Children.Remove(child);
        if (child.Valid)
        {
            child.Parent = null;
            child.Reparented();
        }
    }



    /// <summary>
    /// Returns all children recursively.
    /// </summary>
    /// <param name="includeself"></param>
    /// <returns></returns>
    public List<GameObject> GetChildrenRecursive(bool includeself = false)
    {
        var list = getchildren(this);
        if (includeself) list.Add(this);
        return list;

        List<GameObject> getchildren(GameObject root)
        {
            var children = new List<GameObject>();

            var list1 = root.Children;
            for (int i = 0; i < list1.Count; i++)
            {
                var c = list1[i];
                children = [.. children, .. getchildren(c)];
                children.Add(c);
            }

            return children;
        }
    }




    /// <summary>
    /// Returns the first child of a given name.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="name"></param>
    /// <returns></returns>
    public T FindChildRecursive<T>(string name) where T : GameObject
    {
        for (int i = 0; i < Children.Count; i++)
        {
            GameObject? child = Children[i];
            if (child.Name == name && child is T c) return c;

            if (child.Children.Count != 0)
            {
                var s = child.FindChildRecursive<T>(name);
                if (s != null) return s;
            }
        }

        return null;
    }



    /// <summary>
    /// Recursively finds all children within the group of a given name.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="groupName"></param>
    /// <returns></returns>
    public List<T> FindChildrenInGroupRecursive<T>(string groupName) where T : GameObject
    {
        List<T> final = new();

        for (int i = 0; i < Children.Count; i++)
        {
            GameObject? child = Children[i];
            if (ObjectIsInGroup(child, groupName) && child is T c) final.Add(c);

            if (child.Children.Count != 0) final.AddRange(child.FindChildrenInGroupRecursive<T>(groupName));
        }

        return final;
    }




    /// <summary>
    /// Gets a child via unix-like path.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="path"></param>
    /// <returns></returns>
    public T GetChild<T>(string path) where T : GameObject
    {
        var slash = path.IndexOf('/');


        GameObject get = null;
        if (path == "..") get = Parent;
        else get = GetDirectChild(path);


        if (slash == -1) return (T)get;


        return get.GetChild<T>(path[slash..]);


        T GetDirectChild(string name)
        {

            for (int i = 0; i < Children.Count; i++)
            {
                var child = Children[i];
                if (child.Name == name && child is T cast) return cast;
            }

            return null;
        }
    }


    /// <summary>
    /// Gets a direct child directly via index.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="idx"></param>
    /// <returns></returns>
    public T GetChild<T>(ushort idx) where T : GameObject
    {
        if (Children.Count - 1 < idx) return null;
        return (T)Children[idx];
    }


    /// <summary>
    /// Gets the absolute path to this object.
    /// </summary>
    /// <returns></returns>
    public string GetPath()
    {
        string p = Name;
        var obj = Parent;
        while (obj.Parent != null) p = $"{obj.Name}/{p}";

        return p;
    }



    /// <summary>
    /// The base initialization method for an object.
    /// </summary>

    [GameObjectInitMethod]
    public void Init(string Name = default, Matrix4x4 Transform = default)
    {
        this.Name = Name;
        this.Transform = Transform == default ? EngineMath.Transform.Identity : Transform;

        AllGameObjects.Add(this);

        if (Name != null)
            NamedGameObjects.TryAdd(Name, this);


        FinalInit();
    }



    /// <summary>
    /// Runs at the end of <see cref="Init"/>.
    /// </summary>

    [PartialDefaultReturn]
    protected virtual partial void FinalInit();












    public static readonly List<GameObject> AllGameObjects = new();

    public static readonly List<DrawObject> AllDrawableObjects = new();

    /// <summary>
    /// Contains every <see cref="GameObject"/> with a name. <br/> <b>! ! ! Objects will replace each other if they have the same name. This is only reliably useful for objects you know have unique names. ! ! !</b>
    /// </summary>
    public static readonly Dictionary<string, GameObject> NamedGameObjects = new();

    private static readonly Dictionary<string, HashSet<GameObject>> ObjectGroups = new();




    /// <summary>
    /// Adds this object to a global group and creates that group if it doesn't exist.
    /// </summary>
    /// <param name="obj"></param>
    /// <param name="groupName"></param>
    public static void AddToGroup(GameObject obj, string groupName)
    {
        if (obj.Valid)
        {
            if (!ObjectGroups.TryGetValue(groupName, out var v)) v = ObjectGroups[groupName] = new();

            v.Add(obj);

            CleanGroup(groupName);
        }
    }



    /// <summary>
    /// Removes this object from a global group if that group exists and that object is within it.
    /// </summary>
    /// <param name="obj"></param>
    /// <param name="groupName"></param>
    public static void RemoveFromGroup(GameObject obj, string groupName)
    {
        if (ObjectGroups.TryGetValue(groupName, out var v))
        {
            v.Remove(obj);

            CleanGroup(groupName);
        }
    }


    /// <summary>
    /// Returns a hashset of objects within a group if that group exists, otherwise null.
    /// </summary>
    /// <param name="groupName"></param>
    /// <returns></returns>
    public static ImmutableArray<GameObject>? GetGroupObjects(string groupName)
    {
        CleanGroup(groupName);
        if (ObjectGroups.TryGetValue(groupName, out var v)) return ImmutableArray.ToImmutableArray(v);
        return null;
    }


    public static bool ObjectIsInGroup(GameObject obj, string groupName)
        => ObjectGroups[groupName].Contains(obj);



    /// <summary>
    /// If a group exists, remove any stale/invalid objects, and remove the group entirely if empty.
    /// </summary>
    /// <param name="groupName"></param>
    private static void CleanGroup(string groupName)
    {
        if (ObjectGroups.TryGetValue(groupName, out var v))
        {
            if (v.Count == 0)
            {
                ObjectGroups.Remove(groupName);
                return;
            }

            TempGroup.Clear();

            foreach (var obj in v)
            {
                if (obj != null && obj.Valid)
                    TempGroup.Add(obj);
            }

            if (TempGroup.Count != 0) ObjectGroups[groupName] = TempGroup;
            else ObjectGroups.Remove(groupName);
        }
    }

    private static HashSet<GameObject> TempGroup = new();






}
