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

    bool visible = true; // default ON for testing
    GameObject canvasGo;
    TextMeshProUGUI debugTMP;

    // FPS
    int frameCount;
    float fpsTimer;
    float currentFps;

    void Start()
    {
        // World Space Canvas
        canvasGo = new GameObject("DebugCanvas");
        canvasGo.transform.SetParent(transform, false);
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvasGo.AddComponent<CanvasScaler>();
        canvasGo.AddComponent<GraphicRaycaster>();

        // Canvas RectTransform size
        var canvasRT = canvasGo.GetComponent<RectTransform>();
        canvasRT.sizeDelta = new Vector2(800, 400);
        canvasGo.transform.localScale = Vector3.one * 0.001f;

        // TextMeshProUGUI anchored top-left
        var textGo = new GameObject("DebugText");
        textGo.transform.SetParent(canvasGo.transform, false);
        debugTMP = textGo.AddComponent<TextMeshProUGUI>();
        debugTMP.fontSize = 24;
        debugTMP.color = Color.white;

        RectTransform rt = textGo.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(10, 10);
        rt.offsetMax = new Vector2(-10, -10);

        // Background image for readability
        var bgGo = new GameObject("DebugBG");
        bgGo.transform.SetParent(canvasGo.transform, false);
        bgGo.transform.SetAsFirstSibling();
        var bgImg = bgGo.AddComponent<Image>();
        bgImg.color = new Color(0f, 0f, 0f, 0.5f);
        var bgRT = bgGo.GetComponent<RectTransform>();
        bgRT.anchorMin = Vector2.zero;
        bgRT.anchorMax = Vector2.one;
        bgRT.offsetMin = Vector2.zero;
        bgRT.offsetMax = Vector2.zero;

        // Force visible for testing
        canvasGo.SetActive(true);
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
            var btn = leftCtrl.TryGetChildControl<ButtonControl>("secondaryButton");
            if (btn != null && btn.wasPressedThisFrame) toggleDown = true;
        }
#endif

        if (toggleDown)
        {
            visible = !visible;
            if (canvasGo != null) canvasGo.SetActive(visible);
        }

        // Position canvas in front of camera
        var cam = Camera.main;
        if (cam != null && canvasGo != null && canvasGo.activeSelf)
        {
            canvasGo.transform.position = cam.transform.position
                + cam.transform.forward * 1.0f
                + cam.transform.up * 0.15f
                + cam.transform.right * -0.3f;
            canvasGo.transform.LookAt(cam.transform.position);
            canvasGo.transform.Rotate(0f, 180f, 0f);
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
