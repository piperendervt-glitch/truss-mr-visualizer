using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.XR;
using UnityEngine.Rendering;

public class Tesseract : MonoBehaviour
{
    Vector4[] verts4D = new Vector4[16];
    Vector3[] localVerts = new Vector3[16];
    float size = 0.10f;

    // 6 rotation planes: 0=XY, 1=XZ, 2=XW, 3=YZ, 4=YW, 5=ZW
    float[] angles = new float[6];
    public static readonly string[] planeNames = { "XY", "XZ", "XW", "YZ", "YW", "ZW" };

    // Left stick pairs: [stickX_plane, stickY_plane]
    public static readonly int[][] leftPairs = {
        new[]{2,0}, // XW/XY
        new[]{1,3}, // XZ/YZ
        new[]{2,1}, // XW/XZ
        new[]{4,3}, // YW/YZ
        new[]{0,3}, // XY/YZ
    };
    // Right stick pairs
    public static readonly int[][] rightPairs = {
        new[]{5,4}, // ZW/YW
        new[]{2,5}, // XW/ZW
        new[]{3,4}, // YZ/YW
        new[]{1,5}, // XZ/ZW
        new[]{0,2}, // XY/XW
    };

    public int currentLeftPair;
    public int currentRightPair;
    bool prevLeftClick, prevRightClick, prevDpadLeft, prevDpadRight;

    // Speed control: 0=Slow, 1=Normal, 2=Fast
    public int speedLevel = 1;
    static readonly float[] speedValues = { 0.5f, 1.5f, 3.0f };
    public static readonly string[] speedNames = { "Slow", "Normal", "Fast" };

    public float[] GetAngles() { return (float[])angles.Clone(); }
    public void SetAngles(float[] a) { if (a != null && a.Length == 6) System.Array.Copy(a, angles, 6); }

    // Axis proximity override
    [HideInInspector] public bool planesOverridden;
    [HideInInspector] public int[] overridePlanes; // [0]=stickX plane, [1]=stickY plane

    int[][] faceIndices;
    Mesh faceMesh;
    Vector3[] meshVerts;
    LineRenderer[] lines;
    bool placedOnStart;

    void Start()
    {
        foreach (Transform child in transform)
            Destroy(child.gameObject);

        int i = 0;
        for (int x = -1; x <= 1; x += 2)
            for (int y = -1; y <= 1; y += 2)
                for (int z = -1; z <= 1; z += 2)
                    for (int w = -1; w <= 1; w += 2)
                        verts4D[i++] = new Vector4(x, y, z, w);

        BuildFaces();
        SetupFaceMesh();
        SetupEdges();
    }

    void PlaceInFrontOfCamera()
    {
        var cam = Camera.main;
        if (cam == null) return;
        transform.position = cam.transform.position + cam.transform.forward * 0.8f;
    }

    public string GetPairLabel()
    {
        int[] lp = planesOverridden && overridePlanes != null
            ? overridePlanes : leftPairs[currentLeftPair];
        var rp = rightPairs[currentRightPair];
        return $"L:{planeNames[lp[0]]}/{planeNames[lp[1]]}  R:{planeNames[rp[0]]}/{planeNames[rp[1]]}";
    }

    float Comp(Vector4 v, int dim)
    {
        switch (dim) { case 0: return v.x; case 1: return v.y; case 2: return v.z; default: return v.w; }
    }

    void BuildFaces()
    {
        var list = new System.Collections.Generic.List<int[]>();
        for (int d0 = 0; d0 < 4; d0++)
        {
            for (int d1 = d0 + 1; d1 < 4; d1++)
            {
                int f0 = -1, f1 = -1;
                for (int d = 0; d < 4; d++)
                    if (d != d0 && d != d1) { if (f0 < 0) f0 = d; else f1 = d; }

                for (int sv0 = -1; sv0 <= 1; sv0 += 2)
                {
                    for (int sv1 = -1; sv1 <= 1; sv1 += 2)
                    {
                        int[][] order = { new[]{-1,-1}, new[]{-1,1}, new[]{1,1}, new[]{1,-1} };
                        int[] quad = new int[4];
                        for (int q = 0; q < 4; q++)
                        {
                            for (int vi = 0; vi < 16; vi++)
                            {
                                Vector4 v = verts4D[vi];
                                if (Comp(v, f0) == sv0 && Comp(v, f1) == sv1 &&
                                    Comp(v, d0) == order[q][0] && Comp(v, d1) == order[q][1])
                                {
                                    quad[q] = vi;
                                    break;
                                }
                            }
                        }
                        list.Add(quad);
                    }
                }
            }
        }
        faceIndices = list.ToArray();
    }

