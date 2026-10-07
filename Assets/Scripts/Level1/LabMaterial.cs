using System.Collections;
using UnityEngine;

/// <summary>
/// Información de Nivel 1 para un material de laboratorio: si es el material correcto
/// (vive dentro de Mat_N1) y si ya está esterilizado o necesita pasar por el autoclave.
/// Level1Manager asigna/reasigna estos valores al iniciar o reiniciar el nivel.
///
/// Además, cuando un material se suelta encima de la mesa de química, se acomoda
/// solo en una postura lógica según su forma: los objetos "de pie" (matraz, frasco, gradilla)
/// quedan derechos como estaban en el estante, y los largos y delgados (pipeta, jeringa)
/// quedan acostados a lo largo de la mesa, en un lugar libre y sin caer en la tarja.
/// </summary>
[RequireComponent(typeof(Grabbable))]
public class LabMaterial : MonoBehaviour
{
    public enum TablePose { Auto, Upright, LyingFlat }

    [Tooltip("true si este objeto vive dentro de Mat_N1 (material correcto para este nivel).")]
    public bool isCorrectMaterial;

    [Tooltip("true si este material necesita pasar por el autoclave antes de poder contar en la mesa.")]
    public bool requiresSterilization;

    [Tooltip("true si ya se puede usar: o no necesitaba esterilizarse, o ya salió del autoclave.")]
    public bool isSterilized = true;

    [Tooltip("false = nunca se mete al autoclave (p. ej. la solución salina: viene estéril y sellada de fábrica).")]
    public bool autoclavable = true;

    [Tooltip("Por qué no se mete al autoclave (se muestra al intentarlo).")]
    [TextArea(1, 3)] public string notAutoclavableReason;

    [Tooltip("true una vez que ya se contó correctamente sobre la mesa (para no volver a contarlo).")]
    public bool countedOnTable;

    [Tooltip("Nombre a mostrar en los avisos. Vacío = usa el nombre del Grabbable.")]
    public string displayLabel;

    [Header("Colocación en la mesa")]
    [Tooltip("Auto: de pie si es 'chaparro' (matraz, frasco), acostado si es largo y delgado (pipeta, jeringa).")]
    public TablePose tablePose = TablePose.Auto;
    [Tooltip("Relación alto/ancho a partir de la cual 'Auto' acuesta el objeto.")]
    public float slenderRatio = 3.5f;
    [Tooltip("Segundos que tarda en acomodarse sobre la mesa al soltarlo.")]
    public float snapSeconds = 0.35f;

    Grabbable grabbable;
    Quaternion restRotation;
    Vector3 restUpLocal = Vector3.up;
    float restHeight, restWidth;
    Coroutine snapRoutine;

    public bool IsSnapping => snapRoutine != null;

    /// <summary>Postura natural "de pie" del objeto (la que tenía al arrancar el nivel).</summary>
    public Quaternion RestRotation => restRotation;

    public string DisplayName => string.IsNullOrEmpty(displayLabel)
        ? (grabbable != null ? grabbable.DisplayName : gameObject.name)
        : displayLabel;

    void Awake()
    {
        grabbable = GetComponent<Grabbable>();
        CaptureRestPose();
    }

    void OnEnable()
    {
        if (grabbable == null) return;
        grabbable.Grabbed += HandleGrabbed;
        grabbable.Released += HandleReleased;
    }

    void OnDisable()
    {
        if (grabbable == null) return;
        grabbable.Grabbed -= HandleGrabbed;
        grabbable.Released -= HandleReleased;
    }

    /// <summary>
    /// La postura con la que el objeto está en la escena al arrancar el nivel se toma como su
    /// postura natural "de pie": el eje local que apunta más hacia arriba es su "arriba".
    /// </summary>
    void CaptureRestPose()
    {
        restRotation = transform.rotation;

        Vector3[] axes = { Vector3.right, Vector3.up, Vector3.forward, Vector3.left, Vector3.down, Vector3.back };
        float best = -2f;
        foreach (var a in axes)
        {
            float d = Vector3.Dot(transform.TransformDirection(a), Vector3.up);
            if (d > best) { best = d; restUpLocal = a; }
        }

        var b = ShapeBounds();
        restHeight = b.size.y;
        restWidth = Mathf.Max(b.size.x, b.size.z);
    }

    void HandleGrabbed()
    {
        CancelSnap();
        if (Level1Manager.Instance != null) Level1Manager.Instance.NotifyPickedUp(this);
    }

    void HandleReleased()
    {
        // Todos los materiales se acomodan igual al soltarlos en la mesa (así, en el modo
        // evaluación, la forma de acomodarse no delata cuál es el correcto).
        if (!isActiveAndEnabled) return;

        var zone = PlacementZone.Instance;
        if (zone == null || zone.countertop == null) return;
        if (!zone.IsAboveTable(ShapeBounds())) return;

        CancelSnap();
        snapRoutine = StartCoroutine(SnapToTable(zone));
    }

