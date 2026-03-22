using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.XR;
using UnityEngine.Networking;
using System.Collections;
using System.Collections.Generic;
using System.IO;

public class FanoQ3Animator : MonoBehaviour
{
    // Graph structure: Fano(7) x Q3(8) = 56 vertices, 252 edges
    Vector3[] verts = new Vector3[56];
    float q3Radius = 0.2f;
    float fanoRadius = 0.05f;

    // Animation data
    AnimData animData;
    int currentFrame;
    float frameTimer;
    float baseInterval = 0.05f; // 20fps
    int speedIndex = 4; // 0=0.06x, 1=0.12x, 2=0.25x, 3=0.5x, 4=1x, 5=2x, 6=4x
    static readonly float[] speedMultipliers = { 0.06f, 0.12f, 0.25f, 0.5f, 1f, 2f, 4f };
    public static readonly string[] speedNames = { "0.06x", "0.12x", "0.25x", "0.5x", "1x", "2x", "4x" };
    bool playing = true;
    bool dataLoaded;

    // Current frame state (public for DebugDisplay)
    public string currentPhase = "";
    public float phiRate;
    public float energy;
    public int attackCount;
    public int successCount;

    // Min/max tracking
    public float phiMin = 100f;
    public float energyMax = 0f;

    // Edge rendering
    LineRenderer[] edgeLines;
    int[][] edgeIndices; // vertex index pairs
    string[] edgeKeys;   // JSON key for each edge
    HashSet<string> attackedEdgesSet = new HashSet<string>();

    // Attack edge recovery state (案C)
    // Tracks which edges were attacked and their recovery progress
    HashSet<string> activeAttackEdges = new HashSet<string>();
    float[] edgeAttackLerp; // 1.0 = full orange, 0.0 = fully recovered
    string prevPhase = "";

    // Phase flash overlay
    GameObject flashQuad;
    MeshRenderer flashRenderer;

    bool placedOnStart;
    float rotSpeed = 60f;

    // Q3 vertex positions (unit cube corners)
    static readonly Vector3[] q3Corners = {
        new Vector3(-1,-1,-1), new Vector3(-1,-1,+1),
        new Vector3(-1,+1,-1), new Vector3(-1,+1,+1),
        new Vector3(+1,-1,-1), new Vector3(+1,-1,+1),
        new Vector3(+1,+1,-1), new Vector3(+1,+1,+1),
    };

    // JSON parsing classes
    class AnimData
    {
        public GraphData graph;
        public FrameData[] frames;
    }

    class GraphData
    {
        public string[] nodes;
        public string[][] edges;
    }

    class FrameData
    {
        public string phase;
        public int step;
        public Dictionary<string, float> weights;
        public float phi_rate;
        public float energy;
        public int attack_count;
        public int success_count;
        public string[] attacked_edges;
    }

    void Start()
    {
        foreach (Transform child in transform)
            Destroy(child.gameObject);

        BuildVertexPositions();
        SetupFlashOverlay();
        StartCoroutine(LoadAnimationDataAsync());
    }

    void BuildVertexPositions()
    {
        for (int q = 0; q < 8; q++)
        {
            Vector3 clusterCenter = q3Corners[q].normalized * q3Radius;

            // Build a local tangent frame for placing Fano vertices
            Vector3 up = clusterCenter.normalized;
            Vector3 right = Vector3.Cross(up, Vector3.up).normalized;
            if (right.sqrMagnitude < 0.01f)
                right = Vector3.Cross(up, Vector3.right).normalized;
            Vector3 forward = Vector3.Cross(right, up).normalized;

            for (int f = 0; f < 7; f++)
            {
                Vector3 offset;
                if (f == 0)
                {
                    offset = Vector3.zero;
                }
                else
                {
                    float angle = (f - 1) * Mathf.PI * 2f / 6f;
                    offset = (Mathf.Cos(angle) * right + Mathf.Sin(angle) * forward) * fanoRadius;
                }
                verts[q * 7 + f] = clusterCenter + offset;
            }
        }
    }

