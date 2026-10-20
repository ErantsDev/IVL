using UnityEngine;

/// <summary>
/// Va en el modelo del avatar (junto al Animator humanoide). Traduce el movimiento del
/// jugador a los parámetros del Animator (quieto / caminar / correr / salto / aterrizaje),
/// hace que la cabeza siga la mirada de la cámara y, con IK, que las manos vayan hacia lo
/// que el jugador sostiene (con las dos manos si carga al conejo). Así se ve natural en el espejo.
/// Sin jugador (p. ej. en la vista previa del formulario) sólo respira en reposo.
/// También reproduce los emotes (bailes de cuerpo completo) de la rueda de la tecla B: se cancelan
/// al caminar, saltar, agarrar algo o entrar a un minijuego.
/// </summary>
[RequireComponent(typeof(Animator))]
public class AvatarAnimatorDriver : MonoBehaviour
{
    public static readonly int SpeedId = Animator.StringToHash("Velocidad");
    public static readonly int DirectionId = Animator.StringToHash("Sentido");
    public static readonly int GroundedId = Animator.StringToHash("EnSuelo");
    public static readonly int WaveId = Animator.StringToHash("Saludar");

    [Tooltip("Capa del Animator con los gestos de la parte superior (saludo).")]
    public int gestureLayer = 1;
    [Tooltip("Duración del saludo (s).")]
    public float waveDuration = 1.8f;

    [Header("Emotes")]
    [Tooltip("Capa del Animator con los bailes de cuerpo completo.")]
    public int emoteLayer = 2;
    [Tooltip("Velocidad (m/s) a partir de la cual moverse cancela el baile.")]
    public float cancelSpeed = 0.6f;

    [Header("Mirada e IK")]
    public bool lookAtCamera = true;
    public bool handIK = true;

    Animator anim;
    CharacterController cc;
    Transform cam;
    float airTime;
    float waveTimer;
    float gestureWeight;
    float rightHandW, leftHandW;
    float lookW;
    float emoteWeight;
    float petW;
    RabbitController petRabbit;

    [Header("Acariciar al conejo")]
    [Tooltip("Lado del hueso de la mano (eje Z local) que es la palma: 1 o -1.")]
    public float palmSign = 1f;
    [Tooltip("Distancia de la muñeca al centro de la palma (m).")]
    public float palmCenter = 0.055f;
    [Tooltip("Medio grosor de la mano: separa la palma del lomo (m).")]
    public float palmLift = 0.02f;

    /// <summary>0..1: qué tanto está la mano derecha acariciando al conejo.</summary>
    public float PetWeight => petW;
    float emoteStartTime;
    bool emoteLayerIdle = true;

    /// <summary>Estado de la capa de emotes que se está bailando (o null).</summary>
    public string CurrentEmote { get; private set; }
    public bool IsEmoting => CurrentEmote != null;
    /// <summary>Peso actual de la capa de emotes (0..1), para cámaras o IK.</summary>
    public float EmoteWeight => emoteWeight;

    public bool IsPlayerAvatar => cc != null;

    void Awake()
    {
        anim = GetComponent<Animator>();
        CacheHandAxes(); // pose de reposo, antes de la primera animación
        var pc = GetComponentInParent<PlayerController>();
        if (pc != null)
        {
            cc = pc.GetComponent<CharacterController>();
            cam = pc.cameraTransform;
            // El avatar del jugador casi nunca está a la vista de la cámara en primera persona, pero sus
            // brazos sí (al acariciar al conejo) y su IK debe calcularse siempre.
            anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        }
    }

    void OnEnable()
    {
        if (anim) anim.SetBool(GroundedId, true);
    }

    void Update()
    {
        if (anim == null || anim.runtimeAnimatorController == null) return;
        float dt = Time.deltaTime;
        if (dt <= 0f) return; // juego en pausa
        if (VRMode.Active && cc != null) FollowHeadYaw(dt);

        if (cc != null)
        {
            Vector3 v = cc.velocity;
            Vector3 hv = new Vector3(v.x, 0f, v.z);
            float speed = hv.magnitude;
            // Caminar hacia atrás: la misma animación, reproducida al revés.
            float dir = speed > 0.2f && Vector3.Dot(hv / speed, transform.forward) < -0.35f ? -1f : 1f;
            anim.SetFloat(SpeedId, speed, 0.12f, dt);
            anim.SetFloat(DirectionId, dir);

            // En el aire: sin tocar el piso por un momento, o subiendo rápido (salto).
            airTime = cc.isGrounded ? 0f : airTime + dt;
            bool grounded = airTime < 0.12f && v.y < 1.2f;
            anim.SetBool(GroundedId, grounded);
        }
        else
        {
            anim.SetFloat(SpeedId, 0f);
            anim.SetBool(GroundedId, true);
        }

        UpdateEmote(dt);

        // Peso de la capa de gestos: sube al saludar y baja al terminar.
        if (waveTimer > 0f) waveTimer -= dt;
        float target = waveTimer > 0.25f ? 1f : 0f;
        gestureWeight = Mathf.MoveTowards(gestureWeight, target, dt / 0.2f);
        if (gestureLayer < anim.layerCount) anim.SetLayerWeight(gestureLayer, gestureWeight);
    }

