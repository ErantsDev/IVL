using UnityEngine;

/// <summary>
/// Micropipeta del nivel 3 (ajustada a 6 mL): se carga en el frasco de solución salina y se
/// dispensa en el tubo de la gradilla. La punta se ve llena mientras está cargada.
/// </summary>
[RequireComponent(typeof(Grabbable))]
public class MicropipetteTool : MonoBehaviour
{
    [Tooltip("Extremo de la punta, en coordenadas locales.")]
    public Vector3 tipLocal = new Vector3(0.005f, -0.004f, 0f);
    [Tooltip("Extremo superior (botón), en coordenadas locales.")]
    public Vector3 topLocal = new Vector3(0.0f, 0.235f, 0f);
    public float volumeMl = 6f;
    public Renderer tipLiquid;

    public bool Loaded { get; private set; }
    public Grabbable Grabbable { get; private set; }

    void Awake()
    {
        Grabbable = GetComponent<Grabbable>();
        SetLoaded(false);
    }

    public void SetLoaded(bool on)
    {
        Loaded = on;
        if (tipLiquid != null) tipLiquid.enabled = on;
    }

    /// <summary>Eje local de la punta al botón (normalizado).</summary>
    public Vector3 LocalAxis => (topLocal - tipLocal).normalized;

    /// <summary>Rotación que deja la micropipeta vertical (punta abajo) mirando hacia `facing`.</summary>
    public Quaternion UprightRotation(Vector3 facing)
    {
        facing.y = 0f;
        if (facing.sqrMagnitude < 1e-6f) facing = Vector3.forward;
        var yaw = Quaternion.LookRotation(facing.normalized, Vector3.up);
        return yaw * Quaternion.FromToRotation(LocalAxis, Vector3.up);
    }

    /// <summary>Posición de la raíz para que la punta quede en `tipWorld` con la rotación dada.</summary>
    public Vector3 RootForTip(Vector3 tipWorld, Quaternion rot) => tipWorld - rot * Vector3.Scale(tipLocal, transform.lossyScale);

    public Vector3 TipWorld => transform.TransformPoint(tipLocal);
}
