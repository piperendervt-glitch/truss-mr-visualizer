using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.XR;

public class RotationSnapshot : MonoBehaviour
{
    public GameObject[] shapes;
    public int activeIndex;

    const int slotCount = 5;
    float[][] slots = new float[slotCount][];
    public int currentSlot;
    bool[] slotUsed = new bool[slotCount];

    // Long press tracking
    float leftGripHoldTime;
    float rightGripHoldTime;
    const float holdThreshold = 0.5f;
    bool leftGripSaved;  // prevent repeat save while held
    bool rightGripLoaded;

    bool prevDpadUp;

    void Update()
    {
        bool slotUp = false;
        bool savePress = false;
        bool loadPress = false;
        float leftGripVal = 0f, rightGripVal = 0f;

#if UNITY_EDITOR
        var kb = Keyboard.current;
        if (kb != null)
        {
            if (kb.digit4Key.wasPressedThisFrame) slotUp = true; // 4 key for slot cycle
            if (kb.zKey.wasPressedThisFrame) savePress = true;
            if (kb.xKey.wasPressedThisFrame) loadPress = true;
        }
#else
        var leftCtrl = XRController.leftHand;
        if (leftCtrl != null)
        {
            // Dpad up to cycle slot
            var dpadUp = leftCtrl.TryGetChildControl<ButtonControl>("thumbstickClicked");
            // Use right controller's dpad for slot cycling
        }

        var rightCtrl = XRController.rightHand;
        if (rightCtrl != null)
        {
            // Read thumbstick Y for dpad-up emulation
            var stick = rightCtrl.TryGetChildControl<StickControl>("thumbstick");
            if (stick != null)
            {
                bool up = stick.y.ReadValue() > 0.8f;
                if (up && !prevDpadUp) slotUp = true;
                prevDpadUp = up;
            }
        }

        // Grip hold for save/load
        if (leftCtrl != null)
        {
            var grip = leftCtrl.TryGetChildControl<AxisControl>("grip");
            if (grip != null) leftGripVal = grip.ReadValue();
        }
        if (rightCtrl != null)
        {
            var grip = rightCtrl.TryGetChildControl<AxisControl>("grip");
            if (grip != null) rightGripVal = grip.ReadValue();
        }
#endif

        // Slot cycling
        if (slotUp)
            currentSlot = (currentSlot + 1) % slotCount;

        // Long press save (left grip)
        if (leftGripVal > 0.5f)
        {
            leftGripHoldTime += Time.deltaTime;
            if (leftGripHoldTime >= holdThreshold && !leftGripSaved)
            {
                savePress = true;
                leftGripSaved = true;
            }
        }
        else
        {
            leftGripHoldTime = 0f;
            leftGripSaved = false;
        }

        // Long press load (right grip)
        if (rightGripVal > 0.5f)
        {
            rightGripHoldTime += Time.deltaTime;
            if (rightGripHoldTime >= holdThreshold && !rightGripLoaded)
            {
                loadPress = true;
                rightGripLoaded = true;
            }
        }
        else
        {
            rightGripHoldTime = 0f;
            rightGripLoaded = false;
        }

        if (savePress) SaveCurrentAngles();
        if (loadPress) LoadCurrentAngles();
    }

    void SaveCurrentAngles()
    {
        GameObject shape = GetActiveShape();
        if (shape == null) return;

        float[] a = GetShapeAngles(shape);
        if (a == null) return;

        slots[currentSlot] = (float[])a.Clone();
        slotUsed[currentSlot] = true;
    }

    void LoadCurrentAngles()
    {
        if (!slotUsed[currentSlot]) return;

        GameObject shape = GetActiveShape();
        if (shape == null) return;

        SetShapeAngles(shape, slots[currentSlot]);
    }

    float[] GetShapeAngles(GameObject shape)
    {
        var tess = shape.GetComponent<Tesseract>();
        if (tess != null) return tess.GetAngles();

        var hexa = shape.GetComponent<Hexadecachoron>();
        if (hexa != null) return hexa.GetAngles();

        return null;
    }

    void SetShapeAngles(GameObject shape, float[] a)
    {
        var tess = shape.GetComponent<Tesseract>();
        if (tess != null) { tess.SetAngles(a); return; }

        var hexa = shape.GetComponent<Hexadecachoron>();
        if (hexa != null) { hexa.SetAngles(a); return; }
    }

    GameObject GetActiveShape()
    {
        if (shapes == null || activeIndex < 0 || activeIndex >= shapes.Length)
            return null;
        var s = shapes[activeIndex];
        return (s != null && s.activeInHierarchy) ? s : null;
    }

    public string GetSlotDisplay()
    {
        var sb = new System.Text.StringBuilder();
        sb.Append($"Slot [{currentSlot + 1}/{slotCount}] ");
        for (int i = 0; i < slotCount; i++)
            sb.Append(slotUsed[i] ? "+" : "o");
        return sb.ToString();
    }
}
