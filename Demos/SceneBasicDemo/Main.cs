using Engine.Core;
using Engine.GameObjects;
using Engine.GameResources;
using System.Numerics;
using static Engine.Core.EngineMath;
using static Engine.Core.References;
using System.Runtime.InteropServices;
using static Engine.Core.RenderingBackend;



#if DEBUG
using Engine.Stripped;
#endif





////////////////////////////////////////////
////////////////////////////////////////////
////////////////////////////////////////////


//
//                   .+------+
//                 .' |    .'|
//                +---+--+'  |
//                |   |  |   |
//                |  .+--+---+
//                |.'    | .'
//                +------+'
//
//

//DEMO 4
//This is the sponza demo. It showcases an advanced rendering pipeline.


////////////////////////////////////////////
////////////////////////////////////////////
////////////////////////////////////////////












public static partial class Entry
{



    // -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------



    /// <summary>
    /// <inheritdoc cref="_EngineInitSummary"/>
    /// </summary>
    public static partial EngineSettings.EngineInitSettings EngineInit() => new();




    // -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------


    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private record struct ShaderPointLightData(Vector3 Position, Vector4 Color);


    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private record struct ShaderSunLightData(Vector3 Direction, Vector4 Color);


    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private record struct ShaderViewProjectionPair(Matrix4x4 View, Matrix4x4 Projection);







    const int sunLightCap = 4;
    const int pointLightCap = 32;

    const int shadowCasterCap = 4;


    const string ScreenPresentShaderName = "screen";


    const byte MSAACount = 8;




#if DEBUG



