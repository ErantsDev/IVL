using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Interfaz en las gafas. Sólo lo crea VRBootstrap en modo VR.
///
///  - Los Canvas "Screen Space - Overlay" (HUD, menús, minijuegos...) no se ven en VR. Aquí se
///    pasan a "World Space" y flotan frente al usuario, siguiendo la cabeza con un poco de
///    retraso (más cómodo para leer que pegados a la cara). Se revisa cada medio segundo para
///    tomar también los que los scripts crean después.
///  - Mira y aviso de acción ("Gatillo: agarrar...") a partir de ObjectGrabber, porque su
///    versión de PC se dibuja con OnGUI, que no aparece en las gafas.
/// </summary>
public class VRHud : MonoBehaviour
{
    [Header("Paneles")]
    [Tooltip("Distancia de los paneles a los ojos (m).")]
    public float hudDistance = 0.75f;
    [Tooltip("Ancho que ocupa un Canvas de 1920 px (m).")]
    public float hudWidth = 0.9f;
    [Tooltip("Qué tan rápido alcanzan los paneles a la cabeza.")]
    public float followSpeed = 6f;

    [Header("Mira")]
    public float reticleDistance = 0.6f;
    public Color reticleColor = Color.white;
    public Color reticleActiveColor = new Color(0.3f, 1f, 0.5f);

    readonly List<Canvas> panels = new List<Canvas>();
    float nextScan;
    Quaternion panelRot;
    bool hasRot;

    Canvas reticleCanvas;
    RectTransform dotRT, promptBgRT;
    Image dot, promptBg;
    TextMeshProUGUI promptText;

    void Awake() => BuildReticle();

    void LateUpdate()
    {
        var head = VRHeadPose.Current;
        if (head == null) { reticleCanvas.enabled = false; return; }

        if (Time.unscaledTime >= nextScan)
        {
            nextScan = Time.unscaledTime + 0.5f;
            Scan(head);
        }

        Transform h = head.transform;
        Quaternion target = h.rotation;
        panelRot = hasRot ? Quaternion.Slerp(panelRot, target, 1f - Mathf.Exp(-followSpeed * Time.unscaledDeltaTime)) : target;
        hasRot = true;

        for (int i = panels.Count - 1; i >= 0; i--)
        {
            var c = panels[i];
            if (c == null) { panels.RemoveAt(i); continue; }
            // Los de mayor sortingOrder quedan un poco más cerca para dibujarse encima.
            float z = hudDistance - Mathf.Clamp(c.sortingOrder, 0, 100) * 0.001f;
            c.transform.SetPositionAndRotation(h.position + panelRot * new Vector3(0f, 0f, z), panelRot);
            if (c.worldCamera != head.Cam) c.worldCamera = head.Cam;
        }

        UpdateReticle(head);
    }

    // ------------------------------------------------------------------
    //  Paneles
    // ------------------------------------------------------------------

    void Scan(VRHeadPose head)
    {
        foreach (var c in FindObjectsByType<Canvas>(FindObjectsSortMode.None))
        {
            if (c == reticleCanvas || !c.isRootCanvas || c.renderMode != RenderMode.ScreenSpaceOverlay) continue;
            Convert(c, head);
        }
    }

    void Convert(Canvas c, VRHeadPose head)
    {
        Vector2 size = new Vector2(1920f, 1080f);
        var scaler = c.GetComponent<CanvasScaler>();
        if (scaler != null)
        {
            if (scaler.uiScaleMode == CanvasScaler.ScaleMode.ScaleWithScreenSize) size = scaler.referenceResolution;
            scaler.enabled = false;
        }

        c.renderMode = RenderMode.WorldSpace;
        c.worldCamera = head.Cam;
        var rt = (RectTransform)c.transform;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = size;
        float parentScale = rt.parent != null ? Mathf.Max(0.0001f, rt.parent.lossyScale.x) : 1f;
        rt.localScale = Vector3.one * (hudWidth / size.x / parentScale);
        panels.Add(c);
    }

