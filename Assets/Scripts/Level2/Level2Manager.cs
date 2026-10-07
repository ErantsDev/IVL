using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Nivel 2 · Preparación del animal:
///   1) Ponerse guantes.  2) Sacar al conejo de la jaula con cuidado.  3) Tarar la báscula.
///   4) Pesarlo (2.5–4.0 kg): registrar el peso si es apto o reportarlo si no lo es (se trae otro).
///      Cada pesaje se guarda en la base de datos SQLite y se muestra en la bitácora de la pared.
///   5) Llevarlo al paño.  6) Rasurar la zona dorsal (5 × 5 cm, minijuego).  7) Desinfectar con alcohol (minijuego).
/// Modo educativo: objetivo, lista de pasos, marcador, barra de estrés y avisos.
/// Modo evaluación: sin guías; se registran errores y tiempo y se califica.
/// </summary>
public class Level2Manager : MonoBehaviour, ILevelController
{
    public static Level2Manager Instance { get; private set; }

    public const float MinKg = 2.5f;
    public const float MaxKg = 4.0f;

    [Header("Escena")]
    public GameObject rabbitPrefab;
    public Transform cageSpawnPoint;
    public CageDoor cageDoor;
    public Transform cage;
    public Transform gloveBox;
    public ScaleDevice scale;
    public Transform pad;
    public Transform padPoint;
    public Grabbable clipper;
    public GauzePad gauze;
    public Transform alcoholBottle;
    public DorsalMinigame minigame;
    public Material markerMaterial;

    [Header("Configuración")]
    public float scaleResidualKg = 0.15f;
    [Range(0f, 1f)] public float firstRabbitOutOfRangeChance = 0.35f;
    public float toastDuration = 3.8f;

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
    public GameObject stressGroup;
    public Image stressFill;
    public TMP_Text stressLabel;

    [Header("UI - Fin de nivel")]
    public GameObject completePanel;
    public TMP_Text completeTitleText;
    public TMP_Text completeTimeText;
    public TMP_Text gradeText;
    public TMP_Text resultDetailText;
    public GameObject continueButton;

    [Header("Evaluación")]
    public float parTimeSeconds = 420f;
    public float penaltyPerError = 1f;
    public float timePenaltyPerMinute = 0.5f;
    public float maxTimePenalty = 2f;
    public float passingGrade = 6f;
    [Range(0f, 1f)] public float procedureWeight = 0.5f;
    [Range(0f, 1f)] public float shaveWeight = 0.3f;
    [Range(0f, 1f)] public float disinfectWeight = 0.2f;

    public enum Step { Gloves, TakeRabbit, Tare, Weigh, ToPad, Shave, Disinfect, Done }

    static readonly string[] StepNames =
    {
        "Ponerse guantes", "Sacar al conejo de la jaula", "Tarar la báscula", "Pesar al conejo",
        "Llevar al conejo al paño", "Rasurar la zona dorsal", "Desinfectar con alcohol", "Terminado"
    };

    public GameMode Mode { get; private set; } = GameMode.Educativo;
    public bool IsRunning { get; private set; }
    public bool IsEducational => Mode == GameMode.Educativo;
    public RabbitController Rabbit { get; private set; }
    public int ErrorCount => errors.Count;
    public float ElapsedSeconds => IsRunning ? Time.time - startTime : 0f;

    readonly List<string> errors = new List<string>();
    readonly HashSet<string> errorKeys = new HashSet<string>();

    bool glovesOn;
    bool weightDone;
    bool everTared;
    bool shaved;
    bool disinfected;
    bool completed;
    float registeredKg;
    string registeredCode;
    int rabbitsReported;
    int rabbitVariant;
    float pendingWeight;
    float startTime;
    float lastStressWarn;
    float fleeHintAt;
    const string PetHow = "mantén clic derecho y mueve el mouse de lado a lado";
    DorsalMinigame.Result shaveResult, disinfectResult;
    Coroutine toastRoutine;
    Transform marker;

    // ------------------------------------------------------------------
    //  Ciclo de vida
    // ------------------------------------------------------------------