    /// <summary>¿Existe ese baile en el Animator (capa de emotes)?</summary>
    public bool HasEmote(string state)
    {
        if (anim == null || anim.runtimeAnimatorController == null || emoteLayer >= anim.layerCount) return false;
        return anim.HasState(emoteLayer, Animator.StringToHash(state));
    }

    /// <summary>Empieza un baile de cuerpo completo (se repite hasta cancelarlo).</summary>
    public void PlayEmote(string state)
    {
        if (!HasEmote(state)) return;
        waveTimer = 0f;
        CurrentEmote = state;
        emoteStartTime = Time.time;
        emoteLayerIdle = false;
        anim.CrossFadeInFixedTime(state, 0.2f, emoteLayer, 0f);
    }

    public void StopEmote()
    {
        CurrentEmote = null;
    }

    void UpdateEmote(float dt)
    {
        if (IsEmoting && cc != null && Time.time - emoteStartTime > 0.35f)
        {
            Vector3 v = cc.velocity;
            bool moving = new Vector2(v.x, v.z).magnitude > cancelSpeed || v.y > 1.2f;
            bool busy = (ObjectGrabber.Instance != null && ObjectGrabber.Instance.Held != null)
                        || (GameFlow.Instance != null && GameFlow.Instance.InMinigame);
            if (moving || busy) StopEmote();
        }
        emoteWeight = Mathf.MoveTowards(emoteWeight, IsEmoting ? 1f : 0f, dt / 0.25f);
        if (emoteLayer < anim.layerCount)
        {
            anim.SetLayerWeight(emoteLayer, emoteWeight);
            if (!IsEmoting && emoteWeight <= 0f && !emoteLayerIdle)
            {
                anim.Play("Vacio", emoteLayer, 0f);
                emoteLayerIdle = true;
            }
        }
    }

    /// <summary>Saluda con la mano (se ve en el espejo). También se puede caminar mientras.</summary>
    public void Wave()
    {
        if (anim == null) return;
        StopEmote();
        anim.SetTrigger(WaveId);
        waveTimer = waveDuration;
    }

    public bool IsWaving => waveTimer > 0f;

    void OnAnimatorIK(int layerIndex)
    {
        if (layerIndex != 0 || anim == null) return;
        float dt = Time.deltaTime;
        if (VRMode.Active && cc != null) { ApplyVRIK(dt); return; } // con gafas: cuerpo unido a la cabeza y a los controles

        // La cabeza (y un poco el torso) siguen hacia donde mira la cámara.
        float lookTarget = lookAtCamera && cam != null ? 1f - emoteWeight : 0f; // al bailar, la cabeza sigue el baile
        lookW = Mathf.MoveTowards(lookW, lookTarget, dt * 3f);
        if (lookW > 0.001f && cam != null)
        {
            anim.SetLookAtWeight(lookW, 0.2f, 0.85f, 0f, 0.55f);
            anim.SetLookAtPosition(cam.position + cam.forward * 6f);
        }
        else anim.SetLookAtWeight(0f);

        // Manos hacia el objeto que se sostiene.
        Vector3 rightTarget = Vector3.zero, leftTarget = Vector3.zero;
        bool right = false, left = false;
        var grabber = handIK && cc != null ? ObjectGrabber.Instance : null;
        var held = grabber != null ? grabber.Held : null;
        if (held != null && held.gameObject.activeInHierarchy && !IsWaving && !IsEmoting)
        {
            Vector3 c = CenterOf(held);
            var rabbit = held.GetComponent<RabbitController>();
            if (rabbit != null || held.customCarry)
            {
                // En brazos: una mano de cada lado.
                Vector3 side = transform.right * 0.13f;
                rightTarget = c + side - Vector3.up * 0.04f;
                leftTarget = c - side - Vector3.up * 0.04f;
                right = left = true;

                // Acariciando: la mano derecha recorre el lomo de la cabeza a la cola, siguiendo el mouse.
                petRabbit = rabbit;
                float pet = rabbit != null ? rabbit.PetHandWeight : 0f;
                petW = Mathf.MoveTowards(petW, pet, dt * 5f);
                if (petW > 0.001f && rabbit != null)
                {
                    Vector3 up = rabbit.transform.up;
                    Vector3 f = Vector3.ProjectOnPlane(rabbit.PetStrokeDir, up).normalized;
                    Vector3 wrist = rabbit.PetPoint - f * palmCenter + up * palmLift;
                    rightTarget = Vector3.Lerp(rightTarget, wrist, Mathf.SmoothStep(0f, 1f, petW));
                }
            }
            else
            {
                rightTarget = c;
                right = true;
            }
        }
        if (!(held != null && held.GetComponent<RabbitController>() != null) || IsWaving || IsEmoting) petW = Mathf.MoveTowards(petW, 0f, dt * 5f);
        rightHandW = Mathf.MoveTowards(rightHandW, right ? 1f : 0f, dt * 4f);
        leftHandW = Mathf.MoveTowards(leftHandW, left ? 1f : 0f, dt * 4f);
        ApplyHand(AvatarIKGoal.RightHand, AvatarIKHint.RightElbow, rightHandW, rightTarget, 1f);
        ApplyHand(AvatarIKGoal.LeftHand, AvatarIKHint.LeftElbow, leftHandW, leftTarget, -1f);
    }

