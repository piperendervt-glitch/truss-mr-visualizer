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
        label.fontSize = 1.5f;
        label.alignment = TextAlignmentOptions.TopLeft;
        label.color = Color.cyan;
        label.rectTransform.sizeDelta = new Vector2(2f, 0.5f);

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

        // Position label at top-left of camera view
        var cam = Camera.main;
        if (cam != null && label != null)
        {
            Vector3 fwd = cam.transform.forward;
            Vector3 right = cam.transform.right;
            Vector3 up = cam.transform.up;
            label.transform.position = cam.transform.position
                + fwd * 0.8f
                + up * 0.10f
                - right * 0.12f;
            label.transform.rotation = Quaternion.LookRotation(fwd, up);
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
