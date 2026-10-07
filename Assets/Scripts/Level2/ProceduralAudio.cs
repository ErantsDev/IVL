using System.Collections.Generic;
using UnityEngine;

/// <summary>Sonidos generados por código (pitidos de la báscula y zumbido de la rasuradora).</summary>
public static class ProceduralAudio
{
    const int Rate = 44100;
    static readonly Dictionary<int, AudioClip> beeps = new Dictionary<int, AudioClip>();
    static AudioClip buzz;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        beeps.Clear();
        buzz = null;
    }

    public static AudioClip Beep(float freq, float seconds)
    {
        int key = Mathf.RoundToInt(freq) * 1000 + Mathf.RoundToInt(seconds * 1000f);
        if (beeps.TryGetValue(key, out var c) && c != null) return c;
        int n = Mathf.CeilToInt(Rate * seconds);
        var data = new float[n];
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)Rate;
            float env = Mathf.Min(1f, t / 0.005f) * Mathf.Min(1f, (seconds - t) / 0.02f);
            data[i] = 0.35f * env * Mathf.Sign(Mathf.Sin(2f * Mathf.PI * freq * t)) * 0.6f
                    + 0.15f * env * Mathf.Sin(2f * Mathf.PI * freq * 2f * t);
        }
        c = AudioClip.Create("Pitido_" + key, n, 1, Rate, false);
        c.SetData(data, 0);
        beeps[key] = c;
        return c;
    }

    /// <summary>Zumbido en bucle de una rasuradora eléctrica (~120 Hz con armónicos y algo de ruido).</summary>
    public static AudioClip ClipperBuzz()
    {
        if (buzz != null) return buzz;
        const float f = 120f;
        int n = Rate; // 1 s: 120 ciclos exactos, el bucle no se nota
        var data = new float[n];
        var rng = new System.Random(7);
        float lp = 0f;
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)Rate;
            float ph = (t * f) % 1f;
            float saw = ph * 2f - 1f;
            float h = 0.5f * Mathf.Sin(2f * Mathf.PI * f * 2f * t) + 0.25f * Mathf.Sin(2f * Mathf.PI * f * 5f * t);
            lp = Mathf.Lerp(lp, (float)rng.NextDouble() * 2f - 1f, 0.3f);
            data[i] = 0.22f * saw + 0.12f * h + 0.06f * lp;
        }
        buzz = AudioClip.Create("Rasuradora", n, 1, Rate, false);
        buzz.SetData(data, 0);
        return buzz;
    }
}
