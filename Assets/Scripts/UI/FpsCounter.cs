using TMPro;
using UnityEngine;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// Contador de FPS en pantalla, pensado para probar el rendimiento del juego en equipos
/// distintos al de desarrollo. Se crea solo al iniciar (no hace falta ponerlo en la escena
/// ni engancharlo a ningún Canvas existente) y sobrevive a los cambios de escena.
/// Muestra los FPS actuales, el mínimo alcanzado (para detectar bajones) y datos básicos
/// del equipo (resolución y tarjeta gráfica) para poder comparar entre laptops.
/// Se oculta/muestra con F1 por si estorba durante una demo.
/// </summary>
public class FpsCounter : MonoBehaviour
{
    const float SampleWindow = 0.4f; // cada cuánto se refresca el texto (s)
    const float WarmupTime = 2f;     // ignora los primeros segundos (carga inicial) al calcular el mínimo

    Canvas canvas;
    TMP_Text text;
    string hardwareInfo = "";

    int frames;
    float accum;
    float elapsed;
    float minFps = float.MaxValue;
    bool visible = true;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (FindAnyObjectByType<FpsCounter>() != null) return;
        var go = new GameObject("FpsCounter");
        DontDestroyOnLoad(go);
        go.AddComponent<FpsCounter>();
    }

    void Awake()
    {
        BuildUI();
        string gpu = SystemInfo.graphicsDeviceName;
        if (gpu.Length > 28) gpu = gpu.Substring(0, 25) + "...";
        hardwareInfo = $"{Screen.width}x{Screen.height} · {gpu}";
    }

    void BuildUI()
    {
        var canvasGO = new GameObject("FpsCounterCanvas");
        canvasGO.transform.SetParent(transform, false);
        canvas = canvasGO.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 5000; // siempre encima de cualquier otro Canvas del juego

        var scaler = canvasGO.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 1f;
        // Nota: sin GraphicRaycaster a propósito, para no bloquear clics de la UI real.

        var bgGO = new GameObject("Fondo", typeof(RectTransform));
        bgGO.transform.SetParent(canvasGO.transform, false);
        var bgRT = bgGO.GetComponent<RectTransform>();
        bgRT.anchorMin = new Vector2(0f, 1f);
        bgRT.anchorMax = new Vector2(0f, 1f);
        bgRT.pivot = new Vector2(0f, 1f);
        bgRT.anchoredPosition = new Vector2(12f, -12f);
        bgRT.sizeDelta = new Vector2(360f, 100f);
        var bgImg = bgGO.AddComponent<Image>();
        bgImg.color = new Color(0f, 0f, 0f, 0.55f);

        var textGO = new GameObject("Texto", typeof(RectTransform));
        textGO.transform.SetParent(bgGO.transform, false);
        var textRT = textGO.GetComponent<RectTransform>();
        textRT.anchorMin = Vector2.zero;
        textRT.anchorMax = Vector2.one;
        textRT.offsetMin = new Vector2(10f, 6f);
        textRT.offsetMax = new Vector2(-10f, -6f);
        text = textGO.AddComponent<TextMeshProUGUI>();
        text.fontSize = 19f;
        text.alignment = TextAlignmentOptions.TopLeft;
        text.richText = true;
        text.text = "FPS: --";
    }

    /// <summary>true si se presionó F1 esta trama, funcione el proyecto con el Input Manager viejo o con el Input System nuevo.</summary>
    static bool F1Pressed() => GameInput.DebugTogglePressed();

    void Update()
    {
        if (F1Pressed())
        {
            visible = !visible;
            canvas.enabled = visible;
        }
        if (!visible) return;

        float dt = Time.unscaledDeltaTime;
        if (dt <= 0f) return; // juego en pausa
        elapsed += dt;
        frames++;
        accum += dt;

        if (accum >= SampleWindow)
        {
            float fps = frames / accum;
            if (elapsed > WarmupTime) minFps = Mathf.Min(minFps, fps);
            string minLabel = minFps == float.MaxValue ? "--" : Mathf.RoundToInt(minFps).ToString();
            Color c = fps >= 50f ? new Color(0.55f, 1f, 0.55f) : fps >= 30f ? new Color(1f, 0.9f, 0.4f) : new Color(1f, 0.45f, 0.45f);
            string hex = ColorUtility.ToHtmlStringRGB(c);
            text.text = $"<b><color=#{hex}>{Mathf.RoundToInt(fps)} FPS</color></b>  (mín {minLabel})\n{hardwareInfo}\n<size=70%>F1: mostrar/ocultar</size>";
            frames = 0;
            accum = 0f;
        }
    }
}
