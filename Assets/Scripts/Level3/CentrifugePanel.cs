using System;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

/// <summary>
/// Panel de control de la centrífuga (pantalla completa, con el cursor libre): velocidad en rpm y
/// tiempo, con botones para subir/bajar, «Iniciar» y «Cancelar». Sólo programa: la validación
/// (2500 rpm y 5 min, tapa, balance, muestra) la hace Level3Manager al presionar Iniciar.
/// </summary>
public class CentrifugePanel : MonoBehaviour
{
    public Sprite panelSprite;
    public TMP_FontAsset font;

    public const int MinRpm = 500, MaxRpm = 5000;
    public const int MinSeconds = 30, MaxSeconds = 900;

    public bool IsOpen { get; private set; }
    public int Rpm { get; private set; }
    public int Seconds { get; private set; }

    static readonly Color PanelColor = new Color(0.04f, 0.08f, 0.15f, 0.96f);
    static readonly Color ScreenColor = new Color(0.01f, 0.05f, 0.03f, 1f);
    static readonly Color Digits = new Color(0.45f, 1f, 0.6f);

    GameObject ui;
    TMP_Text rpmText, timeText, hintText, statusText;
    Func<int, int, string> onStart;
    Action onClose;

    /// <summary>Abre el panel con los valores actuales. start devuelve null si arrancó o el motivo si no.</summary>
    public void Open(int rpm, int seconds, bool educational, Func<int, int, string> start, Action closed)
    {
        if (ui == null) BuildUI();
        Rpm = Mathf.Clamp(rpm, MinRpm, MaxRpm);
        Seconds = Mathf.Clamp(seconds, MinSeconds, MaxSeconds);
        onStart = start;
        onClose = closed;
        hintText.text = educational
            ? "Protocolo de lavado de eritrocitos: <b>2500 rpm</b> durante <b>5 min</b>."
            : "Programa la velocidad y el tiempo del protocolo de lavado.";
        statusText.text = "";
        Refresh();
        ui.SetActive(true);
        IsOpen = true;
        if (GameFlow.Instance != null) GameFlow.Instance.EnterMinigame();
    }

    public void Close()
    {
        if (!IsOpen) return;
        IsOpen = false;
        ui.SetActive(false);
        if (GameFlow.Instance != null) GameFlow.Instance.ExitMinigame();
        var cb = onClose;
        onClose = null;
        cb?.Invoke();
    }

    void AddRpm(int d) { Rpm = Mathf.Clamp(Rpm + d, MinRpm, MaxRpm); statusText.text = ""; Refresh(); }
    void AddTime(int d) { Seconds = Mathf.Clamp(Seconds + d, MinSeconds, MaxSeconds); statusText.text = ""; Refresh(); }

    void Refresh()
    {
        rpmText.text = Rpm + " <size=60%>rpm</size>";
        timeText.text = Centrifuge.Clock(Seconds) + " <size=60%>min</size>";
    }

    void PressStart()
    {
        string problem = onStart?.Invoke(Rpm, Seconds);
        if (string.IsNullOrEmpty(problem)) { statusText.text = ""; Close(); return; }
        statusText.text = problem;
    }

    // ---------- Pruebas ----------
    public void TestSet(int rpm, int seconds) { Rpm = rpm; Seconds = seconds; Refresh(); }
    public string TestStart() { PressStart(); return statusText != null ? statusText.text : null; }
    public void TestStep(int rpmDelta, int secondsDelta) { AddRpm(rpmDelta); AddTime(secondsDelta); }
    public string Status => statusText != null ? statusText.text : "";

    // ---------- Construcción de la interfaz ----------