    Vector3 lastRight, lastLeft;

    void LateUpdate()
    {
        if (VRMode.Active && cc != null) { AlignVRWrists(); return; }
        // Después de la animación y el IK: la palma de la mano derecha se apoya sobre el lomo,
        // con los dedos hacia la cola (el humanoide no tiene dedos: basta con girar la mano).
        if (petW <= 0.001f || petRabbit == null || anim == null) return;
        var hand = anim.GetBoneTransform(HumanBodyBones.RightHand);
        if (hand == null) return;
        Vector3 up = petRabbit.transform.up;
        Vector3 f = Vector3.ProjectOnPlane(petRabbit.PetStrokeDir, up);
        if (f.sqrMagnitude < 1e-6f) return;
        var desired = Quaternion.LookRotation(-up * palmSign, f.normalized);
        hand.rotation = Quaternion.Slerp(hand.rotation, desired, Mathf.SmoothStep(0f, 1f, petW));
    }

    void ApplyHand(AvatarIKGoal goal, AvatarIKHint hint, float w, Vector3 target, float sideSign)
    {
        if (w <= 0.001f) { anim.SetIKPositionWeight(goal, 0f); anim.SetIKHintPositionWeight(hint, 0f); return; }
        // Mientras baja el peso, se queda en el último objetivo (no salta al origen).
        if (target == Vector3.zero) target = goal == AvatarIKGoal.RightHand ? lastRight : lastLeft;
        else if (goal == AvatarIKGoal.RightHand) lastRight = target; else lastLeft = target;
        anim.SetIKPositionWeight(goal, w);
        anim.SetIKPosition(goal, target);
        // Codos hacia afuera y abajo, para que el brazo no se doble hacia adentro.
        anim.SetIKHintPositionWeight(hint, w * 0.6f);
        anim.SetIKHintPosition(hint, transform.position + Vector3.up * 1.0f * transform.lossyScale.y + transform.right * (0.45f * sideSign));
    }

    [Header("VR")]
    [Tooltip("Con gafas: cuánto puede bajar la cadera al agacharse (m).")]
    public float vrMaxCrouch = 0.7f;
    [Tooltip("Con gafas: lo que baja la cabeza sólo por inclinarla; menos que esto no es agacharse (m).")]
    public float vrCrouchDeadzone = 0.12f;
    [Tooltip("Con gafas: distancia del control a la muñeca (m), hacia atrás del control.")]
    public float vrWristBack = 0.07f;
    [Tooltip("Con gafas: grados que puede girar la cabeza antes de que el torso la siga.")]
    public float vrBodyTurnDeadzone = 25f;
    [Tooltip("Con gafas: inclinación de la mano respecto al control (grados).")]
    public float vrHandPitch = 25f;
    [Tooltip("Con gafas: 1 = rotación por IK con la convención humanoide; 0 = sólo posición (la muñeca se alinea en LateUpdate).")]
    [Range(0f, 1f)] public float vrHandRotationWeight = 0f;

