using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Minijuegos con cámara propia en VR (rasurado y desinfección, vista cenital del lomo).
/// La vista del minijuego se muestra como una pantalla grande frente al usuario, en un espacio
/// aparte (fondo liso, como el lobby del menú): la cámara del minijuego dibuja en una textura y
/// esa textura se ve en un panel con la interfaz del minijuego encima, a distancia cómoda.
/// Se usa como en PC con el mouse: el láser apunta sobre la pantalla y el gatillo rasura / hace
/// clic. (Antes la cámara del minijuego ocupaba los ojos y su interfaz quedaba pegada a la cara:
/// se veía doble y no se podía rasurar.)
/// </summary>
[DefaultExecutionOrder(-100)] // el puntero se actualiza antes que el minijuego lo lea
public class VRScreen : MonoBehaviour
{
    /// <summary>Capa reservada (ya no se usa para la interfaz; se conserva para no dibujarla en los ojos).</summary>
    public const int ScreenUILayer = 30;

    public static VRScreen Current { get; private set; }

    public static float Width = 1.7f;      // m
    public static float Distance = 1.35f;  // m
    public static float Drop = 0.12f;      // m abajo de los ojos
    const int TexW = 1600, TexH = 900;
    const float CanvasW = 1920f, CanvasH = 1080f;

    /// <summary>¿El láser está sobre la pantalla? Y en qué pixel de la cámara del minijuego.</summary>
    public bool HasPointer { get; private set; }
    public Vector2 PointerPixel { get; private set; }
    /// <summary>¿El láser está sobre un elemento de la interfaz del minijuego (botón, encabezado)?</summary>
    public bool OverUI { get; private set; }
    /// <summary>Pixeles del lienzo por pixel de la cámara del minijuego (para el tamaño del pincel).</summary>
    public float PixelToCanvas => CanvasW / TexW;

    public Camera Cam { get; private set; }
    public Canvas ScreenCanvas { get; private set; }

    RenderTexture rt;
    RectTransform ui;
    Transform uiParent;
    int uiSibling;
    RenderTexture camTarget;
    StereoTargetEyeMask camEye;
    // Vista del jugador mientras dura el minijuego: sólo la interfaz y los controles.
    Camera eye;
    CameraClearFlags eyeClear;
    Color eyeBg;
    int eyeMask;
    readonly List<(GameObject go, int layer)> movedLayers = new List<(GameObject, int)>();
    readonly List<Graphic> noRaycast = new List<Graphic>();
    VRLaser laser;
    int laserLayer;

    /// <summary>Muestra la vista de <paramref name="cam"/> en una pantalla frente al usuario con <paramref name="uiPanel"/> encima.</summary>
    public static VRScreen Show(Camera cam, RectTransform uiPanel, Vector3? work = null)
    {
        Hide();
        var head = VRHeadPose.Current;
        if (cam == null || head == null) return null;

        var go = new GameObject("VRPantallaMinijuego");
        var s = go.AddComponent<VRScreen>();
        Current = s;
        s.Cam = cam;

        // La cámara del minijuego ya no va a los ojos: dibuja en una textura.
        s.rt = new RenderTexture(TexW, TexH, 24) { antiAliasing = 2, name = "VRMinijuego" };
        s.camTarget = cam.targetTexture;
        s.camEye = cam.stereoTargetEye;
        cam.stereoTargetEye = StereoTargetEyeMask.None;
        cam.targetTexture = s.rt;
        cam.enabled = true;

        // Lienzo en el mundo, fijo frente al usuario.
        var cgo = new GameObject("PantallaMinijuegoVR", typeof(RectTransform));
        cgo.layer = VRHud.UILayer;
        cgo.transform.SetParent(go.transform, false);
        var canvas = cgo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.worldCamera = head.Cam;
        canvas.sortingOrder = -10; // el HUD (calma del conejo), los avisos y mensajes se ven encima
        cgo.AddComponent<GraphicRaycaster>();
        s.ScreenCanvas = canvas;
        var crt = (RectTransform)cgo.transform;
        crt.sizeDelta = new Vector2(CanvasW, CanvasH);
        crt.pivot = new Vector2(0.5f, 0.5f);
        Transform h = head.transform;
        Vector3 fwd = Vector3.ProjectOnPlane(h.forward, Vector3.up);
        if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.ProjectOnPlane(h.up, Vector3.up);
        var rot = Quaternion.LookRotation(fwd.normalized, Vector3.up);
        crt.SetPositionAndRotation(h.position + rot * Vector3.forward * Distance + Vector3.down * Drop, rot);
        crt.localScale = Vector3.one * (Width / CanvasW);

        // Marco y la imagen del minijuego.
        var frame = new GameObject("Marco", typeof(RectTransform)).AddComponent<Image>();
        frame.gameObject.layer = VRHud.UILayer;
        frame.transform.SetParent(cgo.transform, false);
        frame.rectTransform.anchorMin = Vector2.zero; frame.rectTransform.anchorMax = Vector2.one;
        frame.rectTransform.offsetMin = new Vector2(-14f, -14f); frame.rectTransform.offsetMax = new Vector2(14f, 14f);
        frame.color = new Color(0.16f, 0.47f, 0.86f, 1f);
        frame.raycastTarget = false;
        var img = new GameObject("Vista", typeof(RectTransform)).AddComponent<RawImage>();
        img.gameObject.layer = VRHud.UILayer;
        img.transform.SetParent(cgo.transform, false);
        img.rectTransform.anchorMin = Vector2.zero; img.rectTransform.anchorMax = Vector2.one;
        img.rectTransform.offsetMin = img.rectTransform.offsetMax = Vector2.zero;
        img.texture = s.rt;
        img.raycastTarget = false;

        if (uiPanel != null)
        {
            s.ui = uiPanel;
            s.uiParent = uiPanel.parent;
            s.uiSibling = uiPanel.GetSiblingIndex();
            uiPanel.SetParent(cgo.transform, false);
            // El pincel y la barra de escala siguen al puntero: no deben "tapar" la pantalla.
            foreach (var g in uiPanel.GetComponentsInChildren<Graphic>(true))
                if (g.raycastTarget && g.GetComponentInParent<Selectable>(true) == null && g.GetComponent<TMPro.TMP_Text>() == null
                    && (g.rectTransform.sizeDelta.x < 300f || g.name == "Barra"))
                { g.raycastTarget = false; s.noRaycast.Add(g); }
        }
        VROnTop.Apply(cgo.transform);
        VRHud.SolidBackgrounds(cgo.transform);
        if (VRHud.Instance != null) VRHud.Instance.AddExternal(canvas);

        // Espacio aparte: la vista del jugador sólo dibuja la interfaz y los controles.
        s.eye = head.Cam;
        s.eyeClear = s.eye.clearFlags;
        s.eyeBg = s.eye.backgroundColor;
        s.eyeMask = s.eye.cullingMask;
        s.eye.clearFlags = CameraClearFlags.SolidColor;
        s.eye.backgroundColor = VRLobby.Background;
        s.eye.cullingMask = (1 << VRHud.UILayer) | (1 << VRLobby.Layer);
        foreach (var pose in new[] { VRControllerPose.Left, VRControllerPose.Right })
        {
            if (pose == null) continue;
            foreach (var t in pose.GetComponentsInChildren<Transform>(true))
            { s.movedLayers.Add((t.gameObject, t.gameObject.layer)); t.gameObject.layer = VRLobby.Layer; }
        }
        if (VRLaser.Instance != null) { s.laserLayer = VRLaser.Instance.gameObject.layer; s.laser = VRLaser.Instance; s.laser.SetLayer(VRLobby.Layer); }

        s.PointerPixel = new Vector2(TexW * 0.5f, TexH * 0.5f);
        return s;
    }

