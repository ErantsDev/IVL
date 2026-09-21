using UnityEngine;

public class MovimientoPersonaje : MonoBehaviour
{
    public float velocidad = 5f;

    void Update()
    {
        // Lee las teclas A/D y W/S
        float moverX = Input.GetAxis("Horizontal");
        float moverZ = Input.GetAxis("Vertical");

        // Crea la dirección del movimiento
        Vector3 movimiento = new Vector3(moverX, 0f, moverZ);

        // CAMBIO CLAVE: Usar Space.Self para que respete la rotación del personaje
        transform.Translate(movimiento * velocidad * Time.deltaTime, Space.Self);
    }
}