    // Eje del antebrazo en el espacio local de cada mano (de la pose de reposo): con él la mano se
    // alinea con el control sin depender de cómo esté orientado el esqueleto del modelo.
    Vector3 rightHandAxis, leftHandAxis;
    bool handAxesReady;

    float vrBodyYaw, vrCrouch;
    bool vrYawInit;
    Quaternion vrBaseLocalRot;

    /// <summary>Con gafas: el torso gira hacia donde mira la cabeza (con un margen), para que los brazos no se crucen.</summary>
    void FollowHeadYaw(float dt)
    {
        var head = VRHeadPose.Current;
        if (head == null) return;
        if (!vrYawInit) { vrBaseLocalRot = transform.localRotation; vrYawInit = true; }
        float headYaw = head.transform.localEulerAngles.y;
        float diff = Mathf.DeltaAngle(vrBodyYaw, headYaw);
        if (Mathf.Abs(diff) > vrBodyTurnDeadzone)
            vrBodyYaw += (diff - Mathf.Sign(diff) * vrBodyTurnDeadzone) * (1f - Mathf.Exp(-8f * dt));
        transform.localRotation = Quaternion.Euler(0f, vrBodyYaw, 0f) * vrBaseLocalRot;

        // Agacharse: sólo cuando la cabeza baja más de lo que baja al inclinarla.
        float drop = Mathf.Clamp(-head.transform.localPosition.y - vrCrouchDeadzone, 0f, vrMaxCrouch);
        vrCrouch = Mathf.Lerp(vrCrouch, drop, 1f - Mathf.Exp(-10f * dt));
    }

    /// <summary>
    /// Sólo con gafas. La cabeza mira hacia donde mira el visor (incluida la inclinación), la cadera
    /// baja cuando el usuario se agacha (los pies se quedan en el piso y las rodillas se doblan hacia
    /// adelante) y cada mano va a su control, con su giro. Así el avatar sigue al usuario, también en el espejo.
    /// </summary>
    void ApplyVRIK(float dt)
    {
        // Brazos visibles en primera persona: se ven unidos a los controles.
        var pa = PlayerAvatar.Instance;
        if (pa != null && pa.Hands != null && pa.Hands.FirstPersonArms != null && !pa.Hands.FirstPersonArms.enabled)
            pa.Hands.FirstPersonArms.enabled = true;

        // Cabeza.
        if (cam != null)
        {
            anim.SetLookAtWeight(1f, 0.15f, 1f, 0f, 0.75f);
            anim.SetLookAtPosition(cam.position + cam.forward * 6f);
        }

        // Agacharse: cadera abajo, pies fijos en el piso bajo el cuerpo, rodillas hacia adelante.
        Transform root = transform;
        if (vrCrouch > 0.02f)
        {
            float scale = root.lossyScale.y;
            anim.bodyPosition += Vector3.down * vrCrouch;
            Vector3 ground = root.position;
            Quaternion footRot = root.rotation;
            Vector3 lf = ground - root.right * 0.11f * scale + Vector3.up * 0.08f * scale;
            Vector3 rf = ground + root.right * 0.11f * scale + Vector3.up * 0.08f * scale;
            float w = Mathf.Clamp01(vrCrouch / 0.08f);
            SetFoot(AvatarIKGoal.LeftFoot, AvatarIKHint.LeftKnee, lf, footRot, w, -1f);
            SetFoot(AvatarIKGoal.RightFoot, AvatarIKHint.RightKnee, rf, footRot, w, 1f);
        }
        else
        {
            SetFoot(AvatarIKGoal.LeftFoot, AvatarIKHint.LeftKnee, Vector3.zero, Quaternion.identity, 0f, -1f);
            SetFoot(AvatarIKGoal.RightFoot, AvatarIKHint.RightKnee, Vector3.zero, Quaternion.identity, 0f, 1f);
        }

        // Manos a los controles.
        var r = VRControllerPose.Right;
        var l = VRControllerPose.Left;
        // Al saludar (espejo) la mano derecha se suelta del control para que se vea la animación.
        rightHandW = Mathf.MoveTowards(rightHandW, r != null && !IsWaving ? 1f : 0f, dt * 4f);
        leftHandW = Mathf.MoveTowards(leftHandW, l != null ? 1f : 0f, dt * 4f);
        VRHand(AvatarIKGoal.RightHand, AvatarIKHint.RightElbow, rightHandW, r != null ? r.transform : null, 1f);
        VRHand(AvatarIKGoal.LeftHand, AvatarIKHint.LeftElbow, leftHandW, l != null ? l.transform : null, -1f);
        petW = 0f;
    }

