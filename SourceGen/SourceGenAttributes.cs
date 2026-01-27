




namespace Engine.Attributes;


using System;



/// <summary>
/// Indicates that a static method on a non static class can be virtually overridden (and a virtual lookup performed via the resulting generated method within <see cref="StaticVirtuals"/>).
/// </summary>
[AttributeUsage(AttributeTargets.Method, Inherited = false)]
public sealed class StaticVirtualAttribute : Attribute;



/// <summary>
/// Indicates a static override. See <see cref="StaticVirtualAttribute"/>.
/// </summary>
[AttributeUsage(AttributeTargets.Method, Inherited = false)]
public sealed class StaticVirtualOverrideAttribute : Attribute;




/// <summary>
/// Indicates that this partial method should auto implement a partial implementation, which returns nothing or default, if one doesn't already exist.
/// </summary>
[AttributeUsage(AttributeTargets.Method, Inherited = false)]
public sealed class PartialDefaultReturnAttribute : Attribute;


/*  Likely unnessecary, no implementation, may change in future
 
/// <summary>
/// Indicates that this public static method should have an <see cref="IDeferredCommand"/> generated for it, which can be found in <see cref="DeferredCommands"/>.
/// <br/> Only unmanaged structs, pointers and <see cref="IGCHandleOwner"/> implementors are supported as arguments.
/// </summary>
[AttributeUsage(AttributeTargets.Method, Inherited = false)]
public sealed class DeferredCommandAttribute : Attribute;

*/



/// <summary>
/// Indicates that this public static method should be used to initialize the enclosing <see cref="GameObjects.GameObject"/> type in the context of instantiation from a <see cref="GameResources.SceneResource"/>. 
/// <br /> <b> The <see cref="GameObjects.GameObject"/> type must be public, and the method must be public, non abstract and non static, have standard arguments (no ref/in/out, etc; default values are allowed), and return void. </b> 
/// <br /> See <see cref="Engine.Core.Parsing.WriteArgumentBytes(List{byte}, Dictionary{string, System.Text.Json.JsonElement})"/> to understand how arguments are parsed and which types are supported.
/// <br /> In most cases, you should call the base ancestor initialization method from this method when applicable, almost as if it were a virtual override.
/// </summary>
[AttributeUsage(AttributeTargets.Method, Inherited = false)]
public sealed class GameObjectInitMethodAttribute : Attribute;




/// <summary>
/// Registers a <see cref="Core.GameResource"/> field along with <paramref name="path"/>, such that providing the enclosing type to <see cref="StaticMetadata.LoadStaticResourceReferencesFor{T}"/> or <see cref="StaticMetadata.DeloadStaticResourceReferencesFor{T}"/> loads or deloads the resources without needing an instance or even concrete type.
/// <br /> The primary use case for this is loading object asset dependencies prior to having an instance, instead of loading dependencies within object init or needing all batch loading to go through some other kind of dependency system like scenes.
/// </summary>
/// <param name="path"></param>
[AttributeUsage(AttributeTargets.Field, Inherited = false)]
public sealed class StaticGameResourceReferenceAttribute(string path) : Attribute
{
    public readonly string ResourcePath = path;
}