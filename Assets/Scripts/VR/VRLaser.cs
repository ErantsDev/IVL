using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// Láser del control derecho (sólo en VR). Es el rayo con el que se apunta para agarrar
/// (CameraView.AimRay lo usa en lugar del centro de la vista) y también funciona como mouse
/// sobre los paneles flotantes: al apuntar a un botón se resalta y el gatillo lo presiona.
/// </summary>
[DefaultExecutionOrder(-150)]
public class VRLaser : MonoBehaviour
{
    public float maxLength = 3f;
    public Color color = new Color(1f, 1f, 1f, 0.55f);
    public Color activeColor = new Color(0.3f, 1f, 0.5f, 0.9f);

    public static VRLaser Instance { get; private set; }
    /// <summary>true si el láser está sobre un elemento de interfaz (botón, texto...).</summary>
    public static bool OverUI => (Instance != null && Instance.uiTarget != null) || (VRScreen.Current != null && VRScreen.Current.OverUI);

    LineRenderer line;
    Transform dot;
    Renderer dotRenderer;
    Material lineMat, dotMat;
    // Sobre los paneles el láser y el punto se dibujan encima (si no, el panel los tapa y no se
    // sabe a dónde se apunta).
    Material lineTopMat, dotTopMat;
    GameObject uiTarget, hoverTarget, lastHover;
    float lastHoverTime;
    PointerEventData ped;
    RaycastResult uiHit;
    Canvas uiCanvas;
    // Presión en curso (como un clic de mouse: se presiona y se suelta).
    GameObject pressTarget, pressClick, dragTarget;
    Canvas pressCanvas;
    float pressTime;
    Transform hoverScaled;
    Vector3 hoverScaleBase;
    readonly List<Canvas> canvasBuffer = new List<Canvas>();
    static readonly RaycastHit[] hits = new RaycastHit[16];

    /// <summary>Rayo del control derecho, si existe.</summary>
    public static bool TryGetRay(out Ray ray)
    {
        if (Instance != null && Instance.isActiveAndEnabled)
        {
            var t = Instance.transform;
            ray = new Ray(t.position, t.forward);
            return true;
        }
        ray = default;
        return false;
    }

    void Awake()
    {
        var mat = new Material(Shader.Find("Sprites/Default"));
        lineMat = mat;
        var ui = Shader.Find("UI/Default");
        if (ui != null)
        {
            lineTopMat = VROnTop.Copy(new Material(ui));
            lineTopMat.renderQueue = 4010;
            dotTopMat = VROnTop.Copy(new Material(ui));
            dotTopMat.renderQueue = 4011;
        }
        line = gameObject.AddComponent<LineRenderer>();
        line.sharedMaterial = mat;
        line.positionCount = 2;
        line.useWorldSpace = true;
        line.widthCurve = AnimationCurve.Linear(0f, 0.004f, 1f, 0.0015f);
        line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        line.receiveShadows = false;

        var d = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        Destroy(d.GetComponent<Collider>());
        d.name = "PuntoLaser";
        d.transform.localScale = Vector3.one * 0.012f;
        dotMat = new Material(mat);
        var r = d.GetComponent<Renderer>();
        dotRenderer = r;
        r.sharedMaterial = dotMat;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        dot = d.transform;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
        if (dot != null) Destroy(dot.gameObject);
    }

    /// <summary>Capa del láser y su punto (el menú en VR usa una capa propia).</summary>
    public void SetLayer(int layer)
    {
        gameObject.layer = layer;
        if (dot != null) dot.gameObject.layer = layer;
    }

    void OnDisable()
    {
        if (dot != null) dot.gameObject.SetActive(false);
        CancelPress();
        SetHover(null);
        if (Instance == this) Instance = null;
    }

    void OnEnable()
    {
        Instance = this;
        if (dot != null) dot.gameObject.SetActive(true);
    }

