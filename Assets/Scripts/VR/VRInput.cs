using UnityEngine;
using UnityEngine.XR;

/// <summary>
/// Lectura de los controles de las gafas (PICO Neo 3) para <see cref="GameInput"/>.
/// Sólo se usa cuando <see cref="VRMode.Active"/> es verdadero; en PC nunca se llama.
///
///   Joystick izquierdo ............ caminar (hacia donde miras)      (WASD)
///   Clic joystick izquierdo ....... correr                          (Shift)
///   Joystick derecho ← → .......... girar en pasos                  (mouse horizontal)
///   Joystick derecho ↑ ↓ .......... acercar / alejar lo sostenido   (rueda)
///   Gatillo derecho ............... agarrar / soltar / usar         (E, clic)
///   Grip derecho + girar control .. acariciar al conejo, invertir el tubo (clic derecho + mouse)
///   Botón A ....................... acción secundaria / confirmar   (R, Enter)
///   Botón B ....................... cancelar                        (Q)
///   Botón X (izquierdo) ........... traer / regresar el panel del objetivo y materiales
/// </summary>
public static class VRInput
{
    struct Hand
    {
        public bool valid, trigger, grip, primary, secondary, menu, stickClick;
        public Vector2 stick;
        public Quaternion rot;
        public Vector3 pos;
    }

    const float Deadzone = 0.15f;
    const float StickOn = 0.75f, StickOff = 0.35f;
    /// <summary>Grados de giro del control → "píxeles de mouse" (para rotar objetos, invertir el tubo...).</summary>
    const float DegreesToMouse = 0.6f;
    /// <summary>Metros que se mueve el control → "píxeles de mouse".</summary>
    const float MetersToMouse = 1500f;

