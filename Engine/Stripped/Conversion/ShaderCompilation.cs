

namespace Engine.Stripped;

using Engine.Core;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Numerics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using static Engine.Core.EngineMath;
using static Engine.Core.Rendering;
using static Engine.Core.RenderingBackend;
using static Engine.Core.RenderingBackend.ResourceSetResourceDeclaration;
using static RunProcess;




/// <summary>
/// Manages shader construction, compilation and reflection in debug builds.
/// </summary>
public static class ShaderCompilation
{



    /// <summary>
    /// Unmanaged types that correspond directly to built-in glsl types (and therefore won't be reflected on and defined as custom structs in shader)
    /// </summary>
    private static readonly ImmutableDictionary<Type, string> CoreGLSLTypes = ImmutableDictionary.ToImmutableDictionary(new Dictionary<Type, string>
    {
        // Primitives
        { typeof(float), "float" },
        { typeof(bool),  "bool" },
        { typeof(uint),  "uint" },
        { typeof(int),   "int" },

        // Vectors
        { typeof(Vector2),       "vec2" },
        { typeof(Vector2<int>),  "ivec2" },
        { typeof(Vector2<uint>), "uvec2" },
        { typeof(Vector3),       "vec3" },
        { typeof(Vector3<int>),  "ivec3" },
        { typeof(Vector3<uint>), "uvec3" },
        { typeof(Vector4),       "vec4" },
        { typeof(Vector4<int>),  "ivec4" },
        { typeof(Vector4<uint>), "uvec4" },

        // Matrices
        { typeof(Matrix3x3), "mat3" },
        { typeof(Matrix4x4), "mat4" }
    });







    //RESOURCE SET
    public record class ShaderResourceSetDefinition(OrderedDictionary<string, IShaderResourceSetResourceDefinition> Content);


    //RESOURCES
    public interface IShaderResourceSetResourceDefinition;


    private interface IShaderDataBufferDefinition
    {
        public OrderedDictionary<string, IShaderBufferStructDeclaration> Vars { get; }
    }



    /// <summary>
    /// Defines a uniform buffer / array within a resource set.
    /// </summary>
    /// <param name="vars"></param>
    public record class ShaderUniformBufferDefinition(OrderedDictionary<string, IShaderBufferStructDeclaration> vars) : IShaderResourceSetResourceDefinition, IShaderDataBufferDefinition
    {
        public OrderedDictionary<string, IShaderBufferStructDeclaration> Vars => vars;
    }

    /// <summary>
    /// Defines a storage buffer / array within a resource set.
    /// </summary>
    /// <param name="vars"></param>
    public record class ShaderStorageBufferDefinition(OrderedDictionary<string, IShaderBufferStructDeclaration> vars, SSBOReadWriteFlags allowedAccess) : IShaderResourceSetResourceDefinition, IShaderDataBufferDefinition
    {
        public OrderedDictionary<string, IShaderBufferStructDeclaration> Vars => vars;
    }



    /// <summary>
    /// Defines a texture (texture sampler combination) / array within a resource set.
    /// <inheritdoc cref="_arraylengthexplanation"/>
    /// </summary>
    /// <param name="SamplerType"></param>
    /// <param name="ArrayLength"></param>
    public record class ShaderTextureDefinition(TextureSamplerTypes SamplerType, uint ArrayLength = 0) : IShaderResourceSetResourceDefinition;






    //BUFFER CONTENT
    public interface IShaderBufferStructDeclaration
    {
        public uint arrayLength { get; }
    }

    /// <summary>
    /// Defines an unmanaged struct field/array inside of a shader buffer. 
    /// <br/> <typeparamref name="StructT"/> can be any unmanaged struct (and its fields will be reflected as you'd expect, if it isn't present in <see cref="CoreGLSLTypes"/>), but if the struct contains padding, marshalling won't work correctly.
    /// <inheritdoc cref="_arraylengthexplanation"/>
    /// </summary>
    /// <typeparam name="StructT"></typeparam>
    /// <param name="ArrayLength"></param>
    public record class ShaderBufferStructDeclaration<StructT>(uint ArrayLength = 0) : IShaderBufferStructDeclaration where StructT : unmanaged
    {
        public uint arrayLength => ArrayLength;
    }






    //ATTRIBUTES
    /// <summary>
    /// Declares an unmanaged struct field/array inside of a shader buffer. 
    /// <inheritdoc cref="_arraylengthexplanation"/>
    /// </summary>
    /// <param name="Format"></param>
    /// <param name="StageMask"></param>
    /// <param name="ArrayLength"></param>
    public record class ShaderAttributeDefinition(ShaderAttributeBufferFinalFormat Format, ShaderAttributeStageMask StageMask, uint ArrayLength = 0);




    /// <summary>
    /// <br/> If <see cref="ArrayLength"/> is less or equal to 1, it'll be treated as a single element. Any higher and it'll be treated as an array.
    /// </summary>
    private struct _arraylengthexplanation;





