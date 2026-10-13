using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// Nivel 3 · Suspensión de glóbulos rojos (lavado de eritrocitos):
///   1) Con la micropipeta, agregar 6 mL de solución salina al tubo con sangre (en la gradilla).
///   2) Resuspender: invertir el tubo suavemente (clic derecho + mover el mouse).
///   3) Colocar el tubo y su balance en el rotor (posiciones opuestas) y cerrar la tapa.
///   4) Programar la centrífuga a 2500 rpm durante 5 min e iniciar (otros valores = error / bloqueo).
///   5) Sacar el tubo y observar el sobrenadante; decantarlo en el vaso de desechos.
///   Se repite hasta que el sobrenadante queda totalmente transparente (3 centrifugaciones).
/// Modo educativo: objetivo, lista de pasos, marcador y explicaciones. Modo evaluación: errores, tiempo y nota.
/// </summary>
public class Level3Manager : MonoBehaviour, ILevelController
{
    public static Level3Manager Instance { get; private set; }

    [Header("Escena")]
    public SampleTube sample;
    public SampleTube balance;
    public Centrifuge centrifuge;
    public CentrifugePanel panel;
    public Level3Station rack;
    public Level3Station salineBottle;
    public Level3Station wasteBeaker;
    public Level3Station pipetteStand;
    public MicropipetteTool pipette;
    [Tooltip("Líquido del vaso de desechos (crece con cada decantado).")]
    public Transform wasteLiquid;
    public Renderer wasteLiquidRenderer;
    public Material streamMaterial;
    public Material markerMaterial;
    public int sampleHomeSlot = 1;
    public int balanceHomeSlot = 3;

    [Header("Protocolo")]
    public int targetRpm = 2500;
    public int targetSeconds = 300;
    public int requiredSpins = 3;
    public float salineMl = 6f;

    [Header("Configuración")]
    public float toastDuration = 4.2f;
    [Tooltip("Inversiones completas (de lado a lado) para resuspender el paquete.")]
    public float inversionsToMix = 5f;
    public float maxMixRate = 0.45f;

    [Header("UI - HUD")]
    public TMP_Text counterText;
    public TMP_Text toastText;
    public CanvasGroup toastGroup;
    public TMP_Text modeText;
    public TMP_Text timerText;
    public GameObject objectivePanel;
    public TMP_Text objectiveText;
    public GameObject checklistPanel;
    public TMP_Text checklistText;
    [Tooltip("Barra de progreso (se reutiliza la del estrés del nivel 2) para la resuspensión.")]
    public GameObject mixGroup;
    public Image mixFill;
    public TMP_Text mixLabel;

    [Header("UI - Fin de nivel")]
    public GameObject completePanel;
    public TMP_Text completeTitleText;
    public TMP_Text completeTimeText;
    public TMP_Text gradeText;
    public TMP_Text resultDetailText;
    public GameObject continueButton;

    [Header("Evaluación")]
    public float parTimeSeconds = 540f;
    public float penaltyPerError = 1f;
    public float timePenaltyPerMinute = 0.5f;
    public float maxTimePenalty = 2f;
    public float passingGrade = 6f;

    public enum Step { AddSaline, Mix, Load, Close, Program, Spinning, Unload, Decant, Done }

    static readonly string[] CycleSteps =
    {
        "Agregar 6 mL de solución salina (micropipeta)",
        "Resuspender: invertir el tubo suavemente",
        "Muestra y balance en el rotor (opuestos)",
        "Cerrar la tapa y centrifugar 2500 rpm · 5 min",
        "Sacar el tubo y observar el sobrenadante",
        "Decantar el sobrenadante (vaso de desechos)"
    };

    public GameMode Mode { get; private set; } = GameMode.Educativo;
    public bool IsRunning { get; private set; }
    public bool IsEducational => Mode == GameMode.Educativo;
    public bool Busy { get; private set; }
    public bool Completed => completed;
    public int ErrorCount => errors.Count;
    public IReadOnlyList<string> Errors => errors;

    readonly List<string> errors = new List<string>();
    readonly HashSet<string> errorKeys = new HashSet<string>();
    readonly List<string> supernatantLog = new List<string>();
    bool completed, finishing;
    float startTime;
    float tilt;
    bool mixing;
    int wrongConfigs;
    int decants;
    Coroutine toastRoutine;
    Transform marker;
    Transform stream;
    MeshRenderer streamRenderer;
    MaterialPropertyBlock mpb;
    PlayerController playerCtl;
    float wasteBaseScale;

    ObjectGrabber Grabber => ObjectGrabber.Instance;

    // ------------------------------------------------------------------
    //  Ciclo de vida
    // ------------------------------------------------------------------

    void Awake()
    {
        Instance = this;
        LabDatabase.EnsureInitialized();
        mpb = new MaterialPropertyBlock();
        if (completePanel != null) completePanel.SetActive(false);
        if (toastGroup != null) toastGroup.alpha = 0f;
        if (mixGroup != null) mixGroup.SetActive(false);
        if (sample != null && rack != null) sample.DockAt(rack.points[sampleHomeSlot], true);
        if (balance != null && rack != null) balance.DockAt(rack.points[balanceHomeSlot], true);
        if (wasteLiquid != null) { wasteBaseScale = wasteLiquid.localScale.y; }
        if (pipette != null)
        {
            pipette.Grabbable.Body.angularDamping = 4f; // si se deja acostada, que no ruede por la mesa
            if (pipetteStand != null) SeatPipette();
        }
        SetupGrabbables();
        CreateMarker();
        CreateStream();
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
        if (playerCtl != null) playerCtl.lookEnabled = true;
    }

    void Start()
    {
        if (GameFlow.Instance == null) BeginLevel(GameSession.Mode);
    }

    public void BeginLevel(GameMode mode)
    {
        Mode = mode;
        IsRunning = true;
        completed = false;
        finishing = false;
        startTime = Time.time;
        errors.Clear();
        errorKeys.Clear();
        supernatantLog.Clear();
        ObjectGrabber.MessageSink = ShowMessage;
        ObjectGrabber.AimHint = AimHint;
        if (toastText != null)
        {
            // Los avisos de este nivel son más largos: se permiten dos renglones.
            toastText.textWrappingMode = TextWrappingModes.Normal;
            toastText.enableAutoSizing = true;
            toastText.fontSizeMin = 20f;
            toastText.fontSizeMax = 30f;
        }
        playerCtl = FindAnyObjectByType<PlayerController>();

        if (modeText != null)
        {
            modeText.text = IsEducational ? "MODO EDUCATIVO · NIVEL 3" : "MODO EVALUACIÓN · NIVEL 3";
            modeText.color = IsEducational ? new Color(0.45f, 0.9f, 0.65f) : new Color(1f, 0.72f, 0.35f);
        }
        ShowMessage(IsEducational
            ? "Nivel 3: lava los eritrocitos con solución salina y la centrífuga hasta que el sobrenadante quede transparente. Sigue el objetivo (arriba a la izquierda)."
            : "Nivel 3 (evaluación): prepara la suspensión de eritrocitos lavados. Se registran tus errores y tu tiempo.");
    }

