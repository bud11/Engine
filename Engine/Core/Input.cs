

namespace Engine.Core;




using SDL3;
using System.Numerics;
using System.Runtime.InteropServices;
using static Engine.Core.EngineMath;
using static Input.IGamepadInputBinding;
using static Input.IKeyboardInputBinding;


/// <summary>
/// Contains classes to handle raw inputs. Also see <see cref="Window.StartTextInput"/>
/// </summary>
public static class Input
{







    public readonly record struct KeyboardScanCodeBinding(SDL.Scancode bind) : IKeyboardInputBinding
    {
        public KeyboardBindingTypes Type => KeyboardBindingTypes.ScanCode;
        uint IKeyboardInputBinding.Bind => (uint)bind;
    }

    public readonly record struct KeyboardKeyCodeBinding(SDL.Keycode bind) : IKeyboardInputBinding
    {
        public KeyboardBindingTypes Type => KeyboardBindingTypes.KeyCode;
        uint IKeyboardInputBinding.Bind => (uint)bind;
    }


    public readonly record struct MouseButtonBinding(SDL.MouseButtonFlags bind);



    public readonly record struct GamepadButtonBinding(SDL.GamepadButton bind) : IGamepadInputBinding
    {
        public GamepadBindingTypes Type => GamepadBindingTypes.GamepadButton;
        uint IGamepadInputBinding.Bind => (uint)bind;
    }

    public readonly record struct GamepadAxisBinding(SDL.GamepadAxis bind) : IGamepadInputBinding
    {
        public GamepadBindingTypes Type => GamepadBindingTypes.GamepadAxis;
        uint IGamepadInputBinding.Bind => (uint)(bind + (int)SDL.GamepadButton.Count);
    }


    public interface IGamepadInputBinding
    {
        public uint Bind { get; }
        public GamepadBindingTypes Type { get; }


        public enum GamepadBindingTypes
        {
            GamepadButton,
            GamepadAxis
        }
    }


    public interface IKeyboardInputBinding
    {
        public uint Bind { get; }
        public KeyboardBindingTypes Type { get; }


        public enum KeyboardBindingTypes
        {
            ScanCode,
            KeyCode,
        }
    }






    private static List<InputInstance> InputInstances = new();




    public static void InputLoop()
    {
    
        lock (InputInstances)
        {
            foreach (var v in InputInstances)
                v.Update();
        }
    }




    /// <summary>
    /// Provides input checking/feedback for one particular input device; for example, the keyboard, or the mouse, or gamepad 0.
    /// <br/> The appropriate Get method will return null if acquisition failed.
    /// <br/> When you're done with the device, you should use <see cref="Freeable.Free"/>.
    /// <br/> Keep in mind that some devices are assumed to always be connected according to SDL, so creation can never fail, <see cref="IsDeviceConnected"/> will always return true, and Get is pointless.
    /// </summary>
    public abstract class InputInstance : Freeable
    {



        public InputInstance(uint bufferLength)
        {
            InputBufferA = new short[bufferLength];
            InputBufferB = new short[bufferLength];

            InputJustPressedEvents = new ThreadSafeEventAction[bufferLength];
            InputJustReleasedEvents = new ThreadSafeEventAction[bufferLength];


            lock (InputInstances)
                InputInstances.Add(this);
        }


        protected override void OnFree()
        {
            lock (InputInstances)
                InputInstances.Remove(this);
        }




        private short[] InputBufferA;
        private short[] InputBufferB;



        /// <summary>
        /// Updates the state. Called automatically
        /// </summary>
        public void Update()
        {
            var p = InputBufferA;
            InputBufferA = InputBufferB;
            InputBufferB = p;

            var connected = IsDeviceConnected();
            if (LastConnectedCheck != null)
            {
                LastConnectedCheck = connected;

                if (connected) OnDeviceConnectEvent.Invoke();
                else OnDeviceDisconnectEvent.Invoke();

            }

            PreReadUpdate();

            for (int i = 0; i < InputBufferA.Length; i++)
            {
                var read = InputBufferA[i] = ReadInput(i);


                if (InputJustPressedEvents[i] != null && (read != 0 && InputBufferB[i] == 0)) 
                    InputJustPressedEvents[i].Invoke();

                else if (InputJustReleasedEvents[i] != null && (read == 0 && InputBufferB[i] != 0))
                    InputJustReleasedEvents[i].Invoke();
            }


            
        }

        protected virtual void PreReadUpdate() { }



        protected abstract short ReadInput(int idx);


        public abstract bool IsDeviceConnected();



