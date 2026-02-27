


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
#endif











/// <summary>
/// Defines a scene of objects and resources. 
/// <br /> A scene can refer to external resources and/or embed them directly, and scenes can reference other scenes.
/// </summary>
/// 


[FileExtensionAssociation(".scn")]
public partial class SceneResource : GameResource, GameResource.ILoads, GameResource.IConverts
{





    private readonly ImmutableArray<GameResource> SceneResources;

    private readonly SceneObjectGenData SceneRootObject;
    private readonly SceneObjectGenData[] SceneObjects;


    private SceneResource(GameResource[] References, SceneObjectGenData[] Objects, SceneObjectGenData RootObject, string Key) : base(Key)
    {
        SceneRootObject = RootObject;
        SceneObjects = Objects;

        SceneResources = ImmutableArray.Create(References);

    }






#if DEBUG


    //always reconverting at dev time to ensure object arguments don't break given code-side cant be considered in the hash in any way
    //subresources aren't reconverted, this is fine

    public static bool ForceReconversion(byte[] bytes, byte[] currentCache) => true;


    

    public static async Task<byte[]> ConvertToFinalAssetBytes(byte[] bytes, string filePath)
    {


        List<byte> final = new();


        var dict = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(bytes, new JsonSerializerOptions() { PropertyNameCaseInsensitive = true });



        if (dict.TryGetValue("Resources", out var resget))
            await WriteResourceBytes(final, JsonSerializer.Deserialize<JsonElement[]>(resget), filePath);

        else 
            final.AddRange(BitConverter.GetBytes(0u));




        //write objects

        var objects = dict["Objects"];

        final.AddRange(BitConverter.GetBytes((uint)objects.GetArrayLength()));

        foreach (var obj in objects.EnumerateArray())
        {



            //if this is a child of another object, write parent object index, otherwise max value   (ordering therefore relies on the order in which the children appeared within the json array)
            //there needs to be one object without a parent (the root object)


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
                    throw new Exception($"Type '{objTypeName}' not found - type must be specified in full, for example 'Engine.Core.GameObject' ");

                final.AddRange(BitConverter.GetBytes(GetGameObjectTypeID(objType)));

            }



            //write each argument

            if (obj.TryGetProperty("Arguments", out var args))
                final.AddRange(WriteArgumentBytes(args));
            else
                final.AddRange(WriteVarUInt64(0));

        }




        return final.ToArray();
    }


#endif







    public static async Task<GameResource> Load(Loading.AssetByteStream stream, string ScenePath)
    {


        var SceneResources = await LoadResourceBytes(stream, ScenePath);




        uint objectcount = stream.DeserializeType<uint>();

        SceneObjectGenData[] SceneObjects = new SceneObjectGenData[objectcount];

        uint?[] parents = new uint?[objectcount];

        SceneObjectGenData SceneRoot = null;



        for (uint obj = 0; obj < objectcount; obj++)
        {


            var parent = stream.DeserializeType<uint>();

            var sceneref = stream.DeserializeType<uint>();



            SceneObjectGenData inst = null;


            //if this object is the root of a scene reference
            if (sceneref != uint.MaxValue)
            {
                var scn = (SceneResource)SceneResources[sceneref];
                var oinst = scn.SceneRootObject;

                inst = new() { GameObjectTypeID = oinst.GameObjectTypeID, SelfSceneInstance = scn };
            }

            //otherwise, make totally fresh data and read type
            else inst = new() { GameObjectTypeID = stream.DeserializeType<ushort>() };



            if (parent == uint.MaxValue)
                SceneRoot = inst;
            else
                parents[obj] = parent;


            inst.ArgumentData = stream.DeserializeType<byte[]>();
            

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


        return new SceneResource(SceneResources, SceneObjects, SceneRoot, ScenePath);
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
