using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.XR;
using TMPro;

public class ShapeManager : MonoBehaviour
{
    public GameObject[] shapes;
    public GameObject passthroughLayer; // OVRPassthroughLayer GameObject
    int currentIndex;
    TextMeshPro label;
    bool prevButton;
    bool prevAButton;

    // Background mode: 0=MR, 1=VR Black, 2=VR Grid
    int bgMode;
    string[] bgModeNames = { "VR Grid", "MR", "VR Black" };
    string[] shapeNames = { "Tesseract", "Hexadecachoron" };

    // Grid
    GameObject gridRoot;
    const int gridHalf = 50;       // ±50 lines = 50m span
    const float gridSpacing = 0.5f;
    static readonly Color gridColor = new Color(0.85f, 0.85f, 0.9f, 0.4f);

    void Start()
    {
        // Create world-space label
        var labelGo = new GameObject("ShapeLabel");
        labelGo.transform.SetParent(transform, false);
        label = labelGo.AddComponent<TextMeshPro>();
        label.fontSize = 0.22f;
        label.alignment = TextAlignmentOptions.Center;
        label.color = new Color(1f, 0.9f, 0f, 1f);
        label.rectTransform.sizeDelta = new Vector2(2.8f, 0.8f);

        // Activate first shape, deactivate others
        for (int i = 0; i < shapes.Length; i++)
            if (shapes[i] != null)
                shapes[i].SetActive(i == currentIndex);

        BuildGrid();
        ApplyBgMode();
        UpdateLabel();
    }

    void Update()
    {
        bool buttonDown = false;
        bool aButtonDown = false;

#if UNITY_EDITOR
        var kb = Keyboard.current;
        if (kb != null)
        {
            if (kb.spaceKey.wasPressedThisFrame) buttonDown = true;
            if (kb.tabKey.wasPressedThisFrame) aButtonDown = true;
        }
#else
        // Quest 3: B button = shape switch, A button = background mode
        var rightCtrl = XRController.rightHand;
        if (rightCtrl != null)
        {
            var bBtn = rightCtrl.TryGetChildControl<ButtonControl>("secondaryButton");
            if (bBtn != null)
            {
                bool pressed = bBtn.isPressed;
                if (pressed && !prevButton) buttonDown = true;
                prevButton = pressed;
            }

            var aBtn = rightCtrl.TryGetChildControl<ButtonControl>("primaryButton");
            if (aBtn != null)
            {
                bool pressed = aBtn.isPressed;
                if (pressed && !prevAButton) aButtonDown = true;
                prevAButton = pressed;
            }
        }
#endif

        if (buttonDown) SwitchToNext();
        if (aButtonDown) CycleBgMode();

        // Update label every frame (pair info changes on stick click)
        UpdateLabel();

        // Position label below the active shape, billboard toward camera
        var activeShape = (shapes != null && currentIndex < shapes.Length) ? shapes[currentIndex] : null;
        var cam = Camera.main;
        if (cam != null && label != null && activeShape != null)
        {
            label.transform.position = activeShape.transform.position + Vector3.down * 0.2f;
            label.transform.LookAt(cam.transform);
            label.transform.Rotate(0f, 180f, 0f);
        }

        // Keep grid centered on camera (XZ snapped to grid spacing)
        if (gridRoot != null && gridRoot.activeSelf && cam != null)
        {
            Vector3 cp = cam.transform.position;
            float sx = Mathf.Round(cp.x / gridSpacing) * gridSpacing;
            float sz = Mathf.Round(cp.z / gridSpacing) * gridSpacing;
            gridRoot.transform.position = new Vector3(sx, 0f, sz);
        }
    }

    // --- Shape switching ---
    void SwitchToNext()
    {
        if (shapes == null || shapes.Length == 0) return;

        if (shapes[currentIndex] != null)
            shapes[currentIndex].SetActive(false);

        currentIndex = (currentIndex + 1) % shapes.Length;

        if (shapes[currentIndex] != null)
            shapes[currentIndex].SetActive(true);

        UpdateLabel();
    }