    void SetupFaceMesh()
    {
        int faceCount = faceIndices.Length;
        meshVerts = new Vector3[faceCount * 4];
        int[] tris = new int[faceCount * 6];
        Color[] colors = new Color[faceCount * 4];
        var faceColor = new Color(0f, 0.85f, 1f, 0.35f);

        for (int f = 0; f < faceCount; f++)
        {
            tris[f * 6 + 0] = f * 4 + 0;
            tris[f * 6 + 1] = f * 4 + 1;
            tris[f * 6 + 2] = f * 4 + 2;
            tris[f * 6 + 3] = f * 4 + 0;
            tris[f * 6 + 4] = f * 4 + 2;
            tris[f * 6 + 5] = f * 4 + 3;
            colors[f * 4 + 0] = faceColor;
            colors[f * 4 + 1] = faceColor;
            colors[f * 4 + 2] = faceColor;
            colors[f * 4 + 3] = faceColor;
        }

        faceMesh = new Mesh();
        faceMesh.vertices = meshVerts;
        faceMesh.triangles = tris;
        faceMesh.colors = colors;

        var go = new GameObject("faces");
        go.transform.SetParent(transform, false);
        go.AddComponent<MeshFilter>().mesh = faceMesh;
        var mr = go.AddComponent<MeshRenderer>();
        var mat = new Material(Shader.Find("Sprites/Default"));
        mat.color = Color.white;
        mat.renderQueue = (int)RenderQueue.Transparent;
        mr.material = mat;
    }

    void SetupEdges()
    {
        int edgeCount = 0;
        for (int a = 0; a < 16; a++)
            for (int b = a + 1; b < 16; b++)
                if (DiffersByOne(verts4D[a], verts4D[b])) edgeCount++;

        lines = new LineRenderer[edgeCount];
        int idx = 0;
        var edgeMat = new Material(Shader.Find("Sprites/Default"));
        for (int a = 0; a < 16; a++)
            for (int b = a + 1; b < 16; b++)
            {
                if (!DiffersByOne(verts4D[a], verts4D[b])) continue;
                var go = new GameObject("edge");
                go.transform.SetParent(transform, false);
                var lr = go.AddComponent<LineRenderer>();
                lr.positionCount = 2;
                lr.startWidth = size * 0.03f;
                lr.endWidth = size * 0.03f;
                lr.useWorldSpace = true;
                lr.material = edgeMat;
                lr.startColor = Color.cyan;
                lr.endColor = Color.cyan;
                lines[idx++] = lr;
            }
    }

