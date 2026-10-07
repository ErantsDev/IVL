using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// Minijuego del nivel 1: "Armado de jeringa y aguja". Se abre desde el organizador de insumos.
/// En una charola aparecen (en orden aleatorio) jeringas reales de 1, 3, 5, 10, 20 y 60 mL y agujas
/// hipodérmicas de 18G a 27G con su color ISO 6009. El jugador elige una jeringa y una aguja y
/// presiona "Armar": la aguja se monta en la jeringa y se valida (lo correcto para el conejo es
/// 5 mL + 21G × 1″).
///   - Modo educativo: al pasar el mouse se ve la etiqueta con su uso típico y, si se equivoca,
///     se explica qué parte está mal y por qué.
///   - Modo evaluación: sólo la etiqueta; cada combinación incorrecta se registra como error.
/// Al acertar, la jeringa armada (estéril, de un solo uso) queda en la mano del jugador.
/// </summary>
public class SyringeKitMinigame : MonoBehaviour
{
    [Header("Materiales")]
    public Material barrelMaterial;
    public Material plungerMaterial;
    public Material rubberMaterial;
    public Material steelMaterial;
    public Material inkMaterial;
    public Material cardMaterial;
    public Material trayMaterial;
    public Material[] hubMaterials;

    [Header("UI")]
    public Sprite panelSprite;
    public TMP_FontAsset font;

    [Header("Escenario (se arma lejos del laboratorio)")]
    public Vector3 stagePosition = new Vector3(600f, 0f, 600f);
    public Vector3 cameraLocalPosition = new Vector3(0f, 0.68f, -0.40f);
    public Vector3 cameraLookAt = new Vector3(0f, 0f, -0.045f);
    public float cameraFov = 42f;
    public float syringeSpacing = 0.095f;
    public float needleSpacing = 0.072f;
    public float syringeRowZ = 0.07f;
    public float needleRowZ = -0.145f;

    public bool IsActive { get; private set; }

    class Item
    {
        public bool isSyringe;
        public int spec;
        public Transform root;
        public Transform needle;
        public Vector3 needleHome;
        public Vector3 tipLocal;
        public Vector3 home;
        public float frontZ;
        public BoxCollider col;
        public float lift;
        public RectTransform tag;
        public TMP_Text tagText;
        public Image tagBg;
    }

    enum Phase { Choosing, Assembling, Result }

    static readonly Color PanelColor = new Color(0.03f, 0.07f, 0.14f, 0.9f);
    static readonly Color TagColor = new Color(0.03f, 0.07f, 0.14f, 0.82f);
    static readonly Color TagHover = new Color(0.16f, 0.32f, 0.6f, 0.95f);
    static readonly Color TagPicked = new Color(0.12f, 0.5f, 0.3f, 0.96f);
    static readonly Color Good = new Color(0.45f, 0.9f, 0.65f);
    static readonly Color Bad = new Color(1f, 0.47f, 0.42f);

    readonly List<Item> items = new List<Item>();
    Transform stage;
    Camera cam;
    Camera playerCam;
    Canvas hudCanvas;
    GameObject ui;
    RectTransform canvasRect;
    TMP_Text titleText, instrText, infoTitle, infoBody, summaryText, resultTitle, resultBody, resultButtonLabel;
    GameObject infoCard, resultCard;
    Button assembleButton, resultButton;
    Item hovered, pickedSyringe, pickedNeedle;
    Phase phase;
    bool lastCorrect, lastSyringeOk, lastNeedleOk;
    float assembleT;
    Vector3 needleFrom;
    readonly HashSet<string> registeredWrong = new HashSet<string>();

    Level1Manager Level => Level1Manager.Instance;
    bool Educational => Level == null || Level.IsEducational;

    // ------------------------------------------------------------------
    //  Abrir / cerrar
    // ------------------------------------------------------------------

