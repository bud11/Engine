

namespace Engine.GameObjects;

using Engine.Core;
using System.Numerics;
using static Engine.Core.EngineMath;



/// <summary>
/// A <see cref="GameObject"/> with a global <see cref="AABB"/>. The AABB is cached where possible.
/// </summary>
public abstract class AABBObject : GameObject
{

    public void Init(string Name = default, Transform transform = default)
    {
        base.Init(Name, transform);
    }





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
        if (!IsEnableCameraCullingInTree()) return AABB.MaxValue;


        if (CachedAABBIsDirty)
        {
            var aabb = BaseAABB;
            var transform = GlobalTransform;

            var extent = aabb.Extents;

            Vector3 transformedExtent = new Vector3(
                Math.Abs(transform.OrientationX.X) * extent.X + Math.Abs(transform.OrientationY.X) * extent.Y + Math.Abs(transform.OrientationZ.X) * extent.Z,
                Math.Abs(transform.OrientationX.Y) * extent.X + Math.Abs(transform.OrientationY.Y) * extent.Y + Math.Abs(transform.OrientationZ.Y) * extent.Z,
                Math.Abs(transform.OrientationX.Z) * extent.X + Math.Abs(transform.OrientationY.Z) * extent.Y + Math.Abs(transform.OrientationZ.Z) * extent.Z
            );


            CachedCurrentGlobalSpaceAABB = aabb with { Center = transform * aabb.Center, Extents = transformedExtent };


            CachedAABBIsDirty = false;
        }

        return CachedCurrentGlobalSpaceAABB;

    }

}

