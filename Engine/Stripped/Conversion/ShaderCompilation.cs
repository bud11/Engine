using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using static Engine.Core.RenderingBackend;
using static Engine.Core.RenderingBackend.ResourceSetResourceDeclaration;

namespace Engine.Stripped;




/// <summary>
/// Manages shader construction, compilation and reflection in debug builds.
/// </summary>
public static partial class ShaderCompilation
{





    private static readonly SemaphoreSlim ShaderModifyLock = new SemaphoreSlim(1,1);
    private static readonly List<Task> ShaderCompilationTasks = new();


    private static object SuccessLock = new();
    private static bool Success = true;



    /// <summary>
    /// Reconstruct and recompile all shaders.
    /// </summary>
    public static async Task<bool> CompileShaders()
    {

        await ShaderModifyLock.WaitAsync();

        Success = true;

        GlobalResourceSetMetadata.Clear();


        // collect outgoing shader register calls 

        Entry.InitShaders();
        EngineDebug.InitShaders();



        // execute

        await Task.WhenAll(ShaderCompilationTasks);

        ShaderCompilationTasks.Clear();

        var ret = Success;


        ShaderModifyLock.Release();

        return ret;


    }




    /// <summary>
    /// Declares a resource set consistent in terms of its layout and contents across all of its usages. Naturally, the first shader to define a set of the given name acts as the one to lock it in. The underlying numerical index across shaders does not need to match.
    /// <br/> Once called, this becomes contractual, and violations will throw.
    /// <br/> <b>This must be called before any shaders are registered!</b>
    /// </summary>
    [Conditional("DEBUG")]
    public static void DeclareResourceSetConsistent(string name)
    {
        ThrowOutsideOfShaderRegister();

        if (ShaderCompilationTasks.Count != 0) 
            throw new Exception("Must be called before any shaders are registered.");

        GlobalResourceSetMetadata.Add(name, null);
    }


    private static void ShaderRegister(Task register)
    {
        ThrowOutsideOfShaderRegister();

        ShaderCompilationTasks.Add(register);
    }



    private static void ThrowOutsideOfShaderRegister()
    {
        if (ShaderModifyLock.CurrentCount != 0) 
            throw new Exception($"Must be called from inside of {nameof(Entry.InitShaders)} or {nameof(Entry.InitDebugShaders)}.");
    }








    private static List<ShaderError> ValidateResourceSetContracts(FrozenDictionary<string, (byte Binding, ShaderMetadata.ShaderResourceSetMetadata Metadata)> sets, ShaderRegisterCallInformation shaderInfo)
    {

        lock (GlobalResourceSetMetadata)
        {
            var err = new List<ShaderError>();


            foreach (var set in sets)
            {

                if (GlobalResourceSetMetadata.TryGetValue(set.Key, out var contract))
                {

                    // add
                    if (contract == null)
                    {
                        GlobalResourceSetMetadata[set.Key] = GlobalResourceSetMetadata[set.Key] = set.Value.Metadata;
                    }


                    // validate match
                    else
                    {
                        var actual = set.Value.Metadata;


                        // -------- TEXTURES --------

                        foreach (var tex in contract.Textures)
                        {
                            if (!actual.Textures.TryGetValue(tex.Key, out var texget))
                                err.Add(new ShaderError(-1, $"Missing texture '{tex.Key}' in set '{set.Key}'"));

                            if (texget.Binding != tex.Value.Binding
                                || texget.Metadata.SamplerType != tex.Value.Metadata.SamplerType
                                || texget.Metadata.ArrayLength != tex.Value.Metadata.ArrayLength)
                                err.Add(new ShaderError(-1, $"Mismatch for texture '{tex.Key}' in set '{set.Key}'"));
                        }

                        foreach (var tex in actual.Textures)
                        {
                            if (!contract.Textures.ContainsKey(tex.Key))
                                err.Add(new ShaderError(-1, $"Extra texture '{tex.Key}' in set '{set.Key}'"));
                        }




                        // -------- BUFFERS --------

                        foreach (var ubo in contract.Buffers)
                        {
                            if (!actual.Buffers.TryGetValue(ubo.Key, out var uboget))
                                err.Add(new ShaderError(-1, $"Missing data buffer '{ubo.Key}' in set '{set.Key}'"));

                            if (uboget.Binding != ubo.Value.Binding
                                || uboget.Metadata.SizeRequirement != ubo.Value.Metadata.SizeRequirement
                                || !uboget.Metadata.Members.SequenceEqual(ubo.Value.Metadata.Members))
                                err.Add(new ShaderError(-1, $"Mismatch for data buffer '{ubo.Key}' in set '{set.Key}'"));
                        }

                        foreach (var ubo in actual.Buffers)
                        {
                            if (!contract.Buffers.ContainsKey(ubo.Key))
                                err.Add(new ShaderError(-1, $"Extra data buffer '{ubo.Key}' in set '{set.Key}'"));
                        }


                    }
                }

            }

            return err;

        }

    }






    public readonly record struct ShaderRegisterCallInformation(string shaderName, string sourceFile, uint line);





    /// <summary>
    /// Compiles a full set of shader stages (or registers them to be precompiled during building in release builds) along with metadata to interact with.
    /// <br/> The entry point for each stage must be named "main".
    /// <br/> <paramref name="resourceSetNames"/> should be populated to give names to resource sets for later access. 
    /// <br/> This method can only be called from <see cref="Entry.InitShaders"/> or <see cref="Entry.InitDebugShaders"/>.
    /// </summary>
    /// <param name="vertexSource"></param>
    /// <param name="fragmentSource"></param>
    /// <param name="languageHandler"></param>
    [Conditional("DEBUG")]
    public static async void RegisterShader(string shaderName,

                                            string[] resourceSetNames,

                                            string vertexSource,
                                            string fragmentSource,

                                            ShaderLanguageHandler languageHandler,

                                            [CallerFilePath] string file = "",
                                            [CallerLineNumber] uint line = 0
        )
    {

        resourceSetNames ??= [];

        ShaderRegister(method());


        async Task method()
        {

            var info = new ShaderRegisterCallInformation(shaderName, file, line);

            var result = await languageHandler.CompileShader(vertexSource, fragmentSource, resourceSetNames, info);

            bool error = false;


            if (result.Vertex.Errors != null && result.Vertex.Errors.Count != 0)
            {
                ShowShaderDebugHtml(shaderName + " [Vertex Stage Error]", result.Vertex.SourceForUserDebugging, result.Vertex.Errors, languageHandler);
                error = true;
            }

            else if (result.Fragment.Errors != null && result.Fragment.Errors.Count != 0)
            {
                ShowShaderDebugHtml(shaderName + " [Fragment Stage Error]", result.Fragment.SourceForUserDebugging, result.Fragment.Errors, languageHandler);
                error = true;
            }

            else if (result.GeneralErrors != null && result.GeneralErrors.Count != 0)
            {
                ShowShaderDebugHtml(shaderName + " [Link or Usage Error]", string.Empty, result.GeneralErrors, languageHandler);
                error = true;
            }


            else if (ValidateResourceSetContracts(result.Metadata.ResourceSets, info).Count != 0)
            {
                ShowShaderDebugHtml(shaderName + " [Contract Error]", string.Empty, ValidateResourceSetContracts(result.Metadata.ResourceSets, info), languageHandler);
                error = true;
            }



            if (!error)
            {
                var src = new ShaderSource(result.Metadata, ImmutableArray.Create(result.Vertex.Spirv), ImmutableArray.Create(result.Fragment.Spirv));

#if ENGINE_BUILD_PASS

                lock (EngineBuildProcess.ShaderSources)
                    EngineBuildProcess.ShaderSources[CurrentBackend].Add(shaderName, src);                
#else
                BackendShaderReference.Create(shaderName, src);
#endif

            }

#if !ENGINE_BUILD_PASS
            else
            {
                lock (SuccessLock) 
                    Success = false;

                EngineDebug.GoToSourceFile(info.sourceFile, info.line);
            }
#endif

        }
    }



