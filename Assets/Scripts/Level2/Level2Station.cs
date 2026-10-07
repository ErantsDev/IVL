using UnityEngine;

/// <summary>Puntos de interacción del nivel 2; lo que hacen lo decide Level2Manager.</summary>
public class Level2Station : Interactable
{
    public enum Kind { Cage, GloveBox, Scale, Pad, AlcoholBottle }

    public Kind kind;

    public override string PromptFor(Grabbable held, int action) =>
        Level2Manager.Instance != null ? Level2Manager.Instance.StationPrompt(this, held, action) : null;

    public override void Interact(Grabbable held, int action)
    {
        if (Level2Manager.Instance != null) Level2Manager.Instance.StationInteract(this, held, action);
    }

    public override string InfoFor(Grabbable held) =>
        Level2Manager.Instance != null ? Level2Manager.Instance.StationInfo(this, held) : null;
}
