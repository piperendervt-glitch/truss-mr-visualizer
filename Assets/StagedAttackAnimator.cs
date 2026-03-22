using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.XR;
using UnityEngine.Networking;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using TMPro;

public class StagedAttackAnimator : MonoBehaviour
{
    // Graph structure: Fano(7) x Q3(8) = 56 vertices, 252 edges
    // 3 scenarios displayed side by side
    const int SCENARIO_COUNT = 3;
    const int NODE_COUNT = 56;
    float q3Radius = 0.2f;
    float fanoRadius = 0.05f;
    float graphScale = 0.6f; // 0.6x normal size

    // Scenario positions (local to this object)
    static readonly Vector3[] scenarioOffsets = {
        new Vector3(-0.3f, 0f, 0f),
        new Vector3( 0.0f, 0f, 0f),
        new Vector3(+0.3f, 0f, 0f),
    };

    // Animation data
    StagedAnimData animData;
    int currentFrame;
    float frameTimer;
    float baseInterval = 0.05f; // 20fps
    int speedIndex = 4;
    static readonly float[] speedMultipliers = { 0.06f, 0.12f, 0.25f, 0.5f, 1f, 2f, 4f };
    public static readonly string[] speedNames = { "0.06x", "0.12x", "0.25x", "0.5x", "1x", "2x", "4x" };
    bool playing = true;
    bool dataLoaded;

    // Per-scenario state
    Vector3[] baseVerts = new Vector3[NODE_COUNT];
    ScenarioView[] views = new ScenarioView[SCENARIO_COUNT];

    // Current frame state (public for DebugDisplay)
    public string currentPhase = "";
    public int currentStep;
    public int totalFrames;
    public float[] phiRates = new float[SCENARIO_COUNT];
    public int[] trials = new int[SCENARIO_COUNT];
    public int[] successCounts = new int[SCENARIO_COUNT];

    float rotSpeed = 60f;
    bool placedOnStart;

    // Trial pause state
    bool trialPausing;
    float trialPauseTimer;
    int lastPausedTrial = -1;
    TextMeshPro pauseLabel;

    // Q3 vertex positions (unit cube corners)
    static readonly Vector3[] q3Corners = {
        new Vector3(-1,-1,-1), new Vector3(-1,-1,+1),
        new Vector3(-1,+1,-1), new Vector3(-1,+1,+1),
        new Vector3(+1,-1,-1), new Vector3(+1,-1,+1),
        new Vector3(+1,+1,-1), new Vector3(+1,+1,+1),
    };

    static readonly Color orangeAttack = new Color(1f, 0.5f, 0f);

    // --- Data classes ---
    class StagedAnimData
    {
        public GraphData graph;
        public ScenarioData[] scenarios;
    }

    class GraphData
    {
        public string[] nodes;
        public string[][] edges;
    }

    class ScenarioData
    {
        public float ratio;
        public string label;
        public int attacked_edge_count;
        public FrameData[] frames;
    }

    class FrameData
    {
        public string phase;
        public int step;
        public Dictionary<string, float> weights;
        public float phi_rate;
        public float energy;
        public string[] attacked_edges;
        public int trial;
        public int success_count;
    }

    // Per-scenario visual state
    class ScenarioView
    {
        public GameObject root;
        public LineRenderer[] edgeLines;
        public float[] edgeAttackLerp;
        public HashSet<string> activeAttackEdges = new HashSet<string>();
        public string prevPhase = "";

        // Phase ring
        public LineRenderer phaseRing;

        // Labels
        public TextMeshPro titleLabel;  // "10%", "20%", "50%"
        public TextMeshPro statsLabel;  // phi + trial info

        // Result highlight
        public LineRenderer failRing;       // large red ring for failure
        public TextMeshPro resultLabel;     // "FAILED: phi=94.6%" or "OK: phi=100%"
        public float resultTimer;           // countdown for result display

        // Background panel
        public MeshRenderer bgPanel;
    }

    void Start()
    {
        foreach (Transform child in transform)
            Destroy(child.gameObject);

        BuildBaseVertexPositions();
        StartCoroutine(LoadAnimationDataAsync());
    }

