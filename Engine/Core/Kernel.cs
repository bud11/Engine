
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
                    if (KernelState != KernelStates.Init)   //<-- waits for backend creation to be done
                        break;
                }



                Loading.ScanForAssetArchives();

#if DEBUG
                Loading.ScanResourceAssociations();


                
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




}

