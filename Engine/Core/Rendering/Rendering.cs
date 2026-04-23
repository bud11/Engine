


namespace Engine.Core;

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using static Engine.Core.References;
using static EngineMath;
using static RenderingBackend;
using static RenderingBackend.DrawPipelineDetails;
using static RenderThread;


#if DEBUG
using Engine.Stripped;
#endif


/// <summary>
/// Provides convinience and certain abstractions over <see cref="RenderingBackend"/>.
/// </summary>
public static class Rendering
{






    /// <summary>
    /// A lazy reference to a named <see cref="BackendShaderReference"/>.
    /// <br/> In other words, this (re)obtains a shader reference via <see cref="BackendShaderReference.Get"/> if it's internal reference is null or invalid, for example after shader hot reload.
    /// <br/> In release builds, this performs no check and is essentially equivalent to a direct <see cref="BackendShaderReference"/> reference.
    /// </summary>
    public sealed class NamedShaderReference(string shaderName)
    {
        public readonly string ShaderName = shaderName;

        private BackendShaderReference _shader = BackendShaderReference.Get(shaderName);


        public BackendShaderReference Shader
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get 
            {
#if DEBUG
                if (_shader == null || !_shader.Valid)
                {
                    _shader = BackendShaderReference.Get(ShaderName);
                }
#endif

                return _shader;
            }
        }
    }





    /// <summary>
    /// Manages/wraps around an active <see cref="BackendFrameBufferPipelineReference"/>.
    /// </summary>
    public unsafe ref struct FrameBufferPipelineStateOperator
    {

        private readonly BackendFrameBufferPipelineReference Pipeline;
        private readonly LogicalFrameBufferObject Fbo;

        private readonly byte StageCount;

        private byte Stage;







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

                SampleCount = framebuffer.SampleCount,
            };



            fixed (FrameBufferPipelineStage* p = stages)
                Unsafe.CopyBlockUnaligned(&details.Stages, p, (uint)sizeof(FrameBufferPipelineDetails.InlineStageArray));



            details.StageCount = (byte)stages.Length;


            for (int i = 0; i < framebuffer.ColorAttachments.Length; i++)
                details.ColorFormats[i] = (byte)((BackendTextureReference)framebuffer.ColorAttachments[i]).TextureFormat;


            var get = BackendFrameBufferPipelineReference.Get(details);

            if (framebuffer.GetFramebufferObjectForPipeline(get) == null)
                framebuffer.AddFramebufferObjectForPipeline(get);


            return new FrameBufferPipelineStateOperator(framebuffer, get);
        }






        private FrameBufferPipelineStateOperator(LogicalFrameBufferObject fbo, BackendFrameBufferPipelineReference pipeline)
        {
            Pipeline = pipeline;
            Fbo = fbo;

            var realFBO = fbo.GetFramebufferObjectForPipeline(Pipeline);


            StageCount = Pipeline.Details.StageCount;


            ActiveFrameBufferObject = realFBO;
            ActiveFramebufferPipeline = pipeline;


            PushDeferredRenderThreadCommand( (realFBO.GetWeakRef(), pipeline.GetWeakRef() ), &Execute);

            static unsafe void Execute((WeakObjRef<BackendFrameBufferObjectReference> fbo, WeakObjRef<BackendFrameBufferPipelineReference> pipeline)* ptr)
                => BeginFrameBufferPipeline(ptr->fbo.Dereference(), ptr->pipeline.Dereference());
        }




        /// <summary>
        /// Advances the underlying pipeline to the next stage, or ends it if there are no more stages.
        /// </summary>
        /// <exception cref="Exception"></exception>
        public unsafe void Advance()
        {
            if (Stage < StageCount - 1)
            {
                PushDeferredRenderThreadCommand( ( Fbo.GetFramebufferObjectForPipeline(Pipeline).GetWeakRef(), Pipeline.GetWeakRef() ), &Execute );
                
                static unsafe void Execute((WeakObjRef<BackendFrameBufferObjectReference> fbo, WeakObjRef<BackendFrameBufferPipelineReference> pipeline )* ptr)
                    => AdvanceFrameBufferPipeline(ptr->fbo.Dereference(), ptr->pipeline.Dereference());
            }
            else
            {

                PushDeferredRenderThreadCommand(Fbo.GetFramebufferObjectForPipeline(Pipeline).GetWeakRef(), &Execute);
                
                static unsafe void Execute(WeakObjRef<BackendFrameBufferObjectReference>* ptr)
                    => EndFrameBufferPipeline(ptr->Dereference());
            }

            ActiveFrameBufferPipelineStage = Stage++;
        }

    }












    /// <summary>
    /// Provides an abstract base for implementing an instance pool, where instances can be created and/or reused if they meet some critera encoded into <typeparamref name="TArg"/>.
    /// <br/> The order of instance reuse is not guaranteed.
    /// </summary>
    /// <typeparam name="TInstance"></typeparam>
    /// <typeparam name="TArg"></typeparam>
    public abstract class PoolingBase<TInstance, TArg>
    {

        private readonly List<TInstance> Pool = new();

        private int Current = -1;


        /// <summary>
        /// Gets the current instance.
        /// </summary>
        /// <returns></returns>
        public TInstance GetCurrent() => Current == -1 ? throw new InvalidOperationException(
#if DEBUG
            "Call Advance first!"
#endif
            ) : Pool[Current];



        /// <summary>
        /// Creates and moves to a new instance of <typeparamref name="TInstance"/>.
        /// </summary>
        /// <param name="requirement"></param>
        public void Advance(TArg requirement)
        {

            Current++;


            for (int i = Current; i < Pool.Count; i++)
            {
                var candidate = Pool[i];
                if (TryReuse(candidate, requirement))
                {
                    if (i != Current)
                    {
                        Pool[i] = Pool[Current];
                        Pool[Current] = candidate;
                    }
                    return; 
                }
            }


            var newBuffer = CreateNew(requirement);

            if (Current < Pool.Count)
                Pool[Current] = newBuffer;
            else
                Pool.Add(newBuffer);
        }




        /// <summary>
        /// Marks all instances as unused besides the current.
        /// </summary>
        public void Reset() => Current = -1;


        /// <summary>
        /// Creates a new underlying instance which meets a certain requirement.
        /// </summary>
        /// <param name="arg"></param>
        /// <returns></returns>
        protected abstract TInstance CreateNew(TArg arg);


        /// <summary>
        /// Checks if this instance meets the requirement for reuse, and if it is, prepares it to be used if needed, and returns true.
        /// </summary>
        /// <param name="res"></param>
        /// <param name="arg"></param>
        /// <returns></returns>
        protected abstract bool TryReuse(TInstance res, TArg arg);


    }




    /// <summary>
    /// Provides an abstract base for implementing an instance pool.
    /// <br/> The order of instance reuse is guaranteed to be the same as the order of creation.
    /// </summary>
    /// <typeparam name="TInstance"></typeparam>
    public abstract class PoolingBase<TInstance>
    {

        private readonly List<TInstance> Pool = new();

        private int Current = -1;


        /// <summary>
        /// Gets the current instance.
        /// </summary>
        /// <returns></returns>
        public TInstance GetCurrent() => Current == -1 ? throw new InvalidOperationException(
#if DEBUG
            "Call Advance first!"
#endif
            ) : Pool[Current];



        /// <summary>
        /// Creates and moves to a new instance of <typeparamref name="TInstance"/>.
        /// </summary>
        public void Advance()
        {
            Current++;

            if (Current <= Pool.Count-1)
                return;

            Pool.Add(CreateNew());
        }


        /// <summary>
        /// Marks all instances as unused besides the current.
        /// </summary>
        public void Reset() => Current = -1;


        /// <summary>
        /// Creates a new underlying instance which meets a certain requirement.
        /// </summary>
        /// <returns></returns>
        protected abstract TInstance CreateNew();

    }









    public class FixedPooledBuffer :
        PoolingBase<BackendBufferReference>
    {

        public readonly BufferUsageFlags UsageFlags;
        public readonly ReadWriteFlags ReadWriteFlags;


        public readonly ShaderMetadata.ShaderDataBufferMetadata Metadata;


        private FixedPooledBuffer(BufferUsageFlags usageFlags, ReadWriteFlags readWriteFlags, ShaderMetadata.ShaderDataBufferMetadata metadata) : base()
        {
            UsageFlags = usageFlags;
            ReadWriteFlags = readWriteFlags;
            Metadata = metadata;
        }


        public static FixedPooledBuffer Create(BufferUsageFlags usageFlags, ReadWriteFlags readWriteFlags)
            => new(usageFlags, readWriteFlags, null);


        public static FixedPooledBuffer CreateDataBufferFromMetadata(ShaderMetadata.ShaderDataBufferMetadata metadata, BufferUsageFlags extraUsageFlags = default, ReadWriteFlags extraAccessFlags = default)
            => new(metadata.UsageFlags | extraUsageFlags, metadata.ReadWriteFlags | extraAccessFlags, metadata);



        protected override BackendBufferReference CreateNew()
            => Metadata == null ? BackendBufferReference.Create(Metadata.SizeRequirement, UsageFlags, ReadWriteFlags) : (BackendBufferReference)BackendBufferReference.CreateDataBufferFromMetadata(Metadata, extraUsageFlags: UsageFlags, extraAccessFlags: ReadWriteFlags);


    }




    public class FlexiblePooledBuffer :
        PoolingBase<BackendBufferReference, uint>
    {

        public readonly BufferUsageFlags UsageFlags;
        public readonly ReadWriteFlags ReadWriteFlags;


        public readonly ShaderMetadata.ShaderDataBufferMetadata Metadata;


        private FlexiblePooledBuffer(BufferUsageFlags usageFlags, ReadWriteFlags readWriteFlags, ShaderMetadata.ShaderDataBufferMetadata metadata) : base()
        {
            UsageFlags = usageFlags;
            ReadWriteFlags = readWriteFlags;
            Metadata = metadata;
        }


        public static FlexiblePooledBuffer Create(BufferUsageFlags usageFlags, ReadWriteFlags readWriteFlags)
            => new(usageFlags, readWriteFlags, null);


        public static FlexiblePooledBuffer CreateDataBufferFromMetadata(ShaderMetadata.ShaderDataBufferMetadata metadata, BufferUsageFlags extraUsageFlags = default, ReadWriteFlags extraAccessFlags = default)
            => new(metadata.UsageFlags | extraUsageFlags, metadata.ReadWriteFlags | extraAccessFlags, metadata);



        protected override BackendBufferReference CreateNew(uint size)
            => Metadata == null ? BackendBufferReference.Create(size, UsageFlags, ReadWriteFlags) : (BackendBufferReference)BackendBufferReference.CreateDataBufferFromMetadata(Metadata, extraUsageFlags: UsageFlags, extraAccessFlags : ReadWriteFlags);


        protected override bool TryReuse(BackendBufferReference res, uint size)
            => res.Size >= size;


    }



    public class PooledResourceSet :
        PoolingBase<BackendResourceSetReference>
    {

        public readonly ShaderMetadata.ShaderResourceSetMetadata Metadata;

        private PooledResourceSet(ShaderMetadata.ShaderResourceSetMetadata metadata) : base()
            => Metadata = metadata;


        public static PooledResourceSet CreateFromMetadata(string name)
            => new(GlobalResourceSetMetadata[name]);

        public static PooledResourceSet CreateFromMetadata(ShaderMetadata.ShaderResourceSetMetadata metadata)
            => new(metadata);

        protected override BackendResourceSetReference CreateNew()
            => BackendResourceSetReference.CreateFromMetadata(Metadata);


    }













    /// <summary>
    /// A collection of attachments to render to.
    /// <br /> This creates multiple <see cref="BackendFrameBufferObjectReference"/>s on demand to allow usage of any <see cref="BackendFrameBufferPipelineReference"/>.
    /// </summary>
    public class LogicalFrameBufferObject
    {

        public readonly MultiSampleCount SampleCount;


        public readonly ImmutableArray<IFramebufferAttachment> ColorAttachments;
        public readonly IFramebufferAttachment DepthStencil;


        public readonly Vector2<uint> Size;



        private readonly Dictionary<BackendFrameBufferPipelineReference, BackendFrameBufferObjectReference> Framebuffers = new(); 





        public void AddFramebufferObjectForPipeline(BackendFrameBufferPipelineReference pipeline)
        {
            var fbo = BackendFrameBufferObjectReference.Create(ColorAttachments.AsSpan()[..int.Min(ColorAttachments.Length, pipeline.Details.ColorAttachmentCount)], pipeline.Details.HasDepthStencil ? DepthStencil : null, pipeline, Size);

            if (!Framebuffers.TryAdd(pipeline, fbo)) throw new Exception();
        }


        public void RemoveFramebufferObjectForPipeline(BackendFrameBufferPipelineReference pipeline)
        {
            if (!Framebuffers.TryGetValue(pipeline, out var fbo))
                throw new Exception();

            Framebuffers.Remove(pipeline);
        }


        public BackendFrameBufferObjectReference GetFramebufferObjectForPipeline(BackendFrameBufferPipelineReference pipeline)
        {
            if (!Framebuffers.TryGetValue(pipeline, out var get)) return null;

            return get;
        }


        private LogicalFrameBufferObject(ImmutableArray<IFramebufferAttachment> colorAttachments, IFramebufferAttachment depthstencil, Vector2<uint> size, MultiSampleCount sampleCount)
        {
            ColorAttachments = colorAttachments;
            DepthStencil = depthstencil;
            Size = size;
            SampleCount = sampleCount;
        }




        public static unsafe LogicalFrameBufferObject Create(ReadOnlySpan<IFramebufferAttachment> colorTargets, IFramebufferAttachment depthStencilTarget)
        {

#if DEBUG
            var chk = colorTargets.ToArray().Cast<BackendTextureReference>().ToList();
            if (depthStencilTarget != null) chk.Add((BackendTextureReference) depthStencilTarget);

            if (chk.Count == 0)
                throw new Exception("no attachments given");

            foreach (var k in chk)
            {
                if (!k.Valid)
                    throw new Exception("invalid attachment supplied");
            }

            foreach (var k in chk)
            {
                if (k.Size != chk[0].Size)
                    throw new Exception("attachment sizes dont match");
            }

            foreach (var k in chk)
            {
                if ((!k.Valid) || k.MultiSampleCount != chk[0].MultiSampleCount)
                    throw new Exception("attachment sample counts dont match");
            }
#endif


            //at least one color attachment or a depth stencil target needs to be given
            var onecommon = (BackendTextureReference)(colorTargets.Length != 0 ? colorTargets[0] : depthStencilTarget);

            return new LogicalFrameBufferObject(ImmutableArray.Create(colorTargets), depthStencilTarget, new Vector2<uint>(onecommon.Size.X, onecommon.Size.Y), onecommon.MultiSampleCount);
        }
    }








    [StructLayout(LayoutKind.Sequential, Pack = 1, Size = 12)]
    private readonly record struct QuadVertex(Vector2 Position, Vector2<byte> UV);



    /// <summary>
    /// Defers a draw call for a basic arbitrary 2D quad.
    /// <br/> The given shader can accept the given resource set, under the name "Resources".
    /// <br/> The shader can also accept the vec2 vertex attributes "Position" and "UV".
    /// </summary>
    /// <param name="bottomLeftNDC"></param>
    /// <param name="topRightNDC"></param>
    /// <param name="resources"></param>
    /// <param name="shader"></param>
    public static void DrawQuad(Vector2 bottomLeftNDC,
                                Vector2 topRightNDC,
                                BackendResourceSetReference resources,
                                BackendShaderReference shader)
    {




        BasicQuadVertexBuffer ??= FlexiblePooledBuffer.Create(BufferUsageFlags.Vertex, ReadWriteFlags.CPUWrite);




        ReadOnlySpan<QuadVertex> bufData =
        [
            new(bottomLeftNDC, new(0, 0)),
            new(new(bottomLeftNDC.X, topRightNDC.Y), new(0, 255)),
            new(topRightNDC, new(255, 255)),

            new(bottomLeftNDC, new(0, 0)),
            new(topRightNDC, new(255, 255)),
            new(new(topRightNDC.X, bottomLeftNDC.Y), new(255, 0)),
        ];



        BasicQuadVertexBuffer.Advance((uint)MemoryMarshal.AsBytes(bufData).Length);

        var buf = BasicQuadVertexBuffer.GetCurrent();

        buf.Write(bufData, 0, false);





        Draw(
            new UnmanagedKeyValueCollection<WeakObjRef<string>, VertexAttributeDefinitionBufferPair.Struct>()
            {
                {
                    "Position".GetWeakRef(),

                    new VertexAttributeDefinitionBufferPair.Struct(
                        ((BackendBufferReference.IVertexBuffer)buf).GetWeakRef(),
                        new
                        (
                            VertexAttributeBufferComponentFormat.Float,
                            12,
                            0,
                            VertexAttributeScope.PerVertex
                        )
                    )
                },

                {
                    "UV".GetWeakRef(),

                    new VertexAttributeDefinitionBufferPair.Struct(
                        ((BackendBufferReference.IVertexBuffer)buf).GetWeakRef(),
                        new
                        (
                            VertexAttributeBufferComponentFormat.UByteNormalized,
                            12,
                            8,
                            VertexAttributeScope.PerVertex
                        )
                    )
                }
            },

            new UnmanagedKeyValueCollection<WeakObjRef<string>, WeakObjRef<BackendResourceSetReference>>()
            {
                { "Resources".GetWeakRef(), resources.GetWeakRef() }
            },

            shader,

            new() { CullMode = CullMode.Disabled },

            new() { Enable = true },

            new() { DepthWrite = false, DepthFunction = DepthOrStencilFunction.Always },

            null,

            new(0, 6, 0, 1, default)

            );

    }


    private static FlexiblePooledBuffer BasicQuadVertexBuffer;

    public static void ResetQuadDrawResources()
    {
        BasicQuadVertexBuffer?.Reset();
    }











    /// <summary>
    /// Defers an arbitrary draw call.
    /// </summary>
    /// <param name="VertexAttributes"></param>
    /// <param name="ResourceSets"></param>
    /// <param name="Shader"></param>
    /// <param name="Rasterization"></param>
    /// <param name="Blending"></param>
    /// <param name="DepthStencil"></param>
    /// <param name="IndexBuffer"></param>
    /// <param name="IndexingDetails"></param>

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static unsafe void Draw(

        UnmanagedKeyValueCollection<WeakObjRef<string>, VertexAttributeDefinitionBufferPair.Struct> VertexAttributes,
        UnmanagedKeyValueCollection<WeakObjRef<string>, WeakObjRef<BackendResourceSetReference>> ResourceSets,
        BackendShaderReference Shader,

        RasterizationDetails Rasterization,
        BlendState Blending,
        DepthStencilState DepthStencil,

        BackendBufferReference.IIndexBuffer IndexBuffer,
        IndexingDetails IndexingDetails

        )
    {


#if DEBUG

        if (EngineDebug.ThrowIfVertexBufferMissing)
        {
            foreach (var kv in Shader.Metadata.VertexInputAttributes)
            {
                if (!VertexAttributes.TryGetUsingReference(kv.Key, out var _, out var _)) 
                    throw new Exception($"Missing vertex attribute buffer '{kv.Key}'\nGiven Attributes:\n{VertexAttributes.ToString()}");
            }
        }

        if (EngineDebug.ThrowIfResourceSetMissing)
        {
            foreach (var kv in Shader.Metadata.ResourceSets)
            {
                if (!ResourceSets.TryGetUsingReference(kv.Key, out var _, out var _))
                    throw new Exception($"Missing resource set '{kv.Key}'\nGiven Sets:\n{ResourceSets.ToString()}");
            }
        }

        if (EngineDebug.ThrowIfResourceMissing)
        {
            foreach (var set in ResourceSets)
            {
                var sp = set.Value.Dereference().GetContents();

                for (byte i = 0; i < sp.Length; i++)
                {
                    var res = sp[i];

                    if (res.Binding == null)
                        throw new Exception($"Missing resource '{res.Name}' in set '{set.Key.Dereference()}'");

                }
            }
        }

        if (IndexingDetails.End < IndexingDetails.Start)
            throw new Exception();
#endif




        if (IndexingDetails.End == IndexingDetails.Start) return;


        var draw = new Rendering_Draw() 
        {
            idxBuffer = IndexBuffer.GetWeakRef(), 
            idxDetails = IndexingDetails 
        };



        PipelineAttributeDetails layoutdetails = new();
        byte count = 0;

        foreach (var kv in Shader.Metadata.VertexInputAttributes)
        {
            var ShaderAttributeLocation = kv.Value.Location;
            var ShaderDataFormat = kv.Value.Metadata.Format;




            if (VertexAttributes.TryGetUsingReference(kv.Key, out var keyHandle, out var value))
            {
                var val = value;


                layoutdetails.Attributes[count] = new PipelineAttributeDetails.PipelineAttributeSpec(location: ShaderAttributeLocation,
                                                                                                     sourceFormat: val.Definition.ComponentFormat,
                                                                                                     finalFormat: ShaderDataFormat,
                                                                                                     stride: val.Definition.Stride,
                                                                                                     offset: val.Definition.Offset,
                                                                                                     scope: val.Definition.Scope);


                draw.attrs[count] = value;
            }

            else
            {
                layoutdetails.Attributes[count] = new PipelineAttributeDetails.PipelineAttributeSpec(location: ShaderAttributeLocation,
                                                                                                     sourceFormat: VertexAttributeBufferComponentFormat.UByteNormalized,
                                                                                                     finalFormat: ShaderDataFormat,
                                                                                                     stride: 0,
                                                                                                     offset: 0,
                                                                                                     scope: VertexAttributeScope.PerVertex);


                draw.attrs[count] = new VertexAttributeDefinitionBufferPair.Struct(((BackendBufferReference.IVertexBuffer)BackendBufferReference.GetPlaceholder((1, BufferUsageFlags.Vertex, default))).GetWeakRef(), new());
            }



            count++;
        }


        draw.attrCount = layoutdetails.AttributeCount = count;
        




        foreach (var kv in Shader.Metadata.ResourceSets)
        {
            var idx = (int)kv.Value.Binding;

            draw.sets[idx] = ResourceSets.TryGetUsingReference(kv.Key, out var kHandle, out var setGet) ? setGet : Shader.DefaultResourceSets[idx].GetWeakRef();

#if DEBUG
            foreach (var res in draw.sets[idx].Dereference().GetContents())
            {
                if (res.Binding is IBackendTextureSamplerPair tex)
                    validate(tex.Texture, res.Name, kv.Key, Shader.Metadata.Name);

                else if (res.Binding is IBackendTextureSamplerPair[] texarr)
                    for (int i = 0; i <  texarr.Length; i++)
                        validate(texarr[i].Texture, res.Name, kv.Key, Shader.Metadata.Name);


                static void validate(BackendTextureReference tex, string name, string ressetname, string shadername)
                {
                    if (ActiveFrameBufferObject != null && tex is IFramebufferAttachment attachment && (ActiveFrameBufferObject.ColorAttachments.Contains(attachment) || ActiveFrameBufferObject.DepthStencilAttachment == tex))
                        throw new Exception($"Texture '{name}', being consumed by resource set '{ressetname}' in shader '{shadername}', is also currently being used as part of the active framebuffer");
                }
            }
#endif
        }

        draw.setCount = (byte)Shader.Metadata.ResourceSets.Count;




        draw.drawpipeline = BackendDrawPipelineReference.Get(new DrawPipelineDetails()
        {
            Attributes = layoutdetails,

            Shader = Shader.GetWeakRef(),
            FrameBufferPipeline = ActiveFramebufferPipeline == null ? default : ActiveFramebufferPipeline.GetWeakRef(),

            Rasterization = Rasterization,
            Blending = Blending,
            DepthStencil = DepthStencil,
        
        }).GetWeakRef();


        PushDeferredRenderThreadCommand(draw);
        
    }



    private unsafe struct Rendering_Draw : IDeferredCommand<Rendering_Draw>
    {
        public WeakObjRef<BackendDrawPipelineReference> drawpipeline;

        public VertexAttributes attrs;
        public byte attrCount;

        public ResourceSets sets;
        public byte setCount;

        public WeakObjRef<BackendBufferReference.IIndexBuffer> idxBuffer;
        public IndexingDetails idxDetails;


        [InlineArray(16)]
        public struct VertexAttributes { public VertexAttributeDefinitionBufferPair.Struct attrib; }

        [InlineArray(16)]
        public struct ResourceSets { public WeakObjRef<BackendResourceSetReference> set; }


        public static unsafe void Execute(Rendering_Draw* self)
        {
            RenderingBackend.Draw(
                new ReadOnlySpan<VertexAttributeDefinitionBufferPair.Struct>(&self->attrs, self->attrCount), 
                new ReadOnlySpan<WeakObjRef<BackendResourceSetReference>>(&self->sets, self->setCount), 
                self->drawpipeline.Dereference(),
                self->idxBuffer.Dereference(),
                0,
                self->idxDetails);
        }
    }

















    /// <summary>
    /// Defers a scissor state write.
    /// </summary>
    /// <param name="offset"></param>
    /// <param name="size"></param
    /// 
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static unsafe void SetScissor(Vector2<uint> offset, Vector2<uint> size)
    {
        PushDeferredRenderThreadCommand( ( offset, size ), &Execute);

        static void Execute((Vector2<uint> offset, Vector2<uint> size)* p)
            => RenderingBackend.SetScissor(p->offset, p->size);
    }


    /// <summary>
    /// Defers a command to start drawing directly to the swapchain.
    /// </summary>
    /// 
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static unsafe void StartDrawToScreen()
    {
        ActiveFramebufferPipeline = null;
        ActiveFrameBufferObject = null;
        ActiveFrameBufferPipelineStage = 0;

        PushDeferredRenderThreadCommand(&RenderingBackend.StartDrawToScreen);
    }


    /// <summary>
    /// Defers a command to end drawing directly to the swapchain.
    /// </summary>
    /// 
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static unsafe void EndDrawToScreen() 
        => PushDeferredRenderThreadCommand(&RenderingBackend.EndDrawToScreen);




}