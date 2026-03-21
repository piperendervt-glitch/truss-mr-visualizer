using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.XR;

public class DebugDisplay : MonoBehaviour
{
    public GameObject[] shapes;
    public AxisDisplay axisDisplay;
    public RotationSnapshot rotationSnapshot;
    public int activeIndex;

    bool visible = true;
    bool prevYButton;
    string debugText = "";

    // FPS
    int frameCount;
    float fpsTimer;
    float currentFps;

    // GUI style (created once)
    GUIStyle guiStyle;

    void Update()
    {
        bool toggleDown = false;

#if UNITY_EDITOR
        var kb = Keyboard.current;
        if (kb != null && kb.fKey.wasPressedThisFrame) toggleDown = true;
#else
        var leftCtrl = XRController.leftHand;
        if (leftCtrl != null)
        {
            var yBtn = leftCtrl.TryGetChildControl<ButtonControl>("secondaryButton");
            if (yBtn != null)
            {
                bool p = yBtn.isPressed;
                if (p && !prevYButton) toggleDown = true;
                prevYButton = p;
            }
        }
#endif

        if (toggleDown) visible = !visible;
        if (!visible) return;

        // FPS
        frameCount++;
        fpsTimer += Time.unscaledDeltaTime;
        if (fpsTimer >= 1f)
        {
            currentFps = frameCount / fpsTimer;
            frameCount = 0;
            fpsTimer = 0f;
        }

        // Build debug text
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"FPS: {currentFps:F1}");

        GameObject activeShape = GetActiveShape();
        string shapeName = "?";
        float[] angles = null;
        int speedLevel = 1;
        string[] planeNames = null;

        if (activeShape != null)
        {
            var tess = activeShape.GetComponent<Tesseract>();
            if (tess != null)
            {
                shapeName = "Tesseract";
                angles = tess.GetAngles();
                speedLevel = tess.speedLevel;
                planeNames = Tesseract.planeNames;
            }
            var hexa = activeShape.GetComponent<Hexadecachoron>();
            if (hexa != null)
            {
                shapeName = "Hexadecachoron";
                angles = hexa.GetAngles();
                speedLevel = hexa.speedLevel;
                planeNames = Hexadecachoron.planeNames;
            }
        }

        string[] speedNames = { "Slow", "Normal", "Fast" };
        sb.AppendLine($"Shape: {shapeName}  Speed: {speedNames[speedLevel]}");

        if (angles != null && planeNames != null)
        {
            sb.Append("Angles: ");
            for (int i = 0; i < 6; i++)
            {
                float deg = angles[i] * Mathf.Rad2Deg % 360f;
                sb.Append($"{planeNames[i]}:{deg:F0} ");
            }
            sb.AppendLine();
        }

        if (axisDisplay != null)
        {
            sb.Append("Axis dist: ");
            string[] axNames = { "X", "Y", "Z", "W" };
            for (int i = 0; i < 4; i++)
                sb.Append($"{axNames[i]}:{axisDisplay.GetAxisDistance(i):F2} ");
            sb.AppendLine();
        }

        if (rotationSnapshot != null)
            sb.AppendLine(rotationSnapshot.GetSlotDisplay());

        debugText = sb.ToString();
    }

    void OnGUI()
    {
        if (!visible) return;

        if (guiStyle == null)
        {
            guiStyle = new GUIStyle(GUI.skin.label);
            guiStyle.fontSize = 24;
            guiStyle.normal.textColor = Color.white;
            guiStyle.wordWrap = true;
        }

        // Semi-transparent background
        GUI.color = new Color(0f, 0f, 0f, 0.5f);
        GUI.DrawTexture(new Rect(10, 10, 500, 220), Texture2D.whiteTexture);
        GUI.color = Color.white;

        GUI.Label(new Rect(15, 15, 490, 210), debugText, guiStyle);
    }

    GameObject GetActiveShape()
    {
        if (shapes == null || activeIndex < 0 || activeIndex >= shapes.Length)
            return null;
        var s = shapes[activeIndex];
        return (s != null && s.activeInHierarchy) ? s : null;
    }
}
