using TMPro;
using UnityEngine;

/// <summary>
/// Báscula digital (PetScale_LowPoly): muestra el peso en su pantalla, se puede tarar, la bandeja
/// baja un poco con el peso, la lectura oscila si el conejo está nervioso y avisa con un pitido
/// cuando la lectura es estable.
/// </summary>
public class ScaleDevice : MonoBehaviour
{
    public Transform tray;
    public TMP_Text lcdText;
    public TMP_Text statusText;
    public TMP_Text idText;
    [Tooltip("Dónde se sienta el conejo sobre la bandeja.")]
    public Transform weighPoint;
    public AudioSource audioSource;

    [Tooltip("Lectura sin nada encima (la báscula viene sin tarar). 0 = ya está en cero.")]
    public float residualKg;
    public float maxDip = 0.006f;
    public float stableSeconds = 1.4f;

    public RabbitController Load { get; private set; }
    public float Displayed { get; private set; }
    public bool IsStable { get; private set; }
    /// <summary>true si la báscula marca cero sin carga (tarada).</summary>
    public bool IsTared => Mathf.Abs(residualKg - tareKg) < 0.001f;

    float tareKg;
    float smooth;
    float dip;
    float stableTimer;
    bool beeped;
    Vector3 trayRest;
    string flash;
    float flashUntil;
    float noiseSeed;

    void Awake()
    {
        if (tray != null) trayRest = tray.localPosition;
        noiseSeed = Random.value * 100f;
        smooth = residualKg;
        if (audioSource == null)
        {
            audioSource = gameObject.AddComponent<AudioSource>();
            audioSource.playOnAwake = false;
            audioSource.spatialBlend = 1f;
            audioSource.minDistance = 0.5f;
            audioSource.maxDistance = 8f;
            audioSource.volume = 0.5f;
        }
    }

    public void SetResidual(float kg)
    {
        residualKg = kg;
        tareKg = 0f;
        smooth = kg;
    }

    public void SetLoad(RabbitController rabbit)
    {
        Load = rabbit;
        stableTimer = 0f;
        beeped = false;
        IsStable = false;
    }

    public void ClearLoad()
    {
        Load = null;
        stableTimer = 0f;
        beeped = false;
        IsStable = false;
    }

    /// <summary>Pone la lectura actual en cero.</summary>
    public void Tare()
    {
        tareKg = Raw;
        smooth = 0f;
        Flash("TARA", 0.9f);
        Beep(1320f);
    }

    public void Flash(string text, float seconds)
    {
        flash = text;
        flashUntil = Time.time + seconds;
    }

    public void Beep(float freq = 1000f)
    {
        if (audioSource != null) audioSource.PlayOneShot(ProceduralAudio.Beep(freq, 0.12f));
    }

    bool LoadResting => Load != null && Load.Place == RabbitPlace.OnScale && !Load.IsMoving;

    float Raw => residualKg + (LoadResting ? Load.WeightKg : 0f);

    void Update()
    {
        float target = Raw - tareKg;
        smooth = Mathf.Lerp(smooth, target, 1f - Mathf.Exp(-3.2f * Time.deltaTime));

        float noiseAmp = LoadResting ? 0.003f + Load.Stress * 0.0011f : 0f;
        float n = (Mathf.PerlinNoise(noiseSeed, Time.time * 5f) - 0.5f) * 2f * noiseAmp;
        Displayed = Mathf.Round((smooth + n) * 100f) / 100f;

        bool settled = Mathf.Abs(smooth - target) < 0.01f && noiseAmp < 0.035f;
        stableTimer = settled ? stableTimer + Time.deltaTime : 0f;
        IsStable = LoadResting && stableTimer >= stableSeconds;
        if (IsStable && !beeped) { beeped = true; Beep(1000f); }

        float dipTarget = LoadResting ? maxDip * Mathf.Clamp01(Load.WeightKg / 4.5f) : 0f;
        dip = Mathf.Lerp(dip, dipTarget, 1f - Mathf.Exp(-10f * Time.deltaTime));
        if (tray != null && tray.parent != null)
            tray.position = tray.parent.TransformPoint(trayRest) + Vector3.down * dip;
        if (LoadResting && weighPoint != null)
            Load.transform.position = weighPoint.position + Vector3.down * dip;

        UpdateTexts();
    }

    void UpdateTexts()
    {
        if (lcdText != null)
        {
            if (Time.time < flashUntil) lcdText.text = flash;
            else lcdText.text = Displayed.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) + "<size=60%> kg</size>";
        }
        if (statusText != null)
        {
            if (Load != null) statusText.text = IsStable ? "ESTABLE" : "PESANDO";
            else statusText.text = Mathf.Abs(Displayed) < 0.005f ? "CERO" : "";
        }
        if (idText != null) idText.text = Load != null ? Load.Code : "";
    }
}
