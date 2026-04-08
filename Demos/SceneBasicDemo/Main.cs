using Engine.Core;
using Engine.GameObjects;
using Engine.GameResources;
using System.Numerics;
using ImGuiNET;
using static Engine.Core.EngineMath;
using static Engine.Core.References;
using System.Diagnostics;






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

//DEMO 3
//This is the sponza demo. It showcases dealing with resources, scenes, and more advanced rendering.


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



#if DEBUG

    /// <summary>
    /// <inheritdoc cref="_InitShadersSummary"/>
    /// </summary>
    public static partial void InitShaders()
    {

        // As shader count and complexity grows in real projects, it may be worth implementing your own mild string concatenation, reuse or generation here. 



        ShaderCompilation.DeclareResourceSetConsistent("GlobalResources");
        ShaderCompilation.DeclareResourceSetConsistent("ModelResources");



        ShaderCompilation.RegisterShader(

            shaderName: "basicPBR",


            resourceSetNames: 
            [
                "GlobalResources", 
                "ModelResources", 
                "MaterialResources"
            ],


            vertexSource:
            """

            layout(set = 0) uniform GlobalUBO   
            {
                mat4 ProjectionMatrix;
                mat4 ViewMatrix;
            };

            layout(set = 1) uniform ModelUBO
            {
                mat4 ModelMatrix;
            };



            in vec3 Position;
            in vec2 Normal;
            in vec2 UVMap;

            out vec3 FragNormal;
            out vec2 FragUV;


            
            //    --------------------------- The blender addon exports octahedrally-encoded byte-vec2 normals ---------------------------
            
            vec3 OctDecode(vec2 e)
            {
                // Convert from [0,1] range if your octahedral encode mapped to [0,1]
                vec2 f = e * 2.0 - 1.0;
            
                vec3 v = vec3(f.x, f.y, 1.0 - abs(f.x) - abs(f.y));
                float t = clamp(-v.z, 0.0, 1.0);
                v.x += v.x >= 0.0 ? -t : t;
                v.y += v.y >= 0.0 ? -t : t;
            
                return normalize(v);
            }
            


            void main()
            {
                gl_Position = ProjectionMatrix * ViewMatrix * ModelMatrix * vec4(Position, 1.0);   
        
                mat3 normalMatrix = transpose(inverse(mat3(ModelMatrix)));
                FragNormal = normalize(normalMatrix * OctDecode(Normal));

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
                // Example lighting
                float NdotL = clamp(dot(FragNormal, normalize(vec3(1.0, 1.0, -1.0))), 0.2, 1.0);
                FinalColor = vec4(vec3(1.0) * NdotL * texture(AlbedoTexture, FragUV).rgb, 1.0);
            }

            """,



            languageHandler: ShaderCompilation.GLSL
        );


        



        ShaderCompilation.RegisterShader(

            shaderName: "screenQuad",

            resourceSetNames: ["TextureResources"],

            vertexSource:
            """

            in vec2 Position;

            out vec2 FragPosition;

            void main()
            {
                FragPosition = Position;
                gl_Position = vec4(Position, 0.0, 1.0);
            }

            """,

            fragmentSource:
            """

            layout(set = 0) uniform sampler2D Texture;


            in vec2 FragPosition;
            out vec4 FinalColor;


            void main()
            {
                FinalColor = textureLod(Texture, -FragPosition * 0.5 + 0.5, 0);
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

    private static RenderingBackend.BackendResourceSetReference GlobalResources;
    private static RenderingBackend.BackendBufferReference.IDataBuffer GlobalUniformBuffer;


    //screen quad for drawing to screen

    private static RenderingBackend.BackendBufferReference.IVertexBuffer ScreenQuadVertPos;
    private static Dictionary<string, RenderingBackend.VertexAttributeDefinitionBufferPair> ScreenQuadAttributes;
    private static Dictionary<string, RenderingBackend.BackendResourceSetReference> ScreenQuadResourceSetCollection;
    private static Rendering.NamedShaderReference ScreenQuadShaderRef;


    //objects

    private static Camera Camera;






    /// <summary>
    /// <inheritdoc cref="_InitSummary"/>
    /// </summary>
    public static partial void Init()
    {



        GlobalResources = RenderingBackend.BackendResourceSetReference.CreateFromMetadata("GlobalResources");

        GlobalUniformBuffer = GlobalResources.SetResource("GlobalUBO", RenderingBackend.BackendBufferReference.CreateDataBufferFromMetadata(GlobalResources.Metadata.Buffers["GlobalUBO"].Metadata, extraAccessFlags: RenderingBackend.ReadWriteFlags.CPUWrite));





        // -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

        // Now we can load and instantiate a scene.
        // Given that we might like to keep the window responsive and rendering during startup, we can push this as a task.


        Task.Run(async () =>
        {
            var sceneResource = await Loading.LoadResource<SceneResource>("Assets/Scene");

            unsafe
            {


                // Certain objects and resources may not have enough information when loaded rather than manually created.
                // In this case, models and materials need to be configured to interface with defined shaders.

                // The best pattern in this case is to pass in a static method pointer that handles each object. That way, nothing is added into the scene tree or readied before it's prepared.


                var sceneInstance = sceneResource.Instantiate(&Handler);


                static void Handler(GameObject obj)
                {

                    if (obj is ModelInstance mod)
                    {
                        var modelresources = RenderingBackend.BackendResourceSetReference.CreateFromMetadata("ModelResources");

                        mod.ModelInstanceResourceSets.Add("GlobalResources", GlobalResources);
                        mod.ModelInstanceResourceSets.Add("ModelResources", modelresources);


                        var modelBuffer = modelresources.SetResource("ModelUBO", RenderingBackend.BackendBufferReference.CreateDataBufferFromMetadata(modelresources.Metadata.Buffers["ModelUBO"].Metadata, extraAccessFlags: RenderingBackend.ReadWriteFlags.CPUWrite));

                        mod.OnPreDraw.Add(
                            () => modelBuffer.WriteFromOffsetOf("ModelMatrix", mod.GlobalTransform)
                            );


                        foreach (var mat in mod.Materials)
                        {
                            if (mat != null && !mat.MaterialResourceSets.ContainsKey("MaterialResources"))
                            {
                                var matresources = RenderingBackend.BackendResourceSetReference.CreateFromMetadata(mat.ShaderRef.Shader, "MaterialResources");

                                mat.MaterialResourceSets.Add("MaterialResources", matresources);


                                foreach (var t in mat.Textures)
                                    if (matresources.Metadata.Textures.ContainsKey(t.Key))
                                        matresources.SetResource(t.Key, t.Value);

                            }
                        }
                    }
                }

            }

        });













        // -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

        // And then the camera and the framebuffer presentation setup.





        Camera = new Camera();
        Camera.Resolution = new Vector2<uint>(1920, 1080);
        Camera.GlobalPosition = new Vector3(0, 0, -10);

        Camera.Init();



        //The final screen quad isn't an object in the scene and doesn't need to be treated like one.


        ScreenQuadVertPos = (RenderingBackend.BackendBufferReference.IVertexBuffer) RenderingBackend.BackendBufferReference.Create<float>([-1, -1, 1, -1, 1, 1, -1, -1, -1, 1, 1, 1], RenderingBackend.BufferUsageFlags.Vertex, default);


        ScreenQuadAttributes =
            new()
            {
                { "Position", new RenderingBackend.VertexAttributeDefinitionBufferPair(
                ScreenQuadVertPos,
                new(
                    RenderingBackend.VertexAttributeBufferComponentFormat.Float,
                    sizeof(float),
                    0,
                    RenderingBackend.VertexAttributeScope.PerVertex
                    )
                    )
                }
            };




        ScreenQuadShaderRef = new Rendering.NamedShaderReference("screenQuad");


        var screenQuadResourceSet = RenderingBackend.BackendResourceSetReference.CreateFromMetadata(ScreenQuadShaderRef.Shader, "TextureResources");

        ScreenQuadResourceSetCollection = new () { ["TextureResources"] = screenQuadResourceSet };





        //Textures (and texture + sampler pairs) are immutable, so if the resolution changes, the old texture becomes invalid, and the set needs to be updated.

        Camera.OnResolutionChanged.Add(() =>
        {

            var cameraTexture = new RenderingBackend.BackendTextureAndSamplerReferencesPair(

                                        //the color buffer from the camera that we want to display on screen
                                        Camera.FrameBuffer.ColorAttachments[0],

                                        //..plus the sampler, to describe how to present it.
                                        RenderingBackend.BackendSamplerReference.Get(

                                            new RenderingBackend.SamplerDetails(
                                                WrapMode: RenderingBackend.TextureWrapModes.ClampToEdge,
                                                MinFilter: RenderingBackend.TextureFilters.Linear,
                                                MagFilter: RenderingBackend.TextureFilters.Linear,
                                                MipmapFilter: RenderingBackend.TextureFilters.Linear,
                                                EnableDepthComparison: false))

                                        );



            screenQuadResourceSet.SetResource("Texture", cameraTexture);
        });





    }




    private static void CameraMove()
    {
        var WASD = ((Vector2)Input.KeyboardInputInstance.Get().GetAxisFromFour(SDL3.SDL.Scancode.A, SDL3.SDL.Scancode.D, SDL3.SDL.Scancode.S, SDL3.SDL.Scancode.W)) / (float)short.MaxValue;


        var mouse = Input.MouseInputInstance.Get();

        if (mouse.MouseButtonPressed(SDL3.SDL.MouseButtonFlags.Right))
        {

            mouse.SetMouseRelative(true);


            var mouseMove = mouse.GetMousePositionDelta();




            var euler = Camera.GlobalTransform.GetEuler() + (new Vector3(mouseMove.Y, mouseMove.X, 0) * 0.05f * Logic.Delta);

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




    // -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

    // Finally, we can draw with more of an abstracted structured pipeline, at the object level.




    public static unsafe partial void Loop()
    {



        // -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

        // First up is logic.
        // All we need to do in this case is call each object's loop, but you can do whatever you want in whatever order you want.

        for (int i = 0; i < GameObject.AllGameObjects.Count; i++)
            GameObject.AllGameObjects[i].Loop();


        CameraMove();



        // -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

        // After that is rendering.



        GlobalUniformBuffer.WriteFromOffsetOf("ProjectionMatrix", Camera.GetProjectionMatrix());
        GlobalUniformBuffer.WriteFromOffsetOf("ViewMatrix", Camera.GetViewMatrix());





        // -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

        // Next up is rendering with the camera.
        // Cameras have a flexible programmable pipeline system, but all we need to do here is draw everything, so this is a simple one stage pipeline with basic material handling.


        Camera.Resolution = RenderingBackend.CurrentSwapchainDetails.Size;  //<-- Enforcing the camera's resolution is the same as the window's

        Rendering.SetScissor(default, Camera.Resolution);






#if DEBUG

        // ===== ( In a rendering scenario with many variables, it can sometimes be worth enabling some or all of the flags offered by EngineDebug ) =====

        EngineDebug.FreeableConstructorStackTraceStorage = 
        EngineDebug.ThrowIfVertexBufferMissing =
        EngineDebug.ThrowIfResourceSetMissing =
        EngineDebug.ThrowIfResourceMissing =
        EngineDebug.DeferredCommandStackTraceStorage = false;

#endif




        Camera.Render(
            [
                new Camera.CameraSubpassDefinition
                (
                    //All we're doing here is saying that this stage in the pipeline should clear the color and depth buffers and then write into them.
                    frameBufferPipelineStageReq: new RenderingBackend.FrameBufferPipelineStage()
                                                .SpecifyColorAttachments(rangeStart: 0, rangeEnd: 1, access: RenderingBackend.FrameBufferPipelineAttachmentAccessFlags.Write, clear: true)
                                                .SpecifyDepth(access: RenderingBackend.FrameBufferPipelineAttachmentAccessFlags.Write, clear: true),


                    ordering: Camera.CameraDrawSortMode.NearToFar,

                    objectWhiteList: DrawObject.AllDrawableObjects,

                    materialResolver: &ResolveMaterial     //<-- a pointer to a static method, which interprets certain material details, per pass, on demand, into near-final draw call details
                ),
            ]
        );



        // This is a very simple and literal resolve, but you could for example differ behavior based on the material's high level parameters or the pass this is being used for.

        static MaterialResource.MaterialResolution ResolveMaterial(MaterialResource mat)
            => new MaterialResource.MaterialResolution(mat.ShaderRef,
                                                       new RenderingBackend.DrawPipelineDetails.RasterizationDetails(),
                                                       new RenderingBackend.DrawPipelineDetails.BlendState(),
                                                       new RenderingBackend.DrawPipelineDetails.DepthStencilState(),
                                                       mat.MaterialResourceSets.DictToUnmanagedKV());








#if DEBUG

        var fb = Rendering.FrameBufferPipelineStateOperator.StartFrameBufferPipeline(Camera.FrameBuffer, [new RenderingBackend.FrameBufferPipelineStage().SpecifyColorAttachment(0, RenderingBackend.FrameBufferPipelineAttachmentAccessFlags.Write, false)]);

        ImGUIController.BeginFrame();

        EngineDebug.DisplayBasicInspectorViaIMGUI();

        ImGUIController.EndFrame();

        fb.Advance();

#endif





        // -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

        //And, finally, we can present to screen, like before.


        Rendering.StartDrawToScreen();


        Rendering.SetScissor(default, RenderingBackend.CurrentSwapchainDetails.Size);


        Rendering.Draw(
            Attributes: ScreenQuadAttributes.VertexAttributeDictToUnmanaged(),
            ResourceSets: ScreenQuadResourceSetCollection.DictToUnmanagedKV(),           
            Shader: ScreenQuadShaderRef.Shader,

            //the rasterization, blending and depth stencil structs already have sane defaults that we can use here.
            Rasterization: new(),
            Blending: new(),
            DepthStencil: default,

            IndexBuffer: null,      //no index buffer needed either here
            IndexingDetails: new(0,6,0,1)
        );


        Rendering.EndDrawToScreen();




#if DEBUG

        // ===== ( We can also disable these debug flags after we're done. These only affect the current thread, so they can be safely used to create logical debugging ranges. ) =====

        EngineDebug.FreeableConstructorStackTraceStorage =
        EngineDebug.ThrowIfVertexBufferMissing = 
        EngineDebug.ThrowIfResourceSetMissing =
        EngineDebug.ThrowIfResourceMissing = 
        EngineDebug.DeferredCommandStackTraceStorage = false;

#endif


    }

}





