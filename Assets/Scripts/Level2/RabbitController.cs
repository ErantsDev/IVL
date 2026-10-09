using System;
using System.Collections;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

public enum RabbitPlace { InCage, Carried, OnScale, OnPad, Escaped, Leaving }

/// <summary>
/// El conejo del nivel 2: se carga en brazos (sin física), reacciona con animaciones según su
/// estrés (tranquilo, olfateando, nervioso, forcejeo, sobresalto, temblor, relajado, salto) y
/// puede escaparse de las manos si el jugador se mueve con brusquedad. Cuando escapa queda
/// espantado: salta por el salón huyendo del jugador hasta que lo atrapan, y después hay que
/// acariciarlo (clic derecho + mover el mouse) para calmarlo antes de seguir con el procedimiento.
/// </summary>
[RequireComponent(typeof(Grabbable))]
public class RabbitController : Interactable
{
    public Animator animator;
    public SkinnedMeshRenderer skin;

    [Header("Cargar en brazos")]
    public Vector3 carryLocalPosition = new Vector3(0.2f, -0.43f, 0.52f);
    public Vector3 carryLocalEuler = new Vector3(-25f, -55f, 0f);
    [Tooltip("Velocidad del jugador mientras carga al conejo (multiplicador).")]
    public float carrySpeedMultiplier = 0.6f;
    [Tooltip("Suavizado de posición al cargarlo (mayor = sigue más despacio, con más peso).")]
    public float carryPositionSmoothTime = 0.12f;
    [Tooltip("Qué tan rápido gira hacia la orientación de la cámara (mayor = más rígido).")]
    public float carryRotationSpeed = 12f;
    [Tooltip("Balanceo suave al caminar con el conejo en brazos.")]
    public float carrySwayAmount = 0.018f;

    [Header("Estrés")]
    [Tooltip("Velocidad (m/s) a partir de la cual el conejo se estresa al ir en brazos.")]
    public float calmSpeed = 2.1f;
    [Tooltip("Giro de cámara (grados/s) a partir del cual el conejo se estresa.")]
    public float calmTurnSpeed = 140f;
    public float speedStressRate = 30f;
    public float turnStressRate = 0.22f;
    public float jumpStress = 45f;
    public float calmDecay = 9f;

    [Header("Huida (conejo espantado)")]
    [Tooltip("Largo de cada salto (m) con el jugador lejos.")]
    public Vector2 fleeHopDistance = new Vector2(0.45f, 0.8f);
    [Tooltip("Largo de cada salto (m) cuando el jugador se acerca.")]
    public Vector2 fleeAlertHopDistance = new Vector2(0.9f, 1.4f);
    [Tooltip("Duración base de un salto (s).")]
    public float fleeHopTime = 0.38f;
    [Tooltip("Altura base de un salto (m).")]
    public float fleeHopHeight = 0.16f;
    [Tooltip("Pausa entre saltos (s) con el jugador lejos: es la ventana para atraparlo.")]
    public Vector2 fleePause = new Vector2(0.5f, 1.1f);
    [Tooltip("Pausa entre saltos (s) con el jugador cerca.")]
    public Vector2 fleeAlertPause = new Vector2(0.25f, 0.5f);
    [Tooltip("Tiempo (s) que tarda en reaccionar cuando el jugador se le acerca durante una pausa.")]
    public float fleeReaction = 0.3f;
    [Tooltip("Margen (m) extra sobre la distancia de captura: si el jugador entra en él durante una pausa, el conejo reacciona.")]
    public float fleeComfortMargin = 0.7f;
    [Tooltip("Distancia (m) a la que el conejo nota al jugador y salta alejándose.")]
    public float fleeAlertDistance = 2.2f;
    [Tooltip("Distancia máxima (m) a la que se le puede atrapar mientras huye.")]
    public float fleeGrabDistance = 1.2f;
    [Tooltip("Zona del piso por la que puede saltar: mínimo (X, Z).")]
    public Vector2 fleeAreaMin = new Vector2(-2.5f, -0.75f);
    [Tooltip("Zona del piso por la que puede saltar: máximo (X, Z).")]
    public Vector2 fleeAreaMax = new Vector2(2.5f, 1.6f);

    [Header("Calmarlo con caricias")]
    [Tooltip("Por debajo de este estrés el conejo ya está calmado.")]
    public float calmThreshold = 35f;
    [Tooltip("Estrés con el que queda al atraparlo después de escapar.")]
    public float regrabStress = 90f;
    [Tooltip("Segundos, tras atraparlo, en los que moverse no le suma estrés.")]
    public float regrabGrace = 1.5f;
    [Tooltip("Estrés que baja por cada caricia (recorrido del mouse de ~1/4 de la altura de la pantalla).")]
    public float petStressPerStroke = 9f;
    [Tooltip("Máximo de estrés que pueden bajar las caricias por segundo.")]
    public float maxPetRate = 20f;

