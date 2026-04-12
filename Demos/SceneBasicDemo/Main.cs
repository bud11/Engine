using Engine.Core;
using Engine.GameObjects;
using Engine.GameResources;
using System.Numerics;
using static Engine.Core.EngineMath;
using static Engine.Core.References;
using System.Drawing;
using System.Runtime;








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
            in vec3 Normal;
            in vec3 Tangent;
            in vec2 UVMap;

            out vec3 FragNormal;
            out vec2 FragUV;


            

            void main()
            {
                gl_Position = ProjectionMatrix * ViewMatrix * ModelMatrix * vec4(Position, 1.0);   
        
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
                // Example lighting
                float NdotL = clamp(dot(FragNormal, normalize(vec3(1.0, 1.0, -1.0))), 0.2, 1.0);
                FinalColor = vec4(vec3(1.0) * NdotL, 1.0) * texture(AlbedoTexture, FragUV);

            }

            """,



            languageHandler: ShaderCompilation.GLSL
        );



        Rendering.InitQuadDrawDefaultShader();


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


    private static RenderingBackend.BackendTextureAndSamplerReferencesPair Screen;



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


            var sceneResource = await GameResource.LoadResource<SceneResource>("Assets/Scene");

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
                            () =>
                            {
                                if (mod.IsGlobalTransformDirtyForDraw())
                                    modelBuffer.WriteFromOffsetOf("ModelMatrix", mod.GlobalTransform, true);
                            }
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




        Camera.OnResolutionChanged.Add(() =>
        {
            Screen = new RenderingBackend.BackendTextureAndSamplerReferencesPair(

                                        Camera.FrameBuffer.ColorAttachments[0],

                                        RenderingBackend.BackendSamplerReference.Get(

                                            new RenderingBackend.SamplerDetails(
                                                WrapMode: RenderingBackend.TextureWrapModes.ClampToEdge,
                                                MinFilter: RenderingBackend.TextureFilters.Linear,
                                                MagFilter: RenderingBackend.TextureFilters.Linear,
                                                MipmapFilter: RenderingBackend.TextureFilters.Linear,
                                                EnableDepthComparison: false))

                                        );



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




    // -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

    // Finally, we can draw with more of an abstracted structured pipeline, at the object level.




    public static unsafe partial void Loop()
    {


        // -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

        lock (GameObject.AllGameObjects)
            for (int i = 0; i < GameObject.AllGameObjects.Count; i++)
                GameObject.AllGameObjects[i].Loop();


        CameraMove();


        // -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------



        GlobalUniformBuffer.WriteFromOffsetOf("ProjectionMatrix", Camera.GetProjectionMatrix(), false);
        GlobalUniformBuffer.WriteFromOffsetOf("ViewMatrix", Camera.GetViewMatrix(), false);



        Camera.Resolution = RenderingBackend.CurrentSwapchainDetails.Size;

        Rendering.SetScissor(default, Camera.Resolution);



#if DEBUG

        // ===== ( In a rendering scenario with many variables, it can sometimes be worth enabling some or all of the flags offered by EngineDebug ) =====

        EngineDebug.ThrowIfVertexBufferMissing =
        EngineDebug.ThrowIfResourceSetMissing =
        EngineDebug.ThrowIfResourceMissing =
        EngineDebug.DeferredCommandStackTraceStorage = false;

#endif




        // -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

        // This time, the drawing pipeline is more advanced, with multiple stages.



        Camera.Render(
            [

                
                // ------------ DEPTH PRE-PASS ------------

                new Camera.CameraSubpassDefinition
                (
                    frameBufferPipelineStageReq: new RenderingBackend.FrameBufferPipelineStage()
                                                .SpecifyColorAttachments(rangeStart: 0, rangeEnd: 1, access: RenderingBackend.FrameBufferPipelineAttachmentAccessFlags.None, clear: false)
                                                .SpecifyDepth(access: RenderingBackend.FrameBufferPipelineAttachmentAccessFlags.Write, clear: true),

                    ordering: Camera.CameraDrawSortMode.NearToFar,
                    objectWhiteList: DrawObject.AllDrawableObjects,

                    materialResolver: &DepthResolve

                ),

                
                // ------------ FORWARD COLOR PASS ------------

                new Camera.CameraSubpassDefinition
                (
                    frameBufferPipelineStageReq: new RenderingBackend.FrameBufferPipelineStage()
                                                .SpecifyColorAttachments(rangeStart: 0, rangeEnd: 1, access: RenderingBackend.FrameBufferPipelineAttachmentAccessFlags.Write, clear: true)
                                                .SpecifyDepth(access: RenderingBackend.FrameBufferPipelineAttachmentAccessFlags.Read, clear: false),

                    ordering: Camera.CameraDrawSortMode.NearToFar,
                    objectWhiteList: DrawObject.AllDrawableObjects,

                    materialResolver: &ColorResolve

                ),



#if DEBUG

                // ------------ DEBUG VIS ------------ 

                new Camera.CameraSubpassDefinition
                (
                    frameBufferPipelineStageReq: new RenderingBackend.FrameBufferPipelineStage()
                                                .SpecifyColorAttachments(rangeStart: 0, rangeEnd: 1, access: RenderingBackend.FrameBufferPipelineAttachmentAccessFlags.Write, clear: false)
                                                .SpecifyDepth(access: RenderingBackend.FrameBufferPipelineAttachmentAccessFlags.Read, clear: false),

                    ordering: Camera.CameraDrawSortMode.NearToFar,
                    objectWhiteList: DrawObject.AllDrawableObjects,


                    // EngineDebug can be used to draw things like bounding boxes and simple shapes.
                    // This is a simple loop, that, instead of drawing actual objects, draws their bounds if enabled.

                    drawCallIssuer: &EngineDebug.DebugVisualizationDrawMethod,  

                    materialResolver: default   
                ),
#endif

            ]
        );


        static MaterialResource.MaterialResolution DepthResolve(MaterialResource mat)
            => new MaterialResource.MaterialResolution(mat.ShaderRef,
                                                       new RenderingBackend.DrawPipelineDetails.RasterizationDetails() { CullMode = RenderingBackend.CullMode.Disabled },
                                                       new RenderingBackend.DrawPipelineDetails.BlendState() { Enable = false },
                                                       new RenderingBackend.DrawPipelineDetails.DepthStencilState() { DepthFunction = RenderingBackend.DepthOrStencilFunction.LessOrEqual, DepthWrite = true },
                                                       mat.MaterialResourceSets.ToUnmanagedKV());



        static MaterialResource.MaterialResolution ColorResolve(MaterialResource mat)
            => new MaterialResource.MaterialResolution(mat.ShaderRef,
                                                       new RenderingBackend.DrawPipelineDetails.RasterizationDetails() { CullMode = RenderingBackend.CullMode.Disabled },
                                                       new RenderingBackend.DrawPipelineDetails.BlendState() { Enable = true },
                                                       new RenderingBackend.DrawPipelineDetails.DepthStencilState() { DepthFunction = RenderingBackend.DepthOrStencilFunction.Equal, DepthWrite = false },
                                                       mat.MaterialResourceSets.ToUnmanagedKV());






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

        Rendering.DrawQuad(Vector2.One, -Vector2.One, Screen);

        Rendering.EndDrawToScreen();




#if DEBUG

        // ===== ( We can also disable these debug flags after we're done. These only affect the current thread, so they can be safely used to create logical debugging ranges. ) =====

        EngineDebug.ThrowIfVertexBufferMissing = 
        EngineDebug.ThrowIfResourceSetMissing =
        EngineDebug.ThrowIfResourceMissing = 
        EngineDebug.DeferredCommandStackTraceStorage = false;

#endif




    }

}