    /// <summary>
    /// <inheritdoc cref="_InitShadersSummary"/>
    /// </summary>
    public static partial void InitShaders()
    {


        // GLOBAL
        // MODEL
        // MATERIAL
        // LIGHTING


        string[] setNames = ["GlobalResources", "ModelResources", "MaterialResources", "LightingResources"];



        
        
        ShaderCompilation.DeclareResourceSetConsistent("GlobalResources");
        ShaderCompilation.DeclareResourceSetConsistent("LightingResources");
        ShaderCompilation.DeclareResourceSetConsistent("ModelResources");



        string global =

            $$"""

            {{ShaderCompilation.GLSL.GetShaderStructDefinition<ShaderViewProjectionPair>()}}
            
            layout (set = 0) uniform GlobalUBO
            {
                {{ShaderCompilation.GLSL.GetShaderStructName<ShaderViewProjectionPair>()}} Camera;

            };
            
            """;


        string model =

            $$"""
            
            layout(set = 1) uniform ModelUBO
            {
                mat4 ModelMatrix;
            };
            
            """;


        string lighting =

            $$"""

          
            {{ShaderCompilation.GLSL.GetShaderStructDefinition<ShaderSunLightData>()}}
            {{ShaderCompilation.GLSL.GetShaderStructDefinition<ShaderPointLightData>()}}
            
            layout (set = 3) uniform LightingUBO
            {
                {{ShaderCompilation.GLSL.GetShaderStructName<ShaderViewProjectionPair>()}} ShadowMatrices[{{shadowCasterCap}}];
            
                {{ShaderCompilation.GLSL.GetShaderStructName<ShaderSunLightData>()}} SunLights[{{sunLightCap}}];
                {{ShaderCompilation.GLSL.GetShaderStructName<ShaderPointLightData>()}} PointLights[{{pointLightCap}}];
            };


            layout (set = 3) uniform sampler2DShadow ShadowTexture;


            
            """;






        ShaderCompilation.RegisterShader(

            shaderName: "pbr",

            resourceSetNames: setNames,

            vertexSource:
            $$"""

            
            {{global}}
            {{model}}
            
            layout(set = 2) uniform sampler2D AlbedoTexture;
            layout(set = 2) uniform sampler2D NormalTexture;
            layout(set = 2) uniform sampler2D RoughnessTexture;
            layout(set = 2) uniform sampler2D MetallicTexture;
            layout(set = 2) uniform sampler2D AOTexture;
            
            {{lighting}}




            in vec3 Position;
            in vec3 Normal;
            in vec3 Tangent;
            in vec2 UVMap;

            out vec3 FragPosition;
            out vec3 FragNormal;
            out vec2 FragUV;

            void main()
            {
                vec4 worldPos = ModelMatrix * vec4(Position, 1.0);

                gl_Position = Camera.Projection * Camera.View * worldPos;

                FragPosition = worldPos.xyz;

                mat3 normalMatrix = transpose(inverse(mat3(ModelMatrix)));
                FragNormal = normalize(normalMatrix * normalize(Normal));

                FragUV = UVMap;
            }

            """,

            fragmentSource:

            $$"""

            in vec3 FragPosition;
            in vec3 FragNormal;
            in vec2 FragUV;

            out vec4 FinalColor;

            {{global}}
            {{model}}

            layout(set = 2) uniform sampler2D AlbedoTexture;
            layout(set = 2) uniform sampler2D NormalTexture;
            layout(set = 2) uniform sampler2D RoughnessTexture;
            layout(set = 2) uniform sampler2D MetallicTexture;

            {{lighting}}

            void main()
            {
                vec3 albedo = pow(texture(AlbedoTexture, FragUV).rgb, vec3(2.2));

                vec3 normalSample = texture(NormalTexture, FragUV).rgb * 2.0 - 1.0;
                vec3 normal = normalize(FragNormal + normalSample * 0.5);

                float roughness = texture(RoughnessTexture, FragUV).r;
                float metallic = texture(MetallicTexture, FragUV).r;

                roughness = clamp(roughness, 0.04, 1.0);
                metallic = clamp(metallic, 0.0, 1.0);

                vec3 viewDir = normalize(inverse(Camera.View)[3].xyz - FragPosition);

                vec3 ambientLighting = albedo * 0.03;
                vec3 pointLighting = vec3(0.0);
                vec3 sunLighting = vec3(0.0);

                vec3 F0 = mix(vec3(0.04), albedo, metallic);

                for (int i = 0; i < {{pointLightCap}}; i++)
                {
                    vec3 lightPos = PointLights[i].Position;
                    vec3 lightColor = PointLights[i].Color.rgb;
                    float lightRadius = PointLights[i].Color.a;

                    vec3 toLight = lightPos - FragPosition;
                    float distance = length(toLight);

                    if (distance >= lightRadius)
                        continue;

                    vec3 lightDir = normalize(toLight);
                    vec3 halfDir = normalize(viewDir + lightDir);

                    float attenuation = 1.0 - (distance / lightRadius);
                    attenuation *= attenuation;

                    float NdotL = max(dot(normal, lightDir), 0.0);
                    float NdotV = max(dot(normal, viewDir), 0.0);
                    float NdotH = max(dot(normal, halfDir), 0.0);
                    float VdotH = max(dot(viewDir, halfDir), 0.0);

                    vec3 F = F0 + (1.0 - F0) * pow(1.0 - VdotH, 5.0);

                    float shininess = mix(128.0, 2.0, roughness);
                    float specularStrength = pow(NdotH, shininess);

                    vec3 diffuse = (1.0 - metallic) * albedo / 3.14159;
                    vec3 specular = F * specularStrength * (1.0 - roughness);

                    pointLighting += (diffuse + specular) * lightColor * NdotL * attenuation;
                }

                for (int i = 0; i < {{sunLightCap}}; i++)
                {
                    vec3 lightDirection = normalize(SunLights[i].Direction);
                    vec3 lightColor = SunLights[i].Color.rgb;

                    vec3 halfDir = normalize(viewDir + lightDirection);

                    float NdotL = max(dot(normal, lightDirection), 0.0);
                    float NdotV = max(dot(normal, viewDir), 0.0);
                    float NdotH = max(dot(normal, halfDir), 0.0);
                    float VdotH = max(dot(viewDir, halfDir), 0.0);

                    vec3 F = F0 + (1.0 - F0) * pow(1.0 - VdotH, 5.0);

                    float shininess = mix(128.0, 2.0, roughness);
                    float specularStrength = pow(NdotH, shininess);

                    vec3 diffuse = (1.0 - metallic) * albedo / 3.14159;
                    vec3 specular = F * specularStrength * (1.0 - roughness);

                    sunLighting += (diffuse + specular) * lightColor * NdotL;
                }

                float shadowFactor = 1.0;

                if ({{sunLightCap}} > 0)
                {
                    {{ShaderCompilation.GLSL.GetShaderStructName<ShaderViewProjectionPair>()}} s_mat = ShadowMatrices[0];

                    vec4 shadowClip = s_mat.Projection * s_mat.View * vec4(FragPosition, 1.0);
                    vec3 shadowNdc = shadowClip.xyz / shadowClip.w;

                    vec2 shadowUV = shadowNdc.xy * 0.5 + 0.5;
                    float shadowDepth = shadowNdc.z;

                    bool shadowBounds =
                        shadowUV.x >= 0.0 && shadowUV.x <= 1.0 &&
                        shadowUV.y >= 0.0 && shadowUV.y <= 1.0 &&
                        shadowDepth >= 0.0 && shadowDepth <= 1.0;

                    if (shadowBounds)
                    {
                        shadowFactor = textureLod(
                            ShadowTexture,
                            vec3(shadowUV, shadowDepth - 0.001),
                            0
                        );
                    }
                }

                vec3 lighting =
                    ambientLighting +
                    pointLighting +
                    (sunLighting * shadowFactor);

                vec3 color = lighting;

                color = color / (color + vec3(1.0));
                color = pow(color, vec3(1.0 / 2.2));

                FinalColor = vec4(color, 1.0);
            }

            """,

            languageHandler: ShaderCompilation.GLSL
        );





        ShaderCompilation.RegisterShader(

            shaderName: "depth",

            resourceSetNames: setNames,

            vertexSource:
            $$"""


            {{global}}
            {{model}}


            in vec3 Position;
            in vec2 UVMap;
            
            out vec3 FragPosition;


            void main()
            {
                vec4 worldPos = ModelMatrix * vec4(Position, 1.0);
                gl_Position = Camera.Projection * Camera.View * worldPos;
                FragPosition = worldPos.xyz;
            }

            """,

            fragmentSource:
            $$"""

            in vec3 FragPosition;

            out vec4 FinalColor;

            void main()
            {
                FinalColor = vec4(1.0);
            }

            """,


            languageHandler: ShaderCompilation.GLSL
        );






        ShaderCompilation.RegisterShader(

            shaderName: "decal",


            resourceSetNames: setNames,


            vertexSource:
            $$"""

            
            {{global}}
            {{model}}
            layout(set = 2) uniform sampler2D AlbedoTexture;
            {{lighting}}


            
            in vec3 Position;
            in vec3 Normal;
            in vec3 Tangent;
            in vec2 UVMap;

            out vec3 FragNormal;
            out vec2 FragUV;


            

            void main()
            {
                gl_Position = Camera.Projection * Camera.View * ModelMatrix * vec4(Position, 1.0);   
        
                mat3 normalMatrix = transpose(inverse(mat3(ModelMatrix)));
                FragNormal = normalize(normalMatrix * normalize(Normal));

                FragUV = UVMap;
            }

            """,


            fragmentSource:
            """

            in vec3 FragNormal;
            in vec2 FragUV;

            out vec4 FinalColor;

            layout(set = 2) uniform sampler2D AlbedoTexture;

            
            void main()
            {
                FinalColor = texture(AlbedoTexture, FragUV);
                FinalColor.a = length(FinalColor.xyz);
            }

            """,



            languageHandler: ShaderCompilation.GLSL
        );










        ShaderCompilation.RegisterShader(

            shaderName: ScreenPresentShaderName,

            resourceSetNames: ["Resources"],

            vertexSource:
            """
            in vec2 Position;
            in vec2 UV;
            out vec2 FragUV;

            void main()
            {
                gl_Position = vec4(Position, 0, 1.0);   
                FragUV = UV;
            }
            """,


            fragmentSource:
            $$"""

            
            in vec2 FragUV;
            out vec4 FinalColor;


            layout(set = 0) uniform sampler2DMS Texture;


            void main()
            {
                ivec2 texSize = textureSize(Texture);

                ivec2 pixelCoord = ivec2(FragUV * vec2(texSize));

                vec4 color = vec4(0.0);

                for (int i = 0; i < {{MSAACount}}; i++)
                    color += texelFetch(Texture, pixelCoord, i);

                FinalColor = color / float({{MSAACount}});
            }

            """,


            languageHandler: ShaderCompilation.GLSL
        );



    }