    public string Code { get; private set; } = "CNJ-0000";
    public float WeightKg { get; private set; } = 3f;
    public bool InRange => WeightKg >= 2.5f && WeightKg <= 4.0f;
    public RabbitPlace Place { get; private set; } = RabbitPlace.InCage;
    public float Stress { get; private set; }
    public bool IsMoving => moveRoutine != null;
    public Grabbable Grabbable { get; private set; }
    public Transform Spine { get; private set; }

    /// <summary>true mientras el conejo espantado salta por el salón.</summary>
    public bool IsFleeing => fleeRoutine != null;
    /// <summary>Segundos que lleva huyendo (0 si no huye).</summary>
    public float FleeElapsed => IsFleeing ? Time.time - fleeStartTime : 0f;
    /// <summary>true a la mitad de un salto (en el aire).</summary>
    public bool IsHopping { get; private set; }
    /// <summary>Veces que se ha escapado de los brazos.</summary>
    public int EscapeCount { get; private set; }
    /// <summary>Tras escaparse: hay que acariciarlo hasta calmarlo antes de colocarlo en otro lugar.</summary>
    public bool NeedsCalming { get; private set; }
    /// <summary>true mientras el jugador lo acaricia (clic derecho mantenido).</summary>
    public bool IsBeingPetted => petting;

    /// <summary>Animación forzada (p. ej. "Temblor" durante la desinfección). null = automática.</summary>
    public string ForcedMood;

    Collider[] colliders;
    Coroutine moveRoutine;
    string currentMood;
    float oneShotUntil;
    float idleSwitchAt;
    bool idleSniff;
    Vector3 lastCamPos;
    float lastCamYaw, lastCamPitch;
    bool hasLast;
    float smoothedSpeed, smoothedTurn;
    Vector3 carryVelocity;
    float swayPhase;
    Coroutine fleeRoutine;
    float fleeStartTime;
    float graceUntil;
    bool petting;
    PlayerController petLookOwner;
    float lastStrokeTime = -10f;
    float petVisual;
    float petHand;
    float petStrokePos = 0.35f;
    Transform earL, earR;

    [Header("Caricias (animación de la mano)")]
    [Tooltip("Inicio del recorrido de la caricia sobre el lomo (local: detrás de la cabeza).")]
    public Vector3 petStrokeStart = new Vector3(0f, 0.235f, 0.035f);
    [Tooltip("Fin del recorrido de la caricia (local: hacia la cola).")]
    public Vector3 petStrokeEnd = new Vector3(0f, 0.2f, -0.1f);
    [Tooltip("Pixeles (a 1080p) de mouse para recorrer el lomo completo.")]
    public float petStrokePixels = 170f;
    [Tooltip("Grados que se echan hacia atrás las orejas mientras lo acarician (se relaja).")]
    public float petEarFold = 25f;
    [Tooltip("Cuánto se acerca al centro de la vista (local de la cámara) mientras lo acarician.")]
    public Vector3 petRaise = new Vector3(0.02f, 0.05f, 0.06f);

    /// <summary>0..1: qué tanto debe ir la mano del jugador al lomo (se está acariciando).</summary>
    public float PetHandWeight => petHand;
    /// <summary>Punto del lomo (mundo) donde va la palma en este momento.</summary>
    public Vector3 PetPoint => transform.TransformPoint(Vector3.Lerp(petStrokeStart, petStrokeEnd, petStrokePos));
    /// <summary>Dirección de la caricia (de la cabeza hacia la cola), en mundo.</summary>
    public Vector3 PetStrokeDir => transform.TransformDirection(petStrokeEnd - petStrokeStart).normalized;
    public float PetStrokePosition => petStrokePos;
    static readonly RaycastHit[] fleeHits = new RaycastHit[24];

    void Awake()
    {
        Grabbable = GetComponent<Grabbable>();
        Grabbable.customCarry = true;
        Grabbable.allowRotate = false;
        if (animator == null) animator = GetComponent<Animator>();
        if (skin == null) skin = GetComponentInChildren<SkinnedMeshRenderer>();
        colliders = GetComponents<Collider>();
        Spine = transform.Find("Armature/Hips/Spine");
        Grabbable.Grabbed += OnGrabbed;
        Grabbable.Released += OnReleased;
        idleSwitchAt = Time.time + UnityEngine.Random.Range(2f, 5f);
    }