    /// <summary>
    /// Compiles a compute shader (or registers it to be precompiled during building in release builds) along with metadata to interact with.
    /// <br/> The entry point must be named "main".
    /// <br/> <paramref name="resourceSetNames"/> should be populated to give names to resource sets for later access. 
    /// <br/> This method can only be called from <see cref="Entry.InitShaders"/> or <see cref="Entry.InitDebugShaders"/>.
    /// </summary>
    /// <param name="shaderName"></param>
    /// <param name="source"></param>
    /// <param name="resourceSetNames"></param>
    /// <param name="languageHandler"></param>
    [Conditional("DEBUG")]
    public static async void RegisterComputeShader(string shaderName,

                                            string source,

                                            string[] resourceSetNames,

                                            ShaderLanguageHandler languageHandler,

                                            [CallerFilePath] string file = "",
                                            [CallerLineNumber] uint line = 0
        )
    {

        ShaderRegister(method());


        async Task method()
        {

            var info = new ShaderRegisterCallInformation(shaderName, file, line);

            var result = await languageHandler.CompileComputeShader(source, resourceSetNames, info);

            bool error = false;



            if (result.Main.Errors != null)
            {
                ShowShaderDebugHtml(shaderName + " [Compute Shader Error]", result.Main.SourceForUserDebugging, result.Main.Errors, languageHandler);
                error = true;
            }


            else if (result.GeneralErrors != null && result.GeneralErrors.Count != 0)
            {
                ShowShaderDebugHtml(shaderName + " [General Error]", string.Empty, result.GeneralErrors, languageHandler);
                error = true;
            }


            else if (ValidateResourceSetContracts(result.Metadata.ResourceSets, info).Count != 0)
            {
                ShowShaderDebugHtml(shaderName + " [Contract Error]", string.Empty, ValidateResourceSetContracts(result.Metadata.ResourceSets, info), languageHandler);
                error = true;
            }




            if (!error)
            {
                var src = new ComputeShaderSource(result.Metadata, ImmutableArray.Create(result.Main.Spirv));

#if ENGINE_BUILD_PASS
                lock (EngineBuildProcess.ComputeShaderSources)
                    EngineBuildProcess.ComputeShaderSources[CurrentBackend].Add(shaderName, src);                
#else
                BackendComputeShaderReference.Create(shaderName, src);
#endif

            }

#if !ENGINE_BUILD_PASS
            else
            {
                lock (SuccessLock)
                    Success = false;

                EngineDebug.GoToSourceFile(info.sourceFile, info.line);
            }
#endif

        }
    }











    private record struct SpirvReflectResults(
            
            List<ShaderError> Errors,

            FrozenDictionary<string, (byte Location, ShaderMetadata.ShaderInOutAttributeMetadata Metadata)> VertexInputAttributes,
            FrozenDictionary<string, (byte Location, ShaderMetadata.ShaderInOutAttributeMetadata Metadata)> FragmentOutputAttributes,
            FrozenDictionary<string, (byte Binding, ShaderMetadata.ShaderResourceSetMetadata Metadata)> ResourceSets

        );







    private static readonly string[][] StageOrders =
    [
        ["comp"],

        ["vert", "tesc", "tese", "geom", "frag"],

        ["task", "mesh", "frag"],
    ];



