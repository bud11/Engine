

namespace Engine.Core;

using Attributes;
using Engine.GameResources;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ZstdSharp;

#if DEBUG
using System.IO.Hashing;
using System.Reflection;
#endif





/// <summary>
/// Facilitates loading and/or indexing assets from the asset directory.
/// </summary>
public static partial class Loading
{


    /// <summary>
    /// Header for a compressed asset file. <br/>
    /// <paramref name="Type"/> will match the type of the asset assuming the original file extension was correctly associated in <see cref="GameResource.GameResourceFileExtensionMap"/>. Otherwise it will be null.
    /// <br/> <paramref name="Length"/> is the size of the uncompressed asset within the archive, not the literal size within the archive.
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
                var assetCount = filestream.ReadUnmanagedType<uint>();
                for (uint i = 0; i < assetCount; i++)
                {
                    var assetOffset = filestream.ReadUnmanagedType<ulong>();
                    var assetLength = filestream.ReadUnmanagedType<ulong>();
                    var assetPath = filestream.ReadUintLengthPrefixedUTF8String();
                    Type assetType = Parsing.GetGameResourceTypeFromTypeID(filestream.ReadUnmanagedType<ushort>());

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








    /// <summary>
    /// Returns a stream over the asset at <paramref name="assetPath"/>, <b> which must be manually disposed of. </b>
    /// <br/> This is very literally just a read only file stream operation and should only manually be used if you're confident you want to read an asset file without engine-aided processing/conversion/resource association.
    /// </summary>
    /// <param name="assetPath"></param>
    /// <returns></returns>
    public static AssetByteStream AcquireAssetByteStream(string assetPath)
    {

#if DEBUG
        assetPath = Path.Combine(EngineSettings.RootAssetDirectoryPath, assetPath);

        if (!AssetExists(assetPath)) 
            throw new Exception();

        return new AssetByteStream(File.OpenRead(assetPath), new FileInfo(assetPath).Length);
#else

        var entry = AssetLookupDirect[assetPath];

        var fs = File.OpenRead(entry.archivePath);
        fs.Position = (long)entry.Offset;

        return new AssetByteStream(new DecompressionStream(fs, bufferSize: (int)entry.Length, leaveOpen: false), (long)entry.Length);
#endif

    }




    /// <summary>
    /// Represents a read-only, advance-only stream over one specific asset file.
    /// </summary>
    public sealed class AssetByteStream : Stream
    {
        private readonly Stream _baseStream;
        private readonly long _length;
        private long _remaining;

        public AssetByteStream(Stream baseStream, long length)
        {
            _baseStream = baseStream ?? throw new ArgumentNullException(nameof(baseStream));
            ArgumentOutOfRangeException.ThrowIfNegative(length);

            _length = length;
            _remaining = length;
        }

        /// <summary>
        /// Reads the entire asset into a byte[] and disposes the stream.
        /// </summary>
        public async Task<byte[]> GetArray()
        {
            var buffer = new byte[_length];
            int offset = 0;

            while (_remaining > 0)
            {
                int read = await ReadAsync(buffer.AsMemory(offset, (int)Math.Min(int.MaxValue, _remaining))).ConfigureAwait(false);

                if (read == 0)
                    break;

                offset += read;
            }

            Dispose();
            return buffer;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                _baseStream.Dispose();

            base.Dispose(disposing);
        }

        public override bool CanRead => _baseStream.CanRead;
        public override bool CanSeek => false;
        public override bool CanWrite => false;

        public override long Length => _length;

        public override long Position
        {
            get => _length - _remaining;
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_remaining <= 0)
                return 0;

            count = (int)Math.Min(count, _remaining);
            int read = _baseStream.Read(buffer, offset, count);
            _remaining -= read;
            return read;
        }

        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            if (_remaining <= 0)
                return 0;

            int count = (int)Math.Min(buffer.Length, _remaining);
            int read = await _baseStream
                .ReadAsync(buffer.Slice(0, count), cancellationToken)
                .ConfigureAwait(false);

            _remaining -= read;
            return read;
        }

        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
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
    /// Debug-only method that deletes development-time cache files which don't have an existing matching asset.
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


            AssetByteStream stream = null;


#if DEBUG


            //scans for an existing file according to ResourceFileExtensionMap

