


namespace Engine.GameResources;



using System.Collections.Immutable;


using Engine.Attributes;
using static Engine.Core.RenderingBackend;
using static Engine.Core.RenderingBackend.DrawPipelineDetails;
using Engine.Core;



#if DEBUG
using System.Text.Json;
#endif




[FileExtensionAssociation(".mat")]
public partial class MaterialResource : GameResource
{

    /// <summary>
    /// Encapsulates material resources and fixed pipeline state that the rendering backend will consume.
    /// <br/> Can be freely modified in any way at any point.
    /// </summary>
    public struct MaterialDefinition
    {
        //resources

        public BackendShaderReference Shader;
        public UnmanagedKeyValueHandleCollection<string, BackendResourceSetReference> MaterialResourceSets;


        //drawing pipeline

        public RasterizationDetails Rasterization;
        public BlendState Blending;
        public DepthStencilState DepthStencil;

    }



    /// <summary>
    /// <inheritdoc cref="MaterialDefinition"/>
    /// <br/> <b> See <see cref="MaterialResourceSets"/> to modify sets returned by this property. </b>
    /// </summary>
    public MaterialDefinition Definition
    {
        get => _definition with { MaterialResourceSets = MaterialResourceSets == null ? default : MaterialResourceSets.GetUnderlyingCollection() };
        set => _definition = value;
    }

    private MaterialDefinition _definition;


    public readonly UnmanagedKeyValueHandleCollectionOwner<string, BackendResourceSetReference> MaterialResourceSets = new();


    private Dictionary<string, object> Arguments;

    public void SetArgument(string name, object value)
    {
        if (Arguments == null) Arguments = new();


        bool changed = (!Arguments.TryGetValue(name, out var get)) || get != value;
        Arguments[name] = value;

        if (changed)
            OnArgumentChangeEvent.Invoke((name, value));
    }
    public object GetArgument(string name) => Arguments == null ? null : (Arguments.TryGetValue(name, out var get) ? get : null);


    public ImmutableDictionary<string, object> GetArguments()
        => Arguments == null ? ImmutableDictionary<string, object>.Empty : ImmutableDictionary.ToImmutableDictionary(Arguments);




    /// <summary>
    /// Called when <see cref="SetArgument"/> results in a change.
    /// </summary>
    public static readonly ThreadSafeEventAction<(string argumentName, object newValue)> OnArgumentChangeEvent = new();




    public MaterialResource(BackendShaderReference shader,

                            RasterizationDetails rasterization,
                            BlendState blending,
                            DepthStencilState depthStencil,

                            Dictionary<string, object> arguments,

                            string key = null) : base(key)
    {


        Definition = Definition with
        {
            Shader = shader,

            Rasterization = rasterization,
            Blending = blending,
            DepthStencil = depthStencil,
        };

        Arguments = arguments;
    }





    
    public static new async Task<GameResource> Load(Loading.AssetByteStream stream, string key)
    {
        var Shader = GetShader(stream.ReadUintLengthPrefixedUTF8String());

        RasterizationDetails rasterization = stream.ReadUnmanagedType<RasterizationDetails>();
        BlendState blending = stream.ReadUnmanagedType<BlendState>();
        DepthStencilState depthStencil = stream.ReadUnmanagedType<DepthStencilState>();


        return new MaterialResource(Shader, rasterization, blending, depthStencil, Parsing.ReadArgumentBytes(stream, await Parsing.LoadResourceBytes(stream, key)), key);
    }


#if DEBUG



    
    public static new async Task<byte[]> ConvertToFinalAssetBytes(byte[] bytes, string filePath)
    {



        var dict = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(bytes);



        var shaderName = dict["Shader"].GetString();



        RasterizationDetails rasterization = default;
        BlendState blending = default;
        DepthStencilState depthStencil = default;



        if (dict.TryGetValue("Rasterization", out var raster))
            rasterization = JsonSerializer.Deserialize<RasterizationDetails>(raster);


        if (dict.TryGetValue("DepthStencil", out var depth))
            depthStencil = JsonSerializer.Deserialize<DepthStencilState>(depth);


        if (dict.TryGetValue("BlendState", out var blend))
            blending = JsonSerializer.Deserialize<BlendState>(blend);



        List<byte> finalbytes = [
            .. Parsing.GetUintLengthPrefixedUTF8StringAsBytes(shaderName),

                .. Parsing.StructToBytes(rasterization),
                .. Parsing.StructToBytes(blending),
                .. Parsing.StructToBytes(depthStencil)

            ];



        if (dict.TryGetValue("Resources", out var resget))
            await Parsing.WriteResourceBytes(finalbytes, JsonSerializer.Deserialize<JsonElement[]>(resget), filePath);

        else finalbytes.AddRange(BitConverter.GetBytes(0u));




        if (dict.TryGetValue("Arguments", out var argsGet))
            finalbytes.AddRange(Parsing.WriteArgumentBytes(argsGet));

        else 
            finalbytes.Add(0);



        return finalbytes.ToArray();

    }

#endif




}
