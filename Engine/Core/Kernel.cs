
namespace Engine.Core;



#if DEBUG
using Engine.Stripped;
#endif

using System.Runtime.CompilerServices;

using static RenderThread;



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
        CloseRequested,
        Closing
    }


    public static void WindowNotifyEngineClosing()
    {
        lock (KernelLock)
            KernelState = KernelStates.CloseRequested;
    }

    public static bool IsClosing()
    {
        lock (KernelLock)
            return KernelState == KernelStates.Closing;
    }
    public static bool IsCloseRequested()
    {
        lock (KernelLock)
            return KernelState == KernelStates.CloseRequested;
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

                window = Window.Init(settings);     //<-- indirectly backend creation on render thread



                while (true)
                {
                    lock (KernelLock)
                    {
                        if (KernelState != KernelStates.Init)   //<-- waits for backend creation to be done
                            break;
                    }
                }



                Loading.ScanForAssetArchives();

#if DEBUG
                Loading.ScanResourceAssociations();


                
                if (!ShaderCompilation.CompileShaders().Result)
                {
                    Thread.Sleep(1000);
                    Environment.Exit(1);
                }



                ImGUIController.Init();
#endif



                Entry.Init();     



                while (true) 
                {

                    Window.WindowPoll();

                    Logic.LogicThreadLoop();



                    if (IsCloseRequested())
                    {


                        HashSet<GameResource> s;

                        lock (GameResource.AllResources)
                        {
                            s = [.. GameResource.AllResources];
                            GameResource.AllResources.Clear();
                        }

                        foreach (var r in s)
                            r.Free();


#if DEBUG
                        ImGUIController.Shutdown();
#endif
                        Window.CloseWindow();



                        lock (KernelLock)
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

                lock (KernelLock)
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




}

