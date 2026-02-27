


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
[FileExtensionAssociation(".exr")]
public class TextureResource : GameResource, GameResource.ILoads, GameResource.IConverts
{

    public readonly BackendTextureReference BackendReference;

    public TextureResource(BackendTextureReference tex, string key) : base(key)
    {
        BackendReference = tex;
        tex.AddUser();
    }




#if DEBUG

    public static bool ForceReconversion(byte[] bytes, byte[] currentCache) => false;

    public static async Task<byte[]> ConvertToFinalAssetBytes(byte[] bytes, string filePath)
    {
        TextureConversion.TextureProcessingData dat;


        var fileExtension = Path.GetExtension(filePath);

        if (fileExtension == ".png") dat = TextureConversion.LoadPNGTextureData(bytes);
        else if (fileExtension == ".exr") dat = TextureConversion.LoadEXRTextureData(bytes);
        else throw new Exception();



        var finaltexturedata = await TextureConversion.TextureToTextureRuntimeFormat(dat);



        List<byte> final = [

            .. BitConverter.GetBytes(finaltexturedata.Dimensions.X),
                .. BitConverter.GetBytes(finaltexturedata.Dimensions.Y),
                .. BitConverter.GetBytes(finaltexturedata.Dimensions.Z),

               (byte)finaltexturedata.InternalImageDataFormat,
               (byte)finaltexturedata.TextureType,

               (byte)finaltexturedata.Mips.Length,
            ];


        foreach (var v in finaltexturedata.Mips)
        {
            final.AddRange(BitConverter.GetBytes((uint)v.Length));
            final.AddRange(v);
        }


        return final.ToArray();

    }


#endif



    
    public static async Task<GameResource> Load(Loading.AssetByteStream stream, string key)
    {

        var w = stream.DeserializeType<uint>();
        var h = stream.DeserializeType<uint>();
        var d = stream.DeserializeType<uint>();

        TextureFormats format = (TextureFormats)stream.ReadByte();
        TextureTypes type = (TextureTypes)stream.ReadByte();


        byte[][] mips = new byte[stream.ReadByte()][];

        for (int i = 0; i < mips.Length; i++)
            mips[i] = stream.DeserializeType<byte[]>();


        return new TextureResource(BackendTextureReference.Create(new Vector3<uint>(w, h, d), type, format, false, mips), key);

    }

}