using System;
using UnityEngine;

/// <summary>
/// Reloj de pared: gira las manecillas según la hora real del sistema.
/// Las manecillas deben estar en pivotes centrados en la carátula, apuntando hacia +Y local (las 12).
/// </summary>
[ExecuteAlways]
public class WallClock : MonoBehaviour
{
    public Transform hourHand;
    public Transform minuteHand;
    public Transform secondHand;
    [Tooltip("Si está activo, el segundero avanza de forma continua en vez de a saltos.")]
    public bool smoothSeconds;

    void Update()
    {
        var now = DateTime.Now;
        float sec = now.Second + (smoothSeconds ? now.Millisecond / 1000f : 0f);
        float min = now.Minute + sec / 60f;
        float hr = (now.Hour % 12) + min / 60f;

        // Visto de frente, un giro negativo en Z es en el sentido de las manecillas.
        if (hourHand != null) hourHand.localRotation = Quaternion.Euler(0f, 0f, -hr * 30f);
        if (minuteHand != null) minuteHand.localRotation = Quaternion.Euler(0f, 0f, -min * 6f);
        if (secondHand != null) secondHand.localRotation = Quaternion.Euler(0f, 0f, -sec * 6f);
    }
}
