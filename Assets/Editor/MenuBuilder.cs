using System.IO;
using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Construye (o reconstruye) el menú principal, la selección de modo (Educativo / Evaluación),
/// Opciones, Créditos, el menú de pausa, los elementos nuevos del HUD (modo, cronómetro, objetivo,
/// lista de materiales), la pantalla de resultados con calificación, la cámara de fondo del menú
/// y conecta todas las referencias. Se puede volver a ejecutar sin duplicar nada.
/// </summary>
public static class MenuBuilder
{
    const string Dir = "Assets/Materials/Nivel1";

    static readonly Color Navy = new Color(0.035f, 0.075f, 0.15f, 0.9f);
    static readonly Color Card = new Color(0.05f, 0.1f, 0.2f, 0.95f);
    static readonly Color Primary = new Color(0.16f, 0.47f, 0.86f, 1f);
    static readonly Color Secondary = new Color(0.18f, 0.24f, 0.34f, 1f);
    static readonly Color Green = new Color(0.2f, 0.72f, 0.48f, 1f);
    static readonly Color Orange = new Color(0.95f, 0.58f, 0.2f, 1f);
    static readonly Color Muted = new Color(0.72f, 0.78f, 0.87f, 1f);
    static readonly Color Accent = new Color(0.55f, 0.78f, 1f, 1f);

    static Sprite rounded;
    static Material hudTextMaterial;

    [MenuItem("Tools/Inmunologia/Construir menú principal y modos de juego")]
    public static void Build()
    {
        EnsureFolder();
        rounded = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");

        var level = Object.FindAnyObjectByType<Level1Manager>();
        var hud = GameObject.Find("Canvas")?.GetComponent<Canvas>();
        var player = GameObject.Find("Player");
        if (level == null || hud == null || player == null)
        {
            Debug.LogError("[MenuBuilder] Falta Level1Manager, Canvas (HUD) o Player en la escena.");
            return;
        }

        hudTextMaterial = CreateHudTextMaterial();
        CreateTapeMaterials(out var tapeSterile, out var tapeUnsterile);
        var markerMat = CreateMarkerMaterial();
        SetMaterialNames();

        var hudRefs = BuildHud(hud, level);
        level.tapeSterileMaterial = tapeSterile;
        level.tapeUnsterileMaterial = tapeUnsterile;
        EditorUtility.SetDirty(level);

        var guide = level.GetComponent<EducationalGuide>();
        if (guide == null) guide = level.gameObject.AddComponent<EducationalGuide>();
        guide.level = level;
        guide.autoclave = Object.FindAnyObjectByType<Autoclave>();
        guide.table = Object.FindAnyObjectByType<PlacementZone>();
        guide.checklistPanel = hudRefs.checklistPanel;
        guide.checklistText = hudRefs.checklistText;
        guide.objectivePanel = hudRefs.objectivePanel;
        guide.objectiveText = hudRefs.objectiveText;
        guide.markerMaterial = markerMat;
        EditorUtility.SetDirty(guide);

        var menu = BuildMenuCanvas(out var menuScreen, out var fade);
        var menuCam = BuildMenuCamera();
        var menuVolume = BuildMenuVolume();

        var flowGo = GameObject.Find("GameFlow");
        if (flowGo == null) flowGo = new GameObject("GameFlow");
        var flow = flowGo.GetComponent<GameFlow>();
        if (flow == null) flow = flowGo.AddComponent<GameFlow>();
        var pause = flowGo.GetComponent<PauseMenu>();
        if (pause == null) pause = flowGo.AddComponent<PauseMenu>();

        pause.panel = hudRefs.pausePanel;
        pause.options = menu.options;
        pause.resumeButton = hudRefs.resume;
        pause.optionsButton = hudRefs.pauseOptions;
        pause.restartButton = hudRefs.restart;
        pause.mainMenuButton = hudRefs.pauseMenu;

        flow.player = player;
        flow.menuCamera = menuCam;
        flow.menuVolume = menuVolume;
        flow.level = level;
        flow.hudCanvas = hud;
        flow.menuScreen = menuScreen;
        flow.mainMenu = menu;
        flow.pauseMenu = pause;
        flow.fade = fade;
        EditorUtility.SetDirty(flow);
        EditorUtility.SetDirty(pause);

        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        AssetDatabase.SaveAssets();
        Debug.Log("[MenuBuilder] Menú principal, modos de juego y HUD construidos.");
    }

    // =====================================================================
    //  Nombres en español de los materiales
    // =====================================================================

