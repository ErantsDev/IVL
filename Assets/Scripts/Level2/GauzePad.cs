using UnityEngine;

/// <summary>Gasa estéril: se empapa con alcohol en la botella y cambia de aspecto.</summary>
[RequireComponent(typeof(Grabbable))]
public class GauzePad : MonoBehaviour
{
    public Renderer[] renderers;
    public Material dryMaterial;
    public Material wetMaterial;

    public bool IsSoaked { get; private set; }
    /// <summary>Veces que se usó para desinfectar (una gasa usada ya no está estéril).</summary>
    public int Uses { get; set; }

    public void SetSoaked(bool soaked)
    {
        IsSoaked = soaked;
        var m = soaked ? wetMaterial : dryMaterial;
        if (m == null || renderers == null) return;
        foreach (var r in renderers) if (r != null) r.sharedMaterial = m;
    }
}
