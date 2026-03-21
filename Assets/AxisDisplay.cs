using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.XR;

public class AxisDisplay : MonoBehaviour
{
    public GameObject[] shapes; // same as ShapeManager.shapes
    public int activeIndex;     // set by ShapeManager

    // Axis definitions
    static readonly Vector3[] axisDirections = {
        Vector3.right,                                // X
        Vector3.up,                                   // Y
        Vector3.forward,                              // Z
        new Vector3(0.707f, 0.707f, 0f),              // W (upper-right diagonal)
    };
    static readonly Color[] axisColors = {
        new Color(1f, 0f, 0f, 1f),         // X red
        new Color(0f, 1f, 0f, 1f),         // Y green
        new Color(0f, 0f, 1f, 1f),         // Z blue
        new Color(1f, 0.9f, 0f, 1f),       // W yellow
    };
    static readonly string[] axisNames = { "X", "Y", "Z", "W" };

    // Rotation planes containing each axis
    // Plane indices: 0=XY, 1=XZ, 2=XW, 3=YZ, 4=YW, 5=ZW
    static readonly int[][] axisPlanes = {
        new[]{0, 1, 2}, // X → XY, XZ, XW
        new[]{0, 3, 4}, // Y → XY, YZ, YW
        new[]{1, 3, 5}, // Z → XZ, YZ, ZW
        new[]{2, 4, 5}, // W → XW, YW, ZW
    };

    const float axisOffset = 0.25f;
    const float arrowLength = 0.08f;
    const float normalWidth = 0.003f;
    const float highlightWidth = 0.007f;
    const float selectDist = 0.05f;
    const float highlightDist = 0.1f;

    LineRenderer[] shaftLines = new LineRenderer[4];
    LineRenderer[] tipLinesA = new LineRenderer[4]; // arrow tip left
    LineRenderer[] tipLinesB = new LineRenderer[4]; // arrow tip right
    GameObject axisRoot;

    // Selection state
    public int selectedAxis = -1; // -1=none, 0=X,1=Y,2=Z,3=W
    int axisPlaneCycleIndex;
    float blinkTimer;
    float[] axisDists = new float[4];

    public float GetAxisDistance(int i) { return (i >= 0 && i < 4) ? axisDists[i] : float.MaxValue; }

    // Saved pair to restore
    int savedLeftPair = -1;

    void Start()
    {
        axisRoot = new GameObject("AxisDisplay");
        axisRoot.transform.SetParent(transform, false);

        var mat = new Material(Shader.Find("Sprites/Default"));

        for (int i = 0; i < 4; i++)
        {
            // Shaft
            shaftLines[i] = CreateLine(mat, axisColors[i]);

            // Arrow tip (two angled lines)
            tipLinesA[i] = CreateLine(mat, axisColors[i]);
            tipLinesB[i] = CreateLine(mat, axisColors[i]);
        }
    }

    LineRenderer CreateLine(Material mat, Color color)
    {
        var go = new GameObject("axisLine");
        go.transform.SetParent(axisRoot.transform, false);
        var lr = go.AddComponent<LineRenderer>();
        lr.positionCount = 2;
        lr.startWidth = normalWidth;
        lr.endWidth = normalWidth;
        lr.useWorldSpace = true;
        lr.material = mat;
        lr.startColor = color;
        lr.endColor = color;
        return lr;
    }

    void Update()
    {
        GameObject activeShape = GetActiveShape();
        if (activeShape == null)
        {
            axisRoot.SetActive(false);
            return;
        }
        axisRoot.SetActive(true);

        Vector3 shapePos = activeShape.transform.position;
        Quaternion shapeRot = activeShape.transform.rotation;

        // Get controller position for proximity
        Vector3 ctrlPos = GetControllerWorldPos();

        int prevSelected = selectedAxis;
        selectedAxis = -1;
        float closestDist = float.MaxValue;

        blinkTimer += Time.deltaTime;

        for (int i = 0; i < 4; i++)
        {
            Vector3 dir = shapeRot * axisDirections[i];
            Vector3 axisStart = shapePos + dir * axisOffset;
            Vector3 axisEnd = axisStart + dir * arrowLength;

            // Set shaft positions
            shaftLines[i].SetPosition(0, axisStart);
            shaftLines[i].SetPosition(1, axisEnd);

            // Arrow tip
            Vector3 tipPerp = shapeRot * GetPerpendicular(axisDirections[i]);
            float tipSize = arrowLength * 0.35f;
            tipLinesA[i].SetPosition(0, axisEnd);
            tipLinesA[i].SetPosition(1, axisEnd - dir * tipSize + tipPerp * tipSize * 0.5f);
            tipLinesB[i].SetPosition(0, axisEnd);
            tipLinesB[i].SetPosition(1, axisEnd - dir * tipSize - tipPerp * tipSize * 0.5f);

            // Distance from controller to axis midpoint
            Vector3 axisMid = (axisStart + axisEnd) * 0.5f;
            float dist = Vector3.Distance(ctrlPos, axisMid);
            axisDists[i] = dist;

            if (dist < closestDist && dist < highlightDist)
            {
                closestDist = dist;
                if (dist < selectDist)
                    selectedAxis = i;
            }

            // Visual state
            bool highlight = dist < highlightDist;
            bool selected = false;
            if (selectedAxis == i) selected = true;

            float width = highlight ? highlightWidth : normalWidth;
            Color col = axisColors[i];

            if (selected)
            {
                // Blink effect
                float alpha = 0.5f + 0.5f * Mathf.Sin(blinkTimer * 10f);
                col.a = alpha;
            }
            else if (highlight)
            {
                col = Color.Lerp(col, Color.white, 0.4f);
            }

            SetLineAppearance(shaftLines[i], width, col);
            SetLineAppearance(tipLinesA[i], width, col);
            SetLineAppearance(tipLinesB[i], width, col);
        }

        // Handle axis selection → override rotation planes
        if (selectedAxis != prevSelected)
        {
            if (selectedAxis >= 0)
            {
                // Save current left pair and apply axis-specific override
                SaveAndOverrideLeftPair(activeShape, selectedAxis);
                axisPlaneCycleIndex = 0;
            }
            else
            {
                // Restore saved pair
                RestoreLeftPair(activeShape);
            }
        }
    }