    static void SetMaterialNames()
    {
        var names = new (string path, string label)[]
        {
            ("Mat_N1/erlenmeyer_flask_lowpoly", "Matraz Erlenmeyer"),
            ("Mat_N1/pasteur_pipette_lowpoly", "Pipeta Pasteur"),
            ("Mat_N1/saline_bottle_lowpoly", "Frasco de solución salina"),
            ("Mat_N1/syringe_5cc_lowpoly", "Jeringa de 5 mL"),
            ("Mat_N1/tube_rack_lowpoly", "Gradilla para tubos"),
            ("Mat_incorrectos/Boiling_Liquid_Stand", "Soporte con matraces en ebullición"),
            ("Mat_incorrectos/analytical_lab_instruments", "Instrumentos analíticos"),
            ("Mat_incorrectos/florence_flask", "Matraz balón"),
            ("Mat_incorrectos/glass_funnel", "Embudo de vidrio"),
            ("Mat_incorrectos/liquids/Beaker3", "Vaso de precipitados con reactivo"),
            ("Mat_incorrectos/liquids/Erlenmeyer_flask3", "Matraz con reactivo"),
            ("Mat_incorrectos/liquids/florence_flask3", "Matraz balón con reactivo"),
            ("Mat_incorrectos/liquids/Glass_Lab_test_tube6", "Tubo de ensayo con reactivo"),
            ("Mat_incorrectos/Glass_medical_dropper", "Gotero de vidrio"),
        };
        foreach (var (path, label) in names)
        {
            var go = GameObject.Find(path);
            var g = go != null ? go.GetComponent<Grabbable>() : null;
            if (g == null) { Debug.LogWarning("[MenuBuilder] No encontré " + path); continue; }
            g.displayName = label;
            EditorUtility.SetDirty(g);
        }
    }

    // =====================================================================
    //  HUD (Canvas existente): se agregan/ajustan elementos en su lugar
    // =====================================================================

    class HudRefs
    {
        public GameObject checklistPanel, objectivePanel, pausePanel;
        public TMP_Text checklistText, objectiveText;
        public UnityEngine.UI.Button resume, pauseOptions, restart, pauseMenu;
    }

    static HudRefs BuildHud(Canvas hud, Level1Manager level)
    {
        var r = new HudRefs();
        var root = hud.transform;
        foreach (var n in new[] { "HudFondo", "ModeText", "TimerText", "ObjectivePanel", "ChecklistPanel", "PausePanel" })
        {
            var old = root.Find(n);
            if (old != null) Object.DestroyImmediate(old.gameObject);
        }

        var scaler = hud.GetComponent<UnityEngine.UI.CanvasScaler>();
        if (scaler != null) { scaler.matchWidthOrHeight = 0.5f; EditorUtility.SetDirty(scaler); }

        // Fondo para contador + modo (arriba a la izquierda).
        var bg = UI("HudFondo", root);
        Place(bg, new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1), new Vector2(18, -18), new Vector2(430, 112));
        Img(bg, new Color(0.035f, 0.075f, 0.15f, 0.62f));
        bg.SetAsFirstSibling();