    void Awake()
    {
        Instance = this;
        LabDatabase.EnsureInitialized();
        if (scale != null) scale.SetResidual(scaleResidualKg);
        if (cageDoor != null) cageDoor.SetOpen(false, true);
        if (gauze != null) gauze.SetSoaked(false);
        pendingWeight = RandomWeight(Random.value < firstRabbitOutOfRangeChance);
        SpawnRabbit("CNJ-····", pendingWeight);
        if (completePanel != null) completePanel.SetActive(false);
        if (toastGroup != null) toastGroup.alpha = 0f;
        if (stressGroup != null) stressGroup.SetActive(false);
        CreateMarker();
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
        PlayerController.SpeedMultiplier = 1f;
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
        startTime = Time.time;
        errors.Clear();
        errorKeys.Clear();
        ObjectGrabber.MessageSink = ShowMessage;
        ObjectGrabber.AimHint = null;
        if (PlayerAvatar.Instance != null) PlayerAvatar.Instance.SetGloves(false);

        // El conejo que ya estaba en la jaula recibe su código y queda dado de alta en la base de datos.
        string code = LabDatabase.NextRabbitCode();
        Rabbit.Setup(code, pendingWeight, rabbitVariant);
        LabDatabase.InsertRabbit(code, pendingWeight);

        if (modeText != null)
        {
            modeText.text = IsEducational ? "MODO EDUCATIVO · NIVEL 2" : "MODO EVALUACIÓN · NIVEL 2";
            modeText.color = IsEducational ? new Color(0.45f, 0.9f, 0.65f) : new Color(1f, 0.72f, 0.35f);
        }
        ShowMessage(IsEducational
            ? "Nivel 2: prepara al conejo. Sigue el objetivo (arriba a la izquierda) y la lista de pasos."
            : "Nivel 2 (evaluación): prepara al conejo siguiendo el procedimiento. Se registran tus errores y tu tiempo.");
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

        var step = CurrentStep;
        if (counterText != null)
            counterText.text = !IsEducational ? "Nivel 2 · Preparación del animal"
                : step == Step.Done ? "Procedimiento terminado" : "Paso " + ((int)step + 1) + "/7 · " + StepNames[(int)step];

        UpdateStress();
        UpdateFlee();
        UpdateGuide(step);
    }

    // ------------------------------------------------------------------
    //  Conejos
    // ------------------------------------------------------------------

    static float RandomWeight(bool outOfRange)
    {
        float w;
        if (outOfRange) w = Random.value < 0.5f ? Random.Range(2.0f, 2.4f) : Random.Range(4.15f, 4.6f);
        else w = Random.Range(2.6f, 3.9f);
        return Mathf.Round(w * 100f) / 100f;
    }

    void SpawnRabbit(string code, float weight)
    {
        var go = Instantiate(rabbitPrefab, cageSpawnPoint.position, cageSpawnPoint.rotation);
        go.name = "Conejo";
        Rabbit = go.GetComponent<RabbitController>();
        rabbitVariant = Random.Range(0, 3);
        Rabbit.Setup(code, weight, rabbitVariant);
        var g = Rabbit.Grabbable;
        g.displayName = "Conejo";
        g.GrabBlocker = RabbitGrabBlocker;
        var rabbit = Rabbit;
        g.ReleaseBlocker = () => rabbit.NeedsCalming
            ? "El conejo sigue asustado: primero cálmalo (" + PetHow + ")." 
            : "No sueltes al conejo en cualquier lugar: apunta a la báscula, al paño o a la jaula y presiona E.";
        g.HeldPrompt = () => rabbit.IsBeingPetted && !rabbit.NeedsCalming
            ? "Acariciando al conejo... (suelta el clic derecho para seguir)"
            : rabbit.NeedsCalming
            ? (rabbit.IsBeingPetted ? "Acariciando... sigue moviendo el mouse de lado a lado." : "¡Está asustado! Acarícialo para calmarlo:\nmantén clic derecho y mueve el mouse de lado a lado.")
            : "Llevas al conejo en brazos: muévete despacio.\nApunta a la báscula, al paño o a la jaula y presiona E.\n<size=14>Clic derecho + mover el mouse: acariciarlo</size>";
    }

    IEnumerator BringNewRabbit()
    {
        yield return new WaitForSeconds(1.6f);
        if (cageDoor != null) cageDoor.SetOpen(false);
        yield return new WaitForSeconds(0.6f);
        pendingWeight = RandomWeight(false);
        string code = LabDatabase.NextRabbitCode();
        SpawnRabbit(code, pendingWeight);
        LabDatabase.InsertRabbit(code, pendingWeight);
        ShowMessage("Llegó otro conejo a la jaula (" + code + "). Sácalo con cuidado y pésalo.");
    }

    string RabbitGrabBlocker()
    {
        if (!IsRunning) return "Primero inicia el nivel.";
        if (minigame != null && minigame.IsActive) return "Termina el procedimiento en curso.";
        if (Rabbit.IsMoving || Rabbit.Place == RabbitPlace.Leaving) return "Espera un momento...";
        if (Rabbit.IsFleeing && Rabbit.IsHopping) return "¡Está saltando! Atrápalo justo cuando aterrice.";
        if (Rabbit.IsFleeing && ObjectGrabber.Instance != null)
        {
            Vector3 d = Rabbit.transform.position - ObjectGrabber.Instance.Cam.transform.position;
            d.y = 0f;
            if (d.magnitude > Rabbit.fleeGrabDistance) return "Está muy lejos: acércate más para atraparlo.";
        }
        if (Rabbit.Place == RabbitPlace.OnPad && shaved) return "El conejo debe quedarse en el paño hasta terminar la desinfección.";
        if (Rabbit.Place == RabbitPlace.OnScale && !weightDone)
            return scale.IsStable ? "Decide: E registra el peso (apto) · R reporta al conejo (no apto)." : "Espera a que la lectura se estabilice.";
        if (!glovesOn)
        {
            if (IsEducational) return "Ponte los guantes antes de tocar al conejo (caja de guantes en el extremo izquierdo de la mesa).";
            AddError("guantes", "Manipulaste al conejo sin guantes.");
        }
        return null;
    }