    void CancelSnap()
    {
        if (snapRoutine == null) return;
        StopCoroutine(snapRoutine);
        snapRoutine = null;
        foreach (var c in GetComponentsInChildren<Collider>()) c.enabled = true;
        Physics.SyncTransforms();
        if (grabbable != null && grabbable.Body != null) grabbable.Body.isKinematic = false;
    }

    bool ShouldLieFlat()
    {
        if (tablePose == TablePose.Upright) return false;
        if (tablePose == TablePose.LyingFlat) return true;
        return restWidth > 0.0001f && restHeight / restWidth >= slenderRatio;
    }

    Quaternion ComputeTableRotation()
    {
        if (!ShouldLieFlat())
        {
            // De pie: misma postura que en reposo, conservando hacia dónde "mira" ahora (solo giro en Y).
            Vector3 refLocal = Perpendicular(restUpLocal);
            Vector3 restH = Flat(restRotation * refLocal);
            Vector3 nowH = Flat(transform.rotation * refLocal);
            if (nowH.sqrMagnitude < 0.01f)
            {
                refLocal = Vector3.Cross(restUpLocal, refLocal);
                restH = Flat(restRotation * refLocal);
                nowH = Flat(transform.rotation * refLocal);
            }
            float yaw = (nowH.sqrMagnitude < 0.0001f || restH.sqrMagnitude < 0.0001f)
                ? 0f : Vector3.SignedAngle(restH, nowH, Vector3.up);
            return Quaternion.AngleAxis(yaw, Vector3.up) * restRotation;
        }

        // Acostado: el eje largo queda horizontal, paralelo al largo de la mesa (eje X del mundo),
        // apuntando hacia el mismo lado al que apuntaba al soltarlo.
        Vector3 longNow = transform.TransformDirection(restUpLocal);
        float side = Vector3.Dot(longNow, Vector3.right) >= 0f ? 1f : -1f;
        return Quaternion.FromToRotation(Vector3.up, Vector3.right * side) * restRotation;
    }

    IEnumerator SnapToTable(PlacementZone zone)
    {
        var rb = grabbable.Body;
        Bounds current = ShapeBounds();

        Vector3 startPos = transform.position;
        Quaternion startRot = transform.rotation;
        Quaternion targetRot = ComputeTableRotation();
        bool lying = ShouldLieFlat();

        // Mide el objeto ya girado en su postura final, con sus colliders (lo que usa la física).
        transform.rotation = targetRot;
        Physics.SyncTransforms();
        Bounds turned = ShapeBounds();
        Vector3 pivotToCenter = turned.center - transform.position;
        float pivotAboveBottom = transform.position.y - turned.min.y;
        Vector3 half = turned.extents;
        transform.rotation = startRot;
        Physics.SyncTransforms();

        Vector3 spot = zone.FindFreeSpot(current.center, half, rb);
        Vector3 targetPos = new Vector3(
            spot.x - pivotToCenter.x,
            spot.y + pivotAboveBottom + 0.001f,
            spot.z - pivotToCenter.z);

        // Mientras se acomoda no choca con nada (así no empuja a los objetos vecinos).
        rb.isKinematic = true;
        var cols = GetComponentsInChildren<Collider>();
        var wasEnabled = new bool[cols.Length];
        for (int i = 0; i < cols.Length; i++) { wasEnabled[i] = cols[i].enabled; cols[i].enabled = false; }

        // Trayectoria: se endereza mientras se coloca encima del lugar libre, y luego baja en vertical.
        Vector3 above = new Vector3(targetPos.x, Mathf.Max(startPos.y, targetPos.y + 0.12f), targetPos.z);
        float dur = Mathf.Max(0.01f, snapSeconds);
        float moveDur = dur * 0.6f, dropDur = dur - moveDur;
        float t = 0f;
        while (t < moveDur)
        {
            t += Time.deltaTime;
            float k = Mathf.SmoothStep(0f, 1f, t / moveDur);
            transform.SetPositionAndRotation(Vector3.Lerp(startPos, above, k), Quaternion.Slerp(startRot, targetRot, k));
            yield return null;
        }
        t = 0f;
        while (t < dropDur)
        {
            t += Time.deltaTime;
            float k = t / dropDur;
            transform.SetPositionAndRotation(Vector3.Lerp(above, targetPos, k * k), targetRot);
            yield return null;
        }

        transform.SetPositionAndRotation(targetPos, targetRot);
        for (int i = 0; i < cols.Length; i++) if (cols[i] != null) cols[i].enabled = wasEnabled[i];
        Physics.SyncTransforms();
        rb.isKinematic = false;
#if UNITY_6000_0_OR_NEWER
        rb.linearVelocity = Vector3.zero;
#else
        rb.velocity = Vector3.zero;
#endif
        rb.angularVelocity = Vector3.zero;

        // Los objetos acostados son cilíndricos: con algo de freno angular no se quedan rodando.
        if (lying)
        {
#if UNITY_6000_0_OR_NEWER
            rb.angularDamping = Mathf.Max(rb.angularDamping, 2f);
#else
            rb.angularDrag = Mathf.Max(rb.angularDrag, 2f);
#endif
        }

        // Queda en reposo exactamente donde se acomodó; despierta si el jugador lo toca o algo lo golpea.
        rb.Sleep();

        snapRoutine = null;
    }

