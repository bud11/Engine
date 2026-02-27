


namespace Engine.Attributes;

using System;




/// <summary>
/// Registers a type as both serializable and deserializable, and assigns the type an ID so that it can be deserialized even if the type is statically unknown at parse time. Backed by AOT-safe source generation.
/// <br/>
/// <br/> This attribute can be applied to:
/// <br/> - A type directly
/// <br/> - An assembly, provided <paramref name="typeOverride"/> is set to specify which type
/// <br/>
/// <br/> Type discovery will also naturally include all of the field types on this type (unless this type implements <see cref="Engine.Core.IBinarySerializeableOverride{TIntermediate}"/>), as well as any type used in a call to <see cref="Engine.Core.Parsing.DeserializeType{T}"/>.
/// <br/>
/// <br/> Involved types cannot be static, abstract or an interface, and each must expose a parameterless constructor.
/// <br/> Serialization will occur by deserializing all publically mutable instance fields, excluding any marked with <see cref="NonSerializedAttribute"/>.
/// <br/> <b> Arrays and dictionaries featuring supported types are also supported, and are the only exceptions to that logic. </b>
/// <br/>
/// <br/> If a type implements <see cref="Engine.Core.IBinarySerializeableOverride{TIntermediate}"/>, it can override how it's serialization and deserialization occurs. 
/// <br/> For example, it may only serialize a self-reference through this particular system.
/// <br/> <see cref="Core.GameObject"/> and <see cref="Core.GameResource"/> are good prexisting examples of that pattern.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Assembly | AttributeTargets.Enum, Inherited = false, AllowMultiple = true)]
public sealed class BinarySerializableTypeAttribute(Type typeOverride = null) : Attribute
{
    public readonly Type TypeOverride = typeOverride;
}



/// <summary>
/// Registers this instance field or property as one that can be set via an index and value upon an instance of the type; for example upon a <see cref="Core.GameObject"/> type by a <see cref="GameResources.SceneResource"/> during instantiation.
/// <br/> The enclosing type must be partial.
/// <br/> All <see cref="DataValueAttribute"/> users within a type can be quickly seen in IDE by inspecting the type's remarks.
/// </summary>
/// <param name="path"></param>
/// <param name="autoManage"></param>
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, Inherited = false)]
public sealed class DataValueAttribute : Attribute;



/// <summary>
/// Registers a <see cref="Core.GameResource"/>-type static field or property as a dependency to the enclosing type.
/// <br/> This means that it gets added to a list of resources loaded and set via <see cref="Core.Loading.LoadResourceDependenciesFor{T}()"/>, where T is the enclosing type.
/// <br/> If <paramref name="autoManage"/> is true, and the enclosing type is a <see cref="Core.GameObject"/> or <see cref="Core.GameResource"/>, resource dependencies are loaded and deloaded automatically as the enclosing type is loaded or instantiated.
/// </summary>
/// <param name="path"></param>
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, Inherited = false)]
public sealed class ResourceDependencyAttribute(string path, bool autoManage = true) : Attribute
{
    public readonly string ResourcePath = path;
    public readonly bool AutoManage = autoManage;
}
