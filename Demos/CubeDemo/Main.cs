using Engine.Core;
using Engine.GameObjects;
using Engine.GameResources;
using System.Numerics;
using ImGuiNET;



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
        // Resource sets are logical groups of resources, such as textures and uniform buffers, which can be shared across shaders.




        ShaderCompilation.RegisterShader(

            ShaderName: "exampleShader",   //the shader name so we can get the shader later.


            // our two resource set definitions

            ResourceSets: new Dictionary<string, ShaderCompilation.ShaderResourceSetDefinition>
            {
                ["GlobalResources"] = new ShaderCompilation.ShaderResourceSetDefinition(
                    new OrderedDictionary<string, ShaderCompilation.IResourceSetResourceSetResourceDefinition>()
                    {
                        ["GlobalUBO"] = new ShaderCompilation.ShaderUniformBufferDefinition(
                            new OrderedDictionary<string, ShaderCompilation.IShaderBufferStructDeclaration>
                            {
                                ["ProjectionMatrix"] = new ShaderCompilation.ShaderBufferStructDeclaration<Matrix4x4>(),
                                ["ViewMatrix"] = new ShaderCompilation.ShaderBufferStructDeclaration<Matrix4x4>(),
                            }
                        )
                    }
                ),

                ["ModelResources"] = new ShaderCompilation.ShaderResourceSetDefinition(
                    new OrderedDictionary<string, ShaderCompilation.IResourceSetResourceSetResourceDefinition>()
                    {
                        ["ModelUBO"] = new ShaderCompilation.ShaderUniformBufferDefinition(
                            new OrderedDictionary<string, ShaderCompilation.IShaderBufferStructDeclaration>
                            {
                                ["ModelMatrix"] = new ShaderCompilation.ShaderBufferStructDeclaration<Matrix4x4>()
                            }
                        )
                    }
                )
            },


            //our attributes

            Attributes: new Dictionary<string, ShaderCompilation.ShaderAttributeDefinition>()
            {
                { "Position", new(RenderingBackend.ShaderAttributeBufferFinalFormat.Vec3, ShaderCompilation.ShaderAttributeStageMask.VertexIn) },
                { "Normal", new(RenderingBackend.ShaderAttributeBufferFinalFormat.Vec3, ShaderCompilation.ShaderAttributeStageMask.VertexIn | ShaderCompilation.ShaderAttributeStageMask.VertexOutFragmentIn) },
                { "FinalColor", new(RenderingBackend.ShaderAttributeBufferFinalFormat.Vec4, ShaderCompilation.ShaderAttributeStageMask.FragmentOut) }
            },




            //a basic hardcoded lighting setup

            VertexMainBody:
                """

                gl_Position = GlobalUBO.ProjectionMatrix * GlobalUBO.ViewMatrix * ModelUBO.ModelMatrix * vec4(Position, 1.0);   
                
                mat3 normalMatrix = transpose(inverse(mat3(ModelUBO.ModelMatrix)));
                FragNormal = normalize(normalMatrix * Normal);

                """,    

            FragmentMainBody:
                "FragOutFinalColor = vec4(vec3(1.0) * clamp(dot(FragNormal, normalize(vec3(1, 1, -1))), 0.2, 1), 1.0);"     



            );



        // And then the screen quad shader.

        ShaderCompilation.RegisterShader(

            ShaderName: "screenQuad",

            ResourceSets: new Dictionary<string, ShaderCompilation.ShaderResourceSetDefinition>
            {
                ["TextureResources"] = new ShaderCompilation.ShaderResourceSetDefinition(
                    new OrderedDictionary<string, ShaderCompilation.IResourceSetResourceSetResourceDefinition>()
                    {
                        ["Texture"] = new ShaderCompilation.ShaderTextureDefinition(RenderingBackend.TextureSamplerTypes.Texture2D)
                    }
                )
            },


            Attributes: new Dictionary<string, ShaderCompilation.ShaderAttributeDefinition>()
            {
                { "Position", new(RenderingBackend.ShaderAttributeBufferFinalFormat.Vec2, ShaderCompilation.ShaderAttributeStageMask.VertexIn | ShaderCompilation.ShaderAttributeStageMask.VertexOutFragmentIn) },
                { "FinalColor", new(RenderingBackend.ShaderAttributeBufferFinalFormat.Vec4, ShaderCompilation.ShaderAttributeStageMask.FragmentOut) }   //our final fragment output.
            },

            VertexMainBody:
                "gl_Position = vec4(Position, 0.0, 1.0);",

            FragmentMainBody:
                "FragOutFinalColor = textureLod(Texture, -FragPosition*0.5+0.5, 0);"      //using the ndc position to double as a uv + flipping on Y

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
            GlobalTransform = GlobalTransform.Rotated(Vector3.UnitY, float.DegreesToRadians(SpinSpeed * Logic.Delta)) with { Origin = new(0, MathF.Sin(SpinSpeed * Logic.TimeActive * 0.03f) * 2f, 0) };

            base.Loop();
        }


        // We also need to quickly define a method to upload the object transform to the shader.
        // DrawObject.PreDraw is only ever called a maximum of once per frame, making it ideal for writes that don't need updates.

        public override void PreDraw()
        {

            // Buffer writes operate as direct, unsafe batches.
            // Writing multiple values in one is typically more optimal than unique batches amongst other benefits.
            
            // Writing can also be done with much less safety than this if speed is a bigger concern. Read summaries for more info


            

            var buffer = ModelInstanceResourceSets["ModelResources"].GetUniformBuffer("ModelUBO");

            var writehandle = buffer.StartWrite(false);
            writehandle.PushWriteFromOffsetOf("ModelMatrix", GlobalTransform);
            writehandle.EndWrite();


            base.PreDraw();
        }
    }






    // -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------



    //global resources + metadata

    public static RenderingBackend.BackendResourceSetReference GlobalResources;
    private static RenderingBackend.BackendUniformBufferAllocationReference GlobalUniformBuffer;


    //screen quad for drawing to screen

    private static RenderingBackend.BackendVertexBufferAllocationReference ScreenQuadVertPos;
    private static UnmanagedKeyValueHandleCollectionOwner<string, RenderingBackend.VertexAttributeDefinitionPlusBufferClass> ScreenQuadAttributes;
    private static UnmanagedKeyValueHandleCollectionOwner<string, RenderingBackend.BackendResourceSetReference> ScreenQuadResourceSetCollection;
    private static RenderingBackend.BackendShaderReference ScreenQuadShader;


    //objects

    private static SpinningCube Cube;
    private static Camera Camera;






    /// <summary>
    /// <inheritdoc cref="_InitSummary"/>
    /// </summary>
    public static partial async Task Init()
    {


        // -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

        // Global resources / materials



        var exampleShader = RenderingBackend.BackendShaderReference.Get("exampleShader");


        //We need a material to draw the cube with.
        //Materials are vague data containers designed to be interpreted at draw time, so no control is particularly lost over manual drawing, nor is any particular pipeline idea imposed.

        var material = new MaterialResource(
                                        shader: exampleShader,

                                        //Arbitrary parameters and texture references can be defined and interpreted later. in this case, neither are required.
                                        parameters: null,         
                                        textures: null,

                                        //GameResources given keys are able to be registered, such that trying to load the same resource via the same key will fetch the existing one.
                                        //Typically null for code-generated resources
                                        key: null

                                        );




        
        //This is fairly self explanatory. We're actualizing GlobalResources and GlobalUBO with a method that cuts down on some boilerplate, and then holding onto it.
        //As usual, check the method summaries for more info.

        GlobalResources = RenderingBackend.BackendResourceSetReference.CreateFromMetadata(exampleShader, "GlobalResources", createInitialBuffers: ["GlobalUBO"]);

        GlobalUniformBuffer = GlobalResources.GetUniformBuffer("GlobalUBO");






        // -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

        // Cube GameObject


        Cube = new SpinningCube();

        Cube.SpinSpeed = 45f;
        Cube.Model = MeshGeneration.GenerateCube(Vector3.One);    //<--  this creates a model resource, which is basically just a wrapper around buffers like the ones from the triangle demo. 
        Cube.Materials = [material];


        Cube.ModelInstanceResourceSets["ModelResources"] = RenderingBackend.BackendResourceSetReference.CreateFromMetadata(exampleShader, "ModelResources", ["ModelUBO"]);

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


        ScreenQuadVertPos = RenderingBackend.BackendVertexBufferAllocationReference.Create<float>(initialcontent: [-1, -1, 1, -1, 1, 1, -1, -1, -1, 1, 1, 1], writeable: false);


        ScreenQuadAttributes =
            new()
            {
                { "Position", new RenderingBackend.VertexAttributeDefinitionPlusBufferClass(
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






        ScreenQuadShader = RenderingBackend.BackendShaderReference.Get("screenQuad");

        var screenQuadResourceSet = RenderingBackend.BackendResourceSetReference.CreateFromMetadata(ScreenQuadShader, "TextureResources");

        ScreenQuadResourceSetCollection = new UnmanagedKeyValueHandleCollectionOwner<string, RenderingBackend.BackendResourceSetReference>() { ["TextureResources"] = screenQuadResourceSet };





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



            var wr3 = screenQuadResourceSet.StartWrite(true);
            wr3.PushWrite("Texture", cameraTexture);
            wr3.EndWrite();
        });





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






        // -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

        // After that is rendering.



        var writehandle = GlobalUniformBuffer.StartWrite(true);

        writehandle.PushWriteFromOffsetOf("ProjectionMatrix", Camera.GetProjectionMatrix());
        writehandle.PushWriteFromOffsetOf("ViewMatrix", Camera.GetViewMatrix());

        writehandle.EndWrite();






        // -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

        // Next up is rendering with the camera.
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
            => new MaterialResource.MaterialResolution(mat.Shader,
                                                       new RenderingBackend.DrawPipelineDetails.RasterizationDetails(),
                                                       new RenderingBackend.DrawPipelineDetails.BlendState(),
                                                       new RenderingBackend.DrawPipelineDetails.DepthStencilState(),
                                                       mat.MaterialResourceSets.GetUnderlyingCollection());











        // -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

        //And, finally, we can present to screen, like before.


        Rendering.StartDrawToScreen();


        Rendering.SetScissor(default, RenderingBackend.CurrentSwapchainDetails.Size);


        Rendering.Draw(
            Attributes: ScreenQuadAttributes.GetUnderlyingCollection(),
            ResourceSets: ScreenQuadResourceSetCollection.GetUnderlyingCollection(),           
            Shader: ScreenQuadShader,

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
        EngineDebug.DeferredCommandStackTraceStorage = false;

#endif


    }

}





