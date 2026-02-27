


namespace Engine.GameObjects;



using Engine.Core;
using Engine.GameResources;
using System.Numerics;
using System.Runtime.InteropServices;
using static Engine.Core.EngineMath;
using static Engine.Core.Rendering;
using static Engine.Core.RenderingBackend;



/// <summary>
/// A camera class with controllable properties, buffers, pipeline logic and post processing.
/// </summary>
/// 
public partial class Camera : GameObject
{
    private Vector2<uint> _size;
    public Vector2<uint> Resolution
    {
        get => _size;
        set
        {
            if (_size != value)
            {
                _size = value;
                DisposeBuffers();
                Setup();

                OnResolutionChanged.Invoke();
            }
        }
    }



    public bool Perspective = true;  //perspective or orthographic
    public float FOV = 45f;    //FOV if perspective
    public float OrthoScale = 1f;  //orthographic scale if orthographic

    public float Near = 0.01f;
    public float Far = 1000f;


    public Matrix4x4 GetProjectionMatrix()
    {
        if (Perspective) return Matrix4x4.CreatePerspectiveFieldOfView(DegToRad(FOV), Resolution.X / (float)Resolution.Y, Near, Far);

        return Matrix4x4.CreateOrthographic(OrthoScale * (Resolution.X / (float)Resolution.Y), OrthoScale, Near, Far);
    }

    public Matrix4x4 GetViewMatrix() => Matrix4x4.CreateLookTo(GlobalTransform.Origin, GlobalTransform.OrientationZ, GlobalTransform.OrientationY);



    public readonly ThreadSafeEventAction OnResolutionChanged = new();




    public LogicalFrameBufferObject FrameBuffer { get; private set; }
    public BackendTextureAndSamplerReferencesPair[] ColorBufferTextures { get; private set; }   //plural for mrt, not for cubemap
    public BackendTextureAndSamplerReferencesPair DepthStencilBufferTexture { get; private set; }








    public enum PostProcessEffectResolutionModes
    {
        Fixed,      //render target resolution = resolutionorfactor    (fixed forever)
        Multiplier  //render target resolution = resolutionorfactor * cam.resolution   (updated if camera resolution changes)
    }





    public static MaterialResource ScalingShader { get; private set; }

    public class PostProcessBuffer
    {

        private PostProcessEffectResolutionModes ResolutionMode;
        private Vector2 ResolutionOrFactor;


        public LogicalFrameBufferObject FrameBuffer { get; private set; }
        public BackendTextureAndSamplerReferencesPair TexturePlusSampler { get; private set; }



        public PostProcessBuffer(Camera owner, PostProcessEffectResolutionModes resolutionMode, Vector2 resolutionOrFactor)
        {
            ResolutionMode = resolutionMode;
            ResolutionOrFactor = resolutionOrFactor;

            Evaluate(owner);
        }


        public void DisposeBuffer()
        {
            if (TexturePlusSampler != null) TexturePlusSampler.Free();
        }


        public void Evaluate(Camera owner)
        {
            var requiredRes = ResolutionMode == PostProcessEffectResolutionModes.Fixed ? (Vector2<uint>)ResolutionOrFactor : (Vector2<uint>)((Vector2)owner.Resolution * ResolutionOrFactor);

            if (TexturePlusSampler == null || new Vector2<uint>(TexturePlusSampler.Texture.Dimensions.X, TexturePlusSampler.Texture.Dimensions.Y) != requiredRes)
            {
                TexturePlusSampler = new BackendTextureAndSamplerReferencesPair(

                    BackendTextureReference.Create(new Vector3<uint>(requiredRes.X, requiredRes.Y, 1),
                                  owner.UseCubeMap ? TextureTypes.TextureCubeMap : TextureTypes.Texture2D,
                                  TextureFormats.RGBA16_SFLOAT,
                                  true),
                    BackendSamplerReference.Get(new(TextureWrapModes.ClampToEdge, TextureFilters.Linear, TextureFilters.Linear, TextureFilters.Linear, false)));

                FrameBuffer = LogicalFrameBufferObject.Create([TexturePlusSampler.Texture], null);
            }
        }
    }







    private Dictionary<string, PostProcessBuffer> PostProcessBuffers;


    /// <summary>
    /// Creates a fixed size color buffer that will be used for post processing. The buffer will use <see cref="UseCubeMap"/> and <see cref="UseHDRColorBuffers"/>.
    /// </summary>
    public unsafe void CreateFixedSizePostProcessBuffer(string Name, Vector2<uint> Resolution)
    {
        if (PostProcessBuffers == null)
            PostProcessBuffers = new();

        PostProcessBuffers[Name] = new PostProcessBuffer(this, PostProcessEffectResolutionModes.Fixed, (Vector2)Resolution);
    }

