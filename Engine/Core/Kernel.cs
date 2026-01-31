
namespace Engine.Core;

using Engine.Core;


#if DEBUG
using Engine.Stripped;
#endif

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.CompilerServices;


public static class Kernel
{




    public static Thread LogicThread { get; private set; }
    public static Thread RenderThread { get; private set; }



    private static readonly object KernelLock = new();
    private static KernelStates KernelState = KernelStates.Init;

    private enum KernelStates
    {
        Init,
        Running,
        Closing
    }


    public static void WindowNotifyEngineClosing()
    {
        lock (KernelLock)
            KernelState = KernelStates.Closing;
    }


    public static bool IsClosing()
    {
        lock (KernelLock)
        {
            return KernelState == KernelStates.Closing;
        }
    }






    //this being a field rather than local is required otherwise release build optimization breaks
    private static volatile nint window = IntPtr.Zero;



    public static void Main()
    {


        RuntimeHelpers.RunClassConstructor(typeof(EngineSettings).TypeHandle);


#if DEBUG
        Loading.CleanAssetCache();
#endif




        var settings = Entry.EngineInit();

        EngineSettings.LogicRateTarget = settings.LogicRateTarget;
        EngineSettings.RenderRateTarget = settings.RenderRateTarget;
        EngineSettings.HDR = settings.UseHDR;







        LogicThread = new Thread(
            () =>
            {

                Loading.ScanForAssetArchives();


                window = Window.Init(settings);     //<-- indirectly backend creation on render thread



                while (true)
                {
                    lock (KernelLock)
                    {
                        if (KernelState != KernelStates.Init)   //<-- waits for backend creation to be done
                            break;
                    }
                }






#if DEBUG
                ShaderCompilation.CompileShaders();

                ImGUIController.Init();
#endif


                Entry.Init().Wait();  //stall nessecary




                while (true) 
                {

                    Window.WindowPoll();

                    if (IsClosing())
                    {
#if DEBUG
                        ImGUIController.Shutdown();
#endif
                        Window.CloseWindow();

                        Loading.UnloadAllResources();

                        return;
                    }


                    Logic.LogicThreadLoop();  
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

                lock (KernelLock)
                    KernelState = KernelStates.Running;


                while (true)
                {
                    if (IsClosing())
                    {
                        RenderingBackend.Destroy();
                        return;
                    }


                    Rendering.RenderThreadLoop();
                }
            }
            );
#if DEBUG
        RenderThread.Name = "render";
#endif


        LogicThread.Start();
        RenderThread.Start();

    }




}

