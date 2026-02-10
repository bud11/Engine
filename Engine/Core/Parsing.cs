


namespace Engine.Core;

using Engine.GameResources;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;


#if DEBUG
using System.Text.Json;
#endif





/// <summary>
/// A <see cref="uint"/> index reference to a <see cref="GameObject"/> within the context of a parsed file.
/// </summary>
/// <param name="Reference"></param>
/// 
[StructLayout(LayoutKind.Sequential, Pack = 1, Size = sizeof(uint))]
public readonly record struct GameObjectReference(uint Reference);



/// <summary>
/// A <see cref="uint"/> index reference to a <see cref="GameResource"/> within the context of a parsed file.
/// </summary>
/// <param name="Reference"></param>
/// 
[StructLayout(LayoutKind.Sequential, Pack = 1, Size = sizeof(uint))]
public readonly record struct GameResourceReference(uint Reference);








public static partial class Parsing
{




    /// <summary>
    /// Reads any unmanaged type. Platform agnostic. Also see <seealso cref="ReadUnmanagedTypeArray{T}(Stream, uint)"/>
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="reader"></param>
    /// <returns></returns>
    public static unsafe T ReadUnmanagedType<T>(this Stream reader) where T : unmanaged
    {
        Span<byte> buf = stackalloc byte[sizeof(T)];
        reader.ReadExactly(buf);

        fixed (byte* p = buf)
            return *(T*)p;
    }



    /// <summary>
    /// Reads an array of any unmanaged type. Platform agnostic. Also see <seealso cref="ReadUnmanagedType{T}(Stream)"/>
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="reader"></param>
    /// <param name="count"></param>
    /// <returns></returns>
    public static unsafe T[] ReadUnmanagedTypeArray<T>(this Stream reader, uint count) where T : unmanaged
    {
        if (count == 0) return null;

        T[] result = new T[count];

        Span<byte> span = MemoryMarshal.AsBytes(result.AsSpan());
        reader.ReadExactly(span);

        return result;
    }













    /// <summary>
    /// Reads any unmanaged type. Platform agnostic. Also see <seealso cref="ReadUnmanagedTypeArray{T}(BinaryReader, uint)"/>
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="reader"></param>
    /// <returns></returns>
    public static unsafe T ReadUnmanagedType<T>(this BinaryReader reader) where T : unmanaged
    {
        fixed (byte* buf = reader.ReadBytes(sizeof(T)))
            return *(T*)buf;
    }



    /// <summary>
    /// Reads an array of any unmanaged type. Platform agnostic. Also see <seealso cref="ReadUnmanagedType{T}(BinaryReader)"/>
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="reader"></param>
    /// <param name="count"></param>
    /// <returns></returns>
    public static unsafe T[] ReadUnmanagedTypeArray<T>(this BinaryReader reader, uint count) where T : unmanaged
    {

        if (count == 0) return null;


        T[] result = new T[count];

        // Create a span over the underlying T[] memory
        Span<byte> span = MemoryMarshal.AsBytes(result.AsSpan());

        reader.Read(span);

        return result;
    }




    /// <summary>
    /// Returns a UTF8 string with a <see cref="uint"/> length prefix as a raw byte array.
    /// </summary>
    /// <param name="str"></param>
    /// <returns></returns>
    public static byte[] GetUintLengthPrefixedUTF8StringAsBytes(string str)
    {
        if (str == string.Empty || str == null)
            return BitConverter.GetBytes(0u);


        var utf8 = Encoding.UTF8.GetBytes(str);
        return [
            .. BitConverter.GetBytes((uint)utf8.Length),
        .. utf8
        ];
    }



    public static string ReadUintLengthPrefixedUTF8String(this BinaryReader reader)
    {
        var len = (int)reader.ReadUInt32();

        return len == 0 ? string.Empty : Encoding.UTF8.GetString(reader.ReadBytes(len));
    }


    public static unsafe string ReadUintLengthPrefixedUTF8String(this Stream reader)
    {
        var len = reader.ReadUnmanagedType<uint>();

        return len == 0 ? string.Empty : Encoding.UTF8.GetString(reader.ReadUnmanagedTypeArray<byte>(len));
    }
















