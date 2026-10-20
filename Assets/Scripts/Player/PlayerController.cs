using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// Controlador en primera persona: WASD para moverse, mouse para mirar,
/// Shift para correr, Espacio para saltar, Esc para liberar el cursor.
/// </summary>
[RequireComponent(typeof(CharacterController))]
public class PlayerController : MonoBehaviour
{
    [Header("Referencias")]
    [Tooltip("Cámara hija del jugador (a la altura de los ojos).")]
    public Transform cameraTransform;

    [Header("Movimiento")]
    /// <summary>Multiplicador global de velocidad (p. ej. más lento al cargar al conejo).</summary>
    public static float SpeedMultiplier = 1f;
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetSpeedMultiplier() => SpeedMultiplier = 1f;

    public float walkSpeed = 3f;
    public float sprintSpeed = 5.5f;
    public float jumpHeight = 1f;
    public float gravity = -9.81f;

    [Header("Cámara")]
    public float mouseSensitivity = 0.15f;
    public float maxLookAngle = 85f;

    [Tooltip("Mientras esté en false, el mouse deja de mover la cámara (usado, por ejemplo, al rotar un objeto agarrado).")]
    public bool lookEnabled = true;

    CharacterController controller;
    float verticalVelocity;
    float pitch;

    void Awake()
    {
        controller = GetComponent<CharacterController>();
        if (cameraTransform == null)
        {
            var cam = GetComponentInChildren<Camera>();
            if (cam != null) cameraTransform = cam.transform;
        }
    }

    void OnEnable() => LockCursor(true);

    void Start() => ResolveSpawnOverlap();

    static readonly Collider[] overlapBuffer = new Collider[16];

    /// <summary>
    /// Si el cuerpo del jugador aparece encimado con un mueble (p. ej. la mesa), el CharacterController
    /// "sube" encima de él. Aquí se busca el lugar libre más cercano, sobre el piso, y se mueve ahí.
    /// Si no hay choque, no cambia nada.
    /// </summary>
    void ResolveSpawnOverlap()
    {
        if (controller == null) return;
        if (!Overlaps(transform.position)) return;
        for (float r = 0.25f; r <= 2.0f; r += 0.25f)
        {
            for (int i = 0; i < 16; i++)
            {
                float a = i * Mathf.PI * 2f / 16f;
                Vector3 p = transform.position + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * r;
                if (Overlaps(p)) continue;
                if (!Physics.Raycast(p + Vector3.up * 0.5f, Vector3.down, 1.0f, ~0, QueryTriggerInteraction.Ignore)) continue; // que haya piso
                controller.enabled = false;
                transform.position = p;
                controller.enabled = true;
                return;
            }
        }
    }

    bool Overlaps(Vector3 pos)
    {
        float radius = controller.radius + 0.02f;
        Vector3 c = pos + transform.rotation * controller.center;
        float half = Mathf.Max(0f, controller.height * 0.5f - radius);
        Vector3 a = c + Vector3.up * half, b = c - Vector3.up * half + Vector3.up * (controller.stepOffset + 0.02f);
        int n = Physics.OverlapCapsuleNonAlloc(a, b, radius, overlapBuffer, ~0, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < n; i++)
        {
            var col = overlapBuffer[i];
            if (col == null || col.transform.IsChildOf(transform)) continue; // el propio jugador y su avatar
            return true;
        }
        return false;
    }

    void Update()
    {
        // Con GameFlow en la escena, el cursor lo controla GameFlow (menú, pausa, resultados).
        if (GameFlow.Instance == null) HandleCursor();
        if (!GameFlow.GameplayInputAllowed) return;

        if (Cursor.lockState == CursorLockMode.Locked && lookEnabled)
            HandleLook();
        HandleMove();
    }

    void HandleLook()
    {
        if (VRMode.Active) return; // con gafas la cabeza mueve la cámara (VRHeadPose)
        mouseSensitivity = GameSettings.MouseSensitivity;
        Vector2 delta = ReadLookDelta() * mouseSensitivity;
        transform.Rotate(Vector3.up, delta.x, Space.World);
        pitch = Mathf.Clamp(pitch - delta.y, -maxLookAngle, maxLookAngle);
        if (cameraTransform != null)
            cameraTransform.localRotation = Quaternion.Euler(pitch, 0f, 0f);
    }

    void HandleMove()
    {
        Vector2 input = ReadMove();
        Vector3 move = transform.right * input.x + transform.forward * input.y;
        if (move.sqrMagnitude > 1f) move.Normalize();

        float speed = (ReadSprint() ? sprintSpeed : walkSpeed) * SpeedMultiplier;

        if (controller.isGrounded)
        {
            if (verticalVelocity < 0f) verticalVelocity = -2f;
            if (ReadJumpPressed())
                verticalVelocity = Mathf.Sqrt(jumpHeight * -2f * gravity);
        }
        verticalVelocity += gravity * Time.deltaTime;

        Vector3 velocity = move * speed + Vector3.up * verticalVelocity;
        controller.Move(velocity * Time.deltaTime);
    }

    void HandleCursor()
    {
        if (ReadEscapePressed()) LockCursor(false);
        else if (Cursor.lockState != CursorLockMode.Locked && ReadClickPressed()) LockCursor(true);
    }

    static void LockCursor(bool locked)
    {
        Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible = !locked;
    }

    // ---------- Entrada (compatible con Input System nuevo y el antiguo) ----------

    static Vector2 ReadMove() => GameInput.Move();

    static Vector2 ReadLookDelta() => GameInput.LookDelta();

    static bool ReadSprint() => GameInput.SprintHeld();

    static bool ReadJumpPressed() => GameInput.JumpPressed();

    static bool ReadEscapePressed() => GameInput.PausePressed();

    static bool ReadClickPressed() => GameInput.PrimaryPressed();
}