    public void OnRabbitPicked(RabbitController r, RabbitPlace previous)
    {
        if (previous == RabbitPlace.OnScale && scale != null) scale.ClearLoad();
        if (!IsEducational) return;
        if (previous == RabbitPlace.InCage)
            ShowMessage("Sosténlo con calma: camina despacio y sin girar bruscamente (mira la barra de estrés).");
        else if (previous == RabbitPlace.Escaped && r.NeedsCalming)
            ShowMessage("¡Lo atrapaste! Ahora cálmalo: " + PetHow + " para acariciarlo.");
        else if (previous == RabbitPlace.Escaped)
            ShowMessage("Lo recuperaste. Esta vez muévete despacio para que no se asuste.");
        else if (previous == RabbitPlace.OnScale && weightDone)
            ShowMessage("Ahora lleva al conejo al paño azul de la mesa.");
    }

    public void OnRabbitEscaped(RabbitController r)
    {
        AddError(null, "El conejo se escapó de tus brazos por moverte con brusquedad.");
        fleeHintAt = Time.time + 15f;
        ShowMessage("¡El conejo se espantó y está saltando por el salón! Atrápalo rápido: acércate y presiona E.");
    }

    public void OnRabbitCalmed(RabbitController r)
    {
        ShowMessage(IsEducational
            ? "El conejo ya está tranquilo. Continúa con el procedimiento y muévete despacio."
            : "El conejo se calmó.");
    }

    /// <summary>Mientras el conejo huye: pista a los 15 s (educativo) o error si tarda más de 20 s (evaluación).</summary>
    void UpdateFlee()
    {
        if (Rabbit == null || !Rabbit.IsFleeing) return;
        if (IsEducational)
        {
            if (Time.time >= fleeHintAt)
            {
                fleeHintAt = Time.time + 15f;
                ShowMessage("Pista: acorrálalo contra una pared o un mueble y presiona E justo cuando aterrice.");
            }
        }
        else if (Rabbit.FleeElapsed > 20f)
            AddError("huida_" + Rabbit.Code + "_" + Rabbit.EscapeCount, "Tardaste más de 20 s en atrapar al conejo espantado.");
    }

    // ------------------------------------------------------------------
    //  Paso actual
    // ------------------------------------------------------------------

    public Step CurrentStep
    {
        get
        {
            if (!glovesOn) return Step.Gloves;
            if (!weightDone)
            {
                if (Rabbit == null || Rabbit.Place == RabbitPlace.InCage || Rabbit.Place == RabbitPlace.Leaving) return Step.TakeRabbit;
                if (Rabbit.Place == RabbitPlace.Escaped) return Step.TakeRabbit;
                if (scale != null && !scale.IsTared && Rabbit.Place != RabbitPlace.OnScale) return Step.Tare;
                return Step.Weigh;
            }
            if (!shaved && Rabbit.Place != RabbitPlace.OnPad) return Step.ToPad;
            if (!shaved) return Step.Shave;
            if (!disinfected) return Step.Disinfect;
            return Step.Done;
        }
    }

    bool Holding(Grabbable held, Object what) => held != null && what != null && (held == what || held.gameObject == (what as Component)?.gameObject);
    bool HoldingRabbit(Grabbable held) => Rabbit != null && held == Rabbit.Grabbable;
    bool HoldingClipper(Grabbable held) => clipper != null && held == clipper;
    bool HoldingGauze(Grabbable held) => gauze != null && held != null && held.gameObject == gauze.gameObject;

    // ------------------------------------------------------------------
    //  Interacciones de las estaciones
    // ------------------------------------------------------------------

    public string StationPrompt(Level2Station st, Grabbable held, int action)
    {
        if (!IsRunning || completed) return null;
        if (HoldingRabbit(held) && Rabbit.NeedsCalming) return null; // primero hay que calmarlo
        switch (st.kind)
        {
            case Level2Station.Kind.GloveBox:
                return action == 0 && !glovesOn && held == null ? "ponerse los guantes de nitrilo" : null;

            case Level2Station.Kind.Cage:
                if (action != 0) return null;
                if (HoldingRabbit(held)) return cageDoor.IsOpen ? "regresar al conejo a la jaula" : "abrir la jaula";
                return cageDoor.IsOpen ? "cerrar la jaula" : "abrir la jaula";

            case Level2Station.Kind.Scale:
                return ScalePrompt(held, action);

            case Level2Station.Kind.Pad:
                return action == 0 && HoldingRabbit(held) ? "colocar al conejo sobre el paño" : null;

            case Level2Station.Kind.AlcoholBottle:
                return action == 0 && HoldingGauze(held) && !gauze.IsSoaked ? "empapar la gasa con alcohol" : null;
        }
        return null;
    }

