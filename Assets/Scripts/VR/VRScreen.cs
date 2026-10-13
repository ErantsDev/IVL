using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Minijuegos con cámara propia en VR (rasurado y desinfección, vista cenital del lomo): igual que
/// en PC, la cámara del minijuego ocupa toda la vista. Como ahí no se ven los controles, el control
/// derecho mueve un puntero sobre la imagen como si fuera un mouse en el aire (girar la muñeca o
/// mover la mano), y el gatillo hace de clic. La interfaz del minijuego se pasa a un lienzo de esa
/// cámara para que se vea en las gafas.
/// </summary>
[DefaultExecutionOrder(-100)] // el puntero se actualiza antes que el minijuego lo lea
public class VRScreen : MonoBehaviour
{
    /// <summary>Capa propia de la interfaz del minijuego (sólo la ve la cámara del minijuego).</summary>
    public const int ScreenUILayer = 30;

    public static VRScreen Current { get; private set; }

    [Tooltip("Pixeles que se mueve el puntero por cada unidad de movimiento del control.")]
    public float pointerGain = 1.6f;

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
    public static VRScreen Show(Camera cam, RectTransform uiPanel)
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
        }

        s.PointerPixel = new Vector2(cam.pixelWidth * 0.5f, cam.pixelHeight * 0.5f);
        s.HasPointer = true;
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

        // Puntero: el control derecho como mouse en el aire.
        Vector2 p = PointerPixel + VRInput.LookDelta() * pointerGain;
        p.x = Mathf.Clamp(p.x, 0f, Cam.pixelWidth);
        p.y = Mathf.Clamp(p.y, 0f, Cam.pixelHeight);
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
