


using Engine.Attributes;
using Engine.Core;
using Engine.GameResources;
using System.Numerics;

namespace Engine.GameObjects;




public partial class PointLight : AABBObject
{
    protected override EngineMath.AABB BaseAABB 
        => EngineMath.AABB.FromCenterExtent(Vector3.Zero, new Vector3(Radius));


    [Indexable]
    public Vector4 Color;

    [Indexable]
    public float Radius;

    [Indexable]
    public bool Shadow;



}