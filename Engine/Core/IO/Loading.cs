




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
/// Facilitates loading/indexing asset files/<see cref="GameResource"/>s.
/// </summary>
public static partial class Loading
{


    /// <summary>
    /// A throttle for the maximum amount of resources that can be asynchronously loading at once. 0 = uncapped
    /// </summary>
    public static int MaxParallelAssetLoads = 0;



#if DEBUG

    public static bool PrintAssetLoadingStatus = false;

    /// <summary>
    /// A throttle for the maximum amount of resources that can be asynchronously converted via <see cref="GameResource.IConverts.ConvertToFinalAssetBytes(Bytes, string)"/> at once. 0 = uncapped
    /// </summary>
    public static int MaxParallelAssetConversions = 0;

#endif








    // --------------- !!!!! Changing these paths will break .props, the build process, .gitignore and potentially external tools !!!!! --------------- \\


#if DEBUG

    /// <summary>
    /// The path to the development-time asset root directory. 
    /// <br/> Each immediate sub folder contained within will be compressed into its own separate archive. For example, if this directory contains two folders, One and Two, then release builds will feature two archives respectively named One and Two.
    /// <br/> <b>This directory cannot directly contain assets.</b>
    /// </summary>
    public static readonly string AssetRootDirectoryPath = Path.GetFullPath("../../../../AssetRoot");


    /// <summary>
    /// The path to the development-time directory used to cache final asset data that has undergone development-time conversion.
    /// </summary>
    public static readonly string AssetCachePath = Path.GetFullPath("../../../../AssetRootCache");

#endif


    /// <summary>
    /// The path to the release-build directory which contains asset archives.
    /// </summary>
    public static readonly string ReleaseRootAssetArchivePath = Directory.GetCurrentDirectory();

    // ------------------------------------------------------------------------------------------------------------------------------------ \\















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
    /// (Re)scans for (registered, not arbitrary) asset archives within <see cref="Loading.ReleaseRootAssetArchivePath"/>. Only affects release builds.
    /// </summary>
    