    /// <summary>
    /// Determines which shader stages a shader attribute will show up in.
    /// <br/> <see cref="VertexIn"/> will add the attribute as a vertex stage input.
    /// <br/> <see cref="VertexOutFragmentIn"/> will add the attribute to the fragment stage with an added "Frag" prefix, and will add an automatic assignment if <see cref="VertexIn"/> is also present.
    /// <br/> <see cref="FragmentOut"/> will add the attribute as a fragment stage output, with an added "FragOut" prefix, and will add an automatic assignment if <see cref="VertexOutFragmentIn"/> is also present.
    /// 
    /// <br/> 
    /// <br/> For example, using all values on a single attribute named "Color" would generate glsl like this:
    /// <br/>
    /// 
    /// <br/> <b>Vertex Stage</b>:
    /// <br/> 
    /// <code>
    /// 
    /// layout(location = 0) in vec4 Color;                // --- VertexIn
    /// layout(location = 0) out vec4 FragColor;           // --- VertexOutFragmentIn
    /// 
    /// void main()
    /// {
    ///     FragColor = Color;                            // --- VertexOutFragmentIn (only if VertexIn is also present)
    ///     //Your main vertex shader body
    /// }
    /// 
    /// </code>
    /// <br/>
    /// 
    /// <br/> <b>Fragment Stage</b>:
    /// <br/> 
    /// <code>
    /// 
    /// layout(location = 0) in vec4 FragColor;             // --- VertexOutFragmentIn
    /// layout(location = 0) out vec4 FragOutColor;         // --- FragmentOut
    /// 
    /// void main()
    /// {
    ///     FragOutColor = FragColor;                      // --- FragmentOut (only if VertexOutFragmentIn is also present)
    ///     //Your main fragment shader body
    /// }
    /// 
    /// 
    /// 
    /// </code>
    /// 
    /// </summary>
    [Flags]
    public enum ShaderAttributeStageMask
    {
        VertexIn = 1,
        VertexOutFragmentIn = 2,
        FragmentOut = 4
    }







    private static bool CompilingShaders;



    /// <summary>
    /// Compiles a full set of shader stages (or registers them to be compiled during building in release builds) along with metadata to interact with.
    /// </summary>
    /// <param name="ShaderName"></param>
    /// <param name="ResourceSets"></param>
    /// <param name="Attributes"></param>
    /// <param name="VertexMainBody"></param>
    /// <param name="FragmentMainBody"></param>
    /// <param name="VertexExtra"></param>
    /// <param name="FragmentExtra"></param>
    public static void RegisterShader(string ShaderName, Dictionary<string, ShaderResourceSetDefinition> ResourceSets, Dictionary<string, ShaderAttributeDefinition> Attributes, string VertexMainBody, string FragmentMainBody)
    {
        ResourceSets ??= new();
        Attributes ??= new();

        ShaderRegister(ShaderName, method());


        async Task method()
        {
            var result = await ConstructShaderSource(ShaderName, ResourceSets, Attributes, VertexMainBody, FragmentMainBody, GetRequiredShaderFormatForBackend(CurrentBackend));

            if (result != null)
            {
                var src = new ShaderSource(result.Value.Item3, result.Value.Item1.Source.ToImmutableArray(), result.Value.Item2.Source.ToImmutableArray());

#if ENGINE_BUILD_PASS
                EngineBuildProcess.ShaderSources[CurrentBackend].Add(ShaderName, src);                
#else
                BackendShaderReference.Create(ShaderName, src);
#endif

            }
        }
    }


    /// <summary>
    /// Compiles a compute shader (or registers it to be compiled during building in release builds) along with metadata to interact with.
    /// </summary>
    /// <param name="ShaderName"></param>
    /// <param name="ResourceSets"></param>
    /// <param name="MainBody"></param>
    /// <param name="LocalSize"></param>
    /// <param name="Extra"></param>
    public static void RegisterComputeShader(string ShaderName, Dictionary<string, ShaderResourceSetDefinition> ResourceSets, string MainBody, Vector3<uint> LocalSize)
    {
        ResourceSets ??= new();

        ShaderRegister(ShaderName, method());


        async Task method()
        {
            var result = await ConstructComputeShaderSource(ShaderName, ResourceSets, LocalSize, MainBody, GetRequiredShaderFormatForBackend(CurrentBackend));

            if (result != null)
            {

                var src = new ComputeShaderSource(result.Value.Item2, result.Value.Item1.Source.ToImmutableArray());

#if ENGINE_BUILD_PASS
                EngineBuildProcess.ComputeShaderSources[CurrentBackend].Add(ShaderName, src);                
#else
                BackendComputeShaderReference.Create(ShaderName, src);
#endif
            }
        }
    }








    private static void ShaderRegister(string ShaderName, Task register)
    {
        if (!CompilingShaders) throw new Exception("Shaders can't be registered outside of Entry.InitShaders.");


        if (refreshingJust != null && refreshingJust != ShaderName)
            return;


        lock (RegisteringTasks)
            RegisteringTasks.Add(register);

    }














    /// <summary>
    ///  Reconstruct and recompile all shaders.
    /// </summary>
    /// <param name="name"></param>
    [Conditional("DEBUG")]
    public static void CompileShaders()
    {

        lock (ModifyingShaders)
        {
            CompilingShaders = true;


            lock (RegisteringTasks)
                RegisteringTasks.Clear();


            Entry.InitShaders();
            EngineDebug.InitShaders();


            lock (RegisteringTasks)
                Task.WaitAll(RegisteringTasks);


            CompilingShaders = false;

        }
    }

