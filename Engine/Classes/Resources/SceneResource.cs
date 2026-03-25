


namespace Engine.GameResources;



using Engine.Attributes;
using Engine.Core;
using System.Collections.Immutable;

using static Engine.Core.Parsing;

using System.Text;
using Engine.GameObjects;
using System.Numerics;
using static Engine.Core.Loading;


#if DEBUG
using System.Reflection;
using System.Text.Json;
using System.Buffers;
#endif











/// <summary>
/// Defines a scene of objects and resources. 
/// <br /> A scene can refer to external resources and/or embed them directly, and scenes can reference other scenes.
/// </summary>
/// 


[FileExtensionAssociation(".scn")]
public partial class SceneResource : GameResource, GameResource.ILoads

#if DEBUG
    , GameResource.IConverts
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


    

    public static async Task<byte[]> ConvertToFinalAssetBytes(Bytes bytes, string filePath)
    {


        var final = ValueWriter.CreateWithBufferWriter();


        using var doc = JsonDocument.Parse(bytes.ByteArray);
        var root = doc.RootElement;


        bytes.Dispose();






        if (root.TryGetProperty("Resources", out var resget))
        {

            var resourcesArr = resget.EnumerateArray().ToArray();



            final.WriteUnmanaged((uint)resourcesArr.Length);


            var finalResourcesArrayBytes = new ValueWriter[resourcesArr.Length];



            ExitAssetConversionSemaphore();



            //prepare each resource in async fashion

            await Parallel.ForAsync<uint>(0, (uint)resourcesArr.Length, async (rIndex, cancellation) =>
            {


                var resourceFinalBytes = ValueWriter.CreateWithBufferWriter();


                var resource = resourcesArr[rIndex];


                var resourceTypeName = resource.GetProperty("Type").GetString();

                var resourceType = Type.GetType(resourceTypeName);



                if (resourceType == null)
                    throw new Exception($"Type '{resourceTypeName}' not found - type must be specified in full, for example '{typeof(GameResource).FullName}' ");




                //write type

                resourceFinalBytes.WriteUnmanaged(GetGameResourceTypeID(resourceType));






                //if external, write length prefixed path

                if (resource.TryGetProperty("Path", out var path))
                {
                    resourceFinalBytes.WriteUnmanaged((byte)1);
                    resourceFinalBytes.WriteString(path.GetString());
                }




                //otherwise, write length prefixed final data

                else
                {
                    byte[] data;

                    if (resource.TryGetProperty("TextData", out var textdata))
                        data = Encoding.UTF8.GetBytes(textdata.GetRawText());


                    else if (resource.TryGetProperty("Base64Data", out var base64data))
                        data = Convert.FromBase64String(base64data.GetString());


                    else
                        throw new Exception("Could not determine resource data source");





                    resourceFinalBytes.WriteUnmanaged((byte)0);



                    //find extension and get the final asset bytes

                    if (resource.TryGetProperty("ExtensionHint", out var extensionHint))
                    {
                        var ex = $".{extensionHint.GetString().Trim().TrimStart('.')}";

                        if (GameResourceFileAssociations.TryGetValue(ex, out var AssetFoundType))
                        {
                            var arg = new Bytes(data);
                            data = null;

                            var ret = await (await (Task<AssetByteStream>)typeof(Loading)
                                        .GetMethod(nameof(GetFinalAssetBytes), [typeof(Bytes), typeof(string)])
                                        .MakeGenericMethod(AssetFoundType)
                                        .Invoke(null, [arg, null])).GetArray();

                            data = ret;

                            // $"{filePath}+resource{rIndex}{ex}"

                        }

                        else
                            throw new Exception($"Extension hint '{ex}' defined in json data invalid for resource type '{resourceType.FullName}' - check type has matching {typeof(FileExtensionAssociationAttribute).FullName} attribute");

                    }

                    else
                        throw new Exception("No extension hint defined in json data for resource");





                    //append final asset bytes + length

                    resourceFinalBytes.WriteVariableLengthUnsigned((ulong)data.Length);
                    resourceFinalBytes.WriteUnmanagedSpan<byte>(data);


                    data = null;
                }


                finalResourcesArrayBytes[rIndex] = resourceFinalBytes;
            });



            await EnterAssetConversionSemaphore();



            //add all resources to final output

            for (int i = 0; i < finalResourcesArrayBytes.Length; i++)
                final.WriteUnmanagedSpan(finalResourcesArrayBytes[i].GetSpan());
        }

        else 
            final.WriteUnmanaged(0u);







        //write objects

        var objects = root.GetProperty("Objects");






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




        final.WriteUnmanaged((uint)objects.GetArrayLength());


        foreach (var obj in objects.EnumerateArray())
        {



            if (obj.TryGetProperty("Parent", out var parentget))
                final.WriteUnmanaged(parentget.GetUInt32());
            else
                final.WriteUnmanaged(uint.MaxValue);






            string objTypeName = obj.GetProperty("Type").GetString();

            Type objType = Type.GetType(objTypeName);






            if (obj.TryGetProperty("SelfSceneInstance", out var sceneInstanceGet))
            {
                final.WriteUnmanaged(sceneInstanceGet.GetUInt32());
            }

            else
            {
                final.WriteUnmanaged(uint.MaxValue);




                objTypeName = obj.GetProperty("Type").GetString();

                objType = Type.GetType(objTypeName);


                if (objType == null)
                    throw new Exception($"Type '{objTypeName}' not found - type must be specified in full, for example '{typeof(GameObject).FullName}' ");


                final.WriteUnmanaged(GameObject.GetGameObjectTypeID(objType));

            }



            //write each argument

            obj.TryGetProperty("Arguments", out var argsGet);
            
            final.WriteLengthPrefixedUnmanagedSpan<byte>(GetArgumentBytes<SceneBinarySerializerDeserializer>(argsGet, objType));
            
        }



        return final.GetSpan().ToArray();
    }


