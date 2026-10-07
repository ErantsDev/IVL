using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Zona (trigger) sobre la mesa de química: detecta cuándo un material queda reposando
/// ahí (soltado por el jugador, no sostenido) y avisa al Level1Manager para evaluarlo.
/// También sabe dónde está la cubierta de la mesa para que LabMaterial pueda acomodar
/// los objetos derechos y en un lugar libre al soltarlos (FindFreeSpot).
/// </summary>
public class PlacementZone : MonoBehaviour
{
    public static PlacementZone Instance { get; private set; }

    [Tooltip("Segundos mínimos entre avisos repetidos para el mismo objeto mientras siga ahí.")]
    public float renotifyCooldown = 2f;

    [Header("Mesa (para acomodar objetos al soltarlos)")]
    [Tooltip("Collider de la cubierta de la mesa de química (superficie donde se apoyan los objetos).")]
    public Collider countertop;
    [Tooltip("Zonas de la cubierta donde no se debe dejar nada (p. ej. la tarja).")]
    public Renderer[] keepOutAreas;
    [Tooltip("Margen extra alrededor de las zonas prohibidas (el hueco de la tarja es más ancho que la tarja).")]
    public float keepOutPadding = 0.05f;
    [Tooltip("Separación mínima con el borde de la mesa.")]
    public float edgeMargin = 0.03f;
    [Tooltip("Altura máxima sobre la mesa desde la que, al soltar, el objeto se acomoda sobre ella.")]
    public float maxDropHeight = 1.2f;

    readonly Dictionary<LabMaterial, float> lastCheck = new Dictionary<LabMaterial, float>();
    static readonly Collider[] overlapBuffer = new Collider[32];

    void Awake()
    {
        Instance = this;
        if (countertop == null)
        {
            var go = GameObject.Find("LabTable_Countertop");
            if (go != null) countertop = go.GetComponent<Collider>();
        }
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    void OnTriggerStay(Collider other)
    {
        var rb = other.attachedRigidbody;
        if (rb == null) return;

        var lm = rb.GetComponent<LabMaterial>();
        if (lm == null || lm.countedOnTable) return;

        if (!rb.useGravity) return; // el jugador lo sigue sosteniendo
        if (lm.IsSnapping) return;  // todavía se está acomodando sobre la mesa

#if UNITY_6000_0_OR_NEWER
        if (rb.linearVelocity.magnitude > 0.4f) return;
#else
        if (rb.velocity.magnitude > 0.4f) return;
#endif

        float now = Time.time;
        if (lastCheck.TryGetValue(lm, out float last) && now - last < renotifyCooldown) return;
        lastCheck[lm] = now;

        Level1Manager.Instance?.TryPlaceOnTable(lm);
    }

    void OnTriggerExit(Collider other)
    {
        var rb = other.attachedRigidbody;
        if (rb == null) return;
        var lm = rb.GetComponent<LabMaterial>();
        if (lm != null) lastCheck.Remove(lm);
    }

    // ---------- Acomodo sobre la mesa ----------

    public float TopY => countertop != null ? countertop.bounds.max.y : transform.position.y;

    /// <summary>¿El objeto se soltó encima de la mesa (dentro de su área y a una altura razonable)?</summary>
    public bool IsAboveTable(Bounds objectBounds)
    {
        if (countertop == null) return false;
        var tb = countertop.bounds;
        Vector3 c = objectBounds.center;
        const float slack = 0.05f;
        return c.x >= tb.min.x - slack && c.x <= tb.max.x + slack
            && c.z >= tb.min.z - slack && c.z <= tb.max.z + slack
            && objectBounds.min.y >= tb.max.y - 0.08f
            && objectBounds.min.y <= tb.max.y + maxDropHeight;
    }

    /// <summary>
    /// Busca, cerca de <paramref name="desiredCenter"/>, un punto de la cubierta donde quepa un
    /// objeto de medio-tamaño <paramref name="halfExtents"/> (caja alineada al mundo) sin chocar
    /// con otros objetos, sin salirse del borde y sin caer en la tarja. Devuelve el centro XZ
    /// y en Y la altura de la cubierta.
    /// </summary>
    public Vector3 FindFreeSpot(Vector3 desiredCenter, Vector3 halfExtents, Rigidbody self)
    {
        var tb = countertop.bounds;
        float top = tb.max.y;
        float minX = tb.min.x + halfExtents.x + edgeMargin, maxX = tb.max.x - halfExtents.x - edgeMargin;
        float minZ = tb.min.z + halfExtents.z + edgeMargin, maxZ = tb.max.z - halfExtents.z - edgeMargin;
        if (minX > maxX) minX = maxX = tb.center.x;
        if (minZ > maxZ) minZ = maxZ = tb.center.z;

        var start = new Vector2(Mathf.Clamp(desiredCenter.x, minX, maxX), Mathf.Clamp(desiredCenter.z, minZ, maxZ));
        const float step = 0.035f;
        const int maxRing = 20;

        for (int ring = 0; ring <= maxRing; ring++)
        {
            for (int i = -ring; i <= ring; i++)
            for (int j = -ring; j <= ring; j++)
            {
                if (Mathf.Max(Mathf.Abs(i), Mathf.Abs(j)) != ring) continue;
                var p = start + new Vector2(i, j) * step;
                if (p.x < minX || p.x > maxX || p.y < minZ || p.y > maxZ) continue;
                if (IsFree(p, halfExtents, top, self)) return new Vector3(p.x, top, p.y);
            }
        }
        return new Vector3(start.x, top, start.y);
    }

    bool IsFree(Vector2 p, Vector3 half, float top, Rigidbody self)
    {
        if (keepOutAreas != null)
        {
            foreach (var r in keepOutAreas)
            {
                if (r == null) continue;
                var kb = r.bounds;
                float pad = keepOutPadding;
                if (p.x + half.x > kb.min.x - pad && p.x - half.x < kb.max.x + pad &&
                    p.y + half.z > kb.min.z - pad && p.y - half.z < kb.max.z + pad)
                    return false;
            }
        }

        var center = new Vector3(p.x, top + half.y + 0.01f, p.y);
        const float clearance = 0.004f; // 4 mm de separación con los vecinos
        var h = new Vector3(half.x + clearance, half.y * 0.97f, half.z + clearance);
        int n = Physics.OverlapBoxNonAlloc(center, h, overlapBuffer, Quaternion.identity, ~0, QueryTriggerInteraction.Ignore);
        for (int k = 0; k < n; k++)
        {
            var c = overlapBuffer[k];
            if (c == countertop) continue;
            if (self != null && c.attachedRigidbody == self) continue;
            if (c is CharacterController) continue;
            return false;
        }
        return true;
    }
}
