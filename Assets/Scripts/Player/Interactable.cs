using UnityEngine;

/// <summary>
/// Algo del escenario con lo que el jugador puede interactuar apuntando y presionando
/// E (acción 0) o R (acción 1), con o sin un objeto en la mano. Lo detecta ObjectGrabber.
/// </summary>
public abstract class Interactable : MonoBehaviour
{
    /// <summary>Texto de la acción disponible (sin la tecla), o null si esa acción no aplica ahora.</summary>
    public abstract string PromptFor(Grabbable held, int action);

    /// <summary>Ejecuta la acción. Sólo se llama si PromptFor devolvió texto.</summary>
    public abstract void Interact(Grabbable held, int action);

    /// <summary>Línea informativa opcional al apuntar (p. ej. "Esperando a que se estabilice...").</summary>
    public virtual string InfoFor(Grabbable held) => null;
}