    /// <summary>
    /// (Re)compile one specific shader.
    /// </summary>
    /// <param name="name"></param>
    [Conditional("DEBUG")]
    public static void CompileShader(string name)
    {
        lock (ModifyingShaders)
        {
            refreshingJust = name;
            CompileShaders();
            refreshingJust = null;
        }
    }







    private static string refreshingJust = null;

    private static List<Task> RegisteringTasks = new();

    private static object ModifyingShaders = new();







    private static (string glslTypeName, string structDeclaration, Dictionary<string, string> structDependencies) GetGLSLTypeInformation(Type structtype)
    {

        if (CoreGLSLTypes.TryGetValue(structtype, out var g))
            return (g, null, null);






        var glslName = structtype.FullName.Replace("+", "_");

        var deps = new Dictionary<string, string>();
        var sb = new StringBuilder();


        sb.AppendLine($"//{structtype}");
        sb.AppendLine($"struct {glslName}\n{{");


        foreach (var field in structtype.GetFields(
             BindingFlags.Public | BindingFlags.Instance))
        {
            var info = GetGLSLTypeInformation(field.FieldType);

            sb.AppendLine($"    //{field.FieldType};");
            sb.AppendLine($"    {info.glslTypeName} {field.Name};");


            if (info.structDeclaration != null)
            {
                if (!deps.ContainsKey(info.glslTypeName))
                    deps[info.glslTypeName] = info.structDeclaration;


                if (info.structDependencies != null)
                {
                    foreach (var kv in info.structDependencies)
                        deps[kv.Key] = kv.Value;
                }
            }
        }

        sb.AppendLine("};");

        return (glslName, sb.ToString(), deps);

    }










    public enum ShaderSuffix
    {
        _vert,  //vertex

        _frag,  //fragment

        _comp,  //compute
    }



    private static async Task<(ShaderStageResultStruct, ShaderStageResultStruct, ShaderMetadata)?> ConstructShaderSource(string ShaderName, Dictionary<string, ShaderResourceSetDefinition> ResourceSets, Dictionary<string, ShaderAttributeDefinition> Attributes, string VertexMainBody, string FragmentMainBody, ShaderFormat targetFormat, bool strip = true)
    {


        var resourceglsl = GenerateResourceGLSL(ResourceSets, [VertexMainBody, FragmentMainBody], strip);


        string vertexFinalGLSL, fragmentFinalGLSL;

        vertexFinalGLSL = 
        fragmentFinalGLSL 
            = gencommentdividerline("RESOURCES") + resourceglsl.code;




        Dictionary<ShaderAttributeStageMask, byte> locationCounters = new();

        string VSins = string.Empty;
        string VSouts = string.Empty;
        string FSins = string.Empty;
        string FSouts = string.Empty;


        Dictionary<string, (byte, ShaderMetadata.ShaderInOutAttributeMetadata)> VertexInAttributes = new();
        Dictionary<string, (byte, ShaderMetadata.ShaderInOutAttributeMetadata)> FragmentOutAttributes = new();



        foreach (var attr in Attributes)
        {
            byte sizeRequirement = attr.Value.Format switch
            {
                ShaderAttributeBufferFinalFormat.Mat4 => 4,
                ShaderAttributeBufferFinalFormat.Mat3 => 3,
                _ => 1,
            };

            // Iterate over the stages we care about
            foreach (var stage in new[] {
                 ShaderAttributeStageMask.VertexIn,
                 ShaderAttributeStageMask.VertexOutFragmentIn,
                 ShaderAttributeStageMask.FragmentOut })
            {
                if ((attr.Value.StageMask & stage) == 0) continue;

                if (!locationCounters.TryGetValue(stage, out var loc))
                    loc = locationCounters[stage] = 0;

                string prefix = stage switch
                {
                    ShaderAttributeStageMask.VertexIn => "",
                    ShaderAttributeStageMask.VertexOutFragmentIn => "Frag",
                    ShaderAttributeStageMask.FragmentOut => "FragOut",
                    _ => ""
                };

                string decl = $"\nlayout(location = {loc}) {(stage == ShaderAttributeStageMask.FragmentOut || stage == ShaderAttributeStageMask.VertexOutFragmentIn && false ? "out" : "in")} {attr.Value.Format.ToString().ToLower()} {prefix}{attr.Key}" +
                              (attr.Value.ArrayLength > 1 ? $"[{attr.Value.ArrayLength}]" : "") + ";";

                // Append to correct string
                switch (stage)
                {
                    case ShaderAttributeStageMask.VertexIn:
                        VSins += decl;
                        VertexInAttributes[attr.Key] = (loc, new ShaderMetadata.ShaderInOutAttributeMetadata(attr.Value.Format, attr.Value.ArrayLength));
                        break;
                    case ShaderAttributeStageMask.VertexOutFragmentIn:
                        VSouts += decl.Replace("in", "out");   // VS out
                        FSins += decl;                        // FS in
                        break;
                    case ShaderAttributeStageMask.FragmentOut:
                        FSouts += decl;
                        FragmentOutAttributes[attr.Key] = (loc, new ShaderMetadata.ShaderInOutAttributeMetadata(attr.Value.Format, attr.Value.ArrayLength));
                        break;
                }

                // Increment location counter by how many slots this attribute consumes
                locationCounters[stage] = (byte)(loc + sizeRequirement);
            }

            // Add automatic assignments
            if ((attr.Value.StageMask & ShaderAttributeStageMask.VertexIn) != 0 &&
                (attr.Value.StageMask & ShaderAttributeStageMask.VertexOutFragmentIn) != 0)
                VertexMainBody = $"\tFrag{attr.Key} = {attr.Key};\n" + VertexMainBody;

            if ((attr.Value.StageMask & ShaderAttributeStageMask.VertexOutFragmentIn) != 0 &&
                (attr.Value.StageMask & ShaderAttributeStageMask.FragmentOut) != 0)
                FragmentMainBody = $"\tFragOut{attr.Key} = {attr.Key};\n" + FragmentMainBody;
        }




        vertexFinalGLSL +=
            gencommentdividerline("INPUTS")
            + VSins
            + gencommentdividerline("OUTPUTS")
            + VSouts
            + "\n\n\nvoid main()\n{\n" + VertexMainBody + "\n}";

        fragmentFinalGLSL +=
            gencommentdividerline("INPUTS")
            + FSins
            + gencommentdividerline("OUTPUTS")
            + FSouts
            + "\n\n\nvoid main()\n{\n" + FragmentMainBody + "\n}";






        var vertexFinalTask = CompileIndividualStageShader(ShaderName, vertexFinalGLSL, ShaderSuffix._vert, targetFormat);
        var fragmentFinalTask = CompileIndividualStageShader(ShaderName, fragmentFinalGLSL, ShaderSuffix._frag, targetFormat);

        var vertexFinal = await vertexFinalTask;
        var fragmentFinal = await fragmentFinalTask;
        if (vertexFinal == null || fragmentFinal == null) return null;




        var reflection = JsonSerializer.Deserialize<JsonElement>(vertexFinal!.Value.reflection);







        var meta = new ShaderMetadata(
            ImmutableDictionary.ToImmutableDictionary(VertexInAttributes),
            ImmutableDictionary.ToImmutableDictionary(FragmentOutAttributes),
            CreateResourceSetMetadataDictionary(ResourceSets, resourceglsl.finalsets, resourceglsl.buffertypenamelookup, reflection));


        meta.GeneratedVertexGLSL = vertexFinalGLSL;
        meta.GeneratedFragmentGLSL = fragmentFinalGLSL;


        return new (vertexFinal!.Value, fragmentFinal!.Value, meta);



    }




