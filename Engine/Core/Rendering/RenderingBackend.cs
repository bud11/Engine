


namespace Engine.Core;

using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using static Comparisons;
using static EngineMath;
using static RenderingBackend.ResourceSetResourceDeclaration;
using static RenderThread;




/// <summary>
/// Provides near direct immediate access to the rendering backend. Also see <seealso cref="Rendering"/> and/or <seealso cref="IDeferredCommand"/>
/// </summary>
public static partial class RenderingBackend
{







    public static SDL3.SDL.WindowFlags GetSDLWindowFlagsForBackend(RenderingBackendEnum Backend)
        => RenderingBackendData[Backend.ToString()].Flags;


    public static ShaderFormat GetRequiredShaderFormatForBackend(RenderingBackendEnum Backend)
        => RenderingBackendData[Backend.ToString()].RequiredShaderFormat;




    public static void CreateBackend(RenderingBackendEnum Backend, nint sdlwindow)
    {
        var get = RenderingBackendData[Backend.ToString()];

        RenderingBackend.Backend = get.Constructor.Invoke(sdlwindow);
        CurrentBackend = Backend;




        CreateBasicObjects();


#if RELEASE 
        foreach (var kv in ShaderSources[RenderingBackendEnum.Vulkan])
            Shaders[kv.Key] = new BackendShaderReference(kv.Value.Metadata, CreateShader(kv.Value));

        foreach (var kv in ComputeShaderSources[RenderingBackendEnum.Vulkan])
            ComputeShaders[kv.Key] = new BackendComputeShaderReference(kv.Value.Metadata, CreateComputeShader(kv.Value));
#endif


        ConfigureSwapchain(Window.GetWindowClientArea(), EngineSettings.HDR);

    }









    /// <summary>
    /// Embodies a reference to a logical rendering backend resource.
    /// <br/> <see cref="BackendReference"/>s should always feel like direct or near direct references to gpu resources.
    /// </summary>
    /// <param name="backendRef"></param>
    public abstract class BackendReference(object backendRef) : RefCounted
    {
        /// <summary>
        /// A rendering-backend-specific object. Usually contains something like a resource handle. Can be null.
        /// <br /> <b>! ! ! Should NEVER be modified or referenced by anything besides the <see cref="IRenderingBackend"/> that created it and any attempt to do so will likely backfire. ! ! !</b>
        /// <br /> Using this to store creation/state information is usually redundant due to the encompassing class already doing that.
        /// </summary>
        public object BackendRef = backendRef;

        protected override abstract void OnFree();
    }








#if DEBUG
    public static Dictionary<string, ShaderMetadata.ShaderResourceSetMetadata> GlobalResourceSetMetadata = new();
#endif





    private static Dictionary<ShaderMetadata.ShaderResourceSetMetadata, BackendResourceSetReference> DummyResourceSets = new();


    public static BackendTextureAndSamplerReferencesPair Dummy2DTextureSamplerPair { get; private set; }
    public static BackendTextureAndSamplerReferencesPair Dummy2DShadowTextureSamplerPair { get; private set; }
    public static BackendTextureAndSamplerReferencesPair DummyCubeTextureSamplerPair { get; private set; }
    public static BackendTextureAndSamplerReferencesPair Dummy3DTextureSamplerPair { get; private set; }

    public static BackendVertexBufferAllocationReference DummyVertex { get; private set; }




    private static SortedDictionary<uint, BackendUniformBufferAllocationReference> DummyUBOs = new();

    public static BackendUniformBufferAllocationReference GetDummyUBO(uint sizeReq)
    {
        lock (DummyUBOs)
        {
            foreach (var kv in DummyUBOs)
                if (kv.Value.Size >= sizeReq) 
                    return kv.Value;


            var ret = DummyUBOs[sizeReq] = BackendUniformBufferAllocationReference.Create(sizeReq, false);

            return ret;
        }
    }


    private static SortedDictionary<uint, BackendStorageBufferAllocationReference> DummySSBOs = new();

    public static BackendStorageBufferAllocationReference GetDummySSBO(uint sizeReq)
    {
        lock (DummySSBOs)
        {
            foreach (var kv in DummySSBOs)
                if (kv.Value.Size >= sizeReq)
                    return kv.Value;


            var ret = DummySSBOs[sizeReq] = BackendStorageBufferAllocationReference.Create(sizeReq, false);

            return ret;
        }
    }


    public static bool IsResourceDummy(IResourceSetResource res)
    {
        if (res == Dummy2DTextureSamplerPair) return true;
        if (res == Dummy2DShadowTextureSamplerPair) return true;
        if (res == Dummy3DTextureSamplerPair) return true;
        if (res == DummyCubeTextureSamplerPair) return true;

        if (DummyUBOs.Values.Contains(res)) return true;
        if (DummySSBOs.Values.Contains(res)) return true;

        return false;
    }






    public static unsafe void CreateBasicObjects()
    {

        DummyVertex = BackendVertexBufferAllocationReference.Create(1, false);



        //solid white 1x1
        Dummy2DTextureSamplerPair = new BackendTextureAndSamplerReferencesPair(
            BackendTextureReference.Create(new Vector3<uint>(1), TextureTypes.Texture2D, TextureFormats.RGB8_UNORM, FramebufferAttachmentCompatible: false, Mips: [[255,255,255]]),
            BackendSamplerReference.Get(new(TextureWrapModes.Repeat, TextureFilters.Nearest, TextureFilters.Nearest, TextureFilters.Nearest, EnableDepthComparison: false)));


        //24 8 depth stencil - max depth, no stencil mask
        Dummy2DShadowTextureSamplerPair = new BackendTextureAndSamplerReferencesPair(
            BackendTextureReference.Create(new Vector3<uint>(1), TextureTypes.Texture2D, TextureFormats.DepthStencil, FramebufferAttachmentCompatible: false, Mips: [[0xFF, 0xFF, 0xFF, 0x00]]),
            BackendSamplerReference.Get(new(TextureWrapModes.Repeat, TextureFilters.Nearest, TextureFilters.Nearest, TextureFilters.Nearest, EnableDepthComparison: true)));


        //solid white 1x1 for each face
        DummyCubeTextureSamplerPair = new BackendTextureAndSamplerReferencesPair(
            BackendTextureReference.Create(new Vector3<uint>(1), TextureTypes.TextureCubeMap, TextureFormats.RGB8_UNORM, FramebufferAttachmentCompatible: false, 
            Mips: 
            [
                [
                    255, 255, 255,
                    255, 255, 255,
                    255, 255, 255,
                    255, 255, 255,
                    255, 255, 255,
                    255, 255, 255,
                ]
            ]
            ),
            BackendSamplerReference.Get(new(TextureWrapModes.Repeat, TextureFilters.Nearest, TextureFilters.Nearest, TextureFilters.Nearest, EnableDepthComparison: false)));


        //solid white 1x1x1
        Dummy3DTextureSamplerPair = new BackendTextureAndSamplerReferencesPair(
            BackendTextureReference.Create(new Vector3<uint>(1), TextureTypes.Texture3D, TextureFormats.RGB8_UNORM, FramebufferAttachmentCompatible: false, Mips: [[255, 255, 255]]),
            BackendSamplerReference.Get(new(TextureWrapModes.Repeat, TextureFilters.Nearest, TextureFilters.Nearest, TextureFilters.Nearest, EnableDepthComparison: false)));

    }











    /// <summary>
    /// A <see cref="BackendVertexBufferAllocationReference"/> <see cref="GCHandle"/> and a <see cref="VertexAttributeDefinition"/> bundled together.
    /// </summary>
    /// <param name="Buffer"></param>
    /// <param name="Definition"></param>
    public readonly record struct VertexAttributeDefinitionPlusBufferStruct(GCHandle<BackendVertexBufferAllocationReference> Buffer, VertexAttributeDefinition Definition);


    /// <summary>
    /// A <see cref="BackendVertexBufferAllocationReference"/> and a <see cref="VertexAttributeDefinition"/> bundled together.
    /// </summary>
    /// <param name="Buffer"></param>
    /// <param name="Definition"></param>
    public record class VertexAttributeDefinitionPlusBufferClass(BackendVertexBufferAllocationReference Buffer, VertexAttributeDefinition Definition);




    /// <summary>
    /// Defines how the gpu should parse a vertex attribute buffer.
    /// </summary>
    /// <param name="ComponentFormat"></param>
    /// <param name="Stride"></param>
    /// <param name="Offset"></param>
    /// <param name="Scope"></param>
    public unsafe record struct VertexAttributeDefinition(
         VertexAttributeBufferComponentFormat ComponentFormat,

         ushort Stride,
         ushort Offset,

         VertexAttributeScope Scope
    );




    public unsafe record struct IndexingDetails(uint Start, uint End, uint BaseVertex, uint InstanceCount);