    void SetupGrabbables()
    {
        foreach (var t in new[] { sample, balance })
        {
            if (t == null) continue;
            var tube = t;
            var g = tube.Grabbable;
            g.allowRotate = false;
            g.holdViewOffset = new Vector3(0f, 0.1f, 0f); // un poco arriba de la mira: el aviso de abajo no tapa el tubo
            g.displayName = tube.isBalance ? "tubo de balance" : "tubo de muestra";
            g.GrabBlocker = () => !IsRunning ? "Primero inicia el nivel." : Busy ? "Espera un momento..." : null;
            g.ReleaseBlocker = () => "No sueltes el tubo en cualquier lugar: colócalo en la gradilla o en la centrífuga (apunta y presiona E).";
            g.HeldPrompt = () => TubeHeldPrompt(tube);
            g.Grabbed += () => { if (Grabber != null) Grabber.holdDistance = TubeHoldDistance; };
        }
        if (pipette != null)
        {
            var g = pipette.Grabbable;
            g.allowRotate = false;
            g.displayName = "micropipeta (6 mL)";
            g.Grabbed += () =>
            {
                if (g.Body.isKinematic) SetPipettePhysics(true); // al tomarla del soporte vuelve a la física
                if (Grabber != null) Grabber.holdDistance = 0.75f;
            };
            g.GrabBlocker = () => !IsRunning ? "Primero inicia el nivel." : Busy ? "Espera un momento..." : null;
            g.HeldPrompt = () => pipette.Loaded
                ? "Micropipeta cargada con 6 mL de solución salina:\napunta al tubo de muestra en la gradilla y presiona E."
                : "Micropipeta ajustada a 6 mL: apunta al frasco de solución salina y presiona E para cargarla.\n<size=14>Soporte: dejarla en su lugar  ·  E / Clic sin apuntar a nada: soltarla</size>";
        }
    }

    string TubeHeldPrompt(SampleTube tube)
    {
        if (finishing || completed) return "¡Lavado terminado! El sobrenadante quedó totalmente transparente.";
        if (tube == sample && sample.NeedsMix)
            return mixing ? "Invirtiendo el tubo... sigue moviendo el mouse de lado a lado."
                : "Resuspende la muestra: mantén clic derecho y mueve el mouse de lado a lado\n(invierte el tubo suavemente, sin agitar con fuerza).";
        string name = tube.isBalance ? "el tubo de balance" : "el tubo de muestra";
        string s = "Llevas " + name + ": apunta a la gradilla o a la centrífuga y presiona E.";
        if (tube == sample && sample.State == SampleTube.Content.Separated) s += "\nVaso de desechos: decantar el sobrenadante.";
        return s;
    }

    string AimHint(Grabbable g)
    {
        if (sample != null && g == sample.Grabbable) return "<size=15><color=#CFE3FF>· " + sample.Describe() + "</color></size>";
        if (balance != null && g == balance.Grabbable) return "<size=15><color=#CFE3FF>· " + balance.Describe() + "</color></size>";
        if (pipette != null && g == pipette.Grabbable) return "<size=15><color=#CFE3FF>· 1 – 10 mL, ajustada a 6 mL</color></size>";
        return null;
    }

    void Update()
    {
        if (timerText != null)
        {
            bool show = IsRunning && GameSettings.ShowTimer;
            if (timerText.gameObject.activeSelf != show) timerText.gameObject.SetActive(show);
            if (show) timerText.text = FormatTime(Time.time - startTime);
        }
        if (!IsRunning) { HideGuide(); return; }

        UpdateMixing();
        var step = CurrentStep;
        if (counterText != null)
            counterText.text = !IsEducational ? "Nivel 3 · Lavado de eritrocitos"
                : step == Step.Done ? "Suspensión lista" : "Lavado " + Cycle + "/" + requiredSpins + " · " + StepLabel(step);
        UpdateMixBar();
        UpdateGuide(step);
    }

    void FixedUpdate()
    {
        if (Busy || Grabber == null) return;
        var g = Grabber.Held;
        if (g == null) return;
        var cam = Grabber.Cam.transform;
        // Con gafas el giro sale de la mano (el control), no de la cabeza: mover la cabeza no
        // debe girar el tubo ni la micropipeta.
        var hand = VRMode.Active && VRControllerPose.Right != null ? VRControllerPose.Right.transform : null;
        Vector3 f = hand != null ? hand.forward : cam.forward; f.y = 0f;
        if (f.sqrMagnitude < 1e-5f) f = hand != null ? hand.up : cam.up;
        var yaw = Quaternion.LookRotation(f.normalized, Vector3.up);
        Quaternion target;
        if ((sample != null && g == sample.Grabbable) || (balance != null && g == balance.Grabbable))
            target = yaw * Quaternion.Euler(0f, 0f, (sample != null && g == sample.Grabbable) ? tilt : 0f);
        else if (pipette != null && g == pipette.Grabbable)
            target = pipette.UprightRotation(-f);
        else return;
        if (g.Body.isKinematic) return;
        g.Body.MoveRotation(Quaternion.Slerp(g.Body.rotation, target, VRMode.Active ? 0.15f : 0.4f));
        g.Body.angularVelocity = Vector3.zero;
    }

    // ------------------------------------------------------------------
    //  Paso actual
    // ------------------------------------------------------------------

    public int Cycle
    {
        get
        {
            if (sample == null) return 1;
            int c = sample.State == SampleTube.Content.Separated ? sample.Spins : sample.Spins + 1;
            return Mathf.Clamp(c, 1, requiredSpins);
        }
    }

    public Step CurrentStep
    {
        get
        {
            if (completed || finishing) return Step.Done;
            switch (sample.State)
            {
                case SampleTube.Content.WholeBlood:
                case SampleTube.Content.Pellet:
                    return Step.AddSaline;
                case SampleTube.Content.Unmixed:
                    return Step.Mix;
                case SampleTube.Content.Suspension:
                    if (centrifuge.IsSpinning) return Step.Spinning;
                    if (!centrifuge.Contains(sample) || !centrifuge.Contains(balance)) return Step.Load;
                    return centrifuge.LidOpen ? Step.Close : Step.Program;
                case SampleTube.Content.Separated:
                    return centrifuge.Contains(sample) ? Step.Unload : Step.Decant;
            }
            return Step.AddSaline;
        }
    }

    static int ChecklistIndex(Step s)
    {
        switch (s)
        {
            case Step.AddSaline: return 0;
            case Step.Mix: return 1;
            case Step.Load: return 2;
            case Step.Close: case Step.Program: case Step.Spinning: return 3;
            case Step.Unload: return 4;
            case Step.Decant: return 5;
            default: return 6;
        }
    }

    string StepLabel(Step s)
    {
        switch (s)
        {
            case Step.AddSaline: return "Agregar solución salina";
            case Step.Mix: return "Resuspender la muestra";
            case Step.Load: return "Cargar el rotor";
            case Step.Close: return "Cerrar la tapa";
            case Step.Program: return "Programar la centrífuga";
            case Step.Spinning: return "Centrifugando";
            case Step.Unload: return "Sacar el tubo";
            case Step.Decant: return "Decantar el sobrenadante";
            default: return "Terminado";
        }
    }

