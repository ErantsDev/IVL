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

    static Vector2 ReadMove()
    {
#if ENABLE_INPUT_SYSTEM
        var k = Keyboard.current;
        if (k == null) return Vector2.zero;
        float x = (k.dKey.isPressed || k.rightArrowKey.isPressed ? 1 : 0) - (k.aKey.isPressed || k.leftArrowKey.isPressed ? 1 : 0);
        float y = (k.wKey.isPressed || k.upArrowKey.isPressed ? 1 : 0) - (k.sKey.isPressed || k.downArrowKey.isPressed ? 1 : 0);
        return new Vector2(x, y);
#else
        return new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"));
#endif
    }

    static Vector2 ReadLookDelta()
    {
#if ENABLE_INPUT_SYSTEM
        return Mouse.current != null ? Mouse.current.delta.ReadValue() : Vector2.zero;
#else
        return new Vector2(Input.GetAxis("Mouse X"), Input.GetAxis("Mouse Y")) * 10f;
#endif
    }

    static bool ReadSprint()
    {
#if ENABLE_INPUT_SYSTEM
        return Keyboard.current != null && Keyboard.current.leftShiftKey.isPressed;
#else
        return Input.GetKey(KeyCode.LeftShift);
#endif
    }

    static bool ReadJumpPressed()
    {
#if ENABLE_INPUT_SYSTEM
        return Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame;
#else
        return Input.GetKeyDown(KeyCode.Space);
#endif
    }

    static bool ReadEscapePressed()
    {
#if ENABLE_INPUT_SYSTEM
        return Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame;
#else
        return Input.GetKeyDown(KeyCode.Escape);
#endif
    }

    static bool ReadClickPressed()
    {
#if ENABLE_INPUT_SYSTEM
        return Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame;
#else
        return Input.GetMouseButtonDown(0);
#endif
    }
}
