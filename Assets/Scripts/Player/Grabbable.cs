using System;
using UnityEngine;

/// <summary>
/// Marca un objeto como agarrable por el jugador. Requiere Rigidbody y un Collider.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class Grabbable : MonoBehaviour
{
    [Tooltip("Nombre que se muestra en pantalla al apuntar al objeto. Vacío = nombre del GameObject.")]
    public string displayName;

    [Tooltip("true = el propio objeto decide cómo se carga (p. ej. el conejo en brazos); ObjectGrabber no lo mueve con física.")]
    public bool customCarry;

    [Tooltip("Desplazamiento (en ejes de la cámara) de dónde se sostiene; p. ej. un tubo un poco arriba de la mira para verlo completo.")]
    public Vector3 holdViewOffset;

    [Tooltip("Permite rotarlo con clic derecho mientras se sostiene.")]
    public bool allowRotate = true;

    public string DisplayName => string.IsNullOrEmpty(displayName) ? gameObject.name : displayName;

    Rigidbody body;
    public Rigidbody Body => body != null ? body : (body = GetComponent<Rigidbody>());

    /// <summary>Si devuelve texto, no se puede agarrar y ese texto se muestra como aviso.</summary>
    public Func<string> GrabBlocker;

    /// <summary>Si devuelve texto, no se puede soltar libremente y ese texto se muestra como aviso.</summary>
    public Func<string> ReleaseBlocker;

    /// <summary>Texto de ayuda mientras se sostiene (reemplaza a "E / Clic: soltar ...").</summary>
    public Func<string> HeldPrompt;

    /// <summary>Se dispara justo cuando el jugador agarra este objeto (ver ObjectGrabber.Grab).</summary>
    public event Action Grabbed;

    /// <summary>Se dispara justo cuando el jugador suelta este objeto (ver ObjectGrabber.Release).</summary>
    public event Action Released;

    void Awake()
    {
        body = GetComponent<Rigidbody>();
    }

    public void RaiseGrabbed() => Grabbed?.Invoke();
    public void RaiseReleased() => Released?.Invoke();
}
