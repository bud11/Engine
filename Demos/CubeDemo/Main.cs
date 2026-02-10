using Engine.Attributes;
using Engine.Core;
using Engine.GameObjects;
using Engine.GameResources;

using System.Diagnostics;
using System.Numerics;

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
//The end result is a custom spinning cube object drawn to a camera object's framebuffer via a basic pipeline, which is then drawn to the screen.


////////////////////////////////////////////
////////////////////////////////////////////
////////////////////////////////////////////











public static unsafe partial class Entry
{




    //global resources + metadata

    public static RenderingBackend.BackendResourceSetReference GlobalResources;
    private static RenderingBackend.BackendUniformBufferAllocationReference GlobalUniformBuffer;



    //objects

    private static SpinningCube Cube;
    private static Camera Camera;



    //screen quad for drawing to screen

    private static RenderingBackend.BackendVertexBufferAllocationReference ScreenQuadVertPos;
    private static UnmanagedKeyValueHandleCollectionOwner<string, RenderingBackend.VertexAttributeDefinitionPlusBufferClass> ScreenQuadAttributes;
    private static UnmanagedKeyValueHandleCollectionOwner<string, RenderingBackend.BackendResourceSetReference> ScreenQuadResourceSetCollection;
    private static RenderingBackend.BackendShaderReference ScreenQuadShader;






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

        //This time, we need to define resource sets in our shader, which're named collections of named resources that shaders can consume and share.


