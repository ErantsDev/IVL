using System.Collections.Generic;
using System.Text.RegularExpressions;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Interfaz en las gafas. Sólo lo crea VRBootstrap en modo VR.
///
/// Los Canvas "Screen Space - Overlay" no se ven en VR; aquí se pasan a "World Space", en la
/// capa UI, que dibuja una cámara aparte encima de todo (ninguna pared o mueble los tapa).
///  - Todos siguen la vista con un pequeño retraso, a una distancia cómoda de lectura.
///  - HUD del nivel (objetivo, materiales, cronómetro, calma del conejo): el botón X lo manda a la
///    pared, a un lado del pizarrón (sin taparlo), y otra vez X lo trae a la vista. En la pared se
///    dibuja con la cámara normal, como un objeto más del salón.
///  - Los textos que hablan de teclado y mouse se cambian por los botones del control.
///  - El contador de FPS se oculta (se encimaba con la lista de materiales).
/// Además dibuja el aviso de acción ("Gatillo: agarrar...") de ObjectGrabber.
/// </summary>
public class VRHud : MonoBehaviour
{
    public const int UILayer = 5;

    [Header("Paneles")]
    [Tooltip("Distancia de los paneles a los ojos (m).")]
    public float distance = 1.2f;
    [Tooltip("Ancho del HUD del nivel (m).")]
    public float hudWidth = 0.95f;
    [Tooltip("Tamaño del lienzo del HUD en VR (px): compacto para que nada se separe ni se encime.")]
    public Vector2 hudCanvasSize = new Vector2(1120f, 760f);
    [Tooltip("Ancho de un panel de pantalla completa (menús, minijuegos) (m).")]
    public float frontWidth = 1.3f;
    [Tooltip("Qué tan rápido alcanzan los paneles a la vista.")]
    public float followSpeed = 4f;
    [Tooltip("Los paneles bajan un poco respecto a la línea de la vista (grados).")]
    public float pitchDown = 6f;
    [Tooltip("Cuánto bajan / suben como máximo al inclinar la cabeza (grados).")]
    public float maxPitchDown = 15f;
    public float maxPitchUp = 20f;

    [Header("Aviso de acción")]
    public float promptDistance = 1.0f;

    public static VRHud Instance { get; private set; }

    /// <summary>Canvas convertidos (el láser los usa para apuntar a botones).</summary>
    public IReadOnlyList<Canvas> Panels => panels;
    /// <summary>¿El HUD del nivel sigue la vista? (false = está en la pared; botón X).</summary>
    public bool HudVisible { get; private set; } = true;

    [Header("HUD en la pared")]
    [Tooltip("Ancho máximo de la pantalla en la pared (m).")]
    public float wallMaxWidth = 1.1f;
    [Tooltip("Separación entre el pizarrón y la pantalla (m).")]
    public float wallGap = 0.12f;
    bool hasWall;
    Vector3 wallPos;
    Quaternion wallRot;
    float wallWidth;

    readonly List<Canvas> panels = new List<Canvas>();
    readonly HashSet<Canvas> hudPanels = new HashSet<Canvas>();
    readonly List<TMP_Text> texts = new List<TMP_Text>();
    readonly Dictionary<TMP_Text, string> translated = new Dictionary<TMP_Text, string>();
    float nextScan;
    Quaternion viewRot;
    bool hasView;

    Canvas promptCanvas;
    RectTransform promptBgRT;
    Image promptBg;
    TextMeshProUGUI promptText;