    void CacheHandAxes()
    {
        if (handAxesReady || anim == null || !anim.isHuman) return;
        rightHandAxis = AxisOf(HumanBodyBones.RightHand, HumanBodyBones.RightLowerArm);
        leftHandAxis = AxisOf(HumanBodyBones.LeftHand, HumanBodyBones.LeftLowerArm);
        handAxesReady = true;
    }

    Vector3 AxisOf(HumanBodyBones handB, HumanBodyBones lowerB)
    {
        var hand = anim.GetBoneTransform(handB);
        var lower = anim.GetBoneTransform(lowerB);
        if (hand == null || lower == null) return Vector3.zero;
        return Quaternion.Inverse(hand.rotation) * (hand.position - lower.position).normalized;
    }

    /// <summary>
    /// Con gafas, después del IK: gira cada mano para que siga la dirección del control (los dedos
    /// hacia donde apunta), conservando el giro natural de la animación.
    /// </summary>
    void AlignVRWrists()
    {
        if (!handAxesReady) return;
        AlignWrist(HumanBodyBones.RightHand, rightHandAxis, VRControllerPose.Right, rightHandW);
        AlignWrist(HumanBodyBones.LeftHand, leftHandAxis, VRControllerPose.Left, leftHandW);
    }

    void AlignWrist(HumanBodyBones handB, Vector3 axisLocal, VRControllerPose ctrl, float w)
    {
        if (ctrl == null || w <= 0.001f || axisLocal == Vector3.zero) return;
        var hand = anim.GetBoneTransform(handB);
        if (hand == null) return;
        Vector3 current = hand.rotation * axisLocal;
        Vector3 desired = ctrl.transform.rotation * Quaternion.Euler(vrHandPitch, 0f, 0f) * Vector3.forward;
        hand.rotation = Quaternion.Slerp(Quaternion.identity, Quaternion.FromToRotation(current, desired), w) * hand.rotation;
    }

    void SetFoot(AvatarIKGoal goal, AvatarIKHint hint, Vector3 pos, Quaternion rot, float w, float side)
    {
        anim.SetIKPositionWeight(goal, w);
        anim.SetIKRotationWeight(goal, w);
        anim.SetIKHintPositionWeight(hint, w);
        if (w <= 0f) return;
        anim.SetIKPosition(goal, pos);
        anim.SetIKRotation(goal, rot);
        Transform t = transform;
        anim.SetIKHintPosition(hint, t.position + Vector3.up * 0.45f * t.lossyScale.y + t.forward * 0.6f + t.right * 0.12f * side);
    }

    void VRHand(AvatarIKGoal goal, AvatarIKHint hint, float w, Transform ctrl, float side)
    {
        if (w <= 0.001f || ctrl == null)
        {
            anim.SetIKPositionWeight(goal, 0f); anim.SetIKRotationWeight(goal, 0f); anim.SetIKHintPositionWeight(hint, 0f);
            return;
        }
        Vector3 wrist = ctrl.position - ctrl.forward * vrWristBack;
        anim.SetIKPositionWeight(goal, w);
        anim.SetIKPosition(goal, wrist);

        // Marco humanoide de la mano: en pose T los dedos apuntan hacia afuera (+X derecha, -X izquierda)
        // y la palma hacia abajo. Se alinea con el control: dedos hacia adelante, palma hacia adentro.
        Quaternion grip = ctrl.rotation * Quaternion.Euler(vrHandPitch, 0f, 0f);
        Quaternion map = side > 0f ? Quaternion.LookRotation(Vector3.up, Vector3.right) : Quaternion.LookRotation(Vector3.up, Vector3.left);
        anim.SetIKRotationWeight(goal, w * vrHandRotationWeight);
        anim.SetIKRotation(goal, grip * map);

        // Codo hacia afuera y abajo, relativo al torso.
        Transform t = transform;
        anim.SetIKHintPositionWeight(hint, w * 0.7f);
        anim.SetIKHintPosition(hint, t.position + Vector3.up * 0.95f * t.lossyScale.y + t.right * (0.5f * side) - t.forward * 0.15f);
    }

    static Vector3 CenterOf(Grabbable g)
    {
        var r = g.GetComponentInChildren<Renderer>();
        return r != null ? r.bounds.center : g.transform.position;
    }
}
