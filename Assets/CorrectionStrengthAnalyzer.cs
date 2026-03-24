using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.XR;
using UnityEngine.Networking;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using TMPro;

public class CorrectionStrengthAnalyzer : MonoBehaviour
{
    // Data arrays
    float[] csValues;
    float[] loopGains;
    float[] convergenceSteps;
    Dictionary<string, float[]> trajectories;
    float threshold = 0.3f;
    bool dataLoaded;

    // Key points
    float currentCs = 0.50f;
    float optimalCs = 0.95f;
    float criticalCs = 1.00f;

    // Key point data from JSON
    float currentGain, optimalGain;
    int currentSteps, optimalSteps;

    // Graph dimensions
    const float graphW = 0.35f;
    const float graphH = 0.12f;
    const float graphSpacingY = 0.18f;

    // Interactive cs position
    float interactiveCs = 0.50f;

    // Visualization objects
    LineRenderer gainCurveLR;
    LineRenderer stepsCurveLR;
    LineRenderer[][] trajLRs; // 6 trajectories
    LineRenderer thresholdLineLR;

    // Markers
    GameObject currentMarker;   // white
    GameObject optimalMarker;   // gold
    GameObject criticalMarker;  // red

    // Graph labels
    TextMeshPro gainLabel;
    TextMeshPro stepsLabel;
    TextMeshPro trajLabel;

    // Stats display
    TextMeshPro statsDisplay;

    // Placement
    bool placedOnStart;
    public string debugMessage = "";

    // Trajectory colors
    static readonly string[] trajKeys = { "0.50", "0.70", "0.85", "0.95", "1.00", "1.05" };
    static readonly Color[] trajColors = {
        Color.red,
        new Color(1f, 0.5f, 0f),   // orange
        Color.yellow,
        Color.green,
        new Color(0.6f, 0.2f, 1f), // purple
        Color.white
    };
    static readonly float[] trajWidths = { 0.002f, 0.002f, 0.002f, 0.004f, 0.002f, 0.002f };

    void Start()
    {
        foreach (Transform child in transform)
            Destroy(child.gameObject);

        StartCoroutine(LoadDataAsync());
    }

    IEnumerator LoadDataAsync()
    {
        string path = Path.Combine(Application.streamingAssetsPath, "cs_analysis_unity.json");
        string json = null;

#if UNITY_ANDROID && !UNITY_EDITOR
        Debug.Log("CSAnalyzer: Loading JSON via UnityWebRequest: " + path);
        using (var req = UnityWebRequest.Get(path))
        {
            yield return req.SendWebRequest();
            if (req.result == UnityWebRequest.Result.Success)
            {
                json = req.downloadHandler.text;
                Debug.Log("CSAnalyzer: UnityWebRequest success, length=" + json.Length);
            }
            else
            {
                Debug.LogError("CSAnalyzer: Failed to load JSON: " + req.error);
                yield break;
            }
        }
#else
        Debug.Log("CSAnalyzer: Loading JSON via File.ReadAllText: " + path);
        try
        {
            json = File.ReadAllText(path);
            Debug.Log("CSAnalyzer: File.ReadAllText success, length=" + json.Length);
        }
        catch (System.Exception e)
        {
            Debug.LogError("CSAnalyzer: Failed to read JSON: " + e.Message);
            yield break;
        }
#endif

        yield return null;

        ParseJson(json);
        json = null;

        if (csValues == null || loopGains == null || convergenceSteps == null)
        {
            Debug.LogError("CSAnalyzer: Parse failed");
            yield break;
        }

        BuildGainGraph();
        BuildStepsGraph();
        BuildTrajectoryGraph();
        BuildMarkers();
        BuildStatsDisplay();
        dataLoaded = true;

        Debug.Log("CSAnalyzer: Ready, points=" + csValues.Length);
    }

    // --- JSON parsing ---
    void ParseJson(string json)
    {
        csValues = ParseFloatArray(json, "cs_values");
        loopGains = ParseFloatArray(json, "loop_gains");

        // Parse convergence_steps as float array then convert
        float[] stepsFloat = ParseFloatArray(json, "convergence_steps");
        if (stepsFloat != null)
        {
            convergenceSteps = stepsFloat;
        }

        threshold = ParseScalar(json, "threshold", 0.3f);

        // Parse key points
        currentGain = ParseNestedScalar(json, "current", "gain", 0.1483f);
        currentSteps = (int)ParseNestedScalar(json, "current", "steps", 6);
        optimalGain = ParseNestedScalar(json, "optimal", "gain", 0.0148f);
        optimalSteps = (int)ParseNestedScalar(json, "optimal", "steps", 2);

        // Parse trajectories
        trajectories = new Dictionary<string, float[]>();
        for (int i = 0; i < trajKeys.Length; i++)
        {
            float[] traj = ParseTrajectory(json, trajKeys[i]);
            if (traj != null)
                trajectories[trajKeys[i]] = traj;
        }
    }