    /// <summary>
    /// Represents a gpu-side buffer.
    /// <br/> This may not literally represent a unique perfectly sized buffer, depending on how the chosen rendering backend implements buffer allocation.
    /// <br/> For example, it could just be pointing to a slice of a larger internal buffer. But it can be reliably thought of as one coherent user-owned buffer that meets the specifications requested.
    /// </summary>
    public abstract unsafe class BackendBufferAllocationReference : BackendReference
    {



        public readonly uint Size;

        public readonly bool Writeable;


        protected BackendBufferAllocationReference(uint size, bool writeable, object backendRef) : base(backendRef)
        {
            Size = size;
            Writeable = writeable;

            Logic.AppendPermanentEndOfFrameAction(() => WritesThisFrame = 0);
        }





        private static unsafe void WriteToBuffer(BackendBufferAllocationReference buffer, ReadOnlySpan<WriteRange> writes, uint idx)
        {
            CheckOutsideOfRendering();
            Backend.WriteToBuffer(buffer, writes, idx);
        }


        private static unsafe void AdvanceActiveBufferWrite(BackendBufferAllocationReference buffer, uint idx)
        {
            if (idx != 0) CheckDuringRendering();
            Backend.AdvanceActiveBufferWrite(buffer, idx);
        }









        private uint WritesThisFrame;


        public unsafe void Write(WriteRange* writes, uint count, bool nessecary)
        {

#if DEBUG
            for (int i = 0; i < count; i++)
            {
                ref var g = ref writes[i];
                if (g.Offset + g.Length > Size) throw new Exception();
            }
#endif


            if (nessecary)
            {
                PushRenderThreadAction(() =>
                {
                    WriteToBuffer(this, new ReadOnlySpan<WriteRange>(writes, (int)count), 0);
                    AdvanceActiveBufferWrite(this, 0);
                    return null;
                }).Wait();
            }
            else
            {
                WriteRange* alloc = (WriteRange*)AllocateRenderTemporaryUnmanaged((int)(sizeof(WriteRange) * count));
                Unsafe.CopyBlockUnaligned(alloc, writes, (uint)(count*sizeof(WriteRange)));

                PushDeferredPreRenderThreadCommand(new WriteStruct(this, alloc, count, WritesThisFrame));
                PushDeferredRenderThreadCommand(new InlineAdvance(this, WritesThisFrame));

                WritesThisFrame++;
            }
        }



        private unsafe readonly struct WriteImmediateStruct(BackendBufferAllocationReference set, WriteRange[] writes) : IDeferredCommand
        {
            private readonly GCHandle<BackendBufferAllocationReference> buf = set.GetGenericGCHandle();
            private readonly GCHandle<WriteRange[]> writes = GCHandle<WriteRange[]>.Alloc(writes, GCHandleType.Normal);

            public static void Execute(void* self)
            {
                var p = (WriteImmediateStruct*)self;
                WriteToBuffer(p->buf.Target, p->writes.Target, 0);
                AdvanceActiveBufferWrite(p->buf.Target, 0);

                p->writes.Free();
            }
        }



        private unsafe readonly struct WriteStruct(BackendBufferAllocationReference buf, WriteRange* writes, uint count, uint index) : IDeferredCommand
        {
            private readonly GCHandle<BackendBufferAllocationReference> buf = buf.GetGenericGCHandle();
            private readonly WriteRange* writes = writes;
            private readonly uint count = count;
            private readonly uint index = index;

            public static void Execute(void* self)
            {
                var p = (WriteStruct*)self;
                WriteToBuffer(p->buf.Target, new ReadOnlySpan<WriteRange>(p->writes, (int)p->count), p->index);
            }
        }

        private unsafe readonly struct InlineAdvance(BackendBufferAllocationReference buf, uint index) : IDeferredCommand
        {
            private readonly GCHandle<BackendBufferAllocationReference> buf = buf.GetGenericGCHandle();
            private readonly uint index = index;

            public static void Execute(void* self)
            {
                var p = (InlineAdvance*)self;
                AdvanceActiveBufferWrite(p->buf.Target, p->index);
            }
        }


        protected override void OnFree()
        {
            PushRenderThreadAction(() => { Backend.DestroyBuffer(this); return null; });
        }

    }



    /// <summary>
    /// <inheritdoc cref="BackendBufferAllocationReference"/>
    /// </summary>
    /// <param name="size"></param>
    /// <param name="backendRef"></param>
    public unsafe class BackendVertexBufferAllocationReference(uint size, bool writeable, object backendRef) : BackendBufferAllocationReference(size, writeable, backendRef)
    {

        public static BackendVertexBufferAllocationReference Create<T>(ReadOnlySpan<T> initialcontent, bool writeable) where T : unmanaged
        {
            fixed (void* c = initialcontent)
            {
                var size = (uint)(initialcontent.Length * sizeof(T));
                var obj = Backend.CreateVertexBuffer(size, writeable, c);

                return new(size, writeable, obj);
            }
        }

        public static BackendVertexBufferAllocationReference Create(uint length, bool writeable)
        {
            var obj = Backend.CreateVertexBuffer(length, writeable, (void*)0);
            return new(length, writeable, obj);
        }

    }


    /// <summary>
    /// <inheritdoc cref="BackendBufferAllocationReference"/>
    /// </summary>
    /// <param name="size"></param>
    /// <param name="backendRef"></param>
    public unsafe class BackendIndexBufferAllocationReference(uint size, bool writeable, object backendRef) : BackendBufferAllocationReference(size, writeable, backendRef)
    {
        public static BackendIndexBufferAllocationReference Create(ReadOnlySpan<uint> initialcontent, bool writeable)
        {
            fixed (void* c = initialcontent)
            {
                var size = (uint)(initialcontent.Length * sizeof(uint));
                var obj = Backend.CreateIndexBuffer(size, writeable, c);

                return new(size, writeable, obj);
            }

        }
        public static BackendIndexBufferAllocationReference Create(uint length, bool writeable)
        {
            var obj = Backend.CreateIndexBuffer(length, writeable, (void*)0);
            return new(length, writeable, obj);
        }
    }






    public unsafe abstract class BackendDataBufferAllocationReference(uint size, bool writeable, ShaderMetadata.ShaderBufferMetadata metadata, object backendRef) : BackendBufferAllocationReference(size, writeable, backendRef), IResourceSetResource
    {
        public readonly ShaderMetadata.ShaderBufferMetadata Metadata = metadata;


        protected unsafe static object Create(uint bufferSize, bool writeable, void* initialContent, ReadOnlySpan<ContiguousRegion> contiguousRegions, delegate*<uint, bool, void*, object> constructor)
        {
            if (initialContent == null) return constructor(bufferSize, writeable, null);


            var temp = Marshal.AllocHGlobal((int)bufferSize);
            BufferToPaddedBufferCopy((byte*)initialContent, bufferSize, 0, (byte*)temp, contiguousRegions);
            var obj = constructor(bufferSize, writeable, (void*)temp);

            Marshal.FreeHGlobal(temp);

            return obj;
        }
    }




    /// <summary>
    /// <inheritdoc cref="BackendBufferAllocationReference"/>
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="size"></param>
    /// <param name="backendRef"></param>
    public unsafe class BackendUniformBufferAllocationReference(uint size, bool writeable, ShaderMetadata.ShaderBufferMetadata metadata, object backendRef) : BackendDataBufferAllocationReference(size, writeable, metadata, backendRef), IResourceSetResource
    {

        public static BackendUniformBufferAllocationReference Create(uint size, bool writeable) 
        {
            return new BackendUniformBufferAllocationReference(size, writeable, default, Create(size, writeable, null, null, &create));
        }


        public static BackendUniformBufferAllocationReference Create<T>(uint size, bool writeable, ReadOnlySpan<T> initialContent, ReadOnlySpan<ContiguousRegion> contiguousRegions) where T : unmanaged
        {
            fixed (T* p = initialContent)
                return new BackendUniformBufferAllocationReference(size, writeable, default, Create(size, writeable, p, contiguousRegions, &create));
        }

        public static BackendUniformBufferAllocationReference CreateFromMetadata(ShaderMetadata.ShaderBufferMetadata metadata, bool writeable)
        {
            var bufferSize = metadata.SizeRequirement;
            return new BackendUniformBufferAllocationReference(bufferSize, writeable, metadata, Create(bufferSize, writeable, null, null, &create));
        }



        public static BackendUniformBufferAllocationReference CreateFromMetadata<T>(ShaderMetadata.ShaderBufferMetadata metadata, bool writeable, ReadOnlySpan<T> initialContent) where T : unmanaged
        {
            fixed (T* p = initialContent)
            {
                var bufferSize = metadata.SizeRequirement;
                return new BackendUniformBufferAllocationReference(bufferSize, writeable, metadata, Create(bufferSize, writeable, p, metadata.ContiguousRegions.AsSpan(), &create));
            }
        }


