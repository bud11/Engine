
namespace Engine.Stripped;


using DdsKtxSharp;
using Engine.Core;
using StbImageSharp;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using TinyEXR;
using static Engine.Core.EngineMath;
using static Engine.Core.Rendering;
using static Engine.Core.RenderingBackend;




/// <summary>
/// Manages loading and processing of texture data.
/// <br/> All textures are converted to linear space if they aren't already linear.
/// </summary>
public static class TextureConversion
{






    public struct TextureProcessingData
    {
        public Vector3<uint> Dimensions;
        public float[] Data;
        public TextureFormats ImageDataFormatTarget;
    }



    public static TextureProcessingData LoadPNGTextureData(byte[] src)
    {

        // Check for sRGB chunk presence in PNG bytes
        static bool HasSrgbChunk(ReadOnlySpan<byte> data)
        {
            int pos = 8; 
            while (pos + 8 < data.Length)
            {
                uint length = (uint)((data[pos] << 24) | (data[pos + 1] << 16) | (data[pos + 2] << 8) | data[pos + 3]);
                pos += 4;
                if (pos + 4 > data.Length) break;
                // chunk type
                if (data[pos] == 's' && data[pos + 1] == 'R' && data[pos + 2] == 'G' && data[pos + 3] == 'B')
                    return true;

                pos += 4 + (int)length + 4;
            }
            return false;
        }




        StbImage.stbi_set_flip_vertically_on_load(1);
        ImageResult tex = ImageResult.FromMemory(src);


        int components = tex.SourceComp switch
        {
            ColorComponents.RedGreenBlueAlpha => 4,
            ColorComponents.RedGreenBlue => 3,
            ColorComponents.Grey => 1,
            ColorComponents.GreyAlpha => 2,

            _ => throw new NotImplementedException(),
        };



        bool SRGB = HasSrgbChunk(src);

        float[] floatData = new float[tex.Data.Length];

        byte c = 0;
        for (int i = 0; i < tex.Data.Length; i++)
        {
            var f = tex.Data[i] / 255f;

            if (SRGB && (components != 4 || c != 3))
            {
                f = SrgbToLinear(f);
                c = 0;
            }
            c++;

            floatData[i] = f;   
        }


        return new TextureProcessingData()
        {
            Dimensions = new((uint)tex.Width, (uint)tex.Height, 1),
            Data = floatData,

            ImageDataFormatTarget = tex.SourceComp switch
            {
                ColorComponents.RedGreenBlueAlpha => TextureFormats.RGBA8_UNORM,
                ColorComponents.RedGreenBlue => TextureFormats.RGB8_UNORM,
                ColorComponents.Grey => TextureFormats.R8_UNORM,
                ColorComponents.GreyAlpha => TextureFormats.RG8_UNORM,


                _ => throw new NotImplementedException(),
            }
        };
    }





