


namespace Engine.GameObjects;



using Engine.Attributes;
using static Engine.Core.EngineMath;
using static Engine.Core.RenderingBackend;
using Engine.Core;
using Engine.GameResources;



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

    public Skeleton Skeleton;


    [GameObjectInitMethod]
    public new void Init(Skeleton Skeleton, ModelResource Model, GameResource[] Materials = null, Dictionary<string, VertexAttributeDefinitionPlusBufferStruct> extraAttributeBuffers = null)
    {

        this.Skeleton = Skeleton;
        base.Init(Model, Materials, extraAttributeBuffers);
    }


    public override void PreDraw()
    {
        Skeleton.RecalculateAndUploadSkinningDataIfNeeded();
        base.PreDraw();
    }

    public override unsafe void Draw(delegate*<DrawObject, MaterialResource, MaterialResource.MaterialDefinition> MaterialDefinitionMutator = default)
    {
        base.Draw(MaterialDefinitionMutator);
    }



}

