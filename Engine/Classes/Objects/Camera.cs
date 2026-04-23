


namespace Engine.GameObjects;

using Engine.Attributes;
using Engine.Core;
using System.Buffers;
using System.Numerics;
using static Engine.Core.EngineMath;
using static Engine.Core.Rendering;
using static Engine.Core.RenderingBackend;



/// <summary>
/// A camera class with controllable properties and arbitrary buffers.
/// </summary>
/// 
public partial class Camera : GameObject
{



    /// <summary>
    /// The primary reference resolution for this camera's buffers/textures.
    /// </summary>
    [Indexable]
    public Vector2<uint> Resolution;



    /// <summary>
    /// Whether any involved buffers/textures should be createad as cubemaps.
    /// </summary>
    [Indexable]
    public bool UseCubeMaps = false;




    /// <summary>
    /// Whether to use perspective or orthographic projection.
    /// </summary>
    [Indexable]
    public bool Perspective = true;

    /// <summary>
    /// The field of view when <see cref="Perspective"/> is enabled.
    /// </summary>
    [Indexable]
    public float PerspectiveFieldOfView = 45f;  

    /// <summary>
    /// The scale when <see cref="Perspective"/> is disabled.
    /// </summary>
    [Indexable]
    public float OrthographicScale = 1f;  

    /// <summary>
    /// The near clip plane distance.
    /// </summary>
    [Indexable]
    public float Near = 0.01f;

    /// <summary>
    /// The far clip plane distance.
    /// </summary>
    [Indexable]
    public float Far = 1000f;




    public override void Init()
    {
        Refresh();

        base.Init();
    }




    public readonly ThreadSafeEventAction OnRefreshChange = new();


    public void Refresh()
    {
        bool callrefresh = false;

        foreach (var buf in CameraFrameBuffers)
        {
            if (buf.Value.Refresh()) 
                callrefresh = true;
        }

        if (callrefresh)
            OnRefreshChange.Invoke();
    }






    public Matrix4x4 GetProjectionMatrix()
    {
        if (Perspective) return Matrix4x4.CreatePerspectiveFieldOfView(DegToRad(PerspectiveFieldOfView), Resolution.X / (float)Resolution.Y, Near, Far);

        return Matrix4x4.CreateOrthographic(OrthographicScale * (Resolution.X / (float)Resolution.Y), OrthographicScale, Near, Far);
    }




    private static readonly Matrix4x4[] CubemapDirections =
        [
            Matrix4x4.Identity,
            Matrix4x4.CreateLookAt(Vector3.Zero,-Vector3.UnitX, Vector3.UnitY),

            Matrix4x4.CreateLookAt(Vector3.Zero,-Vector3.UnitY, -Vector3.UnitZ),
            Matrix4x4.CreateLookAt(Vector3.Zero,Vector3.UnitY, Vector3.UnitZ),

            Matrix4x4.CreateLookAt(Vector3.Zero,-Vector3.UnitZ, Vector3.UnitY),
            Matrix4x4.CreateLookAt(Vector3.Zero,Vector3.UnitZ, Vector3.UnitY),
        ];


    public Matrix4x4 GetViewMatrix(byte cubemapFacing = 0)
    {
        var mat = Matrix4x4.Multiply(GlobalTransform, CubemapDirections[cubemapFacing]);

        return Matrix4x4.CreateLookTo(mat.Translation, mat.GetOrientationZ(), mat.GetOrientationY());
    }






    private record class CameraFrameBuffer(
            Camera Owner, 
            CameraFrameBuffer.SizeMode ResolutionMode,
            Vector2 ResolutionOrFactor,
            byte ColorTargetCount,
            TextureFormats ColorTargetFormat,
            bool CreateDepthStencil,
            MultiSampleCount SampleCount,
            TextureMipCount MipCount
        )
    {

        public enum SizeMode
        {
            Fixed,
            Multiplier
        }



        public LogicalFrameBufferObject FrameBuffer { get; private set; }


        private readonly IFramebufferAttachment[] ColorTargets = new IFramebufferAttachment[ColorTargetCount];
        private IFramebufferAttachment DepthStencilTarget;


        /// <summary>
        /// Recreates the framebuffer and its texture targets if necessary.
        /// </summary>
        public bool Refresh()
        {
            var requiredRes = ResolutionMode == SizeMode.Fixed ? (Vector2<uint>)ResolutionOrFactor : (Vector2<uint>)((Vector2)Owner.Resolution * ResolutionOrFactor);
            var texType = Owner.UseCubeMaps ? TextureTypes.TextureCubeMap : TextureTypes.Texture2D;


            if (FrameBuffer == null || requiredRes != FrameBuffer.Size)
            {
                if (ColorTargetCount != 0)
                {
                    for (int i = 0; i < ColorTargetCount; i++)
                        ColorTargets[i] = SampleCount != 0 ? BackendTexture2DMSAttachmentReference.CreateAttachment(requiredRes, ColorTargetFormat, SampleCount) : BackendTexture2DReference.CreateAttachment(requiredRes, ColorTargetFormat, MipCount);
                }

                if (CreateDepthStencil)
                    DepthStencilTarget = SampleCount != 0 ? BackendTexture2DMSAttachmentReference.CreateAttachment(requiredRes, TextureFormats.Depth24_Stencil8, SampleCount) : BackendTexture2DReference.CreateAttachment(requiredRes, TextureFormats.Depth24_Stencil8, MipCount);


                FrameBuffer = LogicalFrameBufferObject.Create(ColorTargets, DepthStencilTarget);

                return true;
            }

            return false;
        }
    }




