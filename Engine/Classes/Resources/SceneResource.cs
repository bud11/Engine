


namespace Engine.GameResources;



using Engine.Attributes;
using Engine.Core;
using System.Collections.Immutable;

using static Engine.Core.Parsing;


#if DEBUG
using System.Reflection;
using System.Text.Json;
using System.Text;
using Engine.GameObjects;
using static Engine.Core.Loading;
#endif











/// <summary>
/// Defines a scene of objects and resources. 
/// <br /> A scene can refer to external resources and/or embed them directly, and scenes can reference other scenes.
/// </summary>
/// 


[FileExtensionAssociation(".scn")]
public partial class SceneResource : GameResource, GameResource.ILoads,

#if DEBUG
    GameResource.IConverts
#endif
{





    private readonly ImmutableArray<GameResource> SceneResources;

    private readonly SceneObjectGenData SceneRootObject;


    private SceneResource(GameResource[] References, SceneObjectGenData RootObject, string Key) : base(Key)
    {
        SceneRootObject = RootObject;

        SceneResources = ImmutableArray.Create(References);

    }






#if DEBUG


    //always reconverting at dev time to ensure object arguments don't break given code-side cant be considered in the hash in any way
    //subresources aren't reconverted, this is fine

    public static bool ForceReconversion(byte[] bytes, byte[] currentCache) => true;


    

    public static async Task<byte[]> ConvertToFinalAssetBytes(byte[] bytes, string filePath)
    {


        List<byte> final = new();


        var dict = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(bytes, JsonAssetLoadingOptions);



        if (dict.TryGetValue("Resources", out var resget))
        {

            var resourcesArr = JsonSerializer.Deserialize<JsonElement[]>(resget, JsonAssetLoadingOptions);




            final.AddRange(BitConverter.GetBytes((uint)resourcesArr.Length));

            var finalResourcesArrayBytes = new List<byte>[resourcesArr.Length];



            //prepare each resource in async fashion

            await Parallel.ForAsync<uint>(0, (uint)resourcesArr.Length, async (rIndex, cancellation) =>
            {

                var resourceFinalBytes = new List<byte>();


                var resource = resourcesArr[rIndex];


                var resourceTypeName = resource.GetProperty("Type").GetString();

                var resourceType = Type.GetType(resourceTypeName);



                if (resourceType == null)
                    throw new Exception($"Type '{resourceTypeName}' not found - type must be specified in full, for example '{typeof(GameResource).FullName}' ");




                //write type

                resourceFinalBytes.AddRange(BitConverter.GetBytes(GetGameResourceTypeID(resourceType)));






                //if external, write length prefixed path

                if (resource.TryGetProperty("Path", out var path))
                {
                    resourceFinalBytes.Add(1);
                    resourceFinalBytes.AddRange(SerializeType(path.GetString(), false));
                }




                //otherwise, write length prefixed final data

                else if (resource.TryGetProperty("TextData", out var textdata))
                    await WriteEmbedded(Encoding.UTF8.GetBytes(textdata.GetRawText()), true);


                else if (resource.TryGetProperty("Base64Data", out var base64data))
                    await WriteEmbedded(Convert.FromBase64String(base64data.GetString()), false);


                else
                    throw new Exception("Could not determine resource data source");






                async Task WriteEmbedded(byte[] data, bool plaintextwarning)
                {

                    resourceFinalBytes.Add(0);



                    //find extension and get the final asset bytes

                    if (resource.TryGetProperty("ExtensionHint", out var extensionHint))
                    {
                        var ex = $".{extensionHint.GetString().Trim().TrimStart('.')}";

                        if (GameResourceFileAssociations.TryGetValue(ex, out var AssetFoundType))
                        {
                            data = await (await (Task<AssetByteStream>)typeof(Loading)
                                        .GetMethod(nameof(GetFinalAssetBytes), [typeof(byte[]), typeof(string)])
                                        .MakeGenericMethod(AssetFoundType)
                                        .Invoke(null, [data, $"{filePath}+resource{rIndex}{ex}"])).GetArray();
                        }

                        else
                            throw new Exception($"Extension hint '{ex}' defined in json data invalid for resource type '{resourceType.FullName}' - check type has matching {typeof(FileExtensionAssociationAttribute).FullName} attribute");

                    }

                    else 
                        throw new Exception("No extension hint defined in json data for resource");





                    //append final asset bytes + length

                    resourceFinalBytes.AddRange(WriteVarUInt64((ulong)data.Length));
                    resourceFinalBytes.AddRange(data);
                }





                finalResourcesArrayBytes[rIndex] = resourceFinalBytes;
            });



            //add all resources to final output

            for (int i = 0; i < finalResourcesArrayBytes.Length; i++)
                final.AddRange(finalResourcesArrayBytes[i]);
        }

        else 
            final.AddRange(BitConverter.GetBytes(0u));




        //write objects

        var objects = dict["Objects"];





        // -----------------------------------------------------------------------------------------------------------------------------------------------------------




        int count = objects.GetArrayLength();

        uint[] parents = new uint[count];

        int idx = 0;
        foreach (var obj in objects.EnumerateArray())
        {
            if (obj.TryGetProperty("Parent", out var parentget))
                parents[idx] = parentget.GetUInt32();
            else
                parents[idx] = uint.MaxValue;

            idx++;
        }


        // ---- validate root ----

        int rootIndex = -1;
        int rootCount = 0;

        for (int i = 0; i < count; i++)
        {
            if (parents[i] == uint.MaxValue)
            {
                rootIndex = i;
                rootCount++;
            }
        }

        if (rootCount != 1)
            throw new Exception($"Scene must contain exactly one root object, found {rootCount}");


        // ---- validate parent indices ----

        for (int i = 0; i < count; i++)
        {
            if (parents[i] != uint.MaxValue && parents[i] >= count)
                throw new Exception($"Object {i} references invalid parent index {parents[i]}");
        }


        // ---- detect cycles ----

        for (int i = 0; i < count; i++)
        {
            HashSet<int> seen = new();
            List<int> chain = new();

            int current = i;

            while (parents[current] != uint.MaxValue)
            {
                if (!seen.Add(current))
                {
                    int cycleStart = chain.IndexOf(current);
                    var cycle = chain.Skip(cycleStart).Append(current);

                    throw new Exception($"Scene heirarchy cycle detected: {string.Join(" -> ", cycle)}");
                }

                chain.Add(current);
                current = (int)parents[current];
            }
        }


        // ---- detect orphans ----

        bool[] visited = new bool[count];

        void Visit(int i)
        {
            if (visited[i])
                return;

            visited[i] = true;

            for (int j = 0; j < count; j++)
                if (parents[j] == i)
                    Visit(j);
        }

        Visit(rootIndex);

        for (int i = 0; i < count; i++)
        {
            if (!visited[i])
                throw new Exception($"Object {i} is orphaned (not connected to root)");
        }



        // -----------------------------------------------------------------------------------------------------------------------------------------------------------




        final.AddRange(BitConverter.GetBytes((uint)objects.GetArrayLength()));


        foreach (var obj in objects.EnumerateArray())
        {



            if (obj.TryGetProperty("Parent", out var parentget))
                final.AddRange(BitConverter.GetBytes(parentget.GetUInt32()));
            else
                final.AddRange(BitConverter.GetBytes(uint.MaxValue));






            string objTypeName = obj.GetProperty("Type").GetString();

            Type objType = Type.GetType(objTypeName);






            if (obj.TryGetProperty("SelfSceneInstance", out var sceneInstanceGet))
            {
                final.AddRange(BitConverter.GetBytes(sceneInstanceGet.GetUInt32()));
            }

            else
            {

                final.AddRange(BitConverter.GetBytes(uint.MaxValue));




                objTypeName = obj.GetProperty("Type").GetString();

                objType = Type.GetType(objTypeName);


                if (objType == null)
                    throw new Exception($"Type '{objTypeName}' not found - type must be specified in full, for example '{typeof(GameObject).FullName}' ");

                final.AddRange(BitConverter.GetBytes(GetGameObjectTypeID(objType)));

            }



            //write each argument

            if (obj.TryGetProperty("Arguments", out var argsGet))
                final.AddRange(WriteArgumentBytes(argsGet, objType));
            else
                final.AddRange(WriteVarUInt64(0));

        }




        return final.ToArray();
    }


#endif







    public static async Task<GameResource> Load(AssetByteStream stream, string FilePath)
    {


        uint resourceCount = stream.DeserializeKnownType<uint>();

        if (resourceCount == 0) return null;


        GameResource[] SceneResources = new GameResource[resourceCount];

        (ushort type, bool external, byte[] data)[] ress = new (ushort type, bool external, byte[] data)[resourceCount];
        for (int r = 0; r < resourceCount; r++)
        {
            var type = stream.DeserializeKnownType<ushort>();
            bool external = stream.ReadByte() == 1;

            byte[] data;

            if (external)
            {
                var path = stream.DeserializeKnownType<string>();
                data = Encoding.UTF8.GetBytes(path);
            }
            else
            {
                data = stream.DeserializeKnownType<byte[]>();
            }

            ress[r] = (type, external, data);
        }




        await Parallel.ForAsync<uint>(0, resourceCount, async (rIndex, cancellation) =>
        {
            var resource = ress[rIndex];


            string key = null;
            if (resource.external) key = RelativePathToFullPath(Encoding.UTF8.GetString(resource.data), FilePath[..(FilePath.LastIndexOf('/')+1)]);
            else key = (FilePath ?? string.Empty) + "_" + rIndex;


            GameResource res;

            if (resource.external)
                res = SceneResources[rIndex] = await LoadGameResourceFromTypeID(resource.type, key);

            else
            {
                using (var memstream = new AssetByteStream(new MemoryStream(resource.data), resource.data.Length))
                    res = SceneResources[rIndex] = await InternalLoadOrFetchResource(key, async () => await ConstructGameResourceFromTypeID(resource.type, memstream, key));

            }


            res.Register();

            SceneResources[rIndex].AddUser();  //this scene


        });








        uint objectcount = stream.DeserializeKnownType<uint>();

        SceneObjectGenData[] SceneObjects = new SceneObjectGenData[objectcount];

        uint?[] parents = new uint?[objectcount];

        SceneObjectGenData SceneRoot = null;



        for (uint obj = 0; obj < objectcount; obj++)
        {


            var parent = stream.DeserializeKnownType<uint>();

            var sceneref = stream.DeserializeKnownType<uint>();



            SceneObjectGenData inst = null;


            //if this object is the root of a scene reference
            if (sceneref != uint.MaxValue)
            {
                var scn = (SceneResource)SceneResources[sceneref];
                var oinst = scn.SceneRootObject;

                inst = new() { GameObjectTypeID = oinst.GameObjectTypeID, SelfSceneInstance = scn };
            }

            //otherwise, make totally fresh data and read type
            else inst = new() { GameObjectTypeID = stream.DeserializeKnownType<ushort>() };



            if (parent == uint.MaxValue)
                SceneRoot = inst;
            else
                parents[obj] = parent;



            inst.ArgumentData = stream.DeserializeKnownType<byte[]>();
            

            inst.SelfIndex = obj;

            SceneObjects[obj] = inst;
        }




        //sort final object datas into actual heirarchy, add references to other objects where nessecary


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



        return new SceneResource(SceneResources, SceneRoot, FilePath);
    }







    public sealed class SceneBinaryDeserializationContext
    {
        public ImmutableArray<GameObject> Objects;
        public ImmutableArray<GameResource> Resources;
    }


    private class SceneObjectGenData
    {

        public ushort GameObjectTypeID;

        public byte[] ArgumentData;

        public List<SceneObjectGenData> Children;

        public SceneResource SelfSceneInstance;  //if this object is pointing to another scene..


        public uint SelfIndex;
    }






    /// <summary>
    /// Instantiates the scene, sets <see cref="DataValueAttribute"/> users on each <see cref="GameObject"/>, calls <see cref="GameObject.Init"/> for each <see cref="GameObject"/> starting with the deepest children, and returns the root <see cref="GameObject"/>.
    /// </summary>
    /// <returns></returns>
    public GameObject Instantiate() => InstantiateScene(true);




    private GameObject InstantiateScene(bool callInit)
    {
        var objs = new Dictionary<GameObject, List<(SceneObjectGenData, SceneBinaryDeserializationContext)>>();

        var rootObj = InstantiateInternal(objs);


        if (callInit)
            Init(rootObj, objs);


        return rootObj;



        static void Init(GameObject obj, Dictionary<GameObject, List<(SceneObjectGenData, SceneBinaryDeserializationContext)>>? objs)
        {
            ImmutableArray<GameObject> array = obj.GetChildren();

            for (int i = 0; i < array.Length; i++)
                Init(array[i], objs);


            Dictionary<byte, object> args = new();

            var objdata = objs[obj];

            for (int i = 0; i < objdata.Count; i++)
            {
                var get = objdata[i];

                var read = Parsing.ReadArgumentBytes<byte>(new MemoryStream(get.Item1.ArgumentData), get.Item2);

                if (read != null)
                    foreach (var kv in read)
                        args[kv.Key] = kv.Value;
            }

            if (obj is ModelInstance) throw new Exception();

            SetDataValues(obj, args);


            obj.Init();

        }
    }




    private GameObject InstantiateInternal(Dictionary<GameObject, List<(SceneObjectGenData, SceneBinaryDeserializationContext)>> objs)
    {


        var ctx = new SceneBinaryDeserializationContext();

        var rootObj = CreateInstanceTree(SceneRootObject);
        rootObj.IsSceneInstanceRoot = true;


        return rootObj;


        GameObject CreateInstanceTree(SceneObjectGenData gendata)
        {

            GameObject inst =
                gendata.SelfSceneInstance == null ?
                ConstructGameObjectFromTypeID(gendata.GameObjectTypeID) : gendata.SelfSceneInstance.InstantiateInternal(objs);


            if (!objs.TryGetValue(inst, out var get)) 
                get = objs[inst] = new();


            get.Add((gendata, ctx));

            if (gendata.Children != null)
            {
                for (int i = 0; i < gendata.Children.Count; i++)
                {
                    var child = CreateInstanceTree(gendata.Children[i]);

                    inst.AddChild(child);
                }
            }


            return inst;
        
        }

    }





    protected override void OnFree()
    {
        for (int i1 = 0; i1 < SceneResources.Length; i1++)
            SceneResources[i1].RemoveUser();
    }
}
