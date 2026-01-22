

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





    private const string AssetArchiveName = "Assets";


    //the actual final AOT relase publish reads the asset archive right in its working directory, whereas the regular IL release build needs to read from working directory/publish/netX.XX/archive.

#if IS_AOT_PUBLISH   
    public const string ReleaseAssetArchivePath = AssetArchiveName; 
#else
    public static readonly string ReleaseAssetArchivePath = Path.GetRelativePath(Directory.GetCurrentDirectory(), $"{Path.Combine(Directory.GetCurrentDirectory(), "publish", Path.GetFileName(Directory.GetCurrentDirectory()) )}/{AssetArchiveName}");
#endif







#if DEBUG


    /// <summary>
    /// The folder assets can be loaded from, and that will be compressed into a zstd archive in release builds, relative to the debug executable.
    /// </summary>
    public const string AssetFolderPath = $"../../../../Assets";

    public const string AssetCachePath = $"../../../../AssetCache";





    /// <summary>
    /// The path pointing to Nvidia Texture Tools 3's nvtt_export.exe.
    /// </summary>
    public const string NVTT3Path = "C:\\Program Files\\NVIDIA Corporation\\NVIDIA Texture Tools\\nvtt_export.exe";


    /// <summary>
    /// The quality used to compress the asset directory during release build building. Ranges from 1 to 22. Set lower for faster to compress but less effective compression.
    /// </summary>
    public static readonly byte ReleaseZStdCompressionQuality = 22;


    /// <summary>
    /// A limit on the amount of threads that can participate in compression of the asset directory during release build building. 0 = unlimited.
    /// </summary>
    public static readonly ushort ReleaseZStdCompressionThreadLimit = 0;

#endif



}



///////////////////////////////////////////
///////////////////////////////////////////