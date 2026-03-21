using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.XR;
using TMPro;

public class ShapeManager : MonoBehaviour
{
    public GameObject[] shapes;
    int currentIndex;
    TextMeshPro label;
    bool prevButton;

    string[] shapeNames = { "Tesseract", "Hexadecachoron" };

    void Start()
    {
        // Create world-space label
        var labelGo = new GameObject("ShapeLabel");
        labelGo.transform.SetParent(transform, false);
        label = labelGo.AddComponent<TextMeshPro>();
        label.fontSize = 0.08f;
        label.alignment = TextAlignmentOptions.Center;
        label.color = Color.cyan;
        label.rectTransform.sizeDelta = new Vector2(1.0f, 0.2f);

        // Activate first shape, deactivate others
        for (int i = 0; i < shapes.Length; i++)
            if (shapes[i] != null)
                shapes[i].SetActive(i == currentIndex);

        UpdateLabel();
    }

    void Update()
    {
        bool buttonDown = false;

#if UNITY_EDITOR
        var kb = Keyboard.current;
        if (kb != null && kb.spaceKey.wasPressedThisFrame)
            buttonDown = true;
#else
        // Quest 3: B button on right controller (secondaryButton)
        var rightCtrl = XRController.rightHand;
        if (rightCtrl != null)
        {
            var btn = rightCtrl.TryGetChildControl<ButtonControl>("secondaryButton");
            if (btn != null)
            {
                bool pressed = btn.isPressed;
                if (pressed && !prevButton) buttonDown = true;
                prevButton = pressed;
            }
        }
#endif

        if (buttonDown) SwitchToNext();

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

    void UpdateLabel()
    {
        if (label == null) return;
        string name = currentIndex < shapeNames.Length ? shapeNames[currentIndex] : "Shape";
        label.text = $"{name} ({currentIndex + 1}/{shapes.Length})";
    }
}
