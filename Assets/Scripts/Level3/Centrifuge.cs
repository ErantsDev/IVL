using System;
using System.Collections;
using TMPro;
using UnityEngine;

/// <summary>
/// Centrífuga de mesa del nivel 3: tapa con bisagra (animada), rotor de ángulo fijo con dos posiciones
/// opuestas (muestra y balance), pantalla con velocidad/tiempo y zumbido mientras gira.
/// Lo que se puede hacer con ella (y los errores) lo decide Level3Manager.
/// </summary>
public class Centrifuge : Interactable
{
    [Header("Partes")]
    public Transform lidHinge;
    [Tooltip("Ángulo de la bisagra con la tapa cerrada (el modelo viene abierto).")]
    public float lidClosedAngle = 71.8f;
    public float lidOpenAngle = -14f;
    public float lidSeconds = 0.7f;
    public Transform rotorPivot;
    public Transform[] slots = new Transform[2];
    public TMP_Text display;
    public Renderer statusLight;

    [Header("Giro")]
    [Tooltip("Segundos reales que dura un ciclo (los 5 min se simulan acelerados).")]
    public float spinRealSeconds = 14f;
    public float rampSeconds = 2.2f;
    [Tooltip("Velocidad visual del rotor a máxima velocidad (grados/s).")]
    public float visualDegPerSec = 1500f;

    [Header("Panel (valores programados)")]
    public int setRpm = 1000;
    public int setSeconds = 120;

    public bool LidOpen { get; private set; } = true;
    public bool LidMoving { get; private set; }
    public bool IsSpinning { get; private set; }
    public float CurrentRpm { get; private set; }
    public float RemainingSeconds { get; private set; }
    public int RunRpm { get; private set; }
    public bool JustFinished { get; private set; }

    readonly SampleTube[] occupants = new SampleTube[2];
    AudioSource hum, beeper;
    Coroutine lidRoutine;
    float rotorAngle;
    float blinkUntil;

    void Awake()
    {
        hum = gameObject.AddComponent<AudioSource>();
        hum.clip = HumClip();
        hum.loop = true;
        hum.playOnAwake = false;
        hum.spatialBlend = 1f;
        hum.minDistance = 0.6f;
        hum.maxDistance = 9f;
        hum.volume = 0f;
        beeper = gameObject.AddComponent<AudioSource>();
        beeper.playOnAwake = false;
        beeper.spatialBlend = 1f;
        beeper.minDistance = 0.6f;
        beeper.maxDistance = 9f;
        SetLid(true, true);
    }

    // ---------- Interactable: lo decide el nivel ----------

    public override string PromptFor(Grabbable held, int action) =>
        Level3Manager.Instance != null ? Level3Manager.Instance.CentrifugePrompt(held, action) : null;

    public override void Interact(Grabbable held, int action)
    {
        if (Level3Manager.Instance != null) Level3Manager.Instance.CentrifugeInteract(held, action);
    }

    public override string InfoFor(Grabbable held) =>
        Level3Manager.Instance != null ? Level3Manager.Instance.CentrifugeInfo(held) : null;

    // ---------- Tapa ----------

    public void SetLid(bool open, bool instant = false)
    {
        if (lidRoutine != null) StopCoroutine(lidRoutine);
        LidOpen = open;
        if (open) JustFinished = false;
        if (lidHinge == null) return;
        var target = Quaternion.Euler(open ? lidOpenAngle : lidClosedAngle, 0f, 0f);
        if (instant || !isActiveAndEnabled) { lidHinge.localRotation = target; LidMoving = false; return; }
        lidRoutine = StartCoroutine(LidRoutine(target, open));
    }

    IEnumerator LidRoutine(Quaternion target, bool open)
    {
        LidMoving = true;
        var from = lidHinge.localRotation;
        float t = 0f;
        while (t < 1f)
        {
            t += Time.deltaTime / lidSeconds;
            // Abre con un poco de inercia y cierra con un pequeño "clac" al final.
            float k = open ? 1f - Mathf.Pow(1f - Mathf.Clamp01(t), 3f) : Mathf.Clamp01(t) * Mathf.Clamp01(t);
            lidHinge.localRotation = Quaternion.SlerpUnclamped(from, target, k);
            yield return null;
        }
        lidHinge.localRotation = target;
        if (!open) Beep(420f, 0.06f, 0.35f);
        LidMoving = false;
        lidRoutine = null;
    }

    // ---------- Rotor ----------

    public SampleTube Occupant(int i) => occupants[i];
    public int SlotOf(SampleTube t) => occupants[0] == t && t != null ? 0 : occupants[1] == t && t != null ? 1 : -1;
    public bool Contains(SampleTube t) => SlotOf(t) >= 0;
    public int FreeSlot() => occupants[0] == null ? 0 : occupants[1] == null ? 1 : -1;
    public int Count => (occupants[0] != null ? 1 : 0) + (occupants[1] != null ? 1 : 0);

    public void Seat(SampleTube t, int slot)
    {
        occupants[slot] = t;
        t.DockAt(slots[slot], false);
    }

