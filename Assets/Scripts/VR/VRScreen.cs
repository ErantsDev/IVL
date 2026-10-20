using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Minijuegos con cámara propia en VR (rasurado y desinfección, vista cenital del lomo): igual que
/// en PC, la cámara del minijuego ocupa toda la vista. La rasuradora / gasa sigue a la mano
/// derecha como si estuviera sobre el lomo: mover la mano 1 cm la mueve ~1 cm sobre el conejo
/// (en la dirección que se ve en la imagen). Mantener el grip "suelta" la mano para reacomodarla
/// sin mover la rasuradora. El gatillo rasura / hace clic. La interfaz del minijuego se pasa a un
/// lienzo de esa cámara para que se vea en las gafas.
/// </summary>
[DefaultExecutionOrder(-100)] // el puntero se actualiza antes que el minijuego lo lea
public class VRScreen : MonoBehaviour
{
    /// <summary>Capa propia de la interfaz del minijuego (sólo la ve la cámara del minijuego).</summary>
    public const int ScreenUILayer = 30;

    public static VRScreen Current { get; private set; }

    [Tooltip("Sin punto de trabajo: pixeles que se mueve el puntero por unidad de giro del control.")]
    public float pointerGain = 1.6f;
    [Tooltip("Con punto de trabajo: cuánto se mueve la rasuradora por cada cm de la mano (1 = igual).")]
    public float handGain = 1.3f;

    bool hasWork, handInit;
    Vector3 workPoint, handOrigin;
    Vector2 pointerOrigin;

    /// <summary>¿Hay puntero? Y en qué pixel de la cámara del minijuego está.</summary>
    public bool HasPointer { get; set; }
    public Vector2 PointerPixel { get; set; }
    /// <summary>¿El puntero está sobre un elemento de la interfaz del minijuego (botón, panel)?</summary>
    public bool OverUI { get; set; }

    public Camera Cam { get; private set; }
    public Canvas UICanvas { get; private set; }

    RectTransform ui;
    Transform uiParent;
    int uiSibling, uiLayer;
    int camMask;
    GameObject hover;
    PointerEventData ped;
    readonly List<TMP_Text> texts = new List<TMP_Text>();
    readonly Dictionary<TMP_Text, string> translated = new Dictionary<TMP_Text, string>();
    readonly List<Canvas> canvasBuffer = new List<Canvas>();

    /// <summary>Muestra la interfaz <paramref name="uiPanel"/> sobre la vista de <paramref name="cam"/> (que ocupa la vista de las gafas).</summary>
    public static VRScreen Show(Camera cam, RectTransform uiPanel, Vector3? work = null)
    {
        Hide();
        if (cam == null) return null;

        var go = new GameObject("VRMinijuegoPantallaCompleta");
        var s = go.AddComponent<VRScreen>();
        Current = s;
        s.Cam = cam;
        s.camMask = cam.cullingMask;
        cam.cullingMask = s.camMask | (1 << ScreenUILayer);

        var cgo = new GameObject("LienzoMinijuegoVR", typeof(RectTransform));
        cgo.layer = ScreenUILayer;
        cgo.transform.SetParent(go.transform, false);
        s.UICanvas = cgo.AddComponent<Canvas>();
        s.UICanvas.renderMode = RenderMode.ScreenSpaceCamera;
        s.UICanvas.worldCamera = cam;
        s.UICanvas.planeDistance = Mathf.Max(cam.nearClipPlane + 0.02f, 0.08f);
        var scaler = cgo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        if (uiPanel != null)
        {
            s.ui = uiPanel;
            s.uiParent = uiPanel.parent;
            s.uiSibling = uiPanel.GetSiblingIndex();
            s.uiLayer = uiPanel.gameObject.layer;
            uiPanel.SetParent(cgo.transform, false);
            SetLayer(uiPanel, ScreenUILayer);
            uiPanel.GetComponentsInChildren(true, s.texts);
            VROnTop.Apply(uiPanel); // que el lomo no tape las instrucciones
            VRHud.SolidBackgrounds(uiPanel);
        }

        s.PointerPixel = work.HasValue ? (Vector2)cam.WorldToScreenPoint(work.Value) : new Vector2(cam.pixelWidth * 0.5f, cam.pixelHeight * 0.5f);
        s.HasPointer = true;
        s.hasWork = work.HasValue;
        if (work.HasValue) s.workPoint = work.Value;
        return s;
    }

    /// <summary>Deja todo como estaba (la interfaz vuelve a su lugar).</summary>
    public static void Hide()
    {
        var s = Current;
        if (s == null) return;
        Current = null;
        s.SetHover(null);
        if (s.ui != null)
        {
            s.ui.SetParent(s.uiParent, false);
            s.ui.SetSiblingIndex(s.uiSibling);
            SetLayer(s.ui, s.uiLayer);
        }
        if (s.Cam != null) s.Cam.cullingMask = s.camMask;
        Destroy(s.gameObject);
    }