    void BuildBaseVertexPositions()
    {
        for (int q = 0; q < 8; q++)
        {
            Vector3 clusterCenter = q3Corners[q].normalized * q3Radius;
            Vector3 up = clusterCenter.normalized;
            Vector3 right = Vector3.Cross(up, Vector3.up).normalized;
            if (right.sqrMagnitude < 0.01f)
                right = Vector3.Cross(up, Vector3.right).normalized;
            Vector3 forward = Vector3.Cross(right, up).normalized;

            for (int f = 0; f < 7; f++)
            {
                Vector3 offset;
                if (f == 0)
                    offset = Vector3.zero;
                else
                {
                    float angle = (f - 1) * Mathf.PI * 2f / 6f;
                    offset = (Mathf.Cos(angle) * right + Mathf.Sin(angle) * forward) * fanoRadius;
                }
                baseVerts[q * 7 + f] = clusterCenter + offset;
            }
        }
    }

    IEnumerator LoadAnimationDataAsync()
    {
        string path = Path.Combine(Application.streamingAssetsPath, "staged_attack_animation.json");
        string json = null;

#if UNITY_ANDROID && !UNITY_EDITOR
        Debug.Log("Staged: Loading JSON via UnityWebRequest: " + path);
        using (var req = UnityWebRequest.Get(path))
        {
            yield return req.SendWebRequest();
            if (req.result == UnityWebRequest.Result.Success)
            {
                json = req.downloadHandler.text;
                Debug.Log("Staged: UnityWebRequest success, length=" + json.Length);
            }
            else
            {
                Debug.LogError("Staged: Failed to load JSON: " + req.error);
                yield break;
            }
        }
#else
        Debug.Log("Staged: Loading JSON via File.ReadAllText: " + path);
        try
        {
            json = File.ReadAllText(path);
            Debug.Log("Staged: File.ReadAllText success, length=" + json.Length);
        }
        catch (System.Exception e)
        {
            Debug.LogError("Staged: Failed to read JSON: " + e.Message);
            yield break;
        }
#endif

        yield return null;

        animData = ParseStagedAnimData(json);
        json = null;

        if (animData == null || animData.graph == null || animData.scenarios == null)
        {
            Debug.LogError("Staged: ParseStagedAnimData returned null");
            yield break;
        }

        // Build edge index mapping
        var nodeIndex = new Dictionary<string, int>();
        for (int i = 0; i < animData.graph.nodes.Length; i++)
            nodeIndex[animData.graph.nodes[i]] = i;

        int edgeCount = animData.graph.edges.Length;
        var edgeIndicesArr = new int[edgeCount][];
        var edgeKeysArr = new string[edgeCount];
        for (int i = 0; i < edgeCount; i++)
        {
            string n0 = animData.graph.edges[i][0];
            string n1 = animData.graph.edges[i][1];
            edgeIndicesArr[i] = new[] { nodeIndex[n0], nodeIndex[n1] };
            edgeKeysArr[i] = $"({n0},{n1})";
        }

        SetupScenarioViews(edgeIndicesArr, edgeKeysArr);
        totalFrames = animData.scenarios[0].frames.Length;
        dataLoaded = true;

        Debug.Log($"Staged: JSON loaded, scenarios={animData.scenarios.Length}, frames={totalFrames}, edges={edgeCount}");
    }

    // Store edge data for position updates
    int[][] edgeIndices;
    string[] edgeKeys;

    // Background panel colors per scenario
    static readonly Color[] bgColors = {
        new Color(0f, 1f, 0f, 0.1f),   // 10%: light green
        new Color(1f, 1f, 0f, 0.1f),   // 20%: light yellow
        new Color(1f, 0f, 0f, 0.1f),   // 50%: light red
    };

