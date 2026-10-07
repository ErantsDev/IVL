using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

/// <summary>
/// Envoltura mínima de SQLite (llama directamente a sqlite3.dll de Assets/Plugins/x86_64).
/// Uso: using (var db = new SqliteDatabase(ruta)) { db.Execute("...", p1, p2); var filas = db.Query("SELECT ..."); }
/// Los parámetros se enlazan en orden a los '?' de la consulta (null, string, int, long, float, double, bool).
/// </summary>
public sealed class SqliteDatabase : IDisposable
{
    const string Lib = "sqlite3";

    const int SQLITE_OK = 0;
    const int SQLITE_ROW = 100;
    const int SQLITE_DONE = 101;
    const int SQLITE_INTEGER = 1;
    const int SQLITE_FLOAT = 2;
    const int SQLITE_NULL = 5;
    const int OPEN_READWRITE = 0x00000002;
    const int OPEN_CREATE = 0x00000004;
    const int OPEN_FULLMUTEX = 0x00010000;
    static readonly IntPtr SQLITE_TRANSIENT = new IntPtr(-1);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] static extern int sqlite3_open_v2(byte[] filename, out IntPtr db, int flags, IntPtr vfs);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] static extern int sqlite3_close_v2(IntPtr db);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] static extern int sqlite3_prepare_v2(IntPtr db, byte[] sql, int nBytes, out IntPtr stmt, IntPtr tail);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] static extern int sqlite3_step(IntPtr stmt);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] static extern int sqlite3_finalize(IntPtr stmt);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] static extern int sqlite3_bind_text(IntPtr stmt, int index, byte[] value, int nBytes, IntPtr destructor);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] static extern int sqlite3_bind_double(IntPtr stmt, int index, double value);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] static extern int sqlite3_bind_int64(IntPtr stmt, int index, long value);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] static extern int sqlite3_bind_null(IntPtr stmt, int index);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] static extern int sqlite3_column_count(IntPtr stmt);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] static extern int sqlite3_column_type(IntPtr stmt, int col);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] static extern IntPtr sqlite3_column_name(IntPtr stmt, int col);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] static extern IntPtr sqlite3_column_text(IntPtr stmt, int col);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] static extern int sqlite3_column_bytes(IntPtr stmt, int col);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] static extern double sqlite3_column_double(IntPtr stmt, int col);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] static extern long sqlite3_column_int64(IntPtr stmt, int col);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] static extern IntPtr sqlite3_errmsg(IntPtr db);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] static extern long sqlite3_last_insert_rowid(IntPtr db);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] static extern IntPtr sqlite3_libversion();

    IntPtr handle;

    public string Path { get; }

    public static string Version => PtrToString(sqlite3_libversion());

    public SqliteDatabase(string path)
    {
        Path = path;
        int rc = sqlite3_open_v2(Utf8(path), out handle, OPEN_READWRITE | OPEN_CREATE | OPEN_FULLMUTEX, IntPtr.Zero);
        if (rc != SQLITE_OK)
        {
            string msg = handle != IntPtr.Zero ? PtrToString(sqlite3_errmsg(handle)) : "código " + rc;
            if (handle != IntPtr.Zero) sqlite3_close_v2(handle);
            handle = IntPtr.Zero;
            throw new Exception("No se pudo abrir la base de datos '" + path + "': " + msg);
        }
    }

    /// <summary>Ejecuta una sentencia sin resultados. Devuelve el id de la última fila insertada.</summary>
    public long Execute(string sql, params object[] args)
    {
        IntPtr stmt = Prepare(sql, args);
        try
        {
            int rc = sqlite3_step(stmt);
            if (rc != SQLITE_DONE && rc != SQLITE_ROW) throw Error("Execute");
            return sqlite3_last_insert_rowid(handle);
        }
        finally { sqlite3_finalize(stmt); }
    }

    /// <summary>Ejecuta una consulta y devuelve cada fila como diccionario columna -> valor (long, double, string o null).</summary>
    public List<Dictionary<string, object>> Query(string sql, params object[] args)
    {
        var rows = new List<Dictionary<string, object>>();
        IntPtr stmt = Prepare(sql, args);
        try
        {
            int cols = sqlite3_column_count(stmt);
            var names = new string[cols];
            for (int c = 0; c < cols; c++) names[c] = PtrToString(sqlite3_column_name(stmt, c));
            while (true)
            {
                int rc = sqlite3_step(stmt);
                if (rc == SQLITE_DONE) break;
                if (rc != SQLITE_ROW) throw Error("Query");
                var row = new Dictionary<string, object>(cols);
                for (int c = 0; c < cols; c++)
                {
                    switch (sqlite3_column_type(stmt, c))
                    {
                        case SQLITE_INTEGER: row[names[c]] = sqlite3_column_int64(stmt, c); break;
                        case SQLITE_FLOAT: row[names[c]] = sqlite3_column_double(stmt, c); break;
                        case SQLITE_NULL: row[names[c]] = null; break;
                        default: row[names[c]] = ColumnText(stmt, c); break;
                    }
                }
                rows.Add(row);
            }
        }
        finally { sqlite3_finalize(stmt); }
        return rows;
    }

    /// <summary>Devuelve el primer valor de la primera fila (o null).</summary>
    public object Scalar(string sql, params object[] args)
    {
        var rows = Query(sql, args);
        if (rows.Count == 0) return null;
        foreach (var v in rows[0].Values) return v;
        return null;
    }

    IntPtr Prepare(string sql, object[] args)
    {
        if (handle == IntPtr.Zero) throw new ObjectDisposedException("SqliteDatabase");
        byte[] bytes = Utf8(sql);
        if (sqlite3_prepare_v2(handle, bytes, bytes.Length, out IntPtr stmt, IntPtr.Zero) != SQLITE_OK) throw Error("Prepare: " + sql);
        if (args != null)
        {
            for (int i = 0; i < args.Length; i++)
            {
                int idx = i + 1;
                object a = args[i];
                int rc;
                switch (a)
                {
                    case null: rc = sqlite3_bind_null(stmt, idx); break;
                    case string s: { var b = Encoding.UTF8.GetBytes(s); rc = sqlite3_bind_text(stmt, idx, b, b.Length, SQLITE_TRANSIENT); break; }
                    case bool bo: rc = sqlite3_bind_int64(stmt, idx, bo ? 1 : 0); break;
                    case int n: rc = sqlite3_bind_int64(stmt, idx, n); break;
                    case long l: rc = sqlite3_bind_int64(stmt, idx, l); break;
                    case float f: rc = sqlite3_bind_double(stmt, idx, f); break;
                    case double d: rc = sqlite3_bind_double(stmt, idx, d); break;
                    default: { var b = Encoding.UTF8.GetBytes(a.ToString()); rc = sqlite3_bind_text(stmt, idx, b, b.Length, SQLITE_TRANSIENT); break; }
                }
                if (rc != SQLITE_OK) { sqlite3_finalize(stmt); throw Error("Bind #" + idx); }
            }
        }
        return stmt;
    }

    static string ColumnText(IntPtr stmt, int col)
    {
        IntPtr p = sqlite3_column_text(stmt, col);
        if (p == IntPtr.Zero) return null;
        int n = sqlite3_column_bytes(stmt, col);
        var buf = new byte[n];
        Marshal.Copy(p, buf, 0, n);
        return Encoding.UTF8.GetString(buf);
    }

    Exception Error(string where) => new Exception("SQLite (" + where + "): " + PtrToString(sqlite3_errmsg(handle)));

    static byte[] Utf8(string s)
    {
        var b = Encoding.UTF8.GetBytes(s);
        var z = new byte[b.Length + 1];
        Buffer.BlockCopy(b, 0, z, 0, b.Length);
        return z;
    }

    static string PtrToString(IntPtr p)
    {
        if (p == IntPtr.Zero) return null;
        int len = 0;
        while (Marshal.ReadByte(p, len) != 0) len++;
        var buf = new byte[len];
        Marshal.Copy(p, buf, 0, len);
        return Encoding.UTF8.GetString(buf);
    }

    public void Dispose()
    {
        if (handle != IntPtr.Zero)
        {
            sqlite3_close_v2(handle);
            handle = IntPtr.Zero;
        }
    }
}