            var ValidFileExtensions = GameResourceFileAssociations.Where(x => x.Value == typeof(T)).Select(x => x.Key);



            foreach (var v in ValidFileExtensions)
            {
                var chk = resourcePath + v;
                if (AssetExists(chk))
                {
                    loadpath = chk;
                    break;
                }
            }


            if (resourcePath == loadpath) 
                throw new Exception($"Resource '{resourcePath}' not found (scanned for {string.Join(", ", ValidFileExtensions)})");




            //if type converts, try loading cached result, otherwise convert and cache result if cached file doesnt exist or doesnt match

            stream = await GetFinalAssetBytes<T>(Path.Combine(EngineSettings.RootAssetDirectoryPath, loadpath));


#else
            stream = AcquireAssetByteStream(loadpath);
#endif


            var res = await Parsing.ConstructGameResourceFromGeneric<T>(stream, resourcePath);

            res.Init();

            return res;

        });

       
    }






#if DEBUG



    /// <summary>
    /// This method gets the final asset bytes (converting and caching the source file if nessecary) and should not be used directly.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="loadpath"></param>
    /// <returns></returns>
    /// 
    public static async Task<AssetByteStream> GetFinalAssetBytes<T>(string loadpath) where T : GameResource
    {
        var relative = Path.GetRelativePath(EngineSettings.RootAssetDirectoryPath, loadpath);



        var loadingDbgString = $"Loading asset file '{relative}'";
        var convertingDbgString = $"Converting/caching asset file '{relative}'";




        liststatusChange(assetloadingstatusForDebugMsg.Loading);



        loadpath = Path.GetFullPath(loadpath);


        AssetByteStream finalStream = null;



        var convertcheck = typeof(T).GetMethod(nameof(GameResource.ConvertToFinalAssetBytes));

        if (convertcheck != null)
        {


            var cachedFileDir = Path.Combine(EngineSettings.AssetCachePath, Path.GetRelativePath(EngineSettings.RootAssetDirectoryPath, loadpath) + ".cached");


            Directory.CreateDirectory(Directory.GetParent(cachedFileDir).FullName);


            var load = await AcquireAssetByteStream(loadpath).GetArray();


            uint crc = 0;

            if (FileExistsCaseSensitive(cachedFileDir))
            {
                crc = Crc32.HashToUInt32(load);


                var filestream = new FileStream(cachedFileDir, FileMode.Open, FileAccess.Read);



                uint storedCrc = filestream.ReadUnmanagedType<uint>();

                if (storedCrc == crc)
                {
                    statusPrint("Cached file found");

                    int remaining = (int)(filestream.Length - filestream.Position);

                    finalStream = new AssetByteStream(filestream, remaining);
                }
                else
                {
                    crc = 0;
                    filestream.Dispose();
                }


            }


            //cached file wasnt found or doesn't match
            if (crc == 0)
            {


                statusPrint($"Required cache missing or out of date for asset '{relative}', converting...");


                liststatusChange(assetloadingstatusForDebugMsg.Converting);


                var finalBytes = await (Task<byte[]>)typeof(T).GetMethod(nameof(GameResource.ConvertToFinalAssetBytes), BindingFlags.Static | BindingFlags.Public, [typeof(byte[]), typeof(string)]).Invoke(null, [load, loadpath]);


                using (var filestream = new FileStream(cachedFileDir, FileMode.Create))
                {
                    await filestream.WriteAsync(Crc32.Hash(load));
                    await filestream.WriteAsync(finalBytes);
                }

                finalStream = new AssetByteStream(new MemoryStream(finalBytes), finalBytes.Length);

            }

        }


        else finalStream = AcquireAssetByteStream(loadpath);




        statusPrint($"Final asset bytes loaded for '{relative}'");


        liststatusChange(assetloadingstatusForDebugMsg.Done);



        return finalStream;




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

    public static readonly List<string> AssetLoadingDebugList = new();




    
/*
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
        return (GameObjectType)await InternalLoadOrFetchResource(resourcePath, async () => await GameResource.__StaticVirtual_Load<GameObjectType>(bytes, key));
    }
*/


#endif

















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





#if DEBUG

    public static readonly Dictionary<string, Type> GameResourceFileAssociations = new();

    
    /// <summary>
    /// Debug-only method that searches for <see cref="FileExtensionAssociationAttribute"/>s on <see cref="GameResource"/>s
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