    public string StationInfo(Level2Station st, Grabbable held)
    {
        if (!IsRunning) return null;
        switch (st.kind)
        {
            case Level2Station.Kind.GloveBox:
                return glovesOn ? "Ya tienes los guantes puestos." : "Guantes de nitrilo (talla M).";
            case Level2Station.Kind.Scale:
                return ScaleInfo();
            case Level2Station.Kind.AlcoholBottle:
                if (HoldingGauze(held) && gauze.IsSoaked) return "La gasa ya está empapada.";
                return "Alcohol etílico al 70 %. Empapa aquí una gasa.";
            case Level2Station.Kind.Pad:
                return HoldingRabbit(held) ? null : "Paño quirúrgico: aquí se prepara al conejo.";
        }
        return null;
    }

    public void StationInteract(Level2Station st, Grabbable held, int action)
    {
        switch (st.kind)
        {
            case Level2Station.Kind.GloveBox:
                glovesOn = true;
                if (PlayerAvatar.Instance != null) PlayerAvatar.Instance.SetGloves(true);
                ShowMessage(IsEducational ? "Te pusiste los guantes. Ahora abre la jaula y saca al conejo con cuidado." : "Guantes puestos.");
                break;

            case Level2Station.Kind.Cage:
                if (HoldingRabbit(held) && cageDoor.IsOpen)
                {
                    ObjectGrabber.Instance.TakeHeld();
                    Rabbit.PlaceAt(cageSpawnPoint.position, cageSpawnPoint.rotation, RabbitPlace.InCage, 0.6f, 0.08f);
                }
                else cageDoor.Toggle();
                break;

            case Level2Station.Kind.Scale:
                ScaleInteract(held, action);
                break;

            case Level2Station.Kind.Pad:
                PlaceOnPad();
                break;

            case Level2Station.Kind.AlcoholBottle:
                gauze.SetSoaked(true);
                ShowMessage(IsEducational ? "Gasa empapada en alcohol al 70 %. Ahora desinfecta la zona rasurada del conejo." : "Gasa empapada.");
                break;
        }
    }

    // ---------- Báscula ----------

    bool Undecided => Rabbit != null && scale != null && scale.Load == Rabbit && Rabbit.Place == RabbitPlace.OnScale && !weightDone;

    string ScalePrompt(Grabbable held, int action)
    {
        if (scale == null) return null;
        if (HoldingRabbit(held))
        {
            if (action == 0) return scale.Load == null ? "colocar al conejo en la báscula" : null;
            return scale.Load == null ? "tarar la báscula (poner en cero)" : null;
        }
        if (Undecided)
        {
            if (!scale.IsStable) return null;
            return action == 0 ? "registrar el peso (conejo APTO)" : "reportar al conejo como NO APTO";
        }
        if (held == null && scale.Load == null && action == 0) return "tarar la báscula (poner en cero)";
        return null;
    }

    string ScaleInfo()
    {
        if (scale == null) return null;
        string reading = "Lectura: " + Kg(scale.Displayed);
        if (Undecided) return reading + (scale.IsStable ? "  ·  ESTABLE" : "  ·  espera a que se estabilice...");
        if (weightDone && scale.Load == Rabbit) return "Peso registrado: " + Kg(registeredKg) + " (guardado en la bitácora)";
        return reading;
    }

    void ScaleInteract(Grabbable held, int action)
    {
        if (HoldingRabbit(held))
        {
            if (action == 1) { TareScale(); return; }
            PlaceOnScale();
            return;
        }
        if (Undecided)
        {
            if (action == 0) RegisterWeight();
            else ReportRabbit();
            return;
        }
        TareScale();
    }

    void TareScale()
    {
        scale.Tare();
        everTared = true;
        if (IsEducational) ShowMessage("Báscula tarada: ahora marca 0.00 kg.");
    }

    void PlaceOnScale()
    {
        if (!scale.IsTared)
        {
            if (IsEducational)
            {
                ShowMessage("Primero tara la báscula: marca " + Kg(scale.Displayed) + " sin nada encima. Presiona R apuntándola (o E con las manos vacías).");
                return;
            }
            AddError("sinTara", "Pesaste al conejo sin tarar la báscula (la lectura tenía +" + Kg(scaleResidualKg) + ").");
        }
        ObjectGrabber.Instance.TakeHeld();
        scale.SetLoad(Rabbit);
        Rabbit.PlaceAt(scale.weighPoint.position, scale.weighPoint.rotation, RabbitPlace.OnScale, 0.6f, 0.06f);
        if (IsEducational) ShowMessage("Espera a que la lectura se estabilice. Rango válido: 2.5 – 4.0 kg.");
    }