    /// <summary>Deja todo como estaba (la interfaz vuelve a su lugar y la vista vuelve al laboratorio).</summary>
    public static void Hide()
    {
        var s = Current;
        if (s == null) return;
        Current = null;
        if (VRHud.Instance != null && s.ScreenCanvas != null) VRHud.Instance.RemoveExternal(s.ScreenCanvas);
        foreach (var g in s.noRaycast) if (g != null) g.raycastTarget = true;
        if (s.ui != null)
        {
            s.ui.SetParent(s.uiParent, false);
            s.ui.SetSiblingIndex(s.uiSibling);
        }
        if (s.Cam != null)
        {
            s.Cam.targetTexture = s.camTarget;
            s.Cam.stereoTargetEye = s.camEye;
        }
        if (s.eye != null)
        {
            s.eye.clearFlags = s.eyeClear;
            s.eye.backgroundColor = s.eyeBg;
            s.eye.cullingMask = s.eyeMask;
        }
        foreach (var (go, layer) in s.movedLayers) if (go != null) go.layer = layer;
        if (s.laser != null) s.laser.SetLayer(s.laserLayer);
        if (s.rt != null) { s.rt.Release(); Destroy(s.rt); }
        Destroy(s.gameObject);
    }

    /// <summary>Posición en el mundo del pixel <paramref name="px"/> de la cámara del minijuego (sobre la pantalla).</summary>
    public Vector3 PixelToWorld(Vector2 px)
    {
        var crt = (RectTransform)ScreenCanvas.transform;
        var local = new Vector2((px.x / TexW - 0.5f) * CanvasW, (px.y / TexH - 0.5f) * CanvasH);
        return crt.TransformPoint(local);
    }

    void Update()
    {
        if (Cam == null || ScreenCanvas == null) return;
        HasPointer = false;
        OverUI = false;
        if (!VRLaser.TryGetRay(out var ray)) return;
        var crt = (RectTransform)ScreenCanvas.transform;
        var plane = new Plane(crt.forward, crt.position);
        if (!plane.Raycast(ray, out float d)) return;
        Vector2 local = crt.InverseTransformPoint(ray.GetPoint(d));
        var r = crt.rect;
        if (!r.Contains(local)) return;
        HasPointer = true;
        PointerPixel = new Vector2((local.x - r.xMin) / r.width * TexW, (local.y - r.yMin) / r.height * TexH);
        // Sobre un botón o el encabezado: el láser hace clic ahí (VRLaser) y no rasura.
        OverUI = VRLaser.Instance != null && VRLaser.Instance.UITarget != null;
    }

    void OnDestroy()
    {
        if (Current == this) Hide();
    }
}