    /// <summary>
    /// <inheritdoc cref="_InitDebugShadersSummary"/>
    /// </summary>
    public static partial void InitDebugShaders() { }


#endif







    // -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------



    //global resources + metadata

    private static Rendering.PooledResourceSet GlobalResources;
    private static Rendering.FixedPooledBuffer GlobalBuffer;


    private static BackendResourceSetReference LightingResources;
    private static BackendBufferReference.IDataBuffer LightingBuffer;


    private static BackendResourceSetReference ScreenPresentResources;
    private static BackendBufferReference.IDataBuffer ScreenBuffer;



    //objects

    private static Camera Camera;






    /// <summary>
    /// <inheritdoc cref="_InitSummary"/>
    /// </summary>
    public static partial void Init()
    {


#if DEBUG

        // ===== ( In a rendering scenario with many variables, it can sometimes be worth enabling some or all of the flags offered by EngineDebug ) =====

        EngineDebug.DeferredCommandStackTraceStorage = true;

#endif



        GlobalResources = Rendering.PooledResourceSet.CreateFromMetadata("GlobalResources");
        GlobalBuffer = Rendering.FixedPooledBuffer.CreateDataBufferFromMetadata(GlobalResources.Metadata.Buffers["GlobalUBO"].Metadata, extraAccessFlags: ReadWriteFlags.CPUWrite);



        LightingResources = BackendResourceSetReference.CreateFromMetadata("LightingResources");
        LightingBuffer = BackendBufferReference.CreateDataBufferFromMetadata(LightingResources.Metadata.Buffers["LightingUBO"].Metadata, extraAccessFlags: ReadWriteFlags.CPUWrite);
        LightingResources.SetResource("LightingUBO", LightingBuffer);




        Task.Run(async () =>
        {


            var sceneResource = await GameResource.LoadResource<SceneResource>("Assets/Scene");

            unsafe
            {

                var sceneInstance = sceneResource.Instantiate(&Handler);


                static void Handler(GameObject obj)
                {

                    if (obj is ModelInstance mod)
                    {
                        var modelresources = BackendResourceSetReference.CreateFromMetadata("ModelResources");

                        mod.ModelInstanceResourceSets.Add("LightingResources", LightingResources);
                        mod.ModelInstanceResourceSets.Add("ModelResources", modelresources);

                        var modelBuffer = BackendBufferReference.CreateDataBufferFromMetadata(modelresources.Metadata.Buffers["ModelUBO"].Metadata, extraAccessFlags: ReadWriteFlags.CPUWrite);
                        modelresources.SetResource("ModelUBO", modelBuffer);

                        mod.OnPreDraw.Add(
                            () =>
                            {
                                modelBuffer.WriteFromOffsetOf("ModelMatrix", mod.GlobalTransform, false, true);
                            }
                            );

                    }
                }
            }
        });







        // -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

        // And then the camera and the framebuffer presentation setup.



        Camera = new Camera
        {
            Resolution = new Vector2<uint>(1920, 1080),
            GlobalPosition = new Vector3(0, 0, -10)
        };



        ScreenPresentResources = BackendResourceSetReference.CreateFromMetadata(BackendShaderReference.Get(ScreenPresentShaderName).Metadata.ResourceSets["Resources"].Metadata);


        Camera.CreateMultiSampleDynamicSizeCameraFrameBuffer("main",
                                                  Vector2.One,
                                                  1,
                                                  TextureFormats.RGBA8_UNORM,
                                                  true,
                                                  sampleCount: (MultiSampleCount)MSAACount);



        Camera.OnRefreshChange.Add(() =>
        {
            ScreenPresentResources.SetResource(

                "Texture",
                (BackendTexture2DMSAttachmentReference)Camera.GetCameraFrameBuffer("main").ColorAttachments[0]
            );
        });


        Camera.Init();

    }







