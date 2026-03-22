using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.XR;
using UnityEngine.Rendering;
using TMPro;

public class FanoGraph : MonoBehaviour
{
    // Fano plane: 7 vertices, 21 edges, 7 triangular faces
    Vector3[] verts = new Vector3[7];
    float radius = 0.15f;

    // 7 lines of the Fano plane (each contains 3 collinear points)
    static readonly int[][] lines = {
        new[]{0,1,2}, new[]{0,3,4}, new[]{0,5,6},
        new[]{1,3,5}, new[]{1,4,6}, new[]{2,3,6}, new[]{2,4,5}
    };

    // φ values per line: +1=red, -1=blue
    int[] phi = { +1, -1, +1, -1, +1, -1, +1 };

    // Edge weights (21 edges)
    float[] edgeWeights;

    LineRenderer[] edgeLines;
    Mesh faceMesh;
    Vector3[] meshVerts;
    Color[] meshColors;
    int[][] edgeList; // pairs of vertex indices
    TextMeshPro[] vertexLabels;
    bool placedOnStart;

    // Rotation speed
    float rotSpeed = 60f;

    void Start()
    {
        foreach (Transform child in transform)
            Destroy(child.gameObject);

        // Vertex positions: vertex 0 at center, 1-6 on heptagonal ring
        verts[0] = Vector3.zero;
        for (int i = 1; i <= 6; i++)
        {
            float angle = (i - 1) * Mathf.PI * 2f / 6f;
            verts[i] = new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);
        }

        // Build edge list from lines
        var edges = new System.Collections.Generic.List<int[]>();
        var edgeSet = new System.Collections.Generic.HashSet<int>();
        foreach (var line in lines)
        {
            for (int a = 0; a < 3; a++)
                for (int b = a + 1; b < 3; b++)
                {
                    int lo = Mathf.Min(line[a], line[b]);
                    int hi = Mathf.Max(line[a], line[b]);
                    int key = lo * 100 + hi;
                    if (edgeSet.Add(key))
                        edges.Add(new[] { lo, hi });
                }
        }
        edgeList = edges.ToArray();

        // Default weights
        edgeWeights = new float[edgeList.Length];
        for (int i = 0; i < edgeWeights.Length; i++)
            edgeWeights[i] = 1.0f;

