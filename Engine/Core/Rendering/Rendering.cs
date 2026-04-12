


namespace Engine.Core;

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
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






        private FrameBufferPipelineStateOperator(LogicalFrameBufferObject fbo, BackendFrameBufferPipelineReference pipeline)
        {
            Pipeline = pipeline;
            Fbo = fbo;

            StageCount = Pipeline.Details.StageCount;

            PushDeferredRenderThreadCommand( ( fbo.GetFramebufferObjectForPipeline(Pipeline).GetWeakRef(), pipeline.GetWeakRef() ), &Execute);

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

            Stage++;
        }

    }












    /// <summary>
    /// Provides an abstract base for implementing an instance pool with minimal state.
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
    /// Provides an abstract base for implementing an instance pool with minimal state.
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











    public class PooledBuffer :
        PoolingBase<BackendBufferReference, uint>
    {

        public readonly BufferUsageFlags UsageFlags;
        public readonly ReadWriteFlags ReadWriteFlags;


        public readonly ShaderMetadata.ShaderDataBufferMetadata Metadata;


        private PooledBuffer(BufferUsageFlags usageFlags, ReadWriteFlags readWriteFlags, ShaderMetadata.ShaderDataBufferMetadata metadata) : base()
        {
            UsageFlags = usageFlags;
            ReadWriteFlags = readWriteFlags;
            Metadata = metadata;
        }


        public static PooledBuffer Create(BufferUsageFlags usageFlags, ReadWriteFlags readWriteFlags)
            => new(usageFlags, readWriteFlags, null);


        public static PooledBuffer CreateDataBufferFromMetadata(ShaderMetadata.ShaderDataBufferMetadata metadata, BufferUsageFlags extraUsageFlags = default, ReadWriteFlags extraAccessFlags = default)
            => new(metadata.UsageFlags | extraUsageFlags, metadata.ReadWriteFlags | extraAccessFlags, metadata);



        public BackendBufferReference GetReference() => GetCurrent();


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


        public static PooledResourceSet CreateFromMetadata(ShaderMetadata.ShaderResourceSetMetadata metadata)
            => new(metadata);


        public BackendResourceSetReference GetReference() => GetCurrent();


        protected override BackendResourceSetReference CreateNew()
            => BackendResourceSetReference.CreateFromMetadata(Metadata);


    }













    /// <summary>
    /// A collection of attachments to render to.
    /// <br /> This creates multiple <see cref="BackendFrameBufferObjectReference"/>s on demand to allow usage of any <see cref="BackendFrameBufferPipelineReference"/>.
    /// </summary>
    public class LogicalFrameBufferObject
    {

        public readonly ImmutableArray<BackendTextureReference> ColorAttachments;
        public readonly BackendTextureReference DepthStencil;

        public readonly Vector2<uint> Dimensions;

        public readonly TextureTypes Type;


        private readonly Dictionary<BackendFrameBufferPipelineReference, BackendFrameBufferObjectReference> Framebuffers = new(); 


        public void AddFramebufferObjectForPipeline(BackendFrameBufferPipelineReference pipeline)
        {
            var fbo = BackendFrameBufferObjectReference.Create(ColorAttachments.AsSpan()[..int.Min(ColorAttachments.Length, pipeline.Details.ColorAttachmentCount)], pipeline.Details.HasDepthStencil ? DepthStencil : null, pipeline, Dimensions);

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


        private LogicalFrameBufferObject(ImmutableArray<BackendTextureReference> colorAttachments, BackendTextureReference depthstencil, Vector2<uint> dimensions, TextureTypes type)
        {
            ColorAttachments = colorAttachments;
            DepthStencil = depthstencil;
            Dimensions = dimensions;
            Type = type;
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








    [StructLayout(LayoutKind.Sequential, Pack = 1, Size = 12)]
    private readonly record struct QuadVertex(Vector2 Position, byte U, byte V);



    /// <summary>
    /// Defers a draw call for a basic arbitrary 2D quad with an optional texture.
    /// </summary>
    /// <param name="bottomLeftNDC"></param>
    /// <param name="topRightNDC"></param>
    /// <param name="texture"></param>
    public static void DrawQuad(Vector2 bottomLeftNDC,
                                Vector2 topRightNDC,
                                BackendTextureAndSamplerReferencesPair texture = null)
    {


#if DEBUG
        if (!CompiledQuadDrawShader) 
            throw new Exception($"Call {nameof(InitQuadDrawDefaultShader)} from {nameof(Entry.InitShaders)} to enable this method");
#endif


        if (BasicQuadVertexBuffer == null)
        {
            BasicQuadVertexBuffer = PooledBuffer.Create(BufferUsageFlags.Vertex, ReadWriteFlags.CPUWrite);
            BasicQuadDefaultShader = new NamedShaderReference(BasicQuadDefaultShaderName);
            BasicQuadSetPool = PooledResourceSet.CreateFromMetadata(BasicQuadDefaultShader.Shader.Metadata.ResourceSets["Resources"].Metadata);
        }




        ReadOnlySpan<QuadVertex> bufData =
        [
            new() { Position = bottomLeftNDC, U = 0,   V = 0 },
            new() { Position = new(bottomLeftNDC.X, topRightNDC.Y), U = 0,   V = 255 },
            new() { Position = topRightNDC, U = 255, V = 255 },

            new() { Position = bottomLeftNDC, U = 0,   V = 0 },
            new() { Position = topRightNDC, U = 255, V = 255 },
            new() { Position = new(topRightNDC.X, bottomLeftNDC.Y), U = 255, V = 0 },
        ];



        BasicQuadVertexBuffer.Advance((uint)MemoryMarshal.AsBytes(bufData).Length);

        var buf = BasicQuadVertexBuffer.GetCurrent();

        buf.Write(bufData, 0, false);


        BasicQuadSetPool.Advance();
        BasicQuadSetPool.GetCurrent().SetResource("Texture", texture);




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
                            VertexAttributeBufferComponentFormat.Byte,
                            12,
                            8,
                            VertexAttributeScope.PerVertex
                        )
                    )
                }
            },

            new UnmanagedKeyValueCollection<WeakObjRef<string>, WeakObjRef<BackendResourceSetReference>>()
            {
                { "Resources".GetWeakRef(), BasicQuadSetPool.GetCurrent().GetWeakRef() }
            },

            BasicQuadDefaultShader.Shader,

            new() { CullMode = CullMode.Disabled },

            new() { Enable = true },

            new() { DepthWrite = false, DepthFunction = DepthOrStencilFunction.Always },

            null,

            new(0, 6, 0, 1)

            );

    }



    private static PooledBuffer BasicQuadVertexBuffer;
    private static PooledResourceSet BasicQuadSetPool;


    private static NamedShaderReference BasicQuadDefaultShader;


    private const string BasicQuadDefaultShaderName = "___default_draw_quad";


