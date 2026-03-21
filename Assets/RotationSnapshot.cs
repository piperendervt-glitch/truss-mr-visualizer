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

    // Called by ShapeManager on A short press
    public void SaveToCurrentSlot()
    {
        GameObject shape = GetActiveShape();
        if (shape == null) return;

        float[] a = GetShapeAngles(shape);
        if (a == null) return;

        slots[currentSlot] = (float[])a.Clone();
        slotUsed[currentSlot] = true;
    }

    // Called by ShapeManager on B long press
    public void CycleSlotAndRestore()
    {
        currentSlot = (currentSlot + 1) % slotCount;

        // Auto-restore if slot has saved data
        if (slotUsed[currentSlot])
        {
            GameObject shape = GetActiveShape();
            if (shape != null)
                SetShapeAngles(shape, slots[currentSlot]);
        }
    }

    float[] GetShapeAngles(GameObject shape)
    {
        var tess = shape.GetComponent<Tesseract>();
        if (tess != null) return tess.GetAngles();

        var hexa = shape.GetComponent<Hexadecachoron>();
        if (hexa != null) return hexa.GetAngles();

        var dodeca = shape.GetComponent<Dodecaplex>();
        if (dodeca != null) return dodeca.GetAngles();

        return null;
    }

    void SetShapeAngles(GameObject shape, float[] a)
    {
        var tess = shape.GetComponent<Tesseract>();
        if (tess != null) { tess.SetAngles(a); return; }

        var hexa = shape.GetComponent<Hexadecachoron>();
        if (hexa != null) { hexa.SetAngles(a); return; }

        var dodeca = shape.GetComponent<Dodecaplex>();
        if (dodeca != null) { dodeca.SetAngles(a); return; }
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
        {
            if (i == currentSlot)
                sb.Append(slotUsed[i] ? "[+]" : "[o]");
            else
                sb.Append(slotUsed[i] ? "+" : "o");
        }
        return sb.ToString();
    }
}