        private static object create(uint length, bool writeable, void* initialcontent)
            => Backend.CreateUniformBuffer(length, writeable, initialcontent);

    }






    /// <summary>
    /// <inheritdoc cref="BackendBufferAllocationReference"/>
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="size"></param>
    /// <param name="backendRef"></param>
    public unsafe class BackendStorageBufferAllocationReference(uint size, bool writeable, ShaderMetadata.ShaderBufferMetadata metadata, object backendRef) : BackendDataBufferAllocationReference(size, writeable, metadata, backendRef), IResourceSetResource
    {

        public static BackendStorageBufferAllocationReference Create(uint size, bool writeable)
        {
            return new BackendStorageBufferAllocationReference(size, writeable, default, Create(size, writeable, null, null, &create));
        }


        public static BackendStorageBufferAllocationReference Create<T>(uint size, bool writeable, ReadOnlySpan<T> initialContent) where T : unmanaged
        {
            fixed (T* p = initialContent)
                return new BackendStorageBufferAllocationReference(size, writeable, default, Create(size, writeable, p, null, &create));
        }


        public static BackendStorageBufferAllocationReference CreateFromMetadata(ShaderMetadata.ShaderBufferMetadata metadata, bool writeable)
        {
            var bufferSize = metadata.SizeRequirement;
            return new BackendStorageBufferAllocationReference(bufferSize, writeable, metadata, Create(bufferSize, writeable, null, null, &create));
        }

        public static BackendStorageBufferAllocationReference CreateFromMetadata<T>(ShaderMetadata.ShaderBufferMetadata metadata, bool writeable, ReadOnlySpan<T> initialContent = default) where T : unmanaged
        {
            fixed (T* p = initialContent)
            {
                var bufferSize = metadata.SizeRequirement;
                return new BackendStorageBufferAllocationReference(bufferSize, writeable, metadata, Create(bufferSize, writeable, p, metadata.ContiguousRegions.AsSpan(), &create));
            }
        }

        private static object create(uint length, bool writeable, void* initialcontent)
            => Backend.CreateStorageBuffer(length, writeable, initialcontent);

    }








    /// <summary>
    /// Represents something that can be a resource in a resource set.
    /// <br/> This is limited to <see cref="BackendTextureAndSamplerReferencesPair"/> and <see cref="BackendDataBufferAllocationReference"/> at the time of writing.
    /// </summary>
    public interface IResourceSetResource;

    /// <summary>
    /// Represents a collection of <see cref="IResourceSetResource"/>s that shaders can access.
    /// </summary>
    public class BackendResourceSetReference : BackendReference
    {

        public readonly ShaderMetadata.ShaderResourceSetMetadata Metadata;


        private uint WritesThisFrame;


        private readonly IResourceSetResource[] Contents;

        public ReadOnlySpan<IResourceSetResource> GetContents() => Contents.AsSpan();

        public uint ResourceCount => (uint)Contents.Length;




        private BackendResourceSetReference(ShaderMetadata.ShaderResourceSetMetadata metadata, object backendResource) : base(backendResource)
        {
            Metadata = metadata;
            Contents = new IResourceSetResource[metadata.UniformBuffers.Count + metadata.StorageBuffers.Count + metadata.Textures.Count];


            Logic.AppendPermanentEndOfFrameAction(() => WritesThisFrame = 0);
        }




        private static void WriteToResourceSet(BackendResourceSetReference set, ReadOnlySpan<ResourceSetResourceBind> contents, uint idx)
        {
            CheckOutsideOfRendering();
            Backend.WriteToResourceSet(set, contents, idx);
        }


        private static void AdvanceActiveResourceSetWrite(BackendResourceSetReference set, uint idx)
        {
            if (idx != 0) CheckDuringRendering();
            Backend.AdvanceActiveResourceSetWrite(set, idx);
        }







        public unsafe void Write(ResourceSetResourceBind* updates, uint count, bool nessecary)
        {
            lock (this)
            {
                for (int i = 0; i < count; i++)
                {
                    ref var g = ref updates[i];

#if DEBUG
                    if (g.Binding >= ResourceCount) throw new Exception();
#endif
                    Contents[g.Binding] = (IResourceSetResource)g.Resource.Target;

                }
            }

            if (nessecary)
            {
                PushRenderThreadAction(() =>
                {
                    WriteToResourceSet(this, new ReadOnlySpan<ResourceSetResourceBind>(updates, (int)count), 0);
                    AdvanceActiveResourceSetWrite(this, 0);
                    return null;
                }).Wait();

            }
            else
            {
                ResourceSetResourceBind* alloc = (ResourceSetResourceBind*)AllocateRenderTemporaryUnmanaged((int)(sizeof(ResourceSetResourceBind) * count));
                Unsafe.CopyBlockUnaligned(alloc, updates, (uint)(count*sizeof(ResourceSetResourceBind)));

                PushDeferredPreRenderThreadCommand(new WriteStruct(this, alloc, count, WritesThisFrame));
                PushDeferredRenderThreadCommand(new InlineAdvance(this, WritesThisFrame)); 
                
                WritesThisFrame++;
            }

            
        }



        private unsafe readonly struct WriteImmediateStruct(BackendResourceSetReference set, ResourceSetResourceBind[] writes) : IDeferredCommand
        {
            private readonly GCHandle<BackendResourceSetReference> set = set.GetGenericGCHandle();
            private readonly GCHandle<ResourceSetResourceBind[]> writes = GCHandle<ResourceSetResourceBind[]>.Alloc(writes, GCHandleType.Normal);

            public static void Execute(void* self)
            {
                var p = (WriteImmediateStruct*)self;
                WriteToResourceSet(p->set.Target, p->writes.Target, 0);
                AdvanceActiveResourceSetWrite(p->set.Target, 0);

                p->writes.Free();
            }
        }


        private unsafe readonly struct WriteStruct(BackendResourceSetReference set, ResourceSetResourceBind* writes, uint count, uint index) : IDeferredCommand
        {
            private readonly GCHandle<BackendResourceSetReference> set = set.GetGenericGCHandle();
            private readonly ResourceSetResourceBind* writes = writes;
            private readonly uint count = count;
            private readonly uint index = index;

            public static void Execute(void* self)
            {
                var p = (WriteStruct*)self;
                WriteToResourceSet(p->set.Target, new ReadOnlySpan<ResourceSetResourceBind>(p->writes, (int)p->count), p->index);
            }
        }


        private unsafe readonly struct InlineAdvance(BackendResourceSetReference set, uint index) : IDeferredCommand
        {
            private readonly GCHandle<BackendResourceSetReference> set = set.GetGenericGCHandle();
            private readonly uint index = index;

            public static void Execute(void* self)
            {
                var p = (InlineAdvance*)self;
                AdvanceActiveResourceSetWrite(p->set.Target, p->index);
            }
        }







        /// <summary>
        /// <inheritdoc cref="CreateFromMetadata(ShaderMetadata.ShaderResourceSetMetadata, string[], Dictionary{string, IResourceSetResource})"/>
        /// </summary>
        public static BackendResourceSetReference CreateFromMetadata(string SetName, string[] createInitialBuffers = null, Dictionary<string, IResourceSetResource> setInitial = null)
            => CreateFromMetadata(GlobalResourceSetMetadata[SetName], createInitialBuffers, setInitial);




        /// <summary>
        /// <inheritdoc cref="CreateFromMetadata(ShaderMetadata.ShaderResourceSetMetadata, string[], Dictionary{string, IResourceSetResource})"/>
        /// </summary>
        public static BackendResourceSetReference CreateFromMetadata(BackendShaderReference Shader, string SetName, string[] createInitialBuffers = null, Dictionary<string, IResourceSetResource> setInitial = null)
            => CreateFromMetadata(Shader.Metadata.ResourceSets[SetName].Metadata, createInitialBuffers, setInitial);



