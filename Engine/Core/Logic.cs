


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






    private static readonly List<Action> StartOfFrameActions = new();

    /// <summary>
    /// Adds <paramref name="action"/> to a list of actions be invoked on the main logic thread, before any other logic thread processing. Removed after execution.
    /// </summary>
    /// <param name="action"></param>
    public static void AppendStartOfFrameAction(Action action)
    {
        lock (StartOfFrameActions)
            StartOfFrameActions.Add(action);
    }



    private static readonly List<Action> EndOfFrameActions = new();

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




    private static readonly List<Action> PermanentEndOfFrameActions = new();

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
    /// <br/> Thread affinity/identity of callers don't matter, so long as each call is matched with some eventual counteracting <see cref="ExitFrameGate"/> call.
    /// </summary>
    public static void EnterFrameGate()
    {
        while (true)
        {
            // Wait until the gate is open at least once
            _gateOpen.Wait();

            // Speculatively claim activity
            Interlocked.Increment(ref _active);

            // If the gate stayed open, we're good
            if (Volatile.Read(ref _open) != 0)
            {
                _drained.Reset();
                return;
            }

            // Gate closed during entry -> rollback claim
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








    public static float Delta { get; private set; }

    public static float TimeActive { get; private set; }
    public static ulong TimeActiveMsec { get; private set; }






    private static void OpenFrameGate()
    {
        Volatile.Write(ref _open, 1);
        _gateOpen.Set();
    }

    private static void CloseFrameGateAndWait()
    {
        if (Interlocked.Exchange(ref _open, 0) == 0)
            return;

        _gateOpen.Reset();
        _drained.Wait();
    }




    public static void LogicThreadLoop()
    {


        lock (StartOfFrameActions)
        {
            for (int i = 0; i < StartOfFrameActions.Count; i++)
                StartOfFrameActions[i].Invoke();

            StartOfFrameActions.Clear();
        }



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



        RenderThread.TryPushRenderCommands();


        Delta = (float)LogicStopWatch.Elapsed.TotalSeconds;

        TimeActive += Delta;
        TimeActiveMsec += (ulong)LogicStopWatch.Elapsed.Milliseconds;


        LogicStopWatch.Reset();


    }



}