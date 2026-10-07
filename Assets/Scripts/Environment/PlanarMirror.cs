using UnityEngine;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Espejo plano en tiempo real (URP). Una cámara auxiliar se coloca en el reflejo del ojo del
/// jugador, detrás de la pared, y mira "a través" del rectángulo del espejo con una proyección
/// asimétrica cuyo plano cercano coincide con el vidrio. Lo que ve se dibuja en una textura
/// que se muestra sobre el espejo: el resultado es un reflejo exacto (sin invertir caras).
///
/// El transform de este objeto define el espejo: centro en su posición, +Z (forward) apunta
/// hacia el salón y el tamaño es <see cref="size"/>. La cámara sólo trabaja cuando el espejo
/// está a la vista del jugador.
/// </summary>
[DefaultExecutionOrder(10000)]
public class PlanarMirror : MonoBehaviour
{
    [Tooltip("Ancho y alto del vidrio (m).")]
    public Vector2 size = new Vector2(1.2f, 1.9f);
    [Tooltip("Alto de la textura del reflejo en píxeles (el ancho sale de la proporción).")]
    public int textureHeight = 1280;
    public LayerMask reflectLayers = ~0;
    [Tooltip("Renderer del vidrio: recibe la textura del reflejo.")]
    public Renderer glass;
    [Tooltip("Distancia máxima a la que se actualiza el reflejo.")]
    public float maxDistance = 14f;

    Camera mirrorCam;
    RenderTexture rt;
    MaterialPropertyBlock block;
    static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");

    public Camera MirrorCamera => mirrorCam;
    public bool IsRendering => mirrorCam != null && mirrorCam.enabled;

    void OnEnable()
    {
        EnsureCamera();
    }

    void OnDisable()
    {
        if (mirrorCam != null) mirrorCam.enabled = false;
    }

    void OnDestroy()
    {
        if (rt != null) { rt.Release(); Destroy(rt); }
        if (mirrorCam != null) Destroy(mirrorCam.gameObject);
    }

    void EnsureCamera()
    {
        if (mirrorCam != null) return;
        int h = Mathf.Max(64, textureHeight);
        int w = Mathf.Max(64, Mathf.RoundToInt(h * size.x / Mathf.Max(0.01f, size.y)));
        var fmt = SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.DefaultHDR) ? RenderTextureFormat.DefaultHDR : RenderTextureFormat.Default;
        rt = new RenderTexture(w, h, 24, fmt) { name = "ReflejoEspejo", antiAliasing = 2, useMipMap = false };
        rt.Create();

        var go = new GameObject("CamaraEspejo");
        go.hideFlags = HideFlags.DontSave;
        go.transform.SetParent(transform, false);
        mirrorCam = go.AddComponent<Camera>();
        mirrorCam.targetTexture = rt;
        mirrorCam.depth = -50;
        mirrorCam.cullingMask = reflectLayers;
        mirrorCam.useOcclusionCulling = false;
        mirrorCam.allowMSAA = true;
        mirrorCam.clearFlags = CameraClearFlags.Skybox;
        mirrorCam.enabled = false;
        var data = go.AddComponent<UniversalAdditionalCameraData>();
        // Sin post-proceso: la cámara del jugador lo aplica una sola vez al ver el espejo.
        data.renderPostProcessing = false;
        data.renderShadows = true;
        data.requiresDepthTexture = false;
        data.requiresColorTexture = false;

        block = new MaterialPropertyBlock();
        if (glass != null)
        {
            glass.GetPropertyBlock(block);
            block.SetTexture(BaseMapId, rt);
            glass.SetPropertyBlock(block);
        }
    }

    /// <summary>Cámara que está mostrando la escena en pantalla (jugador o menú).</summary>
    static Camera ViewerCamera()
    {
        Camera best = null;
        foreach (var c in Camera.allCameras)
        {
            if (c.targetTexture != null || !c.isActiveAndEnabled) continue;
            if (best == null || c.depth > best.depth) best = c;
        }
        return best;
    }

    void LateUpdate()
    {
        EnsureCamera();
        var viewer = ViewerCamera();
        if (viewer == null || viewer == mirrorCam) { mirrorCam.enabled = false; return; }

        Vector3 n = transform.forward;
        Vector3 c = transform.position;
        Vector3 eye = viewer.transform.position;
        float side = Vector3.Dot(eye - c, n);

        // Sólo si el jugador está frente al espejo, cerca y el espejo cae dentro de su vista.
        bool visible = side > 0.02f && side < maxDistance;
        if (visible)
        {
            var planes = GeometryUtility.CalculateFrustumPlanes(viewer);
            var b = new Bounds(c, new Vector3(0.05f, 0.05f, 0.05f));
            b.Encapsulate(transform.TransformPoint(new Vector3(-size.x * 0.5f, -size.y * 0.5f, 0f)));
            b.Encapsulate(transform.TransformPoint(new Vector3(size.x * 0.5f, size.y * 0.5f, 0f)));
            visible = GeometryUtility.TestPlanesAABB(planes, b);
        }
        mirrorCam.enabled = visible;
        if (!visible) return;

        // Ojo reflejado (detrás del espejo) mirando perpendicular hacia el salón.
        Vector3 reflected = eye - 2f * side * n;
        Quaternion rot = Quaternion.LookRotation(n, transform.up);
        mirrorCam.transform.SetPositionAndRotation(reflected, rot);

        // Rectángulo del espejo en coordenadas de la cámara -> proyección asimétrica.
        Vector3 local = Quaternion.Inverse(rot) * (c - reflected);
        float d = local.z;
        float near = Mathf.Max(0.02f, d - 0.004f);
        float k = near / d;
        float l = (local.x - size.x * 0.5f) * k, r = (local.x + size.x * 0.5f) * k;
        float bt = (local.y - size.y * 0.5f) * k, tp = (local.y + size.y * 0.5f) * k;
        float far = near + maxDistance + 10f;
        mirrorCam.nearClipPlane = near;
        mirrorCam.farClipPlane = far;
        mirrorCam.projectionMatrix = Matrix4x4.Frustum(l, r, bt, tp, near, far);
        mirrorCam.cullingMask = reflectLayers;
    }
}
