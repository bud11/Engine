
namespace Engine.GameObjects;


using Engine.Attributes;
using Engine.Core;
using Engine.GameResources;
using static Engine.Core.EngineMath;
using static Engine.Core.References;
using static Engine.Core.RenderingBackend;


#if DEBUG
using Engine.Stripped;
#endif


/// <summary>
/// A <see cref="GameObject"/> that embodies a <see cref="ModelResource"/>. 
/// </summary>
public partial class ModelInstance : DrawObject
{



    [Indexable]
    public ModelResource Model;


    protected override AABB BaseAABB => Model.BaseAABB;


    [Indexable]
    public MaterialResource[] Materials;




    /// <summary>
    /// Resource sets to be used globally for every model instance.
    /// </summary>
    public static readonly Dictionary<string, BackendResourceSetReference> GlobalModelInstanceResourceSets = new();


    /// <summary>
    /// Resource sets to be used at the individual model instance level.
    /// </summary>
    public readonly Dictionary<string, BackendResourceSetReference> ModelInstanceResourceSets = new();


    /// <summary>
    /// Vertex attributes to be used at the individual model instance level.
    /// </summary>
    public readonly Dictionary<string, VertexAttributeDefinitionBufferPair> ModelInstanceVertexAttributeBuffers = new();





    public override void Init()
    {
        base.Init();

        lock (AllDrawObjects)
            AllDrawObjects.Add(this);
    }


    protected override void OnFree()
    {
        lock (AllDrawObjects)
            AllDrawObjects.Remove(this);

        base.OnFree();
    }



    /// <summary>
    /// Draws the full model using all of its materials.
    /// </summary>
    /// <param name="resolver"></param>
    public unsafe override void Draw(DrawState state)
    {
        base.Draw(state);


        for (var i = 0; i < Model.SubMeshes.Length; i++)
        {
            var mat = Materials[i];

            var sm = Model.SubMeshes[i];

            if (mat != null)
                Draw(mat, state, sm.Start, sm.End);
        }
    }



    /// <summary>
    /// Draws the entire model using one manually provided material.
    /// </summary>
    /// <param name="material"></param>
    /// <param name="resolver"></param>
    public virtual unsafe void DrawWithOneMaterial(MaterialResource material, DrawState state)
    {
        Draw(material, state, 0, Model.SubMeshes[^1].End);
    }


    /// <summary>
    /// Issues an actual draw call.
    /// </summary>
    /// <param name="material"></param>
    /// <param name="resolver"></param>
    /// <param name="Start"></param>
    /// <param name="End"></param>
    public virtual unsafe void Draw(MaterialResource material, DrawState state, uint Start, uint End)
    {
        var resolve = material.ResolveMaterial(state.MaterialResolver);

        if (resolve.Shader == null) return;

        Rendering.Draw(Model.Buffers.VertexAttributesToUnmanaged().Combine(ModelInstanceVertexAttributeBuffers.VertexAttributesToUnmanaged()),
                        GlobalModelInstanceResourceSets.ToUnmanagedKV().Combine(ModelInstanceResourceSets.ToUnmanagedKV()).Combine(resolve.ResourceSets.ToUnmanagedKV().Combine(state.TransientResourceSets)),
                        resolve.Shader,
                        resolve.RasterizationDetails,
                        resolve.BlendState,
                        resolve.DepthStencilState,
                        Model.IndexBuffer,
                        new IndexingDetails(Start, End, 0, 1, Model.IndexBufferFormat));

    }

}


