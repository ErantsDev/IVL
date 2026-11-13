using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

/// <summary>
/// Ventana del código de vestimenta que aparece antes de empezar el Nivel 1 (la abre GameFlow).
///   1) Lista: lo que es obligatorio y lo que NO está permitido dentro del laboratorio.
///   2) Mini prueba: el alumno marca todas las prendas/accesorios que NO se permiten y presiona «Comprobar».
///      - Modo educativo: se explican los errores y se repite hasta acertar todo (después ya no se vuelve a pedir).
///      - Modo evaluación: un solo intento; cada equivocación se registra como error del nivel.
/// La interfaz se arma por código (no hace falta tocar la escena).
/// </summary>
public class DressCodePanel : MonoBehaviour
{
    struct Item
    {
        public string label;
        public bool prohibited;
        public string why;
        public Item(string label, bool prohibited, string why) { this.label = label; this.prohibited = prohibited; this.why = why; }
    }

    static readonly Item[] Items =
    {
        new Item("Sandalias o chanclas", true, "Dejan el pie expuesto a derrames y a objetos punzocortantes."),
        new Item("Shorts o falda corta", true, "Las piernas deben ir cubiertas con pantalón largo."),
        new Item("Cabello largo suelto", true, "Puede contaminar la muestra o engancharse: debe ir recogido."),
        new Item("Anillos, pulseras y reloj", true, "Acumulan microorganismos y rompen los guantes: se retiran."),
        new Item("Gorra o sombrero", true, "No es equipo de protección: estorba y contamina."),
        new Item("Uñas largas o postizas", true, "Perforan los guantes y acumulan suciedad."),
        new Item("Bata de manga larga abotonada", false, "Obligatoria, y siempre cerrada."),
        new Item("Pantalón largo", false, "Obligatorio: protege las piernas."),
        new Item("Zapato cerrado", false, "Obligatorio: protege el pie."),
        new Item("Cabello recogido", false, "Así debe llevarse dentro del laboratorio."),
        new Item("Lentes de seguridad", false, "Obligatorios: protegen los ojos de salpicaduras."),
        new Item("Guantes de látex o nitrilo", false, "Obligatorios al manipular muestras y animales."),
    };

    const string RequiredList =
        "• Bata blanca de manga larga, limpia y <b>siempre abotonada</b>.\n" +
        "• Pantalón largo.\n" +
        "• Zapato cerrado, de piso y antiderrapante.\n" +
        "• Cabello recogido (chongo o coleta firme).\n" +
        "• Lentes de seguridad.\n" +
        "• Guantes desechables al manipular muestras o animales.";

    const string ProhibitedList =
        "• Bata abierta, desabotonada o de manga corta.\n" +
        "• Shorts, bermudas, faldas o vestidos cortos.\n" +
        "• Sandalias, chanclas, tacones o zapatos abiertos.\n" +
        "• Cabello largo suelto.\n" +
        "• Anillos, pulseras, relojes y aretes colgantes.\n" +
        "• Uñas largas o postizas.\n" +
        "• Gorras, sombreros o bufandas.";

