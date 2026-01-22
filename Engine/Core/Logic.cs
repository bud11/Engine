


namespace Engine.Core;

#if DEBUG
using Engine.Stripped;
#endif


using System.Diagnostics;
using System.Threading.Tasks.Sources;


public static class Logic
{



    public static bool Paused;





    private static Stopwatch LogicStopWatch = new();




    private static FrameWaitSync LogicFrameAwaiter = new();

    private class FrameWaitSync : IValueTaskSource
    {
        private ManualResetValueTaskSourceCore<bool> _mrvtsc;
        private short _token;
        private bool _hasPending;

        public ValueTask Wait()
        {
            lock (this)
            {
                if (!_hasPending)
                {
                    _mrvtsc = new ManualResetValueTaskSourceCore<bool>();
                    _mrvtsc.RunContinuationsAsynchronously = true;
                    _token++;
                    _hasPending = true;
                }

                return new ValueTask(this, _token);
            }
        }

        public void NextFrame()
        {
            lock (this)
            {
                if (_hasPending)
                {
                    _mrvtsc.SetResult(true);
                    _hasPending = false;
                }
            }
        }

        public void GetResult(short token) => _mrvtsc.GetResult(token);
        public ValueTaskSourceStatus GetStatus(short token) => _mrvtsc.GetStatus(token);
        public void OnCompleted(Action<object?> continuation, object? state, short token, ValueTaskSourceOnCompletedFlags flags)
            => _mrvtsc.OnCompleted(continuation, state, token, flags);
    }




    public static ValueTask WaitLogicFrame() => LogicFrameAwaiter.Wait();


    public static async ValueTask WaitSeconds(float seconds, bool pausable = true, CancellationToken? tk = null)
    {
        float timesofar = 0f;

        while (timesofar < seconds)
        {
            if (!Paused || !pausable) timesofar += Delta;

            if (tk != null && tk.Value.IsCancellationRequested) return;

            await LogicFrameAwaiter.Wait();
        }
    }










    /// <summary>
    /// Equivalent to calling <see cref="AppendEndOfFrameAction"/> with <see cref="Freeable.Free"/>.
    /// </summary>
    /// <param name="inst"></param>
    public static void FreeDeferred(this Freeable inst) => AppendEndOfFrameAction(inst.Free);




    private static List<Action> EndOfFrameActions = new();

    /// <summary>
    /// Adds <paramref name="action"/> to a list of actions be invoked on the main logic thread, after all logic has been processed but before rendering begins. Removed after execution.
    /// <br/> Also see <seealso cref="AppendPermanentEndOfFrameAction"/>.
    /// </summary>
    /// <param name="action"></param>
    public static void AppendEndOfFrameAction(Action action)
    {
        lock (EndOfFrameActions)
            EndOfFrameActions.Add(action);
    }


    private static List<Action> PermanentEndOfFrameActions = new();

    /// <summary>
    /// Appends <paramref name="action"/> to a list of actions to be invoked on the main logic thread, after all logic has been processed but before rendering begins, every frame until removed.
    /// <br/> See <see cref="RemovePermanentEndOfFrameAction"/> and <seealso cref="AppendEndOfFrameAction"/>.
    /// </summary>
    /// <param name="action"></param>
    public static void AppendPermanentEndOfFrameAction(Action action)
    {
        lock (PermanentEndOfFrameActions)
            PermanentEndOfFrameActions.Add(action);
    }
    public static void RemovePermanentEndOfFrameAction(Action action)
    {
        lock (PermanentEndOfFrameActions)
            PermanentEndOfFrameActions.Remove(action);
    }









    private static Dictionary<Thread, bool> ThreadsWorking = new();


    /// <summary>
    /// Waits for the main thread logic frame to have started, and prevents it from ending while any thread has this (per thread) flag set. 
    /// <br /> As a general rule of thumb, set to true while doing anything engine related on any other thread.
    /// <br /> Also see <seealso cref="SetCurrentThreadAsNotWorking"/>
    /// </summary>
    /// 
    public static void SetCurrentThreadAsWorking()
    {
        var thread = Thread.CurrentThread;


        if (thread != Kernel.LogicThread)
        {
            while (true)
                lock (ThreadsWorking)
                    if (ThreadsWorking[Kernel.LogicThread]) break;
        }


        lock (ThreadsWorking)
            ThreadsWorking[thread] = true;
    }

    /// <summary>
    /// Sets the thread as not working. See <see cref="SetCurrentThreadAsWorking"/>
    /// </summary>
    public static void SetCurrentThreadAsNotWorking()
    {
        var thread = Thread.CurrentThread;
        lock (ThreadsWorking)
            ThreadsWorking[thread] = false;
    }







/*
    private static readonly DynamicUnmanagedHAllocator LogicContentAllocator = new();

    /// <summary>
    /// Allocates temporary unmanaged heap memory that will be valid until the end of the logic frame.
    /// </summary>
    public static unsafe byte* AllocateLogicTemporaryUnmanaged(int bytes) => LogicContentAllocator.Alloc(bytes);

*/




    public static float Delta;

    public static float TimeActive;
    public static ulong TimeActiveMsec;



    public static void LogicThreadLoop()
    {



        Input.InputLoop();



        LogicStopWatch.Start();


        //set this thread as working (forces other threads to wait for this)
        SetCurrentThreadAsWorking();


        LogicFrameAwaiter.NextFrame();



        Entry.Loop();






        //set thread as not working
        SetCurrentThreadAsNotWorking();




        //wait for all threads operating on engine logic to not be working
        while (true)
        {
            lock (ThreadsWorking)
                if (!ThreadsWorking.ContainsValue(true)) 
                    break;

        }








        lock (EndOfFrameActions)
        {
            for (int i = 0; i < EndOfFrameActions.Count; i++)
                EndOfFrameActions[i].Invoke();

            EndOfFrameActions.Clear();
        }

        lock (PermanentEndOfFrameActions)
        {
            for (int i = 0; i < PermanentEndOfFrameActions.Count; i++)
                PermanentEndOfFrameActions[i].Invoke();
        }




        Rendering.WaitForFlushing();



        //wait for framerate lock
        while (LogicStopWatch.Elapsed.TotalSeconds < (1d / EngineSettings.LogicRateTarget)) ;



        var time = LogicStopWatch.Elapsed.TotalSeconds;


        Rendering.TryPushRenderCommands();


        Delta = float.Min((float)LogicStopWatch.Elapsed.TotalSeconds, 1f/60f);

        TimeActive += Delta;
        TimeActiveMsec += (ulong)LogicStopWatch.Elapsed.Milliseconds;


        LogicStopWatch.Reset();


    }



}