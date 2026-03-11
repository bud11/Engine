


namespace Engine.Core;



using Engine.Attributes;
using System.Collections;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;


#if DEBUG
using System.Text.Json;
using static Engine.Core.Loading;
using System.Reflection;
#endif













/// <summary>
/// Used to define how this type should be serialized into <typeparamref name="TIntermediate"/> and/or deserialized from <typeparamref name="TIntermediate"/>, optionally reliant on some specific contextual object.
/// </summary>
public interface ISerializeOverrider<TIntermediate>
{
    static abstract TIntermediate Serialize(object instance, object context);

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











    public static Dictionary<TKey, object> ReadArgumentBytes<TKey>(this Stream reader, object context = null) where TKey : notnull
    {
        var len = reader.DeserializeKnownType<byte>();

        var dict = new Dictionary<TKey, object>(len);

        for (byte i = 0; i < len; i++)
            dict[reader.DeserializeKnownType<TKey>(context)] = reader.DeserializeUnknownType(context);

        return dict;
    }








#if DEBUG





    public static T EnumParse<T>(string value, bool ignoreCase = true) where T : struct, Enum
    {
        if (Enum.TryParse<T>(value, ignoreCase: ignoreCase, out var val))
            return val;


        throw new Exception();
    }








    /// <summary>
    /// Debug only development time method. Writes arbitrary arguments each with a type, value, and indexer.   
    /// <br/> Each involved type must be serializable via <see cref="BinarySerializableTypeAttribute"/> or similar.
    /// <br/> 
    /// <br/> Example data might look like this, where each argument value is an input to <see cref="DeserializeTypeFromJson(JsonElement, Type, object)"/>
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
    /// <br/> For example, instead of effectively serializing { "ExampleArgument" : { ... }  }, something closer to { type.GetFields().IndexOf(type.GetField("ExampleArgument")) : { ... } } will be serialized instead.
    /// <br/> Members to be assigned must have <see cref="DataValueAttribute"/>.
    /// </summary>
    /// <param name="arguments"></param>
    /// <returns></returns>
    public static unsafe byte[] WriteArgumentBytes(JsonElement arguments, Type? DataValueAttributeTargetTypeForNumericalIndexing = null, object context = null)
    {


        List<byte> final = [(byte)arguments.EnumerateObject().Count()];




        var DataValueTargetTypeMembers = DataValueAttributeTargetTypeForNumericalIndexing == null ? null : DataValueAttributeTargetTypeForNumericalIndexing.GetMembers(BindingFlags.Public | BindingFlags.Instance).Where(x => x is FieldInfo || x is PropertyInfo).ToArray();




        foreach (var arg in arguments.EnumerateObject())
        {


            Type deserializeTypeFallback = null;



            // ------------- indexer -------------

            if (DataValueAttributeTargetTypeForNumericalIndexing == null)
            {
                final.AddRange(SerializeType(arg.Name, false));
            }
            else
            {
                var fget = DataValueAttributeTargetTypeForNumericalIndexing.GetField(arg.Name, BindingFlags.Public | BindingFlags.Instance);
                var pget = DataValueAttributeTargetTypeForNumericalIndexing.GetProperty(arg.Name, BindingFlags.Public | BindingFlags.Instance);


                if (fget == null && pget == null)
                    throw new Exception($"Field or property '{arg.Name}' on target type '{DataValueAttributeTargetTypeForNumericalIndexing.FullName}' couldn't be found.");


                if (fget != null && fget.GetCustomAttribute<DataValueAttribute>() == null)
                    throw new Exception($"Field '{arg.Name}' on target type '{DataValueAttributeTargetTypeForNumericalIndexing.FullName}' is not attributed with '{typeof(DataValueAttribute).FullName}'.");


                if (pget != null && pget.GetCustomAttribute<DataValueAttribute>() == null)
                    throw new Exception($"Property '{arg.Name}' on target type '{DataValueAttributeTargetTypeForNumericalIndexing.FullName}' is not attributed with '{typeof(DataValueAttribute).FullName}'.");



                deserializeTypeFallback = fget == null ? pget.PropertyType : fget.FieldType;

                final.Add((byte)Array.IndexOf(DataValueTargetTypeMembers, deserializeTypeFallback));

            }



            // ------------- typed value -------------

            final.AddRange(SerializeType(DeserializeTypeFromJsonAndSerializeToBytes(arg.Value, deserializeTypeFallback, context), true));

        }


        

        return SerializeType(final.ToArray(), false);
    }







    /// <summary>
    /// Debug only development time method. Deserializes an arbitrary type (graph) from json data.
    /// <br/>
    /// <br/> Explicit typing, up/interface casts and boxing can be done by providing a dictionary with Type and Value keys as the json value. Otherwise, the type resolve falls back to <paramref name="typeHint"/>.
    /// <br/> Deserialization will attempt to deserialize via <see cref="ISerializeOverrider{TIntermediate}"/> implementations for applicable types.
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
    /// <returns></returns>
    /// <exception cref="Exception"></exception>
    public static byte[] DeserializeTypeFromJson(JsonElement arg, Type typeHint = null, object context = null)
        => (byte[])DeserializeTypeFromJsonInternal(arg, true, typeHint, context);



