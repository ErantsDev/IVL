using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Interfaz en las gafas. Sólo lo crea VRBootstrap en modo VR.
///
/// Los Canvas "Screen Space - Overlay" no se ven en VR; aquí se pasan a "World Space":
///  - HUD del nivel (objetivo, materiales, cronómetro): una pantalla fija en la pared, junto al
///    pizarrón, con un acomodo compacto para que nada se encime. El botón X la trae frente al
///    usuario a una distancia cómoda de lectura y otra vez X la regresa a la pared. Al terminar
///    el nivel (resultados) o en pausa viene sola.
///  - Cualquier otro Canvas (menús, minijuegos, resultados): se coloca frente al usuario, a la
///    altura de los ojos, cuando aparece; si el usuario se voltea mucho, lo alcanza.
///  - El contador de FPS se oculta (se encimaba con la lista de materiales).
/// Además dibuja el aviso de acción ("Gatillo: agarrar...") de ObjectGrabber.
/// </summary>
public class VRHud : MonoBehaviour
{
    [Header("Pantalla del HUD (pared)")]
    [Tooltip("Tamaño del lienzo del HUD en VR (px): compacto para que los paneles no se separen ni se encimen.")]
    public Vector2 hudCanvasSize = new Vector2(1120f, 760f);
    [Tooltip("Ancho de la pantalla en la pared (m).")]
    public float wallWidth = 1.7f;
    [Tooltip("Ancho de la pantalla al traerla con X (m).")]
    public float nearWidth = 1.1f;
    [Tooltip("Distancia de la pantalla al traerla con X (m).")]
    public float nearDistance = 1.2f;

    [Header("Otros paneles (menús, minijuegos)")]
    public float frontDistance = 1.4f;
    [Tooltip("Ancho de un Canvas de 1920 px frente al usuario (m).")]
    public float frontWidth = 1.7f;
    [Tooltip("Grados que hay que voltear para que el panel venga hacia el frente.")]
    public float recenterAngle = 55f;

    [Header("Aviso de acción")]
    public float promptDistance = 1.0f;

    public static VRHud Instance { get; private set; }

    /// <summary>Canvas convertidos (el láser los usa para apuntar a botones).</summary>
    public IReadOnlyList<Canvas> Panels => panels;

    enum Kind { Hud, Front }
    class Info { public Kind kind; public bool wasVisible; public float yaw; public Vector3 pos; }

    readonly List<Canvas> panels = new List<Canvas>();
    readonly Dictionary<Canvas, Info> info = new Dictionary<Canvas, Info>();
    float nextScan;
    bool hudNear, hudNearPlaced;
    bool hasWall;
    Vector3 wallPos;
    Quaternion wallRot;
    Vector3 nearPos;
    Quaternion nearRot;

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

        if (VRInput.HudTogglePressed()) { hudNear = !hudNear; hudNearPlaced = false; }

        Transform h = head.transform;
        var flow = GameFlow.Instance;
        bool forceNear = flow != null && (flow.CurrentState == GameFlow.State.Results || flow.CurrentState == GameFlow.State.Paused);

        for (int i = panels.Count - 1; i >= 0; i--)
        {
            var c = panels[i];
            if (c == null) { panels.RemoveAt(i); continue; }
            var inf = info[c];
            if (c.worldCamera != head.Cam) c.worldCamera = head.Cam;
            bool visible = c.isActiveAndEnabled;

            if (inf.kind == Kind.Hud)
            {
                bool near = hudNear || forceNear || !hasWall;
                if (near)
                {
                    if (!hudNearPlaced) { PlaceInFront(h, nearDistance, out nearPos, out nearRot); hudNearPlaced = true; }
                    SetPose(c, nearPos, nearRot, nearWidth / hudCanvasSize.x);
                }
                else
                {
                    hudNearPlaced = false;
                    SetPose(c, wallPos, wallRot, wallWidth / hudCanvasSize.x);
                }
            }
            else
            {
                float yaw = Yaw(h);
                if (visible && !inf.wasVisible) Recenter(inf, h, yaw);
                else if (visible && Mathf.Abs(Mathf.DeltaAngle(inf.yaw, yaw)) > recenterAngle) Recenter(inf, h, yaw);
                inf.wasVisible = visible;
                var rt = (RectTransform)c.transform;
                // Los de mayor sortingOrder quedan un poco más cerca para dibujarse encima.
                Vector3 p = inf.pos - Quaternion.Euler(0f, inf.yaw, 0f) * Vector3.forward * (Mathf.Clamp(c.sortingOrder, 0, 100) * 0.001f);
                SetPose(c, p, Quaternion.Euler(0f, inf.yaw, 0f), frontWidth / Mathf.Max(1f, rt.sizeDelta.x));
            }
        }