    float ParseNestedScalar(string json, string section, string key, float defaultVal)
    {
        string search = "\"" + section + "\"";
        int secIdx = json.IndexOf(search);
        if (secIdx < 0) return defaultVal;
        int braceStart = json.IndexOf('{', secIdx);
        if (braceStart < 0) return defaultVal;
        // Find closing brace
        int braceEnd = json.IndexOf('}', braceStart);
        if (braceEnd < 0) return defaultVal;
        string sub = json.Substring(braceStart, braceEnd - braceStart + 1);
        string keySearch = "\"" + key + "\"";
        int keyIdx = sub.IndexOf(keySearch);
        if (keyIdx < 0) return defaultVal;
        return ParseValueAfterKey(sub, keyIdx);
    }

    float[] ParseTrajectory(string json, string csKey)
    {
        string search = "\"" + csKey + "\"";
        // Find within trajectories section
        int trajIdx = json.IndexOf("\"trajectories\"");
        if (trajIdx < 0) return null;
        int keyIdx = json.IndexOf(search, trajIdx);
        if (keyIdx < 0) return null;
        int arrStart = json.IndexOf('[', keyIdx);
        if (arrStart < 0) return null;
        int arrEnd = json.IndexOf(']', arrStart);
        if (arrEnd < 0) return null;
        string arrStr = json.Substring(arrStart + 1, arrEnd - arrStart - 1);
        string[] parts = arrStr.Split(',');
        float[] result = new float[parts.Length];
        for (int i = 0; i < parts.Length; i++)
        {
            float.TryParse(parts[i].Trim(),
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture,
                out result[i]);
        }
        return result;
    }

    float ParseValueAfterKey(string s, int keyIdx)
    {
        int colon = s.IndexOf(':', keyIdx);
        int start = colon + 1;
        while (start < s.Length && (s[start] == ' ' || s[start] == '\t')) start++;
        int end = start;
        while (end < s.Length && s[end] != ',' && s[end] != '}' && s[end] != ']' && s[end] != '\n') end++;
        string val = s.Substring(start, end - start).Trim();
        // Remove boolean/string values
        if (val == "true" || val == "false") return val == "true" ? 1f : 0f;
        float result;
        float.TryParse(val, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out result);
        return result;
    }

    float ParseScalar(string json, string key, float defaultVal)
    {
        string search = "\"" + key + "\"";
        int idx = json.IndexOf(search);
        if (idx < 0) return defaultVal;
        return ParseValueAfterKey(json, idx);
    }

    float[] ParseFloatArray(string json, string key)
    {
        string search = "\"" + key + "\"";
        int keyIdx = json.IndexOf(search);
        if (keyIdx < 0) return null;
        int arrStart = json.IndexOf('[', keyIdx);
        int arrEnd = json.IndexOf(']', arrStart);
        string arrStr = json.Substring(arrStart + 1, arrEnd - arrStart - 1);
        string[] parts = arrStr.Split(',');
        float[] result = new float[parts.Length];
        for (int i = 0; i < parts.Length; i++)
        {
            float.TryParse(parts[i].Trim(),
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture,
                out result[i]);
        }
        return result;
    }

    // --- Coordinate helpers ---
    // Graph 1 (gain) is at top, Graph 2 (steps) in middle, Graph 3 (trajectories) at bottom
    float GraphBaseY(int graphIndex)
    {
        // graphIndex 0=top (gain), 1=middle (steps), 2=bottom (trajectories)
        return (2 - graphIndex) * graphSpacingY;
    }

    Vector3 GainToLocal(int i)
    {
        float nx = (csValues[i] - 0.10f) / (1.00f - 0.10f);
        float maxGain = loopGains[0]; // first value is max
        float ny = loopGains[i] / maxGain;
        return new Vector3(nx * graphW - graphW * 0.5f, GraphBaseY(0) + ny * graphH, 0f);
    }

