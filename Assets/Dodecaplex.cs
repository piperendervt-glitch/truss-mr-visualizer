using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.XR;

public class Dodecaplex : MonoBehaviour
{
    Vector4[] verts4D;
    Vector3[] localVerts;
    float size = 0.06f;

    // 6 rotation planes: 0=XY, 1=XZ, 2=XW, 3=YZ, 4=YW, 5=ZW
    float[] angles = new float[6];
    public static readonly string[] planeNames = { "XY", "XZ", "XW", "YZ", "YW", "ZW" };

    // Left stick pairs: [stickX_plane, stickY_plane]
    public static readonly int[][] leftPairs = {
        new[]{2,0}, new[]{1,3}, new[]{2,1}, new[]{4,3}, new[]{0,3},
    };
    public static readonly int[][] rightPairs = {
        new[]{5,4}, new[]{2,5}, new[]{3,4}, new[]{1,5}, new[]{0,2},
    };

    public int currentLeftPair;
    public int currentRightPair;
    bool prevLeftClick, prevRightClick, prevDpadLeft, prevDpadRight;

    // Speed control
    public int speedLevel = 1;
    static readonly float[] speedValues = { 0.5f, 1.5f, 3.0f };
    public static readonly string[] speedNames = { "Slow", "Normal", "Fast" };

    public float[] GetAngles() { return (float[])angles.Clone(); }
    public void SetAngles(float[] a) { if (a != null && a.Length == 6) System.Array.Copy(a, angles, 6); }

    // Axis proximity override
    [HideInInspector] public bool planesOverridden;
    [HideInInspector] public int[] overridePlanes;

    int[][] edgePairs;
    LineRenderer[] lines;
    bool placedOnStart;

    // 12 even permutations of (0,1,2,3) — alternating group A4
    static readonly int[][] evenPerms = {
        new[]{0,1,2,3}, new[]{0,2,3,1}, new[]{0,3,1,2},
        new[]{1,0,3,2}, new[]{1,2,0,3}, new[]{1,3,2,0},
        new[]{2,0,1,3}, new[]{2,1,3,0}, new[]{2,3,0,1},
        new[]{3,0,2,1}, new[]{3,1,0,2}, new[]{3,2,1,0},
    };

    void Start()
    {
        foreach (Transform child in transform)
            Destroy(child.gameObject);

        GenerateVertices();
        FindEdges();
        SetupEdges();
    }

    void GenerateVertices()
    {
        float phi = (1f + Mathf.Sqrt(5f)) / 2f;
        float phi2 = phi + 1f;       // phi^2
        float iphi = phi - 1f;       // 1/phi
        float iphi2 = 2f - phi;      // 1/phi^2
        float sqrt5 = Mathf.Sqrt(5f);

        // 7 base vectors on 3-sphere of radius 2sqrt2 (norm^2 = 8)
        // All even permutations + all sign changes generate 600 vertices
        float[][] bases = {
            new[]{0f, 0f, 2f, 2f},          // -> 24
            new[]{1f, 1f, 1f, sqrt5},        // -> 64
            new[]{iphi2, phi, phi, phi},      // -> 64
            new[]{iphi, iphi, iphi, phi2},    // -> 64
            new[]{iphi, 1f, phi, 2f},         // -> 192
            new[]{0f, iphi2, 1f, phi2},       // -> 96
            new[]{0f, iphi, phi, sqrt5},      // -> 96
        };

        var vertSet = new System.Collections.Generic.HashSet<long>();
        var vertList = new System.Collections.Generic.List<Vector4>();

        foreach (var b in bases)
        {
            foreach (var perm in evenPerms)
            {
                float p0 = b[perm[0]], p1 = b[perm[1]], p2 = b[perm[2]], p3 = b[perm[3]];

                for (int s = 0; s < 16; s++)
                {
                    float x = ((s & 1) != 0) ? -p0 : p0;
                    float y = ((s & 2) != 0) ? -p1 : p1;
                    float z = ((s & 4) != 0) ? -p2 : p2;
                    float w = ((s & 8) != 0) ? -p3 : p3;

                    // Scale down by 2 so coords fit in [-1, 1]
                    x *= 0.5f; y *= 0.5f; z *= 0.5f; w *= 0.5f;

                    long key = HashVert(x, y, z, w);
                    if (vertSet.Add(key))
                        vertList.Add(new Vector4(x, y, z, w));
                }
            }
        }

        verts4D = vertList.ToArray();
        localVerts = new Vector3[verts4D.Length];
        Debug.Log($"Dodecaplex: {verts4D.Length} vertices generated");
    }

