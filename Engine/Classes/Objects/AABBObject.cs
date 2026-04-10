

namespace Engine.GameObjects;

using Engine.Core;
using System.Numerics;
using static Engine.Core.EngineMath;



/// <summary>
/// A <see cref="GameObject"/> with a global <see cref="AABB"/>. The AABB is cached where possible.
/// </summary>
public abstract partial class AABBObject : GameObject
{




    /// <summary>
    /// The unaltered local AABB of this object.
    /// </summary>
    protected abstract AABB BaseAABB { get; }





    private AABB CachedCurrentGlobalSpaceAABB;
    private bool CachedAABBIsDirty = true;


    protected override void GlobalTransformChanged()
    {
        CachedAABBIsDirty = true;

        base.GlobalTransformChanged();
    }



    public AABB GetOrRecalculateCachedGlobalAABB()
    {
        return AABB.FromCenterExtent(Vector3.Zero, Vector3.One) * GlobalTransform;


        if (!IsEnableCameraCullingInTree()) return AABB.MaxValue;


        if (CachedAABBIsDirty)
        {
            CachedCurrentGlobalSpaceAABB = BaseAABB * GlobalTransform;

            CachedAABBIsDirty = false;
        }

        return CachedCurrentGlobalSpaceAABB;

    }

}

