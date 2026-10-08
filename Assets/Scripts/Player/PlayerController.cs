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
