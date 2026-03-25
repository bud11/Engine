


namespace Engine.GameObjects;

using Engine.Attributes;
using Engine.GameResources;
using static Engine.Core.EngineMath;



public partial class SkinnedModelInstance : ModelInstance
{

    protected override AABB BaseAABB
    {
        get
        {
            var b = Model.BaseAABB;
            return b with { Extents = b.Extents + (b.Extents * 10f) };
        }
    }


    [Indexable]
    public Skeleton Skeleton;



    public override void PreDraw()
    {
        Skeleton?.ReuploadSkinningDataIfNeeded();
        base.PreDraw();
    }

}

