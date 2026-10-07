using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Acomoda al azar (en cada partida del nivel 1) los materiales en lugares donde tiene sentido
/// guardarlos: la repisa bajo el pizarrón y las repisas del estante junto a la mesa.
///   - Cada objeto conserva su postura natural (de pie, acostado...) y sólo gira sobre el eje vertical.
///   - Sólo se pone en una superficie donde cabe completo: ancho/fondo de la repisa y altura libre
///     hasta la repisa de arriba (p. ej. el soporte con matraces sólo cabe arriba del estante).
///   - No choca con muebles, paredes, la báscula, el pizarrón ni con los demás objetos.
///   - Los objetos con <see cref="SpawnFacing"/> (p. ej. el organizador) quedan con su frente hacia el salón.
/// </summary>
public class MaterialSpawner : MonoBehaviour
{
    [Serializable]
    public class Surface
    {
        public string name;
        [Tooltip("Altura (Y) de la cara superior de la repisa.")]
        public float top;
        [Tooltip("Esquina mínima (X, Z) del área útil.")]
        public Vector2 minXZ;
        [Tooltip("Esquina máxima (X, Z) del área útil.")]
        public Vector2 maxXZ;
        [Tooltip("Hacia dónde queda el lado abierto (de donde se toman los objetos).")]
        public Vector3 openSide = Vector3.forward;
        [Tooltip("Peso relativo al sortear la superficie.")]
        public float weight = 1f;
    }

    public Surface[] surfaces =
    {
        new Surface { name = "repisa bajo el pizarrón", top = 0.875f, minXZ = new Vector2(2.712f, -1.27f), maxXZ = new Vector2(2.945f, 1.35f), openSide = Vector3.left, weight = 1.6f },
        new Surface { name = "estante junto a la mesa (repisa de arriba)", top = 1.086f, minXZ = new Vector2(-2.40f, -1.80f), maxXZ = new Vector2(-1.52f, -1.345f), openSide = Vector3.forward, weight = 0.8f },
        new Surface { name = "estante junto a la mesa (repisa de en medio)", top = 0.528f, minXZ = new Vector2(-2.40f, -1.80f), maxXZ = new Vector2(-1.52f, -1.345f), openSide = Vector3.forward, weight = 1f },
        new Surface { name = "estante junto a la mesa (repisa de abajo)", top = 0.046f, minXZ = new Vector2(-2.415f, -1.80f), maxXZ = new Vector2(-1.52f, -1.29f), openSide = Vector3.forward, weight = 0.8f },
    };

    [Tooltip("Separación mínima entre objetos (m).")]
    public float spacing = 0.018f;
    [Tooltip("Intentos por superficie antes de probar otra.")]
    public int attemptsPerSurface = 40;
    [Tooltip("Relación alto/ancho a partir de la cual un objeto (pipeta, tubo...) se guarda acostado: de pie se caería.")]
    public float slenderRatio = 3.5f;

    /// <summary>Superficie donde quedó cada objeto en el último sorteo (para pruebas y la guía).</summary>
    public readonly Dictionary<Transform, string> LastPlacement = new Dictionary<Transform, string>();

    class Entry
    {
        public Transform t;
        public Rigidbody body;
        public Collider[] cols;
        public Vector3 originalPos;
        public Quaternion originalRot;
        public Quaternion baseRot;
        public float footprint;
        public SpawnFacing facing;
    }

    static readonly Collider[] overlapBuffer = new Collider[64];