    /// <summary>
    /// Produces shader metadata given compiled standard-pipeline spirv shader stages.
    /// <br/> This also validates usage, contracts and linkage.
    /// </summary>
    /// <returns></returns>
    private static async Task<SpirvReflectResults> SpirvReflectOnMultipleStages(List<byte[]> stages, string[] SetNames)
    {



        var err = new List<ShaderError>();




        // ------------ REFLECT ON ALL STAGES ------------

        List<Task<JsonDocument>> stageTasks = new();

        foreach (var s in stages)
            stageTasks.Add(GetSpirvReflectJSON(s));

        await Task.WhenAll(stageTasks);






        // ------------ DERIVE TARGET PIPELINE ------------

        var spirvStages = stageTasks
            .Select(x => x.Result)
            .ToDictionary(
                x => x.RootElement
                      .GetProperty("entryPoints")
                      .EnumerateArray()
                      .First()
                      .GetProperty("mode")
                      .GetString()!,
        x => x);


        var presentStages = new HashSet<string>(spirvStages.Keys);



        string[] matchedPipeline = null;

        foreach (var pipeline in StageOrders)
        {
            bool allContained = presentStages.All(s => pipeline.Contains(s));
            if (!allContained)
                continue;

            if (!IsValidSubsequence(pipeline, presentStages))
                continue;

            matchedPipeline = pipeline;
            break;
        }




        static bool IsValidSubsequence(string[] fullPipeline, HashSet<string> present)
        {
            int lastSeen = -1;

            for (int i = 0; i < fullPipeline.Length; i++)
            {
                if (!present.Contains(fullPipeline[i]))
                    continue;

                if (i < lastSeen)
                    return false;

                lastSeen = i;
            }

            // --- Special rules ---

            bool hasTesc = present.Contains("tesc");
            bool hasTese = present.Contains("tese");
            if (hasTesc ^ hasTese)
                return false;

            if (present.Contains("mesh") && !present.Contains("task"))
                return false;

            if (present.Contains("comp") && present.Count > 1)
                return false;

            return true;
        }


        if (matchedPipeline == null)
            throw new Exception($"Invalid shader stage combination: [{string.Join(", ", presentStages)}]");






        Dictionary<string, (byte, ShaderMetadata.ShaderInOutAttributeMetadata)> InputAttributes = new();
        Dictionary<string, (byte, ShaderMetadata.ShaderInOutAttributeMetadata)> OutputAttributes = new();

        Dictionary<byte, Dictionary<string, (byte, ShaderMetadata.ShaderTextureMetadata)>> Textures = new();

        Dictionary<byte, Dictionary<string, (byte, ShaderMetadata.ShaderDataBufferMetadata)>> DataBuffers = new();






        // ------------ VALIDATE LINKING ------------


        Dictionary<byte, ShaderMetadata.ShaderInOutAttributeMetadata> PriorStageOutputAttributes = new();
        Dictionary<byte, ShaderMetadata.ShaderInOutAttributeMetadata> CurrentStageInputAttributes = new();

        string priorStageName = null;



        int matched = 0;

        for (int i = 0; i < matchedPipeline.Length; i++)
        {
            string pipelineStage = matchedPipeline[i];


            if (spirvStages.TryGetValue(pipelineStage, out var doc))
            {

                doc.RootElement.TryGetProperty("types", out var typesElement);


                var mode = doc.RootElement.GetProperty("entryPoints").EnumerateArray().First().GetProperty("mode").ToString();




                // ------------ ATTRIBUTES ------------

                if (matched == 0)
                {
                    priorStageName = pipelineStage;
                    ProcessAttributesByName("inputs", doc, InputAttributes);
                }


                if (matched == matchedPipeline.Length - 1)
                    ProcessAttributesByName("outputs", doc, OutputAttributes);





                // ------------ LINK VALIDATION ------------


                CurrentStageInputAttributes.Clear();
                ProcessAttributesByIndex("inputs", doc, CurrentStageInputAttributes);




                if (matched > 0)
                {
                    foreach (var kv in CurrentStageInputAttributes)
                    {
                        if (!PriorStageOutputAttributes.TryGetValue(kv.Key, out var prev))
                        {
                            err.Add(new(-1,
                                $"Stage '{pipelineStage}' expects input at location {kv.Key} ({kv.Value.Format}) " +
                                $"but previous stage '{priorStageName}' does not provide it"));
                            continue;
                        }

                        if (prev.Format != kv.Value.Format)
                        {
                            err.Add(new(-1,
                                $"Location {kv.Key}: type mismatch between stages. " +
                                $"Previous: {prev.Format}, Current: {kv.Value.Format}"));
                        }

                        if (prev.ArrayLength != kv.Value.ArrayLength)
                        {
                            err.Add(new(-1,
                                $"Location {kv.Key}: array length mismatch. " +
                                $"Previous: {prev.ArrayLength}, Current: {kv.Value.ArrayLength}"));
                        }
                    }

                    foreach (var kv in PriorStageOutputAttributes)
                    {
                        if (!CurrentStageInputAttributes.ContainsKey(kv.Key))
                        {
                            err.Add(new(-1,
                                $"Stage '{priorStageName}' outputs location {kv.Key} ({kv.Value.Format}) " +
                                $"but it is not consumed by stage '{pipelineStage}'"));
                        }
                    }
                }




                // ------------ PREP NEXT STAGE ------------

                PriorStageOutputAttributes.Clear();
                ProcessAttributesByIndex("outputs", doc, PriorStageOutputAttributes);


                priorStageName = pipelineStage;











                static void ProcessAttributesByName(string group, JsonDocument doc, Dictionary<string, (byte, ShaderMetadata.ShaderInOutAttributeMetadata)> addTo)
                {
                    if (doc.RootElement.TryGetProperty(group, out var get))
                    {
                        foreach (var attr in get.EnumerateArray())
                        {
                            if (Enum.TryParse<ShaderAttributeBufferFinalFormat>(attr.GetProperty("type").GetString(), true, out var parse))
                                addTo[attr.GetProperty("name").GetString()] = (attr.GetProperty("location").GetByte(), new ShaderMetadata.ShaderInOutAttributeMetadata(parse, 1));

                            else
                                throw new NotImplementedException();
                        }
                    }
                }



                static void ProcessAttributesByIndex(string group, JsonDocument doc, Dictionary<byte, ShaderMetadata.ShaderInOutAttributeMetadata> addTo)
                {
                    if (doc.RootElement.TryGetProperty(group, out var get))
                    {
                        foreach (var attr in get.EnumerateArray())
                        {
                            if (Enum.TryParse<ShaderAttributeBufferFinalFormat>(attr.GetProperty("type").GetString(), true, out var parse))
                                addTo[attr.GetProperty("location").GetByte()] = new ShaderMetadata.ShaderInOutAttributeMetadata(parse, 1);

                            else
                                throw new NotImplementedException();
                        }
                    }
                }




                // ------------ TEXTURES  ------------

                if (doc.RootElement.TryGetProperty("textures", out var texGet))
                {
                    foreach (var tex in texGet.EnumerateArray())
                    {
                        if (Enum.TryParse<TextureSamplerTypes>(tex.GetProperty("type").GetString(), true, out var parse))
                        {
                            var set = tex.GetProperty("set").GetByte();
                            if (!Textures.TryGetValue(set, out var setGet)) Textures[set] = setGet = new();

                            setGet[tex.GetProperty("name").GetString()] = (tex.GetProperty("binding").GetByte(), new ShaderMetadata.ShaderTextureMetadata(parse, tex.TryGetProperty("array", out var arr) ? arr.EnumerateArray().First().GetUInt32() : 1));

                        }

                        else
                            throw new NotImplementedException();
                    }
                }


                // ------------ UNIFORM / STORAGE BUFFERS ------------

                ProcessBuffers("ubos", doc, typesElement, DataBuffers);
                ProcessBuffers("ssbos", doc, typesElement, DataBuffers);



                static void ProcessBuffers(string group, JsonDocument doc, JsonElement types, Dictionary<byte, Dictionary<string, (byte, ShaderMetadata.ShaderDataBufferMetadata)>> addTo)
                {

                    if (types.ValueKind != JsonValueKind.Undefined && doc.RootElement.TryGetProperty(group, out var buffers))
                    {
                        foreach (var buf in buffers.EnumerateArray())
                        {

                            var bufName = buf.GetProperty("name").GetString();

                            var bufTypeInfo = types.GetProperty(buf.GetProperty("type").ToString());

                            var bufBlockSize = buf.GetProperty("block_size").GetUInt32();




                            Dictionary<string, ShaderMetadata.ShaderDataBufferMetadata.MemberInfo> memberMetadata = new();


                            var members = bufTypeInfo.GetProperty("members").EnumerateArray().ToArray();

                            for (int i = 0; i < members.Length; i++)
                            {
                                var member = members[i];

                                var parse = ParseMember(
                                    member, 
                                    types, 
                                    i == members.Length-1 ? bufBlockSize : members[i + 1].GetProperty("offset").GetUInt32()
                                    );


                                memberMetadata.Add(parse.name, parse.info);
                            }






                            static (string name, ShaderMetadata.ShaderDataBufferMetadata.MemberInfo info) ParseMember(JsonElement m, JsonElement types, uint nextPhysicalStart)
                            {

                                string member_name = m.GetProperty("name").GetString()!;
                                uint member_physicaloffset = m.GetProperty("offset").GetUInt32()!;


                                var member_typeLogicalSize = GetTypeLogicalSize(m, types);


                                bool member_isArray = m.TryGetProperty("array", out var arrayGet);
                                uint member_arrayStride = member_isArray ? m.GetProperty("array_stride").GetUInt32() : 0;

                                uint member_physicalSize = member_isArray ? member_arrayStride : nextPhysicalStart-member_physicaloffset;



                                ShaderMetadata.ShaderDataBufferMetadata.MemberInfo info;



                                // PRIMITIVE

                                if (member_typeLogicalSize.primitive)
                                {
                                    info = new ShaderMetadata.ShaderDataBufferMetadata.PrimitiveInfo(member_physicaloffset, member_physicalSize, member_typeLogicalSize.size);
                                }


                                // STRUCT

                                else
                                {
                                    var memberMetadata = new Dictionary<string, ShaderMetadata.ShaderDataBufferMetadata.MemberInfo>();

                                    var structMembers = types
                                        .GetProperty(m.GetProperty("type").GetString())
                                        .GetProperty("members")
                                        .EnumerateArray()
                                        .ToArray();



                                    for (int i = 0; i < structMembers.Length; i++)
                                    {
                                        var member = structMembers[i];

                                        var parse = ParseMember(
                                            member, 
                                            types, i == structMembers.Length - 1
                                                ? member_physicalSize
                                                : structMembers[i + 1].GetProperty("offset").GetUInt32());

                                        memberMetadata.Add(parse.name, parse.info);
                                    }


                                    info =
                                        new ShaderMetadata.ShaderDataBufferMetadata.StructInfo(member_physicaloffset, member_physicalSize, member_typeLogicalSize.size, 
                                            FrozenDictionary.ToFrozenDictionary(memberMetadata),
                                            ImmutableArray.ToImmutableArray(memberMetadata.Select(x => (x.Key, x.Value)).OrderBy(x => x.Value.RelativeOffset))
                                            );
                                }



                                if (member_isArray)
                                {
                                    var length = arrayGet.EnumerateArray().First().GetUInt32();
                                    return (member_name, new ShaderMetadata.ShaderDataBufferMetadata.ArrayInfo(BaseMemberInfo: info, Length: length));
                                }


                                return (member_name, info);



                                static (bool primitive, uint size) GetTypeLogicalSize(JsonElement t, JsonElement types)
                                {
                                    var typeName = t.GetProperty("type").GetString();

                                    uint? typeLogicalSize = typeName switch
                                    {
                                        "float" => 4,
                                        "int" => 4,
                                        "uint" => 4,
                                        "bool" => 4,

                                        "vec2" => 8,
                                        "vec3" => 12,
                                        "vec4" => 16,

                                        "ivec2" => 8,
                                        "ivec3" => 12,
                                        "ivec4" => 16,

                                        "uvec2" => 8,
                                        "uvec3" => 12,
                                        "uvec4" => 16,

                                        "mat2" => 16,
                                        "mat3" => 36,
                                        "mat4" => 64,

                                        _ => null,
                                    };


                                    // primitive
                                    if (typeLogicalSize.HasValue)
                                        return (true, typeLogicalSize.Value);



                                    // struct
                                    uint logicalSize = 0;
                                    foreach (var m in types.GetProperty(typeName).GetProperty("members").EnumerateArray())
                                        logicalSize += GetTypeLogicalSize(m, types).size;


                                    return (false, logicalSize);
                                }

                            }



                            var set = buf.GetProperty("set").GetByte();
                            if (!addTo.TryGetValue(set, out var setGet)) addTo[set] = setGet = new();


                            setGet[bufName] = new(
                                buf.GetProperty("binding").GetByte(), new ShaderMetadata.ShaderDataBufferMetadata
                                (
                                    UsageFlags : group == "ubos" ? BufferUsageFlags.Uniform : BufferUsageFlags.Storage,
                                    ReadWriteFlags : (buf.TryGetProperty("readonly", out var readonlyGet) && readonlyGet.GetBoolean()) ? ReadWriteFlags.GPURead : ReadWriteFlags.GPURead | ReadWriteFlags.GPUWrite,
                                    Members: FrozenDictionary.ToFrozenDictionary(memberMetadata),
                                    MembersIndexed: ImmutableArray.ToImmutableArray(memberMetadata.Select(x=>(x.Key, x.Value)).OrderBy(x=>x.Value.RelativeOffset)),
                                    SizeRequirement: buf.GetProperty("block_size").GetUInt32()
                                ));


                        }
                    }
                }


                matched++;
            }
        }









        // ------------ RESOURCE SET GROUPING / VALIDATION ------------


        var allSetIndices = new HashSet<byte>();
        allSetIndices.UnionWith(Textures.Keys);
        allSetIndices.UnionWith(DataBuffers.Keys);


        // Ensure contiguous
        if (allSetIndices.Count > 0)
        {
            byte maxIndex = allSetIndices.Max();
            for (byte i = 0; i <= maxIndex; i++)
            {
                if (!allSetIndices.Contains(i))
                    err.Add(new ShaderError(-1, $"Missing resource set at index {i}, sets must be contiguous starting from 0"));
            }
        }


        var sets = new Dictionary<string, (byte Binding, ShaderMetadata.ShaderResourceSetMetadata Metadata)>();



        foreach (byte setIndex in allSetIndices.OrderBy(x => x))
        {
            if (setIndex >= SetNames.Length)
            {
                err.Add(new ShaderError(-1, $"No set name provided for set index {setIndex}"));
                continue;
            }


            string setName = SetNames[setIndex];



            var textures = Textures.TryGetValue(setIndex, out var t) ? t : new Dictionary<string, (byte, ShaderMetadata.ShaderTextureMetadata)>();
            var buffers = DataBuffers.TryGetValue(setIndex, out var u) ? u : new Dictionary<string, (byte, ShaderMetadata.ShaderDataBufferMetadata)>();


            int resCount = textures.Count + buffers.Count;
            if (resCount == 0)
            {
                err.Add(new ShaderError(-1, $"Resource set '{setName}' is empty"));
                continue;
            }



            var dec = new ResourceSetResourceDeclaration[resCount];



            foreach (var v in textures)
                dec[v.Value.Item1] = new ResourceSetResourceDeclaration(ResourceSetResourceType.Texture, v.Value.Item2.ArrayLength);

            foreach (var v in buffers)
                dec[v.Value.Item1] = new ResourceSetResourceDeclaration(ResourceSetResourceType.ConstantDataBuffer, 1);


            sets[setName] = (setIndex, new ShaderMetadata.ShaderResourceSetMetadata(
                ImmutableArray.Create(dec),
                FrozenDictionary.ToFrozenDictionary(textures),
                FrozenDictionary.ToFrozenDictionary(buffers),
                textures.Select(x => KeyValuePair.Create(x.Value.Item1, (x.Key, x.Value.Item2))).ToFrozenDictionary(),
                buffers.Select(x => KeyValuePair.Create(x.Value.Item1, (x.Key, x.Value.Item2))).ToFrozenDictionary()
            ));
        }





        // ------------ RETURN ------------

        return new(err, FrozenDictionary.ToFrozenDictionary(InputAttributes), FrozenDictionary.ToFrozenDictionary(OutputAttributes), FrozenDictionary.ToFrozenDictionary(sets));


    }







