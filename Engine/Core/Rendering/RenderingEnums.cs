
namespace Engine.Core;


using System;


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
        /// Depth + stencil buffer texture. D24 S8.
        /// </summary>
        DepthStencil,
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



    /// <summary>
    /// Flags which indicate the binding capabilities of a buffer.
    /// </summary>
    [Flags]
    public enum BufferUsageFlags : byte
    {
        Vertex = 1 << 0,
        Index = 1 << 1,
        Uniform = 1 << 2,
        Storage = 1 << 3,
    }


    /// <summary>
    /// Flags which indicate capacity of access and mutability for a gpu resource.
    /// </summary>
    [Flags]
    public enum ReadWriteFlags : byte
    {
        GPURead = 1 << 0,
        GPUWrite = 1 << 1,

        CPURead = 1 << 2,
        CPUWrite = 1 << 3
    }


}