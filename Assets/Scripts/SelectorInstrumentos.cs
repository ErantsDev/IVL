using UnityEngine;
using UnityEngine.InputSystem;

public class SelectorInstrumentos : MonoBehaviour
{
    public Transform puntoDeSujeccion;
    public float alcance = 3f;
    public float margenColision = 0.05f; // qué tanto se detiene antes de tocar un obstáculo

    private GameObject objetoSujetado = null;
    private Rigidbody rbObjetoSujetado = null;
    private Collider colliderObjetoSujetado = null;
    private Camera camaraPrincipal;

    void Start()
    {
        camaraPrincipal = Camera.main; // cacheado una sola vez en lugar de buscarlo cada frame
    }

    void Update()
    {
        // 1. Detectar el clic izquierdo para agarrar o soltar
        if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
        {
            if (objetoSujetado == null) IntentarAgarrar();
            else SoltarObjeto();
        }

        // 2. NUEVO: clic derecho para inspeccionar (checklist del Nivel 1)
        if (Mouse.current != null && Mouse.current.rightButton.wasPressedThisFrame)
        {
            IntentarInspeccionar();
        }

        // 3. Mover el objeto sin dejar que atraviese paredes, mesas o estantes
        if (objetoSujetado != null)
        {
            objetoSujetado.transform.position = CalcularPosicionSostenida();
            objetoSujetado.transform.rotation = puntoDeSujeccion.rotation;
        }
    }

    /// <summary>
    /// Calcula dónde debería estar el objeto que cargas: normalmente en puntoDeSujeccion,
    /// pero si entre la cámara y ese punto hay algo sólido de por medio (una mesa, un
    /// estante, una pared), se detiene justo antes de tocarlo. Como el collider del
    /// objeto sujetado está desactivado mientras lo cargas, este rayo nunca choca
    /// contra el propio objeto, solo contra el resto del escenario.
    /// </summary>
    private Vector3 CalcularPosicionSostenida()
    {
        Vector3 origen = camaraPrincipal.transform.position;
        Vector3 destino = puntoDeSujeccion.position;

        Vector3 diferencia = destino - origen;
        float distancia = diferencia.magnitude;
        if (distancia < 0.001f) return destino;

        Vector3 direccion = diferencia / distancia;

        if (Physics.Raycast(origen, direccion, out RaycastHit golpe, distancia, ~0, QueryTriggerInteraction.Ignore))
        {
            return golpe.point - direccion * margenColision;
        }

        return destino;
    }

    void IntentarAgarrar()
    {
        Ray rayo = new Ray(camaraPrincipal.transform.position, camaraPrincipal.transform.forward);
        RaycastHit golpe;

        if (Physics.Raycast(rayo, out golpe, alcance, ~0, QueryTriggerInteraction.Ignore))
        {
            if (golpe.collider.CompareTag("Instrumento"))
            {
                objetoSujetado = golpe.collider.gameObject;

                rbObjetoSujetado = objetoSujetado.GetComponent<Rigidbody>();
                if (rbObjetoSujetado != null)
                {
                    rbObjetoSujetado.isKinematic = true;
                }

                // Lo desvinculamos de cualquier padre problemático al agarrarlo
                objetoSujetado.transform.SetParent(null);

                // NUEVO: desactivamos su collider mientras lo cargamos, así no puede
                // encimarse (overlap) con mesas, paredes o el propio jugador mientras
                // lo movemos a mano; se reactiva al soltarlo en SoltarObjeto()
                colliderObjetoSujetado = objetoSujetado.GetComponent<Collider>();
                if (colliderObjetoSujetado != null) colliderObjetoSujetado.enabled = false;

                // NUEVO: avisar al checklist del Nivel 1 que este instrumento fue tomado,
                // y prender el resaltado verde de su slot en la mesa (si tiene uno)
                Instrument instrumento = objetoSujetado.GetComponent<Instrument>();
                if (instrumento != null)
                {
                    instrumento.TakeItem();
                    InstrumentSlotManager.MostrarSlotPara(instrumento.instrumentId);
                }
            }
        }
    }

