


namespace Engine.Core;



using static Engine.Core.EngineMath;
using static RenderingBackend;
using static RenderingBackend.DrawPipelineDetails;







/// <summary>
/// An <see cref="IDeferredCommand"/> that calls <see cref="Draw(ref UnmanagedKeyValueHandleCollection{string, Rendering.VertexAttributeDefinitionPlusBufferClass}, ref UnmanagedKeyValueHandleCollection{string, Rendering.BackendResourceSetReference}, Rendering.BackendShaderReference, ref Rendering.DrawPipelineDetails.RasterizationDetails, ref Rendering.DrawPipelineDetails.BlendState, ref Rendering.DrawPipelineDetails.DepthStencilState, Rendering.BackendIndexBufferReference, ref Rendering.IndexingDetails)"/>.
/// </summary>
public unsafe struct DrawStruct : IDeferredCommand
{

    private UnmanagedKeyValueHandleCollection<string, VertexAttributeDefinitionPlusBufferClass> AttributeCollection;
    private UnmanagedKeyValueHandleCollection<string, BackendResourceSetReference> ResourceSetCollection;
    private GCHandle<BackendShaderReference> ShaderHandle;

    private RasterizationDetails Rasterization;
    private BlendState Blending;
    private DepthStencilState DepthStencil;

    private GCHandle<BackendIndexBufferAllocationReference> IndexBufferHandle;

    private IndexingDetails DrawRange;



    public DrawStruct(UnmanagedKeyValueHandleCollection<string, VertexAttributeDefinitionPlusBufferClass> attributeCollection, UnmanagedKeyValueHandleCollection<string, BackendResourceSetReference> resourceSetCollection, BackendShaderReference shader, RasterizationDetails rasterization, BlendState blending, DepthStencilState depthStencil, BackendIndexBufferAllocationReference indexBuffer, IndexingDetails drawRange)
    {
        AttributeCollection = attributeCollection;
        ResourceSetCollection = resourceSetCollection;
        ShaderHandle = shader.GetGenericGCHandle();
        Rasterization = rasterization;
        Blending = blending;
        DepthStencil = depthStencil;
        IndexBufferHandle = indexBuffer == null ? default : indexBuffer.GetGenericGCHandle();
        DrawRange=drawRange;


    }

    public static unsafe void Execute(void* self)
    {
        var p = (DrawStruct*)self;

        Draw(ref p->AttributeCollection,
             ref p->ResourceSetCollection,
             p->ShaderHandle.Target,
             ref p->Rasterization,
             ref p->Blending,
             ref p->DepthStencil,
             p->IndexBufferHandle.IsAllocated ? p->IndexBufferHandle.Target : null,
             ref p->DrawRange);


    }

}

public unsafe struct StartDrawToScreenStruct : IDeferredCommand
{
    public static unsafe void Execute(void* self) => StartDrawToScreen();
}

public unsafe struct EndDrawToScreenStruct : IDeferredCommand
{
    public static unsafe void Execute(void* self) => EndDrawToScreen();
}


public unsafe record struct SetScissorStruct(Vector2<uint> offset, Vector2<uint> size) : IDeferredCommand
{
    public static unsafe void Execute(void* self)
    {
        var p = (SetScissorStruct*)self;
        SetScissor(p->offset, p->size);
    }
}