    public static async Task<(ShaderStageResultStruct, ComputeShaderMetadata)?> ConstructComputeShaderSource(string ShaderName, Dictionary<string, ShaderResourceSetDefinition> ResourceSets, Vector3<uint> LocalSize, string MainBody, ShaderFormat targetFormat, bool strip = true)
    {
        var resourceglsl = GenerateResourceGLSL(ResourceSets, [MainBody], strip);


        string finalglsl =
            gencommentdividerline("RESOURCES")
            + resourceglsl.code
            + "\n\n\nvoid main()\n{\n" + MainBody + "\n}";



        var comp = await CompileIndividualStageShader(ShaderName, finalglsl, ShaderSuffix._vert, targetFormat);
        if (comp == null) return null;


        var reflection = JsonSerializer.Deserialize<JsonElement>(comp!.Value.reflection);

        var meta = new ComputeShaderMetadata(
            CreateResourceSetMetadataDictionary(ResourceSets, resourceglsl.finalsets, resourceglsl.buffertypenamelookup, reflection));

        meta.GeneratedGLSL = finalglsl;

        return new(comp.Value, meta);

    }



    private static ImmutableDictionary<string, (uint, ShaderMetadata.ShaderResourceSetMetadata)> CreateResourceSetMetadataDictionary(Dictionary<string, ShaderResourceSetDefinition> ResourceSets, List<string> FinalSetBindings, Dictionary<string, string> BufferTypeNameLookup, JsonElement reflection)
    {

        Dictionary<string, (uint, ShaderMetadata.ShaderResourceSetMetadata)> ResourceSetMetadataFinal = new();



        for (byte i = 0; i < FinalSetBindings.Count; i++)
        {
            var setName = FinalSetBindings[i];



            Dictionary<string, (uint, ShaderMetadata.ShaderTextureMetadata)> texdict = new();

            if (reflection.TryGetProperty("textures", out var textures))
            {
                foreach (var v in textures.EnumerateArray())
                {
                    var set = v.GetProperty("set").GetByte();
                    if (set != i) continue;

                    var texname = v.GetProperty("name").GetString();

                    var definition = ((ShaderTextureDefinition)ResourceSets[setName].Content[texname]);

                    texdict.Add(texname, new (v.GetProperty("binding").GetUInt32(), new ShaderMetadata.ShaderTextureMetadata(definition.SamplerType, definition.ArrayLength)));
                }
            }



            var texes = ImmutableDictionary.ToImmutableDictionary(texdict);
            var ubos = ImmutableDictionary.ToImmutableDictionary(GetShaderDataBufferMetadataDictionary("ubos"));
            var ssbos = ImmutableDictionary.ToImmutableDictionary(GetShaderDataBufferMetadataDictionary("ssbos"));



           var dec = new ResourceSetResourceDeclaration[texes.Count + ubos.Count + ssbos.Count];


            foreach (var v in texes)
                dec[(int)v.Value.Item1] = new ResourceSetResourceDeclaration(ResourceSetResourceType.Texture, v.Value.Item2.ArrayLength);

            foreach (var v in ubos)
                dec[(int)v.Value.Item1] = new ResourceSetResourceDeclaration(ResourceSetResourceType.UniformBuffer, 1);

            foreach (var v in ssbos)
                dec[(int)v.Value.Item1] = new ResourceSetResourceDeclaration(ResourceSetResourceType.StorageBuffer, 1);



            ResourceSetMetadataFinal[setName] = new (i, 
                new ShaderMetadata.ShaderResourceSetMetadata(
                    ImmutableArray.ToImmutableArray(dec),
                    texes,
                    ubos,
                    ssbos));




            Dictionary<string, (uint, ShaderMetadata.ShaderBufferMetadata)> GetShaderDataBufferMetadataDictionary(string reflectionProperty)
            {
                var ret = new Dictionary<string, (uint, ShaderMetadata.ShaderBufferMetadata)>();



                if (reflection.TryGetProperty(reflectionProperty, out var all))
                {
                    foreach (var v in all.EnumerateArray())
                    {
                        var bufferTypeName = v.GetProperty("name").GetString();
                        var set = v.GetProperty("set").GetByte();

                        if (set != i) continue;


                        //the instance name given by user
                        var usableName = BufferTypeNameLookup[bufferTypeName];


                        var spirvInternalTypeDec = reflection.GetProperty("types").GetProperty(v.GetProperty("type").GetString());


                        List<(uint start, uint end)> datastartendpoints = new();


                        Dictionary<string, uint> offsets = new();


                        foreach (var member in spirvInternalTypeDec.GetProperty("members").EnumerateArray())
                        {

                            var MEMBER_NAME = member.GetProperty("name");
                            var MEMBER_TYPE = member.GetProperty("type").GetString();
                            if (MEMBER_TYPE.StartsWith('_')) MEMBER_TYPE = reflection.GetProperty("types").GetProperty(MEMBER_TYPE).GetProperty("name").ToString();

                            MEMBER_TYPE = MEMBER_TYPE.Replace("_", ".");



                            uint realsize = 0;


                            //type is core so we have an entry for it
                            if (CoreGLSLTypes.ContainsValue(MEMBER_TYPE))
                            {
                                var type = CoreGLSLTypes.Where(x => x.Value == MEMBER_TYPE).FirstOrDefault().Key;
                                realsize = (uint)Marshal.SizeOf(type);
                            }

                            //look type up otherwise (must not be padded)
                            else
                            {
                                realsize = (uint)Marshal.SizeOf(Type.GetType(MEMBER_TYPE));
                            }


                            var offs = member.GetProperty("offset").GetUInt32();
                            datastartendpoints.Add(new (offs, offs+realsize));


                            offsets[MEMBER_NAME.ToString()] = offs;

                        }



                        var regionsList = new List<ContiguousRegion>();
                        if (datastartendpoints.Count != 0)
                        {
                            // Sort by start offset
                            datastartendpoints.Sort((a, b) => a.start.CompareTo(b.start));

                            // Initialize first merged region
                            uint mergedStart = datastartendpoints[0].start;
                            uint mergedEnd = datastartendpoints[0].end;

                            for (int i = 1; i < datastartendpoints.Count; i++)
                            {
                                var (start, end) = datastartendpoints[i];

                                if (start <= mergedEnd)
                                {
                                    // Overlapping or touching → extend the current region
                                    mergedEnd = Math.Max(mergedEnd, end);
                                }
                                else
                                {
                                    // Gap → emit previous merged region
                                    regionsList.Add(new ContiguousRegion(mergedStart, mergedEnd));
                                    mergedStart = start;
                                    mergedEnd = end;
                                }
                            }

                            regionsList.Add(new ContiguousRegion(mergedStart, mergedEnd));
                        }


                        //if (offsets.Count == 3) throw new Exception(); 



                        ret.Add(usableName, new(
                            v.GetProperty("binding").GetUInt32(), new ShaderMetadata.ShaderBufferMetadata
                            (
                                FieldOffsets: ImmutableDictionary.ToImmutableDictionary(offsets),
                                ContiguousRegions: regionsList.ToImmutableArray(),
                                SizeRequirement: v.GetProperty("block_size").GetUInt32()
                            )));

                    }
                }

                return ret;
            }
        }

        return ImmutableDictionary.ToImmutableDictionary(ResourceSetMetadataFinal);
    }












