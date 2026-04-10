


namespace Engine.Core;



using Engine.Attributes;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;

using static Engine.Core.Loading;


using System.Buffers;



#if DEBUG
using System.Text.Json;
using System.Reflection;
using System.Diagnostics;
using Engine.Stripped;
#endif















public static partial class Parsing
{



    /// <summary>
    /// Provides a common read only, advance only interface to efficiently read from a <see cref="ReadOnlyMemory{T}"/> or <see cref="Stream"/>.
    /// </summary>
    public unsafe struct ValueReader
    {
        private ReadOnlyMemory<byte> _memory;
        private readonly Stream? _stream;

        private ValueReader(Stream? stream, ReadOnlyMemory<byte> memory)
        {
            _stream = stream;
            _memory = memory;
        }

        public static ValueReader FromMemory(ReadOnlyMemory<byte> memory)
            => new ValueReader(null, memory);

        public static ValueReader FromStream(Stream stream)
            => new ValueReader(stream ?? throw new ArgumentNullException(nameof(stream)), default);

        public T ReadUnmanaged<T>() where T : unmanaged
        {
            int size = sizeof(T);

            if (_stream == null)
            {
                if (_memory.Length < size)
                    throw new EndOfStreamException();

                T val = MemoryMarshal.Read<T>(_memory.Span);
                _memory = _memory[size..];
                return val;
            }

            Span<byte> buf = stackalloc byte[size];
            _stream.ReadExactly(buf);
            return MemoryMarshal.Read<T>(buf);
        }

        public void ReadUnmanagedSpan<T>(Span<T> destination) where T : unmanaged
        {
            int byteCount = destination.Length * sizeof(T);
            Span<byte> bytes = MemoryMarshal.AsBytes(destination);

            if (_stream == null)
            {
                if (_memory.Length < byteCount)
                    throw new EndOfStreamException();

                _memory.Span[..byteCount].CopyTo(bytes);
                _memory = _memory[byteCount..];
                return;
            }

            _stream.ReadExactly(bytes);
        }

        public T[] ReadUnmanagedSpan<T>(uint length) where T : unmanaged
        {
            int len = checked((int)length);
            T[] arr = new T[len];
            ReadUnmanagedSpan(arr.AsSpan());
            return arr;
        }

        public T[] ReadLengthPrefixedUnmanagedSpan<T>() where T : unmanaged
            => ReadUnmanagedSpan<T>((uint)ReadVariableLengthUnsigned());

        public string ReadString()
        {
            ulong lenU = ReadVariableLengthUnsigned();
            int len = checked((int)lenU);

            if (_stream == null)
            {
                if (_memory.Length < len)
                    throw new EndOfStreamException();

                string str = Encoding.UTF8.GetString(_memory.Span[..len]);
                _memory = _memory[len..];
                return str;
            }

            if (len <= 1024)
            {
                Span<byte> span = stackalloc byte[len];
                _stream.ReadExactly(span);
                return Encoding.UTF8.GetString(span);
            }
            else
            {
                byte[] arr = new byte[len];
                _stream.ReadExactly(arr);
                return Encoding.UTF8.GetString(arr);
            }
        }

        public ulong ReadVariableLengthUnsigned()
        {
            ulong result = 0;
            int shift = 0;

            while (true)
            {
                byte b = ReadUnmanaged<byte>();

                result |= ((ulong)(b & 0x7F)) << shift;

                if ((b & 0x80) == 0)
                    break;

                shift += 7;

                if (shift >= 64)
                    throw new FormatException();
            }

            return result;
        }
    }






    /// <summary>
    /// Provides a common write only, advance only interface to efficiently write to a <see cref="Memory{T}"/>, <see cref="ArrayBufferWriter{T}"/>, or <see cref="Stream"/>.
    /// </summary>
    public unsafe struct ValueWriter
    {
        private readonly Memory<byte> _memory;

        public int Written { get; private set; }


        private readonly ArrayBufferWriter<byte>? _bufferWriter;
        private readonly Stream? _stream;