    /// <summary>En modo educativo, true una vez que el alumno acertó la prueba (ya no se le vuelve a pedir).</summary>
    public static bool PassedThisSession { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => PassedThisSession = false;

    static readonly Color Dim = new Color(0.01f, 0.03f, 0.07f, 0.78f);
    static readonly Color CardColor = new Color(0.04f, 0.09f, 0.18f, 0.97f);
    static readonly Color Box = new Color(1f, 1f, 1f, 0.05f);
    static readonly Color Green = new Color(0.45f, 0.9f, 0.65f);
    static readonly Color Red = new Color(1f, 0.5f, 0.45f);
    static readonly Color Muted = new Color(0.72f, 0.78f, 0.87f);
    static readonly Color BtnPrimary = new Color(0.13f, 0.52f, 0.32f);
    static readonly Color BtnSecondary = new Color(0.24f, 0.3f, 0.42f);
    static readonly Color CardIdle = new Color(0.12f, 0.19f, 0.32f, 1f);
    static readonly Color CardPicked = new Color(0.55f, 0.16f, 0.17f, 1f);
    static readonly Color CardRight = new Color(0.13f, 0.42f, 0.27f, 1f);
    static readonly Color CardWrong = new Color(0.62f, 0.38f, 0.1f, 1f);

    GameMode mode;
    Action<List<string>> onDone;
    TMP_FontAsset font;
    Sprite panelSprite;

    GameObject infoPage, quizPage;
    TMP_Text quizInstr, quizResult;
    Button checkButton, retryButton, startButton, backButton;
    readonly List<(Item item, Button button, Image bg, TMP_Text text)> cards = new List<(Item, Button, Image, TMP_Text)>();
    readonly HashSet<int> picked = new HashSet<int>();
    readonly List<string> errors = new List<string>();
    bool checkedOnce;

    bool Educational => mode == GameMode.Educativo;

    /// <summary>Abre la ventana; <paramref name="done"/> recibe los errores de la prueba (vacío si no hubo).</summary>
    public static DressCodePanel Open(GameMode mode, Action<List<string>> done)
    {
        var go = new GameObject("CodigoDeVestimenta", typeof(RectTransform));
        var p = go.AddComponent<DressCodePanel>();
        p.mode = mode;
        p.onDone = done;
        p.Build();
        return p;
    }

    // ------------------------------------------------------------------
    //  Construcción de la interfaz
    // ------------------------------------------------------------------

    void Build()
    {
        var kit = FindAnyObjectByType<SyringeKitMinigame>(FindObjectsInactive.Include);
        if (kit != null) { font = kit.font; panelSprite = kit.panelSprite; }
        if (font == null) font = TMP_Settings.defaultFontAsset;

        var canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 80;
        var scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        gameObject.AddComponent<GraphicRaycaster>();

        var dim = Panel(transform, "Fondo", Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero, Dim, false);
        Stretch(dim);

        var card = Panel(transform, "Ventana", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1320f, 860f), CardColor);

        BuildInfoPage(card);
        BuildQuizPage(card);
        ShowInfo();
    }

    void BuildInfoPage(RectTransform card)
    {
        var page = Group(card, "Lista");
        infoPage = page.gameObject;

        MakeText(page, "Titulo", "CÓDIGO DE VESTIMENTA DEL LABORATORIO", 38f, Color.white, TextAlignmentOptions.Top, new Vector2(40f, 0f), new Vector2(-40f, -30f), FontStyles.Bold);
        MakeText(page, "Intro", "Antes de entrar al laboratorio revisa tu vestimenta. Por tu seguridad y para no contaminar las muestras:",
            22f, Muted, TextAlignmentOptions.Top, new Vector2(60f, 0f), new Vector2(-60f, -88f));

        var left = Panel(page, "Obligatorio", new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(50f, -150f), new Vector2(595f, 520f), Box);
        Bar(left, Green);
        MakeText(left, "Titulo", "OBLIGATORIO", 27f, Green, TextAlignmentOptions.TopLeft, new Vector2(28f, 0f), new Vector2(-20f, -18f), FontStyles.Bold);
        var lt = MakeText(left, "Lista", RequiredList, 23f, Color.white, TextAlignmentOptions.TopLeft, new Vector2(28f, 20f), new Vector2(-20f, -68f));
        lt.lineSpacing = 22f;

        var right = Panel(page, "NoPermitido", new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-50f, -150f), new Vector2(595f, 520f), Box);
        Bar(right, Red);
        MakeText(right, "Titulo", "NO ESTÁ PERMITIDO", 27f, Red, TextAlignmentOptions.TopLeft, new Vector2(28f, 0f), new Vector2(-20f, -18f), FontStyles.Bold);
        var rt = MakeText(right, "Lista", ProhibitedList, 23f, Color.white, TextAlignmentOptions.TopLeft, new Vector2(28f, 20f), new Vector2(-20f, -68f));
        rt.lineSpacing = 14f;