        /// <summary>
        /// Creates a <see cref="BackendResourceSetReference"/> according to <paramref name="Metadata"/>'s spec.
        /// <br/> Buffers found in <paramref name="createInitialBuffers"/> will be created and set. For example, if <paramref name="Metadata"/> defines a buffer "UBO", and <paramref name="createInitialBuffers"/> contains "UBO", that buffer will be created automatically. 
        /// <br/> After that, matching resources found in <paramref name="setInitial"/> will be set automatically.
        /// </summary>
        /// <param name="Metadata"></param>
        /// <returns></returns>
        /// <exception cref="Exception"></exception>
        public static BackendResourceSetReference CreateFromMetadata(ShaderMetadata.ShaderResourceSetMetadata Metadata, string[] createInitialBuffers = null, Dictionary<string, IResourceSetResource> setInitial = null)
        {
            var inst = new BackendResourceSetReference(Metadata, Backend.CreateResourceSet(Metadata.Declaration.AsSpan()));


            var setWriteHandle = inst.StartWrite(true);


            foreach (var v in Metadata.Textures)
            {
                switch (v.Value.Metadata.SamplerType)
                {
                    case TextureSamplerTypes.Sampler2D:
                        setWriteHandle.PushWrite(v.Value.Binding, Dummy2DTextureSamplerPair);
                        break;
                    case TextureSamplerTypes.Sampler2DShadow:
                        setWriteHandle.PushWrite(v.Value.Binding, Dummy2DShadowTextureSamplerPair);
                        break;
                    case TextureSamplerTypes.SamplerCubeMap:
                        setWriteHandle.PushWrite(v.Value.Binding, DummyCubeTextureSamplerPair);
                        break;
                    case TextureSamplerTypes.Sampler3D:
                        setWriteHandle.PushWrite(v.Value.Binding, Dummy3DTextureSamplerPair);
                        break;
                    default:
                        throw new Exception();
                }
            }



            foreach (var v in Metadata.UniformBuffers)
                setWriteHandle.PushWrite(v.Value.Binding, createInitialBuffers != null && createInitialBuffers.Contains(v.Key) ? BackendUniformBufferAllocationReference.CreateFromMetadata(v.Value.Metadata, true) : GetDummyUBO(v.Value.Metadata.SizeRequirement));


            foreach (var v in Metadata.StorageBuffers)
                setWriteHandle.PushWrite(v.Value.Binding, createInitialBuffers != null && createInitialBuffers.Contains(v.Key) ? BackendStorageBufferAllocationReference.CreateFromMetadata(v.Value.Metadata, true) : GetDummySSBO(v.Value.Metadata.SizeRequirement));


            if (setInitial != null)
                foreach (var kv in setInitial)
                    setWriteHandle.PushWrite(kv.Key, kv.Value);




            setWriteHandle.EndWrite();


            return inst;
        }







        protected override void OnFree()
        {
            PushRenderThreadAction(() => { Backend.DestroyResourceSet(this); return null; });
        }





        public BackendUniformBufferAllocationReference GetUniformBuffer(string name)
        {
            lock (this)
                return (BackendUniformBufferAllocationReference)Contents[Metadata.UniformBuffers[name].Binding];
        }

        public BackendStorageBufferAllocationReference GetStorageBuffer(string name)
        {
            lock (this)
                return (BackendStorageBufferAllocationReference)Contents[Metadata.StorageBuffers[name].Binding];
        }

        public BackendTextureAndSamplerReferencesPair GetTexture(string name)
        {
            lock (this)
                return (BackendTextureAndSamplerReferencesPair)Contents[Metadata.Textures[name].Binding];
        }


        public BackendUniformBufferAllocationReference GetUniformBuffer(uint binding)
        {
            lock (this)
                return (BackendUniformBufferAllocationReference)Contents[binding];
        }

        public BackendStorageBufferAllocationReference GetStorageBuffer(uint binding)
        {
            lock (this)
                return (BackendStorageBufferAllocationReference)Contents[binding];
        }

        public BackendTextureAndSamplerReferencesPair GetTexture(uint binding)
        {
            lock (this)
                return (BackendTextureAndSamplerReferencesPair)Contents[binding];
        }




        /// <summary>
        /// Gets a resource from the internal array. Does not interact with the backend.
        /// </summary>
        /// <param name="idx"></param>
        /// <returns></returns>
        public IResourceSetResource GetResource(uint idx)
        {
            lock (this)
                return Contents[idx];
        }


        /// <summary>
        /// <inheritdoc cref="GetResource(uint)"/>
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="name"></param>
        /// <returns></returns>
        /// <exception cref="Exception"></exception>
        public T GetResource<T>(string name) where T : IResourceSetResource
        {
            if (typeof(T) == typeof(BackendTextureAndSamplerReferencesPair)) return (T)GetResource(Metadata.Textures[name].Binding);
            else if (typeof(T) == typeof(BackendUniformBufferAllocationReference)) return (T)GetResource(Metadata.UniformBuffers[name].Binding);
            else if (typeof(T) == typeof(BackendStorageBufferAllocationReference)) return (T)GetResource(Metadata.StorageBuffers[name].Binding);
            else throw new Exception();
        }



    }


















    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public unsafe record struct SamplerDetails(TextureWrapModes WrapMode, TextureFilters MinFilter, TextureFilters MagFilter, TextureFilters MipmapFilter, bool EnableDepthComparison = false);



    public class BackendTextureReference : BackendReference
    {
        public readonly Vector3<uint> Dimensions;
        public readonly TextureTypes TextureType;
        public readonly TextureFormats TextureFormat;
        public readonly uint MipCount;

        private BackendTextureReference(object backendRef, Vector3<uint> dimensions, TextureTypes type, TextureFormats textureFormat, uint mipCount) : base(backendRef)
        {
            Dimensions = dimensions;
            TextureType = type;
            TextureFormat = textureFormat;
            MipCount = mipCount;
        }





        public static BackendTextureReference Create(Vector3<uint> dimensions, TextureTypes type, TextureFormats format, bool FramebufferAttachmentCompatible, byte[][] Mips = null)
        {

#if DEBUG
            if (type == TextureTypes.TextureCubeMap && dimensions.X != dimensions.Y) throw new Exception();
#endif


            if (Mips != default)
            {
                ulong totalSize = 0;
                byte[][] convertedMips = new byte[Mips.Length][];
                ulong[] mipOffsets = new ulong[Mips.Length];

                for (byte mip = 0; mip < Mips.Length; mip++)
                {
                    byte[] mipData = Mips[mip];

                    if (format == TextureFormats.RGB8_UNORM)
                        mipData = ConvertRGB8ToRGBA8(mipData, uint.Max(1, dimensions.X >> mip), uint.Max(1, dimensions.Y >> mip));
                    else if (format == TextureFormats.RGB16_SFLOAT)
                        mipData = ConvertRGB16ToRGBA16(mipData, uint.Max(1, dimensions.X >> mip), uint.Max(1, dimensions.Y >> mip));

                    convertedMips[mip] = mipData;

                    mipOffsets[mip] = totalSize;
                    totalSize += (ulong)mipData.Length;
                }

                Mips = convertedMips;
            }


            format = format switch
            {
                TextureFormats.RGB8_UNORM => TextureFormats.RGBA8_UNORM,
                TextureFormats.RGB16_SFLOAT => TextureFormats.RGBA16_SFLOAT,
                _ => format
            };



            return new BackendTextureReference(Backend.CreateTexture(dimensions, type, format, FramebufferAttachmentCompatible, Mips), dimensions, type, format, Mips == null ? 1 : (uint)Mips.Length);




            static byte[] ConvertRGB8ToRGBA8(byte[] rgbData, uint width, uint height, byte alpha = 255)
            {
                uint pixelCount = width * height;
                byte[] rgbaData = new byte[pixelCount * 4];
                int rgbIndex = 0;
                int rgbaIndex = 0;
                for (int i = 0; i < pixelCount; i++)
                {
                    rgbaData[rgbaIndex++] = rgbData[rgbIndex++];
                    rgbaData[rgbaIndex++] = rgbData[rgbIndex++];
                    rgbaData[rgbaIndex++] = rgbData[rgbIndex++];
                    rgbaData[rgbaIndex++] = alpha;
                }
                return rgbaData;
            }

            static byte[] ConvertRGB16ToRGBA16(byte[] rgbData, uint width, uint height, ushort alpha = 65535)
            {
                uint pixelCount = width * height;
                byte[] rgbaData = new byte[pixelCount * 8];
                int rgbIndex = 0;
                int rgbaIndex = 0;
                byte alphaLow = (byte)(alpha & 0xFF);
                byte alphaHigh = (byte)(alpha >> 8);
                for (int i = 0; i < pixelCount; i++)
                {
                    rgbaData[rgbaIndex++] = rgbData[rgbIndex++];
                    rgbaData[rgbaIndex++] = rgbData[rgbIndex++];
                    rgbaData[rgbaIndex++] = rgbData[rgbIndex++];
                    rgbaData[rgbaIndex++] = rgbData[rgbIndex++];
                    rgbaData[rgbaIndex++] = rgbData[rgbIndex++];
                    rgbaData[rgbaIndex++] = rgbData[rgbIndex++];
                    rgbaData[rgbaIndex++] = alphaLow;
                    rgbaData[rgbaIndex++] = alphaHigh;
                }
                return rgbaData;
            }
        }




        public void GenerateMipmaps() 
            => Backend.GenerateMipmaps(this);



        protected override void OnFree()
        {
            PushRenderThreadAction(() => { Backend.DestroyTexture(this); return null; });
        }

    }





