

namespace Engine.Core;

using Attributes;
using Engine.GameResources;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using ZstdSharp;

#if DEBUG
using System.IO.Hashing;
#endif





/// <summary>
/// Facilitates loading and/or indexing assets from the asset directory.
/// </summary>
public static partial class Loading
{


    /// <summary>
    /// Header for a compressed asset file. <br/>
    /// <paramref name="Type"/> will match the type of the asset assuming the original file extension was correctly associated in <see cref="GameResource.GameResourceFileExtensionMap"/>. Otherwise it will be null.
    /// </summary>
    /// <param name="Type"></param>
    /// <param name="Offset"></param>
    /// <param name="Length"></param>
    public readonly record struct AssetDataRange(string archivePath, Type Type, ulong Offset, ulong Length);

    private static readonly Dictionary<string, AssetDataRange> AssetLookupDirect = new();
    private static readonly Dictionary<string, Dictionary<string, AssetDataRange>> FolderLookupDirect = new();






    /// <summary>
    /// (Re)scans for (registered, not arbitrary) asset archives within <see cref="EngineSettings.ReleaseAssetArchivesPath"/>. Only affects release builds.
    /// </summary>
    
    public static unsafe void ScanForAssetArchives()
    {
#if RELEASE && !ENGINE_BUILD_PASS

        AssetLookupDirect.Clear();
        FolderLookupDirect.Clear();


        //find archives, load headers

        foreach (string archiveName in AssetArchiveNames)
        {
            var archivePath = Path.Combine(EngineSettings.ReleaseRootAssetArchivePath, archiveName);

            using (var filestream = new FileStream(archivePath, FileMode.Open, FileAccess.Read))
            {
                var assetCount = filestream.ReadType<uint>();
                for (uint i = 0; i < assetCount; i++)
                {
                    var assetOffset = filestream.ReadType<ulong>();
                    var assetLength = filestream.ReadType<ulong>();
                    var assetPath = filestream.ReadUintLengthPrefixedUTF8String();
                    var associatedTypeName = filestream.ReadUintLengthPrefixedUTF8String();
                    Type assetType = associatedTypeName == string.Empty ? null : AssetTypeLookup[associatedTypeName];


                    AssetLookupDirect[$"{archiveName}/{assetPath}"] = new AssetDataRange(archivePath, assetType, assetOffset, assetLength);
                }
            }
        }


        //construct folder lookup

        foreach (var kv in AssetLookupDirect)
        {
            var idxof = kv.Key.LastIndexOf('/');
            var container = kv.Key[..idxof];

            if (!FolderLookupDirect.TryGetValue(container, out var dict)) FolderLookupDirect[container] = dict = new();

            dict[kv.Key[(idxof + 1)..]] = kv.Value;
        }

#endif
    }







    public static async Task<byte[]> LoadAssetBytes(string assetPath)
    {

#if DEBUG
        assetPath = Path.Combine(EngineSettings.RootAssetDirectoryPath, assetPath);

        if (!AssetExists(assetPath)) 
            throw new Exception();

        return await File.ReadAllBytesAsync(assetPath);
#else

        var entry = AssetLookupDirect[assetPath];

        using var fs = File.OpenRead(entry.archivePath);
        fs.Position = (long)entry.Offset;

        using var zstdStream = new DecompressionStream(fs);
        using var ms = new MemoryStream();

        await zstdStream.CopyToAsync(ms);
        return ms.ToArray();
#endif

    }




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


        filepaths = [.. Directory.GetFiles(Path.Combine(EngineSettings.RootAssetDirectoryPath, path)).Select(x => Path.GetRelativePath(target, x))];

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
    /// Returns true if an asset file exists. 
    /// </summary>
    /// <param name="path"></param>
    /// <returns></returns>
    public static bool AssetExists(string path)
    {
#if DEBUG

        path = Path.Combine(EngineSettings.RootAssetDirectoryPath, path);

        IsInDirectSubfolderCheck(path);

        return FileExistsCaseSensitive(path);
#else
        return AssetLookupDirect.ContainsKey(path);
#endif
    }