    void IntentarInspeccionar()
    {
        // Si ya tienes algo en la mano, inspeccionamos eso.
        // Si no, inspeccionamos lo que estés mirando directamente.
        GameObject objetivo = objetoSujetado;

        if (objetivo == null)
        {
            Ray rayo = new Ray(camaraPrincipal.transform.position, camaraPrincipal.transform.forward);
            RaycastHit golpe;

            if (Physics.Raycast(rayo, out golpe, alcance, ~0, QueryTriggerInteraction.Ignore) && golpe.collider.CompareTag("Instrumento"))
            {
                objetivo = golpe.collider.gameObject;
            }
        }

        if (objetivo == null) return;

        Instrument instrumento = objetivo.GetComponent<Instrument>();
        if (instrumento != null)
        {
            instrumento.Inspect();
        }
    }

    void SoltarObjeto()
    {
        Collider[] zonasCercanas = Physics.OverlapSphere(objetoSujetado.transform.position, 0.3f);
        bool fueAnclado = false;
        Instrument instrumento = objetoSujetado.GetComponent<Instrument>();

        // Ya nos estamos separando del objeto: apagamos cualquier resaltado que
        // se haya prendido al agarrarlo, sin importar dónde termine cayendo.
        InstrumentSlotManager.OcultarTodos();

        foreach (Collider zona in zonasCercanas)
        {
            // NUEVO: slot exacto de un instrumento en la mesa -> solo ancla si el id coincide
            if (zona.CompareTag("SlotInstrumento"))
            {
                InstrumentSlot slot = zona.GetComponent<InstrumentSlot>();
                bool esElSlotCorrecto = slot != null && instrumento != null
                    && slot.instrumentId == instrumento.instrumentId;

                if (!esElSlotCorrecto)
                {
                    // no es el lugar de este instrumento: lo dejamos caer normal
                    continue;
                }

                objetoSujetado.transform.SetParent(slot.transform, true);
                objetoSujetado.transform.position = slot.transform.position;
                objetoSujetado.transform.rotation = slot.transform.rotation;

                if (rbObjetoSujetado != null) rbObjetoSujetado.isKinematic = true;

                fueAnclado = true;
                break;
            }

            // NUEVO: zona del autoclave -> intenta cargarlo, el ciclo de esterilización
            // completo (cerrar tapa, esperar, abrir) lo maneja AutoclaveController
            if (zona.CompareTag("ZonaAutoclave"))
            {
                AutoclaveController autoclave = zona.GetComponentInParent<AutoclaveController>();
                bool cargado = autoclave != null && autoclave.TryLoadItem(objetoSujetado.transform);

                if (!cargado)
                {
                    // tapa cerrada, cámara ocupada o ciclo en curso: no se pudo meter
                    continue;
                }

                if (rbObjetoSujetado != null) rbObjetoSujetado.isKinematic = true;
                fueAnclado = true;
                break;
            }

            if (zona.CompareTag("ZonaAnclaje"))
            {
                // Al anclarlo al estante, mantenemos su escala visual intacta con 'true'
                objetoSujetado.transform.SetParent(zona.transform, true);

                objetoSujetado.transform.position = zona.transform.GetChild(0).position;
                objetoSujetado.transform.rotation = zona.transform.GetChild(0).rotation;

                if (rbObjetoSujetado != null) rbObjetoSujetado.isKinematic = true;

                fueAnclado = true;
                break;
            }
        }

        if (!fueAnclado)
        {
            objetoSujetado.transform.SetParent(null);
            if (rbObjetoSujetado != null) rbObjetoSujetado.isKinematic = false;
        }

        // NUEVO: reactivamos el collider ahora que ya no lo estamos cargando a mano
        if (colliderObjetoSujetado != null) colliderObjetoSujetado.enabled = true;

        objetoSujetado = null;
        rbObjetoSujetado = null;
        colliderObjetoSujetado = null;
    }
}