    IEnumerator LoadAnimationDataAsync()
    {
        string path = Path.Combine(Application.streamingAssetsPath, "animation_data.json");
        string json = null;

#if UNITY_ANDROID && !UNITY_EDITOR
        // Android/Quest: StreamingAssets is inside the APK (jar:file://)
        // Must use UnityWebRequest
        Debug.Log("FanoQ3: Loading JSON via UnityWebRequest: " + path);
        using (var req = UnityWebRequest.Get(path))
        {
            yield return req.SendWebRequest();
            if (req.result == UnityWebRequest.Result.Success)
            {
                json = req.downloadHandler.text;
                Debug.Log("FanoQ3: UnityWebRequest success, length=" + json.Length);
            }
            else
            {
                Debug.LogError("FanoQ3: Failed to load JSON: " + req.error);
                yield break;
            }
        }
#else
        // Editor / Standalone: direct file read
        Debug.Log("FanoQ3: Loading JSON via File.ReadAllText: " + path);
        try
        {
            json = File.ReadAllText(path);
            Debug.Log("FanoQ3: File.ReadAllText success, length=" + json.Length);
        }
        catch (System.Exception e)
        {
            Debug.LogError("FanoQ3: Failed to read JSON: " + e.Message);
            yield break;
        }
#endif

        // Parse on next frame to avoid stall
        yield return null;

        animData = ParseAnimData(json);
        json = null; // free memory

        if (animData == null || animData.graph == null)
        {
            Debug.LogError("FanoQ3: ParseAnimData returned null");
            yield break;
        }

        // Build edge index mapping from node names to vertex indices
        var nodeIndex = new Dictionary<string, int>();
        for (int i = 0; i < animData.graph.nodes.Length; i++)
            nodeIndex[animData.graph.nodes[i]] = i;

        edgeIndices = new int[animData.graph.edges.Length][];
        edgeKeys = new string[animData.graph.edges.Length];
        for (int i = 0; i < animData.graph.edges.Length; i++)
        {
            string n0 = animData.graph.edges[i][0];
            string n1 = animData.graph.edges[i][1];
            edgeIndices[i] = new[] { nodeIndex[n0], nodeIndex[n1] };
            edgeKeys[i] = $"({n0},{n1})";
        }

        SetupEdges();
        dataLoaded = true;

        int frameCount = animData.frames != null ? animData.frames.Length : 0;
        Debug.Log("FanoQ3: JSON loaded, frames=" + frameCount
            + ", nodes=" + animData.graph.nodes.Length
            + ", edges=" + animData.graph.edges.Length);
    }

    // --- JSON Parser (unchanged) ---