    static void SetLayer(Transform t, int layer)
    {
        foreach (var c in t.GetComponentsInChildren<Transform>(true)) c.gameObject.layer = layer;
    }

    void Update()
    {
        if (Cam == null) return;

        Vector2 p = PointerPixel;
        var hand = VRControllerPose.Right;
        if (hasWork && hand != null)
        {
            // La rasuradora sigue a la mano en escala real sobre el lomo.
            Vector3 hp = hand.transform.position;
            if (!handInit || VRInput.SecondaryHeld()) { handOrigin = hp; pointerOrigin = p; handInit = true; } // grip: reacomodar la mano
            Vector2 a = Cam.WorldToScreenPoint(workPoint);
            Vector2 b = Cam.WorldToScreenPoint(workPoint + Cam.transform.right * 0.01f);
            float pxPerMeter = Vector2.Distance(a, b) * 100f;
            Vector3 d = hp - handOrigin;
            p = pointerOrigin + new Vector2(Vector3.Dot(d, Cam.transform.right), Vector3.Dot(d, Cam.transform.up)) * (pxPerMeter * handGain);
        }
        else
        {
            // Sin punto de trabajo: el control derecho como mouse en el aire.
            p += VRInput.LookDelta() * pointerGain;
        }
        Vector2 clamped = new Vector2(Mathf.Clamp(p.x, 0f, Cam.pixelWidth), Mathf.Clamp(p.y, 0f, Cam.pixelHeight));
        if (clamped != p && hand != null) { handOrigin = hand.transform.position; pointerOrigin = clamped; } // en la orilla no se acumula
        p = clamped;
        PointerPixel = p;

        // Elemento de la interfaz bajo el puntero (y clic con el gatillo).
        GameObject target = null;
        long bestKey = long.MinValue;
        UICanvas.GetComponentsInChildren(false, canvasBuffer);
        foreach (var sub in canvasBuffer)
        {
            var graphics = GraphicRegistry.GetRaycastableGraphicsForCanvas(sub);
            for (int i = 0; i < graphics.Count; i++)
            {
                var g = graphics[i];
                if (g == null || !g.raycastTarget || !g.isActiveAndEnabled || g.depth < 0 || g.canvasRenderer.cull) continue;
                if (g.canvasRenderer.GetInheritedAlpha() < 0.02f) continue;
                long key = ((long)sub.sortingOrder << 32) + g.depth;
                if (key <= bestKey) continue;
                if (!RectTransformUtility.RectangleContainsScreenPoint(g.rectTransform, p, Cam)) continue;
                if (!g.Raycast(p, Cam)) continue;
                bestKey = key;
                target = g.gameObject;
            }
        }
        OverUI = target != null;
        SetHover(target);
        if (target != null && VRInput.PrimaryPressed())
        {
            var clickGO = ExecuteEvents.GetEventHandler<IPointerClickHandler>(target);
            if (clickGO != null && Ped(p) != null)
            {
                ped.pointerPress = clickGO;
                ExecuteEvents.Execute(clickGO, ped, ExecuteEvents.pointerDownHandler);
                ExecuteEvents.Execute(clickGO, ped, ExecuteEvents.pointerUpHandler);
                ExecuteEvents.Execute(clickGO, ped, ExecuteEvents.pointerClickHandler);
            }
        }
    }

    PointerEventData Ped(Vector2 pos)
    {
        if (ped == null && EventSystem.current != null) ped = new PointerEventData(EventSystem.current);
        if (ped != null) { ped.Reset(); ped.button = PointerEventData.InputButton.Left; ped.position = pos; }
        return ped;
    }

    void SetHover(GameObject go)
    {
        if (go == hover) return;
        var e = Ped(PointerPixel);
        if (e == null) { hover = go; return; }
        if (hover != null) ExecuteEvents.ExecuteHierarchy(hover, e, ExecuteEvents.pointerExitHandler);
        hover = go;
        if (hover != null) ExecuteEvents.ExecuteHierarchy(hover, e, ExecuteEvents.pointerEnterHandler);
    }

    void LateUpdate()
    {
        // Textos de teclado y mouse -> botones del control.
        foreach (var t in texts)
        {
            if (t == null || !t.isActiveAndEnabled) continue;
            string cur = t.text;
            if (translated.TryGetValue(t, out var last) && last == cur) continue;
            string tr = VRHud.Translate(cur);
            if (tr != cur) t.text = tr;
            translated[t] = tr;
        }
    }

    void OnDestroy()
    {
        if (Current == this) Current = null;
    }
}