    private static async Task<JsonDocument> GetSpirvReflectJSON(byte[] stage)
    {
        var temp = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.spv");
        var reflection = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.json");


        await File.WriteAllBytesAsync(temp, stage); 
        await RunProcess.Run("spirv-cross", $"\"{temp}\" --reflect --output \"{reflection}\"");

        var doc = JsonDocument.Parse(await File.ReadAllTextAsync(reflection));

        File.Delete(temp);
        File.Delete(reflection);

        return doc;
    }





    /// <summary>
    /// Optimizes spirv non-destructively (will not remove or rearrange inputs, outputs or resources, but will optimize logic and remove names/metadata)
    /// </summary>
    /// <returns></returns>
    private static async Task<byte[]> OptimizeSpirv(byte[] spirv)
    {

        var spvPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}_spv.spv");

        await File.WriteAllBytesAsync(spvPath, spirv);



        var outputPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}_spv_opt.spv");

        using var proc = Process.Start(new ProcessStartInfo
        {
            FileName = "spirv-opt",
            Arguments =

            $"\"{spvPath}\" " +
            "--inline-entry-points-exhaustive " +
            "--convert-local-access-chains " +
            "--eliminate-insert-extract " +
            "--eliminate-dead-branches " +
            "--merge-blocks " +
            "--eliminate-local-multi-store " +
            "--eliminate-dead-functions " +
            $"-o \"{outputPath}\"",


            RedirectStandardOutput = false,
            RedirectStandardError = false,
            UseShellExecute = false,
            CreateNoWindow = true
        });

        await proc.WaitForExitAsync();


        var ret = await File.ReadAllBytesAsync(outputPath);

        File.Delete(spvPath);
        File.Delete(outputPath);


        return ret;

    }





    /// <summary>
    /// <inheritdoc cref="GLSLHandler"/>
    /// </summary>
    public static readonly ShaderLanguageHandler GLSL = new GLSLHandler();


    /// <summary>
    /// <inheritdoc cref="HLSLHandler"/>
    /// </summary>
    public static readonly ShaderLanguageHandler HLSL = new HLSLHandler();








    public readonly record struct ShaderError(int Line, string Message);



    /// <summary>
    /// Generates and opens a temporary HTML file showing shader errors.
    /// </summary>
    /// <param name="source"></param>
    /// <param name="errors"></param>
    private static void ShowShaderDebugHtml(string shaderName, string source, List<ShaderError> errors, ShaderLanguageHandler interpreter)
    {


        var rules = interpreter.GetHighlighting();




        string htmlPath = Path.Combine(Path.GetTempPath(), "shader_debug_" + Guid.NewGuid() + ".html");

        string escape(string s) => s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");

        string[] lines = source.Split('\n');

        string renderCodeBlock(string[] lines)
        {
            var sb = new StringBuilder();
            sb.AppendLine("<div class='code-block'>");

            for (int i = 0; i < lines.Length; i++)
            {
                int line = i;
                var err = errors.FirstOrDefault(e => e.Line == line);
                string rawLine = lines[line];
                string escapedLine = string.IsNullOrWhiteSpace(rawLine) ? "&nbsp;" : escape(rawLine);



                if (rules != null)
                {
                    if (rules.TypePattern != null)
                        escapedLine = Regex.Replace(escapedLine, rules.TypePattern, "<span class='type'>$1</span>");

                    if (rules.KeywordPattern != null)
                        escapedLine = Regex.Replace(escapedLine, rules.KeywordPattern, "<span class='keyword'>$1</span>");

                    if (rules.BuiltinPattern != null)
                        escapedLine = Regex.Replace(escapedLine, rules.BuiltinPattern, "<span class='builtin'>$1</span>");

                    if (rules.NumberPattern != null)
                        escapedLine = Regex.Replace(escapedLine, rules.NumberPattern, "<span class='number'>$1</span>");

                    if (rules.CommentPattern != null)
                        escapedLine = Regex.Replace(escapedLine, rules.CommentPattern, "<span class='comment'>$1</span>");

                }


                string errorAttr = err.Message != null
                    ? $" class='error' title='{escape(err.Message)}'"
                    : "";


                sb.AppendLine($"<div{errorAttr}><code>{escapedLine}</code></div>");
            }

            sb.AppendLine("</div>");
            return sb.ToString();
        }

        string html = $@"
            <!DOCTYPE html>
            <html lang='en'>
            <head>
            <meta charset='UTF-8'>
            <title>{shaderName}</title>
            <style>
            body {{ background: #1e1e1e; color: #d4d4d4; font-family: Consolas, monospace; margin: 0; padding: 0; }}

            .title {{
                font-size: 28px;
                font-weight: bold;
                padding: 16px 20px;
                border-bottom: 2px solid #333;
                background: linear-gradient(90deg, #222, #1a1a1a);
                letter-spacing: 0.5px;
            }}

            .console {{
                background: #111;
                padding: 12px 16px;
                border-bottom: 3px solid #444;
                overflow-x: auto;
            }}

            /* error rows */
            .error-line {{
                display: flex;
                gap: 10px;
                padding: 6px 0;
                border-left: 3px solid #f55;
                padding-left: 10px;
            }}

            .error-line + .error-line {{
                margin-top: 6px;
            }}

            .error-line::before {{
                content: ""•"";
                color: #f55;
                margin-right: 6px;
            }}

            .error-line .line {{
                color: #888;
                min-width: 70px;
            }}

            .error-line .msg {{
                color: #f55;
                flex: 1;
            }}

            .container {{ padding: 1em; overflow-y: auto; height: calc(100vh - 110px); }}

            .code-block div {{ white-space: pre; position: relative; display: block; width: 100%; }}
            .code-block div code {{ display: inline-block; min-width: 100%; }}

            .error {{ background: rgba(255, 0, 0, 0.2); border-left: 3px solid red; padding-left: 4px; }}

            .error:hover::after {{
                content: attr(title);
                position: absolute;
                top: 100%;
                left: 0;
                margin-top: 4px;
                background: #300;
                color: #faa;
                padding: 4px 8px;
                border: 1px solid red;
                z-index: 9999;
                max-width: 400px;
                white-space: normal;
                box-shadow: 0 2px 6px rgba(0,0,0,0.5);
            }}

            .comment {{ color: #6a9955; font-style: italic; }}
            .keyword {{ color: #569CD6; font-weight: bold; }}
            .type {{ color: #4EC9B0; }}
            .builtin {{ color: #DCDCAA; }}
            .number {{ color: #B5CEA8; }}
            </style>
            </head>
            <body>

            <div class='title'>
            {shaderName}
            </div>

            <div class='console'>
            {string.Join("", errors.Select(e =>
                $"<div class='error-line'>" +
                (e.Line >= 0 ? $"<span class='line'>line {e.Line + 1}</span>" : "") +
                $"<span class='msg'>{escape(e.Message)}</span>" +
                "</div>"
            ))}
            </div>

            <div class='container'>
            {renderCodeBlock(lines)}
            </div>

            </body>
            </html>
            ";

        File.WriteAllText(htmlPath, html);
        Process.Start(new ProcessStartInfo
        {
            FileName = htmlPath,
            UseShellExecute = true
        });

        // Optional: delete on exit
        AppDomain.CurrentDomain.ProcessExit += (s, e) =>
        {
            try { File.Delete(htmlPath); } catch { }
        };
    }





    private static string SanitizeTypeName(Type t) 
        => t.FullName.Replace('.', '_').Replace("+", "_");





    /// <summary>
    /// A basis for implementing support for a specific shader language.
    /// </summary>
    public abstract class ShaderLanguageHandler
    {

        public sealed class SyntaxHighlightingRules
        {
            public string CommentPattern { get; init; }
            public string TypePattern { get; init; }
            public string KeywordPattern { get; init; }
            public string BuiltinPattern { get; init; }
            public string NumberPattern { get; init; }
        }


        /// <summary>
        /// An optional method that can be used to supply regex patterns to help make shader code more readable for user shader code debugging.
        /// </summary>
        /// <returns></returns>
        public virtual SyntaxHighlightingRules GetHighlighting() => null;




        public readonly record struct SpirvDrawCompilationResult(SpirvStageCompilationResult Vertex, SpirvStageCompilationResult Fragment, ShaderMetadata Metadata, List<ShaderError> GeneralErrors);

        public readonly record struct SpirvComputeCompilationResult(SpirvStageCompilationResult Main, ComputeShaderMetadata Metadata, List<ShaderError> GeneralErrors);


        public readonly record struct SpirvStageCompilationResult(byte[] Spirv, List<ShaderError> Errors, string? SourceForUserDebugging);




        /// <summary>
        /// Compiles and reflects upon a full set of drawing pipeline shader stages.
        /// <br/> If shader compilation fails, one or more of the respective error lists will be populated.
        /// </summary>
        /// <param name="vertexSource"></param>
        /// <param name="fragmentSource"></param>
        /// <param name="setNames"></param>
        /// <returns></returns>
        public abstract Task<SpirvDrawCompilationResult> CompileShader(string vertexSource, string fragmentSource, string[] setNames, ShaderRegisterCallInformation shaderInfo);



        /// <summary>
        /// Compiles and reflects upon a compute shader.
        /// <br/> If shader compilation fails, one or more of the respective error lists will be populated.
        /// </summary>
        /// <param name="source"></param>
        /// <param name="setNames"></param>
        /// <returns></returns>
        public abstract Task<SpirvComputeCompilationResult> CompileComputeShader(string source, string[] setNames, ShaderRegisterCallInformation shaderInfo);





        /// <summary>
        /// Converts the unmanaged struct type <typeparamref name="T"/> into a struct type definition within the target language. Uses <see cref="GetShaderStructName{T}"/> to name the type.
        /// <br/> An exception will be thrown if the type cannot be represented, or already has a native representation within the target language which would make a definition redundant or impossible.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        public abstract string GetShaderStructDefinition<T>() where T : unmanaged;

        /// <summary>
        /// Gets a shader-safe name for the unmanaged struct type <typeparamref name="T"/>.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        public abstract string GetShaderStructName<T>() where T : unmanaged;

    }








    /// <summary>
    /// Supports modern vulkan-style GLSL. Requires GlslangValidator.
    /// <br/> • "#version 450" is added to shaders. A version should NOT be specified.
    /// <br/> • In and out attributes should NOT be given explicit locations.
    /// <br/> • Resources should be given explicit sets, but should NOT be given set BINDINGS.
    /// </summary>
    private partial class GLSLHandler : ShaderLanguageHandler
    {

        public override SyntaxHighlightingRules GetHighlighting() => new()
        {
            CommentPattern = @"(//.*$)",

            TypePattern =
                @"\b(bool|int|uint|float|vec[234]|ivec[234]|mat[234]|sampler[123]D|samplerCube|sampler2DShadow)\b",

            KeywordPattern =
                @"\b(attribute|const|uniform|buffer|readonly|writeonly|varying|layout|in|out|inout|void|if|else|for|while|return|discard|struct|switch|case|default|break|continue)\b",

            BuiltinPattern =
                @"\b(texture|normalize|mix|dot|clamp|length|sin|cos|abs|pow|max|min|floor|ceil|fract|mod|step|smoothstep|reflect|refract|cross|distance|exp|log)\b",

            NumberPattern =
                @"(?<![\w.])(\d+\.\d+|\d+)(f)?\b"
        };





        private static async Task<List<ShaderError>> GlslangValidateCheckForErrors(string src, string stage)
        {

            // ---------------------------------------- SAVE ----------------------------------------

            var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}_shader.{stage}");
            await File.WriteAllTextAsync(path, src);



            // ---------------------------------------- VALIDATE/OUTPUT ----------------------------------------


            var errors = new List<ShaderError>();

            var psi = new ProcessStartInfo
            {
                FileName = "glslangValidator",
                Arguments = $"-V \"{path}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };


            using var proc = Process.Start(psi);
            string output = await proc.StandardOutput.ReadToEndAsync();
            await proc.WaitForExitAsync();


            // ---------------------------------------- GATHER ERRORS ----------------------------------------

            var regex = new Regex(@"^(ERROR|WARNING):\s*(.*):(\d+):\s*(.+)$", RegexOptions.Multiline);

            foreach (Match m in regex.Matches(output))
            {
                int line = int.Parse(m.Groups[3].Value);
                string msg = m.Groups[4].Value;

                errors.Add(new ShaderError(line - 1, msg));
            }


            File.Delete(path);

            return errors;
        }




        private static async Task<byte[]> GlslangValidateCompileToSpirv(string src, string stage)
        {

            // ---------------------------------------- SAVE ----------------------------------------

            var glslPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}_shader.{stage}");
            await File.WriteAllTextAsync(glslPath, src);


            // ---------------------------------------- GLSL -> SPIRV ----------------------------------------

            var spvPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}_spv.spv");


            using var proc = Process.Start(new ProcessStartInfo
            {
                FileName = "glslangValidator",
                Arguments = $"-V \"{glslPath}\" -o {spvPath}",
                RedirectStandardOutput = false,
                RedirectStandardError = false,
                UseShellExecute = false,
                CreateNoWindow = true
            });
            await proc.WaitForExitAsync();


            // ---------------------------------------- RETURN ----------------------------------------

            var spv = await File.ReadAllBytesAsync(spvPath);

            File.Delete(glslPath);
            File.Delete(spvPath);

            return spv;
        }






        private static async Task<SpirvStageCompilationResult> CompileShaderStage(string src, string stage)
        {

            // ---------------------------------------- VALIDATION ----------------------------------------

            var errors = await GlslangValidateCheckForErrors(src, stage);

            if (errors.Count != 0)
                return new SpirvStageCompilationResult(null, errors, src);


            // ---------------------------------------- COMPILATION ----------------------------------------

            return new SpirvStageCompilationResult(await GlslangValidateCompileToSpirv(src, stage), null, src);

        }



        const string GLSLHeader = "#version 450\n\n\n";


        public override async Task<SpirvDrawCompilationResult> CompileShader(string vertexSource, string fragmentSource, string[] setNames, ShaderRegisterCallInformation shaderInfo)
        {
            
            vertexSource = GLSLHeader + vertexSource;
            fragmentSource = GLSLHeader + fragmentSource;


            var final = InjectLocationsAndBindings([vertexSource, fragmentSource], out var locErrors);


            if (locErrors[0].Count != 0)
                return new(new(null, locErrors[0], vertexSource), default, null, null);

            if (locErrors[1].Count != 0)
                return new(new(null, locErrors[1], fragmentSource), default, null, null);

            vertexSource = final[0];
            fragmentSource = final[1];





            var vertTask = CompileShaderStage(vertexSource, "vert");
            var fragTask = CompileShaderStage(fragmentSource, "frag");

            await vertTask;
            await fragTask;



            if (vertTask.Result.Spirv != null && fragTask.Result.Spirv != null)
            {
                var reflect = await SpirvReflectOnMultipleStages([vertTask.Result.Spirv, fragTask.Result.Spirv], setNames);

                if (reflect.Errors.Count != 0)
                    return new(vertTask.Result, fragTask.Result, null, reflect.Errors);

                return new SpirvDrawCompilationResult(vertTask.Result,
                                                      fragTask.Result,
                                                      new ShaderMetadata(shaderInfo.shaderName, reflect.VertexInputAttributes, reflect.FragmentOutputAttributes, reflect.ResourceSets),
                                                      null);
            }

            return new SpirvDrawCompilationResult(vertTask.Result, fragTask.Result, null, null);

        }




        public override async Task<SpirvComputeCompilationResult> CompileComputeShader(string source, string[] setNames, ShaderRegisterCallInformation shaderInfo)
        {

            source = GLSLHeader + source;


            var final = InjectLocationsAndBindings([source], out var locErrors);

            if (locErrors[0].Count != 0)
                return new(new(null, locErrors[0], source), default, null);

            source = final[0];



            var result = await CompileShaderStage(source, "comp");

            var reflect = await SpirvReflectOnMultipleStages([result.Spirv], setNames);

            if (reflect.Errors.Count != 0)
                return new SpirvComputeCompilationResult(result, null, reflect.Errors);

            return new SpirvComputeCompilationResult(result, result.Errors == null ? new ComputeShaderMetadata(reflect.ResourceSets) : null, null);
        }



        /// <summary>
        /// Injects explicit in/out attribute locations and explicit resource binding assignments, both based on order of appearance within their respective contexts.
        /// </summary>
        /// <param name="stages"></param>
        /// <param name="errors"></param>
        /// <returns></returns>
        private static string[] InjectLocationsAndBindings(string[] stages, out List<ShaderError>[] errors)
        {

            errors = new List<ShaderError>[stages.Length];


            var outputs = new string[stages.Length];

            // Global binding map  (set, name) -> binding
            
            var globalBindings = new Dictionary<(int set, string name), int>();
            var nextBindingPerSet = new Dictionary<int, int>();




            for (int s = 0; s < stages.Length; s++)
            {
                string src = stages[s];
                var sb = new StringBuilder();

                int i = 0;
                int len = src.Length;

                int inLoc = 0;
                int outLoc = 0;

                errors[s] = new();



                while (i < len)
                {


                    // ------------------------------ COPY WHITESPACE ------------------------------

                    if (char.IsWhiteSpace(src[i]))
                    {
                        sb.Append(src[i++]);
                        continue;
                    }



                    // ------------------------------ LAYOUT ------------------------------

                    if (IsWordAt(src, i, "layout"))
                    {
                        int layoutStart = i;

                        int parenStart = src.IndexOf('(', i);
                        int parenEnd = FindMatching(src, parenStart, '(', ')');

                        string layout = src.Substring(layoutStart, parenEnd - layoutStart + 1);

                        i = parenEnd + 1;
                        SkipWhitespace(src, ref i);

                        string keyword = ReadWord(src, ref i);


                        // layout + in/out
                        if (keyword == "in" || keyword == "out")
                        {
                            errors[s].Add(new ShaderError(GetLine(src, layoutStart),
                                "layout() not allowed on in/out"));

                            SkipWhitespace(src, ref i);
                            string type = ReadWord(src, ref i);

                            SkipWhitespace(src, ref i);
                            string name = ReadUntil(src, ref i, ';');

                            int loc = keyword == "out" ? outLoc++ : inLoc++;
                            sb.Append($"layout(location={loc}) {keyword} {type} {name};");

                            i++; // skip ;
                            continue;
                        }




                        // ------------------------------ RESOURCE ------------------------------

                        if (keyword == "uniform" || keyword == "buffer")
                        {
                            if (!layout.Contains("set"))
                            {
                                errors[s].Add(new ShaderError(GetLine(src, layoutStart),
                                    "Missing layout(set=...)"));
                            }

                            if (layout.Contains("binding"))
                            {
                                errors[s].Add(new ShaderError(GetLine(src, layoutStart),
                                    "Explicit binding not allowed"));
                            }

                            int set = ExtractSet(layout);

                            SkipWhitespace(src, ref i);
                            string type = ReadWord(src, ref i);

                            SkipWhitespace(src, ref i);

                            string name;

                            // ----- BLOCK -----
                            if (i < len && src[i] == '{')
                            {
                                int blockStart = i;
                                int blockEnd = FindMatching(src, blockStart, '{', '}');

                                string block = src.Substring(blockStart, blockEnd - blockStart + 1);

                                i = blockEnd + 1;
                                if (i < len && src[i] == ';') i++;

                                name = type; // block name is the type (UBO name)

                                int binding = GetBinding(set, name);

                                sb.Append($"layout(set={set}, binding={binding}) {keyword} {type} {block};");
                            }
                            else
                            {
                                name = ReadUntil(src, ref i, ';').Trim();

                                int binding = GetBinding(set, name);

                                sb.Append($"layout(set={set}, binding={binding}) {keyword} {type} {name};");
                                i++;
                            }

                            continue;
                        }

                        // fallback
                        sb.Append(layout);
                        continue;
                    }


                    // ------------------------------ IN / OUT ------------------------------

                    if (IsWordAt(src, i, "in") || IsWordAt(src, i, "out") ||
                        IsQualifier(PeekWord(src, i)))
                    {
                        List<string> qualifiers = new();

                        // ---------------- collect qualifiers ----------------
                        while (true)
                        {
                            string w = PeekWord(src, i);

                            if (IsQualifier(w))
                            {
                                qualifiers.Add(ReadWord(src, ref i));
                                SkipWhitespace(src, ref i);
                                continue;
                            }
                            break;
                        }

                        string keyword = ReadWord(src, ref i); // in/out

                        SkipWhitespace(src, ref i);
                        string type = ReadWord(src, ref i);

                        SkipWhitespace(src, ref i);
                        string name = ReadUntil(src, ref i, ';');

                        int loc = keyword == "out" ? outLoc++ : inLoc++;

                        // rebuild qualifier prefix
                        string qualPrefix = qualifiers.Count > 0
                            ? string.Join(" ", qualifiers) + " "
                            : "";

                        sb.Append($"layout(location={loc}) {qualPrefix}{keyword} {type} {name};");

                        i++; // skip ;
                        continue;
                    }


                    // ------------------------------ DEFAULT ------------------------------

                    sb.Append(src[i++]);
                }



                outputs[s] = sb.ToString();
            }

            return outputs;






            static bool IsQualifier(string w)
            {
                return w is "flat" or "smooth" or "noperspective" or "centroid" or "sample";
            }


            static string PeekWord(string s, int i)
            {
                SkipWhitespace(s, ref i);

                int start = i;
                while (i < s.Length && (char.IsLetterOrDigit(s[i]) || s[i] == '_'))
                    i++;

                return s.Substring(start, i - start);
            }


            int GetBinding(int set, string name)
            {
                var key = (set, name);

                if (globalBindings.TryGetValue(key, out var existing))
                    return existing;

                if (!nextBindingPerSet.TryGetValue(set, out var next))
                    next = 0;

                globalBindings[key] = next;
                nextBindingPerSet[set] = next + 1;

                return next;
            }



            static void SkipWhitespace(string s, ref int i)
            {
                while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
            }



            static string ReadWord(string s, ref int i)
            {
                int start = i;
                while (i < s.Length && (char.IsLetterOrDigit(s[i]) || s[i] == '_')) i++;
                return s.Substring(start, i - start);
            }



            static string ReadUntil(string s, ref int i, char end)
            {
                int start = i;
                while (i < s.Length && s[i] != end) i++;
                return s.Substring(start, i - start);
            }



            static int FindMatching(string s, int start, char open, char close)
            {
                int depth = 0;
                for (int i = start; i < s.Length; i++)
                {
                    if (s[i] == open) depth++;
                    else if (s[i] == close)
                    {
                        depth--;
                        if (depth == 0) return i;
                    }
                }
                return -1;
            }



            static int ExtractSet(string layout)
            {
                var m = Regex.Match(layout, @"set\s*=\s*(\d+)");
                return int.Parse(m.Groups[1].Value);
            }



            static int GetLine(string text, int index)
            {
                int line = 1;
                for (int i = 0; i < index && i < text.Length; i++)
                    if (text[i] == '\n') line++;
                return line-1;
            }



            static bool IsWordAt(string s, int index, string word)
            {
                if (index + word.Length > s.Length)
                    return false;

                if (s.Substring(index, word.Length) != word)
                    return false;

                bool leftOk = index == 0 || !char.IsLetterOrDigit(s[index - 1]);
                bool rightOk = index + word.Length >= s.Length || !char.IsLetterOrDigit(s[index + word.Length]);

                return leftOk && rightOk;
            }
        }





        public override string GetShaderStructDefinition<T>()
        {
            var map = MapToGlslType(typeof(T));

            if (map.builtIn)
                throw new Exception("Type maps to glsl primitive");

            var t = typeof(T);
            var sb = new StringBuilder();

            sb.AppendLine($"struct {map.name}");
            sb.AppendLine("{");

            foreach (var f in t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                AppendField(sb, f);
            }

            sb.AppendLine("};");

            return sb.ToString();



            static void AppendField(StringBuilder sb, FieldInfo f)
            {
                var type = f.FieldType;

                var fieldName = f.Name;

                if (fieldName.Contains("k__BackingField"))
                    fieldName = fieldName.Replace("<", "").Replace(">", "").Replace("k__BackingField", "");



                // ------------- FIXED BUFFER -------------

                if (f.IsDefined(typeof(FixedBufferAttribute), false))
                {
                    var attr = f.GetCustomAttribute<FixedBufferAttribute>();
                    var elemType = MapToGlslType(attr.ElementType).name;
                    int length = attr.Length;

                    sb.AppendLine($"    {elemType} {fieldName}[{length}];");
                    return;
                }


                // ------------- INLINE ARRAY -------------

                var inlineAttr = type.GetCustomAttribute<InlineArrayAttribute>();
                if (inlineAttr != null)
                {
                    int length = inlineAttr.Length;

                    var elementField = type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                                           .First();

                    var elemType = MapToGlslType(elementField.FieldType).name;

                    sb.AppendLine($"    {elemType} {fieldName}[{length}];");
                    return;
                }


                // ------------- FIELD -------------

                var glslType = MapToGlslType(type).name;
                sb.AppendLine($"    {glslType} {fieldName};");
            }
        }




        private static (string name, bool builtIn) MapToGlslType(Type t)
        {
            if (t == typeof(float)) return ("float", true);
            if (t == typeof(int)) return ("int", true);
            if (t == typeof(uint)) return ("uint", true);
            if (t == typeof(bool)) return ("bool", true);

            if (t == typeof(Vector2)) return ("vec2", true);
            if (t == typeof(Vector3)) return ("vec3", true);
            if (t == typeof(Vector4)) return ("vec4", true);

            if (t == typeof(Matrix4x4)) return ("mat4", true);

            if (t.IsEnum)
                return ("int", true);

            if (t.IsValueType && !t.IsPrimitive)
                return (SanitizeTypeName(t), false); 

            throw new NotSupportedException();
        }




        public override string GetShaderStructName<T>() => MapToGlslType(typeof(T)).name;

    }






    /// <summary>
    /// Supports modern HLSL. Requires DXC.
    /// <br/> This class treats explicit spaces as resource sets. For example, register(t1, space0) becomes equal to set 0, binding 1
    /// </summary>
    private partial class HLSLHandler : ShaderLanguageHandler
    {

        public override SyntaxHighlightingRules GetHighlighting() => new()
        {
            CommentPattern = @"(//.*$)",

            TypePattern =
                @"\b(bool|int|uint|float|float[1-4]|float[1-4]x[1-4]|Texture2D|SamplerState)\b",

            KeywordPattern =
                @"\b(cbuffer|struct|register|packoffset|in|out|inout|return|if|else|for|while|break|continue)\b",

            BuiltinPattern =
                @"\b(lerp|mul|dot|normalize|clamp|length|sin|cos|abs|pow|max|min|floor|ceil|frac|fmod|step|smoothstep|reflect|refract|cross|distance|exp|log)\b",

            NumberPattern =
                @"(?<![\w.])(\d+\.\d+|\d+)(f)?\b"
        };



        public override async Task<SpirvDrawCompilationResult> CompileShader(string vertexSource, string fragmentSource, string[] setNames, ShaderRegisterCallInformation shaderInfo)
        {
            var vertTask = CompileShaderStage(vertexSource, "vs_6_0");
            var fragTask = CompileShaderStage(fragmentSource, "ps_6_0");

            await vertTask;
            await fragTask;

            bool success =
                vertTask.Result.Spirv != null &&
                fragTask.Result.Spirv != null;



            var reflect = await SpirvReflectOnMultipleStages([vertTask.Result.Spirv, fragTask.Result.Spirv], setNames);

            if (reflect.Errors.Count != 0)
                return new(vertTask.Result, fragTask.Result, null, reflect.Errors);


            return new SpirvDrawCompilationResult(vertTask.Result,
                                                  fragTask.Result,
                                                  success ? new ShaderMetadata(shaderInfo.shaderName, reflect.VertexInputAttributes, reflect.FragmentOutputAttributes, reflect.ResourceSets) : null,
                                                  null);
        }





        public override async Task<SpirvComputeCompilationResult> CompileComputeShader(string source, string[] setNames, ShaderRegisterCallInformation shaderInfo)
        {
            var result = await CompileShaderStage(source, "cs_6_0");

            var reflect = await SpirvReflectOnMultipleStages([result.Spirv], setNames);

            if (reflect.Errors.Count != 0)
                return new(result, null, reflect.Errors);

            return new SpirvComputeCompilationResult(result, result.Errors == null ? new ComputeShaderMetadata(reflect.ResourceSets) : null, null);
        }








        private static async Task<SpirvStageCompilationResult> CompileShaderStage(
            string src,
            string stage)
        {
            var errors = await DxcValidateCheckForErrors(src, stage);

            if (errors.Count != 0)
                return new SpirvStageCompilationResult(null, errors, src);

            var spirv = await DxcCompileToSpirv(src, stage);

            return new SpirvStageCompilationResult(spirv, null, src);
        }




        private static async Task<List<ShaderError>> DxcValidateCheckForErrors(
            string src,
            string stage)
        {
            var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.hlsl");
            await File.WriteAllTextAsync(path, src);

            var errors = new List<ShaderError>();


            var psi = new ProcessStartInfo
            {
                FileName = "dxc",
                Arguments =
                    $"-T {stage} " +
                    $"-E main " +
                    "-spirv " +
                    "-fspv-target-env=vulkan1.3 " +
                    "-fvk-use-dx-layout " +
                    $"\"{path}\"",
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };



            using var proc = Process.Start(psi);
            string output = await proc.StandardError.ReadToEndAsync();
            await proc.WaitForExitAsync();



            // DXC errors are a bit different format
            var regex = new Regex(@"^(.*)\((\d+),\d+\):\s*(error|warning).*:\s*(.+)$", RegexOptions.Multiline);


            foreach (Match m in regex.Matches(output))
            {
                int line = int.Parse(m.Groups[2].Value);
                string msg = m.Groups[4].Value;

                errors.Add(new ShaderError(line - 1, msg));
            }


            File.Delete(path);

            return errors;
        }




        private static async Task<byte[]> DxcCompileToSpirv(
            string src,
            string stage)
        {
            var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.hlsl");
            var spvPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.spv");

            await File.WriteAllTextAsync(path, src);


            using var proc = Process.Start(new ProcessStartInfo
            {
                FileName = "dxc",
                Arguments =
                    $"-T {stage} " +
                    $"-E main " +
                    "-spirv " +
                    "-fspv-target-env=vulkan1.3 " +
                    "-fvk-use-dx-layout " +   
                    $"-Fo \"{spvPath}\" " +
                    $"\"{path}\"",
                UseShellExecute = false,
                CreateNoWindow = true
            });

            await proc.WaitForExitAsync();

            var bytes = await File.ReadAllBytesAsync(spvPath);

            File.Delete(path);
            File.Delete(spvPath);

            return bytes;
        }




        public override string GetShaderStructDefinition<T>()
        {
            var t = typeof(T);


            var map = MapToHlslType(t);


            if (map.builtIn)
                throw new Exception("Type maps to HLSL primitive");

            var sb = new StringBuilder();

            sb.AppendLine($"struct {map.name}");
            sb.AppendLine("{");

            foreach (var field in t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                                   .OrderBy(f => f.MetadataToken))
            {
                var fieldType = field.FieldType;



                // ------------- FIXED BUFFER -------------

                var fixedAttr = field.GetCustomAttribute<System.Runtime.CompilerServices.FixedBufferAttribute>();
                if (fixedAttr != null)
                {
                    var elementType = fixedAttr.ElementType;
                    int length = fixedAttr.Length;

                    var (elemName, _) = MapToHlslType(elementType);

                    sb.AppendLine($"    {elemName} {field.Name}[{length}];");
                    continue;
                }



                // ------------- INLINE ARRAY -------------

                var inlineAttr = fieldType.GetCustomAttribute<System.Runtime.CompilerServices.InlineArrayAttribute>();
                if (inlineAttr != null)
                {
                    int length = inlineAttr.Length;

                    var innerField = fieldType.GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
                                              .First();

                    var elementType = innerField.FieldType;
                    var (elemName, _) = MapToHlslType(elementType);

                    sb.AppendLine($"    {elemName} {field.Name}[{length}];");
                    continue;
                }



                // ------------- FIELD -------------

                var (typeName, _) = MapToHlslType(fieldType);

                sb.AppendLine($"    {typeName} {field.Name};");
            }

            sb.AppendLine("};");

            return sb.ToString();
        }



        private static (string name, bool builtIn) MapToHlslType(Type t)
        {
            if (t == typeof(float)) return ("float", true);
            if (t == typeof(int)) return ("int", true);
            if (t == typeof(uint)) return ("uint", true);
            if (t == typeof(bool)) return ("bool", true);

            if (t == typeof(Vector2)) return ("float2", true);
            if (t == typeof(Vector3)) return ("float3", true);
            if (t == typeof(Vector4)) return ("float4", true);

            if (t == typeof(Matrix4x4)) return ("float4x4", true);

            if (t.IsEnum)
                return ("int", true);

            if (t.IsValueType && !t.IsPrimitive)
                return (SanitizeTypeName(t), false);

            throw new NotSupportedException();
        }

        public override string GetShaderStructName<T>() => MapToHlslType(typeof(T)).name;

    }




}
