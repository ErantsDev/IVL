using UnityEngine;
using UnityEngine.XR;

/// <summary>
/// Aplica la rotación y el movimiento de la cabeza (gafas) a esta cámara, en coordenadas locales
/// respecto a su padre ("..._VROffset"), que conserva la posición de los ojos que define el juego.
/// La posición se toma relativa a la primera lectura, así la cámara arranca exactamente a la
/// altura de los ojos del juego y sólo suma lo que el usuario mueva la cabeza.
/// Sólo lo agrega <see cref="VRBootstrap"/> en modo VR.
/// </summary>
[DefaultExecutionOrder(-200)]
public class VRHeadPose : MonoBehaviour
{
    bool hasBaseline;
    Vector3 baseline;
    Camera cam;

    /// <summary>La cabeza que se está usando ahora (cámara del jugador o del menú).</summary>
    public static VRHeadPose Current { get; private set; }
    public Camera Cam => cam != null ? cam : (cam = GetComponent<Camera>());
    /// <summary>Posición de la cabeza en la primera lectura: origen común de cabeza y controles.</summary>
    public Vector3 Baseline => baseline;

    void OnEnable()
    {
        Current = this;
        Application.onBeforeRender += Apply;
    }

    void OnDisable()
    {
        Application.onBeforeRender -= Apply;
        if (Current == this) Current = null;
    }

    // En Update para que los scripts que apuntan con la cámara (agarrar, espejo) usen la pose actual,
    // y otra vez justo antes de dibujar para la menor latencia posible.
    void Update() => Apply();

    /// <summary>Vuelve a tomar la posición actual de la cabeza como el centro.</summary>
    public void Recenter() => hasBaseline = false;

    void Apply()
    {
        var dev = InputDevices.GetDeviceAtXRNode(XRNode.CenterEye);
        if (!dev.isValid) dev = InputDevices.GetDeviceAtXRNode(XRNode.Head);
        if (!dev.isValid) return;

        if (dev.TryGetFeatureValue(CommonUsages.centerEyeRotation, out Quaternion rot)
            || dev.TryGetFeatureValue(CommonUsages.deviceRotation, out rot))
            transform.localRotation = rot;

        if (dev.TryGetFeatureValue(CommonUsages.centerEyePosition, out Vector3 pos)
            || dev.TryGetFeatureValue(CommonUsages.devicePosition, out pos))
        {
            if (!hasBaseline) { baseline = pos; hasBaseline = true; }
            transform.localPosition = pos - baseline;
        }
    }
}
