using UnityEngine;

public class MirarRaton : MonoBehaviour
{
    public float sensibilidad = 2f;
    private float rotacionX = 0f;
    public Transform cuerpoJugador;

    [Header("Configuración de Zoom")]
    private Camera miCamara;
    public float fovNormal = 60f; // Vista estándar del juego
    public float fovZoom = 30f;   // Cuanto menor sea el número, más zoom hace
    public float velocidadZoom = 10f; // Qué tan suave se hace la transición

    void Start()
    {
        Cursor.lockState = CursorLockMode.Locked;
        // Obtiene la cámara adjunta a este objeto automáticamente
        miCamara = GetComponent<Camera>();
    }

    void Update()
    {
        // --- CÓDIGO DE ROTACIÓN EXISTENTE ---
        float mouseX = Input.GetAxis("Mouse X") * sensibilidad;
        float mouseY = Input.GetAxis("Mouse Y") * sensibilidad;

        rotacionX -= mouseY;
        rotacionX = Mathf.Clamp(rotacionX, -90f, 90f);

        transform.localRotation = Quaternion.Euler(rotacionX, 0f, 0f);
        cuerpoJugador.Rotate(Vector3.up * mouseX);

        // --- NUEVO CÓDIGO DE ZOOM ---
        // Si mantienes presionada la tecla Z, elige el FOV de zoom. Si la sueltas, vuelve al normal.
        float fovObjetivo = Input.GetKey(KeyCode.Z) ? fovZoom : fovNormal;

        // Cambia el FOV de forma fluida y suave en lugar de dar un golpe seco
        miCamara.fieldOfView = Mathf.Lerp(miCamara.fieldOfView, fovObjetivo, Time.deltaTime * velocidadZoom);
    }
}
