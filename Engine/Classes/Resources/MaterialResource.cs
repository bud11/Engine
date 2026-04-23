namespace Engine.GameResources;
using static Engine.Core.RenderingBackend;
using static Engine.Core.RenderingBackend.DrawPipelineDetails;
using Engine.Core;


using static Engine.Core.Parsing;
using Engine.Attributes;
using System.Numerics;


using static Engine.Core.References;

using static Engine.Core.IO;



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




    /// <summary>
    /// Contains resolved material details that can be used for draw calls.
    /// </summary>
    public unsafe readonly record struct MaterialResolution(

        BackendShaderReference Shader,
        Dictionary<string, BackendResourceSetReference> ResourceSets,

        RasterizationDetails RasterizationDetails,
        BlendState BlendState,
        DepthStencilState DepthStencilState

        );




    private readonly Dictionary<nint, (MaterialResolution resolution, bool valid)> Resolutions = new();

    public unsafe MaterialResolution ResolveMaterial(delegate*<MaterialResource, MaterialResolution> resolveFn)
    {
        lock (Resolutions)
        {
            var has = Resolutions.TryGetValue((nint)resolveFn, out var get);

            if (has && get.valid)
                return get.resolution;


            var ret = resolveFn(this);
            Resolutions[(nint)resolveFn] = get = new(ret, true);

            return get.resolution;
        }
    }




    private readonly Dictionary<string, TextureResource> Textures;
    private readonly Dictionary<string, object> Parameters;


    public bool TryGetParameter(string name, out object value) => Parameters.TryGetValue(name, out value);
    public object GetParameter(string name) => Parameters[name];

    public void SetParameter(string name, object value)
    {
        if (Parameters.TryGetValue(name, out var get) && get == value)
            return;

        Parameters[name] = value;

        MaterialDataChanged();
    }
    public IReadOnlyDictionary<string, object> GetParameters() => Parameters;




    public bool TryGetTexture(string name, out TextureResource value) => Textures.TryGetValue(name, out value);
    public TextureResource GetTexture(string name) => Textures[name];

    public void SetTexture(string name, TextureResource value)
    {
        if (Textures.TryGetValue(name, out var get) && get == value)
            return;

        Textures[name] = value;

        MaterialDataChanged();
    }
    public IReadOnlyDictionary<string, TextureResource> GetTextures() => Textures;




    private void MaterialDataChanged()
    {
        lock (Resolutions)
        {
            foreach (var kv in Resolutions)
                Resolutions[kv.Key] = kv.Value with { valid = false };
        }
    }






    public MaterialResource(Dictionary<string, object> parameters,
                            Dictionary<string, TextureResource> textures,

                            string key = null) : base(key)
    
    {
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




        // --- load textures ---

        var textureCount = reader.ReadUnmanaged<byte>();


        var texturetasks = new Dictionary<string, Task<TextureResource>>(textureCount);

        for (ulong i = 0; i < textureCount; i++)
            texturetasks[reader.ReadString()] = LoadResource<TextureResource>(reader.ReadString());



        // --- collect textures ---

        var finaltextures = new Dictionary<string, TextureResource>(textureCount);

        foreach (var result in texturetasks)
            finaltextures[result.Key] = await result.Value;



        var arguments = MaterialPropertySerializerDeserializer.Instance.ReadKnownType<Dictionary<string, object>>(ref reader);


        return new MaterialResource(arguments, finaltextures, key);
    }







#if DEBUG




    public static async Task<bool> Validate(byte[] validationBlock, string key) => true;

    public static async Task<IConverts.FinalAssetBytes> ConvertToFinalAssetBytes(Bytes bytes, string key)
    {



        var dict = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(bytes.ByteArray, JsonAssetLoadingOptions);

        bytes.Dispose();



        ValueWriter wr = ValueWriter.CreateWithBufferWriter();


        if (dict.TryGetValue("Textures", out var texGet))
        {
            var textures = texGet.Deserialize<Dictionary<string, string>>(JsonAssetLoadingOptions);

            wr.WriteUnmanaged((byte)textures.Count);

            foreach (var kv in textures)
            {
                wr.WriteString(kv.Key);
                wr.WriteString(kv.Value);
            }
        }
        else
            wr.WriteUnmanaged((byte)0);




        dict.TryGetValue("Parameters", out var argsGet);

        wr.WriteUnmanagedSpan<byte>(GetArgumentBytes<MaterialPropertySerializerDeserializer>(argsGet));

        return new IConverts.FinalAssetBytes(wr.GetSpan().ToArray(), null);

    }

#endif




}