        private ValueWriter(Memory<byte> memory, ArrayBufferWriter<byte>? bufferWriter, Stream? stream)
        {
            _memory = memory;
            _bufferWriter = bufferWriter;
            _stream = stream;
            Written = 0;
        }

        public static ValueWriter FromMemory(Memory<byte> memory)
            => new ValueWriter(memory, null, null);

        public static ValueWriter CreateWithBufferWriter()
            => new ValueWriter(default, new ArrayBufferWriter<byte>(), null);

        public static ValueWriter FromStream(Stream stream)
            => new ValueWriter(default, null, stream ?? throw new ArgumentNullException());


        public void WriteUnmanaged<T>(T value) where T : unmanaged
        {
            int size = sizeof(T);

            if (_bufferWriter != null)
            {
                Span<byte> dst = _bufferWriter.GetSpan(size);
                MemoryMarshal.Write(dst, in value);
                _bufferWriter.Advance(size);
                return;
            }

            if (_stream != null)
            {
                Span<byte> buf = stackalloc byte[size];
                MemoryMarshal.Write(buf, in value);
                _stream.Write(buf);
                return;
            }

            // Memory mode
            if (_memory.Length - Written < size)
                throw new InvalidOperationException();

            MemoryMarshal.Write(_memory.Span.Slice(Written, size), in value);
            Written += size;
        }

        public void WriteUnmanagedSpan<T>(ReadOnlySpan<T> values) where T : unmanaged
        {
            ReadOnlySpan<byte> bytes = MemoryMarshal.AsBytes(values);
            int len = bytes.Length;

            if (_bufferWriter != null)
            {
                _bufferWriter.Write(bytes);
                return;
            }

            if (_stream != null)
            {
                _stream.Write(bytes);
                return;
            }

            // Memory mode
            if (_memory.Length - Written < len)
                throw new InvalidOperationException();

            bytes.CopyTo(_memory.Span.Slice(Written, len));
            Written += len;
        }

        public void WriteLengthPrefixedUnmanagedSpan<T>(ReadOnlySpan<T> values) where T : unmanaged
        {
            WriteVariableLengthUnsigned((ulong)values.Length);
            WriteUnmanagedSpan(values);
        }

        public void WriteString(string str)
        {
            int len = Encoding.UTF8.GetByteCount(str);

            WriteVariableLengthUnsigned((ulong)len);

            if (_bufferWriter != null)
            {
                Span<byte> dst = _bufferWriter.GetSpan(len);
                Encoding.UTF8.GetBytes(str, dst);
                _bufferWriter.Advance(len);
                return;
            }

            if (_stream != null)
            {
                if (len <= 1024)
                {
                    Span<byte> buf = stackalloc byte[len];
                    Encoding.UTF8.GetBytes(str, buf);
                    _stream.Write(buf);
                }
                else
                {
                    byte[] arr = new byte[len];
                    Encoding.UTF8.GetBytes(str, arr);
                    _stream.Write(arr);
                }

                return;
            }

            // Memory mode
            if (_memory.Length - Written < len)
                throw new InvalidOperationException();

            Encoding.UTF8.GetBytes(str, _memory.Span.Slice(Written, len));
            Written += len;
        }

        public void WriteVariableLengthUnsigned(ulong value)
        {
            while (value >= 0x80)
            {
                WriteUnmanaged((byte)((value & 0x7F) | 0x80));
                value >>= 7;
            }

            WriteUnmanaged((byte)value);
        }

        /// <summary>
        /// Returns the written data.
        /// </summary>
        public ReadOnlySpan<byte> GetSpan()
        {
            if (_bufferWriter != null)
                return _bufferWriter.WrittenSpan;

            if (_stream != null)
                throw new InvalidOperationException();

            return _memory.Span[..Written];
        }
    }








