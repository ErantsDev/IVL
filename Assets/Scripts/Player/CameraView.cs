using UnityEngine;
using UnityEngine.Rendering.Universal;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// Vista en primera o tercera persona (tecla V). En tercera persona una segunda cámara se coloca
/// detrás y un poco a la derecha del jugador (sobre el hombro), mirando hacia donde mira la
/// cámara de los ojos, y se acerca sola si hay una pared o un mueble detrás.
///
/// La cámara de los ojos sigue siendo la "lógica" del juego (apuntar, sostener objetos, cargar
/// al conejo, IK de la cabeza): sólo deja de dibujarse. Así todo lo demás funciona igual en las
/// dos vistas. Durante los minijuegos no interviene (ellos usan su propia cámara).
/// </summary>
[DefaultExecutionOrder(500)]
public class CameraView : MonoBehaviour
{
    public static CameraView Instance { get; private set; }

    /// <summary>true si el jugador eligió la vista en tercera persona (se recuerda al reintentar).</summary>
    public static bool ThirdPersonPreferred;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => ThirdPersonPreferred = false;

    [Tooltip("Cámara de los ojos del jugador (vacío = la del PlayerController).")]
    public Camera eyeCamera;

    [Header("Tercera persona")]
    public float distance = 2.1f;
    public float height = 0.18f;
    [Tooltip("Desplazamiento a la derecha (sobre el hombro).")]
    public float shoulder = 0.42f;
    public float collisionRadius = 0.16f;
    public float minDistance = 0.3f;
    [Tooltip("Qué tan rápido se aleja otra vez después de acercarse por una pared.")]
    public float returnSpeed = 4f;

    Camera thirdCam;
    float currentDistance;
    bool eyeDisabledByUs;
    readonly RaycastHit[] hits = new RaycastHit[16];

    public bool IsThirdPerson => ThirdPersonPreferred;
    /// <summary>Cámara que está mostrando el juego ahora (tercera persona u ojos).</summary>
    public Camera ActiveCamera => thirdCam != null && thirdCam.enabled ? thirdCam : eyeCamera;
    public Camera ThirdCamera => thirdCam;

    void Awake()
    {
        Instance = this;
        if (eyeCamera == null)
        {
            var pc = GetComponentInParent<PlayerController>();
            if (pc != null && pc.cameraTransform != null) eyeCamera = pc.cameraTransform.GetComponent<Camera>();
        }
        CreateThirdCamera();
        currentDistance = distance;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    void OnDisable()
    {
        if (thirdCam != null) thirdCam.enabled = false;
        RestoreEye();
    }

    void CreateThirdCamera()
    {
        if (eyeCamera == null || thirdCam != null) return;
        var go = new GameObject("CamaraTerceraPersona");
        go.transform.SetParent(transform, false);
        thirdCam = go.AddComponent<Camera>();
        thirdCam.CopyFrom(eyeCamera);
        thirdCam.nearClipPlane = 0.05f;
        int avatarLayer = LayerMask.NameToLayer(PlayerAvatar.LayerName);
        if (avatarLayer >= 0) thirdCam.cullingMask = eyeCamera.cullingMask | (1 << avatarLayer);
        thirdCam.enabled = false;

        var eyeData = eyeCamera.GetComponent<UniversalAdditionalCameraData>();
        var data = go.AddComponent<UniversalAdditionalCameraData>();
        if (eyeData != null)
        {
            data.renderPostProcessing = eyeData.renderPostProcessing;
            data.antialiasing = eyeData.antialiasing;
            data.antialiasingQuality = eyeData.antialiasingQuality;
            data.volumeLayerMask = eyeData.volumeLayerMask;
            data.renderShadows = eyeData.renderShadows;
            data.dithering = eyeData.dithering;
        }
    }

    void Update()
    {
        if (!GameFlow.GameplayInputAllowed) return;
        if (TogglePressed()) SetThirdPerson(!ThirdPersonPreferred);
    }

    public void SetThirdPerson(bool on)
    {
        ThirdPersonPreferred = on;
        currentDistance = minDistance; // al cambiar, la cámara "se aleja" desde el jugador
    }

    void LateUpdate()
    {
        if (eyeCamera == null || thirdCam == null) return;
        bool minigame = GameFlow.Instance != null && GameFlow.Instance.InMinigame;
        if (!ThirdPersonPreferred || minigame)
        {
            thirdCam.enabled = false;
            if (!minigame) RestoreEye();
            else eyeDisabledByUs = false; // el minijuego manda sobre la cámara de los ojos
            return;
        }

        if (eyeCamera.enabled) { eyeCamera.enabled = false; eyeDisabledByUs = true; }
        thirdCam.enabled = true;
        thirdCam.fieldOfView = eyeCamera.fieldOfView;

        Transform eye = eyeCamera.transform;
        Vector3 pivot = eye.position + Vector3.up * height;
        Vector3 desired = pivot - eye.forward * distance + eye.right * shoulder;
        Vector3 dir = desired - pivot;
        float want = dir.magnitude;
        dir /= Mathf.Max(0.0001f, want);

        // Si hay algo entre el jugador y la cámara (pared, mueble), la cámara se acerca.
        float allowed = want;
        int count = Physics.SphereCastNonAlloc(pivot, collisionRadius, dir, hits, want, ~0, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
        {
            var c = hits[i].collider;
            if (hits[i].distance <= 0f) continue;
            if (c is CharacterController || c.transform.IsChildOf(transform.root)) continue;
            var held = ObjectGrabber.Instance != null ? ObjectGrabber.Instance.Held : null;
            if (held != null && c.transform.IsChildOf(held.transform)) continue;
            allowed = Mathf.Min(allowed, Mathf.Max(minDistance, hits[i].distance - 0.02f));
        }
        currentDistance = allowed < currentDistance ? allowed : Mathf.MoveTowards(currentDistance, allowed, returnSpeed * Time.unscaledDeltaTime);
        thirdCam.transform.SetPositionAndRotation(pivot + dir * currentDistance, eye.rotation);
    }

    void RestoreEye()
    {
        if (eyeDisabledByUs && eyeCamera != null) eyeCamera.enabled = true;
        eyeDisabledByUs = false;
    }

    /// <summary>
    /// Rayo para apuntar: en primera persona, el de los ojos; en tercera persona, el del centro de
    /// la pantalla (donde está la mira), empezando a la altura del jugador para no tomar lo que
    /// quede entre la cámara y él.
    /// </summary>
    public static Ray AimRay(Transform eye)
    {
        var cv = Instance;
        if (cv == null || cv.thirdCam == null || !cv.thirdCam.enabled) return new Ray(eye.position, eye.forward);
        Transform c = cv.thirdCam.transform;
        Vector3 origin = c.position;
        float advance = Vector3.Dot(eye.position - origin, c.forward);
        if (advance > 0f) origin += c.forward * advance;
        return new Ray(origin, c.forward);
    }

    static bool TogglePressed() => GameInput.ToggleViewPressed();
}
