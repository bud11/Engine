
namespace Engine.Core;

using Engine.Attributes;



#if DEBUG
using Engine.Stripped;
using System.Buffers.Text;
using System.Diagnostics;
#endif

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using static RenderThread;

using static Engine.Core.IO;



public static partial class Kernel
{




    public static Thread LogicThread { get; private set; }
    public static Thread RenderThread { get; private set; }



    private static volatile KernelStates KernelState = KernelStates.Init;

    private enum KernelStates
    {
        Init,
        Running,
        CloseRequested,
        Closing
    }


    public static void WindowNotifyEngineClosing() => KernelState = KernelStates.CloseRequested;


    public static bool IsClosing() => KernelState == KernelStates.Closing;
    public static bool IsCloseRequested() => KernelState == KernelStates.CloseRequested;





    // this being a field rather than local is required otherwise release build optimization breaks
    private static volatile nint window = IntPtr.Zero;




#if RELEASE

    [BinarySerializableType(typeof(EngineSettings.EngineInitSettings))]
    public partial class InitParser : Parsing.BinarySerializerDeserializerBase;

    private static readonly InitParser initParse = new();

    private const string ConfigEnvName = "_EngineConfig";

#endif




    public static void Main()
    {


#if DEBUG
        CleanAssetCache();
#endif



        // ------------------------------------------------------------------------------------------------

        // Init

        var settings = Entry.EngineInit();


        EngineSettings.LogicRateTarget = settings.InitialLogicRateTarget;
        EngineSettings.RenderRateTarget = settings.InitialRenderRateTarget;
        EngineSettings.HDR = settings.InitialHDR;

        VRamLimit = settings.VRAMMemoryLimit;

        // ------------------------------------------------------------------------------------------------







        LogicThread = new Thread(
            () =>
            {

                window = Window.Init(settings);     //<-- indirectly backend creation on render thread



                while (true)
                {
                    if (KernelState != KernelStates.Init)   //<-- waits for backend creation to be done
                        break;
                }



                ScanForAssetArchives();

#if DEBUG
                GameResource.ScanResourceAssociations();


                
                if (!ShaderCompilation.CompileShaders().Result)
                {
                    Thread.Sleep(1000);
                    Environment.Exit(1);
                }


                EngineDebug.Init();

                ImGUIController.Init();
#endif



                Entry.Init();     



                while (true) 
                {

                    Window.WindowPoll();

#if DEBUG
                    EngineDebug.Loop();
#endif

                    Logic.LogicThreadLoop();



                    if (IsCloseRequested())
                    {


#if DEBUG
                        ImGUIController.Shutdown();
#endif
                        Window.CloseWindow();



                        KernelState = KernelStates.Closing;


                        return;
                    }



                }
            });

#if DEBUG
        LogicThread.Name = "main_logic";
#endif




        RenderThread = new Thread(
            () =>
            {


                while (window == IntPtr.Zero) ;  //<-- waits for SDL window to exist from logic thread



                //BACKEND
                RenderingBackend.CreateBackend(settings.RenderingBackend, window);

                KernelState = KernelStates.Running;


                while (true)
                {
                    RenderThreadLoop();

                    if (IsClosing())
                    {
                        RenderingBackend.Destroy();
                        return;
                    }
                }
            }
            );
#if DEBUG
        RenderThread.Name = "render";
#endif


        LogicThread.Start();
        RenderThread.Start();

    }








    private static ulong VRamLimit, VRamCurrent;

    private static readonly object VramLock = new();




#if DEBUG

    public static ulong GetVRamCurrent() => VRamCurrent;
    public static ulong GetVRamLimit() => VRamLimit;


#endif


    /// <summary>
    /// Notifies the engine that gpu memory is being allocated.
    /// <br/> If there isn't enough space according to <see cref="VRamLimit"/>, an aggressive garbage collection and finalizer run will occur to try to indirectly free object-associated gpu memory.
    /// <br/> If that couldn't clear out enough space, an <see cref="OutOfMemoryException"/> will be thrown.
    /// </summary>
    /// <param name="amount"></param>
    /// <exception cref="OutOfMemoryException"></exception>
    public static void TryAllocateVram(ulong amount)
    {
        lock (VramLock)
        {
            if (VRamLimit != 0)
            {
                int i = 5;

                while (VRamCurrent + amount > VRamLimit)
                {
                    if (i > 0)
                    {
                        GC.Collect(2, GCCollectionMode.Forced, true, true);
                        GC.WaitForPendingFinalizers();
                    }
                    else
                    {
                        throw new OutOfMemoryException(nameof(VRamLimit));
                    }

                    i--;
                }
            }

            VRamCurrent += amount;
        }
    }

    /// <summary>
    /// Notifies the engine that gpu memory is being released.
    /// </summary>
    /// <param name="amount"></param>
    public static void ReleaseVram(ulong amount)
    {
        lock (VramLock)
            VRamCurrent -= amount;
    }


}