    void Update()
    {
        if (!placedOnStart)
        {
            PlaceInFrontOfCamera();
            placedOnStart = true;
        }

        var grabber = GetComponent<ShapeGrabber>();
        bool grabbed = grabber != null && grabber.isGrabbed;

        float speed = speedValues[speedLevel] * Time.deltaTime;
        float lx = 0f, ly = 0f, rx = 0f, ry = 0f;
        bool gripPressed = false;
        bool leftClickDown = false, rightClickDown = false;

        if (!grabbed)
        {
#if UNITY_EDITOR
        var kb = Keyboard.current;
        if (kb != null)
        {
            if (kb.leftArrowKey.isPressed) lx = -1f;
            if (kb.rightArrowKey.isPressed) lx = 1f;
            if (kb.upArrowKey.isPressed) ly = 1f;
            if (kb.downArrowKey.isPressed) ly = -1f;
            if (kb.jKey.isPressed) rx = -1f;
            if (kb.lKey.isPressed) rx = 1f;
            if (kb.iKey.isPressed) ry = 1f;
            if (kb.kKey.isPressed) ry = -1f;
            if (kb.gKey.wasPressedThisFrame) gripPressed = true;
            if (kb.qKey.wasPressedThisFrame) leftClickDown = true;
            if (kb.eKey.wasPressedThisFrame) rightClickDown = true;
            if (kb.digit1Key.wasPressedThisFrame) speedLevel = 0;
            if (kb.digit2Key.wasPressedThisFrame) speedLevel = 1;
            if (kb.digit3Key.wasPressedThisFrame) speedLevel = 2;
        }
#else
        var leftCtrl = XRController.leftHand;
        if (leftCtrl != null)
        {
            var stick = leftCtrl.TryGetChildControl<StickControl>("thumbstick");
            if (stick != null) { lx = stick.x.ReadValue(); ly = stick.y.ReadValue(); }

            var grip = leftCtrl.TryGetChildControl<AxisControl>("grip");
            if (grip != null && grip.ReadValue() > 0.5f) gripPressed = true;

            var lClick = leftCtrl.TryGetChildControl<ButtonControl>("thumbstickClicked");
            if (lClick != null)
            {
                bool p = lClick.isPressed;
                if (p && !prevLeftClick) leftClickDown = true;
                prevLeftClick = p;
            }
        }

        var rightCtrl = XRController.rightHand;
        if (rightCtrl != null)
        {
            var stick = rightCtrl.TryGetChildControl<StickControl>("thumbstick");
            if (stick != null) { rx = stick.x.ReadValue(); ry = stick.y.ReadValue(); }

            var rClick = rightCtrl.TryGetChildControl<ButtonControl>("thumbstickClicked");
            if (rClick != null)
            {
                bool p = rClick.isPressed;
                if (p && !prevRightClick) rightClickDown = true;
                prevRightClick = p;
            }

            // Dpad left/right via left controller thumbstick X for speed
        }

        if (leftCtrl != null)
        {
            var lstick = leftCtrl.TryGetChildControl<StickControl>("thumbstick");
            if (lstick != null)
            {
                bool dL = lstick.x.ReadValue() < -0.8f;
                bool dR = lstick.x.ReadValue() > 0.8f;
                if (dL && !prevDpadLeft && speedLevel > 0) speedLevel--;
                if (dR && !prevDpadRight && speedLevel < 2) speedLevel++;
                prevDpadLeft = dL;
                prevDpadRight = dR;
            }
        }
#endif
        } // end if (!grabbed)

        // Cycle rotation plane pairs
        if (leftClickDown) currentLeftPair = (currentLeftPair + 1) % leftPairs.Length;
        if (rightClickDown) currentRightPair = (currentRightPair + 1) % rightPairs.Length;

        if (gripPressed) PlaceInFrontOfCamera();

        // Apply stick input to selected rotation planes
        int[] lp = planesOverridden && overridePlanes != null
            ? overridePlanes : leftPairs[currentLeftPair];
        var rp = rightPairs[currentRightPair];
        angles[lp[0]] += lx * speed;
        angles[lp[1]] += ly * speed;
        angles[rp[0]] += rx * speed;
        angles[rp[1]] += ry * speed;

        // Project 4D -> 3D
        for (int i = 0; i < 16; i++)
        {
            Vector4 v = verts4D[i];
            v = Rotate4D(v, 0, angles[0]); // XY
            v = Rotate4D(v, 1, angles[1]); // XZ
            v = Rotate4D(v, 2, angles[2]); // XW
            v = Rotate4D(v, 3, angles[3]); // YZ
            v = Rotate4D(v, 4, angles[4]); // YW
            v = Rotate4D(v, 5, angles[5]); // ZW
            float p = 2f / (2f - v.w);
            localVerts[i] = new Vector3(v.x * p * size, v.y * p * size, v.z * p * size);
        }

        // Update face mesh
        for (int f = 0; f < faceIndices.Length; f++)
        {
            int[] q = faceIndices[f];
            meshVerts[f * 4 + 0] = localVerts[q[0]];
            meshVerts[f * 4 + 1] = localVerts[q[1]];
            meshVerts[f * 4 + 2] = localVerts[q[2]];
            meshVerts[f * 4 + 3] = localVerts[q[3]];
        }
        faceMesh.vertices = meshVerts;
        faceMesh.RecalculateNormals();
        faceMesh.RecalculateBounds();

        // Update wireframe edges
        Vector3 pos = transform.position;
        Quaternion rot = transform.rotation;
        int idx = 0;
        for (int a = 0; a < 16; a++)
            for (int b = a + 1; b < 16; b++)
            {
                if (!DiffersByOne(verts4D[a], verts4D[b])) continue;
                lines[idx].SetPosition(0, pos + rot * localVerts[a]);
                lines[idx].SetPosition(1, pos + rot * localVerts[b]);
                idx++;
            }
    }

    // --- Generic 4D rotation for all 6 planes ---
    Vector4 Rotate4D(Vector4 v, int plane, float a)
    {
        float c = Mathf.Cos(a), s = Mathf.Sin(a);
        switch (plane)
        {
            case 0: // XY
                return new Vector4(v.x*c - v.y*s, v.x*s + v.y*c, v.z, v.w);
            case 1: // XZ
                return new Vector4(v.x*c - v.z*s, v.y, v.x*s + v.z*c, v.w);
            case 2: // XW
                return new Vector4(v.x*c - v.w*s, v.y, v.z, v.x*s + v.w*c);
            case 3: // YZ
                return new Vector4(v.x, v.y*c - v.z*s, v.y*s + v.z*c, v.w);
            case 4: // YW
                return new Vector4(v.x, v.y*c - v.w*s, v.z, v.y*s + v.w*c);
            case 5: // ZW
                return new Vector4(v.x, v.y, v.z*c - v.w*s, v.z*s + v.w*c);
            default: return v;
        }
    }

    bool DiffersByOne(Vector4 a, Vector4 b)
    {
        int d = 0;
        if (a.x != b.x) d++;
        if (a.y != b.y) d++;
        if (a.z != b.z) d++;
        if (a.w != b.w) d++;
        return d == 1;
    }
}