    // -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

    // Finally, we can draw with more of an abstracted structured pipeline, at the object level.





    private unsafe static void ObjectLoop()
    {


        uint sunLightPush = 0;
        uint pointLightPush = 0;


        lock (GameObject.AllGameObjects)
        {
            for (int i = 0; i < GameObject.AllGameObjects.Count; i++)
            {
                var obj = GameObject.AllGameObjects[i];

                obj.Loop();



                if (obj is PointLight pLight)
                {
                    if (pointLightPush != pointLightCap)
                    {
                        LightingBuffer.WriteFromOffsetOfArrayElement("PointLights",
                                                                      pointLightPush++,
                                                                      new ShaderPointLightData() { Color = (pLight.Color * 10f) with { W = 5f }, Position = obj.GlobalPosition },
                                                                      false,
                                                                      true);
                    }
                }


                if (obj is SunLight sunLight)
                {
                 
                    if (sunLightPush != sunLightCap)
                    {
                        LightingBuffer.WriteFromOffsetOfArrayElement("SunLights",
                                                                          sunLightPush++,
                                                                          new ShaderSunLightData() { Color = sunLight.Color * 10f, Direction = sunLight.GlobalTransform.GetOrientationY() },
                                                                          false,
                                                                          true);


                        RenderWithCamera(sunLight.ShadowCam, "main", [DepthPass]);



                        LightingBuffer.WriteFromOffsetOf("ShadowMatrices", new ShaderViewProjectionPair(sunLight.ShadowCam.GetViewMatrix(), sunLight.ShadowCam.GetProjectionMatrix()), necessary: false, skipPadding: true);

                        if (!setonce)
                        {
                            LightingResources.SetResource("ShadowTexture", sunLight.ShadowTextures[0]);

                            setonce = true;
                        }
                    }
                }
            }
        }
    }


