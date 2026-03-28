namespace Engine.GameResources;
using static Engine.Core.RenderingBackend;
using static Engine.Core.RenderingBackend.DrawPipelineDetails;
using Engine.Core;


using static Engine.Core.Parsing;
using Engine.Attributes;
using System.Drawing;
using System.Numerics;


#if DEBUG
using System.Text.Json;
#endif




[FileExtensionAssociation(".mat")]
public partial class MaterialResource :

    GameResource, GameResource.ILoads

#if DEBUG
    , GameResource.IConverts
#endif
{



    public readonly UnmanagedKeyValueHandleCollectionOwner<string, BackendResourceSetReference> MaterialResourceSets = new();


    public readonly string ShaderName;
    public BackendShaderReference Shader { get; private set; }


    public readonly RefCountCollections.RefCountedDictionary<string, BackendTextureAndSamplerReferencesPair> Textures;
    public readonly Dictionary<string, object> Parameters;



    public MaterialResource(string shaderName,

                            Dictionary<string, object> parameters,
                            RefCountCollections.RefCountedDictionary<string, BackendTextureAndSamplerReferencesPair> textures,

                            string key = null) : base(key)
    
    {
        ShaderName = shaderName;
        ObtainShader();

        Shader.OnFreeEvent.Add(ObtainShader);  // <-- hot reload support


        Textures = textures;
        Parameters = parameters;

        Shader?.AddUser();
        Textures?.AddUser();
    }

    protected override void OnFree()
    {
        Shader?.RemoveUser();
        Textures?.RemoveUser();

        base.OnFree();
    }


    /// <summary>
    /// (Re)obtains the shader currently registered under <see cref="ShaderName"/>.
    /// </summary>
    public void ObtainShader()
    {
        Shader?.RemoveUser();
        Shader = BackendShaderReference.Get(ShaderName);
        Shader.AddUser();
    }







    [BinarySerializableType(typeof(Vector2))]
    [BinarySerializableType(typeof(Vector3))]
    [BinarySerializableType(typeof(Vector4))]
    [BinarySerializableType(typeof(float))]
    [BinarySerializableType(typeof(byte))]
    [BinarySerializableType(typeof(Dictionary<string, object>))]
    public partial class MaterialPropertySerializerDeserializer : BinarySerializerDeserializerBase
    {
        public static readonly MaterialPropertySerializerDeserializer Instance = new();
    }





    public static async Task<GameResource> Load(Loading.AssetByteStream stream, string key)
    {

        var reader = ValueReader.FromStream(stream);


        // --- reads ---

        var shaderName = reader.ReadString();



        // --- load textures ---

        Loading.ExitAssetLoadSemaphore();


        var textureCount = reader.ReadUnmanaged<byte>();


        var texturetasks = new Dictionary<string, (Task<TextureResource>, SamplerDetails)>(textureCount);

        for (ulong i = 0; i < textureCount; i++)
            texturetasks[reader.ReadString()] = (Loading.LoadResource<TextureResource>(reader.ReadString()), reader.ReadUnmanaged<SamplerDetails>());



        // --- collect textures ---

        var finaltextures = new RefCountCollections.RefCountedDictionary<string, BackendTextureAndSamplerReferencesPair>(textureCount);

        foreach (var tex in texturetasks)
            finaltextures[tex.Key] = new BackendTextureAndSamplerReferencesPair((await tex.Value.Item1).BackendReference, BackendSamplerReference.Get(tex.Value.Item2));


        await Loading.EnterAssetLoadSemaphore();




        var arguments = MaterialPropertySerializerDeserializer.Instance.ReadKnownType<Dictionary<string, object>>(ref reader); 

        return new MaterialResource(shaderName, arguments, finaltextures, key);
    }





    /// <summary>
    /// Represents expanded details to be used for a material-driven draw call, derived from a <see cref="MaterialResource"/>.
    /// </summary>
    public struct MaterialResolution
    {
        public BackendShaderReference Shader;

        public RasterizationDetails RasterizationDetails;
        public BlendState BlendState;
        public DepthStencilState DepthStencilState;

        public UnmanagedKeyValueHandleCollection<string, BackendResourceSetReference> MaterialResourceSets;

        public MaterialResolution(BackendShaderReference shader, RasterizationDetails rasterizationDetails, BlendState blendState, DepthStencilState depthStencilState, UnmanagedKeyValueHandleCollection<string, BackendResourceSetReference> materialResourceSets)
        {
            Shader=shader;
            RasterizationDetails=rasterizationDetails;
            BlendState=blendState;
            DepthStencilState=depthStencilState;
            MaterialResourceSets=materialResourceSets;
        }
    }




#if DEBUG




    private record struct MaterialTextureDefinition(string Path, SamplerDetails SamplerDetails);




    public static async Task<byte[]> ConvertToFinalAssetBytes(Loading.Bytes bytes, string filePath)
    {



        var dict = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(bytes.ByteArray, Parsing.JsonAssetLoadingOptions);

        bytes.Dispose();


        var shaderName = dict["Shader"].GetString();


        ValueWriter wr = ValueWriter.CreateWithBufferWriter();


        wr.WriteString(shaderName);

        if (dict.TryGetValue("Textures", out var texGet))
        {
            var textures = texGet.Deserialize<Dictionary<string, MaterialTextureDefinition>>(Parsing.JsonAssetLoadingOptions);

            wr.WriteUnmanaged((byte)textures.Count);

            foreach (var kv in textures)
            {
                wr.WriteString(kv.Key);
                wr.WriteString(kv.Value.Path);
                wr.WriteUnmanaged(kv.Value.SamplerDetails);
            }
        }
        else
            wr.WriteUnmanaged((byte)0);




        dict.TryGetValue("Parameters", out var argsGet);

        wr.WriteUnmanagedSpan<byte>(GetArgumentBytes<MaterialPropertySerializerDeserializer>(argsGet));

        return wr.GetSpan().ToArray();

    }

#endif




}