    static Hand lNow, lPrev, rNow, rPrev;
    static int sampledFrame = -1;
    static int snapEvent, scrollEvent;
    static bool snapArmed = true, scrollArmed = true;
    static Vector2 lookDelta;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        lNow = lPrev = rNow = rPrev = default;
        sampledFrame = -1;
        snapArmed = scrollArmed = true;
    }

    /// <summary>Lee los controles una vez por trama (lo llama VRBootstrap; también se llama solo si hace falta).</summary>
    public static void Sample()
    {
        if (sampledFrame == Time.frameCount) return;
        sampledFrame = Time.frameCount;
        lPrev = lNow; rPrev = rNow;
        lNow = Read(XRNode.LeftHand);
        rNow = Read(XRNode.RightHand);

        // Joystick derecho: horizontal = giro por pasos, vertical = acercar/alejar (un evento por empujón).
        snapEvent = 0;
        float x = rNow.stick.x;
        if (snapArmed && Mathf.Abs(x) > StickOn) { snapEvent = x > 0 ? 1 : -1; snapArmed = false; }
        else if (Mathf.Abs(x) < StickOff) snapArmed = true;

        scrollEvent = 0;
        float y = rNow.stick.y;
        if (scrollArmed && Mathf.Abs(y) > StickOn) { scrollEvent = y > 0 ? 1 : -1; scrollArmed = false; }
        else if (Mathf.Abs(y) < StickOff) scrollArmed = true;

        // Cuánto giró el control derecho desde la trama anterior (en "píxeles de mouse").
        lookDelta = Vector2.zero;
        if (rNow.valid && rPrev.valid)
        {
            Vector3 e = (Quaternion.Inverse(rPrev.rot) * rNow.rot).eulerAngles;
            lookDelta = new Vector2(Mathf.DeltaAngle(0f, e.y), -Mathf.DeltaAngle(0f, e.x)) / Mathf.Max(DegreesToMouse, 0.01f);

            // Y cuánto se movió la mano de lado / arriba (acariciar al conejo "de lado a lado").
            var head = VRHeadPose.Current;
            Quaternion hr = head != null ? head.transform.localRotation : Quaternion.identity;
            Vector3 dp = rNow.pos - rPrev.pos;
            lookDelta += new Vector2(Vector3.Dot(dp, hr * Vector3.right), Vector3.Dot(dp, Vector3.up)) * MetersToMouse;
        }
    }

    static Hand Read(XRNode node)
    {
        var h = new Hand { rot = Quaternion.identity };
        var d = InputDevices.GetDeviceAtXRNode(node);
        if (!d.isValid) return h;
        h.valid = true;
        d.TryGetFeatureValue(CommonUsages.triggerButton, out h.trigger);
        d.TryGetFeatureValue(CommonUsages.gripButton, out h.grip);
        d.TryGetFeatureValue(CommonUsages.primaryButton, out h.primary);
        d.TryGetFeatureValue(CommonUsages.secondaryButton, out h.secondary);
        d.TryGetFeatureValue(CommonUsages.menuButton, out h.menu);
        d.TryGetFeatureValue(CommonUsages.primary2DAxisClick, out h.stickClick);
        d.TryGetFeatureValue(CommonUsages.primary2DAxis, out h.stick);
        if (!d.TryGetFeatureValue(CommonUsages.deviceRotation, out h.rot)) h.rot = Quaternion.identity;
        d.TryGetFeatureValue(CommonUsages.devicePosition, out h.pos);
        return h;
    }

    static Vector2 Dead(Vector2 v) => v.magnitude < Deadzone ? Vector2.zero : v;

    // ------------------------------------------------------------------
    //  Mismo contrato que GameInput
    // ------------------------------------------------------------------

    /// <summary>Joystick izquierdo, girado según hacia dónde mira la cabeza respecto al cuerpo.</summary>
    public static Vector2 Move()
    {
        Sample();
        Vector2 s = Dead(lNow.stick);
        if (s == Vector2.zero) return s;
        var head = VRHeadPose.Current;
        float yaw = head != null ? head.transform.localEulerAngles.y : 0f;
        Vector3 v = Quaternion.Euler(0f, yaw, 0f) * new Vector3(s.x, 0f, s.y);
        return new Vector2(v.x, v.z);
    }

    public static Vector2 LookDelta() { Sample(); return lookDelta; }
    public static bool SprintHeld() { Sample(); return lNow.stickClick; }
    public static bool JumpPressed() => false;          // saltar en VR marea
    public static bool ToggleViewPressed() => false;    // sin tercera persona en VR

    public static bool PrimaryPressed() { Sample(); return rNow.trigger && !rPrev.trigger; }
    public static bool PrimaryHeld() { Sample(); return rNow.trigger; }
    public static bool SecondaryHeld() { Sample(); return rNow.grip; }
    /// <summary>Pixel del minijuego bajo el láser (VRScreen) o, si no hay, el centro de la vista.</summary>
    public static Vector2 PointerPosition()
    {
        var scr = VRScreen.Current;
        if (scr != null && scr.HasPointer) return scr.PointerPixel; // láser sobre la pantalla del minijuego
        return new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
    }
    public static float Scroll() { Sample(); return scrollEvent; }

    public static bool InteractPressed() => PrimaryPressed();
    public static bool AltInteractPressed() { Sample(); return rNow.primary && !rPrev.primary; }
    /// <summary>Desactivado hasta que el menú de pausa se vea en VR (si no, el usuario quedaría atorado).</summary>
    /// <summary>En el menú principal B regresa (como Esc en PC); en el juego no hay pausa con botón.</summary>
    public static bool PausePressed()
    {
        var flow = GameFlow.Instance;
        return flow != null && flow.CurrentState == GameFlow.State.Menu && CancelPressed();
    }
    public static bool ConfirmPressed() => AltInteractPressed();
    public static bool CancelPressed() { Sample(); return rNow.secondary && !rPrev.secondary; }

    public static bool EmotePressed() => false;
    public static bool EmoteHeld() => false;
    public static int NumberKeyPressed() => -1;
    public static bool NextFieldPressed() => false;
    public static bool ShiftHeld() => false;
    public static bool DebugTogglePressed() => false;

    /// <summary>Joystick derecho vertical (-1..1), con zona muerta: acercar / alejar lo que se sostiene.</summary>
    public static float RightStickY() { Sample(); float y = rNow.stick.y; return Mathf.Abs(y) < Deadzone ? 0f : y; }

    /// <summary>Botón X (control izquierdo): traer / regresar el panel del objetivo y materiales.</summary>
    public static bool HudTogglePressed() { Sample(); return lNow.primary && !lPrev.primary; }

    /// <summary>-1 / 0 / +1: un giro por pasos a la izquierda o derecha en esta trama.</summary>
    public static int SnapTurn() { Sample(); return snapEvent; }
}