    Vector3 StepsToLocal(int i)
    {
        float nx = (csValues[i] - 0.10f) / (1.00f - 0.10f);
        float maxSteps = convergenceSteps[0]; // first value is max
        float ny = convergenceSteps[i] / maxSteps;
        return new Vector3(nx * graphW - graphW * 0.5f, GraphBaseY(1) + ny * graphH, 0f);
    }

    Vector3 TrajToLocal(int graphIdx, float[] traj, int step)
    {
        int maxLen = 0;
        foreach (var kv in trajectories)
            if (kv.Value.Length > maxLen) maxLen = kv.Value.Length;
        float nx = maxLen > 1 ? (float)step / (maxLen - 1) : 0f;
        float ny = Mathf.Clamp01(traj[step]);
        return new Vector3(nx * graphW - graphW * 0.5f, GraphBaseY(2) + ny * graphH, 0f);
    }

    float CsToLocalX(float cs)
    {
        float nx = (cs - 0.10f) / (1.00f - 0.10f);
        return nx * graphW - graphW * 0.5f;
    }

    // --- Build Graph 1: |G| vs cs ---
    void BuildGainGraph()
    {
        var mat = new Material(Shader.Find("Sprites/Default"));
        int count = csValues.Length;

        // LineRenderer for gain curve
        var go = new GameObject("GainCurve");
        go.transform.SetParent(transform, false);
        gainCurveLR = go.AddComponent<LineRenderer>();
        gainCurveLR.positionCount = count;
        gainCurveLR.startWidth = 0.003f;
        gainCurveLR.endWidth = 0.003f;
        gainCurveLR.useWorldSpace = false;
        gainCurveLR.material = mat;

        // Color gradient based on |G| value
        var gradient = new Gradient();
        int samples = Mathf.Min(count, 8);
        var colorKeys = new GradientColorKey[samples];
        for (int g = 0; g < samples; g++)
        {
            float t = (float)g / (samples - 1);
            int idx = Mathf.RoundToInt(t * (count - 1));
            Color c;
            if (loopGains[idx] > 0.15f) c = Color.red;
            else if (loopGains[idx] >= 0.05f) c = Color.yellow;
            else c = Color.green;
            colorKeys[g] = new GradientColorKey(c, t);
        }
        var alphaKeys = new GradientAlphaKey[] {
            new GradientAlphaKey(1f, 0f),
            new GradientAlphaKey(1f, 1f)
        };
        gradient.SetKeys(colorKeys, alphaKeys);
        gainCurveLR.colorGradient = gradient;

        for (int i = 0; i < count; i++)
            gainCurveLR.SetPosition(i, GainToLocal(i));

        // Label
        var labelGo = new GameObject("GainLabel");
        labelGo.transform.SetParent(transform, false);
        gainLabel = labelGo.AddComponent<TextMeshPro>();
        gainLabel.text = "|G| vs cs";
        gainLabel.fontSize = 0.03f;
        gainLabel.alignment = TextAlignmentOptions.Left;
        gainLabel.color = Color.white;
        gainLabel.rectTransform.sizeDelta = new Vector2(0.2f, 0.04f);
        labelGo.transform.localPosition = new Vector3(-graphW * 0.5f, GraphBaseY(0) + graphH + 0.01f, 0f);
    }