    /// <summary>
    /// The abstract base class for a binary serializer and deserializer, capable of supporting an arbitrary fixed number of types to serialize and/or deserialize, supported via adding <see cref="BinarySerializableTypeAttribute"/> attributes.
    /// <br/> This class is fully source generated / AOT compatible.
    /// </summary>
    public abstract partial class BinarySerializerDeserializerBase
    {


        /// <summary>
        /// Reads a known type (generic must be exact) directly from <paramref name="reader"/>. Throws an exception if the type isn't supported by this class.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="reader"></param>
        /// <returns></returns>
        public virtual T ReadKnownType<T>(ref ValueReader reader) => throw new NotSupportedException();

        /// <summary>
        /// Reads an unknown type from <paramref name="reader"/> (reads type ID, then reads with <see cref="ReadKnownType{T}(ref ValueReader)"/>). Throws an exception if the ID is invalid.
        /// </summary>
        /// <param name="reader"></param>
        /// <returns></returns>
        public virtual object ReadUnknownType(ref ValueReader reader) => throw new NotSupportedException();


        /// <summary>
        /// Serializes a known type (generic must be exact) to binary and writes it via <paramref name="writer"/>. Throws an exception if the type isn't supported by this class.
        /// <br/> If <paramref name="writeTypeID"/> is true, a type ID will be written first, allowing the type to be read via <see cref="ReadUnknownType(ref ValueReader)"/>.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="writer"></param>
        public virtual void WriteKnownType<T>(T instance, ref ValueWriter writer, bool writeTypeID = false) => throw new NotSupportedException();


        /// <summary>
        /// Serializes an unknown/boxed type to binary and writes it via <paramref name="writer"/>. Throws an exception if the type isn't supported.
        /// <br/> If <paramref name="writeTypeID"/> is true, a type ID will be written first, allowing the type to be read via <see cref="ReadUnknownType(ref ValueReader)"/>.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="writer"></param>
        public virtual void WriteUnknownType(object instance, ref ValueWriter writer, bool writeTypeID = false) => throw new NotSupportedException();






        /// <summary>
        /// Returns the type ID for <paramref name="type"/> within the context of this class. Throws an exception if the type isn't supported by this class.
        /// <br/> This should always be serialized as a variable length unsigned number.
        /// </summary>
        /// <param name="type"></param>
        /// <returns></returns>
        public virtual ulong GetTypeID(Type type) => throw new NotSupportedException();


        /// <summary>
        /// Returns the type ID for <paramref name="type"/> within the context of <typeparamref name="TSerializerDeserializer"/>. Throws an exception if the type isn't supported by <typeparamref name="TSerializerDeserializer"/>.
        /// <br/> This should always be serialized as a variable length unsigned number.
        /// </summary>
        /// <param name="type"></param>
        /// <returns></returns>
        public static partial ulong GetTypeID<TSerializerDeserializer>(Type type) where TSerializerDeserializer : BinarySerializerDeserializerBase;



        /// <summary>
        /// Returns the intermediate/serialized type representation of <paramref name="type"/> within the context of this class. Null if the type is unsupported by this class.
        /// </summary>
        /// <param name="type"></param>
        /// <returns></returns>
        public virtual Type GetTypeIntermediateRepresentation(Type type) => throw new NotSupportedException();



        /// <summary>
        /// Returns the intermediate/serialized type representation of <paramref name="type"/> within the context of <typeparamref name="TSerializerDeserializer"/>. Null if the type is unsupported by <typeparamref name="TSerializerDeserializer"/>.
        /// </summary>
        /// <typeparam name="TSerializerDeserializer"></typeparam>
        /// <param name="type"></param>
        /// <returns></returns>
        /// <exception cref="NotSupportedException"></exception>
        public static partial Type GetTypeIntermediateRepresentation<TSerializerDeserializer>(Type type) where TSerializerDeserializer : BinarySerializerDeserializerBase;

    }






