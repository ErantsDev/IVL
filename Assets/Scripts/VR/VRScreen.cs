using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Pantalla flotante para minijuegos con cámara propia (p. ej. rasurado y desinfección, vista
/// cenital). Sólo en VR. La cámara del minijuego dibuja en una textura que se muestra en una
/// pantalla fija frente al usuario, junto con la interfaz del minijuego (instrucciones, cursor de
/// la rasuradora, escala, botones). El láser del control hace de mouse sobre esa pantalla: así se
/// conserva exactamente la misma dinámica que en PC, sin cambiar la vista de las gafas (no marea).
/// </summary>
public class VRScreen : MonoBehaviour
{
    /// <summary>Capa propia de la interfaz del minijuego (sólo la ve la cámara del minijuego).</summary>
    public const int ScreenUILayer = 30;

    public static VRScreen Current { get; private set; }

    /// <summary>¿El láser está sobre la pantalla? Y en qué pixel de la imagen del minijuego.</summary>
    public bool HasPointer { get; set; }
    public Vector2 PointerPixel { get; set; }
    /// <summary>¿El láser está sobre un elemento de la interfaz del minijuego (botón, panel)?</summary>
    public bool OverUI { get; set; }

    public Camera Cam { get; private set; }
    public Canvas UICanvas { get; private set; }
    public RenderTexture Texture { get; private set; }
    public Transform Quad { get; private set; }

    RectTransform ui;
    Transform uiParent;
    int uiSibling, uiLayer;
    int camMask;
    Material mat;
    readonly List<TMP_Text> texts = new List<TMP_Text>();
    readonly Dictionary<TMP_Text, string> translated = new Dictionary<TMP_Text, string>();

    /// <summary>Muestra la imagen de <paramref name="cam"/> y la interfaz <paramref name="uiPanel"/> en una pantalla frente al usuario.</summary>
    public static VRScreen Show(Camera cam, RectTransform uiPanel, float width = 1.4f, float distance = 1.1f)
    {
        Hide();
        var head = VRHeadPose.Current;
        if (head == null || cam == null) return null;

        var go = new GameObject("VRPantallaMinijuego");
        var s = go.AddComponent<VRScreen>();
        Current = s;
        s.Cam = cam;

        // Textura 16:9 donde dibuja la cámara del minijuego.
        s.Texture = new RenderTexture(1600, 900, 24) { name = "PantallaMinijuegoVR", antiAliasing = 2 };
        s.Texture.Create();
        cam.targetTexture = s.Texture;
        s.camMask = cam.cullingMask;
        cam.cullingMask = (s.camMask & ~(1 << VRHud.UILayer)) | (1 << ScreenUILayer);

        // Interfaz del minijuego: se pasa a un lienzo "Screen Space - Camera" de esa cámara.
        var cgo = new GameObject("LienzoMinijuegoVR", typeof(RectTransform));
        cgo.layer = ScreenUILayer;
        cgo.transform.SetParent(go.transform, false);
        s.UICanvas = cgo.AddComponent<Canvas>();
        s.UICanvas.renderMode = RenderMode.ScreenSpaceCamera;
        s.UICanvas.worldCamera = cam;
        s.UICanvas.planeDistance = cam.nearClipPlane + 0.02f;
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
        }

        // Pantalla frente al usuario, fija en el mundo, un poco abajo de los ojos.
        var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
        Destroy(q.GetComponent<Collider>());
        q.name = "Imagen";
        q.layer = VRHud.UILayer; // la dibuja la cámara de interfaz: nada la tapa
        q.transform.SetParent(go.transform, false);
        Transform h = head.transform;
        Vector3 fwd = Vector3.ProjectOnPlane(h.forward, Vector3.up);
        if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.ProjectOnPlane(h.up, Vector3.up);
        fwd.Normalize();
        q.transform.SetPositionAndRotation(h.position + fwd * distance + Vector3.down * 0.1f, Quaternion.LookRotation(fwd, Vector3.up));
        q.transform.localScale = new Vector3(width, width * 9f / 16f, 1f);
        s.mat = new Material(Shader.Find("Sprites/Default")) { mainTexture = s.Texture };
        var r = q.GetComponent<Renderer>();
        r.sharedMaterial = s.mat;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;
        s.Quad = q.transform;
        return s;
    }

    /// <summary>Quita la pantalla y deja todo como estaba (la interfaz vuelve a su lugar).</summary>
    public static void Hide()
    {
        var s = Current;
        if (s == null) return;
        Current = null;
        if (s.ui != null)
        {
            s.ui.SetParent(s.uiParent, false);
            s.ui.SetSiblingIndex(s.uiSibling);
            SetLayer(s.ui, s.uiLayer);
        }
        if (s.Cam != null)
        {
            s.Cam.targetTexture = null;
            s.Cam.cullingMask = s.camMask;
        }
        if (s.Texture != null) { s.Texture.Release(); Destroy(s.Texture); }
        if (s.mat != null) Destroy(s.mat);
        Destroy(s.gameObject);
    }

    static void SetLayer(Transform t, int layer)
    {
        foreach (var c in t.GetComponentsInChildren<Transform>(true)) c.gameObject.layer = layer;
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