    void BuildUI()
    {
        ui = new GameObject("UI_Centrifuga", typeof(RectTransform));
        ui.transform.SetParent(transform, false);
        var canvas = ui.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 60;
        var scaler = ui.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        ui.AddComponent<GraphicRaycaster>();

        var dim = Panel(ui.transform, "Oscurecer", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(4000f, 4000f), new Color(0f, 0f, 0f, 0.45f), false);
        dim.GetComponent<Image>().raycastTarget = true;

        var card = Panel(ui.transform, "Panel", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(980f, 600f), PanelColor, true);
        MakeText(card, "Titulo", "Centrífuga · panel de control", 34f, Color.white, TextAlignmentOptions.Top, new Vector2(30f, 20f), new Vector2(-30f, -24f));
        hintText = MakeText(card, "Indicacion", "", 21f, new Color(0.8f, 0.88f, 0.98f), TextAlignmentOptions.Top, new Vector2(30f, 20f), new Vector2(-30f, -78f));

        rpmText = Row(card, "Velocidad", "VELOCIDAD", 120f,
            new[] { ("−500", (UnityAction)(() => AddRpm(-500))), ("−100", () => AddRpm(-100)), ("+100", () => AddRpm(100)), ("+500", () => AddRpm(500)) });
        timeText = Row(card, "Tiempo", "TIEMPO", -40f,
            new[] { ("−1 min", (UnityAction)(() => AddTime(-60))), ("−30 s", () => AddTime(-30)), ("+30 s", () => AddTime(30)), ("+1 min", () => AddTime(60)) });

        statusText = MakeText(card, "Estado", "", 21f, new Color(1f, 0.55f, 0.48f), TextAlignmentOptions.Center, new Vector2(40f, 100f), new Vector2(-40f, -440f));
        statusText.enableAutoSizing = true;
        statusText.fontSizeMin = 14f;
        statusText.fontSizeMax = 21f;

        MakeButton(card, "Cancelar", "Cancelar", new Vector2(-170f, -250f), new Vector2(280f, 64f), new Color(0.26f, 0.32f, 0.44f), Close);
        MakeButton(card, "Iniciar", "Iniciar", new Vector2(170f, -250f), new Vector2(280f, 64f), new Color(0.13f, 0.52f, 0.32f), PressStart);

        ui.SetActive(false);
    }

    TMP_Text Row(Transform card, string name, string label, float y, (string, UnityAction)[] buttons)
    {
        MakeText(card, name + "_Etiqueta", label, 20f, new Color(0.62f, 0.72f, 0.86f), TextAlignmentOptions.Left,
            new Vector2(60f, 0f), new Vector2(0f, 0f), new Vector2(0.5f, 0.5f), new Vector2(-430f, y + 48f), new Vector2(300f, 30f));
        var screen = Panel(card, name + "_Pantalla", new Vector2(0.5f, 0.5f), new Vector2(-260f, y), new Vector2(300f, 84f), ScreenColor, true);
        var t = MakeText(screen, "Valor", "", 46f, Digits, TextAlignmentOptions.Center);
        float x = 0f;
        foreach (var (txt, act) in buttons)
        {
            MakeButton(card, name + "_" + txt, txt, new Vector2(x, y), new Vector2(110f, 70f), new Color(0.18f, 0.26f, 0.4f), act);
            x += 118f;
        }
        return t;
    }

    RectTransform Panel(Transform parent, string name, Vector2 anchor, Vector2 pos, Vector2 size, Color color, bool sliced)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        rt.anchorMin = rt.anchorMax = anchor;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
        var img = go.GetComponent<Image>();
        img.color = color;
        if (sliced && panelSprite != null) { img.sprite = panelSprite; img.type = Image.Type.Sliced; }
        return rt;
    }

    TMP_Text MakeText(Transform parent, string name, string text, float size, Color color, TextAlignmentOptions align,
        Vector2? offsetMin = null, Vector2? offsetMax = null, Vector2? anchor = null, Vector2? pos = null, Vector2? sizeDelta = null)
    {
        var go = new GameObject(name, typeof(RectTransform));
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        if (anchor.HasValue)
        {
            rt.anchorMin = rt.anchorMax = anchor.Value;
            rt.pivot = new Vector2(0f, 0.5f);
            rt.anchoredPosition = pos ?? Vector2.zero;
            rt.sizeDelta = sizeDelta ?? new Vector2(200f, 40f);
        }
        else
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = offsetMin ?? new Vector2(8f, 2f);
            rt.offsetMax = offsetMax ?? new Vector2(-8f, -2f);
        }
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

    Button MakeButton(Transform parent, string name, string label, Vector2 pos, Vector2 size, Color color, UnityAction onClick)
    {
        var rt = Panel(parent, name, new Vector2(0.5f, 0.5f), pos, size, color, true);
        var b = rt.gameObject.AddComponent<Button>();
        var cb = b.colors;
        cb.normalColor = Color.white;
        cb.highlightedColor = new Color(1.18f, 1.18f, 1.18f);
        cb.pressedColor = new Color(0.8f, 0.8f, 0.8f);
        b.colors = cb;
        b.onClick.AddListener(onClick);
        MakeText(rt, "Label", label, 23f, Color.white, TextAlignmentOptions.Center);
        return b;
    }
}
