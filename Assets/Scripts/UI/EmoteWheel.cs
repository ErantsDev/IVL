using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// Rueda de emotes: mantener B, mover el mouse hacia un baile y soltar B (o hacer clic).
/// También se puede elegir con las teclas 1–4 mientras la rueda está abierta.
/// Sólo se puede con las manos vacías. Moverse, saltar o agarrar algo cancela el baile.
/// Soltar B con el mouse al centro no elige nada (y detiene el baile en curso).
/// </summary>
public class EmoteWheel : MonoBehaviour
{
    [Serializable]
    public class Emote
    {
        public string label;
        [Tooltip("Texto grande dentro del botón.")]
        public string glyph;
        [Tooltip("Estado de la capa Emotes del Animator. Vacío = saludo con la mano.")]
        public string state;
    }

    public Emote[] emotes =
    {
        new Emote { label = "Saludo", glyph = "Hola", state = "" },
        new Emote { label = "Six Seven", glyph = "6 7", state = "Emote_SixSeven" },
        new Emote { label = "Passinho do Jamal", glyph = "Jamal", state = "Emote_PassinhoJamal" },
        new Emote { label = "Baile de Homero", glyph = "Homero", state = "Emote_BaileHomero" },
    };

    [Header("Apariencia")]
    public TMP_FontAsset font;
    [Tooltip("Sensibilidad del puntero de la rueda (mayor = hay que mover menos el mouse).")]
    public float pointerSensitivity = 1f;