    public static unsafe void ScanForAssetArchives()
    {
#if RELEASE && !ENGINE_BUILD_PASS

        AssetLookupDirect.Clear();
        FolderLookupDirect.Clear();


        //find archives, load headers

        foreach (string archiveName in AssetArchiveNames)
        {
            var archivePath = Path.Combine(Loading.ReleaseRootAssetArchivePath, archiveName);

            using (var filestream = new FileStream(archivePath, FileMode.Open, FileAccess.Read))
            {
                var read = Parsing.ValueReader.FromStream(filestream);

                var assetCount = read.ReadUnmanaged<uint>();
                for (uint i = 0; i < assetCount; i++)
                {
                    var assetOffset = read.ReadUnmanaged<ulong>();
                    var assetLength = read.ReadUnmanaged<ulong>();
                    var assetPath = read.ReadString();
                    Type assetType = GameResource.GetGameResourceTypeFromTypeID(read.ReadUnmanaged<ushort>());

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
        assetPath = Path.GetFullPath(Path.Combine(AssetRootDirectoryPath, assetPath));

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
            _baseStream.Dispose();
            base.Dispose(true);
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
                .ReadAsync(buffer[..count], cancellationToken)
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
    /// Returns true if an asset file exists. 
    /// </summary>
    /// <param name="path"></param>
    /// <returns></returns>
    public static bool AssetExists(string path)
    {
#if DEBUG

        path = Path.Combine(AssetRootDirectoryPath, path);

        IsInDirectSubfolderCheck(path);

        return FileExistsCaseSensitive(path);
#else
        return AssetLookupDirect.ContainsKey(path);
#endif
    }




    /// <summary>
    /// Returns true if an asset folder or archive exists. 
    /// </summary>
    /// <param name="path"></param>
    /// <returns></returns>
    public static bool AssetFolderExists(string path)
    {
#if DEBUG

        path = Path.Combine(AssetRootDirectoryPath, path);

        IsInDirectSubfolderCheck(path);

        return DirectoryExistsCaseSensitive(path);
#else
        return FolderLookupDirect.ContainsKey(path);
#endif

    }






#if DEBUG



    private static void IsInDirectSubfolderCheck(string filePath)
    {
        var parentFolder = AssetRootDirectoryPath;

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




        if (!DirectoryExistsCaseSensitive(AssetCachePath))
            return;



        // delete entire cache for debug
        //Directory.Delete(AssetCachePath, true); return;



        foreach (var cacheFile in Directory.EnumerateFiles(AssetCachePath, "*", SearchOption.AllDirectories))
        {
            try
            {
                var relativePath = Path.GetRelativePath(AssetCachePath, cacheFile);
                var sourcePath = Path.Combine(AssetRootDirectoryPath, relativePath).Replace(".cached", string.Empty);

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



        RemoveEmptyDirectories(AssetCachePath);

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
    /// Loads a <see cref="GameResource"/> from a folder/archive within the game's asset root directory (see <see cref="AssetRootDirectoryPath"/>).
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
                if (AssetExists(chk))
                {
                    loadpath = chk;
                    break;
                }
            }


            if (resourcePath == loadpath) 
                throw new Exception($"Resource '{resourcePath}' not found (scanned for {string.Join(", ", ValidFileExtensions)})");




            //if type converts, try loading cached result, otherwise convert and cache result if cached file doesnt exist or doesnt match

            stream = await GetFinalAssetBytes<T>(loadpath);


#else
            stream = AcquireAssetByteStream(loadpath);
#endif


            var res = await GameResource.LoadGameResourceFromGenericAndStream<T>(stream, resourcePath);

            res.Register();


            stream.Dispose();

            return res;

        });

       
    }






#if DEBUG



    /// <summary>
    /// Caches and/or fetches the asset file at <paramref name="relativePath"/>'s final bytes.
    /// <br/> This is a development-time debug-only method and usually shouldn't be used directly.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="relativePath"></param>
    /// <returns></returns>
    public static async Task<AssetByteStream> GetFinalAssetBytes<T>(string relativePath) where T : GameResource
    {
        return await GetFinalAssetBytes<T>(new Bytes(await AcquireAssetByteStream(relativePath).GetArray()), relativePath);
    }

    public static Task<AssetByteStream> GetFinalAssetBytes(Type resourceType, string relativePath)
    {
       return (Task<AssetByteStream>)typeof(Loading).GetMethod(nameof(GetFinalAssetBytes), [typeof(string)]).MakeGenericMethod(resourceType).Invoke(null, [relativePath]);
    }

    public static Task<AssetByteStream> GetFinalAssetBytes(Type resourceType, Bytes unconvertedData, string relativePath = null)
    {
        return (Task<AssetByteStream>)typeof(Loading).GetMethod(nameof(GetFinalAssetBytes), [typeof(Bytes), typeof(string)]).MakeGenericMethod(resourceType).Invoke(null, [unconvertedData, relativePath]);
    }




    /// <summary>
    /// A disposable wrapper over <see cref="byte[]"/> (simply sets own reference to <paramref name="byteArray"/> to null upon disposal to allow GC).
    /// </summary>
    public sealed class Bytes(byte[] byteArray) : IDisposable
    {
        public byte[] ByteArray = byteArray;
        public void Dispose() => ByteArray = null;
    }







    private static readonly SemaphoreSlim AssetConversionThrottle = new(MaxParallelAssetConversions);


    public static async Task EnterAssetConversionSemaphore()
    {
        if (MaxParallelAssetConversions != 0) await AssetConversionThrottle.WaitAsync();
    }
    public static void ExitAssetConversionSemaphore()
    {
        if (MaxParallelAssetConversions != 0) AssetConversionThrottle.Release();
    }




    /// <summary>
    /// Fetches and/or caches an asset file's final bytes, assuming the asset file's contents are <paramref name="unconvertedData"/>, and its path on disk is <paramref name="relativePath"/>.
    /// <br/>
    /// <br/> This is a development-time debug-only method and usually shouldn't be used directly.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="unconvertedData"></param>
    /// <param name="relativePath"></param>
    /// <returns></returns>
    public static async Task<AssetByteStream> GetFinalAssetBytes<T>(Bytes unconvertedData, string relativePath = null) where T : GameResource
    {

       


        var loadingDbgString = $"Loading asset file '{relativePath}'";
        var convertingDbgString = $"Converting/caching asset file '{relativePath}'";



        liststatusChange(assetloadingstatusForDebugMsg.Loading);



        AssetByteStream finalStream = null;



        var convertcheck = typeof(T).IsAssignableTo(typeof(GameResource.IConverts));


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


                            var method = typeof(T)
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
                await EnterAssetConversionSemaphore();


                statusPrint($"Required cache missing or out of date for asset '{relativePath}', converting...");


                liststatusChange(assetloadingstatusForDebugMsg.Converting);




                crc = Crc32.HashToUInt32(unconvertedData.ByteArray);


                //  ---------------------- zstd detection / decompression ----------------------

                byte[] zstdMagic = [0x28, 0xB5, 0x2F, 0xFD];
                bool zstd = true;

                for (int i = 0; i < 4; i++)
                {
                    if (unconvertedData.ByteArray[i] != zstdMagic[i])
                        zstd = false;
                        break;
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




                var method = typeof(T)
                    .GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                    .Single(m =>
                        m.Name.Contains("ConvertToFinalAssetBytes") &&
                        m.GetParameters().Length == 2 &&
                        m.ReturnType == typeof(Task<GameResource.IConverts.FinalAssetBytes>)
                    );


                var finalBytes = await (Task<GameResource.IConverts.FinalAssetBytes>)method.Invoke(null, [unconvertedData, relativePath]);


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


                ExitAssetConversionSemaphore();
            }
        }



        else finalStream = AcquireAssetByteStream(relativePath);



        statusPrint($"Final asset bytes loaded for asset '{relativePath}'");


        liststatusChange(assetloadingstatusForDebugMsg.Done);




        return finalStream;




        void statusPrint(string p)
        {

#if !ENGINE_BUILD_PASS
            if (PrintAssetLoadingStatus)
#endif

            if (relativePath != null)

#if ENGINE_BUILD_PASS
            Console.WriteLine(
#else
            Debug.Print(
#endif
            p);


        }



        void liststatusChange(assetloadingstatusForDebugMsg status)
        {

            if (relativePath != null)

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




#endif













    private static readonly SemaphoreSlim AssetLoadThrottle = new(MaxParallelAssetLoads);


    public static async Task EnterAssetLoadSemaphore()
    {
        if (MaxParallelAssetLoads != 0) await AssetLoadThrottle.WaitAsync();
    }
    public static void ExitAssetLoadSemaphore()
    {
        if (MaxParallelAssetLoads != 0) AssetLoadThrottle.Release();
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
        await EnterAssetLoadSemaphore();


        //acquire semaphore
        await LoadedResourcesSemaphore.WaitAsync();


        //see if resource is already being loaded, if so, return that task
        if (LoadedResources.TryGetValue(resourcePath, out var loadstatus))
        {
            LoadedResourcesSemaphore.Release();
            return await loadstatus.Task;    //LoadedResourcesSemaphore isnt being held. setresourceloaded calls.
        }


        //otherwise, set up a task source and release the semaphore
        LoadedResources[resourcePath] = loadstatus = new TaskCompletionSource<GameResource>();
        LoadedResourcesSemaphore.Release();

        //load the resource
        var resource = await loader.Invoke();





        ExitAssetLoadSemaphore();

        return resource;
    }


    public static async void SetResourceLoaded(this GameResource resource)
    {
        await LoadedResourcesSemaphore.WaitAsync();

        if (!LoadedResources.TryGetValue(resource.Key, out var get))
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


