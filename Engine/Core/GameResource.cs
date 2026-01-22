


namespace Engine.Core;


using Engine.Attributes;
using static Loading;




/// <summary>
/// An abstract resource capable of being parsed and loaded. See <see cref="LoadResource{T}(string)"/>, <see cref="GameResourceFileExtensionMap"/>, <see cref="Load(byte[], string)"/> and <see cref="ConvertToFinalAssetBytes(byte[], string)"/>.
/// </summary>
public abstract partial class GameResource : RefCounted
{



    /// <summary>
    /// The key this resource is indexed by within <see cref="Loading"/>, or if not, null. <br/> If this resource was loaded via <see cref="LoadResource{T}(string)"/> or similar, this will be equal to the resource file path.
    /// </summary>
    public readonly string Key;

    /// <summary>
    /// A <see cref="StaticVirtualAttribute"/> method that can be overridden to provide a resource instance from loaded bytes. 
    /// <br /> Implementation is only nessecary if you want this resource type to be compatible with <see cref="LoadResource{T}(string)"/>.
    /// </summary>
    /// <param name="bytes"></param>
    /// <returns></returns>
    /// <exception cref="Exception"></exception>
    [StaticVirtual]
    public static async Task<GameResource> Load(byte[] bytes, string key) => throw new Exception("Unimplemented loader");


    public GameResource(string key)
    {
        Key = key;
    }


    protected override void OnFree()
    {
        if (Key != null) Loading.SetResourceUnloaded(this);
    }



    /// <summary>
    /// Runs during <see cref="Init"/>.
    /// </summary>

    [PartialDefaultReturn]
    protected virtual partial void FinalInit();




    private bool NotifyLoadedCalled = false;

    /// <summary>
    /// Calls <see cref="FinalInit"/> and <see cref="Loading.SetResourceLoaded(GameResource)"/>. 
    /// </summary>
    public void Init()
    {
        lock (this)
        {
            if (!NotifyLoadedCalled)
            {
                NotifyLoadedCalled = true;

                FinalInit();

                if (Key != null) Loading.SetResourceLoaded(this);

            }
        }

    }

}
