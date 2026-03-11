
namespace Engine.Stripped;



using Engine.Core;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Numerics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using ZstdSharp;
using static Engine.Core.Loading;
using static Engine.Core.RenderingBackend;



/// <summary>
/// Converts assets if defined (see <see cref="GameResource.GameResourceFileExtensionMap"/> and <see cref="GameResource.ConvertToFinalAssetBytes(byte[], string)"/>), and compresses the Assets directory into zstd archives when building Engine in release mode.
/// </summary>
public static class EngineBuildProcess
{



    public static Dictionary<RenderingBackendEnum, Dictionary<string, ShaderSource>> ShaderSources = new();
    public static Dictionary<RenderingBackendEnum, Dictionary<string, ComputeShaderSource>> ComputeShaderSources = new();




    private static void Print(string s) => Console.WriteLine($" ===== ENGINE BUILD PROCESS: {s} =====");



    private static async Task Main(string[] args)
    {

        var compressionQuality = int.Parse(args[0]);





        CleanAssetCache();

        ScanResourceAssociations();






        Directory.CreateDirectory(ReleaseRootAssetArchivePath);


        foreach (var v in Enum.GetValues<RenderingBackendEnum>())
        {
            ShaderSources[v] = new();
            ComputeShaderSources[v] = new();
        }

        ShaderCompilation.CompileShaders();


        Print("Compiled all shaders");






        Dictionary<string, Type> AssetTypesFound = new();

        List<string> AssetArchiveNames = new();


        if (Directory.Exists(AssetRootDirectoryPath))
        {

            List<Task> archiveCompletionTasks = new();


            foreach (var archiveAbsolutePath in Directory.GetDirectories(AssetRootDirectoryPath))
            {



                var archiveName = Path.GetFileName(archiveAbsolutePath);


                AssetArchiveNames.Add(archiveName);


                archiveCompletionTasks.Add(Task.Run(async () =>
                {

                    if (DirectoryExistsCaseSensitive(archiveAbsolutePath))
                    {




                        List<byte> header = new();




                        var allFiles = Directory
                            .GetFiles(archiveAbsolutePath, "*", SearchOption.AllDirectories)
                            .OrderBy(x => x.Replace("\\", "/"))   // Deterministic order
                            .ToArray();



                        Print($"Asset archive '{archiveName}' found");





                        var tempPath = Path.Combine(ReleaseRootAssetArchivePath, archiveName + "_temp");

                        uint actualFiles = 0;


                        object l = new();

                        List<Task<AssetTaskData>> compressionTasks = new();


                        foreach (var assetAbsolutePath in allFiles)
                        {
                            compressionTasks.Add(
                                Task.Run(async () =>
                                {
                                    var relativePath = Path.GetRelativePath(archiveAbsolutePath, assetAbsolutePath);


                                    bool found = GameResourceFileAssociations.TryGetValue(Path.GetExtension(assetAbsolutePath), out var AssetFoundType);

                                    if (found && AssetFoundType == null)
                                    {
                                        Print($"File ignored: {relativePath}");
                                        return default;
                                    }



                                    lock (l)
                                        actualFiles++;


                                    byte[] rawBytes;


                                    if (found)
                                    {

                                        lock (AssetTypesFound)
                                            AssetTypesFound[AssetFoundType.Name] = AssetFoundType;


                                        Print($"File detected as {AssetFoundType.FullName}: {relativePath}");
                                        rawBytes = await (await (Task<AssetByteStream>)typeof(Loading).GetMethod(nameof(GetFinalAssetBytes), [typeof(string)]).MakeGenericMethod(AssetFoundType).Invoke(null, [relativePath])).GetArray();
                                        Print($"File processed successfully: {relativePath}");
                                    }
                                    else
                                    {
                                        Print($"File reading as unknown: {relativePath}");
                                        rawBytes = await File.ReadAllBytesAsync(assetAbsolutePath);
                                        Print($"File read successfully: {relativePath}");
                                    }


                                    using var compressor = new Compressor(compressionQuality);

                                    return new AssetTaskData(Path.ChangeExtension(relativePath, null), AssetFoundType, compressor.Wrap(rawBytes).ToArray(), (uint)rawBytes.Length);

                                })
                            );
                        }




                        ulong currentOffset = 0;

                        List<ulong> offsetsToCorrect = new();


                        using (var archiveStream = File.Create(tempPath))
                        {

                            for (int i = 0; i < compressionTasks.Count; i++)
                            {
                                var result = await compressionTasks[i]; // preserves order

                                if (result.data != null)
                                {
                                    archiveStream.Write(result.data, 0, result.data.Length);

                                    Print($"File written to archive '{archiveName}' successfully ({i}/{allFiles.Length}): {result.path}");


                                    offsetsToCorrect.Add((ulong)header.Count);

                                    header.AddRange(BitConverter.GetBytes((ulong)currentOffset));
                                    header.AddRange(BitConverter.GetBytes((ulong)result.uncompressedLength));
                                    header.AddRange(Parsing.SerializeType(result.path, false));
                                    header.AddRange(BitConverter.GetBytes(Parsing.GetGameResourceTypeID(result.type)));


                                    currentOffset += (ulong)result.data.Length;
                                }
                            }
                        }





                        ulong headerSize =
                             sizeof(uint) +          // actualFiles uint at start
                             (ulong)header.Count;    // header itself


                        Span<byte> headerSpan = CollectionsMarshal.AsSpan(header);


                        foreach (var p in offsetsToCorrect)
                        {
                            ulong oldOffset = BinaryPrimitives.ReadUInt64LittleEndian(
                                headerSpan.Slice((int)p, sizeof(ulong)));

                            ulong patched = oldOffset + headerSize;

                            BinaryPrimitives.WriteUInt64LittleEndian(
                                headerSpan.Slice((int)p, sizeof(ulong)),
                                patched);
                        }





                        using (var outStream = new FileStream(Path.Combine(ReleaseRootAssetArchivePath, archiveName), FileMode.Create, FileAccess.Write))
                        {
                            outStream.Write(BitConverter.GetBytes(actualFiles));

                            outStream.Write(header.ToArray());


                            using var tempStream = new FileStream(tempPath, FileMode.Open, FileAccess.Read);


                            byte[] buffer = ArrayPool<byte>.Shared.Rent(128 * 1024);
                            try
                            {
                                int read;
                                while ((read = tempStream.Read(buffer, 0, buffer.Length)) > 0)
                                {
                                    outStream.Write(buffer, 0, read);
                                }
                            }
                            finally
                            {
                                ArrayPool<byte>.Shared.Return(buffer);
                            }
                        }

                        File.Delete(tempPath);






                        Print($"Asset archive '{archiveName}' complete");

                    }

                    else
                        Print($"Asset archive '{archiveName}' not found");

                }));

            }

            await Task.WhenAll(archiveCompletionTasks);


        }




        var shaderdedup = new DedupFields();

        var shsrc = Emit(ShaderSources, 1, shaderdedup);
        var Cshsrc = Emit(ComputeShaderSources, 1, shaderdedup);




        WriteGeneratedFile("../../../../obj/Generated/EngineBuildProcessGenerated.g.cs", $@"


namespace Engine.Core;

using System.Collections.Immutable;


//SHADERS

public static partial class RenderingBackend
{{

{shaderdedup.ToString()}

    private static Dictionary<RenderingBackendEnum, Dictionary<string, ShaderSource>> ShaderSources = { shsrc };

    private static Dictionary<RenderingBackendEnum, Dictionary<string, ComputeShaderSource>> ComputeShaderSources = { Cshsrc };

}}





public static partial class Loading
{{
    private static readonly List<string> AssetArchiveNames = {Emit(AssetArchiveNames.ToArray())};

}}




");


        Print("Generated source");



    }



    private static ulong FieldsEver;





    private class DedupFields
    {

        class UniversalEqualityComparer : IEqualityComparer<object>
        {
            bool IEqualityComparer<object>.Equals(object x, object y)
            {
                if (ReferenceEquals(x, y)) return true;
                if (x is null || y is null) return false;
                if (x.GetType() != y.GetType()) return false;
                return x.Equals(y);
            }

            int IEqualityComparer<object>.GetHashCode(object obj)
            {
                return obj?.GetHashCode() ?? 0;
            }
        }


        public Dictionary<object, (string name, string fulldeclaration)> Dict = new Dictionary<object, (string name, string fulldeclaration)>(new UniversalEqualityComparer());


        public override string ToString()
        {

            StringBuilder sb = new();

            foreach (var v in Dict.Values)
                sb.AppendLine(v.fulldeclaration + "\n");

            return sb.ToString();
        }

    }





    private static string Emit(object obj, int indentation = 1, DedupFields dedup = null)
    {

        if (obj.GetType().IsClass) 
            indentation = 0;




        const int MAX_INLINE_CHARS = 80;

        if (obj == null)
            return new string('\t', indentation) + "null";

        var T = obj.GetType();
        string indent = new string('\t', indentation);
        string indentInner = new string('\t', indentation + 1);




        if (dedup != null && dedup.Dict.TryGetValue(obj, out var existing))
            return indent + existing.name;



        string RegisterObject(object o, string emitted)
        {
            if (dedup != null && o is not Type && o.GetType().IsClass)
            {
                if (o is string s && s == string.Empty) return "string.Empty";

                if (!dedup.Dict.TryGetValue(o, out var existing))
                {
                    var fieldName = $"_genfield{FieldsEver++}";
                    existing = dedup.Dict[o] = (fieldName, $"\tprivate {(o.GetType() == typeof(string) ? "const" : "static readonly")} {GetFriendlyTypeName(o.GetType())} {fieldName} = {emitted};");
                }
                
                return existing.name;
            }

            return emitted;
        }






        static string Normalize(string s) => s.TrimStart('\t');

        static bool CanInline(string s)
        {
            s = Normalize(s);
            return !s.Contains('\n') && s.Length <= MAX_INLINE_CHARS;
        }

        // Strings
        if (T == typeof(string))
            return RegisterObject(obj, indent + $"\"{obj}\"");

        // Enums
        if (T.IsEnum)
            return indent + $"{T.FullName!.Replace('+', '.')}.{obj}";

        // Types
        if (obj is Type t)
            return indent + $"typeof({GetFriendlyTypeName(t)})";



        // Numbers
        if (ImplementsGenericInterface(T, typeof(INumber<>)))
            return indent + obj.ToString()!;

        // Dictionaries
        if (ImplementsGenericInterface(T, typeof(IDictionary<,>)))
        {
            var entries = new List<string>();

            foreach (var kvp in (IEnumerable)obj)
            {
                var key = kvp.GetType().GetProperty("Key")!.GetValue(kvp);
                var value = kvp.GetType().GetProperty("Value")!.GetValue(kvp);

                var keyStr = Emit(key, indentation + 2, dedup);
                var valStr = Emit(value, indentation + 2, dedup);

                if (CanInline(keyStr) && CanInline(valStr))
                {
                    entries.Add($"{{ {Normalize(keyStr)}, {Normalize(valStr)} }}");
                }
                else
                {
                    string entryIndent = new string('\t', indentation + 2);

                    entries.Add(
                        "{\n" +
                        $"{entryIndent}{Normalize(keyStr)},\n" +
                        $"{entryIndent}{Normalize(valStr)}\n" +
                        $"{indentInner}}}"
                    );
                }
            }

            string inner = string.Join($",\n{indentInner}", entries);
            var initializer = $"{{\n{indentInner}{inner}\n{indent}}}";


            var keyType = GetFriendlyTypeName(T.GetGenericArguments()[0]);
            var valueType = GetFriendlyTypeName(T.GetGenericArguments()[1]);

            var str = $"new Dictionary<{keyType}, {valueType}>(){initializer}";

            return RegisterObject(obj, T.GetGenericTypeDefinition() == typeof(ImmutableDictionary<,>) ? $"ImmutableDictionary.ToImmutableDictionary({str})" : str);
        }





        // Lists / Arrays
        if (T.IsArray || ImplementsGenericInterface(T, typeof(IList<>)))
        {
            var elements = new List<string>();

            foreach (var item in (IEnumerable)obj)
                elements.Add(Normalize(Emit(item, indentation + 2, dedup)));

            var lines = new List<string>();
            var current = new StringBuilder();

            foreach (var elem in elements)
            {
                if (current.Length > 0 &&
                    current.Length + elem.Length + 2 > MAX_INLINE_CHARS)
                {
                    lines.Add(current.ToString());
                    current.Clear();
                }

                if (current.Length > 0)
                    current.Append(", ");

                current.Append(elem);
            }

            if (current.Length > 0)
                lines.Add(current.ToString());

            string inner = string.Join($",\n{indentInner}", lines);

            return RegisterObject(obj, $"{indent}[\n{indentInner}{inner}\n{indent}]");
        }


        // Records / single-constructor types
        var ctors = T.GetConstructors(BindingFlags.Public | BindingFlags.Instance);

        //use the only 1 argument constructor if there is one, otherwise try using the only 0 argument constructor, otherwise fail
        var primaryCtor = ctors.FirstOrDefault(c => c.GetParameters().Length > 0)
                  ?? ctors.FirstOrDefault(c => c.GetParameters().Length == 0);


        if (primaryCtor != null)
        {


            var args = primaryCtor.GetParameters().Select(p =>
            {
                var prop = T.GetProperty(
                    p.Name!,
                    BindingFlags.IgnoreCase | BindingFlags.Public | BindingFlags.Instance);

                if (prop != null)
                    return RegisterObject(prop.GetValue(obj), Emit(prop.GetValue(obj), indentation + 1, dedup));

                var field = T.GetField(
                    p.Name!,
                    BindingFlags.IgnoreCase | BindingFlags.Public | BindingFlags.Instance);

                if (field != null)
                    return RegisterObject(field.GetValue(obj), Emit(field.GetValue(obj), indentation + 1, dedup));

                return "<UNKNOWN>";
            }).ToList();


            var newdec = $"new {(obj.GetType().FullName!.StartsWith("System.ValueTuple`") ? string.Empty : GetFriendlyTypeName(obj.GetType()))}";


            var normalized = args.Select(Normalize).ToList();
            string inline = string.Join(", ", normalized);

            if (normalized.All(CanInline) && inline.Length <= MAX_INLINE_CHARS)
            {
                return $"{indent}{newdec}({inline})";
            }

            string multiline =
                string.Join($",\n{indentInner}", normalized);

            return RegisterObject(obj,
                $"{indent}{newdec}\n" +
                $"{indent}(\n" +
                $"{indentInner}{multiline}\n" +
                $"{indent})");
        }




        // Fallback
        return indent + "<UNKNOWN>";

        static bool ImplementsGenericInterface(Type type, Type genericInterface) =>
            type.GetInterfaces()
                .Any(x =>
                    x.IsGenericType
                        ? x.GetGenericTypeDefinition() == genericInterface
                        : x == genericInterface);



    }







    private static string GetFriendlyTypeName(Type t)
    {
        // Handle ValueTuple
        if (t.IsGenericType && t.FullName!.StartsWith("System.ValueTuple`"))
        {
            var args = t.GetGenericArguments().Select(GetFriendlyTypeName);
            return $"({string.Join(", ", args)})";
        }

        // Handle generic types
        if (t.IsGenericType)
        {
            var genericArgs = t.GetGenericArguments().Select(GetFriendlyTypeName);
            var name = t.Name.Substring(0, t.Name.IndexOf('`')); // strip `N
            return $"{name}<{string.Join(", ", genericArgs)}>";
        }

        // Handle arrays
        if (t.IsArray)
            return $"{GetFriendlyTypeName(t.GetElementType()!)}[]";

        // Normal type
        return t.FullName!.Replace("+", ".");
    }










    private static void WriteGeneratedFile(string outputpath, string text)
    {

        if (FileExistsCaseSensitive(outputpath))
        {
            var f = new FileInfo(outputpath);
            if (f.IsReadOnly)
                f.IsReadOnly = false;
        }


        File.WriteAllText(outputpath, "//<auto-generated>" + "\n\n" + text);


        var fileInfo = new FileInfo(outputpath);
        fileInfo.IsReadOnly = true;
    }



    private record struct AssetTaskData(string path, Type type, byte[] data, uint uncompressedLength);

}
