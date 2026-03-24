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

    bool visible; // default OFF
    GameObject canvasGo;
    TextMeshProUGUI debugTMP;

    // Phi bar graph
    Image phiBarFill;
    GameObject phiBarRoot;
    Image thresholdLine;
    TextMeshProUGUI thresholdLabel;

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
        debugTMP.richText = true;

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

        // Phi bar graph (hidden by default, shown for FanoQ3)
        phiBarRoot = new GameObject("PhiBarRoot");
        phiBarRoot.transform.SetParent(canvasGo.transform, false);
        var phiBarRT = phiBarRoot.AddComponent<RectTransform>();
        phiBarRT.anchorMin = new Vector2(0f, 0f);
        phiBarRT.anchorMax = new Vector2(0f, 0f);
        phiBarRT.pivot = new Vector2(0f, 0f);
        phiBarRT.anchoredPosition = new Vector2(10, 10);
        phiBarRT.sizeDelta = new Vector2(200, 16);

        // Bar background
        var barBg = new GameObject("PhiBarBG");
        barBg.transform.SetParent(phiBarRoot.transform, false);
        var barBgImg = barBg.AddComponent<Image>();
        barBgImg.color = new Color(0.2f, 0.2f, 0.2f, 0.8f);
        var barBgRT = barBg.GetComponent<RectTransform>();
        barBgRT.anchorMin = Vector2.zero;
        barBgRT.anchorMax = Vector2.one;
        barBgRT.offsetMin = Vector2.zero;
        barBgRT.offsetMax = Vector2.zero;

        // Bar fill
        var barFill = new GameObject("PhiBarFill");
        barFill.transform.SetParent(phiBarRoot.transform, false);
        phiBarFill = barFill.AddComponent<Image>();
        phiBarFill.color = Color.green;
        var barFillRT = barFill.GetComponent<RectTransform>();
        barFillRT.anchorMin = Vector2.zero;
        barFillRT.anchorMax = new Vector2(1f, 1f);
        barFillRT.pivot = new Vector2(0f, 0.5f);
        barFillRT.offsetMin = Vector2.zero;
        barFillRT.offsetMax = Vector2.zero;

        // 95% threshold line
        var threshGo = new GameObject("ThresholdLine");
        threshGo.transform.SetParent(phiBarRoot.transform, false);
        thresholdLine = threshGo.AddComponent<Image>();
        thresholdLine.color = Color.white;
        var threshRT = threshGo.GetComponent<RectTransform>();
        threshRT.anchorMin = new Vector2(0.95f, 0f);
        threshRT.anchorMax = new Vector2(0.95f, 1f);
        threshRT.pivot = new Vector2(0.5f, 0.5f);
        threshRT.sizeDelta = new Vector2(2f, 0f);
        threshRT.anchoredPosition = Vector2.zero;

        // 95% label
        var threshLabelGo = new GameObject("ThresholdLabel");
        threshLabelGo.transform.SetParent(phiBarRoot.transform, false);
        thresholdLabel = threshLabelGo.AddComponent<TextMeshProUGUI>();
        thresholdLabel.text = "95%";
        thresholdLabel.fontSize = 10;
        thresholdLabel.color = Color.white;
        thresholdLabel.alignment = TextAlignmentOptions.Center;
        var tlRT = threshLabelGo.GetComponent<RectTransform>();
        tlRT.anchorMin = new Vector2(0.95f, 1f);
        tlRT.anchorMax = new Vector2(0.95f, 1f);
        tlRT.pivot = new Vector2(0.5f, 0f);
        tlRT.sizeDelta = new Vector2(40f, 14f);
        tlRT.anchoredPosition = new Vector2(0f, 1f);

        phiBarRoot.SetActive(false);

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
                + cam.transform.up * -0.15f
                + cam.transform.right * 0.25f;
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
            var lorenz = activeShape.GetComponent<LorenzAttractor>();
            if (lorenz != null)
            {
                shapeName = "LorenzAttractor";
            }
            var thomas = activeShape.GetComponent<ThomasAttractor>();
            if (thomas != null)
            {
                shapeName = "ThomasAttractor";
            }
            var dodeca = activeShape.GetComponent<Dodecaplex>();
            if (dodeca != null)
            {
                shapeName = "Dodecaplex";
                angles = dodeca.GetAngles();
                speedLevel = dodeca.speedLevel;
                planeNames = Dodecaplex.planeNames;
            }
            var fanoQ3 = activeShape.GetComponent<FanoQ3Animator>();
            if (fanoQ3 != null)
            {
                shapeName = "FanoQ3Animator";
            }
            var staged = activeShape.GetComponent<StagedAttackAnimator>();
            if (staged != null)
            {
                shapeName = "StagedAttack";
            }
            var morse = activeShape.GetComponent<MorseLandscape1D>();
            if (morse != null)
            {
                shapeName = "MorseLandscape1D";
            }
            var csAnalyzer = activeShape.GetComponent<CorrectionStrengthAnalyzer>();
            if (csAnalyzer != null)
            {
                shapeName = "CSAnalyzer";
            }
        }

        string[] speedNames = { "Slow", "Normal", "Fast" };

        // Attractor-specific debug info
        if (activeShape != null)
        {
            var lorenz = activeShape.GetComponent<LorenzAttractor>();
            var thomasDbg = activeShape.GetComponent<ThomasAttractor>();
            if (lorenz != null)
            {
                if (phiBarRoot != null) phiBarRoot.SetActive(false);
                sb.AppendLine($"Shape: {shapeName}  dt:{lorenz.dt:F3}");
                sb.AppendLine($"\u03c3:{lorenz.sigma:F1}  \u03c1:{lorenz.rho:F1}  \u03b2:{lorenz.beta:F2}");
                float lv = lorenz.lyapunovExponent;
                string lc = lv > 0.01f ? "red" : lv < -0.01f ? "green" : "yellow";
                sb.AppendLine($"Lyapunov: <color={lc}>{(lv >= 0 ? "+" : "")}{lv:F3}</color>");
                if (lorenz.multiMode)
                    sb.AppendLine($"\u03bb mean:<color={Lc(lorenz.lyapMean)}>{lorenz.lyapMean:+0.00;-0.00}</color>  max:<color={Lc(lorenz.lyapMax)}>{lorenz.lyapMax:+0.00;-0.00}</color>  min:<color={Lc(lorenz.lyapMin)}>{lorenz.lyapMin:+0.00;-0.00}</color>");
            }
            else if (thomasDbg != null)
            {
                if (phiBarRoot != null) phiBarRoot.SetActive(false);
                sb.AppendLine($"Shape: {shapeName}  dt:{thomasDbg.dt:F3}");
                sb.AppendLine($"b:{thomasDbg.b:F3}");
                float lv = thomasDbg.lyapunovExponent;
                string lc = lv > 0.01f ? "red" : lv < -0.01f ? "green" : "yellow";
                sb.AppendLine($"Lyapunov: <color={lc}>{(lv >= 0 ? "+" : "")}{lv:F3}</color>");
                if (thomasDbg.multiMode)
                    sb.AppendLine($"\u03bb mean:<color={Lc(thomasDbg.lyapMean)}>{thomasDbg.lyapMean:+0.00;-0.00}</color>  max:<color={Lc(thomasDbg.lyapMax)}>{thomasDbg.lyapMax:+0.00;-0.00}</color>  min:<color={Lc(thomasDbg.lyapMin)}>{thomasDbg.lyapMin:+0.00;-0.00}</color>");
            }
            else
            {
                var fanoQ3Dbg = activeShape.GetComponent<FanoQ3Animator>();
                var stagedDbg = activeShape.GetComponent<StagedAttackAnimator>();
                if (fanoQ3Dbg != null)
                {
                    sb.AppendLine($"Shape: {shapeName}");
                    sb.AppendLine(fanoQ3Dbg.GetDebugInfo());
                    UpdatePhiBar(fanoQ3Dbg.phiRate);
                }
                else if (stagedDbg != null)
                {
                    sb.AppendLine($"Shape: {shapeName}");
                    sb.AppendLine(stagedDbg.GetDebugInfo());
                    if (phiBarRoot != null) phiBarRoot.SetActive(false);
                }
                else
                {
                    var morseDbg = activeShape.GetComponent<MorseLandscape1D>();
                    if (morseDbg != null)
                    {
                        sb.AppendLine($"Shape: {shapeName}");
                        sb.AppendLine(morseDbg.GetDebugInfo());
                        if (phiBarRoot != null) phiBarRoot.SetActive(false);
                    }
                    else
                    {
                        var csDbg = activeShape.GetComponent<CorrectionStrengthAnalyzer>();
                        if (csDbg != null)
                        {
                            sb.AppendLine($"Shape: {shapeName}");
                            sb.AppendLine(csDbg.GetDebugInfo());
                            if (phiBarRoot != null) phiBarRoot.SetActive(false);
                        }
                        else
                        {
                            sb.AppendLine($"Shape: {shapeName}  Speed: {speedNames[speedLevel]}");
                            if (phiBarRoot != null) phiBarRoot.SetActive(false);
                        }
                    }
                }
            }
        }
        else
        {
            sb.AppendLine($"Shape: {shapeName}  Speed: {speedNames[speedLevel]}");
            if (phiBarRoot != null) phiBarRoot.SetActive(false);
        }

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

    void UpdatePhiBar(float phiRate)
    {
        if (phiBarRoot == null || phiBarFill == null) return;
        phiBarRoot.SetActive(true);

        float ratio = Mathf.Clamp01(phiRate / 100f);
        var fillRT = phiBarFill.GetComponent<RectTransform>();
        fillRT.anchorMax = new Vector2(ratio, 1f);

        if (phiRate >= 95f)
            phiBarFill.color = Color.green;
        else if (phiRate >= 70f)
            phiBarFill.color = Color.yellow;
        else
            phiBarFill.color = Color.red;
    }

    static string Lc(float v) { return v > 0.01f ? "red" : v < -0.01f ? "green" : "yellow"; }

    GameObject GetActiveShape()
    {
        if (shapes == null || activeIndex < 0 || activeIndex >= shapes.Length)
            return null;
        var s = shapes[activeIndex];
        return (s != null && s.activeInHierarchy) ? s : null;
    }
}
