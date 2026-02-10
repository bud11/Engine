


namespace Engine.GameResources;



using Engine.Attributes;
using Engine.Core;
using System.Collections.Immutable;

using static Engine.Core.Parsing;


#if DEBUG
using System.Reflection;
using System.Text.Json;
#endif











/// <summary>
/// Defines a scene of objects and resources. See <see cref="GameObjectInitMethodAttribute"/> for making <see cref="GameObject"/> types compatible with scene instantiation.
/// <br /> A scene can refer to external resources and/or embed them directly, and scenes can reference other scenes.
/// <br /> Scenes themselves can also own resources and buffers, which may be useful for something like lightmapping textures+buffers for example.
/// </summary>
/// 


[FileExtensionAssociation(".scn")]
public partial class SceneResource : GameResource
{



    private class SceneObjectGenData
    {

        public ushort TypeID;

        public Dictionary<string, object> Args;

        public List<SceneObjectGenData> Children;

        public SceneResource SelfSceneInstance;  //if this object is pointing to another scene..
        public SceneObjectGenData OriginalGenData;


        public uint SelfIndex;

    }


    private unsafe record struct SceneObjectGroup(string Name, uint[] ObjectIdxs);








    private readonly ImmutableArray<GameResource> SceneOwnedReferences;

    private readonly SceneObjectGenData SceneRootObject;
    private readonly SceneObjectGenData[] SceneObjects;
    private readonly SceneObjectGroup[] SceneObjectGroups;


    private SceneResource(GameResource[] References, SceneObjectGenData[] Objects, SceneObjectGenData RootObject, SceneObjectGroup[] Groups, string Key) : base(Key)
    {
        SceneRootObject = RootObject;
        SceneObjects = Objects;

        SceneOwnedReferences = ImmutableArray.Create(References);

        SceneObjectGroups = Groups;
    }






#if DEBUG


    
    public static new async Task<byte[]> ConvertToFinalAssetBytes(byte[] bytes, string filePath)
    {


        List<byte> final = new();


        var dict = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(bytes, new JsonSerializerOptions() { PropertyNameCaseInsensitive = true });



        if (dict.TryGetValue("resources", out var resget))
            await Parsing.WriteResourceBytes(final, JsonSerializer.Deserialize<JsonElement[]>(resget), filePath);

        else final.AddRange(BitConverter.GetBytes(0u));





        //write groups

        var groups = dict["groups"];

        final.AddRange(BitConverter.GetBytes((uint)groups.GetArrayLength()));

        foreach (JsonElement group in groups.EnumerateArray())
        {
            var name = group.GetProperty("name").GetString();
            var objIdxs = JsonSerializer.Deserialize<uint[]>(group.GetProperty("objectIdxs"));

            final.AddRange(Parsing.GetUintLengthPrefixedUTF8StringAsBytes(name));
            final.AddRange(BitConverter.GetBytes((uint)objIdxs.Length));

            foreach (var idx in objIdxs)
                final.AddRange(BitConverter.GetBytes(idx));
        }



        //write objects

        var objects = dict["objects"];

        final.AddRange(BitConverter.GetBytes((uint)objects.GetArrayLength()));

        foreach (var obj in objects.EnumerateArray())
        {

            //if this is an instance, write scene resource index, otherwise max value

            if (obj.TryGetProperty("instanceOfScene", out var sceneInstanceGet))
                final.AddRange(BitConverter.GetBytes(sceneInstanceGet.GetUInt32()));
            else
                final.AddRange(BitConverter.GetBytes(uint.MaxValue));



            //if this is a child of another object, write parent object index, otherwise max value   (ordering therefore relies on the order in which the children appeared within the json array)
            //there needs to be one object without a parent (the root object)

            if (obj.TryGetProperty("parent", out var parentget))
                final.AddRange(BitConverter.GetBytes(parentget.GetUInt32()));
            else
                final.AddRange(BitConverter.GetBytes(uint.MaxValue));



            var objTypeName = obj.GetProperty("type").GetString();

            var objType = Type.GetType(objTypeName);


            if (objType == null)
                throw new Exception($"Type '{objTypeName}' not found - type must be specified in full, for example 'Engine.Core.GameObject' ");




            //write object type

            final.AddRange(BitConverter.GetBytes(GetGameObjectTypeID(objType)));



            //write each argument

            if (obj.TryGetProperty("arguments", out var args))
            {


                Type objtypecheck = objType;
                MethodInfo objInitMethod = null;


                while (true)
                {
                    if (objtypecheck == typeof(object))
                        throw new Exception("Missing Init method");


                    var initfind = objtypecheck.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly).Where(x => x.Name == "Init");


                    if (initfind.Any())
                    {
                        objInitMethod = initfind.FirstOrDefault();

                        break;
                    }

                    objtypecheck = objtypecheck.BaseType;
                }


                final.AddRange(WriteArgumentBytes(args, objInitMethod));
            }

            else final.Add(0);


        }




        return final.ToArray();
    }