    public static unsafe TextureProcessingData LoadEXRTextureData(byte[] src)
    {



        using (var stream = new MemoryStream(src))
        using (var stream2 = new MemoryStream(src))
        {

            TinyEXR.Native.EXRVersion v = default;
            TinyEXR.Native.EXRHeader header = default;
            TinyEXR.Native.EXRImage exrimage = default;

            Exr.InitEXRImage(ref exrimage);

            var err1 = Exr.ParseEXRHeaderFromMemory(src, ref v, ref header);

            if (err1 != ResultCode.Success) throw new Exception(err1.ToString());

            for (int c = 0; c < header.num_channels; c++)
            {
                header.requested_pixel_types[c] = (int)ExrPixelType.Float;
            }


            var err2 = Exr.LoadEXRImageFromMemory(ref exrimage, ref header, src);

            if (err2 != ResultCode.Success) throw new Exception(err2.ToString());



            int width = exrimage.width;
            int height = exrimage.height;
            int n = width * height;

            int channelCount = header.num_channels;



            float[] output = new float[n * channelCount];


            int rIndex = -1, gIndex = -1, bIndex = -1, aIndex = -1;

            for (int c = 0; c < header.num_channels; c++)
            {
                string name = Marshal.PtrToStringAnsi((IntPtr)header.channels[c].name);
                switch (name)
                {
                    case "R": rIndex = c; break;
                    case "G": gIndex = c; break;
                    case "B": bIndex = c; break;
                    case "A": aIndex = c; break;
                }
            }


            float* rPtr = rIndex >= 0 ? (float*)exrimage.images[rIndex] : null;
            float* gPtr = gIndex >= 0 ? (float*)exrimage.images[gIndex] : null;
            float* bPtr = bIndex >= 0 ? (float*)exrimage.images[bIndex] : null;
            float* aPtr = aIndex >= 0 ? (float*)exrimage.images[aIndex] : null;


            int channelnext = 0;
            for (int i = 0; i < output.Length; i++)
            {

                switch (channelnext)
                {
                    case 0:
                        output[i] = rPtr[i/channelCount];
                        break;
                    case 1:
                        output[i] = gPtr[i/channelCount];
                        break;
                    case 2:
                        output[i] = bPtr[i/channelCount];
                        break;
                    case 3:
                        output[i] = aPtr[i/channelCount];
                        break;
                }

                channelnext = (channelnext+1) % channelCount;
            }




            Exr.FreeEXRHeader(ref header);
            Exr.FreeEXRImage(ref exrimage);







            bool anyExceed8BitRange = false;
            for (int i = 0; i < output.Length; i++)
            {
                var g = output[i];
                if (g < 0f || g > 1f)
                {
                    anyExceed8BitRange = true;
                    break;
                }
            }



            // flip vertically in-place
            int rowStride = width * channelCount;
            var rowBuffer = new float[rowStride];
            for (int y = 0; y < height / 2; y++)
            {
                int topOffset = y * rowStride;
                int bottomOffset = (height - 1 - y) * rowStride;

                // swap rows
                Array.Copy(output, topOffset, rowBuffer, 0, rowStride);
                Array.Copy(output, bottomOffset, output, topOffset, rowStride);
                Array.Copy(rowBuffer, 0, output, bottomOffset, rowStride);
            }




            return new TextureProcessingData()
            {
                Dimensions = new((uint)width, (uint)height, 1),

                Data = output,

                //if nothing exceeds the 0-1 range then we can safely target unsigned byte
                ImageDataFormatTarget = channelCount switch
                {
                    1 => (anyExceed8BitRange ? TextureFormats.R16_SFLOAT : TextureFormats.R8_UNORM),
                    2 => (anyExceed8BitRange ? TextureFormats.RG16_SFLOAT : TextureFormats.RG8_UNORM),
                    3 => (anyExceed8BitRange ? TextureFormats.RGB16_SFLOAT : TextureFormats.RGB8_UNORM),
                    4 => (anyExceed8BitRange ? TextureFormats.RGBA16_SFLOAT : TextureFormats.RGBA8_UNORM),

                    _ => throw new NotImplementedException(),
                }

            };
        }

    }




    /// <summary>
    /// Defines how textures should be compressed.
    /// </summary>
    /// <param name="channels"></param>
    /// <param name="bitDepth"></param>
    /// <returns></returns>
    /// <exception cref="NotImplementedException"></exception>
    private static (string? bcFormat, TextureFormats format) GetCompressionFormat(int channels, int bitDepth, bool textureis3D)
    {
        if (bitDepth == 16)
        {
            return channels switch
            {
                1 => (null, TextureFormats.R16_SFLOAT),
                2 => (null, TextureFormats.RG16_SFLOAT),
                3 => ("BC6S", TextureFormats.BC6H_SFLOAT),  
                4 => (null, TextureFormats.RGBA16_SFLOAT),
                _ => throw new NotImplementedException()
            };
        }
        else
        {
            return channels switch
            {
                1 => ("BC4", TextureFormats.BC4),
                2 => ("BC5", TextureFormats.BC5),
                3 => ("BC7", TextureFormats.BC7),
                4 => ("BC7", TextureFormats.BC7),
                _ => throw new NotImplementedException()
            };
        }
    }





    public struct TextureGenData
    {
        public Vector3<uint> Dimensions;
        public byte[][] Mips;
        public TextureFormats InternalImageDataFormat;
        public TextureTypes TextureType;
    }




    private static List<int> TextureFilesInProgress = new();



