



namespace Engine.Core;



using System.Diagnostics;
using System.Drawing;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;



/// <summary>
/// A thin abstraction over the rendering backend. <br />
/// See <see cref="IDeferredCommand"/>, <see cref="RenderingBackend"/> and <see cref="RenderingBackend.IRenderingBackend"/> for more control.
/// </summary>
public static partial class Rendering
{








    public static Vector4 ToVector4(this Color Col)
    {
        return new Vector4(Col.R, Col.G, Col.B, Col.A)/255f;
    }
    public static Color ToColor(this Vector4 Col)
    {
        return Color.FromArgb((int)(Col.W * 255), (int)(Col.X * 255), (int)(Col.Y * 255), (int)(Col.Z * 255));
    }









    public static void RenderingInit()
    {
        RenderingBackend.CreateDummyObjects();

#if RELEASE 
        RenderingBackend.CreateShaders();
#endif

        RenderingBackend.ConfigureSwapchain(EngineSettings.HDR);

    }





    public static volatile float Delta;

    private static readonly Stopwatch RenderStopWatch = new();


    private enum RenderThreadState
    {
        Idle,

        ExecutingGeneral,

        ExecutingDeferred,
        ExecutingDeferredPre,

        ExecutingDeferredFlush,
        ExecutingDeferredFlushPre,
    }




    private enum CommandPushState
    {
        None,
        Frame,
        Flush,
    }



    /// <summary>
    /// What the logic thread currently wants the render thread to do.
    /// </summary>
    private static volatile CommandPushState PushState = CommandPushState.None;   
    
    /// <summary>
    /// What the render thread is currently doing.
    /// </summary>
    private static volatile RenderThreadState State = RenderThreadState.Idle;



    private static readonly object RenderThreadLock = new();


    public static void TryPushRenderCommands()
    {


        lock (RenderThreadLock)
        {
            if (State == RenderThreadState.Idle && RenderStopWatch.Elapsed.TotalSeconds <= (1d / EngineSettings.RenderRateTarget) && Window.GetRenderCommandsValid())
            {
                RenderStopWatch.Start();

                PushState = CommandPushState.Frame;
                SwapBuffers();
            }

            ResetBuffers();
        }
    }

    public static void FlushCommandsAndWait()
    {


        lock (RenderThreadLock)
        {
            if (State == RenderThreadState.Idle && Window.GetRenderCommandsValid())
            {
                PushState = CommandPushState.Flush;
                SwapBuffers();
            }
            ResetBuffers();
        }

        WaitForFlushing();
    }


    public static void WaitForFlushing()
    {
        var spin = new SpinWait();

        while (true)
        {
            var st = State;
            if (st != RenderThreadState.ExecutingDeferredFlush && st != RenderThreadState.ExecutingDeferredFlushPre)
                return;

            spin.SpinOnce();
        }
    }








    public static void RenderThreadLoop()
    {

        while (true)
        {
            if (Kernel.IsClosing()) return;



            lock (RenderThreadActions)
            {
                if (RenderThreadActions.Count != 0)
                {

                    lock (RenderThreadLock)
                        State = RenderThreadState.ExecutingGeneral;

                    for (int i = 0; i < RenderThreadActions.Count; i++)
                    {
                        ThreadBoundActionStruct f = RenderThreadActions[i];
                        var res = f.func.Invoke();
                        f.res.SetResult(res);
                    }
                    RenderThreadActions.Clear();


                    lock (RenderThreadLock)
                        State = RenderThreadState.Idle;
                }
            }


            lock (RenderThreadLock)
            {
                if (PushState == CommandPushState.Frame)
                {
                    State = RenderThreadState.ExecutingDeferredPre;
                    PushState = CommandPushState.None;
                    break;
                }

                if (PushState == CommandPushState.Flush)
                {
                    State = RenderThreadState.ExecutingDeferredFlushPre;
                    PushState = CommandPushState.None;
                    break;
                }
            }

        }




        PreRenderingCommandBufferA.Execute();


        lock (RenderThreadLock)
        {
            if (State == RenderThreadState.ExecutingDeferredPre)
                State = RenderThreadState.ExecutingDeferred;
            else State = RenderThreadState.ExecutingDeferredFlush;
        }



        RenderingBackend.StartFrameRendering();

        RenderingCommandBufferA.Execute();

        RenderingBackend.EndFrameRendering();



        RenderContentAllocatorA.Reset();



        if (State != RenderThreadState.ExecutingDeferredFlush)
            Delta = float.Max((float)RenderStopWatch.Elapsed.TotalSeconds, 1f / EngineSettings.RenderRateTarget);



        lock (RenderThreadLock)
            State = RenderThreadState.Idle;



        RenderStopWatch.Reset();

    }











    /// <summary>
    /// Throws if not invoked on the render thread.
    /// </summary>
    /// <exception cref="Exception"></exception>
    [Conditional("DEBUG")]
    public static void CheckThisIsRenderThread()
    {
        if (Thread.CurrentThread != Kernel.RenderThread)
            throw new Exception("Calling render thread method on non render thread - defer to render thread");
    }


