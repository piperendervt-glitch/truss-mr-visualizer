using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.XR;
using UnityEngine.UI;
using TMPro;

public class MenuUI : MonoBehaviour
{
    public GameObject[] shapes;
    public int activeIndex;

    public static bool isMenuOpen;

    GameObject canvasGo;
    TextMeshProUGUI menuTMP;

    int selectedItem;
    const int itemCount = 5; // Particles, Trail, Multi, Speed, Close

    // FPS
    int frameCount;
    float fpsTimer;
    float currentFps;

    // Input edge detection
    bool prevUp, prevDown, prevLeft, prevRight;

    void Start()
    {
        // World Space Canvas
        canvasGo = new GameObject("MenuCanvas");
        canvasGo.transform.SetParent(transform, false);
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvasGo.AddComponent<CanvasScaler>();
        canvasGo.AddComponent<GraphicRaycaster>();

        var canvasRT = canvasGo.GetComponent<RectTransform>();
        canvasRT.sizeDelta = new Vector2(500, 620);
        canvasGo.transform.localScale = Vector3.one * 0.0008f; // ~0.4m x 0.5m

        // Background
        var bgGo = new GameObject("MenuBG");
        bgGo.transform.SetParent(canvasGo.transform, false);
        bgGo.transform.SetAsFirstSibling();
        var bgImg = bgGo.AddComponent<Image>();
        bgImg.color = new Color(0f, 0f, 0.05f, 0.85f);
        var bgRT = bgGo.GetComponent<RectTransform>();
        bgRT.anchorMin = Vector2.zero;
        bgRT.anchorMax = Vector2.one;
        bgRT.offsetMin = Vector2.zero;
        bgRT.offsetMax = Vector2.zero;

        // Text
        var textGo = new GameObject("MenuText");
        textGo.transform.SetParent(canvasGo.transform, false);
        menuTMP = textGo.AddComponent<TextMeshProUGUI>();
        menuTMP.fontSize = 28;
        menuTMP.color = Color.white;
        menuTMP.richText = true;
        menuTMP.alignment = TextAlignmentOptions.TopLeft;

        var rt = textGo.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(20, 20);
        rt.offsetMax = new Vector2(-20, -20);

        canvasGo.SetActive(false);
        isMenuOpen = false;
    }

    void Update()
    {
        // FPS
        frameCount++;
        fpsTimer += Time.unscaledDeltaTime;
        if (fpsTimer >= 1f)
        {
            currentFps = frameCount / fpsTimer;
            frameCount = 0;
            fpsTimer = 0f;
        }

        bool toggleMenu = false;
        bool up = false, down = false, left = false, right = false;
        bool confirm = false;

#if UNITY_EDITOR
        var kb = Keyboard.current;
        if (kb != null)
        {
            if (kb.tabKey.wasPressedThisFrame) toggleMenu = true;
            if (isMenuOpen)
            {
                up = kb.upArrowKey.isPressed;
                down = kb.downArrowKey.isPressed;
                left = kb.leftArrowKey.isPressed;
                right = kb.rightArrowKey.isPressed;
                if (kb.enterKey.wasPressedThisFrame || kb.zKey.wasPressedThisFrame)
                    confirm = true;
            }
        }
#else
        var leftCtrl = XRController.leftHand;
        if (leftCtrl != null)
        {
            var menuBtn = leftCtrl.TryGetChildControl<ButtonControl>("menuButton");
            if (menuBtn != null && menuBtn.wasPressedThisFrame) toggleMenu = true;

            var lClick = leftCtrl.TryGetChildControl<ButtonControl>("thumbstickClicked");
            if (lClick != null && lClick.wasPressedThisFrame) toggleMenu = true;
        }

        if (isMenuOpen)
        {
            var rightCtrl = XRController.rightHand;
            if (rightCtrl != null)
            {
                var stick = rightCtrl.TryGetChildControl<StickControl>("thumbstick");
                if (stick != null)
                {
                    up = stick.y.ReadValue() > 0.5f;
                    down = stick.y.ReadValue() < -0.5f;
                    left = stick.x.ReadValue() < -0.5f;
                    right = stick.x.ReadValue() > 0.5f;
                }

                var aBtn = rightCtrl.TryGetChildControl<ButtonControl>("primaryButton");
                if (aBtn != null && aBtn.wasPressedThisFrame) confirm = true;
            }
        }
#endif

        if (toggleMenu)
        {
            isMenuOpen = !isMenuOpen;
            canvasGo.SetActive(isMenuOpen);
        }

        if (!isMenuOpen) return;

        // Position in front of camera
        var cam = Camera.main;
        if (cam != null)
        {
            canvasGo.transform.position = cam.transform.position + cam.transform.forward * 1.0f;
            canvasGo.transform.LookAt(cam.transform.position);
            canvasGo.transform.Rotate(0f, 180f, 0f);
        }

        // Navigation (edge-triggered)
        if (up && !prevUp) selectedItem = (selectedItem - 1 + itemCount) % itemCount;
        if (down && !prevDown) selectedItem = (selectedItem + 1) % itemCount;

        if (left && !prevLeft) ChangeValue(-1);
        if (right && !prevRight) ChangeValue(1);
        if (confirm) OnConfirm();

        prevUp = up; prevDown = down; prevLeft = left; prevRight = right;

        BuildMenuText();
    }

