using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.XR;
using UnityEngine.Networking;
using System.Collections;
using System.IO;
using TMPro;

public class MorseLandscape1D : MonoBehaviour
{
    // Data
    float[] flowWeights;
    float[] losses;
    float[] criticalFw;
    float[] criticalLoss;
    float threshold = 0.3f;
    float fwMin = 0.05f, fwMax = 0.85f;
    float lossMin = 0.2799f, lossMax = 0.65f;
    int pointCount;
    bool dataLoaded;

    // Display scale
    const float xWidth = 0.4f;   // 0.4m width
    const float yHeight = 0.3f;  // 0.3m height

    // LineRenderer for loss curve
    LineRenderer curveLR;

    // Threshold line (dashed)
    LineRenderer[] thresholdDashes;
    TextMeshPro thresholdLabel;

    // Critical point markers
    GameObject[] minMarkers;
    TextMeshPro[] minLabels;

    // AAS current position sphere
    GameObject aasSphere;
    MeshRenderer aasMR;
    float currentFw = 0.1f;
    bool thresholdCrossed;

    // Threshold flash
    float flashTimer;
    bool flashActive;

    // Stats display
    TextMeshPro statsLabel;

    // Placement
    bool placedOnStart;

    // DebugDisplay message
    public string debugMessage = "";

    void Start()
    {
        foreach (Transform child in transform)
            Destroy(child.gameObject);

        StartCoroutine(LoadDataAsync());
    }

    IEnumerator LoadDataAsync()
    {
        string path = Path.Combine(Application.streamingAssetsPath, "morse_landscape_unity.json");
        string json = null;

#if UNITY_ANDROID && !UNITY_EDITOR
        Debug.Log("MorseLandscape1D: Loading JSON via UnityWebRequest: " + path);
        using (var req = UnityWebRequest.Get(path))
        {
            yield return req.SendWebRequest();
            if (req.result == UnityWebRequest.Result.Success)
            {
                json = req.downloadHandler.text;
                Debug.Log("MorseLandscape1D: UnityWebRequest success, length=" + json.Length);
            }
            else
            {
                Debug.LogError("MorseLandscape1D: Failed to load JSON: " + req.error);
                yield break;
            }
        }
#else
        Debug.Log("MorseLandscape1D: Loading JSON via File.ReadAllText: " + path);
        try
        {
            json = File.ReadAllText(path);
            Debug.Log("MorseLandscape1D: File.ReadAllText success, length=" + json.Length);
        }
        catch (System.Exception e)
        {
            Debug.LogError("MorseLandscape1D: Failed to read JSON: " + e.Message);
            yield break;
        }
#endif

        yield return null;

        ParseJson(json);
        json = null;

        if (flowWeights == null || losses == null)
        {
            Debug.LogError("MorseLandscape1D: Parse failed");
            yield break;
        }

        BuildCurve();
        BuildThresholdLine();
        BuildCriticalMarkers();
        BuildAASSphere();
        BuildStatsLabel();
        dataLoaded = true;

        Debug.Log("MorseLandscape1D: Ready, points=" + pointCount
            + ", criticals=" + criticalFw.Length);
    }

    // --- JSON parsing (manual, no JsonUtility for arrays) ---
    void ParseJson(string json)
    {
        flowWeights = ParseFloatArray(json, "flow_weights");
        losses = ParseFloatArray(json, "losses");

        if (flowWeights == null || losses == null) return;
        pointCount = Mathf.Min(flowWeights.Length, losses.Length);

        // Parse critical_points
        int cpStart = json.IndexOf("\"critical_points\"");
        if (cpStart >= 0)
        {
            int arrStart = json.IndexOf('[', cpStart);
            int arrEnd = json.IndexOf(']', arrStart + 1);
            // Find next ']' that closes the array (skip nested objects)
            int depth = 0;
            for (int i = arrStart; i < json.Length; i++)
            {
                if (json[i] == '[') depth++;
                else if (json[i] == ']') { depth--; if (depth == 0) { arrEnd = i; break; } }
            }
            string cpSection = json.Substring(arrStart, arrEnd - arrStart + 1);

            // Count objects
            int count = 0;
            int idx = 0;
            while ((idx = cpSection.IndexOf("\"fw\"", idx)) >= 0) { count++; idx++; }

            criticalFw = new float[count];
            criticalLoss = new float[count];

            idx = 0;
            for (int i = 0; i < count; i++)
            {
                idx = cpSection.IndexOf("\"fw\"", idx);
                criticalFw[i] = ParseValueAfterKey(cpSection, idx);
                int lossIdx = cpSection.IndexOf("\"loss\"", idx);
                criticalLoss[i] = ParseValueAfterKey(cpSection, lossIdx);
                idx = lossIdx + 1;
            }
        }
        else
        {
            criticalFw = new float[0];
            criticalLoss = new float[0];
        }

        // Parse scalar values
        threshold = ParseScalar(json, "threshold", 0.3f);
        fwMin = ParseScalar(json, "fw_min", 0.05f);
        fwMax = ParseScalar(json, "fw_max", 0.85f);
        lossMin = ParseScalar(json, "loss_min", 0.2799f);
        lossMax = ParseScalar(json, "loss_max", 0.65f);
    }