        public readonly ThreadSafeEventAction OnDeviceConnectEvent = new();
        public readonly ThreadSafeEventAction OnDeviceDisconnectEvent = new();


        private bool? LastConnectedCheck = null;



        protected short GetInput(int idx) => InputBufferA[idx];
        
        protected bool InputPressed(int idx) => InputBufferA[idx] != 0;

        protected bool InputJustPressed(int idx) => InputBufferA[idx] != 0 && InputBufferB[idx] == 0;

        protected bool InputJustReleased(int idx) => InputBufferA[idx] == 0 && InputBufferB[idx] != 0;



        protected ThreadSafeEventAction GetInputJustPressedEvent(int idx)
        {
            ref var get = ref InputJustPressedEvents[idx];
            get ??= new();
            return get;
        }

        protected ThreadSafeEventAction GetInputJustReleasedEvent(int idx)
        {
            ref var get = ref InputJustReleasedEvents[idx];
            get ??= new();
            return get;
        }



        private ThreadSafeEventAction[] InputJustPressedEvents;
        private ThreadSafeEventAction[] InputJustReleasedEvents;


    }






    public class GamepadInputInstance : InputInstance
    {

        private static readonly Dictionary<uint, GamepadInputInstance> Instances = new();

        public static GamepadInputInstance Get(uint gamepadIndex)
        {
            lock (Instances)
            {
                if (Instances.TryGetValue(gamepadIndex, out var ret))
                    return ret;

                var gamepad = SDL.OpenGamepad(gamepadIndex);

                if (gamepad != nint.Zero)
                {
                    ret = Instances[gamepadIndex] = new GamepadInputInstance(gamepad);
                    return ret;
                }
            }

            return null;
        }


        private GamepadInputInstance(nint gamepad) : base((uint)SDL.GamepadButton.Count + (uint)SDL.GamepadAxis.Count)
        {
            SDLGamepad = gamepad;
        }


        protected override void OnFree()
        {
            SDL.CloseGamepad(SDLGamepad);
            base.OnFree();
        } 




        public float AnalogInputDeadzone = 0.35f;


        private readonly nint SDLGamepad;



        public override bool IsDeviceConnected() => SDL.GamepadConnected(SDLGamepad);

        protected override short ReadInput(int idx)
        {
            if (idx <= (uint)SDL.GamepadButton.Count) 
                return SDL.GetGamepadButton(SDLGamepad, (SDL.GamepadButton)idx) ? short.MaxValue : (short)0;
            
            var val = SDL.GetGamepadAxis(SDLGamepad, (SDL.GamepadAxis)idx);
            return short.Abs(val) > (AnalogInputDeadzone * short.MaxValue) ? val : (short)0;

        }



        public bool ButtonPressed(SDL.GamepadButton button) => InputPressed((int)button);
        public bool ButtonJustPressed(SDL.GamepadButton button) => InputJustPressed((int)button);
        public bool ButtonJustReleased(SDL.GamepadButton button) => InputJustReleased((int)button);

        public ThreadSafeEventAction GetButtonJustPressedEvent(SDL.GamepadButton button) => GetInputJustPressedEvent((int)button);
        public ThreadSafeEventAction GetButtonJustReleasedEvent(SDL.GamepadButton button) => GetInputJustReleasedEvent((int)button);



        public bool AxisPressed(SDL.GamepadAxis axis) => InputPressed((int)axis + (int)SDL.GamepadButton.Count);
        public bool AxisJustPressed(SDL.GamepadAxis axis) => InputJustPressed((int)axis + (int)SDL.GamepadButton.Count);
        public bool AxisJustReleased(SDL.GamepadAxis axis) => InputJustReleased((int)axis + (int)SDL.GamepadButton.Count);

        public short ReadAxis(SDL.GamepadAxis axis) => GetInput((int)axis + (int)SDL.GamepadButton.Count);



        public void Rumble(ushort lowFrequency, ushort highFrequency, ushort timeMS)
            => SDL.RumbleGamepad(SDLGamepad, lowFrequency, highFrequency, timeMS);


    }


    /// <summary>
    /// Provides input checking/feedback based on enum bindings.
    /// </summary>
    public class MappedGamepadInputInstance<MappingEnum>(GamepadInputInstance inst) : IMappedInputInstance<MappingEnum> where MappingEnum : Enum
    {

        public readonly GamepadInputInstance Instance = inst;


        protected Dictionary<MappingEnum, IGamepadInputBinding[]> CurrentBindings;
        public void UpdateBindings(Dictionary<MappingEnum, IGamepadInputBinding[]> newBindings)
        {
            if (newBindings != null && newBindings.Count != 0)
                CurrentBindings = newBindings.ToDictionary();
        }



