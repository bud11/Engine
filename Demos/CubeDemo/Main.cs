using Engine.Core;
using Engine.GameObjects;
using Engine.GameResources;
using System.Numerics;

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

//DEMO 2
//This is the cube demo. It shows more structured drawing with objects, cameras and materials, as well as some very basic game logic.
//The end result is a spinning cube object drawn to a camera object's framebuffer via a basic pipeline, which is then drawn to the screen.


////////////////////////////////////////////
////////////////////////////////////////////
////////////////////////////////////////////












public static unsafe partial class Entry
{



    // -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------



    /// <summary>
    /// <inheritdoc cref="_EngineInitSummary"/>
    /// </summary>
    public static partial EngineSettings.EngineInitSettings EngineInit() => new();






    // -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

    // We need a shader to draw the cube, and a shader to draw the camera's primary framebuffer texture to the screen with.




#if DEBUG

    /// <summary>
    /// <inheritdoc cref="_InitShadersSummary"/>
    /// </summary>
    public static partial void InitShaders()
    {

        // This time, we need to define resource sets in our shader.

        // Resource sets are logical groups of shader resources, such as textures and uniform buffers, which can be shared across shaders.



        // Given that resource sets are indeed often shared or used at different semantic levels, sometimes a stable layout is required.
        // This method lets you enforce that as a contract, both for the sake of clarity and to enable fetching metadata for these sets later without also needing a relative shader instance's metadata.

        ShaderCompilation.DeclareResourceSetConsistent("GlobalResources");
        ShaderCompilation.DeclareResourceSetConsistent("ModelResources");




        ShaderCompilation.RegisterShader(

            shaderName: "exampleShader",


            // Resources sets correspond nicely to sets in GLSL.
            resourceSetNames:
            [
                "GlobalResources",  //set = 0
                "ModelResources"    //set = 1
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

            out vec3 FragNormal;

            void main()
            {
                gl_Position = ProjectionMatrix * ViewMatrix * ModelMatrix * vec4(Position, 1.0);   
        
                mat3 normalMatrix = transpose(inverse(mat3(ModelMatrix)));
                FragNormal = normalize(normalMatrix * Normal);
            }

            """,


            fragmentSource:
            """

            in vec3 FragNormal;
            out vec4 FinalColor;

            void main()
            {
                FinalColor = vec4(vec3(1.0) * clamp(dot(FragNormal, normalize(vec3(1, 1, -1))), 0.2, 1), 1.0);
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

    // Now we can define a cube class.



    public partial class SpinningCube : ModelInstance
    {

        public float SpinSpeed;


        //This is a simple logic loop to make the cube spin at a framerate independent speed.

        public override void Loop()
        {
            GlobalTransform = GlobalTransform.Rotated(Vector3.UnitY, float.DegreesToRadians(SpinSpeed * Logic.Delta)) with { Translation = new(0, MathF.Sin(SpinSpeed * Logic.TimeActive * 0.03f) * 2f, 0) };

            base.Loop();
        }


        // We also need to quickly define a method to upload the object transform to the shader.
        // DrawObject.PreDraw is only ever called a maximum of once per frame, making it ideal for writes that don't need updates.

        public override void PreDraw()
        {

            // Here, we can use this simple method to check if we need to (re)upload the global model matrix.
            
            if (IsGlobalTransformDirtyForDraw())
            {
                ModelInstanceResourceSets["ModelResources"]
                    .GetResource<RenderingBackend.BackendBufferReference.IDataBuffer>("ModelUBO")
                    .WriteFromOffsetOf("ModelMatrix", GlobalTransform, nessecary: true);     //  <-----------------


                // Notice the "nessecary" flag - if a resource modification is flagged UNNESSECARY, then if the upcoming rendered frame is dropped, the modification will also be dropped.
                // For consistent every-frame updates, it should be false, to reduce overhead.
            }



            base.PreDraw();
        }
    }






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

    private static SpinningCube Cube;
    private static Camera Camera;






    /// <summary>
    /// <inheritdoc cref="_InitSummary"/>
    /// </summary>
    public static partial void Init()
    {



        // -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

        // Global resources / materials


        // We need a material to draw the cube with.
        // Materials are vague data containers designed to be interpreted at draw time, so no control is particularly lost over manual drawing, nor is any particular pipeline idea imposed.

        var material = new MaterialResource(
                                        shaderRef: new Rendering.NamedShaderReference("exampleShader"),

                                        //Arbitrary parameters and texture references can be defined and interpreted later. in this case, neither are required.
                                        parameters: null,         
                                        textures: null,

                                        //GameResources given keys are able to be registered, such that trying to load the same resource via the same key will fetch the existing one.
                                        //Typically null for code-generated resources
                                        key: null

                                        );




        

        GlobalResources = RenderingBackend.BackendResourceSetReference.CreateFromMetadata("GlobalResources");

        GlobalUniformBuffer = GlobalResources.SetResource("GlobalUBO", RenderingBackend.BackendBufferReference.CreateDataBufferFromMetadata(GlobalResources.Metadata.Buffers["GlobalUBO"].Metadata, extraAccessFlags: RenderingBackend.ReadWriteFlags.CPUWrite));






        // -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

        // Cube GameObject


        Cube = new SpinningCube();

        Cube.SpinSpeed = 45f;
        Cube.Model = MeshGeneration.GenerateCube(Vector3.One);    //<--  this creates a model resource, which is basically just a wrapper around buffers like the ones from the triangle demo. 
        Cube.Materials = [material];




        // Models have differently-scoped resource sets that can be hooked into.

        var modelresources = RenderingBackend.BackendResourceSetReference.CreateFromMetadata("ModelResources");

        modelresources.SetResource("ModelUBO", RenderingBackend.BackendBufferReference.CreateDataBufferFromMetadata(modelresources.Metadata.Buffers["ModelUBO"].Metadata, extraAccessFlags: RenderingBackend.ReadWriteFlags.CPUWrite));

        Cube.ModelInstanceResourceSets["ModelResources"] = modelresources;


        ModelInstance.GlobalModelInstanceResourceSets["GlobalResources"] = GlobalResources;







        // ! ! ! ! ! Any game objects created manually via code (aka not instanced via scene or similar) must call Init() ! ! ! ! ! 
        Cube.Init();






        // -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

        // And then the camera and the framebuffer presentation setup.





        Camera = new Camera();
        Camera.Resolution = new EngineMath.Vector2<uint>(1920, 1080);
        Camera.GlobalPosition = new Vector3(0, 0, -10);

        Camera.Init();



        //The final screen quad isn't an object in the scene and doesn't need to be treated like one.

        ScreenQuadVertPos = (RenderingBackend.BackendBufferReference.IVertexBuffer)RenderingBackend.BackendBufferReference.Create<float>([-1, -1, 1, -1, 1, 1, -1, -1, -1, 1, 1, 1], RenderingBackend.BufferUsageFlags.Vertex, default);


        ScreenQuadAttributes =
            new()
            {
                { 
                    "Position", 

                    new(
                        ScreenQuadVertPos,
                        new
                        (
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

        ScreenQuadResourceSetCollection = new() { ["TextureResources"] = screenQuadResourceSet };





        // Textures (and texture + sampler pairs) are immutable, so if the resolution changes, the old texture becomes invalid, and the set needs to be updated.

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










    // -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

    // Finally, we can draw with more of an abstracted structured pipeline, at the object level.




    public static unsafe partial void Loop()
    {



        // -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

        // First up is logic.
        // All we need to do in this case is call each object's loop, but you can do whatever you want in whatever order you want.

        lock (GameObject.AllGameObjects)
            for (int i = 0; i < GameObject.AllGameObjects.Count; i++)
                GameObject.AllGameObjects[i].Loop();





        // -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

        // Next up is rendering with the camera.



        GlobalUniformBuffer.WriteFromOffsetOf("ProjectionMatrix", Camera.GetProjectionMatrix(), nessecary: false);   // These writes can be treated as unnessecary, since we want changes every frame and caching isnt worth it.
        GlobalUniformBuffer.WriteFromOffsetOf("ViewMatrix", Camera.GetViewMatrix(), nessecary: false);


        // ---------------------------------------------------------------------------

        // Writes can also be done with less safety if speed is a bigger concern, like for example this;

        var upload = stackalloc Matrix4x4[] { Camera.GetProjectionMatrix(), Camera.GetViewMatrix() };

        GlobalUniformBuffer.WriteAndSkipPadding(new WriteRange(0, 128, upload), nessecary: false);


        // Or even this, if you're totally confident in the underlying data layout:

        GlobalUniformBuffer.Write(new WriteRange(0, 128, upload), nessecary: false);

        // ---------------------------------------------------------------------------





        // Cameras have a flexible programmable pipeline system, but all we need to do here is draw everything, so this is a simple one stage pipeline with basic material handling.


        Camera.Resolution = RenderingBackend.CurrentSwapchainDetails.Size;  //<-- Enforcing the camera's resolution is the same as the window's

        Rendering.SetScissor(default, Camera.Resolution);






#if DEBUG

        // ===== ( In a rendering scenario with many variables, it can sometimes be worth enabling some or all of the flags offered by EngineDebug ) =====

        EngineDebug.ThrowIfVertexBufferMissing =
        EngineDebug.ThrowIfResourceSetMissing =
        EngineDebug.ThrowIfResourceMissing =
        EngineDebug.DeferredCommandStackTraceStorage = true;

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
                                                       mat.MaterialResourceSets.ToUnmanagedKV());











        // -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

        //And, finally, we can present to screen, like before.


        Rendering.StartDrawToScreen();


        Rendering.SetScissor(default, RenderingBackend.CurrentSwapchainDetails.Size);


        Rendering.Draw(
            Attributes: ScreenQuadAttributes.VertexAttributesToUnmanaged(),
            ResourceSets: ScreenQuadResourceSetCollection.ToUnmanagedKV(),           
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

        // ===== ( We can disable these debug flags after we're done. These only affect the current thread, so they can be safely used to create logical debugging ranges. ) =====

        EngineDebug.ThrowIfVertexBufferMissing = 
        EngineDebug.ThrowIfResourceSetMissing =
        EngineDebug.ThrowIfResourceMissing = 
        EngineDebug.DeferredCommandStackTraceStorage = true;

#endif


    }

}