    private static bool setonce = false;




    private static void CameraMovementLogic()
    {
        var WASD = ((Vector2)Input.KeyboardInputInstance.Get().GetAxisFromFour(SDL3.SDL.Scancode.A, SDL3.SDL.Scancode.D, SDL3.SDL.Scancode.S, SDL3.SDL.Scancode.W)) / (float)short.MaxValue;


        var mouse = Input.MouseInputInstance.Get();

        if (mouse.MouseButtonPressed(SDL3.SDL.MouseButtonFlags.Right))
        {

            mouse.SetMouseRelative(true);


            var mouseMove = mouse.GetMousePositionDelta();




            var euler = Camera.GlobalTransform.GetEuler() + (new Vector3(mouseMove.Y, mouseMove.X, 0) * 0.001f * Logic.Delta);

            var rotation =
                Matrix4x4.CreateRotationX(euler.X) *
                Matrix4x4.CreateRotationY(euler.Y);

            Camera.GlobalTransform = rotation with { Translation = Camera.GlobalTransform.Translation };



            //wasd
            var move = new Vector3(WASD.X, 0, WASD.Y) * Logic.Delta * 10f;
            Camera.GlobalPosition += Vector3.TransformNormal(move, Camera.GlobalTransform);

            //up down
            Camera.GlobalPosition += new Vector3(0, (Input.KeyboardInputInstance.Get().GetAxisFromTwo(SDL3.SDL.Scancode.LShift, SDL3.SDL.Scancode.Space) / (float)short.MaxValue) * Logic.Delta * 10f, 0);
        }

        else
            mouse.SetMouseRelative(false);



    }






