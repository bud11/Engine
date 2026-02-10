





namespace Engine.GameObjects;

using Engine.Attributes;
using System.Numerics;
using static Engine.Core.EngineMath;





public class CollisionObject : AABBObject
{
    protected override AABB BaseAABB => Shape.BaseAABB;

    public CollisionShape Shape;


    
    public void Init(CollisionShape shape, string Name = null, Matrix4x4 Transform = default)
    {
        Shape = shape;

        base.Init(Name, Transform);
    }


}


public abstract class CollisionShape
{
    public abstract AABB BaseAABB { get; }
}

