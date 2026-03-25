


namespace Engine.GameResources;





using Engine.Attributes;
using static Engine.Core.EngineMath;
using static Engine.Core.RenderingBackend;
using static Engine.Core.Rendering;
using Engine.Core;



#if DEBUG
using Engine.Stripped;
#endif




[FileExtensionAssociation(".png")]
[FileExtensionAssociation(".jpg")]
[FileExtensionAssociation(".jpeg")]
[FileExtensionAssociation(".bmp")]
[FileExtensionAssociation(".tga")]
[FileExtensionAssociation(".gif")]
[FileExtensionAssociation(".hdr")]
[FileExtensionAssociation(".exr")]
public class TextureResource : GameResource, GameResource.ILoads,

#if DEBUG
    GameResource.IConverts
#endif
{

    public readonly BackendTextureReference BackendReference;

    public TextureResource(BackendTextureReference tex, string key) : base(key)
    {
        BackendReference = tex;
        tex.AddUser();
    }




#if DEBUG


    public static async Task<byte[]> ConvertToFinalAssetBytes(Loading.Bytes bytes, string filePath)
    {
        var ext = Path.GetExtension(filePath);

        var header = TextureConversion.InspectTextureHeader(bytes.ByteArray, ext);
        
        var finaltexturedata = await TextureConversion.ConvertTextureToRuntimeFormat(bytes, ext, header, new() { ConvertTo = TextureFormats.RGB8_UNORM, ConvertSrgbToLinear = true, GenerateMips = true });


        var write = Parsing.ValueWriter.CreateWithBufferWriter();

        write.WriteUnmanaged(finaltexturedata.Dimensions);

        write.WriteUnmanaged((byte)finaltexturedata.InternalImageDataFormat);
        write.WriteUnmanaged((byte)finaltexturedata.TextureType);
        write.WriteUnmanaged((byte)finaltexturedata.Mips.Length);


        foreach (var v in finaltexturedata.Mips)
            write.WriteLengthPrefixedUnmanagedSpan<byte>(v);


        return write.GetSpan().ToArray();

    }


#endif



    
    public static async Task<GameResource> Load(Loading.AssetByteStream stream, string key)
    {

        var reader = Parsing.ValueReader.FromStream(stream);

        var w = reader.ReadUnmanaged<uint>();
        var h = reader.ReadUnmanaged<uint>();
        var d = reader.ReadUnmanaged<uint>();


        TextureFormats format = (TextureFormats)stream.ReadByte();
        TextureTypes type = (TextureTypes)stream.ReadByte();


        byte[][] mips = new byte[stream.ReadByte()][];

        for (int i = 0; i < mips.Length; i++)
            mips[i] = reader.ReadLengthPrefixedUnmanagedSpan<byte>();



        return new TextureResource(BackendTextureReference.Create(new Vector3<uint>(w, h, d), type, format, false, mips), key);

    }

}