    /// <summary>Reacomoda al azar los objetos dados (los inactivos se ignoran).</summary>
    public void Randomize(IEnumerable<Transform> items)
    {
        LastPlacement.Clear();
        var entries = new List<Entry>();
        foreach (var t in items)
        {
            if (t == null || !t.gameObject.activeInHierarchy) continue;
            var cols = new List<Collider>();
            foreach (var c in t.GetComponentsInChildren<Collider>())
                if (c.enabled && !c.isTrigger) cols.Add(c);
            if (cols.Count == 0) continue;
            var e = new Entry
            {
                t = t,
                body = t.GetComponent<Rigidbody>(),
                cols = cols.ToArray(),
                originalPos = t.position,
                originalRot = t.rotation,
                facing = t.GetComponent<SpawnFacing>()
            };
            var b = BoundsOf(e.cols);
            e.baseRot = e.originalRot;
            // Lo alto y delgado (pipetas, tubos) se guarda acostado: de pie se volcaría en la repisa.
            var lab = t.GetComponent<LabMaterial>();
            bool slender = b.size.y / Mathf.Max(0.0001f, Mathf.Max(b.size.x, b.size.z)) >= slenderRatio;
            bool lie = lab != null && lab.tablePose != LabMaterial.TablePose.Auto ? lab.tablePose == LabMaterial.TablePose.LyingFlat : slender;
            if (e.facing == null && lie)
                e.baseRot = Quaternion.FromToRotation(Vector3.up, Vector3.right) * e.originalRot;
            e.footprint = b.size.x * b.size.z + b.size.y * 0.05f;
            entries.Add(e);
        }
        if (entries.Count == 0) return;

        // Primero se retiran todos (lejos) para que las posiciones viejas no estorben.
        for (int i = 0; i < entries.Count; i++) entries[i].t.position = ParkPosition(i);
        Physics.SyncTransforms();

        // Los más grandes primero (son los que tienen menos lugares posibles).
        entries.Sort((a, b) => b.footprint.CompareTo(a.footprint));

        for (int i = 0; i < entries.Count; i++)
        {
            var e = entries[i];
            if (!TryPlace(e, ParkPosition(i)))
            {
                e.t.SetPositionAndRotation(e.originalPos, e.originalRot);
                LastPlacement[e.t] = "(posición original)";
                Physics.SyncTransforms();
            }
            if (e.body != null && !e.body.isKinematic)
            {
                // Lo que va de pie (matraces, frascos) lleva el centro de masa bajo, como si tuviera líquido:
                // así no se vuelca solo en la repisa (p. ej. el matraz de fondo redondo) al rozarlo.
                if (e.baseRot == e.originalRot && e.facing == null)
                {
                    var cb = BoundsOf(e.cols);
                    float radius = 0.5f * Mathf.Min(cb.size.x, cb.size.z);
                    float comY = cb.min.y + Mathf.Min(cb.size.y * 0.28f, radius * 0.5f); // por debajo del centro de un fondo redondo
                    e.body.centerOfMass = e.body.transform.InverseTransformPoint(new Vector3(cb.center.x, comY, cb.center.z));
                }
                // Lo acostado es cilíndrico: con algo de freno angular no se queda rodando por la repisa.
#if UNITY_6000_0_OR_NEWER
                if (e.baseRot != e.originalRot) e.body.angularDamping = Mathf.Max(e.body.angularDamping, 5f);
#else
                if (e.baseRot != e.originalRot) e.body.angularDrag = Mathf.Max(e.body.angularDrag, 5f);
#endif
#if UNITY_6000_0_OR_NEWER
                e.body.linearVelocity = Vector3.zero;
#else
                e.body.velocity = Vector3.zero;
#endif
                e.body.angularVelocity = Vector3.zero;
            }
        }
        Physics.SyncTransforms();
    }

    static Vector3 ParkPosition(int i) => new Vector3(1000f + i * 4f, -500f, 1000f);

    bool TryPlace(Entry e, Vector3 park)
    {
        foreach (var s in ShuffledSurfaces())
        {
            for (int a = 0; a < attemptsPerSurface; a++)
            {
                float yaw;
                if (e.facing != null)
                {
                    Vector3 f = s.openSide; f.y = 0f;
                    yaw = Mathf.Atan2(f.x, f.z) * Mathf.Rad2Deg + UnityEngine.Random.Range(-e.facing.yawJitter, e.facing.yawJitter);
                }
                else if (e.baseRot != e.originalRot)
                {
                    // Acostado: el eje largo va a lo ANCHO de la repisa (hacia el fondo), así, si rueda,
                    // rueda a lo largo de la repisa y no hacia el borde.
                    bool shortIsX = (s.maxXZ.x - s.minXZ.x) < (s.maxXZ.y - s.minXZ.y);
                    yaw = (shortIsX ? 0f : 90f) + UnityEngine.Random.Range(-12f, 12f) + (UnityEngine.Random.value < 0.5f ? 180f : 0f);
                }
                else yaw = UnityEngine.Random.Range(0f, 360f);

                Quaternion rot = Quaternion.Euler(0f, yaw, 0f) * e.baseRot;
                e.t.SetPositionAndRotation(park, rot);
                Physics.SyncTransforms();
                Bounds b = BoundsOf(e.cols);
                Vector3 pivotFromCenter = e.t.position - b.center;
                Vector3 half = b.extents;

                float minX = s.minXZ.x + half.x, maxX = s.maxXZ.x - half.x;
                float minZ = s.minXZ.y + half.z, maxZ = s.maxXZ.y - half.z;
                if (minX > maxX || minZ > maxZ) continue; // con este giro no cabe: probar otro

                var center = new Vector3(UnityEngine.Random.Range(minX, maxX), s.top + half.y + 0.003f, UnityEngine.Random.Range(minZ, maxZ));
                if (!IsFree(center, half, e) || !IsSupported(center, half, s.top, e)) continue;

                e.t.SetPositionAndRotation(center + pivotFromCenter, rot);
                Physics.SyncTransforms();
                LastPlacement[e.t] = s.name;
                return true;
            }
        }
        return false;
    }

