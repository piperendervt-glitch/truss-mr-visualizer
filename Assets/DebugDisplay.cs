using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.XR;
using UnityEngine.UI;
using TMPro;

public class DebugDisplay : MonoBehaviour
{
    public GameObject[] shapes;
    public AxisDisplay axisDisplay;
    public RotationSnapshot rotationSnapshot;
    public int activeIndex;

    bool visible;
    bool prevYButton;
    GameObject canvasGo;
    TextMeshProUGUI debugTMP;

    // FPS
    int frameCount;
    float fpsTimer;
    float currentFps;

    void Start()
    {
        // Canvas (Screen Space - Camera)
        canvasGo = new GameObject("DebugCanvas");
        canvasGo.transform.SetParent(transform, false);
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceCamera;
        canvas.worldCamera = Camera.main;
        canvas.planeDistance = 0.5f;
        canvasGo.AddComponent<CanvasScaler>();
        canvasGo.AddComponent<GraphicRaycaster>();

        // TextMeshProUGUI anchored top-left
        var textGo = new GameObject("DebugText");
        textGo.transform.SetParent(canvasGo.transform, false);
        debugTMP = textGo.AddComponent<TextMeshProUGUI>();
        debugTMP.fontSize = 18;
        debugTMP.color = Color.white;

        RectTransform rt = textGo.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0, 1);
        rt.anchorMax = new Vector2(0, 1);
        rt.pivot = new Vector2(0, 1);
        rt.anchoredPosition = new Vector2(10, -10);
        rt.sizeDelta = new Vector2(400, 300);

        // Default: hidden
        canvasGo.SetActive(false);
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
            if (canvasGo != null) canvasGo.SetActive(visible);
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

        debugTMP.text = sb.ToString();
    }

    GameObject GetActiveShape()
    {
        if (shapes == null || activeIndex < 0 || activeIndex >= shapes.Length)
            return null;
        var s = shapes[activeIndex];
        return (s != null && s.activeInHierarchy) ? s : null;
    }
}