    // --- Build Graph 2: Steps vs cs ---
    void BuildStepsGraph()
    {
        var mat = new Material(Shader.Find("Sprites/Default"));
        int count = csValues.Length;

        var go = new GameObject("StepsCurve");
        go.transform.SetParent(transform, false);
        stepsCurveLR = go.AddComponent<LineRenderer>();
        stepsCurveLR.positionCount = count;
        stepsCurveLR.startWidth = 0.003f;
        stepsCurveLR.endWidth = 0.003f;
        stepsCurveLR.useWorldSpace = false;
        stepsCurveLR.material = mat;
        stepsCurveLR.startColor = new Color(0.3f, 0.8f, 1f);
        stepsCurveLR.endColor = new Color(0.3f, 0.8f, 1f);

        for (int i = 0; i < count; i++)
            stepsCurveLR.SetPosition(i, StepsToLocal(i));

        // Red marker at cs=1.00 (last point, steps=1, rapid drop)
        // Actually the first point cs=0.10 has steps=36 (highest)
        // Mark the steep region at cs=0.10 with red
        var markerGo = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        markerGo.name = "StepsHighMarker";
        markerGo.transform.SetParent(transform, false);
        markerGo.transform.localPosition = StepsToLocal(0); // cs=0.10, steps=36
        markerGo.transform.localScale = Vector3.one * 0.012f;
        var mr = markerGo.GetComponent<MeshRenderer>();
        mr.material = new Material(Shader.Find("Sprites/Default"));
        mr.material.color = Color.red;
        var col = markerGo.GetComponent<Collider>();
        if (col != null) Destroy(col);

        // Label
        var labelGo = new GameObject("StepsLabel");
        labelGo.transform.SetParent(transform, false);
        stepsLabel = labelGo.AddComponent<TextMeshPro>();
        stepsLabel.text = "Steps vs cs";
        stepsLabel.fontSize = 0.03f;
        stepsLabel.alignment = TextAlignmentOptions.Left;
        stepsLabel.color = Color.white;
        stepsLabel.rectTransform.sizeDelta = new Vector2(0.2f, 0.04f);
        labelGo.transform.localPosition = new Vector3(-graphW * 0.5f, GraphBaseY(1) + graphH + 0.01f, 0f);
    }

    // --- Build Graph 3: fw trajectories ---
    void BuildTrajectoryGraph()
    {
        var mat = new Material(Shader.Find("Sprites/Default"));

        // Find max trajectory length
        int maxLen = 0;
        foreach (var kv in trajectories)
            if (kv.Value.Length > maxLen) maxLen = kv.Value.Length;

        // Build each trajectory
        for (int t = 0; t < trajKeys.Length; t++)
        {
            if (!trajectories.ContainsKey(trajKeys[t])) continue;
            float[] traj = trajectories[trajKeys[t]];

            var go = new GameObject("Traj_" + trajKeys[t]);
            go.transform.SetParent(transform, false);
            var lr = go.AddComponent<LineRenderer>();
            lr.positionCount = traj.Length;
            lr.startWidth = trajWidths[t];
            lr.endWidth = trajWidths[t];
            lr.useWorldSpace = false;
            lr.material = mat;
            lr.startColor = trajColors[t];
            lr.endColor = trajColors[t];

            for (int i = 0; i < traj.Length; i++)
                lr.SetPosition(i, TrajToLocal(2, traj, i));
        }

        // Threshold line at fw=0.3
        float threshY = GraphBaseY(2) + threshold * graphH;
        var threshGo = new GameObject("TrajThreshold");
        threshGo.transform.SetParent(transform, false);
        thresholdLineLR = threshGo.AddComponent<LineRenderer>();
        thresholdLineLR.positionCount = 2;
        thresholdLineLR.startWidth = 0.001f;
        thresholdLineLR.endWidth = 0.001f;
        thresholdLineLR.useWorldSpace = false;
        thresholdLineLR.material = mat;
        thresholdLineLR.startColor = Color.white;
        thresholdLineLR.endColor = Color.white;
        thresholdLineLR.SetPosition(0, new Vector3(-graphW * 0.5f, threshY, 0f));
        thresholdLineLR.SetPosition(1, new Vector3(graphW * 0.5f, threshY, 0f));

        // Label
        var labelGo = new GameObject("TrajLabel");
        labelGo.transform.SetParent(transform, false);
        trajLabel = labelGo.AddComponent<TextMeshPro>();
        trajLabel.text = "fw trajectories";
        trajLabel.fontSize = 0.03f;
        trajLabel.alignment = TextAlignmentOptions.Left;
        trajLabel.color = Color.white;
        trajLabel.rectTransform.sizeDelta = new Vector2(0.2f, 0.04f);
        labelGo.transform.localPosition = new Vector3(-graphW * 0.5f, GraphBaseY(2) + graphH + 0.01f, 0f);
    }

