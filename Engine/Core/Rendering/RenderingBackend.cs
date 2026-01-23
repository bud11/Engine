


namespace Engine.Core;

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using static Comparisons;
using static EngineMath;
using static Rendering;
using static RenderingBackend.DrawPipelineDetails;
using static RenderingBackend.ResourceSetResourceDeclaration;




/// <summary>
/// Provides direct or near direct access to the rendering backend. Also see <seealso cref="Rendering"/> and/or <seealso cref="IDeferredCommand"/>
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

        RenderingInit();
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








    private static Dictionary<ShaderMetadata.ShaderResourceSetMetadata, BackendResourceSetReference> DummyResourceSets = new();



    public static BackendTextureAndSamplerReferencesPair Dummy2DTextureSamplerPair { get; private set; }
    public static BackendTextureAndSamplerReferencesPair Dummy2DShadowTextureSamplerPair { get; private set; }
    public static BackendTextureAndSamplerReferencesPair DummyCubeTextureSamplerPair { get; private set; }
    public static BackendTextureAndSamplerReferencesPair Dummy3DTextureSamplerPair { get; private set; }

    public static BackendVertexBufferAllocationReference DummyVertex { get; private set; }
    public static BackendUniformBufferAllocationReference DummyUBO { get; private set; }
    public static BackendStorageBufferAllocationReference DummySSBO { get; private set; }




    /// <summary>
    /// Creates dummy resources (resource sets and pipelines need to be fully populated, so these are internally used in any empty slots found)
    /// </summary>
    public static unsafe void CreateDummyObjects()
    {

        DummyVertex = BackendVertexBufferAllocationReference.Create(1, false);

        DummyUBO = BackendUniformBufferAllocationReference.Create(1, false);
        DummySSBO = BackendStorageBufferAllocationReference.Create(1, false);



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


        public BackendBufferAllocationReference(uint size, bool writeable, object backendRef) : base(backendRef)
        {
            Size = size;
            Writeable = writeable;


            Logic.AppendPermanentEndOfFrameAction(() => WritesThisFrame = 0);
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
            PushRenderThreadAction(() => { DestroyBuffer(this); return null; });
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
                var obj = CreateVertexBuffer(size, writeable, c);

                return new(size, writeable, obj);
            }
        }

        public static BackendVertexBufferAllocationReference Create(uint length, bool writeable)
        {
            var obj = CreateVertexBuffer(length, writeable, (void*)0);
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
                var obj = CreateIndexBuffer(size, writeable, c);

                return new(size, writeable, obj);
            }

        }
        public static BackendIndexBufferAllocationReference Create(uint length, bool writeable)
        {
            var obj = CreateIndexBuffer(length, writeable, (void*)0);
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
            => CreateUniformBuffer(length, writeable, initialcontent);

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
            => CreateStorageBuffer(length, writeable, initialcontent);

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




        public BackendResourceSetReference(ShaderMetadata.ShaderResourceSetMetadata metadata, object backendResource) : base(backendResource)
        {
            Metadata = metadata;
            Contents = new IResourceSetResource[metadata.UniformBuffers.Count + metadata.StorageBuffers.Count + metadata.Textures.Count];


            Logic.AppendPermanentEndOfFrameAction(() => WritesThisFrame = 0);
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
        /// Creates a <see cref="BackendResourceSetReference"/> according to <paramref name="Metadata"/>'s spec.
        /// <br/> Buffers found in <paramref name="createInitialBuffers"/> will be created and set. For example, if <paramref name="Metadata"/> defines a buffer "UBO", and <paramref name="createInitialBuffers"/> contains "UBO", that buffer will be created automatically. 
        /// <br/> After that, matching resources found in <paramref name="setInitial"/> will be set automatically.
        /// </summary>
        /// <param name="Metadata"></param>
        /// <returns></returns>
        /// <exception cref="Exception"></exception>
        public static BackendResourceSetReference CreateFromMetadata(ShaderMetadata.ShaderResourceSetMetadata Metadata, string[] createInitialBuffers = null, Dictionary<string, IResourceSetResource> setInitial = null)
        {
            var inst = new BackendResourceSetReference(Metadata, CreateResourceSet(Metadata.Declaration.AsSpan()));


            var setWriteHandle = inst.StartWrite(true);


            foreach (var v in Metadata.Textures)
            {
                switch (v.Value.Metadata.SamplerType)
                {
                    case TextureSamplerTypes.Texture2D:
                        setWriteHandle.PushWrite(v.Value.Binding, Dummy2DTextureSamplerPair);
                        break;
                    case TextureSamplerTypes.Texture2DShadow:
                        setWriteHandle.PushWrite(v.Value.Binding, Dummy2DShadowTextureSamplerPair);
                        break;
                    case TextureSamplerTypes.TextureCubeMap:
                        setWriteHandle.PushWrite(v.Value.Binding, DummyCubeTextureSamplerPair);
                        break;
                    case TextureSamplerTypes.Texture3D:
                        setWriteHandle.PushWrite(v.Value.Binding, Dummy3DTextureSamplerPair);
                        break;
                    default:
                        throw new Exception();
                }
            }



            foreach (var v in Metadata.UniformBuffers)
                setWriteHandle.PushWrite(v.Value.Binding, createInitialBuffers != null && createInitialBuffers.Contains(v.Key) ? BackendUniformBufferAllocationReference.CreateFromMetadata(v.Value.Metadata, true) : DummyUBO);


            foreach (var v in Metadata.StorageBuffers)
                setWriteHandle.PushWrite(v.Value.Binding, createInitialBuffers != null && createInitialBuffers.Contains(v.Key) ? BackendStorageBufferAllocationReference.CreateFromMetadata(v.Value.Metadata, true) : DummySSBO);


            if (setInitial != null)
                foreach (var kv in setInitial)
                    setWriteHandle.PushWrite(kv.Key, kv.Value);




            setWriteHandle.EndWrite();


            return inst;
        }







        protected override void OnFree()
        {
            PushRenderThreadAction(() => { DestroyResourceSet(this); return null; });
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
            MipCount=mipCount;
        }


        public static BackendTextureReference Create(Vector3<uint> Dimensions, TextureTypes Type, TextureFormats Format, bool FramebufferAttachmentCompatible, byte[][] Mips = default)
        {
#if DEBUG
            if (Type == TextureTypes.TextureCubeMap && Dimensions.X != Dimensions.Y) throw new Exception();
#endif

            return new(CreateTexture(Dimensions, Type, Format, FramebufferAttachmentCompatible, Mips), Dimensions, Type, Format, Mips == null ? 1 : (uint)Mips.Length);
        }




        protected override void OnFree()
        {
            PushRenderThreadAction(() => { DestroyTexture(this); return null; });
        }

    }



    public class BackendSamplerReference(object backendRef) : BackendReference(backendRef)
    {
        protected override void OnFree()
        {
            PushRenderThreadAction(() => { DestroyTextureSampler(this); return null; });
        }


        /// <summary>
        /// Fetches and/or creates a sampler of the required spec from the global backend sampler cache.
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
                    get = new BackendSamplerReference(CreateTextureSampler(spec));
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




    public class BackendTextureAndSamplerReferencesPair(BackendTextureReference texture, BackendSamplerReference sampler) : RefCounted, IResourceSetResource
    {
        public readonly BackendTextureReference Texture = texture;
        public readonly BackendSamplerReference Sampler = sampler;

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









#if DEBUG



    /// <summary>
    /// Actually uploads a shader to the gpu under a given name in order to be later fetched via <see cref="GetShader(string)"/>. Will replace and free an existing shader of the same name if found.
    /// </summary>
    /// <param name="name"></param>
    /// <param name="src"></param>
    public static void CreateShader(string name, ShaderSource src)
    {
        var shader = new BackendShaderReference(src.Metadata, CreateShader(src));

        lock (Shaders)
            Shaders.AddRefCountedReference(name, shader);

    }

    /// <summary>
    /// Actually uploads a shader to the gpu under a given name in order to be later fetched via <see cref="GetShader(string)"/>. Will replace and free an existing shader of the same name if found.
    /// </summary>
    /// <param name="name"></param>
    /// <param name="src"></param>
    public static void CreateComputeShader(string name, ComputeShaderSource src)
    {
        var shader = new BackendComputeShaderReference(src.Metadata, CreateComputeShader(src));


        lock (ComputeShaders)
            ComputeShaders.AddRefCountedReference(name, shader);


    }

#endif




#if RELEASE

    public static void CreateShaders()
    {
        foreach (var kv in ShaderSources[RenderingBackendEnum.Vulkan])
            Shaders[kv.Key] = new BackendShaderReference(kv.Value.Metadata, CreateShader(kv.Value));

        foreach (var kv in ComputeShaderSources[RenderingBackendEnum.Vulkan])
            ComputeShaders[kv.Key] = new BackendComputeShaderReference(kv.Value.Metadata, CreateComputeShader(kv.Value));
    }

#endif






    /// <summary>
    /// Fetches a precompiled shader <see cref="BackendShaderReference"/> from the backend. 
    /// </summary>
    /// <param name="name"></param>
    /// <returns></returns>
    /// <exception cref="Exception"></exception>
    public static BackendShaderReference GetShader(string name)
    {

#if DEBUG
        lock (Shaders)
#endif
            return Shaders[name];

    }



    /// <summary>
    /// Fetches a precompiled compute shader <see cref="BackendComputeShaderReference"/> from the backend. 
    /// </summary>
    /// <param name="name"></param>
    /// <returns></returns>
    /// <exception cref="Exception"></exception>
    public static BackendComputeShaderReference GetComputeShader(string name)
    {

#if DEBUG
        lock (ComputeShaders)
#endif
            return ComputeShaders[name];

    }








    public class BackendShaderReference(ShaderMetadata metadata, object backendRef) : BackendReference(backendRef)
    {
        public readonly ShaderMetadata Metadata = metadata;

        public readonly ImmutableArray<BackendResourceSetReference> DefaultResourceSets = CreateDefaultResourceSets(metadata.ResourceSets);

        protected override void OnFree()
        {
            PushRenderThreadAction(() => { DestroyShader(this); return null; });
        }
    }


    public class BackendComputeShaderReference(ComputeShaderMetadata metadata, object backendRef) : BackendReference(backendRef)
    {
        public readonly ComputeShaderMetadata Metadata = metadata;

        public readonly ImmutableArray<BackendResourceSetReference> DefaultResourceSets = CreateDefaultResourceSets(metadata.ResourceSets);

        protected override void OnFree()
        {
            PushRenderThreadAction(() => { DestroyComputeShader(this); return null; });
        }
    }




    public static unsafe ImmutableArray<BackendResourceSetReference> CreateDefaultResourceSets(ImmutableDictionary<string, (uint Binding, ShaderMetadata.ShaderResourceSetMetadata Metadata)> ResourceSets)
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
        ImmutableDictionary<string, (byte Location, ShaderMetadata.ShaderInOutAttributeMetadata Metadata)> VertexInputAttributes,
        ImmutableDictionary<string, (byte Location, ShaderMetadata.ShaderInOutAttributeMetadata Metadata)> FragmentOutputAttributes,
        ImmutableDictionary<string, (uint Binding, ShaderMetadata.ShaderResourceSetMetadata Metadata)> ResourceSets
        )
    {

        public record class ShaderInOutAttributeMetadata(
            ShaderAttributeBufferFinalFormat Format,
            uint ArrayLength);

        public record class ShaderTextureMetadata(
            TextureSamplerTypes SamplerType,
            uint ArrayLength);

        public record class ShaderBufferMetadata(
           ImmutableDictionary<string, uint> FieldOffsets,
           ImmutableArray<ContiguousRegion> ContiguousRegions,
           uint SizeRequirement);

        public record class ShaderResourceSetMetadata(

            ImmutableArray<ResourceSetResourceDeclaration> Declaration,
            ImmutableDictionary<string, (uint Binding, ShaderTextureMetadata Metadata)> Textures,
            ImmutableDictionary<string, (uint Binding, ShaderBufferMetadata Metadata)> UniformBuffers,
            ImmutableDictionary<string, (uint Binding, ShaderBufferMetadata Metadata)> StorageBuffers

            );



#if DEBUG

        /// <summary>
        /// Debug-only field containing originating glsl.
        /// </summary>
        public string GeneratedVertexGLSL, GeneratedFragmentGLSL;

#endif

    }



    public record class ComputeShaderMetadata(
        ImmutableDictionary<string, (uint Binding, ShaderMetadata.ShaderResourceSetMetadata Metadata)> ResourceSets
        )
    {
#if DEBUG

        /// <summary>
        /// Debug-only field containing originating glsl.
        /// </summary>
        public string GeneratedGLSL;

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










    public class BackendDrawPipelineReference(object backendRef, DrawPipelineDetails details) : BackendReference(backendRef)
    {
        public readonly DrawPipelineDetails Details = details;

        protected override void OnFree()
        {
            PushRenderThreadAction(() => { DestroyDrawPipeline(this); return null; });
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















    public ref struct FrameBufferPipelineStateOperator
    {
        private readonly BackendFrameBufferPipelineReference Ref;
        private readonly LogicalFrameBuffer FBO;

        private readonly byte StageCount;

        private byte Stage;


        public FrameBufferPipelineStateOperator(LogicalFrameBuffer fbo, BackendFrameBufferPipelineReference obj)
        {
            Ref = obj;
            FBO = fbo;

            StageCount = Ref.Details.StageCount;

            PushDeferredRenderThreadCommand(new BeginSt(fbo.GetGenericGCHandle(), obj.GetGenericGCHandle()));
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
                    new AdvanceSt(FBO.GetGenericGCHandle(), Ref.GetGenericGCHandle(), Stage)
                );
            }
            else
            {
                PushDeferredRenderThreadCommand(
                    new EndSt(FBO.GetGenericGCHandle())
                );
            }

            Stage++;
        }


        private unsafe readonly record struct BeginSt(GCHandle<LogicalFrameBuffer> fbo, GCHandle<BackendFrameBufferPipelineReference> pipeline) : IDeferredCommand
        {
            public static unsafe void Execute(void* self)
            {
                var ptr = (BeginSt*)self;
                BeginFrameBufferPipeline(ptr->fbo.Target, ptr->pipeline.Target);
            }
        }

        private unsafe readonly record struct AdvanceSt(GCHandle<LogicalFrameBuffer> fbo, GCHandle<BackendFrameBufferPipelineReference> pipeline, byte stage) : IDeferredCommand
        {
            public static unsafe void Execute(void* self)
            {
                var ptr = (AdvanceSt*)self;
                AdvanceFrameBufferPipeline(ptr->fbo.Target, ptr->pipeline.Target, ptr->stage);
            }
        }


        private unsafe readonly record struct EndSt(GCHandle<LogicalFrameBuffer> fbo) : IDeferredCommand
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
    public static unsafe FrameBufferPipelineStateOperator StartFrameBufferPipeline(LogicalFrameBuffer framebuffer, ReadOnlySpan<FrameBufferPipelineStage> stages)
    {

#if DEBUG
        if (stages.Length > 8 || stages.Length == 0) throw new Exception();
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




        lock (FrameBufferPipelineCache)
        {
            if (!FrameBufferPipelineCache.TryGetValue(details, out var get))
            {
                get = new BackendFrameBufferPipelineReference(CreateFrameBufferPipeline(details), details);

                var d = details;
                FrameBufferPipelineCache.Add(d, get);
                get.OnFreeEvent.Add(() =>
                {
                    lock (FrameBufferPipelineCache)
                        FrameBufferPipelineCache.Remove(d);
                });
            }



            if (framebuffer.GetFramebuffer(get) == null)
            {
                bool depthUsed = false;
                bool stencilUsed = false;
                int specifiedColorAttachments = 0;

                for (int i = 0; i < stages.Length; i++)
                {
                    var st = stages[i];

                    if (st.SpecifiedColorAttachments != 0)
                    {
                        int highest = 31 - BitOperations.LeadingZeroCount(st.SpecifiedColorAttachments);
                        specifiedColorAttachments = Math.Max(specifiedColorAttachments, highest + 1);
                    }


                    depthUsed = depthUsed || st.SpecifiedDepth;
                    stencilUsed = stencilUsed || st.SpecifiedStencil;

                }


                framebuffer.AddFramebuffer(get, new BackendFrameBufferObjectReference(Backend.CreateFrameBufferObject(framebuffer.ColorAttachments.AsSpan()[..int.Min(framebuffer.ColorAttachments.Length, specifiedColorAttachments)], depthUsed ? framebuffer.DepthStencil : null, get, framebuffer.Dimensions)));
            }


            return new FrameBufferPipelineStateOperator(framebuffer, get);
        }

    }







    public class BackendFrameBufferPipelineReference(object backendRef, FrameBufferPipelineDetails details) : BackendReference(backendRef)
    {
        public readonly FrameBufferPipelineDetails Details = details;

        protected override void OnFree()
        {   
            PushRenderThreadAction(() => { DestroyFrameBufferPipeline(this); return null; });
        }
    }







    public class BackendFrameBufferObjectReference(object backendRef) : BackendReference(backendRef)
    {
        protected override void OnFree()
        {
            PushRenderThreadAction(() => { DestroyFrameBufferObject(this); return null; });
        }
    }



    /// <summary>
    /// A collection of attachments to render to.
    /// <br /> Internally, this creates and manages multiple <see cref="BackendFrameBufferObjectReference"/>s on demand to allow usage of any <see cref="BackendFrameBufferPipelineReference"/>.
    /// </summary>
    public class LogicalFrameBuffer : Freeable
    {

        public readonly ImmutableArray<BackendTextureReference> ColorAttachments;
        public readonly BackendTextureReference DepthStencil;

        public readonly Vector2<uint> Dimensions;

        public readonly TextureTypes Type;


        private readonly Dictionary<BackendFrameBufferPipelineReference, BackendFrameBufferObjectReference> Framebuffers = new(); 


        public void AddFramebuffer(BackendFrameBufferPipelineReference renderpass, BackendFrameBufferObjectReference fbo)
        {
            renderpass.AddUser();
            fbo.AddUser();

            if (!Framebuffers.TryAdd(renderpass, fbo)) throw new Exception();
        }


        public void RemoveFramebuffer(BackendFrameBufferPipelineReference renderpass)
        {
            if (!Framebuffers.TryGetValue(renderpass, out var fbo))
                throw new Exception();

            Framebuffers.Remove(renderpass);

            renderpass.RemoveUser();

            fbo.RemoveUser();
        }

        public BackendFrameBufferObjectReference GetFramebuffer(BackendFrameBufferPipelineReference renderpass)
        {
            if (!Framebuffers.TryGetValue(renderpass, out var get)) return null;

            return get;
        }






        private LogicalFrameBuffer(ImmutableArray<BackendTextureReference> colorAttachments, BackendTextureReference depthstencil, Vector2<uint> dimensions, TextureTypes type)
        {
            ColorAttachments = colorAttachments;
            DepthStencil = depthstencil;
            Dimensions = dimensions;
            Type= type;
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



        public static unsafe LogicalFrameBuffer Create(ReadOnlySpan<BackendTextureReference> colorTargets, BackendTextureReference depthStencilTarget)
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

            return new LogicalFrameBuffer(ImmutableArray.Create(colorTargets), depthStencilTarget, new Vector2<uint>(onecommon.Dimensions.X, onecommon.Dimensions.Y), onecommon.TextureType);
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







    private static BackendFrameBufferPipelineReference ActiveFramebufferPipeline;
    private static uint ActiveFrameBufferPipelineStage;
    private static LogicalFrameBuffer ActiveFrameBuffer;




    public const ushort MaxResourceSetResources = 32;


    public static RenderingBackendEnum CurrentBackend { get; private set; }

    private static IRenderingBackend Backend;





    /// <summary>
    /// Describes the current state of the render thread's rendering execution.
    /// </summary>
    public enum BackendRenderProgress
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



    public static BackendRenderProgress CurrentBackendRenderProgress { get; private set; }








    private static Dictionary<DrawPipelineDetails, BackendDrawPipelineReference> DrawPipelineCache = CreateUnsafeStructKeyComparisonDictionary<DrawPipelineDetails, BackendDrawPipelineReference>();
    private static Dictionary<SamplerDetails, BackendSamplerReference> SamplerCache = CreateUnsafeStructKeyComparisonDictionary<SamplerDetails, BackendSamplerReference>();
    private static Dictionary<FrameBufferPipelineDetails, BackendFrameBufferPipelineReference> FrameBufferPipelineCache = CreateUnsafeStructKeyComparisonDictionary<FrameBufferPipelineDetails, BackendFrameBufferPipelineReference>();


    private static Dictionary<string, BackendShaderReference> Shaders = new();
    private static Dictionary<string, BackendComputeShaderReference> ComputeShaders = new();






    /// <summary>
    /// <br/><b> WARNING: This method invokes one or more immediate rendering commands, and should only be called from the render thread during frame rendering. See <see cref="PushDeferredRenderThreadCommand{T}"/>. </b>
    /// </summary>
    private struct _callonrenderthread;


    /// <summary>
    /// <br/><b> WARNING: This method touches existing backend resources or state, and should be called outside of active frame rendering, from any thread. See <see cref="PushImmediateRenderThreadCommand{T}"/> or <see cref="PushDeferredPreRenderThreadCommand{T}"/>. </b>
    /// </summary>
    private struct _callonanythreadoutsideofrendering;


    /// <summary>
    /// <br/><b> WARNING: This method creates backend resources, and can be called from any thread regardless of engine state, though the backend implementation may decide to defer it.
    /// <br/>If you're looking to create a usable <see cref="BackendReference"/> instance, see static methods on that particular type, for example <see cref="BackendTextureReference.Create(Vector3{uint}, TextureTypes, TextureFormats, bool, byte[][])"/> </b>
    /// </summary>
    private struct _callonanythread;










    // ================================================================
    // ======================= DATA / RESOURCE MANAGEMENT ============
    // ================================================================



    // ---------------- Buffers ----------------


    /// <summary>
    /// <inheritdoc cref="_callonanythread"/>
    /// </summary>
    /// <param name="length"></param>
    /// <param name="writeable"></param>
    /// <param name="initialContent"></param>
    /// <returns></returns>
    public static unsafe object CreateVertexBuffer(uint length, bool writeable, void* initialContent)
    {
        return Backend.CreateVertexBuffer(length, writeable, initialContent);
    }

    /// <summary>
    /// <inheritdoc cref="_callonanythread"/>
    /// </summary>
    /// <param name="length"></param>
    /// <param name="writeable"></param>
    /// <param name="initialContent"></param>
    /// <returns></returns>
    public static unsafe object CreateIndexBuffer(uint length, bool writeable, void* initialContent)
    {
        return Backend.CreateIndexBuffer(length, writeable, initialContent);
    }

    /// <summary>
    /// <inheritdoc cref="_callonanythread"/>
    /// </summary>
    /// <param name="length"></param>
    /// <param name="writeable"></param>
    /// <param name="initialContent"></param>
    /// <returns></returns>
    public static unsafe object CreateUniformBuffer(uint length, bool writeable, void* initialContent)
    {
        return Backend.CreateUniformBuffer(length, writeable, initialContent);
    }


    /// <summary>
    /// <inheritdoc cref="_callonanythread"/>
    /// </summary>
    /// <param name="length"></param>
    /// <param name="writeable"></param>
    /// <param name="initialContent"></param>
    /// <returns></returns>
    public static unsafe object CreateStorageBuffer(uint length, bool writeable, void* initialContent)
    {
        return Backend.CreateStorageBuffer(length, writeable, initialContent);
    }


    /// <summary>
    /// <inheritdoc cref="_callonanythreadoutsideofrendering"/>
    /// </summary>
    /// <param name="buffer"></param>
    /// <param name="writes"></param>
    public static unsafe void WriteToBuffer(BackendBufferAllocationReference buffer, ReadOnlySpan<WriteRange> writes, uint idx)
    {
        CheckOutsideOfRendering();
        Backend.WriteToBuffer(buffer, writes, idx);
    }


    /// <summary>
    /// Advances/commits writes pushed prior via <see cref="WriteToBuffer(BackendBufferAllocationReference, ReadOnlySpan{WriteRange}, uint)"/>.
    /// <inheritdoc cref="_callonrenderthread"/>
    /// </summary>
    /// <param name="buffer"></param>
    /// <param name="writes"></param>
    public static unsafe void AdvanceActiveBufferWrite(BackendBufferAllocationReference buffer, uint idx)
    {
        if (idx != 0) CheckDuringRendering();
        Backend.AdvanceActiveBufferWrite(buffer, idx);
    }



    /// <summary>
    /// <inheritdoc cref="_callonanythreadoutsideofrendering"/>
    /// </summary>
    /// <param name="buffer"></param>
    public static void DestroyBuffer(BackendBufferAllocationReference buffer)
    {
        CheckOutsideOfRendering();
        Backend.DestroyBuffer(buffer);
    }





    // ---------------- Resource Sets ----------------

    /// <summary>
    /// <inheritdoc cref="_callonanythread"/>
    /// </summary>
    /// <param name="definition"></param>
    /// <returns></returns>
    public static object CreateResourceSet(ReadOnlySpan<ResourceSetResourceDeclaration> definition)
    {
        return Backend.CreateResourceSet(definition);
    }

    /// <summary>
    /// <inheritdoc cref="_callonanythreadoutsideofrendering"/>
    /// </summary>
    /// <param name="set"></param>
    /// <param name="contents"></param>
    public static void WriteToResourceSet(BackendResourceSetReference set, ReadOnlySpan<ResourceSetResourceBind> contents, uint idx)
    {
        CheckOutsideOfRendering();
        Backend.WriteToResourceSet(set, contents, idx);
    }


    /// <summary>
    /// Advances/commits writes pushed prior via <see cref="WriteToResourceSet(BackendResourceSetReference, ReadOnlySpan{ResourceSetResourceBind}, uint)"/>.
    /// <inheritdoc cref="_callonrenderthread"/>
    /// </summary>
    /// <param name="set"></param>
    /// <param name="contents"></param>
    public static void AdvanceActiveResourceSetWrite(BackendResourceSetReference set, uint idx)
    {
        if (idx != 0) CheckDuringRendering();
        Backend.AdvanceActiveResourceSetWrite(set, idx);
    }




    /// <summary>
    /// <inheritdoc cref="_callonanythreadoutsideofrendering"/>
    /// </summary>
    /// <param name="set"></param>
    public static void DestroyResourceSet(BackendResourceSetReference set)
    {
        CheckOutsideOfRendering();
        Backend.DestroyResourceSet(set);
    }





    // ---------------- Textures ----------------

    /// <summary>
    /// <inheritdoc cref="_callonanythread"/>
    /// </summary>
    /// <param name="Dimensions"></param>
    /// <param name="type"></param>
    /// <param name="format"></param>
    /// <param name="FramebufferAttachmentCompatible"></param>
    /// <param name="texturemips"></param>
    /// <returns></returns>
    public static object CreateTexture(Vector3<uint> Dimensions, TextureTypes type, TextureFormats format, bool FramebufferAttachmentCompatible, byte[][] texturemips = default)
    {


        if (texturemips != default)
        {
            ulong totalSize = 0;
            byte[][] convertedMips = new byte[texturemips.Length][];
            ulong[] mipOffsets = new ulong[texturemips.Length];

            for (byte mip = 0; mip < texturemips.Length; mip++)
            {
                byte[] mipData = texturemips[mip];

                if (format == TextureFormats.RGB8_UNORM)
                    mipData = ConvertRGB8ToRGBA8(mipData, uint.Max(1, Dimensions.X >> mip), uint.Max(1, Dimensions.Y >> mip));
                else if (format == TextureFormats.RGB16_SFLOAT)
                    mipData = ConvertRGB16ToRGBA16(mipData, uint.Max(1, Dimensions.X >> mip), uint.Max(1, Dimensions.Y >> mip));

                convertedMips[mip] = mipData;

                mipOffsets[mip] = totalSize;
                totalSize += (ulong)mipData.Length;
            }

            texturemips = convertedMips;
        }


        format = format switch
        {
            TextureFormats.RGB8_UNORM => TextureFormats.RGBA8_UNORM,
            TextureFormats.RGB16_SFLOAT => TextureFormats.RGBA16_SFLOAT,
            _ => format
        };

        return Backend.CreateTexture(Dimensions, type, format, FramebufferAttachmentCompatible, texturemips);

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


    /// <summary>
    /// <inheritdoc cref="_callonanythreadoutsideofrendering"/>
    /// </summary>
    /// <param name="texture"></param>
    public static void GenerateMipmaps(BackendTextureReference texture)
    {
        CheckOutsideOfRendering();
        Backend.GenerateMipmaps(texture);
    }

    /// <summary>
    /// <inheritdoc cref="_callonanythreadoutsideofrendering"/>
    /// </summary>
    /// <param name="tex"></param>
    /// <param name="level"></param>
    /// <param name="offsetX"></param>
    /// <param name="offsetY"></param>
    /// <param name="offsetZ"></param>
    /// <param name="sizeX"></param>
    /// <param name="sizeY"></param>
    /// <param name="sizeZ"></param>
    /// <returns></returns>
    public static ReadOnlySpan<byte> ReadTexturePixels(BackendTextureReference tex, uint level, Vector3<uint> offset, Vector3<uint> size)
    {
        CheckOutsideOfRendering();
        return Backend.ReadTexturePixels(tex, level, offset, size);
    }

    /// <summary>
    /// <inheritdoc cref="_callonanythreadoutsideofrendering"/>
    /// </summary>
    /// <param name="tex"></param>
    /// <param name="level"></param>
    /// <param name="offsetX"></param>
    /// <param name="offsetY"></param>
    /// <param name="offsetZ"></param>
    /// <param name="sizeX"></param>
    /// <param name="sizeY"></param>
    /// <param name="sizeZ"></param>
    /// <param name="content"></param>
    public static void WriteTexturePixels(BackendTextureReference tex, uint level, Vector3<uint> offset, Vector3<uint> size, ReadOnlySpan<byte> content)
    {
        CheckOutsideOfRendering();
        Backend.WriteTexturePixels(tex, level, offset, size, content);
    }

    /// <summary>
    /// <inheritdoc cref="_callonanythreadoutsideofrendering"/>
    /// </summary>
    /// <param name="texture"></param>
    public static void DestroyTexture(BackendTextureReference texture)
    {
        CheckOutsideOfRendering();
        Backend.DestroyTexture(texture);
    }

    /// <summary>
    /// <inheritdoc cref="_callonanythread"/>
    /// </summary>
    /// <param name="details"></param>
    /// <returns></returns>
    public static object CreateTextureSampler(SamplerDetails details)
    {
        return Backend.CreateTextureSampler(details);
    }

    /// <summary>
    /// <inheritdoc cref="_callonanythreadoutsideofrendering"/>
    /// </summary>
    /// <param name="texture"></param>
    public static void DestroyTextureSampler(BackendSamplerReference texture)
    {
        CheckOutsideOfRendering();
        Backend.DestroyTextureSampler(texture);
    }





    // ---------------- FrameBuffer Objects ----------------

    /// <summary>
    /// <inheritdoc cref="_callonanythread"/>
    /// </summary>
    /// <param name="colorTargets"></param>
    /// <param name="depthStencilTarget"></param>
    /// <param name="pipeline"></param>
    /// <param name="resolutionX"></param>
    /// <param name="resolutionY"></param>
    /// <returns></returns>
    public static unsafe object CreateFrameBufferObject(ReadOnlySpan<BackendTextureReference> colorTargets, BackendTextureReference depthStencilTarget, BackendFrameBufferPipelineReference pipeline, Vector2<uint> dimensions)
    {
        return Backend.CreateFrameBufferObject(colorTargets, depthStencilTarget, pipeline, dimensions);
    }

    /// <summary>
    /// <inheritdoc cref="_callonanythreadoutsideofrendering"/>
    /// </summary>
    /// <param name="buffer"></param>
    public static void DestroyFrameBufferObject(BackendFrameBufferObjectReference buffer)
    {
        CheckOutsideOfRendering();
        Backend.DestroyFrameBufferObject(buffer);
    }






    // ---------------- Shaders / Compute ----------------

    /// <summary>
    /// <inheritdoc cref="_callonanythread"/>
    /// </summary>
    /// <param name="ShaderSource"></param>
    /// <returns></returns>
    public static object CreateShader(ShaderSource ShaderSource)
    {
        return Backend.CreateShader(ShaderSource);
    }

    /// <summary>
    /// <inheritdoc cref="_callonanythreadoutsideofrendering"/>
    /// </summary>
    /// <param name="shader"></param>
    public static void DestroyShader(BackendShaderReference shader)
    {
        CheckOutsideOfRendering();
        Backend.DestroyShader(shader);
    }

    /// <summary>
    /// <inheritdoc cref="_callonanythread"/>
    /// </summary>
    /// <param name="ShaderSource"></param>
    /// <returns></returns>
    public static object CreateComputeShader(ComputeShaderSource ShaderSource)
    {
        return Backend.CreateComputeShader(ShaderSource);
    }

    /// <summary>
    /// <inheritdoc cref="_callonanythreadoutsideofrendering"/>
    /// </summary>
    /// <param name="shader"></param>
    /// <param name="groupCountX"></param>
    /// <param name="groupCountY"></param>
    /// <param name="groupCountZ"></param>
    public static void DispatchComputeShader(BackendComputeShaderReference shader, uint groupCountX, uint groupCountY, uint groupCountZ)
    {
        CheckOutsideOfRendering();
        Backend.DispatchComputeShader(shader, groupCountX, groupCountY, groupCountZ);
    }

    /// <summary>
    /// <inheritdoc cref="_callonanythreadoutsideofrendering"/>
    /// </summary>
    public static void WaitForAllComputeShaders()
    {
        CheckOutsideOfRendering();
        Backend.WaitForAllComputeShaders();
    }

    /// <summary>
    /// <inheritdoc cref="_callonanythreadoutsideofrendering"/>
    /// </summary>
    /// <param name="shader"></param>
    public static void DestroyComputeShader(BackendComputeShaderReference shader)
    {
        CheckOutsideOfRendering();
        Backend.DestroyComputeShader(shader);
    }




    // ---------------- Pipelines ----------------

    /// <summary>
    /// <inheritdoc cref="_callonanythread"/>
    /// </summary>
    /// <param name="details"></param>
    /// <returns></returns>
    public static object CreateDrawPipeline(DrawPipelineDetails details)
    {
        return Backend.CreateDrawPipeline(details);
    }

    /// <summary>
    /// <inheritdoc cref="_callonanythreadoutsideofrendering"/>
    /// </summary>
    /// <param name="pipeline"></param>
    public static void DestroyDrawPipeline(BackendDrawPipelineReference pipeline)
    {
        CheckOutsideOfRendering();
        Backend.DestroyDrawPipeline(pipeline);
    }

    /// <summary>
    /// <inheritdoc cref="_callonanythread"/>
    /// </summary>
    /// <param name="details"></param>
    /// <returns></returns>
    public static object CreateFrameBufferPipeline(FrameBufferPipelineDetails details)
    {
        return Backend.CreateFrameBufferPipeline(details);
    }

    /// <summary>
    /// <inheritdoc cref="_callonanythreadoutsideofrendering"/>
    /// </summary>
    /// <param name="backendRenderPassReference"></param>
    public static void DestroyFrameBufferPipeline(BackendFrameBufferPipelineReference backendRenderPassReference)
    {
        CheckOutsideOfRendering();
        Backend.DestroyFrameBufferPipeline(backendRenderPassReference);
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
    public static void ConfigureSwapchain(bool UseHDR)
    {
        CheckOutsideOfRendering();

        var ret = Backend.ConfigureSwapchain(UseHDR);

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

#if DEBUG
        if (CurrentBackendRenderProgress != BackendRenderProgress.Downtime) 
            throw new Exception();
#endif

        Backend.EndFrameRendering();
        CurrentBackendRenderProgress = BackendRenderProgress.NotRendering;
    }




    // ---------------- FrameBuffer / Pipeline Control ----------------

    /// <summary>
    /// <inheritdoc cref="_callonrenderthread"/>
    /// </summary>
    /// <param name="fbo"></param>
    /// <param name="pipeline"></param>
    public static void BeginFrameBufferPipeline(LogicalFrameBuffer fbo, BackendFrameBufferPipelineReference pipeline)
    {
        CheckDuringRendering();

#if DEBUG
        if (CurrentBackendRenderProgress != BackendRenderProgress.Downtime)
            throw new Exception();
#endif

        ActiveFramebufferPipeline = pipeline;
        ActiveFrameBuffer = fbo;
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
    public static void AdvanceFrameBufferPipeline(LogicalFrameBuffer fbo, BackendFrameBufferPipelineReference pipeline, byte stageIndex)
    {
        CheckDuringRendering();
        ActiveFrameBufferPipelineStage++;
        Backend.AdvanceFrameBufferPipeline(fbo, pipeline, stageIndex);
    }


    /// <summary>
    /// <inheritdoc cref="_callonrenderthread"/>
    /// </summary>
    /// <param name="Framebuffer"></param>
    public static void EndFrameBufferPipeline(LogicalFrameBuffer Framebuffer)
    {
#if DEBUG
        if (CurrentBackendRenderProgress != BackendRenderProgress.DrawingViaFramebufferPipeline)
            throw new Exception();
#endif

        CheckDuringRendering();
        Backend.EndFrameBufferPipeline(Framebuffer);
        ActiveFramebufferPipeline = null;
        ActiveFrameBuffer = null;
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


    public static unsafe void Draw(


        ref UnmanagedKeyValueHandleCollection<string, VertexAttributeDefinitionPlusBufferClass> Attributes,
        ref UnmanagedKeyValueHandleCollection<string, BackendResourceSetReference> ResourceSets,
        BackendShaderReference Shader,

        ref RasterizationDetails Rasterization,
        ref BlendState Blending,
        ref DepthStencilState DepthStencil,

        BackendIndexBufferAllocationReference IndexBuffer,
        ref IndexingDetails IndexingDetails

        )

    {

        CheckDuringRendering();



        if (IndexingDetails.End == IndexingDetails.Start) return;


#if DEBUG
        if (IndexingDetails.End < IndexingDetails.Start) 
            throw new Exception();
#endif






#if DEBUG
        if (ActiveFramebufferPipeline == null && CurrentBackendRenderProgress != BackendRenderProgress.DrawingToScreen)
            throw new Exception("no active framebuffer pipeline");  
#endif






        Span<VertexAttributeDefinitionPlusBufferStruct> attrs = stackalloc VertexAttributeDefinitionPlusBufferStruct[Shader.Metadata.VertexInputAttributes.Count];



        PipelineAttributeDetails layoutdetails = new();
        byte count = 0;

        foreach (var kv in Shader.Metadata.VertexInputAttributes)
        {
            var ShaderAttributeLocation = kv.Value.Location;
            var ShaderDataFormat = kv.Value.Metadata.Format;

            var buf = Attributes[kv.Key];




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






        Span<GCHandle<BackendResourceSetReference>> sets = stackalloc GCHandle<BackendResourceSetReference>[Shader.Metadata.ResourceSets.Count];

        foreach (var kv in Shader.Metadata.ResourceSets)
        {
            var idx = (int)kv.Value.Binding;
            var setGet = ResourceSets[kv.Key];
            sets[idx] = setGet.IsAllocated ? setGet : Shader.DefaultResourceSets[idx].GetGenericGCHandle();
        }





        DrawPipelineDetails pipelinerequest = new()
        {
            //derived from buffer definition + shader attributes
            Attributes = layoutdetails,

            ShaderHandle = GCHandle.ToIntPtr(Shader.GCHandle),
            FrameBufferPipelineHandle = CurrentBackendRenderProgress == BackendRenderProgress.DrawingToScreen ? 0 : GCHandle.ToIntPtr(ActiveFramebufferPipeline.GCHandle),

            //derived from draw call/material specifics
            Rasterization = Rasterization,
            Blending = Blending,
            DepthStencil = DepthStencil,

        };




        //find or create a pipeline according to that spec

        lock (DrawPipelineCache)
        {

            if (!DrawPipelineCache.TryGetValue(pipelinerequest, out var get))
            {
                //creation
                get = new BackendDrawPipelineReference(CreateDrawPipeline(pipelinerequest), pipelinerequest);

                //destroys this pipeline if the shader or render pass it relies on are destroyed
                Shader.OnFreeEvent.Add(get.Free);


                get.OnFreeEvent.Add(() => 
                { 
                    lock (DrawPipelineCache) 
                        DrawPipelineCache.Remove(pipelinerequest); 
                });


                if (CurrentBackendRenderProgress != BackendRenderProgress.DrawingToScreen) 
                    ActiveFramebufferPipeline.OnFreeEvent.Add(get.Free);


                DrawPipelineCache.Add(pipelinerequest, get);
            }

            Backend.Draw(attrs, sets, get, IndexBuffer, 0, IndexingDetails);

        }

    }









    // ---------------- FrameBuffer Manipulation ----------------

    /// <summary>
    /// <inheritdoc cref="_callonrenderthread"/>
    /// </summary>
    /// <param name="framebuffer"></param>
    /// <param name="CubemapFaceIfCubemap"></param>
    public static void ClearFramebufferDepthStencil(LogicalFrameBuffer framebuffer, byte CubemapFaceIfCubemap = 0)
    {
        CheckDuringRendering();
        Backend.ClearFramebufferDepthStencil(framebuffer, CubemapFaceIfCubemap);
    }

    /// <summary>
    /// <inheritdoc cref="_callonrenderthread"/>
    /// </summary>
    /// <param name="framebuffer"></param>
    /// <param name="color"></param>
    /// <param name="idx"></param>
    /// <param name="CubemapFaceIfCubemap"></param>
    public static void ClearFramebufferColorAttachment(LogicalFrameBuffer framebuffer, Vector4 color, byte idx = 0, byte CubemapFaceIfCubemap = 0)
    {
        CheckDuringRendering();
        Backend.ClearFramebufferColorAttachment(framebuffer, color, idx, CubemapFaceIfCubemap);
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




    // ---------------- Scissor ----------------

    /// <summary>
    /// <inheritdoc cref="_callonrenderthread"/>
    /// </summary>
    /// <param name="offsetX"></param>
    /// <param name="offsetY"></param>
    /// <param name="sizeX"></param>
    /// <param name="sizeY"></param>
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

        public SwapchainDetails ConfigureSwapchain(bool UseHDR);





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
        public void BeginFrameBufferPipeline(LogicalFrameBuffer fbo, BackendFrameBufferPipelineReference pipeline);
        public void AdvanceFrameBufferPipeline(LogicalFrameBuffer fbo, BackendFrameBufferPipelineReference pipeline, byte stageIndex);
        public void EndFrameBufferPipeline(LogicalFrameBuffer fbo);
        public void StartDrawToScreen();
        public void EndDrawToScreen();



        // ---------------- Drawing ----------------
        public void Draw(ReadOnlySpan<VertexAttributeDefinitionPlusBufferStruct> buffers, ReadOnlySpan<GCHandle<BackendResourceSetReference>> ResourceSets, BackendDrawPipelineReference pipeline, BackendIndexBufferAllocationReference indexbuffer, uint indexBufferOffset, IndexingDetails indexing);



        // ---------------- FrameBuffer Manipulation ----------------
        public void ClearFramebufferDepthStencil(LogicalFrameBuffer framebuffer, byte CubemapFaceIfCubemap = 0);
        public void ClearFramebufferColorAttachment(LogicalFrameBuffer framebuffer, Vector4 color, byte idx = 0, byte CubemapFaceIfCubemap = 0);
        public void SetScissor(Vector2<uint> offset, Vector2<uint> size);
    }


}