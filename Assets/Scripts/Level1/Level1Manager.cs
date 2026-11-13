using System.Collections;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Controla el Nivel 1 (preparación y esterilización de material):
///  - Cuenta los materiales correctos (hijos de Mat_N1) colocados en la mesa.
///  - Sortea al azar, cada vez que arranca/reinicia el nivel, cuáles de esos materiales
///    necesitan pasar por el autoclave antes de contar (se reconoce por su cinta testigo).
///  - Acomoda al azar (MaterialSpawner) todos los materiales y el organizador de insumos en
///    lugares coherentes: la repisa bajo el pizarrón y las repisas del estante junto a la mesa.
///  - La jeringa no está suelta: se arma en el organizador de insumos (SyringeKitMinigame) y,
///    al acertar (5 mL + aguja 21G × 1″), aparece en la mano del jugador. Es estéril de fábrica.
///  - Modo educativo: avisos y explicaciones (la guía paso a paso está en EducationalGuide).
///  - Modo evaluación: sin avisos; registra errores y tiempo y da una calificación sobre 10.
/// </summary>
public class Level1Manager : MonoBehaviour, ILevelController
{
    public static Level1Manager Instance { get; private set; }

    [Header("Materiales de la escena")]
    public Transform matN1Root;
    public Transform matIncorrectosRoot;

    [Header("Aparición aleatoria")]
    public MaterialSpawner spawner;

    [Header("Jeringa (minijuego del organizador de insumos)")]
    [Tooltip("Jeringa de Mat_N1 que se entrega armada al completar el minijuego.")]
    public Transform syringeItem;
    public SupplyOrganizer organizer;

    [Header("UI - HUD")]
    public TMP_Text counterText;
    public TMP_Text toastText;
    public CanvasGroup toastGroup;
    public TMP_Text autoclaveTimerText;
    public TMP_Text modeText;
    public TMP_Text timerText;

    [Header("UI - Fin de nivel")]
    public GameObject completePanel;
    public TMP_Text completeTitleText;
    public TMP_Text completeTimeText;
    public TMP_Text gradeText;
    public TMP_Text resultDetailText;

    [Header("Cinta testigo")]
    public Material tapeSterileMaterial;
    public Material tapeUnsterileMaterial;

    [Header("Configuración")]
    [Range(0f, 1f)] public float sterilizeChancePerItem = 0.5f;
    public float toastDuration = 3.5f;

    [Header("Evaluación")]
    [Tooltip("Tiempo (s) para completar sin penalización.")]
    public float parTimeSeconds = 240f;
    [Tooltip("Puntos que se restan por cada error.")]
    public float penaltyPerError = 1f;
    [Tooltip("Puntos que se restan por cada minuto (o fracción) arriba del tiempo objetivo.")]
    public float timePenaltyPerMinute = 0.5f;
    public float maxTimePenalty = 2f;
    public float passingGrade = 6f;

    readonly List<LabMaterial> correctMaterials = new List<LabMaterial>();
    readonly List<string> errors = new List<string>();
    readonly HashSet<LabMaterial> wrongPlaced = new HashSet<LabMaterial>();
    readonly HashSet<LabMaterial> unsterilePlaced = new HashSet<LabMaterial>();

    int countedTotal;
    int countedSoFar;
    float levelStartTime;
    bool levelRunning;
    bool levelComplete;
    Coroutine toastRoutine;
    LabMaterial syringeLm;

    /// <summary>true cuando ya se armó (correctamente) la jeringa en el organizador.</summary>
    public bool SyringeAssembled { get; private set; }
    /// <summary>true si la jeringa todavía hay que armarla en el organizador de insumos.</summary>
    public bool SyringeNeedsAssembly => syringeLm != null && organizer != null && !SyringeAssembled;
    public LabMaterial SyringeMaterial => syringeLm;

    public GameMode Mode { get; private set; } = GameMode.Educativo;
    public bool IsRunning => levelRunning;
    public bool IsEducational => Mode == GameMode.Educativo;
    public IReadOnlyList<LabMaterial> CorrectMaterials => correctMaterials;
    public int CountedSoFar => countedSoFar;
    public int CountedTotal => countedTotal;
    public int ErrorCount => errors.Count;
    public float ElapsedSeconds => levelRunning ? Time.time - levelStartTime : 0f;