    // --- Build markers ---
    void BuildMarkers()
    {
        var mat = new Material(Shader.Find("Sprites/Default"));

        // Current marker (white) at cs=0.50
        currentMarker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        currentMarker.name = "CurrentMarker";
        currentMarker.transform.SetParent(transform, false);
        currentMarker.transform.localScale = Vector3.one * 0.016f; // radius 0.008m
        var cmr = currentMarker.GetComponent<MeshRenderer>();
        cmr.material = new Material(mat);
        cmr.material.color = Color.white;
        var cc = currentMarker.GetComponent<Collider>();
        if (cc != null) Destroy(cc);

        // Optimal marker (gold) at cs=0.95
        optimalMarker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        optimalMarker.name = "OptimalMarker";
        optimalMarker.transform.SetParent(transform, false);
        optimalMarker.transform.localScale = Vector3.one * 0.016f;
        var omr = optimalMarker.GetComponent<MeshRenderer>();
        omr.material = new Material(mat);
        omr.material.color = new Color(1f, 0.8f, 0f);
        var oc = optimalMarker.GetComponent<Collider>();
        if (oc != null) Destroy(oc);

        // Critical marker (red) at cs=1.00
        criticalMarker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        criticalMarker.name = "CriticalMarker";
        criticalMarker.transform.SetParent(transform, false);
        criticalMarker.transform.localScale = Vector3.one * 0.016f;
        var rmr = criticalMarker.GetComponent<MeshRenderer>();
        rmr.material = new Material(mat);
        rmr.material.color = Color.red;
        var rc = criticalMarker.GetComponent<Collider>();
        if (rc != null) Destroy(rc);

        UpdateMarkerPositions();
    }

    void UpdateMarkerPositions()
    {
        // Current marker on gain graph
        float gainAtCs = InterpolateGain(interactiveCs);
        float maxGain = loopGains[0];
        float ny = gainAtCs / maxGain;
        float x = CsToLocalX(interactiveCs);
        currentMarker.transform.localPosition = new Vector3(x, GraphBaseY(0) + ny * graphH, -0.005f);

        // Optimal marker
        float optGain = InterpolateGain(optimalCs);
        float ony = optGain / maxGain;
        float ox = CsToLocalX(optimalCs);
        optimalMarker.transform.localPosition = new Vector3(ox, GraphBaseY(0) + ony * graphH, -0.005f);

        // Critical marker
        float critGain = InterpolateGain(criticalCs);
        float cny = critGain / maxGain;
        float cx = CsToLocalX(criticalCs);
        criticalMarker.transform.localPosition = new Vector3(cx, GraphBaseY(0) + cny * graphH, -0.005f);
    }

    // --- Build stats display ---
    void BuildStatsDisplay()
    {
        var go = new GameObject("StatsDisplay");
        go.transform.SetParent(transform, false);
        statsDisplay = go.AddComponent<TextMeshPro>();
        statsDisplay.fontSize = 0.025f;
        statsDisplay.alignment = TextAlignmentOptions.Left;
        statsDisplay.color = Color.white;
        statsDisplay.richText = true;
        statsDisplay.rectTransform.sizeDelta = new Vector2(0.25f, 0.2f);
        go.transform.localPosition = new Vector3(graphW * 0.5f + 0.03f, GraphBaseY(1) + graphH * 0.5f, 0f);
    }

    // --- Interpolation ---
    float InterpolateGain(float cs)
    {
        if (csValues == null || csValues.Length < 2) return 0f;
        if (cs <= csValues[0]) return loopGains[0];
        if (cs >= csValues[csValues.Length - 1]) return loopGains[csValues.Length - 1];
        for (int i = 0; i < csValues.Length - 1; i++)
        {
            if (cs >= csValues[i] && cs <= csValues[i + 1])
            {
                float t = (cs - csValues[i]) / (csValues[i + 1] - csValues[i]);
                return Mathf.Lerp(loopGains[i], loopGains[i + 1], t);
            }
        }
        return loopGains[loopGains.Length - 1];
    }

    float InterpolateSteps(float cs)
    {
        if (csValues == null || csValues.Length < 2) return 0f;
        if (cs <= csValues[0]) return convergenceSteps[0];
        if (cs >= csValues[csValues.Length - 1]) return convergenceSteps[csValues.Length - 1];
        for (int i = 0; i < csValues.Length - 1; i++)
        {
            if (cs >= csValues[i] && cs <= csValues[i + 1])
            {
                float t = (cs - csValues[i]) / (csValues[i + 1] - csValues[i]);
                return Mathf.Lerp(convergenceSteps[i], convergenceSteps[i + 1], t);
            }
        }
        return convergenceSteps[convergenceSteps.Length - 1];
    }

    // --- Update ---
    void Update()
    {
        if (!placedOnStart)
        {
            var cam = Camera.main;
            if (cam != null)
            {
                transform.position = cam.transform.position + cam.transform.forward * 1.5f;
                placedOnStart = true;
            }
        }

        if (!dataLoaded) return;

        HandleInput();
        UpdateMarkerPositions();
        UpdateStats();
        BillboardLabels();
    }