    // ------------------------------------------------------------------
    //  Mira y aviso
    // ------------------------------------------------------------------

    void BuildReticle()
    {
        var go = new GameObject("VRMira", typeof(RectTransform));
        go.layer = 5;
        go.transform.SetParent(transform, false);
        reticleCanvas = go.AddComponent<Canvas>();
        reticleCanvas.renderMode = RenderMode.WorldSpace;
        reticleCanvas.sortingOrder = 200;
        var rt = (RectTransform)go.transform;
        rt.sizeDelta = new Vector2(1400f, 600f);
        rt.localScale = Vector3.one * (reticleDistance * 0.001f); // 1 unidad ≈ 1 mm visto a 1 m

        dot = NewImage(go.transform, "Punto", out dotRT);
        dotRT.sizeDelta = new Vector2(10f, 10f);
        var outline = dot.gameObject.AddComponent<Outline>();
        outline.effectColor = new Color(0f, 0f, 0f, 0.8f);
        outline.effectDistance = new Vector2(2f, -2f);

        promptBg = NewImage(go.transform, "FondoAviso", out promptBgRT);
        promptBg.color = new Color(0f, 0f, 0f, 0.55f);
        promptBgRT.anchoredPosition = new Vector2(0f, -70f);

        var textGO = new GameObject("Aviso", typeof(RectTransform));
        textGO.layer = 5;
        textGO.transform.SetParent(promptBgRT, false);
        var trt = (RectTransform)textGO.transform;
        trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one;
        trt.offsetMin = new Vector2(14f, 6f); trt.offsetMax = new Vector2(-14f, -6f);
        promptText = textGO.AddComponent<TextMeshProUGUI>();
        promptText.fontSize = 28f;
        promptText.alignment = TextAlignmentOptions.Center;
        promptText.richText = true;
        promptText.raycastTarget = false;
    }

    static Image NewImage(Transform parent, string name, out RectTransform rt)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.layer = 5;
        go.transform.SetParent(parent, false);
        rt = (RectTransform)go.transform;
        var img = go.AddComponent<Image>();
        img.raycastTarget = false;
        return img;
    }

    void UpdateReticle(VRHeadPose head)
    {
        var g = ObjectGrabber.Instance;
        bool show = GameFlow.GameplayInputAllowed && g != null && g.isActiveAndEnabled && g.Cam == head.Cam;
        reticleCanvas.enabled = show;
        if (!show) return;

        Transform h = head.transform;
        reticleCanvas.transform.SetPositionAndRotation(h.position + h.forward * reticleDistance, h.rotation);
        reticleCanvas.worldCamera = head.Cam;

        bool active = g.Aimed != null || g.AimedInteractable != null;
        dot.color = active ? reticleActiveColor : reticleColor;
        dotRT.sizeDelta = Vector2.one * (active ? 14f : 10f);

        string p = ToVRLabels(g.BuildPrompt());
        bool hasPrompt = !string.IsNullOrEmpty(p);
        promptBg.enabled = hasPrompt;
        promptText.enabled = hasPrompt;
        if (!hasPrompt) return;
        if (promptText.text != p) promptText.text = p;
        Vector2 pref = promptText.GetPreferredValues(p, 1200f, 0f);
        promptBgRT.sizeDelta = new Vector2(Mathf.Min(pref.x, 1200f) + 28f, pref.y + 12f);
    }

    /// <summary>Cambia los nombres de teclas de los avisos de PC por los botones del control.</summary>
    public static string ToVRLabels(string s)
    {
        if (string.IsNullOrEmpty(s)) return s;
        s = s.Replace("Clic derecho (mantener): rotar", "Grip + girar el control: rotar").Replace("<size=15>", "<size=80%>");
        s = System.Text.RegularExpressions.Regex.Replace(s, @"(?m)^(E / Clic|E):", "Gatillo:");
        return System.Text.RegularExpressions.Regex.Replace(s, @"(?m)^R:", "Botón A:");
    }
}
