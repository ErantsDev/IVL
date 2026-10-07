using UnityEngine;

/// <summary>
/// Menú de pausa (Esc durante el juego): Reanudar, Opciones, Reiniciar nivel y Menú principal.
/// </summary>
public class PauseMenu : MonoBehaviour
{
    public GameObject panel;
    public OptionsPanel options;
    public UnityEngine.UI.Button resumeButton;
    public UnityEngine.UI.Button optionsButton;
    public UnityEngine.UI.Button restartButton;
    public UnityEngine.UI.Button mainMenuButton;

    void Awake()
    {
        if (resumeButton) resumeButton.onClick.AddListener(() => { if (GameFlow.Instance) GameFlow.Instance.Resume(); });
        if (optionsButton) optionsButton.onClick.AddListener(OpenOptions);
        if (restartButton) restartButton.onClick.AddListener(() => { if (GameFlow.Instance) GameFlow.Instance.RestartLevel(); });
        if (mainMenuButton) mainMenuButton.onClick.AddListener(() => { if (GameFlow.Instance) GameFlow.Instance.GoToMainMenu(); });
    }

    public void Show()
    {
        if (panel) panel.SetActive(true);
    }

    public void Hide()
    {
        if (options && options.IsOpen)
        {
            // Cierra opciones sin volver a mostrar el panel de pausa.
            options.gameObject.SetActive(false);
            PlayerPrefs.Save();
        }
        if (panel) panel.SetActive(false);
    }

    /// <summary>Esc estando en pausa: si Opciones está abierto lo cierra (regresa a la pausa). true si hizo algo.</summary>
    public bool HandleBack()
    {
        if (options && options.IsOpen) { options.Close(); return true; }
        return false;
    }

    void OpenOptions()
    {
        if (panel) panel.SetActive(false);
        if (options) options.Open(() =>
        {
            if (GameFlow.Instance && GameFlow.Instance.CurrentState == GameFlow.State.Paused && panel) panel.SetActive(true);
        });
    }
}