    void RegisterWeight()
    {
        float shown = scale.Displayed;
        bool tared = scale.IsTared;
        if (!Rabbit.InRange)
        {
            AddError("aptoFuera_" + Rabbit.Code, "Registraste como apto a un conejo fuera del rango (" + Kg(Rabbit.WeightKg) + ").");
            if (IsEducational)
            {
                ShowMessage(Kg(shown) + " está FUERA del rango válido (2.5 – 4.0 kg): este conejo no es apto. Repórtalo con R.");
                return;
            }
        }
        LabDatabase.InsertWeighing(Rabbit.Code, shown, InRange(shown), "registrado", Mode, tared);
        LabDatabase.SetRabbitState(Rabbit.Code, "apto");
        weightDone = true;
        registeredKg = shown;
        registeredCode = Rabbit.Code;
        scale.Flash("GUARDADO", 1.3f);
        scale.Beep(1560f);
        ShowMessage(IsEducational
            ? "Peso registrado: " + Kg(shown) + " (dentro del rango). Quedó guardado en la bitácora. Ahora lleva al conejo al paño."
            : "Peso registrado en la bitácora: " + Kg(shown) + ".");
    }

    void ReportRabbit()
    {
        float shown = scale.Displayed;
        bool tared = scale.IsTared;
        if (Rabbit.InRange)
        {
            AddError("reporteApto_" + Rabbit.Code, "Reportaste como no apto a un conejo con peso válido (" + Kg(Rabbit.WeightKg) + ").");
            if (IsEducational)
            {
                ShowMessage(Kg(shown) + " está DENTRO del rango (2.5 – 4.0 kg): el conejo es apto. Registra su peso con E.");
                return;
            }
        }
        LabDatabase.InsertWeighing(Rabbit.Code, shown, InRange(shown), "no apto (reportado)", Mode, tared);
        LabDatabase.SetRabbitState(Rabbit.Code, "no apto");
        rabbitsReported++;
        scale.Flash("REPORTE", 1.3f);
        scale.Beep(700f);
        string code = Rabbit.Code;
        var leaving = Rabbit;
        scale.ClearLoad();
        leaving.Leave();
        ShowMessage("Conejo " + code + " reportado como NO APTO (" + Kg(shown) + "). Quedó registrado en la bitácora; en seguida traen otro conejo a la jaula.");
        StartCoroutine(BringNewRabbit());
    }

    // ---------- Paño, rasurado y desinfección ----------

    void PlaceOnPad()
    {
        if (!weightDone)
        {
            if (IsEducational) { ShowMessage("Primero pesa al conejo en la báscula y registra su peso."); return; }
            AddError("sinPeso", "Preparaste al conejo sin registrar su peso.");
        }
        ObjectGrabber.Instance.TakeHeld();
        Rabbit.PlaceAt(padPoint.position, padPoint.rotation, RabbitPlace.OnPad, 0.6f, 0.06f);
        if (IsEducational) ShowMessage("El conejo está sobre el paño. Toma la rasuradora de la bandeja y apunta al conejo.");
    }

    public string RabbitPrompt(RabbitController r, Grabbable held, int action)
    {
        if (!IsRunning || completed || r != Rabbit) return null;
        if (r.Place == RabbitPlace.OnScale && Undecided && held == null)
            return ScalePrompt(null, action);
        if (action != 0 || r.Place != RabbitPlace.OnPad) return null;
        if (HoldingClipper(held) && !shaved) return "rasurar la zona dorsal (5 × 5 cm)";
        if (HoldingGauze(held) && !disinfected && gauze.IsSoaked) return "desinfectar la zona rasurada";
        return null;
    }

    public string RabbitInfo(RabbitController r, Grabbable held)
    {
        if (!IsRunning || r != Rabbit) return null;
        if (r.Place == RabbitPlace.OnScale) return ScaleInfo();
        if (r.Place == RabbitPlace.OnPad)
        {
            if (HoldingGauze(held) && !gauze.IsSoaked && !disinfected) return "La gasa está seca: empápala primero en la botella de alcohol.";
            if (HoldingClipper(held) && shaved) return "La zona dorsal ya está rasurada.";
        }
        else if (HoldingClipper(held) || HoldingGauze(held)) return "Coloca al conejo sobre el paño para prepararlo.";
        if (IsEducational && r.Place != RabbitPlace.Carried) return "Conejo " + r.Code;
        return null;
    }

    public void RabbitInteract(RabbitController r, Grabbable held, int action)
    {
        if (r.Place == RabbitPlace.OnScale) { ScaleInteract(null, action); return; }
        if (HoldingClipper(held)) StartShave();
        else if (HoldingGauze(held)) StartDisinfect();
    }

