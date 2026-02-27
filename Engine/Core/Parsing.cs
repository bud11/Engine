


namespace Engine.Core;



using Engine.Attributes;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;


#if DEBUG
using System.Text.Json;
using static Engine.Core.Loading;
#endif













/// <summary>
/// See <see cref="BinarySerializableTypeAttribute"/>. Use this to define how <typeparamref name="TIntermediate"/> should be serialized and deserialized to produce an instance of the implmenting type, given a specific context.
/// </summary>
public interface IBinarySerializeableOverride<TIntermediate>
{
    abstract object Serialize(TIntermediate data, object context);

    static abstract object Deserialize(TIntermediate data, object context);
}







public static partial class Parsing
{




    public static byte[] WriteVarUInt64(ulong value)
    {
        List<byte> val = new();

        while (value >= 0x80)
        {
            val.Add((byte)(value | 0x80));
            value >>= 7;
        }

        val.Add((byte)value);

        return val.ToArray();
    }


    public static ulong ReadVarUInt64(this Stream stream)
    {
        ulong result = 0;
        int shift = 0;

        while (true)
        {
            int b = stream.ReadByte();
            if (b < 0)
                throw new EndOfStreamException();

            result |= ((ulong)(b & 0x7F)) << shift;

            if ((b & 0x80) == 0)
                break;

            shift += 7;

            if (shift >= 64)
                throw new FormatException();
        }

        return result;
    }






    public static async Task<GameResource[]> LoadResourceBytes(Stream reader, string FilePath)
    {

        uint resourceCount = reader.DeserializeType<uint>();

        if (resourceCount == 0) return null;


        GameResource[] resources = new GameResource[resourceCount];


        (ushort type, bool external, byte[] data)[] ress = new (ushort type, bool external, byte[] data)[resourceCount];
        for (int r = 0; r < resourceCount; r++)
        {
            var type = reader.DeserializeType<ushort>();
            bool external = reader.ReadByte() == 1;

            byte[] data;

            if (external)
            {
                var path = reader.DeserializeType<string>();
                data = Encoding.UTF8.GetBytes(path);
            }
            else
            {
                data = reader.DeserializeType<byte[]>();
            }

            ress[r] = (type, external, data);
        }


        await Parallel.ForAsync<uint>(0, resourceCount, async (rIndex, cancellation) =>
        {
            var resource = ress[rIndex];


            string key = null;
            if (resource.external) key = RelativePathToFullPath(Encoding.UTF8.GetString(resource.data), FilePath[..(FilePath.LastIndexOf('/')+1)]);
            else key = (FilePath ?? string.Empty) + "_" + rIndex;


            GameResource res;

            if (resource.external)
                res = resources[rIndex] = await LoadGameResourceFromTypeID(resource.type, key);

            else
            {
                using (var memstream = new AssetByteStream(new MemoryStream(resource.data), resource.data.Length)) 
                    res = resources[rIndex] = await InternalLoadOrFetchResource(key, async () => await ConstructGameResourceFromTypeID(resource.type, memstream, key));

            }


            res.Register();

            resources[rIndex].AddUser();  //this scene


        });


        return resources;
    }






    public static Dictionary<TKey, object> ReadArgumentBytes<TKey>(this Stream reader, object context = null) where TKey : notnull
    {
        var len = reader.DeserializeType<byte>();

        var dict = new Dictionary<TKey, object>(len);

        for (byte i = 0; i < len; i++)
            dict[reader.DeserializeType<TKey>(context)] = reader.DeserializeType(reader.DeserializeType<ushort>(context));

        return dict;
    }










#if DEBUG





    public static T EnumParse<T>(string value, bool ignoreCase = true) where T : struct, Enum
    {
        if (Enum.TryParse<T>(value, ignoreCase: ignoreCase, out var val)) 
            return val;


        throw new Exception();
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


            var resourceTypeName = resource.GetProperty("Type").GetString();

            var resourceType = Type.GetType(resourceTypeName);



            if (resourceType == null)
                throw new Exception($"Type '{resourceTypeName}' not found - type must be specified in full, for example 'Engine.Core.GameResource' ");




            //write type

            resourceFinalBytes.AddRange(BitConverter.GetBytes(GetGameResourceTypeID(resourceType)));






            //if external, write length prefixed path

            if (resource.TryGetProperty("Path", out var path))
            {
                resourceFinalBytes.Add(1);
                resourceFinalBytes.AddRange(SerializeType(path.GetString(), false));
            }




            //otherwise, write length prefixed final data

            else if (resource.TryGetProperty("TextData", out var textdata))
                await WriteEmbedded(Encoding.UTF8.GetBytes(textdata.GetRawText()), true);


            else if (resource.TryGetProperty("Base64Data", out var base64data))
                await WriteEmbedded(Convert.FromBase64String(base64data.GetString()), false);


            else
                throw new Exception("Could not determine resource data source");






            async Task WriteEmbedded(byte[] data, bool plaintextwarning)
            {

                resourceFinalBytes.Add(0);


                //get resource properties

                var exHint = resource.GetProperty("ExtensionHint").GetString();



                //find extension and get the final asset bytes if applicable

                bool extensionHintFound = GameResourceFileAssociations.TryGetValue(exHint, out var AssetFoundType);


                if (extensionHintFound)
                {
                    data = await (await (Task<AssetByteStream>)typeof(Loading)
                                .GetMethod(nameof(GetFinalAssetBytes), [typeof(byte[]), typeof(string)])
                                .MakeGenericMethod(AssetFoundType)
                                .Invoke(null, [$"{filePath}/resource_{rIndex}.{exHint}", data])).GetArray();
                }



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
    /// Writes arbitrary parameters each with a type ID and index. The json data needs to specify the type of each parameter via its full C# type name or appropriate primitive alias.
    /// </summary>
    /// <param name="arguments"></param>
    /// <returns></returns>
    public static unsafe byte[] WriteArgumentBytes(JsonElement arguments)
    {
        var dict = arguments.Deserialize<Dictionary<string,JsonElement>>();

        List<byte> final = [(byte)dict.Count];


        foreach (var arg in dict)
        {
            var TypeName = arg.Value.GetProperty("Type").GetString();
            var Value = arg.Value.GetProperty("Value");


            TypeName = System.Text.RegularExpressions.Regex.Replace(
                TypeName,
                @"\b[a-zA-Z_][a-zA-Z0-9_]*\b",
                match =>
                {
                    if (CSharpAliases.TryGetValue(match.Value, out var type))
                        return type.FullName!;

                    return match.Value;
                });


            var type = Type.GetType(TypeName);



            //indexer
            final.AddRange(SerializeType(arg.Key, false));

            //value
            final.AddRange(type == null
                ? throw new Exception($"Type '{TypeName}' not found - type must be specified in full (with the exception of types with built in aliases) - for example 'System.Numerics.Matrix4x4' ")
                : SerializeType(Value.Deserialize(type), true));

        }


        return [.. WriteVarUInt64((ulong)final.Count), .. final];
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



#endif



}