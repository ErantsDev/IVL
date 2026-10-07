using UnityEngine;

/// <summary>
/// Zona de agarre extra (collider "trigger") más grande que el objeto, para que los objetos
/// delgados (p. ej. la micropipeta) sean fáciles de apuntar y agarrar. No afecta la física:
/// es un trigger y ObjectGrabber sólo lo toma en cuenta al apuntar.
/// </summary>
[RequireComponent(typeof(Collider))]
public class GrabHitbox : MonoBehaviour
{
    void Reset()
    {
        var c = GetComponent<Collider>();
        if (c != null) c.isTrigger = true;
    }

    void Awake()
    {
        var c = GetComponent<Collider>();
        if (c != null) c.isTrigger = true;
    }
}
