using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.XR;

public class ThomasAttractor : MonoBehaviour
{
    // Thomas parameter
    public float b = 0.208186f;
    public float dt = 0.05f;

    // Parameter ranges
    const float bMin = 0.1f, bMax = 0.5f;
    const float dtMin = 0.005f, dtMax = 0.2f;

    // Trail
    const int maxPoints = 2000;
    const int stepsPerFrame = 5;
    const float thomasScale = 0.05f; // ~0.3m total (attractor spans ~[-3,3])

    Vector3[] trailPoints;
    float[] speeds;
    int head;
    int count;

    // Current Thomas state
    float tx, ty, tz;

    LineRenderer lr;
    bool placedOnStart;

    void Start()
    {
        foreach (Transform child in transform)
            Destroy(child.gameObject);

        var go = new GameObject("ThomasTrail");
        go.transform.SetParent(transform, false);
        lr = go.AddComponent<LineRenderer>();
        lr.useWorldSpace = true;
        lr.startWidth = 0.0015f;
        lr.endWidth = 0.0015f;
        lr.material = new Material(Shader.Find("Sprites/Default"));
        lr.positionCount = 0;
        lr.numCapVertices = 2;

        trailPoints = new Vector3[maxPoints];
        speeds = new float[maxPoints];

        ResetTrail();
    }

    public void ResetTrail()
    {
        tx = 0.1f;
        ty = 0f;
        tz = 0f;
        head = 0;
        count = 0;
        if (lr != null) lr.positionCount = 0;
    }

    void PlaceInFrontOfCamera()
    {
        var cam = Camera.main;
        if (cam == null) return;
        transform.position = cam.transform.position + cam.transform.forward * 0.8f;
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

        if (!grabbed)
            HandleInput();

        Vector3 center = transform.position;

        for (int step = 0; step < stepsPerFrame; step++)
        {
            float prevX = tx, prevY = ty, prevZ = tz;

            float dxdt = Mathf.Sin(ty) - b * tx;
            float dydt = Mathf.Sin(tz) - b * ty;
            float dzdt = Mathf.Sin(tx) - b * tz;

            tx += dxdt * dt;
            ty += dydt * dt;
            tz += dzdt * dt;

            // Divergence guard
            if (float.IsNaN(tx) || float.IsInfinity(tx) ||
                float.IsNaN(ty) || float.IsInfinity(ty) ||
                float.IsNaN(tz) || float.IsInfinity(tz))
            {
                ResetTrail();
                return;
            }

            Vector3 localPos = new Vector3(tx, ty, tz) * thomasScale;
            Vector3 worldPos = center + transform.rotation * localPos;

            float dx = tx - prevX, dy = ty - prevY, dz = tz - prevZ;
            float spd = Mathf.Sqrt(dx * dx + dy * dy + dz * dz);

            trailPoints[head] = worldPos;
            speeds[head] = spd;
            head = (head + 1) % maxPoints;
            if (count < maxPoints) count++;
        }

        UpdateLineRenderer();
    }

    void UpdateLineRenderer()
    {
        lr.positionCount = count;

        int start = count < maxPoints ? 0 : head;
        var positions = new Vector3[count];
        float maxSpeed = 0.001f;

        for (int i = 0; i < count; i++)
        {
            int idx = (start + i) % maxPoints;
            positions[i] = trailPoints[idx];
            if (speeds[idx] > maxSpeed) maxSpeed = speeds[idx];
        }

        lr.SetPositions(positions);

        // Color gradient: 8 sample points, slow=green -> fast=yellow
        int numKeys = Mathf.Min(8, Mathf.Max(2, count));
        var colorKeys = new GradientColorKey[numKeys];
        var alphaKeys = new GradientAlphaKey[2];
        alphaKeys[0] = new GradientAlphaKey(0.3f, 0f);
        alphaKeys[1] = new GradientAlphaKey(1f, 1f);

        for (int k = 0; k < numKeys; k++)
        {
            float t = (float)k / (numKeys - 1);
            int sampleIdx = (start + Mathf.FloorToInt(t * (count - 1))) % maxPoints;
            float norm = Mathf.Clamp01(speeds[sampleIdx] / maxSpeed);
            colorKeys[k] = new GradientColorKey(Color.Lerp(Color.green, Color.yellow, norm), t);
        }

        var grad = new Gradient();
        grad.SetKeys(colorKeys, alphaKeys);
        lr.colorGradient = grad;
    }

    void HandleInput()
    {
        float paramSpeed = Time.deltaTime;
        float lxIn = 0f, ryIn = 0f;
        bool gripPressed = false;

#if UNITY_EDITOR
        var kb = Keyboard.current;
        if (kb != null)
        {
            if (kb.leftArrowKey.isPressed) lxIn = -1f;
            if (kb.rightArrowKey.isPressed) lxIn = 1f;
            if (kb.iKey.isPressed) ryIn = 1f;
            if (kb.kKey.isPressed) ryIn = -1f;
            if (kb.gKey.wasPressedThisFrame) gripPressed = true;
        }
#else
        var leftCtrl = XRController.leftHand;
        if (leftCtrl != null)
        {
            var stick = leftCtrl.TryGetChildControl<StickControl>("thumbstick");
            if (stick != null) { lxIn = stick.x.ReadValue(); }

            var grip = leftCtrl.TryGetChildControl<AxisControl>("grip");
            if (grip != null && grip.ReadValue() > 0.5f) gripPressed = true;
        }
        var rightCtrl = XRController.rightHand;
        if (rightCtrl != null)
        {
            var stick = rightCtrl.TryGetChildControl<StickControl>("thumbstick");
            if (stick != null) { ryIn = stick.y.ReadValue(); }
        }
#endif

        if (gripPressed) PlaceInFrontOfCamera();

        // Left stick LR -> b (0.1~0.5)
        b = Mathf.Clamp(b + lxIn * (bMax - bMin) * paramSpeed, bMin, bMax);
        // Right stick FB -> dt (speed)
        dt = Mathf.Clamp(dt + ryIn * 0.1f * paramSpeed, dtMin, dtMax);
    }

    // Public accessors for ShapeManager / DebugDisplay
    public string GetParamLabel()
    {
        return $"Thomas b:{b:F3}";
    }
}