    /// <summary>
    /// Creates a color buffer that will be used for post processing, which will automatically resize according to <see cref="Resolution"/> * <paramref name="ScalingFactor"/>. The buffer will use <see cref="UseCubeMap"/> and <see cref="UseHDRColorBuffers"/>.
    /// </summary>
    /// <param name="Name"></param>
    /// <param name="ResolutionMode"></param>
    /// <param name="ScalingFactor"></param>
    public unsafe void CreateDynamicSizePostProcessBuffer(string Name, Vector2 ScalingFactor)
    {
        if (PostProcessBuffers == null)
            PostProcessBuffers = new();

        PostProcessBuffers[Name] = new PostProcessBuffer(this, PostProcessEffectResolutionModes.Multiplier, ScalingFactor);
    }


    public void DestroyPostProcessBuffer(string Name)
    {
        if (PostProcessBuffers != null && PostProcessBuffers.TryGetValue(Name, out var pp))
        {
            pp.DisposeBuffer();
            PostProcessBuffers.Remove(Name);
        }
    }





    public byte ColorBuffersCount = 1;
    public bool UseDepthStencilBuffer = true;
    public bool UseCubeMap = false;
    public bool UseHDRColorBuffers = true;
    public bool EnableDepthComparison = false;



    /*
            public void ClearDepthStencil()
            {
                var col = Color.Black.ToVector4();

                var count = useCubeMap ? 1 : 6;
                for (byte i = 0; i < count; i++)
                {
                    ClearFramebufferDepthStencil(FrameBuffer, i);
                }
            }

            public void ClearColor()
            {
                var col = Color.Black.ToVector4();

                var count = FrameBuffer.Type == TextureTypes.Texture2D ? 1 : 6;
                for (byte i = 0; i < count; i++)
                {
                    for (byte x = 0; x < ColorBufferTextures.Length; x++)
                    {
                        ClearFramebufferColorAttachment(FrameBuffer, col, x, i);
                    }
                }
            }

    */




    private void Setup()
    {


        for (int i = 0; i < ColorBuffersCount; i++)
        {
            if (ColorBufferTextures == null)
                ColorBufferTextures = new BackendTextureAndSamplerReferencesPair[ColorBuffersCount];

            ColorBufferTextures[i] = new BackendTextureAndSamplerReferencesPair(
                BackendTextureReference.Create(new Vector3<uint>(Resolution.X, Resolution.Y, 1), UseCubeMap ? TextureTypes.TextureCubeMap : TextureTypes.Texture2D, UseHDRColorBuffers ? TextureFormats.RGBA16_SFLOAT : TextureFormats.RGBA8_UNORM, true),

                BackendSamplerReference.Get(new() { MinFilter = TextureFilters.Linear, MagFilter = TextureFilters.Linear, EnableDepthComparison = false, WrapMode = TextureWrapModes.ClampToEdge, MipmapFilter = TextureFilters.Linear })
                );

        }


        if (UseDepthStencilBuffer)
        {
            DepthStencilBufferTexture = new BackendTextureAndSamplerReferencesPair(
                BackendTextureReference.Create(new Vector3<uint>(Resolution.X, Resolution.Y, 1), UseCubeMap ? TextureTypes.TextureCubeMap : TextureTypes.Texture2D, TextureFormats.DepthStencil, true),

                BackendSamplerReference.Get(new() { MinFilter = TextureFilters.Linear, MagFilter = TextureFilters.Linear, EnableDepthComparison = EnableDepthComparison, WrapMode = TextureWrapModes.ClampToEdge, MipmapFilter = TextureFilters.Linear })
                );
        }


        if (PostProcessBuffers != null)
        {
            foreach (var k in PostProcessBuffers)
            {
                k.Value.Evaluate(this);
            }
        }



        FrameBuffer = LogicalFrameBufferObject.Create(ColorBufferTextures == null ? null : ColorBufferTextures.Select(x => x.Texture).ToArray(), DepthStencilBufferTexture == null ? null : DepthStencilBufferTexture.Texture);

    }




    private static Transform[] CubemapDirections = [
                EngineMath.Transform.LookingAt(Vector3.UnitX, Vector3.UnitY),
                    EngineMath.Transform.LookingAt(-Vector3.UnitX, Vector3.UnitY),

                    EngineMath.Transform.LookingAt(-Vector3.UnitY, -Vector3.UnitZ),
                    EngineMath.Transform.LookingAt(Vector3.UnitY, Vector3.UnitZ),

                    EngineMath.Transform.LookingAt(-Vector3.UnitZ, Vector3.UnitY),
                    EngineMath.Transform.LookingAt(Vector3.UnitZ, Vector3.UnitY),
                    ];








