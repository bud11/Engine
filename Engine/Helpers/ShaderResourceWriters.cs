

namespace Engine.Core;




using System.Runtime.CompilerServices;
using static Engine.Core.RenderingBackend;
using static Engine.Core.Rendering;




/// <summary>
/// Contains functionality for defining and interacting with shader structs/buffers.
/// </summary>
public static partial class ShaderResourceWriters
{




    /// <summary>
    /// A helper that converts multiple metadata-aided writes into a call to <see cref="BackendBufferAllocationReference.Write(WriteRange*, uint, bool)"/>.
    /// <br/> <paramref name="nessecary"/> determines if the writes are guaranteed to happen or whether they can be dropped if the upcoming frame is dropped.
    /// </summary>
    /// <param name="buffer"></param>
    /// <returns></returns>
    public static unsafe ShaderDataBufferWriteHandle StartWrite(this BackendDataBufferAllocationReference buffer, bool nessecary)
        => new ShaderDataBufferWriteHandle(buffer, nessecary);





    /// <summary>
    /// A helper that converts multiple writes into a call to <see cref="BackendBufferAllocationReference.Write(ReadOnlySpan{WriteRange})"/>.
    /// <br/> <paramref name="nessecary"/> determines if the writes are guaranteed to happen or whether they can be dropped if the upcoming frame is dropped.
    /// </summary>
    /// <param name="buffer"></param>
    /// <returns></returns>
    public static unsafe ShaderBufferWriteHandle StartWrite(this BackendBufferAllocationReference buffer, bool nessecary)
        => new ShaderBufferWriteHandle(buffer, nessecary);








    /// <summary>
    /// Represents one or more write operations into a gpu buffer.
    /// </summary>
    /// <remarks>
    /// Creates a write handle for the given buffer allocation.
    /// </remarks>
    public unsafe ref struct ShaderBufferWriteHandle(BackendBufferAllocationReference buf, bool nessecary)
    {
        private const byte MaxWritesPer = 100;   //<-- write limit per one of these


        [InlineArray(MaxWritesPer)]
        private struct InlineWriteRangeArray { public WriteRange _value; }


        private byte WriteCount;
        private InlineWriteRangeArray Writes;


        public readonly BackendBufferAllocationReference Buffer = buf;

        private readonly bool Nessecary = nessecary;


        /// <summary>
        /// Queues a write of <paramref name="dataPtr"/> into the buffer. <paramref name="dataPtr"/> is copied to an intermediate temporary buffer and doesn't need to persist beyond this call.
        /// </summary>
        /// <param name="dataPtr"></param>
        /// <param name="size"></param>
        /// <param name="offset"></param>
        /// <param name="regions"></param>
        /// <exception cref="Exception"></exception>
        public unsafe void PushWrite(void* dataPtr, uint size, uint offset, ReadOnlySpan<ContiguousRegion> regions = default)
        {
            var dst = RenderThread.AllocateRenderTemporaryUnmanaged((int)size);

            if (regions.IsEmpty)
                Unsafe.CopyBlockUnaligned(dst, (byte*)dataPtr, size);
            else
                BufferToPaddedBufferCopy((byte*)dataPtr, size, 0, dst, regions);

            var range = new WriteRange(offset, size, dst);


#if DEBUG
            if (WriteCount >= MaxWritesPer || range.Length == 0) throw new Exception();
#endif
            Writes[WriteCount] = range;
            WriteCount++;

        }



        /// <summary>
        /// Writes to the buffer via <see cref="BackendBufferAllocationReference.Write(ReadOnlySpan{WriteRange})"/>.
        /// </summary>
        public void EndWrite()
        {
            fixed (WriteRange* p = &Writes[0])
                Buffer.Write(p, WriteCount, Nessecary);
        }
    }