        public short GetAnalogAction(MappingEnum what)
        {
            var get = CurrentBindings[what];
            for (int i = 0; i < get.Length; i++)
            {
                var binding = get[i];
                if (binding.Type == GamepadBindingTypes.GamepadButton && Instance.ButtonPressed((SDL.GamepadButton)binding.Bind)) return short.MaxValue;
                if (binding.Type == GamepadBindingTypes.GamepadAxis)
                {
                    var read = Instance.ReadAxis((SDL.GamepadAxis)binding.Bind);
                    if (read != 0) return read;
                }
            }
            return 0;
        }



        public bool ActionPressed(MappingEnum what)
        {
            var get = CurrentBindings[what];
            for (int i = 0; i < get.Length; i++)
            {
                var binding = get[i];
                if (binding.Type == GamepadBindingTypes.GamepadButton && Instance.ButtonPressed((SDL.GamepadButton)binding.Bind)) return true;
                if (binding.Type == GamepadBindingTypes.GamepadAxis && Instance.AxisPressed((SDL.GamepadAxis)binding.Bind)) return true;
            }
            return false;
        }

        public bool ActionJustPressed(MappingEnum what)
        {
            var get = CurrentBindings[what];
            for (int i = 0; i < get.Length; i++)
            {
                var binding = get[i];
                if (binding.Type == GamepadBindingTypes.GamepadButton && Instance.ButtonJustPressed((SDL.GamepadButton)binding.Bind)) return true;
                if (binding.Type == GamepadBindingTypes.GamepadAxis && Instance.AxisJustPressed((SDL.GamepadAxis)binding.Bind)) return true;
            }
            return false;
        }

        public bool ActionJustReleased(MappingEnum what)
        {
            var get = CurrentBindings[what];
            for (int i = 0; i < get.Length; i++)
            {
                var binding = get[i];
                if (binding.Type == GamepadBindingTypes.GamepadButton && Instance.ButtonJustReleased((SDL.GamepadButton)binding.Bind)) return true;
                if (binding.Type == GamepadBindingTypes.GamepadAxis && Instance.AxisJustReleased((SDL.GamepadAxis)binding.Bind)) return true;
            }
            return false;
        }


    }










    /// <summary>
    /// <inheritdoc cref="InputInstance"/>
    /// </summary>
    public unsafe class KeyboardInputInstance : InputInstance
    {

        private static readonly KeyboardInputInstance Inst = new();
        public static KeyboardInputInstance Get() => Inst;



        private KeyboardInputInstance() : base((uint)SDL.Scancode.Count)
        {
        }


        public override bool IsDeviceConnected() => true;

        private bool* KeyboardState;
        private uint KeyboardKeyLength;

        protected override void PreReadUpdate()
        {
            ReadOnlySpan<bool> span = SDL.GetKeyboardState(out int keyCount);

            if (!span.IsEmpty)
            {
                ref bool first = ref MemoryMarshal.GetReference(span);
                fixed (bool* ptr = &first)
                {
                    KeyboardState = ptr;
                    KeyboardKeyLength = (uint)keyCount;
                }
            }
            else
            {
                KeyboardState = null;
                KeyboardKeyLength = 0;
            }
        }

        protected override short ReadInput(int idx)
            => KeyboardState[idx] ? short.MaxValue : (short)0;



        public bool KeyPressed(SDL.Scancode key) => InputPressed((int)key);
        public bool KeyJustPressed(SDL.Scancode key) => InputJustPressed((int)key);
        public bool KeyJustReleased(SDL.Scancode key) => InputJustReleased((int)key);


        public ThreadSafeEventAction GetKeyJustPressedEvent(SDL.Scancode key) => GetInputJustPressedEvent((int)key);
        public ThreadSafeEventAction GetKeyJustReleasedEvent(SDL.Scancode key) => GetInputJustReleasedEvent((int)key);




        public short GetAxisFromTwo(SDL.Scancode negative, SDL.Scancode positive)
        {
            return KeyPressed(negative) ? short.MinValue : KeyPressed(positive) ? short.MaxValue : (short)0;
        }

        public Vector2<short> GetAxisFromFour(SDL.Scancode negativeX, SDL.Scancode positiveX, SDL.Scancode negativeY, SDL.Scancode positiveY)
        {
            return new Vector2<short>(GetAxisFromTwo(negativeX, positiveX), GetAxisFromTwo(negativeY, positiveY));
        }





