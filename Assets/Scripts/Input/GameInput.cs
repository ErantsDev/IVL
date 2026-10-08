using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// Punto único de entrada del juego. Todos los scripts preguntan aquí por las acciones
/// (moverse, agarrar, confirmar...) en lugar de leer el teclado o el mouse directamente.
///
/// Etapa 1: sólo existe la versión de PC (teclado + mouse), copiada tal cual de los métodos
/// que antes tenía cada script, así que el comportamiento en computadora no cambia.
/// En la etapa de VR se agregará aquí la lectura de los controles de las gafas.
/// </summary>
public static class GameInput
{
    // ------------------------------------------------------------------
    //  Movimiento y vista
    // ------------------------------------------------------------------

    /// <summary>WASD / flechas. x = lateral, y = adelante.</summary>
    public static Vector2 Move()
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

    /// <summary>Movimiento del mouse de esta trama (mirar, rotar objetos, invertir el tubo, rueda de emotes).</summary>
    public static Vector2 LookDelta()
    {
#if ENABLE_INPUT_SYSTEM
        return Mouse.current != null ? Mouse.current.delta.ReadValue() : Vector2.zero;
#else
        return new Vector2(Input.GetAxis("Mouse X"), Input.GetAxis("Mouse Y")) * 10f;
#endif
    }

    /// <summary>Shift izquierdo mantenido.</summary>
    public static bool SprintHeld()
    {
#if ENABLE_INPUT_SYSTEM
        return Keyboard.current != null && Keyboard.current.leftShiftKey.isPressed;
#else
        return Input.GetKey(KeyCode.LeftShift);
#endif
    }

    /// <summary>Espacio.</summary>
    public static bool JumpPressed()
    {
#if ENABLE_INPUT_SYSTEM
        return Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame;
#else
        return Input.GetKeyDown(KeyCode.Space);
#endif
    }

    /// <summary>V: alternar primera / tercera persona.</summary>
    public static bool ToggleViewPressed()
    {
#if ENABLE_INPUT_SYSTEM
        return Keyboard.current != null && Keyboard.current.vKey.wasPressedThisFrame;
#else
        return Input.GetKeyDown(KeyCode.V);
#endif
    }

    // ------------------------------------------------------------------
    //  Puntero (clic izquierdo / derecho, posición, rueda)
    // ------------------------------------------------------------------

    /// <summary>Clic izquierdo presionado esta trama.</summary>
    public static bool PrimaryPressed()
    {
#if ENABLE_INPUT_SYSTEM
        return Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame;
#else
        return Input.GetMouseButtonDown(0);
#endif
    }

    /// <summary>Clic izquierdo mantenido.</summary>
    public static bool PrimaryHeld()
    {
#if ENABLE_INPUT_SYSTEM
        return Mouse.current != null && Mouse.current.leftButton.isPressed;
#else
        return Input.GetMouseButton(0);
#endif
    }

    /// <summary>Clic derecho mantenido (rotar objeto, acariciar al conejo, invertir el tubo).</summary>
    public static bool SecondaryHeld()
    {
#if ENABLE_INPUT_SYSTEM
        return Mouse.current != null && Mouse.current.rightButton.isPressed;
#else
        return Input.GetMouseButton(1);
#endif
    }

    /// <summary>Posición del puntero en pixeles de pantalla (minijuegos).</summary>
    public static Vector2 PointerPosition()
    {
#if ENABLE_INPUT_SYSTEM
        return Mouse.current != null ? Mouse.current.position.ReadValue() : Vector2.zero;
#else
        return Input.mousePosition;
#endif
    }

    /// <summary>Rueda del mouse (acercar / alejar el objeto sostenido).</summary>
    public static float Scroll()
    {
#if ENABLE_INPUT_SYSTEM
        return Mouse.current != null ? Mouse.current.scroll.ReadValue().y : 0f;
#else
        return Input.mouseScrollDelta.y;
#endif
    }

    // ------------------------------------------------------------------
    //  Acciones
    // ------------------------------------------------------------------