#if DEBUG
    private static bool CompiledQuadDrawShader = false;
#endif


#if DEBUG
    /// <summary>
    /// Registers a basic default shader, which enables <see cref="DrawQuad(Vector2, Vector2, BackendTextureAndSamplerReferencesPair)"/>.
    /// </summary>
    public static void InitQuadDrawDefaultShader()
    {

        CompiledQuadDrawShader = true;



        ShaderCompilation.RegisterShader(

            shaderName: BasicQuadDefaultShaderName,

            resourceSetNames:
            [
                "Resources"
            ],

            vertexSource:
            """

            in vec2 Position;
            in vec2 UV;

            out vec2 FragUV;
            out vec4 FragColor;

            void main()
            {
                FragUV = UV;
                FragColor = vec4(1.0);
                gl_Position = vec4(Position, 0.0, 1.0);
            }

            """,

            fragmentSource:
            """

            layout(set = 0) uniform sampler2D Texture;

            in vec2 FragUV;
            in vec4 FragColor;

            out vec4 FinalColor;

            void main()
            {
                FinalColor = FragColor * texture(Texture, FragUV);
            }

            """,


            languageHandler: ShaderCompilation.GLSL

        );
    }

#endif


    public static void ResetQuadDrawResources()
    {
        if (BasicQuadVertexBuffer != null)
        {
            BasicQuadVertexBuffer.Reset();
            BasicQuadSetPool.Reset();
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

        UnmanagedKeyValueCollection<WeakObjRef<string>, VertexAttributeDefinitionBufferPair.Struct> Attributes,
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
                if (!Attributes.TryGetUsingReference(kv.Key, out var _, out var _)) 
                    throw new Exception($"Missing vertex attribute buffer '{kv.Key}'\nGiven Attributes:\n{Attributes.ToString()}");
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

                    if (res == null)
                        throw new Exception($"Missing resource '{(res is BackendTextureAndSamplerReferencesPair ? set.Value.Dereference().Metadata.TexturesIndexed[i].Name : set.Value.Dereference().Metadata.BuffersIndexed[i].Name)}' in set '{set.Key.Dereference()}'");

                }
            }
        }