    private readonly Dictionary<string, CameraFrameBuffer> CameraFrameBuffers = new();





    /// <summary>
    /// Creates a fixed-size named framebuffer owned by this camera.
    /// </summary>
    public unsafe void CreateFixedSizeCameraFrameBuffer(string name,
                                                        Vector2<uint> resolution,

                                                        byte colorTargetCount,
                                                        TextureFormats colorTargetFormat,

                                                        bool createDepthStencil,

                                                        TextureMipCount mipCount)
    {
        CameraFrameBuffers[name] = new CameraFrameBuffer(this, CameraFrameBuffer.SizeMode.Fixed, (Vector2)resolution, colorTargetCount, colorTargetFormat, createDepthStencil, 0, mipCount);
    }

    /// <summary>
    /// Creates a named framebuffer owned by this camera, which will be sized according to <see cref="Resolution"/> * <paramref name="scalingFactor"/>.
    /// </summary>
    /// <param name="name"></param>
    /// <param name="scalingFactor"></param>
    public unsafe void CreateDynamicSizeCameraFrameBuffer(string name,
                                                          Vector2 scalingFactor,

                                                          byte colorTargetCount,
                                                          TextureFormats colorTargetFormat,

                                                          bool createDepthStencil,
                                                          
                                                          TextureMipCount mipCount)

    {
        CameraFrameBuffers[name] = new CameraFrameBuffer(this, CameraFrameBuffer.SizeMode.Multiplier, scalingFactor, colorTargetCount, colorTargetFormat, createDepthStencil, 0, mipCount);
    }






    /// <summary>
    /// Creates a fixed-size named framebuffer owned by this camera.
    /// </summary>
    public unsafe void CreateMultiSampleFixedSizeCameraFrameBuffer(string name,
                                                        Vector2<uint> resolution,

                                                        byte colorTargetCount,
                                                        TextureFormats colorTargetFormat,

                                                        bool createDepthStencil,

                                                        MultiSampleCount sampleCount)
    {
        sampleCount.Validate();
        CameraFrameBuffers[name] = new CameraFrameBuffer(this, CameraFrameBuffer.SizeMode.Fixed, (Vector2)resolution, colorTargetCount, colorTargetFormat, createDepthStencil, sampleCount, 1);
    }

    /// <summary>
    /// Creates a named framebuffer owned by this camera, which will be sized according to <see cref="Resolution"/> * <paramref name="scalingFactor"/>.
    /// </summary>
    /// <param name="name"></param>
    /// <param name="scalingFactor"></param>
    public unsafe void CreateMultiSampleDynamicSizeCameraFrameBuffer(string name,
                                                          Vector2 scalingFactor,

                                                          byte colorTargetCount,
                                                          TextureFormats colorTargetFormat,

                                                          bool createDepthStencil,

                                                          MultiSampleCount sampleCount)

    {
        sampleCount.Validate();
        CameraFrameBuffers[name] = new CameraFrameBuffer(this, CameraFrameBuffer.SizeMode.Multiplier, scalingFactor, colorTargetCount, colorTargetFormat, createDepthStencil, sampleCount, 1);
    }









    public LogicalFrameBufferObject GetCameraFrameBuffer(string Name) 
        => CameraFrameBuffers[Name].FrameBuffer;

    public void FreeCameraFrameBuffer(string Name)
        => CameraFrameBuffers?.Remove(Name);





    public readonly record struct DrawObjectAndDistance(DrawObject Object, float Distance);
    public readonly record struct CameraMatrices(Matrix4x4 ProjectionMatrix, Matrix4x4 ViewMatrix);




    public static unsafe ArrayFromPool<DrawObject> CullDrawObjectList(IList<DrawObject> array, in Matrix4x4 viewMatrix, in Matrix4x4 projectionMatrix)
    {
        var ret = ArrayFromPool<DrawObject>.Rent(array.Count);


        var frustum = EngineMath.ExtractFrustumPlanes(viewMatrix * projectionMatrix);

        var count = 0;

        for (int i = 0; i < array.Count; i++)
        {
            var entry = array[i];

            if (entry.IsVisibleInTree() && (!entry.IsEnableCameraCullingInTree() || frustum.ContainsAABB(entry.GetOrRecalculateCachedGlobalAABB())))
            {
                ret[count] = entry;
                count++;
            }
        }

        ret.Length = count;

        return ret;
    }



