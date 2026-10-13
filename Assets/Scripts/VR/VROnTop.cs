using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

/// <summary>
/// Hace que la interfaz flotante de VR se dibuje siempre encima de la escena (paredes, mesa,
/// conejo) sin una segunda cámara: a cada elemento se le da una copia de su material con la
/// prueba de profundidad desactivada (ZTest Always) y cola de dibujo al final.
/// Apilar cámaras en las gafas obliga a URP a renderizar en una textura intermedia y copiarla
/// por ojo, lo que se notaba como lag en la PICO Neo 3.
/// </summary>
public static class VROnTop
{
    static readonly int ZTestId = Shader.PropertyToID("unity_GUIZTestMode");
    static readonly Dictionary<Material, Material> copies = new Dictionary<Material, Material>();
    static readonly HashSet<Material> copySet = new HashSet<Material>();
    static readonly List<Graphic> buffer = new List<Graphic>();
    static Material defaultUI;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void Reset()
    {
        copies.Clear();
        copySet.Clear();
        defaultUI = null;
    }

    /// <summary>Copia (en caché) de <paramref name="m"/> que se dibuja encima de todo.</summary>
    public static Material Copy(Material m)
    {
        if (m == null) return null;
        if (copySet.Contains(m)) return m;
        if (copies.TryGetValue(m, out var c) && c != null) return c;
        c = new Material(m) { name = m.name + " (VR encima)" };
        c.SetInt(ZTestId, (int)CompareFunction.Always);
        c.renderQueue = 4000;
        copies[m] = c;
        copySet.Add(c);
        return c;
    }

    /// <summary>Material de UI por defecto, encima de todo (para imágenes y mallas).</summary>
    public static Material DefaultUI
    {
        get
        {
            if (defaultUI == null) defaultUI = Copy(Canvas.GetDefaultCanvasMaterial());
            return defaultUI;
        }
    }

    /// <summary>Aplica el material "encima de todo" a los elementos de <paramref name="root"/> que aún no lo tengan.</summary>
    public static void Apply(Transform root)
    {
        root.GetComponentsInChildren(true, buffer);
        foreach (var g in buffer)
        {
            if (g == null) continue;
            if (g is TMP_Text t)
            {
                var src = t.fontSharedMaterial;
                if (src != null && !copySet.Contains(src)) t.fontSharedMaterial = Copy(src);
                continue;
            }
            var m = g.material; // si no tiene propio, devuelve el de por defecto
            if (m != null && copySet.Contains(m)) continue;
            g.material = m == null || m == g.defaultMaterial ? DefaultUI : Copy(m);
        }
    }
}