        MakeText(page, "Nota", "Tampoco se come, se bebe ni se maquilla dentro del laboratorio.",
            19f, Muted, TextAlignmentOptions.Bottom, new Vector2(60f, 112f), new Vector2(-60f, 0f), FontStyles.Italic);

        bool skipQuiz = Educational && PassedThisSession;
        if (skipQuiz)
        {
            MakeButton(page, "Prueba", "Repetir la prueba", new Vector2(0.5f, 0f), new Vector2(-190f, 30f), new Vector2(340f, 64f), BtnSecondary, ShowQuiz);
            MakeButton(page, "Comenzar", "Comenzar el nivel", new Vector2(0.5f, 0f), new Vector2(190f, 30f), new Vector2(340f, 64f), BtnPrimary, Finish);
        }
        else
            MakeButton(page, "Siguiente", "Entendido: ir a la prueba", new Vector2(0.5f, 0f), new Vector2(0f, 30f), new Vector2(420f, 64f), BtnPrimary, ShowQuiz);
    }

    void BuildQuizPage(RectTransform card)
    {
        var page = Group(card, "Prueba");
        quizPage = page.gameObject;

        MakeText(page, "Titulo", "¿QUÉ NO SE PUEDE USAR EN EL LABORATORIO?", 36f, Color.white, TextAlignmentOptions.Top, new Vector2(40f, 0f), new Vector2(-40f, -30f), FontStyles.Bold);
        quizInstr = MakeText(page, "Instrucciones", "", 22f, Muted, TextAlignmentOptions.Top, new Vector2(60f, 0f), new Vector2(-60f, -86f));

        // Cuadrícula de 4 × 3 tarjetas en orden aleatorio.
        var order = new List<int>();
        for (int i = 0; i < Items.Length; i++) order.Add(i);
        for (int i = order.Count - 1; i > 0; i--) { int j = UnityEngine.Random.Range(0, i + 1); (order[i], order[j]) = (order[j], order[i]); }

        const float w = 290f, h = 132f, gapX = 18f, gapY = 18f;
        float totalW = 4 * w + 3 * gapX;
        for (int k = 0; k < order.Count; k++)
        {
            int idx = order[k];
            int col = k % 4, row = k / 4;
            var pos = new Vector2(-totalW * 0.5f + col * (w + gapX), -160f - row * (h + gapY));
            var rtCard = Panel(page, "Tarjeta_" + idx, new Vector2(0.5f, 1f), new Vector2(0f, 1f), pos, new Vector2(w, h), CardIdle);
            var b = rtCard.gameObject.AddComponent<Button>();
            var cb = b.colors;
            cb.normalColor = Color.white; cb.highlightedColor = new Color(1.18f, 1.18f, 1.18f); cb.pressedColor = new Color(0.8f, 0.8f, 0.8f);
            cb.disabledColor = Color.white; cb.selectedColor = Color.white;
            b.colors = cb;
            var t = MakeText(rtCard, "Texto", "", 22f, Color.white, TextAlignmentOptions.Center, new Vector2(12f, 8f), new Vector2(-12f, -8f));
            t.enableAutoSizing = true; t.fontSizeMin = 14f; t.fontSizeMax = 22f;
            int captured = cards.Count;
            b.onClick.AddListener(() => Toggle(captured));
            cards.Add((Items[idx], b, rtCard.GetComponent<Image>(), t));
        }

        quizResult = MakeText(page, "Resultado", "", 22f, Color.white, TextAlignmentOptions.Bottom, new Vector2(60f, 110f), new Vector2(-60f, 0f));
        backButton = MakeButton(page, "VerLista", "Ver la lista", new Vector2(0.5f, 0f), new Vector2(-300f, 30f), new Vector2(300f, 64f), BtnSecondary, ShowInfo);
        checkButton = MakeButton(page, "Comprobar", "Comprobar", new Vector2(0.5f, 0f), new Vector2(110f, 30f), new Vector2(300f, 64f), BtnPrimary, Check);
        retryButton = MakeButton(page, "Reintentar", "Intentar de nuevo", new Vector2(0.5f, 0f), new Vector2(110f, 30f), new Vector2(300f, 64f), BtnPrimary, ResetQuiz);
        startButton = MakeButton(page, "Comenzar", "Comenzar el nivel", new Vector2(0.5f, 0f), new Vector2(0f, 30f), new Vector2(360f, 64f), BtnPrimary, Finish);
    }

    // ------------------------------------------------------------------
    //  Lógica
    // ------------------------------------------------------------------

    void ShowInfo()
    {
        infoPage.SetActive(true);
        quizPage.SetActive(false);
    }

    void ShowQuiz()
    {
        infoPage.SetActive(false);
        quizPage.SetActive(true);
        if (!checkedOnce) ResetQuiz();
    }

    void ResetQuiz()
    {
        picked.Clear();
        quizInstr.text = "Haz clic en <b>todas</b> las prendas y accesorios que <b><color=#FF8073>NO</color></b> están permitidos y presiona «Comprobar»."
                         + (Educational ? "" : "\n<size=85%>Modo evaluación: tienes un solo intento y cada equivocación cuenta como error.</size>");
        quizResult.text = "";
        foreach (var c in cards) c.button.interactable = true;
        RefreshCards(false);
        checkButton.gameObject.SetActive(true);
        retryButton.gameObject.SetActive(false);
        startButton.gameObject.SetActive(false);
        // En evaluación no se puede regresar a la lista una vez que empieza la prueba.
        backButton.gameObject.SetActive(Educational);
    }

    void Toggle(int i)
    {
        if (checkedOnce && !Educational) return;
        if (!picked.Remove(i)) picked.Add(i);
        RefreshCards(false);
    }

    void RefreshCards(bool showResult)
    {
        for (int i = 0; i < cards.Count; i++)
        {
            var c = cards[i];
            bool sel = picked.Contains(i);
            string mark = sel ? "<size=80%><color=#FFB3AA>NO SE PERMITE</color></size>\n" : "";
            if (!showResult)
            {
                c.bg.color = sel ? CardPicked : CardIdle;
                c.text.text = mark + "<b>" + c.item.label + "</b>";
                continue;
            }
            bool right = sel == c.item.prohibited;
            c.bg.color = right ? CardRight : CardWrong;
            string verdict = c.item.prohibited
                ? (sel ? "<color=#BFF5D6>Bien: no se permite</color>" : "<color=#FFE2B0>Faltó marcarla: no se permite</color>")
                : (sel ? "<color=#FFE2B0>Esta sí se permite</color>" : "<color=#BFF5D6>Bien: sí se permite</color>");
            string why = Educational && !right ? "\n<size=75%>" + c.item.why + "</size>" : "";
            c.text.text = "<b>" + c.item.label + "</b>\n<size=80%>" + verdict + "</size>" + why;
        }
    }

    void Check()
    {
        checkedOnce = true;
        errors.Clear();
        int wrong = 0;
        for (int i = 0; i < cards.Count; i++)
        {
            var it = cards[i].item;
            bool sel = picked.Contains(i);
            if (sel == it.prohibited) continue;
            wrong++;
            errors.Add(it.prohibited
                ? "Código de vestimenta: no identificaste que «" + it.label + "» no se permite en el laboratorio."
                : "Código de vestimenta: marcaste «" + it.label + "» como no permitido.");
        }
        RefreshCards(true);
        checkButton.gameObject.SetActive(false);
        foreach (var c in cards) c.button.interactable = false;

        if (Educational)
        {
            if (wrong == 0)
            {
                PassedThisSession = true;
                quizResult.text = "<color=#73E6A6><b>¡Correcto!</b></color> Ya conoces el código de vestimenta. Recuerda llevarlo siempre.";
                backButton.gameObject.SetActive(false);
                startButton.gameObject.SetActive(true);
            }
            else
            {
                quizResult.text = "<color=#FFB070><b>Tienes " + wrong + (wrong == 1 ? " equivocación" : " equivocaciones") + ".</b></color> Lee la explicación en las tarjetas naranjas y vuelve a intentarlo.";
                retryButton.gameObject.SetActive(true);
            }
        }
        else
        {
            int ok = cards.Count - wrong;
            quizResult.text = wrong == 0
                ? "<color=#73E6A6><b>¡Sin errores!</b></color> " + ok + "/" + cards.Count + " correctas."
                : "<color=#FFB070><b>" + ok + "/" + cards.Count + " correctas.</b></color> Se registraron " + wrong + (wrong == 1 ? " error" : " errores") + " en tu evaluación.";
            backButton.gameObject.SetActive(false);
            startButton.gameObject.SetActive(true);
        }
    }

    void Finish()
    {
        var result = Educational ? new List<string>() : new List<string>(errors);
        var cb = onDone;
        onDone = null;
        Destroy(gameObject);
        cb?.Invoke(result);
    }

    // ------------------------------------------------------------------
    //  Ayudantes de interfaz
    // ------------------------------------------------------------------

    RectTransform Group(Transform parent, string name)
    {
        var go = new GameObject(name, typeof(RectTransform));
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        Stretch(rt);
        return rt;
    }

    RectTransform Panel(Transform parent, string name, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size, Color color, bool sliced = true)
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
        if (sliced && panelSprite != null) { img.sprite = panelSprite; img.type = Image.Type.Sliced; }
        return rt;
    }

    void Bar(RectTransform box, Color c)
    {
        var bar = Panel(box, "Barra", new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), Vector2.zero, new Vector2(8f, 0f), c, false);
        bar.anchorMin = new Vector2(0f, 0f);
        bar.anchorMax = new Vector2(0f, 1f);
        bar.offsetMin = new Vector2(0f, 10f);
        bar.offsetMax = new Vector2(8f, -10f);
    }

    TMP_Text MakeText(Transform parent, string name, string text, float size, Color color, TextAlignmentOptions align,
                  Vector2 offsetMin, Vector2 offsetMax, FontStyles style = FontStyles.Normal)
    {
        var go = new GameObject(name, typeof(RectTransform));
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        Stretch(rt);
        rt.offsetMin = offsetMin;
        rt.offsetMax = offsetMax;
        var t = go.AddComponent<TextMeshProUGUI>();
        if (font != null) t.font = font;
        t.text = text;
        t.fontSize = size;
        t.color = color;
        t.alignment = align;
        t.fontStyle = style;
        t.richText = true;
        t.raycastTarget = false;
        t.textWrappingMode = TextWrappingModes.Normal;
        return t;
    }

    Button MakeButton(Transform parent, string name, string label, Vector2 anchor, Vector2 pos, Vector2 size, Color color, UnityAction onClick)
    {
        var rt = Panel(parent, name, anchor, new Vector2(0.5f, 0f), pos, size, color);
        var b = rt.gameObject.AddComponent<Button>();
        var cb = b.colors;
        cb.normalColor = Color.white;
        cb.highlightedColor = new Color(1.15f, 1.15f, 1.15f);
        cb.pressedColor = new Color(0.8f, 0.8f, 0.8f);
        cb.disabledColor = new Color(0.55f, 0.55f, 0.55f, 0.6f);
        b.colors = cb;
        b.onClick.AddListener(onClick);
        MakeText(rt, "Label", label, 24f, Color.white, TextAlignmentOptions.Center, new Vector2(8f, 2f), new Vector2(-8f, -2f), FontStyles.Bold);
        return b;
    }

    static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
    }
}