    void OnDestroy()
    {
        if (Place == RabbitPlace.Carried) PlayerController.SpeedMultiplier = 1f;
        SetPetting(false);
    }

    /// <summary>Asigna código, peso y un aspecto ligeramente distinto a cada conejo.</summary>
    public void Setup(string code, float weightKg, int variant)
    {
        Code = code;
        WeightKg = weightKg;
        float size = Mathf.Lerp(0.9f, 1.1f, Mathf.InverseLerp(2f, 4.6f, weightKg));
        transform.localScale = Vector3.one * size;
        if (skin != null)
        {
            Color[] tints = { Color.white, new Color(1f, 0.95f, 0.87f), new Color(0.9f, 0.9f, 0.93f) };
            var mpb = new MaterialPropertyBlock();
            skin.GetPropertyBlock(mpb);
            mpb.SetColor("_BaseColor", tints[Mathf.Abs(variant) % tints.Length]);
            skin.SetPropertyBlock(mpb);
        }
        Stress = UnityEngine.Random.Range(5f, 15f);
    }

    public void AddStress(float amount) => Stress = Mathf.Clamp(Stress + amount, 0f, 100f);
    public void SetStress(float value) => Stress = Mathf.Clamp(value, 0f, 100f);

    // ------------------------------------------------------------------
    //  Movimiento entre lugares
    // ------------------------------------------------------------------

    void OnGrabbed()
    {
        var previous = Place;
        StopFlee();
        Place = RabbitPlace.Carried;
        SetColliders(false);
        if (NeedsCalming)
        {
            // Recién atrapado sigue muy asustado: hay que acariciarlo. Unos instantes de gracia
            // para que el giro brusco al atraparlo no lo haga escapar otra vez de inmediato.
            Stress = Mathf.Min(Stress, regrabStress);
            graceUntil = Time.time + regrabGrace;
            PlayReaction("Forcejeo", 0.8f);
        }
        PlayerController.SpeedMultiplier = carrySpeedMultiplier;
        hasLast = false;
        carryVelocity = Vector3.zero;
        if (Level2Manager.Instance != null) Level2Manager.Instance.OnRabbitPicked(this, previous);
    }

    void OnReleased()
    {
        // Sólo pasa si el jugador se desactiva con el conejo en brazos (p. ej. al salir al menú).
        if (Place != RabbitPlace.Carried) return;
        transform.SetParent(null, true);
        PlayerController.SpeedMultiplier = 1f;
        Place = RabbitPlace.Escaped;
        SetColliders(true);
    }

    /// <summary>Lleva al conejo (ya fuera de las manos) hasta un punto con un pequeño salto.</summary>
    public void PlaceAt(Vector3 position, Quaternion rotation, RabbitPlace place, float seconds = 0.55f, float arc = 0.06f, Action done = null)
    {
        if (transform.parent != null) transform.SetParent(null, true);
        if (Place == RabbitPlace.Carried) PlayerController.SpeedMultiplier = 1f;
        StopFlee();
        SetPetting(false);
        Place = place;
        Move(() => transform.position, () => transform.rotation, position, rotation, seconds, arc, false, () =>
        {
            SetColliders(true);
            done?.Invoke();
        });
    }

