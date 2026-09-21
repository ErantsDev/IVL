using System.Collections.Generic;

/// <summary>
/// Lleva el registro de todos los InstrumentSlot de la escena y prende/apaga sus
/// resaltados verdes según qué instrumento tengas agarrado en la mano.
/// Es estático: no hace falta ponerlo en ningún GameObject, cada InstrumentSlot
/// se registra solo al activarse.
/// </summary>
public static class InstrumentSlotManager
{
    private static readonly List<InstrumentSlot> slots = new List<InstrumentSlot>();

    public static void Registrar(InstrumentSlot slot)
    {
        if (!slots.Contains(slot)) slots.Add(slot);
    }

    public static void Desregistrar(InstrumentSlot slot)
    {
        slots.Remove(slot);
    }

    /// <summary>Llamado al agarrar un instrumento: prende solo el slot que le corresponde.</summary>
    public static void MostrarSlotPara(string instrumentId)
    {
        foreach (var slot in slots)
        {
            slot.MostrarResaltado(slot.instrumentId == instrumentId);
        }
    }

    /// <summary>Llamado al soltar cualquier cosa: apaga todos los resaltados.</summary>
    public static void OcultarTodos()
    {
        foreach (var slot in slots)
        {
            slot.MostrarResaltado(false);
        }
    }
}
