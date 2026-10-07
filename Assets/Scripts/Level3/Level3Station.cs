using UnityEngine;

/// <summary>Puntos de interacción del nivel 3 (gradilla, frasco de solución salina, vaso de desechos, soporte de la micropipeta); lo que hacen lo decide Level3Manager.</summary>
public class Level3Station : Interactable
{
    public enum Kind { Rack, Saline, Waste, PipetteStand }

    public Kind kind;
    [Tooltip("Gradilla: huecos donde se acomodan los tubos. Frasco/vaso: punto de la boca.")]
    public Transform[] points;

    public override string PromptFor(Grabbable held, int action) =>
        Level3Manager.Instance != null ? Level3Manager.Instance.StationPrompt(this, held, action) : null;

    public override void Interact(Grabbable held, int action)
    {
        if (Level3Manager.Instance != null) Level3Manager.Instance.StationInteract(this, held, action);
    }

    public override string InfoFor(Grabbable held) =>
        Level3Manager.Instance != null ? Level3Manager.Instance.StationInfo(this, held) : null;
}