    /// <summary>
    /// Returns true if an asset folder exists. 
    /// </summary>
    /// <param name="path"></param>
    /// <returns></returns>
    public static bool AssetFolderExists(string path)
    {
#if DEBUG

        path = Path.Combine(EngineSettings.RootAssetDirectoryPath, path);

        IsInDirectSubfolderCheck(path);

        return DirectoryExistsCaseSensitive(path);
#else
        return FolderLookupDirect.ContainsKey(path);
#endif

    }






#if DEBUG



    private static void IsInDirectSubfolderCheck(string filePath)
    {
        var parentFolder = EngineSettings.RootAssetDirectoryPath;

        var fileDirectory = Path.GetDirectoryName(filePath);
        if (fileDirectory == null)
            Throw();

        var parentOfFile = Directory.GetParent(fileDirectory)?.FullName;
        if (parentOfFile == null)
            Throw();

        if (!string.Equals(
            parentOfFile.TrimEnd(Path.DirectorySeparatorChar),
            parentFolder.TrimEnd(Path.DirectorySeparatorChar),
            StringComparison.OrdinalIgnoreCase
        )) Throw();

        static void Throw() => 
            throw new Exception("cannot load asset from outside of a subfolder of the root asset directory");
    }








    /// <summary>
    /// Deletes development cache files that don't have an existing matching asset.
    /// </summary>
    public static void CleanAssetCache()
    {

        if (!DirectoryExistsCaseSensitive(EngineSettings.AssetCachePath))
            return;


        foreach (var cacheFile in Directory.EnumerateFiles(EngineSettings.AssetCachePath, "*", SearchOption.AllDirectories))
        {
            try
            {
                var relativePath = Path.GetRelativePath(EngineSettings.AssetCachePath, cacheFile);
                var sourcePath = Path.Combine(EngineSettings.RootAssetDirectoryPath, relativePath).Replace(".cached", string.Empty);

                if (!FileExistsCaseSensitive(sourcePath))
                {
                    File.Delete(cacheFile);
                    continue;
                }

                var info = new FileInfo(cacheFile);
                if (info.Length < 16)
                {
                    File.Delete(cacheFile);
                }
            }
            catch
            {

                try { File.Delete(cacheFile); } catch { }
            }
        }



        RemoveEmptyDirectories(EngineSettings.AssetCachePath);

        static void RemoveEmptyDirectories(string root)
        {
            foreach (var dir in Directory.GetDirectories(root))
            {
                RemoveEmptyDirectories(dir);

                if (!Directory.EnumerateFileSystemEntries(dir).Any())
                {
                    Directory.Delete(dir);
                }
            }
        }
    }


#endif






    /// <summary>
    /// Loads a <see cref="GameResource"/> from the game's asset folder (see <see cref="EngineSettings.RootAssetDirectoryPath"/>, <see cref="GameResource.Load(byte[], string)"></see> and <see cref="GameResource.GameResourceFileExtensionMap"/>).
    /// <br /> <b><paramref name="resourcePath"/> should be relative to the asset folder and should not include a file extension. </b>
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="resourcePath"></param>
    /// <returns></returns>
    public static async Task<T> LoadResource<T>(string resourcePath) where T : GameResource
    {

        return (T) await InternalLoadOrFetchResource(resourcePath, async () =>
        {

            var loadpath = resourcePath;  //gets mutated, not useless


            byte[] finalBytes = null;


#if DEBUG

            //scans for an existing file according to ResourceFileExtensionMap

            var ValidFileExtensions = GameResource.GameResourceFileExtensionMap.Where(x => x.Value == typeof(T)).Select(x => x.Key);

            if (ValidFileExtensions.Contains(null)) throw new Exception("Resource excluded from loading via presence of null extension key");


            foreach (var v in ValidFileExtensions)
            {
                var chk = resourcePath + v;
                if (AssetExists(chk))
                {
                    loadpath = chk;
                    break;
                }
            }


            if (resourcePath == loadpath) throw new Exception($"Resource '{resourcePath}' not found (scanned for {string.Join(", ", ValidFileExtensions)})");



            //if type converts, try loading cached result, otherwise convert and cache result if cached file doesnt exist or doesnt match

            finalBytes = await LoadFinalAssetBytes<T>(Path.Combine(EngineSettings.RootAssetDirectoryPath, loadpath));


#else
            finalBytes = await LoadAssetBytes(loadpath);
#endif


            var res = await StaticVirtuals.Engine_Core_GameResource_Load<T>(finalBytes, resourcePath);

            res.Init();

            return res;

        });

       
    }






#if DEBUG


