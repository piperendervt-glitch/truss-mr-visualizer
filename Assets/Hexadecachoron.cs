using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.XR;
using UnityEngine.Rendering;

public class Hexadecachoron : MonoBehaviour
{
    // 16-cell: 8 vertices, 24 edges, 32 triangular faces
    Vector4[] verts4D = new Vector4[8];
    Vector3[] localVerts = new Vector3[8]; // local offsets from transform.position
    float size = 0.15f;
    float angleXW, angleZW, angleXY, angleYW;

    int[][] faceIndices; // 32 triangles, each 3 vertex indices
    Mesh faceMesh;
    Vector3[] meshVerts;

    LineRenderer[] lines;
    bool placedOnStart;

    void Start()
    {
        foreach (Transform child in transform)
            Destroy(child.gameObject);

        // 8 vertices: ±1 along each of the 4 axes
        verts4D[0] = new Vector4(+1, 0, 0, 0);
        verts4D[1] = new Vector4(-1, 0, 0, 0);
        verts4D[2] = new Vector4(0, +1, 0, 0);
        verts4D[3] = new Vector4(0, -1, 0, 0);
        verts4D[4] = new Vector4(0, 0, +1, 0);
        verts4D[5] = new Vector4(0, 0, -1, 0);
        verts4D[6] = new Vector4(0, 0, 0, +1);
        verts4D[7] = new Vector4(0, 0, 0, -1);

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

    // --- 32 triangular faces ---
    // Pick 3 of 4 axis pairs, then choose +/- from each => C(4,3)*2^3 = 32
    void BuildFaces()
    {
        var list = new System.Collections.Generic.List<int[]>();
        int[][] axisPairs = { new[]{0,1}, new[]{2,3}, new[]{4,5}, new[]{6,7} };

        for (int skip = 0; skip < 4; skip++)
        {
            // Choose 3 axis pairs (skip one)
            int[] chosen = new int[3];
            int ci = 0;
            for (int a = 0; a < 4; a++)
                if (a != skip) chosen[ci++] = a;

            // 2^3 = 8 sign combinations
            for (int sign = 0; sign < 8; sign++)
            {
                int v0 = axisPairs[chosen[0]][(sign >> 0) & 1];
                int v1 = axisPairs[chosen[1]][(sign >> 1) & 1];
                int v2 = axisPairs[chosen[2]][(sign >> 2) & 1];
                list.Add(new[] { v0, v1, v2 });
            }
        }
        faceIndices = list.ToArray(); // 32
    }

    // --- Translucent triangle mesh ---
    void SetupFaceMesh()
    {
        int faceCount = faceIndices.Length; // 32
        meshVerts = new Vector3[faceCount * 3];
        int[] tris = new int[faceCount * 3];
        Color[] colors = new Color[faceCount * 3];

        var faceColor = new Color(0f, 0.85f, 1f, 0.35f);

        for (int f = 0; f < faceCount; f++)
        {
            tris[f * 3 + 0] = f * 3 + 0;
            tris[f * 3 + 1] = f * 3 + 1;
            tris[f * 3 + 2] = f * 3 + 2;
            colors[f * 3 + 0] = faceColor;
            colors[f * 3 + 1] = faceColor;
            colors[f * 3 + 2] = faceColor;
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

    // --- 24 edges (all non-opposing pairs, distance = sqrt2) ---
    void SetupEdges()
    {
        var edgeList = new System.Collections.Generic.List<int[]>();
        for (int a = 0; a < 8; a++)
            for (int b = a + 1; b < 8; b++)
                if (!IsOpposing(a, b))
                    edgeList.Add(new[] { a, b });

        lines = new LineRenderer[edgeList.Count]; // 24
        var edgeMat = new Material(Shader.Find("Sprites/Default"));
        for (int i = 0; i < edgeList.Count; i++)
        {
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
            lines[i] = lr;
        }
    }

    // Opposing vertices: (0,1), (2,3), (4,5), (6,7)
    bool IsOpposing(int a, int b)
    {
        int lo = Mathf.Min(a, b);
        int hi = Mathf.Max(a, b);
        return (lo % 2 == 0) && (hi == lo + 1);
    }

    void Update()
    {
        // A: Place in front of HMD once on first frame
        if (!placedOnStart)
        {
            PlaceInFrontOfCamera();
            placedOnStart = true;
        }

        float speed = 1.5f * Time.deltaTime;
        float lx = 0f, ly = 0f, rx = 0f, ry = 0f;
        bool gripPressed = false;

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

        // B: Reposition on grip press
        if (gripPressed) PlaceInFrontOfCamera();

        angleXW += lx * speed;
        angleXY += ly * speed;
        angleZW += rx * speed;
        angleYW += ry * speed;

        // 4D rotation + perspective projection (local offsets)
        for (int i = 0; i < 8; i++)
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
            int[] tri = faceIndices[f];
            meshVerts[f * 3 + 0] = localVerts[tri[0]];
            meshVerts[f * 3 + 1] = localVerts[tri[1]];
            meshVerts[f * 3 + 2] = localVerts[tri[2]];
        }
        faceMesh.vertices = meshVerts;
        faceMesh.RecalculateNormals();
        faceMesh.RecalculateBounds();

        // Update wireframe edges (world space)
        Vector3 pos = transform.position;
        int idx = 0;
        for (int a = 0; a < 8; a++)
            for (int b = a + 1; b < 8; b++)
            {
                if (IsOpposing(a, b)) continue;
                lines[idx].SetPosition(0, pos + localVerts[a]);
                lines[idx].SetPosition(1, pos + localVerts[b]);
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
}