    void LateUpdate()
    {
        var ray = new Ray(transform.position, transform.forward);
        float len = maxLength;
        bool active = false;

        float uiDist = UpdateUI(ray);
        if (uiDist < float.PositiveInfinity)
        {
            len = uiDist;
            active = uiTarget != null;
        }
        else
        {
            var held = ObjectGrabber.Instance != null ? ObjectGrabber.Instance.Held : null;
            int avatarLayer = LayerMask.NameToLayer(PlayerAvatar.LayerName);
            int mask = ~((1 << 2) | (1 << 5) | (avatarLayer >= 0 ? 1 << avatarLayer : 0));
            int n = Physics.RaycastNonAlloc(ray, hits, maxLength, mask, QueryTriggerInteraction.Ignore);
            float best = maxLength;
            for (int i = 0; i < n; i++)
            {
                if (hits[i].distance >= best) continue;
                if (held != null && hits[i].collider.transform.IsChildOf(held.transform)) continue;
                if (hits[i].collider is CharacterController) continue;
                best = hits[i].distance;
            }
            len = best;
            var g = ObjectGrabber.Instance;
            active = g != null && (g.Aimed != null || g.AimedInteractable != null);
        }

        Vector3 end = ray.GetPoint(len);
        line.SetPosition(0, ray.origin);
        line.SetPosition(1, end);
        var c = active ? activeColor : color;
        line.startColor = c;
        line.endColor = new Color(c.r, c.g, c.b, c.a * 0.3f);
        dot.position = end;
        bool onUI = uiDist < float.PositiveInfinity && lineTopMat != null;
        line.sharedMaterial = onUI ? lineTopMat : lineMat;
        dotRenderer.sharedMaterial = onUI ? dotTopMat : dotMat;
        dot.localScale = Vector3.one * (onUI ? 0.016f : 0.012f);
        dotRenderer.sharedMaterial.color = active ? activeColor : Color.white;
        if (onUI) line.sharedMaterial.color = Color.white;
    }

    // ------------------------------------------------------------------
    //  Interfaz: el láser como mouse sobre los paneles de VRHud
    // ------------------------------------------------------------------

    /// <summary>Distancia al panel que toca el láser (o infinito) y actualiza el elemento apuntado.</summary>
    float UpdateUI(Ray ray)
    {
        uiTarget = null;
        uiCanvas = null;
        var hud = VRHud.Instance;
        var head = VRHeadPose.Current;
        if (hud == null || head == null || EventSystem.current == null) { CancelPress(); SetHover(null); return float.PositiveInfinity; }


        Canvas best = null;
        float bestD = maxLength * 2f;
        Vector3 bestP = default;
        foreach (var c in hud.Panels)
        {
            if (c == null || !c.isActiveAndEnabled) continue;
            var rt = (RectTransform)c.transform;
            var plane = new Plane(rt.forward, rt.position);
            if (!plane.Raycast(ray, out float d) || d > bestD) continue;
            Vector3 p = ray.GetPoint(d);
            Vector2 local = rt.InverseTransformPoint(p);
            if (!rt.rect.Contains(local)) continue;
            best = c; bestD = d; bestP = p;
        }
        if (best == null) { ReleaseIfUp(); SetHover(null); return float.PositiveInfinity; }

        // Elemento de interfaz bajo el láser: el de mayor profundidad de dibujo que lo contenga
        // (lo mismo que hace el GraphicRaycaster, pero sin depender de la cámara de eventos).
        var cam = head.Cam;
        Vector2 sp = cam.WorldToScreenPoint(bestP);
        long bestKey = long.MinValue;
        best.GetComponentsInChildren(false, canvasBuffer); // incluye Canvas anidados
        foreach (var sub in canvasBuffer)
        {
            var graphics = UnityEngine.UI.GraphicRegistry.GetRaycastableGraphicsForCanvas(sub);
            for (int i = 0; i < graphics.Count; i++)
            {
                var g = graphics[i];
                if (g == null || !g.raycastTarget || !g.isActiveAndEnabled || g.depth < 0 || g.canvasRenderer.cull) continue;
                // Lo invisible (p. ej. un aviso con alfa 0) no debe atrapar el láser.
                if (g.canvasRenderer.GetInheritedAlpha() < 0.02f) continue;
                long key = ((long)sub.sortingOrder << 32) + g.depth;
                if (key <= bestKey) continue;
                var grt = g.rectTransform;
                if (!grt.rect.Contains((Vector2)grt.InverseTransformPoint(bestP))) continue;
                if (!g.Raycast(sp, cam)) continue; // respeta CanvasGroup (blocksRaycasts) y máscaras
                bestKey = key;
                uiTarget = g.gameObject;
            }
        }
        if (ped == null) ped = new PointerEventData(EventSystem.current);
        ped.Reset();
        ped.button = PointerEventData.InputButton.Left;
        ped.delta = sp - ped.position;
        ped.position = sp;
        uiCanvas = best;
        uiHit = new RaycastResult
        {
            gameObject = uiTarget,
            module = best.GetComponent<UnityEngine.UI.GraphicRaycaster>(),
            distance = bestD,
            worldPosition = bestP,
            worldNormal = -best.transform.forward,
            screenPosition = sp,
            sortingOrder = best.sortingOrder,
        };
        ped.pointerCurrentRaycast = uiHit;

        // Al jalar el gatillo la mano se mueve un poco: si justo se salió del botón, cuenta el
        // último que estaba apuntado (si fue hace muy poco).
        GameObject target = uiTarget;
        if (target != null) { lastHover = target; lastHoverTime = Time.unscaledTime; }
        else if (lastHover != null && Time.unscaledTime - lastHoverTime < 0.25f) target = lastHover;

        SetHover(uiTarget);

        if (VRInput.PrimaryPressed() && target != null) Press(target);
        else if (pressTarget != null) Hold();

        // Zona transparente del panel: el láser sigue de largo hacia la escena.
        if (uiTarget == null) return float.PositiveInfinity;
        return bestD;
    }

