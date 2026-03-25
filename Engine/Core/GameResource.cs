


namespace Engine.Core;


using Engine.GameResources;
using static Engine.Core.Loading;





/// <summary>
/// An abstract resource.
/// <br/>
/// <br/> <inheritdoc cref="ILoads"/>
/// <br/>
/// <br/> <inheritdoc cref="IConverts"/>
/// </summary>
/// 
public abstract partial class GameResource(string key) : RefCounted
{





    /// <summary>
    /// Given a <see cref="GameResource"/>-derived type implements <see cref="ILoads"/>, and has one or more <see cref="FileExtensionAssociationAttribute"/>s, it can be loaded via <see cref="LoadResource{T}(string)"/>.
    /// </summary>
    public interface ILoads
    {
		/// <summary>
		/// <inheritdoc cref="ILoads"/>
		/// </summary>
		/// <param name="stream"></param>
		/// <param name="key"></param>
		/// <returns></returns>
		public static abstract Task<GameResource> Load(AssetByteStream stream, string key);
    }


#if DEBUG

    /// <summary>
    /// <see cref="GameResource"/>s can implement development-time preprocessing/conversion from intermediary formats via <see cref="IConverts"/> and <see cref="ConvertToFinalAssetBytes(byte[], string)"/>, and should they, the conversion will be cached in <see cref="AssetCachePath"/>.
    /// <br/> The result will then be fed into <see cref="ILoads.Load(AssetByteStream, string)"/>.
    /// <br/> The cache will only be used if the md5 hash of the asset file matches the hash of the asset file at the time it was cached.
    /// <br/>
    /// <br/> Development time intermediary formats automatically support zstd decompression. For example, a json file could be zstd compressed, keep its original extension, and then be fed into <see cref="ConvertToFinalAssetBytes(byte[], string)"/> as the original raw json.
    /// <br/>
    /// <br/> <b> ! ! ! Implementation of <see cref="IConverts"/> must be excluded from release builds via preprocessor directives or similar. For release builds, <see cref="IConverts"/> will be invoked at compile/asset compression time instead. ! ! ! </b>
    /// </summary>
    public interface IConverts
    {
		/// <summary>
		/// <inheritdoc cref="IConverts"/>
		/// </summary>
		/// <param name="bytes"></param>
		/// <param name="filePath"></param>
		/// <returns></returns>
		public static abstract Task<byte[]> ConvertToFinalAssetBytes(Bytes bytes, string filePath);

    }

#endif





    /// <summary>
    /// The key this resource is indexed by within <see cref="Loading"/>, or if not, null. <br/> If this resource was loaded via <see cref="LoadResource{T}(string)"/> or similar, this will be equal to the resource file path.
    /// </summary>
    public readonly string Key = key;






    private bool NotifyLoadedCalled = false;

    public void Register()
    {
        lock (this)
        {
            if (!NotifyLoadedCalled)
            {
                NotifyLoadedCalled = true;

                if (Key != null) Loading.SetResourceLoaded(this);
            }
        }
    }



    protected override void OnFree()
    {
        if (Key != null) Loading.SetResourceUnloaded(this);
    }



    /// <summary>
    /// Creates a <see cref="GameResource"/> type instance via type ID + <see cref="ILoads.Load(AssetByteStream, string)"/>.
    /// </summary>
    /// <param name="TypeID"></param>
    /// <param name="stream"></param>
    /// <param name="key"></param>
    /// <returns></returns>
    public static partial Task<GameResource> LoadGameResourceFromTypeIDAndStream(ushort TypeID, AssetByteStream stream, string key);

    /// <summary>
    /// Creates a <see cref="GameResource"/> type instance via type ID + <see cref="LoadResource{T}(string)"/>.
    /// </summary>
    /// <param name="TypeID"></param>
    /// <param name="path"></param>
    /// <returns></returns>
    public static partial Task<GameResource> LoadGameResourceFromTypeIDAndPath(ushort TypeID, string path);

    /// <summary>
    /// Creates a <see cref="GameResource"/> type instance via generic + <see cref="ILoads.Load(AssetByteStream, string)"/>.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="stream"></param>
    /// <param name="key"></param>
    /// <returns></returns>
    public static partial Task<GameResource> LoadGameResourceFromGenericAndStream<T>(AssetByteStream stream, string key) where T : GameResource;


    /// <summary>
    /// Gets the numerical type ID for the given <see cref="GameResource"/> type.
    /// </summary>
    /// <param name="type"></param>
    /// <returns></returns>
    public static partial ushort GetGameResourceTypeID(Type type);

    /// <summary>
    /// Gets the <see cref="GameResource"/> type correspondant to the given ID.
    /// </summary>
    /// <param name="TypeID"></param>
    /// <returns></returns>
    public static partial Type GetGameResourceTypeFromTypeID(ushort TypeID);

}
