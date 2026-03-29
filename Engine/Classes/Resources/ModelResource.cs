namespace Engine.GameResources;



using System.Runtime.CompilerServices;
using static Engine.Core.EngineMath;
using static Engine.Core.RenderingBackend;
using Engine.Core;

using static Engine.Core.Parsing;



#if DEBUG
using System.Text.Json;
#endif




[FileExtensionAssociation(".mdl")]
public class ModelResource : GameResource, GameResource.ILoads

#if DEBUG
    , GameResource.IConverts
#endif
{


    public readonly SubmeshRange[] SubMeshes;

    public readonly AABB BaseAABB;

    public readonly BackendIndexBufferAllocationReference IndexBuffer;

    public readonly UnmanagedKeyValueHandleCollectionOwner<string, VertexAttributeDefinitionPlusBufferClass> Buffers;




    public record struct SubmeshRange(uint Start, uint End);



    /// <summary>
    /// A vertex attribute buffer definition bundled with an initial content byte array. <br /> See <see cref="CreateFromArray{ArrayType}(VertexAttributeDefinition, ArrayType[], bool)"/> for creating from a generic array.
    /// </summary>
    /// <param name="def"></param>
    /// <param name="data"></param>
    /// <param name="writeable"></param>
    public record struct VertexAttributeDefinitionPlusData(VertexAttributeDefinition def, byte[] data, bool writeable)
    {



        /// <summary>
        /// Creates a <see cref="VertexAttributeDefinitionPlusData"/> from an unmanaged array by copying it into a byte array.
        /// </summary>
        /// <typeparam name="ArrayType"></typeparam>
        /// <param name="def"></param>
        /// <param name="data"></param>
        /// <param name="writeable"></param>
        /// <returns></returns>
        public static unsafe VertexAttributeDefinitionPlusData CreateFromArray<ArrayType>(VertexAttributeDefinition def, ArrayType[] data, bool writeable) where ArrayType : unmanaged
        {
            if (data == null) throw new Exception("Data can't be null");


            //no need to convert
            if (typeof(ArrayType) == typeof(byte))
                return new(def, (byte[])(object)data, writeable);


            byte[] newdata = new byte[data.Length * sizeof(ArrayType)];

            fixed (void* d = newdata)
            fixed (void* p = data)
                Unsafe.CopyBlockUnaligned(d, p, (uint)newdata.Length);

            return new VertexAttributeDefinitionPlusData(def, newdata, writeable);
        }



        /// <summary>
        /// Creates a new single interwoven array from multiple existing arrays.
        /// <br/> Original offsets and strides are ignored.
        /// </summary>
        public static unsafe Dictionary<string, VertexAttributeDefinitionPlusData> CreateInterwoven(
            Dictionary<string, VertexAttributeDefinitionPlusData> buffers,
            bool writeable)

        {



            // Freeze order so offsets are deterministic
            var ordered = buffers.ToArray();



            // Validate vertex counts
            var first = ordered[0].Value;
            int vertexCount = first.data.Length / first.def.Stride;



            foreach (var kv in ordered)
            {
                int count = kv.Value.data.Length / kv.Value.def.Stride;
                if (count != vertexCount)
                    throw new Exception("All buffers must have the same vertex count");
            }



            // Alignment helpers
            static int AlignUp(int value, int alignment)
                => (value + alignment - 1) & ~(alignment - 1);



            static int GetAlignment(VertexAttributeBufferComponentFormat fmt) => fmt switch
            {
                VertexAttributeBufferComponentFormat.Byte => 1,
                VertexAttributeBufferComponentFormat.Half => 2,
                VertexAttributeBufferComponentFormat.Float => 4,
                _ => throw new NotImplementedException()
            };




            // Compute offsets + final stride
            int runningOffset = 0;
            int maxAlignment = 1;

            var offsets = new int[ordered.Length];
            var sizes = new int[ordered.Length];

            for (int i = 0; i < ordered.Length; i++)
            {
                var def = ordered[i].Value.def;

                int align = GetAlignment(def.ComponentFormat);
                maxAlignment = Math.Max(maxAlignment, align);

                runningOffset = AlignUp(runningOffset, align);
                offsets[i] = runningOffset;

                sizes[i] = def.Stride; // bytes per vertex
                runningOffset += sizes[i];
            }




            int vertexStride = AlignUp(runningOffset, maxAlignment);

            // Allocate interleaved buffer
            byte[] interleaved = new byte[vertexCount * vertexStride];

            // Interleave per vertex
            fixed (byte* dstBase = interleaved)
            {
                for (int v = 0; v < vertexCount; v++)
                {
                    byte* dstVertex = dstBase + v * vertexStride;

                    for (int a = 0; a < ordered.Length; a++)
                    {
                        var srcData = ordered[a].Value.data;
                        int srcStride = ordered[a].Value.def.Stride;

                        fixed (byte* srcBase = srcData)
                        {
                            byte* srcPtr = srcBase + v * srcStride;
                            byte* dstPtr = dstVertex + offsets[a];

                            Unsafe.CopyBlockUnaligned(
                                destination: dstPtr,
                                source: srcPtr,
                                byteCount: (uint)srcStride
                            );
                        }
                    }
                }
            }




            // Build output definitions
            var result = new Dictionary<string, VertexAttributeDefinitionPlusData>(ordered.Length);

            for (int i = 0; i < ordered.Length; i++)
            {
                var (name, old) = ordered[i];

                var newDef = old.def with
                {
                    Offset = (ushort)offsets[i],
                    Stride = (ushort)vertexStride
                };

                result[name] = new VertexAttributeDefinitionPlusData(
                    newDef,
                    interleaved,
                    writeable
                );
            }

            return result;
        }

    }




    
    public static async Task<GameResource> Load(Loading.AssetByteStream stream, string key)
    {

        var reader = ValueReader.FromStream(stream);


        var attribDefCount = reader.ReadUnmanaged<byte>();
        var bufferCount = reader.ReadUnmanaged<byte>();



        BackendVertexBufferAllocationReference[] buffers = new BackendVertexBufferAllocationReference[bufferCount];

        for (var i = 0; i < bufferCount; i++)
        {
            bool writeable = stream.ReadByte() == 1;
            var data = reader.ReadLengthPrefixedUnmanagedSpan<byte>();

            buffers[i] = BackendVertexBufferAllocationReference.Create<byte>(data, writeable);
        }


        var attributes = new Dictionary<string, VertexAttributeDefinitionPlusBufferClass>();


        for (var i = 0; i < attribDefCount; i++)
        {
            string name = reader.ReadString();

            var componentFormat = (VertexAttributeBufferComponentFormat)stream.ReadByte();
            byte offset = (byte)stream.ReadByte();
            byte stride = (byte)stream.ReadByte();
            var scope = (VertexAttributeScope)stream.ReadByte();
            byte bufferIndex = (byte)stream.ReadByte();

            attributes[name] = new VertexAttributeDefinitionPlusBufferClass(buffers[bufferIndex], new VertexAttributeDefinition(componentFormat, stride, offset, scope));
        }


        var submeshes = reader.ReadLengthPrefixedUnmanagedSpan<SubmeshRange>();

        var idxbuffer = reader.ReadLengthPrefixedUnmanagedSpan<uint>();

        BackendIndexBufferAllocationReference indexbuffer = idxbuffer == null ? null : BackendIndexBufferAllocationReference.Create(idxbuffer, false);




        var aabb = reader.ReadUnmanaged<AABB>();

        return new ModelResource(
            attributes,
            submeshes,
            indexbuffer,
            aabb,
            key
        );
    }





#if DEBUG

    public static async Task<bool> Validate(byte[] validationBlock, string key) => true;

    public static async Task<IConverts.FinalAssetBytes> ConvertToFinalAssetBytes(Loading.Bytes bytes, string key)
    {
        var dict = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(bytes.ByteArray, Parsing.JsonAssetLoadingOptions);

        bytes.Dispose();


        var attribs = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(dict["AttributeData"], Parsing.JsonAssetLoadingOptions);





        Dictionary<string, VertexAttributeDefinitionPlusData> buffers = new Dictionary<string, VertexAttributeDefinitionPlusData>(attribs.Count);


        foreach (var kv in attribs)
        {
            var componentFormat = Enum.Parse<VertexAttributeBufferComponentFormat>(kv.Value.GetProperty("ComponentFormat").GetString(), true);

            var componentCount = kv.Value.GetProperty("ComponentCount").GetByte();



            var scope = Enum.Parse<VertexAttributeScope>(kv.Value.GetProperty("Scope").GetString(), true);

            var data = Convert.FromBase64String(kv.Value.GetProperty("Base64Data").GetString());

            buffers[kv.Key] = new VertexAttributeDefinitionPlusData(new VertexAttributeDefinition(

                componentFormat,

                (ushort)((componentFormat switch
                {
                    VertexAttributeBufferComponentFormat.Byte => 1,
                    VertexAttributeBufferComponentFormat.Half => 2,
                    VertexAttributeBufferComponentFormat.Float => 4,
                    _ => throw new NotImplementedException(),
                }) * componentCount),

                0,

                scope),

                data, false);
        }




        var interwoven = VertexAttributeDefinitionPlusData.CreateInterwoven(buffers, false);





        var write = ValueWriter.CreateWithBufferWriter();

        //attribute count
        write.WriteUnmanaged((byte)interwoven.Count);

        //buffer count
        write.WriteUnmanaged<byte>(1);

        //buffer 0 writeable
        write.WriteUnmanaged<byte>(0);

        //buffer 0 content
        write.WriteLengthPrefixedUnmanagedSpan<byte>(interwoven.FirstOrDefault().Value.data);


        //attributes
        foreach (var kv in interwoven)
        {
            write.WriteString(kv.Key);

            write.WriteUnmanaged<byte>((byte)kv.Value.def.ComponentFormat);
            write.WriteUnmanaged<byte>((byte)kv.Value.def.Offset);
            write.WriteUnmanaged<byte>((byte)kv.Value.def.Stride);
            write.WriteUnmanaged<byte>((byte)kv.Value.def.Scope);

            write.WriteUnmanaged<byte>(0);
        }





        // Submeshes
        write.WriteLengthPrefixedUnmanagedSpan<SubmeshRange>(dict["SubMeshes"].Deserialize<SubmeshRange[]>(Parsing.JsonAssetLoadingOptions));



        // Index buffer
        if (dict.TryGetValue("IndexBufferBase64Data", out var idxbuf))
        {
            byte[] b = Convert.FromBase64String(idxbuf.GetString());
            uint[] uints = new uint[b.Length / 4];
            Buffer.BlockCopy(b, 0, uints, 0, b.Length);

            write.WriteLengthPrefixedUnmanagedSpan<uint>(uints);
        }
        else
            write.WriteVariableLengthUnsigned(0);



        // AABB
        write.WriteUnmanaged(dict.TryGetValue("LocalAABB", out var aabb) ? aabb.Deserialize<AABB>(Parsing.JsonAssetLoadingOptions) : AABB.MaxValue);



        return new IConverts.FinalAssetBytes(write.GetSpan().ToArray(), null);
    }
    



#endif




    public ModelResource(

        Dictionary<string, VertexAttributeDefinitionPlusData> buffers,
        SubmeshRange[] submeshes,
        uint[] indexBuffer = null,
        bool writeableIndexBuffer = false,

        AABB baseAABB = default,

        string key = null) : base(key)
    {


        var buffersfinal = new Dictionary<string, VertexAttributeDefinitionPlusBufferClass>();

        var createdBuffers = new Dictionary<byte[], BackendVertexBufferAllocationReference>();


        foreach (var kv in buffers)
        {
            if (!createdBuffers.TryGetValue(kv.Value.data, out var get))
                get = createdBuffers[kv.Value.data] = BackendVertexBufferAllocationReference.Create<byte>(kv.Value.data, kv.Value.writeable);

            buffersfinal.Add(kv.Key, new VertexAttributeDefinitionPlusBufferClass(get, kv.Value.def));
        }



        Buffers = new (buffersfinal);

        SubMeshes = submeshes;
        BaseAABB = baseAABB;

        IndexBuffer = indexBuffer != null ? BackendIndexBufferAllocationReference.Create(indexBuffer, writeableIndexBuffer) : null;
    }




    public ModelResource(

        Dictionary<string, VertexAttributeDefinitionPlusBufferClass> buffers,
        SubmeshRange[] submeshes,
        BackendIndexBufferAllocationReference indexBuffer = null,

        AABB baseAABB = default,

        string key = null) : base(key)
    {

        Buffers = new (buffers);
        SubMeshes = submeshes;
        BaseAABB = baseAABB;
        IndexBuffer = indexBuffer;
    }


}
