using UnityEngine;

/// <summary>
/// Un lugar fijo y exacto en la mesa (o estante) donde debe quedar UN instrumento
/// específico del checklist. Colócalo como hijo de la mesa, en la posición y
/// rotación exactas donde quieres que el instrumento quede "parado" al soltarlo ahí.
///
/// Necesita un Collider marcado como Is Trigger con el tag "SlotInstrumento" en el
/// mismo GameObject (así SelectorInstrumentos lo detecta al soltar algo cerca).
/// </summary>
[DisallowMultipleComponent]
public class InstrumentSlot : MonoBehaviour
{
    [Tooltip("Debe coincidir EXACTAMENTE con el Instrument Id del instrumento que va aquí")]
    public string instrumentId;

    [Tooltip("El objeto visual (cuadro verde) que se prende cuando cargas el instrumento correcto")]
    public GameObject resaltado;

    void Awake()
    {
        if (resaltado != null) resaltado.SetActive(false);
        InstrumentSlotManager.Registrar(this);
    }

    void OnDestroy()
    {
        InstrumentSlotManager.Desregistrar(this);
    }

    public void MostrarResaltado(bool mostrar)
    {
        if (resaltado != null) resaltado.SetActive(mostrar);
    }
}