#endif







    public static async Task<GameResource> Load(AssetByteStream stream, string FilePath)
    {

        var reader = ValueReader.FromStream(stream);


        uint resourceCount = reader.ReadUnmanaged<uint>();

        if (resourceCount == 0) return null;



        GameResource[] SceneResources = new GameResource[resourceCount];

        (ushort type, object data)[] ress = new (ushort type, object data)[resourceCount];
        for (int r = 0; r < resourceCount; r++)
        {
            var type = reader.ReadUnmanaged<ushort>();

            object data;

            if (stream.ReadByte() == 1) 
                data = reader.ReadString();

            else
                data = reader.ReadLengthPrefixedUnmanagedSpan<byte>();


            ress[r] = (type, data);
        }


        ExitAssetLoadSemaphore();

        await Parallel.ForAsync<uint>(0, resourceCount, async (rIndex, cancellation) =>
        {
            var resource = ress[rIndex];



            GameResource res;

            if (resource.data is string path)
                res = SceneResources[rIndex] = await LoadGameResourceFromTypeIDAndPath(resource.type, RelativePathToFullPath(path, FilePath[..(FilePath.LastIndexOf('/')+1)]));

            else
            {
                var dat = (byte[])resource.data;

                var key = (FilePath ?? string.Empty) + "_" + rIndex;


                using (var memstream = new AssetByteStream(new MemoryStream(dat), dat.Length))
                    res = SceneResources[rIndex] = await InternalLoadOrFetchResource(key, async () => await LoadGameResourceFromTypeIDAndStream(resource.type, memstream, key));

            }


            res.Register();

            SceneResources[rIndex].AddUser();  //this scene


        });

        await EnterAssetLoadSemaphore();






        uint objectcount = reader.ReadUnmanaged<uint>();

        SceneObjectGenData[] SceneObjects = new SceneObjectGenData[objectcount];

        uint?[] parents = new uint?[objectcount];

        SceneObjectGenData SceneRoot = null;



        for (uint obj = 0; obj < objectcount; obj++)
        {


            var parent = reader.ReadUnmanaged<uint>();

            var sceneref = reader.ReadUnmanaged<uint>();



            SceneObjectGenData inst = null;


            //if this object is the root of a scene reference
            if (sceneref != uint.MaxValue)
            {
                var scn = (SceneResource)SceneResources[sceneref];
                var oinst = scn.SceneRootObject;

                inst = new() { GameObjectTypeID = oinst.GameObjectTypeID, SelfSceneInstance = scn };
            }

            //otherwise, make totally fresh data and read type
            else inst = new() { GameObjectTypeID = reader.ReadUnmanaged<ushort>() };



            if (parent == uint.MaxValue)
                SceneRoot = inst;
            else
                parents[obj] = parent;



            inst.ArgumentData = reader.ReadLengthPrefixedUnmanagedSpan<byte>();


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






    // basic
    [BinarySerializableType(typeof(bool))]
    [BinarySerializableType(typeof(float))]
    [BinarySerializableType(typeof(string))]
    [BinarySerializableType(typeof(Vector3))]
    [BinarySerializableType(typeof(Matrix4x4))]

    // references 
    [BinarySerializableType(deserializeMethod: nameof(ReadGameObject))]
    [BinarySerializableType(deserializeMethod: nameof(ReadGameResource))]
    [BinarySerializableType(deserializeMethod: nameof(ReadMaterialResource))]
    [BinarySerializableType(deserializeMethod: nameof(ReadModelResource))]

    [BinarySerializableType(typeof(GameObject[]))]
    [BinarySerializableType(typeof(GameResource[]))]
    [BinarySerializableType(typeof(MaterialResource[]))]

    //arguments
    [BinarySerializableType(typeof(Dictionary<byte, object>))]
    public partial class SceneBinarySerializerDeserializer : BinarySerializerDeserializerBase
    {

        public Dictionary<uint, GameObject> Objects;
        public ImmutableArray<GameResource> Resources;

        private GameObject ReadGameObject(uint val) => Objects[val];
        private GameResource ReadGameResource(uint val) => Resources[(int)val];
        private MaterialResource ReadMaterialResource(uint val) => (MaterialResource)ReadGameResource(val);
        private ModelResource ReadModelResource(uint val) => (ModelResource)ReadGameResource(val);
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
    /// Instantiates the scene, sets <see cref="IndexableAttribute"/> users on each <see cref="GameObject"/>, calls <see cref="GameObject.Init"/> for each <see cref="GameObject"/> starting with the deepest children, and returns the root <see cref="GameObject"/>.
    /// </summary>
    /// <returns></returns>
    public GameObject Instantiate() 
    {
        var objs = new Dictionary<GameObject, List<(SceneObjectGenData, SceneBinarySerializerDeserializer)>>();

        var rootObj = InstantiateInternal(objs);


        var argsBuffer = new Dictionary<byte, object>(capacity : 16);


        Init(rootObj, objs, argsBuffer);


        return rootObj;



        static void Init(GameObject obj, Dictionary<GameObject, List<(SceneObjectGenData, SceneBinarySerializerDeserializer)>>? objs, Dictionary<byte, object> args)
        {
            ImmutableArray<GameObject> array = obj.GetChildren();

            for (int i = 0; i < array.Length; i++)
                Init(array[i], objs, args);


            args.Clear();


            var objdata = objs[obj];

            for (int i = 0; i < objdata.Count; i++)
            {
                var get = objdata[i];

                var reader = ValueReader.FromMemory(get.Item1.ArgumentData);

                var read = get.Item2.ReadKnownType<Dictionary<byte, object>>(ref reader);

                if (read != null)
                    foreach (var kv in read)
                        args[kv.Key] = kv.Value;
            }


            foreach (var kv in args)
                obj.SetIndexable(kv.Key, kv.Value);


            obj.Init();

        }
    }



    private GameObject InstantiateInternal(Dictionary<GameObject, List<(SceneObjectGenData, SceneBinarySerializerDeserializer)>> objs)
    {


        var ctx = new SceneBinarySerializerDeserializer
        {
            Resources = SceneResources,
            Objects = new()
        };



        var rootObj = CreateInstanceTree(SceneRootObject, objs, ctx);
        rootObj.IsSceneInstanceRoot = true;



        return rootObj;


        static GameObject CreateInstanceTree(SceneObjectGenData gendata, Dictionary<GameObject, List<(SceneObjectGenData, SceneBinarySerializerDeserializer)>> objs, SceneBinarySerializerDeserializer ctx)
        {

            GameObject inst =
                gendata.SelfSceneInstance == null ?
                GameObject.ConstructGameObjectFromTypeID(gendata.GameObjectTypeID) : gendata.SelfSceneInstance.InstantiateInternal(objs);



            ctx.Objects[gendata.SelfIndex] = inst;


            if (!objs.TryGetValue(inst, out var get)) 
                get = objs[inst] = new();


            get.Add((gendata, ctx));

            if (gendata.Children != null)
            {
                for (int i = 0; i < gendata.Children.Count; i++)
                {
                    var child = CreateInstanceTree(gendata.Children[i], objs, ctx);

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
