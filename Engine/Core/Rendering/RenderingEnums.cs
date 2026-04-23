
namespace Engine.Core;


using System;
using System.Diagnostics;
using static Engine.Core.EngineMath;

public static partial class RenderingBackend
{





    public enum BlendingFactor : byte
    {
        Zero,
        One,
        SrcColor,
        OneMinusSrcColor,
        OneMinusDstColor,
        SrcAlpha,
        OneMinusSrcAlpha,
        DstAlpha,
        OneMinusDstAlpha,
        DstColor,
        SrcAlphaSaturate,
        ConstantColor,
        OneMinusConstantColor,
        ConstantAlpha,
        OneMinusConstantAlpha,
        Src1Alpha,
        Src1Color,
        OneMinusSrc1Color,
        OneMinusSrc1Alpha
    }



    public enum StencilOperation : byte
    {
        Keep,
        Zero,
        Replace,
        IncrementClamp,
        DecrementClamp,
        Invert,
        IncrementWrap,
        DecrementWrap
    }




    public enum BlendOperation : byte
    {
        Add,
        Subtract,
        ReverseSubtract,
        Min,
        Max
    }





    public enum ColorWriteMask : byte
    {
        None = 0,
        R = 1 << 0,
        G = 1 << 1,
        B = 1 << 2,
        A = 1 << 3,
        All = R | G | B | A
    }




    public enum CullMode : byte
    {
        Disabled,
        Back,
        Front,
    }


    public enum PrimitiveType : byte
    {
        Triangles,
        Lines
    }

    public enum PolygonMode : byte
    {
        Fill,
        Point,
        Line,
    }


    public enum DepthOrStencilFunction : byte
    {
        Always,
        Never,
        Less,
        Equal,
        LessOrEqual,
        Greater,
        NotEqual,
        GreaterOrEqual,
    }






    public enum TextureTypes : byte
    {
        Texture2D,
        TextureCubeMap,
        Texture3D
    }



    public enum TextureSamplerTypes : byte
    {
        /// <summary>
        /// For sampling standard 2D textures.
        /// </summary>
        Sampler2D,

        /// <summary>
        /// For sampling multi-sampled 2D textures.
        /// </summary>
        Sampler2DMS,

        /// <summary>
        /// For sampling 2D depth textures using a third depth comparison argument.
        /// </summary>
        Sampler2DShadow,

        /// <summary>
        /// For sampling standard cubemaps.
        /// </summary>
        SamplerCubeMap,

        /// <summary>
        /// For sampling standard 3D textures.
        /// </summary>
        Sampler3D
    }








    public enum TextureFormats : byte
    {
        /// <summary>
        /// Uncompressed 1 channel unsigned byte format.
        /// </summary>
        R8_UNORM,
        /// <summary>
        /// Uncompressed 2 channel unsigned byte format.
        /// </summary>
        RG8_UNORM,
        /// <summary>
        /// Uncompressed 3 channel unsigned byte format.  Will be padded by the engine to <see cref="RGBA8_UNORM"/> (A=255) during upload to backend.
        /// </summary>
        RGB8_UNORM,
        /// <summary>
        /// Uncompressed 4 channel unsigned byte format.
        /// </summary>
        RGBA8_UNORM,


        /// <summary>
        /// Uncompressed 1 channel half format.
        /// </summary>
        R16_SFLOAT,
        /// <summary>
        /// Uncompressed 2 channel half format.
        /// </summary>
        RG16_SFLOAT,
        /// <summary>
        /// Uncompressed 3 channel half format. Will be padded by the engine to <see cref="RGBA16_SFLOAT"/> (A=1.0) during upload to backend.
        /// </summary>
        RGB16_SFLOAT,
        /// <summary>
        /// Uncompressed 4 channel half format.
        /// </summary>
        RGBA16_SFLOAT,


        /// <summary>
        /// Block compressed 1 channel byte format.
        /// </summary>
        R8_BC4_UNORM,
        /// <summary>
        /// Block compressed 2 channel byte format.
        /// </summary>
        RG8_BC5_UNORM,
        /// <summary>
        /// Block compressed 4 channel byte format.
        /// </summary>
        RGBA8_BC7_UNORM,
        /// <summary>
        /// Block compressed 3 channel half format. Alpha is treated as 1.0.
        /// </summary>
        RGB16_BC6H_SFLOAT,


        /// <summary>
        /// Depth + stencil buffer texture.
        /// </summary>
        Depth24_Stencil8,

        /// <summary>
        /// Depth buffer texture.
        /// </summary>
        Depth32
    }