#endif

        PushDeferredRenderThreadCommand(new DrawCommandStruct(Attributes, ResourceSets, Shader, Rasterization, Blending, DepthStencil, IndexBuffer, IndexingDetails));
    }





    private struct DrawCommandStruct(UnmanagedKeyValueCollection<WeakObjRef<string>, VertexAttributeDefinitionBufferPair.Struct> attributeCollection,
                                     UnmanagedKeyValueCollection<WeakObjRef<string>, WeakObjRef<BackendResourceSetReference>> resourceSetCollection,
                                     BackendShaderReference shader,
                                     RasterizationDetails rasterization,
                                     BlendState blending,
                                     DepthStencilState depthStencil,
                                     BackendBufferReference.IIndexBuffer indexBuffer,
                                     IndexingDetails drawRange) : IDeferredCommand<DrawCommandStruct>
    {

        private UnmanagedKeyValueCollection<WeakObjRef<string>, VertexAttributeDefinitionBufferPair.Struct> AttributeCollection = attributeCollection;
        private UnmanagedKeyValueCollection<WeakObjRef<string>, WeakObjRef<BackendResourceSetReference>> ResourceSetCollection = resourceSetCollection;
        private WeakObjRef<BackendShaderReference> ShaderHandle = shader.GetWeakRef();

        private RasterizationDetails Rasterization = rasterization;
        private BlendState Blending = blending;
        private DepthStencilState DepthStencil = depthStencil;

        private WeakObjRef<BackendBufferReference.IIndexBuffer> IndexBufferHandle = indexBuffer == null ? default : indexBuffer.GetWeakRef();

        private IndexingDetails IndexingDetails = drawRange;



        public static unsafe void Execute(DrawCommandStruct* self)
        {


            if (self->IndexingDetails.End == self->IndexingDetails.Start) return;


#if DEBUG
            if (self->IndexingDetails.End < self->IndexingDetails.Start)
                throw new Exception();
#endif



            var shader = self->ShaderHandle.Dereference();


            Span<VertexAttributeDefinitionBufferPair.Struct> attrs = stackalloc VertexAttributeDefinitionBufferPair.Struct[shader.Metadata.VertexInputAttributes.Count];



            PipelineAttributeDetails layoutdetails = new();
            byte count = 0;

            foreach (var kv in shader.Metadata.VertexInputAttributes)
            {
                var ShaderAttributeLocation = kv.Value.Location;
                var ShaderDataFormat = kv.Value.Metadata.Format;




                if (self->AttributeCollection.TryGetUsingReference(kv.Key, out var keyHandle, out var value))
                {
                    var val = value;


                    layoutdetails.Attributes[count] = new PipelineAttributeDetails.PipelineAttributeSpec(location: ShaderAttributeLocation,
                                                                                                         sourceFormat: val.Definition.ComponentFormat,
                                                                                                         finalFormat: ShaderDataFormat,
                                                                                                         stride: val.Definition.Stride,
                                                                                                         offset: val.Definition.Offset,
                                                                                                         scope: val.Definition.Scope);


                    attrs[count] = value;
                }

                else
                {
                    layoutdetails.Attributes[count] = new PipelineAttributeDetails.PipelineAttributeSpec(location: ShaderAttributeLocation,
                                                                                                         sourceFormat: VertexAttributeBufferComponentFormat.Byte,
                                                                                                         finalFormat: ShaderDataFormat,
                                                                                                         stride: 0,
                                                                                                         offset: 0,
                                                                                                         scope: VertexAttributeScope.PerVertex);

                    attrs[count] = new VertexAttributeDefinitionBufferPair.Struct(DummyVertex.GetWeakRef(), new());
                }




                count++;
            }

            layoutdetails.AttributeCount = count;





            Span<WeakObjRef<BackendResourceSetReference>> sets = stackalloc WeakObjRef<BackendResourceSetReference>[shader.Metadata.ResourceSets.Count];

            foreach (var kv in shader.Metadata.ResourceSets)
            {
                var idx = (int)kv.Value.Binding;
                
                sets[idx] = self->ResourceSetCollection.TryGetUsingReference(kv.Key, out var kHandle, out var setGet) ? setGet : shader.DefaultResourceSets[idx].GetWeakRef();
            }




            DrawPipelineDetails drawdetails = new()
            {
                Attributes = layoutdetails,


                Shader = self -> ShaderHandle,
                FrameBufferPipeline = ActiveFramebufferPipeline == null ? default : ActiveFramebufferPipeline.GetWeakRef(),


                Rasterization = self->Rasterization,
                Blending = self->Blending,
                DepthStencil = self->DepthStencil,

            };


            var get = BackendDrawPipelineReference.Get(drawdetails);

            RenderingBackend.Draw(attrs, sets, get, self->IndexBufferHandle.Dereference(), 0, self->IndexingDetails); 

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
        => PushDeferredRenderThreadCommand(&RenderingBackend.StartDrawToScreen);


    /// <summary>
    /// Defers a command to end drawing directly to the swapchain.
    /// </summary>
    /// 
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static unsafe void EndDrawToScreen() 
        => PushDeferredRenderThreadCommand(&RenderingBackend.EndDrawToScreen);




}