    /// <summary>El conejo salta de los brazos del jugador y cae al piso asustado.</summary>
    public void EscapeFromHands()
    {
        if (Place != RabbitPlace.Carried) return;
        if (ObjectGrabber.Instance != null && ObjectGrabber.Instance.Held == Grabbable) ObjectGrabber.Instance.TakeHeld();
        transform.SetParent(null, true);
        PlayerController.SpeedMultiplier = 1f;

        var cam = ObjectGrabber.Instance != null ? ObjectGrabber.Instance.Cam.transform : Camera.main.transform;
        Vector3 fwd = Vector3.ProjectOnPlane(cam.forward, Vector3.up);
        if (fwd.sqrMagnitude < 0.01f) fwd = cam.up;
        fwd.Normalize();
        // Salta lejos de los brazos (hasta ~1.7 m) hacia un punto libre del piso; si al frente hay
        // un mueble prueba otras direcciones y, en último caso, cae justo frente al jugador.
        Vector3 feet = new Vector3(cam.position.x, 0f, cam.position.z);
        Vector3 dir = fwd, target = feet + fwd * 0.8f;
        bool found = false;
        for (int attempt = 0; attempt < 24 && !found; attempt++)
        {
            float spread = attempt < 8 ? 35f : attempt < 16 ? 90f : 180f;
            Vector3 d = Quaternion.Euler(0f, UnityEngine.Random.Range(-spread, spread), 0f) * fwd;
            float dist = attempt < 12 ? UnityEngine.Random.Range(1.4f, 1.8f) : UnityEngine.Random.Range(0.9f, 1.3f);
            Vector3 p = feet + d * dist;
            if (p.x < fleeAreaMin.x || p.x > fleeAreaMax.x || p.z < fleeAreaMin.y || p.z > fleeAreaMax.y) continue;
            if (!PathClear(feet, d, dist) || !FloorAt(p, out float fy)) continue;
            p.y = fy;
            target = p; dir = d; found = true;
        }
        if (!found)
        {
            target.x = Mathf.Clamp(target.x, fleeAreaMin.x, fleeAreaMax.x);
            target.z = Mathf.Clamp(target.z, fleeAreaMin.y, fleeAreaMax.y);
            target.y = FloorAt(target, out float fy) ? fy : 0f;
        }
        var rot = Quaternion.LookRotation(dir, Vector3.up);

        Place = RabbitPlace.Escaped;
        Stress = 100f;
        NeedsCalming = true;
        EscapeCount++;
        SetPetting(false);
        StopFlee();
        fleeStartTime = Time.time;
        fleeRoutine = StartCoroutine(FleeRoutine(target, rot));
    }

    // ------------------------------------------------------------------
    //  Huida: el conejo espantado salta por el salón
    // ------------------------------------------------------------------

    IEnumerator FleeRoutine(Vector3 firstTarget, Quaternion firstRot)
    {
        // 1) Salta de los brazos al piso (sin colisión hasta aterrizar).
        yield return Hop(firstTarget, firstRot, 0.75f, 0.35f);
        SetColliders(true);
        yield return new WaitForSeconds(0.12f); // aterriza y sale corriendo casi de inmediato

        // 2) Brinca por el salón alejándose del jugador hasta que lo atrapen. Entre salto y
        //    salto hace pausas cortas: esa es la ventana para atraparlo.
        while (Place == RabbitPlace.Escaped)
        {
            Vector3 player = PlayerPosition();
            bool alert = Flat(transform.position - player).magnitude < fleeAlertDistance;
            if (TryPickHop(player, alert, out Vector3 to))
            {
                Vector3 delta = Flat(to - transform.position);
                float k = Mathf.InverseLerp(0.4f, 1.2f, delta.magnitude);
                Quaternion rot = delta.sqrMagnitude > 0.0001f ? Quaternion.LookRotation(delta.normalized, Vector3.up) : transform.rotation;
                yield return Hop(to, rot, fleeHopTime * Mathf.Lerp(0.85f, 1.2f, k), fleeHopHeight * Mathf.Lerp(0.8f, 1.3f, k));
            }
            else PlayReaction("Sobresalto", 0.6f); // acorralado: se sobresalta y se queda quieto un momento

            Vector2 range = alert ? fleeAlertPause : fleePause;
            float until = Time.time + UnityEngine.Random.Range(range.x, range.y);
            while (Time.time < until && Place == RabbitPlace.Escaped)
            {
                // Si el jugador se le acerca durante la pausa, reacciona casi de inmediato.
                float near = alert ? fleeGrabDistance + fleeComfortMargin : fleeAlertDistance;
                if (Flat(transform.position - PlayerPosition()).magnitude < near)
                {
                    until = Mathf.Min(until, Time.time + fleeReaction);
                    alert = true;
                }
                yield return null;
            }
        }
        fleeRoutine = null;
    }