    /// <summary>
    /// Works the same way as <see cref="DeserializeTypeFromJson(JsonElement, Type, object)"/>, but serializes into bytes immediately, meaning no live object instances are created or derived.
    /// </summary>
    /// <param name="arg"></param>
    /// <returns></returns>
    /// <exception cref="Exception"></exception>
    public static byte[] DeserializeTypeFromJsonAndSerializeToBytes(JsonElement arg, Type typeHint = null, object context = null)
       => (byte[])DeserializeTypeFromJsonInternal(arg, true, typeHint, context);





    private static object DeserializeTypeFromJsonInternal(JsonElement arg, bool serialize, Type typeHint = null, object context = null)
    {




        Type type;



        //type in data

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


        //type provided

        else if (typeHint != null)
        {
            type = typeHint;
            Value = arg;
        }



        else
            throw new Exception("Type was not specified and could not be inferred");






        // ------------- custom parsed -------------

        var overrides = type.GetInterfaces()
            .Where(x =>
                x.IsGenericType
                    ? x.GetGenericTypeDefinition() == typeof(ISerializeOverrider<>)
                    : x == typeof(ISerializeOverrider<>));


        if (overrides.Any())
        {
            foreach (var v in overrides)
            {
                var generic = v.GetGenericArguments()[0];

                try
                {
                    return type.GetMethod("Deserialize", BindingFlags.Public | BindingFlags.Static).Invoke(null, [DeserializeTypeFromJsonInternal(arg, serialize, generic, context), context]);
                }
                catch { }
            }
        }








        // ------------- primitives -------------

        if (type.GetInterfaces()
            .Any(x =>
                x.IsGenericType
                    ? x.GetGenericTypeDefinition() == typeof(INumber<>)
                    : x == typeof(INumber<>)))

            return SerializeType(Value.Deserialize(type, JsonAssetLoadingOptions), false);


        if (type == typeof(bool))
            return SerializeType(Value.Deserialize<bool>(JsonAssetLoadingOptions), false);

        if (type == typeof(string))
            return SerializeType(Value.GetString(), false);

        if (type.IsEnum)
            return SerializeType(Value.Deserialize(type, JsonAssetLoadingOptions), false);









        // ------------- collections -------------


        if (type.IsArray)
        {
            var arr = Value.Deserialize<JsonElement[]>(JsonAssetLoadingOptions);

            var elementType = type.GetElementType();


            if (serialize)
            {
                List<byte> ret = [ .. WriteVarUInt64((ulong)arr.Length) ];

                for (int i = 0; i < arr.Length; i++)
                    ret.AddRange(SerializeType(DeserializeTypeFromJsonInternal(arr[i], true, elementType, context), false));

                return ret.ToArray();
            }


            else
            {
                IList ret = Array.CreateInstance(type, arr.Length);

                for (int i = 0; i < arr.Length; i++)
                    ret[i] = DeserializeTypeFromJsonInternal(arr[i], false, elementType, context);

                return ret;
            }
        }




        if (type.GetInterfaces().Where(x =>
                x.IsGenericType
                    ? x.GetGenericTypeDefinition() == typeof(IDictionary<,>)
                    : x == typeof(IDictionary<,>)).Any())
        {
            var keyType = type.GetGenericArguments()[0];
            var valueType = type.GetGenericArguments()[1];

            var obj = Value.EnumerateObject();


            var dictType = type.MakeGenericType(keyType, valueType);
            

            if (serialize)
            {
                List<byte> ret = [.. WriteVarUInt64((ulong)obj.Count())];

                foreach (var kv in obj)
                {
                    ret.AddRange( SerializeType(kv.Name, false) );
                    ret.AddRange( SerializeType(DeserializeTypeFromJsonInternal(kv.Value, true, valueType, context), false) );
                }

                return ret;
            }

            else
            {
                IDictionary ret = (IDictionary)Activator.CreateInstance(dictType);

                foreach (var kv in obj)
                    ret[Convert.ChangeType(kv.Name, keyType)] = DeserializeTypeFromJsonInternal(kv.Value, false, valueType, context);


                return ret;
            }


        }





        // -------------- other types --------------


        object? inst = serialize ? new List<byte>() : Activator.CreateInstance(type);


        foreach (var kv in Value.EnumerateObject())
        {
            var fget = type.GetField(kv.Name, BindingFlags.Public | BindingFlags.Instance);
            var pget = type.GetProperty(kv.Name, BindingFlags.Public | BindingFlags.Instance);

            if (fget != null)
            {
                if (serialize)
                    ((List<byte>)inst).AddRange((byte[])DeserializeTypeFromJsonInternal(kv.Value, true, fget.FieldType, context));
                else
                    fget.SetValue(inst, DeserializeTypeFromJsonInternal(kv.Value, false, fget.FieldType, context));
            }

            else if (pget != null)
            {
                if (serialize)
                    ((List<byte>)inst).AddRange((byte[])DeserializeTypeFromJsonInternal(kv.Value, true, pget.PropertyType, context));
                else
                    pget.SetValue(inst, DeserializeTypeFromJsonInternal(kv.Value, false, pget.PropertyType, context));
            }

            else throw new Exception($"Could not parse type '{type.FullName}'");
        }



        return serialize ? ((List<byte>)inst).ToArray() : inst;

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