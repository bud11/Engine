
namespace Engine.GameObjects;


using Engine.Attributes;
using Engine.Core;
using Engine.GameResources;
using static Engine.Core.EngineMath;
using static Engine.Core.RenderingBackend;




/// <summary>
/// A <see cref="GameObject"/> that embodies a <see cref="ModelResource"/>. 
/// </summary>
public partial class ModelInstance : DrawObject
{



    [Indexable]
    public ModelResource Model;


    protected override AABB BaseAABB => Model.BaseAABB;


    [Indexable]
    public RefCountCollections.RefCountedArray<MaterialResource> Materials;




    /// <summary>
    /// Resource sets to be used globally for every model instance.
    /// </summary>
    public static readonly UnmanagedKeyValueHandleCollectionOwner<string, BackendResourceSetReference> GlobalModelInstanceResourceSets = new();


    /// <summary>
    /// Resource sets to be used at the individual model instance level.
    /// </summary>
    public readonly UnmanagedKeyValueHandleCollectionOwner<string, BackendResourceSetReference> ModelInstanceResourceSets = new();


    /// <summary>
    /// Vertex attributes to be used at the individual model instance level.
    /// </summary>
    public readonly UnmanagedKeyValueHandleCollectionOwner<string, VertexAttributeDefinitionPlusBufferClass> ModelInstanceVertexAttributeBuffers = new();





    public override void Init()
    {
        base.Init();
        AllDrawableObjects.Add(this);
    }


    protected override void OnFree()
    {
        AllDrawableObjects.Remove(this);

        Model.RemoveUser();

        Materials.Free();

        base.OnFree();
    }



    /// <summary>
    /// Draws the full model using all of its materials.
    /// </summary>
    /// <param name="resolver"></param>
    public unsafe override void Draw(delegate*<MaterialResource, MaterialResource.MaterialResolution> resolver)
    {
        for (var i = 0; i < Model.SubMeshes.Length; i++)
        {
            var mat = Materials[i];

            var sm = Model.SubMeshes[i];

            if (mat != null)
                Draw(mat, resolver, sm.Start, sm.End);
        }
    }



    /// <summary>
    /// Draws the entire model using one manually provided material.
    /// </summary>
    /// <param name="material"></param>
    /// <param name="resolver"></param>
    public virtual unsafe void DrawWithOneMaterial(MaterialResource material, delegate*<MaterialResource, MaterialResource.MaterialResolution> resolver)
    {
        Draw(material, resolver, 0, Model.SubMeshes[^1].End);
    }


    /// <summary>
    /// Issues an actual draw call.
    /// </summary>
    /// <param name="material"></param>
    /// <param name="resolver"></param>
    /// <param name="Start"></param>
    /// <param name="End"></param>
    public virtual unsafe void Draw(MaterialResource material, delegate*<MaterialResource, MaterialResource.MaterialResolution> resolver, uint Start, uint End)
    {
        var resolve = resolver(material);
        if (resolve.Shader == null) return;

        Rendering.Draw(Model.Buffers.GetUnderlyingCollection().Combine(in ModelInstanceVertexAttributeBuffers.GetUnderlyingCollection()),
                        GlobalModelInstanceResourceSets.GetUnderlyingCollection().Combine(in ModelInstanceResourceSets.GetUnderlyingCollection()).Combine(in resolve.MaterialResourceSets),
                        resolve.Shader,
                        resolve.RasterizationDetails,
                        resolve.BlendState,
                        resolve.DepthStencilState,
                        Model.IndexBuffer,
                        new IndexingDetails(Start, End, 0, 1));

    }

}


