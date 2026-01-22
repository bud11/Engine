
namespace Engine.GameObjects;


using Engine.Attributes;
using Engine.Core;
using System.Collections.Immutable;

using static Engine.Core.EngineMath;
using static Engine.Core.Rendering;
using static Engine.Core.RenderingBackend;
using Engine.GameResources;




/// <summary>
/// An object that embodies one or more instances of a <see cref="ModelResource"/>. By default, just one, but functionality can be expanded via overrides. See <seealso cref="MultiModelInstance"/> for an implementation of instancing
/// </summary>
public partial class ModelInstance : DrawObject
{




    public ModelResource Model { get; private set; }


    protected override AABB BaseAABB => Model.BaseAABB;




    private MaterialResource[] Materials;
    public ImmutableArray<MaterialResource> GetMaterials() => ImmutableArray.ToImmutableArray(Materials);

    public MaterialResource GetMaterial(byte index) => Materials[index];

    public virtual void SetMaterial(byte index, MaterialResource mat) => Materials.AddRefCountedReference(index, mat);





    public readonly UnmanagedKeyValueHandleCollectionOwner<string, BackendResourceSetReference> ModelInstanceResourceSets = new();
    public readonly UnmanagedKeyValueHandleCollectionOwner<string, VertexAttributeDefinitionPlusBufferClass> VertexAttributeBuffers = new();






    [GameObjectInitMethod]
    public void Init(ModelResource Model, GameResource[] Materials = null, Dictionary<string, VertexAttributeDefinitionPlusBufferStruct> extraAttributeBuffers = null)
    {

        this.Model = Model;
        this.Model.AddUser();

        this.Materials = new MaterialResource[Model.SubMeshes.Length];


        if (Materials != null)
            for (byte i = 0; i < Materials.Length; i++)
                SetMaterial(i, (MaterialResource)Materials[i]);




        foreach (var kv in Model.Buffers)
            VertexAttributeBuffers[kv.Key] = new VertexAttributeDefinitionPlusBufferClass(kv.Value.Buffer, kv.Value.Definition);

        if (extraAttributeBuffers != null)
            foreach (var kv in extraAttributeBuffers)
                VertexAttributeBuffers[kv.Key] = new VertexAttributeDefinitionPlusBufferClass(kv.Value.Buffer.Target, kv.Value.Definition);


        base.Init();
        AllDrawableObjects.Add(this);
    }





    protected override void OnFree()
    {
        AllDrawableObjects.Remove(this);

        Model.RemoveUser();

        foreach (var mat in Materials) mat?.RemoveUser();

        base.OnFree();
    }



    public unsafe override void Draw(delegate*<DrawObject, MaterialResource, MaterialResource.MaterialDefinition> MaterialDefinitionMutator = null)
    {
        for (var i = 0; i < Model.SubMeshes.Length; i++)
        {
            var mat = Materials[i];

            var sm = Model.SubMeshes[i];

            if (mat != null)
                Draw(mat, sm.Start, sm.End);
        }
    }


    /// <summary>
    /// Draws the entire model using one manually provided material.
    /// </summary>
    /// <param name="Material"></param>
    /// <param name="Priority"></param>
    /// <param name="MaterialDefinitionMutator"></param>
    public virtual unsafe void DrawWithOneMaterial(MaterialResource Material, delegate*<DrawObject, MaterialResource, MaterialResource.MaterialDefinition> MaterialDefinitionMutator = null)
    {
        Draw(Material, 0, Model.SubMeshes[^1].End);
    }



    /// <summary>
    /// Issues an actual draw call.
    /// </summary>
    /// <param name="Material"></param>
    /// <param name="Start"></param>
    /// <param name="End"></param>
    /// <param name="MaterialDefinitionMutator"></param>
    public virtual unsafe void Draw(MaterialResource Material, uint Start, uint End, delegate*<DrawObject, MaterialResource, MaterialResource.MaterialDefinition> MaterialDefinitionMutator = null)
    {
        var def = Material.Definition;
        if (MaterialDefinitionMutator != null) def = MaterialDefinitionMutator(this, Material);

        PushDeferredRenderThreadCommand(new DrawStruct(VertexAttributeBuffers.GetUnderlyingCollection(),
                                                        ModelInstanceResourceSets.GetUnderlyingCollection().Combine(def.MaterialResourceSets),
                                                        def.Shader,
                                                        def.Rasterization,
                                                        def.Blending,
                                                        def.DepthStencil,
                                                        Model.IndexBuffer,
                                                        new(Start, End, 0, 1)));
    }


}


