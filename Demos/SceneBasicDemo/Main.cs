using Engine.Core;
using Engine.GameObjects;
using Engine.GameResources;
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

//DEMO 3
//This is the first scene demo. It borrows most of the logic from the cube demo, except we load a basic existing scene with a single model, texture and material instead.


////////////////////////////////////////////
////////////////////////////////////////////
////////////////////////////////////////////












//Same as before, we need to do some mild extension to get things working.

namespace Engine.GameObjects
{
    public partial class ModelInstance : DrawObject
    {

        protected override void PostInit()
        {
            ModelInstanceResourceSets["ModelResources"] = RenderingBackend.BackendResourceSetReference.CreateFromMetadata(Entry.exampleShader.Metadata.ResourceSets["ModelResources"].Metadata, ["ModelUBO"]);
            ModelInstanceResourceSets["GlobalResources"] = Entry.GlobalResources;

            base.PostInit();
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





//Though this time, we also need to define a PostInit for materials.
//Materials, as eluded to in the cube demo, are capable of carrying references to arbitrary arguments and resources.


namespace Engine.GameResources
{

    public partial class MaterialResource : GameResource
    {

        protected override void PostInit()
        {
            var initial = new Dictionary<string, RenderingBackend.IResourceSetResource>();

            var meta = Entry.exampleShader.Metadata.ResourceSets["MaterialResources"].Metadata;


            foreach (var kv in Arguments)  //<-- parsed earlier internally

                if (meta.Textures.ContainsKey(kv.Key))
                    initial[kv.Key] =
                        new RenderingBackend.BackendTextureAndSamplerReferencesPair(

                            ((TextureResource)kv.Value).BackendReference,

                            RenderingBackend.BackendSamplerReference.Get(
                                new RenderingBackend.SamplerDetails(Rendering.TextureWrapModes.Repeat, Rendering.TextureFilters.Linear, Rendering.TextureFilters.Linear, Rendering.TextureFilters.Linear, false))
                            );



            MaterialResourceSets["MaterialResources"] = RenderingBackend.BackendResourceSetReference.CreateFromMetadata(meta, ["MaterialUBO"], initial);
        }

    }

}














/// <summary>
/// The first scene demo.
/// </summary>
public static partial class Entry
{




    //global resources + metadata
    public static RenderingBackend.BackendResourceSetReference GlobalResources;
    public static RenderingBackend.BackendUniformBufferAllocationReference GlobalUniformBuffer;



    //objects
    private static Camera Camera;



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


        //Ths is all essentially the same as the cube demo, but this time featuring a texture on exampleShader.



        ShaderCompilation.RegisterShader(

            ShaderName: "exampleShader", 

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
                ),

                ["MaterialResources"] = new ShaderCompilation.ShaderResourceSetDefinition(
                    new OrderedDictionary<string, ShaderCompilation.IShaderResourceSetResourceDefinition>()
                    {
                        ["Texture"] = new ShaderCompilation.ShaderTextureDefinition(Rendering.TextureSamplerTypes.Texture2D)
                    }
                )
            },


            Attributes: new Dictionary<string, ShaderCompilation.ShaderAttributeDefinition>()
            {
                { "position", new(Rendering.ShaderAttributeBufferFinalFormat.Vec3, ShaderCompilation.ShaderAttributeStageMask.VertexIn) },
                { "normal", new(Rendering.ShaderAttributeBufferFinalFormat.Vec3, ShaderCompilation.ShaderAttributeStageMask.VertexIn | ShaderCompilation.ShaderAttributeStageMask.VertexOutFragmentIn) },
                { "uv", new(Rendering.ShaderAttributeBufferFinalFormat.Vec2, ShaderCompilation.ShaderAttributeStageMask.VertexIn | ShaderCompilation.ShaderAttributeStageMask.VertexOutFragmentIn) },
                { "FinalColor", new(Rendering.ShaderAttributeBufferFinalFormat.Vec4, ShaderCompilation.ShaderAttributeStageMask.FragmentOut) }
            },


            VertexMainBody:
                """

                gl_Position = GlobalUBO.ProjectionMatrix * GlobalUBO.ViewMatrix * ModelUBO.ModelMatrix * vec4(position, 1.0);   
                
                mat3 normalMatrix = transpose(inverse(mat3(ModelUBO.ModelMatrix)));
                Fragnormal = normalize(normalMatrix * normal);

                """,

            FragmentMainBody:
                "FragOutFinalColor = vec4(texture(Texture, Fraguv).rgb * clamp(dot(Fragnormal, normalize(vec3(1, 1, -1))), 0.2, 1), 1.0);"



            );



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
                { "FinalColor", new(Rendering.ShaderAttributeBufferFinalFormat.Vec4, ShaderCompilation.ShaderAttributeStageMask.FragmentOut) }  
            },