    void SetupScenarioViews(int[][] indices, string[] keys)
    {
        edgeIndices = indices;
        edgeKeys = keys;

        var mat = new Material(Shader.Find("Sprites/Default"));
        var unlitMat = new Material(Shader.Find("Unlit/Transparent"));
        string[] labels = { "10%", "20%", "50%" };

        for (int s = 0; s < SCENARIO_COUNT; s++)
        {
            var view = new ScenarioView();
            views[s] = view;

            // Root object for this scenario
            view.root = new GameObject($"Scenario_{s}");
            view.root.transform.SetParent(transform, false);
            view.root.transform.localPosition = scenarioOffsets[s];

            // Background panel (Quad behind each graph)
            var bgGo = GameObject.CreatePrimitive(PrimitiveType.Quad);
            bgGo.name = "BgPanel";
            bgGo.transform.SetParent(view.root.transform, false);
            bgGo.transform.localPosition = new Vector3(0f, 0f, 0.02f);
            bgGo.transform.localScale = new Vector3(0.28f, 0.28f, 1f);
            Object.Destroy(bgGo.GetComponent<Collider>());
            var bgMat = new Material(Shader.Find("Sprites/Default"));
            bgMat.color = bgColors[s];
            view.bgPanel = bgGo.GetComponent<MeshRenderer>();
            view.bgPanel.material = bgMat;

            // Edges
            view.edgeLines = new LineRenderer[indices.Length];
            view.edgeAttackLerp = new float[indices.Length];

            for (int i = 0; i < indices.Length; i++)
            {
                var go = new GameObject("edge");
                go.transform.SetParent(view.root.transform, false);
                var lr = go.AddComponent<LineRenderer>();
                lr.positionCount = 2;
                lr.startWidth = 0.0015f;
                lr.endWidth = 0.0015f;
                lr.useWorldSpace = true;
                lr.material = mat;
                lr.startColor = Color.white;
                lr.endColor = Color.white;
                view.edgeLines[i] = lr;
            }

            // Phase ring (circle around each graph)
            view.phaseRing = CreateRing(view.root.transform, 0.15f, mat);
            view.phaseRing.gameObject.SetActive(false);

            // Fail ring (2x size, for failure highlight)
            view.failRing = CreateRing(view.root.transform, 0.30f, mat);
            view.failRing.startWidth = 0.006f;
            view.failRing.endWidth = 0.006f;
            view.failRing.gameObject.SetActive(false);

            // Title label above graph
            view.titleLabel = CreateWorldLabel(view.root.transform,
                labels[s], new Vector3(0f, 0.12f, 0f), 0.07f, Color.yellow);

            // Stats label below graph
            view.statsLabel = CreateWorldLabel(view.root.transform,
                "", new Vector3(0f, -0.18f, 0f), 0.025f, Color.white);

            // Result label (center of graph, hidden by default)
            view.resultLabel = CreateWorldLabel(view.root.transform,
                "", Vector3.zero, 0.08f, Color.red);
            view.resultLabel.gameObject.SetActive(false);
        }

        // Pause label (global, child of this object)
        pauseLabel = CreateWorldLabel(transform, "", new Vector3(0f, -0.25f, 0f), 0.04f, Color.cyan);
        pauseLabel.gameObject.SetActive(false);
    }

    TextMeshPro CreateWorldLabel(Transform parent, string text, Vector3 localPos, float fontSize, Color color)
    {
        var go = new GameObject("Label");
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        var tmp = go.AddComponent<TextMeshPro>();
        tmp.text = text;
        tmp.fontSize = fontSize;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = color;
        tmp.rectTransform.sizeDelta = new Vector2(0.3f, 0.1f);
        return tmp;
    }

    LineRenderer CreateRing(Transform parent, float radius, Material mat)
    {
        var go = new GameObject("PhaseRing");
        go.transform.SetParent(parent, false);
        var lr = go.AddComponent<LineRenderer>();
        int segments = 48;
        lr.positionCount = segments + 1;
        lr.startWidth = 0.003f;
        lr.endWidth = 0.003f;
        lr.useWorldSpace = false;
        lr.material = mat;
        lr.loop = false;

        for (int i = 0; i <= segments; i++)
        {
            float angle = i * Mathf.PI * 2f / segments;
            lr.SetPosition(i, new Vector3(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius, 0f));
        }

        return lr;
    }

    // --- Public API (called by ShapeManager) ---
    public void TogglePlayPause()
    {
        if (!playing)
        {
            if (animData != null && animData.scenarios != null
                && currentFrame >= animData.scenarios[0].frames.Length - 1)
            {
                ResetPlayback();
                playing = true;
                Debug.Log("Staged: restart from frame 0");
            }
            else
            {
                playing = true;
                Debug.Log("Staged: resumed at frame " + currentFrame);
            }
        }
        else
        {
            playing = false;
            Debug.Log("Staged: paused at frame " + currentFrame);
        }
    }

    public void ResetPlayback()
    {
        currentFrame = 0;
        frameTimer = 0f;
        trialPausing = false;
        trialPauseTimer = 0f;
        lastPausedTrial = -1;
        if (pauseLabel != null) pauseLabel.gameObject.SetActive(false);
        if (views != null)
        {
            for (int s = 0; s < SCENARIO_COUNT; s++)
            {
                if (views[s] == null) continue;
                views[s].activeAttackEdges.Clear();
                views[s].prevPhase = "";
                views[s].resultTimer = 0f;
                if (views[s].edgeAttackLerp != null)
                    for (int i = 0; i < views[s].edgeAttackLerp.Length; i++)
                        views[s].edgeAttackLerp[i] = 0f;
                if (views[s].resultLabel != null)
                    views[s].resultLabel.gameObject.SetActive(false);
                if (views[s].failRing != null)
                    views[s].failRing.gameObject.SetActive(false);
            }
        }
        Debug.Log("Staged: reset to frame 0");
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
            var cam = Camera.main;
            if (cam != null)
            {
                PlaceInFrontOfCamera();
                placedOnStart = true;
            }
        }