    /// <summary>
    /// Throws if called during frame rendering (between <see cref="RenderingBackend.StartFrameRendering"/> and <see cref="RenderingBackend.EndFrameRendering"/>).
    /// </summary>
    /// <exception cref="Exception"></exception>
    [Conditional("DEBUG")]
    public static void CheckOutsideOfRendering()
    {
        var st = State;

        if (st == RenderThreadState.ExecutingDeferred || st == RenderThreadState.ExecutingDeferredFlush)
            throw new Exception($"Calling method designed to be called outside of rendering - consider deferring via {nameof(PushDeferredPreRenderThreadCommand)} or {nameof(PushRenderThreadAction)}");
    }


    /// <summary>
    /// Throws if called outside of frame rendering (between <see cref="RenderingBackend.StartFrameRendering"/> and <see cref="RenderingBackend.EndFrameRendering"/>) or not on the render thread.
    /// </summary>
    /// <exception cref="Exception"></exception>
    [Conditional("DEBUG")]
    public static void CheckDuringRendering()
    {
        CheckThisIsRenderThread();

        var st = State;

        if (!(st == RenderThreadState.ExecutingDeferred || st == RenderThreadState.ExecutingDeferredFlush))
            throw new Exception($"Calling method designed to be called outside of rendering - consider deferring via {nameof(PushDeferredPreRenderThreadCommand)} or {nameof(PushRenderThreadAction)}");
    }








    private static DynamicUnmanagedHAllocator RenderContentAllocatorA = new(), RenderContentAllocatorB = new();


    /// <summary>
    /// Allocates temporary unmanaged heap memory that will be valid until the end of the next rendered frame.
    /// </summary>
    public static unsafe byte* AllocateRenderTemporaryUnmanaged(int bytes) => RenderContentAllocatorB.Alloc(bytes);




    //B buffers are logically owned by logic thread, A buffers are logically owned by render thread

    private static DeferredCommandBuffer RenderingCommandBufferA = new(), RenderingCommandBufferB = new();
    private static DeferredCommandBuffer PreRenderingCommandBufferA = new(), PreRenderingCommandBufferB = new();





    /// <summary>
    /// Pushes an <see cref="IDeferredCommand"/> to be executed during the upcoming frame's rendering on the render thread. Will be discarded if that frame is dropped. Thread safe.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="cmd"></param>
    public static void PushDeferredRenderThreadCommand<T>(in T cmd
#if DEBUG
        , [CallerFilePath] string file = "",
          [CallerLineNumber] int line = 0
#endif
        ) where T : unmanaged, IDeferredCommand 
        => RenderingCommandBufferB.PushCommand(cmd

#if DEBUG
            , file, line
#endif
            );



    /// <summary>
    /// Pushes an <see cref="IDeferredCommand"/> to be executed just before the upcoming frame's rendering on the render thread. Will be discarded if that frame is dropped. Thread safe.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="cmd"></param>
    public static void PushDeferredPreRenderThreadCommand<T>(in T cmd
#if DEBUG
        , [CallerFilePath] string file = "",
          [CallerLineNumber] int line = 0
#endif
        ) where T : unmanaged, IDeferredCommand
        => PreRenderingCommandBufferB.PushCommand(cmd

#if DEBUG
            , file, line
#endif
            );




    private struct ThreadBoundActionStruct
    {
        public Func<object> func;
        public TaskCompletionSource<object> res;
    }

    private static List<ThreadBoundActionStruct> RenderThreadActions = new();


    /// <summary>
    /// Pushes <paramref name="func"/> to be executed on the render thread as soon as possible (in other words, whenever the render thread isn't currently rendering).
    /// <br/> This allocates and should be used sparingly. <see cref="PushDeferredRenderThreadCommand{T}(in T, string, int)"/> and <see cref="PushDeferredPreRenderThreadCommand{T}(in T, string, int)"/> are usually recommended instead.
    /// </summary>
    /// <param name="func"></param>
    /// <returns></returns>
    public static Task<object> PushRenderThreadAction(Func<object> func)
    {

        if (Thread.CurrentThread == Kernel.RenderThread) throw new Exception();


        var task = new TaskCompletionSource<object>();
        lock (RenderThreadActions) RenderThreadActions.Add(new ThreadBoundActionStruct() { func = func, res = task });


        return task.Task;
    }








    public static void ResetBuffers()
    {
        RenderingCommandBufferB.Reset();
        PreRenderingCommandBufferB.Reset();

        RenderContentAllocatorB.Reset();
    }


    public static void SwapBuffers()
    {
        (RenderingCommandBufferB, RenderingCommandBufferA) = (RenderingCommandBufferA, RenderingCommandBufferB);
        (PreRenderingCommandBufferB, PreRenderingCommandBufferA) = (PreRenderingCommandBufferA, PreRenderingCommandBufferB);
 
        (RenderContentAllocatorB, RenderContentAllocatorA) = (RenderContentAllocatorA, RenderContentAllocatorB);

    }






}