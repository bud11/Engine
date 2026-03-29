


namespace Engine.GameResources;



using static Engine.Core.EngineMath;
using static Engine.Core.RenderingBackend;
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


    private static TextureConversion.TextureLoadOptions GetTextureConversionDetails(TextureConversion.TextureHeaderData header, string key)
        => new TextureConversion.TextureLoadOptions() { ConvertTo = TextureFormats.RGBA8_BC7_UNORM, ConvertSrgbToLinear = true, GenerateMips = true };



	static async Task<bool> IConverts.Validate(byte[] validationBlock, string key)
    {
        var st = Parsing.ValueReader.FromMemory(validationBlock);

        var header = st.ReadUnmanaged<TextureConversion.TextureHeaderData>();
        var options = st.ReadUnmanaged<TextureConversion.TextureLoadOptions>();

        return GetTextureConversionDetails(header, key) == options;
	}





    static async Task<IConverts.FinalAssetBytes> IConverts.ConvertToFinalAssetBytes(Loading.Bytes bytes, string key) 
    {
        var ext = Path.GetExtension(key);

        var header = TextureConversion.InspectTextureHeader(bytes.ByteArray, ext);
        var options = GetTextureConversionDetails(header, key);


		var finaltexturedata = await TextureConversion.ConvertTextureToRuntimeFormat(bytes, ext, header, options);



        var textureWrite = Parsing.ValueWriter.CreateWithBufferWriter();

        textureWrite.WriteUnmanaged(finaltexturedata.Dimensions);

        textureWrite.WriteUnmanaged((byte)finaltexturedata.InternalImageDataFormat);
        textureWrite.WriteUnmanaged((byte)finaltexturedata.TextureType);
        textureWrite.WriteUnmanaged((byte)finaltexturedata.Mips.Length);


        foreach (var v in finaltexturedata.Mips)
            textureWrite.WriteLengthPrefixedUnmanagedSpan<byte>(v);



        var validationWrite = Parsing.ValueWriter.CreateWithBufferWriter();

        validationWrite.WriteUnmanaged(header);
        validationWrite.WriteUnmanaged(options);

        return new IConverts.FinalAssetBytes(textureWrite.GetSpan().ToArray(), validationWrite.GetSpan().ToArray());

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