    public static bool IsOpen { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => IsOpen = false;

    static readonly Color Panel = new Color(0.03f, 0.07f, 0.14f, 0.78f);
    static readonly Color ItemColor = new Color(0.08f, 0.14f, 0.25f, 0.92f);
    static readonly Color ItemHover = new Color(0.16f, 0.45f, 0.8f, 0.98f);
    static readonly Color ItemOff = new Color(0.1f, 0.12f, 0.16f, 0.75f);

    GameObject ui;
    RectTransform pointer;
    Image[] itemBg;
    Image[] itemRing;
    TMP_Text[] itemGlyph, itemLabel;
    TMP_Text centerTitle, centerHint;
    Vector2 virtualPointer;
    int selected = -1;
    PlayerController lookOwner;
    bool lookWasEnabled;
    Sprite disc, ring;

    void OnDisable() => CloseWheel(false);

    void Update()
    {
        if (!IsOpen)
        {
            if (OpenPressed() && GameFlow.GameplayInputAllowed) TryOpen();
            return;
        }

        if (!GameFlow.GameplayInputAllowed) { CloseWheel(false); return; }

        // Puntero virtual (el cursor sigue bloqueado): el movimiento del mouse lo empuja.
        Vector2 d = MouseDelta() * pointerSensitivity / Mathf.Max(1f, Screen.height * 0.12f);
        virtualPointer = Vector2.ClampMagnitude(virtualPointer + d, 1f);
        selected = -1;
        if (virtualPointer.magnitude > 0.3f)
        {
            float ang = Mathf.Atan2(virtualPointer.y, virtualPointer.x) * Mathf.Rad2Deg; // 0 = derecha
            // 0 arriba, 1 derecha, 2 abajo, 3 izquierda
            selected = Mathf.RoundToInt(Mathf.Repeat(90f - ang, 360f) / 90f) % 4;
            if (selected >= emotes.Length) selected = -1;
        }
        int key = NumberKey();
        if (key >= 0 && key < emotes.Length) { selected = key; CloseWheel(true); return; }

        Refresh();
        if (!OpenHeld() || ClickPressed()) CloseWheel(true);
    }

    void TryOpen()
    {
        var avatar = PlayerAvatar.Instance;
        if (avatar == null || avatar.Driver == null) return;
        var grabber = ObjectGrabber.Instance;
        if (grabber != null && (grabber.Held != null || grabber.IsReceiving))
        {
            ObjectGrabber.MessageSink?.Invoke("Suelta lo que llevas en la mano para hacer un emote.");
            return;
        }
        if (ui == null) BuildUI();
        IsOpen = true;
        virtualPointer = Vector2.zero;
        selected = -1;
        lookOwner = avatar.GetComponent<PlayerController>();
        if (lookOwner != null) { lookWasEnabled = lookOwner.lookEnabled; lookOwner.lookEnabled = false; }
        ui.SetActive(true);
        Refresh();
    }

    void CloseWheel(bool apply)
    {
        if (!IsOpen) return;
        IsOpen = false;
        if (ui != null) ui.SetActive(false);
        if (lookOwner != null) lookOwner.lookEnabled = lookWasEnabled;
        lookOwner = null;
        if (apply) Perform(selected);
    }

    void Perform(int index)
    {
        var avatar = PlayerAvatar.Instance;
        var driver = avatar != null ? avatar.Driver : null;
        if (driver == null) return;
        if (index < 0) { driver.StopEmote(); return; }
        var e = emotes[index];
        if (string.IsNullOrEmpty(e.state)) { avatar.Wave(); return; }
        if (!driver.HasEmote(e.state))
        {
            ObjectGrabber.MessageSink?.Invoke(e.label + ": próximamente.");
            return;
        }
        driver.PlayEmote(e.state);
    }

    bool Available(int i)
    {
        var driver = PlayerAvatar.Instance != null ? PlayerAvatar.Instance.Driver : null;
        return driver != null && (string.IsNullOrEmpty(emotes[i].state) || driver.HasEmote(emotes[i].state));
    }

    void Refresh()
    {
        for (int i = 0; i < itemBg.Length; i++)
        {
            bool ok = Available(i);
            bool sel = i == selected;
            itemBg[i].color = !ok ? ItemOff : sel ? ItemHover : ItemColor;
            itemRing[i].enabled = sel;
            itemBg[i].rectTransform.localScale = Vector3.one * (sel ? 1.08f : 1f);
            itemGlyph[i].color = ok ? Color.white : new Color(1f, 1f, 1f, 0.35f);
            itemLabel[i].text = ok ? emotes[i].label : emotes[i].label + "\n<size=80%><color=#8A96A8>próximamente</color></size>";
        }
        pointer.anchoredPosition = virtualPointer * 150f;
        var driver = PlayerAvatar.Instance != null ? PlayerAvatar.Instance.Driver : null;
        if (selected >= 0)
        {
            centerTitle.text = emotes[selected].label;
            centerHint.text = Available(selected) ? "Suelta B para hacerlo" : "Próximamente";
        }
        else
        {
            centerTitle.text = "Emotes";
            centerHint.text = driver != null && driver.IsEmoting ? "Suelta B aquí para dejar de bailar" : "Mueve el mouse y suelta B";
        }
    }

    // ------------------------------------------------------------------
    //  Interfaz
    // ------------------------------------------------------------------

    void BuildUI()
    {
        disc = MakeCircle(256, 0f);
        ring = MakeCircle(256, 0.1f);

        ui = new GameObject("UI_RuedaEmotes", typeof(RectTransform));
        ui.transform.SetParent(transform, false);
        var canvas = ui.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 70;
        var scaler = ui.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        var root = Rect(ui.transform, "Rueda", Vector2.zero, new Vector2(640f, 640f));
        var bg = Img(root, "Fondo", Vector2.zero, new Vector2(620f, 620f), disc, Panel);
        bg.raycastTarget = false;

        int n = emotes.Length;
        itemBg = new Image[n];
        itemRing = new Image[n];
        itemGlyph = new TMP_Text[n];
        itemLabel = new TMP_Text[n];
        for (int i = 0; i < n; i++)
        {
            float ang = (90f - 90f * i) * Mathf.Deg2Rad;
            var pos = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * 205f;
            var item = Img(root, "Emote_" + i, pos, new Vector2(186f, 186f), disc, ItemColor);
            itemBg[i] = item;
            itemRing[i] = Img(item.rectTransform, "Anillo", Vector2.zero, new Vector2(200f, 200f), ring, new Color(0.55f, 0.85f, 1f, 1f));
            itemGlyph[i] = Txt(item.rectTransform, "Icono", emotes[i].glyph, 40f, FontStyles.Bold, new Vector2(0f, 22f), new Vector2(170f, 60f));
            itemLabel[i] = Txt(item.rectTransform, "Nombre", emotes[i].label, 19f, FontStyles.Normal, new Vector2(0f, -38f), new Vector2(170f, 60f));
            itemLabel[i].color = new Color(0.85f, 0.9f, 0.97f);
        }

        var center = Img(root, "Centro", Vector2.zero, new Vector2(200f, 200f), disc, new Color(0.02f, 0.05f, 0.1f, 0.92f));
        centerTitle = Txt(center.rectTransform, "Titulo", "Emotes", 24f, FontStyles.Bold, new Vector2(0f, 18f), new Vector2(180f, 60f));
        centerHint = Txt(center.rectTransform, "Pista", "", 15f, FontStyles.Normal, new Vector2(0f, -34f), new Vector2(170f, 50f));
        centerHint.color = new Color(0.72f, 0.8f, 0.9f);

        pointer = Img(root, "Puntero", Vector2.zero, new Vector2(18f, 18f), disc, new Color(1f, 1f, 1f, 0.9f)).rectTransform;

        var foot = Txt(ui.transform, "Ayuda", "Moverte, saltar o agarrar algo detiene el baile   ·   V: cámara en 1ª / 3ª persona", 18f, FontStyles.Normal, new Vector2(0f, -360f), new Vector2(1000f, 40f));
        foot.color = new Color(0.85f, 0.9f, 0.97f, 0.9f);

        ui.SetActive(false);
    }

    static RectTransform Rect(Transform parent, string name, Vector2 pos, Vector2 size)
    {
        var go = new GameObject(name, typeof(RectTransform));
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
        return rt;
    }

    static Image Img(Transform parent, string name, Vector2 pos, Vector2 size, Sprite sprite, Color color)
    {
        var rt = Rect(parent, name, pos, size);
        var img = rt.gameObject.AddComponent<Image>();
        img.sprite = sprite;
        img.color = color;
        img.raycastTarget = false;
        return img;
    }

    TMP_Text Txt(Transform parent, string name, string text, float size, FontStyles style, Vector2 pos, Vector2 box)
    {
        var rt = Rect(parent, name, pos, box);
        var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
        if (font != null) t.font = font;
        t.text = text;
        t.fontSize = size;
        t.fontStyle = style;
        t.alignment = TextAlignmentOptions.Center;
        t.color = Color.white;
        t.raycastTarget = false;
        t.enableAutoSizing = true;
        t.fontSizeMin = size * 0.6f;
        t.fontSizeMax = size;
        return t;
    }

    /// <summary>Círculo (o anillo si <paramref name="ringWidth"/> &gt; 0) con borde suave, generado por código.</summary>
    static Sprite MakeCircle(int size, float ringWidth)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
        var px = new Color32[size * size];
        float r = size * 0.5f, edge = 1.5f;
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(r, r));
                float a = Mathf.Clamp01((r - d) / edge);
                if (ringWidth > 0f) a *= Mathf.Clamp01((d - (r - ringWidth * size)) / edge);
                px[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255f));
            }
        tex.SetPixels32(px);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
    }

    // ------------------------------------------------------------------
    //  Entrada
    // ------------------------------------------------------------------

    static bool OpenPressed()
    {
#if ENABLE_INPUT_SYSTEM
        return Keyboard.current != null && Keyboard.current.bKey.wasPressedThisFrame;
#else
        return Input.GetKeyDown(KeyCode.B);
#endif
    }

    static bool OpenHeld()
    {
#if ENABLE_INPUT_SYSTEM
        return Keyboard.current != null && Keyboard.current.bKey.isPressed;
#else
        return Input.GetKey(KeyCode.B);
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

    static Vector2 MouseDelta()
    {
#if ENABLE_INPUT_SYSTEM
        return Mouse.current != null ? Mouse.current.delta.ReadValue() : Vector2.zero;
#else
        return new Vector2(Input.GetAxis("Mouse X"), Input.GetAxis("Mouse Y")) * 10f;
#endif
    }

    static int NumberKey()
    {
#if ENABLE_INPUT_SYSTEM
        var k = Keyboard.current;
        if (k == null) return -1;
        if (k.digit1Key.wasPressedThisFrame) return 0;
        if (k.digit2Key.wasPressedThisFrame) return 1;
        if (k.digit3Key.wasPressedThisFrame) return 2;
        if (k.digit4Key.wasPressedThisFrame) return 3;
        return -1;
#else
        for (int i = 0; i < 4; i++) if (Input.GetKeyDown(KeyCode.Alpha1 + i)) return i;
        return -1;
#endif
    }

    // ---------- Para pruebas automáticas ----------
    public void TestOpen() => TryOpen();
    public void TestPoint(Vector2 v) { virtualPointer = Vector2.ClampMagnitude(v, 1f); }
    public void TestRelease(int index) { selected = index; CloseWheel(true); }
    public int Selected => selected;
}
