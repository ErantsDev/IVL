using UnityEngine;

/// <summary>
/// Giro por pasos con el joystick derecho (gira el cuerpo del jugador). Girar de golpe en vez de
/// suave evita el mareo. Sólo lo agrega VRBootstrap en modo VR.
/// </summary>
public class VRSnapTurn : MonoBehaviour
{
    public float degrees = 30f;

    void Update()
    {
        if (!GameFlow.GameplayInputAllowed) return;
        int t = VRInput.SnapTurn();
        if (t != 0) transform.Rotate(0f, degrees * t, 0f, Space.World);
    }
}
