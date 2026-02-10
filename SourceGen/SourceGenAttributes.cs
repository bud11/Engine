




namespace Engine.Attributes;


using System;



/// <summary>
/// Indicates that this partial method within a public partial class should auto implement a partial implementation, which does nothing / returns default, if one doesn't already exist.
/// <br/> In other words, this allows an unimplemented partial method to become optional to implement.
/// </summary>
[AttributeUsage(AttributeTargets.Method, Inherited = false)]
public sealed class PartialDefaultReturnAttribute : Attribute;


/// <summary>
/// Registers a <see cref="Core.GameResource"/> field along with <paramref name="path"/>, such that providing the enclosing type to <see cref="Loading.LoadStaticResourceReferencesFor{T}"/> or <see cref="Loading.DeloadStaticResourceReferencesFor{T}"/> loads or deloads the resources without needing an instance or even concrete type.
/// <br /> The primary use case for this is loading object asset dependencies prior to having an instance, instead of loading dependencies within object init or needing all batch loading to go through some other kind of dependency system like scenes.
/// </summary>
/// <param name="path"></param>
[AttributeUsage(AttributeTargets.Field, Inherited = false)]
public sealed class StaticGameResourceReferenceAttribute(string path) : Attribute
{
    public readonly string ResourcePath = path;
}


/// <summary>
/// Indicates that this type is associated with a particular file extension.
/// </summary>
/// <param name="extension"></param>
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = true)]
public sealed class FileExtensionAssociationAttribute(string extension) : Attribute
{
    public readonly string Extension = $".{extension.Trim().TrimStart('.')}";
}



