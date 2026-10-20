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
///  - HUD del nivel (objetivo, materiales, cronómetro, calma del conejo): sigue la vista; el botón X
///    lo oculta y lo vuelve a mostrar.
///  - Paneles de minijuegos y menús: se quedan fijos frente al usuario (a la altura de los ojos,
///    un poco arriba para no tapar la charola o el conejo) y vuelven al frente si se voltea mucho.
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
    public float hudWidth = 1.1f;
    [Tooltip("Tamaño del lienzo del HUD en VR (px): compacto para que nada se separe ni se encime.")]
    public Vector2 hudCanvasSize = new Vector2(1120f, 760f);
    [Header("Paneles de minijuegos y menús")]
    [Tooltip("Lienzo de estos paneles en VR (px): más chico que 1920×1080 para que la letra se vea más grande.")]
    public Vector2 frontCanvasSize = new Vector2(1440f, 810f);
    [Tooltip("Ancho del panel (m).")]
    public float frontWidth = 1.15f;
    [Tooltip("Distancia del panel a los ojos (m).")]
    public float frontDistance = 1.0f;
    [Tooltip("Qué tanto arriba de los ojos queda el centro del panel (m): deja libre la charola / el conejo.")]
    public float frontRaise = 0.1f;
    [Tooltip("Grados que hay que voltear para que el panel vuelva al frente.")]
    public float frontRecenterAngle = 70f;

    [Header("Menú principal (lobby)")]
    [Tooltip("El menú conserva su lienzo de 1920×1080 y se centra el panel principal.")]
    public float menuWidth = 1.65f;
    public float menuDistance = 1.5f;
    [Tooltip("Centro del menú respecto a los ojos (m): un poco abajo, cómodo para leer.")]
    public float menuRaise = -0.05f;
    Canvas menuCanvas;

    class FrontInfo { public bool wasVisible; public float yaw; public Vector3 pos; }
    readonly Dictionary<Canvas, FrontInfo> front = new Dictionary<Canvas, FrontInfo>();

    static bool AnyActiveChild(Canvas c)
    {
        var t = c.transform;
        for (int i = 0; i < t.childCount; i++) if (t.GetChild(i).gameObject.activeSelf) return true;
        return false;
    }
    [Tooltip("Qué tan rápido alcanzan los paneles a la vista.")]
    public float followSpeed = 4f;
    [Tooltip("Los paneles bajan un poco respecto a la línea de la vista (grados).")]
    public float pitchDown = 6f;
    [Tooltip("Cuánto bajan / suben como máximo al inclinar la cabeza (grados).")]
    public float maxPitchDown = 15f;
    public float maxPitchUp = 20f;

    [Header("Aviso de acción")]
    public float promptDistance = 1.0f;

    [Header("Mensajes (toast)")]
    [Tooltip("Tamaño de letra de los mensajes que salen al realizar acciones.")]
    public float toastFontSize = 34f;
    [Tooltip("Grados arriba del centro de la vista: así no se enciman con el aviso de acción (abajo).")]
    public float toastUp = 9f;

    public static VRHud Instance { get; private set; }

    /// <summary>Canvas convertidos (el láser los usa para apuntar a botones).</summary>
    public IReadOnlyList<Canvas> Panels => panels;
    /// <summary>¿El HUD del nivel está visible? (botón X).</summary>
    public bool HudVisible { get; private set; } = true;

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
        BuildToast();
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
            nextScan = Time.unscaledTime + 1f;
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
            if (hud)
            {
                // X oculta sólo el objetivo y la lista de materiales (también durante los minijuegos);
                // avisos, medidor de calma del conejo, temporizadores y barras de acción siguen visibles.
                bool inMinigame = GameFlow.Instance != null && GameFlow.Instance.InMinigame;
                SetInfoHidden(c, (!HudVisible || inMinigame) && !ForceHud());
                float d = distance - Mathf.Clamp(c.sortingOrder, 0, 100) * 0.002f;
                SetPose(c, h.position + viewRot * Vector3.forward * d, viewRot, hudWidth / Mathf.Max(1f, rt.sizeDelta.x));
            }
            else
            {
                var inf = front[c];
                bool menu = c == menuCanvas;
                bool visible = c.isActiveAndEnabled && AnyActiveChild(c);
                float yaw = Quaternion.LookRotation(Vector3.ProjectOnPlane(h.forward, Vector3.up).sqrMagnitude > 1e-4f ? Vector3.ProjectOnPlane(h.forward, Vector3.up) : h.up).eulerAngles.y;
                if (visible && (!inf.wasVisible || Mathf.Abs(Mathf.DeltaAngle(inf.yaw, yaw)) > frontRecenterAngle))
                {
                    inf.yaw = yaw;
                    inf.pos = menu
                        ? h.position + Quaternion.Euler(0f, yaw, 0f) * Vector3.forward * menuDistance + Vector3.up * menuRaise
                        : h.position + Quaternion.Euler(0f, yaw, 0f) * Vector3.forward * frontDistance + Vector3.up * frontRaise;
                }
                inf.wasVisible = visible;
                Vector3 p = inf.pos - Quaternion.Euler(0f, inf.yaw, 0f) * Vector3.forward * (Mathf.Clamp(c.sortingOrder, 0, 100) * 0.002f);
                SetPose(c, p, Quaternion.Euler(0f, inf.yaw, 0f), (menu ? menuWidth : frontWidth) / Mathf.Max(1f, rt.sizeDelta.x));
            }
        }

        TranslateTexts();
        UpdatePrompt(head);
        UpdateToast(head);
    }

    /// <summary>Partes del HUD que oculta el botón X (información de consulta, no avisos).</summary>
    static readonly HashSet<string> InfoParts = new HashSet<string>
        { "HudFondo", "ModeText", "AlumnoText", "CounterText", "ObjectivePanel", "ChecklistPanel", "TimerText" };
    readonly Dictionary<Transform, Vector3> infoScale = new Dictionary<Transform, Vector3>();
    bool infoHidden;

    void SetInfoHidden(Canvas c, bool hide)
    {
        if (hide == infoHidden && infoScale.Count > 0) return;
        infoHidden = hide;
        var t = c.transform;
        for (int i = 0; i < t.childCount; i++)
        {
            var ch = t.GetChild(i);
            if (!InfoParts.Contains(ch.name)) continue;
            if (!infoScale.ContainsKey(ch)) infoScale[ch] = ch.localScale;
            ch.localScale = hide ? Vector3.zero : infoScale[ch];
        }
    }

    /// <summary>En pausa o resultados el HUD se muestra aunque se haya ocultado con X.</summary>
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
            if (IsMenuCanvas(c)) { menuCanvas = c; LayoutMenu(c); }
        }

        // Encima de todo (material sin prueba de profundidad) y textos a traducir.
        texts.Clear();
        foreach (var c in panels)
        {
            if (c == null) continue;
            VROnTop.Apply(c.transform);
            SolidBackgrounds(c.transform);
            c.GetComponentsInChildren(true, tmpBuffer);
            texts.AddRange(tmpBuffer);
            // Mensajes de acción del nivel: se muestran en un panel propio, grande y con fondo.
            foreach (var t in tmpBuffer)
                if (t != null && t.name == "ToastText" && !sources.Contains(t))
                {
                    sources.Add(t);
                    t.transform.localScale = Vector3.zero; // el original no se ve en VR
                }
        }
        sources.RemoveAll(t => t == null);
    }

    // ------------------------------------------------------------------
    //  Menú principal: un panel centrado, como un lobby
    // ------------------------------------------------------------------

    static bool IsMenuCanvas(Canvas c)
    {
        var flow = GameFlow.Instance;
        if (flow == null || flow.menuScreen == null) return false;
        var mc = flow.menuScreen.GetComponentInParent<Canvas>(true);
        return mc != null && mc.rootCanvas == c;
    }

    /// <summary>
    /// En PC el menú es una barra a la izquierda sobre el laboratorio desenfocado. En VR se centra
    /// el panel principal y se quita el oscurecido de fondo (atrás está el lobby).
    /// </summary>
    static void LayoutMenu(Canvas c)
    {
        foreach (var rt in c.GetComponentsInChildren<RectTransform>(true))
        {
            if (rt.name == "PanelPrincipal" && rt.parent != null && rt.parent.name == "PantallaMenu")
            {
                rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
                rt.pivot = new Vector2(0.5f, 0.5f);
                rt.sizeDelta = new Vector2(rt.sizeDelta.x, 940f);
                rt.anchoredPosition = Vector2.zero;
            }
            else if (rt.name == "Oscurecer" || (rt.name == "PanelOpciones" && rt.parent == c.transform))
            {
                var img = rt.GetComponent<Image>();
                if (img != null) { var col = img.color; col.a = 0f; img.color = col; }
            }
        }
    }

    // ------------------------------------------------------------------
    //  Fondos más opacos
    // ------------------------------------------------------------------

    public const float SolidAlpha = 0.92f;
    static readonly HashSet<Image> solidDone = new HashSet<Image>();
    static readonly List<Image> imgBuffer = new List<Image>();

    /// <summary>
    /// Los fondos oscuros semitransparentes se hacen casi opacos: en las gafas, con el laboratorio
    /// blanco detrás, el texto blanco no se distinguía.
    /// </summary>
    public static void SolidBackgrounds(Transform root)
    {
        root.GetComponentsInChildren(true, imgBuffer);
        foreach (var img in imgBuffer)
        {
            if (img == null || solidDone.Contains(img)) continue;
            solidDone.Add(img);
            // Los oscurecedores de pantalla completa no: serían un rectángulo negro enorme.
            var irt = img.rectTransform;
            if (img.sprite == null && irt.anchorMin == Vector2.zero && irt.anchorMax == Vector2.one) continue;
            var col = img.color;
            float bright = Mathf.Max(col.r, Mathf.Max(col.g, col.b));
            if (bright < 0.35f && col.a >= 0.15f && col.a < SolidAlpha)
            {
                col.a = SolidAlpha;
                img.color = col;
            }
        }
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
        size = isHud ? hudCanvasSize : IsMenuCanvas(c) ? new Vector2(1920f, 1080f) : frontCanvasSize;

        c.renderMode = RenderMode.WorldSpace;
        c.worldCamera = head.Cam;
        var rt = (RectTransform)c.transform;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = size;
        panels.Add(c);
        if (isHud) hudPanels.Add(c);
        else front[c] = new FrontInfo();
    }

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
        (new Regex(@"Tab: siguiente campo(\s*·\s*)Enter: (\w+)"), "Gatillo: elegir campo$1A: $2"),
        (new Regex(@"Esc: regresar"), "B: regresar"),
        (new Regex(@"Arrastra con el mouse para girarlo", RegexOptions.IgnoreCase), "Apunta, mantén el gatillo y mueve el láser para girarlo"),
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
        promptBg.color = new Color(0f, 0f, 0f, 0.85f);
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
        VROnTop.Apply(go.transform);
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

    // ------------------------------------------------------------------
    //  Mensajes al realizar acciones (ToastText de cada nivel)
    // ------------------------------------------------------------------

    readonly List<TMP_Text> sources = new List<TMP_Text>();
    Canvas toastCanvas;
    CanvasGroup toastGroup;
    RectTransform toastBgRT;
    TextMeshProUGUI toastText;

    void BuildToast()
    {
        var go = new GameObject("VRMensaje", typeof(RectTransform));
        go.layer = UILayer;
        go.transform.SetParent(transform, false);
        toastCanvas = go.AddComponent<Canvas>();
        toastCanvas.renderMode = RenderMode.WorldSpace;
        toastCanvas.sortingOrder = 210;
        toastGroup = go.AddComponent<CanvasGroup>();
        toastGroup.blocksRaycasts = false;
        toastGroup.interactable = false;
        var rt = (RectTransform)go.transform;
        rt.sizeDelta = new Vector2(1400f, 500f);
        rt.localScale = Vector3.one * (promptDistance * 0.0012f);

        var bgGO = new GameObject("Fondo", typeof(RectTransform));
        bgGO.layer = UILayer;
        bgGO.transform.SetParent(go.transform, false);
        toastBgRT = (RectTransform)bgGO.transform;
        var bg = bgGO.AddComponent<Image>();
        bg.color = new Color(0.05f, 0.07f, 0.1f, SolidAlpha);
        bg.raycastTarget = false;
        var outline = bgGO.AddComponent<Outline>();
        outline.effectColor = new Color(1f, 1f, 1f, 0.5f);
        outline.effectDistance = new Vector2(3f, -3f);

        var textGO = new GameObject("Texto", typeof(RectTransform));
        textGO.layer = UILayer;
        textGO.transform.SetParent(toastBgRT, false);
        var trt = (RectTransform)textGO.transform;
        trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one;
        trt.offsetMin = new Vector2(24f, 12f); trt.offsetMax = new Vector2(-24f, -12f);
        toastText = textGO.AddComponent<TextMeshProUGUI>();
        toastText.fontSize = toastFontSize;
        toastText.alignment = TextAlignmentOptions.Center;
        toastText.richText = true;
        toastText.color = Color.white;
        toastText.raycastTarget = false;
        VROnTop.Apply(go.transform);
        toastCanvas.enabled = false;
    }

    void UpdateToast(VRHeadPose head)
    {
        // El mensaje visible más reciente (el nivel lo muestra subiendo el alpha de su CanvasGroup).
        TMP_Text src = null;
        float alpha = 0f;
        foreach (var t in sources)
        {
            if (t == null || !t.gameObject.activeInHierarchy || string.IsNullOrEmpty(t.text)) continue;
            var g = t.GetComponentInParent<CanvasGroup>();
            float a = g != null ? g.alpha : 1f;
            if (a > alpha) { alpha = a; src = t; }
        }
        bool show = src != null && alpha > 0.02f;
        toastCanvas.enabled = show;
        if (!show) return;

        // Arriba del centro de la vista, siempre de frente: no se encima con el aviso de acción.
        Transform h = head.transform;
        Quaternion rot = h.rotation * Quaternion.Euler(-toastUp, 0f, 0f);
        toastCanvas.transform.SetPositionAndRotation(h.position + rot * Vector3.forward * (promptDistance - 0.02f), rot);
        toastCanvas.worldCamera = head.Cam;
        toastGroup.alpha = alpha;

        string msg = Translate(src.text);
        if (toastText.text != msg)
        {
            toastText.text = msg;
            toastText.fontSize = toastFontSize;
            Vector2 pref = toastText.GetPreferredValues(msg, 1250f, 0f);
            toastBgRT.sizeDelta = new Vector2(Mathf.Min(pref.x, 1250f) + 48f, pref.y + 24f);
        }
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
