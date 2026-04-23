


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
using static Engine.Core.References;
using static EngineMath;
using static RenderingBackend.ResourceSetResourceDeclaration;
using static RenderThread;





/// <summary>
/// Provides near direct immediate access to the current rendering backend. Also see <seealso cref="Rendering"/> and/or <seealso cref="IDeferredCommand{TSelf}"/>
/// </summary>
public static partial class RenderingBackend
{




    public static SDL3.SDL.WindowFlags GetSDLWindowFlagsForBackend(RenderingBackendEnum Backend)
        => RenderingBackendData[Backend.ToString()].Flags;





    public static void CreateBackend(RenderingBackendEnum backend, nint sdlwindow)
    {
        var get = RenderingBackendData[backend.ToString()];

        Backend = get.Constructor.Invoke(sdlwindow);
        CurrentBackend = backend;



#if RELEASE 
        foreach (var kv in ShaderSources[backend])
            Shaders[kv.Key] = BackendShaderReference.Create(kv.Key, kv.Value);

        foreach (var kv in ComputeShaderSources[backend])
            ComputeShaders[kv.Key] = BackendComputeShaderReference.Create(kv.Key, kv.Value);
#endif


        ConfigureSwapchain(Window.GetWindowClientArea(), EngineSettings.HDR);

    }









    /// <summary>
    /// Represents a real gpu-backed resource.
    /// </summary>
    public abstract class BackendReference : Freeable
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
                AllBackendReferences.Add(this.GetWeakRef());
        }

        protected override void OnFree()
        {
            lock (AllBackendReferences)
                AllBackendReferences.Remove(this.GetWeakRef());
        }

    }

    private static readonly HashSet<WeakObjRef<BackendReference>> AllBackendReferences = new();













#if DEBUG
    public static readonly Dictionary<string, ShaderMetadata.ShaderResourceSetMetadata> GlobalResourceSetMetadata = new();