    /// <summary>
    /// Reads a dictionary of supported arguments from an internal byte format. Also see <seealso cref="WriteArgumentBytes(JsonElement, Dictionary{Type, Type})"/> / <see cref="WriteArgumentBytes(JsonElement, System.Reflection.MethodInfo, Dictionary{Type, Type})"/>.
    /// <br/> <paramref name="resourceReferenceLookup"/> is a special parameter to faciliate any <see cref="FixedSerializableParameterTypes.ResourceReference"/>s.
    /// </summary>
    /// <param name="stream"></param>
    /// <returns></returns>
    /// <exception cref="Exception"></exception>
    public static Dictionary<string, object> ReadArgumentBytes(Stream stream, GameResource[] resourceReferenceLookup = null)
    {

        var argCount = stream.ReadUnmanagedType<byte>();


        if (argCount == 0) return null;


        var final = new Dictionary<string, object>(capacity: argCount);




        for (byte i = 0; i < argCount; i++)
        {
            var argName = stream.ReadUintLengthPrefixedUTF8String();
            var argType = (FixedSerializableParameterTypes)stream.ReadByte();

            object argument = null;

            uint count = stream.ReadUnmanagedType<uint>();
            bool isArray = count != uint.MaxValue;




            switch (argType)
            {

                case FixedSerializableParameterTypes.Half:
                    readUnmanaged<Half>();
                    break;

                case FixedSerializableParameterTypes.Float:
                    readUnmanaged<float>();
                    break;

                case FixedSerializableParameterTypes.Int:
                    readUnmanaged<int>();
                    break;

                case FixedSerializableParameterTypes.Uint:
                    readUnmanaged<uint>();
                    break;

                case FixedSerializableParameterTypes.Short:
                    readUnmanaged<short>();
                    break;

                case FixedSerializableParameterTypes.UShort:
                    readUnmanaged<ushort>();
                    break;

                case FixedSerializableParameterTypes.Byte:
                    readUnmanaged<byte>();
                    break;


                case FixedSerializableParameterTypes.SByte:
                    readUnmanaged<sbyte>();
                    break;

                case FixedSerializableParameterTypes.Long:
                    readUnmanaged<long>();
                    break;





                case FixedSerializableParameterTypes.ULong:
                    readUnmanaged<ulong>();
                    break;

                case FixedSerializableParameterTypes.Double:
                    readUnmanaged<double>();
                    break;

                case FixedSerializableParameterTypes.Bool:
                    readUnmanaged<bool>();
                    break;

                case FixedSerializableParameterTypes.Vector2:
                    readUnmanaged<Vector2>();
                    break;

                case FixedSerializableParameterTypes.Vector3:
                    readUnmanaged<Vector3>();
                    break;

                case FixedSerializableParameterTypes.Vector4:
                    readUnmanaged<Vector4>();
                    break;




                case FixedSerializableParameterTypes.Matrix4x4:
                    readUnmanaged<Matrix4x4>();
                    break;




                case FixedSerializableParameterTypes.String:
                    if (!isArray) argument = stream.ReadUintLengthPrefixedUTF8String();
                    else
                    {
                        string[] arr = new string[count];
                        for (int s = 0; s < arr.Length; s++)
                            arr[s] = stream.ReadUintLengthPrefixedUTF8String();
                        argument = arr;
                    }
                    break;



                case FixedSerializableParameterTypes.ObjectReference:
                    readUnmanaged<GameObjectReference>();
                    break;




                case FixedSerializableParameterTypes.ResourceReference:
                    if (!isArray) argument = resourceReferenceLookup[stream.ReadUnmanagedType<uint>()];
                    else
                    {
                        GameResource[] arr = new GameResource[count];

                        for (int x = 0; x < arr.Length; x++)
                            arr[x] = resourceReferenceLookup[stream.ReadUnmanagedType<uint>()];

                        argument = arr;
                    }
                    break;



                default:
                    throw new NotSupportedException();
            }



            final[argName] = argument;



            void readUnmanaged<T>() where T : unmanaged
            {
                if (!isArray) argument = stream.ReadUnmanagedType<T>();
                else argument = stream.ReadUnmanagedTypeArray<T>(count);
            }
        }


        return final;
    }









