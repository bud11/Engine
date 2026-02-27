





namespace Engine.GameObjects;

using Engine.Attributes;
using System.Numerics;
using static Engine.Core.EngineMath;





public partial class CollisionObject : AABBObject
{
    protected override AABB BaseAABB => Shape.BaseAABB;

    public CollisionShape Shape;


}


public abstract class CollisionShape
{
    public abstract AABB BaseAABB { get; }
}