    // ---------- Cinta testigo de esterilización ----------

    Renderer tapeRenderer;

    /// <summary>Marca el material como esterilizado (o no) y actualiza su cinta testigo.</summary>
    public void SetSterilized(bool value)
    {
        isSterilized = value;
        RefreshIndicator();
    }

    /// <summary>
    /// Cinta testigo de autoclave pegada al material: rayas claras = sin esterilizar,
    /// rayas oscuras = ya pasó por el autoclave (así se reconoce en el modo evaluación).
    /// </summary>
    public void RefreshIndicator()
    {
        var mgr = Level1Manager.Instance;
        if (mgr == null || mgr.tapeSterileMaterial == null || mgr.tapeUnsterileMaterial == null) return;
        if (tapeRenderer == null) tapeRenderer = CreateTape();
        if (tapeRenderer != null) tapeRenderer.sharedMaterial = isSterilized ? mgr.tapeSterileMaterial : mgr.tapeUnsterileMaterial;
    }

    Renderer CreateTape()
    {
        Physics.SyncTransforms();
        var b = ShapeBounds();
        if (b.size.sqrMagnitude < 1e-6f) return null;

        // Se pega del lado que mira hacia el centro del salón (lo que se ve desde el estante).
        Vector3 toRoom = new Vector3(-b.center.x, 0f, -b.center.z);
        Vector3 dir = toRoom.sqrMagnitude > 1e-4f ? toRoom.normalized : Vector3.forward;
        float y = b.min.y + b.size.y * 0.42f;
        float reach = b.extents.magnitude + 0.2f;
        var ray = new Ray(new Vector3(b.center.x, y, b.center.z) + dir * reach, -dir);

        RaycastHit best = default;
        bool found = false;
        foreach (var c in GetComponentsInChildren<Collider>())
        {
            if (!c.enabled || c.isTrigger) continue;
            if (c.Raycast(ray, out var hit, reach * 2f) && (!found || hit.distance < best.distance)) { best = hit; found = true; }
        }
        Vector3 point = found ? best.point : new Vector3(b.center.x, y, b.center.z) + Vector3.Scale(dir, b.extents);
        Vector3 normal = found ? best.normal : dir;
        normal.y = 0f;
        normal = normal.sqrMagnitude > 1e-4f ? normal.normalized : dir;

        float width = Mathf.Clamp(Mathf.Min(b.size.x, b.size.z) * 0.6f, 0.014f, 0.04f);
        float height = Mathf.Clamp(b.size.y * 0.16f, 0.01f, 0.022f);

        var tape = GameObject.CreatePrimitive(PrimitiveType.Cube);
        tape.name = "CintaTestigo";
        var tapeCol = tape.GetComponent<Collider>();
        if (tapeCol != null) DestroyImmediate(tapeCol);
        tape.transform.SetPositionAndRotation(point + normal * 0.0012f, Quaternion.LookRotation(-normal, Vector3.up));
        tape.transform.localScale = new Vector3(width, height, 0.0016f);
        tape.transform.SetParent(transform, true);
        var r = tape.GetComponent<Renderer>();
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        return r;
    }

    /// <summary>
    /// Caja (alineada al mundo) de la forma física del objeto: la unión de sus colliders.
    /// Si no tiene colliders activos (p. ej. dentro del autoclave), usa sus renderers.
    /// </summary>
    Bounds ShapeBounds()
    {
        Bounds b = new Bounds(transform.position, Vector3.zero);
        bool init = false;
        foreach (var c in GetComponentsInChildren<Collider>())
        {
            if (!c.enabled || c.isTrigger) continue;
            if (!init) { b = c.bounds; init = true; }
            else b.Encapsulate(c.bounds);
        }
        if (init) return b;

        foreach (var r in GetComponentsInChildren<Renderer>())
        {
            if (!r.enabled) continue;
            if (!init) { b = r.bounds; init = true; }
            else b.Encapsulate(r.bounds);
        }
        return b;
    }

    static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);

    static Vector3 Perpendicular(Vector3 v)
    {
        Vector3 other = Mathf.Abs(Vector3.Dot(v, Vector3.right)) < 0.9f ? Vector3.right : Vector3.up;
        return Vector3.Cross(v, other).normalized;
    }
}