    /// <summary>
    /// Sets a field/property marked with <see cref="IndexableAttribute"/>. Throws if not found, inaccessible, <typeparamref name="T"/> is incompatible, or the assignment was otherwise invalid.
    /// <br/> For <typeparamref name="T"/> to be compatible, it should ideally match exactly. If not, <see cref="UnknownCast{TTo}(object)"/> will attempt to cast it.
    /// </summary>
    /// <param name="inst"></param>
    /// <param name="idx"></param>
    /// <param name="value"></param>
    public static partial void SetIndexable<T>(this object inst, string idx, T value);


    /// <summary>
    /// <inheritdoc cref="SetIndexable{T}(object, string, T)"/>
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="inst"></param>
    /// <param name="idx"></param>
    /// <param name="value"></param>
    public static partial void SetIndexable<T>(this object inst, ushort idx, T value);


    /// <summary>
    /// Gets a field/property marked with <see cref="IndexableAttribute"/>. Throws if not found, inaccessible, or <typeparamref name="T"/> is incompatible.
    /// <br/> For <typeparamref name="T"/> to be compatible, it should ideally match exactly. If not, <see cref="UnknownCast{TTo}(object)"/> will attempt to cast it.
    /// </summary>
    /// <param name="inst"></param>
    /// <param name="idx"></param>
    /// <returns></returns>
    public static partial T GetIndexable<T>(this object inst, string idx);



    /// <summary>
    /// <inheritdoc cref="GetIndexable{T}(object, string)"/>
    /// </summary>
    /// <param name="inst"></param>
    /// <param name="idx"></param>
    /// <returns></returns>
    public static partial T GetIndexable<T>(this object inst, ushort idx);




    /// <summary>
    /// Casts <paramref name="value"/> to <typeparamref name="TTo"/> using an explicit compile-time-known cast. AOT safe.
    /// <br/> This isn't magic - both types must be recognised by the source generator as possibly ever unknown.
    /// <br/> As of current source generator implementation, and in practice, that means <see cref="object.GetType()"/> and typeof(<typeparamref name="TTo"/>) must be included directly or indirectly via association with <see cref="BinarySerializableTypeAttribute"/> or <see cref="IndexableAttribute"/>.
    /// </summary>
    /// <typeparam name="TTo"></typeparam>
    /// <param name="value"></param>
    /// <returns></returns>
    public static partial TTo UnknownCast<TTo>(object value);








#if DEBUG



    public static readonly System.Text.Json.JsonSerializerOptions JsonAssetLoadingOptions = new System.Text.Json.JsonSerializerOptions()
    {
        IncludeFields = true,
        Converters =
        {
            new System.Text.Json.Serialization.JsonStringEnumConverter()
        }
    };






    private static bool DebugJsonArgumentParsing = false;





