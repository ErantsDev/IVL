using System;
using TMPro;
using UnityEngine;

/// <summary>
/// Panel de Opciones (compartido entre el menú principal y el menú de pausa).
/// Lee y guarda los valores en GameSettings.
/// </summary>
public class OptionsPanel : MonoBehaviour
{
    [Header("Controles")]
    public UnityEngine.UI.Slider sensitivitySlider;
    public TMP_Text sensitivityValue;
    public UnityEngine.UI.Slider volumeSlider;
    public TMP_Text volumeValue;
    public TMP_Dropdown qualityDropdown;
    public UnityEngine.UI.Toggle fullscreenToggle;
    public UnityEngine.UI.Toggle timerToggle;

    [Header("Botones")]
    public UnityEngine.UI.Button resetButton;
    public UnityEngine.UI.Button backButton;

    Action onClose;
    bool refreshing;

    public bool IsOpen => gameObject.activeSelf;

    void Awake()
    {
        if (sensitivitySlider != null)
        {
            sensitivitySlider.minValue = GameSettings.MinSensitivity;
            sensitivitySlider.maxValue = GameSettings.MaxSensitivity;
            sensitivitySlider.onValueChanged.AddListener(v => { if (!refreshing) GameSettings.MouseSensitivity = v; UpdateLabels(); });
        }
        if (volumeSlider != null)
        {
            volumeSlider.minValue = 0f;
            volumeSlider.maxValue = 1f;
            volumeSlider.onValueChanged.AddListener(v => { if (!refreshing) GameSettings.Volume = v; UpdateLabels(); });
        }
        if (qualityDropdown != null)
        {
            qualityDropdown.ClearOptions();
            var names = QualitySettings.names;
            var options = new System.Collections.Generic.List<string>();
            for (int i = 0; i < names.Length; i++) options.Add(FriendlyQualityName(names[i], i, names.Length));
            qualityDropdown.AddOptions(options);
            qualityDropdown.onValueChanged.AddListener(i => { if (!refreshing) GameSettings.Quality = i; });
        }
        if (fullscreenToggle != null) fullscreenToggle.onValueChanged.AddListener(v => { if (!refreshing) GameSettings.Fullscreen = v; });
        if (timerToggle != null) timerToggle.onValueChanged.AddListener(v => { if (!refreshing) GameSettings.ShowTimer = v; });
        if (resetButton != null) resetButton.onClick.AddListener(() => { GameSettings.ResetToDefaults(); Refresh(); });
        if (backButton != null) backButton.onClick.AddListener(Close);
    }

    public void Open(Action whenClosed)
    {
        onClose = whenClosed;
        gameObject.SetActive(true);
        Refresh();
    }

    public void Close()
    {
        if (!gameObject.activeSelf) return;
        PlayerPrefs.Save();
        gameObject.SetActive(false);
        var cb = onClose;
        onClose = null;
        cb?.Invoke();
    }

    void Refresh()
    {
        refreshing = true;
        if (sensitivitySlider != null) sensitivitySlider.value = GameSettings.MouseSensitivity;
        if (volumeSlider != null) volumeSlider.value = GameSettings.Volume;
        if (qualityDropdown != null) { qualityDropdown.value = GameSettings.Quality; qualityDropdown.RefreshShownValue(); }
        if (fullscreenToggle != null) fullscreenToggle.isOn = GameSettings.Fullscreen;
        if (timerToggle != null) timerToggle.isOn = GameSettings.ShowTimer;
        refreshing = false;
        UpdateLabels();
    }

    void UpdateLabels()
    {
        if (sensitivityValue != null && sensitivitySlider != null)
        {
            // 1.0x = sensibilidad por defecto
            sensitivityValue.text = (sensitivitySlider.value / GameSettings.DefaultSensitivity).ToString("0.0") + "x";
        }
        if (volumeValue != null && volumeSlider != null)
            volumeValue.text = Mathf.RoundToInt(volumeSlider.value * 100f) + "%";
    }

    static string FriendlyQualityName(string raw, int index, int count)
    {
        string lower = raw.ToLowerInvariant();
        if (lower.Contains("mobile") || lower.Contains("low") || lower.Contains("performant")) return "Baja";
        if (lower.Contains("pc") || lower.Contains("high") || lower.Contains("ultra")) return "Alta";
        if (lower.Contains("balanced") || lower.Contains("medium")) return "Media";
        if (count == 2) return index == 0 ? "Baja" : "Alta";
        return raw;
    }
}