    void StartShave()
    {
        if (minigame == null) return;
        minigame.Begin(DorsalMinigame.Kind.Shave, Rabbit, IsEducational, res =>
        {
            shaved = true;
            shaveResult = res;
            ShowMessage(IsEducational
                ? "Rasurado: " + res.grade.ToString("0.0") + "/10. Ahora empapa una gasa con alcohol y desinfecta la zona."
                : "Rasurado terminado.");
        });
    }

    void StartDisinfect()
    {
        if (!shaved)
        {
            AddError("desinfSinRasurar", "Intentaste desinfectar antes de rasurar.");
            ShowMessage("Primero se rasura la zona dorsal; después se desinfecta.");
            return;
        }
        if (minigame == null) return;
        minigame.Begin(DorsalMinigame.Kind.Disinfect, Rabbit, IsEducational, res =>
        {
            disinfected = true;
            disinfectResult = res;
            gauze.Uses++;
            CompleteLevel();
        });
    }

    // ------------------------------------------------------------------
    //  Estrés, guía y marcador
    // ------------------------------------------------------------------

    void UpdateStress()
    {
        if (Rabbit == null) return;
        bool show = IsEducational && (Rabbit.Place == RabbitPlace.Carried || Rabbit.Stress > 30f || Rabbit.IsFleeing) && Rabbit.Place != RabbitPlace.Leaving
                    && (minigame == null || !minigame.IsActive);
        if (stressGroup != null && stressGroup.activeSelf != show) stressGroup.SetActive(show);
        if (show && stressFill != null)
        {
            float k = Rabbit.Stress / 100f;
            stressFill.fillAmount = k;
            stressFill.color = k < 0.35f ? new Color(0.35f, 0.85f, 0.55f) : k < 0.7f ? new Color(1f, 0.78f, 0.3f) : new Color(1f, 0.4f, 0.35f);
            if (stressLabel != null)
                stressLabel.text = Rabbit.IsFleeing ? "¡Espantado! Atrápalo"
                    : Rabbit.NeedsCalming ? (Rabbit.IsBeingPetted ? "Acariciando: se está calmando..." : "Asustado: acarícialo (clic derecho)")
                    : k < 0.35f ? "Conejo tranquilo" : k < 0.7f ? "Conejo nervioso: más despacio" : "¡Muy estresado! Detente";
        }
        if (IsEducational && Rabbit.Place == RabbitPlace.Carried && !Rabbit.NeedsCalming && Rabbit.Stress > 65f && Time.time - lastStressWarn > 5f)
        {
            lastStressWarn = Time.time;
            ShowMessage("El conejo está muy nervioso: detente un momento para que se calme.");
        }
    }

    void HideGuide()
    {
        if (objectivePanel != null && objectivePanel.activeSelf) objectivePanel.SetActive(false);
        if (checklistPanel != null && checklistPanel.activeSelf) checklistPanel.SetActive(false);
        if (marker != null && marker.gameObject.activeSelf) marker.gameObject.SetActive(false);
        if (stressGroup != null && stressGroup.activeSelf) stressGroup.SetActive(false);
    }

