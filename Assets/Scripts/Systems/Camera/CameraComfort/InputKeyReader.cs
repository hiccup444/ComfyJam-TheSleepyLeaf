// Assets/Scripts/CameraComfort/InputKeyReader.cs
using UnityEngine;

#if USE_INPUT_SYSTEM
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
#endif

namespace Comfy.Camera
{
    /// <summary>
    /// Helper that bridges legacy <see cref="Input"/> and the new Input System, ensuring consistent KeyCode reads.
    /// </summary>
    public static class InputKeyReader
    {
        public static bool GetKeyDown(KeyCode key)
        {
            if (key == KeyCode.None)
                return false;

#if USE_INPUT_SYSTEM
            var keyboard = Keyboard.current;
            if (keyboard != null && TryGetKeyControl(keyboard, key, out var control))
                return control.wasPressedThisFrame;
#endif
            return Input.GetKeyDown(key);
        }

        public static string DescribeKey(KeyCode key)
        {
            if (key == KeyCode.None)
                return "None";

            if (key >= KeyCode.Alpha0 && key <= KeyCode.Alpha9)
            {
                int digit = key - KeyCode.Alpha0;
                return $"{digit} ({key})";
            }

            if (key >= KeyCode.Keypad0 && key <= KeyCode.Keypad9)
            {
                int digit = key - KeyCode.Keypad0;
                return $"{digit} (Numpad{digit})";
            }

            return key.ToString();
        }

#if USE_INPUT_SYSTEM
        static bool TryGetKeyControl(Keyboard keyboard, KeyCode key, out KeyControl control)
        {
            control = key switch
            {
                KeyCode.A => keyboard.aKey,
                KeyCode.B => keyboard.bKey,
                KeyCode.C => keyboard.cKey,
                KeyCode.D => keyboard.dKey,
                KeyCode.E => keyboard.eKey,
                KeyCode.F => keyboard.fKey,
                KeyCode.G => keyboard.gKey,
                KeyCode.H => keyboard.hKey,
                KeyCode.I => keyboard.iKey,
                KeyCode.J => keyboard.jKey,
                KeyCode.K => keyboard.kKey,
                KeyCode.L => keyboard.lKey,
                KeyCode.M => keyboard.mKey,
                KeyCode.N => keyboard.nKey,
                KeyCode.O => keyboard.oKey,
                KeyCode.P => keyboard.pKey,
                KeyCode.Q => keyboard.qKey,
                KeyCode.R => keyboard.rKey,
                KeyCode.S => keyboard.sKey,
                KeyCode.T => keyboard.tKey,
                KeyCode.U => keyboard.uKey,
                KeyCode.V => keyboard.vKey,
                KeyCode.W => keyboard.wKey,
                KeyCode.X => keyboard.xKey,
                KeyCode.Y => keyboard.yKey,
                KeyCode.Z => keyboard.zKey,

                KeyCode.Alpha0 => keyboard.digit0Key,
                KeyCode.Alpha1 => keyboard.digit1Key,
                KeyCode.Alpha2 => keyboard.digit2Key,
                KeyCode.Alpha3 => keyboard.digit3Key,
                KeyCode.Alpha4 => keyboard.digit4Key,
                KeyCode.Alpha5 => keyboard.digit5Key,
                KeyCode.Alpha6 => keyboard.digit6Key,
                KeyCode.Alpha7 => keyboard.digit7Key,
                KeyCode.Alpha8 => keyboard.digit8Key,
                KeyCode.Alpha9 => keyboard.digit9Key,

                KeyCode.Keypad0 => keyboard.numpad0Key,
                KeyCode.Keypad1 => keyboard.numpad1Key,
                KeyCode.Keypad2 => keyboard.numpad2Key,
                KeyCode.Keypad3 => keyboard.numpad3Key,
                KeyCode.Keypad4 => keyboard.numpad4Key,
                KeyCode.Keypad5 => keyboard.numpad5Key,
                KeyCode.Keypad6 => keyboard.numpad6Key,
                KeyCode.Keypad7 => keyboard.numpad7Key,
                KeyCode.Keypad8 => keyboard.numpad8Key,
                KeyCode.Keypad9 => keyboard.numpad9Key,

                KeyCode.Space => keyboard.spaceKey,
                KeyCode.Tab => keyboard.tabKey,
                KeyCode.Escape => keyboard.escapeKey,
                KeyCode.Backspace => keyboard.backspaceKey,
                KeyCode.Delete => keyboard.deleteKey,
                KeyCode.Return => keyboard.enterKey,
                KeyCode.KeypadEnter => keyboard.numpadEnterKey,
                KeyCode.LeftShift => keyboard.leftShiftKey,
                KeyCode.RightShift => keyboard.rightShiftKey,
                KeyCode.LeftControl => keyboard.leftCtrlKey,
                KeyCode.RightControl => keyboard.rightCtrlKey,
                KeyCode.LeftAlt => keyboard.leftAltKey,
                KeyCode.RightAlt => keyboard.rightAltKey,
                KeyCode.LeftArrow => keyboard.leftArrowKey,
                KeyCode.RightArrow => keyboard.rightArrowKey,
                KeyCode.UpArrow => keyboard.upArrowKey,
                KeyCode.DownArrow => keyboard.downArrowKey,
                KeyCode.Home => keyboard.homeKey,
                KeyCode.End => keyboard.endKey,
                KeyCode.PageUp => keyboard.pageUpKey,
                KeyCode.PageDown => keyboard.pageDownKey,
                KeyCode.Insert => keyboard.insertKey,
                KeyCode.F1 => keyboard.f1Key,
                KeyCode.F2 => keyboard.f2Key,
                KeyCode.F3 => keyboard.f3Key,
                KeyCode.F4 => keyboard.f4Key,
                KeyCode.F5 => keyboard.f5Key,
                KeyCode.F6 => keyboard.f6Key,
                KeyCode.F7 => keyboard.f7Key,
                KeyCode.F8 => keyboard.f8Key,
                KeyCode.F9 => keyboard.f9Key,
                KeyCode.F10 => keyboard.f10Key,
                KeyCode.F11 => keyboard.f11Key,
                KeyCode.F12 => keyboard.f12Key,
                _ => null
            };

            return control != null;
        }
#endif
    }
}
