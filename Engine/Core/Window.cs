
namespace Engine.Core;

using SDL3;
using System.Runtime.InteropServices;
using System.Text;
using static Engine.Core.EngineMath;



#if DEBUG
using Engine.Stripped;
#endif

public static class Window
{





    private static nint SDLWindowHandle;
    private static EngineSettings.EngineInitSettings GivenInitSettings;

    public static nint Init(EngineSettings.EngineInitSettings settings)
    {

        // SDL

        if (!SDL.Init(SDL.InitFlags.Video | SDL.InitFlags.Gamepad))
            throw new Exception($"Failed to create SDL3 - {SDL.GetError()}");



        // SDL WINDOW

        SDL.WindowFlags flags = RenderingBackend.GetSDLWindowFlagsForBackend(settings.RenderingBackend);
        if (settings.InitialWindowResizeable) flags |= SDL.WindowFlags.Resizable;

        SDLWindowHandle = SDL.CreateWindow(settings.WindowTitle, (int)settings.InitialWindowSize.X, (int)settings.InitialWindowSize.Y, flags);
        if (SDLWindowHandle == IntPtr.Zero) 
            throw new Exception($"Failed to create SDL3 window - {SDL.GetError()}");


        GivenInitSettings = settings;


        SDL.SetWindowMinimumSize(SDLWindowHandle, 64, 64);


        SDL.SetWindowPosition(SDLWindowHandle, (int)GivenInitSettings.InitialWindowPosition.X, (int)GivenInitSettings.InitialWindowPosition.Y);
        SDL.SetWindowSurfaceVSync(SDLWindowHandle, GivenInitSettings.VSync);
        SDL.SetWindowFullscreen(SDLWindowHandle, GivenInitSettings.InitialWindowFullscreen);
        SDL.SetWindowAlwaysOnTop(SDLWindowHandle, GivenInitSettings.InitialWindowAlwaysOnTop);

        SDL.SetWindowTitle(SDLWindowHandle, "Window");

        SDL.SyncWindow(SDLWindowHandle);


        lock (windowValidLock)
            windowValid = true;


        return SDLWindowHandle;
    }



    public static bool MouseModeRelative;



    public static void WindowPoll()
    {


        MouseScrollWheelDelta = 0;


        SDL.SetWindowRelativeMouseMode(SDLWindowHandle, MouseModeRelative);


        while (SDL.PollEvent(out var @ev))
        {
            switch ((SDL.EventType)ev.Type)
            {
                case SDL.EventType.WindowDestroyed:
                case SDL.EventType.WindowCloseRequested:
                case SDL.EventType.Quit:
                    Kernel.WindowNotifyEngineClosing();

                    break;


                case SDL.EventType.WindowResized:
                case SDL.EventType.WindowEnterFullscreen:
                case SDL.EventType.WindowLeaveFullscreen:
                    ReconfigureWindow(GetWindowClientArea(), false, true, false, 0, true, string.Empty);
                    break;


                case SDL.EventType.MouseWheel:
                    MouseScrollWheelDelta = ev.Wheel.Y / Logic.Delta;

                    break;

            }



            lock (textInputLock)
            {
                if (CurrentTextInput != null)
                {
                    lock (CurrentTextInput)
                    {
                        if (@ev.Type == (uint)SDL.EventType.TextInput)
                        {
                            var txt = Marshal.PtrToStringUTF8(@ev.Text.Text);
                            CurrentTextInput.Append(txt);
                            TextInputTextAddedEvent.Invoke(txt);
                        }
                    }
                }
            }


        }


    }




    public static float MouseScrollWheelDelta { get; private set; }



    public static void CloseWindow()
    {
        SDL.DestroyWindow(SDLWindowHandle);
        SDL.Quit();
    }



    /// <summary>
    /// Gets the drawable region of the screen. Be aware that the actual swapchain may not currently be equal in size - see <see cref="RenderingBackend.CurrentSwapchainDetails"/> instead
    /// </summary>
    /// <returns></returns>
    public static Vector2<uint> GetWindowClientArea()
    {
        SDL.GetWindowSizeInPixels(SDLWindowHandle, out var x, out var y);
        return new((uint)x, (uint)y);
    }







    private static StringBuilder CurrentTextInput;
    private static readonly object textInputLock = new();


    public static readonly ThreadSafeEventAction<string> TextInputTextAddedEvent = new();


    public static StringBuilder StartTextInput()
    {
        lock (textInputLock)
        {
            if (CurrentTextInput != null) 
                return CurrentTextInput;


            SDL.StartTextInput(SDLWindowHandle);

            CurrentTextInput = new();

            return CurrentTextInput;
        }
    }

    public static void EndTextInput()
    {
        lock (textInputLock)
        {
            if (CurrentTextInput == null) return;

            SDL.StopTextInput(SDLWindowHandle);
            CurrentTextInput = null;
        }
    }








    public static void SetWindowPosition(Vector2<uint> position) =>
        SDL.SetWindowPosition(SDLWindowHandle, (int)position.X, (int)position.Y);



    public static void ReconfigureWindow(

        Vector2<uint> Size,

        bool Fullscreen,
        bool Resizeable,
        bool AlwaysOnTop,

        byte VSync,
        bool UseHDR,

        string WindowTitle = "Window"

        )
    {

        lock (windowValidLock)
        {

            windowValid = false;

            SDL.SetWindowSize(SDLWindowHandle, (int)Size.X, (int)Size.Y);

            SDL.SetWindowSurfaceVSync(SDLWindowHandle, VSync);
            SDL.SetWindowResizable(SDLWindowHandle, Resizeable);
            SDL.SetWindowFullscreen(SDLWindowHandle, Fullscreen);
            SDL.SetWindowAlwaysOnTop(SDLWindowHandle, AlwaysOnTop);

            SDL.SetWindowTitle(SDLWindowHandle, WindowTitle);

            SDL.SyncWindow(SDLWindowHandle);

            RenderThread.PushDeferredIdleRenderThreadAction(() => { RenderingBackend.ConfigureSwapchain(Size, UseHDR); return null; }).Wait();

            windowValid = true;

        }
    }


    private static object windowValidLock = new();
    private static bool windowValid;


    /// <summary>
    /// Returns false if the frame's command buffer is invalid due to current window state or recent window state changes. Reset at the beginning of each frame.
    /// </summary>
    /// <returns></returns>
    public static bool GetRenderCommandsValid()
    {
        lock (windowValidLock)
            return windowValid && ((SDL.GetWindowFlags(SDLWindowHandle) & SDL.WindowFlags.Minimized) == 0);
    }


}