using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

/// <summary>
/// Base de datos del laboratorio (SQLite, archivo "inmunologia.db" en Application.persistentDataPath).
/// Tablas:
///   conejos    (id, codigo, peso_real_kg, estado, creado)
///   pesajes    (id, fecha, conejo_codigo, peso_kg, en_rango, decision, modo, tarada)
///   resultados (id, fecha, nivel, modo, calificacion, tiempo_s, errores, detalle, matricula)
///   alumnos    (id, matricula, nombre, correo, sexo, creado, ultimo_acceso)
/// Los pesajes y resultados guardan la matrícula del alumno que jugó (StudentProfile).
/// Si SQLite no está disponible en la plataforma, el juego sigue funcionando sin guardar (Available = false).
/// </summary>
public static class LabDatabase
{
    public const string FileName = "inmunologia.db";

    public struct WeighingRecord
    {
        public long id;
        public string fecha;
        public string conejo;
        public double pesoKg;
        public bool enRango;
        public string decision;
        public string modo;
    }

    /// <summary>Se dispara cuando se agrega un pesaje (la bitácora se actualiza sola).</summary>
    public static event Action WeighingsChanged;

    public static string FilePath => System.IO.Path.Combine(Application.persistentDataPath, FileName);
    public static bool Available { get; private set; }
    public static string LastError { get; private set; }