        UpdatePrompt(head);
    }

    static float Yaw(Transform h) => Quaternion.LookRotation(Vector3.ProjectOnPlane(h.forward, Vector3.up).sqrMagnitude > 1e-4f ? Vector3.ProjectOnPlane(h.forward, Vector3.up) : h.up).eulerAngles.y;

    void Recenter(Info inf, Transform h, float yaw)
    {
        inf.yaw = yaw;
        inf.pos = h.position + Quaternion.Euler(0f, yaw, 0f) * Vector3.forward * frontDistance;
    }

    static void PlaceInFront(Transform h, float dist, out Vector3 pos, out Quaternion rot)
    {
        rot = Quaternion.Euler(0f, Yaw(h), 0f);
        pos = h.position + rot * Vector3.forward * dist - Vector3.up * 0.1f;
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
            Convert(c, head, c == hudCanvas ? Kind.Hud : Kind.Front);
        }
    }

    void Convert(Canvas c, VRHeadPose head, Kind kind)
    {
        Vector2 size = new Vector2(1920f, 1080f);
        var scaler = c.GetComponent<CanvasScaler>();
        if (scaler != null)
        {
            if (scaler.uiScaleMode == CanvasScaler.ScaleMode.ScaleWithScreenSize) size = scaler.referenceResolution;
            scaler.enabled = false;
        }
        if (kind == Kind.Hud) size = hudCanvasSize;

        c.renderMode = RenderMode.WorldSpace;
        c.worldCamera = head.Cam;
        var rt = (RectTransform)c.transform;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = size;

        var inf = new Info { kind = kind };
        Recenter(inf, head.transform, Yaw(head.transform));
        info[c] = inf;
        panels.Add(c);

        if (kind == Kind.Hud) hasWall = FindWallPose(head.transform, out wallPos, out wallRot);
    }

    /// <summary>Lugar en la pared, a un lado del pizarrón, mirando hacia el salón.</summary>
    bool FindWallPose(Transform viewer, out Vector3 pos, out Quaternion rot)
    {
        pos = default; rot = Quaternion.identity;
        var board = GameObject.Find("Salon/Pizarron");
        if (board == null) board = GameObject.Find("Pizarron");
        if (board == null) return false;
        var rs = board.GetComponentsInChildren<Renderer>();
        if (rs.Length == 0) return false;
        Bounds b = rs[0].bounds;
        foreach (var r in rs) b.Encapsulate(r.bounds);

        // La normal del pizarrón es el eje horizontal más delgado; apunta hacia el salón.
        Vector3 n = b.size.x < b.size.z ? Vector3.right : Vector3.forward;
        if (Vector3.Dot(viewer.position - b.center, n) < 0f) n = -n;
        Vector3 side = Vector3.Cross(Vector3.up, n);
        float half = Mathf.Abs(Vector3.Dot(b.extents, new Vector3(Mathf.Abs(side.x), 0f, Mathf.Abs(side.z))));

        // Del lado con más pared libre.
        Vector3 origin = b.center + n * 0.08f;
        float right = FreeDistance(origin, side), left = FreeDistance(origin, -side);
        Vector3 dir = right >= left ? side : -side;
        float need = half + wallWidth * 0.5f + 0.15f;
        float avail = Mathf.Max(right, left) - wallWidth * 0.5f - 0.05f;
        pos = b.center + dir * Mathf.Min(need, Mathf.Max(half, avail)) + n * 0.03f;
        rot = Quaternion.LookRotation(-n, Vector3.up);
        return true;
    }

    static float FreeDistance(Vector3 origin, Vector3 dir) =>
        Physics.Raycast(origin, dir, out var hit, 8f, ~0, QueryTriggerInteraction.Ignore) ? hit.distance : 8f;

    // ------------------------------------------------------------------
    //  Aviso de acción
    // ------------------------------------------------------------------

    void BuildPrompt()
    {
        var go = new GameObject("VRAviso", typeof(RectTransform));
        go.layer = 5;
        go.transform.SetParent(transform, false);
        promptCanvas = go.AddComponent<Canvas>();
        promptCanvas.renderMode = RenderMode.WorldSpace;
        promptCanvas.sortingOrder = 200;
        var rt = (RectTransform)go.transform;
        rt.sizeDelta = new Vector2(1400f, 400f);
        rt.localScale = Vector3.one * (promptDistance * 0.0012f);

        var bgGO = new GameObject("Fondo", typeof(RectTransform));
        bgGO.layer = 5;
        bgGO.transform.SetParent(go.transform, false);
        promptBgRT = (RectTransform)bgGO.transform;
        promptBg = bgGO.AddComponent<Image>();
        promptBg.color = new Color(0f, 0f, 0f, 0.6f);
        promptBg.raycastTarget = false;

        var textGO = new GameObject("Texto", typeof(RectTransform));
        textGO.layer = 5;
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
        s = s.Replace("   ·   Clic derecho (mantener): rotar", "").Replace("<size=15>", "<size=80%>");
        s = System.Text.RegularExpressions.Regex.Replace(s, @"(?m)^(E / Clic|E):", "Gatillo:");
        return System.Text.RegularExpressions.Regex.Replace(s, @"(?m)^R:", "Botón A:");
    }
}