    SampleTube HeldTube(Grabbable held)
    {
        if (held == null) return null;
        if (sample != null && held == sample.Grabbable) return sample;
        if (balance != null && held == balance.Grabbable) return balance;
        return null;
    }

    bool HoldingPipette(Grabbable held) => pipette != null && held == pipette.Grabbable;
    bool HoldingSample(Grabbable held) => sample != null && held == sample.Grabbable;

    bool InRack(SampleTube t) => t != null && t.Dock != null && rack != null && Array.IndexOf(rack.points, t.Dock) >= 0;

    int FreeRackSlot(SampleTube t)
    {
        int home = t == sample ? sampleHomeSlot : balanceHomeSlot;
        if (IsFree(home)) return home;
        for (int i = 0; i < rack.points.Length; i++) if (IsFree(i)) return i;
        return -1;
    }

    bool IsFree(int i) => (sample == null || sample.Dock != rack.points[i]) && (balance == null || balance.Dock != rack.points[i]);

    // ------------------------------------------------------------------
    //  Centrífuga
    // ------------------------------------------------------------------

    public string CentrifugePrompt(Grabbable held, int action)
    {
        if (!IsRunning || completed || finishing || Busy) return null;
        var c = centrifuge;
        if (c.IsSpinning) return action == 0 && !IsEducational && (held == null || HeldTube(held) != null) ? "abrir la tapa" : null;
        if (c.LidMoving) return null;
        var tube = HeldTube(held);
        if (c.LidOpen)
        {
            if (action == 0)
            {
                if (tube != null) return c.FreeSlot() >= 0 ? "colocar " + (tube.isBalance ? "el tubo de balance" : "el tubo de muestra") + " en el rotor" : null;
                if (held == null)
                {
                    if (c.Contains(sample)) return "sacar el tubo de muestra";
                    if (c.Contains(balance)) return "sacar el tubo de balance";
                    return "cerrar la tapa";
                }
                return null;
            }
            if (held != null && tube == null) return null;
            return held == null && c.Count == 0 ? null : "cerrar la tapa";
        }
        if (action == 0) return held == null || tube != null ? "abrir la tapa" : null;
        return "programar la centrífuga (panel de control)";
    }

    public string CentrifugeInfo(Grabbable held)
    {
        if (!IsRunning) return null;
        var c = centrifuge;
        if (c.IsSpinning) return "Centrifugando · " + Mathf.RoundToInt(c.CurrentRpm / 10f) * 10 + " rpm · faltan " + Centrifuge.Clock(c.RemainingSeconds)
                                  + (IsEducational ? "  ·  la tapa queda bloqueada mientras gira" : "");
        if (c.LidOpen)
        {
            string inside = c.Count == 0 ? "rotor vacío" : c.Count == 1 ? (c.Contains(sample) ? "sólo la muestra (falta el balance)" : "sólo el balance (falta la muestra)") : "muestra y balance en posiciones opuestas";
            return "Tapa abierta · " + inside;
        }
        return "Tapa cerrada · programada a " + c.setRpm + " rpm · " + Centrifuge.Clock(c.setSeconds);
    }

    public void CentrifugeInteract(Grabbable held, int action)
    {
        var c = centrifuge;
        if (c.IsSpinning)
        {
            AddError("tapaGirando", "Intentaste abrir la tapa con el rotor girando.");
            ShowMessage("¡La tapa está bloqueada mientras el rotor gira! Nunca abras una centrífuga en movimiento.");
            return;
        }
        var tube = HeldTube(held);
        if (c.LidOpen)
        {
            if (action == 0 && tube != null) { StartCoroutine(PlaceInCentrifuge(tube)); return; }
            if (action == 0 && held == null && c.Contains(sample)) { StartCoroutine(TakeFromCentrifuge(sample)); return; }
            if (action == 0 && held == null && c.Contains(balance)) { StartCoroutine(TakeFromCentrifuge(balance)); return; }
            c.SetLid(false);
            if (IsEducational && c.Count == 2 && sample.State == SampleTube.Content.Suspension)
                ShowMessage("Tapa cerrada. Ahora programa la centrífuga: apúntala y presiona R.");
            return;
        }
        if (action == 0)
        {
            c.SetLid(true);
            if (IsEducational && sample.State == SampleTube.Content.Separated && c.Contains(sample))
                ShowMessage("Saca el tubo de muestra con cuidado (E) para no resuspender el paquete.");
            return;
        }
        if (panel != null) panel.Open(c.setRpm, c.setSeconds, IsEducational, TryStartSpin, null);
    }

    /// <summary>Lo llama el botón «Iniciar» del panel. Devuelve null si arrancó o el motivo si no.</summary>
    public string TryStartSpin(int rpm, int seconds)
    {
        var c = centrifuge;
        c.setRpm = rpm;
        c.setSeconds = seconds;
        int cycle = Cycle;
        if (c.LidOpen)
        {
            AddError("tapaAbierta", "Intentaste iniciar la centrífuga con la tapa abierta.");
            return "La tapa está abierta: ciérrala antes de iniciar.";
        }
        if (!c.Contains(sample))
            return c.Contains(balance) ? "Sólo está el tubo de balance: coloca también el tubo de muestra." : "El rotor está vacío: coloca el tubo de muestra y su balance.";
        if (!c.Contains(balance))
        {
            AddError("balance" + cycle, "Intentaste centrifugar sin el tubo de balance (rotor desbalanceado).");
            return IsEducational
                ? "Rotor desbalanceado: coloca el tubo de balance (con el mismo volumen) en la posición opuesta. Sin balance la centrífuga vibra y puede dañarse o romper el tubo."
                : "Rotor desbalanceado: la centrífuga no se inició (error registrado).";
        }
        switch (sample.State)
        {
            case SampleTube.Content.Unmixed:
                AddError("sinMezcla" + cycle, "Intentaste centrifugar sin resuspender el paquete de eritrocitos.");
                return IsEducational
                    ? "La muestra no está resuspendida: saca el tubo e inviértelo suavemente hasta que quede homogéneo. Si no, la solución salina no lava a los eritrocitos."
                    : "La muestra no está resuspendida (error registrado).";
            case SampleTube.Content.WholeBlood:
            case SampleTube.Content.Pellet:
                AddError("sinSalina" + cycle, "Intentaste centrifugar sin agregar la solución salina.");
                return "Falta agregar la solución salina al tubo de muestra.";
            case SampleTube.Content.Separated:
                return "La muestra ya está centrifugada: saca el tubo y decanta el sobrenadante.";
        }

        bool rpmOk = rpm == targetRpm, timeOk = seconds == targetSeconds;
        if (!rpmOk || !timeOk)
        {
            wrongConfigs++;
            AddError("cfg_" + rpm + "_" + seconds, "Programaste la centrífuga a " + rpm + " rpm durante " + Centrifuge.Clock(seconds) + " min (protocolo: " + targetRpm + " rpm · " + Centrifuge.Clock(targetSeconds) + " min).");
            if (!IsEducational) return "Valores fuera del protocolo: la centrífuga no se inició (error registrado).";
            var sb = new System.Text.StringBuilder();
            if (!rpmOk)
                sb.Append(rpm > targetRpm
                    ? "Velocidad muy alta (" + rpm + " rpm): compacta de más el paquete y puede dañar (hemolizar) a los eritrocitos. "
                    : "Velocidad muy baja (" + rpm + " rpm): los eritrocitos no sedimentan por completo y se perderían al decantar. ");
            if (!timeOk)
                sb.Append(seconds > targetSeconds
                    ? "Tiempo muy largo (" + Centrifuge.Clock(seconds) + "): el paquete queda demasiado compactado. "
                    : "Tiempo muy corto (" + Centrifuge.Clock(seconds) + "): no da tiempo a que sedimenten. ");
            sb.Append("Usa " + targetRpm + " rpm durante " + Centrifuge.Clock(targetSeconds) + " min.");
            return sb.ToString();
        }

        c.StartSpin(rpm, seconds, OnSpinFinished);
        ShowMessage(IsEducational
            ? "Centrifugando a 2500 rpm durante 5 min (el tiempo corre acelerado). Espera a que el rotor se detenga."
            : "Centrifugando...");
        return null;
    }

