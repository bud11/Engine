


using Engine.Attributes;
using Engine.Core;
using Engine.GameResources;
using System.Numerics;

namespace Engine.GameObjects;




public partial class SunLight : AABBObject
{
    protected override EngineMath.AABB BaseAABB => EngineMath.AABB.MaxValue;

    [DataValue]
    public Vector4 Color;

    [DataValue]
    public bool Shadow;

}