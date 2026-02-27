


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

public class MaterialResource :

    GameResource, GameResource.ILoads, GameResource.IConverts
{



    public readonly UnmanagedKeyValueHandleCollectionOwner<string, BackendResourceSetReference> MaterialResourceSets = new();
    
    public readonly BackendShaderReference Shader;

    public readonly RefCountCollections.RefCountedDictionary<string, BackendTextureAndSamplerReferencesPair> Textures;
    public readonly Dictionary<string, object> Parameters;



    public MaterialResource(BackendShaderReference shader,

                            Dictionary<string, object> parameters,
                            RefCountCollections.RefCountedDictionary<string, BackendTextureAndSamplerReferencesPair> textures,

                            string key = null) : base(key)
    
    {
        Shader = shader;
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





    public static async Task<GameResource> Load(Loading.AssetByteStream stream, string key)
    {

        // --- reads ---

        var Shader = BackendShaderReference.Get(stream.DeserializeType<string>());

        var textures = Parsing.DeserializeType<Dictionary<string, MaterialTextureDefinition>>(stream);

        var arguments = Parsing.ReadArgumentBytes<string>(stream, null);  



        // --- load textures ---

        var texturetasks = new Dictionary<string, Task<TextureResource>>(textures.Count);

        foreach (var kv in textures)
            texturetasks[kv.Key] = Loading.LoadResource<TextureResource>(kv.Value.Path);



        // --- collect textures ---

        var finaltextures = new RefCountCollections.RefCountedDictionary<string, BackendTextureAndSamplerReferencesPair>(textures.Count);

        foreach (var tex in texturetasks)
            finaltextures[tex.Key] = new BackendTextureAndSamplerReferencesPair((await tex.Value).BackendReference, BackendSamplerReference.Get(textures[tex.Key].SamplerDetails));



        return new MaterialResource(Shader, arguments, finaltextures, key);
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




    [BinarySerializableType]
    public struct MaterialTextureDefinition
    {
        public string Path;
        public string Name;
        public SamplerDetails SamplerDetails;
    }




#if DEBUG

    public static bool ForceReconversion(byte[] bytes, byte[] currentCache) => false;

    public static async Task<byte[]> ConvertToFinalAssetBytes(byte[] bytes, string filePath)
    {



        var dict = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(bytes);



        var shaderName = dict["Shader"].GetString();



        List<byte> finalbytes = [
            .. Parsing.SerializeType(shaderName, false),
            ];



        if (dict.TryGetValue("Textures", out var resget))
            finalbytes.AddRange(Parsing.SerializeType(resget.Deserialize<Dictionary<string, MaterialTextureDefinition>>(), false));
        else 
            finalbytes.AddRange(Parsing.WriteVarUInt64(0));



        if (dict.TryGetValue("Parameters", out var argsGet))
            finalbytes.AddRange(Parsing.WriteArgumentBytes(argsGet));

        else
            finalbytes.AddRange(Parsing.WriteVarUInt64(0));



        return finalbytes.ToArray();

    }

#endif




}