    public class BackendSamplerReference : BackendReference
    {
        public readonly SamplerDetails Details;

        private BackendSamplerReference(SamplerDetails details, object backendRef) : base(backendRef)
        {
            Details = details;
        }

        protected override void OnFree()
        {
            PushRenderThreadAction(() => { Backend.DestroyTextureSampler(this); return null; });
        }




        private static Dictionary<SamplerDetails, BackendSamplerReference> SamplerCache = CreateUnsafeStructKeyComparisonDictionary<SamplerDetails, BackendSamplerReference>();

        /// <summary>
        /// Fetches or creates a <see cref="BackendSamplerReference"/> from the cache, according to a given specification.
        /// </summary>
        /// <param name="spec"></param>
        /// <returns></returns>
        public static BackendSamplerReference Get(SamplerDetails spec)
        {
            lock (SamplerCache)
            {
                //find or create a pipeline according to that spec
                if (!SamplerCache.TryGetValue(spec, out var get))
                {
                    //creation
                    get = new BackendSamplerReference(spec, Backend.CreateTextureSampler(spec));
                    SamplerCache.Add(spec, get);


                    get.OnFreeEvent.Add(() =>
                    {
                        lock (SamplerCache)
                            SamplerCache.Remove(spec);
                    });

                }

                return get;
            }
        }
    }




    public class BackendTextureAndSamplerReferencesPair : RefCounted, IResourceSetResource
    {
        public readonly BackendTextureReference Texture;
        public readonly BackendSamplerReference Sampler;

        public BackendTextureAndSamplerReferencesPair(BackendTextureReference texture, BackendSamplerReference sampler)
        {
            Texture = texture;
            Sampler = sampler;

            texture.AddUser();
            sampler.AddUser();
        }

        protected override void OnFree()
        {
            Texture.RemoveUser();
            Sampler.RemoveUser();
        }
    }



    public class BackendTextureAndSamplerReferencesPairsArray(BackendTextureAndSamplerReferencesPair[] array) : RefCounted, IResourceSetResource
    {
        public readonly ImmutableArray<BackendTextureAndSamplerReferencesPair> Array = array.ToImmutableArray();

        protected override void OnFree()
        {
            for (int i = 0; i < Array.Length; i++) 
                Array[i].RemoveUser();
        }
    }







    private static RefCountCollections.RefCountedDictionary<string, BackendShaderReference> Shaders = new();
    private static RefCountCollections.RefCountedDictionary<string, BackendComputeShaderReference> ComputeShaders = new();



    public class BackendShaderReference : BackendReference
    {
        public readonly ShaderMetadata Metadata;

        /// <summary>
        /// Contains dummy resource sets with dummy resource assignments which this shader can use.
        /// </summary>
        public readonly ImmutableArray<BackendResourceSetReference> DefaultResourceSets;

        private BackendShaderReference(ShaderMetadata metadata, object backendRef) : base(backendRef)
        {
            Metadata = metadata;
            DefaultResourceSets = CreateDefaultResourceSets(metadata.ResourceSets);
        }

        protected override void OnFree()
        {
            PushRenderThreadAction(() => { Backend.DestroyShader(this); return null; });
        }



#if DEBUG
        /// <summary>
        /// Debug only development-time method which compiles and uploads a shader under a given name, such that it can be fetched in future via <see cref="Get"/>.
        /// </summary>
        /// <param name="name"></param>
        /// <param name="src"></param>
        public static void Create(string name, ShaderSource src)
        {
            var shader = new BackendShaderReference(src.Metadata, Backend.CreateShader(src));

            lock (Shaders)
                Shaders[name] = shader;
        }
#endif


        /// <summary>
        /// Fetches a preexisting precompiled <see cref="BackendShaderReference"/>. 
        /// </summary>
        /// <param name="name"></param>
        /// <returns></returns>
        /// <exception cref="Exception"></exception>
        public static BackendShaderReference Get(string name)
        {
#if DEBUG
            lock (Shaders)
#endif
                return Shaders[name];

        }

    }






    public class BackendComputeShaderReference : BackendReference
    {
        public readonly ComputeShaderMetadata Metadata;

        public readonly ImmutableArray<BackendResourceSetReference> DefaultResourceSets;

        private BackendComputeShaderReference(ComputeShaderMetadata metadata, object backendRef) : base(backendRef)
        {
            Metadata= metadata;
            DefaultResourceSets= CreateDefaultResourceSets(metadata.ResourceSets);
        }

        protected override void OnFree()
        {
            PushRenderThreadAction(() => { Backend.DestroyComputeShader(this); return null; });
        }





#if DEBUG
        /// <summary>
        /// Debug only development-time method which compiles and uploads a shader under a given name, such that it can be fetched in future via <see cref="Get"/>.
        /// </summary>
        /// <param name="name"></param>
        /// <param name="src"></param>
        public static void Create(string name, ComputeShaderSource src)
        {
            var shader = new BackendComputeShaderReference(src.Metadata, Backend.CreateComputeShader(src));

            lock (ComputeShaders)
                ComputeShaders[name] = shader;
        }
#endif


        /// <summary>
        /// Fetches a preexisting precompiled <see cref="BackendComputeShaderReference"/>. 
        /// </summary>
        /// <param name="name"></param>
        /// <returns></returns>
        /// <exception cref="Exception"></exception>
        public static BackendComputeShaderReference Get(string name)
        {
#if DEBUG
            lock (ComputeShaders)
#endif
                return ComputeShaders[name];

        }

    }






    public static unsafe ImmutableArray<BackendResourceSetReference> CreateDefaultResourceSets(FrozenDictionary<string, (byte Binding, ShaderMetadata.ShaderResourceSetMetadata Metadata)> ResourceSets)
    {

        var ResourceSetBinds = new BackendResourceSetReference[ResourceSets.Count];

        foreach (var resKV in ResourceSets)
        {
            if (!DummyResourceSets.TryGetValue(resKV.Value.Metadata, out BackendResourceSetReference set))
                DummyResourceSets[resKV.Value.Metadata] = set = BackendResourceSetReference.CreateFromMetadata(resKV.Value.Metadata);

            ResourceSetBinds[resKV.Value.Binding] = set;
        }


        return ImmutableArray.ToImmutableArray(ResourceSetBinds);
    }












    public record class ShaderMetadata(
        FrozenDictionary<string, (byte Location, ShaderMetadata.ShaderInOutAttributeMetadata Metadata)> VertexInputAttributes,
        FrozenDictionary<string, (byte Location, ShaderMetadata.ShaderInOutAttributeMetadata Metadata)> FragmentOutputAttributes,
        FrozenDictionary<string, (byte Binding, ShaderMetadata.ShaderResourceSetMetadata Metadata)> ResourceSets
        )
    {

        public record class ShaderInOutAttributeMetadata(
            ShaderAttributeBufferFinalFormat Format,
            uint ArrayLength);

        public record class ShaderTextureMetadata(
            TextureSamplerTypes SamplerType,
            uint ArrayLength);

        public record class ShaderBufferMetadata(
           FrozenDictionary<string, uint> FieldOffsets,
           ImmutableArray<ContiguousRegion> ContiguousRegions,
           uint SizeRequirement);

        public record class ShaderResourceSetMetadata(

            ImmutableArray<ResourceSetResourceDeclaration> Declaration,
            FrozenDictionary<string, (byte Binding, ShaderTextureMetadata Metadata)> Textures,
            FrozenDictionary<string, (byte Binding, ShaderBufferMetadata Metadata)> UniformBuffers,
            FrozenDictionary<string, (byte Binding, ShaderBufferMetadata Metadata)> StorageBuffers

            );



#if DEBUG

        public string VertexSourceCodeForDebugging, FragmentSourceCodeForDebugging;

#endif

    }



    public record class ComputeShaderMetadata(
        FrozenDictionary<string, (byte Binding, ShaderMetadata.ShaderResourceSetMetadata Metadata)> ResourceSets
        )
    {
#if DEBUG

        public string GeneratedSourceForDebugging;

#endif

    }



    /// <summary>
    /// Contains compiled shader code + metadata <b>(valid only for one specific backend)</b>
    /// </summary>
    /// <param name="VertexSource"></param>
    /// <param name="FragmentSource"></param>
    public record class ShaderSource(ShaderMetadata Metadata, ImmutableArray<byte> VertexSource, ImmutableArray<byte> FragmentSource);

    /// <summary>
    /// <inheritdoc cref="ShaderSource"/>
    /// </summary>
    /// <param name="metadata"></param>
    /// <param name="Source"></param>
    public record class ComputeShaderSource(ComputeShaderMetadata Metadata, ImmutableArray<byte> Source);









