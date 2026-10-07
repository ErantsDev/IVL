using System.Text;
using TMPro;
using UnityEngine;

/// <summary>
/// Guía del MODO EDUCATIVO (se apaga sola en el modo evaluación):
///   - Lista de materiales a preparar con su estado (pendiente / necesita autoclave / en autoclave / en la mesa).
///   - Objetivo paso a paso según lo que esté haciendo el jugador.
///   - Marcador flotante sobre el siguiente objetivo (material, autoclave o mesa).
///   - Pista al apuntar un objeto: si está o no en la lista.
/// </summary>
public class EducationalGuide : MonoBehaviour
{
    [Header("Referencias")]
    public Level1Manager level;
    public Autoclave autoclave;
    public PlacementZone table;

    [Header("UI")]
    public GameObject checklistPanel;
    public TMP_Text checklistText;
    public GameObject objectivePanel;
    public TMP_Text objectiveText;

    [Header("Marcador")]
    public Material markerMaterial;
    public float markerHeight = 0.14f;

    const string Green = "#5BE39A";
    const string Orange = "#FFB35C";
    const string Yellow = "#FFE27A";
    const string Gray = "#B8C4D6";

    Transform marker;
    Transform markerGem;
    readonly StringBuilder sb = new StringBuilder(512);

    void Awake()
    {
        if (level == null) level = GetComponent<Level1Manager>();
        if (autoclave == null) autoclave = FindAnyObjectByType<Autoclave>();
        if (table == null) table = FindAnyObjectByType<PlacementZone>();
        CreateMarker();
        SetVisible(false);
    }

    void OnDisable()
    {
        if (ObjectGrabber.AimHint == (System.Func<Grabbable, string>)AimHint) ObjectGrabber.AimHint = null;
        SetVisible(false);
    }

    void Update()
    {
        bool active = level != null && level.IsRunning && level.IsEducational;
        SetVisible(active);
        if (!active)
        {
            if (ObjectGrabber.AimHint == (System.Func<Grabbable, string>)AimHint) ObjectGrabber.AimHint = null;
            return;
        }

        ObjectGrabber.AimHint = AimHint;
        UpdateChecklist();
        Transform target = UpdateObjective();
        UpdateMarker(target);
    }

    // ---------- Lista de materiales ----------

    void UpdateChecklist()
    {
        if (checklistText == null) return;
        sb.Clear();
        sb.Append("<b>MATERIALES A PREPARAR</b>  <color=").Append(Gray).Append('>')
          .Append(level.CountedSoFar).Append('/').Append(level.CountedTotal).Append("</color>\n");

        foreach (var lm in level.CorrectMaterials)
        {
            if (lm == null) continue;
            sb.Append('\n');
            if (lm.countedOnTable)
                sb.Append("<color=").Append(Green).Append(">● ").Append(lm.DisplayName).Append("</color>  <size=80%><color=").Append(Green).Append(">en la mesa</color></size>");
            else if (lm == level.SyringeMaterial && level.SyringeNeedsAssembly)
                sb.Append("○ ").Append(lm.DisplayName).Append("  <size=80%><color=").Append(Orange).Append(">ármala en el organizador</color></size>");
            else if (IsInAutoclave(lm))
                sb.Append("<color=").Append(Yellow).Append(">○ ").Append(lm.DisplayName).Append("</color>  <size=80%><color=").Append(Yellow).Append(">en el autoclave</color></size>");
            else if (lm.requiresSterilization && !lm.isSterilized)
                sb.Append("○ ").Append(lm.DisplayName).Append("  <size=80%><color=").Append(Orange).Append(">necesita autoclave</color></size>");
            else
                sb.Append("○ ").Append(lm.DisplayName).Append("  <size=80%><color=").Append(Gray).Append(">estéril, va a la mesa</color></size>");
        }

        sb.Append("\n\n<size=78%><color=").Append(Gray).Append(">Cinta testigo: rayas <b>claras</b> = sin esterilizar, rayas <b>oscuras</b> = estéril.</color></size>");
        checklistText.text = sb.ToString();
    }

    bool IsInAutoclave(LabMaterial lm)
    {
        if (autoclave == null) return false;
        var g = lm.GetComponent<Grabbable>();
        return g != null && (autoclave.CurrentItem == g || autoclave.ReadyItem == g);
    }

    // ---------- Objetivo paso a paso ----------

