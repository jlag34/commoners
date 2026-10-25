using UnityEngine;
#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
using UnityEngine.InputSystem;
#endif

namespace SwarmPrototype
{
    // Works whether the project uses the old Input Manager or the new Input System
    // package (the default in new Unity 6 projects).
    public static class InputShim
    {
#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
        public static Vector2 MousePosition => Mouse.current != null ? Mouse.current.position.ReadValue() : Vector2.zero;
        public static bool RightClickDown => Mouse.current != null && Mouse.current.rightButton.wasPressedThisFrame;
        public static bool LeftClickDown => Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame;
        public static bool CancelPressed => Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame;
        public static bool ShopPressed => Keyboard.current != null && Keyboard.current.bKey.wasPressedThisFrame;
        public static bool StatsPressed => Keyboard.current != null && Keyboard.current.tabKey.wasPressedThisFrame;
        public static bool DebugPressed => Keyboard.current != null && Keyboard.current.f1Key.wasPressedThisFrame;
        public static bool GearPressed => Keyboard.current != null && Keyboard.current.f2Key.wasPressedThisFrame;
        // Roughly 1 per mouse-wheel notch; a trackpad gives a stream of small values.
        public static float Scroll => Mouse.current != null ? Mouse.current.scroll.ReadValue().y / 120f : 0f;
        public static bool RestartPressed => Keyboard.current != null && Keyboard.current.rKey.wasPressedThisFrame;
        public static bool MenuPressed => Keyboard.current != null && Keyboard.current.mKey.wasPressedThisFrame;
        public static bool ConfirmPressed => Keyboard.current != null && (Keyboard.current.enterKey.wasPressedThisFrame || Keyboard.current.numpadEnterKey.wasPressedThisFrame);
        public static bool FocusPressed => Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame;
        public static bool EnemyFocusPressed => Keyboard.current != null && Keyboard.current.eKey.wasPressedThisFrame;
        public static bool MiddleHeld => Mouse.current != null && Mouse.current.middleButton.isPressed;

        public static Vector2 Pan
        {
            get
            {
                var k = Keyboard.current;
                if (k == null) return Vector2.zero;
                float x = (k.dKey.isPressed || k.rightArrowKey.isPressed ? 1f : 0f) - (k.aKey.isPressed || k.leftArrowKey.isPressed ? 1f : 0f);
                float y = (k.wKey.isPressed || k.upArrowKey.isPressed ? 1f : 0f) - (k.sKey.isPressed || k.downArrowKey.isPressed ? 1f : 0f);
                return new Vector2(x, y);
            }
        }
#else
        public static Vector2 MousePosition => Input.mousePosition;
        public static bool RightClickDown => Input.GetMouseButtonDown(1);
        public static bool LeftClickDown => Input.GetMouseButtonDown(0);
        public static bool CancelPressed => Input.GetKeyDown(KeyCode.Escape);
        public static bool ShopPressed => Input.GetKeyDown(KeyCode.B);
        public static bool StatsPressed => Input.GetKeyDown(KeyCode.Tab);
        public static bool DebugPressed => Input.GetKeyDown(KeyCode.F1);
        public static bool GearPressed => Input.GetKeyDown(KeyCode.F2);
        // Roughly 1 per mouse-wheel notch; a trackpad gives a stream of small values.
        public static float Scroll => Input.mouseScrollDelta.y;
        public static bool RestartPressed => Input.GetKeyDown(KeyCode.R);
        public static bool MenuPressed => Input.GetKeyDown(KeyCode.M);
        public static bool ConfirmPressed => Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter);
        public static bool FocusPressed => Input.GetKeyDown(KeyCode.Space);
        public static bool EnemyFocusPressed => Input.GetKeyDown(KeyCode.E);
        public static bool MiddleHeld => Input.GetMouseButton(2);

        public static Vector2 Pan
        {
            get
            {
                float x = (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow) ? 1f : 0f) - (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow) ? 1f : 0f);
                float y = (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow) ? 1f : 0f) - (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow) ? 1f : 0f);
                return new Vector2(x, y);
            }
        }
#endif
    }
}
