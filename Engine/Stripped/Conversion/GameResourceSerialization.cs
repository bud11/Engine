

namespace Engine.Core;

using Engine.Attributes;
using Engine.GameResources;
using System.Collections.Immutable;



public abstract partial class GameResource : RefCounted
{


    /// <summary>
    /// A debug-only <see cref="StaticVirtualAttribute"/> method that can be overridden to provide a way of converting a raw asset file to a game compatible or game optimal asset file. Without an override, the method just returns the original bytes.
    /// <br />This method is used lazily at debug runtime, and ahead of time during building/asset compression in release builds.
    /// <br />Also see <seealso cref="GameResourceFileExtensionMap"/>.
    /// </summary>
    /// <param name="bytes"></param>
    /// <param name="filePath"></param>
    /// <returns></returns>
    [StaticVirtual]
    public static async Task<byte[]> ConvertToFinalAssetBytes(byte[] bytes, string filePath) => bytes;





    /// <summary>
    /// A debug-only string dictionary that informs <see cref="Loading"/> at debug runtime, and/or the release build process, which file extensions to associate with which <see cref="GameResource"/> types. Also see <seealso cref="ConvertToFinalAssetBytes(byte[], string)"/>.
    /// <b><br/> Files without an associated type will be copied in unaltered and unassociated. <br/>To exclude a particular file type, add it to this extension map with a null key.</b>
    /// </summary>
    public static readonly ImmutableDictionary<string, Type> GameResourceFileExtensionMap = ImmutableDictionary.ToImmutableDictionary(new Dictionary<string, Type>()
        {

            //texture
            { ".png", typeof(TextureResource) },
            { ".exr", typeof(TextureResource) },
            
            //material
            { ".mat", typeof(MaterialResource) },
            
            //model
            { ".mdl", typeof(ModelResource) },
            
            //scene
            { ".scn", typeof(SceneResource) },


            //animation
            { ".anim", typeof(AnimationResource) }



            //audio
            //{ "wav", typeof(AudioResource) },
            //{ "ogg", typeof(AudioResource) },

            //collision
            
        });

}