    public static async Task<GameResource[]> LoadResourceBytes(Stream reader, string FilePath)
    {

        uint resourceCount = reader.ReadUnmanagedType<uint>();

        if (resourceCount == 0) return null;


        GameResource[] resources = new GameResource[resourceCount];


        (ushort type, bool external, byte[] data)[] ress = new (ushort type, bool external, byte[] data)[resourceCount];
        for (int r = 0; r < resourceCount; r++)
        {
            var type = reader.ReadUnmanagedType<ushort>();
            bool external = reader.ReadByte() == 1;

            byte[] data;

            if (external)
            {
                var path = reader.ReadUintLengthPrefixedUTF8String();
                data = Encoding.UTF8.GetBytes(path);
            }
            else
            {
                data = reader.ReadUnmanagedTypeArray<byte>(reader.ReadUnmanagedType<uint>());
            }

            ress[r] = (type, external, data);
        }


        await Parallel.ForAsync<uint>(0, resourceCount, async (rIndex, cancellation) =>
        {
            var resource = ress[rIndex];


            string key = null;
            if (resource.external) key = Loading.RelativePathToFullPath(Encoding.UTF8.GetString(resource.data), FilePath[..(FilePath.LastIndexOf('/')+1)]);
            else key = FilePath + "_" + rIndex;


            GameResource res;

            if (resource.external)
                res = resources[rIndex] = await LoadGameResourceFromTypeID(resource.type, key);

            else
            {
                using (var memstream = new Loading.AssetByteStream(new MemoryStream(resource.data), resource.data.Length)) 
                    res = resources[rIndex] = await Loading.InternalLoadOrFetchResource(key, async () => await ConstructGameResourceFromTypeID(resource.type, memstream, key));

            }


            res.Init();

            resources[rIndex].AddUser();  //this scene


        });


        return resources;
    }





    private enum FixedSerializableParameterTypes : byte
    {
        Half,
        Float,

        Int,
        Uint,

        Short,
        UShort,

        Byte,
        SByte,

        Long,
        ULong,
        Double,

        Bool,


        Vector2,
        Vector3,
        Vector4,

        Matrix4x4,

        String,


        /// <summary>
        /// Represents a reference to an object via a <see cref="uint"/> index within the context of a parsed file, which will be converted to an actual <see cref="GameObject"/> reference at usage time.
        /// </summary>
        ObjectReference,

        /// <summary>
        /// Represents a reference to a resource via a <see cref="uint"/> index within the context of a parsed file, which will be converted to an actual <see cref="GameResource"/> reference at usage time.
        /// </summary>
        ResourceReference

    }








#if DEBUG


    /// <summary>
    /// Converts an unmanaged struct to a byte[].
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="str"></param>
    /// <returns></returns>
    public static byte[] StructToBytes<T>(T str) where T : unmanaged 
        => MemoryMarshal.AsBytes(MemoryMarshal.CreateSpan(ref str, 1)).ToArray();





    /// <summary>
    /// Converts an unknown unmanaged struct to a byte[].
    /// </summary>
    /// <param name="obj"></param>
    /// <returns></returns>
    /// <exception cref="ArgumentNullException"></exception>
    /// <exception cref="ArgumentException"></exception>
    public static byte[] BoxedStructToBytes(object obj)
    {
        ArgumentNullException.ThrowIfNull(obj);

        Type type = obj.GetType();

        if (!type.IsValueType)
            throw new ArgumentException("Object is not a value type.");

        int size = Marshal.SizeOf(type);
        byte[] bytes = new byte[size];
        IntPtr ptr = Marshal.AllocHGlobal(size);

        try
        {
            Marshal.StructureToPtr(obj, ptr, false);
            Marshal.Copy(ptr, bytes, 0, size);
        }
        finally
        {
            Marshal.FreeHGlobal(ptr);
        }

        return bytes;
    }







    /// <summary>
    /// Returns the enum value T based on the string.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="parse"></param>
    /// <returns></returns>
    /// <exception cref="Exception"></exception>
    public static T EnumParse<T>(string parse, bool ignoreCase = true) where T : struct, Enum
    {
        if (Enum.TryParse<T>(parse, ignoreCase: ignoreCase, out var val)) return val;
        throw new Exception($"Invalid enum value '{parse}' for {typeof(T).Name}");
    }