#endif




    
    public static new async Task<GameResource> Load(Loading.AssetByteStream stream, string ScenePath)
    {


        var SceneResources = await LoadResourceBytes(stream, ScenePath);




        SceneObjectGroup[] Groups = new SceneObjectGroup[stream.ReadUnmanagedType<uint>()];
        for (int i = 0; i < Groups.Length; i++)
        {
            Groups[i] = new()
            {
                Name = stream.ReadUintLengthPrefixedUTF8String(),

                ObjectIdxs = stream.ReadUnmanagedTypeArray<uint>(stream.ReadUnmanagedType<uint>())
            };
        }




        uint objectcount = stream.ReadUnmanagedType<uint>();

        SceneObjectGenData[] SceneObjects = new SceneObjectGenData[objectcount];

        uint?[] parents = new uint?[objectcount];

        SceneObjectGenData SceneRoot = null;



        for (uint obj = 0; obj < objectcount; obj++)
        {

            uint sceneref = stream.ReadUnmanagedType<uint>();
            uint parent = stream.ReadUnmanagedType<uint>();


            var objectTypeID = stream.ReadUnmanagedType<ushort>();  



            SceneObjectGenData inst = null;


            //if this object is the root of a scene reference
            if (sceneref != uint.MaxValue)
            {
                var scn = (SceneResource)SceneResources[sceneref];
                var oinst = scn.SceneRootObject;

                inst = new() { TypeID = objectTypeID, Args = new(), SelfSceneInstance = scn, OriginalGenData = oinst };
            }

            //otherwise make totally fresh data
            else inst = new() { TypeID = objectTypeID, Args = new() };



            if (parent == uint.MaxValue)
                SceneRoot = inst;
            else
                parents[obj] = parent;



            inst.Args = ReadArgumentBytes(stream, SceneResources);
            
            inst.SelfIndex = obj;

            SceneObjects[obj] = inst;
        }



        if (SceneRoot == null) throw new Exception("Scene is missing a root.");




        //sort final object datas into actual heirarchy, add into groups, and add references to other objects where nessecary

        for (uint obj = 0; obj < objectcount; obj++)
        {
            var p = parents[obj];
            if (p != null)
            {
                var parent = SceneObjects[p.Value];
                if (parent.Children == null) parent.Children = new();
                parent.Children.Add(SceneObjects[obj]);
            }
        }


        return new SceneResource(SceneResources, SceneObjects, SceneRoot, Groups, ScenePath);
    }





    public GameObject Instantiate(Dictionary<string, object> RootObjectArgumentOverrides = null) => InstantiateInternal(RootObjectArgumentOverrides, true).root;



    private (GameObject root, Dictionary<uint, GameObject> objs) InstantiateInternal(Dictionary<string, object> args, bool callReadies = true)
    {

        var readyArgs = new Dictionary<GameObject, Dictionary<string, object>>();
        var objIdxs = new Dictionary<uint, GameObject>();
        var sceneInstances = new Dictionary<GameObject, Dictionary<uint, GameObject>>();



        var rootObj = CreateInstanceTree(SceneRootObject, args);


        rootObj.IsSceneInstanceRoot = true;


        if (callReadies)
            InitAll(rootObj);

        return (rootObj, objIdxs);




        GameObject CreateInstanceTree(SceneObjectGenData gendata, Dictionary<string, object> parentArgs)
        {
            Dictionary<string, object> resolvedArgs = ResolveArgs(gendata, parentArgs);


            GameObject inst;


            if (gendata.SelfSceneInstance != null)
            {
                var nested = gendata.SelfSceneInstance.InstantiateInternal(resolvedArgs, false);

                sceneInstances.Add(nested.root, nested.objs); 
                inst = nested.root;
            }

            else
                inst = ConstructGameObjectFromTypeID(gendata.TypeID);



            uint id = gendata.SelfIndex;
            objIdxs[id] = inst;
            readyArgs[inst] = resolvedArgs;




            if (gendata.Children != null)
            {
                foreach (var childGen in gendata.Children)
                {
                    var child = CreateInstanceTree(childGen, resolvedArgs);
                    inst.AddChild(child);
                }
            }

            return inst;
        }





        Dictionary<string, object> ResolveArgs(SceneObjectGenData gendata, Dictionary<string, object> parentArgs)
        {
            Dictionary<string, object> args =
                gendata.Args != null
                    ? new Dictionary<string, object>(gendata.Args)
                    : new Dictionary<string, object>();

            var gather = gendata;
            while (gather.SelfSceneInstance != null)
            {
                if (gather.OriginalGenData.Args != null)
                    args = Union(gather.OriginalGenData.Args, args);

                gather = gather.OriginalGenData;
            }

            if (gendata == SceneRootObject && parentArgs != null)
                args = Union(args, parentArgs);

            return args;
        }




        void InitAll(GameObject obj)
        {

            ImmutableArray<GameObject> array = obj.GetChildren();
            for (int i = 0; i < array.Length; i++)
                InitAll(array[i]);


            if (readyArgs.TryGetValue(obj, out var args))
            {
                FixupReferences(obj, args);
                CallInitFor(obj, args);
            }
            else
            {
                CallInitFor(obj, null);
            }
        }



        void FixupReferences(GameObject obj, Dictionary<string, object> args)
        {
            foreach (var entry in args)
            {
                //object index references

                if (entry.Value is GameObjectReference singleRef)
                    args[entry.Key] = ResolveRef(obj, singleRef);

                else if (entry.Value is GameObjectReference[] arr)
                    args[entry.Key] = arr.Select(r => ResolveRef(obj, r)).ToArray();

            }
        }



        GameObject ResolveRef(GameObject localRoot, GameObjectReference reference)
        {
            var lookup = objIdxs;
            if (sceneInstances.TryGetValue(localRoot, out var nested))
                lookup = nested;

            return lookup[reference.Reference];
        }


        Dictionary<string, object> Union(Dictionary<string, object> baseArgs, Dictionary<string, object> overlay)
        {
            if (baseArgs == null) return overlay;
            if (overlay == null) return baseArgs;

            var ret = new Dictionary<string, object>(baseArgs);
            foreach (var kvp in overlay)
                ret[kvp.Key] = kvp.Value;

            return ret;
        }
    }





    protected override void OnFree()
    {
        for (int i1 = 0; i1 < SceneOwnedReferences.Length; i1++)
            SceneOwnedReferences[i1].RemoveUser();
    }
}