        ShaderCompilation.RegisterShader(

            ShaderName: "exampleShader",   //the shader name so we can get the shader later.


            //our two resource sets.

            ResourceSets: new Dictionary<string, ShaderCompilation.ShaderResourceSetDefinition>
            {
                ["GlobalResources"] = new ShaderCompilation.ShaderResourceSetDefinition(
                    new OrderedDictionary<string, ShaderCompilation.IShaderResourceSetResourceDefinition>()
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
                    new OrderedDictionary<string, ShaderCompilation.IShaderResourceSetResourceDefinition>()
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


            Attributes: new Dictionary<string, ShaderCompilation.ShaderAttributeDefinition>()
            {
                { "Position", new(Rendering.ShaderAttributeBufferFinalFormat.Vec3, ShaderCompilation.ShaderAttributeStageMask.VertexIn) },
                { "Normal", new(Rendering.ShaderAttributeBufferFinalFormat.Vec3, ShaderCompilation.ShaderAttributeStageMask.VertexIn | ShaderCompilation.ShaderAttributeStageMask.VertexOutFragmentIn) },
                { "FinalColor", new(Rendering.ShaderAttributeBufferFinalFormat.Vec4, ShaderCompilation.ShaderAttributeStageMask.FragmentOut) }
            },




            //a basic hardcoded lighting setup

            VertexMainBody:
                """

                gl_Position = GlobalUBO.ProjectionMatrix * GlobalUBO.ViewMatrix * ModelUBO.ModelMatrix * vec4(Position, 1.0);   
                
                mat3 normalMatrix = transpose(inverse(mat3(ModelUBO.ModelMatrix)));
                FragNormal = normalize(normalMatrix * Normal);

                """,    

            FragmentMainBody:
                "FragOutFinalColor = vec4(vec3(1.0) * clamp(dot(FragNormal, normalize(vec3(-1, 1, -1))), 0.2, 1), 1.0);"     



            );








        //we also need a screen quad shader this time, so we can draw the camera object's framebuffer texture to the screen.

        ShaderCompilation.RegisterShader(

            ShaderName: "screenQuad",

            ResourceSets: new Dictionary<string, ShaderCompilation.ShaderResourceSetDefinition>
            {
                ["TextureResources"] = new ShaderCompilation.ShaderResourceSetDefinition(
                    new OrderedDictionary<string, ShaderCompilation.IShaderResourceSetResourceDefinition>()
                    {
                        ["Texture"] = new ShaderCompilation.ShaderTextureDefinition(Rendering.TextureSamplerTypes.Texture2D)
                    }
                )
            },


            Attributes: new Dictionary<string, ShaderCompilation.ShaderAttributeDefinition>()
            {
                { "Position", new(Rendering.ShaderAttributeBufferFinalFormat.Vec2, ShaderCompilation.ShaderAttributeStageMask.VertexIn | ShaderCompilation.ShaderAttributeStageMask.VertexOutFragmentIn) },
                { "FinalColor", new(Rendering.ShaderAttributeBufferFinalFormat.Vec4, ShaderCompilation.ShaderAttributeStageMask.FragmentOut) }   //our final fragment output.
            },

            VertexMainBody:
                "gl_Position = vec4(Position, 0.0, 1.0);",

            FragmentMainBody:
                "FragOutFinalColor = textureLod(Texture, FragPosition*0.5+0.5, 0);"      //using the ndc position to double as a uv 

            );


    }

#endif





    //Now lets define a cube class.

    public class SpinningCube : ModelInstance
    {

        private float SpinSpeed;



        //Each object needs to call base.Init to function properly.

        //This is an example of a typical GameObject Init implementation. 

        //This isn't virtual or abstract in the interest of allowing you to shape and adjust argument requirements as they head downstream. That does mean you need to be careful and make sure to call the correct base method at the end though.
        //This also doesn't technically need to be named Init, but it keeps things consistent.

        //The added GameObjectInitMethod attribute also isn't nessecary in this particular case, but it enables this object type to be scene instantiated smoothly.


        
        public new void Init(float spinSpeed, ModelResource model, GameResource[] materials = null, Dictionary<string, RenderingBackend.VertexAttributeDefinitionPlusBufferStruct> extraAttributeBuffers = null)
        {
            SpinSpeed = spinSpeed;

            base.Init(model, materials, extraAttributeBuffers);  //  <----  and here we're calling the original ModelInstance Init method.
        }



        //This is a simple logic loop to make the cube spin at a framerate independent speed according to what was supplied from Init.
        public override void Loop()
        {
            GlobalTransform = GlobalTransform.Rotated(Vector3.UnitY, float.DegreesToRadians(SpinSpeed * Logic.Delta)) with { Origin = new(0, MathF.Sin(SpinSpeed * Logic.TimeActive * 0.03f) * 2f, 0) };

            base.Loop();
        }

    }










    public static RenderingBackend.BackendShaderReference exampleShader;



    /// <summary>
    /// <inheritdoc cref="_InitSummary"/>
    /// </summary>
    public static partial async Task Init()
    {

        exampleShader = RenderingBackend.GetShader("exampleShader");



        //We need a material to draw the cube with.
        //Materials are essentially only a wrapper around the combined idea of fixed state draw calls and shader resources, so no control is particularly lost over manual drawing nor is any particular pipeline idea imposed, a point emphasised later.



        var material = new MaterialResource(

            //resources
            shader: exampleShader,

            //fixed pipeline state. these structs already have sensible defaults and can be left as is here.
            rasterization: new(),
            blending: new(),
            depthStencil: new(),

            //this would be a dictionary which can encapsulate higher level material hints as well as references to relevant resources like textures, but we dont need it yet.
            arguments: null,

            //an optional gameresource key. typically null for code-initialized resources.
            key: null

            );




        
        //This is fairly self explanatory. We're actualizing GlobalResources as defined earlier, and having this method also create and assign GlobalUBO to it to reduce boilerplate, which we can then read back to assign the field.
        //As usual, check the method summaries for more info.

        GlobalResources = RenderingBackend.BackendResourceSetReference.CreateFromMetadata(exampleShader.Metadata.ResourceSets["GlobalResources"].Metadata, createInitialBuffers: ["GlobalUBO"]);

        GlobalUniformBuffer = GlobalResources.GetUniformBuffer("GlobalUBO");






        //Any game objects created manually via code (aka not instanced via a scene system) must call Init to recieve arguments and enter collections.

        Cube = new SpinningCube();
        Cube.Init(
            spinSpeed: 45f,
            model: MeshGeneration.GenerateCube(Vector3.One),    //<--  this creates a model resource, which is basically just a wrapper around buffers like the ones from the triangle demo. 
            materials: [material]);



        Camera = new();












        //Now we can move onto making the camera and screen quad.
        //The final screen quad isn't an object in the scene and doesn't need to be treated like one.


        ScreenQuadVertPos = RenderingBackend.BackendVertexBufferAllocationReference.Create<float>(initialcontent: [-1, -1, 1, -1, 1, 1, -1, -1, -1, 1, 1, 1], writeable: false);


        ScreenQuadAttributes =
            new()
            {
                { "Position", new RenderingBackend.VertexAttributeDefinitionPlusBufferClass(
                ScreenQuadVertPos,
                new(
                    Rendering.VertexAttributeBufferComponentFormat.Float,
                    sizeof(float),
                    0,
                    Rendering.VertexAttributeScope.PerVertex
                    )
                    )
                }
            };






        ScreenQuadShader = RenderingBackend.GetShader("screenQuad");

        var texresourcesetmeta = ScreenQuadShader.Metadata.ResourceSets["TextureResources"].Metadata;
        var screenQuadResourceSet = RenderingBackend.BackendResourceSetReference.CreateFromMetadata(texresourcesetmeta);

        ScreenQuadResourceSetCollection = new() { ["TextureResources"] = screenQuadResourceSet };



        //Textures (and texture + sampler pairs) are immutable, so if the resolution changes, the old texture just becomes invalid, and the set needs to be updated.

        Camera.OnResolutionChanged.Add(() =>
        {

            var cameraTexture = new RenderingBackend.BackendTextureAndSamplerReferencesPair(

                                        //the color buffer from the camera that we want to display on screen
                                        Camera.FrameBuffer.ColorAttachments[0],

                                        //..plus the sampler, to describe how to present it.
                                        RenderingBackend.BackendSamplerReference.Get(

                                            new RenderingBackend.SamplerDetails(
                                                WrapMode: Rendering.TextureWrapModes.ClampToEdge,
                                                MinFilter: Rendering.TextureFilters.Linear,
                                                MagFilter: Rendering.TextureFilters.Linear,
                                                MipmapFilter: Rendering.TextureFilters.Linear,
                                                EnableDepthComparison: false))

                                        );



            var wr3 = screenQuadResourceSet.StartWrite(true);
            wr3.PushWrite("Texture", cameraTexture);
            wr3.EndWrite();
        });




        //doing the above resolution changed hook BEFORE calling init ensures it'll be used during init as well.

        Camera.Init(

            Resolution: new EngineMath.Vector2<uint>(1920, 1080),
            ColorBuffersCount: 1,
            useDepthStencilBuffer: true,
            useHDRColorBuffers: true,
            useShadowSample: false,
            useCubeMap: false

            );




        Camera.GlobalPosition = new Vector3(0, 0, -10);



    }












    public static unsafe partial void Loop()
    {


        //First up is logic.
        //All we need to do in this case is call each object's loop, but you can do whatever you want in whatever order you want.

        for (int i = 0; i < GameObject.AllGameObjects.Count; i++)
            GameObject.AllGameObjects[i].Loop();





        Camera.Resolution = RenderingBackend.CurrentSwapchainDetails.Size;





        var writehandle = GlobalUniformBuffer.StartWrite(true);

        writehandle.PushWriteFromOffsetOf("ProjectionMatrix", Camera.GetProjectionMatrix());
        writehandle.PushWriteFromOffsetOf("ViewMatrix", Camera.GetViewMatrix());

        writehandle.EndWrite();




        Rendering.PushDeferredRenderThreadCommand(new SetScissorStruct(
            default,
            Camera.Resolution
        ));



        //Next up is rendering with the camera.
        //Cameras have a flexible programmable pipeline system, but all we need to do here is draw everything, so this is a simple one stage pipeline.

        Camera.Render(
            [
                new Camera.CameraSubpassDefinition
                (
                    //all we're doing here is saying that this stage in the pipeline should clear the color and depth buffers and then write into them.
                    frameBufferPipelineStageReq: new RenderingBackend.FrameBufferPipelineStage()
                                                .SpecifyColorAttachments(rangeStart: 0, rangeEnd: 1, access: RenderingBackend.FrameBufferPipelineAttachmentAccessFlags.Write, clear: true)
                                                .SpecifyDepth(access: RenderingBackend.FrameBufferPipelineAttachmentAccessFlags.Write, clear: true),


                    ordering: Camera.CameraDrawSortMode.NearToFar,

                    objectWhiteList: DrawObject.AllDrawableObjects
                ),
            ]
        );






        //And finally we draw our screen quad with the camera framebuffer similar to before.


        Rendering.PushDeferredRenderThreadCommand(new StartDrawToScreenStruct());


        Rendering.PushDeferredRenderThreadCommand(new SetScissorStruct(
            default,
            RenderingBackend.CurrentSwapchainDetails.Size
        ));

        Rendering.PushDeferredRenderThreadCommand(new DrawStruct(ScreenQuadAttributes.GetUnderlyingCollection(),
                                                   ScreenQuadResourceSetCollection.GetUnderlyingCollection(),
                                                   ScreenQuadShader,
                                                   rasterization: new(),
                                                   blending: new(),
                                                   depthStencil: new(),
                                                   indexBuffer: null,
                                                   drawRange: new(0, 6, 0, 1)));




        Rendering.PushDeferredRenderThreadCommand(new EndDrawToScreenStruct());


    }

}






//Most default objects and resources are set up to be ready for partial extension.
//Many partial implementations are optional thanks to reactive source generation.


namespace Engine.GameObjects
{
    public partial class ModelInstance : DrawObject
    {

        protected override void FinalInit()
        {
            ModelInstanceResourceSets["ModelResources"] = RenderingBackend.BackendResourceSetReference.CreateFromMetadata(Entry.exampleShader.Metadata.ResourceSets["ModelResources"].Metadata, ["ModelUBO"]);
            ModelInstanceResourceSets["GlobalResources"] = Entry.GlobalResources;

            base.FinalInit();
        }

        public override void PreDraw()
        {

            var buffer = ModelInstanceResourceSets["ModelResources"].GetUniformBuffer("ModelUBO");

            var writehandle = buffer.StartWrite(false);
            writehandle.PushWriteFromOffsetOf("ModelMatrix", GlobalTransform);
            writehandle.EndWrite();


            base.PreDraw();
        }

    }
}