        var counter = level.counterText as TextMeshProUGUI;
        if (counter != null)
        {
            var crt = counter.rectTransform;
            Place(crt, new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1), new Vector2(38, -24), new Vector2(390, 50));
            counter.fontSize = 34;
            counter.fontStyle = FontStyles.Bold;
            counter.alignment = TextAlignmentOptions.Left;
            counter.raycastTarget = false;
        }

        var mode = Txt(UI("ModeText", root), "MODO EDUCATIVO", 22, new Color(0.45f, 0.9f, 0.65f), TextAlignmentOptions.Left, FontStyles.Bold);
        Place(mode.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1), new Vector2(38, -78), new Vector2(390, 34));
        mode.characterSpacing = 4f;
        mode.transform.SetSiblingIndex(bg.GetSiblingIndex() + 1);

        var timer = Txt(UI("TimerText", root), "00:00", 40, Color.white, TextAlignmentOptions.Right, FontStyles.Bold);
        Place(timer.rectTransform, new Vector2(1, 1), new Vector2(1, 1), new Vector2(1, 1), new Vector2(-30, -22), new Vector2(240, 56));
        timer.fontSharedMaterial = hudTextMaterial;

        foreach (var t in new[] { level.toastText, level.autoclaveTimerText })
            if (t != null) { t.fontSharedMaterial = hudTextMaterial; t.raycastTarget = false; }

        // Objetivo (modo educativo), debajo del contador.
        var obj = UI("ObjectivePanel", root);
        Place(obj, new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1), new Vector2(18, -142), new Vector2(560, 150));
        Img(obj, new Color(0.035f, 0.075f, 0.15f, 0.74f));
        var objText = Txt(UI("ObjectiveText", obj), "OBJETIVO", 23, Color.white, TextAlignmentOptions.TopLeft);
        Stretch(objText.rectTransform, 20, 18, 14, 12);
        AutoSize(objText, 15, 23);
        r.objectivePanel = obj.gameObject;
        r.objectiveText = objText;

        // Lista de materiales (modo educativo), arriba a la derecha bajo el cronómetro.
        var list = UI("ChecklistPanel", root);
        Place(list, new Vector2(1, 1), new Vector2(1, 1), new Vector2(1, 1), new Vector2(-18, -88), new Vector2(480, 340));
        Img(list, new Color(0.035f, 0.075f, 0.15f, 0.74f));
        var listText = Txt(UI("ChecklistText", list), "MATERIALES A PREPARAR", 22, Color.white, TextAlignmentOptions.TopLeft);
        Stretch(listText.rectTransform, 22, 18, 16, 14);
        AutoSize(listText, 14, 22);
        r.checklistPanel = list.gameObject;
        r.checklistText = listText;

        BuildResultsPanel(level);
        r.pausePanel = BuildPausePanel(root, r);

        // El aviso (toast) queda hasta arriba para verse incluso sobre resultados/pausa.
        if (level.toastText != null) level.toastText.transform.SetAsLastSibling();

        level.modeText = mode;
        level.timerText = timer;
        timer.gameObject.SetActive(false);
        obj.gameObject.SetActive(false);
        list.gameObject.SetActive(false);
        return r;
    }

    static void BuildResultsPanel(Level1Manager level)
    {
        var panel = level.completePanel;
        if (panel == null) return;
        var p = panel.transform;
        foreach (var n in new[] { "GradeText", "DetalleFondo", "MenuButton" })
        {
            var old = p.Find(n);
            if (old != null) Object.DestroyImmediate(old.gameObject);
        }

        var bgImg = panel.GetComponent<UnityEngine.UI.Image>();
        if (bgImg != null) bgImg.color = new Color(0.02f, 0.045f, 0.09f, 0.9f);

        var title = p.Find("Title")?.GetComponent<TextMeshProUGUI>();
        if (title != null)
        {
            Place(title.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 310), new Vector2(1200, 90));
            title.fontSize = 58; title.fontStyle = FontStyles.Bold; title.color = Color.white;
            level.completeTitleText = title;
        }

        var grade = Txt(UI("GradeText", p), "Calificación: 10.0 / 10", 40, new Color(0.45f, 0.9f, 0.65f), TextAlignmentOptions.Center, FontStyles.Bold);
        Place(grade.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 226), new Vector2(1200, 64));
        level.gradeText = grade;

        var time = level.completeTimeText as TextMeshProUGUI;
        if (time != null)
        {
            Place(time.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 162), new Vector2(1200, 50));
            time.fontSize = 30; time.color = Muted;
        }

        var box = UI("DetalleFondo", p);
        Place(box, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -30), new Vector2(1100, 310));
        Img(box, new Color(1f, 1f, 1f, 0.06f));
        var detail = Txt(UI("ResultDetailText", box), "", 24, Color.white, TextAlignmentOptions.TopLeft);
        Stretch(detail.rectTransform, 30, 30, 24, 20);
        AutoSize(detail, 15, 24);
        level.resultDetailText = detail;

        StyleExistingButton(p.Find("RetryButton"), "Reintentar", Primary, new Vector2(-340, -270));
        StyleExistingButton(p.Find("ContinueButton"), "Continuar", Green, new Vector2(0, -270));

        var menuBtn = Btn(p, "MenuButton", "Menú principal", Secondary, 28);
        Place((RectTransform)menuBtn.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(340, -270), new Vector2(300, 72));
        UnityEventTools.AddPersistentListener(menuBtn.onClick, new UnityAction(level.BackToMainMenu));
    }

    static void StyleExistingButton(Transform t, string label, Color c, Vector2 pos)
    {
        if (t == null) return;
        Place((RectTransform)t, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), pos, new Vector2(300, 72));
        var img = t.GetComponent<UnityEngine.UI.Image>();
        if (img != null) { img.sprite = rounded; img.type = UnityEngine.UI.Image.Type.Sliced; img.color = Color.white; }
        var b = t.GetComponent<UnityEngine.UI.Button>();
        if (b != null) b.colors = Colors(c);
        var lbl = t.GetComponentInChildren<TextMeshProUGUI>();
        if (lbl != null) { lbl.text = label; lbl.fontSize = 28; lbl.fontStyle = FontStyles.Bold; lbl.color = Color.white; lbl.raycastTarget = false; }
    }

    static GameObject BuildPausePanel(Transform root, HudRefs r)
    {
        var panel = UI("PausePanel", root);
        Stretch(panel);
        Img(panel, new Color(0f, 0f, 0f, 0.6f), false, true);

        var card = UI("Tarjeta", panel);
        Place(card, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(560, 560));
        Img(card, Card, true, true);

        var title = Txt(UI("Titulo", card), "PAUSA", 56, Color.white, TextAlignmentOptions.Center, FontStyles.Bold);
        Place(title.rectTransform, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -40), new Vector2(480, 80));
        title.characterSpacing = 8f;

        var col = Column(card, new Vector2(0, -150), 420, 4, 72, 18);
        r.resume = Btn(col, "Reanudar", "Reanudar", Primary, 30);
        r.pauseOptions = Btn(col, "Opciones", "Opciones", Secondary, 30);
        r.restart = Btn(col, "Reiniciar", "Reiniciar nivel", Secondary, 30);
        r.pauseMenu = Btn(col, "MenuPrincipal", "Menú principal", Secondary, 30);

        var hint = Txt(UI("Ayuda", card), "Esc: volver al juego", 20, Muted, TextAlignmentOptions.Center);
        Place(hint.rectTransform, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 24), new Vector2(480, 30));

        panel.gameObject.SetActive(false);
        return panel.gameObject;
    }

    // =====================================================================
    //  Canvas del menú principal
    // =====================================================================

    static MainMenuUI BuildMenuCanvas(out GameObject menuScreen, out CanvasGroup fade)
    {
        var old = GameObject.Find("MenuCanvas");
        if (old != null) Object.DestroyImmediate(old);

        var go = new GameObject("MenuCanvas", typeof(RectTransform));
        go.layer = 5;
        var canvas = go.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 10;
        var scaler = go.AddComponent<UnityEngine.UI.CanvasScaler>();
        scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;
        go.AddComponent<UnityEngine.UI.GraphicRaycaster>();
        var menu = go.AddComponent<MainMenuUI>();
        var root = go.transform;

        // ---------- Pantalla del menú ----------
        var screen = UI("PantallaMenu", root);
        Stretch(screen);
        menuScreen = screen.gameObject;

        var dim = UI("Oscurecer", screen);
        Stretch(dim);
        Img(dim, new Color(0.02f, 0.05f, 0.1f, 0.3f), false);

        // Panel principal (columna izquierda)
        var main = UI("PanelPrincipal", screen);
        Place(main, new Vector2(0, 0), new Vector2(0, 1), new Vector2(0, 0.5f), Vector2.zero, new Vector2(760, 0));
        Img(main, Navy, false, true);
        menu.mainPanel = main.gameObject;

        var t1 = Txt(UI("Titulo", main), "INMUNOLOGÍA", 88, Color.white, TextAlignmentOptions.Left, FontStyles.Bold);
        Place(t1.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1), new Vector2(90, -150), new Vector2(660, 110));
        t1.characterSpacing = 3f;
        t1.textWrappingMode = TextWrappingModes.NoWrap;
        AutoSize(t1, 44, 88);
        var t2 = Txt(UI("Subtitulo", main), "Laboratorio virtual", 36, Accent, TextAlignmentOptions.Left);
        Place(t2.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1), new Vector2(94, -262), new Vector2(640, 50));
        var line = UI("Linea", main);
        Place(line, new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1), new Vector2(94, -330), new Vector2(120, 6));
        Img(line, Primary, false);
        var t3 = Txt(UI("Descripcion", main), "Nivel 1 · Preparación y esterilización del material", 24, Muted, TextAlignmentOptions.Left);
        Place(t3.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1), new Vector2(94, -352), new Vector2(620, 40));

        var buttons = Column(main, Vector2.zero, 440, 4, 74, 18);
        var brt = (RectTransform)buttons;
        Place(brt, new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1), new Vector2(94, -470), brt.sizeDelta);
        menu.playButton = Btn(buttons, "Jugar", "Jugar", Primary, 32);
        menu.optionsButton = Btn(buttons, "Opciones", "Opciones", Secondary, 30);
        menu.creditsButton = Btn(buttons, "Creditos", "Créditos", Secondary, 30);
        menu.quitButton = Btn(buttons, "Salir", "Salir", Secondary, 30);

        var foot = Txt(UI("Pie", main), "Esc: regresar", 18, new Color(0.55f, 0.62f, 0.72f), TextAlignmentOptions.Left);
        Place(foot.rectTransform, new Vector2(0, 0), new Vector2(0, 0), new Vector2(0, 0), new Vector2(94, 44), new Vector2(600, 30));

        // Selección de modo
        var modes = UI("PanelModo", screen);
        Stretch(modes);
        menu.modePanel = modes.gameObject;
        var mt = Txt(UI("Titulo", modes), "ELIGE EL MODO DE JUEGO", 56, Color.white, TextAlignmentOptions.Center, FontStyles.Bold);
        Place(mt.rectTransform, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -110), new Vector2(1400, 80));
        mt.fontSharedMaterial = hudTextMaterial;
        mt.textWrappingMode = TextWrappingModes.NoWrap;
        AutoSize(mt, 30, 56);
        var ms = Txt(UI("Subtitulo", modes), "Puedes cambiar de modo cuando quieras desde el menú principal.", 24, Muted, TextAlignmentOptions.Center);
        Place(ms.rectTransform, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -192), new Vector2(1400, 40));
        ms.fontSharedMaterial = hudTextMaterial;

        menu.educationalButton = ModeCard(modes, "TarjetaEducativo", new Vector2(-310, -20), Green, "MODO EDUCATIVO",
            "Aprende el procedimiento paso a paso.\n\n" +
            "• Lista de pasos o materiales en pantalla\n" +
            "• Objetivo guiado y marcador\n" +
            "• Avisos cuando algo no es correcto\n" +
            "• Explicación de cada paso y barra de estrés");
        menu.evaluationButton = ModeCard(modes, "TarjetaEvaluacion", new Vector2(310, -20), Orange, "MODO EVALUACIÓN",
            "Demuestra lo que aprendiste.\n\n" +
            "• Sin listas, marcadores ni guías\n" +
            "• Aplica el procedimiento por tu cuenta\n" +
            "• Se registran tus errores y tu tiempo\n" +
            "• Calificación final sobre 10");

        menu.modeBackButton = Btn(modes, "Volver", "Volver", Secondary, 28);
        Place((RectTransform)menu.modeBackButton.transform, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 90), new Vector2(300, 66));
        modes.gameObject.SetActive(false);

        // Créditos
        var credits = UI("PanelCreditos", screen);
        Place(credits, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(900, 640));
        Img(credits, Card, true, true);
        menu.creditsPanel = credits.gameObject;
        var ct = Txt(UI("Titulo", credits), "CRÉDITOS", 56, Color.white, TextAlignmentOptions.Center, FontStyles.Bold);
        Place(ct.rectTransform, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -40), new Vector2(800, 80));
        ct.characterSpacing = 8f;
        var cb = Txt(UI("Nombres", credits),
            "<size=26><color=#8CC8FF>Desarrollo del juego en Unity</color></size>\n<size=42><b>Diego Pineda Gumeta</b></size>\n\n" +
            "<size=26><color=#8CC8FF>Modelado 3D en Blender</color></size>\n<size=42><b>Juan de Dios</b></size>\n\n" +
            "<size=26><color=#8CC8FF>Lógica de programación</color></size>\n<size=42><b>Emmanuel</b></size>",
            40, Color.white, TextAlignmentOptions.Center);
        Place(cb.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 10), new Vector2(800, 400));
        menu.creditsBackButton = Btn(credits, "Volver", "Volver", Secondary, 28);
        Place((RectTransform)menu.creditsBackButton.transform, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 36), new Vector2(300, 66));
        credits.gameObject.SetActive(false);

        // ---------- Opciones (compartido con la pausa) ----------
        menu.options = BuildOptions(root);

        // ---------- Fundido ----------
        var f = UI("Fundido", root);
        Stretch(f);
        Img(f, Color.black, false, true);
        fade = f.gameObject.AddComponent<CanvasGroup>();
        fade.alpha = 0f;
        fade.blocksRaycasts = false;
        f.SetAsLastSibling();

        return menu;
    }

    static UnityEngine.UI.Button ModeCard(Transform parent, string name, Vector2 pos, Color accent, string title, string body)
    {
        var card = UI(name, parent);
        Place(card, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), pos, new Vector2(580, 500));
        var img = Img(card, Color.white, true, true);
        var btn = card.gameObject.AddComponent<UnityEngine.UI.Button>();
        btn.targetGraphic = img;
        var cc = Colors(Card);
        cc.highlightedColor = new Color(0.09f, 0.16f, 0.3f, 0.98f);
        cc.pressedColor = new Color(0.04f, 0.08f, 0.16f, 1f);
        btn.colors = cc;

        var bar = UI("Acento", card);
        Place(bar, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1), new Vector2(0, 0), new Vector2(0, 10));
        Img(bar, accent, false);

        var t = Txt(UI("Nombre", card), title, 40, accent, TextAlignmentOptions.Left, FontStyles.Bold);
        Place(t.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1), new Vector2(0, -44), new Vector2(-80, 56));
        t.characterSpacing = 2f;

        var d = Txt(UI("Descripcion", card), body, 25, Color.white, TextAlignmentOptions.TopLeft);
        var drt = d.rectTransform;
        drt.anchorMin = new Vector2(0, 0); drt.anchorMax = new Vector2(1, 1);
        drt.offsetMin = new Vector2(40, 90); drt.offsetMax = new Vector2(-40, -120);
        AutoSize(d, 16, 25);

        var a = Txt(UI("Accion", card), "Jugar en este modo  →", 26, accent, TextAlignmentOptions.Left, FontStyles.Bold);
        Place(a.rectTransform, new Vector2(0, 0), new Vector2(1, 0), new Vector2(0.5f, 0), new Vector2(0, 34), new Vector2(-80, 40));
        return btn;
    }

    static OptionsPanel BuildOptions(Transform root)
    {
        var panel = UI("PanelOpciones", root);
        Stretch(panel);
        Img(panel, new Color(0f, 0f, 0f, 0.55f), false, true);
        var opt = panel.gameObject.AddComponent<OptionsPanel>();

        var card = UI("Tarjeta", panel);
        Place(card, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(920, 720));
        Img(card, Card, true, true);
        var title = Txt(UI("Titulo", card), "OPCIONES", 56, Color.white, TextAlignmentOptions.Center, FontStyles.Bold);
        Place(title.rectTransform, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -36), new Vector2(800, 80));
        title.characterSpacing = 8f;

        var res = new UnityEngine.UI.DefaultControls.Resources
        {
            standard = rounded,
            background = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Background.psd"),
            inputField = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/InputFieldBackground.psd"),
            knob = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Knob.psd"),
            checkmark = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Checkmark.psd"),
            dropdown = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/DropdownArrow.psd"),
            mask = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UIMask.psd"),
        };

        float y = 170f;
        const float step = 92f;
        opt.sensitivitySlider = Slider(card, res, "SliderSensibilidad", y, out opt.sensitivityValue);
        RowLabel(card, "Sensibilidad del mouse", y); y -= step;
        opt.volumeSlider = Slider(card, res, "SliderVolumen", y, out opt.volumeValue);
        RowLabel(card, "Volumen general", y); y -= step;
        opt.qualityDropdown = Dropdown(card, res, y);
        RowLabel(card, "Calidad gráfica", y); y -= step;
        opt.fullscreenToggle = Toggle(card, res, "TogglePantallaCompleta", y);
        RowLabel(card, "Pantalla completa", y); y -= step;
        opt.timerToggle = Toggle(card, res, "ToggleCronometro", y);
        RowLabel(card, "Mostrar cronómetro", y);

        opt.resetButton = Btn(card, "Restablecer", "Restablecer", Secondary, 26);
        Place((RectTransform)opt.resetButton.transform, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(-170, 44), new Vector2(300, 66));
        opt.backButton = Btn(card, "Volver", "Volver", Primary, 28);
        Place((RectTransform)opt.backButton.transform, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(170, 44), new Vector2(300, 66));

        panel.gameObject.SetActive(false);
        return opt;
    }

    static void RowLabel(Transform card, string text, float y)
    {
        var t = Txt(UI("Etiqueta_" + text, card), text, 28, Color.white, TextAlignmentOptions.Left);
        Place(t.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 0.5f), new Vector2(-400, y), new Vector2(380, 50));
    }

    static UnityEngine.UI.Slider Slider(Transform card, UnityEngine.UI.DefaultControls.Resources res, string name, float y, out TMP_Text valueText)
    {
        var go = UnityEngine.UI.DefaultControls.CreateSlider(res);
        go.name = name;
        go.transform.SetParent(card, false);
        SetLayer(go);
        Place((RectTransform)go.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 0.5f), new Vector2(20, y), new Vector2(290, 22));
        var bg = go.transform.Find("Background")?.GetComponent<UnityEngine.UI.Image>();
        if (bg) bg.color = new Color(1f, 1f, 1f, 0.16f);
        var fill = go.transform.Find("Fill Area/Fill")?.GetComponent<UnityEngine.UI.Image>();
        if (fill) fill.color = Primary;
        var handle = go.transform.Find("Handle Slide Area/Handle")?.GetComponent<UnityEngine.UI.Image>();
        if (handle) handle.color = Color.white;

        valueText = Txt(UI(name + "_Valor", card), "1.0x", 26, Accent, TextAlignmentOptions.Right, FontStyles.Bold);
        Place(valueText.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(1, 0.5f), new Vector2(400, y), new Vector2(80, 44));
        return go.GetComponent<UnityEngine.UI.Slider>();
    }

    static UnityEngine.UI.Toggle Toggle(Transform card, UnityEngine.UI.DefaultControls.Resources res, string name, float y)
    {
        var go = UnityEngine.UI.DefaultControls.CreateToggle(res);
        go.name = name;
        go.transform.SetParent(card, false);
        SetLayer(go);
        var label = go.transform.Find("Label");
        if (label) Object.DestroyImmediate(label.gameObject); // se usa una etiqueta TMP aparte
        Place((RectTransform)go.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(1, 0.5f), new Vector2(400, y), new Vector2(46, 46));
        var bg = go.transform.Find("Background") as RectTransform;
        if (bg)
        {
            Stretch(bg);
            var img = bg.GetComponent<UnityEngine.UI.Image>();
            img.color = new Color(1f, 1f, 1f, 0.22f);
            var check = bg.Find("Checkmark") as RectTransform;
            if (check) { Stretch(check, 4, 4, 4, 4); check.GetComponent<UnityEngine.UI.Image>().color = Color.white; }
        }
        return go.GetComponent<UnityEngine.UI.Toggle>();
    }

    static TMP_Dropdown Dropdown(Transform card, UnityEngine.UI.DefaultControls.Resources res, float y)
    {
        var tres = new TMP_DefaultControls.Resources
        {
            standard = res.standard, background = res.background, inputField = res.inputField,
            knob = res.knob, checkmark = res.checkmark, dropdown = res.dropdown, mask = res.mask,
        };
        var go = TMP_DefaultControls.CreateDropdown(tres);
        go.name = "DropdownCalidad";
        go.transform.SetParent(card, false);
        SetLayer(go);
        Place((RectTransform)go.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(1, 0.5f), new Vector2(400, y), new Vector2(380, 54));

        var dd = go.GetComponent<TMP_Dropdown>();
        var mainImg = go.GetComponent<UnityEngine.UI.Image>();
        mainImg.color = new Color(1f, 1f, 1f, 0.14f);
        dd.captionText.color = Color.white;
        dd.captionText.fontSize = 26;
        var arrow = go.transform.Find("Arrow")?.GetComponent<UnityEngine.UI.Image>();
        if (arrow) arrow.color = Accent;

        var template = go.transform.Find("Template");
        if (template)
        {
            var timg = template.GetComponent<UnityEngine.UI.Image>();
            if (timg) timg.color = new Color(0.07f, 0.12f, 0.22f, 1f);
            var item = template.Find("Viewport/Content/Item");
            if (item)
            {
                var itemBg = item.Find("Item Background")?.GetComponent<UnityEngine.UI.Image>();
                if (itemBg) itemBg.color = new Color(0.12f, 0.19f, 0.32f, 1f);
                var itemCheck = item.Find("Item Checkmark")?.GetComponent<UnityEngine.UI.Image>();
                if (itemCheck) itemCheck.color = Accent;
                var itemToggle = item.GetComponent<UnityEngine.UI.Toggle>();
                if (itemToggle)
                {
                    var c = itemToggle.colors;
                    c.normalColor = new Color(0.8f, 0.8f, 0.8f, 1f);
                    c.highlightedColor = Color.white;
                    c.selectedColor = Color.white;
                    itemToggle.colors = c;
                }
                var itemRt = item as RectTransform;
                if (itemRt) itemRt.sizeDelta = new Vector2(itemRt.sizeDelta.x, 44);
            }
            if (dd.itemText != null) { dd.itemText.color = Color.white; dd.itemText.fontSize = 24; }
        }
        return dd;
    }

    // =====================================================================
    //  Cámara y post-proceso del menú
    // =====================================================================

    static Camera BuildMenuCamera()
    {
        var old = GameObject.Find("CamaraMenu");
        if (old != null) Object.DestroyImmediate(old);
        var go = new GameObject("CamaraMenu");
        var cam = go.AddComponent<Camera>();
        cam.fieldOfView = 58f;
        cam.nearClipPlane = 0.05f;
        cam.depth = 1f;
        var data = go.AddComponent<UniversalAdditionalCameraData>();
        data.renderPostProcessing = true;
        go.AddComponent<AudioListener>();
        var orbit = go.AddComponent<MenuCameraOrbit>();
        // Pose inicial (la misma que tomará al arrancar) para que se vea bien en el editor.
        float a = orbit.startAngle * Mathf.Deg2Rad;
        var pos = new Vector3(orbit.center.x + Mathf.Cos(a) * orbit.radiusX, orbit.height, orbit.center.z + Mathf.Sin(a) * orbit.radiusZ);
        go.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(orbit.center - pos));
        return cam;
    }

    static GameObject BuildMenuVolume()
    {
        var old = GameObject.Find("VolumenMenu");
        if (old != null) Object.DestroyImmediate(old);

        string path = Dir + "/Menu_VolumeProfile.asset";
        var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(path);
        if (profile == null)
        {
            profile = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(profile, path);
        }
        if (!profile.TryGet<DepthOfField>(out var dof)) dof = profile.Add<DepthOfField>(true);
        dof.active = true;
        dof.mode.Override(DepthOfFieldMode.Gaussian);
        dof.gaussianStart.Override(0.4f);
        dof.gaussianEnd.Override(2.6f);
        dof.gaussianMaxRadius.Override(1.4f);
        dof.highQualitySampling.Override(true);
        if (!profile.TryGet<Vignette>(out var vig)) vig = profile.Add<Vignette>(true);
        vig.active = true;
        vig.intensity.Override(0.38f);
        vig.smoothness.Override(0.5f);
        EditorUtility.SetDirty(profile);
        foreach (var c in profile.components) EditorUtility.SetDirty(c);
        if (!AssetDatabase.Contains(dof)) AssetDatabase.AddObjectToAsset(dof, profile);
        if (!AssetDatabase.Contains(vig)) AssetDatabase.AddObjectToAsset(vig, profile);

        var go = new GameObject("VolumenMenu");
        var vol = go.AddComponent<Volume>();
        vol.isGlobal = true;
        vol.priority = 10f;
        vol.sharedProfile = profile;
        return go;
    }

    // =====================================================================
    //  Materiales: cinta testigo, marcador, texto del HUD con contorno
    // =====================================================================

    static void CreateTapeMaterials(out Material sterile, out Material unsterile)
    {
        var texSterile = StripeTexture("T_Cinta_Esteril", new Color(0.93f, 0.86f, 0.66f), new Color(0.1f, 0.08f, 0.07f));
        var texUnsterile = StripeTexture("T_Cinta_NoEsteril", new Color(0.93f, 0.86f, 0.66f), new Color(0.99f, 0.97f, 0.9f));
        sterile = LitMat("M_Cinta_Esteril", texSterile);
        unsterile = LitMat("M_Cinta_NoEsteril", texUnsterile);
    }

    static Texture2D StripeTexture(string name, Color baseColor, Color stripe)
    {
        const int W = 128, H = 64;
        var px = new Color[W * H];
        for (int y = 0; y < H; y++)
        for (int x = 0; x < W; x++)
        {
            int d = (x + y) % 26;
            px[y * W + x] = d < 10 ? stripe : baseColor;
        }
        var tex = new Texture2D(W, H, TextureFormat.RGBA32, false);
        tex.SetPixels(px);
        tex.Apply();
        string path = Dir + "/" + name + ".png";
        File.WriteAllBytes(path, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        var imp = (TextureImporter)AssetImporter.GetAtPath(path);
        imp.wrapMode = TextureWrapMode.Clamp;
        imp.mipmapEnabled = true;
        imp.anisoLevel = 4;
        imp.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    static Material LitMat(string name, Texture2D tex)
    {
        string path = Dir + "/" + name + ".mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null) { m = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(m, path); }
        m.SetTexture("_BaseMap", tex);
        m.SetColor("_BaseColor", Color.white);
        m.SetFloat("_Smoothness", 0.25f);
        EditorUtility.SetDirty(m);
        return m;
    }

    static Material CreateMarkerMaterial()
    {
        string path = Dir + "/M_Marcador_Guia.mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null) { m = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(m, path); }
        m.SetColor("_BaseColor", new Color(1f, 0.85f, 0.25f));
        m.SetFloat("_Smoothness", 0.7f);
        m.EnableKeyword("_EMISSION");
        m.SetColor("_EmissionColor", new Color(1f, 0.78f, 0.2f) * 2.2f);
        m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        EditorUtility.SetDirty(m);
        return m;
    }

    static Material CreateHudTextMaterial()
    {
        var font = TMP_Settings.defaultFontAsset;
        if (font == null) return null;
        string path = Dir + "/LiberationSans SDF - HUD Contorno.mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null) { m = new Material(font.material); AssetDatabase.CreateAsset(m, path); }
        else m.CopyPropertiesFromMaterial(font.material);
        m.shaderKeywords = font.material.shaderKeywords;
        m.EnableKeyword(ShaderUtilities.Keyword_Outline);
        m.SetFloat(ShaderUtilities.ID_OutlineWidth, 0.2f);
        m.SetColor(ShaderUtilities.ID_OutlineColor, new Color(0f, 0f, 0f, 0.9f));
        m.EnableKeyword(ShaderUtilities.Keyword_Underlay);
        m.SetColor(ShaderUtilities.ID_UnderlayColor, new Color(0f, 0f, 0f, 0.55f));
        m.SetFloat(ShaderUtilities.ID_UnderlayOffsetY, -0.7f);
        m.SetFloat(ShaderUtilities.ID_UnderlaySoftness, 0.5f);
        EditorUtility.SetDirty(m);
        return m;
    }

    // =====================================================================
    //  Utilidades de UI
    // =====================================================================

    static void EnsureFolder()
    {
        if (!AssetDatabase.IsValidFolder("Assets/Materials")) AssetDatabase.CreateFolder("Assets", "Materials");
        if (!AssetDatabase.IsValidFolder(Dir)) AssetDatabase.CreateFolder("Assets/Materials", "Nivel1");
    }

    static RectTransform UI(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.layer = 5;
        go.transform.SetParent(parent, false);
        return (RectTransform)go.transform;
    }

    static void SetLayer(GameObject go)
    {
        foreach (var t in go.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = 5;
    }

    static void Place(RectTransform rt, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 pos, Vector2 size)
    {
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.pivot = pivot;
        rt.sizeDelta = size;
        rt.anchoredPosition = pos;
    }

    static void Stretch(RectTransform rt, float left = 0, float right = 0, float top = 0, float bottom = 0)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.offsetMin = new Vector2(left, bottom);
        rt.offsetMax = new Vector2(-right, -top);
    }

    static UnityEngine.UI.Image Img(RectTransform rt, Color c, bool roundedSprite = true, bool raycast = false)
    {
        var img = rt.gameObject.AddComponent<UnityEngine.UI.Image>();
        if (roundedSprite) { img.sprite = rounded; img.type = UnityEngine.UI.Image.Type.Sliced; }
        img.color = c;
        img.raycastTarget = raycast;
        return img;
    }

    static TextMeshProUGUI Txt(RectTransform rt, string text, float size, Color c, TextAlignmentOptions align, FontStyles style = FontStyles.Normal)
    {
        var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
        if (TMP_Settings.defaultFontAsset != null) t.font = TMP_Settings.defaultFontAsset;
        t.text = text;
        t.fontSize = size;
        t.color = c;
        t.alignment = align;
        t.fontStyle = style;
        t.richText = true;
        t.raycastTarget = false;
        t.textWrappingMode = TextWrappingModes.Normal;
        return t;
    }

    static void AutoSize(TextMeshProUGUI t, float min, float max)
    {
        t.enableAutoSizing = true;
        t.fontSizeMin = min;
        t.fontSizeMax = max;
    }

    static UnityEngine.UI.ColorBlock Colors(Color baseColor)
    {
        var cb = UnityEngine.UI.ColorBlock.defaultColorBlock;
        cb.normalColor = baseColor;
        cb.highlightedColor = Color.Lerp(baseColor, Color.white, 0.22f);
        cb.pressedColor = Color.Lerp(baseColor, Color.black, 0.25f);
        cb.selectedColor = baseColor;
        cb.disabledColor = new Color(0.3f, 0.3f, 0.3f, 0.6f);
        cb.colorMultiplier = 1f;
        cb.fadeDuration = 0.08f;
        return cb;
    }

    static UnityEngine.UI.Button Btn(Transform parent, string name, string label, Color baseColor, float fontSize)
    {
        var rt = UI(name, parent);
        rt.sizeDelta = new Vector2(300, 72);
        var img = Img(rt, Color.white, true, true);
        var b = rt.gameObject.AddComponent<UnityEngine.UI.Button>();
        b.targetGraphic = img;
        b.colors = Colors(baseColor);
        var le = rt.gameObject.AddComponent<UnityEngine.UI.LayoutElement>();
        le.preferredHeight = 72;
        var t = Txt(UI("Label", rt), label, fontSize, Color.white, TextAlignmentOptions.Center, FontStyles.Bold);
        Stretch(t.rectTransform);
        return b;
    }

    static Transform Column(Transform parent, Vector2 pos, float width, int count, float buttonHeight, float spacing)
    {
        var rt = UI("Botones", parent);
        float h = count * buttonHeight + (count - 1) * spacing;
        Place(rt, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0.5f, 1), pos, new Vector2(width, h));
        var vlg = rt.gameObject.AddComponent<UnityEngine.UI.VerticalLayoutGroup>();
        vlg.spacing = spacing;
        vlg.childControlWidth = true;
        vlg.childControlHeight = true;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;
        vlg.childAlignment = TextAnchor.UpperCenter;
        return rt;
    }
}