    public static async Task WriteResourceBytes(List<byte> final, JsonElement[] resourcesArr, string filePath)
    {

        final.AddRange(BitConverter.GetBytes((uint)resourcesArr.Length));

        var finalResourcesArrayBytes = new List<byte>[resourcesArr.Length];



        //prepare each resource in async fashion

        await Parallel.ForAsync<uint>(0, (uint)resourcesArr.Length, async (rIndex, cancellation) =>
        {

            var resourceFinalBytes = new List<byte>();


            var resource = resourcesArr[rIndex];


            var resourceTypeName = resource.GetProperty("type").GetString();

            var resourceType = Type.GetType(resourceTypeName);



            if (resourceType == null)
                throw new Exception($"Type '{resourceTypeName}' not found - type must be specified in full, for example 'Engine.Core.GameResource' ");




            //write type

            resourceFinalBytes.AddRange(BitConverter.GetBytes(GetGameResourceTypeID(resourceType)));

            var external = resource.GetProperty("external").GetBoolean();


            //write if external

            resourceFinalBytes.Add((byte)(external ? 1 : 0));


            //if external, write length prefixed path
            if (external)
            {

                resourceFinalBytes.AddRange(GetUintLengthPrefixedUTF8StringAsBytes(resource.GetProperty("path").GetString()));
            }


            //otherwise, write length prefixed final data
            else
            {
                //get resource properties

                var exHint = resource.GetProperty("extensionHint").GetString();
                var dataproperty = resource.GetProperty("data");
                byte[] data;


                //load data as either a literal byte array (ie [0,255,etc]) or a self contained json string

                bool JSON = false;
                if (resource.TryGetProperty("dataIsJson", out var isjson) && isjson.GetBoolean())
                {
                    data = Encoding.UTF8.GetBytes(dataproperty.GetRawText());
                    JSON = true;
                }

                else
                    data = [.. dataproperty.EnumerateArray().Select(x => x.GetByte())];



                //find extension and get the final asset bytes if applicable

                bool extensionHintFound = Loading.GameResourceFileAssociations.TryGetValue(exHint, out var AssetFoundType);


                if (extensionHintFound)
                {
                    data = await (Task<byte[]>)AssetFoundType.GetMethod(nameof(GameResource.ConvertToFinalAssetBytes), System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static).
                    Invoke(null, [data, $"{filePath}/resource_{rIndex}.{exHint}"]);
                }

                else if (JSON)
                    throw new Exception("serializing raw json data into final release-ready scene binary - likely unintentional, disable this exeception otherwise");



                //append final asset bytes + length

                resourceFinalBytes.AddRange(BitConverter.GetBytes((uint)data.Length));
                resourceFinalBytes.AddRange(data);
            }

            finalResourcesArrayBytes[rIndex] = resourceFinalBytes;
        });



        //add all resources to final output

        for (int i = 0; i < finalResourcesArrayBytes.Length; i++)
            final.AddRange(finalResourcesArrayBytes[i]);

    }








    public readonly record struct DeserializedArgument (bool hasValue, object value);





    /// <summary>
    /// Writes parameters for a particular method. The json data should not specify the parameter type.
    /// <br/> See <see cref="FixedSerializableParameterTypes"/> for a list of currently compatible types.
    /// </summary>
    /// <param name="arguments"></param>
    /// <param name="method"></param>
    /// <param name="typeReplacement"></param>
    /// <returns></returns>
    public static unsafe List<byte> WriteArgumentBytes(JsonElement arguments, System.Reflection.MethodInfo method)
    {
        var dict = arguments.Deserialize<Dictionary<string, JsonElement>>();

        List<byte> final = [(byte)dict.Count];


        OrderedDictionary<string, Type> signature = new();

        System.Reflection.ParameterInfo[] array = method.GetParameters();

        for (int i = 0; i < array.Length; i++)
        {
            var arg = array[i];

            var type = arg.ParameterType;

            signature.Insert(i, arg.Name, arg.ParameterType);
        }


        foreach (var kv in dict)
            final.AddRange(SerializeParameter(kv.Key, signature[kv.Key].FullName, kv.Value));


        return final;
    }





    /// <summary>
    /// Writes arbitrary parameters each with a type ID and name. The json data needs to specify the type of each parameter via its full C# type name or appropriate primitive alias.
    /// <br/> See <see cref="FixedSerializableParameterTypes"/> for a list of currently compatible types.
    /// </summary>
    /// <param name="arguments"></param>
    /// <param name="typeReplacement"></param>
    /// <returns></returns>
    public static unsafe List<byte> WriteArgumentBytes(JsonElement arguments)
    {
        var dict = arguments.Deserialize<Dictionary<string,JsonElement>>();

        List<byte> final = [(byte)dict.Count];


        foreach (var arg in dict)
        {
            var argTypeName = arg.Value.GetProperty("type").GetString();
            var argument = arg.Value.GetProperty("value");

            final.AddRange(SerializeParameter(arg.Key, argTypeName, argument));
        }


        return final;
    }









