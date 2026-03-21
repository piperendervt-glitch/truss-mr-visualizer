using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.XR;
using UnityEngine.Rendering;

public class Tesseract : MonoBehaviour
{
    Vector4[] verts4D = new Vector4[16];
    Vector3[] localVerts = new Vector3[16]; // local offsets from transform.position
    float size = 0.10f;
    float angleXW, angleZW, angleXY, angleYW;

    // Faces (24 quads, each defined by 4 vertex indices)
    int[][] faceIndices;
    Mesh faceMesh;
    Vector3[] meshVerts;

    // Wireframe edges
    LineRenderer[] lines;
    bool placedOnStart;

    void Start()
    {
        foreach (Transform child in transform)
            Destroy(child.gameObject);

        // Generate 16 vertices of a tesseract
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

    // --- vertex index from sign values (-1 or +1) ---
    int Idx(int x, int y, int z, int w)
    {
        return ((x + 1) / 2) * 8 + ((y + 1) / 2) * 4 + ((z + 1) / 2) * 2 + ((w + 1) / 2);
    }

    float Comp(Vector4 v, int dim)
    {
        switch (dim) { case 0: return v.x; case 1: return v.y; case 2: return v.z; default: return v.w; }
    }

    // --- Build the 24 square faces ---
    void BuildFaces()
    {
        // A tesseract has 24 faces.
        // Each face: pick 2 dims to vary (6 pairs), fix the other 2 dims at +/-1 (4 combos) = 24.
        var list = new System.Collections.Generic.List<int[]>();

        for (int d0 = 0; d0 < 4; d0++)
        {
            for (int d1 = d0 + 1; d1 < 4; d1++)
            {
                // The two fixed dimensions
                int f0 = -1, f1 = -1;
                for (int d = 0; d < 4; d++)
                    if (d != d0 && d != d1) { if (f0 < 0) f0 = d; else f1 = d; }

                for (int sv0 = -1; sv0 <= 1; sv0 += 2)
                {
                    for (int sv1 = -1; sv1 <= 1; sv1 += 2)
                    {
                        // Find the 4 vertices that match fixed dims, ordered as a quad
                        // Winding: (--), (-+), (++), (+-) in the varying dims d0,d1
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

    // --- Translucent face mesh (Sprites/Default: alpha-blend + double-sided built-in) ---
    void SetupFaceMesh()
    {
        int faceCount = faceIndices.Length; // 24
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

        // Sprites/Default: always available, alpha-blend + double-sided built-in,
        // never stripped by shader variant stripping.
        // Uses vertex color * _Color, so transparency works out of the box.
        var mat = new Material(Shader.Find("Sprites/Default"));
        mat.color = Color.white; // vertex color drives the actual tint
        mat.renderQueue = (int)RenderQueue.Transparent;
        mr.material = mat;
    }

    // --- Wireframe edges (thin cyan lines) ---
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
        // A: Place in front of HMD once on first frame
        if (!placedOnStart)
        {
            PlaceInFrontOfCamera();
            placedOnStart = true;
        }

        // Pause 4D rotation while grabbed
        var grabber = GetComponent<ShapeGrabber>();
        bool grabbed = grabber != null && grabber.isGrabbed;

        float speed = 1.5f * Time.deltaTime;
        float lx = 0f, ly = 0f, rx = 0f, ry = 0f;
        bool gripPressed = false;

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
        }
#else
        // Quest 3: read thumbstick via Input System XRController
        var leftCtrl = XRController.leftHand;
        if (leftCtrl != null)
        {
            var stick = leftCtrl.TryGetChildControl<StickControl>("thumbstick");
            if (stick != null) { lx = stick.x.ReadValue(); ly = stick.y.ReadValue(); }

            // B: Left grip to reposition
            var grip = leftCtrl.TryGetChildControl<AxisControl>("grip");
            if (grip != null && grip.ReadValue() > 0.5f) gripPressed = true;
        }

        var rightCtrl = XRController.rightHand;
        if (rightCtrl != null)
        {
            var stick = rightCtrl.TryGetChildControl<StickControl>("thumbstick");
            if (stick != null) { rx = stick.x.ReadValue(); ry = stick.y.ReadValue(); }
        }
#endif
        } // end if (!grabbed)

        // B: Reposition on grip press
        if (gripPressed) PlaceInFrontOfCamera();

        angleXW += lx * speed;
        angleXY += ly * speed;
        angleZW += rx * speed;
        angleYW += ry * speed;

        // Project 4D -> 3D (local offsets)
        for (int i = 0; i < 16; i++)
        {
            Vector4 v = verts4D[i];
            v = RotateXW(v, angleXW);
            v = RotateXY(v, angleXY);
            v = RotateZW(v, angleZW);
            v = RotateYW(v, angleYW);
            float p = 2f / (2f - v.w);
            localVerts[i] = new Vector3(v.x * p * size, v.y * p * size, v.z * p * size);
        }

        // Update face mesh (local space — mesh GO is at transform.position)
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

        // Update wireframe edges (world space, applying transform rotation)
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

    // --- 4D Rotations ---
    Vector4 RotateXW(Vector4 v, float a)
    {
        float c = Mathf.Cos(a), s = Mathf.Sin(a);
        return new Vector4(v.x * c - v.w * s, v.y, v.z, v.x * s + v.w * c);
    }
    Vector4 RotateXY(Vector4 v, float a)
    {
        float c = Mathf.Cos(a), s = Mathf.Sin(a);
        return new Vector4(v.x * c - v.y * s, v.x * s + v.y * c, v.z, v.w);
    }
    Vector4 RotateZW(Vector4 v, float a)
    {
        float c = Mathf.Cos(a), s = Mathf.Sin(a);
        return new Vector4(v.x, v.y, v.z * c - v.w * s, v.z * s + v.w * c);
    }
    Vector4 RotateYW(Vector4 v, float a)
    {
        float c = Mathf.Cos(a), s = Mathf.Sin(a);
        return new Vector4(v.x, v.y * c - v.w * s, v.z, v.y * s + v.w * c);
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
