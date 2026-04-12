



using Engine.Attributes;
using System.Diagnostics;
using System.Threading.Tasks;
using static Engine.Core.EngineSettings;





/// <summary>
/// The entry point to the application.
/// </summary>
public static partial class Entry
{



#if DEBUG
    /// <summary>
    /// Runs instantly on launch to obtain launch settings. Also see <seealso cref="Init"/> and <see cref="Engine.Core.EngineSettings"/>.
    /// </summary>
    private readonly struct _EngineInitSummary;
#endif


    public static partial EngineInitSettings EngineInit();



#if DEBUG
    /// <summary>
    /// Called once the engine is ready (along with shaders defined in <see cref="InitShaders"/>).
    /// </summary>
    private readonly struct _InitSummary;
#endif


    public static partial void Init();




#if DEBUG
    /// <summary>
    /// Called every logic frame.
    /// </summary>
    private readonly struct _LoopSummary;
#endif



    public static unsafe partial void Loop();


#if DEBUG

    /// <summary>
    /// A development-time, debug-only method <b>(implementation must be wrapped in #if DEBUG)</b> where shaders can be initialized. <b>Registering shaders from an entry point outside of this method isn't supported. </b> <br/>
    /// This is to facilitate the engine stripping this method (as well as the entirety of <see cref="Engine.Stripped.ShaderCompilation"/>) and instead directly precompiling shaders into release builds at build time, while also supporting shader hot reloading in debug builds prompted by <see cref="Engine.Stripped.ShaderCompilation.CompileShaders"/> (which in turn calls this).
    /// <br/> Also see <seealso cref="InitDebugShaders"/> to define shaders that shouldn't be precompiled into release.
    /// </summary>
    private readonly struct _InitShadersSummary;
    public static partial void InitShaders();


    /// <summary>
    /// Works the same way as <see cref="InitShaders"/>, except shaders defined here won't be precompiled and included in release.
    /// </summary>
    private readonly struct _InitDebugShadersSummary;
    public static partial void InitDebugShaders();


#endif


}