    static bool initialized;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        initialized = false;
        Available = false;
        WeighingsChanged = null;
    }

    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    static string Now() => DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", Inv);

    public static void EnsureInitialized()
    {
        if (initialized) return;
        initialized = true;
        try
        {
            using (var db = Open())
            {
                db.Execute("PRAGMA journal_mode=WAL;");
                db.Execute(@"CREATE TABLE IF NOT EXISTS conejos (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    codigo TEXT NOT NULL UNIQUE,
                    peso_real_kg REAL NOT NULL,
                    estado TEXT NOT NULL DEFAULT 'en espera',
                    creado TEXT NOT NULL);");
                db.Execute(@"CREATE TABLE IF NOT EXISTS pesajes (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    fecha TEXT NOT NULL,
                    conejo_codigo TEXT NOT NULL,
                    peso_kg REAL NOT NULL,
                    en_rango INTEGER NOT NULL,
                    decision TEXT NOT NULL,
                    modo TEXT NOT NULL,
                    tarada INTEGER NOT NULL);");
                db.Execute(@"CREATE TABLE IF NOT EXISTS resultados (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    fecha TEXT NOT NULL,
                    nivel INTEGER NOT NULL,
                    modo TEXT NOT NULL,
                    calificacion REAL,
                    tiempo_s REAL NOT NULL,
                    errores INTEGER NOT NULL,
                    detalle TEXT);");
                db.Execute(@"CREATE TABLE IF NOT EXISTS alumnos (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    matricula TEXT NOT NULL UNIQUE,
                    nombre TEXT NOT NULL,
                    correo TEXT NOT NULL,
                    sexo TEXT NOT NULL,
                    creado TEXT NOT NULL,
                    ultimo_acceso TEXT NOT NULL);");
                // Bases creadas antes del registro de alumnos: se les agrega la columna.
                EnsureColumn(db, "resultados", "matricula", "TEXT");
                EnsureColumn(db, "pesajes", "matricula", "TEXT");
            }
            Available = true;
            Debug.Log("[LabDatabase] SQLite " + SqliteDatabase.Version + " listo en " + FilePath);
        }
        catch (Exception e)
        {
            Available = false;
            LastError = e.Message;
            Debug.LogWarning("[LabDatabase] Base de datos no disponible, se jugará sin guardar: " + e.Message);
        }
    }

    static SqliteDatabase Open() => new SqliteDatabase(FilePath);

    static void EnsureColumn(SqliteDatabase db, string table, string column, string type)
    {
        foreach (var r in db.Query("PRAGMA table_info(" + table + ");"))
            if (r.TryGetValue("name", out var n) && string.Equals(n as string, column, StringComparison.OrdinalIgnoreCase)) return;
        db.Execute("ALTER TABLE " + table + " ADD COLUMN " + column + " " + type + ";");
    }

    /// <summary>Matrícula del alumno actual (o null si no se registró).</summary>
    static object CurrentStudentId => string.IsNullOrEmpty(StudentProfile.Matricula) ? null : StudentProfile.Matricula;

    // ---------- Alumnos ----------

    /// <summary>Registra al alumno (o actualiza sus datos y su último acceso si ya existía).</summary>
    public static void UpsertStudent(string matricula, string nombre, string correo, string sexo)
    {
        if (string.IsNullOrEmpty(matricula)) return;
        Run(db =>
        {
            string now = Now();
            db.Execute("INSERT OR IGNORE INTO alumnos (matricula, nombre, correo, sexo, creado, ultimo_acceso) VALUES (?, ?, ?, ?, ?, ?);",
                matricula, nombre, correo, sexo, now, now);
            return db.Execute("UPDATE alumnos SET nombre = ?, correo = ?, sexo = ?, ultimo_acceso = ? WHERE matricula = ?;",
                nombre, correo, sexo, now, matricula);
        }, 0L);
    }

    static T Run<T>(Func<SqliteDatabase, T> action, T fallback)
    {
        EnsureInitialized();
        if (!Available) return fallback;
        try
        {
            using (var db = Open()) return action(db);
        }
        catch (Exception e)
        {
            LastError = e.Message;
            Debug.LogWarning("[LabDatabase] " + e.Message);
            return fallback;
        }
    }

    // ---------- Conejos ----------

    /// <summary>Genera el siguiente código de conejo (CNJ-0001, CNJ-0002, ...).</summary>
    public static string NextRabbitCode()
    {
        long next = Run(db => { var v = db.Scalar("SELECT IFNULL(MAX(id),0)+1 FROM conejos;"); return v is long l ? l : 1L; }, 0L);
        if (next <= 0) next = UnityEngine.Random.Range(100, 999);
        return "CNJ-" + next.ToString("0000", Inv);
    }

    public static void InsertRabbit(string codigo, double pesoRealKg)
    {
        Run(db => db.Execute("INSERT OR IGNORE INTO conejos (codigo, peso_real_kg, estado, creado) VALUES (?, ?, 'en espera', ?);",
            codigo, Math.Round(pesoRealKg, 3), Now()), 0L);
    }

    public static void SetRabbitState(string codigo, string estado)
    {
        Run(db => db.Execute("UPDATE conejos SET estado = ? WHERE codigo = ?;", estado, codigo), 0L);
    }

    // ---------- Pesajes ----------

    public static long InsertWeighing(string codigo, double pesoKg, bool enRango, string decision, GameMode modo, bool tarada)
    {
        long id = Run(db => db.Execute(
            "INSERT INTO pesajes (fecha, conejo_codigo, peso_kg, en_rango, decision, modo, tarada, matricula) VALUES (?, ?, ?, ?, ?, ?, ?, ?);",
            Now(), codigo, Math.Round(pesoKg, 2), enRango, decision, ModeName(modo), tarada, CurrentStudentId), -1L);
        WeighingsChanged?.Invoke();
        return id;
    }

    public static List<WeighingRecord> RecentWeighings(int count)
    {
        return Run(db =>
        {
            var list = new List<WeighingRecord>();
            foreach (var r in db.Query("SELECT id, fecha, conejo_codigo, peso_kg, en_rango, decision, modo FROM pesajes ORDER BY id DESC LIMIT ?;", count))
            {
                list.Add(new WeighingRecord
                {
                    id = (long)r["id"],
                    fecha = (string)r["fecha"],
                    conejo = (string)r["conejo_codigo"],
                    pesoKg = Convert.ToDouble(r["peso_kg"], Inv),
                    enRango = Convert.ToInt64(r["en_rango"], Inv) != 0,
                    decision = (string)r["decision"],
                    modo = (string)r["modo"],
                });
            }
            return list;
        }, new List<WeighingRecord>());
    }

    public static long CountWeighings() => Run(db => { var v = db.Scalar("SELECT COUNT(*) FROM pesajes;"); return v is long l ? l : 0L; }, 0L);

    // ---------- Resultados ----------

    public static long InsertResult(int nivel, GameMode modo, float? calificacion, float tiempoS, int errores, string detalle)
    {
        return Run(db => db.Execute(
            "INSERT INTO resultados (fecha, nivel, modo, calificacion, tiempo_s, errores, detalle, matricula) VALUES (?, ?, ?, ?, ?, ?, ?, ?);",
            Now(), nivel, ModeName(modo), calificacion.HasValue ? (object)Math.Round(calificacion.Value, 2) : null,
            Math.Round(tiempoS, 1), errores, detalle, CurrentStudentId), -1L);
    }

    public static string ModeName(GameMode m) => m == GameMode.Educativo ? "educativo" : "evaluacion";
}