    void SetLineAppearance(LineRenderer lr, float width, Color col)
    {
        lr.startWidth = width;
        lr.endWidth = width;
        lr.startColor = col;
        lr.endColor = col;
    }

    Vector3 GetPerpendicular(Vector3 dir)
    {
        if (Mathf.Abs(Vector3.Dot(dir, Vector3.up)) < 0.9f)
            return Vector3.Cross(dir, Vector3.up).normalized;
        return Vector3.Cross(dir, Vector3.right).normalized;
    }

    Vector3 GetControllerWorldPos()
    {
#if UNITY_EDITOR
        // Editor: use mouse position projected into world at shape depth
        var mouse = Mouse.current;
        var cam = Camera.main;
        if (mouse != null && cam != null)
        {
            Vector3 mousePos = mouse.position.ReadValue();
            mousePos.z = 0.8f;
            return cam.ScreenToWorldPoint(mousePos);
        }
        return Vector3.zero;
#else
        var rightCtrl = XRController.rightHand;
        if (rightCtrl != null)
        {
            var posCtrl = rightCtrl.TryGetChildControl<Vector3Control>("devicePosition");
            if (posCtrl != null) return posCtrl.ReadValue();
        }
        return Vector3.zero;
#endif
    }

    void SaveAndOverrideLeftPair(GameObject shape, int axis)
    {
        // Build a left pair from the axis's first two planes
        int[] planes = axisPlanes[axis];
        int planeA = planes[0];
        int planeB = planes[1];

        var tess = shape.GetComponent<Tesseract>();
        if (tess != null)
        {
            if (savedLeftPair < 0) savedLeftPair = tess.currentLeftPair;
            // Find or force a matching pair — set directly via angles mapping
            tess.overridePlanes = new int[] { planeA, planeB };
            tess.planesOverridden = true;
        }

        var hexa = shape.GetComponent<Hexadecachoron>();
        if (hexa != null)
        {
            if (savedLeftPair < 0) savedLeftPair = hexa.currentLeftPair;
            hexa.overridePlanes = new int[] { planeA, planeB };
            hexa.planesOverridden = true;
        }

        var dodeca = shape.GetComponent<Dodecaplex>();
        if (dodeca != null)
        {
            if (savedLeftPair < 0) savedLeftPair = dodeca.currentLeftPair;
            dodeca.overridePlanes = new int[] { planeA, planeB };
            dodeca.planesOverridden = true;
        }
    }

    void RestoreLeftPair(GameObject shape)
    {
        var tess = shape.GetComponent<Tesseract>();
        if (tess != null)
        {
            tess.planesOverridden = false;
            if (savedLeftPair >= 0) tess.currentLeftPair = savedLeftPair;
        }

        var hexa = shape.GetComponent<Hexadecachoron>();
        if (hexa != null)
        {
            hexa.planesOverridden = false;
            if (savedLeftPair >= 0) hexa.currentLeftPair = savedLeftPair;
        }

        var dodeca = shape.GetComponent<Dodecaplex>();
        if (dodeca != null)
        {
            dodeca.planesOverridden = false;
            if (savedLeftPair >= 0) dodeca.currentLeftPair = savedLeftPair;
        }

        savedLeftPair = -1;
    }

    GameObject GetActiveShape()
    {
        if (shapes == null || activeIndex < 0 || activeIndex >= shapes.Length)
            return null;
        var s = shapes[activeIndex];
        return (s != null && s.activeInHierarchy) ? s : null;
    }

    public string GetSelectedAxisName()
    {
        if (selectedAxis >= 0 && selectedAxis < axisNames.Length)
            return $"[{axisNames[selectedAxis]}-Axis]";
        return "";
    }
}
