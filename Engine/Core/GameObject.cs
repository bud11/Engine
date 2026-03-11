


namespace Engine.Core;


using Engine.Attributes;
using Engine.GameResources;
using System.Collections.Immutable;
using System.Numerics;
using System.Runtime.InteropServices;


using static Engine.Core.EngineMath;





/// <summary>
/// An object within the game world/heirarchy.
/// <br/> 
/// <br/> <see cref="GameObject"/> types must be public and expose a parameterless constructor.
/// <br/>
/// <br/> To allow fields/properties to be set via data, see <see cref="BinarySerializableTypeAttribute"/> and <see cref="DataValueAttribute"/>.
/// </summary>
public partial class GameObject : Freeable,
    ISerializeOverrider<uint>
{



    static uint ISerializeOverrider<uint>.Serialize(object instance, object context)
        => throw new NotImplementedException();

    static object ISerializeOverrider<uint>.Deserialize(uint data, object context)
        => ((SceneResource.SceneBinaryDeserializationContext)context).Objects[(int)data];










    [DataValue]
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




    [DataValue]
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


    [DataValue]
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


    [DataValue]
    public bool Visible = true;




    /// <summary>
    /// Whether this object and all of its ancestors have <see cref="Visible"/> enabled.
    /// </summary>
    /// <returns></returns>
    public bool IsVisibleInTree()
    {
        return check(this);


        static bool check(GameObject obj)
        {
            if (obj.Parent != null) return obj.Visible && check(obj.Parent);
            return obj.Visible;
        }
    }




    [DataValue]
    public bool EnableCameraCulling = true;

    /// <summary>
    /// Whether this object and all of its ancestors have <see cref="EnableCameraCulling"/> enabled.
    /// </summary>
    /// <returns></returns>
    public bool IsEnableCameraCullingInTree()
    {
        return check(this);


        static bool check(GameObject obj)
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

        static List<GameObject> getchildren(GameObject root)
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
    /// Gets another object via unix-like path.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="path"></param>
    /// <returns></returns>
    public T GetObject<T>(string path) where T : GameObject
    {
        var slash = path.IndexOf('/');


        GameObject get = null;
        if (path == "..") get = Parent;
        else get = GetDirectChild(path);


        if (slash == -1) return (T)get;


        return get.GetObject<T>(path[slash..]);


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
    /// The base initialization method for an object. Must be called for the object to function correctly.
    /// </summary>
    public virtual void Init()
    {
        AllGameObjects.Add(this);

        if (Name != null)
            NamedGameObjects.TryAdd(Name, this);
    }






    public static readonly List<GameObject> AllGameObjects = new();

    /// <summary>
    /// Contains every <see cref="GameObject"/> with a name. <br/> <b>! ! ! Objects will replace each other if they have the same name. This is only reliably useful for objects with globally unique names. ! ! !</b>
    /// </summary>
    public static readonly Dictionary<string, GameObject> NamedGameObjects = new();




}
