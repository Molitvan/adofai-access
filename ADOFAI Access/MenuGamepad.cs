using System.Collections.Generic;
using Rewired;
using UnityEngine;

namespace ADOFAI_Access
{
    // Controller input for the mod's custom menus, matching the main game's feel.
    //
    // Directional navigation comes from two sources OR'd together:
    //   1. the game's own joystick action mappings (RDInput.joystickInputs[i].Up/Down/Left/Right) -
    //      this covers the D-Pad and the analog stick wherever the current scene's map category binds
    //      them (level select, custom levels, pause, ...). Because CustomMenuInputGuard zeroes
    //      RDInputType.isActive to suppress game input while a menu is open, isActive is temporarily
    //      re-enabled for the read (the static RDInput.GetState path stays blocked).
    //   2. the Rewired gamepad template D-Pad - scene-independent, so navigation still works in
    //      categories that don't bind directional actions (e.g. the "Gameplay" map used on the
    //      press-to-begin screen, which has no Up/Down action).
    //
    // The four face buttons are always read from the template (the game does not map them as joystick
    // actions). Layout matches the main game's B = confirm / A = back convention:
    //   B (circle, right) -> confirm,  A (cross, bottom) -> back,
    //   X (square, left)  -> open F5,   Y (triangle, top) -> open F6.
    // Template reads give a live value, so rising edges are tracked here.
    internal static class MenuGamepad
    {
        private struct TemplateState
        {
            public bool DpadUp;
            public bool DpadDown;
            public bool DpadLeft;
            public bool DpadRight;
            public bool A;
            public bool B;
            public bool X;
            public bool Y;
        }

        private static int _frame = -1;
        private static bool _up;
        private static bool _down;
        private static bool _left;
        private static bool _right;
        private static bool _confirm;
        private static bool _cancel;
        private static bool _openAccessibleMenu;
        private static bool _openSettings;
        private static readonly Dictionary<int, TemplateState> PreviousByJoystick = new Dictionary<int, TemplateState>();

        public static bool UpPressed() { EnsureFrame(); return _up; }
        public static bool DownPressed() { EnsureFrame(); return _down; }
        public static bool LeftPressed() { EnsureFrame(); return _left; }
        public static bool RightPressed() { EnsureFrame(); return _right; }
        public static bool ConfirmPressed() { EnsureFrame(); return _confirm; }
        public static bool CancelPressed() { EnsureFrame(); return _cancel; }
        public static bool OpenAccessibleMenuPressed() { EnsureFrame(); return _openAccessibleMenu; }
        public static bool OpenSettingsPressed() { EnsureFrame(); return _openSettings; }

        private static void EnsureFrame()
        {
            int frame = Time.frameCount;
            if (frame == _frame)
            {
                return;
            }

            _frame = frame;
            _up = _down = _left = _right = false;
            _confirm = _cancel = _openAccessibleMenu = _openSettings = false;

            // Action-based directions only matter (and isActive is only zeroed) while a mod menu is open.
            if (CustomMenuInputGuard.ShouldBlockInput)
            {
                ReadActionDirections();
            }

            ReadTemplateEdges();
        }

        private static void ReadActionDirections()
        {
            RDInputType_Joystick[] inputs = RDInput.joystickInputs;
            if (inputs == null)
            {
                return;
            }

            for (int i = 0; i < inputs.Length; i++)
            {
                RDInputType_Joystick joystick = inputs[i];
                if (joystick == null)
                {
                    continue;
                }

                bool previousActive = joystick.isActive;
                joystick.isActive = true;
                try
                {
                    if (joystick.Up(ButtonState.WentDown)) _up = true;
                    if (joystick.Down(ButtonState.WentDown)) _down = true;
                    if (joystick.Left(ButtonState.WentDown)) _left = true;
                    if (joystick.Right(ButtonState.WentDown)) _right = true;
                }
                catch
                {
                    // Reading raw input must never break the menu loop.
                }
                finally
                {
                    joystick.isActive = previousActive;
                }
            }
        }

        private static void ReadTemplateEdges()
        {
            try
            {
                IList<Joystick> joysticks = ReInput.controllers.Joysticks;
                for (int i = 0; i < joysticks.Count; i++)
                {
                    Joystick joystick = joysticks[i];
                    if (joystick == null)
                    {
                        continue;
                    }

                    IGamepadTemplate gamepad = joystick.GetTemplate<IGamepadTemplate>();
                    if (gamepad == null)
                    {
                        continue;
                    }

                    TemplateState current = ReadTemplate(gamepad);
                    int id = joystick.id;
                    PreviousByJoystick.TryGetValue(id, out TemplateState previous);

                    if (current.DpadUp && !previous.DpadUp) _up = true;
                    if (current.DpadDown && !previous.DpadDown) _down = true;
                    if (current.DpadLeft && !previous.DpadLeft) _left = true;
                    if (current.DpadRight && !previous.DpadRight) _right = true;
                    if (current.B && !previous.B) _confirm = true;
                    if (current.A && !previous.A) _cancel = true;
                    if (current.Y && !previous.Y) _openAccessibleMenu = true;
                    if (current.X && !previous.X) _openSettings = true;

                    PreviousByJoystick[id] = current;
                }
            }
            catch
            {
                // Template support varies by controller; degrade to no-op if unavailable.
            }
        }

        private static TemplateState ReadTemplate(IGamepadTemplate gamepad)
        {
            bool dpadUp = false;
            bool dpadDown = false;
            bool dpadLeft = false;
            bool dpadRight = false;
            IControllerTemplateDPad dpad = gamepad.dPad;
            if (dpad != null)
            {
                dpadUp = dpad.up != null && dpad.up.value;
                dpadDown = dpad.down != null && dpad.down.value;
                dpadLeft = dpad.left != null && dpad.left.value;
                dpadRight = dpad.right != null && dpad.right.value;
            }

            return new TemplateState
            {
                DpadUp = dpadUp,
                DpadDown = dpadDown,
                DpadLeft = dpadLeft,
                DpadRight = dpadRight,
                A = gamepad.a != null && gamepad.a.value,
                B = gamepad.b != null && gamepad.b.value,
                X = gamepad.x != null && gamepad.x.value,
                Y = gamepad.y != null && gamepad.y.value
            };
        }
    }
}
