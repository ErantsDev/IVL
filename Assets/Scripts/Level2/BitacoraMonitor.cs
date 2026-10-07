using System.Text;
using TMPro;
using UnityEngine;

/// <summary>
/// Pantalla en la pared que muestra la bitácora de pesajes guardada en la base de datos SQLite
/// (se actualiza sola cada vez que se registra o reporta un conejo) y la lectura actual de la báscula.
/// </summary>
public class BitacoraMonitor : MonoBehaviour
{
    public TMP_Text bodyText;
    public TMP_Text footerText;
    public TMP_Text liveText;
    public ScaleDevice scale;
    public int rows = 7;

    void OnEnable()
    {
        LabDatabase.WeighingsChanged += Refresh;
        Refresh();
    }

    void OnDisable()
    {
        LabDatabase.WeighingsChanged -= Refresh;
    }

    void Update()
    {
        if (liveText == null || scale == null) return;
        string code = scale.Load != null ? scale.Load.Code : "—";
        liveText.text = "Báscula: <b>" + scale.Displayed.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) + " kg</b>   ·   Conejo: " + code +
                        (scale.Load != null ? (scale.IsStable ? "   ·   <color=#7CF0B0>ESTABLE</color>" : "   ·   <color=#FFD27A>PESANDO...</color>") : "");
    }

    public void Refresh()
    {
        if (bodyText == null) return;
        LabDatabase.EnsureInitialized();
        if (!LabDatabase.Available)
        {
            bodyText.text = "<color=#FF9C8C>Base de datos no disponible</color>\n" + LabDatabase.LastError;
            return;
        }
        var list = LabDatabase.RecentWeighings(rows);
        var sb = new StringBuilder();
        sb.Append("<color=#8FB8E8><b>#<pos=5%>FECHA<pos=28%>CONEJO<pos=47%>PESO<pos=62%>RANGO<pos=78%>DECISIÓN</b></color>\n");
        if (list.Count == 0) sb.Append("\n<color=#9AA7B8>Aún no hay pesajes registrados.</color>");
        foreach (var r in list)
        {
            string fecha = r.fecha != null && r.fecha.Length >= 16 ? r.fecha.Substring(8, 2) + "/" + r.fecha.Substring(5, 2) + " " + r.fecha.Substring(11, 5) : r.fecha;
            string rango = r.enRango ? "<color=#7CF0B0>2.5–4 OK</color>" : "<color=#FF9C8C>FUERA</color>";
            string dec = r.decision == "registrado" ? "<color=#7CF0B0>APTO</color>" : "<color=#FFB36B>NO APTO</color>";
            sb.Append(r.id).Append("<pos=5%>").Append(fecha).Append("<pos=28%>").Append(r.conejo)
              .Append("<pos=47%>").Append(r.pesoKg.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)).Append(" kg")
              .Append("<pos=62%>").Append(rango).Append("<pos=77%>").Append(dec).Append('\n');
        }
        bodyText.text = sb.ToString();
        if (footerText != null)
            footerText.text = "SQLite " + SqliteDatabase.Version + "  ·  " + LabDatabase.FileName + "  ·  " + LabDatabase.CountWeighings() + " pesaje(s) guardado(s)";
    }
}
