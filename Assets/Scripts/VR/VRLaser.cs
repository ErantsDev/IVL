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
    public static bool OverUI => Instance != null && Instance.uiTarget != null;

    LineRenderer line;
    Transform dot;
    Material dotMat;
    GameObject uiTarget, hoverTarget;
    PointerEventData ped;
    readonly List<RaycastResult> results = new List<RaycastResult>();
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
        Instance = this;
        var mat = new Material(Shader.Find("Sprites/Default"));
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
        r.sharedMaterial = dotMat;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        dot = d.transform;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
        if (dot != null) Destroy(dot.gameObject);
    }

    void OnDisable()
    {
        if (dot != null) dot.gameObject.SetActive(false);
        SetHover(null);
    }

    void OnEnable()
    {
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
        dotMat.color = active ? activeColor : Color.white;
    }

    // ------------------------------------------------------------------
    //  Interfaz: el láser como mouse sobre los paneles de VRHud
    // ------------------------------------------------------------------

    /// <summary>Distancia al panel que toca el láser (o infinito) y actualiza el elemento apuntado.</summary>
    float UpdateUI(Ray ray)
    {
        uiTarget = null;
        var hud = VRHud.Instance;
        var head = VRHeadPose.Current;
        if (hud == null || head == null || EventSystem.current == null) { SetHover(null); return float.PositiveInfinity; }

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
        if (best == null) { SetHover(null); return float.PositiveInfinity; }

        var gr = best.GetComponent<UnityEngine.UI.GraphicRaycaster>();
        if (gr != null)
        {
            if (ped == null) ped = new PointerEventData(EventSystem.current);
            ped.Reset();
            ped.button = PointerEventData.InputButton.Left;
            ped.position = head.Cam.WorldToScreenPoint(bestP);
            results.Clear();
            gr.Raycast(ped, results);
            if (results.Count > 0)
            {
                uiTarget = results[0].gameObject;
                ped.pointerCurrentRaycast = results[0];
            }
        }
        SetHover(uiTarget);
        // Zona transparente del panel: el láser sigue de largo hacia la escena.
        if (uiTarget == null) return float.PositiveInfinity;

        if (uiTarget != null && VRInput.PrimaryPressed())
        {
            var clickGO = ExecuteEvents.GetEventHandler<IPointerClickHandler>(uiTarget);
            if (clickGO != null)
            {
                ped.pointerPress = clickGO;
                ExecuteEvents.Execute(clickGO, ped, ExecuteEvents.pointerDownHandler);
                ExecuteEvents.Execute(clickGO, ped, ExecuteEvents.pointerUpHandler);
                ExecuteEvents.Execute(clickGO, ped, ExecuteEvents.pointerClickHandler);
            }
        }
        return bestD;
    }

    void SetHover(GameObject go)
    {
        if (go == hoverTarget) return;
        if (ped == null && EventSystem.current != null) ped = new PointerEventData(EventSystem.current);
        if (hoverTarget != null && ped != null) ExecuteEvents.ExecuteHierarchy(hoverTarget, ped, ExecuteEvents.pointerExitHandler);
        hoverTarget = go;
        if (hoverTarget != null && ped != null) ExecuteEvents.ExecuteHierarchy(hoverTarget, ped, ExecuteEvents.pointerEnterHandler);
    }
}
