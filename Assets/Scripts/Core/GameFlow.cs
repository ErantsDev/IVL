using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// Controla el flujo general del juego dentro de la escena del laboratorio:
///   Menú principal  ->  (elige modo)  ->  [Nivel 1: código de vestimenta]  ->  Jugando  <->  Pausa (Esc)  ->  Resultados
/// En el menú, el jugador y el HUD están apagados y una cámara recorre el laboratorio de fondo.
/// "Reintentar" recarga la escena y entra directo al nivel; "Menú principal" recarga y muestra el menú.
/// </summary>
public class GameFlow : MonoBehaviour
{
    public enum State { Menu, Transition, Playing, Paused, Results, Minigame, Briefing }

    public static GameFlow Instance { get; private set; }

    /// <summary>true cuando el jugador puede moverse/mirar/agarrar (o si no hay GameFlow en la escena).</summary>
    public static bool GameplayInputAllowed => Instance == null || Instance.state == State.Playing;

    [Header("Escena")]
    public GameObject player;
    public Camera menuCamera;
    [Tooltip("Volumen de post-proceso del menú (desenfoque del fondo). Se apaga al jugar.")]
    public GameObject menuVolume;
    [Tooltip("Controlador del nivel si la escena no tiene LevelDirector.")]
    public MonoBehaviour level;
    public LevelDirector director;

    [Header("UI")]
    public Canvas hudCanvas;
    public GameObject menuScreen;
    public MainMenuUI mainMenu;
    public PauseMenu pauseMenu;
    [Tooltip("Imagen negra a pantalla completa para los fundidos.")]
    public CanvasGroup fade;
    public float fadeSeconds = 0.35f;

    State state = State.Menu;
    State stateBeforePause = State.Playing;

    ILevelController CurrentLevel => director != null && director.Current != null ? director.Current : level as ILevelController;

    /// <summary>true mientras un minijuego (p. ej. el rasurado) controla el mouse.</summary>
    public bool InMinigame => state == State.Minigame || (state == State.Paused && stateBeforePause == State.Minigame);
    public State CurrentState => state;

    void Awake()
    {
        Instance = this;
        Time.timeScale = 1f;
        GameSettings.ApplyAll();
        if (fade != null) { fade.alpha = 1f; fade.blocksRaycasts = true; }
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
        Time.timeScale = 1f;
    }

    void Start()
    {
        if (GameSession.StartLevelDirectly)
        {
            GameSession.StartLevelDirectly = false;
            EnterPlaying();
        }
        else
        {
            EnterMenu();
        }
        StartCoroutine(Fade(0f));
    }

    void Update()
    {
        if (!EscapePressed()) return;
        switch (state)
        {
            case State.Playing:
            case State.Minigame:
                Pause();
                break;
            case State.Paused:
                if (pauseMenu == null || !pauseMenu.HandleBack()) Resume();
                break;
            case State.Menu:
                if (mainMenu != null) mainMenu.HandleBack();
                break;
        }
    }

    // ---------- Estados ----------

    void EnterMenu()
    {
        state = State.Menu;
        Time.timeScale = 1f;
        if (player != null) player.SetActive(false);
        if (menuCamera != null) menuCamera.gameObject.SetActive(true);
        if (menuVolume != null) menuVolume.SetActive(true);
        if (hudCanvas != null) hudCanvas.enabled = false;
        if (pauseMenu != null) pauseMenu.Hide();
        if (menuScreen != null) menuScreen.SetActive(true);
        if (mainMenu != null) mainMenu.ShowMain();
        SetCursor(false);
    }

    void EnterPlaying()
    {
        if (menuScreen != null) menuScreen.SetActive(false);
        if (menuCamera != null) menuCamera.gameObject.SetActive(false);
        if (menuVolume != null) menuVolume.SetActive(false);
        if (player != null) player.SetActive(true);
        Time.timeScale = 1f;

        // Antes del Nivel 1 se muestra el código de vestimenta (con su mini prueba); el nivel y su
        // cronómetro arrancan hasta que el alumno termina.
        if (CurrentLevel is Level1Manager)
        {
            state = State.Briefing;
            if (hudCanvas != null) hudCanvas.enabled = false;
            SetCursor(false);
            DressCodePanel.Open(GameSession.Mode, errors =>
            {
                if (state != State.Briefing) return;
                BeginCurrentLevel();
                if (CurrentLevel is Level1Manager l1) l1.RegisterDressCodeErrors(errors);
            });
            return;
        }
        BeginCurrentLevel();
    }

