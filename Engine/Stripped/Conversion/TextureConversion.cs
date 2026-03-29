
namespace Engine.Stripped;


using DdsKtxSharp;
using Engine.Core;
using StbImageSharp;
using System.Diagnostics;
using System.IO.MemoryMappedFiles;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using TinyEXR;
using TinyEXR.Native;
using static Engine.Core.EngineMath;
using static Engine.Core.RenderingBackend;



/// <summary>
/// Manages loading and processing of texture data.
/// <br/> All textures are converted to linear space if they aren't already linear.
/// </summary>
public static class TextureConversion
{








    /// <summary>
    /// Allows optimal texture loading steps.
    /// </summary>
    /// <param name="GenerateMips"></param>
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public record struct TextureLoadOptions(bool GenerateMips, TextureFormats ConvertTo, bool ConvertSrgbToLinear);


    /// <summary>
    /// Texture header data.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public readonly record struct TextureHeaderData(Vector3<uint> Dimensions, TextureFormats Format, TextureTypes Type);


    /// <summary>
    /// Final texture data.
    /// </summary>
    public struct TextureData
    {
        public Vector3<uint> Dimensions;
        public byte[][] Mips;
        public TextureFormats InternalImageDataFormat;
        public TextureTypes TextureType;
    }






    private enum TextureReaderBackend
    {
        StbImage,
        TinyExr
    }



    private static TextureReaderBackend GetTextureBackend(string extension)
    {

        if (extension == ".png" || extension == ".jpg" || extension == ".jpeg" || extension == ".bmp" || extension == ".tga" || extension == ".gif" || extension == ".hdr")
            return TextureReaderBackend.StbImage;

        if (extension == ".exr") 
            return TextureReaderBackend.TinyExr;

        throw new NotImplementedException($"Texture format '{extension}' unsupported");
    }






    public static unsafe TextureHeaderData InspectTextureHeader(byte[] src, string extension)
    {
        var backend = GetTextureBackend(extension);


        if (backend == TextureReaderBackend.StbImage)
        {
            var firstImageInfo = ImageInfo.FromStream(new MemoryStream(src));
            if (!firstImageInfo.HasValue) throw new InvalidDataException();
            var imgInfo = firstImageInfo.Value;

            return new TextureHeaderData(new Vector3<uint>((uint)imgInfo.Width, (uint)imgInfo.Height, 1), (int)imgInfo.ColorComponents switch
            {
                1 => TextureFormats.R8_UNORM,
                
                2 => TextureFormats.RG8_UNORM,
                
                3 => TextureFormats.RGB8_UNORM,

                4 => TextureFormats.RGBA8_UNORM,

                _ => throw new NotImplementedException(),
            },

            TextureTypes.Texture2D);
        }



        else if (backend == TextureReaderBackend.TinyExr)
        {

            EXRVersion v = default;
            EXRHeader header = default;
            EXRImage exrimage = default;


            Exr.InitEXRImage(ref exrimage);

            var err1 = Exr.ParseEXRHeaderFromMemory(src, ref v, ref header);

            if (err1 != ResultCode.Success) 
                throw new Exception(err1.ToString());



            var ret = new TextureHeaderData(new Vector3<uint>((uint)header.data_window.max_x + 1, (uint)header.data_window.max_y + 1, 1), header.num_channels switch
            {
                1 => TextureFormats.R16_SFLOAT,

                2 => TextureFormats.RG16_SFLOAT,

                3 => TextureFormats.RGB16_SFLOAT,

                4 => TextureFormats.RGBA16_SFLOAT,

                _ => throw new NotImplementedException(),
            },

            TextureTypes.Texture2D);


            Exr.FreeEXRHeader(ref header);

            return ret;

        }


        else 
            throw new Exception();

    }





    private static bool IsFormatHDR(TextureFormats format)
    {
        return format switch
        {
            TextureFormats.R8_UNORM => false,
            TextureFormats.RG8_UNORM => false,
            TextureFormats.RGB8_UNORM => false,
            TextureFormats.RGBA8_UNORM => false,

            TextureFormats.R16_SFLOAT => true,
            TextureFormats.RG16_SFLOAT => true,
            TextureFormats.RGB16_SFLOAT => true,
            TextureFormats.RGBA16_SFLOAT => true,

            TextureFormats.R8_BC4_UNORM => false,
            TextureFormats.RG8_BC5_UNORM => false,
            TextureFormats.RGBA8_BC7_UNORM => false,
            TextureFormats.RGB16_BC6H_SFLOAT => true,

            _ => throw new NotImplementedException(),
        };
    }

