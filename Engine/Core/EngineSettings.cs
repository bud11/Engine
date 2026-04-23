

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
    /// Initial settings to start the engine with. Encompasses all adjustable settings in addition to settings that require being set on startup and/or as process environment variables.
    /// <br/> Adjustable settings can be set at runtime via <see cref="EngineSettings"/> and <see cref="Window"/>.
    /// </summary>
    public struct EngineInitSettings()
    {

        //backend/system

        public RenderingBackend.RenderingBackendEnum RenderingBackend;
        public ulong VRAMMemoryLimit = 0;



        //window

        public Vector2<uint> InitialWindowSize = new(1280,720);
        public Vector2<uint> InitialWindowPosition = new(64);

        public bool InitialWindowFullscreen = false;
        public bool InitialWindowResizeable = true;
        public bool InitialWindowAlwaysOnTop = false;
        public bool InitialHDR = true;
        public byte InitialVSync = 0;


        public string InitialWindowTitle = "Window";





        //other

        /// <summary>
        /// <inheritdoc cref="LogicRateTarget"/>
        /// </summary>
        public ushort InitialLogicRateTarget = 0;

        /// <summary>
        /// <inheritdoc cref="RenderRateTarget"/>
        /// </summary>
        public ushort InitialRenderRateTarget = 0;
    }


}



///////////////////////////////////////////
///////////////////////////////////////////