    /// <summary>
    /// <inheritdoc cref="ShaderBufferWriteHandle"/>
    /// </summary>
    /// <param name="buf"></param>
    public unsafe ref struct ShaderDataBufferWriteHandle(BackendDataBufferAllocationReference buf, bool nessecary)
    {

        private ShaderBufferWriteHandle Handle = new(buf, nessecary);


        private readonly BackendDataBufferAllocationReference Buffer => (BackendDataBufferAllocationReference)Handle.Buffer;



        /// <summary>
        /// <inheritdoc cref="ShaderBufferWriteHandle.EndWrite"/>
        /// </summary>
        public readonly void EndWrite() => Handle.EndWrite();



        /// <summary>
        /// Queues a write from the offset of field (assumedly) <paramref name="fieldName"/>, assuming it exists. Will naturally throw an exception if not.
        /// <br/> The data passed in is allowed to exceed the size of the field in order to write further into the buffer, but must not exceed the bounds of the buffer.
        /// <br/> <b>! ! ! The passed data should NOT be padded. Appropriate padding/alignment is handled automatically. ! ! !</b>
        /// </summary>
        /// <typeparam name="ValueT"></typeparam>
        /// <param name="fieldName"></param>
        /// <param name="val"></param>
        /// <param name="extraOffset"></param>
        public void PushWriteFromOffsetOf<ValueT>(string fieldName, ValueT val, uint extraOffset = 0) where ValueT : unmanaged
            => Handle.PushWrite(&val, (uint)sizeof(ValueT), Buffer.Metadata.FieldOffsets[fieldName] + extraOffset, Buffer.Metadata.ContiguousRegions.AsSpan());


        /// <summary>
        /// Functionally almost the same as <see cref="PushWriteFromOffsetOf(string, void*, uint, uint)"/>, but assumes <paramref name="arrayName"/> is the start of an array instead of a single value.
        /// </summary>
        /// <typeparam name="ValueT"></typeparam>
        /// <param name="fieldName"></param>
        /// <param name="val"></param>
        /// <param name="extraOffset"></param>
        public void PushWriteFromOffsetOf<ValueT>(string arrayName, uint idx, ValueT val, uint extraOffset = 0) where ValueT : unmanaged
            => Handle.PushWrite(&val, (uint)sizeof(ValueT), Buffer.Metadata.FieldOffsets[arrayName] + extraOffset + (uint)(sizeof(ValueT)*idx), Buffer.Metadata.ContiguousRegions.AsSpan());



        /// <summary>
        /// <inheritdoc cref="PushWriteFromOffsetOf{ValueT}(string, ValueT, uint)"/>
        /// </summary>
        /// <param name="fieldName"></param>
        /// <param name="data"></param>
        /// <param name="size"></param>
        /// <param name="extraOffset"></param>
        public void PushWriteFromOffsetOf(string fieldName, void* data, uint size, uint extraOffset = 0)
            => Handle.PushWrite(data, size, Buffer.Metadata.FieldOffsets[fieldName] + extraOffset, Buffer.Metadata.ContiguousRegions.AsSpan());

    }












    /// <summary>
    /// A helper that converts multiple writes into a call to <see cref="BackendResourceSetReference.Write(ResourceSetResourceBind*, uint, bool)"/>.
    /// <br/> <paramref name="nessecary"/> determines if the writes are guaranteed to happen or whether they can be dropped if the upcoming frame is dropped.
    /// </summary>
    /// <param name="set"></param>
    /// <returns></returns>
    public static unsafe ResourceSetWriteHandle StartWrite(this BackendResourceSetReference set, bool nessecary)
        => new ResourceSetWriteHandle(set, nessecary);




    /// <summary>
    /// Represents one or more writes into a resource set.
    /// </summary>
    /// <remarks>
    /// Creates a write handle for the given buffer allocation.
    /// </remarks>
    public unsafe ref struct ResourceSetWriteHandle(BackendResourceSetReference set, bool nessecary)
    {
        private const byte MaxWritesPer = 100;   //<-- write limit per one of these



        [InlineArray(MaxWritesPer)]
        private struct InlineWriteRangeArray { public ResourceSetResourceBind _value; }



        private byte WriteCount;
        private InlineWriteRangeArray Writes;


        public readonly BackendResourceSetReference Set = set;


        private readonly bool Nessecary = nessecary;


        public unsafe void PushWrite(string name, IResourceSetResource resource) 
        {
            if (resource is BackendTextureAndSamplerReferencesPair)
            {
                if (resource == null)
                {
                    var get = Set.Metadata.Textures[name];

                    resource = get.Metadata.SamplerType switch
                    {
                        TextureSamplerTypes.Texture2D => Dummy2DTextureSamplerPair,
                        TextureSamplerTypes.Texture2DShadow => Dummy2DShadowTextureSamplerPair,
                        TextureSamplerTypes.TextureCubeMap => DummyCubeTextureSamplerPair,
                        TextureSamplerTypes.Texture3D => Dummy3DTextureSamplerPair,
                        _ => throw new NotImplementedException(),
                    };

                    PushWrite(get.Binding, resource);

                    return;
                }

                PushWrite(Set.Metadata.Textures[name].Binding, resource);
            }

            else if (resource is BackendUniformBufferAllocationReference)
            {
                var get = Set.Metadata.UniformBuffers[name];
                PushWrite(get.Binding, resource ??GetDummyUBO(get.Metadata.SizeRequirement));
            }

            else if (resource is BackendStorageBufferAllocationReference)
            {
                var get = Set.Metadata.StorageBuffers[name];
                PushWrite(get.Binding, resource ??GetDummySSBO(get.Metadata.SizeRequirement));
            }

            else throw new Exception();
        }




        public unsafe void PushWrite(uint binding, IResourceSetResource resource)
        {

#if DEBUG
            ArgumentNullException.ThrowIfNull(resource);    
#endif

            var write = new ResourceSetResourceBind(binding, ((RefCounted)resource).GCHandle);

#if DEBUG
            if (WriteCount >= MaxWritesPer || write.Binding > Set.ResourceCount) throw new Exception();
#endif
            Writes[WriteCount] = write;
            WriteCount++;

        }


        public void EndWrite()
        {
            fixed (ResourceSetResourceBind* p = &Writes[0])
                Set.Write(p, WriteCount, Nessecary);
        }
    }
}