

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
        public readonly ushort LogicRateTarget = 120;

        /// <summary>
        /// <inheritdoc cref="EngineSettings.RenderRateTarget"/>
        /// </summary>
        public readonly ushort RenderRateTarget = 120;
    }






    public static readonly string ReleaseRootAssetArchivePath =

#if ENGINE_BUILD_PASS && IS_PUBLISH
        Path.Combine(Directory.GetCurrentDirectory(), "publish");
#else
        Directory.GetCurrentDirectory();
#endif






#if DEBUG



    /// <summary>
    /// The path to the base asset folder.
    /// <br/> Each immediate sub folder contained within will be compressed into its own separate archive. For example, if this directory contains two folders, One and Two, then release builds will feature two archives respectively named One and Two.
    /// <br/> <b>This directory cannot directly contain assets.</b>
    /// </summary>
    public static readonly string RootAssetDirectoryPath = Path.GetFullPath("../../../../Assets");


    /// <summary>
    /// The directory used to cache final asset data that has undergone load-time conversion.
    /// </summary>
    public static readonly string AssetCachePath = Path.GetFullPath("../../../../AssetCache");




    /// <summary>
    /// The path pointing to Nvidia Texture Tools 3's nvtt_export.exe.
    /// </summary>
    public const string NVTT3Path = "C:\\Program Files\\NVIDIA Corporation\\NVIDIA Texture Tools\\nvtt_export.exe";




    /// <summary>
    /// The quality used to compress the asset directory. Ranges from 1 to 22. Set lower for faster to compress but less effective compression.
    /// </summary>
    public static readonly byte ReleaseZStdCompressionQuality =

#if IS_PUBLISH
        22
#else
        1
#endif
        ;



#endif



}



///////////////////////////////////////////
///////////////////////////////////////////