    public void Begin()
    {
        if (IsActive) return;
        if (stage == null) BuildStage();
        if (ui == null) BuildUI();

        ResetChoices(false, false);
        ShuffleSlots();
        stage.gameObject.SetActive(true);
        cam.gameObject.SetActive(true);
        playerCam = ObjectGrabber.Instance != null ? ObjectGrabber.Instance.Cam : Camera.main;
        if (playerCam != null && playerCam != cam) playerCam.enabled = false;
        hudCanvas = Level != null && Level.counterText != null ? Level.counterText.canvas.rootCanvas : null;
        if (hudCanvas != null) hudCanvas.enabled = false;

        titleText.text = "Organizador de insumos · Armado de jeringa y aguja";
        instrText.text = Educational
            ? "Elige la <b>jeringa</b> y la <b>aguja</b> adecuadas para trabajar con el conejo (clic para seleccionar) y presiona <b>Armar</b>. Pasa el mouse sobre cada una para leer su etiqueta."
            : "Elige la jeringa y la aguja adecuadas para el procedimiento con el conejo y presiona <b>Armar</b>.";
        resultCard.SetActive(false);
        infoCard.SetActive(false);
        ui.SetActive(true);
        phase = Phase.Choosing;
        IsActive = true;
        RefreshSummary();

        if (GameFlow.Instance != null) GameFlow.Instance.EnterMinigame();
        else { Cursor.lockState = CursorLockMode.None; Cursor.visible = true; }
    }

    void Close(bool assembled)
    {
        if (!IsActive) return;
        IsActive = false;
        ui.SetActive(false);
        cam.gameObject.SetActive(false);
        stage.gameObject.SetActive(false);
        if (playerCam != null) playerCam.enabled = true;
        if (hudCanvas != null) hudCanvas.enabled = true;
        if (GameFlow.Instance != null) GameFlow.Instance.ExitMinigame();
        else { Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false; }
        if (assembled && Level != null) Level.OnSyringeAssembled();
    }

    // ------------------------------------------------------------------
    //  Bucle
    // ------------------------------------------------------------------

    void Update()
    {
        if (!IsActive) return;
        if (GameFlow.Instance != null && GameFlow.Instance.CurrentState != GameFlow.State.Minigame) return; // pausa

        float dt = Time.unscaledDeltaTime;
        if (phase == Phase.Choosing)
        {
            hovered = null;
            if (!PointerOverUI() && Physics.Raycast(cam.ScreenPointToRay(MousePosition()), out var hit, 3f, ~0, QueryTriggerInteraction.Collide))
                hovered = items.Find(it => it.col == hit.collider);
            if (hovered != null && ClickPressed()) Pick(hovered);
            if (CancelPressed()) { Close(false); return; }
        }
        else if (phase == Phase.Assembling)
        {
            assembleT += dt;
            float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(assembleT / 0.8f));
            Vector3 to = NeedleTarget();
            pickedNeedle.needle.position = Vector3.Lerp(needleFrom, to, k) + Vector3.up * (Mathf.Sin(k * Mathf.PI) * 0.06f);
            if (assembleT >= 1.15f) ShowResult();
        }