    private static unsafe void RenderWithCamera(Camera cam, string fbName, ReadOnlySpan<RenderingPass> passes)
    {

        GlobalResources.Advance();
        GlobalBuffer.Advance();


        var buf = (BackendBufferReference.IDataBuffer)GlobalBuffer.GetCurrent();
        buf.WriteFromOffsetOf("Camera", new ShaderViewProjectionPair(cam.GetViewMatrix(), cam.GetProjectionMatrix()), necessary: false, skipPadding: true);

        GlobalResources.GetCurrent().SetResource("GlobalUBO", buf);




        Span<FrameBufferPipelineStage> stages = stackalloc FrameBufferPipelineStage[passes.Length];
        for (int p = 0; p < passes.Length; p++) 
            stages[p] = passes[p].Stage;


        var op = Rendering.FrameBufferPipelineStateOperator.StartFrameBufferPipeline(cam.GetCameraFrameBuffer(fbName), stages);


        lock (DrawObject.AllDrawObjects)
        {
            using ArrayFromPool<Camera.DrawObjectAndDistance> whitelistWithDistances = Camera.GetDrawObjectSquaredDistances(DrawObject.AllDrawObjects, cam.GlobalPosition);
            using ArrayFromPool<Camera.DrawObjectAndDistance> sortedWhitelist = Camera.SortDrawObjects(whitelistWithDistances, Camera.CameraDrawSortMode.NearToFar);
            using ArrayFromPool<Camera.DrawObjectAndDistance> culledWhitelist = Camera.CullDrawObjectList(sortedWhitelist, cam.GetViewMatrix(), cam.GetProjectionMatrix());


            for (int i = 0; i < passes.Length; i++) 
            {
                var pass = passes[i];

                for (int x = 0; x < culledWhitelist.Length; x++)
                    culledWhitelist[x].Object.Draw(
                        new DrawObject.DrawState() 
                        { 
                            MaterialResolver = pass.MaterialResolverFnPtr, 
                            TransientResourceSets = new UnmanagedKeyValueCollection<WeakObjRef<string>, WeakObjRef<BackendResourceSetReference>>() { { "GlobalResources".GetWeakRef(), GlobalResources.GetCurrent().GetWeakRef() } }
                        }
                        );

                op.Advance();
            }
        }

    }




    private unsafe struct RenderingPass
    {
        public FrameBufferPipelineStage Stage;
        public delegate*<MaterialResource, MaterialResource.MaterialResolution> MaterialResolverFnPtr;
    }






    // -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------



    private static RenderingPass DepthPass = new RenderingPass()
    {
        Stage = new FrameBufferPipelineStage()
                        .SpecifyColorAttachments(rangeStart: 0, rangeEnd: 1, access: FrameBufferPipelineAttachmentAccessFlags.None, clear: false)
                        .SpecifyDepth(access: FrameBufferPipelineAttachmentAccessFlags.Write, clear: true),

        MaterialResolverFnPtr = &DepthPassMaterialResolve
    };


    static MaterialResource.MaterialResolution DepthPassMaterialResolve(MaterialResource mat)
    {
        if (InTransparentPipeline(mat))
            return default;

        return new MaterialResource.MaterialResolution(

            BackendShaderReference.Get("depth"),
            null,

            new DrawPipelineDetails.RasterizationDetails() { CullMode = CullMode.Disabled },
            new DrawPipelineDetails.BlendState() { Enable = false },
            new DrawPipelineDetails.DepthStencilState() { DepthWrite = true, DepthFunction = DepthOrStencilFunction.LessOrEqual }
            );
    }






    // -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------


    private static RenderingPass ColorPass = new RenderingPass()
    {
        Stage = new FrameBufferPipelineStage()
                        .SpecifyColorAttachments(rangeStart: 0, rangeEnd: 1, access: FrameBufferPipelineAttachmentAccessFlags.Write, clear: true)
                        .SpecifyDepth(access: FrameBufferPipelineAttachmentAccessFlags.Read, clear: false),

        MaterialResolverFnPtr = &ColorPassMaterialResolve
    };


    static MaterialResource.MaterialResolution ColorPassMaterialResolve(MaterialResource mat)
    {
        if (InTransparentPipeline(mat))
            return default;



        var shader = BackendShaderReference.Get((string)mat.GetParameter("shader"));
        var matresources = BackendResourceSetReference.CreateFromMetadata(shader, "MaterialResources");


        foreach (var tex in mat.GetTextures())
            if (matresources.Metadata.Textures.ContainsKey(tex.Key))
                matresources.SetResource(tex.Key, new BackendTexture2DSamplerPair((BackendTexture2DReference)mat.GetTexture(tex.Key).BackendReference, TextureWrapModes.Repeat, TextureFilters.Linear, TextureFilters.Linear, TextureFilters.Linear));


        return new MaterialResource.MaterialResolution(

            shader,
            new() 
            {
                { "MaterialResources", matresources } 
            },

            new DrawPipelineDetails.RasterizationDetails() { CullMode = CullMode.Disabled },
            new DrawPipelineDetails.BlendState() { Enable = false },
            new DrawPipelineDetails.DepthStencilState() { DepthWrite = false, DepthFunction = DepthOrStencilFunction.Equal }

            );
    }





