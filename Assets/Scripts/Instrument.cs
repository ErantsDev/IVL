using UnityEngine;

/// <summary>
/// Estado de bioseguridad de un instrumento al iniciar el Nivel 1.
/// </summary>
public enum InstrumentCondition
{
    Sterile,      // Ya viene estéril / sellado (ej. jeringas, solución salina)
    NeedsReview,  // Hay que inspeccionarlo (clic derecho) para confirmar su estado
    NotSterile    // Debe pasar por el autoclave antes de poder tomarse
}

/// <summary>
/// Se coloca en cada prop instrumental de la escena (jeringa, pipeta, matraz, balanza...).
/// No conoce el resto del juego: solo reporta eventos al Level1Manager.
/// Conecta esto a tu sistema de clic actual llamando TakeItem() en clic izquierdo
/// e Inspect() en clic derecho.
/// </summary>
[DisallowMultipleComponent]
public class Instrument : MonoBehaviour
{
    [Header("Identidad")]
    [Tooltip("Debe coincidir con un id en Level1Manager.checklistIds")]
    public string instrumentId;
    public string displayName;

    [Header("Estado inicial (como en el mockup)")]
    public InstrumentCondition condition = InstrumentCondition.Sterile;

    [HideInInspector] public bool verified = false;

    /// <summary>Clic izquierdo: "tomar".</summary>
    public void TakeItem()
    {
        if (verified) return;

        if (condition == InstrumentCondition.NotSterile)
        {
            Level1Manager.Instance.ShowFeedback(
                $"{displayName} sin esterilizar",
                $"Marcaste \"{displayName}\" como listo, pero el indicador del autoclave sigue en rojo. Envíala a esterilizar.");
            return;
        }

        if (condition == InstrumentCondition.NeedsReview)
        {
            Level1Manager.Instance.ShowFeedback(
                $"{displayName} sin revisar",
                $"Inspecciona \"{displayName}\" (clic derecho) antes de marcarlo como listo.");
            return;
        }

        MarkVerified();
    }

    /// <summary>Clic derecho: "inspeccionar".</summary>
    public void Inspect()
    {
        if (condition == InstrumentCondition.NeedsReview)
        {
            condition = InstrumentCondition.Sterile; // ya se revisó, queda apto
            Level1Manager.Instance.ShowFeedback(
                $"{displayName} revisado",
                $"\"{displayName}\" está en buen estado. Ya puedes tomarlo para agregarlo al checklist.");
            return;
        }

        Level1Manager.Instance.ShowFeedback(
            displayName,
            condition == InstrumentCondition.Sterile
                ? "Instrumento estéril, listo para usarse."
                : "Este instrumento necesita pasar por el autoclave antes de continuar.");
    }

    /// <summary>Llamado desde SelectorInstrumentos al soltar el objeto en la zona del autoclave.</summary>
    public void Sterilize()
    {
        condition = InstrumentCondition.Sterile;
        MarkVerified(); // ya salió esterilizado, se marca listo automáticamente en el checklist
    }

    private void MarkVerified()
    {
        verified = true;
        Level1Manager.Instance.RegisterChecklistItem(instrumentId);
    }
}