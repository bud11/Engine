


namespace Engine.Core;

using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using static Comparisons;
using static EngineMath;
using static Engine.Core.References;
using static RenderingBackend.ResourceSetResourceDeclaration;
using static RenderThread;




/// <summary>
/// Provides near direct immediate access to the current rendering backend. Also see <seealso cref="Rendering"/> and/or <seealso cref="IDeferredCommand{TSelf}"/>
/// </summary>
public static partial class RenderingBackend
{




    public static SDL3.SDL.WindowFlags GetSDLWindowFlagsForBackend(RenderingBackendEnum Backend)
        => RenderingBackendData[Backend.ToString()].Flags;





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
    /// Represents a real gpu-backed resource.
    /// </summary>
    public abstract class BackendReference : RefCounted
    {
        /// <summary>
        /// A rendering-backend-specific object. Usually contains something like a handle and possibly some internal state. Can be null.
        /// <br /> <b>! ! ! Should NEVER be modified or referenced by anything besides the <see cref="IRenderingBackend"/> that created it and any attempt to do so will likely backfire. ! ! !</b>
        /// <br /> Using this to store creation/state information is usually redundant due to the encompassing class already doing that.
        /// </summary>
        public object BackendRef;


        public BackendReference(object backendRef)
        {
            BackendRef = backendRef;

            lock (AllBackendReferences)
                AllBackendReferences.Add(this);
        }

        protected override void OnFree()
        {
            lock (AllBackendReferences)
                AllBackendReferences.Remove(this);
        }

    }

    private static readonly HashSet<BackendReference> AllBackendReferences = new();













#if DEBUG
    public static readonly Dictionary<string, ShaderMetadata.ShaderResourceSetMetadata> GlobalResourceSetMetadata = new();
#endif





    private static readonly Dictionary<ShaderMetadata.ShaderResourceSetMetadata, BackendResourceSetReference> DummyResourceSets = new();


    public static BackendTextureAndSamplerReferencesPair Dummy2DTextureSamplerPair { get; private set; }
    public static BackendTextureAndSamplerReferencesPair Dummy2DShadowTextureSamplerPair { get; private set; }
    public static BackendTextureAndSamplerReferencesPair DummyCubeTextureSamplerPair { get; private set; }
    public static BackendTextureAndSamplerReferencesPair Dummy3DTextureSamplerPair { get; private set; }

    public static BackendBufferReference.IVertexBuffer DummyVertex { get; private set; }



    private static SortedDictionary<uint, BackendBufferReference> DummyBuffers = new();

    public static BackendBufferReference GetDummyBuffer(uint sizeReq, BufferUsageFlags type, ReadWriteFlags flags)
    {
        lock (DummyBuffers)
        {
            foreach (var kv in DummyBuffers)
                if (kv.Value.Size >= sizeReq)
                    return kv.Value;

            var ret = DummyBuffers[sizeReq] = BackendBufferReference.Create(sizeReq, type, flags);

            return ret;
        }
    }

    public static bool IsResourceDummy(IBackendResourceReference res)
    {
        if (res == Dummy2DTextureSamplerPair) return true;
        if (res == Dummy2DShadowTextureSamplerPair) return true;
        if (res == Dummy3DTextureSamplerPair) return true;
        if (res == DummyCubeTextureSamplerPair) return true;

        if (res is BackendBufferReference databuf && DummyBuffers.ContainsValue(databuf)) return true;

        return false;
    }






