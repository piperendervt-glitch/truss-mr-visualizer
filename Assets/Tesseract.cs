using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.XR;
using UnityEngine.Rendering;

public class Tesseract : MonoBehaviour
{
    Vector4[] verts4D = new Vector4[16];
    Vector3[] verts3D = new Vector3[16];
    float size = 0.15f;
    float angleXW, angleZW, angleXY, angleYW;

    // Faces (24 quads, each defined by 4 vertex indices)
    int[][] faceIndices;
    Mesh faceMesh;
    Vector3[] meshVerts;

    // Wireframe edges
    LineRenderer[] lines;

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

    // --- Translucent face mesh (URP Transparent, Cull Off) ---
    void SetupFaceMesh()
    {
        int faceCount = faceIndices.Length; // 24
        meshVerts = new Vector3[faceCount * 4];
        int[] tris = new int[faceCount * 6];

        for (int f = 0; f < faceCount; f++)
        {
            tris[f * 6 + 0] = f * 4 + 0;
            tris[f * 6 + 1] = f * 4 + 1;
            tris[f * 6 + 2] = f * 4 + 2;
            tris[f * 6 + 3] = f * 4 + 0;
            tris[f * 6 + 4] = f * 4 + 2;
            tris[f * 6 + 5] = f * 4 + 3;
        }

        faceMesh = new Mesh();
        faceMesh.vertices = meshVerts;
        faceMesh.triangles = tris;

        var go = new GameObject("faces");
        go.transform.SetParent(transform, false);

        go.AddComponent<MeshFilter>().mesh = faceMesh;
        var mr = go.AddComponent<MeshRenderer>();

        // URP Unlit, Transparent, Double-sided
        var shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null) shader = Shader.Find("Sprites/Default");
        var mat = new Material(shader);
        mat.SetFloat("_Surface", 1f);     // Transparent
        mat.SetFloat("_Blend", 0f);       // Alpha blend
        mat.SetFloat("_Cull", (float)CullMode.Off);
        mat.SetFloat("_ZWrite", 0f);
        mat.SetFloat("_AlphaClip", 0f);
        mat.SetColor("_BaseColor", new Color(0f, 0.85f, 1f, 0.35f));
        mat.SetOverrideTag("RenderType", "Transparent");
        mat.renderQueue = (int)RenderQueue.Transparent;
        mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        mat.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
        mat.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
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
        float speed = 1.5f * Time.deltaTime;
        float lx = 0f, ly = 0f, rx = 0f, ry = 0f;

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
        }
#else
        // Quest 3: read thumbstick via Input System XRController
        var leftCtrl = XRController.leftHand;
        if (leftCtrl != null)
        {
            var stick = leftCtrl.TryGetChildControl<StickControl>("thumbstick");
            if (stick != null) { lx = stick.x.ReadValue(); ly = stick.y.ReadValue(); }
        }

        var rightCtrl = XRController.rightHand;
        if (rightCtrl != null)
        {
            var stick = rightCtrl.TryGetChildControl<StickControl>("thumbstick");
            if (stick != null) { rx = stick.x.ReadValue(); ry = stick.y.ReadValue(); }
        }
#endif

        angleXW += lx * speed;
        angleXY += ly * speed;
        angleZW += rx * speed;
        angleYW += ry * speed;

        // Project 4D -> 3D
        for (int i = 0; i < 16; i++)
        {
            Vector4 v = verts4D[i];
            v = RotateXW(v, angleXW);
            v = RotateXY(v, angleXY);
            v = RotateZW(v, angleZW);
            v = RotateYW(v, angleYW);
            float p = 2f / (2f - v.w);
            verts3D[i] = transform.position + new Vector3(
                v.x * p * size,
                v.y * p * size,
                v.z * p * size);
        }

        // Update face mesh
        for (int f = 0; f < faceIndices.Length; f++)
        {
            int[] q = faceIndices[f];
            meshVerts[f * 4 + 0] = verts3D[q[0]];
            meshVerts[f * 4 + 1] = verts3D[q[1]];
            meshVerts[f * 4 + 2] = verts3D[q[2]];
            meshVerts[f * 4 + 3] = verts3D[q[3]];
        }
        faceMesh.vertices = meshVerts;
        faceMesh.RecalculateNormals();
        faceMesh.RecalculateBounds();

        // Update wireframe edges
        int idx = 0;
        for (int a = 0; a < 16; a++)
            for (int b = a + 1; b < 16; b++)
            {
                if (!DiffersByOne(verts4D[a], verts4D[b])) continue;
                lines[idx].SetPosition(0, verts3D[a]);
                lines[idx].SetPosition(1, verts3D[b]);
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