    /// <summary>
    /// Represents a contiguous region of data within a buffer, used by <see cref="BufferToPaddedBufferCopy(byte*, uint, uint, byte*, ContiguousRegion[])"/> etc
    /// </summary>
    /// <param name="start"></param>
    /// <param name="end"></param>
    public readonly record struct ContiguousRegion(uint start, uint end);


    /// <summary>
    /// Copies data from src to dst while skipping over padding (gaps in <paramref name="regions"/>).
    /// </summary>
    /// <param name="srcPtr"></param>
    /// <param name="size"></param>
    /// <param name="offset"></param>
    /// <param name="dstPtr"></param>
    /// <param name="regions"></param>
    /// <exception cref="OverflowException"></exception>
    public static unsafe void BufferToPaddedBufferCopy(
        byte* srcPtr,
        uint size,
        uint offset,
        byte* dstPtr,
        ReadOnlySpan<ContiguousRegion> regions)
    {
        uint srcIndex = 0; // index into the source buffer

        for (int i = 0; i < regions.Length && srcIndex < size; i++)
        {
            var region = regions[i];

            // region.start and region.end are absolute offsets in the destination buffer
            // we want to start at "offset + region.start" in the dstPtr
            ulong dstStart = region.start + offset;
            ulong regionLength = region.end - region.start;

            // number of bytes we can copy into this region
            uint toCopy = (uint)Math.Min(regionLength, size - srcIndex);

            Unsafe.CopyBlockUnaligned(dstPtr + dstStart, srcPtr + srcIndex, toCopy);

            srcIndex += toCopy;
        }

#if DEBUG
        if (srcIndex < size)
            throw new OverflowException("Not all source bytes were copied: ran out of regions.");
#endif
    }










    public class BackendDrawPipelineReference : BackendReference
    {
        public readonly DrawPipelineDetails Details;

        private BackendDrawPipelineReference(object backendRef, DrawPipelineDetails details) : base(backendRef)
        {
            Details = details;
        }

        protected override void OnFree()
        {
            PushRenderThreadAction(() => { Backend.DestroyDrawPipeline(this); return null; });
        }





        private static Dictionary<DrawPipelineDetails, BackendDrawPipelineReference> DrawPipelineCache = CreateUnsafeStructKeyComparisonDictionary<DrawPipelineDetails, BackendDrawPipelineReference>();

        /// <summary>
        /// Fetches or creates a <see cref="BackendDrawPipelineReference"/> from the cache, according to a given specification.
        /// </summary>
        /// <param name="spec"></param>
        /// <returns></returns>
        public static BackendDrawPipelineReference Get(DrawPipelineDetails spec)
        {
            lock (DrawPipelineCache)
            {
                if (!DrawPipelineCache.TryGetValue(spec, out var get))
                {
                    get = new BackendDrawPipelineReference(Backend.CreateDrawPipeline(spec), spec);

                    GCHandle<BackendShaderReference>.FromIntPtr(spec.ShaderHandle).Target.OnFreeEvent.Add(get.Free);

                    get.OnFreeEvent.Add(() =>
                    {
                        lock (DrawPipelineCache)
                            DrawPipelineCache.Remove(spec);
                    });


                    if (CurrentBackendRenderProgress != BackendRenderProgress.DrawingToScreen)
                        ActiveFramebufferPipeline.OnFreeEvent.Add(get.Free);


                    DrawPipelineCache.Add(spec, get);
                }

                return get;
            }
        }
    }




    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public unsafe struct DrawPipelineDetails
    {
        public nint ShaderHandle;
        public nint FrameBufferPipelineHandle;

        public PipelineAttributeDetails Attributes;

        public RasterizationDetails Rasterization;
        public BlendState Blending;
        public DepthStencilState DepthStencil;




        public const byte MaxResourceSetResources = 32;

        public const byte MaxVertAttributes = 16;


        public unsafe struct PipelineAttributeDetails
        {
            public byte AttributeCount;
            public PipelineAttributeSpecs Attributes;


            [InlineArray(MaxVertAttributes)]
            public unsafe struct PipelineAttributeSpecs { public PipelineAttributeSpec value; }


            public unsafe readonly struct PipelineAttributeSpec
            {
                public readonly byte Location;
                public readonly VertexAttributeBufferComponentFormat SourceFormat;
                public readonly ShaderAttributeBufferFinalFormat FinalFormat;
                public readonly ushort Stride;
                public readonly ushort Offset;

                public readonly VertexAttributeScope Scope;

                public PipelineAttributeSpec(byte location, VertexAttributeBufferComponentFormat sourceFormat, ShaderAttributeBufferFinalFormat finalFormat, ushort stride, ushort offset, VertexAttributeScope scope)
                {
                    Location=location;
                    SourceFormat=sourceFormat;
                    FinalFormat=finalFormat;
                    Stride=stride;
                    Offset=offset;
                    Scope=scope;
                }
            }
        }





        public unsafe struct RasterizationDetails()
        {
            public CullMode CullMode = CullMode.Back;
            public PolygonMode PolygonMode = PolygonMode.Fill;
            public PrimitiveType Primitive = PrimitiveType.Triangles;
        }

        public unsafe struct DepthStencilState()
        {
            public bool DepthWrite = true;
            public DepthOrStencilFunction DepthFunction = DepthOrStencilFunction.LessOrEqual;

            public bool StencilTestEnable = false;
            public StencilOp FrontStencil = default;
            public StencilOp BackStencil = default;

            public unsafe struct StencilOp()
            {
                public StencilOperation FailOp = StencilOperation.Keep;
                public StencilOperation PassOp = StencilOperation.Keep;
                public StencilOperation DepthFailOp = StencilOperation.Keep;
                public DepthOrStencilFunction CompareOp = DepthOrStencilFunction.Always;
            }
        }

        public unsafe struct BlendState()
        {
            public bool Enable = false;
            public BlendingFactor SrcColor = BlendingFactor.One;
            public BlendingFactor DstColor = BlendingFactor.Zero;
            public BlendOperation ColorOp = BlendOperation.Add;
            public BlendingFactor SrcAlpha = BlendingFactor.One;
            public BlendingFactor DstAlpha = BlendingFactor.Zero;
            public BlendOperation AlphaOp = BlendOperation.Add;
            public ColorWriteMask WriteMask = ColorWriteMask.All;
        }

    }






    [Flags]
    public enum FrameBufferPipelineAttachmentAccessFlags : byte
    {
        None = 1 << 0,
        Read = 1 << 1,
        Write = 1 << 2,
    }






    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public unsafe struct FrameBufferPipelineDetails
    {
        //color attachment count on the fbo
        public byte ColorAttachmentCount;

        //depth+stencil attachment present on fbo
        public bool HasDepthStencil;


        /// <summary>
        /// A <see cref="TextureFormats"/> for each of the 8 possible color attachments (all 1 byte)
        /// </summary>
        public fixed byte ColorFormats[8];

        // ( depth format is omitted for now because its always just gonna be depthstencil 24 8 )



        public FramebufferSampleCount SampleCount;

        public byte StageCount;
        public InlineStageArray Stages;


        [InlineArray(8)]  //<-- stage limit per pipeline
        public struct InlineStageArray { public FrameBufferPipelineStage _value; }
    }





    public unsafe struct FrameBufferPipelineStage
    {
        public byte SpecifiedColorAttachments;  //bitmask
        public byte SpecifiedColorClears;  //bitmask

        /// <summary>
        /// A <see cref="FrameBufferPipelineAttachmentAccessFlags"/> for each of the 8 possible color attachments (all 1 byte)
        /// </summary>
        public fixed byte SpecifiedColorAccesses[8];
        

        public bool SpecifiedDepth;
        public byte SpecifiedDepthAccess;
        public bool SpecifiedClearDepth;


        public bool SpecifiedStencil;
        public byte SpecifiedStencilAccess;
        public bool SpecifiedClearStencil;


        public unsafe FrameBufferPipelineStage SpecifyColorAttachments(byte rangeStart, byte rangeEnd, FrameBufferPipelineAttachmentAccessFlags access, bool clear)
        {
            for (byte i = rangeStart; i < rangeEnd+1; i++) 
                SpecifyColorAttachment(i, access, clear);

            return this;
        }


        public unsafe FrameBufferPipelineStage SpecifyColorAttachment(byte index, FrameBufferPipelineAttachmentAccessFlags access, bool clear)
        {
#if DEBUG
            if (index < 0 || index > 7) throw new Exception();
#endif

            SpecifiedColorAttachments |= (byte)(1 << index);

            SpecifiedColorAccesses[index] = (byte)access;


            if (clear) SpecifiedColorClears |= (byte)(1 << index);
            else SpecifiedColorClears &= (byte)~(1 << index);


            return this;
        }



        public unsafe FrameBufferPipelineStage SpecifyDepth(FrameBufferPipelineAttachmentAccessFlags access, bool clear)
        {
            SpecifiedDepth = true;

            SpecifiedDepthAccess = (byte)access;
            SpecifiedClearDepth = clear;

            return this;
        }

