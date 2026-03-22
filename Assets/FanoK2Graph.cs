using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.XR;

public class FanoK2Graph : MonoBehaviour
{
    // Fano×K₂: 14 vertices, 49 edges (21+21 intra-layer + 7 inter-layer)
    Vector3[] verts = new Vector3[14];
    float radius = 0.15f;
    float layerOffset = 0.1f;

    // Fano plane lines
    static readonly int[][] fanoLines = {
        new[]{0,1,2}, new[]{0,3,4}, new[]{0,5,6},
        new[]{1,3,5}, new[]{1,4,6}, new[]{2,3,6}, new[]{2,4,5}
    };

    LineRenderer[] edgeLines;
    int[][] edgeList;
    bool placedOnStart;
    float rotSpeed = 60f;

    void Start()
    {
        foreach (Transform child in transform)
            Destroy(child.gameObject);

        // Layer 1 (Y=+offset): vertices 0-6
        // Layer 2 (Y=-offset): vertices 7-13
        Vector3[] basePos = new Vector3[7];
        basePos[0] = Vector3.zero;
        for (int i = 1; i <= 6; i++)
        {
            float angle = (i - 1) * Mathf.PI * 2f / 6f;
            basePos[i] = new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);
        }

        for (int i = 0; i < 7; i++)
        {
            verts[i] = basePos[i] + Vector3.up * layerOffset;
            verts[i + 7] = basePos[i] + Vector3.down * layerOffset;
        }

        // Build edge list
        var edges = new System.Collections.Generic.List<int[]>();
        var edgeSet = new System.Collections.Generic.HashSet<int>();

        // Layer 1 edges (from Fano lines, vertices 0-6)
        AddFanoEdges(edges, edgeSet, 0);
        // Layer 2 edges (from Fano lines, vertices 7-13)
        AddFanoEdges(edges, edgeSet, 7);
        // Inter-layer edges
        for (int i = 0; i < 7; i++)
            edges.Add(new[] { i, i + 7 });

        edgeList = edges.ToArray();
        SetupEdges();
    }

    void AddFanoEdges(System.Collections.Generic.List<int[]> edges,
                      System.Collections.Generic.HashSet<int> edgeSet, int offset)
    {
        foreach (var line in fanoLines)
        {
            for (int a = 0; a < 3; a++)
                for (int b = a + 1; b < 3; b++)
                {
                    int v0 = line[a] + offset;
                    int v1 = line[b] + offset;
                    int lo = Mathf.Min(v0, v1);
                    int hi = Mathf.Max(v0, v1);
                    int key = lo * 100 + hi;
                    if (edgeSet.Add(key))
                        edges.Add(new[] { lo, hi });
                }
        }
    }

    void SetupEdges()
    {
        edgeLines = new LineRenderer[edgeList.Length];
        var mat = new Material(Shader.Find("Sprites/Default"));

        for (int i = 0; i < edgeList.Length; i++)
        {
            var go = new GameObject("edge");
            go.transform.SetParent(transform, false);
            var lr = go.AddComponent<LineRenderer>();
            lr.positionCount = 2;
            lr.useWorldSpace = true;
            lr.material = mat;

            int v0 = edgeList[i][0];
            int v1 = edgeList[i][1];
            bool isLayer1 = v0 < 7 && v1 < 7;
            bool isLayer2 = v0 >= 7 && v1 >= 7;

            if (isLayer1)
            {
                lr.startColor = Color.cyan;
                lr.endColor = Color.cyan;
                lr.startWidth = 0.003f;
                lr.endWidth = 0.003f;
            }
            else if (isLayer2)
            {
                lr.startColor = Color.magenta;
                lr.endColor = Color.magenta;
                lr.startWidth = 0.003f;
                lr.endWidth = 0.003f;
            }
            else
            {
                // Inter-layer
                lr.startColor = Color.white;
                lr.endColor = Color.white;
                lr.startWidth = 0.0015f;
                lr.endWidth = 0.0015f;
            }

            edgeLines[i] = lr;
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

        Vector3 pos = transform.position;
        Quaternion rot = transform.rotation;
        for (int i = 0; i < edgeList.Length; i++)
        {
            edgeLines[i].SetPosition(0, pos + rot * verts[edgeList[i][0]]);
            edgeLines[i].SetPosition(1, pos + rot * verts[edgeList[i][1]]);
        }
    }

    public string GetParamLabel()
    {
        return $"Fano×K₂  V:14 E:{edgeList.Length}";
    }
}