    const string commentslashes = "////////////";
    static string gencommentdividerline(string name) => $"\n\n\n\n\n{commentslashes}{name}{commentslashes}\n\n";




    public static (string code, List<string> finalsets, Dictionary<string, string> buffertypenamelookup) GenerateResourceGLSL(Dictionary<string, ShaderResourceSetDefinition> resourceSets, string[] bodiesToCheck, bool strip = true)
    {


        OrderedDictionary<string, ShaderResourceSetDefinition> FinalResourceSets = new();



        foreach (var kv in resourceSets)
        {
            bool used = false;

            if (strip)
            {
                foreach (var get in kv.Value.Content)
                {
                    if (get.Value is IShaderDataBufferDefinition buffer)
                    {
                        foreach (var varGet in buffer.Vars)
                        {
                            check(varGet.Key);
                            if (used) break;
                        }
                        if (used) break;
                    }


                    else if (get.Value is ShaderTextureDefinition tex)
                    {
                        check(get.Key);
                        if (used) break;
                    }

                    else throw new Exception("Unimplemented resource set resource type");


                    if (used) break;


                    void check(string n)
                    {
                        foreach (var value in bodiesToCheck)
                        {
                            if (value.Contains($"{kv.Key}.{n}"))  //<-- buffer name qualified
                            {
                                resourceSets.TryAdd(kv.Key, kv.Value);
                                used = true;
                                return;
                            }
                        }

                        used = true;
                    }
                }
            }
            else used = true;


            if (used)
                FinalResourceSets.Add(kv.Key, kv.Value);
        }







        OrderedDictionary<string, string> structDeclarations = new();


        void SerializeStructs(Type[] defs)
        {
            foreach (var def in defs)
            {
                var chk = GetGLSLTypeInformation(def);

                if (chk.structDependencies != null) foreach (var kv in chk.structDependencies) pushStructDec(kv.Key, kv.Value);
                if (chk.structDeclaration != null) pushStructDec(chk.glslTypeName, chk.structDeclaration);

                void pushStructDec(string typeName, string content) => structDeclarations[typeName] = content;
            }
        }




        string final = string.Empty;


        var setIndex = 0;

        var bufferDecIndex = 0;


        var buffertypenamelookup = new Dictionary<string, string>();



        foreach (var resSet in FinalResourceSets)
        {
            string n = gencommentdividerline($"Set {setIndex} - {resSet.Key}");

            final += n;


            var resourceIndex = 0;
            var resfields = resSet.Value.Content;

            foreach (var get in resfields)
            {
                string rep = null;


                if (get.Value is IShaderDataBufferDefinition buf)
                {

                    var buffertypename = $"_buffer_{bufferDecIndex++}";

                    rep = $"\n" +

                        //buffer layout
                        $"layout({(get.Value is ShaderStorageBufferDefinition ssbochk1 ? "std430" : "std140")}, " +


                        //set/binding
                        $"set = {setIndex}, binding = {resourceIndex}) " +


                        //buffer declaration
                        $"{(get.Value is ShaderStorageBufferDefinition ssbochk2 ? ((ssbochk2.allowedAccess switch
                        {
                            SSBOReadWriteFlags.Read => "readonly",
                            SSBOReadWriteFlags.Write => "writeonly",
                            SSBOReadWriteFlags.Read | SSBOReadWriteFlags.Write => string.Empty,
                            _ => throw new Exception()
                        }) + " buffer") : "uniform")} {buffertypename}\n";



