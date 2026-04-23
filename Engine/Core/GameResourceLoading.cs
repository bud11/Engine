




namespace Engine.Core;



using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ZstdSharp;
using static Engine.Core.IO;


using static Engine.Core.References;


#if DEBUG
using System.IO.Hashing;
using System.Reflection;
using Engine.Stripped;
#endif





public partial class GameResource
{





    /// <summary>
    /// A throttle for the maximum amount of resources that can be asynchronously loading at once. 0 = uncapped
    /// </summary>
    public static int MaxParalleResourceLoads = 0;



#if DEBUG

    public static bool PrintResourceLoadingStatus = false;

    /// <summary>
    /// A throttle for the maximum amount of resources that can be asynchronously converted via <see cref="GameResource.IConverts.ConvertToFinalAssetBytes(Bytes, string)"/> at once. 0 = uncapped
    /// </summary>
    public static int MaxParallelAssetConversions = 0;

#endif











    /// <summary>
    /// Loads all assets guaranteed to be of the given type found in the given folder.
    /// </summary>
    /// <typeparam name="ResType"></typeparam>
    /// <param name="path"></param>
    /// <returns></returns>
    /// <exception cref="Exception"></exception>
    public static async Task<Dictionary<string, ResType>> LoadAllResourcesOfTypeFromFolder<ResType>(string path) where ResType : GameResource
    {


        var target = Path.GetDirectoryName(path);


        string[] filepaths = null;

#if DEBUG

        if (!AssetFolderExists(path)) 
            throw new Exception();


        filepaths = [.. Directory.GetFiles(Path.Combine(AssetRootDirectoryPath, path)).Select(x => Path.GetRelativePath(target, x))];

#else

        filepaths = FolderLookupDirect[path].Where(x => x.Value.Type == typeof(ResType)).Select(x => x.Key).ToArray();
        
#endif


        Dictionary<string, Task<ResType>> tasks = new();
        for (int i = 0; i < filepaths.Length; i++)
        {
            ref string filepath = ref filepaths[i];
            tasks[filepath[filepath.LastIndexOf('\\')..]] = LoadResource<ResType>(filepath);
        }

        await Task.WhenAll(tasks.Values);
        return tasks.Select(x => KeyValuePair.Create(x.Key, x.Value.Result)).ToDictionary();
    }











    /// <summary>
    /// Loads or fetches a <see cref="GameResource"/> from a folder/archive within the game's asset root directory (see <see cref="AssetRootDirectoryPath"/>).
    /// <br/> <b><paramref name="resourcePath"/> should be relative to the asset root directory and should not include a file extension. </b>
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="resourcePath"></param>
    /// <returns></returns>
    public static async Task<T> LoadResource<T>(string resourcePath) where T : GameResource
    {



        return (T) await InternalLoadOrFetchResource(resourcePath, async () =>
        {

            var loadpath = resourcePath;  //gets mutated, not useless


            AssetByteStream stream = null;


#if DEBUG


            //scans for an existing file according to ResourceFileExtensionMap

            var ValidFileExtensions = GameResourceFileAssociations.Where(x => x.Value == typeof(T)).Select(x => x.Key);



            foreach (var v in ValidFileExtensions)
            {
                var chk = resourcePath + v;
                if (AssetFileExists(chk))
                {
                    loadpath = chk;
                    break;
                }
            }


            if (resourcePath == loadpath) 
                throw new Exception($"Resource '{resourcePath}' not found (scanned for {string.Join(", ", ValidFileExtensions)})");




            //if type converts, try loading cached result, otherwise convert and cache result if cached file doesnt exist or doesnt match

            stream = await GetFinalAssetBytes(typeof(T), loadpath);


#else
            stream = AcquireAssetByteStream(loadpath);
#endif


            var res = await LoadGameResourceFromGenericAndStream<T>(stream, resourcePath);

            stream.Dispose();

            return res;

        });

       
    }






#if DEBUG




    private static readonly SemaphoreSlim AssetConversionThrottle = new(MaxParallelAssetConversions);
    private static readonly AsyncLocal<string> CurrentConvertingTopLevelAsset = new();




