using TMPro;
using UnityEngine;

/// <summary>
/// Organizador de insumos estériles (jeringas y agujas desechables) del nivel 1. Aparece en un lugar
/// al azar (ver <see cref="MaterialSpawner"/>). Al apuntarlo y presionar E se abre el minijuego
/// de armado (<see cref="SyringeKitMinigame"/>): hay que elegir la jeringa y la aguja correctas
/// para trabajar con el conejo (5 mL + 21G × 1″).
/// </summary>
[RequireComponent(typeof(BoxCollider))]
public class SupplyOrganizer : Interactable
{
    public SyringeKitMinigame minigame;

    [Header("Materiales (modelo)")]
    public Material plasticMaterial;
    public Material labelMaterial;
    public Material barrelMaterial;
    public Material plungerMaterial;
    public Material rubberMaterial;
    public Material steelMaterial;
    public Material inkMaterial;
    public Material cardMaterial;
    public Material[] hubMaterials;

    [Header("Medidas (m)")]
    public Vector3 size = new Vector3(0.34f, 0.075f, 0.21f);
    public float wall = 0.005f;

    Level1Manager Level => Level1Manager.Instance;

    public override string PromptFor(Grabbable held, int action)
    {
        if (action != 0 || minigame == null || Level == null || !Level.IsRunning) return null;
        if (!Level.SyringeNeedsAssembly || held != null) return null;
        return "abrir el organizador de insumos (armar jeringa y aguja)";
    }

    public override void Interact(Grabbable held, int action)
    {
        if (minigame != null && !minigame.IsActive) minigame.Begin();
    }

