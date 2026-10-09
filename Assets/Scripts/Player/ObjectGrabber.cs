using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// Va en la cámara del jugador. Apunta a un objeto con Grabbable y:
///   E o clic izquierdo -> agarrar / soltar
///   E (apuntando al autoclave, con un material sin esterilizar en mano) -> meterlo a esterilizar
///   E (apuntando al autoclave, con las manos vacías y un material ya listo dentro) -> sacarlo
///   E / R apuntando a un Interactable (báscula, jaula, paño...) -> sus acciones
///   Clic derecho (mantener) + mover el mouse -> rotar el objeto sostenido
///   Rueda del mouse    -> acercar / alejar el objeto
/// </summary>
public class ObjectGrabber : MonoBehaviour
{
    [Header("Alcance")]
    public float grabDistance = 3f;
    public LayerMask grabMask = ~0;

    [Header("Sostener")]
    public float holdDistance = 1.5f;
    public float minHoldDistance = 0.8f;
    public float maxHoldDistance = 2.5f;
    public float followStrength = 15f;
    public float maxFollowSpeed = 10f;

    [Header("Rotar")]
    [Tooltip("Sensibilidad de rotación al mantener clic derecho y mover el mouse.")]
    public float rotateSensitivity = 3f;

    [Header("UI · Puntero")]
    public bool showCrosshair = true;
    [Tooltip("Largo de cada brazo de la cruz, en píxeles a 1080p (se escala con la resolución).")]
    public float crosshairArm = 12f;
    [Tooltip("Grosor de la cruz, en píxeles a 1080p.")]
    public float crosshairThickness = 3f;
    [Tooltip("Grosor del contorno oscuro, en píxeles a 1080p.")]
    public float crosshairOutline = 2f;
    public Color crosshairColor = Color.white;
    [Tooltip("Color al apuntar a algo con lo que se puede interactuar.")]
    public Color crosshairActiveColor = new Color(0.3f, 1f, 0.5f);

    Camera cam;
    PlayerController playerController;
    Grabbable held;
    Grabbable aimed;
    Autoclave aimedAutoclave;
    Interactable aimedInteractable;
    bool heldUsedGravity;
    RigidbodyInterpolation heldInterpolation;
    bool isRotating;
    float crosshairActive;

    [Header("VR")]
    [Tooltip("Con gafas: qué tan adelante del control queda el centro del objeto sostenido (m).")]
    public float vrHoldForward = 0.09f;
    Quaternion vrHoldRotation = Quaternion.identity;

    static readonly RaycastHit[] hitBuffer = new RaycastHit[32];

    public static ObjectGrabber Instance { get; private set; }

    /// <summary>Texto extra opcional al apuntar un objeto (lo usa la guía del modo educativo).</summary>
    public static System.Func<Grabbable, string> AimHint;

    /// <summary>Dónde se muestran los avisos del agarre (lo conecta el controlador del nivel).</summary>
    public static System.Action<string> MessageSink;

    /// <summary>Objeto que el jugador sostiene en este momento (o null).</summary>
    public Grabbable Held => held;
    public Camera Cam => cam;
    public Interactable AimedInteractable => aimedInteractable;
    public Grabbable Aimed => aimed;

    /// <summary>true mientras un objeto viene volando hacia la mano (p. ej. al sacarlo del autoclave).</summary>
    public bool IsReceiving { get; private set; }
    string receivingLabel;

    /// <summary>Bloquea (o libera) las acciones del jugador mientras un objeto llega a su mano.</summary>
    public void SetReceiving(bool value, string label = null)
    {
        IsReceiving = value;
        receivingLabel = value ? label : null;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        AimHint = null;
        MessageSink = null;
    }