    IEnumerator Hop(Vector3 to, Quaternion toRot, float seconds, float height)
    {
        IsHopping = true;
        PlayReaction("Salto", seconds + 0.05f);
        Vector3 from = transform.position;
        Quaternion fromRot = transform.rotation;
        float t = 0f;
        while (t < seconds)
        {
            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / seconds);
            Vector3 p = Vector3.Lerp(from, to, k) + Vector3.up * (height * 4f * k * (1f - k));
            transform.SetPositionAndRotation(p, Quaternion.Slerp(fromRot, toRot, Mathf.Clamp01(k * 3f)));
            yield return null;
        }
        transform.SetPositionAndRotation(to, toRot);
        IsHopping = false;
    }

    void StopFlee()
    {
        if (fleeRoutine != null) StopCoroutine(fleeRoutine);
        fleeRoutine = null;
        IsHopping = false;
    }

    /// <summary>Elige a dónde saltar: un punto libre del piso, lejos del jugador si está cerca.</summary>
    bool TryPickHop(Vector3 player, bool alert, out Vector3 best)
    {
        best = default;
        float bestScore = float.NegativeInfinity;
        Vector3 pos = transform.position;
        Vector3 away = Flat(pos - player);
        away = away.sqrMagnitude > 0.0001f ? away.normalized : Flat(transform.forward).normalized;
        Vector2 hop = alert ? fleeAlertHopDistance : fleeHopDistance;
        for (int i = 0; i < 20; i++)
        {
            Vector3 dir = Quaternion.Euler(0f, UnityEngine.Random.Range(0f, 360f), 0f) * Vector3.forward;
            float dist = UnityEngine.Random.Range(hop.x, hop.y);
            Vector3 to = pos + dir * dist;
            if (to.x < fleeAreaMin.x || to.x > fleeAreaMax.x || to.z < fleeAreaMin.y || to.z > fleeAreaMax.y) continue;
            if (!PathClear(pos, dir, dist) || !FloorAt(to, out float y)) continue;
            to.y = y;
            float toPlayer = Flat(to - player).magnitude;
            if (toPlayer < 0.7f) continue;
            float score = alert
                ? toPlayer * 2f + Vector3.Dot(dir, away) * 1.5f
                : UnityEngine.Random.value * 1.5f + Vector3.Dot(dir, away) * 0.6f + Mathf.Min(toPlayer, 3f) * 0.3f;
            float edge = Mathf.Min(Mathf.Min(to.x - fleeAreaMin.x, fleeAreaMax.x - to.x), Mathf.Min(to.z - fleeAreaMin.y, fleeAreaMax.y - to.z));
            score += Mathf.Clamp01(edge / 0.5f) * 0.5f; // evita quedarse pegado a las paredes
            if (score > bestScore) { bestScore = score; best = to; }
        }
        return bestScore > float.NegativeInfinity;
    }

    /// <summary>¿Puede saltar en esa dirección sin chocar con muebles, paredes o el jugador?</summary>
    bool PathClear(Vector3 from, Vector3 dir, float dist)
    {
        int count = Physics.SphereCastNonAlloc(from + Vector3.up * 0.14f, 0.1f, dir, fleeHits, dist + 0.12f, ~0, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
        {
            var h = fleeHits[i];
            if (h.distance <= 0f) continue; // ya se tocaba al empezar (p. ej. el piso): no bloquea
            if (h.collider.GetComponentInParent<RabbitController>() == this) continue;
            return false;
        }
        return true;
    }

    /// <summary>¿Hay piso libre (sin mesa, silla ni nada encima) en ese punto? Devuelve su altura.</summary>
    bool FloorAt(Vector3 p, out float y)
    {
        y = 0f;
        float top = float.NegativeInfinity;
        int count = Physics.RaycastNonAlloc(new Vector3(p.x, 1.8f, p.z), Vector3.down, fleeHits, 2.3f, ~0, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
        {
            if (fleeHits[i].collider.GetComponentInParent<RabbitController>() == this) continue;
            top = Mathf.Max(top, fleeHits[i].point.y);
        }
        if (float.IsNegativeInfinity(top) || top > 0.08f) return false;
        y = top;
        return true;
    }

    Vector3 PlayerPosition()
    {
        var c = ObjectGrabber.Instance != null ? ObjectGrabber.Instance.Cam : Camera.main;
        return c != null ? c.transform.position : transform.position + transform.forward * 10f;
    }

    static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);

    /// <summary>El conejo se retira (reportado como no apto): se encoge y desaparece.</summary>
    public void Leave(Action done = null)
    {
        StopFlee();
        SetPetting(false);
        Place = RabbitPlace.Leaving;
        SetColliders(false);
        StartCoroutine(LeaveRoutine(done));
    }

    IEnumerator LeaveRoutine(Action done)
    {
        yield return new WaitForSeconds(0.6f);
        Vector3 s0 = transform.localScale;
        float t = 0f;
        while (t < 0.45f)
        {
            t += Time.deltaTime;
            transform.localScale = s0 * (1f - Mathf.SmoothStep(0f, 1f, t / 0.45f));
            yield return null;
        }
        done?.Invoke();
        Destroy(gameObject);
    }

    void Move(Func<Vector3> getPos, Func<Quaternion> getRot, Vector3 toPos, Quaternion toRot, float seconds, float arc, bool local, Action done)
    {
        if (moveRoutine != null) StopCoroutine(moveRoutine);
        moveRoutine = StartCoroutine(MoveRoutine(getPos(), getRot(), toPos, toRot, seconds, arc, local, done));
    }

    IEnumerator MoveRoutine(Vector3 fromPos, Quaternion fromRot, Vector3 toPos, Quaternion toRot, float seconds, float arc, bool local, Action done)
    {
        float t = 0f;
        while (t < seconds)
        {
            t += Time.deltaTime;
            float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / seconds));
            Vector3 p = Vector3.Lerp(fromPos, toPos, k) + Vector3.up * (arc * Mathf.Sin(k * Mathf.PI));
            Quaternion r = Quaternion.Slerp(fromRot, toRot, k);
            if (local) { transform.localPosition = p; transform.localRotation = r; }
            else transform.SetPositionAndRotation(p, r);
            yield return null;
        }
        moveRoutine = null;
        done?.Invoke();
    }

    void SetColliders(bool on)
    {
        if (colliders == null) return;
        foreach (var c in colliders) if (c != null) c.enabled = on;
    }

    // ------------------------------------------------------------------
    //  Estrés y animaciones
    // ------------------------------------------------------------------

    void Update()
    {
        UpdateStress();
        UpdatePetting();
        UpdateCarryFollow();
        UpdateMood();
    }

    void UpdateStress()
    {
        if (Place == RabbitPlace.Carried && !IsMoving && GameFlow.GameplayInputAllowed)
        {
            var camObj = ObjectGrabber.Instance != null ? ObjectGrabber.Instance.Cam : Camera.main;
            if (camObj == null) return;
            var cam = camObj.transform;
            // Con gafas mover la cabeza es natural: lo que asusta al conejo es caminar o girar el cuerpo.
            if (VRMode.Active)
            {
                var body = camObj.GetComponentInParent<PlayerController>();
                if (body != null) cam = body.transform;
            }
            Vector3 e = cam.eulerAngles;
            if (hasLast && Time.deltaTime > 0f)
            {
                Vector3 d = cam.position - lastCamPos;
                float horizontal = new Vector2(d.x, d.z).magnitude / Time.deltaTime;
                float vertical = d.y / Time.deltaTime;
                float turn = (Mathf.Abs(Mathf.DeltaAngle(lastCamYaw, e.y)) + Mathf.Abs(Mathf.DeltaAngle(lastCamPitch, e.x))) / Time.deltaTime;
                smoothedSpeed = Mathf.Lerp(smoothedSpeed, horizontal, 1f - Mathf.Exp(-8f * Time.deltaTime));
                smoothedTurn = Mathf.Lerp(smoothedTurn, turn, 1f - Mathf.Exp(-8f * Time.deltaTime));

                float gain = 0f;
                if (smoothedSpeed > calmSpeed) gain += (smoothedSpeed - calmSpeed) * speedStressRate;
                if (smoothedTurn > calmTurnSpeed) gain += (smoothedTurn - calmTurnSpeed) * turnStressRate;
                if (vertical > 1.2f) gain += jumpStress; // brincar con el conejo en brazos

                if (Time.time < graceUntil) gain = 0f;
                if (gain > 0f) Stress += gain * Time.deltaTime;
                else if (!NeedsCalming) Stress -= calmDecay * 0.6f * Time.deltaTime; // espantado: sólo se calma con caricias
                Stress = Mathf.Clamp(Stress, 0f, 100f);

                if (Stress >= 100f)
                {
                    if (Level2Manager.Instance != null) Level2Manager.Instance.OnRabbitEscaped(this);
                    EscapeFromHands();
                }
            }
            lastCamPos = cam.position;
            lastCamYaw = e.y;
            lastCamPitch = e.x;
            hasLast = true;
        }
        else if (IsFleeing)
        {
            Stress = 100f;
        }
        else if (Place != RabbitPlace.Carried)
        {
            float decay = Place == RabbitPlace.Escaped ? calmDecay * 0.5f : calmDecay;
            Stress = Mathf.Max(0f, Stress - decay * Time.deltaTime);
        }
    }

    /// <summary>
    /// Caricias: con el conejo en brazos, mantener clic derecho y mover el mouse de lado a lado.
    /// Mientras se acaricia, el mouse no gira la cámara. Cada caricia baja el estrés; después de
    /// una huida es la única forma de calmarlo.
    /// </summary>
    void LateUpdate()
    {
        // Al acariciarlo echa las orejas hacia atrás (relajado), encima de la animación.
        float k = Mathf.Max(petVisual, petHand * 0.6f) * petEarFold;
        if (k <= 0.01f && !earsTouched) return;
        if (earL == null) earL = FindDeep(transform, "Ear1.L");
        if (earR == null) earR = FindDeep(transform, "Ear1.R");
        FoldEar(earL, ref earLBase, ref earLSet, k);
        FoldEar(earR, ref earRBase, ref earRSet, k);
        earsTouched = k > 0.01f;
    }

    bool earsTouched;
    Quaternion earLBase, earRBase, earLSet, earRSet;

    void FoldEar(Transform ear, ref Quaternion baseRot, ref Quaternion lastSet, float degrees)
    {
        if (ear == null) return;
        // Si la animación no escribió la oreja este cuadro, sigue valiendo la base anterior (no se acumula).
        if (!earsTouched || ear.localRotation != lastSet) baseRot = ear.localRotation;
        Vector3 axis = ear.parent != null ? ear.parent.InverseTransformDirection(transform.right) : Vector3.right;
        ear.localRotation = Quaternion.AngleAxis(-degrees, axis) * baseRot;
        lastSet = ear.localRotation;
    }

    static Transform FindDeep(Transform root, string name)
    {
        foreach (var t in root.GetComponentsInChildren<Transform>(true)) if (t.name == name) return t;
        return null;
    }

    void UpdatePetting()
    {
        bool want = Place == RabbitPlace.Carried && !IsMoving && GameFlow.GameplayInputAllowed && PetButtonHeld();
        SetPetting(want);
        if (petting) ApplyPetStroke(ReadMouseDelta());
        petVisual = Mathf.MoveTowards(petVisual, petting && Time.time - lastStrokeTime < 0.25f ? 1f : 0f, Time.deltaTime * 4f);
        petHand = Mathf.MoveTowards(petHand, petting ? 1f : 0f, Time.deltaTime * 5f);

        if (NeedsCalming && Place == RabbitPlace.Carried && Stress <= calmThreshold)
        {
            NeedsCalming = false;
            if (Level2Manager.Instance != null) Level2Manager.Instance.OnRabbitCalmed(this);
        }
    }

    /// <summary>
    /// Aplica el movimiento del mouse de este cuadro como caricia (sólo si se está acariciando).
    /// Sobre todo cuenta el vaivén horizontal; el recorrido vertical cuenta menos.
    /// </summary>
    public void ApplyPetStroke(Vector2 mouseDelta)
    {
        if (!petting || Time.deltaTime <= 0f) return;
        float stroke = (Mathf.Abs(mouseDelta.x) + 0.35f * Mathf.Abs(mouseDelta.y)) / Mathf.Max(1f, Screen.height * 0.25f);
        // La mano sigue al mouse a lo largo del lomo (ida y vuelta).
        petStrokePos = Mathf.Clamp01(petStrokePos + mouseDelta.x / Mathf.Max(1f, Screen.height / 1080f * petStrokePixels));
        if (stroke <= 0.0005f) return;
        Stress = Mathf.Max(0f, Stress - Mathf.Min(stroke * petStressPerStroke, maxPetRate * Time.deltaTime));
        lastStrokeTime = Time.time;
    }

    void SetPetting(bool on)
    {
        if (petting == on) return;
        petting = on;
        if (petLookOwner == null && ObjectGrabber.Instance != null)
            petLookOwner = ObjectGrabber.Instance.GetComponentInParent<PlayerController>();
        if (petLookOwner != null) petLookOwner.lookEnabled = !on;
    }

    static bool PetButtonHeld() => GameInput.SecondaryHeld();

    static Vector2 ReadMouseDelta() => GameInput.LookDelta();

    /// <summary>
    /// Sigue a la cámara con un pequeño resorte (posición y rotación) en vez de ir pegado
    /// rígidamente: se siente como si tuviera peso en los brazos, con un balanceo suave al
    /// caminar, en lugar de "teletransportarse" con cada giro de cámara.
    /// </summary>
    void UpdateCarryFollow()
    {
        if (Place != RabbitPlace.Carried) return;
        var camObj = ObjectGrabber.Instance != null ? ObjectGrabber.Instance.Cam : Camera.main;
        if (camObj == null) return;
        var camT = camObj.transform;
        if (VRMode.Active && VRCarry(camT)) return; // con gafas: en los brazos, entre los dos controles

        swayPhase += Time.deltaTime * (1.3f + smoothedSpeed * 0.6f);
        float swayK = Mathf.Clamp01(smoothedSpeed / Mathf.Max(0.01f, calmSpeed)) * carrySwayAmount;

        // Al acariciarlo lo sube un poco hacia el centro de la vista, para ver la mano en su lomo.
        Vector3 targetPos = camT.TransformPoint(carryLocalPosition + petRaise * Mathf.SmoothStep(0f, 1f, petHand))
            + camT.right * Mathf.Sin(swayPhase) * swayK
            + camT.up * Mathf.Sin(swayPhase * 1.7f) * swayK * 0.5f;
        Quaternion targetRot = camT.rotation * Quaternion.Euler(carryLocalEuler);
        // Al acariciarlo se acurruca: baja un poco la cabeza y se mece suavemente.
        if (petVisual > 0.001f)
            targetRot *= Quaternion.Euler(10f * petVisual, 0f, Mathf.Sin(Time.time * 7f) * 4f * petVisual);

        transform.position = Vector3.SmoothDamp(transform.position, targetPos, ref carryVelocity, carryPositionSmoothTime);
        transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, 1f - Mathf.Exp(-carryRotationSpeed * Time.deltaTime));
    }

    /// <summary>
    /// Con gafas: el conejo va entre las dos manos (los controles), mirando de lado como en brazos.
    /// Devuelve false si no hay controles (se usa el acomodo normal frente a la cámara).
    /// </summary>
    bool VRCarry(Transform head)
    {
        var l = VRControllerPose.Left;
        var r = VRControllerPose.Right;
        if (l == null && r == null) return false;
        Vector3 mid = l != null && r != null ? (l.transform.position + r.transform.position) * 0.5f
                    : (r != null ? r.transform.position : l.transform.position);
        Vector3 fwd = Vector3.ProjectOnPlane(head.forward, Vector3.up);
        if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.ProjectOnPlane(head.up, Vector3.up);
        Quaternion targetRot = Quaternion.LookRotation(fwd.normalized, Vector3.up) * Quaternion.Euler(0f, carryLocalEuler.y, 0f);
        if (petVisual > 0.001f)
            targetRot *= Quaternion.Euler(10f * petVisual, 0f, Mathf.Sin(Time.time * 7f) * 4f * petVisual);
        Vector3 targetPos = mid + Vector3.up * 0.06f;
        transform.position = Vector3.SmoothDamp(transform.position, targetPos, ref carryVelocity, carryPositionSmoothTime * 0.5f);
        transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, 1f - Mathf.Exp(-carryRotationSpeed * Time.deltaTime));
        return true;
    }

    void UpdateMood()
    {
        if (animator == null || Time.time < oneShotUntil) return;
        string mood = !string.IsNullOrEmpty(ForcedMood) ? ForcedMood : AutoMood();
        if (mood != currentMood)
        {
            currentMood = mood;
            animator.CrossFadeInFixedTime(mood, 0.3f);
        }
    }

    string AutoMood()
    {
        switch (Place)
        {
            case RabbitPlace.Carried:
                return Stress < 35f ? "Tranquilo" : Stress < 70f ? "Nervioso" : "Forcejeo";
            case RabbitPlace.OnScale:
                return Stress > 40f ? "Nervioso" : "Olfateando";
            case RabbitPlace.OnPad:
                return Stress > 40f ? "Nervioso" : "Relajado";
            case RabbitPlace.Escaped:
                return IsFleeing || Stress > 30f ? "Temblor" : "Olfateando";
            case RabbitPlace.Leaving:
                return "Tranquilo";
            default:
                if (Stress > 45f) return "Nervioso";
                if (Time.time >= idleSwitchAt)
                {
                    idleSniff = !idleSniff;
                    idleSwitchAt = Time.time + UnityEngine.Random.Range(idleSniff ? 3f : 4f, idleSniff ? 6f : 8f);
                }
                return idleSniff ? "Olfateando" : "Tranquilo";
        }
    }

    /// <summary>Reproduce una reacción corta (Sobresalto, Salto...) y luego vuelve a la animación normal.</summary>
    public void PlayReaction(string clip, float seconds)
    {
        if (animator == null) return;
        animator.CrossFadeInFixedTime(clip, 0.06f);
        currentMood = clip;
        oneShotUntil = Time.time + seconds;
    }

    // ------------------------------------------------------------------
    //  Interacción (la decide el controlador del nivel)
    // ------------------------------------------------------------------

    public override string PromptFor(Grabbable held, int action) =>
        Level2Manager.Instance != null ? Level2Manager.Instance.RabbitPrompt(this, held, action) : null;

    public override void Interact(Grabbable held, int action)
    {
        if (Level2Manager.Instance != null) Level2Manager.Instance.RabbitInteract(this, held, action);
    }

    public override string InfoFor(Grabbable held) =>
        Level2Manager.Instance != null ? Level2Manager.Instance.RabbitInfo(this, held) : null;
}
