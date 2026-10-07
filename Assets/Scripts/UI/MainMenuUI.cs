using UnityEngine;

/// <summary>
/// Menú principal: Jugar (datos del alumno -> elegir modo), Opciones, Créditos y Salir.
/// Los botones se conectan aquí por código a partir de las referencias del Inspector.
/// </summary>
public class MainMenuUI : MonoBehaviour
{
    [Header("Paneles")]
    public GameObject mainPanel;
    public GameObject modePanel;
    public GameObject creditsPanel;
    public OptionsPanel options;
    [Tooltip("Formulario con los datos del alumno que se pide al presionar Jugar, antes de elegir el modo.")]
    public StudentFormUI studentForm;

    [Header("Botones del menú principal")]
    public UnityEngine.UI.Button playButton;
    public UnityEngine.UI.Button optionsButton;
    public UnityEngine.UI.Button creditsButton;
    public UnityEngine.UI.Button quitButton;

    [Header("Selección de modo")]
    public UnityEngine.UI.Button educationalButton;
    public UnityEngine.UI.Button evaluationButton;
    public UnityEngine.UI.Button modeBackButton;

    [Header("Selección de nivel")]
    public UnityEngine.UI.Button level1Button;
    public UnityEngine.UI.Button level2Button;
    public UnityEngine.UI.Button level3Button;
    public TMPro.TMP_Text levelDescription;
    public Color levelSelectedColor = new Color(0.16f, 0.47f, 0.86f, 1f);
    public Color levelIdleColor = new Color(0.18f, 0.24f, 0.34f, 1f);

    [Header("Créditos")]
    public UnityEngine.UI.Button creditsBackButton;

    void Awake()
    {
        if (playButton) playButton.onClick.AddListener(ShowStudentForm);
        if (studentForm)
        {
            studentForm.Continued += () => Show(modePanel);
            studentForm.Cancelled += ShowMain;
        }
        if (optionsButton) optionsButton.onClick.AddListener(OpenOptions);
        if (creditsButton) creditsButton.onClick.AddListener(() => Show(creditsPanel));
        if (quitButton) quitButton.onClick.AddListener(() => { if (GameFlow.Instance) GameFlow.Instance.QuitGame(); });
        if (educationalButton) educationalButton.onClick.AddListener(() => StartGame(GameMode.Educativo));
        if (evaluationButton) evaluationButton.onClick.AddListener(() => StartGame(GameMode.Evaluacion));
        if (modeBackButton) modeBackButton.onClick.AddListener(ShowStudentForm);
        if (creditsBackButton) creditsBackButton.onClick.AddListener(ShowMain);
        if (level1Button) level1Button.onClick.AddListener(() => SelectLevel(1));
        if (level2Button) level2Button.onClick.AddListener(() => SelectLevel(2));
        if (level3Button) level3Button.onClick.AddListener(() => SelectLevel(3));
        SelectLevel(GameSession.Level);
    }

    static readonly string[] LevelInfo =
    {
        "<b>Nivel 1 · Preparación de material estéril</b>\nReúne en la mesa el material correcto y esteriliza en el autoclave el que lo necesite.",
        "<b>Nivel 2 · Preparación del animal</b>\nToma al conejo con cuidado, pésalo en la báscula (2.5–4 kg), rasura la zona dorsal (5 × 5 cm) y desinfecta con alcohol.",
        "<b>Nivel 3 · Suspensión de glóbulos rojos</b>\nLava los eritrocitos con solución salina y la centrífuga (2500 rpm · 5 min) hasta que el sobrenadante quede transparente."
    };

    public void SelectLevel(int level)
    {
        level = Mathf.Clamp(level, 1, GameSession.MaxLevel);
        GameSession.Level = level;
        Paint(level1Button, level == 1);
        Paint(level2Button, level == 2);
        Paint(level3Button, level == 3);
        if (levelDescription) levelDescription.text = LevelInfo[level - 1];
    }

    void Paint(UnityEngine.UI.Button b, bool selected)
    {
        if (!b) return;
        var cb = b.colors;
        var c = selected ? levelSelectedColor : levelIdleColor;
        cb.normalColor = c;
        cb.selectedColor = c;
        cb.highlightedColor = Color.Lerp(c, Color.white, 0.2f);
        b.colors = cb;
    }

    public void ShowMain() => Show(mainPanel);

    /// <summary>"Jugar": primero se piden (o confirman) los datos del alumno; después se elige el modo.</summary>
    public void ShowStudentForm()
    {
        if (studentForm == null) { Show(modePanel); return; }
        Show(studentForm.gameObject);
    }

    public void OpenOptions()
    {
        Show(null);
        if (options) options.Open(ShowMain);
    }

    /// <summary>Esc/Atrás dentro del menú: cierra el submenú abierto. Devuelve true si hizo algo.</summary>
    public bool HandleBack()
    {
        if (options && options.IsOpen) { options.Close(); return true; }
        if (modePanel && modePanel.activeSelf && studentForm) { ShowStudentForm(); return true; }
        if (mainPanel && !mainPanel.activeSelf) { ShowMain(); return true; }
        return false;
    }

    void StartGame(GameMode mode)
    {
        // Por si se llegó aquí sin pasar por el formulario (no debería): se exigen los datos.
        if (studentForm && !StudentProfile.Confirmed) { ShowStudentForm(); return; }
        if (GameFlow.Instance) GameFlow.Instance.StartGame(mode);
    }

    void Show(GameObject panel)
    {
        if (mainPanel) mainPanel.SetActive(panel == mainPanel);
        if (modePanel) modePanel.SetActive(panel == modePanel);
        if (creditsPanel) creditsPanel.SetActive(panel == creditsPanel);
        if (studentForm)
        {
            bool open = studentForm.gameObject == panel;
            if (open) studentForm.Open();
            else if (studentForm.gameObject.activeSelf) studentForm.Close();
        }
    }
}