    /// <summary>
    /// <inheritdoc cref="GetFinalAssetBytes(Type, Bytes, string)"/>
    /// </summary>
    /// <param name="resourceT"></param>
    /// <param name="relativePath"></param>
    /// <returns></returns>
    /// <exception cref="InvalidOperationException"></exception>
    public static async Task<AssetByteStream> GetFinalAssetBytes(Type resourceT, string relativePath = null)
    {
        if (!resourceT.IsAssignableTo(typeof(GameResource)))
            throw new InvalidOperationException();

        return await GetFinalAssetBytes(resourceT, new Bytes(await AcquireAssetByteStream(relativePath).GetArray()), relativePath);
    }





    /// <summary>
    /// Fetches and/or caches a file's final bytes, assuming the asset file's contents are <paramref name="unconvertedData"/>, and its path on disk is <paramref name="relativePath"/>.
    /// <br/>
    /// <br/> This is a development-time debug-only method and usually shouldn't be used directly.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="unconvertedData"></param>
    /// <param name="relativePath"></param>
    /// <returns></returns>
    public static async Task<AssetByteStream> GetFinalAssetBytes(Type resourceT, Bytes unconvertedData, string relativePath = null)
    {

        if (!resourceT.IsAssignableTo(typeof(GameResource))) 
            throw new InvalidOperationException();



        var loadingDbgString = $"Loading asset file '{relativePath}'";
        var convertingDbgString = $"Converting/caching asset file '{relativePath}'";



        liststatusChange(assetloadingstatusForDebugMsg.Loading);



        AssetByteStream finalStream = null;



        var convertcheck = resourceT.IsAssignableTo(typeof(IConverts));


        if (convertcheck)
        {

            uint crc = 0;


            var cachedFileDir = relativePath == null ? null : Path.Combine(AssetCachePath, relativePath + ".cached");



            // path supplied, check if cache file exists and is valid

            if (relativePath != null)
            {

                Directory.CreateDirectory(Directory.GetParent(cachedFileDir).FullName);


                if (FileExistsCaseSensitive(cachedFileDir))
                {
                    crc = Crc32.HashToUInt32(unconvertedData.ByteArray);


                    var filestream = new FileStream(cachedFileDir, FileMode.Open, FileAccess.Read);

                    var read = Parsing.ValueReader.FromStream(filestream);


                    uint storedCrc = read.ReadUnmanaged<uint>();


                    //cache matches
                    if (storedCrc == crc)
                    {
                        var validationLength = read.ReadUnmanaged<uint>();

                        //validation block found
                        if (validationLength != 0)
                        {
                            var validationBlock = read.ReadUnmanagedSpan<byte>(validationLength);


                            var method = resourceT
                                .GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                                .Single(m =>
                                    m.Name.Contains("Validate") &&
                                    m.GetParameters().Length == 2 &&
                                    m.ReturnType == typeof(Task<bool>)
                                );


                            var validation = await (Task<bool>)method.Invoke(null, [validationBlock, relativePath]);

                            
                            Validate(validation);
                        }

                        else
                            Validate(true);

                    }
                    else 
                        Validate(false);




                    void Validate(bool valid)
                    {
                        if (valid)
                        {
                            var len = read.ReadUnmanaged<uint>();

                            statusPrint($"Cached file found for asset '{relativePath}'");

                            finalStream = new AssetByteStream(new DecompressionStream(filestream, (int)len, leaveOpen: false), len);
                        }
                        else
                        {
                            crc = 0;
                            filestream.Dispose();
                            filestream = null;
                        }
                    }
                }
            }




            // path not supplied, cache file wasnt found, or cache file doesn't match

            if (crc == 0)
            {


                if (CurrentConvertingTopLevelAsset.Value == null)
                    CurrentConvertingTopLevelAsset.Value = relativePath;



                statusPrint($"Required cache missing or out of date for asset '{relativePath}', converting...");


                liststatusChange(assetloadingstatusForDebugMsg.Converting);




                crc = Crc32.HashToUInt32(unconvertedData.ByteArray);


                //  ---------------------- zstd detection / decompression ----------------------

                byte[] zstdMagic = [0x28, 0xB5, 0x2F, 0xFD];
                bool zstd = true;

                for (int i = 0; i < 4; i++)
                {
                    if (unconvertedData.ByteArray[i] != zstdMagic[i])
                    {
                        zstd = false;
                        break;
                    }
                }


                if (zstd)
                {
                    try
                    {
                        using var input = new MemoryStream(unconvertedData.ByteArray);
                        using var zstdStream = new DecompressionStream(input);
                        using var output = new MemoryStream();

                        zstdStream.CopyTo(output);

                        unconvertedData.ByteArray = output.ToArray();
                    }

                    catch (ZstdException) { }  //invalid zstd

                }

                // -----------------------------------------------------------------------------





                var method = resourceT
                    .GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                    .Single(m =>
                        m.Name.Contains("ConvertToFinalAssetBytes") &&
                        m.GetParameters().Length == 2 &&
                        m.ReturnType == typeof(Task<IConverts.FinalAssetBytes>)
                    );


                var finalBytes = await (Task<IConverts.FinalAssetBytes>)method.Invoke(null, [unconvertedData, relativePath]);


                //just incase not disposed..
                unconvertedData.Dispose();
                unconvertedData = null;





                finalStream = new AssetByteStream(new MemoryStream(finalBytes.Bytes), finalBytes.Bytes.Length);



                // path supplied, compress and cache

                if (relativePath != null)
                {

                    using var compressor = new Compressor(4);

                    var compressedFinalBytes = compressor.Wrap(finalBytes.Bytes);


                    using var filestream = new FileStream(cachedFileDir, FileMode.Create, FileAccess.Write, FileShare.None,

                        sizeof(uint)  //crc
                        + sizeof(uint)   //len
                        + compressedFinalBytes.Length //bytes

                        + sizeof(uint)      //validation len
                        + (finalBytes.ValidationBlock == null ? 0 : finalBytes.ValidationBlock.Length)  //validation bytes
                        );


                    var writer = Parsing.ValueWriter.FromStream(filestream);


                    // CRC
                    writer.WriteUnmanaged(crc);

                    // VALIDATION
                    if (finalBytes.ValidationBlock != null)
                    {
                        writer.WriteUnmanaged((uint)finalBytes.ValidationBlock.Length);
                        writer.WriteUnmanagedSpan<byte>(finalBytes.ValidationBlock);
                    }
                    else
                    {
                        writer.WriteUnmanaged(0u);
                    }

                    // BYTES
                    writer.WriteUnmanaged((uint)finalBytes.Bytes.Length);
                    writer.WriteUnmanagedSpan<byte>(compressedFinalBytes);
                    



                    compressedFinalBytes = default;
                }


                //conversion often leaves a lot of junk/temporary data

                System.Runtime.GCSettings.LargeObjectHeapCompactionMode = System.Runtime.GCLargeObjectHeapCompactionMode.CompactOnce;
                System.Runtime.GCSettings.LatencyMode = System.Runtime.GCLatencyMode.Batch;
                GC.Collect();



                if (CurrentConvertingTopLevelAsset.Value == relativePath)
                    CurrentConvertingTopLevelAsset.Value = null;


            }
        }



        else finalStream = AcquireAssetByteStream(relativePath);



        statusPrint($"Final asset bytes loaded for asset '{relativePath}'");


        liststatusChange(assetloadingstatusForDebugMsg.Done);




        return finalStream;




        void statusPrint(string p)
        {

#if !ENGINE_BUILD_PASS
            if (PrintResourceLoadingStatus)
#endif

            if (relativePath != null)

#if ENGINE_BUILD_PASS
            Console.WriteLine(
#else
            EngineDebug.Print(
#endif
            p);


        }



        void liststatusChange(assetloadingstatusForDebugMsg status)
        {

            if (relativePath != null)

                lock (ResourceLoadingDebugList)
                {
                    ResourceLoadingDebugList.Remove(loadingDbgString);
                    ResourceLoadingDebugList.Remove(convertingDbgString);

                    switch (status)
                    {
                        case assetloadingstatusForDebugMsg.Loading:
                            ResourceLoadingDebugList.Add(loadingDbgString);
                            return;

                        case assetloadingstatusForDebugMsg.Converting:
                            ResourceLoadingDebugList.Add(convertingDbgString);
                            return;
                    }
                }
        }
    }

    private enum assetloadingstatusForDebugMsg
    {
        Loading,
        Converting,
        Done
    }

    public static readonly List<string> ResourceLoadingDebugList = new();




#endif













    private static readonly SemaphoreSlim ResourceLoadThrottle = new(MaxParalleResourceLoads);







    private static readonly SemaphoreSlim LoadedResourcesSemaphore = new(1, 1);

    private static readonly Dictionary<string, Task<GameResource>> LoadingResources = new();
    private static readonly Dictionary<string, WeakObjRef<GameResource>> WeakLoadedResources = new();


    /// <summary>
    /// If resource of <paramref name="resourcePath"/> isn't registered, invokes <paramref name="loader"/> to load and register it. Thread safe.
    /// </summary>
    /// <param name="resourcePath"></param>
    /// <param name="loader"></param>
    /// <returns></returns>
    public static async Task<GameResource> InternalLoadOrFetchResource(
        string resourcePath,
        Func<Task<GameResource>> loader)
    {
        Task<GameResource> loadTask;

        await LoadedResourcesSemaphore.WaitAsync();

        try
        {
            if (LoadingResources.TryGetValue(resourcePath, out loadTask))
                return await loadTask;

            if (WeakLoadedResources.TryGetValue(resourcePath, out var weakRef))
            {
                var existing = weakRef.Dereference();

                if (existing != null)
                    return existing;

                WeakLoadedResources.Remove(resourcePath);
            }

            loadTask = loader();
            LoadingResources[resourcePath] = loadTask;
        }
        finally
        {
            LoadedResourcesSemaphore.Release();
        }

        try
        {
            return await loadTask;
        }
        finally
        {
            await LoadedResourcesSemaphore.WaitAsync();

            try
            {
                if (LoadingResources.TryGetValue(resourcePath, out var existingTask) &&
                    existingTask == loadTask)
                {
                    if (loadTask.IsCompletedSuccessfully)
                    {
                        var resource = await loadTask;
                        WeakLoadedResources[resourcePath] = resource.GetWeakRef();
                    }

                    LoadingResources.Remove(resourcePath);
                }
            }
            finally
            {
                LoadedResourcesSemaphore.Release();
            }
        }
    }






#if DEBUG

    public static readonly Dictionary<string, Type> GameResourceFileAssociations = new();

    
    /// <summary>
    /// Debug-only method that scans for <see cref="FileExtensionAssociationAttribute"/>s on <see cref="GameResource"/> types.
    /// </summary>
    /// <exception cref="Exception"></exception>
    [Conditional("DEBUG")]
    public static void ScanResourceAssociations()
    {
        GameResourceFileAssociations.Clear();

        var types = Assembly.GetExecutingAssembly().GetTypes().Where(x => x.IsAssignableTo(typeof(GameResource)));

        foreach (var type in types)
        {
            foreach (var attrib in type.GetCustomAttributes<FileExtensionAssociationAttribute>())
            {
                if (!GameResourceFileAssociations.TryAdd(attrib.Extension, type)) 
                    throw new Exception($"Multiple resources associated with extension '.{attrib.Extension}'");
            }
        }
    }


#endif





}



/// <summary>
/// Indicates that this type is associated with a particular file extension at development time.
/// </summary>
/// <param name="extension"></param>
/// 
[Conditional("DEBUG")]
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = true)]
public sealed class FileExtensionAssociationAttribute(string extension) : Attribute
{
    public readonly string Extension = $".{extension.Trim().TrimStart('.')}";
}


