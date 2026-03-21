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
    const float lorenzScale = 0.005f;

    Vector3[] trailPoints;
    float[] speeds;
    int head;
    int count;

    // Current Lorenz state
    float lx, ly, lz;

    LineRenderer lr;
    bool placedOnStart;

    // Lyapunov exponent (simplified Wolf method)
    float refX, refY, refZ;
    const float lyapD0 = 1e-4f; // must be > float epsilon * attractor_scale
    float lyapSum;
    int lyapFrames;
    public float lyapunovExponent;
    const int lyapAvgFrames = 30;

    // Per-particle Lyapunov (sampled every lyapSampleInterval-th particle)
    const int lyapSampleInterval = 4;
    float[] pRefX, pRefY, pRefZ, pLyapSum, particleLyap;
    int[] pLyapFrames;
    public float lyapMean, lyapMax, lyapMin;

    // Multi-particle mode
    public int particleCount = 200;
    public int trailLength = 100;
    const float particleRadius = 0.003f;
    const float scatterRange = 0.1f;
    const float multiLongPress = 1.0f;

    public bool multiMode;
    float yHoldTime;
    bool yWasPressed, yLongFired;

    float[] px, py, pz;
    GameObject[] particleSpheres;
    LineRenderer[] particleTrails;
    Vector3[][] pTrailPts;
    int[] pHeads, pCounts;
    GameObject multiRoot;
    Mesh sharedSphereMesh;
    Material sharedSphereMat;

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
        lx = 0.1f; ly = 0f; lz = 0f;
        lyapunovExponent = 0f;
        ResetLyapunov();
        head = 0; count = 0;
        if (lr != null) lr.positionCount = 0;

        if (multiMode) ResetParticles();
    }

    void ResetLyapunov()
    {
        refX = lx + lyapD0; refY = ly; refZ = lz;
        lyapSum = 0f; lyapFrames = 0;
        if (pLyapSum != null)
            for (int i = 0; i < pLyapSum.Length; i++) { pLyapSum[i] = 0f; pLyapFrames[i] = 0; }
        if (particleLyap != null)
            for (int i = 0; i < particleLyap.Length; i++) particleLyap[i] = 0f;
    }

    void ResetParticles()
    {
        if (px == null) return;
        for (int i = 0; i < particleCount; i++)
        {
            px[i] = 0.1f + Random.Range(-scatterRange, scatterRange);
            py[i] = Random.Range(-scatterRange, scatterRange);
            pz[i] = Random.Range(-scatterRange, scatterRange);
            pHeads[i] = 0;
            pCounts[i] = 0;
            if (particleTrails[i] != null) particleTrails[i].positionCount = 0;
            if (particleLyap != null) particleLyap[i] = 0f;
            if (pRefX != null && i % lyapSampleInterval == 0)
            {
                int si = i / lyapSampleInterval;
                pRefX[si] = px[i] + lyapD0; pRefY[si] = py[i]; pRefZ[si] = pz[i];
                pLyapSum[si] = 0f; pLyapFrames[si] = 0;
            }
        }
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

        if (!grabbed && !MenuUI.isMenuOpen)
            HandleInput();

        Vector3 center = transform.position;
        Quaternion rot = transform.rotation;

        // Main trail
        for (int step = 0; step < stepsPerFrame; step++)
        {
            float prevX = lx, prevY = ly, prevZ = lz;

            float dxdt = sigma * (ly - lx);
            float dydt = lx * (rho - lz) - ly;
            float dzdt = lx * ly - beta * lz;

            lx += dxdt * dt;
            ly += dydt * dt;
            lz += dzdt * dt;

            if (float.IsNaN(lx) || float.IsInfinity(lx) ||
                float.IsNaN(ly) || float.IsInfinity(ly) ||
                float.IsNaN(lz) || float.IsInfinity(lz))
            {
                ResetTrail();
                return;
            }

            Vector3 localPos = new Vector3(lx, lz - 25f, ly) * lorenzScale;
            Vector3 worldPos = center + rot * localPos;

            float dx = lx - prevX, dy = ly - prevY, dz = lz - prevZ;
            float spd = Mathf.Sqrt(dx * dx + dy * dy + dz * dz);

            trailPoints[head] = worldPos;
            speeds[head] = spd;
            head = (head + 1) % maxPoints;
            if (count < maxPoints) count++;
        }

        // Lyapunov: evolve reference particle
        for (int step = 0; step < stepsPerFrame; step++)
        {
            float rdx = sigma * (refY - refX);
            float rdy = refX * (rho - refZ) - refY;
            float rdz = refX * refY - beta * refZ;
            refX += rdx * dt; refY += rdy * dt; refZ += rdz * dt;
        }
        float sepX = refX - lx, sepY = refY - ly, sepZ = refZ - lz;
        float d1 = Mathf.Sqrt(sepX * sepX + sepY * sepY + sepZ * sepZ);
        if (d1 > 0f)
        {
            lyapSum += Mathf.Log(d1 / lyapD0);
            lyapFrames++;
            float s = lyapD0 / d1;
            refX = lx + sepX * s; refY = ly + sepY * s; refZ = lz + sepZ * s;
        }
        if (lyapFrames >= lyapAvgFrames)
        {
            lyapunovExponent = lyapSum / (lyapFrames * stepsPerFrame * dt);
#if UNITY_EDITOR
            Debug.Log($"[Lorenz] d1={d1:E3} lyapSum={lyapSum:F4} λ={lyapunovExponent:F4}");
#endif
            lyapSum = 0f; lyapFrames = 0;
        }

        UpdateLineRenderer();

        // Multi-particle update
        if (multiMode && px != null)
            UpdateParticles(center, rot);
    }

    void UpdateParticles(Vector3 center, Quaternion rot)
    {
        for (int i = 0; i < particleCount; i++)
        {
            float cx = px[i], cy = py[i], cz = pz[i];

            for (int step = 0; step < stepsPerFrame; step++)
            {
                float dxdt = sigma * (cy - cx);
                float dydt = cx * (rho - cz) - cy;
                float dzdt = cx * cy - beta * cz;

                cx += dxdt * dt;
                cy += dydt * dt;
                cz += dzdt * dt;
            }

            if (float.IsNaN(cx) || float.IsInfinity(cx))
            {
                cx = 0.1f + Random.Range(-scatterRange, scatterRange);
                cy = Random.Range(-scatterRange, scatterRange);
                cz = Random.Range(-scatterRange, scatterRange);
            }

            px[i] = cx; py[i] = cy; pz[i] = cz;

            Vector3 localPos = new Vector3(cx, cz - 25f, cy) * lorenzScale;
            Vector3 worldPos = center + rot * localPos;

            // Update sphere position
            particleSpheres[i].transform.position = worldPos;

            // Per-particle Lyapunov (sampled)
            if (particleLyap != null && i % lyapSampleInterval == 0 && pRefX != null)
            {
                int si = i / lyapSampleInterval;
                float rx = pRefX[si], ry = pRefY[si], rz = pRefZ[si];
                for (int step = 0; step < stepsPerFrame; step++)
                {
                    float rdx = sigma * (ry - rx);
                    float rdy = rx * (rho - rz) - ry;
                    float rdz = rx * ry - beta * rz;
                    rx += rdx * dt; ry += rdy * dt; rz += rdz * dt;
                }
                float sx = rx - cx, sy = ry - cy, sz = rz - cz;
                float dd = Mathf.Sqrt(sx * sx + sy * sy + sz * sz);
                if (dd > 0f)
                {
                    pLyapSum[si] += Mathf.Log(dd / lyapD0);
                    pLyapFrames[si]++;
                    float sc = lyapD0 / dd;
                    pRefX[si] = cx + sx * sc; pRefY[si] = cy + sy * sc; pRefZ[si] = cz + sz * sc;
                }
                if (pLyapFrames[si] >= lyapAvgFrames)
                {
                    float lv = pLyapSum[si] / (pLyapFrames[si] * stepsPerFrame * dt);
                    particleLyap[i] = lv;
                    for (int n = 1; n < lyapSampleInterval && i + n < particleCount; n++)
                        particleLyap[i + n] = lv;
                    pLyapSum[si] = 0f; pLyapFrames[si] = 0;
                }
            }

            // Color by per-particle Lyapunov: blue(stable) → red(chaotic), dynamic range
            Color col;
            if (particleLyap != null)
            {
                float lambdaRange = Mathf.Max(0.01f, lyapMax - lyapMin);
                float t = Mathf.Clamp01((particleLyap[i] - lyapMin) / lambdaRange);
                float h = Mathf.Lerp(0.66f, 0f, t);
                col = Color.HSVToRGB(h, 1f, 1f);
            }
            else
            {
                float hue = (float)i / particleCount;
                col = Color.HSVToRGB(hue, 1f, 1f);
            }
            particleSpheres[i].GetComponent<MeshRenderer>().material.color = col;

            // Particle trail
            var tlr = particleTrails[i];
            tlr.enabled = trailLength > 0;
            if (trailLength > 0)
            {
                var pts = pTrailPts[i];
                int h = pHeads[i];
                pts[h] = worldPos;
                pHeads[i] = (h + 1) % trailLength;
                if (pCounts[i] < trailLength) pCounts[i]++;

                int pc = pCounts[i];
                int st = pc < trailLength ? 0 : pHeads[i];
                tlr.positionCount = pc;
                var pos = new Vector3[pc];
                for (int j = 0; j < pc; j++)
                    pos[j] = pts[(st + j) % trailLength];
                tlr.SetPositions(pos);
            }
        }

        // Compute mean/max/min λ
        if (particleLyap != null)
        {
            float sum = 0f, mx = float.MinValue, mn = float.MaxValue;
            for (int i = 0; i < particleCount; i++)
            {
                sum += particleLyap[i];
                if (particleLyap[i] > mx) mx = particleLyap[i];
                if (particleLyap[i] < mn) mn = particleLyap[i];
            }
            lyapMean = sum / particleCount;
            lyapMax = mx;
            lyapMin = mn;
        }
    }

    void UpdateLineRenderer()
    {
        lr.enabled = trailLength > 0;
        if (!lr.enabled) return;

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
        bool yPressed = false;

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
            if (kb.mKey.wasPressedThisFrame) ToggleMultiMode();
        }