    public static uint GetTextureSizeBytes(Vector3<uint> dims, TextureFormats format, byte mips)
    {
        uint width = dims.X;
        uint height = dims.Y;
        uint depth = dims.Z == 0 ? 1u : dims.Z;

        return format switch
        {
            TextureFormats.R8_UNORM => width * height * depth * 1,
            TextureFormats.RG8_UNORM => width * height * depth * 2,
            TextureFormats.RGB8_UNORM => width * height * depth * 4,
            TextureFormats.RGBA8_UNORM => width * height * depth * 4,
            TextureFormats.R16_SFLOAT => width * height * depth * 2,
            TextureFormats.RG16_SFLOAT => width * height * depth * 4,
            TextureFormats.RGB16_SFLOAT => width * height * depth * 8,
            TextureFormats.RGBA16_SFLOAT => width * height * depth * 8,
            TextureFormats.R8_BC4_UNORM => GetBCSize(width, height, depth, 8),
            TextureFormats.RG8_BC5_UNORM => GetBCSize(width, height, depth, 16),
            TextureFormats.RGBA8_BC7_UNORM => GetBCSize(width, height, depth, 16),
            TextureFormats.RGB16_BC6H_SFLOAT => GetBCSize(width, height, depth, 16),

            TextureFormats.Depth24_Stencil8 => width * height * depth * 4,
            TextureFormats.Depth32 => width * height * depth * 4,

            _ => throw new NotSupportedException(),
        };

        static uint GetBCSize(uint width, uint height, uint depth, uint bytesPerBlock)
        {
            uint blockWidth = (width  + 3) / 4;
            uint blockHeight = (height + 3) / 4;

            return blockWidth * blockHeight * depth * bytesPerBlock;
        }
    }


    public static bool IsTextureFormatDepth(TextureFormats format) => format == TextureFormats.Depth32 || format == TextureFormats.Depth24_Stencil8;








    /// <summary>
    /// MSAA sample counts. 
    /// </summary>
    public enum MultiSampleCount : byte
    {
        Sample2 = 2,
        Sample4 = 4,
        Sample8 = 8,
        Sample16 = 16
    }


    [StackTraceHidden]
    [DebuggerHidden]
    [Conditional("DEBUG")]
    public static void Validate(this MultiSampleCount msaaCount)
    {
        if (((byte)msaaCount) < (byte)MultiSampleCount.Sample2 || ((byte)msaaCount) > (byte)MultiSampleCount.Sample16) 
            throw new InvalidOperationException("Invalid MSAA count");
    }





    public enum TextureWrapModes : byte
    {
        Repeat,
        ClampToEdge
    }

    public enum TextureFilters : byte
    {
        Nearest,
        Linear,
    }






    /// <summary>
    /// The format of one single component/element as supplied in the buffer data.
    /// </summary>
    public enum VertexAttributeBufferComponentFormat : byte
    {
        UByteNormalized,
        UByte,

        SByteNormalized,
        SByte,

        UShortNormalized,
        UShort,

        ShortNormalized,
        Short,

        UInt,
        Int,

        Half,
        Float,

        Double
    }

    public static byte GetVertexAttributeBufferComponentFormatSize(VertexAttributeBufferComponentFormat format)
        => format switch
        {
            VertexAttributeBufferComponentFormat.UByteNormalized => 1,
            VertexAttributeBufferComponentFormat.UByte => 1,
            VertexAttributeBufferComponentFormat.SByteNormalized => 1,
            VertexAttributeBufferComponentFormat.SByte => 1,

            VertexAttributeBufferComponentFormat.UShortNormalized => 2,
            VertexAttributeBufferComponentFormat.UShort => 2,
            VertexAttributeBufferComponentFormat.ShortNormalized => 2,
            VertexAttributeBufferComponentFormat.Short => 2,

            VertexAttributeBufferComponentFormat.Half => 2,
            VertexAttributeBufferComponentFormat.Float => 4,

            VertexAttributeBufferComponentFormat.UInt => 4,
            VertexAttributeBufferComponentFormat.Int => 4,
            VertexAttributeBufferComponentFormat.Double => 8,

            _ => throw new NotImplementedException(),
        };





    /// <summary>
    /// The final attribute format that the shader receives (or outputs).
    /// </summary>
    public enum ShaderAttributeBufferFinalFormat : byte
    {
        Float,
        Int,
        UInt,

        Vec2,
        Vec3,
        Vec4,

        IVec2,
        IVec3,
        IVec4,

        UVec2,
        UVec3,
        UVec4,

        BVec2,
        BVec3,
        BVec4,

        Mat2,
        Mat3,
        Mat4,

        Mat2x3,
        Mat2x4,

        Mat3x2,
        Mat3x4,

        Mat4x2,
        Mat4x3
    }



    /// <summary>
    /// The format of indices within an index buffer.
    /// </summary>
    public enum IndexBufferFormat : byte
    {
        UByte,
        UShort,
        UInt
    }










    public enum VertexAttributeScope : byte
    {
        PerVertex,
        PerInstance
    }



    /// <summary>
    /// Flags which indicate the binding capabilities of a buffer.
    /// </summary>
    public enum BufferUsageFlags : byte
    {
        Vertex = 1 << 0,
        Index = 1 << 1,
        Uniform = 1 << 2,
        Storage = 1 << 3,
    }


    /// <summary>
    /// Flags which indicate capacity for transfer access and mutability for a gpu resource.
    /// </summary>
    public enum ReadWriteFlags : byte
    {
        GPURead = 1 << 0,
        GPUWrite = 1 << 1,

        CPURead = 1 << 2,
        CPUWrite = 1 << 3
    }


}