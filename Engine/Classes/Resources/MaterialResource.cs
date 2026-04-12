namespace Engine.GameResources;
using static Engine.Core.RenderingBackend;
using static Engine.Core.RenderingBackend.DrawPipelineDetails;
using Engine.Core;


using static Engine.Core.Parsing;
using Engine.Attributes;
using System.Numerics;


using static Engine.Core.References;



#if DEBUG
using System.Text.Json;
using static Engine.Core.IO;
#endif




[FileExtensionAssociation(".mat")]
public partial class MaterialResource :

    GameResource, GameResource.ILoads

#if DEBUG
    , GameResource.IConverts
#endif
{



    public readonly Dictionary<string, BackendResourceSetReference> MaterialResourceSets = new();


    public readonly Rendering.NamedShaderReference ShaderRef;


    public readonly Dictionary<string, BackendTextureAndSamplerReferencesPair> Textures;
    public readonly Dictionary<string, object> Parameters;



    public MaterialResource(Rendering.NamedShaderReference shaderRef,

                            Dictionary<string, object> parameters,
                            Dictionary<string, BackendTextureAndSamplerReferencesPair> textures,

                            string key = null) : base(key)
    
    {
        ShaderRef = shaderRef;

        Textures = textures;
        Parameters = parameters;

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





    public static async Task<GameResource> Load(AssetByteStream stream, string key)
    {

        var reader = ValueReader.FromStream(stream);


        // --- reads ---

        var shaderName = reader.ReadString();



        // --- load textures ---

        ExitResourceLoadThrottleSemaphore();


        var textureCount = reader.ReadUnmanaged<byte>();


        var texturetasks = new Dictionary<string, (Task<TextureResource>, SamplerDetails)>(textureCount);

        for (ulong i = 0; i < textureCount; i++)
            texturetasks[reader.ReadString()] = (LoadResource<TextureResource>(reader.ReadString()), reader.ReadUnmanaged<SamplerDetails>());



        // --- collect textures ---

        var finaltextures = new Dictionary<string, BackendTextureAndSamplerReferencesPair>(textureCount);

        foreach (var tex in texturetasks)
            finaltextures[tex.Key] = new BackendTextureAndSamplerReferencesPair((await tex.Value.Item1).BackendReference, BackendSamplerReference.Get(tex.Value.Item2));


        await EnterResourceLoadThrottleSemaphore();




        var arguments = MaterialPropertySerializerDeserializer.Instance.ReadKnownType<Dictionary<string, object>>(ref reader); 

        return new MaterialResource(new Rendering.NamedShaderReference(shaderName), arguments, finaltextures, key);
    }





    /// <summary>
    /// Represents expanded details to be used for a material-driven draw call, derived from a <see cref="MaterialResource"/>.
    /// </summary>
    public struct MaterialResolution
    {
        public Rendering.NamedShaderReference ShaderRef;

        public RasterizationDetails RasterizationDetails;
        public BlendState BlendState;
        public DepthStencilState DepthStencilState;

        public UnmanagedKeyValueCollection<WeakObjRef<string>, WeakObjRef<BackendResourceSetReference>> MaterialResourceSets;

        public MaterialResolution(Rendering.NamedShaderReference shaderName, RasterizationDetails rasterizationDetails, BlendState blendState, DepthStencilState depthStencilState, UnmanagedKeyValueCollection<WeakObjRef<string>, WeakObjRef<BackendResourceSetReference>> materialResourceSets)
        {
            ShaderRef = shaderName;
            RasterizationDetails = rasterizationDetails;
            BlendState = blendState;
            DepthStencilState = depthStencilState;
            MaterialResourceSets = materialResourceSets;
        }
    }




#if DEBUG




    private record struct MaterialTextureDefinition(string Path, SamplerDetails SamplerDetails);



    public static async Task<bool> Validate(byte[] validationBlock, string key) => true;

    public static async Task<IConverts.FinalAssetBytes> ConvertToFinalAssetBytes(Bytes bytes, string key)
    {



        var dict = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(bytes.ByteArray, JsonAssetLoadingOptions);

        bytes.Dispose();


        var shaderName = dict["Shader"].GetString();


        ValueWriter wr = ValueWriter.CreateWithBufferWriter();


        wr.WriteString(shaderName);

        if (dict.TryGetValue("Textures", out var texGet))
        {
            var textures = texGet.Deserialize<Dictionary<string, MaterialTextureDefinition>>(JsonAssetLoadingOptions);

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

        return new IConverts.FinalAssetBytes(wr.GetSpan().ToArray(),null);

    }

#endif




}
