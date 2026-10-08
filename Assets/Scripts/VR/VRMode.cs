using UnityEngine;
using UnityEngine.XR;

/// <summary>
/// ¿Está corriendo el juego con las gafas? Sólo puede ser verdadero en Android con un visor
/// activo (PICO). En computadora (editor o build de PC) siempre es falso, así que todo lo que
/// dependa de esto no cambia nada en PC.
/// </summary>
public static class VRMode
{
    public static bool Active => Application.platform == RuntimePlatform.Android && XRSettings.isDeviceActive;
}
