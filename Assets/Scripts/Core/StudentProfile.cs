using System.Text.RegularExpressions;
using UnityEngine;

/// <summary>Sexo elegido en el registro; decide qué avatar se le asigna al jugador.</summary>
public enum Sexo { SinElegir, Hombre, Mujer }

/// <summary>
/// Datos del alumno que se piden al presionar "Jugar" (antes de elegir el modo):
/// nombre, matrícula, correo institucional y sexo. Se recuerdan en PlayerPrefs para
/// rellenar el formulario la próxima vez y se guardan en la base de datos junto con
/// los resultados. Sobreviven a las recargas de escena de una misma partida.
/// </summary>
public static class StudentProfile
{
    const string KeyName = "alumno_nombre";
    const string KeyId = "alumno_matricula";
    const string KeyEmail = "alumno_correo";
    const string KeySex = "alumno_sexo";

    /// <summary>Dominio del correo institucional (se aceptan subdominios, p. ej. alumno.unach.mx).</summary>
    public const string InstitutionalDomain = "unach.mx";

    public static string Nombre { get; private set; } = "";
    public static string Matricula { get; private set; } = "";
    public static string Correo { get; private set; } = "";
    public static Sexo Sexo { get; private set; } = Sexo.SinElegir;

    /// <summary>true cuando el alumno confirmó sus datos en esta sesión de juego.</summary>
    public static bool Confirmed { get; private set; }

    /// <summary>Se dispara cuando cambian los datos (p. ej. para cambiar el avatar).</summary>
    public static event System.Action Changed;

    static bool loaded;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        loaded = false;
        Confirmed = false;
        Changed = null;
        Nombre = Matricula = Correo = "";
        Sexo = Sexo.SinElegir;
    }

    public static void EnsureLoaded()
    {
        if (loaded) return;
        loaded = true;
        Nombre = PlayerPrefs.GetString(KeyName, "");
        Matricula = PlayerPrefs.GetString(KeyId, "");
        Correo = PlayerPrefs.GetString(KeyEmail, "");
        int s = PlayerPrefs.GetInt(KeySex, 0);
        Sexo = s == 1 ? Sexo.Hombre : s == 2 ? Sexo.Mujer : Sexo.SinElegir;
    }

    /// <summary>Guarda los datos ya validados (ver Validate) y los registra en la base de datos.</summary>
    public static void Set(string nombre, string matricula, string correo, Sexo sexo)
    {
        EnsureLoaded();
        Nombre = NormalizeName(nombre);
        Matricula = NormalizeId(matricula);
        Correo = NormalizeEmail(correo);
        Sexo = sexo;
        Confirmed = true;

        PlayerPrefs.SetString(KeyName, Nombre);
        PlayerPrefs.SetString(KeyId, Matricula);
        PlayerPrefs.SetString(KeyEmail, Correo);
        PlayerPrefs.SetInt(KeySex, sexo == Sexo.Hombre ? 1 : sexo == Sexo.Mujer ? 2 : 0);
        PlayerPrefs.Save();

        LabDatabase.UpsertStudent(Matricula, Nombre, Correo, SexName(Sexo));
        Changed?.Invoke();
    }

    /// <summary>"Diego Pineda · A210123" (o vacío si no hay datos).</summary>
    public static string ShortLabel
    {
        get
        {
            EnsureLoaded();
            if (string.IsNullOrEmpty(Nombre)) return "";
            return string.IsNullOrEmpty(Matricula) ? Nombre : Nombre + "  ·  " + Matricula;
        }
    }

    public static string SexName(Sexo s) => s == Sexo.Hombre ? "hombre" : s == Sexo.Mujer ? "mujer" : "";

    // ---------------------------------------------------------------
    //  Normalización y validación (los mensajes se muestran en el formulario)
    // ---------------------------------------------------------------

    static readonly Regex Spaces = new Regex(@"\s+");
    static readonly Regex NameRx = new Regex(@"^[A-Za-zÁÉÍÓÚÜÑáéíóúüñ'.\- ]+$");
    static readonly Regex IdRx = new Regex(@"^[A-Z0-9\-]{5,12}$");
    static readonly Regex EmailRx = new Regex(@"^[a-z0-9._%+\-]+@[a-z0-9\-]+(\.[a-z0-9\-]+)+$");

    static readonly System.Globalization.CultureInfo Es = new System.Globalization.CultureInfo("es-MX");
    static readonly string[] Particles = { "de", "del", "la", "las", "los", "y" };

    /// <summary>Quita espacios de más y pone mayúscula inicial a cada palabra ("diego de la cruz" -> "Diego de la Cruz").</summary>
    public static string NormalizeName(string s)
    {
        s = Spaces.Replace((s ?? "").Trim(), " ");
        if (s.Length == 0) return s;
        var words = Es.TextInfo.ToTitleCase(s.ToLower(Es)).Split(' ');
        for (int i = 1; i < words.Length; i++)
            if (System.Array.IndexOf(Particles, words[i].ToLowerInvariant()) >= 0) words[i] = words[i].ToLowerInvariant();
        return string.Join(" ", words);
    }
    public static string NormalizeId(string s) => Spaces.Replace((s ?? "").Trim(), "").ToUpperInvariant();
    public static string NormalizeEmail(string s) => (s ?? "").Trim().ToLowerInvariant();

    public static string ValidateName(string s)
    {
        s = NormalizeName(s);
        if (s.Length == 0) return "Escribe tu nombre completo.";
        if (!NameRx.IsMatch(s)) return "El nombre sólo puede tener letras y espacios.";
        if (s.Length < 5 || s.Split(' ').Length < 2) return "Escribe tu nombre completo (nombre y apellido).";
        return null;
    }

    public static string ValidateId(string s)
    {
        s = NormalizeId(s);
        if (s.Length == 0) return "Escribe tu matrícula.";
        if (!IdRx.IsMatch(s)) return "La matrícula debe tener de 5 a 12 letras o números.";
        return null;
    }

    public static string ValidateEmail(string s)
    {
        s = NormalizeEmail(s);
        if (s.Length == 0) return "Escribe tu correo institucional.";
        if (!EmailRx.IsMatch(s)) return "El correo no es válido (ejemplo: nombre@" + InstitutionalDomain + ").";
        string domain = s.Substring(s.IndexOf('@') + 1);
        if (domain != InstitutionalDomain && !domain.EndsWith("." + InstitutionalDomain))
            return "Usa tu correo institucional (debe terminar en @" + InstitutionalDomain + ").";
        return null;
    }

    public static string ValidateSex(Sexo s) => s == Sexo.SinElegir ? "Elige tu sexo para asignarte un avatar." : null;
}
