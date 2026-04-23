


using Engine.Attributes;
using Engine.Core;
using System.Numerics;

namespace Engine.GameObjects;




public partial class SunLight : AABBObject
{
    protected override EngineMath.AABB BaseAABB => EngineMath.AABB.MaxValue;

    [Indexable]
    public Vector4 Color;

    [Indexable]
    public bool Shadow;






    public Camera ShadowCam { get; private set; }

    public RenderingBackend.BackendTexture2DShadowSamplerPair[] ShadowTextures { get; private set; }


    public override void Init()
    {

        ShadowCam = new()
        {
            Resolution = new(4096),
            GlobalTransform = Matrix4x4.CreateLookTo(GlobalPosition, GlobalTransform.GetOrientationY(), -GlobalTransform.GetOrientationZ()),
            Perspective = false,
            OrthographicScale = 70f,
            Far = 110,
            Near = 0.05f
        };

        ShadowCam.CreateDynamicSizeCameraFrameBuffer("main", Vector2.One, 0, default, true, 1);
        ShadowCam.Init();


        ShadowTextures = 
            [
                new RenderingBackend.BackendTexture2DShadowSamplerPair
                (
                    (RenderingBackend.BackendTexture2DReference)ShadowCam.GetCameraFrameBuffer("main").DepthStencil, 

                    RenderingBackend.TextureWrapModes.ClampToEdge, 
                    RenderingBackend.TextureFilters.Linear, 
                    RenderingBackend.TextureFilters.Linear, 
                    RenderingBackend.TextureFilters.Linear
                )
            ];



        AddChild(ShadowCam);


        base.Init();
    
    }




}