    public SampleTube Unseat(int slot)
    {
        var t = occupants[slot];
        occupants[slot] = null;
        return t;
    }

    public void StartSpin(int rpm, int seconds, Action onFinished)
    {
        if (IsSpinning) return;
        StartCoroutine(SpinRoutine(rpm, seconds, onFinished));
    }

    IEnumerator SpinRoutine(int rpm, int seconds, Action onFinished)
    {
        IsSpinning = true;
        JustFinished = false;
        RunRpm = rpm;
        RemainingSeconds = seconds;
        Beep(1200f, 0.08f, 0.5f);
        hum.pitch = 0.4f;
        hum.volume = 0f;
        hum.Play();

        float total = Mathf.Max(spinRealSeconds, rampSeconds * 2f + 1f);
        float t = 0f;
        while (t < total)
        {
            t += Time.deltaTime;
            float speed = Mathf.Clamp01(Mathf.Min(t / rampSeconds, (total - t) / rampSeconds));
            speed = Mathf.SmoothStep(0f, 1f, speed);
            CurrentRpm = rpm * speed;
            RemainingSeconds = Mathf.Max(0f, seconds * (1f - t / total));
            rotorAngle = (rotorAngle + visualDegPerSec * speed * Time.deltaTime) % 360f;
            if (rotorPivot != null) rotorPivot.localRotation = Quaternion.Euler(0f, rotorAngle, 0f);
            hum.volume = 0.32f * speed * GameSettings.Volume;
            hum.pitch = 0.4f + 0.75f * speed;
            yield return null;
        }
        CurrentRpm = 0f;
        RemainingSeconds = 0f;
        hum.Stop();
        IsSpinning = false;
        JustFinished = true;
        blinkUntil = Time.time + 4f;
        StartCoroutine(EndBeeps());
        onFinished?.Invoke();
    }

    IEnumerator EndBeeps()
    {
        for (int i = 0; i < 3; i++)
        {
            Beep(1700f, 0.12f, 0.55f);
            yield return new WaitForSeconds(0.25f);
        }
    }

    public void Beep(float freq, float seconds, float volume)
    {
        if (beeper == null) return;
        beeper.PlayOneShot(ProceduralAudio.Beep(freq, seconds), volume);
    }

    // ---------- Pantalla ----------

    void Update()
    {
        if (display == null) return;
        string txt;
        Color c = new Color(0.45f, 1f, 0.6f);
        if (IsSpinning)
            txt = Mathf.RoundToInt(CurrentRpm / 10f) * 10 + " rpm\n" + Clock(RemainingSeconds);
        else if (JustFinished && Time.time < blinkUntil)
        {
            txt = ((int)(Time.time * 3f) % 2 == 0) ? "FIN\n00:00" : "\n";
            c = new Color(1f, 0.85f, 0.35f);
        }
        else if (LidOpen)
        {
            txt = "TAPA\nABIERTA";
            c = new Color(1f, 0.6f, 0.35f);
        }
        else txt = setRpm + " rpm\n" + Clock(setSeconds);
        if (display.text != txt) display.text = txt;
        display.color = c;
        if (statusLight != null)
        {
            var col = IsSpinning ? new Color(0.2f, 1f, 0.35f) : LidOpen ? new Color(1f, 0.55f, 0.2f) : new Color(0.25f, 0.5f, 1f);
            statusLight.material.SetColor("_BaseColor", col);
            if (statusLight.material.HasProperty("_EmissionColor")) statusLight.material.SetColor("_EmissionColor", col * 1.5f);
        }
    }

    public static string Clock(float seconds)
    {
        int s = Mathf.Max(0, Mathf.CeilToInt(seconds - 0.001f));
        return (s / 60).ToString("00") + ":" + (s % 60).ToString("00");
    }

    // ---------- Sonido ----------

    static AudioClip hum1;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => hum1 = null;

    /// <summary>Zumbido de motor en bucle (1 s exacto: 60 Hz y armónicos + un silbido agudo).</summary>
    static AudioClip HumClip()
    {
        if (hum1 != null) return hum1;
        const int rate = 44100;
        var data = new float[rate];
        var rng = new System.Random(3);
        float lp = 0f;
        for (int i = 0; i < rate; i++)
        {
            float t = i / (float)rate;
            lp = Mathf.Lerp(lp, (float)rng.NextDouble() * 2f - 1f, 0.08f);
            data[i] = 0.30f * Mathf.Sin(2f * Mathf.PI * 60f * t)
                    + 0.16f * Mathf.Sin(2f * Mathf.PI * 120f * t)
                    + 0.07f * Mathf.Sin(2f * Mathf.PI * 180f * t)
                    + 0.05f * Mathf.Sin(2f * Mathf.PI * 840f * t)
                    + 0.10f * lp;
        }
        hum1 = AudioClip.Create("Centrifuga", rate, 1, rate, false);
        hum1.SetData(data, 0);
        return hum1;
    }
}