    float ParseValueAfterKey(string s, int keyIdx)
    {
        int colon = s.IndexOf(':', keyIdx);
        int start = colon + 1;
        while (start < s.Length && (s[start] == ' ' || s[start] == '\t')) start++;
        int end = start;
        while (end < s.Length && s[end] != ',' && s[end] != '}' && s[end] != ']' && s[end] != '\n') end++;
        string val = s.Substring(start, end - start).Trim();
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

    // --- Coordinate mapping ---
    Vector3 FwLossToLocal(float fw, float loss)
    {
        float nx = (fw - fwMin) / (fwMax - fwMin);
        float ny = (loss - lossMin) / (lossMax - lossMin);
        return new Vector3(nx * xWidth - xWidth * 0.5f, ny * yHeight, 0f);
    }

    float FwToLocalX(float fw)
    {
        float nx = (fw - fwMin) / (fwMax - fwMin);
        return nx * xWidth - xWidth * 0.5f;
    }

    float LossAtFw(float fw)
    {
        if (flowWeights == null || pointCount < 2) return lossMin;
        if (fw <= flowWeights[0]) return losses[0];
        if (fw >= flowWeights[pointCount - 1]) return losses[pointCount - 1];

        for (int i = 0; i < pointCount - 1; i++)
        {
            if (fw >= flowWeights[i] && fw <= flowWeights[i + 1])
            {
                float t = (fw - flowWeights[i]) / (flowWeights[i + 1] - flowWeights[i]);
                return Mathf.Lerp(losses[i], losses[i + 1], t);
            }
        }
        return losses[pointCount - 1];
    }

    // --- Build visualization ---
    void BuildCurve()
    {
        var go = new GameObject("LossCurve");
        go.transform.SetParent(transform, false);
        curveLR = go.AddComponent<LineRenderer>();
        curveLR.positionCount = pointCount;
        curveLR.startWidth = 0.003f;
        curveLR.endWidth = 0.003f;
        curveLR.useWorldSpace = false;
        curveLR.material = new Material(Shader.Find("Sprites/Default"));

        // Set positions and gradient
        var gradient = new Gradient();
        var colorKeys = new GradientColorKey[pointCount];
        var alphaKeys = new GradientAlphaKey[2];
        alphaKeys[0] = new GradientAlphaKey(1f, 0f);
        alphaKeys[1] = new GradientAlphaKey(1f, 1f);

        // Unity Gradient supports max 8 color keys, so sample 8 points
        int gradientSamples = 8;
        var gColorKeys = new GradientColorKey[gradientSamples];
        for (int g = 0; g < gradientSamples; g++)
        {
            float t = (float)g / (gradientSamples - 1);
            int idx = Mathf.RoundToInt(t * (pointCount - 1));
            float normLoss = (losses[idx] - lossMin) / (lossMax - lossMin);
            gColorKeys[g] = new GradientColorKey(
                Color.Lerp(Color.blue, Color.red, normLoss), t);
        }
        gradient.SetKeys(gColorKeys, alphaKeys);
        curveLR.colorGradient = gradient;

        for (int i = 0; i < pointCount; i++)
        {
            curveLR.SetPosition(i, FwLossToLocal(flowWeights[i], losses[i]));
        }
    }

    void BuildThresholdLine()
    {
        float threshX = FwToLocalX(threshold);

        // Dashed line: multiple small segments
        int dashCount = 10;
        float dashLen = yHeight / (dashCount * 2f);
        thresholdDashes = new LineRenderer[dashCount];
        var mat = new Material(Shader.Find("Sprites/Default"));

        for (int i = 0; i < dashCount; i++)
        {
            var go = new GameObject("ThreshDash_" + i);
            go.transform.SetParent(transform, false);
            var lr = go.AddComponent<LineRenderer>();
            lr.positionCount = 2;
            lr.startWidth = 0.002f;
            lr.endWidth = 0.002f;
            lr.useWorldSpace = false;
            lr.material = mat;
            lr.startColor = Color.white;
            lr.endColor = Color.white;

            float y0 = i * 2f * dashLen;
            float y1 = y0 + dashLen;
            lr.SetPosition(0, new Vector3(threshX, y0, 0f));
            lr.SetPosition(1, new Vector3(threshX, y1, 0f));
            thresholdDashes[i] = lr;
        }

        // Label
        var labelGo = new GameObject("ThreshLabel");
        labelGo.transform.SetParent(transform, false);
        thresholdLabel = labelGo.AddComponent<TextMeshPro>();
        thresholdLabel.text = "fw=0.3";
        thresholdLabel.fontSize = 0.08f;
        thresholdLabel.alignment = TextAlignmentOptions.Center;
        thresholdLabel.color = Color.white;
        thresholdLabel.rectTransform.sizeDelta = new Vector2(0.15f, 0.05f);
        labelGo.transform.localPosition = new Vector3(threshX, yHeight + 0.02f, 0f);
    }

    void BuildCriticalMarkers()
    {
        int count = criticalFw.Length;
        minMarkers = new GameObject[count];
        minLabels = new TextMeshPro[count];

        for (int i = 0; i < count; i++)
        {
            Vector3 pos = FwLossToLocal(criticalFw[i], criticalLoss[i]);

            // Yellow sphere
            var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            sphere.name = "MinMarker_" + i;
            sphere.transform.SetParent(transform, false);
            sphere.transform.localPosition = pos;
            sphere.transform.localScale = Vector3.one * 0.02f; // radius 0.01m
            var mr = sphere.GetComponent<MeshRenderer>();
            mr.material = new Material(Shader.Find("Sprites/Default"));
            mr.material.color = Color.yellow;
            // Remove collider
            var col = sphere.GetComponent<Collider>();
            if (col != null) Destroy(col);
            minMarkers[i] = sphere;

            // Label
            var labelGo = new GameObject("MinLabel_" + i);
            labelGo.transform.SetParent(transform, false);
            var tmp = labelGo.AddComponent<TextMeshPro>();
            tmp.text = "min";
            tmp.fontSize = 0.06f;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = Color.yellow;
            tmp.rectTransform.sizeDelta = new Vector2(0.1f, 0.04f);
            labelGo.transform.localPosition = pos + Vector3.up * 0.025f;
            minLabels[i] = tmp;
        }
    }

    void BuildAASSphere()
    {
        aasSphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        aasSphere.name = "AASSphere";
        aasSphere.transform.SetParent(transform, false);
        aasSphere.transform.localScale = Vector3.one * 0.024f; // radius 0.012m
        aasMR = aasSphere.GetComponent<MeshRenderer>();
        aasMR.material = new Material(Shader.Find("Sprites/Default"));
        aasMR.material.color = Color.white;
        var col = aasSphere.GetComponent<Collider>();
        if (col != null) Destroy(col);

        UpdateAASPosition();
    }

    void BuildStatsLabel()
    {
        var go = new GameObject("StatsLabel");
        go.transform.SetParent(transform, false);
        statsLabel = go.AddComponent<TextMeshPro>();
        statsLabel.fontSize = 0.06f;
        statsLabel.alignment = TextAlignmentOptions.Left;
        statsLabel.color = Color.white;
        statsLabel.rectTransform.sizeDelta = new Vector2(0.3f, 0.15f);
        go.transform.localPosition = new Vector3(xWidth * 0.5f + 0.02f, yHeight * 0.5f, 0f);
    }

    void UpdateAASPosition()
    {
        float loss = LossAtFw(currentFw);
        Vector3 pos = FwLossToLocal(currentFw, loss);
        aasSphere.transform.localPosition = pos;
    }

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
        UpdateAASPosition();
        UpdateStats();
        UpdateFlash();
        BillboardLabels();
    }