    /// <summary>
    /// Defines a subpass to be used by <see cref="Render(Span{DrawObject}, CameraDrawSortMode)"/>.
    /// </summary>
    public unsafe readonly struct CameraSubpassDefinition
    {
        public readonly FrameBufferPipelineStage FrameBufferPipelineStageReq;

        public readonly CameraDrawSortMode Ordering;

        public readonly GCHandle<IList<DrawObject>> ObjectWhiteList;

        public readonly delegate*<MaterialResource, MaterialResource.MaterialResolution> MaterialResolver;

        public readonly delegate*<ReadOnlySpan<(DrawObject obj, float distance)>, delegate*<MaterialResource, MaterialResource.MaterialResolution>, void> DrawCallIssuer;



        public CameraSubpassDefinition(

            FrameBufferPipelineStage frameBufferPipelineStageReq,
            CameraDrawSortMode ordering,

            IList<DrawObject> objectWhiteList,

            delegate*<MaterialResource, MaterialResource.MaterialResolution> materialResolver = null,

            delegate*<ReadOnlySpan<(DrawObject obj, float distance)>, delegate*<MaterialResource, MaterialResource.MaterialResolution>, void> drawCallIssuer = null

            )
        {
            FrameBufferPipelineStageReq = frameBufferPipelineStageReq;

            Ordering = ordering;

            ObjectWhiteList = GCHandle<IList<DrawObject>>.Alloc(objectWhiteList, GCHandleType.Normal);

            MaterialResolver = materialResolver;

            DrawCallIssuer = drawCallIssuer;
        }
    }




    public enum CameraDrawSortMode
    {
        Unordered,
        NearToFar,
        FarToNear,
    }