    public static unsafe void CreateBasicObjects()
    {

        DummyVertex = (BackendBufferReference.IVertexBuffer)BackendBufferReference.Create(1, BufferUsageFlags.Vertex, ReadWriteFlags.GPURead);




        //solid white 1x1
        Dummy2DTextureSamplerPair = new BackendTextureAndSamplerReferencesPair(
            BackendTextureReference.Create(new Vector3<uint>(1), TextureTypes.Texture2D, TextureFormats.RGB8_UNORM, FramebufferAttachmentCompatible: false, Mips: [[255, 255, 255]]),
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







    public class VertexAttributeDefinitionBufferPair
    {

        private BackendBufferReference.IVertexBuffer Buf;
        private WeakObjRef<BackendBufferReference.IVertexBuffer> BufRef;

        public BackendBufferReference.IVertexBuffer Buffer
        {
            get => Buf;
            set
            {
                Buf = value;
                BufRef = Buf.GetRef();
            }
        }


        public VertexAttributeDefinition Definition;

        public VertexAttributeDefinitionBufferPair(BackendBufferReference.IVertexBuffer buffer, VertexAttributeDefinition definition)
        {
            Buffer = buffer;
            Definition = definition;
        }


        public Struct GetStruct() => new Struct(BufRef, Definition);

        public readonly record struct Struct(WeakObjRef<BackendBufferReference.IVertexBuffer> BufferRef, VertexAttributeDefinition Definition);
    }



    public static UnmanagedKeyValueCollection<WeakObjRef<string>, VertexAttributeDefinitionBufferPair.Struct> VertexAttributeDictToUnmanaged(this IDictionary<string, VertexAttributeDefinitionBufferPair> dict)
    {
        var attrs = new UnmanagedKeyValueCollection<WeakObjRef<string>, VertexAttributeDefinitionBufferPair.Struct>();

        foreach (var kv in dict)
            attrs.KeyValuePairs[attrs.Count++] = new(kv.Key.GetRef(), kv.Value.GetStruct());

        return attrs;
    }







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
    /// Directly represents a gpu-side buffer.
    /// <br/> Given a buffer supports one or more usages, it can be explicitly cast to one or more corresponding interfaces.
    /// <br/> For example, given a buffer was created with <see cref="BufferUsageFlags.Vertex"/>, it can be cast to <see cref="IVertexBuffer"/>.
    /// </summary>
    public unsafe class BackendBufferReference : BackendReference
    {




        /// <summary>
        /// A <see cref="BackendBufferReference"/> created with <see cref="BufferUsageFlags.Vertex"/>. Should never be implemented elsewhere.
        /// </summary>
        public interface IVertexBuffer;


        /// <summary>
        /// A <see cref="BackendBufferReference"/> created with <see cref="BufferUsageFlags.Index"/>. Should never be implemented elsewhere.
        /// </summary>
        public interface IIndexBuffer;



        /// <summary>
        /// A <see cref="BackendBufferReference"/> created with <see cref="BufferUsageFlags.Uniform"/> or <see cref="BufferUsageFlags.Storage"/>. Should never be implemented elsewhere.
        /// </summary>
        public unsafe interface IDataBuffer : IBackendResourceReference
        {
            public ShaderMetadata.ShaderDataBufferMetadata Metadata { get; set; }


            /// <summary>
            /// Writes from the offset of a top level member defined in <see cref="IDataBuffer.Metadata"/>.
            /// <br/> If <paramref name="skipPadding"/> is true, input data should be tightly packed, and will be scatter copied into the buffer, skipping over padding.
            /// </summary>
            /// <typeparam name="ValueT"></typeparam>
            /// <param name="fieldName"></param>
            /// <param name="val"></param>
            /// <param name="extraOffset"></param>
            /// <param name="skipPadding"></param>
            public void WriteFromOffsetOf<ValueT>(string fieldName, ValueT val, uint extraOffset = 0, bool skipPadding = true) where ValueT : unmanaged;


            /// <summary>
            /// <inheritdoc cref="WriteFromOffsetOf{ValueT}(string, ValueT, uint, bool)"/>
            /// </summary>
            /// <param name="fieldName"></param>
            /// <param name="dataSize"></param>
            /// <param name="dataPtr"></param>
            /// <param name="extraOffset"></param>
            /// <param name="skipPadding"></param>
            public void WriteFromOffsetOf(string fieldName, uint dataSize, void* dataPtr, uint extraOffset = 0, bool skipPadding = true);


            /// <summary>
            /// Writes into this buffer while skipping over padding defined in given metadata.
            /// </summary>
            /// <param name="write"></param>
            public unsafe void WriteAndSkipPadding(WriteRange write);
        }







        public readonly uint Size;

        public readonly BufferUsageFlags UsageFlags;
        public readonly ReadWriteFlags AccessFlags;



        private BackendBufferReference(uint size, BufferUsageFlags usageFlags, ReadWriteFlags accessFlags, object backendRef) : base(backendRef)
        {
            Size = size;
            UsageFlags = usageFlags;
            AccessFlags = accessFlags;
        }



        protected override void OnFree()
        {
            Backend.DestroyBuffer(this);

            base.OnFree();
        }







        /// <summary>
        /// Creates an empty zeroed buffer from raw information.
        /// </summary>
        /// <param name="size"></param>
        /// <param name="usageFlags"></param>
        /// <param name="accessFlags"></param>
        /// <returns></returns>
        [DebuggerHidden]
        [StackTraceHidden]
        public static BackendBufferReference Create(uint size, BufferUsageFlags usageFlags, ReadWriteFlags accessFlags)
            => CreateBufferInternal<byte>(size, default, default, usageFlags, accessFlags, null);



        /// <summary>
        /// Creates a buffer from raw information with initial content set via internal staging buffer copy.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="size"></param>
        /// <param name="initialContent"></param>
        /// <param name="initialContentWriteOffset"></param>
        /// <param name="usageFlags"></param>
        /// <param name="accessFlags"></param>
        /// <returns></returns>
        [DebuggerHidden]
        [StackTraceHidden]
        public static BackendBufferReference Create<T>(uint size, ReadOnlySpan<T> initialContent, uint initialContentWriteOffset, BufferUsageFlags usageFlags, ReadWriteFlags accessFlags) where T : unmanaged
            => CreateBufferInternal(size, initialContent, initialContentWriteOffset, usageFlags, accessFlags, null);



        /// <summary>
        /// Creates a buffer from raw information with initial content set via internal staging buffer copy.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="initialContent"></param>
        /// <param name="usageFlags"></param>
        /// <param name="accessFlags"></param>
        /// <returns></returns>
        [DebuggerHidden]
        [StackTraceHidden]
        public static BackendBufferReference Create<T>(ReadOnlySpan<T> initialContent, BufferUsageFlags usageFlags, ReadWriteFlags accessFlags) where T : unmanaged
            => CreateBufferInternal((uint)(initialContent.Length*sizeof(T)), initialContent, 0, usageFlags, accessFlags, null);




        /// <summary>
        /// Creates an empty zeroed buffer based on data within an instance of <see cref="ShaderMetadata.ShaderDataBufferMetadata"/>.
        /// </summary>
        /// <param name="metadata"></param>
        /// <param name="sizeOverride"></param>
        /// <param name="extraUsageFlags"></param>
        /// <param name="extraAccessFlags"></param>
        /// <returns></returns>
        [DebuggerHidden]
        [StackTraceHidden]
        public static IDataBuffer CreateDataBufferFromMetadata(ShaderMetadata.ShaderDataBufferMetadata metadata, uint sizeOverride = default, BufferUsageFlags extraUsageFlags = default, ReadWriteFlags extraAccessFlags = default)
            => (IDataBuffer)CreateBufferInternal<byte>(uint.Max(sizeOverride, metadata.SizeRequirement), default, default, metadata.UsageFlags | extraUsageFlags, metadata.ReadWriteFlags | extraAccessFlags, metadata);


        /// <summary>
        /// Creates a buffer based on data within an instance of <see cref="ShaderMetadata.ShaderDataBufferMetadata"/>, with initial content set via internal staging buffer copy.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="metadata"></param>
        /// <param name="initialContent"></param>
        /// <param name="initialContentWriteOffset"></param>
        /// <param name="sizeOverride"></param>
        /// <param name="extraUsageFlags"></param>
        /// <param name="extraAccessFlags"></param>
        /// <returns></returns>
        [DebuggerHidden]
        [StackTraceHidden]
        public static IDataBuffer CreateDataBufferFromMetadata<T>(ShaderMetadata.ShaderDataBufferMetadata metadata, ReadOnlySpan<T> initialContent, uint initialContentWriteOffset, uint sizeOverride = default, BufferUsageFlags extraUsageFlags = default, ReadWriteFlags extraAccessFlags = default) where T : unmanaged
            => (IDataBuffer)CreateBufferInternal(uint.Max(sizeOverride, metadata.SizeRequirement), initialContent, initialContentWriteOffset, metadata.UsageFlags | extraUsageFlags, metadata.ReadWriteFlags | extraAccessFlags, metadata);






        [DebuggerHidden]
        [StackTraceHidden]

        private static unsafe BackendBufferReference CreateBufferInternal<T>(uint size,
                                                                             ReadOnlySpan<T> initialContent,
                                                                             uint initialContentWriteOffset,
                                                                             BufferUsageFlags usageFlags,
                                                                             ReadWriteFlags accessFlags,
                                                                             ShaderMetadata.ShaderDataBufferMetadata metadata) where T : unmanaged
        {


#if DEBUG
            if (size == 0) 
                throw new Exception("Size cannot be zero");
#endif




            object backendRef = null;

            if (!initialContent.IsEmpty)
                fixed (void* p = initialContent)
                    backendRef = Backend.CreateBuffer(size, p, usageFlags, accessFlags);

            else
                backendRef = Backend.CreateBuffer(size, null, usageFlags, accessFlags);



            if (usageFlags.HasFlags([BufferUsageFlags.Vertex, BufferUsageFlags.Storage]))
                return new BackendBufferReference_VertexANDStorage(size, usageFlags, accessFlags, metadata, backendRef);

            if (usageFlags.HasFlags([BufferUsageFlags.Vertex]))
                return new BackendBufferReference_Vertex(size, usageFlags, accessFlags, backendRef);

            if (usageFlags.HasFlags([BufferUsageFlags.Index, BufferUsageFlags.Storage]))
                return new BackendBufferReference_IndexANDStorage(size, usageFlags, accessFlags, metadata, backendRef);

            if (usageFlags.HasFlags([BufferUsageFlags.Index]))
                return new BackendBufferReference_Index(size, usageFlags, accessFlags, backendRef);

            if (usageFlags.HasFlag(BufferUsageFlags.Storage) || usageFlags.HasFlag(BufferUsageFlags.Uniform))
                return new BackendBufferReference_Data(size, usageFlags, accessFlags, metadata, backendRef);


            throw new NotImplementedException("Invalid buffer usage combination");
        }




        private class BackendBufferReference_Vertex
            (uint size, BufferUsageFlags usageFlags, ReadWriteFlags accessFlags, object backendRef) :
            BackendBufferReference(size, usageFlags, accessFlags, backendRef),
            IVertexBuffer;

        private class BackendBufferReference_VertexANDStorage
            (uint size, BufferUsageFlags usageFlags, ReadWriteFlags accessFlags, ShaderMetadata.ShaderDataBufferMetadata? metadata, object backendRef) :
            BackendBufferReference_Data(size, usageFlags, accessFlags, metadata, backendRef),
            IVertexBuffer, IDataBuffer;

        private class BackendBufferReference_Index
            (uint size, BufferUsageFlags usageFlags, ReadWriteFlags accessFlags, object backendRef) :
            BackendBufferReference(size, usageFlags, accessFlags, backendRef),
            IIndexBuffer;

        private class BackendBufferReference_IndexANDStorage
            (uint size, BufferUsageFlags usageFlags, ReadWriteFlags accessFlags, ShaderMetadata.ShaderDataBufferMetadata? metadata, object backendRef) :
            BackendBufferReference_Data(size, usageFlags, accessFlags, metadata, backendRef),
            IIndexBuffer, IDataBuffer;


        private class BackendBufferReference_Data
            (uint size, BufferUsageFlags usageFlags, ReadWriteFlags accessFlags, ShaderMetadata.ShaderDataBufferMetadata? metadata, object backendRef) :
            BackendBufferReference(size, usageFlags, accessFlags, backendRef),
            IDataBuffer
        
        {


            [DebuggerHidden]
            [StackTraceHidden]

            [Conditional("DEBUG")]
            private void AssetHasMetadata()
            {
                if (IsResourceDummy(this))
                    throw new InvalidOperationException($"This buffer is a dummy buffer and shouldn't be written to.");


                if (Metadata == null)
                    throw new InvalidOperationException($"This data buffer has no assigned metadata and cannot be written into with '{nameof(IDataBuffer)}' methods.");
            }



            public ShaderMetadata.ShaderDataBufferMetadata Metadata { get; set; } = metadata;


            [DebuggerHidden]
            [StackTraceHidden]
            public void WriteFromOffsetOf<ValueT>(string fieldName, ValueT val, uint extraOffset = 0, bool skipPadding = true) where ValueT : unmanaged
            {
                AssetHasMetadata();

                if (skipPadding) WriteAndSkipPadding(new WriteRange(Metadata.FieldOffsets[fieldName] + extraOffset, (uint)sizeof(ValueT), &val));
                else PushDeferredWrite(new WriteRange(Metadata.FieldOffsets[fieldName] + extraOffset, (uint)sizeof(ValueT), &val));
            }

            [DebuggerHidden]
            [StackTraceHidden]
            public void WriteFromOffsetOf(string fieldName, uint dataSize, void* dataPtr, uint extraOffset = 0, bool skipPadding = true)
            {
                AssetHasMetadata();

                if (skipPadding) WriteAndSkipPadding(new WriteRange(Metadata.FieldOffsets[fieldName] + extraOffset, dataSize, dataPtr));
                else PushDeferredWrite(new WriteRange(Metadata.FieldOffsets[fieldName] + extraOffset, dataSize, dataPtr));
            }

            [DebuggerHidden]
            [StackTraceHidden]
            public unsafe void WriteAndSkipPadding(WriteRange write)
            {
                AssetHasMetadata();

                var alloc = AllocateRenderTemporaryUnmanaged((int)write.Length);
                BufferToPaddedBufferCopy((byte*)write.Content, write.Length, 0, alloc, Metadata.ContiguousRegions.AsSpan());

                PushDeferredWrite(new WriteRange(write.Offset, write.Length, alloc));
            }

        }




        [DebuggerHidden]
        [StackTraceHidden]
        public unsafe void PushDeferredWrite(WriteRange write)
        {

#if DEBUG
            if (!AccessFlags.HasFlag(ReadWriteFlags.CPUWrite)) 
                throw new InvalidOperationException($"This buffer wasn't created with {nameof(ReadWriteFlags.CPUWrite)}");

            if (write.Length == 0 || write.Content == null)
                throw new InvalidOperationException($"Null/empty write given");

            if (write.Length + write.Offset > Size)
                throw new InvalidOperationException($"Out-of-bounds write given:\nWrite Length: {write.Length}\nWrite Offset: {write.Offset}\nBuffer Size: {Size}");
#endif



            var alloc = AllocateRenderTemporaryUnmanaged((int)write.Length);

            Unsafe.CopyBlockUnaligned(alloc, (byte*)write.Content, write.Length);



            PushDeferredPreRenderThreadCommand( (new WriteRange(write.Offset, write.Length, alloc), this.GetRef()),  &Execute);

            static void Execute( (WriteRange wr, WeakObjRef<BackendBufferReference> bufhandle)* arg )
                => Backend.WriteToBuffer(arg->bufhandle.Dereference(), arg->wr);

        }

    }

















    /// <summary>
    /// Represents a resource in a <see cref="BackendResourceSetReference"/>.
    /// </summary>
    public interface IBackendResourceReference;



    /// <summary>
    /// Represents a collection of <see cref="IBackendResourceReference"/>s that shaders can access.
    /// </summary>
    public class BackendResourceSetReference : BackendReference
    {

        public readonly ShaderMetadata.ShaderResourceSetMetadata Metadata;


        private readonly RefCountCollections.RefCountedArray<RefCounted> Contents;

        public ReadOnlySpan<RefCounted> GetContents() => Contents.AsSpan();

        public uint ResourceCount => (uint)Metadata.Declaration.Length;





        private BackendResourceSetReference(ShaderMetadata.ShaderResourceSetMetadata metadata, object backendResource) : base(backendResource)
        {
            Metadata = metadata;
            Contents = new ((int)ResourceCount);
        }


        protected override void OnFree()
        {
            PushRenderThreadAction(() => { Backend.DestroyResourceSet(this); return null; });

            base.OnFree();
        }




        public unsafe T SetResource<T>(string name, T resource) where T : IBackendResourceReference
        {
            if (resource is BackendTextureAndSamplerReferencesPair) return (T)SetResource(Metadata.Textures[name].Binding, resource);
            else if (resource is BackendBufferReference.IDataBuffer) return (T)SetResource(Metadata.Buffers[name].Binding, resource);
            else throw new Exception();
        }




        private unsafe IBackendResourceReference SetResource(byte binding, IBackendResourceReference resource)
        {

            uint range = 0;

            bool dummy = resource == null;


            switch (Metadata.Declaration[binding].ResourceType)
            {
                case ResourceSetResourceType.Texture:

                    var samplerType = Metadata.TexturesIndexed[binding].Metadata.SamplerType;

                    resource ??= samplerType switch
                    {
                        TextureSamplerTypes.Sampler2D => Dummy2DTextureSamplerPair,
                        TextureSamplerTypes.Sampler2DShadow => Dummy2DShadowTextureSamplerPair,
                        TextureSamplerTypes.SamplerCubeMap => DummyCubeTextureSamplerPair,
                        TextureSamplerTypes.Sampler3D => Dummy3DTextureSamplerPair,
                        _ => throw new NotImplementedException(),
                    };
                    break;


                case ResourceSetResourceType.ConstantDataBuffer:
                case ResourceSetResourceType.ReadOnlyDataBuffer:
                case ResourceSetResourceType.ReadWriteDataBuffer:

                    var bufferMeta = Metadata.BuffersIndexed[binding].Metadata;

                    resource ??= (IBackendResourceReference) GetDummyBuffer(bufferMeta.SizeRequirement, bufferMeta.UsageFlags, bufferMeta.ReadWriteFlags);



                    range = bufferMeta.SizeRequirement;

                    break;


                default:
                    throw new NotImplementedException();
            }



            lock (this)
                Contents[binding] = (RefCounted)(dummy ? null : resource);  //<-- avoid exposing dummy resources




            var write = new ResourceSetResourceBind(binding, resource, range);

            PushDeferredPreRenderThreadCommand((this.GetRef(), write), &Execute);


            return resource;


            static void Execute( (WeakObjRef<BackendResourceSetReference> Target, ResourceSetResourceBind Bind)* ptr)
            {
                CheckOutsideOfRendering();
                Backend.WriteToResourceSet(ptr->Target.Dereference(), ptr->Bind);
            }
        }



        public static BackendResourceSetReference CreateFromMetadata(string SetName)
            => CreateFromMetadata(GlobalResourceSetMetadata[SetName]);


        public static BackendResourceSetReference CreateFromMetadata(BackendShaderReference Shader, string SetName)
            => CreateFromMetadata(Shader.Metadata.ResourceSets[SetName].Metadata);



        /// <summary>
        /// Creates a <see cref="BackendResourceSetReference"/> according to <paramref name="Metadata"/>'s spec.
        /// </summary>
        /// <param name="Metadata"></param>
        /// <returns></returns>
        /// <exception cref="Exception"></exception>
        public static BackendResourceSetReference CreateFromMetadata(ShaderMetadata.ShaderResourceSetMetadata Metadata)
        {
            var inst = new BackendResourceSetReference(Metadata, Backend.CreateResourceSet(Metadata.Declaration.AsSpan()));

            for (byte i = 0; i < Metadata.Declaration.Length; i++)
                inst.SetResource(i, null);

            return inst;
        }


        public T GetResource<T>(string name) where T : IBackendResourceReference
        {
            lock (this)
            {
                if (typeof(T) == typeof(BackendTextureAndSamplerReferencesPair)) return (T)(object)Contents[Metadata.Textures[name].Binding];
                else if (typeof(T).IsAssignableTo(typeof(BackendBufferReference.IDataBuffer))) return (T)(object)Contents[Metadata.Buffers[name].Binding];
                else throw new NotImplementedException();
            }
        }

        public T GetResource<T>(uint idx) where T : IBackendResourceReference
        {
            lock (this)
                return (T)(object)Contents[(int)idx];
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

            base.OnFree();
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
            base.OnFree();
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




    public class BackendTextureAndSamplerReferencesPair : RefCounted, IBackendResourceReference
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



    public class BackendTextureAndSamplerReferencesPairsArray(BackendTextureAndSamplerReferencesPair[] array) : RefCounted, IBackendResourceReference
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

            base.OnFree();
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

            base.OnFree();
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



        public record class ShaderDataBufferMetadata(
            BufferUsageFlags UsageFlags,
            ReadWriteFlags ReadWriteFlags,

            uint SizeRequirement,  // 0 = unsized

            FrozenDictionary<string, uint> FieldOffsets,
            ImmutableArray<ContiguousRegion> ContiguousRegions
        );



        public record class ShaderResourceSetMetadata(

            ImmutableArray<ResourceSetResourceDeclaration> Declaration,

            FrozenDictionary<string, (byte Binding, ShaderTextureMetadata Metadata)> Textures,
            FrozenDictionary<string, (byte Binding, ShaderDataBufferMetadata Metadata)> Buffers,

            FrozenDictionary<byte, (string Name, ShaderTextureMetadata Metadata)> TexturesIndexed,
            FrozenDictionary<byte, (string Name, ShaderDataBufferMetadata Metadata)> BuffersIndexed

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
        uint srcIndex = 0; 

        for (int i = 0; i < regions.Length && srcIndex < size; i++)
        {
            var region = regions[i];

            ulong dstStart = region.start + offset;
            ulong regionLength = region.end - region.start;

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

            base.OnFree();
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

                    spec.Shader.Dereference().OnFreeEvent.Add(get.Free);

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

        public WeakObjRef<BackendShaderReference> Shader;
        public WeakObjRef<BackendFrameBufferPipelineReference> FrameBufferPipeline;


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

            base.OnFree();
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
            ConstantDataBuffer,
            ReadOnlyDataBuffer,
            ReadWriteDataBuffer,

            Texture
        }
    }



    public struct ResourceSetResourceBind
    {
        public uint Binding;
        public uint Range;
        public WeakObjRef<IBackendResourceReference> ResourceHandle;

        public ResourceSetResourceBind(uint binding, WeakObjRef<IBackendResourceReference> resource, uint range = 0)
        {
            Binding = binding;
            ResourceHandle = resource;
            Range = range;
        }

        public ResourceSetResourceBind(uint binding, IBackendResourceReference resource, uint range = 0)
        {
            Binding = binding;
            ResourceHandle = resource.GetRef();
            Range = range;
        }
    }






    public static BackendFrameBufferObjectReference ActiveFrameBufferObject { get; private set; }
    public static BackendFrameBufferPipelineReference ActiveFramebufferPipeline { get; private set; }
    public static byte ActiveFrameBufferPipelineStage { get; private set; }





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

        lock (AllBackendReferences)
        {
            foreach (var res in AllBackendReferences)
                res.Free();
        }

        Backend.Destroy();
    }


    /// <summary>
    /// <inheritdoc cref="_callonanythreadoutsideofrendering"/>
    /// </summary>
    /// <param name="UseHDR"></param>
    public static void ConfigureSwapchain(Vector2<uint> Size, bool UseHDR)
    {
        CheckOutsideOfRendering();
        CurrentSwapchainDetails = Backend.ConfigureSwapchain(Size, UseHDR);
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
    public static void AdvanceFrameBufferPipeline(BackendFrameBufferObjectReference fbo, BackendFrameBufferPipelineReference pipeline)
    {
        CheckDuringRendering();
        Backend.AdvanceFrameBufferPipeline(fbo, pipeline, ActiveFrameBufferPipelineStage++);
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
    public static void Draw(ReadOnlySpan<VertexAttributeDefinitionBufferPair.Struct> buffers, ReadOnlySpan<WeakObjRef<BackendResourceSetReference>> ResourceSets, BackendDrawPipelineReference pipeline, BackendBufferReference.IIndexBuffer indexbuffer, uint indexBufferOffset, IndexingDetails indexing)
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
        public unsafe object CreateBuffer(uint length, void* initialContent, BufferUsageFlags usageFlags, ReadWriteFlags readWriteFlags);

        public unsafe void WriteToBuffer(BackendBufferReference buffer, WriteRange write);

        public void DestroyBuffer(BackendBufferReference buffer);






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
        public void WriteToResourceSet(BackendResourceSetReference set, ResourceSetResourceBind write);
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
        public void Draw(ReadOnlySpan<VertexAttributeDefinitionBufferPair.Struct> buffers, ReadOnlySpan<WeakObjRef<BackendResourceSetReference>> ResourceSets, BackendDrawPipelineReference pipeline, BackendBufferReference.IIndexBuffer indexbuffer, uint indexBufferOffset, IndexingDetails indexing);



        // ---------------- FrameBuffer Manipulation ----------------
        public void ClearFramebufferDepthStencil(BackendFrameBufferObjectReference framebuffer, byte CubemapFaceIfCubemap = 0);
        public void ClearFramebufferColorAttachment(BackendFrameBufferObjectReference framebuffer, Vector4 color, byte idx = 0, byte CubemapFaceIfCubemap = 0);
        public void SetScissor(Vector2<uint> offset, Vector2<uint> size);
    }


}