    AnimData ParseAnimData(string json)
    {
        var data = new AnimData();

        int nodesStart = json.IndexOf("\"nodes\"");
        int arrStart = json.IndexOf('[', nodesStart);
        int arrEnd = FindMatchingBracket(json, arrStart);
        data.graph = new GraphData();
        data.graph.nodes = ParseStringArray(json.Substring(arrStart, arrEnd - arrStart + 1));

        int edgesStart = json.IndexOf("\"edges\"");
        arrStart = json.IndexOf('[', edgesStart);
        arrEnd = FindMatchingBracket(json, arrStart);
        string edgesStr = json.Substring(arrStart, arrEnd - arrStart + 1);
        data.graph.edges = ParseEdgesArray(edgesStr);

        int framesStart = json.IndexOf("\"frames\"");
        arrStart = json.IndexOf('[', framesStart);
        arrEnd = FindMatchingBracket(json, arrStart);
        string framesStr = json.Substring(arrStart + 1, arrEnd - arrStart - 1);
        data.frames = ParseFrames(framesStr);

        return data;
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
        frame.attack_count = ExtractInt(obj, "attack_count");
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

    // --- Edge setup (called after JSON load completes) ---

    void SetupEdges()
    {
        if (edgeIndices == null) return;

        edgeLines = new LineRenderer[edgeIndices.Length];
        edgeAttackLerp = new float[edgeIndices.Length];
        var mat = new Material(Shader.Find("Sprites/Default"));

        for (int i = 0; i < edgeIndices.Length; i++)
        {
            var go = new GameObject("edge");
            go.transform.SetParent(transform, false);
            var lr = go.AddComponent<LineRenderer>();
            lr.positionCount = 2;
            lr.startWidth = 0.002f;
            lr.endWidth = 0.002f;
            lr.useWorldSpace = true;
            lr.material = mat;
            lr.startColor = Color.white;
            lr.endColor = Color.white;
            edgeLines[i] = lr;
        }

        Debug.Log("FanoQ3: SetupEdges complete, lines=" + edgeLines.Length);
    }

    void SetupFlashOverlay()
    {
        flashQuad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        flashQuad.name = "PhaseFlash";
        flashQuad.transform.SetParent(transform, false);
        Destroy(flashQuad.GetComponent<Collider>());

        flashRenderer = flashQuad.GetComponent<MeshRenderer>();
        var mat = new Material(Shader.Find("Sprites/Default"));
        mat.renderQueue = 4000;
        mat.color = new Color(0, 0, 0, 0);
        flashRenderer.material = mat;
        flashQuad.transform.localScale = new Vector3(2f, 2f, 1f);
    }

    void PlaceInFrontOfCamera()
    {
        var cam = Camera.main;
        if (cam == null) return;
        transform.position = cam.transform.position + cam.transform.forward * 1.5f;
    }

    // --- Public: called by ShapeManager to delegate A-short press ---
    public void TogglePlayPause()
    {
        if (!playing)
        {
            // At last frame: reset and play from beginning
            if (animData != null && animData.frames != null
                && currentFrame >= animData.frames.Length - 1)
            {
                ResetPlayback();
                playing = true;
                Debug.Log("FanoQ3: restart from frame 0");
            }
            else
            {
                // Paused mid-playback: resume from current frame
                playing = true;
                Debug.Log("FanoQ3: resumed at frame " + currentFrame);
            }
        }
        else
        {
            // Playing: pause, keep frame position
            playing = false;
            Debug.Log("FanoQ3: paused at frame " + currentFrame);
        }
    }

    public void ResetPlayback()
    {
        currentFrame = 0;
        frameTimer = 0f;
        phiMin = 100f;
        energyMax = 0f;
        activeAttackEdges.Clear();
        if (edgeAttackLerp != null)
            for (int i = 0; i < edgeAttackLerp.Length; i++)
                edgeAttackLerp[i] = 0f;
        Debug.Log("FanoQ3: reset to frame 0");
    }

    void Update()
    {
        if (!placedOnStart)
        {
            PlaceInFrontOfCamera();
            placedOnStart = true;
        }

        if (!dataLoaded) return;

        HandleInput();
        UpdateAnimation();
        UpdateEdgeVisuals();
        UpdateFlashOverlay();
        UpdateEdgePositions();
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
            // play/pause is handled via ShapeManager -> TogglePlayPause()
            if (kb.gKey.wasPressedThisFrame) resetPressed = true;
            if (kb.iKey.wasPressedThisFrame) ChangeSpeed(1);
            if (kb.kKey.wasPressedThisFrame) ChangeSpeed(-1);
            if (kb.jKey.isPressed) rxRot = -1f;
            if (kb.lKey.isPressed) rxRot = 1f;
        }
#else
        // A button play/pause is handled by ShapeManager -> TogglePlayPause()
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

        // Left grip: reset
        var leftCtrl = XRController.leftHand;
        if (leftCtrl != null)
        {
            var grip = leftCtrl.TryGetChildControl<AxisControl>("grip");
            if (grip != null && grip.ReadValue() > 0.5f) resetPressed = true;
        }

        // Speed change via right stick Y (with threshold)
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

        // 3D rotation
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
            currentFrame++;
            if (currentFrame >= animData.frames.Length)
            {
                currentFrame = animData.frames.Length - 1;
                playing = false;
                frameTimer = 0f;
                Debug.Log("FanoQ3: reached end, auto-stopped");
                return;
            }
        }
    }

    static readonly Color orangeAttack = new Color(1f, 0.5f, 0f);

    Color WeightToColor(float w)
    {
        // weight=1.0 → green, weight=0.5 → white, weight=0.0 → blue
        if (w >= 0.5f)
        {
            float t = (w - 0.5f) * 2f;
            return Color.Lerp(Color.white, Color.green, t);
        }
        else
        {
            float t = w * 2f;
            return Color.Lerp(Color.blue, Color.white, t);
        }
    }

    void UpdateEdgeVisuals()
    {
        if (edgeLines == null) return;

        var frame = animData.frames[currentFrame];
        currentPhase = frame.phase;
        phiRate = frame.phi_rate;
        energy = frame.energy;
        attackCount = frame.attack_count;
        successCount = frame.success_count;

        // Track min/max
        phiMin = Mathf.Min(phiMin, phiRate);
        energyMax = Mathf.Max(energyMax, energy);

        // Detect phase transitions for attack edge management
        attackedEdgesSet.Clear();
        if (frame.attacked_edges != null)
        {
            foreach (var ae in frame.attacked_edges)
                attackedEdgesSet.Add(ae);
        }

        // On entering attack phase: mark newly attacked edges
        if (currentPhase == "attacking")
        {
            for (int i = 0; i < edgeKeys.Length; i++)
            {
                if (attackedEdgesSet.Contains(edgeKeys[i]))
                {
                    activeAttackEdges.Add(edgeKeys[i]);
                    edgeAttackLerp[i] = 1f;
                }
            }
        }

        // During recovery: fade attack lerp toward 0
        if (currentPhase == "recovering")
        {
            float fadeSpeed = 2f * Time.deltaTime; // ~0.5s to fully recover
            for (int i = 0; i < edgeAttackLerp.Length; i++)
            {
                if (edgeAttackLerp[i] > 0f)
                    edgeAttackLerp[i] = Mathf.Max(0f, edgeAttackLerp[i] - fadeSpeed);
            }
        }

        // On converging: clear all attack state
        if (currentPhase == "converging" && prevPhase == "recovering")
        {
            activeAttackEdges.Clear();
            for (int i = 0; i < edgeAttackLerp.Length; i++)
                edgeAttackLerp[i] = 0f;
        }

        prevPhase = currentPhase;

        // Apply colors
        for (int i = 0; i < edgeLines.Length; i++)
        {
            float w = 0.5f;
            if (frame.weights != null)
                frame.weights.TryGetValue(edgeKeys[i], out w);

            Color weightColor = WeightToColor(w);
            float attackT = edgeAttackLerp[i];

            Color c;
            float width;
            if (attackT > 0.01f)
            {
                // Lerp between orange (attacked) and weight color (recovered)
                c = Color.Lerp(weightColor, orangeAttack, attackT);
                width = Mathf.Lerp(0.002f, 0.006f, attackT);
            }
            else
            {
                c = weightColor;
                width = 0.002f;
            }

            edgeLines[i].startColor = c;
            edgeLines[i].endColor = c;
            edgeLines[i].startWidth = width;
            edgeLines[i].endWidth = width;
        }
    }

    void UpdateFlashOverlay()
    {
        if (flashQuad == null) return;

        var cam = Camera.main;
        if (cam == null) return;

        flashQuad.transform.position = cam.transform.position + cam.transform.forward * 0.3f;
        flashQuad.transform.rotation = cam.transform.rotation;

        Color flashColor;
        switch (currentPhase)
        {
            case "attacking":
                float redFlash = Mathf.PingPong(Time.time * 3f, 1f);
                flashColor = new Color(1f, 0f, 0f, redFlash * 0.15f);
                break;
            case "recovering":
                float greenPulse = (Mathf.Sin(Time.time * 2f) + 1f) * 0.5f;
                flashColor = new Color(0f, 1f, 0f, greenPulse * 0.1f);
                break;
            default:
                flashColor = new Color(0, 0, 0, 0);
                break;
        }

        flashRenderer.material.color = flashColor;
    }

    void UpdateEdgePositions()
    {
        if (edgeLines == null) return;

        Vector3 pos = transform.position;
        Quaternion rot = transform.rotation;
        for (int i = 0; i < edgeIndices.Length; i++)
        {
            edgeLines[i].SetPosition(0, pos + rot * verts[edgeIndices[i][0]]);
            edgeLines[i].SetPosition(1, pos + rot * verts[edgeIndices[i][1]]);
        }
    }

    public string GetParamLabel()
    {
        string playState = playing ? "Play" : "Pause";
        string spd = speedNames[speedIndex];
        int total = (animData != null && animData.frames != null) ? animData.frames.Length : 0;
        return $"FanoQ3 [{playState}] [{spd}] F:{currentFrame}/{total}";
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
            default: phaseColor = "cyan"; break;
        }
        return $"\u03c6: {phiRate:F1}%  E: {energy:F3}\n" +
               $"Phase: <color={phaseColor}>{currentPhase}</color>  Attack: {attackCount}/{successCount}\n" +
               $"\u03c6 min: {phiMin:F1}%  E max: {energyMax:F3}";
    }
}