    /// <summary>Actualiza el texto del objetivo y devuelve a qué debe apuntar el marcador.</summary>
    Transform UpdateObjective()
    {
        var held = ObjectGrabber.Instance != null ? ObjectGrabber.Instance.Held : null;
        var heldLm = held != null ? held.GetComponent<LabMaterial>() : null;
        string step = "Paso " + Mathf.Min(level.CountedSoFar + 1, level.CountedTotal) + " de " + level.CountedTotal;
        string text;
        Transform target = null;

        if (autoclave != null && autoclave.HasReadyItem && held == null)
        {
            text = "Saca <b>" + Name(autoclave.ReadyItem) + "</b> del autoclave: apunta al autoclave y presiona <b>E</b>.";
            target = autoclave.transform;
        }
        else if (heldLm != null && !heldLm.isCorrectMaterial)
        {
            text = "<color=" + Orange + ">" + heldLm.DisplayName + " no está en tu lista.</color> Suelta el material (<b>E</b>) y busca los materiales de la lista.";
        }
        else if (heldLm != null && heldLm.requiresSterilization && !heldLm.isSterilized)
        {
            if (autoclave != null && (autoclave.IsBusy || autoclave.HasReadyItem))
                text = "El autoclave está ocupado. Deja <b>" + heldLm.DisplayName + "</b> y espera a que termine el ciclo.";
            else
                text = "Lleva <b>" + heldLm.DisplayName + "</b> al <b>autoclave</b>, apunta hacia él y presiona <b>E</b> para esterilizar el material.";
            target = autoclave != null ? autoclave.transform : null;
        }
        else if (heldLm != null)
        {
            text = "Lleva <b>" + heldLm.DisplayName + "</b> a la <b>mesa de química</b> y suelta el material encima (<b>E</b>).";
            target = table != null ? table.transform : null;
        }
        else
        {
            var next = NextPending();
            if (next != null && next == level.SyringeMaterial && level.SyringeNeedsAssembly)
            {
                text = "Arma la <b>jeringa de 5 mL con aguja 21G × 1″</b>: busca el <b>organizador de insumos</b>, apúntalo y presiona <b>E</b>.";
                target = level.organizer != null ? level.organizer.transform : null;
            }
            else if (next != null)
            {
                string extra = next.requiresSterilization && !next.isSterilized
                    ? " <color=" + Orange + ">(no está estéril: necesitará autoclave)</color>"
                    : " <color=" + Gray + ">(ya está estéril)</color>";
                text = "Busca y toma: <b>" + next.DisplayName + "</b>" + extra + ".";
                target = next.transform;
            }
            else if (autoclave != null && autoclave.IsBusy)
            {
                text = "Espera a que el autoclave termine de esterilizar <b>" + Name(autoclave.CurrentItem) + "</b> (" + Mathf.CeilToInt(autoclave.RemainingSeconds) + " s).";
                target = autoclave.transform;
            }
            else
            {
                text = "Coloca los materiales restantes en la mesa de química.";
                target = table != null ? table.transform : null;
            }
        }

        if (objectiveText != null)
            objectiveText.text = "<size=75%><color=" + Gray + ">OBJETIVO · " + step + "</color></size>\n" + text;
        return target;
    }

    LabMaterial NextPending()
    {
        foreach (var lm in level.CorrectMaterials)
        {
            if (lm == null || lm.countedOnTable || IsInAutoclave(lm)) continue;
            return lm;
        }
        return null;
    }

    static string Name(Grabbable g)
    {
        if (g == null) return "el material";
        var lm = g.GetComponent<LabMaterial>();
        return lm != null ? lm.DisplayName : g.DisplayName;
    }

    string AimHint(Grabbable g)
    {
        var lm = g != null ? g.GetComponent<LabMaterial>() : null;
        if (lm == null) return null;
        if (!lm.isCorrectMaterial) return "<color=#FF7B72>(no está en tu lista)</color>";
        if (lm.countedOnTable) return "<color=" + Green + ">(ya está en la mesa)</color>";
        if (lm.requiresSterilization && !lm.isSterilized) return "<color=" + Orange + ">(en tu lista · necesita autoclave)</color>";
        return "<color=" + Green + ">(en tu lista)</color>";
    }

    // ---------- Marcador ----------

    void CreateMarker()
    {
        marker = new GameObject("MarcadorGuia").transform;
        markerGem = GameObject.CreatePrimitive(PrimitiveType.Cube).transform;
        markerGem.name = "Gema";
        var col = markerGem.GetComponent<Collider>();
        if (col != null) Destroy(col);
        markerGem.SetParent(marker, false);
        markerGem.localScale = new Vector3(0.045f, 0.045f, 0.045f);
        markerGem.localRotation = Quaternion.Euler(45f, 0f, 45f);
        var r = markerGem.GetComponent<Renderer>();
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        if (markerMaterial != null) r.sharedMaterial = markerMaterial;
        marker.localScale = new Vector3(1f, 1.7f, 1f); // rombo alargado
    }

    void UpdateMarker(Transform target)
    {
        if (marker == null) return;
        if (target == null) { marker.gameObject.SetActive(false); return; }

        var b = BoundsOf(target);
        marker.gameObject.SetActive(true);
        float bob = Mathf.Sin(Time.unscaledTime * 3f) * 0.025f;
        marker.position = new Vector3(b.center.x, b.max.y + markerHeight + bob, b.center.z);
        marker.rotation = Quaternion.Euler(0f, Time.unscaledTime * 90f, 0f);
    }

    static Bounds BoundsOf(Transform t)
    {
        var colliders = t.GetComponentsInChildren<Collider>();
        Bounds b = new Bounds(t.position, Vector3.zero);
        bool init = false;
        foreach (var c in colliders)
        {
            if (!c.enabled) continue;
            if (!init) { b = c.bounds; init = true; } else b.Encapsulate(c.bounds);
        }
        if (!init)
            foreach (var r in t.GetComponentsInChildren<Renderer>())
            {
                if (!init) { b = r.bounds; init = true; } else b.Encapsulate(r.bounds);
            }
        return b;
    }

    void SetVisible(bool visible)
    {
        if (checklistPanel != null && checklistPanel.activeSelf != visible) checklistPanel.SetActive(visible);
        if (objectivePanel != null && objectivePanel.activeSelf != visible) objectivePanel.SetActive(visible);
        if (!visible && marker != null) marker.gameObject.SetActive(false);
    }
}