    IEnumerable<Surface> ShuffledSurfaces()
    {
        // Sorteo ponderado sin repetición.
        var pool = new List<Surface>(surfaces);
        while (pool.Count > 0)
        {
            float total = 0f;
            foreach (var s in pool) total += Mathf.Max(0.01f, s.weight);
            float r = UnityEngine.Random.value * total;
            int pick = pool.Count - 1;
            for (int i = 0; i < pool.Count; i++)
            {
                r -= Mathf.Max(0.01f, pool[i].weight);
                if (r <= 0f) { pick = i; break; }
            }
            var s2 = pool[pick];
            pool.RemoveAt(pick);
            yield return s2;
        }
    }

    bool IsFree(Vector3 center, Vector3 half, Entry e)
    {
        // Un poco más ancha (separación con los vecinos) y apenas despegada de la repisa.
        var h = new Vector3(half.x + spacing, Mathf.Max(0.001f, half.y - 0.002f), half.z + spacing);
        int n = Physics.OverlapBoxNonAlloc(center, h, overlapBuffer, Quaternion.identity, ~0, QueryTriggerInteraction.Ignore);
        for (int k = 0; k < n; k++)
        {
            var c = overlapBuffer[k];
            if (Array.IndexOf(e.cols, c) >= 0) continue;
            return false;
        }
        return true;
    }

    /// <summary>¿La repisa sostiene al objeto? (nada de dejarlo con medio cuerpo en el aire, al borde).</summary>
    bool IsSupported(Vector3 center, Vector3 half, float top, Entry e)
    {
        for (int i = 0; i < 5; i++)
        {
            Vector3 p = center;
            if (i > 0)
            {
                p.x += (i == 1 || i == 3 ? 1f : -1f) * half.x * 0.7f;
                p.z += (i == 1 || i == 2 ? 1f : -1f) * half.z * 0.7f;
            }
            var ray = new Ray(new Vector3(p.x, top + 0.02f, p.z), Vector3.down);
            bool hit = false;
            int n = Physics.RaycastNonAlloc(ray, rayBuffer, 0.05f, ~0, QueryTriggerInteraction.Ignore);
            for (int k = 0; k < n; k++)
                if (Array.IndexOf(e.cols, rayBuffer[k].collider) < 0) { hit = true; break; }
            if (!hit) return false;
        }
        return true;
    }

    static readonly RaycastHit[] rayBuffer = new RaycastHit[16];

    static Bounds BoundsOf(Collider[] cols)
    {
        Bounds b = cols[0].bounds;
        for (int i = 1; i < cols.Length; i++) b.Encapsulate(cols[i].bounds);
        return b;
    }

#if UNITY_EDITOR
    void OnDrawGizmosSelected()
    {
        if (surfaces == null) return;
        Gizmos.color = new Color(0.2f, 1f, 0.5f, 0.6f);
        foreach (var s in surfaces)
        {
            var c = new Vector3((s.minXZ.x + s.maxXZ.x) * 0.5f, s.top, (s.minXZ.y + s.maxXZ.y) * 0.5f);
            var size = new Vector3(s.maxXZ.x - s.minXZ.x, 0.005f, s.maxXZ.y - s.minXZ.y);
            Gizmos.DrawWireCube(c, size);
        }
    }
#endif
}
