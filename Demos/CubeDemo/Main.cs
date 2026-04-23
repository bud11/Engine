using Engine.Core;
using Engine.GameObjects;
using Engine.GameResources;
using System.Numerics;

using static Engine.Core.References;
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

// DEMO 2
// This is the cube demo. It shows more structured drawing with objects, cameras and materials, as well as some very basic game logic.
// The end result is a spinning cube object drawn to a camera object's framebuffer via a basic pipeline, which is then drawn to the screen.


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





#if DEBUG

    /// <summary>
    /// <inheritdoc cref="_InitShadersSummary"/>
    /// </summary>
    public static partial void InitShaders()
    {

        // -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

        // This time, we need to define resource sets in our shader.

        // Resource sets are logical groups of shader resources, such as textures and uniform buffers, which can be shared across shaders.




        // -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

        // Given that resource sets are indeed often shared or used at different semantic levels, sometimes a stable layout is required across multiple shaders.
        // This method lets you enforce that as a contract, both for the sake of clarity and to enable fetching metadata for these sets later without needing to access a specific shader instance's metadata.

        ShaderCompilation.DeclareResourceSetConsistent("GlobalResources");
        ShaderCompilation.DeclareResourceSetConsistent("ModelResources");





        // -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

        // Cube shader



        ShaderCompilation.RegisterShader(

            shaderName: "cubeShader",


            // Resource sets correspond nicely to sets in GLSL.
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





        // -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

        // We also need a small basic shader to handle camera framebuffer -> screen drawing.



        ShaderCompilation.RegisterShader(

            shaderName: "unshaded",

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
            """
            in vec2 FragUV;
            out vec4 FinalColor;

            layout (set = 0) uniform sampler2D Texture;

            void main()
            {
                FinalColor = textureLod(Texture, FragUV, 0);
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
        // DrawObject.PreDraw is only ever called a maximum of once per frame, making it ideal for this sort of thing.

        public override void PreDraw()
        {
            ModelInstanceResourceSets["ModelResources"]
                   .GetResource<BackendBufferReference.IDataBuffer>("ModelUBO")
                   .WriteFromOffsetOf("ModelMatrix", GlobalTransform, necessary: false, skipPadding: true);

            base.PreDraw();
        }
    }







    // -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

    // Global resources 

    private static BackendResourceSetReference GlobalResources;
    private static BackendBufferReference.IDataBuffer GlobalBuffer;

    private static BackendResourceSetReference ScreenPresentResources;




    // -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

    // Objects

    private static SpinningCube Cube;
    private static Camera Camera;







    /// <summary>
    /// <inheritdoc cref="_InitSummary"/>
    /// </summary>
    public static partial void Init()
    {


        // -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------
        
        // Global resources 


        // This is the resource set. Given it opted into the fixed layout contract, we can just create it by name.
        GlobalResources = BackendResourceSetReference.CreateFromMetadata("GlobalResources");


        // And this is the global buffer.
        GlobalBuffer = BackendBufferReference.CreateDataBufferFromMetadata(metadata: GlobalResources.Metadata.Buffers["GlobalUBO"].Metadata, extraAccessFlags: ReadWriteFlags.CPUWrite);
        GlobalResources.SetResource("GlobalUBO", GlobalBuffer);


        ModelInstance.GlobalModelInstanceResourceSets["GlobalResources"] = GlobalResources;





        // -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

        // Cube


        // We need a material to draw the cube with.
        // Materials are vague data containers designed to be interpreted and cached at draw time, so no control or performance is particularly lost over manual drawing, nor is any particular pipeline idea imposed.

        MaterialResource cubeMaterial = new
        (
            parameters: new() { { "shader", new Rendering.NamedShaderReference("cubeShader") } },   //<-- we can reference the shader here, as well as any other properties or resources we might like a material to have
            textures: null,
            key: null
        );


        Cube = new SpinningCube
        {
            SpinSpeed = 45f,
            Model = MeshGeneration.GenerateCube(Vector3.One),    //<--  this creates a model resource, which is basically just a wrapper around buffers like the ones from the triangle demo. 
            Materials = [cubeMaterial]
        };



        var modelresources = BackendResourceSetReference.CreateFromMetadata("ModelResources");
        modelresources.SetResource("ModelUBO", BackendBufferReference.CreateDataBufferFromMetadata(modelresources.Metadata.Buffers["ModelUBO"].Metadata, extraAccessFlags: ReadWriteFlags.CPUWrite));
        Cube.ModelInstanceResourceSets["ModelResources"] = modelresources;


        Cube.Init();






        // -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

        // And then the camera and the framebuffer presentation setup.


        Camera = new Camera
        {
            Resolution = new EngineMath.Vector2<uint>(1920, 1080),
            GlobalPosition = new Vector3(0, 0, -10)
        };


        ScreenPresentResources = BackendResourceSetReference.CreateFromMetadata(BackendShaderReference.Get("unshaded").Metadata.ResourceSets["Resources"].Metadata);


        // This creates a framebuffer owned by the camera, which in this case, will always update to have the same resolution as Camera.Resolution, given res * 1x1 = res.

        Camera.CreateDynamicSizeCameraFrameBuffer(name: "main",
                                                  scalingFactor: Vector2.One,  //<-- 1x1
                                                  colorTargetCount: 1,
                                                  colorTargetFormat: TextureFormats.RGBA8_UNORM,  //<-- simple 8 bit rgba
                                                  createDepthStencil: true,
                                                  mipCount: 1);




        // Whenever the camera refreshes and it leads to a change, we need to reobtain the framebuffer texture.

        Camera.OnRefreshChange.Add(() =>
        {
            ScreenPresentResources.SetResource(
                
                "Texture", 

                new BackendTexture2DSamplerPair
                (
                    (BackendTexture2DReference)Camera.GetCameraFrameBuffer("main").ColorAttachments[0],

                    WrapMode: TextureWrapModes.ClampToEdge,
                    MinFilter: TextureFilters.Linear,
                    MagFilter: TextureFilters.Linear,
                    MipmapFilter: TextureFilters.Linear
                )
            );
        });



        Camera.Init(); 
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



        // --------------------------------------------------------------------------

        // Enforcing the camera's resolution is the same as the window's

        Camera.Resolution = CurrentSwapchainDetails.Size;  
        Camera.Refresh();




        // --------------------------------------------------------------------------

        // Writing the view/projection matrix to the global buffer

        GlobalBuffer.WriteFromOffsetOf("ProjectionMatrix", Camera.GetProjectionMatrix(), necessary: false, skipPadding: true);
        GlobalBuffer.WriteFromOffsetOf("ViewMatrix", Camera.GetViewMatrix(), necessary: false, skipPadding: true);





        // --------------------------------------------------------------------------

        // This defines and initializes a framebuffer pipeline with the camera's framebuffer.
        // The resulting ref struct can be used to advance the specified pipeline.

        var op = Rendering.FrameBufferPipelineStateOperator.StartFrameBufferPipeline(

            framebuffer: 
            Camera.GetCameraFrameBuffer("main"),

            // In this case, we just want one single stage, wherein which we clear and then write to color and depth.

            stages:
            [
                new FrameBufferPipelineStage()
                                        .SpecifyColorAttachments(rangeStart: 0, rangeEnd: 1, access: FrameBufferPipelineAttachmentAccessFlags.Write, clear: true)
                                        .SpecifyDepth(access: FrameBufferPipelineAttachmentAccessFlags.Write, clear: true),
            ]
        );






        // --------------------------------------------------------------------------

        // Drawing objects


        // Camera exposes a few methods for sorting and culling object lists.
        // While not technically needed here, they're useful for any kind of real rendering pipeline.
        
        using ArrayFromPool<DrawObject> culledWhitelist = Camera.CullDrawObjectList(DrawObject.AllDrawObjects, Camera.GetViewMatrix(), Camera.GetProjectionMatrix());



        for (int i = 0; i < culledWhitelist.Length; i++)
        {

            // This is where materials are resolved from high level details and parameters into draw-ready resources and details.
            
            culledWhitelist[i].Draw(new DrawObject.DrawState() { MaterialResolver = &MaterialResolve });



            // Material resolutions are cached using the given static fn pointer as a key, so this is only called once.

            static MaterialResource.MaterialResolution MaterialResolve(MaterialResource mat)
            {
                return new MaterialResource.MaterialResolution(
                        ((Rendering.NamedShaderReference)mat.GetParameter("shader")).Shader,   //<-- the shader we stored earlier
                        null,
                        new DrawPipelineDetails.RasterizationDetails() { CullMode = CullMode.Back },
                        new DrawPipelineDetails.BlendState() { Enable = false },
                        new DrawPipelineDetails.DepthStencilState() { DepthWrite = true, DepthFunction = DepthOrStencilFunction.LessOrEqual }
                    );
            }
        }




        // --------------------------------------------------------------------------

        // Now we can advance the operator.
        // This will move onto the next stage, or in our case, the end, given there are no more.

        op.Advance();  






        // -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

        //And, finally, we can present to screen.


        Rendering.StartDrawToScreen();

        // This is a small helper method capable of drawing arbitrary NDC-space quads. Read the summary for more info.
        Rendering.DrawQuad(Vector2.One, -Vector2.One, ScreenPresentResources, BackendShaderReference.Get("unshaded"));

        Rendering.EndDrawToScreen();




    }

}





