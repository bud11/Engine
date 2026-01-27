


namespace Engine.Core;

using Engine.GameResources;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using static Engine.Core.EngineMath;


public static class Parsing
{

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
    /// Parses a struct from json data using a type object, rather than generic, via reflection.
    /// </summary>
    /// <param name="type"></param>
    /// <param name="json"></param>
    /// <returns></returns>
    public static object ParseStructFromJson(Type type, JsonElement json)
        => typeof(JsonSerializer)
                    .GetMethod(nameof(JsonSerializer.Deserialize))
                    .MakeGenericMethod(type)
                    .Invoke(json, null);






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



            //write type

            resourceFinalBytes.AddRange(Parsing.GetUintLengthPrefixedUTF8StringAsBytes(resource.GetProperty("type").GetString()));

            var external = resource.GetProperty("external").GetBoolean();


            //write if external

            resourceFinalBytes.Add((byte)(external ? 1 : 0));


            //if external, write length prefixed path
            if (external)
            {

                resourceFinalBytes.AddRange(Parsing.GetUintLengthPrefixedUTF8StringAsBytes(resource.GetProperty("path").GetString()));
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

                bool extensionHintFound = GameResource.GameResourceFileExtensionMap.TryGetValue(exHint, out var AssetFoundType);

                if (extensionHintFound)
                {
                    data = await (Task<byte[]>)typeof(StaticVirtuals).GetMethod(nameof(StaticVirtuals.Engine_Core_GameResource_ConvertToFinalAssetBytes), System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
                    .MakeGenericMethod(AssetFoundType).
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











    /// <summary>
    /// Writes a json dictionary of supported arguments to a compact byte form.
    /// </summary>
    /// <param name="final"></param>
    /// <param name="args"></param>
    /// <exception cref="Exception"></exception>
    public static unsafe void WriteArgumentBytes(List<byte> final, Dictionary<string, JsonElement> args)
    {

        final.Add((byte)args.Count);



        //writes each argument.

        foreach (var arg in args)
        {

            var argTypeName = arg.Value.GetProperty("type").GetString().ToLowerInvariant();
            var argument = arg.Value.GetProperty("value");




            bool isArray = argTypeName.EndsWith("[]");

            if (isArray)
                argTypeName = argTypeName[..^"[]".Length];



            final.AddRange(GetUintLengthPrefixedUTF8StringAsBytes(arg.Key));


            final.Add((byte)EnumParse<SerializedArgumentTypes>(argTypeName));




            switch (argTypeName)
            {

                case "half":
                    writeUnmanaged(x => (Half)x.GetSingle());
                    break;


                case "float":
                    writeUnmanaged(x => x.GetSingle());
                    break;


                case "int":
                    writeUnmanaged(x => x.GetInt32());
                    break;

                case "uint":
                case "objectreference":
                case "resourcereference":
                    writeUnmanaged(x => x.GetUInt32());
                    break;

                case "short":
                    writeUnmanaged(x => x.GetInt16());
                    break;

                case "ushort":
                    writeUnmanaged(x => x.GetUInt16());
                    break;

                case "byte":
                    writeUnmanaged(x => x.GetByte());
                    break;

                case "sbyte":
                    writeUnmanaged(x => x.GetSByte());
                    break;

                case "long":
                    writeUnmanaged(x => x.GetInt64());
                    break;

                case "ulong":
                    writeUnmanaged(x => x.GetUInt64());
                    break;

                case "double":
                    writeUnmanaged(x => x.GetDouble());
                    break;

                case "bool":
                    writeUnmanaged(x => x.GetBoolean());
                    break;

                case "vector2":
                    writePackedFloats<Vector2>(2);
                    break;

                case "vector3":
                    writePackedFloats<Vector3>(3);
                    break;

                case "vector4":
                    writePackedFloats<Vector4>(4);
                    break;

                case "matrix3x3":
                    writePackedFloats<Matrix3x3>(9);
                    break;

                case "matrix4x4":
                    writePackedFloats<Matrix4x4>(16);
                    break;


                case "string":
                case "objectpath":
                    if (!isArray)
                    {
                        final.AddRange(BitConverter.GetBytes(uint.MaxValue));
                        final.AddRange(GetUintLengthPrefixedUTF8StringAsBytes(argument.GetString()));
                    }
                    else
                    {
                        var stringArray = JsonSerializer.Deserialize<string[]>(argument);

                        final.AddRange(BitConverter.GetBytes((uint)stringArray.Length));

                        for (int i = 0; i < stringArray.Length; i++)
                            final.AddRange(GetUintLengthPrefixedUTF8StringAsBytes(stringArray[i]));
                    }
                    break;


                default:
                    throw new NotSupportedException();
            }




            unsafe void writePackedFloats<T>(uint count) where T : unmanaged
            {
                writeUnmanaged<T>(x =>
                {
                    var arr = JsonSerializer.Deserialize<float[]>(x);

                    if (arr.Length != count) 
                        throw new Exception($"invalid component count for argument {arg.Key}");

                    T val;
                    fixed (float* p = arr)
                        val = *(T*)p;

                    return val;
                } 
                );
            }




            unsafe void writeUnmanaged<T>(Func<JsonElement, T> serialize) where T : unmanaged
            {
               
                if (!isArray)
                {
                    final.AddRange(BitConverter.GetBytes(uint.MaxValue));
                    final.AddRange(StructToBytes(serialize.Invoke(argument)));
                }
                else
                {
                    T[] arr = new T[argument.EnumerateArray().Count()];
                    var idx = 0;
                    foreach (var value in argument.EnumerateArray())
                        arr[idx++] = serialize.Invoke(value);

                    byte[] byteArray = new byte[arr.Length * sizeof(T)];
                    Buffer.BlockCopy(arr, 0, byteArray, 0, byteArray.Length);

                    final.AddRange(BitConverter.GetBytes((uint)arr.Length));
                    final.AddRange(byteArray);
                }
            }
        }
    }



#endif







    /// <summary>
    /// Reads any unmanaged type. Platform agnostic. Also see <seealso cref="ReadTypeArray{T}(BinaryReader, uint)"/>
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="reader"></param>
    /// <returns></returns>
    public static unsafe T ReadType<T>(this BinaryReader reader) where T : unmanaged
    {
        fixed (byte* buf = reader.ReadBytes(sizeof(T)))
            return *(T*)buf;
    }



    /// <summary>
    /// Reads an array of any unmanaged type. Platform agnostic. Also see <seealso cref="ReadType{T}(BinaryReader)"/>
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="reader"></param>
    /// <param name="count"></param>
    /// <returns></returns>
    public static unsafe T[] ReadTypeArray<T>(this BinaryReader reader, uint count) where T : unmanaged
    {
        uint totalSize = (uint)(sizeof(T) * count);
        byte[] buffer = reader.ReadBytes((int)totalSize);

        T[] result = new T[count];

        fixed (byte* src = buffer)
        fixed (T* dst = result)
            Unsafe.CopyBlockUnaligned(dst, src, totalSize);

        return result;
    }



    /// <summary>
    /// Returns a UTF8 string with a <see cref="uint"/> length prefix as a raw byte array.
    /// </summary>
    /// <param name="str"></param>
    /// <returns></returns>
    public static byte[] GetUintLengthPrefixedUTF8StringAsBytes(string str)
    {
        var utf8 = Encoding.UTF8.GetBytes(str);
        return [
            .. BitConverter.GetBytes((uint)utf8.Length),
        .. utf8
        ];
    }


    public static unsafe string ReadUintLengthPrefixedUTF8String(this BinaryReader reader)
        => Encoding.UTF8.GetString(reader.ReadBytes((int)reader.ReadUInt32()));




    /// <summary>
    /// Represents the types that can be loaded/deloaded from resources. See <see cref="ReadArgumentBytes"/> and <see cref="WriteArgumentBytes"/>.
    /// </summary>
    private enum SerializedArgumentTypes : byte
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

        Matrix3x3,
        Matrix4x4,

        String,


        /// <summary>
        /// Represents a reference to an object via a <see cref="uint"/> index.
        /// </summary>
        ObjectReference,

        /// <summary>
        /// Represents a reference to an object via a <see cref="string"/> path relative to the scene root.
        /// </summary>
        ObjectPath,

        /// <summary>
        /// Represents a reference to a resource via a <see cref="uint"/> index.
        /// </summary>
        ResourceReference

    }







    /// <summary>
    /// Reads a dictionary of supported arguments from an internal byte format. Also see <seealso cref="WriteArgumentBytes"/>.
    /// <br/> <paramref name="resourceReferenceLookup"/> is a special parameter to faciliate any <see cref="SerializedArgumentTypes.ResourceReference"/>s.
    /// </summary>
    /// <param name="reader"></param>
    /// <returns></returns>
    /// <exception cref="Exception"></exception>
    public static Dictionary<string, object> ReadArgumentBytes(BinaryReader reader, GameResource[] resourceReferenceLookup = null)
    {



        var argCount = reader.ReadByte();


        if (argCount == 0) return null;




        var final = new Dictionary<string, object>(capacity: argCount);


        for (byte i = 0; i < argCount; i++)
        {
            var argName = reader.ReadUintLengthPrefixedUTF8String();
            var argType = (SerializedArgumentTypes)reader.ReadByte();

            object argument = null;

            uint count = reader.ReadUInt32();
            bool isArray = count != uint.MaxValue;


            switch (argType)
            {

                case SerializedArgumentTypes.Half:
                    readUnmanaged<Half>();
                    break;

                case SerializedArgumentTypes.Float:
                    readUnmanaged<float>();
                    break;

                case SerializedArgumentTypes.Int:
                    readUnmanaged<int>();
                    break;

                case SerializedArgumentTypes.Uint:
                    readUnmanaged<uint>();
                    break;

                case SerializedArgumentTypes.Short:
                    readUnmanaged<short>();
                    break;

                case SerializedArgumentTypes.UShort:
                    readUnmanaged<ushort>();
                    break;

                case SerializedArgumentTypes.Byte:
                    readUnmanaged<byte>();
                    break;

                case SerializedArgumentTypes.SByte:
                    readUnmanaged<sbyte>();
                    break;

                case SerializedArgumentTypes.Long:
                    readUnmanaged<long>();
                    break;

                case SerializedArgumentTypes.ULong:
                    readUnmanaged<ulong>();
                    break;

                case SerializedArgumentTypes.Double:
                    readUnmanaged<double>();
                    break;

                case SerializedArgumentTypes.Bool:
                    readUnmanaged<bool>();
                    break;

                case SerializedArgumentTypes.Vector2:
                    readUnmanaged<Vector2>();
                    break;

                case SerializedArgumentTypes.Vector3:
                    readUnmanaged<Vector3>();
                    break;

                case SerializedArgumentTypes.Vector4:
                    readUnmanaged<Vector4>();
                    break;


                case SerializedArgumentTypes.Matrix3x3:
                    readUnmanaged<Matrix3x3>();
                    break;

                case SerializedArgumentTypes.Matrix4x4:
                    readUnmanaged<Matrix4x4>();
                    break;




                case SerializedArgumentTypes.String:
                    if (!isArray) argument = reader.ReadUintLengthPrefixedUTF8String();
                    else
                    {
                        string[] arr = new string[count];
                        for (int s = 0; s < arr.Length; s++)
                            arr[s] = reader.ReadUintLengthPrefixedUTF8String();
                        argument = arr;
                    }
                    break;


                case SerializedArgumentTypes.ObjectPath:
                    if (!isArray) argument = new ObjectPath(reader.ReadUintLengthPrefixedUTF8String());
                    else
                    {
                        ObjectPath[] arr = new ObjectPath[count];
                        for (int s = 0; s < arr.Length; s++)
                            arr[s] = new ObjectPath(reader.ReadUintLengthPrefixedUTF8String());
                        argument = arr;
                    }
                    break;



                case SerializedArgumentTypes.ObjectReference:
                    readUnmanaged<ObjectReference>();
                    break;



                case SerializedArgumentTypes.ResourceReference:
                    if (!isArray) argument = resourceReferenceLookup[reader.ReadUInt32()];
                    else
                    {
                        GameResource[] arr = new GameResource[count];

                        for (int x = 0; x < arr.Length; x++)
                            arr[x] = resourceReferenceLookup[reader.ReadUInt32()];

                        argument = arr;
                    }
                    break;



                default:
                    throw new NotSupportedException();
            }



            final[argName] = argument;



            void readUnmanaged<T>() where T : unmanaged
            {
                if (!isArray) argument = reader.ReadType<T>();
                else argument = reader.ReadTypeArray<T>(count);
            }
        }


        return final;
    }





    public static async Task<GameResource[]> LoadResourceBytes(BinaryReader reader, string FilePath)
    {

        uint resourceCount = reader.ReadUInt32();

        if (resourceCount == 0) return null;


        GameResource[] resources = new GameResource[resourceCount];


        (string type, bool external, byte[] data)[] ress = new (string type, bool external, byte[] data)[resourceCount];
        for (int r = 0; r < resourceCount; r++)
        {
            var type = reader.ReadUintLengthPrefixedUTF8String();
            bool external = reader.ReadByte() == 1;

            byte[] data;

            if (external)
            {
                var path = reader.ReadUintLengthPrefixedUTF8String();
                data = Encoding.UTF8.GetBytes(path);
            }
            else
            {
                uint len = reader.ReadUInt32();
                data = reader.ReadBytes((int)len);
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
                res = resources[rIndex] = await GameResource.LoadResourceViaTypeName(resource.type, key);

            else
                res = resources[rIndex] = await Loading.InternalLoadOrFetchResource(key, async () => await GameResource.ConstructResourceViaTypeName(resource.type, resource.data, key));


            res.Init();
            
            resources[rIndex].AddUser();  //this scene


        });


        return resources;
    }






}