    /// <summary>E: agarrar / soltar / acción 0 de un Interactable.</summary>
    public static bool InteractPressed()
    {
#if ENABLE_INPUT_SYSTEM
        return Keyboard.current != null && Keyboard.current.eKey.wasPressedThisFrame;
#else
        return Input.GetKeyDown(KeyCode.E);
#endif
    }

    /// <summary>R: acción 1 de un Interactable.</summary>
    public static bool AltInteractPressed()
    {
#if ENABLE_INPUT_SYSTEM
        return Keyboard.current != null && Keyboard.current.rKey.wasPressedThisFrame;
#else
        return Input.GetKeyDown(KeyCode.R);
#endif
    }

    /// <summary>Esc: pausa / liberar el cursor.</summary>
    public static bool PausePressed()
    {
#if ENABLE_INPUT_SYSTEM
        return Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame;
#else
        return Input.GetKeyDown(KeyCode.Escape);
#endif
    }

    /// <summary>Enter / Enter del teclado numérico.</summary>
    public static bool ConfirmPressed()
    {
#if ENABLE_INPUT_SYSTEM
        return Keyboard.current != null && (Keyboard.current.enterKey.wasPressedThisFrame || Keyboard.current.numpadEnterKey.wasPressedThisFrame);
#else
        return Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter);
#endif
    }

    /// <summary>Q: cancelar / salir del minijuego de la jeringa.</summary>
    public static bool CancelPressed()
    {
#if ENABLE_INPUT_SYSTEM
        return Keyboard.current != null && Keyboard.current.qKey.wasPressedThisFrame;
#else
        return Input.GetKeyDown(KeyCode.Q);
#endif
    }

    // ------------------------------------------------------------------
    //  Rueda de emotes
    // ------------------------------------------------------------------

    /// <summary>B presionada esta trama.</summary>
    public static bool EmotePressed()
    {
#if ENABLE_INPUT_SYSTEM
        return Keyboard.current != null && Keyboard.current.bKey.wasPressedThisFrame;
#else
        return Input.GetKeyDown(KeyCode.B);
#endif
    }

    /// <summary>B mantenida.</summary>
    public static bool EmoteHeld()
    {
#if ENABLE_INPUT_SYSTEM
        return Keyboard.current != null && Keyboard.current.bKey.isPressed;
#else
        return Input.GetKey(KeyCode.B);
#endif
    }

    /// <summary>Teclas 1–4 presionadas esta trama: devuelve 0–3, o -1 si ninguna.</summary>
    public static int NumberKeyPressed()
    {
#if ENABLE_INPUT_SYSTEM
        var k = Keyboard.current;
        if (k == null) return -1;
        if (k.digit1Key.wasPressedThisFrame) return 0;
        if (k.digit2Key.wasPressedThisFrame) return 1;
        if (k.digit3Key.wasPressedThisFrame) return 2;
        if (k.digit4Key.wasPressedThisFrame) return 3;
        return -1;
#else
        for (int i = 0; i < 4; i++) if (Input.GetKeyDown(KeyCode.Alpha1 + i)) return i;
        return -1;
#endif
    }

    // ------------------------------------------------------------------
    //  Formulario y depuración (sólo teclado)
    // ------------------------------------------------------------------

    /// <summary>Tab presionado esta trama.</summary>
    public static bool NextFieldPressed()
    {
#if ENABLE_INPUT_SYSTEM
        return Keyboard.current != null && Keyboard.current.tabKey.wasPressedThisFrame;
#else
        return Input.GetKeyDown(KeyCode.Tab);
#endif
    }

    /// <summary>Cualquier Shift mantenido (Shift+Tab = campo anterior).</summary>
    public static bool ShiftHeld()
    {
#if ENABLE_INPUT_SYSTEM
        var k = Keyboard.current;
        return k != null && (k.leftShiftKey.isPressed || k.rightShiftKey.isPressed);
#else
        return Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
#endif
    }

    /// <summary>F1: mostrar / ocultar el contador de FPS.</summary>
    public static bool DebugTogglePressed()
    {
#if ENABLE_INPUT_SYSTEM
        var kb = Keyboard.current;
        return kb != null && kb.f1Key.wasPressedThisFrame;
#else
        return Input.GetKeyDown(KeyCode.F1);
#endif
    }
}
