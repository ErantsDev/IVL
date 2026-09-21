using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Coordina el ciclo de esterilización sobre AutoclaveLid:
///   1) El jugador abre la tapa (tecla E mirando al autoclave, con la cámara vacía).
///   2) Suelta un instrumento sobre la ZonaAutoclave (ver SelectorInstrumentos) -> se
///      carga en el LoadPoint del FBX, pero todavía sigue sin esterilizar.
///   3) El jugador presiona E de nuevo -> se cierra la tapa sola y arranca el ciclo:
///      espera "duracionEsterilizacion" segundos, marca el instrumento como estéril,
///      abre la tapa sola y lo libera para poder tomarlo otra vez.
///
/// Se coloca en el mismo GameObject que AutoclaveLid (Autoclave_Body).
/// </summary>
[RequireComponent(typeof(AutoclaveLid))]
public class AutoclaveController : MonoBehaviour
{
    [Header("Duración del ciclo, en segundos")]
    public float duracionEsterilizacion = 5f;

    [Header("Interacción (tecla E mirando al autoclave)")]
    public float alcanceInteraccion = 3f;

    private AutoclaveLid lid;
    private Transform itemEnCamara;
    private bool cicloEnCurso;

    void Awake()
    {
        lid = GetComponent<AutoclaveLid>();
    }

    void Update()
    {
        if (Keyboard.current == null || !Keyboard.current.eKey.wasPressedThisFrame) return;
        if (cicloEnCurso || lid.IsMoving) return;
        if (!JugadorMirandoAutoclave()) return;

        if (itemEnCamara != null)
        {
            StartCoroutine(CicloDeEsterilizacion());
        }
        else
        {
            lid.Toggle();
        }
    }

    /// <summary>
    /// Llamado desde SelectorInstrumentos al soltar un objeto sobre la ZonaAutoclave.
    /// Devuelve true si logró meterlo (tapa abierta y cámara libre).
    /// </summary>
    public bool TryLoadItem(Transform item)
    {
        if (!lid.IsOpen || itemEnCamara != null || cicloEnCurso) return false;
        if (!lid.LoadItem(item)) return false;

        itemEnCamara = item;

        Level1Manager.Instance?.ShowFeedback(
            "Instrumento cargado",
            "Cierra la tapa del autoclave (tecla E) para iniciar el ciclo de esterilización.");

        return true;
    }

    private bool JugadorMirandoAutoclave()
    {
        if (Camera.main == null) return false;

        Ray rayo = new Ray(Camera.main.transform.position, Camera.main.transform.forward);
        return Physics.Raycast(rayo, out RaycastHit golpe, alcanceInteraccion)
            && golpe.collider.transform.root == transform.root;
    }

    private IEnumerator CicloDeEsterilizacion()
    {
        cicloEnCurso = true;

        lid.Close();
        yield return new WaitUntil(() => !lid.IsMoving);

        Level1Manager.Instance?.ShowFeedback(
            "Esterilizando",
            $"El autoclave está en ciclo, espera {duracionEsterilizacion:0} segundos.");

        yield return new WaitForSeconds(duracionEsterilizacion);

        Instrument instrumento = itemEnCamara != null ? itemEnCamara.GetComponent<Instrument>() : null;
        if (instrumento != null) instrumento.Sterilize();

        lid.Open();
        yield return new WaitUntil(() => !lid.IsMoving);

        if (itemEnCamara != null)
        {
            lid.UnloadItem(itemEnCamara);
        }

        Level1Manager.Instance?.ShowFeedback(
            instrumento != null ? $"{instrumento.displayName} esterilizado" : "Ciclo terminado",
            "El instrumento ya está listo, tómalo de la cámara del autoclave.");

        itemEnCamara = null;
        cicloEnCurso = false;
    }
}