    public static readonly List<string> AssetLoadingDebugList = new();


    /// <summary>
    /// This method returns final asset bytes (converted and cached if nessecary) and should not be used directly.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="loadpath"></param>
    /// <returns></returns>
    public static async Task<byte[]> LoadFinalAssetBytes<T>(string loadpath) where T : GameResource
    {
        var relative = Path.GetRelativePath(EngineSettings.RootAssetDirectoryPath, loadpath);



        var loadingDbgString = $"Loading asset file '{relative}'";
        var convertingDbgString = $"Converting/caching asset file '{relative}'";




        liststatusChange(assetloadingstatusForDebugMsg.Loading);



        loadpath = Path.GetFullPath(loadpath);


        byte[] finalBytes = null;


        var convertcheck = typeof(T).GetMethod(nameof(GameResource.ConvertToFinalAssetBytes));

        if (convertcheck != null && convertcheck.GetCustomAttribute<StaticVirtualOverrideAttribute>() != null)
        {


            var cachedFileDir = Path.Combine(EngineSettings.AssetCachePath, Path.GetRelativePath(EngineSettings.RootAssetDirectoryPath, loadpath) + ".cached");


            Directory.CreateDirectory(Directory.GetParent(cachedFileDir).FullName);


            var load = await LoadAssetBytes(loadpath);


            uint crc = 0;

            if (FileExistsCaseSensitive(cachedFileDir))
            {
                crc = Crc32.HashToUInt32(load);


                using (var filestream = new FileStream(cachedFileDir, FileMode.Open, FileAccess.Read))
                {
                    byte[] stored = new byte[sizeof(uint)];

                    await filestream.ReadExactlyAsync(stored);

                    uint storedCrc = BitConverter.ToUInt32(stored);


                    if (storedCrc == crc)
                    {

                        statusPrint("Cached file found");

                        int remaining = (int)(filestream.Length - filestream.Position);
                        finalBytes = new byte[remaining];
                        await filestream.ReadExactlyAsync(finalBytes);
                    }
                    else
                    {
                        crc = 0;
                    }
                }
            }


            //cached file wasnt found or doesn't match
            if (crc == 0)
            {
                statusPrint($"Required cache missing or out of date for asset '{relative}', converting...");


                liststatusChange(assetloadingstatusForDebugMsg.Converting);


                finalBytes = await StaticVirtuals.Engine_Core_GameResource_ConvertToFinalAssetBytes<T>(load, loadpath);

                using (var filestream = new FileStream(cachedFileDir, FileMode.Create))
                {
                    await filestream.WriteAsync(Crc32.Hash(load));
                    await filestream.WriteAsync(finalBytes);
                }

            }

        }


        else finalBytes = await LoadAssetBytes(loadpath);




        statusPrint($"Final asset bytes loaded for '{relative}'");


        liststatusChange(assetloadingstatusForDebugMsg.Done);



        return finalBytes;




        static void statusPrint(string p)
        {

#if ENGINE_BUILD_PASS
            Console.WriteLine(
#else
            Debug.Print(
#endif
            p);
        }



        void liststatusChange(assetloadingstatusForDebugMsg status)
        {
            lock (AssetLoadingDebugList)
            {
                AssetLoadingDebugList.Remove(loadingDbgString);
                AssetLoadingDebugList.Remove(convertingDbgString);

                switch (status)
                {
                    case assetloadingstatusForDebugMsg.Loading:
                        AssetLoadingDebugList.Add(loadingDbgString);
                        return;

                    case assetloadingstatusForDebugMsg.Converting:
                        AssetLoadingDebugList.Add(convertingDbgString);
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


#endif












    /// <summary>
    /// Prepares a <typeparamref name="GameObjectType"/> from a final byte array (bytes already ran through the type's <see cref="GameResource.ConvertToFinalAssetBytes(byte[], string)"/> override, if there is one).
    /// </summary>
    /// <typeparam name="GameObjectType"></typeparam>
    /// <param name="bytes"></param>
    /// <param name="key"></param>
    /// <param name="resourcePath"></param>
    /// <returns></returns>
    public static async Task<GameObjectType> ConstructResourceFromFinalBytes<GameObjectType>(byte[] bytes, string key, string resourcePath = null) where GameObjectType : GameResource
    {
        return (GameObjectType)await InternalLoadOrFetchResource(resourcePath, async () => await StaticVirtuals.Engine_Core_GameResource_Load<GameObjectType>(bytes, key));
    }








    private static readonly SemaphoreSlim LoadedResourcesSemaphore = new(1, 1);

    private static readonly Dictionary<string, TaskCompletionSource<GameResource>> LoadedResources = new();

    /// <summary>
    /// If resource of <paramref name="resourcePath"/> isn't registered, invokes <paramref name="loader"/> to load and register it. Thread safe.
    /// </summary>
    /// <param name="resourcePath"></param>
    /// <param name="loader"></param>
    /// <returns></returns>
    public static async Task<GameResource> InternalLoadOrFetchResource(string resourcePath, Func<Task<GameResource>> loader)
    {
        //acquire semaphore
        await LoadedResourcesSemaphore.WaitAsync();


        //see if resource is already being loaded, if so, return that task
        if (LoadedResources.TryGetValue(resourcePath, out var loadstatus) &&
            !(loadstatus.Task.IsCompletedSuccessfully && !loadstatus.Task.Result.Valid))
        {
            LoadedResourcesSemaphore.Release();
            return await loadstatus.Task;
        }

        
        //otherwise, set up a task source and release the semaphore
        var tcs = new TaskCompletionSource<GameResource>();
        LoadedResources[resourcePath] = tcs;
        LoadedResourcesSemaphore.Release();


        //load the resource
        var resource = await loader.Invoke();  

        return resource;
    }


    public static void SetResourceLoaded(this GameResource resource)
    {
        LoadedResourcesSemaphore.Wait();

        if (LoadedResources.TryGetValue(resource.Key, out var get))
            if (get.Task.IsCompletedSuccessfully)
                throw new Exception($"Resource {resource.Key} already registered");

        else
            LoadedResources[resource.Key] = get = new TaskCompletionSource<GameResource>();


        get.SetResult(resource);

        LoadedResourcesSemaphore.Release();

    }


    public static void SetResourceUnloaded(this GameResource resource)
    {
        LoadedResourcesSemaphore.Wait();
        LoadedResources.Remove(resource.Key);
        LoadedResourcesSemaphore.Release();
    }



    public static void UnloadAllResources()
    {
        LoadedResourcesSemaphore.Wait();
        var l = LoadedResources.Values.ToArray();
        LoadedResourcesSemaphore.Release();

        foreach (var entry in l) entry.Task.Result.Free();

        LoadedResourcesSemaphore.Wait();
        LoadedResources.Clear();
        LoadedResourcesSemaphore.Release();
    }











    public static string RelativePathToFullPath(string path, string baseDir)
    {
        if (!path.StartsWith('/')) path = baseDir + path;
        return path;
    }






    /// <summary>
    /// Determines whether a file exists. Always case sensitive unlike <see cref="File.Exists(string?)"/>
    /// </summary>
    /// <param name="path"></param>
    /// <returns></returns>
    public static bool FileExistsCaseSensitive(string path)
    {
        var fullPath = Path.GetFullPath(path);

        var directory = Path.GetDirectoryName(fullPath);
        var fileName = Path.GetFileName(fullPath);

        if (directory == null || !Directory.Exists(directory))
            return false;

        foreach (var entry in Directory.EnumerateFiles(directory, fileName))
        {
            if (Path.GetFileName(entry) == fileName)
                return true;
        }

        return false;
    }


    /// <summary>
    /// Determines whether a directory exists. Always case sensitive unlike <see cref="Directory.Exists(string?)"/>
    /// </summary>
    /// <param name="path"></param>
    /// <returns></returns>
    public static bool DirectoryExistsCaseSensitive(string path)
    {
        var fullPath = Path.GetFullPath(path);

        var parent = Path.GetDirectoryName(fullPath);
        var name = Path.GetFileName(fullPath);

        if (parent == null || !Directory.Exists(parent))
            return false;

        foreach (var entry in Directory.EnumerateDirectories(parent, name))
        {
            if (Path.GetFileName(entry) == name)
                return true;
        }

        return false;
    }





}