    void Press(GameObject target)
    {
        CancelPress();
        pressTarget = target;
        pressCanvas = uiCanvas;
        pressTime = Time.unscaledTime;
        ped.eligibleForClick = true;
        ped.delta = Vector2.zero;
        ped.dragging = false;
        ped.useDragThreshold = true;
        ped.pressPosition = ped.position;
        ped.pointerPressRaycast = uiHit;
        ped.rawPointerPress = target;

        var down = ExecuteEvents.ExecuteHierarchy(target, ped, ExecuteEvents.pointerDownHandler);
        pressClick = ExecuteEvents.GetEventHandler<IPointerClickHandler>(target);
        ped.pointerPress = down != null ? down : pressClick;

        dragTarget = ExecuteEvents.GetEventHandler<IDragHandler>(target);
        ped.pointerDrag = dragTarget;
        if (dragTarget != null)
        {
            ExecuteEvents.Execute(dragTarget, ped, ExecuteEvents.initializePotentialDrag);
            ExecuteEvents.Execute(dragTarget, ped, ExecuteEvents.beginDragHandler);
            ped.dragging = true;
        }
    }

    void Hold()
    {
        if (!VRInput.PrimaryHeld()) { Release(); return; }
        if (dragTarget != null && ped.dragging) ExecuteEvents.Execute(dragTarget, ped, ExecuteEvents.dragHandler);
    }

    void ReleaseIfUp()
    {
        if (pressTarget != null && !VRInput.PrimaryHeld()) Release();
    }

    void Release()
    {
        if (pressTarget == null) return;
        GameObject overClick = uiTarget != null ? ExecuteEvents.GetEventHandler<IPointerClickHandler>(uiTarget) : null;
        if (ped.pointerPress != null) ExecuteEvents.Execute(ped.pointerPress, ped, ExecuteEvents.pointerUpHandler);
        // Clic si se suelta sobre el mismo botón o si fue un toque rápido (la mano tiembla un poco).
        bool quick = Time.unscaledTime - pressTime < 0.6f;
        if (pressClick != null && (overClick == pressClick || (quick && !(ped.dragging && dragTarget == pressClick))))
            ExecuteEvents.Execute(pressClick, ped, ExecuteEvents.pointerClickHandler);
        if (dragTarget != null && ped.dragging) ExecuteEvents.Execute(dragTarget, ped, ExecuteEvents.endDragHandler);

        // Que el botón no se quede "seleccionado" (con otro color); los campos de texto sí.
        var es = EventSystem.current;
        if (es != null && es.currentSelectedGameObject != null && es.currentSelectedGameObject.GetComponent<TMPro.TMP_InputField>() == null)
            es.SetSelectedGameObject(null);

        pressTarget = pressClick = dragTarget = null;
        ped.pointerPress = ped.rawPointerPress = ped.pointerDrag = null;
        ped.dragging = false;
    }

    void CancelPress()
    {
        if (pressTarget == null) return;
        if (ped != null)
        {
            if (ped.pointerPress != null) ExecuteEvents.Execute(ped.pointerPress, ped, ExecuteEvents.pointerUpHandler);
            if (dragTarget != null && ped.dragging) ExecuteEvents.Execute(dragTarget, ped, ExecuteEvents.endDragHandler);
            ped.pointerPress = ped.rawPointerPress = ped.pointerDrag = null;
            ped.dragging = false;
        }
        pressTarget = pressClick = dragTarget = null;
    }

    void SetHover(GameObject go)
    {
        if (go == hoverTarget) return;
        if (ped == null && EventSystem.current != null) ped = new PointerEventData(EventSystem.current);
        if (hoverTarget != null && ped != null) ExecuteEvents.ExecuteHierarchy(hoverTarget, ped, ExecuteEvents.pointerExitHandler);
        hoverTarget = go;
        if (ped != null) ped.pointerEnter = hoverTarget;
        if (hoverTarget != null && ped != null) ExecuteEvents.ExecuteHierarchy(hoverTarget, ped, ExecuteEvents.pointerEnterHandler);

        // El botón apuntado crece un poco: se nota claramente cuál se va a presionar.
        if (hoverScaled != null) hoverScaled.localScale = hoverScaleBase;
        hoverScaled = null;
        var sel = hoverTarget != null ? hoverTarget.GetComponentInParent<UnityEngine.UI.Selectable>() : null;
        if (sel != null && sel.interactable && sel is UnityEngine.UI.Button)
        {
            hoverScaled = sel.transform;
            hoverScaleBase = hoverScaled.localScale;
            hoverScaled.localScale = hoverScaleBase * 1.06f;
        }
    }
}