    void Awake()
    {
        Instance = this;
        BuildPrompt();
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    void LateUpdate()
    {
        var head = VRHeadPose.Current;
        if (head == null) { promptCanvas.enabled = false; return; }

        if (Time.unscaledTime >= nextScan)
        {
            nextScan = Time.unscaledTime + 0.5f;
            Scan(head);
        }

        if (VRInput.HudTogglePressed()) HudVisible = !HudVisible;

        // Orientación de la vista, suavizada: los paneles la siguen sin pegarse a la cara.
        // Siguen el giro completo, pero la inclinación sólo un poco: al mirar hacia abajo (la charola,
        // el conejo) el panel no baja a taparlo.
        Transform h = head.transform;
        Vector3 e = Quaternion.LookRotation(h.forward, Vector3.up).eulerAngles;
        float pitch = Mathf.Clamp(Mathf.DeltaAngle(0f, e.x), -maxPitchUp, maxPitchDown);
        Quaternion target = Quaternion.Euler(pitch + pitchDown, e.y, 0f);
        viewRot = hasView ? Quaternion.Slerp(viewRot, target, 1f - Mathf.Exp(-followSpeed * Time.unscaledDeltaTime)) : target;
        hasView = true;

        for (int i = panels.Count - 1; i >= 0; i--)
        {
            var c = panels[i];
            if (c == null) { panels.RemoveAt(i); continue; }
            if (c.worldCamera != head.Cam) c.worldCamera = head.Cam;
            bool hud = hudPanels.Contains(c);
            var rt = (RectTransform)c.transform;
            if (hud && OnWall)
            {
                SetLayer(c, 0); // en la pared: con la cámara normal (lo tapan los objetos que estén enfrente)
                SetPose(c, wallPos, wallRot, wallWidth / Mathf.Max(1f, rt.sizeDelta.x));
                continue;
            }
            SetLayer(c, UILayer);
            float width = hud ? hudWidth : frontWidth;
            // Los de mayor sortingOrder quedan un poco más cerca para dibujarse encima.
            float d = distance - Mathf.Clamp(c.sortingOrder, 0, 100) * 0.002f;
            SetPose(c, h.position + viewRot * Vector3.forward * d, viewRot, width / Mathf.Max(1f, rt.sizeDelta.x));
        }

        TranslateTexts();
        UpdatePrompt(head);
    }

    /// <summary>true si el HUD está en la pared (oculto con X, hay pizarrón y no hay pausa ni resultados).</summary>
    public bool OnWall => !HudVisible && hasWall && !ForceHud();

    readonly Dictionary<Canvas, int> layerOf = new Dictionary<Canvas, int>();

    void SetLayer(Canvas c, int layer)
    {
        if (layerOf.TryGetValue(c, out int cur) && cur == layer) return;
        foreach (var t in c.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = layer;
        layerOf[c] = layer;
    }

    /// <summary>En pausa o resultados el HUD viene a la vista aunque se haya mandado a la pared.</summary>
    static bool ForceHud()
    {
        var flow = GameFlow.Instance;
        return flow != null && (flow.CurrentState == GameFlow.State.Results || flow.CurrentState == GameFlow.State.Paused);
    }

    static void SetPose(Canvas c, Vector3 pos, Quaternion rot, float worldScale)
    {
        var t = c.transform;
        t.SetPositionAndRotation(pos, rot);
        float parentScale = t.parent != null ? Mathf.Max(0.0001f, t.parent.lossyScale.x) : 1f;
        t.localScale = Vector3.one * (worldScale / parentScale);
    }

    // ------------------------------------------------------------------
    //  Conversión de Canvas
    // ------------------------------------------------------------------

    void Scan(VRHeadPose head)
    {
        var hudCanvas = GameFlow.Instance != null ? GameFlow.Instance.hudCanvas : null;
        foreach (var c in FindObjectsByType<Canvas>())
        {
            if (c == promptCanvas || !c.isRootCanvas || c.renderMode != RenderMode.ScreenSpaceOverlay) continue;
            if (c.GetComponentInParent<FpsCounter>() != null) { c.enabled = false; continue; }
            Convert(c, head, c == hudCanvas);
        }

        // Capa (UI = encima de todo; 0 = HUD en la pared) y textos a traducir.
        texts.Clear();
        foreach (var c in panels)
        {
            if (c == null) continue;
            layerOf.Remove(c); // por si el juego agregó elementos nuevos: se vuelve a aplicar la capa
            SetLayer(c, hudPanels.Contains(c) && OnWall ? 0 : UILayer);
            c.GetComponentsInChildren(true, tmpBuffer);
            texts.AddRange(tmpBuffer);
        }
        if (!hasWall && hudPanels.Count > 0) hasWall = FindWallPose(out wallPos, out wallRot, out wallWidth);
    }

    readonly List<TMP_Text> tmpBuffer = new List<TMP_Text>();

    void Convert(Canvas c, VRHeadPose head, bool isHud)
    {
        Vector2 size = new Vector2(1920f, 1080f);
        var scaler = c.GetComponent<CanvasScaler>();
        if (scaler != null)
        {
            if (scaler.uiScaleMode == CanvasScaler.ScaleMode.ScaleWithScreenSize) size = scaler.referenceResolution;
            scaler.enabled = false;
        }
        if (isHud) size = hudCanvasSize;

        c.renderMode = RenderMode.WorldSpace;
        c.worldCamera = head.Cam;
        var rt = (RectTransform)c.transform;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = size;
        panels.Add(c);
        if (isHud) hudPanels.Add(c);
    }

    /// <summary>
    /// Lugar en la pared, a un lado del pizarrón y sin taparlo: del lado con más pared libre, con el
    /// ancho que quepa. Si no cabe a ningún lado, arriba del pizarrón.
    /// </summary>
    bool FindWallPose(out Vector3 pos, out Quaternion rot, out float width)
    {
        pos = default; rot = Quaternion.identity; width = wallMaxWidth;
        var board = GameObject.Find("Salon/Pizarron");
        if (board == null) board = GameObject.Find("Pizarron");
        if (board == null) return false;
        var surface = board.transform.Find("Superficie");
        Transform bt = board.transform;
        Vector3 front = -bt.forward;                          // la cara del pizarrón mira hacia su -Z local
        float half = (surface != null ? surface.lossyScale.x : 2f) * 0.5f + 0.04f; // con marco
        float centerY = surface != null ? surface.position.y : bt.position.y + 1.7f;
        Vector3 center = new Vector3(bt.position.x, centerY, bt.position.z) + front * 0.012f;
        float aspect = hudCanvasSize.y / Mathf.Max(1f, hudCanvasSize.x);

        // Pared libre a cada lado (rayos a la altura del pizarrón, pegados a la pared).
        Vector3 probe = center + front * 0.05f;
        float freeR = Free(probe + bt.right * half, bt.right);
        float freeL = Free(probe - bt.right * half, -bt.right);
        Vector3 dir = freeR >= freeL ? bt.right : -bt.right;
        float room = Mathf.Max(freeR, freeL) - wallGap - 0.05f;
        rot = Quaternion.LookRotation(-front, Vector3.up);
        if (room >= 0.6f)
        {
            width = Mathf.Min(wallMaxWidth, room);
            pos = center + dir * (half + wallGap + width * 0.5f);
        }
        else
        {
            width = 0.8f;
            float top = surface != null ? surface.position.y + surface.lossyScale.y * 0.5f + 0.04f : centerY + 0.6f;
            pos = new Vector3(center.x, top + wallGap + width * aspect * 0.5f, center.z);
        }
        return true;
    }

    static float Free(Vector3 origin, Vector3 dir) =>
        Physics.Raycast(origin, dir, out var hit, 6f, ~(1 << UILayer), QueryTriggerInteraction.Ignore) ? hit.distance : 6f;

    // ------------------------------------------------------------------
    //  Textos: teclado y mouse -> botones del control
    // ------------------------------------------------------------------

    static readonly (Regex re, string to)[] Rules =
    {
        (new Regex(@"mantén (el )?clic derecho y mueve el mouse de lado a lado", RegexOptions.IgnoreCase), "mantén el grip derecho y mueve la mano de lado a lado"),
        (new Regex(@"sigue moviendo el mouse", RegexOptions.IgnoreCase), "sigue moviendo la mano"),
        (new Regex(@"suelta el clic derecho", RegexOptions.IgnoreCase), "suelta el grip"),
        (new Regex(@"Clic derecho \+ mover el mouse", RegexOptions.IgnoreCase), "Grip + mover la mano"),
        (new Regex(@"Clic derecho \+ mover", RegexOptions.IgnoreCase), "Grip + girar la mano"),
        (new Regex(@"(mant[eé]n|con) el clic izquierdo", RegexOptions.IgnoreCase), "$1 el gatillo"),
        (new Regex(@"\(clic para seleccionar\)", RegexOptions.IgnoreCase), "(gatillo para seleccionar)"),
        (new Regex(@"Pasa el mouse sobre", RegexOptions.IgnoreCase), "Apunta con el láser a"),
        (new Regex(@"presiona (<b>)?E(</b>)?(?![\w])"), "presiona $1el gatillo$2"),
        (new Regex(@"presiona (<b>)?Enter(</b>)?"), "presiona $1A$2"),
        (new Regex(@"\bclic derecho\b", RegexOptions.IgnoreCase), "grip"),
        (new Regex(@"\bel mouse\b", RegexOptions.IgnoreCase), "la mano"),
    };

    public static string Translate(string s)
    {
        if (string.IsNullOrEmpty(s)) return s;
        foreach (var (re, to) in Rules) s = re.Replace(s, to);
        return s;
    }

    void TranslateTexts()
    {
        foreach (var t in texts)
        {
            if (t == null || !t.isActiveAndEnabled) continue;
            string cur = t.text;
            if (translated.TryGetValue(t, out var last) && last == cur) continue;
            string tr = Translate(cur);
            if (tr != cur) t.text = tr;
            translated[t] = tr;
        }
    }

    // ------------------------------------------------------------------
    //  Aviso de acción
    // ------------------------------------------------------------------

    void BuildPrompt()
    {
        var go = new GameObject("VRAviso", typeof(RectTransform));
        go.layer = UILayer;
        go.transform.SetParent(transform, false);
        promptCanvas = go.AddComponent<Canvas>();
        promptCanvas.renderMode = RenderMode.WorldSpace;
        promptCanvas.sortingOrder = 200;
        var rt = (RectTransform)go.transform;
        rt.sizeDelta = new Vector2(1400f, 400f);
        rt.localScale = Vector3.one * (promptDistance * 0.0012f);

        var bgGO = new GameObject("Fondo", typeof(RectTransform));
        bgGO.layer = UILayer;
        bgGO.transform.SetParent(go.transform, false);
        promptBgRT = (RectTransform)bgGO.transform;
        promptBg = bgGO.AddComponent<Image>();
        promptBg.color = new Color(0f, 0f, 0f, 0.6f);
        promptBg.raycastTarget = false;

        var textGO = new GameObject("Texto", typeof(RectTransform));
        textGO.layer = UILayer;
        textGO.transform.SetParent(promptBgRT, false);
        var trt = (RectTransform)textGO.transform;
        trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one;
        trt.offsetMin = new Vector2(16f, 8f); trt.offsetMax = new Vector2(-16f, -8f);
        promptText = textGO.AddComponent<TextMeshProUGUI>();
        promptText.fontSize = 30f;
        promptText.alignment = TextAlignmentOptions.Center;
        promptText.richText = true;
        promptText.raycastTarget = false;
    }

    void UpdatePrompt(VRHeadPose head)
    {
        var g = ObjectGrabber.Instance;
        string p = GameFlow.GameplayInputAllowed && g != null && g.isActiveAndEnabled && g.Cam == head.Cam
            ? ToVRLabels(g.BuildPrompt()) : null;
        bool show = !string.IsNullOrEmpty(p);
        promptCanvas.enabled = show;
        if (!show) return;

        // Un poco abajo del centro de la vista, siempre de frente.
        Transform h = head.transform;
        Quaternion rot = h.rotation * Quaternion.Euler(14f, 0f, 0f);
        promptCanvas.transform.SetPositionAndRotation(h.position + rot * Vector3.forward * promptDistance, rot);
        promptCanvas.worldCamera = head.Cam;

        if (promptText.text != p) promptText.text = p;
        Vector2 pref = promptText.GetPreferredValues(p, 1300f, 0f);
        promptBgRT.sizeDelta = new Vector2(Mathf.Min(pref.x, 1300f) + 32f, pref.y + 16f);
    }

    /// <summary>Cambia los nombres de teclas de los avisos de PC por los botones del control.</summary>
    public static string ToVRLabels(string s)
    {
        if (string.IsNullOrEmpty(s)) return s;
        // En VR el objeto gira con la mano: no hace falta la indicación de rotar.
        s = s.Replace("   ·   Clic derecho (mantener): rotar", "").Replace("<size=14>", "<size=80%>").Replace("<size=15>", "<size=80%>");
        s = Regex.Replace(s, @"(?m)^(E / Clic|E):", "Gatillo:");
        s = Regex.Replace(s, @"(?m)^R:", "Botón A:");
        s = Regex.Replace(s, @"y presiona E\.?", "y presiona el gatillo.");
        s = Regex.Replace(s, @"(?m)^(Gatillo: soltar [^\n]*)$", "$1   ·   Joystick ↑↓: acercar / alejar");
        return Translate(s);
    }
}
