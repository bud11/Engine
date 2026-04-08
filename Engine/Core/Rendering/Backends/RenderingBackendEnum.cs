



namespace Engine.Core;

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
        { RenderingBackendEnum.Vulkan.ToString(), new RenderingBackendCreationInfo(SDL3.SDL.WindowFlags.Vulkan, init => new VulkanBackend(init)) }
#endif

    };


    private readonly record struct RenderingBackendCreationInfo(

        SDL3.SDL.WindowFlags Flags,        //flag(s) the backend needs the sdl window to have

        Func<nint, IRenderingBackend> Constructor   //constructor that returns an instance of the backend

        );


}