    void OnSpinFinished()
    {
        sample.Separate();
        ShowMessage(IsEducational
            ? "Centrifugado terminado. Abre la tapa (E) y saca el tubo de muestra para observar el sobrenadante."
            : "Centrifugado terminado.");
    }

    // ------------------------------------------------------------------
    //  Estaciones: gradilla, frasco de solución salina, vaso de desechos
    // ------------------------------------------------------------------

    public string StationPrompt(Level3Station st, Grabbable held, int action)
    {
        if (!IsRunning || completed || finishing || Busy || action != 0) return null;
        var tube = HeldTube(held);
        switch (st.kind)
        {
            case Level3Station.Kind.Rack:
                if (tube != null) return FreeRackSlot(tube) >= 0 ? "colocar " + (tube.isBalance ? "el tubo de balance" : "el tubo de muestra") + " en la gradilla" : null;
                if (HoldingPipette(held) && pipette.Loaded && InRack(sample)) return "dispensar 6 mL de solución salina en el tubo de muestra";
                return null;
            case Level3Station.Kind.Saline:
                return HoldingPipette(held) && !pipette.Loaded ? "cargar 6 mL de solución salina" : null;
            case Level3Station.Kind.PipetteStand:
                return HoldingPipette(held) ? "colocar la micropipeta en su soporte" : null;
            case Level3Station.Kind.Waste:
                if (tube == sample && tube != null) return "decantar el sobrenadante";
                if (HoldingPipette(held) && pipette.Loaded) return "descartar el contenido de la punta";
                return null;
        }
        return null;
    }

    public string StationInfo(Level3Station st, Grabbable held)
    {
        if (!IsRunning) return null;
        switch (st.kind)
        {
            case Level3Station.Kind.Rack:
                if (HoldingPipette(held) && !pipette.Loaded) return "Primero carga la micropipeta en el frasco de solución salina.";
                if (HoldingPipette(held) && pipette.Loaded && !InRack(sample)) return "Coloca el tubo de muestra en la gradilla para agregarle la solución.";
                return "Gradilla para tubos de ensayo";
            case Level3Station.Kind.Saline:
                if (HeldTube(held) != null) return "Usa la micropipeta para medir y agregar la solución salina.";
                if (HoldingPipette(held) && pipette.Loaded) return "La micropipeta ya está cargada.";
                return "Solución salina isotónica (NaCl 0.9 %)";
            case Level3Station.Kind.PipetteStand:
                return "Soporte de la micropipeta";
            case Level3Station.Kind.Waste:
                return "Vaso de desechos biológicos (con hipoclorito)" + (decants > 0 ? " · " + decants + " decantado(s)" : "");
        }
        return null;
    }

    public void StationInteract(Level3Station st, Grabbable held, int action)
    {
        var tube = HeldTube(held);
        switch (st.kind)
        {
            case Level3Station.Kind.Rack:
                if (tube != null) { StartCoroutine(PlaceInRack(tube)); return; }
                if (HoldingPipette(held)) TryDispense();
                return;
            case Level3Station.Kind.Saline:
                StartCoroutine(LoadPipette());
                return;
            case Level3Station.Kind.PipetteStand:
                StartCoroutine(ReturnPipette());
                return;
            case Level3Station.Kind.Waste:
                if (tube != null) TryDecant();
                else if (HoldingPipette(held)) StartCoroutine(DiscardPipette());
                return;
        }
    }

    void TryDispense()
    {
        switch (sample.State)
        {
            case SampleTube.Content.Separated:
                AddError("salinaSinDecantar" + Cycle, "Intentaste agregar solución salina sin decantar el sobrenadante.");
                ShowMessage("Primero decanta el sobrenadante en el vaso de desechos; si no, el tubo se desborda y no se lava.");
                return;
            case SampleTube.Content.Unmixed:
            case SampleTube.Content.Suspension:
                ShowMessage("El tubo ya tiene la solución salina de este lavado.");
                return;
        }
        StartCoroutine(Dispense());
    }

    void TryDecant()
    {
        switch (sample.State)
        {
            case SampleTube.Content.Separated:
                StartCoroutine(Decant());
                return;
            case SampleTube.Content.Pellet:
                ShowMessage("Ya decantaste el sobrenadante: ahora agrega solución salina para el siguiente lavado.");
                return;
            default:
                AddError("decantSinCentrifugar", "Intentaste decantar sin centrifugar (se perderían los eritrocitos).");
                ShowMessage(IsEducational
                    ? "¡No decantes todavía! Sin centrifugar, los eritrocitos siguen suspendidos y los tirarías junto con el líquido."
                    : "No se puede decantar sin centrifugar (error registrado).");
                return;
        }
    }

    // ------------------------------------------------------------------
    //  Animaciones
    // ------------------------------------------------------------------

    const float TubeHoldDistance = 0.5f;

    Vector3 HandPoint(Grabbable g)
    {
        var cam = Grabber.Cam.transform;
        float d = g.GetComponent<SampleTube>() != null ? TubeHoldDistance : 0.7f;
        return cam.position + cam.forward * d + cam.TransformDirection(g.holdViewOffset) - Vector3.up * (g.GetComponent<SampleTube>() != null ? 0.08f : 0f);
    }

    Quaternion HandRotation(Grabbable g)
    {
        Vector3 f = Grabber.Cam.transform.forward; f.y = 0f;
        if (f.sqrMagnitude < 1e-5f) f = Vector3.forward;
        if (pipette != null && g == pipette.Grabbable) return pipette.UprightRotation(-f);
        return Quaternion.LookRotation(f.normalized, Vector3.up);
    }

    void BeginAction(string label)
    {
        Busy = true;
        Grabber.SetReceiving(true, label);
        Grabber.TakeHeld();
    }

    IEnumerator BackToHand(Grabbable g)
    {
        yield return MoveTo(g.transform, HandPoint(g), HandRotation(g), 0.35f);
        var tube = g.GetComponent<SampleTube>();
        if (tube != null) tube.Undock();
        else SetPipettePhysics(true);
        Grabber.Grab(g);
        EndAction();
    }

    void EndAction()
    {
        Busy = false;
        if (Grabber != null) Grabber.SetReceiving(false);
    }

