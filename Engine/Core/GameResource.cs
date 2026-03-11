


namespace Engine.Core;


using Engine.GameResources;
using static Loading;





/// <summary>
/// An abstract resource.
/// <br/>
/// <br/> <inheritdoc cref="ILoads"/>
/// <br/>
/// <br/> <inheritdoc cref="IConverts"/>
/// </summary>
/// 
public abstract partial class GameResource(string key) : RefCounted,
    ISerializeOverrider<uint>
{



    static uint ISerializeOverrider<uint>.Serialize(object instance, object context)
        => throw new NotImplementedException();

    static object ISerializeOverrider<uint>.Deserialize(uint data, object context)
        => ((SceneResource.SceneBinaryDeserializationContext)context).Objects[(int)data];








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
    /// <br/> The cache will only be used if the md5 hash of the asset file matches the hash of the asset file at the time it was cached and <see cref="ForceReconversion(byte[], byte[])"/> returns false.
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
		public static abstract Task<byte[]> ConvertToFinalAssetBytes(byte[] bytes, string filePath);

        /// <summary>
        /// An extra layer of control over whether to reuse a seemingly valid found cache file or not. See <see cref="IConverts"/> 
        /// </summary>
        /// <param name="bytes"></param>
        /// <param name="currentCache"></param>
        /// <returns></returns>
        public static abstract bool ForceReconversion(byte[] bytes, byte[] currentCache);
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

}
