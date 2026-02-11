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




public static partial class Entry
{


    private static RenderingBackend.BackendVertexBufferAllocationReference TriangleVertPos;
    private static UnmanagedKeyValueHandleCollectionOwner<string, RenderingBackend.VertexAttributeDefinitionPlusBufferClass> TriangleAttributes;

    private const string ShaderName = "TriangleShader";





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

        //First we need a shader to draw the triangle. This is a simple red NDC coordinate shader.
        //Shaders are always written as full sets of stages.
        //Shaders can only be registered in this one method - see the method summary for more info.


        ShaderCompilation.RegisterShader(

            ShaderName: ShaderName,

            ResourceSets: [],

            Attributes: new()
            {
                { "Position", new(Rendering.ShaderAttributeBufferFinalFormat.Vec2, ShaderCompilation.ShaderAttributeStageMask.VertexIn) },     //our float vector2 position input.
                { "FinalColor", new(Rendering.ShaderAttributeBufferFinalFormat.Vec4, ShaderCompilation.ShaderAttributeStageMask.FragmentOut) }   //our final fragment output.
            },

            //here we can write direct glsl method bodies referencing the declared attributes and resources.

            VertexMainBody:
                "gl_Position = vec4(Position.x, -Position.y, 0.0, 1.0);",     //notice the flipped Y coordinate to match screen space expectations.

            FragmentMainBody:
                "FragOutFinalColor = vec4(1.0, 0.0, 0.0, 1.0);"    //notice the added FragOutFinal prefix - see the documentation on ShaderAttributeStageMask for more info.
        );
    }

#endif






    /// <summary>
    /// <inheritdoc cref="_InitSummary"/>
    /// </summary>
    public static partial async Task Init()
    {

        //Next up is creating vertex buffers. here we only need one, and a collection to hold it and define its usage.

        TriangleVertPos = RenderingBackend.BackendVertexBufferAllocationReference.Create(
             [
                -0.5f, -0.5f,
                 0.5f, -0.5f,
                 0.0f,  0.5f
             ],
             writeable: false);



        //If any buffer keys are missing or dont match in the context of later draw calls, thats okay, they'll either be ignored or internally filled in with dummy buffers that meet shader specifications at draw time.

        TriangleAttributes = new()
        {
            {
                "Position",     //<-- this matches our shader's Position attribute name. 

                new(

                    TriangleVertPos,    //<-- this is the buffer we just created

                    new(         //and this is how the GPU should interpret the buffer.
                            
                        Rendering.VertexAttributeBufferComponentFormat.Float,
                        Stride: sizeof(float) * 2,
                        Offset: 0,
                        Scope: Rendering.VertexAttributeScope.PerVertex
                    )
                )

            }

        };
    }




    /// <summary>
    /// <inheritdoc cref="_LoopSummary"/>
    /// </summary>
    public static unsafe partial void Loop()
    {

        //Heres the main engine loop, which runs on the logic thread, but can push rendering commands to the render thread to consume.
        //In this case we just want to draw the triangle to screen, so all we need to do is this.


        Rendering.PushDeferredRenderThreadCommand(new StartDrawToScreenStruct());



        Rendering.PushDeferredRenderThreadCommand(new SetScissorStruct(
            default,
            RenderingBackend.CurrentSwapchainDetails.Size
        ));



        Rendering.PushDeferredRenderThreadCommand(

            new DrawStruct(

                attributeCollection: TriangleAttributes.GetUnderlyingCollection(),
                resourceSetCollection: default,             //no resource sets needed here
                shader: RenderingBackend.GetShader(ShaderName),

                //the rasterization, blending and depth stencil structs already have sane defaults that we can use here.
                rasterization: new(),
                blending: new(),
                depthStencil: default,

                indexBuffer: null,      //no index buffer needed either here
                drawRange: new(Start: 0, End: 3, BaseVertex: 0, InstanceCount: 1)
            )
        );


        Rendering.PushDeferredRenderThreadCommand(new EndDrawToScreenStruct());


    }
}
