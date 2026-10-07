using System.Collections;
using UnityEngine;

/// <summary>
/// Autoclave del laboratorio. El jugador apunta con un material en mano y presiona E
/// (ver ObjectGrabber) para meterlo. La tapa cierra, la rueda gira mientras "trabaja" un tiempo
/// (con aviso de cuenta regresiva), y al terminar la tapa se abre sola dejando el material
/// esterilizado: el jugador vuelve a apuntar al autoclave y presiona E para sacarlo a la mano.
/// Al meterlo, el material vuela desde la mano y entra por la boca (tapa abierta); al sacarlo,
/// sube por la boca del autoclave y vuelve volando a la mano del jugador.
///
/// Modo educativo: solo acepta los materiales correctos que necesitan esterilizarse (guía).
/// Modo evaluación: acepta cualquier material que quepa (no revela cuál necesitaba esterilizarse).
/// </summary>
public class Autoclave : MonoBehaviour
{
    [Header("Partes")]
    public Transform lid;
    public Transform wheel;
    public Transform loadPoint;

    [Header("Tiempos")]
    [Tooltip("Cuántos segundos tarda el ciclo de esterilizado (sin contar abrir/cerrar la tapa).")]
    public float sterilizeSeconds = 8f;
    public float lidMoveSeconds = 1f;
    public Vector3 lidClosedLocalEuler = Vector3.zero;
    public Vector3 lidOpenLocalEuler = new Vector3(-100f, 0f, 0f);
    public float lidWheelSpins = 720f;
    public float workingWheelSpeed = 90f;

    [Header("Animación de meter / sacar")]
    [Tooltip("Altura (m) sobre la boca del autoclave a la que sube el material al entrar o salir.")]
    public float mouthClearance = 0.16f;
    [Tooltip("Desplazamiento (mundo) de la boca respecto al punto de carga, hacia el frente, para no cruzar la tapa abierta.")]
    public Vector3 mouthOffset = new Vector3(0f, 0f, 0.13f);
    [Tooltip("Segundos para ir de la mano a la boca (y de la boca a la mano).")]
    public float flySeconds = 0.5f;
    [Tooltip("Segundos para bajar al interior (o subir desde él).")]
    public float chamberSeconds = 0.55f;
    [Tooltip("Distancia (m) frente a la cámara a la que llega el material al sacarlo.")]
    public float handDistance = 0.8f;

    [Header("Capacidad")]
    [Tooltip("Tamaño máximo (m) que puede tener un objeto para caber en la cámara del autoclave.")]
    public float maxItemSize = 0.3f;

    public bool IsBusy { get; private set; }

    /// <summary>Material que se está esterilizando ahora (o null).</summary>
    public Grabbable CurrentItem { get; private set; }

    /// <summary>Segundos que faltan del ciclo de esterilizado (0 si no está trabajando).</summary>
    public float RemainingSeconds { get; private set; }

    /// <summary>Material ya esterilizado, sentado en el autoclave, esperando a que el jugador lo saque.</summary>
    public Grabbable ReadyItem { get; private set; }
    public bool HasReadyItem => ReadyItem != null;

    /// <summary>true mientras un material sale del autoclave hacia la mano del jugador.</summary>
    public bool IsDelivering { get; private set; }

    Coroutine routine;
    // Postura que tenía el material en la mano (relativa a la cámara), para devolverlo igual.
    readonly System.Collections.Generic.Dictionary<Grabbable, Quaternion> handPose = new System.Collections.Generic.Dictionary<Grabbable, Quaternion>();

    void Start()
    {
        // Arranca "en reposo": tapa abierta, lista para recibir un material.
        if (lid != null) lid.localEulerAngles = lidOpenLocalEuler;
    }

    public bool CanLoad(Grabbable candidate)
    {
        if (IsBusy || HasReadyItem || IsDelivering || candidate == null) return false;
        var lm = candidate.GetComponent<LabMaterial>();
        if (lm == null || !lm.autoclavable || !Fits(candidate)) return false;

        var mode = Level1Manager.Instance != null ? Level1Manager.Instance.Mode : GameSession.Mode;
        if (mode == GameMode.Evaluacion) return true;
        return lm.isCorrectMaterial && lm.requiresSterilization && !lm.isSterilized;
    }

    /// <summary>Si el objeto en la mano nunca debe entrar al autoclave, el motivo (si no, null).</summary>
    public string RefuseReason(Grabbable held)
    {
        if (held == null || IsBusy || HasReadyItem || IsDelivering) return null;
        var lm = held.GetComponent<LabMaterial>();
        if (lm == null || lm.autoclavable) return null;
        return string.IsNullOrEmpty(lm.notAutoclavableReason)
            ? lm.DisplayName + " no se esteriliza en el autoclave."
            : lm.notAutoclavableReason;
    }