    void UpdateGuide(Step step)
    {
        bool show = IsEducational && (minigame == null || !minigame.IsActive);
        if (objectivePanel != null && objectivePanel.activeSelf != show) objectivePanel.SetActive(show);
        if (checklistPanel != null && checklistPanel.activeSelf != show) checklistPanel.SetActive(show);
        if (!show) { if (marker != null) marker.gameObject.SetActive(false); return; }

        var held = ObjectGrabber.Instance != null ? ObjectGrabber.Instance.Held : null;
        string objective;
        Transform target;
        switch (step)
        {
            case Step.Gloves:
                objective = "Ponte los guantes: apunta a la caja de guantes (extremo izquierdo de la mesa) y presiona E.";
                target = gloveBox; break;
            case Step.TakeRabbit:
                if (Rabbit != null && Rabbit.Place == RabbitPlace.Escaped)
                { objective = "El conejo escapó: acércate despacio y tómalo de nuevo (E)."; target = Rabbit.transform; }
                else if (cageDoor != null && !cageDoor.IsOpen)
                { objective = "Abre la jaula del conejo (carrito junto a la mesa): apunta a la puerta y presiona E."; target = cage; }
                else { objective = "Toma al conejo con cuidado (E). Camina despacio y sin girar bruscamente."; target = Rabbit != null ? Rabbit.transform : cage; }
                break;
            case Step.Tare:
                objective = "Tara la báscula: debe marcar 0.00 kg sin nada encima. Apúntala y presiona R (o E con las manos vacías).";
                target = scale != null ? scale.transform : null; break;
            case Step.Weigh:
                if (HoldingRabbit(held)) { objective = "Coloca al conejo en la báscula: apúntala y presiona E."; target = scale.transform; }
                else if (Undecided && !scale.IsStable) { objective = "Espera a que la lectura se estabilice (pitido y ESTABLE)."; target = Rabbit.transform; }
                else if (Undecided)
                {
                    objective = "Lectura: " + Kg(scale.Displayed) + ". Rango válido 2.5 – 4.0 kg.\nE: registrar (apto)   ·   R: reportar (no apto).";
                    target = Rabbit.transform;
                }
                else { objective = "Toma al conejo y colócalo en la báscula."; target = Rabbit.transform; }
                break;
            case Step.ToPad:
                if (HoldingRabbit(held)) { objective = "Coloca al conejo sobre el paño azul: apúntalo y presiona E."; target = pad; }
                else { objective = "Toma al conejo de la báscula para llevarlo al paño."; target = Rabbit.transform; }
                break;
            case Step.Shave:
                if (HoldingClipper(held)) { objective = "Apunta al conejo y presiona E para rasurar la zona dorsal (5 × 5 cm)."; target = Rabbit.transform; }
                else { objective = "Toma la rasuradora eléctrica de la bandeja metálica."; target = clipper != null ? clipper.transform : null; }
                break;
            case Step.Disinfect:
                if (!HoldingGauze(held)) { objective = "Toma la gasa estéril de la bandeja metálica."; target = gauze != null ? gauze.transform : null; }
                else if (!gauze.IsSoaked) { objective = "Empapa la gasa: apunta a la botella de alcohol y presiona E."; target = alcoholBottle; }
                else { objective = "Apunta al conejo y presiona E para desinfectar la zona rasurada."; target = Rabbit.transform; }
                break;
            default:
                objective = "¡Procedimiento terminado!"; target = null; break;
        }
        // El conejo espantado tiene prioridad sobre el paso en curso.
        if (Rabbit != null && Rabbit.IsFleeing)
        {
            objective = "¡El conejo se espantó y salta por el salón! Atrápalo rápido: acércate y presiona E.";
            target = Rabbit.transform;
        }
        else if (Rabbit != null && Rabbit.NeedsCalming && HoldingRabbit(held))
        {
            objective = "Calma al conejo antes de seguir: " + PetHow + " para acariciarlo.";
            target = null;
        }
        if (objectiveText != null) objectiveText.text = "<b>OBJETIVO</b>\n" + objective;
        if (checklistText != null) checklistText.text = BuildChecklist(step);
        UpdateMarker(target);
    }

    string BuildChecklist(Step step)
    {
        var sb = new System.Text.StringBuilder("<b>PROCEDIMIENTO</b>\n");
        bool[] done =
        {
            glovesOn,
            Rabbit != null && (weightDone || Rabbit.Place == RabbitPlace.OnScale || Rabbit.Place == RabbitPlace.Carried || Rabbit.Place == RabbitPlace.OnPad),
            everTared || weightDone,
            weightDone,
            shaved || (weightDone && Rabbit != null && Rabbit.Place == RabbitPlace.OnPad),
            shaved,
            disinfected
        };
        for (int i = 0; i < 7; i++)
        {
            bool current = (int)step == i;
            string mark = done[i] ? "<color=#7CF0B0>●</color>" : current ? "<color=#FFD24A>○</color>" : "<color=#8A96A8>○</color>";
            string name = StepNames[i];
            if (i == 3) name += " (2.5 – 4.0 kg)";
            if (i == 5) name += " (5 × 5 cm)";
            sb.Append(mark).Append(' ').Append(current ? "<b>" + name + "</b>" : name).Append('\n');
        }
        if (weightDone) sb.Append("\n<size=85%>Peso registrado: ").Append(Kg(registeredKg)).Append(" · ").Append(registeredCode).Append("</size>");
        else if (rabbitsReported > 0) sb.Append("\n<size=85%>Conejos reportados como no aptos: ").Append(rabbitsReported).Append("</size>");
        return sb.ToString().TrimEnd();
    }

    void CreateMarker()
    {
        if (markerMaterial == null) return;
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = "MarcadorGuia_N2";
        Destroy(go.GetComponent<Collider>());
        go.GetComponent<Renderer>().sharedMaterial = markerMaterial;
        go.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        go.transform.localScale = new Vector3(0.05f, 0.05f, 0.05f);
        marker = go.transform;
        go.SetActive(false);
    }