    /// <summary>
    /// Debug only development time method. Serializes arbitrary arguments into Dictionary string, object, or a Dictionary byte, object, depending on whether <paramref name="DataValueAttributeTargetTypeForNumericalIndexing"/> is set.
    /// <br/> 
    /// <br/> Example data might look like this, where each argument value is an input to <see cref="DeserializeTypeGraphFromJson"/>
    /// <br/>
    /// <br/>
    /// <code>
    /// {
    ///     "ExampleArgument": { ... },
    ///     "ExampleArgument2": { ... }
    /// }
    /// </code>
    /// 
    /// <br/> If supplying <paramref name="DataValueAttributeTargetTypeForNumericalIndexing"/>, arguments will be treated as destined field or property assignments for that type, and will be indexed by a numerical indexer rather than string name. 
    /// <br/> Members to be assigned must have <see cref="IndexableAttribute"/>.
    /// </summary>
    /// <param name="arguments"></param>
    /// <returns></returns>
    public static unsafe byte[] GetArgumentBytes<TSerializerDeserializer>(JsonElement arguments, Type? DataValueAttributeTargetTypeForNumericalIndexing = null) where TSerializerDeserializer : BinarySerializerDeserializerBase
    {


        StringBuilder log = null;

        if (DebugJsonArgumentParsing)
            log.AppendLine("\n\n---- WRITING ARGUMENT BYTES ----");




        var writer = ValueWriter.CreateWithBufferWriter();


        if (arguments.ValueKind == JsonValueKind.Undefined || !arguments.EnumerateObject().Any())
        {
            writer.WriteVariableLengthUnsigned(0);
            return writer.GetSpan().ToArray();
        }



        writer.WriteVariableLengthUnsigned((uint)arguments.EnumerateObject().Count());



        var DataValueTargetTypeMembers = 
            DataValueAttributeTargetTypeForNumericalIndexing == null ? 
            null 
            : 
            DataValueAttributeTargetTypeForNumericalIndexing.GetMembers(BindingFlags.Public | BindingFlags.Instance)
            .Where(x => (x is FieldInfo || x is PropertyInfo) && x.GetCustomAttribute<IndexableAttribute>() != null)
            .OrderBy(x=>x.Name)
            .ToArray();




        foreach (var arg in arguments.EnumerateObject())
        {


            Type deserializeTypeFallback = null;


            if (DebugJsonArgumentParsing)
                log.AppendLine($"\tARGUMENT '{arg.Name}'");


            // ------------- indexer -------------

            if (DataValueAttributeTargetTypeForNumericalIndexing == null)
            {
                writer.WriteVariableLengthUnsigned(BinarySerializerDeserializerBase.GetTypeID<TSerializerDeserializer>(typeof(string)));
                writer.WriteString(arg.Name);

                if (DebugJsonArgumentParsing)
                    log.AppendLine("\tWROTE ARGUMENT STRING ID + STRING KEY");

            }
            else
            {
                var fget = DataValueAttributeTargetTypeForNumericalIndexing.GetField(arg.Name, BindingFlags.Public | BindingFlags.Instance);
                var pget = DataValueAttributeTargetTypeForNumericalIndexing.GetProperty(arg.Name, BindingFlags.Public | BindingFlags.Instance);


                if (fget == null && pget == null)
                    throw new Exception($"Field or property '{arg.Name}' on target type '{DataValueAttributeTargetTypeForNumericalIndexing.FullName}' couldn't be found.");


                if (fget != null && fget.GetCustomAttribute<IndexableAttribute>() == null)
                    throw new Exception($"Field '{arg.Name}' on target type '{DataValueAttributeTargetTypeForNumericalIndexing.FullName}' is not attributed with '{typeof(IndexableAttribute).FullName}'.");


                if (pget != null && pget.GetCustomAttribute<IndexableAttribute>() == null)
                    throw new Exception($"Property '{arg.Name}' on target type '{DataValueAttributeTargetTypeForNumericalIndexing.FullName}' is not attributed with '{typeof(IndexableAttribute).FullName}'.");




                var idx = Array.IndexOf(DataValueTargetTypeMembers, fget == null ? pget : fget);


                if (idx == -1) 
                    throw new Exception(); 


                writer.WriteUnmanaged((byte)idx);


                if (DebugJsonArgumentParsing)
                    log.AppendLine($"\tWROTE ARGUMENT BYTE INDEX - FOUND INDEX [{idx}]");



                deserializeTypeFallback = fget == null ? pget.PropertyType : fget.FieldType;

            }


            // ------------- typed value -------------


            DeserializeTypeGraphFromJson<TSerializerDeserializer>(arg.Value, ref writer, true, deserializeTypeFallback, log);

            if (DebugJsonArgumentParsing)
                log.AppendLine("\t\t\tWROTE ARGUMENT VALUE");
        }


        if (DebugJsonArgumentParsing)
            EngineDebug.Print(log.ToString());


        return writer.GetSpan().ToArray();
    }






