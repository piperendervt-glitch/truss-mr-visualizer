using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.XR;

public class PetersenGraph : MonoBehaviour
{
    // Petersen graph: 10 vertices, 15 edges
    Vector3[] verts = new Vector3[10];
    float outerRadius = 0.18f;
    float innerRadius = 0.09f;

    // Edges: outer pentagon (5) + inner pentagram (5) + spokes (5) = 15
    static readonly int[][] edges = {
        // Outer pentagon: 0-1-2-3-4-0
        new[]{0,1}, new[]{1,2}, new[]{2,3}, new[]{3,4}, new[]{4,0},
        // Inner pentagram: 5-7-9-6-8-5
        new[]{5,7}, new[]{7,9}, new[]{9,6}, new[]{6,8}, new[]{8,5},
        // Spokes: outer i → inner i
        new[]{0,5}, new[]{1,6}, new[]{2,7}, new[]{3,8}, new[]{4,9}
    };

    LineRenderer[] edgeLines;
    bool placedOnStart;
    float rotSpeed = 60f;

    void Start()
    {
        foreach (Transform child in transform)
            Destroy(child.gameObject);

        // Outer 5 vertices (0-4): regular pentagon
        for (int i = 0; i < 5; i++)
        {
            float angle = i * Mathf.PI * 2f / 5f - Mathf.PI / 2f;
            verts[i] = new Vector3(Mathf.Cos(angle) * outerRadius, 0f, Mathf.Sin(angle) * outerRadius);
        }
        // Inner 5 vertices (5-9): pentagon rotated 36 degrees
        for (int i = 0; i < 5; i++)
        {
            float angle = i * Mathf.PI * 2f / 5f - Mathf.PI / 2f + Mathf.Deg2Rad * 36f;
            verts[i + 5] = new Vector3(Mathf.Cos(angle) * innerRadius, 0f, Mathf.Sin(angle) * innerRadius);
        }

        SetupEdges();
    }

    void SetupEdges()
    {
        edgeLines = new LineRenderer[edges.Length];
        var mat = new Material(Shader.Find("Sprites/Default"));
        var orange = new Color(1f, 0.6f, 0.2f, 1f);

        for (int i = 0; i < edges.Length; i++)
        {
            var go = new GameObject("edge");
            go.transform.SetParent(transform, false);
            var lr = go.AddComponent<LineRenderer>();
            lr.positionCount = 2;
            lr.startWidth = 0.003f;
            lr.endWidth = 0.003f;
            lr.useWorldSpace = true;
            lr.material = mat;
            lr.startColor = orange;
            lr.endColor = orange;
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
        for (int i = 0; i < edges.Length; i++)
        {
            edgeLines[i].SetPosition(0, pos + rot * verts[edges[i][0]]);
            edgeLines[i].SetPosition(1, pos + rot * verts[edges[i][1]]);
        }
    }

    public string GetParamLabel()
    {
        return "Petersen  V:10 E:15";
    }
}
