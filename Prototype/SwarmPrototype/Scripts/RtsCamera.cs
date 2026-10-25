using UnityEngine;

namespace SwarmPrototype
{
    // Steep top-down camera in the Warcraft 3 style. WASD/arrows, the screen edges or a middle-mouse drag
    // pan; scroll zooms; Space jumps home and E cycles the enemy bases. The view
    // stays inside the map.
    public class RtsCamera : MonoBehaviour
    {
        const float Pitch = 62f;
        const float PanSpeed = 30f;
        // Zoom is multiplicative (this factor per wheel notch) and eased toward a
        // target, so a trackpad's stream of tiny scroll events stays gentle.
        const float ZoomPerNotch = 1.1f;
        const float MaxNotchesPerFrame = 2f;
        const float ZoomSmoothing = 12f;
        const float MinHeight = 12f;
        const float MaxHeight = 100f;
        const float EdgeSize = 12f;

        Vector3 focus;
        Vector3 home;
        Vector3[] enemies = new Vector3[0];
        int enemyIndex = -1;
        float limit = 60f;
        float height = 36f;
        float zoomTarget = 36f;
        Vector3 lastMouse;
        bool dragging;

        public void FocusOn(Vector3 point)
        {
            focus = point;
            focus.x = Mathf.Clamp(focus.x, -limit, limit);
            focus.z = Mathf.Clamp(focus.z, -limit, limit);
            Apply();
        }

        public void SetHome(Vector3 point, Vector3[] enemyPoints, float mapSize)
        {
            home = point;
            enemies = enemyPoints;
            enemyIndex = -1;
            focus = point;
            limit = mapSize * 0.5f;
            Apply();
        }

        void LateUpdate()
        {
            if (SocialOverlay.IsOpen || SocialOverlay.IsTyping) { dragging = false; return; }
            Vector2 pan = InputShim.Pan + EdgePan();
            float speed = PanSpeed * (height / 30f);
            focus += new Vector3(pan.x, 0f, pan.y) * speed * Time.unscaledDeltaTime;
            DragPan();

            float scroll = Mathf.Clamp(InputShim.Scroll, -MaxNotchesPerFrame, MaxNotchesPerFrame);
            if (scroll != 0f) zoomTarget = Mathf.Clamp(zoomTarget * Mathf.Pow(ZoomPerNotch, -scroll), MinHeight, MaxHeight);
            height = Mathf.Lerp(height, zoomTarget, 1f - Mathf.Exp(-ZoomSmoothing * Time.unscaledDeltaTime));

            if (InputShim.FocusPressed) focus = home;
            if (InputShim.EnemyFocusPressed && enemies.Length > 0)
            {
                enemyIndex = (enemyIndex + 1) % enemies.Length;
                focus = enemies[enemyIndex];
            }

            focus.x = Mathf.Clamp(focus.x, -limit, limit);
            focus.z = Mathf.Clamp(focus.z, -limit, limit);
            Apply();
        }

        // Only while the game window has focus and the mouse is inside it, so the
        // camera doesn't drift when you're clicking around elsewhere.
        static Vector2 EdgePan()
        {
            if (!Application.isFocused) return Vector2.zero;
            Vector2 m = InputShim.MousePosition;
            if (m.x < 0f || m.y < 0f || m.x > Screen.width || m.y > Screen.height) return Vector2.zero;
            float x = m.x < EdgeSize ? -1f : m.x > Screen.width - EdgeSize ? 1f : 0f;
            float y = m.y < EdgeSize ? -1f : m.y > Screen.height - EdgeSize ? 1f : 0f;
            return new Vector2(x, y);
        }

        // Grab the ground with the middle mouse button and drag it.
        void DragPan()
        {
            Vector3 mouse = InputShim.MousePosition;
            if (InputShim.MiddleHeld)
            {
                if (dragging)
                {
                    Vector3 delta = mouse - lastMouse;
                    // Roughly how many meters one pixel covers at this zoom.
                    float metersPerPixel = height * 2f / Mathf.Max(1, Screen.height);
                    focus -= new Vector3(delta.x, 0f, delta.y) * metersPerPixel;
                }
                dragging = true;
            }
            else dragging = false;
            lastMouse = mouse;
        }

        void Apply()
        {
            Quaternion rotation = Quaternion.Euler(Pitch, 0f, 0f);
            float back = height / Mathf.Tan(Pitch * Mathf.Deg2Rad);
            transform.SetPositionAndRotation(focus + new Vector3(0f, height, -back), rotation);
        }
    }
}
