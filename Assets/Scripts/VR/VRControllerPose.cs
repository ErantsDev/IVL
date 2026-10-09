using UnityEngine;
using UnityEngine.XR;

/// <summary>
/// Hace que este objeto siga la posición y rotación de un control de las gafas (mano izquierda o
/// derecha). Vive junto a la cámara, dentro del mismo "..._VROffset", así que comparte el mismo
/// origen que la cabeza (<see cref="VRHeadPose"/>). Sólo lo crea VRBootstrap en modo VR.
/// </summary>
[DefaultExecutionOrder(-190)]
public class VRControllerPose : MonoBehaviour
{
    public XRNode node = XRNode.RightHand;
    [Tooltip("Cabeza de referencia: su punto inicial es el origen de los controles.")]
    public VRHeadPose head;

    /// <summary>Controles del jugador (null si no hay gafas).</summary>
    public static VRControllerPose Left { get; private set; }
    public static VRControllerPose Right { get; private set; }

    void OnEnable()
    {
        if (node == XRNode.LeftHand) Left = this; else Right = this;
        Application.onBeforeRender += Apply;
    }

    void OnDisable()
    {
        Application.onBeforeRender -= Apply;
        if (Left == this) Left = null;
        if (Right == this) Right = null;
    }
    void Update() => Apply();

    void Apply()
    {
        var d = InputDevices.GetDeviceAtXRNode(node);
        if (!d.isValid) return;
        Vector3 origin = head != null ? head.Baseline : Vector3.zero;
        if (d.TryGetFeatureValue(CommonUsages.devicePosition, out Vector3 p)) transform.localPosition = p - origin;
        if (d.TryGetFeatureValue(CommonUsages.deviceRotation, out Quaternion r)) transform.localRotation = r;
    }

    /// <summary>
    /// Crea un control con el modelo oficial de PICO (el SDK elige Neo 3 / PICO 4 según el visor)
    /// dentro de <paramref name="parent"/>.
    /// </summary>
    public static VRControllerPose Create(Transform parent, XRNode node, VRHeadPose head)
    {
        bool left = node == XRNode.LeftHand;
        var go = new GameObject(left ? "MandoIzquierdo" : "MandoDerecho");
        go.transform.SetParent(parent, false);
        var pose = go.AddComponent<VRControllerPose>();
        pose.node = node;
        pose.head = head;

        // Ojo: el prefab derecho del SDK se llama con un espacio al final.
        var prefab = Resources.Load<GameObject>(left ? "Prefabs/LeftControllerModel" : "Prefabs/RightControllerModel ");
        if (prefab != null) Object.Instantiate(prefab, go.transform, false);
        else Debug.LogWarning("[VR] No se encontró el modelo del control en el SDK de PICO.");

        // El derecho lleva el láser para apuntar y usar los paneles.
        if (!left) go.AddComponent<VRLaser>();
        return pose;
    }
}