            VertexMainBody:
                "gl_Position = vec4(Position, 0.0, 1.0);",

            FragmentMainBody:
                "FragOutFinalColor = textureLod(Texture, -FragPosition*0.5+0.5, 0);" 
            );
    }



#endif




    public static RenderingBackend.BackendShaderReference exampleShader;



    // We can also define a spinning cow class.
    // This is mostly same as the spinning cube, though in this context, we're not creating it manually. Instead, the scene defines it.

    public class SpinningCow : ModelInstance
    {

        private float SpinSpeed;

        public new void Init(float spinSpeed, ModelResource Model, GameResource[] Materials = null, Dictionary<string, RenderingBackend.VertexAttributeDefinitionPlusBufferStruct> extraAttributeBuffers = null)
        {
            SpinSpeed = spinSpeed;
            base.Init(Model, Materials, extraAttributeBuffers); 
        }

        public override void Loop()
        {
            GlobalTransform = GlobalTransform.Rotated(Vector3.UnitY, float.DegreesToRadians(SpinSpeed * Logic.Delta)) with { Origin = new(0, MathF.Sin(SpinSpeed * Logic.TimeActive * 0.03f) * 1f, 0) };
            base.Loop();
        }

    }






    /// <summary>
    /// <inheritdoc cref="_InitSummary"/>
    /// </summary>
    public static partial async Task Init()
    {


        exampleShader = RenderingBackend.GetShader("exampleShader");

        GlobalResources = RenderingBackend.BackendResourceSetReference.CreateFromMetadata(exampleShader.Metadata.ResourceSets["GlobalResources"].Metadata, ["GlobalUBO"]);
        GlobalUniformBuffer = GlobalResources.GetUniformBuffer("GlobalUBO");






        //Now we can instantiate the scene.
        var scn = await Loading.LoadResource<SceneResource>("Assets/cow");    //<-- paths are relative to the Asset root directory and should never feature extensions.

        //Scene instantiation automatically calls the Init method for each object involved with arguments derived from the scene data, so this is all we need to do.
        var root = scn.Instantiate();


        //To learn a little more about how this works, start by reading the GameObject and GameResource summaries.










        /////  ============================================================== Everything else in the file from here is the same as the cube demo ==============================================================  ///////





        Camera = new();

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


        Camera.OnResolutionChanged.Add(() =>
        {

            var cameraTexture = new RenderingBackend.BackendTextureAndSamplerReferencesPair(

                                        Camera.FrameBuffer.ColorAttachments[0],

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



        Camera.Init(

            Resolution: new EngineMath.Vector2<uint>(1920, 1080),
            ColorBuffersCount: 1,
            useDepthStencilBuffer: true,
            useHDRColorBuffers: true,
            useShadowSample: false,
            useCubeMap: false

            );


        Camera.GlobalPosition = new Vector3(0, 0, -5);


    }





    /// <summary>
    /// <inheritdoc cref="_LoopSummary"/>
    /// </summary>
    public static unsafe partial void Loop()
    {




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



        Camera.Render(
            [
                new Camera.CameraSubpassDefinition
                (
                    frameBufferPipelineStageReq: new RenderingBackend.FrameBufferPipelineStage()
                                                .SpecifyColorAttachment(0, access: RenderingBackend.FrameBufferPipelineAttachmentAccessFlags.Write, clear: true)
                                                .SpecifyDepth(access: RenderingBackend.FrameBufferPipelineAttachmentAccessFlags.Write, clear: true),


                    ordering: Camera.CameraDrawSortMode.NearToFar,

                    objectWhiteList: DrawObject.AllDrawableObjects
                ),
            ]
        );





        Rendering.PushDeferredRenderThreadCommand(new StartDrawToScreenStruct());


        Rendering.PushDeferredRenderThreadCommand(new DrawStruct(ScreenQuadAttributes.GetUnderlyingCollection(),
                                                   ScreenQuadResourceSetCollection.GetUnderlyingCollection(),
                                                   ScreenQuadShader,
                                                   rasterization: new(),
                                                   blending: new(),
                                                   depthStencil: new(),
                                                   indexBuffer: null,
                                                   drawRange: new(0, 6, 0, 1)));




        Rendering.PushDeferredRenderThreadCommand(new SetScissorStruct(
            default,
            RenderingBackend.CurrentSwapchainDetails.Size
        ));




        Rendering.PushDeferredRenderThreadCommand(new EndDrawToScreenStruct());


    }

}