    /// <summary>
    /// Debug only development time method. Compiles an arbitrary type graph from json data into serialized binary, that can later be deserialized by the given <typeparamref name="TSerializerDeserializer"/> type.
    /// <br/>
    /// <br/> Explicit typing can be done by providing a dictionary with only Type and Value keys exactly as the json value. Otherwise, the type resolve falls back to <paramref name="typeHint"/>.
    /// <br/>
    /// <br/> An example input might look like this:
    /// <br/>
    /// <br/>
    /// <code>
    /// {
    ///     "Type": "System.Collections.Generic.KeyValuePair&lt;string, object&gt;",
    ///     "Value": { 
    ///         "Key": "ExampleKey",
    ///         "Value": {
    ///             "Type" : "bool",
    ///             "Value" : true
    ///         }
    ///     }
    /// }
    /// </code>
    /// </summary>
    /// <param name="arg"></param>
    /// <param name="typeHint"></param>
    /// <returns></returns>
    /// <exception cref="Exception"></exception>
    private static void DeserializeTypeGraphFromJson<TSerializerDeserializer>(JsonElement arg, ref ValueWriter writer, bool writeTypeID, Type typeHint = null, StringBuilder debugLog = null) where TSerializerDeserializer : BinarySerializerDeserializerBase
    {


        void PushToLog(string txt)
        {
            if (DebugJsonArgumentParsing && debugLog != null)
                debugLog.AppendLine($"\t\t{txt}");
        }





        Type type;



        // type in data

        if (arg.ValueKind == JsonValueKind.Object && arg.GetPropertyCount() == 2 && arg.TryGetProperty("Type", out var typeGet) && arg.TryGetProperty("Value", out JsonElement Value))
        {

            var TypeName = typeRegex().Replace(typeGet.GetString(), match =>
            {
                if (CSharpAliases.TryGetValue(match.Value, out var type))
                    return type.FullName!;

                return match.Value;
            });



            type = Type.GetType(TypeName);



            if (type == null)
                throw new Exception($"Type '{TypeName}' not found - type must be specified in full (with the exception of types with primitive aliases such as 'int') - for example '{typeof(Matrix4x4).FullName}' ");

        }


        // type provided fallback

        else if (typeHint != null)
        {
            type = typeHint;
            Value = arg;
        }



        else
            throw new Exception("Type was not specified and could not be inferred");





        //write type ID

        if (writeTypeID)
        {
            if (DebugJsonArgumentParsing)
                PushToLog($"Wrote type ID for type {type.FullName}");

            writer.WriteVariableLengthUnsigned(BinarySerializerDeserializerBase.GetTypeID<TSerializerDeserializer>(type));
        }





        // ------------- intermediates -------------

        var intermediate = BinarySerializerDeserializerBase.GetTypeIntermediateRepresentation<TSerializerDeserializer>(type);


        if (intermediate == null) 
            throw new Exception($"Unsupported type '{type.FullName}' for deserializer '{typeof(TSerializerDeserializer).FullName}' present in json data");

        

        if (intermediate != type)
        {
            if (DebugJsonArgumentParsing)
                PushToLog($"Intermediate detected, {type.FullName} as {intermediate.FullName}");

            DeserializeTypeGraphFromJson<TSerializerDeserializer>(Value, ref writer, false, intermediate, debugLog);
            return;
        }








        // ------------- primitives -------------

        if (type.GetInterfaces()
            .Any(x =>
                x.IsGenericType
                    ? x.GetGenericTypeDefinition() == typeof(INumber<>)
                    : x == typeof(INumber<>)) || type.IsEnum)
        {

            object value;

            if (type.IsEnum)
            {
                value = Value.Deserialize(type, JsonAssetLoadingOptions);
                type = Enum.GetUnderlyingType(type);
            }


            value = Value.Deserialize(type, JsonAssetLoadingOptions);
            Span<byte> buf = stackalloc byte[Marshal.SizeOf(type)];

            unsafe
            {
                fixed (byte* ptr = buf)
                {
                    Marshal.StructureToPtr(value, (IntPtr)ptr, false);
                    writer.WriteUnmanagedSpan<byte>(buf);
                }
            }

            if (DebugJsonArgumentParsing)
                PushToLog($"Wrote numerical value");

            return;
        }



        if (type == typeof(bool))
        {
            writer.WriteUnmanaged(Value.Deserialize<bool>(JsonAssetLoadingOptions));

            if (DebugJsonArgumentParsing)
                PushToLog($"Wrote bool");

            return;
        }

        if (type == typeof(string))
        {
            writer.WriteString(Value.GetString());

            if (DebugJsonArgumentParsing)
                PushToLog($"Wrote string");

            return;
        }






        // ------------- collections -------------


        if (type.IsArray)
        {
            var arr = Value.Deserialize<JsonElement[]>(JsonAssetLoadingOptions);

            var elementType = type.GetElementType();


            writer.WriteVariableLengthUnsigned((ulong)arr.Length);

            if (DebugJsonArgumentParsing)
                PushToLog($"Wrote array length");

            for (int i = 0; i < arr.Length; i++)
            {
                DeserializeTypeGraphFromJson<TSerializerDeserializer>(arr[i], ref writer, !elementType.IsValueType, elementType, debugLog);


                if (DebugJsonArgumentParsing)
                    PushToLog("Wrote array value");
            }


            if (DebugJsonArgumentParsing)
                PushToLog($"\t => Finished writing array");


            return;
        }





        if (type.GetInterfaces().Where(x =>
                x.IsGenericType
                    ? x.GetGenericTypeDefinition() == typeof(IDictionary<,>)
                    : x == typeof(IDictionary<,>)).Any())
        {
            var keyType = type.GetGenericArguments()[0];
            var valueType = type.GetGenericArguments()[1];



            writer.WriteVariableLengthUnsigned((ulong)Value.EnumerateObject().Count());


            if (DebugJsonArgumentParsing)
                PushToLog($"Wrote dictionary length");


            foreach (var kv in Value.EnumerateObject())
            {
                writer.WriteVariableLengthUnsigned(BinarySerializerDeserializerBase.GetTypeID<TSerializerDeserializer>(typeof(string)));
                writer.WriteString(kv.Name);

                if (DebugJsonArgumentParsing)
                {

                    PushToLog($"Wrote type ID for type '{typeof(string).FullName}'");
                    PushToLog($"Wrote dictionary string key");
                }

                DeserializeTypeGraphFromJson<TSerializerDeserializer>(kv.Value, ref writer, !valueType.IsValueType, valueType, debugLog);


                if (DebugJsonArgumentParsing)
                    PushToLog($"Wrote dictionary value");
            }


            if (DebugJsonArgumentParsing)
                PushToLog($"\t => Finished writing dictionary");


            return;
        }






        // -------------- other types --------------


        var wr = ValueWriter.CreateWithBufferWriter();

        foreach (var m in type.GetFields(BindingFlags.Public | BindingFlags.Instance))
        {
            if (Value.TryGetProperty(m.Name, out var fget))
                DeserializeTypeGraphFromJson<TSerializerDeserializer>(fget, ref writer, !m.FieldType.IsValueType, m.FieldType, debugLog);

            else throw new Exception($"Required fields missing from '{type.FullName}' declaration");
        }

        if (DebugJsonArgumentParsing)
            PushToLog($"\t => Finished writing complex type '{type.FullName}'");

    }







    private static readonly Dictionary<string, Type> CSharpAliases =
        new(StringComparer.Ordinal)
        {
            ["bool"] = typeof(bool),
            ["byte"] = typeof(byte),
            ["sbyte"] = typeof(sbyte),
            ["short"] = typeof(short),
            ["ushort"] = typeof(ushort),
            ["int"] = typeof(int),
            ["uint"] = typeof(uint),
            ["long"] = typeof(long),
            ["ulong"] = typeof(ulong),
            ["float"] = typeof(float),
            ["double"] = typeof(double),
            ["half"] = typeof(Half),
            ["char"] = typeof(char),
            ["string"] = typeof(string),
            ["object"] = typeof(object)
        };

    [System.Text.RegularExpressions.GeneratedRegex(@"\b[a-zA-Z_][a-zA-Z0-9_]*\b")]
    private static partial System.Text.RegularExpressions.Regex typeRegex();



#endif



}