using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.XR;
using TMPro;

public class DebugDisplay : MonoBehaviour
{
    public GameObject[] shapes;
    public AxisDisplay axisDisplay;
    public RotationSnapshot rotationSnapshot;
    public int activeIndex;

    TextMeshPro debugLabel;
    bool visible;
    bool prevYButton;

    // FPS
    int frameCount;
    float fpsTimer;
    float currentFps;

    void Start()
    {
        var go = new GameObject("DebugLabel");
        go.transform.SetParent(transform, false);
        debugLabel = go.AddComponent<TextMeshPro>();
        debugLabel.fontSize = 0.04f;
        debugLabel.alignment = TextAlignmentOptions.TopLeft;
        debugLabel.color = Color.white;
        debugLabel.rectTransform.sizeDelta = new Vector2(3f, 2f);
        debugLabel.gameObject.SetActive(false);
    }

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

        if (toggleDown)
        {
            visible = !visible;
            debugLabel.gameObject.SetActive(visible);
        }

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

        // Position below shape label
        GameObject activeShape = GetActiveShape();
        var cam = Camera.main;
        if (cam != null && activeShape != null)
        {
            debugLabel.transform.position = activeShape.transform.position + Vector3.down * 0.35f;
            debugLabel.transform.LookAt(cam.transform);
            debugLabel.transform.Rotate(0f, 180f, 0f);
        }

        // Build debug text
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"FPS: {currentFps:F1}");

        // Shape info
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

        // Angles
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

        // Axis distances
        if (axisDisplay != null)
        {
            sb.Append("Axis dist: ");
            string[] axNames = { "X", "Y", "Z", "W" };
            for (int i = 0; i < 4; i++)
                sb.Append($"{axNames[i]}:{axisDisplay.GetAxisDistance(i):F2} ");
            sb.AppendLine();
        }

        // Snapshot
        if (rotationSnapshot != null)
            sb.AppendLine(rotationSnapshot.GetSlotDisplay());

        debugLabel.text = sb.ToString();
    }

    GameObject GetActiveShape()
    {
        if (shapes == null || activeIndex < 0 || activeIndex >= shapes.Length)
            return null;
        var s = shapes[activeIndex];
        return (s != null && s.activeInHierarchy) ? s : null;
    }
}