#else
        var leftCtrl = XRController.leftHand;
        if (leftCtrl != null)
        {
            var stick = leftCtrl.TryGetChildControl<StickControl>("thumbstick");
            if (stick != null) { lxIn = stick.x.ReadValue(); lyIn = stick.y.ReadValue(); }

            var grip = leftCtrl.TryGetChildControl<AxisControl>("grip");
            if (grip != null && grip.ReadValue() > 0.5f) gripPressed = true;

            var yBtn = leftCtrl.TryGetChildControl<ButtonControl>("secondaryButton");
            if (yBtn != null) yPressed = yBtn.isPressed;
        }
        var rightCtrl = XRController.rightHand;
        if (rightCtrl != null)
        {
            var stick = rightCtrl.TryGetChildControl<StickControl>("thumbstick");
            if (stick != null) { rxIn = stick.x.ReadValue(); ryIn = stick.y.ReadValue(); }
        }

        // Y button long press
        if (yPressed)
        {
            yHoldTime += Time.deltaTime;
            if (yHoldTime >= multiLongPress && !yLongFired)
            {
                ToggleMultiMode();
                yLongFired = true;
            }
        }
        else
        {
            yHoldTime = 0f;
            yLongFired = false;
        }
#endif

        if (gripPressed) PlaceInFrontOfCamera();

        float pSigma = sigma, pRho = rho, pBeta = beta;
        sigma = Mathf.Clamp(sigma + lxIn * (sigmaMax - sigmaMin) * paramSpeed, sigmaMin, sigmaMax);
        rho = Mathf.Clamp(rho + lyIn * (rhoMax - rhoMin) * paramSpeed, rhoMin, rhoMax);
        beta = Mathf.Clamp(beta + rxIn * (betaMax - betaMin) * paramSpeed, betaMin, betaMax);
        dt = Mathf.Clamp(dt + ryIn * 0.01f * paramSpeed, dtMin, dtMax);
        if (sigma != pSigma || rho != pRho || beta != pBeta) ResetLyapunov();
    }

    void ToggleMultiMode() { SetMultiMode(!multiMode); }

    public void SetMultiMode(bool on)
    {
        if (on == multiMode) return;
        multiMode = on;
        if (on) InitMultiParticles();
        else DestroyMultiParticles();
    }

    public void SetParticleCount(int count)
    {
        particleCount = count;
        if (multiMode) { DestroyMultiParticles(); InitMultiParticles(); }
    }

    public void SetTrailLength(int len)
    {
        trailLength = len;
        if (lr != null) lr.enabled = len > 0;
        if (multiMode) { DestroyMultiParticles(); InitMultiParticles(); }
    }

    void InitMultiParticles()
    {
        DestroyMultiParticles();

        multiRoot = new GameObject("MultiParticles");
        multiRoot.transform.SetParent(transform, false);

        // Get sphere mesh from a temp primitive
        var tmp = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        sharedSphereMesh = tmp.GetComponent<MeshFilter>().sharedMesh;
        Destroy(tmp);

        sharedSphereMat = new Material(Shader.Find("Sprites/Default"));
        var trailMat = new Material(Shader.Find("Sprites/Default"));

        px = new float[particleCount];
        py = new float[particleCount];
        pz = new float[particleCount];
        particleSpheres = new GameObject[particleCount];
        particleTrails = new LineRenderer[particleCount];
        pTrailPts = new Vector3[particleCount][];
        pHeads = new int[particleCount];
        pCounts = new int[particleCount];

        int sampleCount = (particleCount + lyapSampleInterval - 1) / lyapSampleInterval;
        pRefX = new float[sampleCount];
        pRefY = new float[sampleCount];
        pRefZ = new float[sampleCount];
        pLyapSum = new float[sampleCount];
        pLyapFrames = new int[sampleCount];
        particleLyap = new float[particleCount];

        for (int i = 0; i < particleCount; i++)
        {
            px[i] = 0.1f + Random.Range(-scatterRange, scatterRange);
            py[i] = Random.Range(-scatterRange, scatterRange);
            pz[i] = Random.Range(-scatterRange, scatterRange);

            if (i % lyapSampleInterval == 0)
            {
                int si = i / lyapSampleInterval;
                pRefX[si] = px[i] + lyapD0; pRefY[si] = py[i]; pRefZ[si] = pz[i];
            }

            float hue = (float)i / particleCount;
            Color col = Color.HSVToRGB(hue, 1f, 1f);

            // Sphere
            var sGo = new GameObject($"p{i}");
            sGo.transform.SetParent(multiRoot.transform, false);
            sGo.AddComponent<MeshFilter>().sharedMesh = sharedSphereMesh;
            var mr = sGo.AddComponent<MeshRenderer>();
            mr.material = new Material(sharedSphereMat);
            mr.material.color = col;
            sGo.transform.localScale = Vector3.one * particleRadius * 2f;
            particleSpheres[i] = sGo;

            // Trail
            var tGo = new GameObject($"t{i}");
            tGo.transform.SetParent(multiRoot.transform, false);
            var tlr = tGo.AddComponent<LineRenderer>();
            tlr.useWorldSpace = true;
            tlr.startWidth = 0.001f;
            tlr.endWidth = 0.001f;
            tlr.material = trailMat;
            Color tc = col; tc.a = 0.3f;
            tlr.startColor = tc;
            tlr.endColor = tc;
            tlr.positionCount = 0;
            particleTrails[i] = tlr;

            pTrailPts[i] = new Vector3[trailLength];
            pHeads[i] = 0;
            pCounts[i] = 0;
        }
    }

    void DestroyMultiParticles()
    {
        if (multiRoot != null)
        {
            Destroy(multiRoot);
            multiRoot = null;
        }
        px = null; py = null; pz = null;
        particleSpheres = null;
        particleTrails = null;
        pTrailPts = null;
        pHeads = null;
        pCounts = null;
        pRefX = null; pRefY = null; pRefZ = null;
        pLyapSum = null; pLyapFrames = null; particleLyap = null;
    }

    public string GetParamLabel()
    {
        string label = $"Lorenz \u03c3:{sigma:F1} \u03c1:{rho:F1} \u03b2:{beta:F2}";
        if (multiMode) label += " [Multi]";
        return label;
    }
}