                    buffertypenamelookup[buffertypename] = get.Key;



                    //buffer content

                    var bufferElements = buf.Vars;
                    var sb = new StringBuilder();


                    sb.AppendLine("{");

                    int i = 0;
                    foreach (var v in bufferElements)
                    {
                        var info = GetGLSLTypeInformation(v.Value.GetType().GetGenericArguments()[0]);

                        sb.Append("\t");
                        sb.Append(info.glslTypeName);
                        sb.Append(' ');
                        sb.Append(v.Key);

                        if (v.Value.arrayLength > 1) sb.Append($"[{v.Value.arrayLength}]");

                        sb.AppendLine(";");

                        i++;
                    }


                    sb.AppendLine($"}} {get.Key};\n");

                    rep += sb.ToString();

                    SerializeStructs(bufferElements.Select(x => x.Value.GetType().GetGenericArguments()[0]).ToArray());
                }



                else if (get.Value is ShaderTextureDefinition tex)
                {
                    rep = $"\nlayout(set = {setIndex}, binding = {resourceIndex}) uniform {(tex.SamplerType switch
                    {
                        TextureSamplerTypes.Texture2D => "sampler2D",
                        TextureSamplerTypes.Texture2DShadow => "sampler2DShadow",
                        TextureSamplerTypes.TextureCubeMap => "samplerCube",
                        TextureSamplerTypes.Texture3D => "sampler3D",
                        _ => throw new NotImplementedException(),

                    })} {get.Key}" +
                        (tex.ArrayLength > 1 ? $"[{(tex.ArrayLength == 0 ? string.Empty : tex.ArrayLength.ToString())}]" : string.Empty) + ";";

                }

                else throw new Exception();

                resourceIndex++;

                final += rep;
            }


            setIndex++;
        }





        var structs = gencommentdividerline("STRUCTS");
        var structsfinal = "\n";
        foreach (var kv in structDeclarations) structsfinal += kv.Value + "\n";


        return (structsfinal + final, FinalResourceSets.Keys.ToList(), buffertypenamelookup);

    }








    private static async Task<List<ShaderError>> ValidateShader(string glslFile)
    {
        var errors = new List<ShaderError>();

        var tmp = Path.Combine(Path.GetTempPath(), $"{Path.GetFileNameWithoutExtension(glslFile)}__dummy.spv");

        var psi = new ProcessStartInfo
        {
            FileName = "glslangValidator",
            Arguments = $"-V \"{glslFile}\" -o " + tmp,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var proc = Process.Start(psi);
        string output = await proc.StandardOutput.ReadToEndAsync();
        await proc.WaitForExitAsync();

        var regex = new Regex(@"^(ERROR|WARNING):\s*(.*):(\d+):\s*(.+)$", RegexOptions.Multiline);

        foreach (Match m in regex.Matches(output))
        {
            int line = int.Parse(m.Groups[3].Value);
            string msg = m.Groups[4].Value;

            errors.Add(new ShaderError(line - 1, msg));
        }

        File.Delete(tmp);    

        return errors;
    }








    private static List<string> ShaderFilesInProgress = new();



    /// <summary>
    /// Contains the shader compiled to the target format, the generated glsl used to construct it, and the json reflection data provided by spirv-cross.
    /// </summary>
    /// <param name="Source"></param>
    /// <param name="glsl"></param>
    /// <param name="reflection"></param>
    public record struct ShaderStageResultStruct(byte[] Source, string glsl, string reflection);




    /// <summary>
    /// Returns null if failed to compile.
    /// </summary>
    /// <param name="ShaderName"></param>
    /// <param name="glsl"></param>
    /// <param name="stage"></param>
    /// <param name="format"></param>
    /// <returns></returns>
    /// <exception cref="Exception"></exception>
    /// <exception cref="NotImplementedException"></exception>
    private static async Task<ShaderStageResultStruct?> CompileIndividualStageShader(string ShaderName, string glsl, ShaderSuffix stage, ShaderFormat format)
    {

        //save glsl to disk to feed into spirv cross, optimize spirv but keep reflection info, gather reflection info, then strip reflection info

        //returns null if failed


        ShaderName += stage.ToString();




        lock (ShaderFilesInProgress)
        {
            while (true)
            {
                if (!ShaderFilesInProgress.Contains(ShaderName))
                {
                    ShaderFilesInProgress.Add(ShaderName);
                    break;
                }
            }
        }


        glsl = $"""
            # version 450 core

            ///////////////////////
            //{ShaderName}
            ///////////////////////

            """ + glsl;


        string glslFile = Path.Combine(Path.GetTempPath(), $"{ShaderName}_glsl.{(stage switch
        {
            ShaderSuffix._vert => "vert",
            ShaderSuffix._frag => "frag",
            ShaderSuffix._comp => "comp",
            _ => throw new Exception()
        })}");             //written code to disk





        string spirvFile = Path.Combine(Path.GetTempPath(), $"{ShaderName}_Spirv.spv");   //written code on disk -> spirv

        string spirvOptimizedFile = Path.Combine(Path.GetTempPath(), $"{ShaderName}_SpirvOptimized.spv");   //spirv -> optimized stripped spirv

        string spirvReflectionFile = Path.Combine(Path.GetTempPath(), $"{ShaderName}_SpirvOptimizedReflection.json");   //optimized spirv reflection


        string outputFile = Path.Combine(Path.GetTempPath(), $"{ShaderName}_Final.{(format switch
        {
            ShaderFormat.GLSL => ".glsl",
            ShaderFormat.SPIRV => ".spv",
            ShaderFormat.HLSL => ".hlsl",
            _ => throw new NotImplementedException(),
        })}");                                                            //optimized spirv -> final requested shader format



        File.WriteAllText(glslFile, glsl);



        //validation
        var errs = await ValidateShader(glslFile);


        if (errs.Count != 0)
        //if (true)
        {
            ShowShaderDebugHtml(glsl, errs);
            
            if (errs.Count != 0)  return null;
        }



        //this should NOT remove, modify or rearrange any resources even if apparently redundant within the scope of the shader - resource/attribute stripping should be done manually within the context of generation or by user


        //glsl -> spirv
        await Run("glslangValidator", $"-V \"{glslFile}\" -o \"{spirvFile}\"");




        //spirv -> optimized spirv (keeps reflection info until later)
        await Run("spirv-opt",
            $"\"{spirvFile}\" " +
            "--inline-entry-points-exhaustive " +
            "--convert-local-access-chains " +
            "--eliminate-insert-extract " +
            "--eliminate-dead-branches " +
            "--merge-blocks " +
            "--eliminate-local-multi-store " +
            "--eliminate-dead-functions " +
            $"-o \"{spirvOptimizedFile}\""
        );



        //optimized spirv -> target format (so we can get the correct offsets in reflection)


        switch (format)
        {
            case ShaderFormat.SPIRV:
                break;

            case ShaderFormat.GLSL:
                await Run("spirv-cross", $"\"{spirvOptimizedFile}\" --version 450 --output \"{outputFile}\"");
                break;

            case ShaderFormat.HLSL:
                await Run("spirv-cross", $"\"{spirvOptimizedFile}\" --hlsl --shader-model 50 --output \"{outputFile}\"");
                break;


            default:
                throw new NotImplementedException();
        }



        //target format -> back to spirv


        switch (format)
        {
            case ShaderFormat.SPIRV:
                break;

            case ShaderFormat.GLSL:
                await Run("glslangValidator", $"-V \"{outputFile}\" -o \"{spirvOptimizedFile}\"");
                break;

            case ShaderFormat.HLSL:
                await Run("dxc",
                    $"-T {stage switch
                    {
                        ShaderSuffix._vert => "vs_6_0",
                        ShaderSuffix._frag => "ps_6_0",
                        ShaderSuffix._comp => "cs_6_0",
                        _ => throw new Exception()
                    }} " +
                    "-E main " +
                    "-spirv " +
                    "-fvk-use-dx-layout " +    
                    $"\"{outputFile}\" -Fo \"{spirvOptimizedFile}\""
                );
                break;


            default:
                throw new NotImplementedException();
        }




        //reflection from the spirv
        await Run("spirv-cross", $"\"{spirvOptimizedFile}\" --reflect --output \"{spirvReflectionFile}\"");


        var reflect = await File.ReadAllTextAsync(spirvReflectionFile);


        //now strip reflection

        await Run("spirv-opt",
            $"--strip-reflect --strip-debug \"{spirvOptimizedFile}\" -o \"{spirvFile}\""
        );




        byte[] src = null;


        //spirv -> target format
        switch (format)
        {
            case ShaderFormat.SPIRV:
                src = await File.ReadAllBytesAsync(spirvFile);
                break;

            case ShaderFormat.GLSL:
                await Run("spirv-cross", $"\"{spirvFile}\" --version 450 --output \"{outputFile}\"");
                src = Encoding.UTF8.GetBytes(await File.ReadAllTextAsync(outputFile));
                break;

            case ShaderFormat.HLSL:
                await Run("spirv-cross", $"\"{spirvFile}\" --hlsl --shader-model 50 --output \"{outputFile}\"");
                src = Encoding.UTF8.GetBytes(await File.ReadAllTextAsync(outputFile));
                break;


            default:
                throw new NotImplementedException();
        }



        File.Delete(glslFile);
        File.Delete(spirvFile);
        File.Delete(outputFile);
        File.Delete(spirvReflectionFile);




        lock (ShaderFilesInProgress)
        {
            ShaderFilesInProgress.Remove(ShaderName);
        }



        return new ShaderStageResultStruct(src, glsl, reflect);
    }







    private record ShaderError(int Line, string Message);




    /// <summary>
    /// Generates and opens a temporary HTML file showing shader errors.
    /// </summary>
    /// <param name="source"></param>
    /// <param name="errors"></param>
    private static void ShowShaderDebugHtml(string source, List<ShaderError> errors)
    {
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

                // Highlight comments
                escapedLine = Regex.Replace(escapedLine, @"(//.*$)", "<span class='comment'>$1</span>", RegexOptions.Compiled);

                // Highlight GLSL types
                escapedLine = Regex.Replace(escapedLine, @"\b(bool|int|uint|float|vec[234]|ivec[234]|mat[234]|sampler[123]D|samplerCube|sampler2DShadow)\b",
                                            "<span class='type'>$1</span>", RegexOptions.Compiled);

                // Highlight GLSL keywords
                escapedLine = Regex.Replace(escapedLine, @"\b(attribute|const|uniform|buffer|readonly|writeonly|varying|layout|in|out|inout|void|if|else|for|while|return|discard|struct|switch|case|default|break|continue)\b",
                                            "<span class='keyword'>$1</span>", RegexOptions.Compiled);

                // Highlight built-in functions
                escapedLine = Regex.Replace(escapedLine, @"\b(texture|normalize|mix|dot|clamp|length|sin|cos|abs|pow|max|min|floor|ceil|fract|mod|step|smoothstep|reflect|refract|cross|distance|exp|log)\b",
                                            "<span class='builtin'>$1</span>", RegexOptions.Compiled);

                // Highlight numeric literals
                escapedLine = Regex.Replace(escapedLine, @"(?<![\w.])(\d+\.\d+|\d+)(f)?\b", "<span class='number'>$1</span>", RegexOptions.Compiled);

                string errorAttr = err != null ? $" class='error' title='{escape(err.Message)}'" : "";
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
<title>Shader Debug</title>
<style>
body {{ background: #1e1e1e; color: #d4d4d4; font-family: Consolas, monospace; margin: 0; padding: 0; }}
.console {{ background: #111; padding: 1em; color: #f55; font-weight: bold; border-bottom: 2px solid #444; overflow-x: auto; }}
.container {{ padding: 1em; overflow-y: auto; height: calc(100vh - 60px); }}
.code-block div {{ white-space: pre; position: relative; display: block; width: 100%; }}
.code-block div code {{ display: inline-block; min-width: 100%; }}
.error {{ background: rgba(255, 0, 0, 0.2); border-left: 3px solid red; padding-left: 4px; }}
.error:hover::after {{ content: attr(title); position: absolute; top: 100%; left: 0; margin-top: 4px; background: #300; color: #faa; padding: 4px 8px; border: 1px solid red; z-index: 9999; max-width: 400px; white-space: normal; box-shadow: 0 2px 6px rgba(0,0,0,0.5); }}
.comment {{ color: #6a9955; font-style: italic; }}
.keyword {{ color: #569CD6; font-weight: bold; }}
.type {{ color: #4EC9B0; }}
.builtin {{ color: #DCDCAA; }}
.number {{ color: #B5CEA8; }}
</style>
</head>
<body>

<div class='console'>
{string.Join("<br>", errors.Select(e => e.Line >= 0 ? $"line {e.Line + 1}: {escape(e.Message)}" : escape(e.Message)))}
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


}
