


namespace Engine.Core;

using Engine.Stripped;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Drawing;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using static EngineMath;
using static RenderingBackend;
using static RenderingBackend.DrawPipelineDetails;
using static RenderThread;




/// <summary>
/// Provides convinience over <see cref="RenderingBackend"/>.
/// </summary>
public static class Rendering
{




    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector4 ToVector4(this Color Col)
    {
        return new Vector4(Col.R, Col.G, Col.B, Col.A)/255f;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Color ToColor(this Vector4 Col)
    {
        return Color.FromArgb((int)(Col.W * 255), (int)(Col.X * 255), (int)(Col.Y * 255), (int)(Col.Z * 255));
    }






    /// <summary>
    /// Fetches a precompiled shader <see cref="BackendShaderReference"/> from the backend. 
    /// </summary>
    /// <param name="name"></param>
    /// <returns></returns>
    /// <exception cref="Exception"></exception>
    public static BackendShaderReference GetShader(string name) 
        => BackendShaderReference.Get(name);




    /// <summary>
    /// Fetches a precompiled compute shader <see cref="BackendComputeShaderReference"/> from the backend. 
    /// </summary>
    /// <param name="name"></param>
    /// <returns></returns>
    /// <exception cref="Exception"></exception>
    public static BackendComputeShaderReference GetComputeShader(string name) 
        => BackendComputeShaderReference.Get(name);







    /// <summary>
    /// Manages/wraps around an active <see cref="BackendFrameBufferPipelineReference"/>.
    /// </summary>
    public ref struct FrameBufferPipelineStateOperator
    {
        private readonly BackendFrameBufferPipelineReference Pipeline;
        private readonly LogicalFrameBufferObject Fbo;

        private readonly byte StageCount;

        private byte Stage;


        public FrameBufferPipelineStateOperator(LogicalFrameBufferObject fbo, BackendFrameBufferPipelineReference pipeline)
        {
            Pipeline = pipeline;
            Fbo = fbo;

            StageCount = Pipeline.Details.StageCount;

            PushDeferredRenderThreadCommand(new BeginSt(fbo.GetFramebufferObjectForPipeline(Pipeline).GetGenericGCHandle(), pipeline.GetGenericGCHandle()));
        }


        /// <summary>
        /// Advances the underlying pipeline to the next stage, or ends it if there are no more stages.
        /// </summary>
        /// <exception cref="Exception"></exception>
        public unsafe void Advance()
        {
            if (Stage < StageCount - 1)
            {
                PushDeferredRenderThreadCommand(
                    new AdvanceSt(Fbo.GetFramebufferObjectForPipeline(Pipeline).GetGenericGCHandle(), Pipeline.GetGenericGCHandle(), Stage)
                );
            }
            else
            {
                PushDeferredRenderThreadCommand(
                    new EndSt(Fbo.GetFramebufferObjectForPipeline(Pipeline).GetGenericGCHandle())
                );
            }

            Stage++;
        }


        private unsafe readonly record struct BeginSt(GCHandle<BackendFrameBufferObjectReference> fbo, GCHandle<BackendFrameBufferPipelineReference> pipeline) : IDeferredCommand
        {
            public static unsafe void Execute(void* self)
            {
                var ptr = (BeginSt*)self;
                BeginFrameBufferPipeline(ptr->fbo.Target, ptr->pipeline.Target);
            }
        }

        private unsafe readonly record struct AdvanceSt(GCHandle<BackendFrameBufferObjectReference> fbo, GCHandle<BackendFrameBufferPipelineReference> pipeline, byte stage) : IDeferredCommand
        {
            public static unsafe void Execute(void* self)
            {
                var ptr = (AdvanceSt*)self;
                AdvanceFrameBufferPipeline(ptr->fbo.Target, ptr->pipeline.Target, ptr->stage);
            }
        }


        private unsafe readonly record struct EndSt(GCHandle<BackendFrameBufferObjectReference> fbo) : IDeferredCommand
        {
            public static unsafe void Execute(void* self)
            {
                var ptr = (EndSt*)self;
                EndFrameBufferPipeline(ptr->fbo.Target);
            }
        }
    }






    /// <summary>
    /// Starts a pipeline generated from <paramref name="stages"/>, using <paramref name="framebuffer"/>.
    /// <br /> There cannot be more than 8 stages in <paramref name="stages"/>.
    /// <br /> Excess or erroneous stage details will be safely ignored, for example requesting a depth stencil clear when <paramref name="framebuffer"/> has no depth stencil attachment.
    /// </summary>
    /// <param name="framebuffer"></param>
    /// <param name="stages"></param>
    /// <returns></returns>
    /// <exception cref="Exception"></exception>
    public static unsafe FrameBufferPipelineStateOperator StartFrameBufferPipeline(LogicalFrameBufferObject framebuffer, ReadOnlySpan<FrameBufferPipelineStage> stages)
    {

#if DEBUG
        if (stages.Length > 8 || stages.Length == 0) 
            throw new Exception();
#endif


        var details = new FrameBufferPipelineDetails()
        {
            ColorAttachmentCount = (byte)framebuffer.ColorAttachments.Length,
            HasDepthStencil = framebuffer.DepthStencil != null,
        };


        fixed (FrameBufferPipelineStage* p = stages)
            Unsafe.CopyBlockUnaligned(&details.Stages, p, (uint)sizeof(FrameBufferPipelineDetails.InlineStageArray));



        details.StageCount = (byte)stages.Length;


        for (int i = 0; i < framebuffer.ColorAttachments.Length; i++)
            details.ColorFormats[i] = (byte)framebuffer.ColorAttachments[i].TextureFormat;


        var get = BackendFrameBufferPipelineReference.Get(details);

        if (framebuffer.GetFramebufferObjectForPipeline(get) == null)
            framebuffer.AddFramebufferObjectForPipeline(get);



        return new FrameBufferPipelineStateOperator(framebuffer, get);
    }






    /// <summary>
    /// A collection of attachments to render to.
    /// <br /> This creates multiple <see cref="BackendFrameBufferObjectReference"/>s on demand to allow usage of any <see cref="BackendFrameBufferPipelineReference"/>.
    /// </summary>
    public class LogicalFrameBufferObject : Freeable
    {

        public readonly ImmutableArray<BackendTextureReference> ColorAttachments;
        public readonly BackendTextureReference DepthStencil;

        public readonly Vector2<uint> Dimensions;

        public readonly TextureTypes Type;


        private readonly Dictionary<BackendFrameBufferPipelineReference, BackendFrameBufferObjectReference> Framebuffers = new(); 


        public void AddFramebufferObjectForPipeline(BackendFrameBufferPipelineReference pipeline)
        {
            var fbo = BackendFrameBufferObjectReference.Create(ColorAttachments.AsSpan()[..int.Min(ColorAttachments.Length, pipeline.Details.ColorAttachmentCount)], pipeline.Details.HasDepthStencil ? DepthStencil : null, pipeline, Dimensions);

            pipeline.AddUser();
            fbo.AddUser();

            if (!Framebuffers.TryAdd(pipeline, fbo)) throw new Exception();
        }


        public void RemoveFramebufferObjectForPipeline(BackendFrameBufferPipelineReference pipeline)
        {
            if (!Framebuffers.TryGetValue(pipeline, out var fbo))
                throw new Exception();

            Framebuffers.Remove(pipeline);

            pipeline.RemoveUser();
            fbo.RemoveUser();
        }


        public BackendFrameBufferObjectReference GetFramebufferObjectForPipeline(BackendFrameBufferPipelineReference pipeline)
        {
            if (!Framebuffers.TryGetValue(pipeline, out var get)) return null;

            return get;
        }


        private LogicalFrameBufferObject(ImmutableArray<BackendTextureReference> colorAttachments, BackendTextureReference depthstencil, Vector2<uint> dimensions, TextureTypes type)
        {
            ColorAttachments = colorAttachments;
            DepthStencil = depthstencil;
            Dimensions = dimensions;
            Type = type;
        }



        protected override void OnFree()
        {
            foreach (var kv in Framebuffers)
            {
                kv.Key.Free();
                kv.Value.Free();
            }

            for (int i = 0; i < ColorAttachments.Length; i++)
                ColorAttachments[i]?.Free();


            DepthStencil?.Free();

        }


        public static unsafe LogicalFrameBufferObject Create(ReadOnlySpan<BackendTextureReference> colorTargets, BackendTextureReference depthStencilTarget)
        {

#if DEBUG
            var chk = colorTargets.ToArray().ToList();
            if (depthStencilTarget != null) chk.Add(depthStencilTarget);

            if (chk.Count == 0)
                throw new Exception("no attachments given");

            foreach (var k in chk)
            {
                if ((!k.Valid) || k.Dimensions != chk[0].Dimensions)
                    throw new Exception("attachment sizes dont match");
            }
#endif


            //at least one color attachment or a depth stencil target needs to be given
            var onecommon = colorTargets.Length != 0 ? colorTargets[0] : depthStencilTarget;

            return new LogicalFrameBufferObject(ImmutableArray.Create(colorTargets), depthStencilTarget, new Vector2<uint>(onecommon.Dimensions.X, onecommon.Dimensions.Y), onecommon.TextureType);
        }
    }






    /// <summary>
    /// Defers an arbitrary draw call.
    /// </summary>
    /// <param name="Attributes"></param>
    /// <param name="ResourceSets"></param>
    /// <param name="Shader"></param>
    /// <param name="Rasterization"></param>
    /// <param name="Blending"></param>
    /// <param name="DepthStencil"></param>
    /// <param name="IndexBuffer"></param>
    /// <param name="IndexingDetails"></param>
    /// 
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static unsafe void Draw(

        UnmanagedKeyValueHandleCollection<string, VertexAttributeDefinitionPlusBufferClass> Attributes,
        UnmanagedKeyValueHandleCollection<string, BackendResourceSetReference> ResourceSets,
        BackendShaderReference Shader,

        RasterizationDetails Rasterization,
        BlendState Blending,
        DepthStencilState DepthStencil,

        BackendIndexBufferAllocationReference IndexBuffer,
        IndexingDetails IndexingDetails

        )
    {

#if DEBUG

        if (EngineDebug.ThrowIfVertexBufferMissing)
        {
            foreach (var kv in Shader.Metadata.VertexInputAttributes)
            {
                if (!Attributes[kv.Key].IsAllocated) 
                    throw new Exception($"Missing vertex attribute buffer '{kv.Key}'");
            }
        }

        if (EngineDebug.ThrowIfResourceSetMissing)
        {
            foreach (var kv in Shader.Metadata.ResourceSets)
            {
                if (!ResourceSets[kv.Key].IsAllocated)
                    throw new Exception($"Missing resource set '{kv.Key}'");
            }
        }

        if (EngineDebug.ThrowIfResourceMissing)
        {
            foreach (var set in ResourceSets)
            {
                if (set.Value.IsAllocated)
                    foreach (var res in set.Value.Target.GetContents())
                    {
                        if (res == null || IsResourceDummy(res))
                            throw new Exception($"Missing resource in set '{set.Key}'");
                    }
            }
        }

#endif




        PushDeferredRenderThreadCommand(new DrawCommandStruct(Attributes, ResourceSets, Shader, Rasterization, Blending, DepthStencil, IndexBuffer, IndexingDetails));
    }





    private struct DrawCommandStruct(UnmanagedKeyValueHandleCollection<string, VertexAttributeDefinitionPlusBufferClass> attributeCollection,
                                     UnmanagedKeyValueHandleCollection<string, BackendResourceSetReference> resourceSetCollection,
                                     BackendShaderReference shader,
                                     RasterizationDetails rasterization,
                                     BlendState blending,
                                     DepthStencilState depthStencil,
                                     BackendIndexBufferAllocationReference indexBuffer,
                                     IndexingDetails drawRange) : IDeferredCommand
    {

        private UnmanagedKeyValueHandleCollection<string, VertexAttributeDefinitionPlusBufferClass> AttributeCollection = attributeCollection;
        private UnmanagedKeyValueHandleCollection<string, BackendResourceSetReference> ResourceSetCollection = resourceSetCollection;
        private GCHandle<BackendShaderReference> ShaderHandle = shader.GetGenericGCHandle();

        private RasterizationDetails Rasterization = rasterization;
        private BlendState Blending = blending;
        private DepthStencilState DepthStencil = depthStencil;

        private GCHandle<BackendIndexBufferAllocationReference> IndexBufferHandle = indexBuffer == null ? default : indexBuffer.GetGenericGCHandle();

        private IndexingDetails IndexingDetails = drawRange;



        public static unsafe void Execute(void* self)
        {
            var p = (DrawCommandStruct*)self;


            if (p->IndexingDetails.End == p->IndexingDetails.Start) return;


#if DEBUG
            if (p->IndexingDetails.End < p->IndexingDetails.Start)
                throw new Exception();
#endif



            Span<VertexAttributeDefinitionPlusBufferStruct> attrs = stackalloc VertexAttributeDefinitionPlusBufferStruct[p->ShaderHandle.Target.Metadata.VertexInputAttributes.Count];



            PipelineAttributeDetails layoutdetails = new();
            byte count = 0;

            foreach (var kv in p->ShaderHandle.Target.Metadata.VertexInputAttributes)
            {
                var ShaderAttributeLocation = kv.Value.Location;
                var ShaderDataFormat = kv.Value.Metadata.Format;

                var buf = p->AttributeCollection[kv.Key];




                if (buf.IsAllocated)
                {
                    var val = buf.Target;


#if DEBUG
                    //if (val.Definition.Stride % 4 != 0) 
                    //    throw new Exception("Strides must be power of 4");       some kind of checking needs to happen here for compatibility, but Im not sure what to enforce yet.
#endif


                    layoutdetails.Attributes[count] = new PipelineAttributeDetails.PipelineAttributeSpec(location: ShaderAttributeLocation,
                                                                                                         sourceFormat: val.Definition.ComponentFormat,
                                                                                                         finalFormat: ShaderDataFormat,
                                                                                                         stride: val.Definition.Stride,
                                                                                                         offset: val.Definition.Offset,
                                                                                                         scope: val.Definition.Scope);


                    attrs[count] = new VertexAttributeDefinitionPlusBufferStruct(val.Buffer.GetGenericGCHandle(), val.Definition);
                }

                else
                {
                    // dummy filler
                    layoutdetails.Attributes[count] = new PipelineAttributeDetails.PipelineAttributeSpec(location: ShaderAttributeLocation,
                                                                                                         sourceFormat: VertexAttributeBufferComponentFormat.Byte,
                                                                                                         finalFormat: ShaderDataFormat,
                                                                                                         stride: 0,
                                                                                                         offset: 0,
                                                                                                         scope: VertexAttributeScope.PerVertex);

                    attrs[count] = new VertexAttributeDefinitionPlusBufferStruct(DummyVertex.GetGenericGCHandle(), new());
                }




                count++;
            }

            layoutdetails.AttributeCount = count;





            Span<GCHandle<BackendResourceSetReference>> sets = stackalloc GCHandle<BackendResourceSetReference>[p->ShaderHandle.Target.Metadata.ResourceSets.Count];

            foreach (var kv in p->ShaderHandle.Target.Metadata.ResourceSets)
            {
                var idx = (int)kv.Value.Binding;
                var setGet = p->ResourceSetCollection[kv.Key];
                sets[idx] = setGet.IsAllocated ? setGet : p->ShaderHandle.Target.DefaultResourceSets[idx].GetGenericGCHandle();
            }




            DrawPipelineDetails drawdetails = new()
            {
                //derived from buffer definition + shader attributes
                Attributes = layoutdetails,

                ShaderHandle = GCHandle.ToIntPtr(p->ShaderHandle.Target.GCHandle),
                FrameBufferPipelineHandle = ActiveFramebufferPipeline == null ? default : GCHandle.ToIntPtr(ActiveFramebufferPipeline.GCHandle),

                //derived from draw call/material specifics
                Rasterization = p->Rasterization,
                Blending = p->Blending,
                DepthStencil = p->DepthStencil,

            };


            var get = BackendDrawPipelineReference.Get(drawdetails);


            RenderingBackend.Draw(attrs, sets, get, p->IndexBufferHandle.IsAllocated ? p->IndexBufferHandle.Target : null, 0, p->IndexingDetails); 

        }
    }



    /// <summary>
    /// Defers a scissor state write.
    /// </summary>
    /// <param name="offset"></param>
    /// <param name="size"></param
    /// 
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void SetScissor(Vector2<uint> offset, Vector2<uint> size) 
        => PushDeferredRenderThreadCommand(new SetScissorStruct(offset, size));


    private unsafe record struct SetScissorStruct(Vector2<uint> offset, Vector2<uint> size) : IDeferredCommand
    {
        public static unsafe void Execute(void* self)
        {
            var p = (SetScissorStruct*)self;
            RenderingBackend.SetScissor(p->offset, p->size);
        }
    }


    /// <summary>
    /// Defers a command to start drawing directly to the swapchain.
    /// </summary>
    /// 
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static unsafe void StartDrawToScreen() => PushDeferredRenderThreadCommand(&RenderingBackend.StartDrawToScreen);

    /// <summary>
    /// Defers a command to end drawing directly to the swapchain.
    /// </summary>
    /// 
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static unsafe void EndDrawToScreen() => PushDeferredRenderThreadCommand(&RenderingBackend.EndDrawToScreen);




}