    void UpdateMarker(Transform target)
    {
        if (marker == null) return;
        if (target == null) { marker.gameObject.SetActive(false); return; }
        var held = ObjectGrabber.Instance != null ? ObjectGrabber.Instance.Held : null;
        if (held != null && target == held.transform) { marker.gameObject.SetActive(false); return; }
        float top = target.position.y + 0.25f;
        foreach (var r in target.GetComponentsInChildren<Renderer>())
            if (r.transform != marker) top = Mathf.Max(top, r.bounds.max.y + 0.12f);
        marker.gameObject.SetActive(true);
        marker.position = new Vector3(target.position.x, top + Mathf.Sin(Time.time * 3f) * 0.025f, target.position.z);
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
        float elapsed = Time.time - startTime;
        HideGuide();
        if (timerText != null) timerText.gameObject.SetActive(false);
        if (toastRoutine != null) { StopCoroutine(toastRoutine); toastRoutine = null; }
        if (toastGroup != null) toastGroup.alpha = 0f;
        if (counterText != null) counterText.text = "Nivel 2 · Procedimiento terminado";

        float overSeconds = elapsed - parTimeSeconds;
        float timePenalty = overSeconds > 0f ? Mathf.Min(maxTimePenalty, Mathf.Ceil(overSeconds / 60f) * timePenaltyPerMinute) : 0f;
        float procedure = Mathf.Clamp(10f - errors.Count * penaltyPerError - timePenalty, 0f, 10f);
        float final = Mathf.Clamp(procedureWeight * procedure + shaveWeight * shaveResult.grade + disinfectWeight * disinfectResult.grade, 0f, 10f);
        final = Mathf.Round(final * 10f) / 10f;
        bool passed = final >= passingGrade;

        if (completeTimeText != null) completeTimeText.text = "Tiempo: " + FormatTime(elapsed);
        if (completeTitleText != null) completeTitleText.text = IsEducational ? "¡Conejo preparado!" : "Evaluación del nivel 2 terminada";
        if (gradeText != null)
        {
            if (IsEducational)
            {
                gradeText.text = "Modo educativo  ·  Rasurado " + shaveResult.grade.ToString("0.0") + "  ·  Desinfección " + disinfectResult.grade.ToString("0.0");
                gradeText.color = new Color(0.45f, 0.9f, 0.65f);
            }
            else
            {
                gradeText.text = "Calificación: " + final.ToString("0.0") + " / 10   —   " + (passed ? "APROBADO" : "NO APROBADO");
                gradeText.color = passed ? new Color(0.45f, 0.9f, 0.65f) : new Color(1f, 0.45f, 0.4f);
            }
        }

        var sb = new System.Text.StringBuilder();
        sb.Append("Conejo ").Append(registeredCode ?? Rabbit.Code).Append(" · Peso registrado: ")
          .Append(weightDone ? Kg(registeredKg) : "sin registrar").Append(" (guardado en la base de datos)");
        if (rabbitsReported > 0) sb.Append(" · Reportados no aptos: ").Append(rabbitsReported);
        sb.Append('\n');
        sb.Append("• Rasurado: ").Append(shaveResult.grade.ToString("0.0")).Append("/10 — ").Append(shaveResult.verdict)
          .Append(" (zona ").Append(Mathf.RoundToInt(shaveResult.coverage * 100f)).Append("%, fuera ").Append(shaveResult.overshootCm2.ToString("0.0")).Append(" cm²)\n");
        sb.Append("• Desinfección: ").Append(disinfectResult.grade.ToString("0.0")).Append("/10 — ").Append(disinfectResult.verdict).Append('\n');
        if (!IsEducational)
        {
            sb.Append("• Procedimiento: ").Append(procedure.ToString("0.0")).Append("/10");
            if (timePenalty > 0f) sb.Append(" (tardaste más de ").Append(FormatTime(parTimeSeconds)).Append(", −").Append(timePenalty.ToString("0.#")).Append(')');
            sb.Append("   ·   Nota final = 50% procedimiento + 30% rasurado + 20% desinfección\n");
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
        // "Continuar" lleva al nivel 3 (GameFlow.ContinueToNextLevel).
        if (continueButton != null) continueButton.SetActive(true);
        if (completePanel != null) completePanel.SetActive(true);

        LabDatabase.InsertResult(2, Mode, IsEducational ? (float?)null : final, elapsed, errors.Count,
            "rasurado=" + shaveResult.grade.ToString("0.0", CultureInfo.InvariantCulture) +
            "; desinfeccion=" + disinfectResult.grade.ToString("0.0", CultureInfo.InvariantCulture) +
            "; peso=" + registeredKg.ToString("0.00", CultureInfo.InvariantCulture) + "; " + string.Join(" | ", errors));

        if (GameFlow.Instance != null) GameFlow.Instance.ShowResults();
    }

    static bool InRange(float kg) => kg >= MinKg && kg <= MaxKg;
    static string Kg(float kg) => kg.ToString("0.00", CultureInfo.InvariantCulture) + " kg";

    static string FormatTime(float seconds)
    {
        int s = Mathf.Max(0, Mathf.FloorToInt(seconds));
        return (s / 60).ToString("00") + ":" + (s % 60).ToString("00");
    }

    // ---------- Para pruebas automáticas ----------
    public bool GlovesOn => glovesOn;
    public bool WeightDone => weightDone;
    public bool Shaved => shaved;
    public bool Disinfected => disinfected;
    public IReadOnlyList<string> Errors => errors;
}
