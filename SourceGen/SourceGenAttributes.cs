


namespace Engine.Attributes;

using System;



/// <summary>
/// <br/> This attribute must be applied to a <see cref="Core.Parsing.BinarySerializerDeserializerBase"/> derived class.
/// <br/>
/// <br/> Registers a type as both serializable and deserializable by this class. This generates an AOT-safe serialization and deserialization codepath, and assigns the type a numerical ID so that it can be deserialized even if the type is statically unknown at parse time. 
/// <br/>
/// <br/> Involved types cannot be static and must each expose a parameterless constructor. 
/// <br/>
/// <br/> Serialization will usually occur by recursively serializing all publically mutable instance fields, excluding any marked with <see cref="NonSerializedAttribute"/>, as efficiently as possible.
/// <br/> Special cases:
/// <br/> - Arrays of supported types
/// <br/> - Dictionaries involving supported types
/// <br/> - Types included via custom serialize/deserialize methods 
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = true, AllowMultiple = true)]
public sealed class BinarySerializableTypeAttribute : Attribute
{
    public readonly Type Type;
    public readonly string SerializeMethodName;
    public readonly string DeserializeMethodName;

    public BinarySerializableTypeAttribute(Type type)
    {
        Type = type;
    }

    public BinarySerializableTypeAttribute(string serializeMethod = null, string deserializeMethod = null)
    {
        SerializeMethodName = serializeMethod;
        DeserializeMethodName = deserializeMethod;
    }

}





/// <summary>
/// Registers this instance field or property as indexable via <see cref="string"/> name.
/// <br/> The enclosing type must be partial.
/// <br/> All <see cref="IndexableAttribute"/> members within a type can be quickly seen in IDE by inspecting the type's remarks.
/// </summary>
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, Inherited = false)]
public sealed class IndexableAttribute : Attribute;




/// <summary>
/// Registers a <see cref="Core.GameResource"/>-type static field or property as a dependency to the enclosing type.
/// <br/> This means that it gets added to a list of resources loaded and set via <see cref="Core.Loading.LoadResourceDependenciesFor{T}()"/>, where T is the enclosing type.
/// <br/> If <paramref name="autoLoad"/> is true, and the enclosing type is a <see cref="Core.GameObject"/> or <see cref="Core.GameResource"/>, resource dependencies are loaded and deloaded automatically as the enclosing type is loaded or instantiated.
/// </summary>
/// <param name="path"></param>
/// <param name="autoLoad"></param>
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, Inherited = false)]
public sealed class ResourceDependencyAttribute(string path, bool autoLoad) : Attribute
{
    public readonly string ResourcePath = path;
    public readonly bool AutoManage = autoLoad;
}