        foreach (var it in items)
        {
            bool picked = it == pickedSyringe || it == pickedNeedle;
            float target = picked ? 0.02f : it == hovered ? 0.009f : 0f;
            it.lift = Mathf.MoveTowards(it.lift, target, dt * 0.12f);
            it.root.localPosition = it.home + Vector3.up * it.lift;
        }
        UpdateTags();
        UpdateInfo();
    }

    Vector3 NeedleTarget()
    {
        // La parte trasera del cono de la aguja cubre la punta Luer de la jeringa.
        return pickedSyringe.root.TransformPoint(pickedSyringe.tipLocal) + new Vector3(0f, 0f, 0.0075f);
    }

    void Pick(Item it)
    {
        if (it.isSyringe) pickedSyringe = pickedSyringe == it ? null : it;
        else pickedNeedle = pickedNeedle == it ? null : it;
        RefreshSummary();
    }

    void StartAssemble()
    {
        if (phase != Phase.Choosing || pickedSyringe == null || pickedNeedle == null) return;
        phase = Phase.Assembling;
        assembleT = 0f;
        hovered = null;
        infoCard.SetActive(false);
        pickedNeedle.needle.SetParent(stage, true);
        needleFrom = pickedNeedle.needle.position;
        RefreshSummary();
    }

    void ShowResult()
    {
        phase = Phase.Result;
        pickedNeedle.needle.position = NeedleTarget();
        var s = SyringeCatalog.Syringes[pickedSyringe.spec];
        var n = SyringeCatalog.Needles[pickedNeedle.spec];
        lastSyringeOk = s.id == SyringeCatalog.CorrectSyringeId;
        lastNeedleOk = n.id == SyringeCatalog.CorrectNeedleId;
        lastCorrect = lastSyringeOk && lastNeedleOk;

        if (lastCorrect)
        {
            resultTitle.text = "¡Armado correcto!";
            resultTitle.color = Good;
            resultBody.text = Educational
                ? "<b>" + s.Name + "</b>: " + s.use + "\n\n<b>" + n.Name + "</b> (cono " + n.colorName + "): " + n.use +
                  "\n\n<color=#9FB3CC>Es material estéril de un solo uso: no necesita autoclave. Llévalo a la mesa de química.</color>"
                : "Jeringa de 5 mL con aguja 21G × 1″ lista. Llévala a la mesa de química.";
            resultButtonLabel.text = "Tomar la jeringa";
        }
        else
        {
            resultTitle.text = "Armado incorrecto";
            resultTitle.color = Bad;
            string key = s.id + "+" + n.id;
            if (Level != null && registeredWrong.Add(key))
                Level.RegisterMinigameError("Armaste una combinación incorrecta: jeringa de " + s.label + " con aguja " + n.Label + ".");
            if (Educational)
            {
                var sb = new System.Text.StringBuilder();
                sb.Append(Line(lastSyringeOk, s.Name, lastSyringeOk ? "es la adecuada." : s.whyWrong)).Append("\n\n");
                sb.Append(Line(lastNeedleOk, n.Name + " (cono " + n.colorName + ")", lastNeedleOk ? "es la adecuada." : n.whyWrong));
                sb.Append("\n\n<color=#9FB3CC>Para trabajar con el conejo arma una <b>jeringa de 5 mL</b> con <b>aguja 21G × 1″</b> (cono verde).</color>");
                resultBody.text = sb.ToString();
            }
            else resultBody.text = "Esta combinación no es la adecuada para el procedimiento con el conejo.\nSe registró como error. Vuelve a elegir.";
            resultButtonLabel.text = "Volver a elegir";
        }
        resultCard.SetActive(true);
        RefreshSummary();
    }

    static string Line(bool ok, string what, string why)
    {
        string mark = ok ? "<color=#73E6A6>●</color>" : "<color=#FF786B>●</color>";
        return mark + " <b>" + what + "</b>: " + why;
    }

    void OnResultButton()
    {
        if (phase != Phase.Result) return;
        if (lastCorrect) { Close(true); return; }
        // En el modo educativo se conserva la parte que sí estaba bien; en evaluación se reinicia todo.
        ResetChoices(Educational && lastSyringeOk, Educational && lastNeedleOk);
        resultCard.SetActive(false);
        phase = Phase.Choosing;
        RefreshSummary();
    }

    void ResetChoices(bool keepSyringe, bool keepNeedle)
    {
        foreach (var it in items)
        {
            if (it.isSyringe || it.needle == null) continue;
            it.needle.SetParent(it.root, false);
            it.needle.localPosition = it.needleHome;
            it.needle.localRotation = Quaternion.identity;
        }
        if (!keepSyringe) pickedSyringe = null;
        if (!keepNeedle) pickedNeedle = null;
        hovered = null;
    }

    void ShuffleSlots()
    {
        var syr = items.FindAll(i => i.isSyringe);
        var ndl = items.FindAll(i => !i.isSyringe);
        Place(syr, syringeSpacing, syringeRowZ);
        Place(ndl, needleSpacing, needleRowZ);
    }

    static void Place(List<Item> list, float spacing, float z)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
        float x0 = -spacing * (list.Count - 1) * 0.5f;
        for (int i = 0; i < list.Count; i++)
        {
            list[i].home = new Vector3(x0 + spacing * i, 0f, z);
            list[i].lift = 0f;
            list[i].root.localPosition = list[i].home;
        }
    }

    // ------------------------------------------------------------------
    //  Escenario 3D
    // ------------------------------------------------------------------

    void BuildStage()
    {
        stage = new GameObject("MinijuegoJeringas").transform;
        stage.SetParent(transform, false);
        stage.SetPositionAndRotation(stagePosition, Quaternion.identity);

        var tray = GameObject.CreatePrimitive(PrimitiveType.Cube);
        tray.name = "Pano_Charola";
        Destroy(tray.GetComponent<Collider>());
        tray.transform.SetParent(stage, false);
        tray.transform.localPosition = new Vector3(0f, -0.01f, 0f);
        tray.transform.localScale = new Vector3(0.7f, 0.02f, 0.52f);
        if (trayMaterial != null) tray.GetComponent<Renderer>().sharedMaterial = trayMaterial;

        var lightGo = new GameObject("Luz");
        lightGo.transform.SetParent(stage, false);
        lightGo.transform.localPosition = new Vector3(0.05f, 0.55f, -0.25f);
        var l = lightGo.AddComponent<Light>();
        l.type = LightType.Point;
        l.range = 2f;
        l.intensity = 0.6f;
        l.color = new Color(1f, 0.97f, 0.92f);
        l.shadows = LightShadows.None;

        var camGo = new GameObject("CamaraJeringas");
        camGo.transform.SetParent(stage, false);
        camGo.transform.localPosition = cameraLocalPosition;
        camGo.transform.LookAt(stage.TransformPoint(cameraLookAt));
        cam = camGo.AddComponent<Camera>();
        cam.fieldOfView = cameraFov;
        cam.nearClipPlane = 0.02f;
        cam.farClipPlane = 5f;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.06f, 0.08f, 0.12f);
        cam.depth = 5f;
        camGo.SetActive(false);

        var mats = new SyringeCatalog.Materials
        {
            barrel = barrelMaterial, plunger = plungerMaterial, rubber = rubberMaterial, steel = steelMaterial,
            ink = inkMaterial, card = cardMaterial, hubs = hubMaterials
        };

        for (int i = 0; i < SyringeCatalog.Syringes.Length; i++)
        {
            var go = SyringeCatalog.BuildSyringe(SyringeCatalog.Syringes[i], mats, stage, out Vector3 tip);
            items.Add(MakeItem(go.transform, true, i, tip, null));
        }
        for (int i = 0; i < SyringeCatalog.Needles.Length; i++)
        {
            var go = SyringeCatalog.BuildNeedlePack(SyringeCatalog.Needles[i], i, mats, stage, out Transform needle);
            items.Add(MakeItem(go.transform, false, i, Vector3.zero, needle));
        }
        stage.gameObject.SetActive(false);
    }

    Item MakeItem(Transform root, bool isSyringe, int spec, Vector3 tip, Transform needle)
    {
        Bounds b = new Bounds(root.position, Vector3.zero);
        bool init = false;
        foreach (var r in root.GetComponentsInChildren<Renderer>())
        {
            if (!init) { b = r.bounds; init = true; } else b.Encapsulate(r.bounds);
        }
        var col = root.gameObject.AddComponent<BoxCollider>();
        col.center = root.InverseTransformPoint(b.center);
        col.size = b.size + new Vector3(0.012f, 0.01f, 0.012f);
        return new Item
        {
            isSyringe = isSyringe,
            spec = spec,
            root = root,
            needle = needle,
            needleHome = needle != null ? needle.localPosition : Vector3.zero,
            tipLocal = tip,
            frontZ = col.center.z - col.size.z * 0.5f,
            col = col
        };
    }

    // ------------------------------------------------------------------
    //  Interfaz
    // ------------------------------------------------------------------

    void BuildUI()
    {
        ui = new GameObject("UI_Jeringas", typeof(RectTransform));
        ui.transform.SetParent(transform, false);
        var canvas = ui.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 60;
        var scaler = ui.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        ui.AddComponent<GraphicRaycaster>();
        canvasRect = (RectTransform)ui.transform;

        // Etiquetas de cada objeto (primero, para que queden debajo de los paneles).
        var tagsRoot = new GameObject("Etiquetas", typeof(RectTransform)).transform;
        tagsRoot.SetParent(ui.transform, false);
        Stretch((RectTransform)tagsRoot);
        foreach (var it in items)
        {
            var bg = Panel(tagsRoot, "Etiqueta", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 1f), Vector2.zero, new Vector2(128f, 34f), TagColor);
            bg.GetComponent<Image>().raycastTarget = false;
            it.tag = bg;
            it.tagBg = bg.GetComponent<Image>();
            it.tagText = MakeText(bg, "Texto", ItemShortLabel(it), 19f, Color.white, TextAlignmentOptions.Center);
            it.tagText.enableAutoSizing = true;
            it.tagText.fontSizeMin = 12f;
            it.tagText.fontSizeMax = 19f;
        }

        // Encabezado.
        var header = Panel(ui.transform, "Encabezado", new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -16f), new Vector2(1160f, 118f), PanelColor);
        titleText = MakeText(header, "Titulo", "", 30f, Color.white, TextAlignmentOptions.Top, new Vector2(20f, 10f), new Vector2(-20f, -10f));
        instrText = MakeText(header, "Instrucciones", "", 20f, new Color(0.85f, 0.9f, 0.97f), TextAlignmentOptions.Bottom, new Vector2(26f, 10f), new Vector2(-26f, -52f));

        // Etiqueta del objeto bajo el mouse.
        var info = Panel(ui.transform, "InfoObjeto", new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(28f, 28f), new Vector2(600f, 180f), PanelColor);
        infoCard = info.gameObject;
        infoTitle = MakeText(info, "Titulo", "", 25f, new Color(1f, 0.92f, 0.7f), TextAlignmentOptions.TopLeft, new Vector2(20f, 12f), new Vector2(-20f, -12f));
        infoBody = MakeText(info, "Cuerpo", "", 19f, new Color(0.85f, 0.9f, 0.97f), TextAlignmentOptions.TopLeft, new Vector2(20f, 12f), new Vector2(-20f, -50f));

        // Resumen de lo elegido + botones.
        var summary = Panel(ui.transform, "Seleccion", new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-28f, 28f), new Vector2(560f, 180f), PanelColor);
        summaryText = MakeText(summary, "Texto", "", 22f, Color.white, TextAlignmentOptions.TopLeft, new Vector2(22f, 84f), new Vector2(-22f, -14f));
        MakeButton(summary, "Cerrar", "Cerrar (Q)", new Vector2(0f, 0f), new Vector2(22f, 16f), new Vector2(230f, 58f), new Color(0.24f, 0.3f, 0.42f), () => Close(false));
        assembleButton = MakeButton(summary, "Armar", "Armar", new Vector2(1f, 0f), new Vector2(-22f, 16f), new Vector2(260f, 58f), new Color(0.13f, 0.52f, 0.32f), StartAssemble);

        // Tarjeta de resultado.
        var result = Panel(ui.transform, "Resultado", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 10f), new Vector2(820f, 470f), new Color(0.04f, 0.09f, 0.18f, 0.97f));
        resultCard = result.gameObject;
        resultTitle = MakeText(result, "Titulo", "", 34f, Color.white, TextAlignmentOptions.Top, new Vector2(30f, 20f), new Vector2(-30f, -22f));
        resultBody = MakeText(result, "Detalle", "", 21f, new Color(0.88f, 0.92f, 0.98f), TextAlignmentOptions.TopLeft, new Vector2(40f, 100f), new Vector2(-40f, -84f));
        resultBody.enableAutoSizing = true;
        resultBody.fontSizeMin = 14f;
        resultBody.fontSizeMax = 21f;
        resultButton = MakeButton(result, "Continuar", "", new Vector2(0.5f, 0f), new Vector2(0f, 24f), new Vector2(340f, 64f), new Color(0.13f, 0.52f, 0.32f), OnResultButton);
        resultButtonLabel = resultButton.GetComponentInChildren<TMP_Text>();
        resultCard.SetActive(false);

        ui.SetActive(false);
    }

    string ItemShortLabel(Item it) => it.isSyringe
        ? SyringeCatalog.Syringes[it.spec].label
        : SyringeCatalog.Needles[it.spec].Label;

    void UpdateTags()
    {
        bool show = phase != Phase.Result;
        foreach (var it in items)
        {
            bool hideForAssembly = phase != Phase.Choosing && it == pickedNeedle;
            it.tag.gameObject.SetActive(show && !hideForAssembly);
            if (!show) continue;
            Vector3 world = it.root.TransformPoint(new Vector3(0f, 0f, it.frontZ - 0.004f));
            Vector3 sp = cam.WorldToScreenPoint(world);
            if (sp.z <= 0f) { it.tag.gameObject.SetActive(false); continue; }
            RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, sp, null, out Vector2 lp);
            it.tag.anchoredPosition = lp;
            bool picked = it == pickedSyringe || it == pickedNeedle;
            it.tagBg.color = picked ? TagPicked : it == hovered ? TagHover : TagColor;
        }
    }

    void UpdateInfo()
    {
        bool show = phase == Phase.Choosing && hovered != null;
        if (infoCard.activeSelf != show) infoCard.SetActive(show);
        if (!show) return;
        if (hovered.isSyringe)
        {
            var s = SyringeCatalog.Syringes[hovered.spec];
            infoTitle.text = s.Name;
            infoBody.text = s.Specs + "\nEstéril · desechable · un solo uso" +
                            (Educational ? "\n<color=#9FB3CC>Uso típico:</color> " + s.use : "");
        }
        else
        {
            var n = SyringeCatalog.Needles[hovered.spec];
            infoTitle.text = n.Name + "  <size=80%><color=#B8C4D6>(cono " + n.colorName + ")</color></size>";
            infoBody.text = "Aguja hipodérmica · " + n.Specs + "\nEstéril · desechable · un solo uso" +
                            (Educational ? "\n<color=#9FB3CC>Uso típico:</color> " + n.use : "");
        }
    }

    void RefreshSummary()
    {
        string none = "<color=#8A96A8>sin elegir</color>";
        string syr = pickedSyringe != null ? SyringeCatalog.Syringes[pickedSyringe.spec].Name : none;
        string ndl = pickedNeedle != null ? SyringeCatalog.Needles[pickedNeedle.spec].Name + " <size=80%><color=#B8C4D6>(" + SyringeCatalog.Needles[pickedNeedle.spec].colorName + ")</color></size>" : none;
        summaryText.text = "<b>Tu selección</b>\nJeringa: " + syr + "\nAguja: " + ndl;
        assembleButton.interactable = phase == Phase.Choosing && pickedSyringe != null && pickedNeedle != null;
    }

    // ---------- Ayudantes de UI ----------

    RectTransform Panel(Transform parent, string name, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        rt.anchorMin = rt.anchorMax = anchor;
        rt.pivot = pivot;
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
        var img = go.GetComponent<Image>();
        img.color = color;
        if (panelSprite != null) { img.sprite = panelSprite; img.type = Image.Type.Sliced; }
        return rt;
    }

    TMP_Text MakeText(Transform parent, string name, string text, float size, Color color, TextAlignmentOptions align,
                  Vector2? offsetMin = null, Vector2? offsetMax = null)
    {
        var go = new GameObject(name, typeof(RectTransform));
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        Stretch(rt);
        rt.offsetMin = offsetMin ?? new Vector2(8f, 2f);
        rt.offsetMax = offsetMax ?? new Vector2(-8f, -2f);
        var t = go.AddComponent<TextMeshProUGUI>();
        if (font != null) t.font = font;
        t.text = text;
        t.fontSize = size;
        t.color = color;
        t.alignment = align;
        t.richText = true;
        t.raycastTarget = false;
        return t;
    }

    Button MakeButton(Transform parent, string name, string label, Vector2 anchor, Vector2 pos, Vector2 size, Color color, UnityAction onClick)
    {
        var rt = Panel(parent, name, anchor, anchor, pos, size, color);
        var b = rt.gameObject.AddComponent<Button>();
        var cb = b.colors;
        cb.normalColor = Color.white;
        cb.highlightedColor = new Color(1.15f, 1.15f, 1.15f);
        cb.pressedColor = new Color(0.8f, 0.8f, 0.8f);
        cb.disabledColor = new Color(0.55f, 0.55f, 0.55f, 0.6f);
        b.colors = cb;
        b.onClick.AddListener(onClick);
        MakeText(rt, "Label", label, 24f, Color.white, TextAlignmentOptions.Center);
        return b;
    }

    static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
    }

    // ---------- Entrada ----------

    static Vector2 MousePosition()
    {
#if ENABLE_INPUT_SYSTEM
        return Mouse.current != null ? Mouse.current.position.ReadValue() : Vector2.zero;
#else
        return Input.mousePosition;
#endif
    }

    static bool ClickPressed()
    {
#if ENABLE_INPUT_SYSTEM
        return Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame;
#else
        return Input.GetMouseButtonDown(0);
#endif
    }

    static bool CancelPressed()
    {
#if ENABLE_INPUT_SYSTEM
        return Keyboard.current != null && Keyboard.current.qKey.wasPressedThisFrame;
#else
        return Input.GetKeyDown(KeyCode.Q);
#endif
    }

    static bool PointerOverUI() => EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();

    // ---------- Para pruebas automáticas ----------
    public int ItemCount => items.Count;
    public bool InResult => phase == Phase.Result;
    public bool LastWasCorrect => lastCorrect;

    /// <summary>Selecciona por id (jeringa "5", aguja "21G-1"...) como si se hiciera clic.</summary>
    public void TestPick(string syringeId, string needleId)
    {
        int si = System.Array.FindIndex(SyringeCatalog.Syringes, s => s.id == syringeId);
        int ni = SyringeCatalog.IndexOfNeedle(needleId);
        pickedSyringe = items.Find(i => i.isSyringe && i.spec == si);
        pickedNeedle = items.Find(i => !i.isSyringe && i.spec == ni);
        RefreshSummary();
    }
    public void TestAssemble() => StartAssemble();
    public void TestResultButton() => OnResultButton();
    public Vector3 ScreenPointOf(string id, bool syringe)
    {
        var it = items.Find(i => i.isSyringe == syringe && (syringe ? SyringeCatalog.Syringes[i.spec].id : SyringeCatalog.Needles[i.spec].id) == id);
        return it != null ? cam.WorldToScreenPoint(it.col.bounds.center) : Vector3.zero;
    }
}
