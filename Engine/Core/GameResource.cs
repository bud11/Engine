


namespace Engine.Core;


using Engine.Attributes;
using static Loading;




/// <summary>
/// An abstract resource capable of being parsed and loaded. See <see cref="LoadResource{T}(string)"/> and <see cref="ConvertToFinalAssetBytes(byte[], string)"/>.
/// <br/>
/// <br/> GameResources do not need to support loading/deserialization, but should they, they must implement a method with the same signature as <see cref="Load"/>.
/// <br/>
/// <br/> GameResources are also capable of supporting debug-time / release-compile-time pre-processing, for example texture compression. To implement that, a method with the same signature <see cref="ConvertToFinalAssetBytes(byte[], string)"/> must be implemented.
/// <br/>
/// <br/> To associate a GameResource with one or more file types in a way that the engine recognises, one or more <see cref="FileExtensionAssociationAttribute"/>s can be added to it.
/// </summary>
public abstract partial class GameResource(string key) : RefCounted
{



    /// <summary>
    /// The key this resource is indexed by within <see cref="Loading"/>, or if not, null. <br/> If this resource was loaded via <see cref="LoadResource{T}(string)"/> or similar, this will be equal to the resource file path.
    /// </summary>
    public readonly string Key = key;




    /// <summary>
    /// The loading method template. See <see cref="GameResource"/>.
    /// </summary>
    /// <param name="stream"></param>
    /// <param name="key"></param>
    /// <returns></returns>
    /// <exception cref="Exception"></exception>
    public static async Task<GameResource> Load(AssetByteStream stream, string key)
        => throw new Exception();


#if DEBUG

    /// <summary>
    /// The pre-processing method template. See <see cref="GameResource"/>.
    /// </summary>
    /// <param name="bytes"></param>
    /// <param name="filePath"></param>
    /// <returns></returns>
    public static async Task<byte[]> ConvertToFinalAssetBytes(byte[] bytes, string filePath) 
        => bytes;

#endif



    private bool NotifyLoadedCalled = false;

    /// <summary>
    /// Calls <see cref="PostInit"/> and <see cref="Loading.SetResourceLoaded(GameResource)"/>. 
    /// </summary>
    public void Init()
    {
        lock (this)
        {
            if (!NotifyLoadedCalled)
            {
                NotifyLoadedCalled = true;

                PostInit();

                if (Key != null) Loading.SetResourceLoaded(this);

            }
        }
    }




    [PartialDefaultReturn]
    protected virtual partial void PostInit();




    protected override void OnFree()
    {
        if (Key != null) Loading.SetResourceUnloaded(this);
    }








}