    void BeginCurrentLevel()
    {
        if (hudCanvas != null) hudCanvas.enabled = true;
        state = State.Playing;
        SetCursor(true);
        CurrentLevel?.BeginLevel(GameSession.Mode);
    }

    /// <summary>Llamado desde la selección de modo del menú principal.</summary>
    public void StartGame(GameMode mode)
    {
        if (state != State.Menu) return;
        GameSession.Mode = mode;
        // Si se eligió un nivel distinto al que está montado en la escena, se recarga directo en ese nivel.
        if (director != null && director.AppliedLevel != GameSession.Level)
        {
            GameSession.StartLevelDirectly = true;
            StartCoroutine(ReloadAfterFade());
            return;
        }
        StartCoroutine(PlayTransition());
    }

    IEnumerator PlayTransition()
    {
        state = State.Transition;
        yield return Fade(1f);
        EnterPlaying();
        yield return Fade(0f);
    }

    public void Pause()
    {
        if (state != State.Playing && state != State.Minigame) return;
        stateBeforePause = state;
        state = State.Paused;
        Time.timeScale = 0f;
        if (pauseMenu != null) pauseMenu.Show();
        SetCursor(false);
    }

    public void Resume()
    {
        if (state != State.Paused) return;
        if (pauseMenu != null) pauseMenu.Hide();
        Time.timeScale = 1f;
        state = stateBeforePause;
        SetCursor(state == State.Playing);
    }

    /// <summary>Un minijuego toma el control: el jugador no se mueve y el cursor queda libre.</summary>
    public void EnterMinigame()
    {
        if (state != State.Playing) return;
        state = State.Minigame;
        SetCursor(false);
    }

    public void ExitMinigame()
    {
        if (state != State.Minigame) return;
        state = State.Playing;
        SetCursor(true);
    }

    /// <summary>Llamado por el controlador del nivel al completarlo: libera el cursor para usar los botones.</summary>
    public void ShowResults()
    {
        state = State.Results;
        SetCursor(false);
    }

    public void RestartLevel()
    {
        if (state == State.Transition) return;
        GameSession.StartLevelDirectly = true;
        StartCoroutine(ReloadAfterFade());
    }

    /// <summary>Botón "Continuar" de la pantalla de resultados: pasa al siguiente nivel.</summary>
    public void ContinueToNextLevel()
    {
        if (state == State.Transition) return;
        if (GameSession.Level >= GameSession.MaxLevel) { GoToMainMenu(); return; }
        GameSession.Level++;
        GameSession.StartLevelDirectly = true;
        StartCoroutine(ReloadAfterFade());
    }

    public void GoToMainMenu()
    {
        if (state == State.Transition) return;
        GameSession.StartLevelDirectly = false;
        StartCoroutine(ReloadAfterFade());
    }

    public void QuitGame()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    IEnumerator ReloadAfterFade()
    {
        state = State.Transition;
        Time.timeScale = 1f;
        yield return Fade(1f);
        var scene = SceneManager.GetActiveScene();
        if (scene.buildIndex >= 0) SceneManager.LoadScene(scene.buildIndex);
        else SceneManager.LoadScene(scene.name);
    }

    IEnumerator Fade(float target)
    {
        if (fade == null) yield break;
        fade.blocksRaycasts = true;
        float start = fade.alpha;
        float t = 0f;
        while (t < fadeSeconds)
        {
            t += Time.unscaledDeltaTime;
            fade.alpha = Mathf.Lerp(start, target, t / fadeSeconds);
            yield return null;
        }
        fade.alpha = target;
        fade.blocksRaycasts = target > 0.01f;
    }

    static void SetCursor(bool locked)
    {
        Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible = !locked;
    }

    static bool EscapePressed()
    {
#if ENABLE_INPUT_SYSTEM
        return Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame;
#else
        return Input.GetKeyDown(KeyCode.Escape);
#endif
    }
}
