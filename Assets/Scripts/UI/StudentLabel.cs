using TMPro;
using UnityEngine;

/// <summary>Muestra los datos del alumno registrado en un texto (HUD, resultados, menú).</summary>
[RequireComponent(typeof(TMP_Text))]
public class StudentLabel : MonoBehaviour
{
    [Tooltip("{0} = nombre, {1} = matrícula, {2} = correo.")]
    public string format = "{0}  ·  {1}";
    [Tooltip("Texto si todavía no hay datos (vacío = ocultar).")]
    public string emptyText = "";

    TMP_Text text;

    void OnEnable()
    {
        StudentProfile.Changed += Refresh;
        Refresh();
    }

    void OnDisable() => StudentProfile.Changed -= Refresh;

    public void Refresh()
    {
        if (text == null) text = GetComponent<TMP_Text>();
        StudentProfile.EnsureLoaded();
        bool has = !string.IsNullOrEmpty(StudentProfile.Nombre);
        text.text = has ? string.Format(format, StudentProfile.Nombre, StudentProfile.Matricula, StudentProfile.Correo) : emptyText;
        text.enabled = has || !string.IsNullOrEmpty(emptyText);
    }
}