    void HandleInput()
    {
        float stickY = 0f;
        bool resetPressed = false;
        bool gripPressed = false;

#if UNITY_EDITOR
        var kb = Keyboard.current;
        if (kb != null)
        {
            if (kb.upArrowKey.isPressed) stickY = 1f;
            if (kb.downArrowKey.isPressed) stickY = -1f;
            if (kb.rKey.wasPressedThisFrame) resetPressed = true;
            if (kb.gKey.wasPressedThisFrame) gripPressed = true;
        }
#else
        var rightCtrl = XRController.rightHand;
        if (rightCtrl != null)
        {
            var stick = rightCtrl.TryGetChildControl<StickControl>("thumbstick");
            if (stick != null)
                stickY = stick.ReadValue().y;
        }

        var leftCtrl = XRController.leftHand;
        if (leftCtrl != null)
        {
            var grip = leftCtrl.TryGetChildControl<AxisControl>("grip");
            if (grip != null && grip.ReadValue() > 0.5f)
                gripPressed = true;
        }

        if (rightCtrl != null)
        {
            var aBtn = rightCtrl.TryGetChildControl<ButtonControl>("primaryButton");
            if (aBtn != null && aBtn.wasPressedThisFrame)
                resetPressed = true;
        }
#endif

        // Move along curve
        float speed = 0.15f; // fw units per second

        // Attraction to critical points: slow down near minima
        float attractionFactor = 1f;
        for (int i = 0; i < criticalFw.Length; i++)
        {
            float dist = Mathf.Abs(currentFw - criticalFw[i]);
            if (dist < 0.01f)
            {
                // Attract: pull toward minimum
                float pull = (criticalFw[i] - currentFw) * 3f;
                currentFw += pull * Time.deltaTime;
                attractionFactor = 0.3f; // slow down
            }
        }

        if (Mathf.Abs(stickY) > 0.1f)
        {
            float prevFw = currentFw;
            currentFw += stickY * speed * attractionFactor * Time.deltaTime;
            currentFw = Mathf.Clamp(currentFw, fwMin, fwMax);

            // Check threshold crossing
            if (!thresholdCrossed && prevFw < threshold && currentFw >= threshold)
            {
                OnThresholdCrossed();
            }
            else if (thresholdCrossed && currentFw < threshold)
            {
                thresholdCrossed = false;
                aasMR.material.color = Color.white;
                debugMessage = "";
            }
        }

        // A button: reset to fw=0.1
        if (resetPressed)
        {
            currentFw = 0.1f;
            thresholdCrossed = false;
            aasMR.material.color = Color.white;
            debugMessage = "";
            flashActive = false;
        }

        // Left grip: reposition in front of HMD
        if (gripPressed)
        {
            var cam = Camera.main;
            if (cam != null)
                transform.position = cam.transform.position + cam.transform.forward * 1.5f;
        }
    }

