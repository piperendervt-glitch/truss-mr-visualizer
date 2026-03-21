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
    bool isMR = true;

    string[] shapeNames = { "Tesseract", "Hexadecachoron" };

    void Start()
    {
        // Create world-space label
        var labelGo = new GameObject("ShapeLabel");
        labelGo.transform.SetParent(transform, false);
        label = labelGo.AddComponent<TextMeshPro>();
        label.fontSize = 0.22f;
        label.alignment = TextAlignmentOptions.Center;
        label.color = new Color(1f, 0.9f, 0f, 1f);
        label.rectTransform.sizeDelta = new Vector2(2.8f, 0.5f);

        // Activate first shape, deactivate others
        for (int i = 0; i < shapes.Length; i++)
            if (shapes[i] != null)
                shapes[i].SetActive(i == currentIndex);

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
        // Quest 3: B button = shape switch, A button = MR/VR toggle
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
        if (aButtonDown) ToggleMRVR();

        // Position label below the active shape, billboard toward camera
        var activeShape = (shapes != null && currentIndex < shapes.Length) ? shapes[currentIndex] : null;
        var cam = Camera.main;
        if (cam != null && label != null && activeShape != null)
        {
            label.transform.position = activeShape.transform.position + Vector3.down * 0.2f;
            label.transform.LookAt(cam.transform);
            label.transform.Rotate(0f, 180f, 0f); // LookAt faces away, flip to face camera
        }
    }

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

    void ToggleMRVR()
    {
        isMR = !isMR;
        var cam = Camera.main;

        if (isMR)
        {
            if (cam != null) cam.backgroundColor = new Color(0f, 0f, 0f, 0f);
            if (passthroughLayer != null) passthroughLayer.SetActive(true);
        }
        else
        {
            if (cam != null) cam.backgroundColor = Color.black;
            if (passthroughLayer != null) passthroughLayer.SetActive(false);
        }

        UpdateLabel();
    }

    void UpdateLabel()
    {
        if (label == null) return;
        string name = currentIndex < shapeNames.Length ? shapeNames[currentIndex] : "Shape";
        string mode = isMR ? "MR" : "VR";
        label.text = $"{name} ({currentIndex + 1}/{shapes.Length}) [{mode}]";
    }
}
