using UnityEngine;

/// <summary>
/// Muestra los controles de un soporte de cámara (menú o jugador) sólo mientras esa cámara está
/// en uso. Así nunca hay dos pares de controles a la vista. Sólo en VR.
/// </summary>
public class VRRigVisibility : MonoBehaviour
{
    public GameObject cameraObject;
    public GameObject[] controllers;

    void LateUpdate()
    {
        bool on = cameraObject != null && cameraObject.activeInHierarchy;
        if (controllers == null) return;
        foreach (var c in controllers)
            if (c != null && c.activeSelf != on) c.SetActive(on);
    }
}