    void SetPipettePhysics(bool on)
    {
        var rb = pipette.Grabbable.Body;
        rb.isKinematic = !on;
        rb.interpolation = RigidbodyInterpolation.None;
        foreach (var c in pipette.GetComponentsInChildren<Collider>(true)) c.enabled = on;
    }

    static IEnumerator MoveTo(Transform t, Vector3 pos, Quaternion rot, float seconds)
    {
        Vector3 p0 = t.position;
        Quaternion r0 = t.rotation;
        float k = 0f;
        while (k < 1f)
        {
            k += Time.deltaTime / Mathf.Max(0.01f, seconds);
            float e = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(k));
            t.SetPositionAndRotation(Vector3.Lerp(p0, pos, e), Quaternion.Slerp(r0, rot, e));
            yield return null;
        }
        t.SetPositionAndRotation(pos, rot);
    }

    IEnumerator PlaceInRack(SampleTube tube)
    {
        int i = FreeRackSlot(tube);
        if (i < 0) yield break;
        var slot = rack.points[i];
        BeginAction("Colocando el tubo en la gradilla...");
        tube.Detach();
        yield return MoveTo(tube.transform, slot.position + Vector3.up * 0.13f, slot.rotation, 0.35f);
        yield return MoveTo(tube.transform, slot.position, slot.rotation, 0.25f);
        tube.DockAt(slot, true);
        EndAction();
        if (IsEducational && tube == sample && (sample.State == SampleTube.Content.WholeBlood || sample.State == SampleTube.Content.Pellet))
            ShowMessage("Tubo en la gradilla. Toma la micropipeta y cárgala con 6 mL de solución salina.");
    }

    IEnumerator PlaceInCentrifuge(SampleTube tube)
    {
        int slot = centrifuge.FreeSlot();
        if (slot < 0) yield break;
        var s = centrifuge.slots[slot];
        BeginAction("Colocando el tubo en el rotor...");
        tube.Detach();
        yield return MoveTo(tube.transform, s.position + s.up * 0.2f, s.rotation, 0.4f);
        yield return MoveTo(tube.transform, s.position, s.rotation, 0.3f);
        centrifuge.Seat(tube, slot);
        centrifuge.Beep(900f, 0.03f, 0.25f);
        EndAction();
        if (!IsEducational) yield break;
        if (tube == sample && sample.State == SampleTube.Content.Unmixed)
            ShowMessage("Ojo: la muestra aún no está resuspendida. Sácala y mézclala antes de centrifugar.");
        else if (centrifuge.Count == 1)
            ShowMessage(tube.isBalance ? "Balance colocado. Ahora coloca el tubo de muestra en la posición opuesta."
                                       : "Ahora coloca el tubo de balance: queda en la posición opuesta para equilibrar el rotor.");
        else ShowMessage("Rotor equilibrado (muestra y balance opuestos). Cierra la tapa con R.");
    }

    IEnumerator TakeFromCentrifuge(SampleTube tube)
    {
        int slot = centrifuge.SlotOf(tube);
        if (slot < 0) yield break;
        var s = centrifuge.slots[slot];
        BeginAction("Sacando el tubo del rotor...");
        centrifuge.Unseat(slot);
        tube.Detach();
        yield return MoveTo(tube.transform, s.position + s.up * 0.2f, s.rotation, 0.4f);
        yield return BackToHand(tube.Grabbable);
        if (tube == sample && sample.State == SampleTube.Content.Separated) OnSampleInspected();
    }

    void OnSampleInspected()
    {
        int n = sample.Spins;
        if (supernatantLog.Count < n) supernatantLog.Add(sample.SupernatantDescription);
        if (n >= requiredSpins && sample.SupernatantTint <= 0.001f)
        {
            finishing = true;
            ShowMessage("¡El sobrenadante quedó totalmente transparente! Los eritrocitos están lavados: la suspensión está lista.");
            StartCoroutine(FinishAfter(3.2f));
            return;
        }
        ShowMessage(IsEducational
            ? "Sobrenadante " + sample.SupernatantDescription + ". Todavía no está transparente: decántalo en el vaso de desechos y repite el lavado."
            : "Centrifugación " + n + " lista.");
    }

    IEnumerator FinishAfter(float seconds)
    {
        yield return new WaitForSeconds(seconds);
        CompleteLevel();
    }

    IEnumerator LoadPipette()
    {
        var g = pipette.Grabbable;
        BeginAction("Cargando la micropipeta...");
        SetPipettePhysics(false);
        Vector3 mouth = salineBottle.points[0].position;
        var rot = HandRotation(g);
        yield return MoveTo(g.transform, pipette.RootForTip(mouth + Vector3.up * 0.07f, rot), rot, 0.4f);
        yield return MoveTo(g.transform, pipette.RootForTip(mouth - Vector3.up * 0.035f, rot), rot, 0.3f);
        yield return new WaitForSeconds(0.25f);
        pipette.SetLoaded(true);
        yield return new WaitForSeconds(0.2f);
        yield return MoveTo(g.transform, pipette.RootForTip(mouth + Vector3.up * 0.07f, rot), rot, 0.3f);
        yield return BackToHand(g);
        if (IsEducational) ShowMessage("Micropipeta cargada con 6 mL. Ahora apunta al tubo de muestra en la gradilla y presiona E.");
    }

    IEnumerator Dispense()
    {
        var g = pipette.Grabbable;
        BeginAction("Dispensando la solución salina...");
        SetPipettePhysics(false);
        Vector3 mouth = sample.MouthPoint;
        var rot = HandRotation(g);
        yield return MoveTo(g.transform, pipette.RootForTip(mouth + Vector3.up * 0.06f, rot), rot, 0.4f);
        yield return MoveTo(g.transform, pipette.RootForTip(mouth - Vector3.up * 0.012f, rot), rot, 0.3f);
        float t = 0f;
        while (t < 1f)
        {
            t += Time.deltaTime / 0.9f;
            sample.PourSaline(salineMl, Mathf.Clamp01(t));
            yield return null;
        }
        pipette.SetLoaded(false);
        yield return MoveTo(g.transform, pipette.RootForTip(mouth + Vector3.up * 0.06f, rot), rot, 0.3f);
        yield return BackToHand(g);
        if (IsEducational)
            ShowMessage("Solución salina agregada. Deja la micropipeta en su soporte, toma el tubo y resuspende los eritrocitos invirtiéndolo suavemente.");
    }

    /// <summary>Deja la micropipeta vertical en su soporte (cinemática, se puede volver a tomar).</summary>
    void SeatPipette()
    {
        var seat = pipetteStand.points[0];
        SetPipettePhysics(false);
        foreach (var c in pipette.GetComponentsInChildren<Collider>(true)) c.enabled = true;
        var rot = SeatRotation();
        pipette.transform.SetPositionAndRotation(pipette.RootForTip(seat.position, rot), rot);
    }

    Quaternion SeatRotation() => pipette.UprightRotation(-pipetteStand.transform.forward);

    IEnumerator ReturnPipette()
    {
        var g = pipette.Grabbable;
        BeginAction("Colocando la micropipeta en su soporte...");
        SetPipettePhysics(false);
        var seat = pipetteStand.points[0];
        var rot = SeatRotation();
        yield return MoveTo(g.transform, pipette.RootForTip(seat.position + Vector3.up * 0.08f, rot), rot, 0.35f);
        yield return MoveTo(g.transform, pipette.RootForTip(seat.position, rot), rot, 0.25f);
        SeatPipette();
        EndAction();
    }

    IEnumerator DiscardPipette()
    {
        var g = pipette.Grabbable;
        BeginAction("Descartando el contenido de la punta...");
        SetPipettePhysics(false);
        Vector3 mouth = wasteBeaker.points[0].position;
        var rot = HandRotation(g);
        yield return MoveTo(g.transform, pipette.RootForTip(mouth + Vector3.up * 0.02f, rot), rot, 0.4f);
        yield return new WaitForSeconds(0.3f);
        pipette.SetLoaded(false);
        yield return BackToHand(g);
    }

    IEnumerator Decant()
    {
        BeginAction("Decantando el sobrenadante...");
        sample.Detach();
        var tr = sample.transform;
        Vector3 rim = wasteBeaker.points[0].position;
        Vector3 dir = rim - Grabber.Cam.transform.position; dir.y = 0f;
        dir = dir.sqrMagnitude > 1e-5f ? dir.normalized : Vector3.forward;
        var yaw = Quaternion.LookRotation(dir, Vector3.up);
        Vector3 side = Vector3.Cross(Vector3.up, dir); // derecha del jugador
        float top = sample.LocalTop;

        // Se acerca vertical por la derecha del vaso y se inclina de lado (de frente al jugador) hasta que la boca queda sobre él.
        Vector3 mouthTarget = rim + Vector3.up * 0.03f + side * 0.012f;
        Vector3 upright = mouthTarget + side * 0.07f - Vector3.up * (top - 0.03f);
        yield return MoveTo(tr, upright, yaw, 0.4f);
        var half = Quaternion.AngleAxis(88f, dir) * yaw;
        yield return MoveTo(tr, mouthTarget + side * 0.02f - half * new Vector3(0f, top, 0f), half, 0.4f);
        sample.SetPouring(true); // al pasar de horizontal, el líquido corre hacia la boca
        var tilted = Quaternion.AngleAxis(118f, dir) * yaw;
        yield return MoveTo(tr, mouthTarget - tilted * new Vector3(0f, top, 0f), tilted, 0.3f);

        sample.Decant(0f);
        Color c = SampleTube.SupernatantColor(sample.SupernatantTint);
        c.a = Mathf.Max(0.35f, c.a);
        float t = 0f;
        float w0 = wasteLiquid != null ? wasteLiquid.localScale.y : 0f;
        while (t < 1f)
        {
            t += Time.deltaTime / 1.3f;
            sample.Decant(Mathf.Clamp01(t));
            ShowStream(sample.MouthPoint, rim + Vector3.down * 0.06f, c, 1f - Mathf.Clamp01(t) * 0.6f);
            if (wasteLiquid != null)
            {
                var s = wasteLiquid.localScale;
                s.y = w0 + wasteBaseScale * 0.55f * Mathf.Clamp01(t);
                wasteLiquid.localScale = s;
            }
            yield return null;
        }
        HideStream();
        sample.SetPouring(false);
        decants++;
        if (wasteLiquidRenderer != null)
        {
            mpb.SetColor("_BaseColor", new Color(0.78f, 0.38f, 0.28f, 0.75f));
            wasteLiquidRenderer.SetPropertyBlock(mpb);
            wasteLiquidRenderer.enabled = true;
        }
        yield return MoveTo(tr, upright, yaw, 0.45f);
        yield return BackToHand(sample.Grabbable);
        if (IsEducational)
            ShowMessage("Sobrenadante decantado: el paquete de eritrocitos quedó en el fondo. Coloca el tubo en la gradilla y agrega solución salina para el siguiente lavado.");
    }

    void CreateStream()
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        go.name = "ChorroLiquido_N3";
        Destroy(go.GetComponent<Collider>());
        streamRenderer = go.GetComponent<MeshRenderer>();
        streamRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        streamRenderer.sortingOrder = 2;
        if (streamMaterial != null) streamRenderer.sharedMaterial = streamMaterial;
        stream = go.transform;
        stream.SetParent(transform, false);
        go.SetActive(false);
    }

    void ShowStream(Vector3 from, Vector3 to, Color c, float width)
    {
        if (stream == null) return;
        stream.gameObject.SetActive(true);
        Vector3 d = to - from;
        stream.position = from + d * 0.5f;
        stream.rotation = Quaternion.FromToRotation(Vector3.up, d.normalized);
        float w = 0.007f * width;
        stream.localScale = new Vector3(w, d.magnitude * 0.5f, w);
        mpb.SetColor("_BaseColor", c);
        streamRenderer.SetPropertyBlock(mpb);
    }

    void HideStream()
    {
        if (stream != null) stream.gameObject.SetActive(false);
    }

    // ------------------------------------------------------------------
    //  Resuspensión (clic derecho + mover el mouse: invertir el tubo)
    // ------------------------------------------------------------------

    void UpdateMixing()
    {
        bool can = !Busy && GameFlow.GameplayInputAllowed && Grabber != null && HoldingSample(Grabber.Held) && sample.NeedsMix;
        bool want = can && RightHeld();
        if (want != mixing)
        {
            mixing = want;
            if (playerCtl != null) playerCtl.lookEnabled = !want;
        }
        if (mixing) ApplyMixStroke(MouseDelta());
        else tilt = Mathf.MoveTowards(tilt, 0f, 220f * Time.deltaTime);
    }

    /// <summary>Un movimiento del mouse mientras se invierte el tubo (público para pruebas).</summary>
    public void ApplyMixStroke(Vector2 delta)
    {
        if (!sample.NeedsMix) return;
        float px = delta.x / Mathf.Max(1f, Screen.height) * 1080f;
        float prev = tilt;
        tilt = Mathf.Clamp(tilt + px * 0.45f, -80f, 80f);
        float moved = Mathf.Abs(tilt - prev);
        float gain = Mathf.Min(moved / (320f * inversionsToMix), maxMixRate * Time.deltaTime);
        if (sample.AddMix(gain))
        {
            ShowMessage(IsEducational
                ? "¡Suspensión homogénea! Lleva el tubo a la centrífuga con su tubo de balance."
                : "Muestra resuspendida.");
        }
    }

    void UpdateMixBar()
    {
        bool show = Grabber != null && HoldingSample(Grabber.Held) && sample.NeedsMix && !Busy;
        if (mixGroup != null && mixGroup.activeSelf != show) mixGroup.SetActive(show);
        if (!show) return;
        if (mixFill != null)
        {
            mixFill.fillAmount = sample.Mix;
            mixFill.color = Color.Lerp(new Color(1f, 0.78f, 0.3f), new Color(0.35f, 0.85f, 0.55f), sample.Mix);
        }
        if (mixLabel != null)
            mixLabel.text = mixing ? "Resuspendiendo... " + Mathf.RoundToInt(sample.Mix * 100f) + " %" : "Clic derecho + mover: resuspender";
    }

    static bool RightHeld() => GameInput.SecondaryHeld();

    static Vector2 MouseDelta() => GameInput.LookDelta();

    // ------------------------------------------------------------------
    //  Guía (objetivo, lista de pasos y marcador)
    // ------------------------------------------------------------------

    void HideGuide()
    {
        if (objectivePanel != null && objectivePanel.activeSelf) objectivePanel.SetActive(false);
        if (checklistPanel != null && checklistPanel.activeSelf) checklistPanel.SetActive(false);
        if (marker != null && marker.gameObject.activeSelf) marker.gameObject.SetActive(false);
        if (mixGroup != null && mixGroup.activeSelf) mixGroup.SetActive(false);
    }

    void UpdateGuide(Step step)
    {
        bool show = IsEducational && (panel == null || !panel.IsOpen);
        if (objectivePanel != null && objectivePanel.activeSelf != show) objectivePanel.SetActive(show);
        if (checklistPanel != null && checklistPanel.activeSelf != show) checklistPanel.SetActive(show);
        if (!show) { if (marker != null) marker.gameObject.SetActive(false); return; }
        if (Busy) { UpdateMarker(null); return; } // durante una animación se conserva el objetivo anterior

        var held = Grabber != null ? Grabber.Held : null;
        var c = centrifuge;
        string objective;
        Transform target;
        switch (step)
        {
            case Step.AddSaline:
                if (!InRack(sample))
                {
                    if (HoldingSample(held)) { objective = "Coloca el tubo de muestra en la gradilla: apúntala y presiona E."; target = rack.transform; }
                    else if (c.Contains(sample)) { objective = c.LidOpen ? "Saca el tubo de muestra de la centrífuga (E)." : "Abre la centrífuga (E) y saca el tubo de muestra."; target = c.transform; }
                    else { objective = "Toma el tubo de muestra y colócalo en la gradilla."; target = sample.transform; }
                }
                else if (!HoldingPipette(held))
                {
                    objective = (sample.State == SampleTube.Content.WholeBlood ? "Agrega 6 mL de solución salina (NaCl 0.9 %) a la sangre. " : "Nuevo lavado: agrega 6 mL de solución salina al paquete de eritrocitos. ")
                                + (held != null && HeldTube(held) != null ? "Primero deja el tubo en la gradilla." : "Toma la micropipeta (ajustada a 6 mL).");
                    target = held != null && HeldTube(held) != null ? rack.transform : pipette.transform;
                }
                else if (!pipette.Loaded) { objective = "Carga la micropipeta: apunta al frasco de solución salina y presiona E."; target = salineBottle.transform; }
                else { objective = "Dispensa la solución salina: apunta al tubo de muestra en la gradilla y presiona E."; target = sample.transform; }
                break;
            case Step.Mix:
                if (HoldingSample(held)) { objective = "Resuspende: mantén clic derecho y mueve el mouse de lado a lado para invertir el tubo suavemente hasta que quede homogéneo."; target = null; }
                else if (HoldingPipette(held)) { objective = "Deja la micropipeta en su soporte (apúntalo y presiona E) y toma el tubo de muestra."; target = pipetteStand != null ? pipetteStand.transform : sample.transform; }
                else { objective = "Toma el tubo de muestra para resuspender los eritrocitos."; target = sample.transform; }
                break;
            case Step.Load:
                if (!c.LidOpen) { objective = "Abre la tapa de la centrífuga: apúntala y presiona E."; target = c.transform; }
                else if (!c.Contains(sample))
                {
                    if (HoldingSample(held)) { objective = "Coloca el tubo de muestra en el rotor: apunta a la centrífuga y presiona E."; target = c.transform; }
                    else if (held != null && HeldTube(held) == null) { objective = "Deja lo que llevas y toma el tubo de muestra."; target = sample.transform; }
                    else if (HeldTube(held) == balance) { objective = "Coloca el tubo de balance en el rotor (E); después, la muestra."; target = c.transform; }
                    else { objective = "Toma el tubo de muestra y colócalo en el rotor."; target = sample.transform; }
                }
                else
                {
                    if (HeldTube(held) == balance) { objective = "Coloca el tubo de balance en el rotor: queda en la posición opuesta a la muestra (E)."; target = c.transform; }
                    else { objective = "Equilibra el rotor: toma el tubo de balance (mismo volumen, en la gradilla) y colócalo opuesto a la muestra."; target = balance.transform; }
                }
                break;
            case Step.Close:
                objective = "Cierra la tapa de la centrífuga: apúntala y presiona R."; target = c.transform; break;
            case Step.Program:
                objective = "Programa la centrífuga: apúntala y presiona R. Ajusta 2500 rpm y 5 min y presiona Iniciar."; target = c.transform; break;
            case Step.Spinning:
                objective = "Centrifugando a 2500 rpm... espera a que el rotor se detenga (faltan " + Centrifuge.Clock(c.RemainingSeconds) + ")."; target = c.transform; break;
            case Step.Unload:
                if (!c.LidOpen) { objective = "Abre la tapa de la centrífuga (E)."; target = c.transform; }
                else if (held != null) { objective = "Deja lo que llevas para sacar el tubo de muestra."; target = c.transform; }
                else { objective = "Saca el tubo de muestra (E) y observa el sobrenadante."; target = c.transform; }
                break;
            case Step.Decant:
                if (HoldingSample(held)) { objective = "Decanta el sobrenadante " + sample.SupernatantDescription + ": apunta al vaso de desechos y presiona E."; target = wasteBeaker.transform; }
                else { objective = "Toma el tubo de muestra para decantar el sobrenadante."; target = sample.transform; }
                break;
            default:
                objective = "¡Sobrenadante transparente! Suspensión de eritrocitos lista."; target = null; break;
        }
        if (objectiveText != null) objectiveText.text = "<b>OBJETIVO</b>\n" + objective;
        if (checklistText != null) checklistText.text = BuildChecklist(step);
        UpdateMarker(target);
    }

    string BuildChecklist(Step step)
    {
        var sb = new System.Text.StringBuilder();
        int cycle = Cycle;
        sb.Append("<b>LAVADO ").Append(cycle).Append(" DE ").Append(requiredSpins).Append("</b>\n");
        int cur = ChecklistIndex(step);
        bool last = cycle >= requiredSpins;
        for (int i = 0; i < CycleSteps.Length; i++)
        {
            string name = CycleSteps[i];
            if (i == 5 && last) name = "Sobrenadante transparente: ¡listo!";
            bool done = step == Step.Done || i < cur;
            bool current = i == cur;
            string mark = done ? "<color=#7CF0B0>●</color>" : current ? "<color=#FFD24A>○</color>" : "<color=#8A96A8>○</color>";
            sb.Append(mark).Append(' ').Append(current ? "<b>" + name + "</b>" : name).Append('\n');
        }
        if (supernatantLog.Count > 0)
        {
            sb.Append("\n<size=85%>Sobrenadante: ");
            for (int i = 0; i < supernatantLog.Count; i++)
            {
                if (i > 0) sb.Append("  >  ");
                sb.Append(ShortTint(supernatantLog[i]));
            }
            sb.Append("</size>");
        }
        return sb.ToString().TrimEnd();
    }

    static string ShortTint(string description) =>
        description.StartsWith("rojizo") ? "<color=#E0805A>rojizo</color>" : description.StartsWith("rosado") ? "<color=#F2A6A0>rosado</color>" : "<color=#BFE8FF>transparente</color>";

    void CreateMarker()
    {
        if (markerMaterial == null) return;
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = "MarcadorGuia_N3";
        Destroy(go.GetComponent<Collider>());
        var r = go.GetComponent<Renderer>();
        r.sharedMaterial = markerMaterial;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        go.transform.localScale = new Vector3(0.045f, 0.045f, 0.045f);
        marker = go.transform;
        go.SetActive(false);
    }

    void UpdateMarker(Transform target)
    {
        if (marker == null) return;
        if (target == null || Busy) { marker.gameObject.SetActive(false); return; }
        var held = Grabber != null ? Grabber.Held : null;
        if (held != null && target == held.transform) { marker.gameObject.SetActive(false); return; }
        float top = target.position.y + 0.2f;
        foreach (var r in target.GetComponentsInChildren<Renderer>())
            if (r.transform != marker && r.enabled) top = Mathf.Max(top, r.bounds.max.y + 0.1f);
        top = Mathf.Min(top, target.position.y + 0.8f);
        marker.gameObject.SetActive(true);
        marker.position = new Vector3(target.position.x, top + Mathf.Sin(Time.time * 3f) * 0.02f, target.position.z);
        marker.rotation = Quaternion.Euler(45f, Time.time * 90f, 45f);
    }

    // ------------------------------------------------------------------
    //  Errores, mensajes y fin del nivel
    // ------------------------------------------------------------------

    void AddError(string key, string description)
    {
        if (!IsRunning) return;
        if (key != null && !errorKeys.Add(key)) return;
        errors.Add(description);
    }

    public void ShowMessage(string msg)
    {
        if (toastText == null) return;
        toastText.text = msg;
        if (toastRoutine != null) StopCoroutine(toastRoutine);
        toastRoutine = StartCoroutine(ToastRoutine());
    }

    IEnumerator ToastRoutine()
    {
        if (toastGroup != null) toastGroup.alpha = 1f;
        yield return new WaitForSecondsRealtime(toastDuration);
        float t = 0f;
        while (t < 0.4f)
        {
            t += Time.unscaledDeltaTime;
            if (toastGroup != null) toastGroup.alpha = 1f - t / 0.4f;
            yield return null;
        }
        if (toastGroup != null) toastGroup.alpha = 0f;
    }

    void CompleteLevel()
    {
        if (completed) return;
        completed = true;
        IsRunning = false;
        if (mixing && playerCtl != null) playerCtl.lookEnabled = true;
        mixing = false;
        float elapsed = Time.time - startTime;
        HideGuide();
        if (timerText != null) timerText.gameObject.SetActive(false);
        if (toastRoutine != null) { StopCoroutine(toastRoutine); toastRoutine = null; }
        if (toastGroup != null) toastGroup.alpha = 0f;
        if (counterText != null) counterText.text = "Nivel 3 · Suspensión de eritrocitos lista";

        float overSeconds = elapsed - parTimeSeconds;
        float timePenalty = overSeconds > 0f ? Mathf.Min(maxTimePenalty, Mathf.Ceil(overSeconds / 60f) * timePenaltyPerMinute) : 0f;
        float final = Mathf.Clamp(10f - errors.Count * penaltyPerError - timePenalty, 0f, 10f);
        final = Mathf.Round(final * 10f) / 10f;
        bool passed = final >= passingGrade;

        if (completeTimeText != null) completeTimeText.text = "Tiempo: " + FormatTime(elapsed);
        if (completeTitleText != null) completeTitleText.text = IsEducational ? "¡Eritrocitos lavados!" : "Evaluación del nivel 3 terminada";
        if (gradeText != null)
        {
            if (IsEducational)
            {
                gradeText.text = "Modo educativo  ·  " + sample.Spins + " lavados  ·  sobrenadante claro";
                gradeText.color = new Color(0.45f, 0.9f, 0.65f);
            }
            else
            {
                gradeText.text = "Calificación: " + final.ToString("0.0") + " / 10   —   " + (passed ? "APROBADO" : "NO APROBADO");
                gradeText.color = passed ? new Color(0.45f, 0.9f, 0.65f) : new Color(1f, 0.45f, 0.4f);
            }
        }

        var sb = new System.Text.StringBuilder();
        sb.Append("Suspensión de eritrocitos lavados · ").Append(sample.Spins).Append(" centrifugaciones a ")
          .Append(targetRpm).Append(" rpm × ").Append(Centrifuge.Clock(targetSeconds)).Append(" min\n");
        sb.Append("• Sobrenadante: ");
        for (int i = 0; i < supernatantLog.Count; i++)
        {
            if (i > 0) sb.Append("  >  ");
            sb.Append(i + 1).Append("º ").Append(ShortTint(supernatantLog[i]));
        }
        sb.Append('\n');
        if (wrongConfigs > 0) sb.Append("• Programaciones fuera del protocolo: ").Append(wrongConfigs).Append('\n');
        if (!IsEducational)
        {
            sb.Append("• Procedimiento: ").Append(final.ToString("0.0")).Append("/10");
            if (timePenalty > 0f) sb.Append(" (tardaste más de ").Append(FormatTime(parTimeSeconds)).Append(", −").Append(timePenalty.ToString("0.#")).Append(')');
            sb.Append('\n');
        }
        if (errors.Count == 0) sb.Append("Sin errores en el procedimiento. ¡Excelente!");
        else
        {
            sb.Append("Errores (").Append(errors.Count).Append("):\n");
            foreach (var e in errors) sb.Append("• ").Append(e).Append('\n');
        }

        if (resultDetailText != null)
        {
            resultDetailText.enableAutoSizing = true;
            resultDetailText.fontSizeMin = 13f;
            resultDetailText.fontSizeMax = 22f;
            resultDetailText.text = sb.ToString().TrimEnd();
        }
        if (continueButton != null)
        {
            // El nivel 3 es el último por ahora: sólo quedan "Reintentar" y "Menú principal", centrados.
            continueButton.SetActive(false);
            var parent = continueButton.transform.parent;
            var retry = parent.Find("RetryButton") as RectTransform;
            var menu = parent.Find("MenuButton") as RectTransform;
            if (retry != null) retry.anchoredPosition = new Vector2(-170f, retry.anchoredPosition.y);
            if (menu != null) menu.anchoredPosition = new Vector2(170f, menu.anchoredPosition.y);
        }
        if (completePanel != null) completePanel.SetActive(true);

        LabDatabase.InsertResult(3, Mode, IsEducational ? (float?)null : final, elapsed, errors.Count,
            "lavados=" + sample.Spins.ToString(CultureInfo.InvariantCulture) +
            "; config_incorrectas=" + wrongConfigs.ToString(CultureInfo.InvariantCulture) +
            "; sobrenadante=" + string.Join(">", supernatantLog) + "; " + string.Join(" | ", errors));

        if (GameFlow.Instance != null) GameFlow.Instance.ShowResults();
    }

    static string FormatTime(float seconds)
    {
        int s = Mathf.Max(0, Mathf.FloorToInt(seconds));
        return (s / 60).ToString("00") + ":" + (s % 60).ToString("00");
    }
}
