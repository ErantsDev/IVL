using UnityEngine;

/// <summary>Modo de juego elegido en el menú principal.</summary>
public enum GameMode
{
    /// <summary>Pasos guiados, lista de materiales, marcador y avisos.</summary>
    Educativo,
    /// <summary>Sin guías: se registran errores y tiempo y se da una calificación sobre 10.</summary>
    Evaluacion
}

/// <summary>
/// Estado que sobrevive a recargas de escena durante una misma partida
/// (modo elegido y si al recargar se debe entrar directo al nivel, p. ej. al "Reintentar").
/// </summary>
public static class GameSession
{
    public static GameMode Mode = GameMode.Educativo;

    /// <summary>Nivel que se juega (1 = material estéril, 2 = preparación del animal, 3 = lavado de eritrocitos).</summary>
    public static int Level = 1;
    public const int MaxLevel = 3;

    /// <summary>true = al recargar la escena se salta el menú y arranca el nivel con el mismo modo.</summary>
    public static bool StartLevelDirectly;

    // Con "Enter Play Mode Options" sin recarga de dominio, los estáticos no se reinician solos.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        Mode = GameMode.Educativo;
        Level = 1;
        StartLevelDirectly = false;
    }
}

/// <summary>Opciones del jugador, guardadas en PlayerPrefs.</summary>
public static class GameSettings
{
    const string KeySensitivity = "opt_sensibilidad";
    const string KeyVolume = "opt_volumen";
    const string KeyShowTimer = "opt_cronometro";
    const string KeyQuality = "opt_calidad";
    const string KeyFullscreen = "opt_pantallaCompleta";

    public const float DefaultSensitivity = 0.15f;
    public const float MinSensitivity = 0.03f;
    public const float MaxSensitivity = 0.5f;

    static bool loaded;
    static float sensitivity, volume;
    static bool showTimer, fullscreen;
    static int quality;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => loaded = false;

    static void EnsureLoaded()
    {
        if (loaded) return;
        loaded = true;
        sensitivity = PlayerPrefs.GetFloat(KeySensitivity, DefaultSensitivity);
        volume = PlayerPrefs.GetFloat(KeyVolume, 1f);
        showTimer = PlayerPrefs.GetInt(KeyShowTimer, 1) == 1;
        fullscreen = PlayerPrefs.GetInt(KeyFullscreen, Screen.fullScreen ? 1 : 0) == 1;
        quality = PlayerPrefs.GetInt(KeyQuality, QualitySettings.GetQualityLevel());
    }

    public static float MouseSensitivity
    {
        get { EnsureLoaded(); return sensitivity; }
        set { EnsureLoaded(); sensitivity = Mathf.Clamp(value, MinSensitivity, MaxSensitivity); PlayerPrefs.SetFloat(KeySensitivity, sensitivity); }
    }

    public static float Volume
    {
        get { EnsureLoaded(); return volume; }
        set { EnsureLoaded(); volume = Mathf.Clamp01(value); PlayerPrefs.SetFloat(KeyVolume, volume); AudioListener.volume = volume; }
    }

    public static bool ShowTimer
    {
        get { EnsureLoaded(); return showTimer; }
        set { EnsureLoaded(); showTimer = value; PlayerPrefs.SetInt(KeyShowTimer, value ? 1 : 0); }
    }

    public static bool Fullscreen
    {
        get { EnsureLoaded(); return fullscreen; }
        set { EnsureLoaded(); fullscreen = value; PlayerPrefs.SetInt(KeyFullscreen, value ? 1 : 0); Screen.fullScreen = value; }
    }

    public static int Quality
    {
        get { EnsureLoaded(); return quality; }
        set
        {
            EnsureLoaded();
            quality = Mathf.Clamp(value, 0, QualitySettings.names.Length - 1);
            PlayerPrefs.SetInt(KeyQuality, quality);
            QualitySettings.SetQualityLevel(quality, true);
        }
    }

    /// <summary>Aplica al motor las opciones guardadas (al iniciar el juego).</summary>
    public static void ApplyAll()
    {
        EnsureLoaded();
        AudioListener.volume = volume;
        if (QualitySettings.GetQualityLevel() != quality) QualitySettings.SetQualityLevel(quality, true);
        if (!Application.isEditor && Screen.fullScreen != fullscreen) Screen.fullScreen = fullscreen;
    }

    public static void ResetToDefaults()
    {
        MouseSensitivity = DefaultSensitivity;
        Volume = 1f;
        ShowTimer = true;
        Quality = QualitySettings.names.Length - 1;
        Fullscreen = true;
    }
}