    void Awake()
    {
        cam = GetComponent<Camera>();
        if (cam == null) cam = Camera.main;
        playerController = GetComponentInParent<PlayerController>();
        Instance = this;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    void Update()
    {
        aimed = null;
        aimedAutoclave = null;
        aimedInteractable = null;
        if (!GameFlow.GameplayInputAllowed) { crosshairActive = 0f; return; } // menú, pausa, minijuego o resultados

        // Usamos RaycastNonAlloc + ordenamos por distancia para poder "ver a través" del
        // objeto que llevamos en la mano (si no, el propio objeto sostenido -que va justo
        // frente a la cámara- tapa el rayo y nunca deja apuntar a nada detrás, como el autoclave).
        Ray ray = CameraView.AimRay(cam.transform); // en tercera persona, desde la mira de la pantalla
        // Se incluyen triggers sólo para las zonas de agarre extra (GrabHitbox) de los objetos delgados.
        int count = Physics.RaycastNonAlloc(ray, hitBuffer, grabDistance, grabMask, QueryTriggerInteraction.Collide);
        System.Array.Sort(hitBuffer, 0, count, HitComparer.Instance);

        for (int i = 0; i < count; i++)
        {
            var hit = hitBuffer[i];
            if (hit.collider.isTrigger && hit.collider.GetComponent<GrabHitbox>() == null) continue;
            var hitGrabbable = hit.collider.GetComponentInParent<Grabbable>();

            // Ignora los colliders del objeto que ya llevamos en la mano: no cuentan como
            // "apuntado" y no deben bloquear la detección de lo que hay detrás (autoclave, etc.).
            if (held != null && hitGrabbable == held) continue;
            if (hitGrabbable != null && !hitGrabbable.enabled) hitGrabbable = null;

            if (held == null) aimed = hitGrabbable;
            aimedAutoclave = hit.collider.GetComponentInParent<Autoclave>();
            var inter = hit.collider.GetComponentInParent<Interactable>();
            if (inter != null && inter.enabled) aimedInteractable = inter;
            break;
        }

        // El puntero se agranda y cambia de color cuando hay algo con lo que interactuar.
        bool hasTarget = aimed != null
            || (aimedAutoclave != null && (held != null ? aimedAutoclave.CanLoad(held) : aimedAutoclave.HasReadyItem))
            || (aimedInteractable != null && (aimedInteractable.PromptFor(held, 0) != null || aimedInteractable.PromptFor(held, 1) != null));
        crosshairActive = Mathf.MoveTowards(crosshairActive, hasTarget ? 1f : 0f, Time.unscaledDeltaTime * 10f);

        if (IsReceiving || EmoteWheel.IsOpen) return; // un objeto viene en camino a la mano, o la rueda de emotes está abierta

        if (GrabPressed())
        {
            if (held != null && aimedAutoclave != null && aimedAutoclave.CanLoad(held))
            {
                aimedAutoclave.BeginSterilize(held);
                held = null;
            }
            else if (held != null && aimedAutoclave != null && aimedAutoclave.RefuseReason(held) != null)
            {
                aimedAutoclave.Refuse(held);
            }
            else if (aimedInteractable != null && aimedInteractable.PromptFor(held, 0) != null)
            {
                aimedInteractable.Interact(held, 0);
            }
            else if (held != null)
            {
                string block = held.ReleaseBlocker?.Invoke();
                if (!string.IsNullOrEmpty(block)) MessageSink?.Invoke(block);
                else Release();
            }
            else if (aimedAutoclave != null && aimedAutoclave.HasReadyItem)
            {
                // El material ya esterilizado queda "dentro" del autoclave (con su collider
                // apagado), así que no aparece como `aimed` por el rayo normal: se saca
                // directo a la mano apuntando al autoclave, igual que se metió.
                aimedAutoclave.DeliverReadyItem(this);
            }
            else if (aimed != null)
            {
                string block = aimed.GrabBlocker?.Invoke();
                if (!string.IsNullOrEmpty(block)) MessageSink?.Invoke(block);
                else Grab(aimed);
            }
        }
        else if (SecondaryPressed())
        {
            if (aimedInteractable != null && aimedInteractable.PromptFor(held, 1) != null)
                aimedInteractable.Interact(held, 1);
        }

        UpdateRotation();

        float scroll = ReadScroll();
        if (held != null && !held.customCarry && Mathf.Abs(scroll) > 0.01f)
            holdDistance = Mathf.Clamp(holdDistance + Mathf.Sign(scroll) * 0.15f, minHoldDistance, maxHoldDistance);
    }

    sealed class HitComparer : System.Collections.Generic.IComparer<RaycastHit>
    {
        public static readonly HitComparer Instance = new HitComparer();
        public int Compare(RaycastHit a, RaycastHit b) => a.distance.CompareTo(b.distance);
    }

    void UpdateRotation()
    {
        // Con gafas el objeto gira con la mano, no hace falta rotarlo con el grip.
        bool wantsRotate = !VRMode.Active && held != null && held.allowRotate && !held.customCarry && RotatePressed();

        if (wantsRotate != isRotating)
        {
            isRotating = wantsRotate;
            if (playerController != null)
                playerController.lookEnabled = !isRotating;
        }

        if (!isRotating) return;

        Vector2 delta = ReadLookDelta() * rotateSensitivity;
        if (delta.sqrMagnitude < 0.0000001f) return;

        // Gira el objeto sobre los ejes de la cámara para que se sienta natural sin importar hacia dónde mires.
        held.transform.Rotate(cam.transform.up, delta.x, Space.World);
        held.transform.Rotate(cam.transform.right, -delta.y, Space.World);
    }

    void FixedUpdate()
    {
        if (held == null || held.customCarry) return;

        Vector3 target = cam.transform.position + cam.transform.forward * holdDistance + cam.transform.TransformDirection(held.holdViewOffset);
        var vrHand = VRMode.Active && VRControllerPose.Right != null ? VRControllerPose.Right.transform : null;
        if (vrHand != null) target = vrHand.position + vrHand.forward * vrHoldForward; // con gafas: en la mano
        Vector3 toTarget = target - held.Body.worldCenterOfMass;
        Vector3 velocity = Vector3.ClampMagnitude(toTarget * followStrength, maxFollowSpeed);
#if UNITY_6000_0_OR_NEWER
        held.Body.linearVelocity = velocity;
#else
        held.Body.velocity = velocity;
#endif
        if (vrHand != null)
        {
            // Gira junto con la mano, conservando cómo estaba al agarrarlo.
            Quaternion delta = (vrHand.rotation * vrHoldRotation) * Quaternion.Inverse(held.Body.rotation);
            delta.ToAngleAxis(out float angle, out Vector3 axis);
            if (angle > 180f) angle -= 360f;
            held.Body.angularVelocity = float.IsFinite(axis.x) && Mathf.Abs(angle) > 0.01f
                ? axis.normalized * (angle * Mathf.Deg2Rad * followStrength * 0.6f)
                : Vector3.zero;
        }
        else if (!isRotating)
            held.Body.angularVelocity *= 0.8f;

        // Si el objeto queda atorado muy lejos (p. ej. detrás de una pared), se suelta.
        if (toTarget.magnitude > grabDistance + 1f)
            Release();
    }

    /// <summary>Pone un objeto directamente en la mano del jugador (suelta el anterior).</summary>
    public void Grab(Grabbable g)
    {
        if (g == null) return;
        if (held != null) Release();
        held = g;
        if (!g.customCarry)
        {
            heldUsedGravity = g.Body.useGravity;
            heldInterpolation = g.Body.interpolation;
            g.Body.useGravity = false;
            g.Body.interpolation = RigidbodyInterpolation.Interpolate;
            holdDistance = Mathf.Clamp(Vector3.Distance(cam.transform.position, g.Body.worldCenterOfMass), minHoldDistance, maxHoldDistance);
            if (VRMode.Active && VRControllerPose.Right != null)
                vrHoldRotation = Quaternion.Inverse(VRControllerPose.Right.transform.rotation) * g.Body.rotation;
        }
        g.RaiseGrabbed();
    }

    /// <summary>
    /// Quita el objeto de la mano SIN el evento de "soltar" (quien lo pide se encarga de
    /// colocarlo, p. ej. el conejo sobre la báscula). Devuelve el objeto que se sostenía.
    /// </summary>
    public Grabbable TakeHeld()
    {
        var g = held;
        if (g == null) return null;
        if (!g.customCarry)
        {
            g.Body.useGravity = heldUsedGravity;
            g.Body.interpolation = heldInterpolation;
        }
        held = null;
        StopRotating();
        return g;
    }

    public void Release()
    {
        if (held == null) return;
        var released = held;
        if (!released.customCarry)
        {
            held.Body.useGravity = heldUsedGravity;
            held.Body.interpolation = heldInterpolation;
        }
        held = null;
        StopRotating();

        // Avisa al objeto (p. ej. LabMaterial lo acomoda derecho si se soltó sobre la mesa).
        released.RaiseReleased();
    }

    void StopRotating()
    {
        if (!isRotating) return;
        isRotating = false;
        if (playerController != null)
            playerController.lookEnabled = true;
    }

    void OnDisable() => Release();

    void OnGUI()
    {
        if (VRMode.Active) return; // en las gafas la mira y el aviso los dibuja VRHud
        if (!showCrosshair || !GameFlow.GameplayInputAllowed || EmoteWheel.IsOpen) return; // la rueda de emotes tiene su propia interfaz
        float cx = Mathf.Round(Screen.width / 2f), cy = Mathf.Round(Screen.height / 2f);
        float uiScale = Mathf.Max(0.75f, Screen.height / 1080f);
        DrawCrosshair(cx, cy, uiScale);

        string prompt = BuildPrompt();

        if (prompt != null)
        {
            var style = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = 17, richText = true };
            var size = style.CalcSize(new GUIContent(prompt));
            var rect = new Rect(cx - size.x * 0.5f - 12f, cy + 32f * uiScale, size.x + 24f, size.y + 8f);
            var prev = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.5f);
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = prev;
            GUI.Label(rect, prompt, style);
        }
    }

    /// <summary>Texto del aviso de acción para lo que se está apuntando o sosteniendo (o null).</summary>
    public string BuildPrompt()
    {
        string prompt = null;
        string ePrompt = aimedInteractable != null ? aimedInteractable.PromptFor(held, 0) : null;
        string rPrompt = aimedInteractable != null ? aimedInteractable.PromptFor(held, 1) : null;
        string info = aimedInteractable != null ? aimedInteractable.InfoFor(held) : null;

        if (IsReceiving)
            prompt = receivingLabel ?? "Recibiendo el material...";
        else if (held != null && aimedAutoclave != null && aimedAutoclave.CanLoad(held))
            prompt = $"E: meter {held.DisplayName} en el autoclave";
        else if (held != null && aimedAutoclave != null && aimedAutoclave.RefuseReason(held) != null)
            prompt = "<color=#FFB0A0>" + held.DisplayName + " no va al autoclave</color>";
        else if (ePrompt != null)
            prompt = "E: " + ePrompt;
        else if (held != null)
        {
            string hp = held.HeldPrompt?.Invoke();
            if (!string.IsNullOrEmpty(hp)) prompt = hp;
            else prompt = held.allowRotate && !held.customCarry
                ? $"E / Clic: soltar {held.DisplayName}   ·   Clic derecho (mantener): rotar"
                : $"E / Clic: soltar {held.DisplayName}";
        }
        else if (aimedAutoclave != null && aimedAutoclave.HasReadyItem)
            prompt = "E: sacar el material esterilizado del autoclave";
        else if (aimedAutoclave != null && aimedAutoclave.IsBusy)
            prompt = "Esterilizando... espera a que termine";
        else if (aimed != null)
        {
            prompt = $"E / Clic: agarrar {aimed.DisplayName}";
            var hint = AimHint?.Invoke(aimed);
            if (!string.IsNullOrEmpty(hint)) prompt += "   " + hint;
        }

        if (rPrompt != null) prompt = (prompt != null ? prompt + "\n" : "") + "R: " + rPrompt;
        if (!string.IsNullOrEmpty(info)) prompt = (prompt != null ? prompt + "\n" : "") + "<size=15><color=#CFE3FF>" + info + "</color></size>";
        return prompt;
    }

    /// <summary>
    /// Cruz blanca con contorno oscuro (se ve sobre fondos claros y oscuros). Se escala con la
    /// resolución y, al apuntar a algo interactuable, crece un poco y se vuelve verde.
    /// </summary>
    void DrawCrosshair(float cx, float cy, float uiScale)
    {
        if (Event.current.type != EventType.Repaint) return;
        float k = Mathf.SmoothStep(0f, 1f, crosshairActive);
        float arm = Mathf.Round((crosshairArm + 4f * k) * uiScale);
        float th = Mathf.Max(2f, Mathf.Round(crosshairThickness * uiScale));
        if (((int)th & 1) == 1) { cx += 0.5f; cy += 0.5f; } // grosor impar: centrado exacto en el píxel
        float o = Mathf.Max(1f, Mathf.Round(crosshairOutline * uiScale));

        var prev = GUI.color;
        GUI.color = new Color(0f, 0f, 0f, 0.8f);
        DrawPlus(cx, cy, arm + o, th + 2f * o);
        GUI.color = Color.Lerp(crosshairColor, crosshairActiveColor, k);
        DrawPlus(cx, cy, arm, th);
        GUI.color = prev;
    }

    static void DrawPlus(float cx, float cy, float halfLength, float thickness)
    {
        var tex = Texture2D.whiteTexture;
        float h = thickness * 0.5f;
        GUI.DrawTexture(new Rect(cx - halfLength, cy - h, halfLength * 2f, thickness), tex);
        GUI.DrawTexture(new Rect(cx - h, cy - halfLength, thickness, halfLength * 2f), tex);
    }

    // ---------- Entrada ----------

    static bool GrabPressed() => GameInput.InteractPressed() || (GameInput.PrimaryPressed() && Cursor.lockState == CursorLockMode.Locked);

    static bool SecondaryPressed() => GameInput.AltInteractPressed();

    static bool RotatePressed() => GameInput.SecondaryHeld();

    static Vector2 ReadLookDelta() => GameInput.LookDelta();

    static float ReadScroll() => GameInput.Scroll();
}
