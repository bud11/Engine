using Engine.Core;


using static Engine.Core.References;



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

    private static Rendering.NamedShaderReference ShaderRef;




#if DEBUG

    /// <summary>
    /// <inheritdoc cref="_InitShadersSummary"/>
    /// </summary>
    public static partial void InitShaders()
    {


        // This is a simple red NDC coordinate vertex + fragment shader pair written in GLSL.


        ShaderCompilation.RegisterShader(

            shaderName: ShaderName,   //<-- naming the shader allows fetching later.

            resourceSetNames: null,  //<-- we dont need this yet.


            vertexSource:

            """
            

            // ------------------------------------------------------------------------------------------------------------
            // Explicit glsl attribute locations are derived from order of appearance, and should never be specified.
            // Attributes are later referenced by name instead.


            in vec2 Position;

            void main()
            {
                gl_Position = vec4(Position.x, -Position.y, 0.0, 1.0);
            }

            """,


            fragmentSource:

            """
            
            out vec4 FinalColor;

            void main()
            {
                FinalColor = vec4(1.0, 0.0, 0.0, 1.0);
            }

            """,


            languageHandler: ShaderCompilation.GLSL    //<-- this is a class instance equipped to compile GLSL into SPIRV. Another is supplied for HLSL out of the box if that's your preference.

            );
    }






    /// <summary>
    /// <inheritdoc cref="_InitDebugShadersSummary"/>
    /// </summary>
    public static partial void InitDebugShaders() { }

#endif






    // -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

    // Next up is creating a vertex buffer, and a collection to hold/interpret it.


    private static RenderingBackend.BackendBufferReference.IVertexBuffer TriangleVertPos;
    private static Dictionary<string, RenderingBackend.VertexAttributeDefinitionBufferPair> TriangleAttributes;



    /// <summary>
    /// <inheritdoc cref="_InitSummary"/>
    /// </summary>
    public static partial void Init()
    {

        ShaderRef = new Rendering.NamedShaderReference(ShaderName);



        TriangleVertPos = (RenderingBackend.BackendBufferReference.IVertexBuffer)   //<-- created buffers can be cast to interfaces, allowing specific usages...
            RenderingBackend.BackendBufferReference.Create(
             [
                -0.5f, -0.5f,
                 0.5f, -0.5f,
                 0.0f,  0.5f
             ], RenderingBackend.BufferUsageFlags.Vertex, default);   //<-- ...given they were created with the correct corresponding usage flag




        TriangleAttributes = new()
        {
            {
                "Position",     

                new(

                    TriangleVertPos,    //<-- this is the buffer we just created

                    new(         // and this is how the GPU should interpret the buffer.
                            
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

        // In this case, we just want to draw the triangle to screen, so all we need to do is this.

        // The Rendering class is a slight abstraction over RenderingBackend, and by default handles things like command deferral, fetching certain resources from caches, etc.
        // So these commands are being deferred.


        Rendering.StartDrawToScreen();


        
        Rendering.SetScissor(new (0,0), RenderingBackend.CurrentSwapchainDetails.Size);   // The scissor state needs to be manually maintained.



        Rendering.Draw(
            Attributes: TriangleAttributes.VertexAttributesToUnmanaged(),   //<-- this converts the collection into a weak-referencing, ummanaged copy.
            ResourceSets: default,            
            Shader: ShaderRef.Shader,         //<-- our shader

            // The rasterization, blending and depth stencil structs already have sane new()s/defaults that we can use here.
            Rasterization: new(),
            Blending: new(),
            DepthStencil: default,

            //we dont need an index buffer given that this is a simple triangle.
            IndexBuffer: null,     
            IndexingDetails: new(Start: 0, End: 3, BaseVertex: 0, InstanceCount: 1)    
        );


        Rendering.EndDrawToScreen();


    }
}