        /// <summary>
        /// <b>This performs a conversion to scancode and may be unreliable. Consider using <see cref="KeyPressed(SDL.Scancode)"/> or similar instead for regular inputs, and <see cref="Window.StartTextInput"/> for text entry.</b>
        /// </summary>
        /// <param name="key"></param>
        /// <returns></returns>
        public bool KeyPressed(SDL.Keycode key)
        {
            var sc = SDL.GetScancodeFromKey(key, out var _);
            return sc != SDL.Scancode.Unknown && KeyPressed(sc);
        }

        /// <summary>
        /// <inheritdoc cref="KeyPressed(SDL.Keycode)"/>
        /// </summary>
        /// <param name="key"></param>
        /// <returns></returns>
        public bool KeyJustPressed(SDL.Keycode key)
        {
            var sc = SDL.GetScancodeFromKey(key, out var _);
            return sc != SDL.Scancode.Unknown && KeyJustPressed(sc);
        }

        /// <summary>
        /// <inheritdoc cref="KeyPressed(SDL.Keycode)"/>
        /// </summary>
        /// <param name="key"></param>
        /// <returns></returns>
        public bool KeyJustReleased(SDL.Keycode key)
        {
            var sc = SDL.GetScancodeFromKey(key, out var _);
            return sc != SDL.Scancode.Unknown && KeyJustReleased(sc);
        }


    }


    /// <summary>
    /// Provides input checking/feedback based on enum bindings.
    /// </summary>
    public class MappedKeyboardInputInstance<MappingEnum>(KeyboardInputInstance inst) : IMappedInputInstance<MappingEnum> where MappingEnum : Enum
    {

        public readonly KeyboardInputInstance Instance = inst;


        protected Dictionary<MappingEnum, IKeyboardInputBinding[]> CurrentBindings;
        public void UpdateBindings(Dictionary<MappingEnum, IKeyboardInputBinding[]> newBindings)
        {
            if (newBindings != null && newBindings.Count != 0)
                CurrentBindings = newBindings.ToDictionary();
        }


        public short GetAnalogAction(MappingEnum what) => ActionPressed(what) ? short.MaxValue : (short)0;

        public bool ActionPressed(MappingEnum what)
        {
            var get = CurrentBindings[what];
            for (int i = 0; i < get.Length; i++)
            {
                var binding = get[i];
                if (binding.Type == KeyboardBindingTypes.ScanCode && Instance.KeyPressed((SDL.Scancode)binding.Bind)) return true;
                if (binding.Type == KeyboardBindingTypes.KeyCode && Instance.KeyPressed((SDL.Keycode)binding.Bind)) return true;
            }
            return false;
        }

        public bool ActionJustPressed(MappingEnum what)
        {
            var get = CurrentBindings[what];
            for (int i = 0; i < get.Length; i++)
            {
                var binding = get[i];
                if (binding.Type == KeyboardBindingTypes.ScanCode && Instance.KeyJustPressed((SDL.Scancode)binding.Bind)) return true;
                if (binding.Type == KeyboardBindingTypes.KeyCode && Instance.KeyJustPressed((SDL.Keycode)binding.Bind)) return true;
            }
            return false;
        }

        public bool ActionJustReleased(MappingEnum what)
        {
            var get = CurrentBindings[what];
            for (int i = 0; i < get.Length; i++)
            {
                var binding = get[i];
                if (binding.Type == KeyboardBindingTypes.ScanCode && Instance.KeyJustReleased((SDL.Scancode)binding.Bind)) return true;
                if (binding.Type == KeyboardBindingTypes.KeyCode && Instance.KeyJustReleased((SDL.Keycode)binding.Bind)) return true;
            }
            return false;
        }
    }





    /// <summary>
    /// <inheritdoc cref="InputInstance"/>
    /// </summary>
    public unsafe class MouseInputInstance : InputInstance
    {

        private static readonly MouseInputInstance Inst = new();
        public static MouseInputInstance Get() => Inst;

        private MouseInputInstance() : base((uint)SDL.MouseButtonFlags.X2 + 1 + 1)
        {
        }


        public override bool IsDeviceConnected() => true;

        private SDL.MouseButtonFlags MouseState;
        private float MouseX, MouseY;
        private float MouseDeltaX, MouseDeltaY;

        protected override void PreReadUpdate()
        {
            MouseState = SDL.GetMouseState(out MouseX, out MouseY);

            SDL.GetRelativeMouseState(out MouseDeltaX, out MouseDeltaY);
        }


        protected override short ReadInput(int idx)
            => ((MouseState & (SDL.MouseButtonFlags)idx) != 0) ? short.MaxValue : (short)0;