    public static unsafe ArrayFromPool<DrawObjectAndDistance> CullDrawObjectList(ReadOnlySpan<DrawObjectAndDistance> array, in Matrix4x4 viewMatrix, in Matrix4x4 projectionMatrix)
    {
        var ret = ArrayFromPool<DrawObjectAndDistance>.Rent(array.Length);


        var frustum = EngineMath.ExtractFrustumPlanes(viewMatrix * projectionMatrix);

        var count = 0;

        for (int i = 0; i < array.Length; i++)
        {
            var entry = array[i];

            if (entry.Object.IsVisibleInTree() && (!entry.Object.IsEnableCameraCullingInTree() || frustum.ContainsAABB(entry.Object.GetOrRecalculateCachedGlobalAABB())))
            {
                ret[count] = entry;
                count++;
            }
        }

        ret.Length = count;

        return ret;
    }





    public enum CameraDrawSortMode
    {
        Unordered,
        NearToFar,
        FarToNear,
    }




    public static ArrayFromPool<DrawObjectAndDistance> GetDrawObjectSquaredDistances(IList<DrawObject> list, Vector3 fromPosition)
    {
        var ret = ArrayFromPool<DrawObjectAndDistance>.Rent(list.Count);

        for (int i = 0; i < list.Count; i++)
        {
            var obj = list[i];
            ret[i] = new DrawObjectAndDistance(obj, obj.GlobalPosition.DistanceToSquared(fromPosition));
        }

        return ret;
    }




    public static ArrayFromPool<DrawObjectAndDistance> SortDrawObjects(
        ReadOnlySpan<DrawObjectAndDistance> arr,
        CameraDrawSortMode order)
    {

        int n = arr.Length;


        if (n <= 1 || order == CameraDrawSortMode.Unordered)
            return ArrayFromPool<DrawObjectAndDistance>.RentClone(arr);



        if (n < 1_500)
        {
            var result = ArrayFromPool<DrawObjectAndDistance>.RentClone(arr);

            Array.Sort(
                result.Array,
                0,
                result.Length,
                Comparer<DrawObjectAndDistance>.Create(
                    static (a, b) => a.Distance.CompareTo(b.Distance)));

            if (order == CameraDrawSortMode.FarToNear)
                Array.Reverse(result.Array, 0, result.Length);


            return result;
        }



        // Radix sort below for large counts




        DrawObjectAndDistance[] bufferA =
            ArrayPool<DrawObjectAndDistance>.Shared.Rent(n);

        DrawObjectAndDistance[] bufferB =
            ArrayPool<DrawObjectAndDistance>.Shared.Rent(n);



        uint[] keys = ArrayPool<uint>.Shared.Rent(n);
        uint[] keysB = ArrayPool<uint>.Shared.Rent(n);



        try
        {
            Span<int> count = stackalloc int[256];

            arr.CopyTo(bufferA);

            var src = bufferA;
            var dst = bufferB;
            var ksrc = keys;
            var kdst = keysB;

            for (int i = 0; i < n; i++)
            {
                uint x = BitConverter.SingleToUInt32Bits(src[i].Distance);
                ksrc[i] = (x & 0x80000000u) != 0 ? ~x : x ^ 0x80000000u;
            }

            const int RADIX = 256;

            for (int shift = 0; shift < 32; shift += 8)
            {
                for (int i = 0; i < RADIX; i++)
                    count[i] = 0;

                for (int i = 0; i < n; i++)
                    count[(int)((ksrc[i] >> shift) & 255)]++;

                int sum = 0;
                for (int i = 0; i < RADIX; i++)
                {
                    int c = count[i];
                    count[i] = sum;
                    sum += c;
                }

                for (int i = 0; i < n; i++)
                {
                    uint k = ksrc[i];
                    int bucket = (int)((k >> shift) & 255);
                    int pos = count[bucket]++;

                    dst[pos] = src[i];
                    kdst[pos] = k;
                }

                (src, dst) = (dst, src);
                (ksrc, kdst) = (kdst, ksrc);
            }

            var result = ArrayFromPool<DrawObjectAndDistance>.Rent(n);

            src.AsSpan(0, n).CopyTo(result.Array);

            if (order == CameraDrawSortMode.FarToNear)
                Array.Reverse(result.Array, 0, result.Length);

            return result;
        }
        finally
        {
            ArrayPool<DrawObjectAndDistance>.Shared.Return(bufferA, false);
            ArrayPool<DrawObjectAndDistance>.Shared.Return(bufferB, false);
            ArrayPool<uint>.Shared.Return(keys, false);
            ArrayPool<uint>.Shared.Return(keysB, false);
        }
    }



}