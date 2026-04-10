

namespace Engine.Core;



///////////////////////////////////////////
///////////////////////////////////////////




using static Engine.Core.EngineMath;





public static partial class EngineSettings
{



    /// <summary>
    /// The logic frame per second target.
    /// </summary>
    public static ushort LogicRateTarget;

    /// <summary>
    /// The render frame per second target. Limited by the logic framerate and potentially capped by display settings.
    /// </summary>
    public static ushort RenderRateTarget;

    /// <summary>
    /// The vsync interval. 0 = never sync, 1 = sync every frame, 2 = sync every other frame, etc.
    /// </summary>
    public static byte VSync;

    /// <summary>
    /// Whether the swapchain should use HDR.
    /// </summary>
    public static bool HDR;




    /// <summary>
    /// Initial settings to start the engine with. Encompasses all adjustable settings. Some settings can be set later during runtime via <see cref="EngineSettings"/> and <see cref="Window"/>.
    /// </summary>
    public readonly struct EngineInitSettings()
    {

        //backend(s)

        public readonly RenderingBackend.RenderingBackendEnum RenderingBackend;


        //window

        public readonly Vector2<uint> InitialWindowSize = new(1280,720);
        public readonly Vector2<uint> InitialWindowPosition = new(64);

        public readonly bool InitialWindowFullscreen = false;
        public readonly bool InitialWindowResizeable = true;
        public readonly bool InitialWindowAlwaysOnTop = false;
        public readonly bool UseHDR = true;
        public readonly byte VSync = 1;


        public readonly string WindowTitle = "Window";


        //other

        /// <summary>
        /// <inheritdoc cref="EngineSettings.LogicRateTarget"/>
        /// </summary>
        public readonly ushort LogicRateTarget = 0;

        /// <summary>
        /// <inheritdoc cref="EngineSettings.RenderRateTarget"/>
        /// </summary>
        public readonly ushort RenderRateTarget = 0;
    }


}



///////////////////////////////////////////
///////////////////////////////////////////