    private static bool IsFormatCompressed(TextureFormats format)
    {
        return format switch
        {
            TextureFormats.R8_UNORM => false,
            TextureFormats.RG8_UNORM => false,
            TextureFormats.RGB8_UNORM => false,
            TextureFormats.RGBA8_UNORM => false,

            TextureFormats.R16_SFLOAT => false,
            TextureFormats.RG16_SFLOAT => false,
            TextureFormats.RGB16_SFLOAT => false,
            TextureFormats.RGBA16_SFLOAT => false,

            TextureFormats.R8_BC4_UNORM => true,
            TextureFormats.RG8_BC5_UNORM => true,
            TextureFormats.RGBA8_BC7_UNORM => true,
            TextureFormats.RGB16_BC6H_SFLOAT => true,

            _ => throw new NotImplementedException(),
        };
    }


    private static byte GetFormatChannelCount(TextureFormats format)
    {
        return format switch
        {
            TextureFormats.R8_UNORM => 1,
            TextureFormats.RG8_UNORM => 2,
            TextureFormats.RGB8_UNORM => 3,
            TextureFormats.RGBA8_UNORM => 4,

            TextureFormats.R16_SFLOAT => 1,
            TextureFormats.RG16_SFLOAT => 2,
            TextureFormats.RGB16_SFLOAT => 3,
            TextureFormats.RGBA16_SFLOAT => 4,

            TextureFormats.R8_BC4_UNORM => 1,
            TextureFormats.RG8_BC5_UNORM => 2,
            TextureFormats.RGBA8_BC7_UNORM => 4,
            TextureFormats.RGB16_BC6H_SFLOAT => 3,

            _ => throw new NotImplementedException(),
        };
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





    public enum EncoderBackend
    {
        Compressonator,
        NVTT3
    }



    public static async Task<TextureData> ConvertTextureToRuntimeFormat(Loading.Bytes src, string extension, TextureHeaderData ImageHeader, TextureLoadOptions options, EncoderBackend encoderBackend = EncoderBackend.Compressonator)
    {

        var stopwatch = new Stopwatch();
        stopwatch.Start();



        var TargetIsHDR = IsFormatHDR(options.ConvertTo);
        var TargetIsCompressed = IsFormatCompressed(options.ConvertTo);
        var TargetChannelCount = GetFormatChannelCount(options.ConvertTo);

        var IsHDR = IsFormatHDR(ImageHeader.Format);
        var IsCompressed = IsFormatCompressed(ImageHeader.Format);
        var ChannelCount = GetFormatChannelCount(ImageHeader.Format);


        if (IsCompressed)
            throw new NotImplementedException();







        var backend = GetTextureBackend(extension);

        int width = (int)ImageHeader.Dimensions.X;
        int height = (int)ImageHeader.Dimensions.Y;
        int pixelCount = width * height;



        ArrayPools.ArrayFromPool<Half> finalBuffer = default;



        Vector4<Half> ConstantColor = default;




        bool IsConstantColor = true;


        if (backend == TextureReaderBackend.StbImage)
        {
            unsafe
            {

                StbImage.stbi_set_flip_vertically_on_load(1);

                if (IsHDR)
                {
                    var tex = ImageResultFloat.FromMemory(src.ByteArray);
                    src.Dispose();


                    int srcComp = (int)tex.Comp;

                    finalBuffer = ArrayPools.RentArrayFromPool<Half>(pixelCount * TargetChannelCount);

                    fixed (float* srcPtr = tex.Data)
                    fixed (Half* dstBase = finalBuffer.Ref)
                    {
                        float* firstPixel = stackalloc float[TargetChannelCount];
                        for (int c = 0; c < TargetChannelCount; c++)
                        {
                            if (c < srcComp)
                                firstPixel[c] = options.ConvertSrgbToLinear && c < 3
                                    ? SrgbToLinear(srcPtr[c])
                                    : srcPtr[c];
                            else
                                firstPixel[c] = (c == 3) ? 1f : 0f;
                        }


                        for (int c = 0; c < TargetChannelCount; c++)
                        {
                            Half* dst = dstBase + (c * pixelCount);

                            for (int i = 0; i < pixelCount; i++)
                            {
                                float value;

                                if (c < srcComp)
                                {
                                    value = srcPtr[i * srcComp + c];
                                    if (options.ConvertSrgbToLinear && c < 3)
                                        value = SrgbToLinear(value);
                                }
                                else
                                {
                                    value = (c == 3) ? 1f : 0f;
                                }

                                dst[i] = (Half)value;

                                if (IsConstantColor && Math.Abs(value - firstPixel[c]) > 1e-6f)
                                    IsConstantColor = false;
                            }
                        }
                    }

                    tex = null;
                }


                else
                {
                    var tex = ImageResult.FromMemory(src.ByteArray);
                    src.Dispose();



                    int srcComp = (int)tex.Comp;

                    finalBuffer = ArrayPools.RentArrayFromPool<Half>(pixelCount * TargetChannelCount);


                    fixed (byte* srcPtr = tex.Data)
                    fixed (Half* dstBase = finalBuffer.Ref)
                    {
                        float* firstPixel = stackalloc float[TargetChannelCount];
                        for (int c = 0; c < TargetChannelCount; c++)
                        {
                            if (c < srcComp)
                                firstPixel[c] = options.ConvertSrgbToLinear && c < 3
                                    ? SrgbToLinear(srcPtr[c])
                                    : srcPtr[c];
                            else
                                firstPixel[c] = (c == 3) ? 1f : 0f;
                        }


                        for (int c = 0; c < TargetChannelCount; c++)
                        {
                            Half* dst = dstBase + (c * pixelCount);

                            for (int i = 0; i < pixelCount; i++)
                            {
                                float value;

                                if (c < srcComp)
                                {
                                    value = srcPtr[i * srcComp + c] / 255f;
                                    if (options.ConvertSrgbToLinear && c < 3)
                                        value = SrgbToLinear(value);
                                }
                                else
                                {
                                    value = (c == 3) ? 1f : 0f;
                                }

                                dst[i] = (Half)value;

                                if (IsConstantColor && Math.Abs(value - firstPixel[c]) > 1e-6f)
                                    IsConstantColor = false;
                            }
                        }
                    }

                    tex = null;
                }
            }

        }






        else if (backend == TextureReaderBackend.TinyExr)
        {

            unsafe
            {

                EXRVersion v = default;
                EXRHeader header = default;
                EXRImage exrimage = default;



                Exr.InitEXRImage(ref exrimage);


                var err1 = Exr.ParseEXRHeaderFromMemory(src.ByteArray, ref v, ref header);
                if (err1 != ResultCode.Success)
                    throw new Exception(err1.ToString());


                for (int c = 0; c < header.num_channels; c++)
                    header.requested_pixel_types[c] = (int)ExrPixelType.Half;



                var err2 = Exr.LoadEXRImageFromMemory(ref exrimage, ref header, src.ByteArray);
                if (err2 != ResultCode.Success)
                    throw new Exception(err2.ToString());


                src.Dispose();




                int rIndex = -1, gIndex = -1, bIndex = -1, aIndex = -1;

                for (int c = 0; c < header.num_channels; c++)
                {
                    string name = new string(header.channels[c].name);
                    switch (name)
                    {
                        case "R": rIndex = c; break;
                        case "G": gIndex = c; break;
                        case "B": bIndex = c; break;
                        case "A": aIndex = c; break;
                    }
                }



                finalBuffer = ArrayPools.RentArrayFromPool<Half>(pixelCount * TargetChannelCount);

                Half* rPtr = rIndex >= 0 ? (Half*)exrimage.images[rIndex] : null;
                Half* gPtr = gIndex >= 0 ? (Half*)exrimage.images[gIndex] : null;
                Half* bPtr = bIndex >= 0 ? (Half*)exrimage.images[bIndex] : null;
                Half* aPtr = aIndex >= 0 ? (Half*)exrimage.images[aIndex] : null;


                float* candidatePixel = stackalloc float[TargetChannelCount];
                for (int c = 0; c < TargetChannelCount; c++)
                {
                    Half* srcPtr = c switch
                    {
                        0 => rPtr,
                        1 => gPtr,
                        2 => bPtr,
                        3 => aPtr,
                        _ => null
                    };

                    if (srcPtr != null)
                    {
                        float value = (float)srcPtr[0];
                        if (options.ConvertSrgbToLinear && c < 3) value = SrgbToLinear(value);
                        if (!TargetIsHDR) value = MathF.Min(MathF.Max(value, 0f), 1f);
                        candidatePixel[c] = value;
                    }
                    else
                    {
                        candidatePixel[c] = (c == 3) ? 1f : 0f;
                    }
                }



                fixed (Half* dstBase = finalBuffer.Ref)
                {
                    for (int c = 0; c < TargetChannelCount; c++)
                    {
                        Half* dst = dstBase + (c * pixelCount);
                        Half* srcPtr = c switch
                        {
                            0 => rPtr,
                            1 => gPtr,
                            2 => bPtr,
                            3 => aPtr,
                            _ => null
                        };

                        if (srcPtr != null)
                        {
                            for (int i = 0; i < pixelCount; i++)
                            {
                                float value = (float)srcPtr[i];
                                if (options.ConvertSrgbToLinear && c < 3) value = SrgbToLinear(value);
                                if (!TargetIsHDR) value = MathF.Min(MathF.Max(value, 0f), 1f);

                                dst[i] = (Half)value;

                                if (IsConstantColor && Math.Abs(value - candidatePixel[c]) > 1e-6f)
                                    IsConstantColor = false;
                            }
                        }
                        else
                        {
                            Half fill = (c == 3) ? (Half)1f : (Half)0f;
                            for (int i = 0; i < pixelCount; i++)
                            {
                                dst[i] = fill;
                                if (IsConstantColor && Math.Abs(((float)fill) - candidatePixel[c]) > 1e-6f)
                                    IsConstantColor = false;
                            }
                        }
                    }
                }



                Exr.FreeEXRHeader(ref header);
                Exr.FreeEXRImage(ref exrimage);
                header = default;
                exrimage = default;


            }

        }



        //we can just store 1x1 for completely constant colors
        //ideally this is caught on the image authoring end but catching above is relatively cheap 

        if (IsConstantColor)
        {
            width = 1;
            height = 1;

            Span<Half> constColor = stackalloc Half[TargetChannelCount];
            for (int c = 0; c < TargetChannelCount; c++)
                constColor[c] = finalBuffer[c * pixelCount];

            constColor.CopyTo(finalBuffer.Ref);
        }





        string texpathtemp = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.exr");
        string texpathtempOut = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.ktx");

        await File.WriteAllBytesAsync(texpathtemp, GetInMemoryExr(width, height, TargetChannelCount, finalBuffer.Ref));

        finalBuffer.Return();
        finalBuffer = default;



        Debug.Print($"Data -> final exr took {stopwatch.Elapsed.TotalSeconds} seconds");



        await TextureCLILock.WaitAsync();

        stopwatch.Restart();




        if (encoderBackend == EncoderBackend.Compressonator)
        {
            var cmpFormat = options.ConvertTo switch
            {
                TextureFormats.R8_UNORM => "R_8",
                TextureFormats.R16_SFLOAT => "R_16F",

                TextureFormats.RG16_SFLOAT => "RG_16F",

                TextureFormats.RGB8_UNORM => "RGB_888",
                TextureFormats.RGBA8_UNORM => "RGBA_8888",
                TextureFormats.RGBA16_SFLOAT => "RGBA_16F",

                TextureFormats.R8_BC4_UNORM => "BC4",
                TextureFormats.RG8_BC5_UNORM => "BC5",
                TextureFormats.RGBA8_BC7_UNORM => "BC7",
                TextureFormats.RGB16_BC6H_SFLOAT => "BC6H",

                _ => throw GetNotSupportedException()
            };


            int mipLevels = options.GenerateMips
                ? (int)Math.Floor(Math.Log2(Math.Max(width, height))) + 1
                : 1;

            await RunProcess.Run(
                "compressonatorcli",
                $"-fd {cmpFormat} \"{texpathtemp}\" \"{texpathtempOut}\" " +
                $"-EncodeWith GPU " +
                $"-mipsize 1 " +         
                $"-Quality 0.05 " +
                $"-Performance 1"
            );
        }



        if (encoderBackend == EncoderBackend.NVTT3)
        {
            var nvttFormat = options.ConvertTo switch
            {
                TextureFormats.R8_UNORM => "a8",
                TextureFormats.R16_SFLOAT => "r16f",

                //TextureFormats.RG8_UNORM => "rgb8",       
                TextureFormats.RG16_SFLOAT => "rg16f",

                TextureFormats.RGB8_UNORM => "rgb8",
                //TextureFormats.RGB16_SFLOAT => "rgba16f",  

                TextureFormats.RGBA8_UNORM => "rgba8",
                TextureFormats.RGBA16_SFLOAT => "rgba16f",

                TextureFormats.R8_BC4_UNORM => "bc4",
                TextureFormats.RG8_BC5_UNORM => "bc5",
                TextureFormats.RGBA8_BC7_UNORM => "bc7",
                TextureFormats.RGB16_BC6H_SFLOAT => "bc6s",

                _ => throw GetNotSupportedException()
            };


            await RunProcess.Run(
                "nvtt_export", 
                $"{texpathtemp} --format {nvttFormat} --output {texpathtempOut} --mip-filter box --export-transfer-function 2 --quality fastest"
                );
        }




        Exception GetNotSupportedException()
            => new NotImplementedException($"Conversion to the exact given format '{options.ConvertTo.ToString()}' is not implemented and/or is not supported by one or more components of the texture toolchain");






        Debug.Print($"Texture encoding (via {encoderBackend}) took {stopwatch.Elapsed.TotalSeconds} seconds");
        stopwatch.Restart();


        TextureCLILock.Release();





        unsafe
        {
            var mmf = MemoryMappedFile.CreateFromFile(texpathtempOut, FileMode.Open);
            var accessor = mmf.CreateViewAccessor();

            byte* filePtr = null;
            accessor.SafeMemoryMappedViewHandle.AcquirePointer(ref filePtr);

            DdsKtx.ddsktx_texture_info info;

            if (!DdsKtx.ddsktx_parse(&info, filePtr, (int)accessor.Capacity))
                throw new Exception(DdsKtx.LastError);




            int mipCount = info.num_mips;

            byte[][] mipLevels = new byte[mipCount][];

            for (int mip = 0; mip < mipCount; mip++)
            {
                DdsKtx.ddsktx_sub_data sub;

                DdsKtx.ddsktx_get_sub(
                    &info,
                    &sub,
                    filePtr,
                    (int)accessor.Capacity,
                    0, 0, mip);


                var arr = new byte[sub.size_bytes];
                Buffer.MemoryCopy(sub.buff, Unsafe.AsPointer(ref arr[0]), sub.size_bytes, sub.size_bytes);
                mipLevels[mip] = arr;
            }



            accessor.SafeMemoryMappedViewHandle.ReleasePointer();
            accessor.Dispose();
            mmf.Dispose();


            File.Delete(texpathtemp);
            File.Delete(texpathtempOut);



            Debug.Print($"Ktx extraction and finalization took {stopwatch.Elapsed.TotalSeconds} seconds");
            stopwatch.Restart();



            return new TextureData()
            {
                Mips = mipLevels,

                InternalImageDataFormat = options.ConvertTo,
                TextureType = ImageHeader.Type,

                Dimensions = new Vector3<uint>((uint)width,(uint)height,1)
            };
        }


    }


    private static readonly SemaphoreSlim TextureCLILock = new(1,1);
    










    private static unsafe byte[] GetInMemoryExr(
        int width,
        int height,
        int channelCount,
        Half[] data
    )
    {
        if (channelCount < 1 || channelCount > 4)
            throw new ArgumentException("Only 1–4 channels supported.");

        int numPixels = width * height;



        fixed (byte* basePtr = MemoryMarshal.AsBytes<Half>(data))
        {

            IntPtr[] channelPtrsManaged = new IntPtr[channelCount];


            int elementSize = sizeof(Half);



            for (int c = 0; c < channelCount; c++)
                channelPtrsManaged[channelCount-1-c] = (IntPtr)(basePtr + (c * numPixels * elementSize));





            fixed (IntPtr* channelPtrs = channelPtrsManaged)
            {



                EXRImage image = default;
                Exr.InitEXRImage(ref image);

                image.num_channels = channelCount;
                image.width = width;
                image.height = height;
                image.images = (byte**)channelPtrs;




                EXRHeader header = default;
                Exr.InitEXRHeader(ref header);

                header.num_channels = channelCount;

                header.compression_type = (int)CompressionType.None;
                header.line_order = 0;



                int* pixelTypes = stackalloc int[channelCount];
                int* requestedTypes = stackalloc int[channelCount];

                for (int i = 0; i < channelCount; i++)
                {
                    pixelTypes[i] = (int)ExrPixelType.Half;
                    requestedTypes[i] = (int)ExrPixelType.Half;
                }

                header.pixel_types = pixelTypes;
                header.requested_pixel_types = requestedTypes;




                EXRChannelInfo* channels = stackalloc EXRChannelInfo[channelCount];
                header.channels = channels;

                string[] names = channelCount switch
                {
                    1 => ["R"],
                    2 => ["R", "G"],
                    3 => ["R", "G", "B"],
                    4 => ["R", "G", "B", "A"],
                    _ => throw new NotImplementedException(),
                };




                Span<byte> nameBytes = stackalloc byte[256];

                for (int c = 0; c < channelCount; c++)
                {
                    nameBytes.Clear();

                    var strBytes = System.Text.Encoding.ASCII.GetBytes(names[c]);
                    strBytes.CopyTo(nameBytes);

                    // Copy into fixed buffer
                    fixed (byte* src = nameBytes)
                    {
                        Buffer.MemoryCopy(
                            src,
                            channels[c].name,
                            256,
                            strBytes.Length + 1
                        );
                    }
                }


                //SaveExrImageToFile has unreliable timing/locking

                var bytes = Exr.SaveEXRImageToMemory(ref image, ref header);



                return bytes;

            }
        }




    }

}