    public override string InfoFor(Grabbable held)
    {
        if (Level == null || !Level.IsRunning) return null;
        if (!Level.SyringeNeedsAssembly)
            return Level.IsEducational ? "Ya armaste la " + SyringeCatalog.CorrectLabel + "." : "Ya armaste tu jeringa.";
        if (held != null) return "Deja lo que llevas en la mano para usar el organizador.";
        return "Jeringas y agujas hipodérmicas estériles, desechables.";
    }

#if UNITY_EDITOR
    /// <summary>Genera el modelo (caja con compartimentos, jeringas y agujas, etiqueta). Sólo en el editor.</summary>
    [ContextMenu("Construir modelo")]
    public void BuildModel()
    {
        for (int i = transform.childCount - 1; i >= 0; i--) DestroyImmediate(transform.GetChild(i).gameObject);

        var col = GetComponent<BoxCollider>();
        col.center = new Vector3(0f, size.y * 0.5f, 0f);
        col.size = size;

        var vis = new GameObject("Modelo").transform;
        vis.SetParent(transform, false);
        float w = size.x, h = size.y, d = size.z, t = wall;

        // Caja: fondo y paredes.
        Box(vis, "Fondo", new Vector3(0f, t * 0.5f, 0f), new Vector3(w, t, d), plasticMaterial);
        float hf = h * 0.62f; // el frente es más bajo para ver las agujas
        Box(vis, "Pared_Frente", new Vector3(0f, hf * 0.5f, d * 0.5f - t * 0.5f), new Vector3(w, hf, t), plasticMaterial);
        Box(vis, "Pared_Fondo", new Vector3(0f, h * 0.5f, -d * 0.5f + t * 0.5f), new Vector3(w, h, t), plasticMaterial);
        Box(vis, "Pared_Izq", new Vector3(-w * 0.5f + t * 0.5f, h * 0.5f, 0f), new Vector3(t, h, d), plasticMaterial);
        Box(vis, "Pared_Der", new Vector3(w * 0.5f - t * 0.5f, h * 0.5f, 0f), new Vector3(t, h, d), plasticMaterial);
        // Divisiones: una a lo largo (jeringas atrás, agujas adelante) y dos atrás.
        float splitZ = 0.03f;
        Box(vis, "Division_Larga", new Vector3(0f, h * 0.42f, splitZ), new Vector3(w - 2 * t, h * 0.84f, t * 0.8f), plasticMaterial);
        Box(vis, "Division_1", new Vector3(-w / 6f, h * 0.42f, (splitZ - d * 0.5f) * 0.5f), new Vector3(t * 0.8f, h * 0.84f, d * 0.5f + splitZ - t), plasticMaterial);
        Box(vis, "Division_2", new Vector3(w / 6f, h * 0.42f, (splitZ - d * 0.5f) * 0.5f), new Vector3(t * 0.8f, h * 0.84f, d * 0.5f + splitZ - t), plasticMaterial);

        var mats = new SyringeCatalog.Materials
        {
            barrel = barrelMaterial, plunger = plungerMaterial, rubber = rubberMaterial, steel = steelMaterial,
            ink = inkMaterial, card = cardMaterial, hubs = hubMaterials
        };

        // Jeringas acostadas (a lo ancho) en los tres compartimentos de atrás.
        string[] perCompartment = { "1", "3", "5" };
        for (int c = 0; c < 3; c++)
        {
            var spec = System.Array.Find(SyringeCatalog.Syringes, s => s.id == perCompartment[c]);
            float cx = -w / 3f + c * (w / 3f);
            float backZ = -d * 0.5f + t, frontZ = splitZ - t;
            int count = 3;
            for (int k = 0; k < count; k++)
            {
                var sy = SyringeCatalog.BuildSyringe(spec, mats, vis, out _, false);
                sy.transform.localRotation = Quaternion.Euler(0f, 90f + (k % 2 == 0 ? 0f : 180f), 0f);
                float z = Mathf.Lerp(backZ + 0.018f, frontZ - 0.016f, count == 1 ? 0.5f : k / (float)(count - 1));
                sy.transform.localPosition = new Vector3(cx + (k - 1) * 0.004f, t + 0.0005f + (k == 1 ? 0.004f : 0f), z);
                float scale = Mathf.Min(1f, (w / 3f - 2f * t - 0.01f) / (spec.barrelMm / 1000f + 0.045f));
                sy.transform.localScale = Vector3.one * scale;
            }
        }

        // Agujas en fila en el compartimento de adelante (empaques de pie, con la franja de color arriba).
        int n = SyringeCatalog.Needles.Length;
        for (int i = 0; i < n; i++)
        {
            float x = Mathf.Lerp(-w * 0.5f + 0.03f, w * 0.5f - 0.03f, i / (float)(n - 1));
            for (int k = 0; k < 2; k++)
            {
                var pack = SyringeCatalog.BuildNeedlePack(SyringeCatalog.Needles[i], i, mats, vis, out _);
                pack.transform.localRotation = Quaternion.Euler(-72f + k * 6f, 0f, 0f);
                pack.transform.localPosition = new Vector3(x + (k == 0 ? -0.004f : 0.004f), t + 0.004f, splitZ + 0.02f + k * 0.022f);
                pack.transform.localScale = new Vector3(0.9f, 1f, 0.8f);
            }
        }

        // Etiqueta al frente.
        Box(vis, "Etiqueta", new Vector3(0f, hf * 0.5f, d * 0.5f + 0.0008f), new Vector3(w * 0.8f, hf * 0.82f, 0.0012f), labelMaterial);
        var textGo = new GameObject("Texto_Etiqueta");
        textGo.transform.SetParent(vis, false);
        textGo.transform.localPosition = new Vector3(0f, hf * 0.5f, d * 0.5f + 0.0018f);
        textGo.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
        var tmp = textGo.AddComponent<TextMeshPro>();
        tmp.text = "<b>INSUMOS ESTÉRILES</b>\n<size=70%>JERINGAS · AGUJAS HIPODÉRMICAS</size>";
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = new Color(0.08f, 0.2f, 0.42f);
        tmp.fontSize = 0.14f;
        tmp.textWrappingMode = TextWrappingModes.NoWrap;
        tmp.rectTransform.sizeDelta = new Vector2(w * 0.76f, hf * 0.78f);
        tmp.enableAutoSizing = true;
        tmp.fontSizeMin = 0.05f;
        tmp.fontSizeMax = 0.2f;
        UnityEditor.EditorUtility.SetDirty(gameObject);
    }

    static GameObject Box(Transform parent, string name, Vector3 pos, Vector3 scale, Material mat)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        DestroyImmediate(go.GetComponent<Collider>());
        go.transform.SetParent(parent, false);
        go.transform.localPosition = pos;
        go.transform.localScale = scale;
        var r = go.GetComponent<Renderer>();
        if (mat != null) r.sharedMaterial = mat;
        return go;
    }
#endif
}
