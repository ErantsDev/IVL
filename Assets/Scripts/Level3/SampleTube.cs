using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Tubo de ensayo del nivel 3. Su contenido se dibuja con dos capas de líquido generadas por código
/// (capa inferior: sangre / paquete de eritrocitos / suspensión; capa superior: solución salina o
/// sobrenadante) cuyo color y altura cambian en cada paso del lavado:
///   Sangre entera → (+ solución salina) sin mezclar → (agitar) suspensión → (centrifugar) separado
///   → (decantar) paquete celular → (+ solución salina) ... hasta que el sobrenadante queda transparente.
/// El eje del tubo es +Y local y su fondo exterior está en y = 0.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class SampleTube : MonoBehaviour
{
    public enum Content { WholeBlood, Unmixed, Suspension, Separated, Pellet, Saline }

    [Tooltip("Tubo de balance (sólo solución salina): equilibra el rotor.")]
    public bool isBalance;

    [Header("Interior del tubo (local, eje +Y)")]
    public float innerRadius = 0.0072f;
    public float innerBottom = 0.0016f;
    [Tooltip("Altura que ocupan 10 mL.")]
    public float heightPer10Ml = 0.125f;
    public int segments = 20;

    [Header("Volúmenes (mL)")]
    public float bloodMl = 3f;
    [Tooltip("Volumen del tubo de balance.")]
    public float balanceMl = 9f;
    [Tooltip("Hematocrito de la sangre del conejo (fracción de eritrocitos).")]
    public float hematocrit = 0.42f;
    public float minPelletHeight = 0.006f;

    [Header("Render")]
    public Material liquidMaterial;
    [Tooltip("Dónde queda el tubo cuando no está en un hueco (raíz del nivel).")]
    public Transform looseRoot;

    public static readonly Color BloodColor = new Color(0.36f, 0.015f, 0.03f, 1f);
    public static readonly Color PelletColor = new Color(0.30f, 0.01f, 0.025f, 1f);
    public static readonly Color SuspensionColor = new Color(0.62f, 0.05f, 0.07f, 0.96f);
    public static readonly Color SalineColor = new Color(0.86f, 0.93f, 1f, 0.16f);
    public static readonly Color PlasmaRedColor = new Color(0.70f, 0.13f, 0.05f, 0.93f);
    public static readonly Color PinkColor = new Color(0.92f, 0.42f, 0.40f, 0.62f);

    public Content State { get; private set; } = Content.WholeBlood;
    /// <summary>0 = capas separadas, 1 = homogéneo (sólo en el estado Unmixed).</summary>
    public float Mix { get; private set; }
    public int Spins { get; private set; }
    /// <summary>Qué tan coloreado está el sobrenadante (1 = plasma rojizo, 0 = transparente).</summary>
    public float SupernatantTint { get; private set; }
    public float BottomMl { get; private set; }
    public float TopMl { get; private set; }
    public float TotalMl => BottomMl + TopMl;
    public float CellsMl => bloodMl * hematocrit;

    Color bottomColor, topColor;
    Mesh bottomMesh, topMesh;
    MeshRenderer bottomR, topR;
    MaterialPropertyBlock mpb;
    Rigidbody body;
    Grabbable grabbable;
    Transform looseParent;
    readonly List<Collider> colliders = new List<Collider>();

    public Rigidbody Body => body != null ? body : (body = GetComponent<Rigidbody>());
    public Grabbable Grabbable => grabbable != null ? grabbable : (grabbable = GetComponent<Grabbable>());
    /// <summary>Dónde está acomodado (hueco de la gradilla o del rotor), o null si está suelto / en la mano.</summary>
    public Transform Dock { get; private set; }

    void Awake()
    {
        body = GetComponent<Rigidbody>();
        grabbable = GetComponent<Grabbable>();
        looseParent = looseRoot != null ? looseRoot : transform.parent;
        GetComponentsInChildren(true, colliders);
        EnsureVisuals();
        if (isBalance) SetSaline(balanceMl);
        else ResetBlood();
        if (grabbable != null) grabbable.Grabbed += OnGrabbed;
    }

    void OnDestroy()
    {
        if (grabbable != null) grabbable.Grabbed -= OnGrabbed;
        if (bottomMesh != null) Destroy(bottomMesh);
        if (topMesh != null) Destroy(topMesh);
    }

    void OnGrabbed()
    {
        // Al tomarlo de la gradilla deja de estar fijo: vuelve a la física para ir en la mano.
        Undock();
    }

    // ------------------------------------------------------------------
    //  Posición: gradilla / rotor / mano
    // ------------------------------------------------------------------

    /// <summary>Lo fija (cinemático) en un hueco. collidersOn = se puede apuntar/agarrar ahí.</summary>
    public void DockAt(Transform slot, bool collidersOn)
    {
        Dock = slot;
        Body.isKinematic = true;
        Body.interpolation = RigidbodyInterpolation.None;
        transform.SetParent(slot, false);
        transform.localPosition = Vector3.zero;
        transform.localRotation = Quaternion.identity;
        SetColliders(collidersOn);
    }

    public void Undock()
    {
        Dock = null;
        if (transform.parent != looseParent) transform.SetParent(looseParent, true);
        Body.isKinematic = false;
        SetColliders(true);
    }

    /// <summary>Para animaciones: cinemático, sin colisiones y fuera de cualquier hueco.</summary>
    public void Detach()
    {
        Dock = null;
        if (transform.parent != looseParent) transform.SetParent(looseParent, true);
        Body.isKinematic = true;
        Body.interpolation = RigidbodyInterpolation.None;
        SetColliders(false);
    }

    public void SetColliders(bool on)
    {
        foreach (var c in colliders) if (c != null) c.enabled = on;
    }

    /// <summary>Punto del borde superior (boca) del tubo, en mundo.</summary>
    public Vector3 MouthPoint => transform.TransformPoint(new Vector3(0f, LocalTop, 0f));
    public float LocalTop => innerBottom + heightPer10Ml * 1.15f;

    // ------------------------------------------------------------------
    //  Contenido
    // ------------------------------------------------------------------

    public void ResetBlood()
    {
        State = Content.WholeBlood;
        Spins = 0;
        Mix = 0f;
        SupernatantTint = 1f;
        BottomMl = bloodMl;
        TopMl = 0f;
        bottomColor = BloodColor;
        topColor = SalineColor;
        Refresh();
    }

    public void SetSaline(float ml)
    {
        State = Content.Saline;
        BottomMl = 0f;
        TopMl = ml;
        topColor = SalineColor;
        bottomColor = SalineColor;
        Refresh();
    }

    public bool CanReceiveSaline => !isBalance && (State == Content.WholeBlood || State == Content.Pellet);

    /// <summary>Agrega solución salina (progress 0→1 durante la animación del dispensado).</summary>
    public void PourSaline(float ml, float progress)
    {
        if (progress <= 0f) return;
        if (State == Content.WholeBlood || State == Content.Pellet)
        {
            State = Content.Unmixed;
            Mix = 0f;
            topColor = SalineColor;
            TopMl = 0f;
            pourTarget = ml;
        }
        if (State != Content.Unmixed) return;
        TopMl = pourTarget * Mathf.Clamp01(progress);
        Refresh();
    }
    float pourTarget;

    public bool NeedsMix => State == Content.Unmixed;

    /// <summary>Avanza la mezcla (inversión suave del tubo). Devuelve true al quedar homogéneo.</summary>
    public bool AddMix(float amount)
    {
        if (State != Content.Unmixed) return false;
        Mix = Mathf.Clamp01(Mix + amount);
        if (Mix >= 1f)
        {
            State = Content.Suspension;
            BottomMl = BottomMl + TopMl;
            TopMl = 0f;
            bottomColor = SuspensionColor;
            Refresh();
            return true;
        }
        Refresh();
        return false;
    }

    /// <summary>Tinte del sobrenadante tras cada centrifugación (1ª rojizo, 2ª rosado, 3ª transparente).</summary>
    public static float TintForSpin(int spinNumber)
    {
        switch (spinNumber)
        {
            case 1: return 1f;
            case 2: return 0.42f;
            default: return 0f;
        }
    }

    /// <summary>Resultado de centrifugar: los eritrocitos forman el paquete y queda el sobrenadante encima.</summary>
    public void Separate()
    {
        if (State != Content.Suspension) return;
        Spins++;
        SupernatantTint = TintForSpin(Spins);
        float total = TotalMl;
        BottomMl = CellsMl;
        TopMl = Mathf.Max(0f, total - CellsMl);
        bottomColor = PelletColor;
        topColor = SupernatantColor(SupernatantTint);
        State = Content.Separated;
        Refresh();
    }

    public static Color SupernatantColor(float tint)
    {
        if (tint <= 0.001f) return SalineColor;
        if (tint < 0.5f) return Color.Lerp(SalineColor, PinkColor, tint / 0.5f);
        return Color.Lerp(PinkColor, PlasmaRedColor, (tint - 0.5f) / 0.5f);
    }

    public string SupernatantDescription => SupernatantTint >= 0.7f ? "rojizo y turbio (plasma con hemoglobina libre)"
        : SupernatantTint > 0.01f ? "rosado claro (aún quedan restos de plasma)"
        : "totalmente transparente";

    float decantStartTop;
    bool pouring;

    /// <summary>Con el tubo inclinado boca abajo el sobrenadante se dibuja junto a la boca (el paquete queda pegado al fondo).</summary>
    public void SetPouring(bool on)
    {
        if (pouring == on) return;
        pouring = on;
        Refresh();
    }

    /// <summary>Decantado: el sobrenadante se tira y el paquete celular queda en el fondo.</summary>
    public void Decant(float progress)
    {
        if (State != Content.Separated) return;
        if (progress <= 0f) { decantStartTop = TopMl; return; }
        TopMl = decantStartTop * (1f - Mathf.Clamp01(progress));
        if (progress >= 1f)
        {
            TopMl = 0f;
            State = Content.Pellet;
        }
        Refresh();
    }

    public string Describe()
    {
        if (isBalance) return "Tubo de balance · " + TopMl.ToString("0.#") + " mL de solución salina";
        switch (State)
        {
            case Content.WholeBlood: return "Sangre con anticoagulante · " + BottomMl.ToString("0.#") + " mL";
            case Content.Unmixed: return "Eritrocitos + solución salina (sin mezclar)";
            case Content.Suspension: return "Suspensión de eritrocitos · " + TotalMl.ToString("0.#") + " mL (homogénea)";
            case Content.Separated: return "Centrifugado: paquete de eritrocitos + sobrenadante " + SupernatantDescription;
            case Content.Pellet: return "Paquete de eritrocitos (sobrenadante decantado)";
            default: return TopMl.ToString("0.#") + " mL de solución salina";
        }
    }

    // ------------------------------------------------------------------
    //  Dibujo de las capas
    // ------------------------------------------------------------------

    void EnsureVisuals()
    {
        if (mpb == null) mpb = new MaterialPropertyBlock();
        if (bottomR == null) bottomR = MakeLayer("Liquido_Inferior", out bottomMesh);
        if (topR == null) topR = MakeLayer("Liquido_Superior", out topMesh);
    }

    MeshRenderer MakeLayer(string name, out Mesh mesh)
    {
        var old = transform.Find(name);
        if (old != null) Destroy(old.gameObject);
        var go = new GameObject(name);
        go.transform.SetParent(transform, false);
        mesh = new Mesh { name = name };
        mesh.MarkDynamic();
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var r = go.AddComponent<MeshRenderer>();
        r.sharedMaterial = liquidMaterial;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.sortingOrder = 2; // después del vidrio: el reflejo blanco del vidrio no "lava" el color del líquido
        return r;
    }

    float HeightOf(float ml) => ml / 10f * heightPer10Ml;

    void Refresh()
    {
        EnsureVisuals();
        float y0 = innerBottom;
        float hb = BottomMl > 0.0001f ? Mathf.Max(HeightOf(BottomMl), State == Content.Separated || State == Content.Pellet || State == Content.Unmixed ? minPelletHeight : 0f) : 0f;
        float ht = TopMl > 0.0001f ? HeightOf(TopMl) : 0f;

        Color cb = bottomColor, ct = topColor;
        if (State == Content.Unmixed)
        {
            // Mientras se invierte el tubo, las dos capas se van tiñendo hacia el color de la suspensión.
            float m = Mathf.SmoothStep(0f, 1f, Mix);
            cb = Color.Lerp(bottomColor, SuspensionColor, m);
            ct = Color.Lerp(topColor, SuspensionColor, m * 0.95f);
        }

        BuildColumn(bottomMesh, y0, y0 + hb, true);
        bottomR.enabled = hb > 0.0001f;
        if (pouring)
        {
            float mouth = LocalTop - 0.004f;
            BuildColumn(topMesh, Mathf.Max(y0 + hb, mouth - ht), mouth, false);
        }
        else BuildColumn(topMesh, y0 + hb, y0 + hb + ht, hb <= 0.0001f);
        topR.enabled = ht > 0.0001f;

        mpb.SetColor("_BaseColor", cb);
        bottomR.SetPropertyBlock(mpb);
        mpb.SetColor("_BaseColor", ct);
        topR.SetPropertyBlock(mpb);
    }

    /// <summary>Columna cilíndrica entre y0 y y1; con fondo redondo si es la capa más baja.</summary>
    void BuildColumn(Mesh mesh, float y0, float y1, bool roundBottom)
    {
        mesh.Clear();
        if (y1 - y0 < 0.0001f) return;
        float r = innerRadius;
        var verts = new List<Vector3>();
        var tris = new List<int>();
        var rings = new List<(float y, float rad)>();
        if (roundBottom)
        {
            // Casquete semiesférico que sigue el fondo del tubo (sin salirse del vidrio).
            const int capRings = 5;
            for (int i = 0; i <= capRings; i++)
            {
                float a = Mathf.PI * 0.5f * i / capRings;            // 0 = punta, π/2 = ecuador
                float y = y0 + r - Mathf.Cos(a) * r;
                float rad = Mathf.Sin(a) * r;
                if (y > y1) { rings.Add((y1, Mathf.Sqrt(Mathf.Max(0f, r * r - (y0 + r - y1) * (y0 + r - y1))))); break; }
                rings.Add((y, Mathf.Max(rad, 0.0002f)));
            }
            if (rings[rings.Count - 1].y < y1) rings.Add((y1, r));
        }
        else
        {
            rings.Add((y0, r));
            rings.Add((y1, r));
        }

        int n = segments;
        foreach (var ring in rings)
            for (int s = 0; s <= n; s++)
            {
                float a = s * Mathf.PI * 2f / n;
                verts.Add(new Vector3(Mathf.Cos(a) * ring.rad, ring.y, Mathf.Sin(a) * ring.rad));
            }
        for (int k = 0; k < rings.Count - 1; k++)
            for (int s = 0; s < n; s++)
            {
                int a0 = k * (n + 1) + s, a1 = a0 + 1, b0 = a0 + n + 1, b1 = b0 + 1;
                tris.Add(a0); tris.Add(b0); tris.Add(a1);
                tris.Add(a1); tris.Add(b0); tris.Add(b1);
            }
        // Tapa superior (superficie del líquido).
        int top = verts.Count;
        verts.Add(new Vector3(0f, rings[rings.Count - 1].y, 0f));
        float tr = rings[rings.Count - 1].rad;
        for (int s = 0; s <= n; s++)
        {
            float a = s * Mathf.PI * 2f / n;
            verts.Add(new Vector3(Mathf.Cos(a) * tr, rings[rings.Count - 1].y, Mathf.Sin(a) * tr));
        }
        for (int s = 0; s < n; s++) { tris.Add(top); tris.Add(top + 2 + s); tris.Add(top + 1 + s); }
        // Tapa inferior (sólo si no es redonda).
        if (!roundBottom)
        {
            int bot = verts.Count;
            verts.Add(new Vector3(0f, y0, 0f));
            for (int s = 0; s <= n; s++)
            {
                float a = s * Mathf.PI * 2f / n;
                verts.Add(new Vector3(Mathf.Cos(a) * r, y0, Mathf.Sin(a) * r));
            }
            for (int s = 0; s < n; s++) { tris.Add(bot); tris.Add(bot + 1 + s); tris.Add(bot + 2 + s); }
        }
        mesh.SetVertices(verts);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
    }

    // ---------- Pruebas ----------
    public void TestSetState(Content c, int spins)
    {
        Spins = spins;
        switch (c)
        {
            case Content.Suspension: State = Content.Suspension; BottomMl = CellsMl + 4f; TopMl = 0; bottomColor = SuspensionColor; break;
            case Content.Pellet: State = Content.Pellet; BottomMl = CellsMl; TopMl = 0; bottomColor = PelletColor; break;
        }
        Refresh();
    }
}