    /// <summary>El jugador intentó meter algo que no va al autoclave: se explica y (en evaluación) cuenta como error.</summary>
    public void Refuse(Grabbable held)
    {
        string reason = RefuseReason(held);
        if (reason == null) return;
        var mgr = Level1Manager.Instance;
        if (mgr != null)
        {
            var lm = held.GetComponent<LabMaterial>();
            if (!mgr.IsEducational && refusedOnce.Add(lm))
                mgr.RegisterMinigameError("Intentaste meter " + lm.DisplayName + " al autoclave.");
            mgr.ShowMessage(mgr.IsEducational ? reason : "Ese material no va al autoclave.");
        }
        else ObjectGrabber.MessageSink?.Invoke(reason);
    }

    readonly System.Collections.Generic.HashSet<LabMaterial> refusedOnce = new System.Collections.Generic.HashSet<LabMaterial>();

    bool Fits(Grabbable g)
    {
        Bounds b = new Bounds(g.transform.position, Vector3.zero);
        bool init = false;
        foreach (var c in g.GetComponentsInChildren<Collider>())
        {
            if (!c.enabled || c.isTrigger) continue;
            if (!init) { b = c.bounds; init = true; } else b.Encapsulate(c.bounds);
        }
        return !init || Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z)) <= maxItemSize;
    }

    public void BeginSterilize(Grabbable g)
    {
        if (!CanLoad(g)) return;
        if (routine != null) StopCoroutine(routine);
        routine = StartCoroutine(SterilizeRoutine(g));
    }

    /// <summary>
    /// Le entrega al jugador el material ya esterilizado que esperaba dentro (o null si no hay
    /// ninguno). Restaura su física/colliders para que quede como cualquier objeto sostenido
    /// normal; ObjectGrabber.Grab() se encarga de apagar la gravedad mientras se sostiene.
    /// </summary>
    /// <summary>
    /// Saca el material listo con animación: sube por la boca del autoclave y vuela a la mano del
    /// jugador; al llegar, <paramref name="grabber"/> lo toma (como si lo hubiera agarrado).
    /// </summary>
    public void DeliverReadyItem(ObjectGrabber grabber)
    {
        if (ReadyItem == null || IsDelivering || grabber == null) return;
        var g = ReadyItem;
        ReadyItem = null;
        StartCoroutine(DeliverRoutine(g, grabber));
    }

    IEnumerator DeliverRoutine(Grabbable g, ObjectGrabber grabber)
    {
        IsDelivering = true;
        var lm = g.GetComponent<LabMaterial>();
        string name = lm != null ? lm.DisplayName : g.DisplayName;
        grabber.SetReceiving(true, "Sacando " + name + " del autoclave...");

        g.transform.SetParent(null, true);
        Vector3 start = g.transform.position;
        Quaternion startRot = g.transform.rotation;
        Vector3 mouth = MouthPoint();

        // 1) Sube en vertical por la boca del autoclave (acelera y frena suave).
        float t = 0f;
        while (t < chamberSeconds)
        {
            t += Time.deltaTime;
            float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / chamberSeconds));
            g.transform.position = Vector3.Lerp(start, mouth, k);
            yield return null;
        }

        // 2) Vuela en arco hasta la mano (el destino sigue a la cámara aunque el jugador se mueva).
        Vector3 from = g.transform.position;
        Quaternion fromRot = g.transform.rotation;
        t = 0f;
        while (t < flySeconds)
        {
            t += Time.deltaTime;
            float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / flySeconds));
            Vector3 to = HandPoint(grabber);
            Vector3 mid = (from + to) * 0.5f + Vector3.up * 0.18f;
            Vector3 p = Vector3.Lerp(Vector3.Lerp(from, mid, k), Vector3.Lerp(mid, to, k), k);
            g.transform.SetPositionAndRotation(p, Quaternion.Slerp(fromRot, HandRotation(g, grabber, startRot), k));
            yield return null;
        }
        g.transform.SetPositionAndRotation(HandPoint(grabber), HandRotation(g, grabber, startRot));

        // 3) Vuelve a ser un objeto normal y queda en la mano.
        foreach (var col in g.GetComponentsInChildren<Collider>()) col.enabled = true;
        var rb = g.Body;
        rb.isKinematic = false;
        rb.useGravity = true;
#if UNITY_6000_0_OR_NEWER
        rb.linearVelocity = Vector3.zero;
#else
        rb.velocity = Vector3.zero;
