using Engine.Core;


#if DEBUG
using Engine.Stripped;
#endif



////////////////////////////////////////////
////////////////////////////////////////////
////////////////////////////////////////////


//
//                      /\
//                     /  \
//                    /____\
//
//

//DEMO 1
//This is the triangle demo. It shows basic manual drawing.
//The end result is a red triangle drawn directly to screen in NDC.


////////////////////////////////////////////
////////////////////////////////////////////
////////////////////////////////////////////






// -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

// To start, an application needs to partialy extend Entry.



public static partial class Entry
{



    // -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

    // EngineInit allows you to control the settings the engine starts with. new() gives sane defaults.


    /// <summary>
    /// <inheritdoc cref="_EngineInitSummary"/>
    /// </summary>
    public static partial EngineSettings.EngineInitSettings EngineInit() => new();





    // -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

    // We need a shader to draw the triangle. 
    // Shaders are always written as full sets of stages.
    // Shaders can only be registered in one of these two methods. See their summaries for more info.



    private const string ShaderName = "TriangleShader";



#if DEBUG

    /// <summary>
    /// <inheritdoc cref="_InitShadersSummary"/>
    /// </summary>
    public static partial void InitShaders()
    {

        //This is a simple red NDC coordinate shader.


        ShaderCompilation.RegisterShader(

            ShaderName: ShaderName,

            ResourceSets: null,

            Attributes: new()
            {
                { "Position", new ShaderCompilation.ShaderAttributeDefinition(RenderingBackend.ShaderAttributeBufferFinalFormat.Vec2, ShaderCompilation.ShaderAttributeStageMask.VertexIn) },     //our float vector2 position input.
                { "FinalColor", new ShaderCompilation.ShaderAttributeDefinition(RenderingBackend.ShaderAttributeBufferFinalFormat.Vec4, ShaderCompilation.ShaderAttributeStageMask.FragmentOut) }   //our final fragment output.
            },

            //here we can write direct glsl method bodies referencing the declared attributes and resources.

            VertexMainBody:
                "gl_Position = vec4(Position.x, -Position.y, 0.0, 1.0);",     //notice the flipped Y coordinate to match screen space expectations.

            FragmentMainBody:
                "FragOutFinalColor = vec4(1.0, 0.0, 0.0, 1.0);"    //notice the added FragOutFinal prefix - see the documentation on ShaderAttributeStageMask for more info.
        );
    }




    /// <summary>
    /// <inheritdoc cref="_InitDebugShadersSummary"/>
    /// </summary>
    public static partial void InitDebugShaders() { }

#endif






    // -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

    // Next up is creating a vertex buffer, and a collection to hold/interpret it.
    



    private static RenderingBackend.BackendVertexBufferAllocationReference TriangleVertPos;
    private static UnmanagedKeyValueHandleCollectionOwner<string, RenderingBackend.VertexAttributeDefinitionPlusBufferClass> TriangleAttributes;



    /// <summary>
    /// <inheritdoc cref="_InitSummary"/>
    /// </summary>
    public static partial void Init()
    {

        TriangleVertPos = RenderingBackend.BackendVertexBufferAllocationReference.Create(
             [
                -0.5f, -0.5f,
                 0.5f, -0.5f,
                 0.0f,  0.5f
             ],
             writeable: false);


        TriangleAttributes = new()
        {
            {
                "Position",     //<-- this matches our shader's Position attribute name. 

                new(

                    TriangleVertPos,    //<-- this is the buffer we just created

                    new(         //and this is how the GPU should interpret the buffer.
                            
                        RenderingBackend.VertexAttributeBufferComponentFormat.Float,
                        Stride: sizeof(float) * 2,
                        Offset: 0,
                        Scope: RenderingBackend.VertexAttributeScope.PerVertex
                    )
                )

            }

        };
    }



    // -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

    // This is the main engine loop, which runs on the main logic thread.
    // All threads can push rendering commands to the render thread for it to consume in the upcoming frame.



    /// <summary>
    /// <inheritdoc cref="_LoopSummary"/>
    /// </summary>
    public static unsafe partial void Loop()
    {

        //In this case, we just want to draw the triangle to screen, so all we need to do is this.

        //The Rendering class is a slight abstraction over RenderingBackend, and by default handles things like command deferral, fetching certain resources from caches, etc.
        

        Rendering.StartDrawToScreen();


        
        Rendering.SetScissor(new (0,0), RenderingBackend.CurrentSwapchainDetails.Size);   //The scissor state needs to be manually maintained.



        Rendering.Draw(
            Attributes: TriangleAttributes.GetUnderlyingCollection(),
            ResourceSets: default,             
            Shader: RenderingBackend.BackendShaderReference.Get(ShaderName),

            //the rasterization, blending and depth stencil structs already have sane new()s/defaults that we can use here.
            Rasterization: new(),
            Blending: new(),
            DepthStencil: default,

            IndexBuffer: null,     
            IndexingDetails: new(Start: 0, End: 3, BaseVertex: 0, InstanceCount: 1)
        );


        Rendering.EndDrawToScreen();


    }
}