    private static List<byte> SerializeParameter(string argName, string typeName, JsonElement value)
    {
        List<byte> final = new();



        bool isArray = typeName.EndsWith("[]");
        if (isArray)
            typeName = typeName.Replace("[]", string.Empty);


        typeName = typeName switch
        {
            "bool" => "System.Boolean",

            "byte" => "System.Byte",
            "sbyte" => "System.SByte",

            "short" => "System.Int16",
            "ushort" => "System.UInt16",

            "int" => "System.Int32",
            "uint" => "System.UInt32",

            "long" => "System.Int64",
            "ulong" => "System.UInt64",

            "float" => "System.Single",
            "double" => "System.Double",
            "half" => "System.Half",

            "char" => "System.Char",
            "string" => "System.String",
            "object" => "System.Object",

            _ => typeName
        };

        if (isArray) 
            typeName += "[]";



        var type = Type.GetType(typeName);



        if (type == null)
            throw new Exception($"Type '{typeName}' not found - type must be specified in full (with the exception of types with built in aliases) - for example 'System.Numerics.Matrix4x4' ");


        replace(typeof(GameObject), typeof(GameObjectReference));
        replace(typeof(GameResource), typeof(GameResourceReference));



        void replace(Type detection, Type replacement)
        {
            if ((type.IsArray ? type.GetElementType() : type).IsAssignableTo(detection))
                type = type.IsArray ? replacement.MakeArrayType() : replacement;

        }





        //name
        final.AddRange(GetUintLengthPrefixedUTF8StringAsBytes(argName));

        //type (fixed lookup in place for future proofing incase arbitrary type idx source generation is introduced)
        final.Add((byte)TypeToEnumLookup[(type.IsArray ? type.GetElementType() : type)]);




        if (type.IsArray)
        {
            var arr = value.Deserialize<JsonElement[]>();

            final.AddRange(BitConverter.GetBytes((uint)arr.Length));  //array length

            var elementtype = type.GetElementType();

            for (int i = 0; i < arr.Length; i++)
                WriteType(elementtype, arr[i]);

        }
        else
        {
            final.AddRange(BitConverter.GetBytes(uint.MaxValue));
            WriteType(type, value);
        }




        return final;





        // ======     only writes unmanaged types with the exception of string   ======

        void WriteType(Type type, JsonElement? jsonValue)
        {





            if (type == typeof(bool))
                writeUnmanaged(x => x.GetBoolean());

            else if (type == typeof(byte))
                writeUnmanaged(x => x.GetByte());

            else if (type == typeof(sbyte))
                writeUnmanaged(x => x.GetSByte());

            else if (type == typeof(char))
                writeUnmanaged(x => x.GetString()[0]);

            else if (type == typeof(short))
                writeUnmanaged(x => x.GetInt16());

            else if (type == typeof(ushort))
                writeUnmanaged(x => x.GetUInt16());

            else if (type == typeof(int))
                writeUnmanaged(x => x.GetInt32());


            else if (type == typeof(uint)
                || type == typeof(GameObjectReference)
                || type == typeof(GameResourceReference))
                writeUnmanaged(x => x.GetUInt32());


            else if (type == typeof(long))
                writeUnmanaged(x => x.GetInt64());

            else if (type == typeof(ulong))
                writeUnmanaged(x => x.GetUInt64());

            else if (type == typeof(Half))
                writeUnmanaged(x => (Half)x.GetSingle());

            else if (type == typeof(float))
                writeUnmanaged(x => x.GetSingle());

            else if (type == typeof(double))
                writeUnmanaged(x => x.GetDouble());

            else if (type == typeof(decimal))
                writeUnmanaged(x => x.GetDecimal());


            else if (type.IsEnum)
            {
                if (jsonValue == null)
                    final.AddRange(BoxedStructToBytes(Convert.ChangeType(Activator.CreateInstance(type.GetEnumUnderlyingType()), type.GetEnumUnderlyingType())));

                else
                {
                    Type underlying = Enum.GetUnderlyingType(type);

                    object value = underlying switch
                    {
                        Type t when t == typeof(byte) => jsonValue.Value.GetByte(),
                        Type t when t == typeof(sbyte) => (sbyte)jsonValue.Value.GetInt32(),
                        Type t when t == typeof(short) => jsonValue.Value.GetInt16(),
                        Type t when t == typeof(ushort) => jsonValue.Value.GetUInt16(),
                        Type t when t == typeof(int) => jsonValue.Value.GetInt32(),
                        Type t when t == typeof(uint) => jsonValue.Value.GetUInt32(),
                        Type t when t == typeof(long) => jsonValue.Value.GetInt64(),
                        Type t when t == typeof(ulong) => jsonValue.Value.GetUInt64(),
                        Type t when t == typeof(string) => typeof(Parsing).GetMethod(nameof(EnumParse)).MakeGenericMethod(underlying).Invoke(null, [jsonValue.Value, jsonValue.Value.GetString()]),
                        _ => throw new NotSupportedException()
                    };

                    final.AddRange(BoxedStructToBytes(value));
                    return;
                }
            }




            else if (type == typeof(string))
                final.AddRange(GetUintLengthPrefixedUTF8StringAsBytes(jsonValue == null ? string.Empty : jsonValue.Value.GetString()));


            else
            {

                //if not public + unmanaged, throw
                if (!type.IsValueType ||
                    type.IsEnum ||
                    type.ContainsGenericParameters ||
                    type.IsPointer ||
                    !IsUnManaged(type) ||
                    !IsFullyPublic(type))
                    throw new NotSupportedException();



                foreach (var field in type.GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public))
                {
                    if (jsonValue == null || !jsonValue!.Value.TryGetProperty(field.Name, out var fieldValue))
                        WriteType(field.FieldType, null);

                    else
                        WriteType(field.FieldType, fieldValue);
                }

            }





            unsafe void writeUnmanaged<T>(Func<JsonElement, T> serialize) where T : unmanaged
                => final.AddRange(StructToBytes(jsonValue == null ? default(T) : serialize.Invoke(jsonValue!.Value)));



            static bool IsFullyPublic(Type type)
            {
                while (type != null)
                {
                    if (type.IsNested)
                    {
                        if (!type.IsNestedPublic)
                            return false;

                        type = type.DeclaringType;
                    }
                    else
                    {
                        if (!type.IsPublic)
                            return false;

                        break;
                    }
                }

                return true;
            }

        }

    }




    class U<T> where T : unmanaged { }
    static bool IsUnManaged(this Type t)
    {
        try { typeof(U<>).MakeGenericType(t); return true; }
        catch (Exception) { return false; }
    }



    private static Dictionary<Type, FixedSerializableParameterTypes> TypeToEnumLookup = new()
    {
        { typeof(Half),   FixedSerializableParameterTypes.Half },
        { typeof(float),  FixedSerializableParameterTypes.Float },
        { typeof(double), FixedSerializableParameterTypes.Double },

        { typeof(sbyte), FixedSerializableParameterTypes.SByte },
        { typeof(short), FixedSerializableParameterTypes.Short },
        { typeof(int),   FixedSerializableParameterTypes.Int },
        { typeof(long),  FixedSerializableParameterTypes.Long },

        { typeof(byte),   FixedSerializableParameterTypes.Byte },
        { typeof(ushort), FixedSerializableParameterTypes.UShort },
        { typeof(uint),   FixedSerializableParameterTypes.Uint },
        { typeof(ulong),  FixedSerializableParameterTypes.ULong },

        { typeof(bool),   FixedSerializableParameterTypes.Bool },
        { typeof(string), FixedSerializableParameterTypes.String },

        { typeof(Vector2), FixedSerializableParameterTypes.Vector2 },
        { typeof(Vector3), FixedSerializableParameterTypes.Vector3 },
        { typeof(Vector4), FixedSerializableParameterTypes.Vector4 },
        { typeof(Matrix4x4), FixedSerializableParameterTypes.Matrix4x4 },

        { typeof(GameObjectReference), FixedSerializableParameterTypes.ObjectReference },
        { typeof(GameResourceReference), FixedSerializableParameterTypes.ResourceReference },
    };




#endif



}