


namespace Engine.GameResources;



using Engine.Attributes;
using Engine.Core;
using Engine.GameObjects;
using System.Collections.Immutable;
using System.Runtime.InteropServices;
using System.Text.Json;





/// <summary>
/// An index reference to a <see cref="GameObject"/>.
/// </summary>
/// <param name="Reference"></param>
[StructLayout(LayoutKind.Sequential, Pack = 1, Size = sizeof(uint))]
public readonly record struct ObjectReference(uint Reference);






/// <summary>
/// Defines a scene of objects and resources. See <see cref="GameObjectInitMethodAttribute"/> for making <see cref="GameObject"/> types compatible with scene instantiation.
/// <br /> A scene can refer to external resources and/or embed them directly, and scenes can reference other scenes.
/// <br /> Scenes themselves can also own resources and buffers, which may be useful for something like lightmapping textures+buffers for example.
/// </summary>
public partial class SceneResource : GameResource
{



    private class SceneObjectGenData
    {

        public string TypeName;

        public Dictionary<string, object> Args;

        public List<SceneObjectGenData> Children;

        public SceneResource SelfSceneInstance;  //if this object is pointing to another scene..
        public SceneObjectGenData OriginalGenData;

    }


    private unsafe record struct SceneObjectGroup(string Name, uint[] ObjectIdxs);








    public readonly ImmutableArray<GameResource> SceneOwnedReferences;

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

    [StaticVirtualOverride]
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



            //write object type

            final.AddRange(Parsing.GetUintLengthPrefixedUTF8StringAsBytes(obj.GetProperty("type").GetString()));



            //write each argument

            if (obj.TryGetProperty("arguments", out var argsGet))
            {
                var args = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(argsGet);

                Parsing.WriteArgumentBytes(final, args);
            }
            else final.Add(0);
        }




        return final.ToArray();
    }


#endif




    [StaticVirtualOverride]
    public static new async Task<GameResource> Load(byte[] bytes, string ScenePath)
    {




        using (MemoryStream ms = new(bytes))
        using (BinaryReader reader = new BinaryReader(ms))
        {


            var SceneResources = await Parsing.LoadResourceBytes(reader, ScenePath);




            SceneObjectGroup[] Groups = new SceneObjectGroup[reader.ReadUInt32()];
            for (int i = 0; i < Groups.Length; i++)
            {
                Groups[i] = new()
                {
                    Name = reader.ReadUintLengthPrefixedUTF8String(),

                    ObjectIdxs = reader.ReadTypeArray<uint>(reader.ReadUInt32())
                };
            }




            uint objectcount = reader.ReadUInt32();

            SceneObjectGenData[] SceneObjects = new SceneObjectGenData[objectcount];

            uint?[] parents = new uint?[objectcount];

            SceneObjectGenData SceneRoot = null;



            for (uint obj = 0; obj < objectcount; obj++)
            {

                uint sceneref = reader.ReadUInt32();
                uint parent = reader.ReadUInt32();


                parents[obj] = parent == uint.MaxValue ? null : parent;


                var objectTypeName = reader.ReadUintLengthPrefixedUTF8String();



                SceneObjectGenData inst = null;


                //if this object is the root of a scene reference
                if (sceneref != uint.MaxValue)
                {
                    var scn = (SceneResource)SceneResources[sceneref];
                    var oinst = scn.SceneRootObject;

                    inst = new() { TypeName = objectTypeName, Args = new(), SelfSceneInstance = scn, OriginalGenData = oinst };
                }

                //otherwise make totally fresh data
                else inst = new() { TypeName = objectTypeName, Args = new() };




                if (parent == uint.MaxValue) SceneRoot = inst;


                inst.Args = Parsing.ReadArgumentBytes(reader, SceneResources);


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
    }





    public GameObject Instantiate(Dictionary<string, object> RootObjectArgumentOverrides = null) => InstantiateInternal(RootObjectArgumentOverrides, true).root;



    private (GameObject root, Dictionary<uint, GameObject> objs) InstantiateInternal(Dictionary<string, object> args, bool callReadies = true)
    {

        var readyArgs = new Dictionary<GameObject, Dictionary<string, object>>();
        var objIdxs = new Dictionary<uint, GameObject>();
        var sceneInstances = new Dictionary<GameObject, Dictionary<uint, GameObject>>();

        // Create the full object tree from the RootObject:
        var rootObj = CreateInstanceTree(SceneRootObject, args);



        if (callReadies)
            InitAll(rootObj);

        return (rootObj, objIdxs);




        GameObject CreateInstanceTree(SceneObjectGenData gendata, Dictionary<string, object> parentArgs)
        {
            // Resolve and merge argument dictionaries
            Dictionary<string, object> resolvedArgs = ResolveArgs(gendata, parentArgs);


            GameObject inst;


            // Embedded scene?
            if (gendata.SelfSceneInstance != null)
            {
                var nested = gendata.SelfSceneInstance.InstantiateInternal(resolvedArgs, false);

                sceneInstances.Add(nested.root, nested.objs); // Store nested instance mapping
                inst = nested.root;
            }

            else
                inst = GameObject.ConstructObject(gendata.TypeName);



            // Register this instance
            uint id = (uint)Array.IndexOf(SceneObjects, gendata);
            objIdxs[id] = inst;
            readyArgs[inst] = resolvedArgs;



            // Recursively build children
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
            // Base args
            var args = gendata.Args ?? new Dictionary<string, object>();


            // Root override
            if (gendata == SceneRootObject && parentArgs != null)
                return Union(args, parentArgs);


            // Walk up through scene instance ancestry
            var gather = gendata;
            while (gather.SelfSceneInstance != null)
            {
                args = Union(gather.OriginalGenData.Args, args);
                gather = gather.OriginalGenData;
            }

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
                GameObject.CallObjectInit(obj, args);
            }
            else
            {
                GameObject.CallObjectInit(obj, null);
            }
        }



        void FixupReferences(GameObject obj, Dictionary<string, object> args)
        {
            foreach (var entry in args)
            {
                if (entry.Value is ObjectReference singleRef)
                    args[entry.Key] = ResolveRef(obj, singleRef);

                else if (entry.Value is ObjectReference[] arr)
                    args[entry.Key] = arr.Select(r => ResolveRef(obj, r)).ToArray();
            }
        }



        GameObject ResolveRef(GameObject localRoot, ObjectReference reference)
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
