/// <summary>
/// Al apuntar al espejo: E = saludar (el avatar saluda con la mano) y se muestra el nombre del alumno.
/// </summary>
public class MirrorInteractable : Interactable
{
    public override string PromptFor(Grabbable held, int action)
    {
        if (action != 0 || PlayerAvatar.Instance == null) return null;
        return "saludar";
    }

    public override void Interact(Grabbable held, int action)
    {
        if (action == 0 && PlayerAvatar.Instance != null) PlayerAvatar.Instance.Wave();
    }

    public override string InfoFor(Grabbable held)
    {
        string who = StudentProfile.ShortLabel;
        return string.IsNullOrEmpty(who) ? "Espejo" : "Espejo  ·  " + who;
    }
}
