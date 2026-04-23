



namespace Engine.Core;



using System.Diagnostics;
using System.Runtime.CompilerServices;



/// <summary>
/// Manages rendering commands. <br />
/// See <see cref="IDeferredCommand"/>, <see cref="RenderingBackend"/> and <see cref="RenderingBackend.IRenderingBackend"/> for more control.
/// </summary>
public static class RenderThread
{






#if DEBUG
    public static volatile uint DrawCalls = 0;
#endif




    public static float Delta { get; private set; }

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
            if (State == RenderThreadState.Idle && (EngineSettings.RenderRateTarget == 0 || RenderStopWatch.Elapsed.TotalSeconds <= (1d / EngineSettings.RenderRateTarget)) && Window.GetRenderCommandsValid())
            {
                RenderStopWatch.Restart();

                PushState = CommandPushState.Frame;
                SwapBuffers();

                ResetnecessaryBuffers();
            }

            ResetDiscardableBuffers();
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

                ResetnecessaryBuffers();
            }

            ResetDiscardableBuffers();

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



    private static void ResetDiscardableBuffers()
    {
        RenderingCommandBufferB.Reset();
        PreRenderingCommandBufferB.Reset();

        RenderContentAllocatorB.Reset();
    }

    private static void ResetnecessaryBuffers()
    {
        necessaryPreRenderingCommandBufferB.Reset();
        necessaryRenderContentAllocatorB.Reset();
    }


    private static void SwapBuffers()
    {
        (RenderingCommandBufferB, RenderingCommandBufferA) = (RenderingCommandBufferA, RenderingCommandBufferB);
        (PreRenderingCommandBufferB, PreRenderingCommandBufferA) = (PreRenderingCommandBufferA, PreRenderingCommandBufferB);

        (RenderContentAllocatorB, RenderContentAllocatorA) = (RenderContentAllocatorA, RenderContentAllocatorB);

        (necessaryPreRenderingCommandBufferB, necessaryPreRenderingCommandBufferA) = (necessaryPreRenderingCommandBufferA, necessaryPreRenderingCommandBufferB);
        (necessaryRenderContentAllocatorB, necessaryRenderContentAllocatorA) = (necessaryRenderContentAllocatorA, necessaryRenderContentAllocatorB);
    }









    public static void RenderThreadLoop()
    {

        while (true)
        {
            if (Kernel.IsClosing()) return;



            if (AnyRenderThreadActions)
            {
                lock (RenderThreadLock)
                    State = RenderThreadState.ExecutingGeneral;


                lock (RenderThreadActions)
                {
                    for (int i = 0; i < RenderThreadActions.Count; i++)
                    {
                        ThreadBoundActionStruct f = RenderThreadActions[i];
                        var res = f.func.Invoke();
                        f.res.SetResult(res);
                    }
                    RenderThreadActions.Clear();
                    AnyRenderThreadActions = false;
                }


                lock (RenderThreadLock)
                    State = RenderThreadState.Idle;
            }



            if (PushState == CommandPushState.Frame)
            {
                lock (RenderThreadLock)
                {
                    State = RenderThreadState.ExecutingDeferredPre;
                    PushState = CommandPushState.None;
                }
                break;
            }


            if (PushState == CommandPushState.Flush)
            {
                lock (RenderThreadLock)
                {
                    State = RenderThreadState.ExecutingDeferredFlushPre;
                    PushState = CommandPushState.None;
                }
                break;
            }

        }


        necessaryPreRenderingCommandBufferA.Execute();


        PreRenderingCommandBufferA.Execute();


        lock (RenderThreadLock)
        {
            if (State == RenderThreadState.ExecutingDeferredPre)
                State = RenderThreadState.ExecutingDeferred;
            else State = RenderThreadState.ExecutingDeferredFlush;
        }


#if DEBUG
        DrawCalls = 0;
#endif


        RenderingBackend.StartFrameRendering(); 

        RenderingCommandBufferA.Execute();



#if DEBUG
        Stripped.EngineDebug.RenderThreadProcessingTime = (float)RenderStopWatch.Elapsed.TotalSeconds;
#endif
        var renderTime = RenderingBackend.EndFrameRendering(); 
#if DEBUG
        Stripped.EngineDebug.RenderThreadRenderingTime = renderTime - Stripped.EngineDebug.RenderThreadProcessingTime;
#endif



        RenderContentAllocatorA.Reset();



        if (State != RenderThreadState.ExecutingDeferredFlush && EngineSettings.RenderRateTarget != 0)
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
    [DebuggerHidden]
    [StackTraceHidden]
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
    [DebuggerHidden]
    [StackTraceHidden]
    public static void CheckOutsideOfRendering()
    {
        var st = State;

        if (st == RenderThreadState.ExecutingDeferred || st == RenderThreadState.ExecutingDeferredFlush)
            throw new Exception($"Calling method designed to be called outside of rendering - consider deferring via {nameof(PushDeferredPreRenderThreadCommand)} or {nameof(PushDeferredIdleRenderThreadAction)}");
    }


    /// <summary>
    /// Throws if called outside of frame rendering (between <see cref="RenderingBackend.StartFrameRendering"/> and <see cref="RenderingBackend.EndFrameRendering"/>) or not on the render thread.
    /// </summary>
    /// <exception cref="Exception"></exception>
    [Conditional("DEBUG")]
    [DebuggerHidden]
    [StackTraceHidden]
    public static void CheckDuringRendering()
    {
        CheckThisIsRenderThread();

        var st = State;

        if (!(st == RenderThreadState.ExecutingDeferred || st == RenderThreadState.ExecutingDeferredFlush))
            throw new Exception($"Calling method designed to be called outside of rendering - consider deferring via {nameof(PushDeferredPreRenderThreadCommand)} or {nameof(PushDeferredIdleRenderThreadAction)}");
    }








    private static DynamicUnmanagedHeapAllocator RenderContentAllocatorA = new(), RenderContentAllocatorB = new();


    /// <summary>
    /// Allocates temporary unmanaged heap memory that will be valid until the upcoming frame is processed or dropped.
    /// </summary>
    public static unsafe byte* AllocateRenderTemporaryUnmanaged(int bytes) => RenderContentAllocatorB.Alloc(bytes);


    /// <summary>
    /// Allocates temporary unmanaged heap memory that will be valid until the end of the next successfully rendered frame.
    /// </summary>
    public static unsafe byte* AllocatenecessaryRenderTemporaryUnmanaged(int bytes) => necessaryRenderContentAllocatorB.Alloc(bytes);





    //B buffers are logically owned by logic thread, A buffers are logically owned by render thread

    private static DeferredCommandBuffer RenderingCommandBufferA = new(), RenderingCommandBufferB = new();
    private static DeferredCommandBuffer PreRenderingCommandBufferA = new(), PreRenderingCommandBufferB = new();

    private static DeferredCommandBuffer necessaryPreRenderingCommandBufferA = new(), necessaryPreRenderingCommandBufferB = new();


    private static DynamicUnmanagedHeapAllocator necessaryRenderContentAllocatorA = new(), necessaryRenderContentAllocatorB = new();






    /// <summary>
    /// Pushes an <see cref="IDeferredCommand{T}"/> to be executed during the upcoming frame's rendering on the render thread. Will be discarded if that frame is dropped. Thread safe.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="cmd"></param>
    public static void PushDeferredRenderThreadCommand<T>(in T cmd) where T : unmanaged, IDeferredCommand<T>
        => RenderingCommandBufferB.PushCommand(cmd);

    /// <summary>
    /// Pushes an static method pointer to be executed during the upcoming frame's rendering on the render thread. Will be discarded if that frame is dropped. Thread safe.
    /// </summary>
    /// <param name="ptr"></param>
    public static unsafe void PushDeferredRenderThreadCommand(delegate*<void> ptr) 
        => RenderingCommandBufferB.PushCommand(ptr);

    /// <summary>
    /// Pushes an static method pointer to be executed during the upcoming frame's rendering on the render thread. Will be discarded if that frame is dropped. Thread safe.
    /// </summary>
    /// <typeparam name="TData"></typeparam>
    /// <param name="data"></param>
    /// <param name="ptr"></param>
    public static unsafe void PushDeferredRenderThreadCommand<TData>(TData data, delegate*<TData*, void> ptr) where TData : unmanaged
        => RenderingCommandBufferB.PushCommand(data, ptr);




    /// <summary>
    /// Pushes an <see cref="IDeferredCommand{T}"/> to be executed just before the upcoming frame's rendering on the render thread. Will be discarded if that frame is dropped. Thread safe.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="cmd"></param>
    public static void PushDeferredPreRenderThreadCommand<T>(in T cmd) where T : unmanaged, IDeferredCommand<T>
        => PreRenderingCommandBufferB.PushCommand(cmd);

    /// <summary>
    /// Pushes an static method pointer to be executed just before the upcoming frame's rendering on the render thread. Will be discarded if that frame is dropped. Thread safe.
    /// </summary>
    /// <param name="ptr"></param>
    public static unsafe void PushDeferredPreRenderThreadCommand(delegate*<void> ptr)
        => PreRenderingCommandBufferB.PushCommand(ptr);

    /// <summary>
    /// Pushes an static method pointer to be executed just before the upcoming frame's rendering on the render thread. Will be discarded if that frame is dropped. Thread safe.
    /// </summary>
    /// <typeparam name="TData"></typeparam>
    /// <param name="data"></param>
    /// <param name="ptr"></param>
    public static unsafe void PushDeferredPreRenderThreadCommand<TData>(TData data, delegate*<TData*, void> ptr) where TData : unmanaged
        => PreRenderingCommandBufferB.PushCommand(data, ptr);





    /// <summary>
    /// Pushes an <see cref="IDeferredCommand{T}"/> to be executed just before the upcoming frame's rendering on the render thread. Guaranteed to run before the next frame. Thread safe.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="cmd"></param>
    public static void PushDeferrednecessaryPreRenderThreadCommand<T>(in T cmd) where T : unmanaged, IDeferredCommand<T>
        => necessaryPreRenderingCommandBufferB.PushCommand(cmd);

    /// <summary>
    /// Pushes an static method pointer to be executed just before the upcoming frame's rendering on the render thread. Guaranteed to run before the next frame. Thread safe.
    /// </summary>
    /// <param name="ptr"></param>
    public static unsafe void PushDeferrednecessaryPreRenderThreadCommand(delegate*<void> ptr)
        => necessaryPreRenderingCommandBufferB.PushCommand(ptr);



    /// <summary>
    /// Pushes an static method pointer to be executed just before the upcoming frame's rendering on the render thread. Guaranteed to run before the next frame. Thread safe.
    /// </summary>
    /// <typeparam name="TData"></typeparam>
    /// <param name="data"></param>
    /// <param name="ptr"></param>
    public static unsafe void PushDeferrednecessaryPreRenderThreadCommand<TData>(TData data, delegate*<TData*, void> ptr) where TData : unmanaged
        => necessaryPreRenderingCommandBufferB.PushCommand(data, ptr);










    private readonly record struct ThreadBoundActionStruct(Func<object> func, TaskCompletionSource<object> res);

    private static readonly List<ThreadBoundActionStruct> RenderThreadActions = new();

    private static volatile bool AnyRenderThreadActions;


    /// <summary>
    /// Pushes <paramref name="func"/> to be executed during render thread idle time, guaranteeing it runs either immediately or after the upcoming frame. Thread safe.
    /// <br/> This allocates, and should, if ever, be used primarily in cases where you specifically need to wait for execution to complete/wait for a result. 
    /// </summary>
    /// <param name="func"></param>
    /// <returns></returns>
    public static async Task<object> PushDeferredIdleRenderThreadAction(Func<object> func)
    {
        if (Thread.CurrentThread == Kernel.RenderThread) return func.Invoke();

        var task = new TaskCompletionSource<object>();
        lock (RenderThreadActions)
        {
            RenderThreadActions.Add(new ThreadBoundActionStruct(func, task));
            AnyRenderThreadActions = true;
        }

        return await task.Task;
    }










}