    void ChangeValue(int dir)
    {
        var shape = GetActiveShape();
        if (shape == null) return;

        var lorenz = shape.GetComponent<LorenzAttractor>();
        var thomas = shape.GetComponent<ThomasAttractor>();
        var tess = shape.GetComponent<Tesseract>();
        var hexa = shape.GetComponent<Hexadecachoron>();
        var dodeca = shape.GetComponent<Dodecaplex>();

        switch (selectedItem)
        {
            case 0: // Particles
                if (lorenz != null) lorenz.SetParticleCount(Mathf.Clamp(lorenz.particleCount + dir * 50, 50, 2000));
                if (thomas != null) thomas.SetParticleCount(Mathf.Clamp(thomas.particleCount + dir * 50, 50, 2000));
                break;
            case 1: // Trail Length
                if (lorenz != null) lorenz.SetTrailLength(Mathf.Clamp(lorenz.trailLength + dir * 50, 50, 500));
                if (thomas != null) thomas.SetTrailLength(Mathf.Clamp(thomas.trailLength + dir * 50, 50, 500));
                break;
            case 2: // Multi Mode (use left/right as toggle)
                if (lorenz != null) lorenz.SetMultiMode(!lorenz.multiMode);
                if (thomas != null) thomas.SetMultiMode(!thomas.multiMode);
                break;
            case 3: // Speed
                if (tess != null) tess.speedLevel = Mathf.Clamp(tess.speedLevel + dir, 0, 2);
                if (hexa != null) hexa.speedLevel = Mathf.Clamp(hexa.speedLevel + dir, 0, 2);
                if (dodeca != null) dodeca.speedLevel = Mathf.Clamp(dodeca.speedLevel + dir, 0, 2);
                break;
        }
    }

    void OnConfirm()
    {
        var shape = GetActiveShape();

        switch (selectedItem)
        {
            case 2: // Multi Mode toggle
                if (shape != null)
                {
                    var lorenz = shape.GetComponent<LorenzAttractor>();
                    var thomas = shape.GetComponent<ThomasAttractor>();
                    if (lorenz != null) lorenz.SetMultiMode(!lorenz.multiMode);
                    if (thomas != null) thomas.SetMultiMode(!thomas.multiMode);
                }
                break;
            case 4: // Close
                isMenuOpen = false;
                canvasGo.SetActive(false);
                break;
        }
    }

    void BuildMenuText()
    {
        var sb = new System.Text.StringBuilder();

        var shape = GetActiveShape();
        int pCount = 0;
        int tLen = 0;
        bool multi = false;
        string speedStr = "---";

        if (shape != null)
        {
            var lorenz = shape.GetComponent<LorenzAttractor>();
            var thomas = shape.GetComponent<ThomasAttractor>();
            var tess = shape.GetComponent<Tesseract>();
            var hexa = shape.GetComponent<Hexadecachoron>();
            var dodeca = shape.GetComponent<Dodecaplex>();

            if (lorenz != null) { pCount = lorenz.particleCount; tLen = lorenz.trailLength; multi = lorenz.multiMode; }
            if (thomas != null) { pCount = thomas.particleCount; tLen = thomas.trailLength; multi = thomas.multiMode; }
            if (tess != null) speedStr = Tesseract.speedNames[tess.speedLevel];
            if (hexa != null) speedStr = Hexadecachoron.speedNames[hexa.speedLevel];
            if (dodeca != null) speedStr = Dodecaplex.speedNames[dodeca.speedLevel];
        }

        sb.AppendLine($"<size=24>FPS: {currentFps:F0}   Particles: {(multi ? pCount : 0)}</size>");
        sb.AppendLine("<color=#555555>\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500</color>");

        string[] labels = {
            $"Particles      <b>[ {pCount} ]</b>",
            $"Trail Length   <b>[ {tLen} ]</b>",
            $"Multi Mode     <b>[ {(multi ? "ON" : "OFF")} ]</b>",
            $"Speed          <b>[ {speedStr} ]</b>",
            "Close",
        };

        for (int i = 0; i < itemCount; i++)
        {
            string prefix = (i == selectedItem) ? "\u25b6 " : "  ";
            if (i == selectedItem)
                sb.AppendLine($"<color=yellow>{prefix}{labels[i]}</color>");
            else
                sb.AppendLine($"  {labels[i]}");
        }

        sb.AppendLine();
        sb.AppendLine("<size=20><color=#888888>\u2191\u2193 Select   \u2190\u2192 Change   A Confirm</color></size>");

        menuTMP.text = sb.ToString();
    }

    GameObject GetActiveShape()
    {
        if (shapes == null || activeIndex < 0 || activeIndex >= shapes.Length)
            return null;
        var s = shapes[activeIndex];
        return (s != null && s.activeInHierarchy) ? s : null;
    }
}