#endif
        rb.angularVelocity = Vector3.zero;
        g.enabled = true;
        Physics.SyncTransforms();

        grabber.SetReceiving(false);
        IsDelivering = false;
        handPose.Remove(g);
        grabber.Grab(g);
    }

    /// <summary>Punto por encima de la boca del autoclave (centro de la cámara, arriba de la tapa).</summary>
    Vector3 MouthPoint()
    {
        Vector3 c = loadPoint != null ? loadPoint.position : transform.position;
        float top = c.y;
        var r = GetComponent<Renderer>();
        if (r != null) top = r.bounds.max.y;
        return new Vector3(c.x + mouthOffset.x, top + mouthClearance, c.z + mouthOffset.z);
    }

    Vector3 HandPoint(ObjectGrabber grabber)
    {
        var cam = grabber.Cam != null ? grabber.Cam.transform : grabber.transform;
        return cam.position + cam.forward * handDistance;
    }

    Quaternion HandRotation(Grabbable g, ObjectGrabber grabber, Quaternion fallback)
    {
        var cam = grabber.Cam != null ? grabber.Cam.transform : grabber.transform;
        return handPose.TryGetValue(g, out var rel) ? cam.rotation * rel : fallback;
    }

    public Grabbable TakeReadyItem()
    {
        var g = ReadyItem;
        if (g == null) return null;
        ReadyItem = null;

        g.transform.SetParent(null, true);
        foreach (var col in g.GetComponentsInChildren<Collider>()) col.enabled = true;
        var rb = g.Body;
        rb.isKinematic = false;
        rb.useGravity = true;
        g.enabled = true;

        return g;
    }

    IEnumerator SterilizeRoutine(Grabbable g)
    {
        IsBusy = true;
        CurrentItem = g;
        var lm = g.GetComponent<LabMaterial>();
        var rb = g.Body;

        rb.isKinematic = true;
        foreach (var col in g.GetComponentsInChildren<Collider>()) col.enabled = false;
        g.enabled = false;

        // Recuerda cómo lo llevaba en la mano (para devolverlo igual al sacarlo).
        var grabber = ObjectGrabber.Instance;
        if (grabber != null && grabber.Cam != null)
            handPose[g] = Quaternion.Inverse(grabber.Cam.transform.rotation) * g.transform.rotation;

        if (loadPoint != null)
        {
            // Vuela de la mano a la boca del autoclave y baja al interior por la tapa abierta.
            Vector3 from = g.transform.position;
            Quaternion fromRot = g.transform.rotation;
            Vector3 mouth = MouthPoint();
            // Dentro va de pie (su postura natural), con el frente hacia el salón.
            Quaternion inside = lm != null ? lm.RestRotation : loadPoint.rotation;
            float ft = 0f;
            while (ft < flySeconds)
            {
                ft += Time.deltaTime;
                float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(ft / flySeconds));
                Vector3 mid = (from + mouth) * 0.5f + Vector3.up * 0.18f;
                Vector3 p = Vector3.Lerp(Vector3.Lerp(from, mid, k), Vector3.Lerp(mid, mouth, k), k);
                g.transform.SetPositionAndRotation(p, Quaternion.Slerp(fromRot, inside, k));
                yield return null;
            }
            ft = 0f;
            while (ft < chamberSeconds)
            {
                ft += Time.deltaTime;
                float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(ft / chamberSeconds));
                g.transform.SetPositionAndRotation(Vector3.Lerp(mouth, loadPoint.position, k), inside);
                yield return null;
            }
            g.transform.SetParent(loadPoint, true);
            g.transform.SetPositionAndRotation(loadPoint.position, inside);
        }

        Level1Manager.Instance?.ShowMessage("Cerrando el autoclave con " + lm.DisplayName + "...");
        RemainingSeconds = sterilizeSeconds;
        yield return AnimateLid(true);

        float t = sterilizeSeconds;
        while (t > 0f)
        {
            RemainingSeconds = t;
            Level1Manager.Instance?.ShowAutoclaveTimer(Mathf.CeilToInt(t));
            if (wheel != null) wheel.Rotate(Vector3.up, workingWheelSpeed * Time.deltaTime, Space.Self);
            t -= Time.deltaTime;
            yield return null;
        }
        RemainingSeconds = 0f;

        lm.SetSterilized(true);
        Level1Manager.Instance?.HideAutoclaveTimer();

        yield return AnimateLid(false);

        // El objeto se queda dentro (sin física activa, colliders apagados) hasta que el
        // jugador lo retire con E; así no depende de que el rayo de apuntado lo "vea" a
        // través de la carcasa del autoclave (TakeReadyItem() lo reactiva al sacarlo).
        CurrentItem = null;
        ReadyItem = g;

        Level1Manager.Instance?.ShowMessage("Ciclo terminado: " + lm.DisplayName + " ya está estéril. Apunta al autoclave y presiona E para sacarlo.");

        IsBusy = false;
        routine = null;
    }

    IEnumerator AnimateLid(bool closing)
    {
        Vector3 from = closing ? lidOpenLocalEuler : lidClosedLocalEuler;
        Vector3 to = closing ? lidClosedLocalEuler : lidOpenLocalEuler;
        float elapsed = 0f;
        while (elapsed < lidMoveSeconds)
        {
            elapsed += Time.deltaTime;
            float p = Mathf.Clamp01(elapsed / lidMoveSeconds);
            if (lid != null) lid.localEulerAngles = Vector3.Lerp(from, to, p);
            if (wheel != null) wheel.Rotate(Vector3.up, (lidWheelSpins / lidMoveSeconds) * Time.deltaTime, Space.Self);
            yield return null;
        }
        if (lid != null) lid.localEulerAngles = to;
    }
}
