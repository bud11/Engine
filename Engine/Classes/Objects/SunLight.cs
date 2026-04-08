


using Engine.Attributes;
using Engine.Core;
using Engine.GameResources;
using System.Numerics;

namespace Engine.GameObjects;




public partial class SunLight : AABBObject
{
    protected override EngineMath.AABB BaseAABB => EngineMath.AABB.MaxValue;

    [Indexable]
    public Vector4 Color;

    [Indexable]
    public bool Shadow;





    private Camera ShadowCam;
    public override void Init()
    {
        ShadowCam = new()
        {
            Resolution = new(4096),
            GlobalTransform = GlobalTransform,
            ColorBuffersCount = 0,
            UseDepthStencilBuffer = true,
            Perspective = false
        };

        AddChild(ShadowCam);

        ShadowCam.Init();


        base.Init();
    }


}