    // --- Background mode cycling ---
    void CycleBgMode()
    {
        bgMode = (bgMode + 1) % bgModeNames.Length;
        ApplyBgMode();
        UpdateLabel();
    }

    void ApplyBgMode()
    {
        var cam = Camera.main;

        switch (bgMode)
        {
            case 0: // VR Grid
                if (cam != null) cam.backgroundColor = new Color(0.65f, 0.65f, 0.72f, 1f);
                if (passthroughLayer != null) passthroughLayer.SetActive(false);
                if (gridRoot != null) gridRoot.SetActive(true);
                RenderSettings.fog = true;
                RenderSettings.fogMode = FogMode.Linear;
                RenderSettings.fogColor = new Color(0.7f, 0.7f, 0.75f, 1f);
                RenderSettings.fogStartDistance = 5f;
                RenderSettings.fogEndDistance = 25f;
                break;
            case 1: // MR
                if (cam != null) cam.backgroundColor = new Color(0f, 0f, 0f, 0f);
                if (passthroughLayer != null) passthroughLayer.SetActive(true);
                if (gridRoot != null) gridRoot.SetActive(false);
                RenderSettings.fog = false;
                break;
            case 2: // VR Black
                if (cam != null) cam.backgroundColor = Color.black;
                if (passthroughLayer != null) passthroughLayer.SetActive(false);
                if (gridRoot != null) gridRoot.SetActive(false);
                RenderSettings.fog = false;
                break;
        }
    }

    // --- Grid construction ---
    void BuildGrid()
    {
        gridRoot = new GameObject("VRGrid");
        gridRoot.transform.SetParent(transform, false);
        gridRoot.SetActive(false);

        var mat = new Material(Shader.Find("Sprites/Default"));

        float extent = gridHalf * gridSpacing;

        // Horizontal plane (XZ) only
        for (int i = -gridHalf; i <= gridHalf; i++)
        {
            float offset = i * gridSpacing;
            // Line along Z
            CreateGridLine(mat,
                new Vector3(offset, 0f, -extent),
                new Vector3(offset, 0f, extent));
            // Line along X
            CreateGridLine(mat,
                new Vector3(-extent, 0f, offset),
                new Vector3(extent, 0f, offset));
        }
    }

    void CreateGridLine(Material mat, Vector3 a, Vector3 b)
    {
        var go = new GameObject("gline");
        go.transform.SetParent(gridRoot.transform, false);
        var lr = go.AddComponent<LineRenderer>();
        lr.positionCount = 2;
        lr.SetPosition(0, a);
        lr.SetPosition(1, b);
        lr.startWidth = 0.003f;
        lr.endWidth = 0.003f;
        lr.useWorldSpace = false;
        lr.material = mat;
        lr.startColor = gridColor;
        lr.endColor = gridColor;
    }

    // --- Label (updated every frame to reflect plane pair changes) ---
    void UpdateLabel()
    {
        if (label == null) return;
        string name = currentIndex < shapeNames.Length ? shapeNames[currentIndex] : "Shape";
        string mode = bgMode < bgModeNames.Length ? bgModeNames[bgMode] : "?";
        string pairLabel = GetActivePairLabel();
        label.text = $"{name} ({currentIndex + 1}/{shapes.Length}) [{mode}]\n{pairLabel}";
    }

    string GetActivePairLabel()
    {
        if (shapes == null || currentIndex >= shapes.Length || shapes[currentIndex] == null)
            return "";

        var tess = shapes[currentIndex].GetComponent<Tesseract>();
        if (tess != null) return tess.GetPairLabel();

        var hexa = shapes[currentIndex].GetComponent<Hexadecachoron>();
        if (hexa != null) return hexa.GetPairLabel();

        return "";
    }
}