        public Vector2 GetWindowRelativeMousePosition() => new(MouseX, MouseY);


        public float GetMouseScrollDelta() => Window.MouseScrollWheelDelta;
        public Vector2 GetMousePositionDelta() => new Vector2(MouseDeltaX, MouseDeltaY) / Logic.Delta;


        public void SetMouseRelative(bool enabled) => Window.MouseModeRelative = enabled;
        public void SetMouseVisible(bool visible)
        {
            if (visible) SDL.ShowCursor();
            else SDL.HideCursor();
        }




        public ThreadSafeEventAction GetMouseButtonJustPressedEvent(SDL.MouseButtonFlags button) => GetInputJustPressedEvent((int)button);
        public ThreadSafeEventAction GetMouseButtonJustReleasedEvent(SDL.MouseButtonFlags button) => GetInputJustReleasedEvent((int)button);



        public bool MouseButtonPressed(SDL.MouseButtonFlags button) => InputPressed((int)button);
        public bool MouseButtonJustPressed(SDL.MouseButtonFlags button) => InputJustPressed((int)button);
        public bool MouseButtonJustReleased(SDL.MouseButtonFlags button) => InputJustReleased((int)button);
    }





    /// <summary>
    /// Provides input checking/feedback based on enum bindings.
    /// </summary>
    public class MappedMouseInputInstance<MappingEnum>(MouseInputInstance inst) : IMappedInputInstance<MappingEnum> where MappingEnum : Enum
    {

        public readonly MouseInputInstance Instance = inst;


        protected Dictionary<MappingEnum, IKeyboardInputBinding[]> CurrentBindings;
        public void UpdateBindings(Dictionary<MappingEnum, IKeyboardInputBinding[]> newBindings)
        {
            if (newBindings != null && newBindings.Count != 0)
                CurrentBindings = newBindings.ToDictionary();
        }



        public short GetAnalogAction(MappingEnum what) => ActionPressed(what) ? short.MaxValue : (short)0;


        public bool ActionPressed(MappingEnum what)
        {
            var get = CurrentBindings[what];
            for (int i = 0; i < get.Length; i++)
                if (Instance.MouseButtonPressed((SDL.MouseButtonFlags)get[i].Bind)) return true;

            return false;
        }

        public bool ActionJustPressed(MappingEnum what)
        {
            var get = CurrentBindings[what];
            for (int i = 0; i < get.Length; i++)
                if (Instance.MouseButtonJustPressed((SDL.MouseButtonFlags)get[i].Bind)) return true;

            return false;
        }

        public bool ActionJustReleased(MappingEnum what)
        {
            var get = CurrentBindings[what];
            for (int i = 0; i < get.Length; i++)
                if (Instance.MouseButtonJustReleased((SDL.MouseButtonFlags)get[i].Bind)) return true;

            return false;
        }
    }



    public interface IMappedInputInstance<MappingEnum> where MappingEnum : Enum
    {

        public short GetAnalogAction(MappingEnum what);

        public bool ActionPressed(MappingEnum what);

        public bool ActionJustPressed(MappingEnum what);

        public bool ActionJustReleased(MappingEnum what);
    }








    /// <summary>
    /// Provides input checking/feedback for multiple logically related devices (for example, keyboard + mouse + gamepad 0, to form the basis for interchangeable input support for a single player)
    /// </summary>
    public class CombinedMappedInputInstances<MappingEnum> where MappingEnum : Enum
    {

        public CombinedMappedInputInstances(IMappedInputInstance<MappingEnum>[] instances)
        {
            Instances = instances;
        }

        private IMappedInputInstance<MappingEnum>[] Instances;


        public short ReadAction(MappingEnum what)
        {
            for (int i = 0; i < Instances.Length; i++)
            {
                var read = Instances[i].GetAnalogAction(what);
                if (read != 0) return read;
            }
            return 0;
        }

        public bool ActionJustPressed(MappingEnum what)
        {
            for (int i = 0; i < Instances.Length; i++)
            {
                if (Instances[i].ActionJustPressed(what)) return true;
            }
            return false;
        }

        public bool ActionJustReleased(MappingEnum what)
        {
            for (int i = 0; i < Instances.Length; i++)
            {
                if (Instances[i].ActionJustReleased(what)) return true;
            }
            return false;
        }

        public bool ActionPressed(MappingEnum what)
        {
            for (int i = 0; i < Instances.Length; i++)
            {
                if (Instances[i].ActionPressed(what)) return true;
            }
            return false;
        }

    }



}