    /// <summary>
    /// Takes raw texture data and parameters from <paramref name="TextureData"/> and creates compressed texture mips in the best avaliable format.
    /// <br /> <b> Requires Nvidia Texture Tools 3 to be installed. </b>
    /// <br /> Information about the result will be logged via <see cref="EngineDebug.Print(object, string, int, string)"/> if <paramref name="debugName"/> is included.
    /// </summary>
    /// <param name="TextureData"></param>
    /// <param name="debugName"></param>
    /// <returns></returns>
    /// <exception cref="Exception"></exception>
    /// <exception cref="NotImplementedException"></exception>
    public static async Task<TextureGenData> TextureToTextureRuntimeFormat(TextureProcessingData TextureData, string debugName = null)
    {


        var channelCount = TextureData.ImageDataFormatTarget switch
        {
            TextureFormats.R8_UNORM => 1,
            TextureFormats.RG8_UNORM => 2,
            TextureFormats.RGB8_UNORM => 3,
            TextureFormats.RGBA8_UNORM => 4,

            TextureFormats.R16_SFLOAT => 1,
            TextureFormats.RG16_SFLOAT => 2,
            TextureFormats.RGB16_SFLOAT => 3,
            TextureFormats.RGBA16_SFLOAT => 4,

            _ => throw new Exception()
        };


        var bitdepth = TextureData.ImageDataFormatTarget switch
        {
            TextureFormats.R8_UNORM => 8,
            TextureFormats.RG8_UNORM => 8,
            TextureFormats.RGB8_UNORM => 8,
            TextureFormats.RGBA8_UNORM => 8,

            TextureFormats.R16_SFLOAT => 16,
            TextureFormats.RG16_SFLOAT => 16,
            TextureFormats.RGB16_SFLOAT => 16,
            TextureFormats.RGBA16_SFLOAT => 16,

            _ => throw new Exception()
        };



        float[] floatData = TextureData.Data;


        var f = GetCompressionFormat(channelCount, bitdepth, TextureData.Dimensions.Z > 1);






        // otherwise just use original with no compression
        var originalDataForReferenceOrBypass = new byte[
            TextureData.Data.Length *
            (bitdepth == 8 ? 1 : 2)
        ];






        if (f.bcFormat != null)
        {


            int TEXTURESAVENAME = 0;
            lock (TextureFilesInProgress)
            {
                while (true)
                {
                    if (!TextureFilesInProgress.Contains(TEXTURESAVENAME))
                    {
                        TextureFilesInProgress.Add(TEXTURESAVENAME);
                        break;
                    }
                    TEXTURESAVENAME++;
                }
            }




            string texpathtemp = Path.Combine(Path.GetTempPath(), $"tempimage{TEXTURESAVENAME}.exr");
            string texpathtempOut = Path.Combine(Path.GetTempPath(), $"tempimage{TEXTURESAVENAME}Out.ktx");


            var save = Exr.SaveEXRToMemory(floatData, (int)TextureData.Dimensions.X, (int)TextureData.Dimensions.Y, channelCount, false, out var exrbytes);
            if (save != ResultCode.Success) throw new Exception(save.ToString());


            await File.WriteAllBytesAsync(texpathtemp, exrbytes);

            using (var fs = File.Open(texpathtemp, FileMode.Open, FileAccess.Read, FileShare.Read)) { }



            await RunProcess.Run(
                    "NVTT3",
                    $""" "{texpathtemp}" --format {f.bcFormat.ToLower()} --output "{texpathtempOut}" --mip-filter box --export-transfer-function 2"""
                );


            var bytes = await File.ReadAllBytesAsync(texpathtempOut);


            File.Delete(texpathtempOut);


            lock (TextureFilesInProgress)
                TextureFilesInProgress.Remove(TEXTURESAVENAME);



            var read = DdsKtxParser.FromMemory(bytes);

            byte[][] mipLevels = new byte[read.Info.num_mips][];

            DdsKtx.ddsktx_sub_data sub;

            for (int mip = 0; mip < read.Info.num_mips; mip++)
            {
                mipLevels[mip] = read.GetSubData(0, 0, mip, out sub);
            }


            debugprint(mipLevels[0].Length);


            return new TextureGenData()
            {
                Mips = mipLevels,

                InternalImageDataFormat = f.format,

                Dimensions = TextureData.Dimensions
            };
        }




        if (bitdepth == 8)
        {
            for (int i = 0; i < TextureData.Data.Length; i++)
                originalDataForReferenceOrBypass[i] = (byte)float.Clamp(LinearToSrgb(TextureData.Data[i]) * 255f, 0, 255);
        }

        else
        {
            for (int i = 0; i < TextureData.Data.Length; i++)
            {
                var half = (Half)TextureData.Data[i];
                ushort bits = Unsafe.As<Half, ushort>(ref half);

                int offset = i * 2;
                originalDataForReferenceOrBypass[offset]     = (byte)(bits & 0xFF);
                originalDataForReferenceOrBypass[offset + 1] = (byte)(bits >> 8);
            }
        }


        debugprint(originalDataForReferenceOrBypass.Length);


        return new TextureGenData()
        {
            Mips = [originalDataForReferenceOrBypass],
            InternalImageDataFormat = TextureData.ImageDataFormatTarget,

            Dimensions = TextureData.Dimensions,
        };




        void debugprint(int newByteSize)
        {
            if (debugName != null)
                EngineDebug.Print($"TEXTURE COMPRESSION : '{debugName}' : NO COMPRESSION {TextureData.ImageDataFormatTarget} ({ByteSizeLib.ByteSize.FromBytes(newByteSize).ToBinaryString()})");
        }
    }






    private static float SrgbToLinear(float x)
    {
        if (x <= 0f) return 0f;
        if (x >= 1f) return 1f;
        return (x < 0.04045f) ? x / 12.92f : MathF.Pow((x + 0.055f) / 1.055f, 2.4f);
    }

    private static float LinearToSrgb(float x)
    {
        if (x <= 0f) return 0f;
        if (x >= 1f) return 1f;
        return (x < 0.0031308f) ? x * 12.92f : MathF.Pow(x, 1f / 2.4f) * 1.055f - 0.055f;
    }







}