        SetupFaces();
        SetupEdges();
        SetupLabels();
    }

    void SetupFaces()
    {
        int faceCount = lines.Length; // 7 faces
        meshVerts = new Vector3[faceCount * 3];
        int[] tris = new int[faceCount * 6]; // double-sided
        meshColors = new Color[faceCount * 3];

        for (int f = 0; f < faceCount; f++)
        {
            // Front face
            tris[f * 6 + 0] = f * 3 + 0;
            tris[f * 6 + 1] = f * 3 + 1;
            tris[f * 6 + 2] = f * 3 + 2;
            // Back face
            tris[f * 6 + 3] = f * 3 + 2;
            tris[f * 6 + 4] = f * 3 + 1;
            tris[f * 6 + 5] = f * 3 + 0;
        }

        UpdateFaceColors();
        UpdateFaceVerts();

        faceMesh = new Mesh();
        faceMesh.vertices = meshVerts;
        faceMesh.triangles = tris;
        faceMesh.colors = meshColors;

        var go = new GameObject("faces");
        go.transform.SetParent(transform, false);
        go.AddComponent<MeshFilter>().mesh = faceMesh;
        var mr = go.AddComponent<MeshRenderer>();
        var mat = new Material(Shader.Find("Sprites/Default"));
        mat.color = Color.white;
        mat.renderQueue = (int)RenderQueue.Transparent;
        mr.material = mat;
    }

    void UpdateFaceColors()
    {
        for (int f = 0; f < lines.Length; f++)
        {
            Color c = phi[f] > 0
                ? new Color(1f, 0f, 0f, 0.4f)
                : new Color(0f, 0f, 1f, 0.4f);
            meshColors[f * 3 + 0] = c;
            meshColors[f * 3 + 1] = c;
            meshColors[f * 3 + 2] = c;
        }
    }

    void UpdateFaceVerts()
    {
        for (int f = 0; f < lines.Length; f++)
        {
            meshVerts[f * 3 + 0] = verts[lines[f][0]];
            meshVerts[f * 3 + 1] = verts[lines[f][1]];
            meshVerts[f * 3 + 2] = verts[lines[f][2]];
        }
    }

    void SetupEdges()
    {
        edgeLines = new LineRenderer[edgeList.Length];
        var edgeMat = new Material(Shader.Find("Sprites/Default"));

        for (int i = 0; i < edgeList.Length; i++)
        {
            var go = new GameObject("edge");
            go.transform.SetParent(transform, false);
            var lr = go.AddComponent<LineRenderer>();
            lr.positionCount = 2;
            float w = 0.003f * edgeWeights[i];
            lr.startWidth = w;
            lr.endWidth = w;
            lr.useWorldSpace = true;
            lr.material = edgeMat;
            lr.startColor = Color.white;
            lr.endColor = Color.white;
            edgeLines[i] = lr;
        }
    }

    void SetupLabels()
    {
        vertexLabels = new TextMeshPro[7];
        for (int i = 0; i < 7; i++)
        {
            var go = new GameObject($"label_{i}");
            go.transform.SetParent(transform, false);
            var tmp = go.AddComponent<TextMeshPro>();
            tmp.text = i.ToString();
            tmp.fontSize = 0.05f;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = Color.yellow;
            tmp.rectTransform.sizeDelta = new Vector2(0.1f, 0.06f);
            vertexLabels[i] = tmp;
        }
    }

    void PlaceInFrontOfCamera()
    {
        var cam = Camera.main;
        if (cam == null) return;
        transform.position = cam.transform.position + cam.transform.forward * 1.5f;
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

        if (!grabbed && !MenuUI.isMenuOpen)
        {
            float rx = 0f, ry = 0f;
#if UNITY_EDITOR
            var kb = Keyboard.current;
            if (kb != null)
            {
                if (kb.jKey.isPressed) rx = -1f;
                if (kb.lKey.isPressed) rx = 1f;
                if (kb.iKey.isPressed) ry = 1f;
                if (kb.kKey.isPressed) ry = -1f;
            }
#else
            var rightCtrl = XRController.rightHand;
            if (rightCtrl != null)
            {
                var stick = rightCtrl.TryGetChildControl<StickControl>("thumbstick");
                if (stick != null) { rx = stick.x.ReadValue(); ry = stick.y.ReadValue(); }
            }
#endif
            transform.Rotate(Vector3.up, rx * rotSpeed * Time.deltaTime, Space.World);
            transform.Rotate(Vector3.right, ry * rotSpeed * Time.deltaTime, Space.World);
        }

        // Update edges
        Vector3 pos = transform.position;
        Quaternion rot = transform.rotation;
        for (int i = 0; i < edgeList.Length; i++)
        {
            edgeLines[i].SetPosition(0, pos + rot * verts[edgeList[i][0]]);
            edgeLines[i].SetPosition(1, pos + rot * verts[edgeList[i][1]]);
        }

        // Update labels billboard toward camera
        var cam = Camera.main;
        for (int i = 0; i < 7; i++)
        {
            vertexLabels[i].transform.position = pos + rot * verts[i] + Vector3.up * 0.015f;
            if (cam != null)
            {
                vertexLabels[i].transform.LookAt(cam.transform);
                vertexLabels[i].transform.Rotate(0f, 180f, 0f);
            }
        }
    }

    public string GetParamLabel()
    {
        int pos = 0, neg = 0;
        foreach (int p in phi) { if (p > 0) pos++; else neg++; }
        return $"Fano φ: +{pos}/-{neg}";
    }

    // --- API for TRUSS real-time updates ---

    public void SetPhiValues(int[] phiValues)
    {
        if (phiValues == null || phiValues.Length != 7) return;
        System.Array.Copy(phiValues, phi, 7);
        UpdateFaceColors();
        if (faceMesh != null)
            faceMesh.colors = meshColors;
    }

    public void SetEdgeWeights(float[] weights)
    {
        if (weights == null || weights.Length != edgeList.Length) return;
        System.Array.Copy(weights, edgeWeights, weights.Length);
        for (int i = 0; i < edgeLines.Length; i++)
        {
            float w = 0.003f * edgeWeights[i];
            edgeLines[i].startWidth = w;
            edgeLines[i].endWidth = w;
        }
    }
}