    static long HashVert(float x, float y, float z, float w)
    {
        int ix = Mathf.RoundToInt(x * 10000f) + 20000;
        int iy = Mathf.RoundToInt(y * 10000f) + 20000;
        int iz = Mathf.RoundToInt(z * 10000f) + 20000;
        int iw = Mathf.RoundToInt(w * 10000f) + 20000;
        return ((long)ix << 48) | ((long)iy << 32) | ((long)iz << 16) | (long)iw;
    }

    void FindEdges()
    {
        float phi = (1f + Mathf.Sqrt(5f)) / 2f;
        // Edge length^2 in original coords: 8 - 4*phi
        // After /2 scaling: (8 - 4*phi) / 4 = 2 - phi ≈ 0.382
        float edgeLenSq = 2f - phi;
        float tolerance = 0.02f;

        var edgeList = new System.Collections.Generic.List<int[]>();
        int n = verts4D.Length;

        for (int a = 0; a < n; a++)
        {
            for (int b = a + 1; b < n; b++)
            {
                Vector4 d = verts4D[a] - verts4D[b];
                float distSq = d.x * d.x + d.y * d.y + d.z * d.z + d.w * d.w;
                if (Mathf.Abs(distSq - edgeLenSq) < tolerance)
                    edgeList.Add(new[] { a, b });
            }
        }

        edgePairs = edgeList.ToArray();
        Debug.Log($"Dodecaplex: {edgePairs.Length} edges found");
    }

    void SetupEdges()
    {
        lines = new LineRenderer[edgePairs.Length];
        var edgeMat = new Material(Shader.Find("Sprites/Default"));
        Color edgeColor = new Color(1f, 0.4f, 0.7f, 1f); // magenta/pink

        for (int i = 0; i < edgePairs.Length; i++)
        {
            var go = new GameObject("edge");
            go.transform.SetParent(transform, false);
            var lr = go.AddComponent<LineRenderer>();
            lr.positionCount = 2;
            lr.startWidth = 0.002f;
            lr.endWidth = 0.002f;
            lr.useWorldSpace = true;
            lr.material = edgeMat;
            lr.startColor = edgeColor;
            lr.endColor = edgeColor;
            lines[i] = lr;
        }
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

        if (!grabbed && !MenuUI.isMenuOpen)
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

        // Project 4D -> 3D (perspective projection)
        int vertCount = verts4D.Length;
        for (int i = 0; i < vertCount; i++)
        {
            Vector4 v = verts4D[i];
            v = Rotate4D(v, 0, angles[0]);
            v = Rotate4D(v, 1, angles[1]);
            v = Rotate4D(v, 2, angles[2]);
            v = Rotate4D(v, 3, angles[3]);
            v = Rotate4D(v, 4, angles[4]);
            v = Rotate4D(v, 5, angles[5]);
            float p = 2f / (2f - v.w);
            localVerts[i] = new Vector3(v.x * p * size, v.y * p * size, v.z * p * size);
        }

        // Update wireframe edges
        Vector3 pos = transform.position;
        Quaternion rot = transform.rotation;
        for (int e = 0; e < edgePairs.Length; e++)
        {
            int a = edgePairs[e][0], b = edgePairs[e][1];
            lines[e].SetPosition(0, pos + rot * localVerts[a]);
            lines[e].SetPosition(1, pos + rot * localVerts[b]);
        }
    }

    Vector4 Rotate4D(Vector4 v, int plane, float a)
    {
        float c = Mathf.Cos(a), s = Mathf.Sin(a);
        switch (plane)
        {
            case 0: return new Vector4(v.x*c - v.y*s, v.x*s + v.y*c, v.z, v.w);
            case 1: return new Vector4(v.x*c - v.z*s, v.y, v.x*s + v.z*c, v.w);
            case 2: return new Vector4(v.x*c - v.w*s, v.y, v.z, v.x*s + v.w*c);
            case 3: return new Vector4(v.x, v.y*c - v.z*s, v.y*s + v.z*c, v.w);
            case 4: return new Vector4(v.x, v.y*c - v.w*s, v.z, v.y*s + v.w*c);
            case 5: return new Vector4(v.x, v.y, v.z*c - v.w*s, v.z*s + v.w*c);
            default: return v;
        }
    }
}