        if (!dataLoaded) return;

        HandleInput();

        // Trial pause countdown
        if (trialPausing)
        {
            trialPauseTimer -= Time.deltaTime;
            if (pauseLabel != null)
            {
                pauseLabel.gameObject.SetActive(true);
                pauseLabel.text = $"Trial {lastPausedTrial}/5 complete - resuming in {Mathf.CeilToInt(trialPauseTimer)}s...";
            }
            if (trialPauseTimer <= 0f)
            {
                trialPausing = false;
                playing = true;
                if (pauseLabel != null) pauseLabel.gameObject.SetActive(false);
            }
        }
        else
        {
            UpdateAnimation();
        }

        for (int s = 0; s < SCENARIO_COUNT; s++)
            UpdateScenarioVisuals(s);

        UpdateEdgePositions();
        UpdateLabels();
        UpdateResultDisplays();
    }

    void HandleInput()
    {
        var grabber = GetComponent<ShapeGrabber>();
        bool grabbed = grabber != null && grabber.isGrabbed;
        if (grabbed || MenuUI.isMenuOpen) return;

        bool resetPressed = false;
        float ry = 0f;
        float rxRot = 0f;

#if UNITY_EDITOR
        var kb = Keyboard.current;
        if (kb != null)
        {
            if (kb.gKey.wasPressedThisFrame) resetPressed = true;
            if (kb.iKey.wasPressedThisFrame) ChangeSpeed(1);
            if (kb.kKey.wasPressedThisFrame) ChangeSpeed(-1);
            if (kb.jKey.isPressed) rxRot = -1f;
            if (kb.lKey.isPressed) rxRot = 1f;
        }
#else
        var rightCtrl = XRController.rightHand;
        if (rightCtrl != null)
        {
            var stick = rightCtrl.TryGetChildControl<StickControl>("thumbstick");
            if (stick != null)
            {
                ry = stick.y.ReadValue();
                rxRot = stick.x.ReadValue();
            }
        }

        var leftCtrl = XRController.leftHand;
        if (leftCtrl != null)
        {
            var grip = leftCtrl.TryGetChildControl<AxisControl>("grip");
            if (grip != null && grip.ReadValue() > 0.5f)
            {
                PlaceInFrontOfCamera();
            }
        }

        if (Mathf.Abs(ry) > 0.7f)
        {
            if (!speedChangeHeld)
            {
                ChangeSpeed(ry > 0 ? 1 : -1);
                speedChangeHeld = true;
            }
        }
        else
        {
            speedChangeHeld = false;
        }
#endif

        if (resetPressed) ResetPlayback();
        transform.Rotate(Vector3.up, rxRot * rotSpeed * Time.deltaTime, Space.World);
    }

    bool speedChangeHeld;

    void ChangeSpeed(int dir)
    {
        speedIndex = Mathf.Clamp(speedIndex + dir, 0, speedMultipliers.Length - 1);
    }

    void UpdateAnimation()
    {
        if (!playing) return;

        frameTimer += Time.deltaTime;
        float interval = baseInterval / speedMultipliers[speedIndex];

        while (frameTimer >= interval)
        {
            frameTimer -= interval;

            // Check if current frame is result and next frame is different trial (transition)
            var curFrame = animData.scenarios[0].frames[currentFrame];
            int nextIdx = currentFrame + 1;

            if (nextIdx >= animData.scenarios[0].frames.Length)
            {
                currentFrame = animData.scenarios[0].frames.Length - 1;
                playing = false;
                frameTimer = 0f;
                Debug.Log("Staged: reached end, auto-stopped");
                return;
            }

            // Detect trial boundary: result phase → next frame has different trial
            var nextFrame = animData.scenarios[0].frames[nextIdx];
            if (curFrame.phase == "result" && nextFrame.trial != curFrame.trial
                && curFrame.trial != lastPausedTrial)
            {
                // Trigger result highlight for all scenarios
                for (int s = 0; s < SCENARIO_COUNT; s++)
                    TriggerResultHighlight(s);

                // Auto-pause for 2 seconds
                playing = false;
                trialPausing = true;
                trialPauseTimer = 2f;
                lastPausedTrial = curFrame.trial;
                frameTimer = 0f;
                Debug.Log($"Staged: trial {curFrame.trial} complete, pausing 2s");
                return;
            }

            currentFrame = nextIdx;
        }
    }

    // Weight color mapping for s_l = ±1 version
    Color WeightToColor(float w)
    {
        // weight=+1.0 → green, weight=0.0 → white, weight=-1.0 → blue
        if (w >= 0f)
        {
            return Color.Lerp(Color.white, Color.green, w);
        }
        else
        {
            return Color.Lerp(Color.blue, Color.white, w + 1f);
        }
    }

    void UpdateScenarioVisuals(int s)
    {
        var view = views[s];
        if (view == null || view.edgeLines == null) return;

        var frame = animData.scenarios[s].frames[currentFrame];
        string phase = frame.phase;

        // Update public state
        phiRates[s] = frame.phi_rate;
        trials[s] = frame.trial;
        successCounts[s] = frame.success_count;
        if (s == 0)
        {
            currentPhase = phase;
            currentStep = frame.step;
        }

        // Attack edge tracking
        var attackedSet = new HashSet<string>();
        if (frame.attacked_edges != null)
            foreach (var ae in frame.attacked_edges)
                attackedSet.Add(ae);

        if (phase == "attacking")
        {
            for (int i = 0; i < edgeKeys.Length; i++)
            {
                if (attackedSet.Contains(edgeKeys[i]))
                {
                    view.activeAttackEdges.Add(edgeKeys[i]);
                    view.edgeAttackLerp[i] = 1f;
                }
            }
        }

        if (phase == "recovering")
        {
            float fadeSpeed = 2f * Time.deltaTime;
            for (int i = 0; i < view.edgeAttackLerp.Length; i++)
                if (view.edgeAttackLerp[i] > 0f)
                    view.edgeAttackLerp[i] = Mathf.Max(0f, view.edgeAttackLerp[i] - fadeSpeed);
        }

        if (phase == "converging" && view.prevPhase == "recovering")
        {
            view.activeAttackEdges.Clear();
            for (int i = 0; i < view.edgeAttackLerp.Length; i++)
                view.edgeAttackLerp[i] = 0f;
        }

        view.prevPhase = phase;

        // Apply edge colors
        for (int i = 0; i < view.edgeLines.Length; i++)
        {
            float w = 0f;
            if (frame.weights != null)
                frame.weights.TryGetValue(edgeKeys[i], out w);

            Color weightColor = WeightToColor(w);
            float attackT = view.edgeAttackLerp[i];

            Color c;
            float width;
            if (attackT > 0.01f)
            {
                c = Color.Lerp(weightColor, orangeAttack, attackT);
                width = Mathf.Lerp(0.0015f, 0.005f, attackT);
            }
            else
            {
                c = weightColor;
                width = 0.0015f;
            }

            view.edgeLines[i].startColor = c;
            view.edgeLines[i].endColor = c;
            view.edgeLines[i].startWidth = width;
            view.edgeLines[i].endWidth = width;
        }

        // Phase ring
        UpdatePhaseRing(view, phase, s);
    }

    void TriggerResultHighlight(int s)
    {
        var view = views[s];
        if (view == null) return;
        float phi = phiRates[s];
        bool failed = phi < 95f;

        if (failed)
        {
            // Show large red ring + FAILED text for 3 seconds
            view.resultLabel.text = $"FAILED: \u03c6={phi:F1}%";
            view.resultLabel.color = Color.red;
            view.resultLabel.gameObject.SetActive(true);
            view.resultTimer = 3f;

            if (view.failRing != null)
            {
                view.failRing.startColor = Color.red;
                view.failRing.endColor = Color.red;
                view.failRing.gameObject.SetActive(true);
            }
        }
        else
        {
            // Show OK text in green for 1 second
            view.resultLabel.text = $"OK: \u03c6={phi:F1}%";
            view.resultLabel.color = Color.green;
            view.resultLabel.gameObject.SetActive(true);
            view.resultTimer = 1f;

            if (view.failRing != null)
                view.failRing.gameObject.SetActive(false);
        }
    }

    void UpdateResultDisplays()
    {
        var cam = Camera.main;
        for (int s = 0; s < SCENARIO_COUNT; s++)
        {
            var view = views[s];
            if (view == null) continue;

            if (view.resultTimer > 0f)
            {
                view.resultTimer -= Time.deltaTime;
                if (view.resultTimer <= 0f)
                {
                    view.resultLabel.gameObject.SetActive(false);
                    if (view.failRing != null)
                        view.failRing.gameObject.SetActive(false);
                }
            }

            // Billboard result label and fail ring
            if (cam != null)
            {
                if (view.resultLabel != null && view.resultLabel.gameObject.activeSelf)
                {
                    view.resultLabel.transform.LookAt(cam.transform);
                    view.resultLabel.transform.Rotate(0f, 180f, 0f);
                }
                if (view.failRing != null && view.failRing.gameObject.activeSelf)
                {
                    view.failRing.transform.LookAt(cam.transform);
                    view.failRing.transform.Rotate(0f, 180f, 0f);
                }
            }
        }

        // Billboard pause label
        if (cam != null && pauseLabel != null && pauseLabel.gameObject.activeSelf)
        {
            pauseLabel.transform.LookAt(cam.transform);
            pauseLabel.transform.Rotate(0f, 180f, 0f);
        }
    }

    void UpdatePhaseRing(ScenarioView view, string phase, int scenarioIndex)
    {
        if (view.phaseRing == null) return;

        Color ringColor;
        bool showRing = true;

        switch (phase)
        {
            case "attacking":
                ringColor = Color.red;
                break;
            case "recovering":
                ringColor = Color.green;
                break;
            case "result":
                // 50% attack failure: right graph ring stays orange
                if (scenarioIndex == 2 && phiRates[2] < 95f)
                {
                    ringColor = orangeAttack;
                }
                else
                {
                    showRing = false;
                    ringColor = Color.clear;
                }
                break;
            default:
                showRing = false;
                ringColor = Color.clear;
                break;
        }

        view.phaseRing.gameObject.SetActive(showRing);
        if (showRing)
        {
            view.phaseRing.startColor = ringColor;
            view.phaseRing.endColor = ringColor;
        }
    }

    void UpdateEdgePositions()
    {
        if (edgeIndices == null) return;

        Vector3 pos = transform.position;
        Quaternion rot = transform.rotation;

        for (int s = 0; s < SCENARIO_COUNT; s++)
        {
            var view = views[s];
            if (view == null || view.edgeLines == null) continue;

            Vector3 offset = rot * scenarioOffsets[s];

            for (int i = 0; i < edgeIndices.Length; i++)
            {
                Vector3 v0 = baseVerts[edgeIndices[i][0]] * graphScale;
                Vector3 v1 = baseVerts[edgeIndices[i][1]] * graphScale;
                view.edgeLines[i].SetPosition(0, pos + offset + rot * v0);
                view.edgeLines[i].SetPosition(1, pos + offset + rot * v1);
            }
        }
    }

    void UpdateLabels()
    {
        var cam = Camera.main;
        if (cam == null) return;

        for (int s = 0; s < SCENARIO_COUNT; s++)
        {
            var view = views[s];
            if (view == null) continue;

            // Billboard labels toward camera
            if (view.titleLabel != null)
            {
                view.titleLabel.transform.LookAt(cam.transform);
                view.titleLabel.transform.Rotate(0f, 180f, 0f);
            }

            if (view.statsLabel != null)
            {
                var frame = animData.scenarios[s].frames[currentFrame];
                int totalTrials = 5;
                view.statsLabel.text = $"\u03c6: {frame.phi_rate:F1}%\nTrial: {frame.trial}/{totalTrials}  OK: {frame.success_count}";
                view.statsLabel.transform.LookAt(cam.transform);
                view.statsLabel.transform.Rotate(0f, 180f, 0f);
            }

            // Billboard ring
            if (view.phaseRing != null && view.phaseRing.gameObject.activeSelf)
            {
                view.phaseRing.transform.LookAt(cam.transform);
                view.phaseRing.transform.Rotate(0f, 180f, 0f);
            }
        }
    }

    public string GetParamLabel()
    {
        string playState = playing ? "Play" : "Pause";
        string spd = speedNames[speedIndex];
        int total = totalFrames;
        return $"Staged [{playState}] [{spd}] F:{currentFrame}/{total}";
    }

    public string GetDebugInfo()
    {
        if (!dataLoaded)
            return "Loading JSON...";

        string phaseColor;
        switch (currentPhase)
        {
            case "attacking": phaseColor = "red"; break;
            case "recovering": phaseColor = "green"; break;
            case "result": phaseColor = "yellow"; break;
            default: phaseColor = "cyan"; break;
        }

        return $"Staged: Phase:<color={phaseColor}>{currentPhase}</color> Step:{currentStep}/{totalFrames}\n" +
               $"10%: \u03c6={phiRates[0]:F1}% T:{trials[0]} OK:{successCounts[0]}\n" +
               $"20%: \u03c6={phiRates[1]:F1}% T:{trials[1]} OK:{successCounts[1]}\n" +
               $"50%: \u03c6={phiRates[2]:F1}% T:{trials[2]} OK:{successCounts[2]}";
    }

    // ============================================================
    // JSON Parser (same approach as FanoQ3Animator)
    // ============================================================

    StagedAnimData ParseStagedAnimData(string json)
    {
        var data = new StagedAnimData();
        data.graph = new GraphData();

        // Parse graph.nodes
        int nodesStart = json.IndexOf("\"nodes\"");
        int arrStart = json.IndexOf('[', nodesStart);
        int arrEnd = FindMatchingBracket(json, arrStart);
        data.graph.nodes = ParseStringArray(json.Substring(arrStart, arrEnd - arrStart + 1));

        // Parse graph.edges
        int edgesStart = json.IndexOf("\"edges\"");
        arrStart = json.IndexOf('[', edgesStart);
        arrEnd = FindMatchingBracket(json, arrStart);
        data.graph.edges = ParseEdgesArray(json.Substring(arrStart, arrEnd - arrStart + 1));

        // Parse scenarios array
        int scenariosStart = json.IndexOf("\"scenarios\"");
        arrStart = json.IndexOf('[', scenariosStart);
        arrEnd = FindMatchingBracket(json, arrStart);
        string scenariosStr = json.Substring(arrStart + 1, arrEnd - arrStart - 1);
        data.scenarios = ParseScenarios(scenariosStr);

        return data;
    }

    ScenarioData[] ParseScenarios(string content)
    {
        var result = new List<ScenarioData>();
        int pos = 0;
        while (pos < content.Length)
        {
            int objStart = content.IndexOf('{', pos);
            if (objStart < 0) break;
            int objEnd = FindMatchingBracket(content, objStart);
            string obj = content.Substring(objStart, objEnd - objStart + 1);
            result.Add(ParseScenario(obj));
            pos = objEnd + 1;
        }
        return result.ToArray();
    }

    ScenarioData ParseScenario(string obj)
    {
        var scenario = new ScenarioData();
        scenario.ratio = ExtractFloat(obj, "ratio");
        scenario.label = ExtractString(obj, "label");
        scenario.attacked_edge_count = ExtractInt(obj, "attacked_edge_count");

        // Parse frames array within scenario
        int framesStart = obj.IndexOf("\"frames\"");
        if (framesStart >= 0)
        {
            int arrStart = obj.IndexOf('[', framesStart);
            int arrEnd = FindMatchingBracket(obj, arrStart);
            string framesStr = obj.Substring(arrStart + 1, arrEnd - arrStart - 1);
            scenario.frames = ParseFrames(framesStr);
        }

        return scenario;
    }

    FrameData[] ParseFrames(string framesContent)
    {
        var result = new List<FrameData>();
        int pos = 0;
        while (pos < framesContent.Length)
        {
            int objStart = framesContent.IndexOf('{', pos);
            if (objStart < 0) break;
            int objEnd = FindMatchingBracket(framesContent, objStart);
            string obj = framesContent.Substring(objStart, objEnd - objStart + 1);
            result.Add(ParseFrame(obj));
            pos = objEnd + 1;
        }
        return result.ToArray();
    }

    FrameData ParseFrame(string obj)
    {
        var frame = new FrameData();
        frame.phase = ExtractString(obj, "phase");
        frame.step = ExtractInt(obj, "step");
        frame.phi_rate = ExtractFloat(obj, "phi_rate");
        frame.energy = ExtractFloat(obj, "energy");
        frame.trial = ExtractInt(obj, "trial");
        frame.success_count = ExtractInt(obj, "success_count");

        int wStart = obj.IndexOf("\"weights\"");
        if (wStart >= 0)
        {
            int dictStart = obj.IndexOf('{', wStart);
            int dictEnd = FindMatchingBracket(obj, dictStart);
            frame.weights = ParseWeightsDict(obj.Substring(dictStart, dictEnd - dictStart + 1));
        }

        int aeStart = obj.IndexOf("\"attacked_edges\"");
        if (aeStart >= 0)
        {
            int arrS = obj.IndexOf('[', aeStart);
            int arrE = FindMatchingBracket(obj, arrS);
            frame.attacked_edges = ParseStringArray(obj.Substring(arrS, arrE - arrS + 1));
        }
        else
        {
            frame.attacked_edges = new string[0];
        }

        return frame;
    }

    int FindMatchingBracket(string s, int openPos)
    {
        char open = s[openPos];
        char close = open == '[' ? ']' : '}';
        int depth = 1;
        bool inStr = false;
        for (int i = openPos + 1; i < s.Length; i++)
        {
            char c = s[i];
            if (c == '"' && (i == 0 || s[i - 1] != '\\')) inStr = !inStr;
            if (inStr) continue;
            if (c == open) depth++;
            else if (c == close) { depth--; if (depth == 0) return i; }
        }
        return s.Length - 1;
    }

    string[] ParseStringArray(string arr)
    {
        var result = new List<string>();
        int i = 1;
        while (i < arr.Length - 1)
        {
            int q1 = arr.IndexOf('"', i);
            if (q1 < 0) break;
            int q2 = arr.IndexOf('"', q1 + 1);
            result.Add(arr.Substring(q1 + 1, q2 - q1 - 1));
            i = q2 + 1;
        }
        return result.ToArray();
    }

    string[][] ParseEdgesArray(string arr)
    {
        var result = new List<string[]>();
        int i = 1;
        while (i < arr.Length - 1)
        {
            int subStart = arr.IndexOf('[', i);
            if (subStart < 0) break;
            int subEnd = arr.IndexOf(']', subStart);
            string sub = arr.Substring(subStart, subEnd - subStart + 1);
            var pair = ParseStringArray(sub);
            if (pair.Length == 2) result.Add(pair);
            i = subEnd + 1;
        }
        return result.ToArray();
    }

    Dictionary<string, float> ParseWeightsDict(string dict)
    {
        var result = new Dictionary<string, float>();
        int i = 1;
        while (i < dict.Length - 1)
        {
            int q1 = dict.IndexOf('"', i);
            if (q1 < 0) break;
            int q2 = dict.IndexOf('"', q1 + 1);
            string key = dict.Substring(q1 + 1, q2 - q1 - 1);

            int colon = dict.IndexOf(':', q2);
            int valStart = colon + 1;
            while (valStart < dict.Length && dict[valStart] == ' ') valStart++;

            int valEnd = valStart;
            while (valEnd < dict.Length && dict[valEnd] != ',' && dict[valEnd] != '}')
                valEnd++;

            string valStr = dict.Substring(valStart, valEnd - valStart).Trim();
            if (float.TryParse(valStr, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out float val))
            {
                result[key] = val;
            }

            i = valEnd + 1;
        }
        return result;
    }

    string ExtractString(string obj, string key)
    {
        int idx = obj.IndexOf($"\"{key}\"");
        if (idx < 0) return "";
        int colon = obj.IndexOf(':', idx);
        int q1 = obj.IndexOf('"', colon);
        int q2 = obj.IndexOf('"', q1 + 1);
        return obj.Substring(q1 + 1, q2 - q1 - 1);
    }

    int ExtractInt(string obj, string key)
    {
        int idx = obj.IndexOf($"\"{key}\"");
        if (idx < 0) return 0;
        int colon = obj.IndexOf(':', idx);
        int start = colon + 1;
        while (start < obj.Length && (obj[start] == ' ' || obj[start] == '\n' || obj[start] == '\r')) start++;
        int end = start;
        while (end < obj.Length && (char.IsDigit(obj[end]) || obj[end] == '-')) end++;
        if (int.TryParse(obj.Substring(start, end - start), out int val)) return val;
        return 0;
    }

    float ExtractFloat(string obj, string key)
    {
        int idx = obj.IndexOf($"\"{key}\"");
        if (idx < 0) return 0f;
        int colon = obj.IndexOf(':', idx);
        int start = colon + 1;
        while (start < obj.Length && (obj[start] == ' ' || obj[start] == '\n' || obj[start] == '\r')) start++;
        int end = start;
        while (end < obj.Length && (char.IsDigit(obj[end]) || obj[end] == '.' || obj[end] == '-' || obj[end] == 'e' || obj[end] == 'E' || obj[end] == '+')) end++;
        if (float.TryParse(obj.Substring(start, end - start),
            System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out float val))
            return val;
        return 0f;
    }
}
