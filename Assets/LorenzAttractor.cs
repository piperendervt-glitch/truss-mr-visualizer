using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.XR;

public class LorenzAttractor : MonoBehaviour
{
    // Lorenz parameters
    public float sigma = 10f;
    public float rho = 28f;
    public float beta = 2.667f;
    public float dt = 0.005f;

    // Parameter ranges
    const float sigmaMin = 5f, sigmaMax = 20f;
    const float rhoMin = 20f, rhoMax = 35f;
    const float betaMin = 1f, betaMax = 4f;
    const float dtMin = 0.001f, dtMax = 0.02f;

    // Trail
    const int maxPoints = 2000;
    const int stepsPerFrame = 5;
    const float lorenzScale = 0.005f; // ~0.3m total size

    Vector3[] trailPoints;
    float[] speeds;
    int head;
    int count;

    // Current Lorenz state
    float lx, ly, lz;

    LineRenderer lr;
    bool placedOnStart;

    void Start()
    {
        foreach (Transform child in transform)
            Destroy(child.gameObject);

        var go = new GameObject("LorenzTrail");
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
        lx = 0.1f;
        ly = 0f;
        lz = 0f;
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
            float prevX = lx, prevY = ly, prevZ = lz;

            float dxdt = sigma * (ly - lx);
            float dydt = lx * (rho - lz) - ly;
            float dzdt = lx * ly - beta * lz;

            lx += dxdt * dt;
            ly += dydt * dt;
            lz += dzdt * dt;

            // Divergence guard
            if (float.IsNaN(lx) || float.IsInfinity(lx) ||
                float.IsNaN(ly) || float.IsInfinity(ly) ||
                float.IsNaN(lz) || float.IsInfinity(lz))
            {
                ResetTrail();
                return;
            }

            // Map to world: x→right, (z-25)→up, y→forward, scaled
            Vector3 localPos = new Vector3(lx, lz - 25f, ly) * lorenzScale;
            Vector3 worldPos = center + transform.rotation * localPos;

            float dx = lx - prevX, dy = ly - prevY, dz = lz - prevZ;
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

        // Color gradient: 8 sample points, slow=blue → fast=red
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
            colorKeys[k] = new GradientColorKey(Color.Lerp(Color.blue, Color.red, norm), t);
        }

        var grad = new Gradient();
        grad.SetKeys(colorKeys, alphaKeys);
        lr.colorGradient = grad;
    }

    void HandleInput()
    {
        float paramSpeed = Time.deltaTime;
        float lxIn = 0f, lyIn = 0f, rxIn = 0f, ryIn = 0f;
        bool gripPressed = false;

#if UNITY_EDITOR
        var kb = Keyboard.current;
        if (kb != null)
        {
            if (kb.leftArrowKey.isPressed) lxIn = -1f;
            if (kb.rightArrowKey.isPressed) lxIn = 1f;
            if (kb.upArrowKey.isPressed) lyIn = 1f;
            if (kb.downArrowKey.isPressed) lyIn = -1f;
            if (kb.jKey.isPressed) rxIn = -1f;
            if (kb.lKey.isPressed) rxIn = 1f;
            if (kb.iKey.isPressed) ryIn = 1f;
            if (kb.kKey.isPressed) ryIn = -1f;
            if (kb.gKey.wasPressedThisFrame) gripPressed = true;
        }
#else
        var leftCtrl = XRController.leftHand;
        if (leftCtrl != null)
        {
            var stick = leftCtrl.TryGetChildControl<StickControl>("thumbstick");
            if (stick != null) { lxIn = stick.x.ReadValue(); lyIn = stick.y.ReadValue(); }

            var grip = leftCtrl.TryGetChildControl<AxisControl>("grip");
            if (grip != null && grip.ReadValue() > 0.5f) gripPressed = true;
        }
        var rightCtrl = XRController.rightHand;
        if (rightCtrl != null)
        {
            var stick = rightCtrl.TryGetChildControl<StickControl>("thumbstick");
            if (stick != null) { rxIn = stick.x.ReadValue(); ryIn = stick.y.ReadValue(); }
        }
#endif

        if (gripPressed) PlaceInFrontOfCamera();

        // Left stick LR → sigma (5~20)
        sigma = Mathf.Clamp(sigma + lxIn * (sigmaMax - sigmaMin) * paramSpeed, sigmaMin, sigmaMax);
        // Left stick FB → rho (20~35)
        rho = Mathf.Clamp(rho + lyIn * (rhoMax - rhoMin) * paramSpeed, rhoMin, rhoMax);
        // Right stick LR → beta (1~4)
        beta = Mathf.Clamp(beta + rxIn * (betaMax - betaMin) * paramSpeed, betaMin, betaMax);
        // Right stick FB → dt (speed)
        dt = Mathf.Clamp(dt + ryIn * 0.01f * paramSpeed, dtMin, dtMax);
    }

    // Public accessors for ShapeManager / DebugDisplay
    public string GetParamLabel()
    {
        return $"Lorenz \u03c3:{sigma:F1} \u03c1:{rho:F1} \u03b2:{beta:F2}";
    }
}
