using System.Collections.Generic;
using UnityEngine;
using TMPro;

/// <summary>
/// Gestor del Nivel 1 · Revisión del instrumental.
/// Guarda el progreso del checklist (independiente de cómo se dibuje en pantalla)
/// y expone ShowFeedback/RegisterChecklistItem para que Instrument.cs lo llame.
///
/// Los campos de UI son opcionales: si los dejas vacíos en el Inspector, el nivel
/// sigue funcionando y verás el progreso en la consola (Debug.Log).
/// </summary>
public class Level1Manager : MonoBehaviour
{
    public static Level1Manager Instance { get; private set; }

    [Header("Checklist esperado (debe matchear los instrumentId de la escena)")]
    public List<string> checklistIds = new List<string>
    {
        "jeringas_5cc",
        "gradilla_tubos",
        "solucion_salina",
        "pipeta_pasteur",
        "matraz_erlenmeyer",
        "balanza_calibrada"
    };

    [Header("UI")]
    public TMP_Text progressLabel;
    public GameObject feedbackPanel;
    public TMP_Text feedbackTitle;
    public TMP_Text feedbackBody;
    public float feedbackDuration = 3f; // segundos que se queda visible el panel de feedback

    [Header("Al completar el nivel")]
    public GameObject botonContinuar; // el GameObject completo del botón, se activa solo al terminar

    private readonly HashSet<string> completedIds = new HashSet<string>();

    public bool LevelComplete => completedIds.Count >= checklistIds.Count;

    private void Awake()
    {
        Instance = this;
    }

    private void Start()
    {
        if (feedbackPanel != null) feedbackPanel.SetActive(false);
        if (botonContinuar != null) botonContinuar.SetActive(false);
        UpdateProgressUI();
    }

    public void RegisterChecklistItem(string id)
    {
        if (completedIds.Contains(id)) return;

        completedIds.Add(id);
        UpdateProgressUI();

        if (LevelComplete)
        {
            OnLevelComplete();
        }
    }

    public void ShowFeedback(string title, string body)
    {
        Debug.Log($"[Nivel 1] {title}: {body}");

        if (feedbackPanel == null) return;

        feedbackPanel.SetActive(true);
        if (feedbackTitle != null) feedbackTitle.text = title;
        if (feedbackBody != null) feedbackBody.text = body;

        CancelInvoke(nameof(HideFeedback));
        Invoke(nameof(HideFeedback), feedbackDuration);
    }

    private void HideFeedback()
    {
        if (feedbackPanel != null) feedbackPanel.SetActive(false);
    }

    private void UpdateProgressUI()
    {
        Debug.Log($"[Nivel 1] Checklist de instrumental: {completedIds.Count}/{checklistIds.Count}");

        if (progressLabel != null)
            progressLabel.text = $"{completedIds.Count}/{checklistIds.Count}";
    }

    private void OnLevelComplete()
    {
        Debug.Log("[Nivel 1] Checklist completo. Se habilita el botón Continuar.");

        if (botonContinuar != null)
            botonContinuar.SetActive(true);

        // Aquí después conectamos con el gestor general de niveles / máquina de estados
        // para pasar al Nivel 2.
    }
}