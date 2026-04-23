using ZstdSharp;

namespace Engine.Core;






public static partial class IO
{



    // --------------- !!!!! Changing these paths will break .props, the build process, .gitignore and potentially external tools !!!!! --------------- \\



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





    /// <summary>
    /// Header for a compressed asset file. <br/>
    /// <paramref name="Type"/> will match the type of the asset assuming the original file extension was correctly associated in <see cref="GameResource.GameResourceFileExtensionMap"/>. Otherwise it will be null.
    /// <br/> <paramref name="Length"/> is the size of the uncompressed asset within the archive, not the literal size within the archive.
    /// </summary>
    /// <param name="Type"></param>
    /// <param name="Offset"></param>
    /// <param name="Length"></param>
    public readonly record struct AssetDataRange(string archivePath, Type Type, ulong Offset, ulong Length);



    public static readonly Dictionary<string, AssetDataRange> AssetLookupDirect = new();
    public static readonly Dictionary<string, Dictionary<string, AssetDataRange>> FolderLookupDirect = new();










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
    /// Returns a stream over the asset at <paramref name="assetPath"/>, <b> which must be manually disposed of. </b>
    /// <br/> This is very literally just a read only file stream operation and should only manually be used if you're confident you want to read an asset file without engine-aided processing/conversion/resource association.
    /// </summary>
    /// <param name="assetPath"></param>
    /// <returns></returns>
    public static AssetByteStream AcquireAssetByteStream(string assetPath)
    {

#if DEBUG
        assetPath = Path.GetFullPath(Path.Combine(AssetRootDirectoryPath, assetPath));

        if (!AssetFileExists(assetPath))
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
    /// Returns true if an asset file exists. 
    /// </summary>
    /// <param name="path"></param>
    /// <returns></returns>
    public static bool AssetFileExists(string path)
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








    /// <summary>
    /// Debug-only method that deletes any now-redundant development-time cache files.
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











    public static string RelativePathToFullPath(string path, string baseDir)
    {
        if (!path.StartsWith('/')) path = baseDir + path;
        return path;
    }






    /// <summary>
    /// (Re)scans for (registered, not arbitrary) asset archives>. Only affects release builds.
    /// </summary>

    public static unsafe void ScanForAssetArchives()
    {
#if RELEASE && !ENGINE_BUILD_PASS

        AssetLookupDirect.Clear();
        FolderLookupDirect.Clear();


        //find archives, load headers

        foreach (string archiveName in AssetArchiveNames)
        {
            var archivePath = Path.Combine(Directory.GetCurrentDirectory(), archiveName);

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

        void Throw() =>
            throw new Exception($"Invalid path '{filePath}'; Cannot load asset from outside of a subfolder of the root asset directory '{AssetRootDirectoryPath}'");
    }
}