#endif









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
                BufRef = Buf.GetWeakRef();
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






    public static UnmanagedKeyValueCollection<WeakObjRef<string>, VertexAttributeDefinitionBufferPair.Struct> VertexAttributesToUnmanaged(this Dictionary<string, VertexAttributeDefinitionBufferPair> dict)
    {
        var basecol = dict.ToUnmanagedKV();

        var attrs = new UnmanagedKeyValueCollection<WeakObjRef<string>, VertexAttributeDefinitionBufferPair.Struct>();

        ref var kvs = ref basecol.KeyValuePairs;

        for (int i = 0; i < basecol.Count; i++)
        {
            ref var get = ref kvs[i];
            attrs.KeyValuePairs[attrs.Count++] = new(get.Key, get.Value.Dereference().GetStruct());
        }


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




    public unsafe record struct IndexingDetails(uint Start, uint End, uint BaseVertex, uint InstanceCount, IndexBufferFormat IndexBufferFormat);













    /// <summary>
    /// Directly represents a gpu-side buffer.
    /// <br/> Given a buffer supports one or more usages, it can be explicitly cast to one or more corresponding interfaces.
    /// <br/> For example, given a buffer was created with <see cref="BufferUsageFlags.Vertex"/>, it can be cast to <see cref="IVertexBuffer"/>.
    /// </summary>
    public unsafe class BackendBufferReference : BackendReference, IPlaceholderProvider<BackendBufferReference, (uint size, BufferUsageFlags usageFlags, ReadWriteFlags accessFlags)>
    {

        private bool Dummy;
        public bool IsPlaceholder => Dummy;



        private static readonly List<BackendBufferReference> DummyBuffers = new();

        public static BackendBufferReference GetPlaceholder((uint size, BufferUsageFlags usageFlags, ReadWriteFlags accessFlags) requirement)
        {
            lock (DummyBuffers)
            {
                for (int i = 0; i < DummyBuffers.Count; i++)
                {
                    var get = DummyBuffers[i];
                    if (get.Size > requirement.size)
                        return get;
                }

                var ret = Create(requirement.size, requirement.usageFlags, requirement.accessFlags);

                ret.Dummy = true;

                DummyBuffers.Add(ret);

                return ret;
            }
        }






        /// <summary>
        /// A <see cref="BackendBufferReference"/> created with <see cref="BufferUsageFlags.Vertex"/>. Should never be implemented elsewhere.
        /// </summary>
        public interface IVertexBuffer
        {
            public unsafe void Write(WriteRange write, bool necessary);
            public unsafe void Write<T>(T write, uint offset, bool necessary) where T : unmanaged;
            public unsafe void Write<T>(ReadOnlySpan<T> write, uint offset, bool necessary) where T : unmanaged;
        }


        /// <summary>
        /// A <see cref="BackendBufferReference"/> created with <see cref="BufferUsageFlags.Index"/>. Should never be implemented elsewhere.
        /// </summary>
        public interface IIndexBuffer
        {
            public unsafe void Write(WriteRange write, bool necessary);
            public unsafe void Write<T>(T write, uint offset, bool necessary) where T : unmanaged;
            public unsafe void Write<T>(ReadOnlySpan<T> write, uint offset, bool necessary) where T : unmanaged;
        }



        /// <summary>
        /// A <see cref="BackendBufferReference"/> created with <see cref="BufferUsageFlags.Uniform"/> or <see cref="BufferUsageFlags.Storage"/>. Should never be implemented elsewhere.
        /// </summary>
        public unsafe interface IDataBuffer : IBackendResourceReference
        {
            public ShaderMetadata.ShaderDataBufferMetadata Metadata { get; set; }



            public unsafe void WriteFromOffsetOf<T>(string MemberName, T value, bool necessary, bool skipPadding) where T : unmanaged;
            public unsafe void WriteFromOffsetOf<T>(string MemberName, ReadOnlySpan<T> value, bool necessary, bool skipPadding) where T : unmanaged;


            public unsafe void WriteFromOffsetOfArrayElement<T>(string ArrayName, uint index, T value, bool necessary, bool skipPadding) where T : unmanaged;
            public unsafe void WriteFromOffsetOfArrayElement<T>(string ArrayName, uint index, ReadOnlySpan<T> value, bool necessary, bool skipPadding) where T : unmanaged;



            public unsafe void WriteAndSkipPadding(WriteRange write, bool necessary);



            public unsafe void Write(WriteRange write, bool necessary);
            public unsafe void Write<T>(T write, uint offset, bool necessary) where T : unmanaged;
            public unsafe void Write<T>(ReadOnlySpan<T> write, uint offset, bool necessary) where T : unmanaged;
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
            Kernel.ReleaseVram(Size);

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


            Kernel.TryAllocateVram(size);


            if (!initialContent.IsEmpty)
                fixed (void* p = initialContent)
                    backendRef = Backend.CreateBuffer(size, p, usageFlags, accessFlags);

            else
                backendRef = Backend.CreateBuffer(size, null, usageFlags, accessFlags);



            if (usageFlags.EnumHasValues([BufferUsageFlags.Vertex, BufferUsageFlags.Storage]))
                return new BackendBufferReference_VertexANDStorage(size, usageFlags, accessFlags, metadata, backendRef);

            if (usageFlags.EnumHasValues([BufferUsageFlags.Vertex]))
                return new BackendBufferReference_Vertex(size, usageFlags, accessFlags, backendRef);

            if (usageFlags.EnumHasValues([BufferUsageFlags.Index, BufferUsageFlags.Storage]))
                return new BackendBufferReference_IndexANDStorage(size, usageFlags, accessFlags, metadata, backendRef);

            if (usageFlags.EnumHasValues([BufferUsageFlags.Index]))
                return new BackendBufferReference_Index(size, usageFlags, accessFlags, backendRef);

            if (usageFlags.EnumHasValue(BufferUsageFlags.Storage) ^ usageFlags.EnumHasValue(BufferUsageFlags.Uniform))
                return new BackendBufferReference_Data(size, usageFlags, accessFlags, metadata, backendRef);


            throw new NotImplementedException(
#if DEBUG
                "Invalid buffer usage combination"
#endif
                );
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
            private void AssertHasMetadata()
            {
                if (this.IsPlaceholder)
                    throw new InvalidOperationException($"This buffer is a dummy buffer and shouldn't be written to.");


                if (Metadata == null)
                    throw new InvalidOperationException($"This data buffer has no assigned metadata and cannot be written into with '{nameof(IDataBuffer)}' methods.");
            }



            public ShaderMetadata.ShaderDataBufferMetadata Metadata { get; set; } = metadata;



            [DebuggerHidden]
            [StackTraceHidden]
            public void WriteFromOffsetOf<ValueT>(string fieldName, ValueT val, bool necessary, bool skipPadding = true) where ValueT : unmanaged
                => WriteFromOffsetOf(fieldName, (uint)sizeof(ValueT), &val, necessary, skipPadding);



            [DebuggerHidden]
            [StackTraceHidden]
            public void WriteFromOffsetOf<ValueT>(string fieldName, ReadOnlySpan<ValueT> val, bool necessary, bool skipPadding = true) where ValueT : unmanaged
            {
                fixed (ValueT* p = val)
                    WriteFromOffsetOf(fieldName, (uint)sizeof(ValueT), p, necessary, skipPadding);
            }




            [DebuggerHidden]
            [StackTraceHidden]
            public unsafe void WriteFromOffsetOfArrayElement<ValueT>(string ArrayName, uint index, ValueT value, bool necessary, bool skipPadding) where ValueT : unmanaged
            {
                var lookup = (ShaderMetadata.ShaderDataBufferMetadata.ArrayInfo)Metadata.Members[ArrayName];

                var wr = new WriteRange(
                    lookup.RelativeOffset + (lookup.BaseMemberInfo.PaddedSize * index),
                    (uint)(sizeof(ValueT)),
                    &value);


                if (skipPadding)
                    WriteAndSkipPadding(wr, necessary);
                else
                    Write(wr, necessary);
            }


            [DebuggerHidden]
            [StackTraceHidden]
            public unsafe void WriteFromOffsetOfArrayElement<ValueT>(string ArrayName, uint index, ReadOnlySpan<ValueT> val, bool necessary, bool skipPadding) where ValueT : unmanaged
            {
                var lookup = (ShaderMetadata.ShaderDataBufferMetadata.ArrayInfo)Metadata.Members[ArrayName];

                fixed (ValueT* p = val)
                {
                    var wr = new WriteRange(
                        lookup.RelativeOffset + (lookup.BaseMemberInfo.PaddedSize * index),
                        (uint)(sizeof(ValueT)*val.Length),
                        p);

                    if (skipPadding)
                        WriteAndSkipPadding(wr, necessary);
                    else
                        Write(wr, necessary);
                }
            }








            [DebuggerHidden]
            [StackTraceHidden]
            private void WriteFromOffsetOf(string fieldName, uint dataSize, void* dataPtr, bool necessary, bool skipPadding)
            {
                AssertHasMetadata();

                var memberInfo = Metadata.Members[fieldName];
                var wr = new WriteRange(memberInfo.RelativeOffset, dataSize, dataPtr);

                if (skipPadding)
                    WriteAndSkipPadding(wr, necessary);
                else
                    Write(wr, necessary); 
            }




            public unsafe void WriteAndSkipPadding(WriteRange write, bool necessary)
            {
                AssertHasMetadata();

                byte* srcPtr = write.Src;
                uint remaining = write.LengthOfSrc;

                ProcessMembers(
                    Metadata.MembersIndexed,
                    basePhysicalOffset: 0,
                    writeStartPhysicalOffset: write.OffsetIntoDst,
                    ref srcPtr,
                    ref remaining,
                    necessary);
            }



            private unsafe void ProcessMembers(
                ImmutableArray<(string Name, ShaderMetadata.ShaderDataBufferMetadata.MemberInfo Info)> members,
                uint basePhysicalOffset,
                uint writeStartPhysicalOffset,
                ref byte* srcPtr,
                ref uint remaining,
                bool necessary)
            {
                for (int i = 0; i < members.Length && remaining > 0; i++)
                {
                    var member = members[i].Info;
                    ProcessMember(
                        member,
                        basePhysicalOffset + member.RelativeOffset,
                        writeStartPhysicalOffset,
                        ref srcPtr,
                        ref remaining,
                        necessary);
                }
            }



            private unsafe void ProcessMember(
                ShaderMetadata.ShaderDataBufferMetadata.MemberInfo member,
                uint memberBase,
                uint writeStartPhysicalOffset,
                ref byte* srcPtr,
                ref uint remaining,
                bool necessary)
            {


                if (remaining == 0)
                    return;


                uint memberPhysicalEnd = memberBase + member.PaddedSize;



                if (memberPhysicalEnd <= writeStartPhysicalOffset)
                    return;




                // ---------------- PRIMITIVE ----------------
                if (member is ShaderMetadata.ShaderDataBufferMetadata.PrimitiveInfo primitive)
                {
                    uint write_start = Math.Max(writeStartPhysicalOffset, memberBase);
                    uint write_size = Math.Min(primitive.RealSize, remaining);

                    if (write_size == 0)
                        return;

                    Write(new WriteRange(write_start, write_size, srcPtr), necessary);

                    srcPtr += write_size;
                    remaining -= write_size;

                    return;
                }




                // ---------------- STRUCT ----------------
                if (member is ShaderMetadata.ShaderDataBufferMetadata.StructInfo str)
                {
                    ProcessMembers(
                        str.MembersIndexed,
                        memberBase,
                        writeStartPhysicalOffset,
                        ref srcPtr,
                        ref remaining,
                        necessary);
                    return;
                }




                // ---------------- ARRAY ----------------
                if (member is ShaderMetadata.ShaderDataBufferMetadata.ArrayInfo arr)
                {
                    uint count = arr.Length == 0 ? uint.MaxValue : arr.Length;
                    uint stride = arr.BaseMemberInfo.PaddedSize;


                    for (uint i = 0; i < count && remaining > 0; i++)
                    {

                        uint elementBase = memberBase + (i * stride);
                        uint elementEnd = elementBase + stride;

                        if (elementEnd <= writeStartPhysicalOffset)
                            continue; 


                        ProcessMember(
                            arr.BaseMemberInfo,
                            elementBase,
                            writeStartPhysicalOffset,
                            ref srcPtr,
                            ref remaining,
                            necessary);
                    }
                    return;
                }
            }
        }





        [DebuggerHidden]
        [StackTraceHidden]
        public unsafe void Write<T>(T write, uint offset, bool necessary) where T : unmanaged
            => Write(new WriteRange(offset, (uint)sizeof(T), &write), necessary);



        [DebuggerHidden]
        [StackTraceHidden]
        public unsafe void Write<T>(ReadOnlySpan<T> write, uint offset, bool necessary) where T : unmanaged
        {
            fixed (void* ptr = write)
                Write(new WriteRange(offset, (uint)(write.Length*sizeof(T)), ptr), necessary);
        }




        [DebuggerHidden]
        [StackTraceHidden]
        public unsafe void Write(WriteRange write, bool necessary)
        {

#if DEBUG
            if (!AccessFlags.EnumHasValue(ReadWriteFlags.CPUWrite)) 
                throw new InvalidOperationException($"This buffer wasn't created with {nameof(ReadWriteFlags.CPUWrite)}");

            if (write.LengthOfSrc == 0 || write.Src == null)
                throw new InvalidOperationException($"Null/empty write given");

            if (write.LengthOfSrc + write.OffsetIntoDst > Size)
                throw new InvalidOperationException($"Out-of-bounds write given:\nWrite Length: {write.LengthOfSrc}\nWrite Offset: {write.OffsetIntoDst}\nBuffer Size: {Size}");
#endif



            var alloc 
                = necessary 
                ? AllocatenecessaryRenderTemporaryUnmanaged((int)write.LengthOfSrc)
                : AllocateRenderTemporaryUnmanaged((int)write.LengthOfSrc);



            Unsafe.CopyBlockUnaligned(alloc, (byte*)write.Src, write.LengthOfSrc);

            var cmddata = (new WriteRange(write.OffsetIntoDst, write.LengthOfSrc, alloc), this.GetWeakRef());


            if (necessary)
                PushDeferrednecessaryPreRenderThreadCommand(cmddata, &Execute);
            else
                PushDeferredPreRenderThreadCommand(cmddata, &Execute);


            static void Execute( (WriteRange wr, WeakObjRef<BackendBufferReference> bufhandle)* arg )
                => Backend.WriteToBuffer(arg->bufhandle.Dereference(), arg->wr);

        }

    }

















    /// <summary>
    /// Any type that <see cref="BackendResourceSetReference"/> can accept as a resource.
    /// </summary>
    public interface IBackendResourceReference;




    /// <summary>
    /// A collection of <see cref="IBackendResourceReference"/>s that shaders can access.
    /// </summary>
    public class BackendResourceSetReference : BackendReference
    {

        public readonly ShaderMetadata.ShaderResourceSetMetadata Metadata;


        public record struct ResourceSetInternalBinding(string Name, object Binding);



        private readonly ResourceSetInternalBinding[] Contents;

        public ReadOnlySpan<ResourceSetInternalBinding> GetContents() => Contents.AsSpan();

        public uint ResourceCount => (uint)Metadata.Declaration.Length;





        private BackendResourceSetReference(ShaderMetadata.ShaderResourceSetMetadata metadata, object backendResource) : base(backendResource)
        {
            Metadata = metadata;


            Contents = new ResourceSetInternalBinding[ResourceCount];

            foreach (var kv in metadata.Buffers) 
                Contents[kv.Value.Binding] = new(kv.Key, null);

            foreach (var kv in metadata.Textures) 
                Contents[kv.Value.Binding] = new(kv.Key, kv.Value.Metadata.ArrayLength <= 1 ? null : new IBackendResourceReference[kv.Value.Metadata.ArrayLength]);

        }


        protected override void OnFree()
        {
            Backend.DestroyResourceSet(this); 

            base.OnFree();
        }




        public unsafe void SetResource<T>(string name, T resource, uint? indexOfArray = default) where T : IBackendResourceReference
        {
            lock (this)
            {
                if (resource is IBackendTextureSamplerPair || resource is BackendTexture2DMSAttachmentReference) SetResource(Metadata.Textures[name].Binding, resource, indexOfArray ?? 0);
                else if (resource is BackendBufferReference.IDataBuffer) SetResource(Metadata.Buffers[name].Binding, resource, indexOfArray ?? 0);
                else throw new NotImplementedException();
            }
        }


        public T GetResource<T>(string name, uint? indexOfArray = default) where T : IBackendResourceReference
        {
            lock (this)
            {
                int binding = 0;

                if (typeof(T).IsAssignableTo(typeof(IBackendTextureSamplerPair)) || typeof(T) == typeof(BackendTexture2DMSAttachmentReference)) binding = Metadata.Textures[name].Binding;
                else if (typeof(T).IsAssignableTo(typeof(BackendBufferReference.IDataBuffer))) binding = Metadata.Buffers[name].Binding;
                else throw new NotImplementedException();

                var get = Contents[binding];
                if (get.Binding is IBackendResourceReference[] arr)
                    return (T)(object)arr[indexOfArray ?? 0];


                return (T)get.Binding;

            }
        }




        private unsafe void SetResource(byte binding, IBackendResourceReference resource, uint idx, bool force = false)
        {

            uint range = 0;

            bool dummy = resource == null;


            switch (Metadata.Declaration[binding].ResourceType)
            {
                case ResourceSetResourceType.Texture:

                    var samplerType = Metadata.TexturesIndexed[binding].Metadata.SamplerType;

                    resource ??= samplerType switch
                    {
                        TextureSamplerTypes.Sampler2D => BackendTexture2DSamplerPair.GetPlaceholder(),
                        TextureSamplerTypes.Sampler2DMS => BackendTexture2DMSAttachmentReference.GetPlaceholder(),
                        TextureSamplerTypes.Sampler2DShadow => BackendTexture2DShadowSamplerPair.GetPlaceholder(),
                        TextureSamplerTypes.SamplerCubeMap => BackendTextureCubeMapSamplerPair.GetPlaceholder(),
                        TextureSamplerTypes.Sampler3D => BackendTexture3DSamplerPair.GetPlaceholder(),
                        _ => throw new NotImplementedException(),
                    };

                    break;




                case ResourceSetResourceType.ConstantDataBuffer:
                case ResourceSetResourceType.ReadOnlyDataBuffer:
                case ResourceSetResourceType.ReadWriteDataBuffer:

                    var bufferMeta = Metadata.BuffersIndexed[binding].Metadata;

                    resource ??= (IBackendResourceReference) BackendBufferReference.GetPlaceholder((bufferMeta.SizeRequirement, bufferMeta.UsageFlags, bufferMeta.ReadWriteFlags));

                    range = bufferMeta.SizeRequirement;

                    break;


                default:
                    throw new NotImplementedException();
            }




            var final = dummy ? null : resource;

            bool changed = false;
            lock (this)
            {
                ref var existing = ref Contents[binding];

                if (existing.Binding is IBackendResourceReference[] resArr)
                {
                    if (resArr[idx] != final)
                    {
                        resArr[idx] = final;
                        changed = true;
                    }
                }
                else if (existing.Binding != final)
                {
                    existing.Binding = final;
                    changed = true;
                }
            }


            if (changed || force)
            {

                PushDeferrednecessaryPreRenderThreadCommand((this.GetWeakRef(), binding, idx, resource.GetWeakRef(), range), &Execute);


                static void Execute((WeakObjRef<BackendResourceSetReference> Target, byte bind, uint idx, WeakObjRef<IBackendResourceReference> resource, uint range)* ptr)
                {
                    var set = ptr->Target.Dereference();
                    var resource = ptr->resource.Dereference();

                    if (set == null || resource == null) return;

                    CheckOutsideOfRendering();
                    Backend.WriteToResourceSet(set, new ResourceSetResourceBind(ptr->bind, ptr->idx, resource, ptr->range));
                }
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
                for (byte e = 0; e < Metadata.Declaration[i].ArrayLength; e++)
                    inst.SetResource(i, null, e, true);

            return inst;
        }



    }














    /// <summary>
    /// Wraps a byte index + data array and offers validation methods.
    /// </summary>
    /// <param name="Index"></param>
    /// <param name="Data"></param>
    public readonly record struct TextureMipData(byte Index, byte[] Data)
    {
        [Conditional("DEBUG")]
        public readonly void CheckValidForSize(uint SizeWH) => CheckValidForSize(new Vector3<uint>(SizeWH, SizeWH, 1));

        [Conditional("DEBUG")]
        public readonly void CheckValidForSize(Vector2<uint> SizeWH) => CheckValidForSize(new Vector3<uint>(SizeWH.X, SizeWH.Y, 1));

        [Conditional("DEBUG")]
        public readonly void CheckValidForSize(Vector3<uint> SizeWHD)
        {
            if (TextureMipCount.CalculateFromTextureSize(SizeWHD)-1 > Index)
                throw new Exception("Invalid mip data index");
        }
    }

    [Conditional("DEBUG")]
    public static void CheckTextureMipArrayValidForSize(this TextureMipData[] arr, uint SizeWH) => CheckTextureMipArrayValidForSize(arr, new Vector3<uint>(SizeWH, SizeWH, 1));

    [Conditional("DEBUG")]
    public static void CheckTextureMipArrayValidForSize(this TextureMipData[] arr, Vector2<uint> SizeWH) => CheckTextureMipArrayValidForSize(arr, new Vector3<uint>(SizeWH.X, SizeWH.Y, 1));


    [Conditional("DEBUG")]
    public static void CheckTextureMipArrayValidForSize(this TextureMipData[] arr, Vector3<uint> size)
    {
        if (arr == null)
            return;


        byte mipCount = TextureMipCount.CalculateFromTextureSize(size);

        Span<bool> seen = stackalloc bool[mipCount];


        for (int i = 0; i < arr.Length; i++)
        {
            var mip = arr[i];

            if (mip.Index >= mipCount)
                throw new Exception($"Mip index {mip.Index} out of range (max {mipCount - 1})");

            if (seen[mip.Index])
                throw new Exception($"Duplicate mip index {mip.Index}");

            seen[mip.Index] = true;

            if (mip.Data == null)
                throw new Exception($"Mip {mip.Index} has null data");
        }
    }








    /// <summary>
    /// Wraps a byte and offers calculation/validation methods.
    /// </summary>
    /// <param name="Count"></param>
    public readonly record struct TextureMipCount(byte Count)
    {
        public static TextureMipCount CalculateFromTextureSize(uint SizeWH) => CalculateFromTextureSize(new Vector3<uint>(SizeWH, SizeWH, 1));
        public static TextureMipCount CalculateFromTextureSize(Vector2<uint> SizeWH) => CalculateFromTextureSize(new Vector3<uint>(SizeWH.X, SizeWH.Y, 1));
        public static TextureMipCount CalculateFromTextureSize(Vector3<uint> SizeWHD)
            => new TextureMipCount((byte)(BitOperations.Log2(Math.Max(Math.Max(SizeWHD.X, SizeWHD.Y), SizeWHD.Z)) + 1));


        public static implicit operator byte(TextureMipCount a) => a.Count;
        public static implicit operator TextureMipCount(byte a) => new(a);



        [Conditional("DEBUG")]
        public readonly void CheckValidForSize(uint SizeWH) => CheckValidForSize(new Vector3<uint>(SizeWH, SizeWH, 1));

        [Conditional("DEBUG")]
        public readonly void CheckValidForSize(Vector2<uint> SizeWH) => CheckValidForSize(new Vector3<uint>(SizeWH.X, SizeWH.Y, 1));

        [Conditional("DEBUG")]
        public readonly void CheckValidForSize(Vector3<uint> SizeWHD)
        {
            var max = CalculateFromTextureSize(SizeWHD);

            if (Count == 0)
                throw new Exception("Count is zero");

            if (Count > max)
                throw new Exception($"Count ({Count}) exceeds max mip count ({max})");
        }
    }










    public abstract class BackendTextureReference : BackendReference
    {

        public readonly Vector3<uint> Size;
        public readonly TextureFormats TextureFormat;
        public readonly TextureMipCount MipCount;

        public readonly uint FullSizeInMemory;

        public readonly bool FramebufferCompatible;
        public readonly MultiSampleCount MultiSampleCount;


        protected BackendTextureReference(object backendRef, Vector3<uint> size, TextureFormats textureFormat, TextureMipCount mipCount, uint fullSizeInMemory, bool framebufferCompatible, MultiSampleCount framebufferSampleCount) : base(backendRef)
        {
            Size=size;
            TextureFormat=textureFormat;
            MipCount=mipCount;
            FullSizeInMemory=fullSizeInMemory;
            FramebufferCompatible=framebufferCompatible;
            MultiSampleCount=framebufferSampleCount;
        }




        protected static (object backendObj, uint memReq) Create(Vector3<uint> Size,
                                                                       TextureTypes type,
                                                                       TextureFormats format,

                                                                       TextureMipCount MipCount,
                                                                       TextureMipData[] Mips = null,

                                                                       bool FramebufferAttachmentCompatible = false,
                                                                       MultiSampleCount FramebufferSampleCount = 0)
        {


#if DEBUG
            if ((Size.X == 0 || Size.Y == 0 || Size.Z == 0) || (Size.X > 8192 || Size.Y > 8192 || Size.Z > 8192))
                throw new Exception("Invalid size");

            if (type == TextureTypes.TextureCubeMap && Size.X != Size.Y)
                throw new Exception();


            MipCount.CheckValidForSize(Size);
            Mips.CheckTextureMipArrayValidForSize(Size);
#endif




            if (Mips != default)
            {
                ulong totalSize = 0;
                TextureMipData[] convertedMips = new TextureMipData[Mips.Length];

                ulong[] mipOffsets = new ulong[Mips.Length];

                for (byte mip = 0; mip < Mips.Length; mip++)
                {
                    byte[] mipData = Mips[mip].Data;

                    if (format == TextureFormats.RGB8_UNORM)
                        mipData = ConvertRGB8ToRGBA8(mipData, uint.Max(1, Size.X >> mip), uint.Max(1, Size.Y >> mip));
                    else if (format == TextureFormats.RGB16_SFLOAT)
                        mipData = ConvertRGB16ToRGBA16(mipData, uint.Max(1, Size.X >> mip), uint.Max(1, Size.Y >> mip));

                    convertedMips[mip] = new(Mips[mip].Index, mipData);

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



            uint memReq = GetTextureSizeBytes(new(Size.X, Size.Y, 1), format, MipCount);


            Kernel.TryAllocateVram(memReq);


            return (Backend.CreateTexture(new(Size.X, Size.Y, 1), type, format, FramebufferAttachmentCompatible, MipCount, Mips, FramebufferSampleCount), memReq);




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
            Backend.DestroyTexture(this);
            Kernel.ReleaseVram(FullSizeInMemory);

            base.OnFree();
        }

    }





    /// <summary>
    /// Represents some part of a <see cref="BackendTextureReference"/> which can be used as a framebuffer attachment.
    /// </summary>
    public interface IFramebufferAttachment;


    /// <summary>
    /// A standard 2D texture.
    /// </summary>
    public class BackendTexture2DReference : BackendTextureReference, IFramebufferAttachment
    {

        private BackendTexture2DReference(object backendRef,
                                          Vector2<uint> size,
                                          TextureFormats textureFormat,
                                          TextureMipCount mipCount,
                                          uint fullSizeInMemory,
                                          bool attachment) : base(backendRef, new(size.X, size.Y, 1), textureFormat, mipCount, fullSizeInMemory, attachment, 0)
        { 
        }


        public static BackendTexture2DReference Create(Vector2<uint> Size, TextureFormats Format, TextureMipCount MipCount, TextureMipData[] Mips = null)
        {
            var inst = Create(new Vector3<uint>(Size.X, Size.Y, 1), TextureTypes.Texture2D, Format, MipCount, Mips, false, default);
            return new(inst.backendObj, Size, Format, MipCount, inst.memReq, false);
        }


        public static BackendTexture2DReference CreateAttachment(Vector2<uint> Size, TextureFormats Format, TextureMipCount MipCount)
        {
            var inst = Create(new Vector3<uint>(Size.X, Size.Y, 1), TextureTypes.Texture2D, Format, MipCount, null, true, 0);
            return new(inst.backendObj, Size, Format, 1, inst.memReq, true);
        }

    }


    /// <summary>
    /// A multisampled 2D attachment texture.
    /// </summary>
    public class BackendTexture2DMSAttachmentReference : BackendTextureReference, IBackendResourceReference, IFramebufferAttachment, IPlaceholderProvider<BackendTexture2DMSAttachmentReference>
    {

        private static readonly BackendTexture2DMSAttachmentReference Placeholder = CreateAttachment(Vector2<uint>.One, TextureFormats.R8_UNORM, MultiSampleCount.Sample2);
        public bool IsPlaceholder => this == Placeholder;

        public static BackendTexture2DMSAttachmentReference GetPlaceholder() => Placeholder;


        private BackendTexture2DMSAttachmentReference(object backendRef,
                                          Vector2<uint> size,
                                          TextureFormats textureFormat,
                                          TextureMipCount mipCount,
                                          uint fullSizeInMemory,
                                          MultiSampleCount count) : base(backendRef, new(size.X, size.Y, 1), textureFormat, mipCount, fullSizeInMemory, true, count)
        {
        }

        public static BackendTexture2DMSAttachmentReference CreateAttachment(Vector2<uint> Size, TextureFormats Format, MultiSampleCount SampleCount)
        {
            var inst = Create(new Vector3<uint>(Size.X, Size.Y, 1), TextureTypes.Texture2D, Format, 1, null, true, SampleCount);
            return new(inst.backendObj, Size, Format, 1, inst.memReq, SampleCount);
        }

    }


    /// <summary>
    /// A standard 3D texture.
    /// </summary>
    public class BackendTexture3DReference : BackendTextureReference
    {

        private BackendTexture3DReference(object backendRef,
                                          Vector3<uint> size,
                                          TextureFormats textureFormat,
                                          TextureMipCount mipCount,
                                          uint fullSizeInMemory) : base(backendRef, size, textureFormat, mipCount, fullSizeInMemory, false, 0)
        {
        }



        public static BackendTexture3DReference Create(Vector3<uint> Size, TextureFormats Format, TextureMipCount MipCount, TextureMipData[] Mips = null)
        {
            var inst = Create(Size, TextureTypes.Texture3D, Format, MipCount, Mips, false, default);
            return new(inst.backendObj, Size, Format, MipCount, inst.memReq);
        }
    }


    /// <summary>
    /// A standard cube map texture.
    /// </summary>
    public class BackendTextureCubeMapReference : BackendTextureReference
    {

        private BackendTextureCubeMapReference(object backendRef,
                                          uint size,
                                          TextureFormats textureFormat,
                                          TextureMipCount mipCount,
                                          uint fullSizeInMemory) : base(backendRef, new Vector3<uint>(size,size,1), textureFormat, mipCount, fullSizeInMemory, false, 0)
        {
        }



        public static BackendTextureCubeMapReference Create(uint Size, TextureFormats Format, TextureMipCount MipCount, TextureMipData[] Mips = null)
        {
            var inst = Create(new Vector3<uint>(Size, Size, 1), TextureTypes.TextureCubeMap, Format, MipCount, Mips, false, default);
            return new(inst.backendObj, Size, Format, MipCount, inst.memReq);
        }
    }










    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public unsafe record struct SamplerDetails(TextureWrapModes WrapMode, TextureFilters MinFilter, TextureFilters MagFilter, TextureFilters MipmapFilter, bool EnableDepthComparison = false);



    public class BackendSamplerReference : BackendReference
    {
        public readonly SamplerDetails Details;

        private BackendSamplerReference(SamplerDetails details, object backendRef) : base(backendRef)
        {
            Details = details;
        }

        protected override void OnFree()
        {
            Backend.DestroyTextureSampler(this);
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




    public interface IBackendTextureSamplerPair : IBackendResourceReference
    {
        public BackendTextureReference Texture { get; }
        public BackendSamplerReference Sampler { get; }
    }




    public class BackendTexture2DSamplerPair(BackendTexture2DReference texture, TextureWrapModes WrapMode, TextureFilters MinFilter, TextureFilters MagFilter, TextureFilters MipmapFilter) : IBackendTextureSamplerPair, IPlaceholderProvider<BackendTexture2DSamplerPair>
    {
        public bool IsPlaceholder => this == Placeholder;


        public static BackendTexture2DSamplerPair GetPlaceholder() => Placeholder;


        private static readonly BackendTexture2DSamplerPair Placeholder
            = new(BackendTexture2DReference.Create(
                Vector2<uint>.One, TextureFormats.RGB8_UNORM, 1, [new TextureMipData(0, [255, 255, 255])]), 
                TextureWrapModes.Repeat, TextureFilters.Nearest, TextureFilters.Nearest, TextureFilters.Nearest
                );




        public BackendTextureReference Texture { get; } = texture;
        public BackendSamplerReference Sampler { get; } = BackendSamplerReference.Get(new SamplerDetails(WrapMode, MinFilter, MagFilter, MipmapFilter));
    }


    public class BackendTexture2DShadowSamplerPair(BackendTexture2DReference texture, TextureWrapModes WrapMode, TextureFilters MinFilter, TextureFilters MagFilter, TextureFilters MipmapFilter) : IBackendTextureSamplerPair, IPlaceholderProvider<BackendTexture2DShadowSamplerPair>
    {
        public bool IsPlaceholder => this == Placeholder;

        private static readonly BackendTexture2DShadowSamplerPair Placeholder 
            = new(BackendTexture2DReference.Create(
                Vector2<uint>.One, TextureFormats.Depth32, 1, [new TextureMipData(0, BitConverter.GetBytes(float.MaxValue))]),
                TextureWrapModes.Repeat, TextureFilters.Nearest, TextureFilters.Nearest, TextureFilters.Nearest
                );


        public static BackendTexture2DShadowSamplerPair GetPlaceholder() => Placeholder;



        public BackendTextureReference Texture { get; } = texture;
        public BackendSamplerReference Sampler { get; } = BackendSamplerReference.Get(new SamplerDetails(WrapMode, MinFilter, MagFilter, MipmapFilter, true));
    }


    public class BackendTexture3DSamplerPair(BackendTexture3DReference texture, TextureWrapModes WrapMode, TextureFilters MinFilter, TextureFilters MagFilter, TextureFilters MipmapFilter) : IBackendTextureSamplerPair, IPlaceholderProvider<BackendTexture3DSamplerPair>
    {
        public bool IsPlaceholder => this == Placeholder;


        public static BackendTexture3DSamplerPair GetPlaceholder() => Placeholder;


        private static readonly BackendTexture3DSamplerPair Placeholder
            = new(BackendTexture3DReference.Create(
                Vector3<uint>.One, TextureFormats.RGB8_UNORM, 1, [new TextureMipData(0, [255, 255, 255])]),
                TextureWrapModes.Repeat, TextureFilters.Nearest, TextureFilters.Nearest, TextureFilters.Nearest
                );



        public BackendTextureReference Texture { get; } = texture;
        public BackendSamplerReference Sampler { get; } = BackendSamplerReference.Get(new SamplerDetails(WrapMode, MinFilter, MagFilter, MipmapFilter));
    }



    public class BackendTextureCubeMapSamplerPair(BackendTextureCubeMapReference texture, TextureWrapModes WrapMode, TextureFilters MinFilter, TextureFilters MagFilter, TextureFilters MipmapFilter) : IBackendTextureSamplerPair, IPlaceholderProvider<BackendTextureCubeMapSamplerPair>
    {
        public bool IsPlaceholder => this == Placeholder;


        public static BackendTextureCubeMapSamplerPair GetPlaceholder() => Placeholder;


        private static readonly BackendTextureCubeMapSamplerPair Placeholder =
                new(
                    BackendTextureCubeMapReference.Create(1, TextureFormats.RGB8_UNORM, 1,
                        [
                            new TextureMipData(0, [255, 255, 255]),
                            new TextureMipData(0, [255, 255, 255]),
                            new TextureMipData(0, [255, 255, 255]),
                            new TextureMipData(0, [255, 255, 255]),
                            new TextureMipData(0, [255, 255, 255]),
                            new TextureMipData(0, [255, 255, 255])
                        ]
                    ),
                    TextureWrapModes.Repeat, TextureFilters.Nearest, TextureFilters.Nearest, TextureFilters.Nearest
                );



        public BackendTextureReference Texture { get; } = texture;
        public BackendSamplerReference Sampler { get; } = BackendSamplerReference.Get(new SamplerDetails(WrapMode, MinFilter, MagFilter, MipmapFilter));
    }










    private static readonly Dictionary<ShaderMetadata.ShaderResourceSetMetadata, BackendResourceSetReference> DummyResourceSets = new();

    private static unsafe ImmutableArray<BackendResourceSetReference> CreateDefaultResourceSets(FrozenDictionary<string, (byte Binding, ShaderMetadata.ShaderResourceSetMetadata Metadata)> ResourceSets)
    {

        var ResourceSetBinds = new BackendResourceSetReference[ResourceSets.Count];


        lock (DummyResourceSets)
            foreach (var resKV in ResourceSets)
            {
                if (!DummyResourceSets.TryGetValue(resKV.Value.Metadata, out BackendResourceSetReference set))
                    DummyResourceSets[resKV.Value.Metadata] = set = BackendResourceSetReference.CreateFromMetadata(resKV.Value.Metadata);

                ResourceSetBinds[resKV.Value.Binding] = set;
            }


        return ImmutableArray.ToImmutableArray(ResourceSetBinds);
    }









    private static Dictionary<string, BackendShaderReference> Shaders = new();
    private static Dictionary<string, BackendComputeShaderReference> ComputeShaders = new();



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
            Backend.DestroyShader(this);

            base.OnFree();
        }



        /// <summary>
        /// Compiles and uploads a shader under a given name, such that it can be fetched in future via <see cref="Get"/>.
        /// </summary>
        /// <param name="name"></param>
        /// <param name="src"></param>
        public static BackendShaderReference Create(string name, ShaderSource src)
        {
            var shader = new BackendShaderReference(src.Metadata, Backend.CreateShader(src));

            lock (Shaders)
            {
                if (Shaders.TryGetValue(name, out var get)) get.Free();
                Shaders[name] = shader;
            }

            return shader;
        }




        /// <summary>
        /// Fetches a preexisting precompiled <see cref="BackendShaderReference"/>. 
        /// </summary>
        /// <param name="name"></param>
        /// <returns></returns>
        /// <exception cref="Exception"></exception>
        public static BackendShaderReference Get(string name)
        {
            lock (Shaders)
                return Shaders.TryGetValue(name, out var get) ? get : null;
        }

    }






    public class BackendComputeShaderReference : BackendReference
    {
        public readonly ComputeShaderMetadata Metadata;

        public readonly ImmutableArray<BackendResourceSetReference> DefaultResourceSets;

        private BackendComputeShaderReference(ComputeShaderMetadata metadata, object backendRef) : base(backendRef)
        {
            Metadata = metadata;
            DefaultResourceSets = CreateDefaultResourceSets(metadata.ResourceSets);
        }

        protected override void OnFree()
        {
            Backend.DestroyComputeShader(this);

            base.OnFree();
        }





        /// <summary>
        /// Compiles and uploads a shader under a given name, such that it can be fetched in future via <see cref="Get"/>.
        /// </summary>
        /// <param name="name"></param>
        /// <param name="src"></param>
        public static BackendComputeShaderReference Create(string name, ComputeShaderSource src)
        {
            var shader = new BackendComputeShaderReference(src.Metadata, Backend.CreateComputeShader(src));


            lock (ComputeShaders)
            {
                if (ComputeShaders.TryGetValue(name, out var get)) get.Free();
                ComputeShaders[name] = shader;
            }

            return shader;
        }



        /// <summary>
        /// Fetches a preexisting precompiled <see cref="BackendComputeShaderReference"/>. 
        /// </summary>
        /// <param name="name"></param>
        /// <returns></returns>
        /// <exception cref="Exception"></exception>
        public static BackendComputeShaderReference Get(string name)
        {
            lock (ComputeShaders)
                return ComputeShaders.TryGetValue(name, out var get) ? get : null;

        }

    }

















    public record class ShaderMetadata(
        string Name,
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

            FrozenDictionary<string, ShaderDataBufferMetadata.MemberInfo> Members,
            ImmutableArray<(string Name, ShaderDataBufferMetadata.MemberInfo Info)> MembersIndexed
        )
        {


            public abstract record class MemberInfo(uint RelativeOffset, uint PaddedSize);

            public abstract record class ValueMemberInfo(uint RelativeOffset, uint PaddedSize, uint RealSize) : MemberInfo(RelativeOffset, PaddedSize);


            public record class PrimitiveInfo(uint RelativeOffset, uint PaddedSize, uint RealSize) : ValueMemberInfo(RelativeOffset, PaddedSize, RealSize);

            public record class StructInfo(
                    uint RelativeOffset,
                    uint PhysicalSize,
                    uint LogicalSize,
                    FrozenDictionary<string, MemberInfo> Members,
                    ImmutableArray<(string Name, MemberInfo Info)> MembersIndexed
                ) : ValueMemberInfo(RelativeOffset, PhysicalSize, LogicalSize)
            {
                public virtual bool Equals(StructInfo? other)
                {
                    if (ReferenceEquals(this, other))
                        return true;

                    if (other is null)
                        return false;

                    return
                        RelativeOffset == other.RelativeOffset &&
                        PaddedSize == other.PaddedSize &&
                        RealSize == other.RealSize &&

                        Members.Count == other.Members.Count &&
                        Members.OrderBy(x => x.Key).SequenceEqual(other.Members.OrderBy(x => x.Key)) &&

                        MembersIndexed.SequenceEqual(other.MembersIndexed);
                }

                public override int GetHashCode()
                {
                    var hash = new HashCode();

                    hash.Add(RelativeOffset);
                    hash.Add(PaddedSize);
                    hash.Add(RealSize);

                    foreach (var kv in Members.OrderBy(x => x.Key))
                    {
                        hash.Add(kv.Key);
                        hash.Add(kv.Value);
                    }

                    foreach (var item in MembersIndexed)
                        hash.Add(item);

                    return hash.ToHashCode();
                }
            }




            public record class ArrayInfo(MemberInfo BaseMemberInfo, uint Length) : MemberInfo(BaseMemberInfo.RelativeOffset, BaseMemberInfo.PaddedSize*Length);

        }





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








    public class BackendDrawPipelineReference : BackendReference
    {
        public readonly DrawPipelineDetails Details;

        private BackendDrawPipelineReference(object backendRef, DrawPipelineDetails details) : base(backendRef)
        {
            Details = details;
        }

        protected override void OnFree()
        {
            Backend.DestroyDrawPipeline(this);

            base.OnFree();
        }





        private static readonly Dictionary<DrawPipelineDetails, BackendDrawPipelineReference> DrawPipelineCache = CreateUnsafeStructKeyComparisonDictionary<DrawPipelineDetails, BackendDrawPipelineReference>();

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

                    AddRemovePipelineFromCacheEvent(get, spec);

                    if (ActiveFramebufferPipelineInFlight != null)
                        ActiveFramebufferPipelineInFlight.OnFreeEvent.Add(get.Free);


                    DrawPipelineCache.Add(spec, get);
                }

                return get;
            }
        }


        // this being separate prevents constant lambda allocations
        private static void AddRemovePipelineFromCacheEvent(BackendDrawPipelineReference get, DrawPipelineDetails spec)
        {
            get.OnFreeEvent.Add(() =>
            {
                lock (DrawPipelineCache)
                    DrawPipelineCache.Remove(spec);
            });

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
            public bool Enable = true;

            public BlendingFactor SrcColor = BlendingFactor.SrcAlpha;
            public BlendingFactor DstColor = BlendingFactor.OneMinusSrcAlpha;
            public BlendOperation ColorOp = BlendOperation.Add;

            public BlendingFactor SrcAlpha = BlendingFactor.One;
            public BlendingFactor DstAlpha = BlendingFactor.OneMinusSrcAlpha;
            public BlendOperation AlphaOp = BlendOperation.Add;

            public ColorWriteMask WriteMask = ColorWriteMask.All;
        }

    }






    public enum FrameBufferPipelineAttachmentAccessFlags : byte
    {
        None = 0,
        Read = 1 << 0,
        Write = 1 << 1,
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



        public MultiSampleCount SampleCount;

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
            Backend.DestroyFrameBufferPipeline(this);

            base.OnFree();
        }





        private static readonly Dictionary<FrameBufferPipelineDetails, BackendFrameBufferPipelineReference> FrameBufferPipelineCache = CreateUnsafeStructKeyComparisonDictionary<FrameBufferPipelineDetails, BackendFrameBufferPipelineReference>();

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

        public readonly ImmutableArray<IFramebufferAttachment> ColorAttachments;
        public readonly IFramebufferAttachment DepthStencilAttachment;
        public readonly BackendFrameBufferPipelineReference Pipeline;

        public readonly Vector2<uint> Size;

        private BackendFrameBufferObjectReference(ImmutableArray<IFramebufferAttachment> colorAttachments,

                                                       IFramebufferAttachment depthstencil,

                                                       BackendFrameBufferPipelineReference pipeline,

                                                       Vector2<uint> size,
                                                       
                                                       object backendRef) : base(backendRef)
        {
            ColorAttachments = colorAttachments;
            DepthStencilAttachment = depthstencil;
            Pipeline = pipeline;
            Size = size;
        }


        public static unsafe BackendFrameBufferObjectReference Create(ReadOnlySpan<IFramebufferAttachment> colorTargets, IFramebufferAttachment depthStencilTarget, BackendFrameBufferPipelineReference pipeline, Vector2<uint> Size)
        {
            return new BackendFrameBufferObjectReference(colorTargets.ToImmutableArray(), depthStencilTarget, pipeline, Size, Backend.CreateFrameBufferObject(colorTargets, depthStencilTarget, pipeline, Size));
        }



        protected override void OnFree()
        {
            Backend.DestroyFrameBufferObject(this);
            base.OnFree();
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



    public readonly record struct ResourceSetResourceBind(uint Binding, uint Element, IBackendResourceReference Resource, uint MappingRange);







    public static BackendFrameBufferObjectReference ActiveFrameBufferObject;
    public static BackendFrameBufferPipelineReference ActiveFramebufferPipeline;
    public static byte ActiveFrameBufferPipelineStage;



    private static BackendFrameBufferObjectReference ActiveFrameBufferObjectInFlight;
    private static BackendFrameBufferPipelineReference ActiveFramebufferPipelineInFlight;
    private static byte ActiveFrameBufferPipelineStageInFlight;


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
    /// <br/><b> WARNING: This method touches existing backend resources or state, and should be called outside of active frame rendering, from any thread. See <see cref="PushDeferredIdleRenderThreadAction(Func{object})"/> or <see cref="PushDeferredPreRenderThreadCommand{T}"/>. </b>
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
            foreach (var v in AllBackendReferences) 
                v.Dereference()?.Free();
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
    public static float EndFrameRendering()
    {
        CheckDuringRendering();
        var ret = Backend.EndFrameRendering();
        CurrentBackendRenderProgress = BackendRenderProgress.NotRendering;

        return ret;
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

        ActiveFramebufferPipelineInFlight = pipeline;
        ActiveFrameBufferObjectInFlight = fbo;
        ActiveFrameBufferPipelineStageInFlight = 0;
        CurrentBackendRenderProgress = BackendRenderProgress.DrawingViaFramebufferPipeline;
        Backend.BeginFrameBufferPipeline(fbo, pipeline);

        Backend.SetScissor(default, fbo.Size);
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
        ActiveFrameBufferPipelineStageInFlight++;
        Backend.AdvanceFrameBufferPipeline(fbo, pipeline, ActiveFrameBufferPipelineStageInFlight);
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
        ActiveFramebufferPipelineInFlight = null;
        ActiveFrameBufferObjectInFlight = null;
        ActiveFrameBufferPipelineStageInFlight = 0;
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

#if DEBUG
        DrawCalls++;
#endif
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
        Backend.SetScissor(default, CurrentSwapchainDetails.Size);
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
        public object CreateTexture(Vector3<uint> Size, TextureTypes type, TextureFormats format, bool FramebufferAttachmentCompatible, TextureMipCount mipCount, TextureMipData[] Mips = null, MultiSampleCount msaa = 0);

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
        public unsafe object CreateFrameBufferObject(ReadOnlySpan<IFramebufferAttachment> colorTargets, IFramebufferAttachment depthStencilTarget, BackendFrameBufferPipelineReference pipeline, Vector2<uint> Size);
        public void DestroyFrameBufferObject(BackendFrameBufferObjectReference buffer);






        // ================================================================
        // ======================= IMMEDIATE DRAWING COMMANDS ============
        // ================================================================



        // ---------------- Frame Rendering ----------------
        public void StartFrameRendering();
        public float EndFrameRendering();



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