    // -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------


    private static RenderingPass TransparentPass = new RenderingPass()
    {
        Stage = new FrameBufferPipelineStage()
                        .SpecifyColorAttachments(rangeStart: 0, rangeEnd: 1, access: FrameBufferPipelineAttachmentAccessFlags.Write, clear: false)
                        .SpecifyDepth(access: FrameBufferPipelineAttachmentAccessFlags.None, clear: false),

        MaterialResolverFnPtr = &TransparentPassMaterialResolve
    };


    static MaterialResource.MaterialResolution TransparentPassMaterialResolve(MaterialResource mat)
    {
        if (!InTransparentPipeline(mat))
            return default;


        var shader = BackendShaderReference.Get((string)mat.GetParameter("shader"));
        var matresources = BackendResourceSetReference.CreateFromMetadata(shader, "MaterialResources");



        foreach (var tex in mat.GetTextures())
            if (matresources.Metadata.Textures.ContainsKey(tex.Key))
                matresources.SetResource(tex.Key, new BackendTexture2DSamplerPair((BackendTexture2DReference)mat.GetTexture(tex.Key).BackendReference, TextureWrapModes.Repeat, TextureFilters.Linear, TextureFilters.Linear, TextureFilters.Linear));



        return new MaterialResource.MaterialResolution(

            shader,
            new()
            {
                { "MaterialResources", matresources }
            },



            new DrawPipelineDetails.RasterizationDetails() { CullMode = CullMode.Disabled },
            new DrawPipelineDetails.BlendState() { Enable = true },
            new DrawPipelineDetails.DepthStencilState() { DepthWrite = true, DepthFunction = DepthOrStencilFunction.LessOrEqual }
            );
    }




    // -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------


    static bool InTransparentPipeline(MaterialResource mat)
        => ((string)mat.GetParameter("shader")) == "decal";


    // -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------











    public static unsafe partial void Loop()
    {


        GlobalBuffer.Reset();
        GlobalResources.Reset();





#if DEBUG

        // ===== ( In a rendering scenario with many variables, it can sometimes be worth enabling some or all of the flags offered by EngineDebug ) =====

        EngineDebug.ThrowIfVertexBufferMissing =
        EngineDebug.ThrowIfResourceSetMissing =
        EngineDebug.ThrowIfResourceMissing =
        EngineDebug.DeferredCommandStackTraceStorage = false;

#endif

        ObjectLoop();

        CameraMovementLogic();






        // -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------


        Camera.Resolution = CurrentSwapchainDetails.Size;
        Camera.Refresh();

        RenderWithCamera(Camera, "main", [DepthPass, ColorPass, TransparentPass]);


        // -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------


        //And, finally, we can present to screen, like before.


        Rendering.StartDrawToScreen();

        Rendering.DrawQuad(Vector2.One, -Vector2.One, ScreenPresentResources, BackendShaderReference.Get(ScreenPresentShaderName));

#if DEBUG
        ImGUIController.BeginFrame();
        EngineDebug.DisplayBasicInspectorViaIMGUI();
        ImGUIController.EndFrame();
#endif


        Rendering.EndDrawToScreen();



        // -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

#if DEBUG

        // ===== ( We can also disable these debug flags after we're done. These only affect the current thread, so they can be safely used to create logical debugging ranges. ) =====

        EngineDebug.ThrowIfVertexBufferMissing = 
        EngineDebug.ThrowIfResourceSetMissing =
        EngineDebug.ThrowIfResourceMissing = 
        EngineDebug.DeferredCommandStackTraceStorage = false;

#endif



    }

}





