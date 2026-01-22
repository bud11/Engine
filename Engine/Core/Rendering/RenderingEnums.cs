
namespace Engine.Core;


using System;


public static partial class Rendering
{

    public enum ShaderFormat : byte
    {
        SPIRV,
        GLSL,
        HLSL
    }






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





    [Flags]
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
        Front,
        Back,
        Disabled
    }

    public enum PrimitiveType : byte
    {
        Triangles,
        Lines
    }

    public enum PolygonMode : byte
    {
        Point,
        Line,
        Fill,
    }


    public enum DepthOrStencilFunction : byte
    {
        Never,
        Less,
        Equal,
        LessOrEqual,
        Greater,
        NotEqual,
        GreaterOrEqual,
        Always,
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
        Texture2D,

        /// <summary>
        /// For sampling 2D depth textures using a third depth comparison argument.
        /// </summary>
        Texture2DShadow,

        /// <summary>
        /// For sampling standard cubemaps.
        /// </summary>
        TextureCubeMap,

        /// <summary>
        /// For sampling standard 3D textures.
        /// </summary>
        Texture3D
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
        BC4,
        /// <summary>
        /// Block compressed 2 channel byte format.
        /// </summary>
        BC5,
        /// <summary>
        /// Block compressed 4 channel byte format.
        /// </summary>
        BC7,
        /// <summary>
        /// Block compressed 3 channel half format. Alpha is treated as 1.0.
        /// </summary>
        BC6H_SFLOAT,


        /// <summary>
        /// Depth + stencil buffer texture. D24 S8.
        /// </summary>
        DepthStencil,
    }




    public static uint GetBytesPerLayer(
        TextureFormats format,
        uint width,
        uint height)
    {
        return format switch
        {
            TextureFormats.R8_UNORM => width * height * 1,
            TextureFormats.RG8_UNORM => width * height * 2,
            TextureFormats.RGB8_UNORM or TextureFormats.RGBA8_UNORM => width * height * 4,
            TextureFormats.R16_SFLOAT => width * height * 2,
            TextureFormats.RG16_SFLOAT => width * height * 4,
            TextureFormats.RGB16_SFLOAT or TextureFormats.RGBA16_SFLOAT => width * height * 8,

            TextureFormats.DepthStencil => width * height * 4,
                                                              
            TextureFormats.BC4 => GetBCSize(width, height, 8),
            TextureFormats.BC5 or TextureFormats.BC6H_SFLOAT or TextureFormats.BC7 => GetBCSize(width, height, 16),
            _ => throw new Exception(),
        };

        static uint GetBCSize(uint width, uint height, uint bytesPerBlock)
        {
            uint blocksX = (width  + 3) / 4;
            uint blocksY = (height + 3) / 4;
            return blocksX * blocksY * bytesPerBlock;
        }
    }









    /// <summary>
    /// MSAA sample counts.
    /// </summary>
    public enum FramebufferSampleCount : byte
    {
        Sample1,
        Sample2,
        Sample4,
        Sample8,
        Sample16
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
        Byte,
        Half,
        Float
    }


    /// <summary>
    /// The final attribute format that the shader recieves (or outputs).
    /// </summary>
    public enum ShaderAttributeBufferFinalFormat : byte
    {
        Float,
        UInt,

        Vec2,
        Vec3,
        Vec4,

        UVec2,
        UVec3,
        UVec4,

        Mat3,
        Mat4,
    }


    public enum VertexAttributeScope
    {
        PerVertex,
        PerInstance
    }



    [Flags]
    public enum SSBOReadWriteFlags : byte
    {
        Read = 1 << 0,
        Write = 1 << 1,
    }



}