        public unsafe FrameBufferPipelineStage SpecifyStencil(FrameBufferPipelineAttachmentAccessFlags access, bool clear)
        {
            SpecifiedStencil = true;

            SpecifiedStencilAccess = (byte)access;
            SpecifiedClearStencil = clear;

            return this;
        }
    }







    public class BackendFrameBufferPipelineReference : BackendReference
    {
        public readonly FrameBufferPipelineDetails Details;

        private BackendFrameBufferPipelineReference(object backendRef, FrameBufferPipelineDetails details) : base(backendRef)
        {
            Details = details;
        }

        protected override void OnFree()
        {   
            PushRenderThreadAction(() => { Backend.DestroyFrameBufferPipeline(this); return null; });
        }





        private static Dictionary<FrameBufferPipelineDetails, BackendFrameBufferPipelineReference> FrameBufferPipelineCache = CreateUnsafeStructKeyComparisonDictionary<FrameBufferPipelineDetails, BackendFrameBufferPipelineReference>();

        /// <summary>
        /// Fetches or creates a <see cref="BackendFrameBufferPipelineReference"/> from the cache, according to a given specification.
        /// </summary>
        /// <param name="spec"></param>
        /// <returns></returns>
        public static BackendFrameBufferPipelineReference Get(FrameBufferPipelineDetails spec)
        {
            lock (FrameBufferPipelineCache)
            {
                if (!FrameBufferPipelineCache.TryGetValue(spec, out var get))
                {
                    get = new BackendFrameBufferPipelineReference(Backend.CreateFrameBufferPipeline(spec), spec);

                    var d = spec;
                    FrameBufferPipelineCache.Add(d, get);
                    get.OnFreeEvent.Add(() =>
                    {
                        lock (FrameBufferPipelineCache)
                            FrameBufferPipelineCache.Remove(d);
                    });
                }

                return get;
            }
        }
    }





    public class BackendFrameBufferObjectReference : BackendReference
    {

        public readonly ImmutableArray<BackendTextureReference> ColorAttachments;
        public readonly BackendTextureReference DepthStencilAttachment;
        public readonly BackendFrameBufferPipelineReference Pipeline;

        public readonly Vector2<uint> Dimensions;

        private BackendFrameBufferObjectReference(ImmutableArray<BackendTextureReference> colorAttachments,

                                                       BackendTextureReference depthstencil,

                                                       BackendFrameBufferPipelineReference pipeline,

                                                       Vector2<uint> dimensions,
                                                       
                                                       object backendRef) : base(backendRef)
        {
            ColorAttachments = colorAttachments;
            DepthStencilAttachment = depthstencil;
            Pipeline = pipeline;
            Dimensions = dimensions;
        }


        public static unsafe BackendFrameBufferObjectReference Create(ReadOnlySpan<BackendTextureReference> colorTargets, BackendTextureReference depthStencilTarget, BackendFrameBufferPipelineReference pipeline, Vector2<uint> dimensions)
        {
            return new BackendFrameBufferObjectReference(colorTargets.ToImmutableArray(), depthStencilTarget, pipeline, dimensions, Backend.CreateFrameBufferObject(colorTargets, depthStencilTarget, pipeline, dimensions));
        }



        protected override void OnFree()
        {
            PushRenderThreadAction(() => { Backend.DestroyFrameBufferObject(this); return null; });
        }
    }



    public readonly record struct ResourceSetResourceDeclaration(ResourceSetResourceType ResourceType, uint ArrayLength)
    {
        public enum ResourceSetResourceType
        {
            UniformBuffer,
            StorageBuffer,
            Texture
        }
    }


    public record struct ResourceSetResourceBind(uint Binding, GCHandle Resource);




    public static BackendFrameBufferObjectReference ActiveFrameBufferObject { get; private set; }
    public static BackendFrameBufferPipelineReference ActiveFramebufferPipeline { get; private set; }
    public static uint ActiveFrameBufferPipelineStage { get; private set; }





    public const ushort MaxResourceSetResources = 32;


    public static RenderingBackendEnum CurrentBackend { get; private set; }

    private static IRenderingBackend Backend;





    /// <summary>
    /// Describes the current state of the render thread's rendering execution.
    /// </summary>
    private enum BackendRenderProgress
    {
        /// <summary>
        /// No rendering is happening whatsoever. That isn't to say the render thread is guaranteed to be doing absolutely nothing, but it isn't actively rendering a frame.
        /// </summary>
        NotRendering,

        /// <summary>
        /// A frame is rendering but is midway through miscellaneous non-drawing commands.
        /// </summary>
        Downtime,

        /// <summary>
        /// A frame is rendering and is mid way through drawing using a framebuffer pipeline.
        /// </summary>
        DrawingViaFramebufferPipeline,

        /// <summary>
        /// A frame is rendering and is drawing directly to the window swapchain.
        /// </summary>
        DrawingToScreen
    }



    private static BackendRenderProgress CurrentBackendRenderProgress;











#if DEBUG

    /// <summary>
    /// <br/><b> WARNING: This method invokes one or more immediate rendering commands, and should only be called from the render thread during frame rendering. See <see cref="PushDeferredRenderThreadCommand{T}"/>. </b>
    /// </summary>
    private struct _callonrenderthread;


    /// <summary>
    /// <br/><b> WARNING: This method touches existing backend resources or state, and should be called outside of active frame rendering, from any thread. See <see cref="PushRenderThreadAction(Func{object})"/> or <see cref="PushDeferredPreRenderThreadCommand{T}"/>. </b>
    /// </summary>
    private struct _callonanythreadoutsideofrendering;


    /// <summary>
    /// <br/><b> WARNING: This method creates backend resources, and can be called from any thread regardless of engine state, though the backend implementation may decide to defer it.
    /// <br/>If you're looking to create a usable <see cref="BackendReference"/> instance, see static methods on that particular type, for example <see cref="BackendTextureReference.Create(Vector3{uint}, TextureTypes, TextureFormats, bool, byte[][])"/> </b>
    /// </summary>
    private struct _callonanythread;

#endif












    /// <summary>
    /// <inheritdoc cref="_callonanythreadoutsideofrendering"/>
    /// </summary>
    public static void WaitForAllComputeShaders()
    {
        CheckOutsideOfRendering();
        Backend.WaitForAllComputeShaders();
    }





    // ---------------- Backend Control ----------------

    /// <summary>
    /// <inheritdoc cref="_callonanythreadoutsideofrendering"/>
    /// </summary>
    public static void Destroy()
    {
        CheckOutsideOfRendering();
        Backend.Destroy();
    }


    /// <summary>
    /// <inheritdoc cref="_callonanythreadoutsideofrendering"/>
    /// </summary>
    /// <param name="UseHDR"></param>
    public static void ConfigureSwapchain(Vector2<uint> Size, bool UseHDR)
    {
        CheckOutsideOfRendering();

        var ret = Backend.ConfigureSwapchain(Size, UseHDR);

        CurrentSwapchainDetails = ret;
    }



    public static SwapchainDetails CurrentSwapchainDetails { get; private set; }
    public readonly record struct SwapchainDetails(Vector2<uint> Size, bool HDR);








    // ================================================================
    // ======================= IMMEDIATE DRAWING COMMANDS =============
    // ================================================================


    // ---------------- Frame Rendering ----------------

    /// <summary>
    /// <inheritdoc cref="_callonrenderthread"/>
    /// </summary>
    public static void StartFrameRendering()
    {
        CheckDuringRendering();
        Backend.StartFrameRendering();
        CurrentBackendRenderProgress = BackendRenderProgress.Downtime;
    }


    /// <summary>
    /// <inheritdoc cref="_callonrenderthread"/>
    /// </summary>
    public static void EndFrameRendering()
    {
        CheckDuringRendering();
        Backend.EndFrameRendering();
        CurrentBackendRenderProgress = BackendRenderProgress.NotRendering;
    }





    // ---------------- FrameBuffer / Pipeline Control ----------------

    /// <summary>
    /// <inheritdoc cref="_callonrenderthread"/>
    /// </summary>
    /// <param name="fbo"></param>
    /// <param name="pipeline"></param>
    public static void BeginFrameBufferPipeline(BackendFrameBufferObjectReference fbo, BackendFrameBufferPipelineReference pipeline)
    {
        CheckDuringRendering();

#if DEBUG
        if (CurrentBackendRenderProgress != BackendRenderProgress.Downtime)
            throw new Exception();
#endif

        ActiveFramebufferPipeline = pipeline;
        ActiveFrameBufferObject = fbo;
        ActiveFrameBufferPipelineStage = 0;
        CurrentBackendRenderProgress = BackendRenderProgress.DrawingViaFramebufferPipeline;
        Backend.BeginFrameBufferPipeline(fbo, pipeline);
    }