    void Awake()
    {
        Instance = this;
        // Se prepara en Awake para que esté listo antes de que GameFlow (Start) arranque el nivel.
        SetupLevel();
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    void Start()
    {
        SleepAllMaterials();
        // Si la escena no tiene menú/GameFlow, el nivel arranca de inmediato.
        if (GameFlow.Instance == null) BeginLevel(GameSession.Mode);
    }

    void Update()
    {
        if (timerText == null) return;
        bool show = levelRunning && GameSettings.ShowTimer;
        if (timerText.gameObject.activeSelf != show) timerText.gameObject.SetActive(show);
        if (show) timerText.text = FormatTime(Time.time - levelStartTime);
    }

    public void SetupLevel()
    {
        levelComplete = false;
        levelRunning = false;
        countedSoFar = 0;
        correctMaterials.Clear();
        errors.Clear();
        wrongPlaced.Clear();
        unsterilePlaced.Clear();

        if (matN1Root != null)
        {
            foreach (Transform child in matN1Root)
            {
                var lm = child.GetComponent<LabMaterial>();
                if (lm == null) lm = child.gameObject.AddComponent<LabMaterial>();
                lm.isCorrectMaterial = true;
                lm.countedOnTable = false;
                correctMaterials.Add(lm);
            }
        }

        var incorrect = new List<LabMaterial>();
        if (matIncorrectosRoot != null)
        {
            foreach (var g in matIncorrectosRoot.GetComponentsInChildren<Grabbable>(true))
            {
                var lm = g.GetComponent<LabMaterial>();
                if (lm == null) lm = g.gameObject.AddComponent<LabMaterial>();
                lm.isCorrectMaterial = false;
                lm.countedOnTable = false;
                // Aspecto al azar: así la cinta testigo no delata cuáles son los materiales correctos.
                bool looksSterile = Random.value >= 0.5f;
                lm.requiresSterilization = !looksSterile;
                lm.isSterilized = looksSterile;
                incorrect.Add(lm);
            }
        }

        countedTotal = correctMaterials.Count;

        // La jeringa se arma en el organizador de insumos: mientras tanto no está en la escena.
        SyringeAssembled = false;
        syringeLm = null;
        if (syringeItem != null && organizer != null)
        {
            syringeLm = syringeItem.GetComponent<LabMaterial>();
            if (syringeLm != null && correctMaterials.Contains(syringeLm))
            {
                syringeLm.displayLabel = "Jeringa de 5 mL con aguja 21G × 1″";
                var sg = syringeLm.GetComponent<Grabbable>();
                if (sg != null) sg.displayName = "Jeringa de 5 mL con aguja 21G";
                syringeLm.gameObject.SetActive(false);
            }
            else syringeLm = null;
        }

        // Sortea al azar (cada partida/reintento) cuáles necesitan autoclave (al menos uno).
        // La jeringa armada es estéril de fábrica (desechable, de un solo uso): nunca va al autoclave.
        var shuffled = correctMaterials.OrderBy(_ => Random.value).ToList();
        int needCount = 0;
        foreach (var lm in shuffled)
        {
            if (lm == syringeLm || !lm.autoclavable) { lm.requiresSterilization = false; lm.isSterilized = true; continue; }
            bool needs = Random.value < sterilizeChancePerItem;
            lm.requiresSterilization = needs;
            lm.isSterilized = !needs;
            if (needs) needCount++;
        }
        var candidates = shuffled.Where(lm => lm != syringeLm && lm.autoclavable).ToList();
        if (needCount == 0 && candidates.Count > 0)
        {
            var pick = candidates[Random.Range(0, candidates.Count)];
            pick.requiresSterilization = true;
            pick.isSterilized = false;
        }

        // Cada partida los materiales (y el organizador) aparecen en lugares distintos, pero coherentes.
        if (spawner != null)
        {
            var toPlace = new List<Transform>();
            foreach (var lm in correctMaterials) if (lm.gameObject.activeInHierarchy) toPlace.Add(lm.transform);
            foreach (var lm in incorrect) toPlace.Add(lm.transform);
            if (organizer != null) toPlace.Add(organizer.transform);
            spawner.Randomize(toPlace);
        }

        foreach (var lm in correctMaterials) if (lm.gameObject.activeInHierarchy) lm.RefreshIndicator();
        foreach (var lm in incorrect) lm.RefreshIndicator();
        // Lugar de cada material al empezar: ahí se repone uno nuevo si se contamina uno que no va al autoclave.
        foreach (var lm in correctMaterials) lm.RememberHome();
        foreach (var lm in incorrect) lm.RememberHome();

        SleepAllMaterials();
        UpdateCounterUI();
        if (completePanel != null) completePanel.SetActive(false);
        HideAutoclaveTimer();
        if (toastGroup != null) toastGroup.alpha = 0f;
        if (timerText != null) timerText.gameObject.SetActive(false);
    }

    /// <summary>
    /// Deja los materiales "dormidos" tal como están acomodados en la escena, para que los
    /// objetos altos y delgados no se caigan solos del estante; despiertan en cuanto se tocan.
    /// </summary>
    void SleepAllMaterials()
    {
        foreach (var root in new[] { matN1Root, matIncorrectosRoot })
        {
            if (root == null) continue;
            foreach (var body in root.GetComponentsInChildren<Rigidbody>())
                if (!body.isKinematic) body.Sleep();
        }
    }

    /// <summary>Arranca el cronómetro y el nivel en el modo indicado (lo llama GameFlow).</summary>
    public void BeginLevel(GameMode mode)
    {
        Mode = mode;
        ObjectGrabber.MessageSink = ShowMessage;
        levelStartTime = Time.time;
        levelRunning = true;
        levelComplete = false;
        errors.Clear();
        wrongPlaced.Clear();
        unsterilePlaced.Clear();

        if (modeText != null)
        {
            modeText.text = IsEducational ? "MODO EDUCATIVO" : "MODO EVALUACIÓN";
            modeText.color = IsEducational ? new Color(0.45f, 0.9f, 0.65f) : new Color(1f, 0.72f, 0.35f);
        }
        UpdateCounterUI();

        ShowMessage(IsEducational
            ? "Modo educativo: sigue el objetivo (arriba a la izquierda) y la lista de materiales."
            : "Modo evaluación: prepara el material correcto. Se registran tus errores y tu tiempo.");
    }

    // ---------- Eventos del juego ----------

    public void TryPlaceOnTable(LabMaterial item)
    {
        if (!levelRunning || levelComplete || item.countedOnTable) return;

        if (!item.isCorrectMaterial)
        {
            if (wrongPlaced.Add(item))
                RegisterError("Colocaste en la mesa un material que no corresponde: " + item.DisplayName + ".");
            if (IsEducational)
                ShowMessage(item.DisplayName + " no se usa en esta práctica: retira el material de la mesa.");
            return;
        }

        if (!item.isSterilized)
        {
            if (unsterilePlaced.Add(item))
                RegisterError("Colocaste " + item.DisplayName + " en la mesa sin esterilizar.");
            if (IsEducational)
                ShowMessage(item.DisplayName + " aún no está estéril (su cinta testigo tiene rayas claras). Lleva el material al autoclave.");
            return;
        }

        item.countedOnTable = true;
        countedSoFar++;
        UpdateCounterUI();
        ShowMessage(IsEducational
            ? "¡Bien! " + item.DisplayName + " quedó en la mesa (" + countedSoFar + "/" + countedTotal + ")."
            : "Material registrado.");

        if (countedSoFar >= countedTotal)
            CompleteLevel();
    }

    /// <summary>Llamado por LabMaterial cuando el jugador agarra un material.</summary>
    public void NotifyPickedUp(LabMaterial item)
    {
        if (!levelRunning || !IsEducational) return;

        if (!item.isCorrectMaterial)
            ShowMessage(item.DisplayName + " no está en tu lista de materiales. Suelta el material y busca los de la lista.");
        else if (item.countedOnTable)
            ShowMessage(item.DisplayName + " ya estaba en la mesa. Vuelve a dejar el material sobre la mesa de química.");
        else if (item.requiresSterilization && !item.isSterilized)
            ShowMessage(item.DisplayName + " no está estéril: su cinta testigo tiene rayas claras. Lleva el material al autoclave.");
        else
            ShowMessage(item.DisplayName + " está estéril (rayas oscuras en su cinta). Lleva el material a la mesa de química.");
    }

    /// <summary>
    /// Un material estéril cayó (o se dejó) en un lugar que no es la mesa de química: pierde su esterilidad.
    ///  - Si va al autoclave: su cinta testigo vuelve a rayas claras y hay que esterilizarlo de nuevo.
    ///  - La jeringa armada (desechable): se desecha y se arma otra en el organizador de insumos.
    ///  - Lo que no va al autoclave (solución salina): se desecha y aparece uno nuevo en su lugar original.
    /// Si ya estaba contado en la mesa, deja de contar. En ambos modos se registra como error.
    /// </summary>
    public void NotifyContaminated(LabMaterial item)
    {
        if (!levelRunning || levelComplete || item == null) return;
        if (!item.isSterilized && !item.countedOnTable) return; // ya necesitaba autoclave: no cambia nada

        if (item.countedOnTable)
        {
            item.countedOnTable = false;
            countedSoFar = Mathf.Max(0, countedSoFar - 1);
            UpdateCounterUI();
        }

        string name = item.DisplayName;
        bool isSyringe = item == syringeLm;
        string where = null;
        if (!isSyringe && !item.autoclavable && spawner != null && spawner.LastPlacement.TryGetValue(item.transform, out var place) && !place.StartsWith("("))
            where = place;

        if (item.isCorrectMaterial)
            RegisterError(name + " se contaminó al caer fuera de la mesa de química"
                + (isSyringe ? " (hubo que armar otra jeringa)." : item.autoclavable ? " (hubo que volver a esterilizarlo)." : " (hubo que reponerlo)."));

        string msg;
        if (isSyringe)
        {
            SyringeAssembled = false;
            item.gameObject.SetActive(false);
            msg = IsEducational
                ? "La jeringa cayó fuera de la mesa y ya no es estéril: se desecha. Arma una nueva en el organizador de insumos."
                : "La jeringa cayó fuera de la mesa: se desecha.";
        }
        else if (!item.autoclavable)
        {
            item.ReplaceWithNew();
            msg = IsEducational
                ? name + " cayó fuera de la mesa y ya no es estéril. No se puede esterilizar en el autoclave, así que se desecha: toma uno nuevo"
                  + (where != null ? " (está en: " + where + ")" : " en su lugar original") + "."
                : name + " cayó fuera de la mesa: se desecha y se repone en su lugar.";
        }
        else
        {
            item.requiresSterilization = true;
            item.SetSterilized(false);
            msg = IsEducational
                ? (item.isCorrectMaterial
                    ? name + " cayó fuera de la mesa y perdió su esterilidad (su cinta testigo volvió a rayas claras). Llévalo de nuevo al autoclave."
                    : name + " cayó fuera de la mesa y perdió su esterilidad (de todos modos no se usa en esta práctica).")
                : name + " cayó fuera de la mesa: ya no está estéril.";
        }
        ShowMessage(msg);
    }

    /// <summary>Compatibilidad con la versión anterior.</summary>
    public void NotifyPickedUpUnsterilized(LabMaterial item) => NotifyPickedUp(item);

    void RegisterError(string description)
    {
        errors.Add(description);
    }

    /// <summary>Errores de la prueba del código de vestimenta (se hace justo antes de empezar el nivel).</summary>
    public void RegisterDressCodeErrors(IEnumerable<string> list)
    {
        if (list == null || !levelRunning) return;
        foreach (var e in list) if (!string.IsNullOrEmpty(e)) errors.Add(e);
    }

    /// <summary>Error registrado por un minijuego (p. ej. armar una jeringa incorrecta).</summary>
    public void RegisterMinigameError(string description)
    {
        if (levelRunning && !levelComplete) errors.Add(description);
    }

    /// <summary>
    /// El minijuego del organizador se completó bien: la jeringa armada (estéril) aparece en la
    /// mano del jugador para llevarla a la mesa de química.
    /// </summary>
    public void OnSyringeAssembled()
    {
        if (syringeLm == null || SyringeAssembled) return;
        SyringeAssembled = true;
        var go = syringeLm.gameObject;
        var grabber = ObjectGrabber.Instance;
        Transform cam = grabber != null ? grabber.Cam.transform : (Camera.main != null ? Camera.main.transform : null);
        if (cam != null)
        {
            Vector3 p = cam.position + cam.forward * 0.6f;
            foreach (var c in Physics.OverlapSphere(p, 0.07f, ~0, QueryTriggerInteraction.Ignore))
                if (!(c is CharacterController)) { p = cam.position + cam.forward * 0.3f; break; }
            go.transform.position = p;
        }
        go.SetActive(true);
        Physics.SyncTransforms();
        syringeLm.RefreshIndicator();
        if (grabber != null) grabber.Grab(syringeLm.GetComponent<Grabbable>());
        ShowMessage(IsEducational
            ? "Armaste la jeringa de 5 mL con aguja 21G × 1″ (estéril, de un solo uso). Llévala a la mesa de química."
            : "Jeringa armada.");
    }

    // ---------- UI ----------

    void UpdateCounterUI()
    {
        if (counterText != null) counterText.text = "Materiales: " + countedSoFar + "/" + countedTotal;
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

    public void ShowAutoclaveTimer(int seconds)
    {
        if (autoclaveTimerText == null) return;
        autoclaveTimerText.gameObject.SetActive(true);
        autoclaveTimerText.text = "Esterilizando... " + seconds + " s";
    }

    public void HideAutoclaveTimer()
    {
        if (autoclaveTimerText != null) autoclaveTimerText.gameObject.SetActive(false);
    }

    // ---------- Fin del nivel ----------

    void CompleteLevel()
    {
        levelComplete = true;
        levelRunning = false;
        float elapsed = Time.time - levelStartTime;
        if (timerText != null) timerText.gameObject.SetActive(false);

        if (completeTimeText != null) completeTimeText.text = "Tiempo: " + FormatTime(elapsed);

        var sb = new System.Text.StringBuilder();
        float? savedGrade = null;
        if (IsEducational)
        {
            if (completeTitleText != null) completeTitleText.text = "¡Práctica completada!";
            if (gradeText != null)
            {
                gradeText.text = "Modo educativo";
                gradeText.color = new Color(0.45f, 0.9f, 0.65f);
            }
            if (errors.Count == 0) sb.AppendLine("No cometiste errores. ¡Excelente!");
            else
            {
                sb.AppendLine("Errores cometidos (" + errors.Count + "):");
                foreach (var e in errors) sb.AppendLine("• " + e);
            }
            sb.AppendLine();
            sb.Append("Cuando te sientas listo, prueba el modo evaluación desde el menú principal.");
        }
        else
        {
            float overSeconds = elapsed - parTimeSeconds;
            float timePenalty = overSeconds > 0f
                ? Mathf.Min(maxTimePenalty, Mathf.Ceil(overSeconds / 60f) * timePenaltyPerMinute)
                : 0f;
            float errorPenalty = errors.Count * penaltyPerError;
            float grade = Mathf.Clamp(10f - errorPenalty - timePenalty, 0f, 10f);
            bool passed = grade >= passingGrade;
            savedGrade = grade;

            if (completeTitleText != null) completeTitleText.text = "Evaluación terminada";
            if (gradeText != null)
            {
                gradeText.text = "Calificación: " + grade.ToString("0.0") + " / 10   —   " + (passed ? "APROBADO" : "NO APROBADO");
                gradeText.color = passed ? new Color(0.45f, 0.9f, 0.65f) : new Color(1f, 0.45f, 0.4f);
            }

            if (errors.Count == 0) sb.AppendLine("Sin errores. ¡Excelente trabajo!");
            else
            {
                sb.AppendLine("Errores (" + errors.Count + ", −" + errorPenalty.ToString("0.#") + "):");
                foreach (var e in errors) sb.AppendLine("• " + e);
            }
            if (timePenalty > 0f)
                sb.AppendLine("• Tardaste más de " + FormatTime(parTimeSeconds) + " (−" + timePenalty.ToString("0.#") + ").");
            else
                sb.AppendLine("• Terminaste dentro del tiempo objetivo (" + FormatTime(parTimeSeconds) + ").");
        }

        if (resultDetailText != null) resultDetailText.text = sb.ToString().TrimEnd();
        LabDatabase.InsertResult(1, Mode, savedGrade, elapsed, errors.Count, string.Join(" | ", errors));
        if (completePanel != null)
        {
            var cont = completePanel.transform.Find("ContinueButton");
            if (cont != null) cont.gameObject.SetActive(true);
        }
        if (completePanel != null) completePanel.SetActive(true);
        if (GameFlow.Instance != null) GameFlow.Instance.ShowResults();
    }

    static string FormatTime(float seconds)
    {
        int s = Mathf.Max(0, Mathf.FloorToInt(seconds));
        return (s / 60).ToString("00") + ":" + (s % 60).ToString("00");
    }

    // ---------- Botones de la pantalla de fin de nivel ----------

    public void RetryLevel()
    {
        if (GameFlow.Instance != null) { GameFlow.Instance.RestartLevel(); return; }
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }

    public void ContinueLevel()
    {
        if (GameFlow.Instance != null) GameFlow.Instance.ContinueToNextLevel();
    }

    public void BackToMainMenu()
    {
        if (GameFlow.Instance != null) GameFlow.Instance.GoToMainMenu();
        else SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }
}
