


namespace Engine.Core;

#if DEBUG
using Engine.Stripped;
#endif


using System.Diagnostics;
using System.Threading.Tasks.Sources;


public static class Logic
{






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


    public static async ValueTask WaitSeconds(float seconds, CancellationToken? tk = null)
    {
        float timesofar = 0f;

        while (timesofar < seconds)
        {
            timesofar += Delta;

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





    private static int _active;
    private static int _open = 1;

    private static readonly ManualResetEventSlim _gateOpen = new(true);
    private static readonly ManualResetEventSlim _drained = new(true);



    /// <summary>
    /// Blocks until a frame is processing, and indicates that you don't want the frame to be allowed to end until <see cref="ExitFrameGate"/> is called.
    /// <br/> Thread affinity/identity of callers don't matter, so long as each call is matched with some eventual <see cref="ExitFrameGate"/> call.
    /// </summary>
    public static void EnterFrameGate()
    {
        while (true)
        {
            // 1. Wait until the gate is open *at least once*
            _gateOpen.Wait();

            // 2. Speculatively claim activity
            Interlocked.Increment(ref _active);

            // 3. If the gate stayed open, we're good
            if (Volatile.Read(ref _open) != 0)
            {
                _drained.Reset();
                return;
            }

            // 4. Gate closed during entry → rollback claim
            ExitFrameGate();
            // loop and try again
        }
    }





    /// <summary>
    /// See <see cref="EnterFrameGate"/>
    /// </summary>
    /// <exception cref="InvalidOperationException"></exception>
    public static void ExitFrameGate()
    {


#if DEBUG
        if (Volatile.Read(ref _active) <= 0)
            throw new InvalidOperationException();
#endif


        if (Interlocked.Decrement(ref _active) == 0)
            _drained.Set();
    }



    public readonly ref struct FrameGateUsingScope : IDisposable
    {
        public readonly void Dispose() => ExitFrameGate();
    }

    /// <summary>
    /// An <see cref="IDisposable"/> wrapper around <see cref="EnterFrameGate"/> and <see cref="ExitFrameGate"/>
    /// </summary>
    public static FrameGateUsingScope AcquireFrameUsingScope()
    {
        EnterFrameGate();

        return new();
    }







    private static void CloseFrameGateAndWait()
    {
        if (Interlocked.Exchange(ref _open, 0) == 0)
            return;

        _gateOpen.Reset();   // block new entrants
        _drained.Wait();    // wait for all active to exit
    }


    private static void OpenFrameGate()
    {
        Volatile.Write(ref _open, 1);
        _gateOpen.Set();
    }






    /*
        private static readonly DynamicUnmanagedHAllocator LogicContentAllocator = new();

        /// <summary>
        /// Allocates temporary unmanaged heap memory that will be valid until the end of the logic frame.
        /// </summary>
        public static unsafe byte* AllocateLogicTemporaryUnmanaged(int bytes) => LogicContentAllocator.Alloc(bytes);

    */





    public static float Delta { get; private set; }

    public static float TimeActive { get; private set; }
    public static ulong TimeActiveMsec { get; private set; }





    public static void LogicThreadLoop()
    {



        Input.InputLoop();



        LogicStopWatch.Start();



        OpenFrameGate();

        LogicFrameAwaiter.NextFrame();

        Entry.Loop();

        CloseFrameGateAndWait();









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




        RenderThread.WaitForFlushing();



        //wait for framerate lock
        while (LogicStopWatch.Elapsed.TotalSeconds < (1d / EngineSettings.LogicRateTarget)) ;



        var time = LogicStopWatch.Elapsed.TotalSeconds;


        RenderThread.TryPushRenderCommands();


        Delta = (float)LogicStopWatch.Elapsed.TotalSeconds;

        TimeActive += Delta;
        TimeActiveMsec += (ulong)LogicStopWatch.Elapsed.Milliseconds;


        LogicStopWatch.Reset();


    }



}