    /// <summary>
    /// Renders objects according to a defined programmable render pipeline.
    /// </summary>
    /// <param name="subpasses"></param>
    public unsafe void Render(ReadOnlySpan<CameraSubpassDefinition> subpasses, MaterialResource? forceOneMaterial = null)
    {

        

        ///////////////////////////////////////////////////////////////////////////////////////
        //for each internal logical framebuffer, call MainRenderLogic. cubemaps cameras have 6, other cameras just have 1


        if (UseCubeMap)
        {
            var t = GlobalTransform;

            for (byte i = 0; i < 6; i++)
            {
                GlobalTransform = t * CubemapDirections[i];
                MainRenderLogic(i, subpasses);
            }

            GlobalTransform = t;

        }
        else
        {
            MainRenderLogic(0, subpasses);
        }




        void MainRenderLogic(byte framebuffer, ReadOnlySpan<CameraSubpassDefinition> subpasses)
        {

            Span<FrameBufferPipelineStage> stages = stackalloc FrameBufferPipelineStage[subpasses.Length];
            for (int i = 0; i < subpasses.Length; i++)
                stages[i] = subpasses[i].FrameBufferPipelineStageReq;


            var pipelineOperator = StartFrameBufferPipeline(FrameBuffer, stages);




            Span<Plane> frustumPlanes = stackalloc Plane[6];
            ExtractFrustumPlanes(GetViewMatrix() * GetProjectionMatrix(), frustumPlanes);



            ///////////////////////////////////////////////////////////////////////////////////////
            //for each subpass..



            for (int sp = 0; sp < subpasses.Length; sp++)
            {
                var subpass = subpasses[sp];
                var whitelist = subpass.ObjectWhiteList.Target;




                ///////////////////////////////////////////////////////////////////////////////////////
                //get a list of the visible and in camera objects...

                ArrayPools.ArrayFromPool<(DrawObject, float)> objs = ArrayPools.RentArrayFromPool<(DrawObject, float)>(whitelist.Count);

                var idx = 0;
                for (int i = 0; i < whitelist.Count; i++)
                {
                    var o = whitelist[i];

                    //if (o.IsVisibleInTree() && IsAABBInFrustum(o.GetOrRecalculateCachedGlobalAABB(), frustumPlanes))
                    objs[idx++] = (o, subpass.Ordering != CameraDrawSortMode.Unordered ? o.GlobalPosition.DistanceToSquared(GlobalPosition) : 0);
                }

                objs.Length = idx;




                ///////////////////////////////////////////////////////////////////////////////////////
                //..sort them if needed..

                //if (subpass.Ordering != CameraDrawSortMode.Unordered)
                //    ((Span<(DrawObject, float)>)objs).RadixSort(subpass.Ordering == CameraDrawSortMode.FarToNear);


                //throw new NotImplementedException();


                ///////////////////////////////////////////////////////////////////////////////////////
                //..and draw them.


                for (int i = 0; i < objs.Length; i++)
                {

                    var obj = objs[i].Item1;
                    if (!obj.DrawnThisFrame)
                        objs[i].Item1.PreDraw();
                }



                if (subpass.DrawCallIssuer != null) subpass.DrawCallIssuer(objs, subpass.MaterialResolver);

                else
                {
                    for (int i = 0; i < objs.Length; i++)
                        objs[i].Item1.Draw(subpass.MaterialResolver);
                }



                ///////////////////////////////////////////////////////////////////////////////////////
                //free whitelist handle + return object sorting array

                subpass.ObjectWhiteList.Free();

                objs.Return();



                pipelineOperator.Advance();
            }


        }




        static bool IsAABBInFrustum(AABB bounds, Span<Plane> frustumPlanes)
        {

            if (bounds == AABB.MaxValue) return true;



            var maxX = bounds.Center.X + bounds.Extents.X;
            var maxY = bounds.Center.Y + bounds.Extents.Y;
            var maxZ = bounds.Center.Z + bounds.Extents.Z;

            var minX = bounds.Center.X - bounds.Extents.X;
            var minY = bounds.Center.Y - bounds.Extents.Y;
            var minZ = bounds.Center.Z - bounds.Extents.Z;



            for (int i = 0; i < frustumPlanes.Length; i++)
            {
                var plane = frustumPlanes[i];

                Vector3 positive = new Vector3(
                    plane.Normal.X >= 0 ? maxX : minX,
                    plane.Normal.Y >= 0 ? maxY : minY,
                    plane.Normal.Z >= 0 ? maxZ : minZ
                );

                Vector3 negative = new Vector3(
                    plane.Normal.X >= 0 ? minX : maxX,
                    plane.Normal.Y >= 0 ? minY : maxY,
                    plane.Normal.Z >= 0 ? minZ : maxZ
                );


                if (Vector3.Dot(plane.Normal, positive) + plane.D < 0)
                    return false;
            }

            return true;
        }




        static void ExtractFrustumPlanes(Matrix4x4 viewProj, Span<Plane> frustumPlanes)
        {

            // Left Plane
            frustumPlanes[0] = new Plane(
                viewProj.M14 + viewProj.M11,
                viewProj.M24 + viewProj.M21,
                viewProj.M34 + viewProj.M31,
                viewProj.M44 + viewProj.M41
            );

            // Right Plane
            frustumPlanes[1] = new Plane(
                viewProj.M14 - viewProj.M11,
                viewProj.M24 - viewProj.M21,
                viewProj.M34 - viewProj.M31,
                viewProj.M44 - viewProj.M41
            );

            // Bottom Plane
            frustumPlanes[2] = new Plane(
                viewProj.M14 + viewProj.M12,
                viewProj.M24 + viewProj.M22,
                viewProj.M34 + viewProj.M32,
                viewProj.M44 + viewProj.M42
            );

            // Top Plane
            frustumPlanes[3] = new Plane(
                viewProj.M14 - viewProj.M12,
                viewProj.M24 - viewProj.M22,
                viewProj.M34 - viewProj.M32,
                viewProj.M44 - viewProj.M42
            );

            // Near Plane
            frustumPlanes[4] = new Plane(
                viewProj.M14 + viewProj.M13,
                viewProj.M24 + viewProj.M23,
                viewProj.M34 + viewProj.M33,
                viewProj.M44 + viewProj.M43
            );

            // Far Plane
            frustumPlanes[5] = new Plane(
                viewProj.M14 - viewProj.M13,
                viewProj.M24 - viewProj.M23,
                viewProj.M34 - viewProj.M33,
                viewProj.M44 - viewProj.M43
            );

            // Normalize
            for (int i = 0; i < 6; i++)
            {
                float length = frustumPlanes[i].Normal.Length();
                frustumPlanes[i] = new Plane(
                    frustumPlanes[i].Normal / length,
                    frustumPlanes[i].D / length
                );
            }
        }
    }



    protected override void OnFree()
    {
        DisposeBuffers();

        if (PostProcessBuffers != null)
        {
            foreach (var k in PostProcessBuffers)
                k.Value.DisposeBuffer();
        }
    }


    public void DisposeBuffers()
    {

        //post process buffers are disposed in Free and in Evaluate, that way any constant fixed size buffers IE for bloom dont need to be recreated for no reason

        for (int i = 0; i < ColorBufferTextures?.Length; i++)
            ColorBufferTextures[i].Free();

        DepthStencilBufferTexture?.Free();

        FrameBuffer?.Free();

    }
}