    void HandleInput()
    {
        float stickX = 0f;
        bool resetPressed = false;
        bool gripPressed = false;

#if UNITY_EDITOR
        var kb = Keyboard.current;
        if (kb != null)
        {
            if (kb.rightArrowKey.isPressed) stickX = 1f;
            if (kb.leftArrowKey.isPressed) stickX = -1f;
            if (kb.rKey.wasPressedThisFrame) resetPressed = true;
            if (kb.gKey.wasPressedThisFrame) gripPressed = true;
        }
#else
        var rightCtrl = XRController.rightHand;
        if (rightCtrl != null)
        {
            var stick = rightCtrl.TryGetChildControl<StickControl>("thumbstick");
            if (stick != null)
                stickX = stick.ReadValue().x;

            var aBtn = rightCtrl.TryGetChildControl<ButtonControl>("primaryButton");
            if (aBtn != null && aBtn.wasPressedThisFrame)
                resetPressed = true;
        }

        var leftCtrl = XRController.leftHand;
        if (leftCtrl != null)
        {
            var grip = leftCtrl.TryGetChildControl<AxisControl>("grip");
            if (grip != null && grip.ReadValue() > 0.5f)
                gripPressed = true;
        }
#endif

        // Move cs with right stick horizontal
        if (Mathf.Abs(stickX) > 0.1f)
        {
            float speed = 0.3f;
            interactiveCs += stickX * speed * Time.deltaTime;
            interactiveCs = Mathf.Clamp(interactiveCs, 0.10f, 1.05f);
        }

        // A button: reset to cs=0.50
        if (resetPressed)
        {
            interactiveCs = 0.50f;
            debugMessage = "";
        }

        // Left grip: reposition in front of HMD
        if (gripPressed)
        {
            var cam = Camera.main;
            if (cam != null)
                transform.position = cam.transform.position + cam.transform.forward * 1.5f;
        }
    }

    void UpdateStats()
    {
        if (statsDisplay == null) return;

        float gain = InterpolateGain(Mathf.Min(interactiveCs, 1.0f));
        float steps = InterpolateSteps(Mathf.Min(interactiveCs, 1.0f));

        string status;
        Color statusColor;
        if (interactiveCs >= 1.00f)
        {
            status = "<color=red>OVERSHOOT!</color>";
            statusColor = Color.red;
        }
        else if (Mathf.Abs(interactiveCs - 0.95f) < 0.03f)
        {
            status = "<color=#FFcc00>Optimal \u2605</color>";
            statusColor = new Color(1f, 0.8f, 0f);
        }
        else
        {
            status = "Current";
            statusColor = Color.white;
        }

        statsDisplay.text = $"cs: {interactiveCs:F2}\n|G|: {gain:F4}\nSteps: {steps:F0}\nStatus: {status}";
    }

    void BillboardLabels()
    {
        var cam = Camera.main;
        if (cam == null) return;

        TextMeshPro[] labels = { gainLabel, stepsLabel, trajLabel, statsDisplay };
        for (int i = 0; i < labels.Length; i++)
        {
            if (labels[i] != null)
            {
                labels[i].transform.LookAt(cam.transform);
                labels[i].transform.Rotate(0f, 180f, 0f);
            }
        }
    }

    // --- Public API ---
    public string GetParamLabel()
    {
        if (!dataLoaded) return "Loading...";
        float gain = InterpolateGain(Mathf.Min(interactiveCs, 1.0f));
        return $"cs:{interactiveCs:F2}  |G|:{gain:F4}";
    }

    public string GetDebugInfo()
    {
        if (!dataLoaded) return "Loading JSON...";
        float gain = InterpolateGain(Mathf.Min(interactiveCs, 1.0f));
        float steps = InterpolateSteps(Mathf.Min(interactiveCs, 1.0f));
        string status = interactiveCs >= 1.0f ? "<color=red>OVERSHOOT</color>"
            : Mathf.Abs(interactiveCs - 0.95f) < 0.03f ? "<color=#FFcc00>Optimal</color>"
            : "<color=green>Stable</color>";
        return $"cs: {interactiveCs:F2}  |G|: {gain:F4}\n"
             + $"Steps: {steps:F0}\n"
             + $"Status: {status}\n"
             + (debugMessage != "" ? debugMessage : "");
    }
}
