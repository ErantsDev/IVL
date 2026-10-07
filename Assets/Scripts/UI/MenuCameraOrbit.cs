using UnityEngine;

/// <summary>
/// Cámara de fondo del menú principal: recorre lentamente el laboratorio en una elipse
/// mirando hacia el centro del salón. Usa tiempo sin escalar.
/// </summary>
public class MenuCameraOrbit : MonoBehaviour
{
    [Tooltip("Punto al que mira la cámara (centro del recorrido en XZ).")]
    public Vector3 center = new Vector3(0.1f, 1.0f, -0.2f);
    public float radiusX = 2.0f;
    public float radiusZ = 1.15f;
    public float height = 1.75f;
    [Tooltip("Segundos para dar una vuelta completa.")]
    public float secondsPerLoop = 80f;
    [Tooltip("Ángulo inicial (grados) del recorrido.")]
    public float startAngle = 110f;

    void LateUpdate()
    {
        float a = (startAngle + Time.unscaledTime * 360f / Mathf.Max(1f, secondsPerLoop)) * Mathf.Deg2Rad;
        var pos = new Vector3(
            center.x + Mathf.Cos(a) * radiusX,
            height + Mathf.Sin(a * 2f) * 0.06f,
            center.z + Mathf.Sin(a) * radiusZ);
        transform.SetPositionAndRotation(pos, Quaternion.LookRotation(center - pos, Vector3.up));
    }
}
