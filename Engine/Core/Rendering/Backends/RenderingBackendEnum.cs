



namespace Engine.Core;


using static Rendering;


public static partial class RenderingBackend
{

    //Add rendering backends here. Enum name strings are used to prevent indexing issues.



    public enum RenderingBackendEnum
    {
#if IncludeVulkanBackend
        Vulkan
#endif
    }

    private static readonly Dictionary<string, RenderingBackendCreationInfo> RenderingBackendData = new()
    {

#if IncludeVulkanBackend
        { RenderingBackendEnum.Vulkan.ToString(), new RenderingBackendCreationInfo(SDL3.SDL.WindowFlags.Vulkan, ShaderFormat.SPIRV, init => new VulkanBackend(init)) }
#endif

    };


    private readonly record struct RenderingBackendCreationInfo(

        SDL3.SDL.WindowFlags Flags,        //flag(s) the backend needs the sdl window to have

        ShaderFormat RequiredShaderFormat,   //the shader format the backend needs to consume (this may be replaced with a delegate of some form in future for more control over how shaders are compiled per backend)

        Func<nint, IRenderingBackend> Constructor   //constructor that returns an instance of the backend

        );


}