    /// <summary>
    /// <inheritdoc cref="_callonrenderthread"/>
    /// </summary>
    /// <param name="fbo"></param>
    /// <param name="pipeline"></param>
    /// <param name="stageIndex"></param>
    public static void AdvanceFrameBufferPipeline(BackendFrameBufferObjectReference fbo, BackendFrameBufferPipelineReference pipeline, byte stageIndex)
    {
        CheckDuringRendering();
        ActiveFrameBufferPipelineStage++;
        Backend.AdvanceFrameBufferPipeline(fbo, pipeline, stageIndex);
    }


    /// <summary>
    /// <inheritdoc cref="_callonrenderthread"/>
    /// </summary>
    /// <param name="fbo"></param>
    public static void EndFrameBufferPipeline(BackendFrameBufferObjectReference fbo)
    {
#if DEBUG
        if (CurrentBackendRenderProgress != BackendRenderProgress.DrawingViaFramebufferPipeline)
            throw new Exception();
#endif

        CheckDuringRendering();
        Backend.EndFrameBufferPipeline(fbo);
        ActiveFramebufferPipeline = null;
        ActiveFrameBufferObject = null;
        ActiveFrameBufferPipelineStage = 0;
        CurrentBackendRenderProgress = BackendRenderProgress.Downtime;
    }






    // ---------------- Drawing ----------------

    /// <summary>
    /// <inheritdoc cref="_callonrenderthread"/>
    /// </summary>
    /// <param name="buffers"></param>
    /// <param name="ResourceSets"></param>
    /// <param name="pipeline"></param>
    /// <param name="indexbuffer"></param>
    /// <param name="indexBufferOffset"></param>
    /// <param name="indexing"></param>
    public static void Draw(ReadOnlySpan<VertexAttributeDefinitionPlusBufferStruct> buffers, ReadOnlySpan<GCHandle<BackendResourceSetReference>> ResourceSets, BackendDrawPipelineReference pipeline, BackendIndexBufferAllocationReference indexbuffer, uint indexBufferOffset, IndexingDetails indexing)
    {
        CheckDuringRendering();
        Backend.Draw(buffers, ResourceSets, pipeline, indexbuffer, indexBufferOffset, indexing);
    }






    /// <summary>
    /// <inheritdoc cref="_callonrenderthread"/>
    /// </summary>
    /// <exception cref="Exception"></exception>
    public static void StartDrawToScreen()
    {
        CheckDuringRendering();
#if DEBUG
        if (CurrentBackendRenderProgress == BackendRenderProgress.DrawingViaFramebufferPipeline)
            throw new Exception("end framebuffer pipeline first");
#endif
        Backend.StartDrawToScreen();
        CurrentBackendRenderProgress = BackendRenderProgress.DrawingToScreen;
    }



    /// <summary>
    /// <inheritdoc cref="_callonrenderthread"/>
    /// </summary>
    public static void EndDrawToScreen()
    {
        CheckDuringRendering();
        Backend.EndDrawToScreen();
        CurrentBackendRenderProgress = BackendRenderProgress.Downtime;
    }


    /// <summary>
    /// <inheritdoc cref="_callonrenderthread"/>
    /// </summary>
    public static void SetScissor(Vector2<uint> offset, Vector2<uint> size)
    {
        CheckDuringRendering();
        Backend.SetScissor(offset, size);
    }







    /// <summary>
    /// The interface to implement a rendering backend.
    /// <br/> <see cref="BackendReference"/> creation methods should return the <see cref="object"/> that'll be used as <see cref="BackendReference.BackendRef"/> rather than an actual full <see cref="BackendReference"/> instance.
    /// </summary>
    private interface IRenderingBackend
    {





        // ================================================================
        // ===================== GENERAL BACKEND CONTROL =================
        // ================================================================


        public void Destroy();

        public SwapchainDetails ConfigureSwapchain(Vector2<uint> Size, bool UseHDR);





        // ================================================================
        // ======================= DATA / RESOURCE MANAGEMENT ===========
        // ================================================================



        // ---------------- Buffers ----------------
        public unsafe object CreateVertexBuffer(uint length, bool writeable, void* initialContent);
        public unsafe object CreateIndexBuffer(uint length, bool writeable, void* initialContent);
        public unsafe object CreateUniformBuffer(uint length, bool writeable, void* initialContent);
        public unsafe object CreateStorageBuffer(uint length, bool writeable, void* initialContent);

        public unsafe void WriteToBuffer(BackendBufferAllocationReference buffer, ReadOnlySpan<WriteRange> writes, uint idx);
        public unsafe void AdvanceActiveBufferWrite(BackendBufferAllocationReference buffer, uint idx);
        public void DestroyBuffer(BackendBufferAllocationReference buffer);





        // ---------------- Textures ----------------
        public object CreateTexture(Vector3<uint> Dimensions, TextureTypes type, TextureFormats format, bool FramebufferAttachmentCompatible, byte[][] texturemips = default);
        public void GenerateMipmaps(BackendTextureReference texture);
        public ReadOnlySpan<byte> ReadTexturePixels(BackendTextureReference tex, uint level, Vector3<uint> offset, Vector3<uint> size);
        public void WriteTexturePixels(BackendTextureReference tex, uint level, Vector3<uint> offset, Vector3<uint> size, ReadOnlySpan<byte> content);
        public void DestroyTexture(BackendTextureReference texture);
        public object CreateTextureSampler(SamplerDetails details);
        public void DestroyTextureSampler(BackendSamplerReference texture);



        // ---------------- Resource Sets ----------------
        public object CreateResourceSet(ReadOnlySpan<ResourceSetResourceDeclaration> definition);
        public void WriteToResourceSet(BackendResourceSetReference set, ReadOnlySpan<ResourceSetResourceBind> contents, uint idx);
        public unsafe void AdvanceActiveResourceSetWrite(BackendResourceSetReference set, uint idx);
        public void DestroyResourceSet(BackendResourceSetReference set);



        // ---------------- Shaders ----------------
        public object CreateShader(ShaderSource ShaderSource);
        public void DestroyShader(BackendShaderReference shader);
        public object CreateComputeShader(ComputeShaderSource ShaderSource);
        public void DispatchComputeShader(BackendComputeShaderReference shader, uint groupCountX, uint groupCountY, uint groupCountZ);
        public void WaitForAllComputeShaders();
        public void DestroyComputeShader(BackendComputeShaderReference shader);



        // ---------------- Pipelines ----------------
        public object CreateDrawPipeline(DrawPipelineDetails details);
        public void DestroyDrawPipeline(BackendDrawPipelineReference pipeline);
        public object CreateFrameBufferPipeline(FrameBufferPipelineDetails details);
        public void DestroyFrameBufferPipeline(BackendFrameBufferPipelineReference backendRenderPassReference);



        // ---------------- FrameBuffer Objects ----------------
        public unsafe object CreateFrameBufferObject(ReadOnlySpan<BackendTextureReference> colorTargets, BackendTextureReference depthStencilTarget, BackendFrameBufferPipelineReference pipeline, Vector2<uint> dimensions);
        public void DestroyFrameBufferObject(BackendFrameBufferObjectReference buffer);






        // ================================================================
        // ======================= IMMEDIATE DRAWING COMMANDS ============
        // ================================================================



        // ---------------- Frame Rendering ----------------
        public void StartFrameRendering();
        public void EndFrameRendering();



        // ---------------- FrameBuffer / Pipeline Control ----------------
        public void BeginFrameBufferPipeline(BackendFrameBufferObjectReference fbo, BackendFrameBufferPipelineReference pipeline);
        public void AdvanceFrameBufferPipeline(BackendFrameBufferObjectReference fbo, BackendFrameBufferPipelineReference pipeline, byte stageIndex);
        public void EndFrameBufferPipeline(BackendFrameBufferObjectReference fbo);
        public void StartDrawToScreen();
        public void EndDrawToScreen();



        // ---------------- Drawing ----------------
        public void Draw(ReadOnlySpan<VertexAttributeDefinitionPlusBufferStruct> buffers, ReadOnlySpan<GCHandle<BackendResourceSetReference>> ResourceSets, BackendDrawPipelineReference pipeline, BackendIndexBufferAllocationReference indexbuffer, uint indexBufferOffset, IndexingDetails indexing);



        // ---------------- FrameBuffer Manipulation ----------------
        public void ClearFramebufferDepthStencil(BackendFrameBufferObjectReference framebuffer, byte CubemapFaceIfCubemap = 0);
        public void ClearFramebufferColorAttachment(BackendFrameBufferObjectReference framebuffer, Vector4 color, byte idx = 0, byte CubemapFaceIfCubemap = 0);
        public void SetScissor(Vector2<uint> offset, Vector2<uint> size);
    }


}