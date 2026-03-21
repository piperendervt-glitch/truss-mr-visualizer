using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.XR;

public class Tesseract : MonoBehaviour
{
    Vector4[] verts4D = new Vector4[16];
    Vector3[] verts3D = new Vector3[16];
    LineRenderer[] lines;
    float size = 0.15f;
    float angleXW, angleZW, angleXY, angleYW;

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

        int edgeCount = 0;
        for (int a = 0; a < 16; a++)
            for (int b = a + 1; b < 16; b++)
                if (DiffersByOne(verts4D[a], verts4D[b])) edgeCount++;

        lines = new LineRenderer[edgeCount];
        int idx = 0;
        for (int a = 0; a < 16; a++)
            for (int b = a + 1; b < 16; b++)
            {
                if (!DiffersByOne(verts4D[a], verts4D[b])) continue;
                var go = new GameObject("edge");
                go.transform.parent = transform;
                var lr = go.AddComponent<LineRenderer>();
                lr.positionCount = 2;
                lr.startWidth = size * 0.05f;
                lr.endWidth = size * 0.05f;
                lr.useWorldSpace = true;
                lr.material = new Material(Shader.Find("Sprites/Default"));
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
        // �G�f�B�^�F�L�[�{�[�h����
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