    void OnThresholdCrossed()
    {
        thresholdCrossed = true;
        flashActive = true;
        flashTimer = 0f;
        aasMR.material.color = new Color(1f, 0.6f, 0f); // orange
        debugMessage = "Threshold crossed!";
        Debug.Log("MorseLandscape1D: Threshold crossed at fw=" + currentFw);
    }

    void UpdateFlash()
    {
        if (!flashActive) return;

        flashTimer += Time.deltaTime;
        if (flashTimer < 0.3f)
        {
            // Flash threshold dashes yellow
            Color flashColor = Color.Lerp(Color.yellow, Color.white, flashTimer / 0.3f);
            for (int i = 0; i < thresholdDashes.Length; i++)
            {
                thresholdDashes[i].startColor = flashColor;
                thresholdDashes[i].endColor = flashColor;
            }
        }
        else
        {
            // Restore white
            for (int i = 0; i < thresholdDashes.Length; i++)
            {
                thresholdDashes[i].startColor = Color.white;
                thresholdDashes[i].endColor = Color.white;
            }
            flashActive = false;
        }
    }

    void UpdateStats()
    {
        if (statsLabel == null) return;
        float loss = LossAtFw(currentFw);
        float delta = threshold - currentFw;
        statsLabel.text = $"fw: {currentFw:F3}\nLoss: {loss:F3}\n\u0394 to threshold: {delta:F3}";
    }

    void BillboardLabels()
    {
        var cam = Camera.main;
        if (cam == null) return;

        // Billboard threshold label
        if (thresholdLabel != null)
        {
            thresholdLabel.transform.LookAt(cam.transform);
            thresholdLabel.transform.Rotate(0f, 180f, 0f);
        }

        // Billboard min labels
        if (minLabels != null)
        {
            for (int i = 0; i < minLabels.Length; i++)
            {
                if (minLabels[i] != null)
                {
                    minLabels[i].transform.LookAt(cam.transform);
                    minLabels[i].transform.Rotate(0f, 180f, 0f);
                }
            }
        }

        // Billboard stats label
        if (statsLabel != null)
        {
            statsLabel.transform.LookAt(cam.transform);
            statsLabel.transform.Rotate(0f, 180f, 0f);
        }
    }

    // --- Public API (called by ShapeManager) ---
    public string GetParamLabel()
    {
        if (!dataLoaded) return "Loading...";
        return $"fw:{currentFw:F3}  Loss:{LossAtFw(currentFw):F3}";
    }

    public string GetDebugInfo()
    {
        if (!dataLoaded) return "Loading JSON...";
        float loss = LossAtFw(currentFw);
        float delta = threshold - currentFw;
        string status = thresholdCrossed ? "<color=orange>ABOVE THRESHOLD</color>" : "<color=green>below threshold</color>";
        return $"fw: {currentFw:F3}  Loss: {loss:F3}\n"
             + $"\u0394 to threshold: {delta:F3}\n"
             + $"Status: {status}\n"
             + (debugMessage != "" ? debugMessage : "");
    }
}
