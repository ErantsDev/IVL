using System;
using UnityEngine;

/// <summary>
/// Jeringas y agujas hipodérmicas reales (medidas aproximadas de jeringas Luer-Lock y colores del
/// cono de la aguja según la norma ISO 6009) para el minijuego de armado del nivel 1, y funciones
/// para construir sus modelos 3D con primitivas.
/// Para la práctica con el conejo lo correcto es: jeringa de 5 mL + aguja 21G × 1″ (cono verde).
/// </summary>
public static class SyringeCatalog
{
    [Serializable]
    public struct SyringeSpec
    {
        public string id;
        public float mL;
        public string label;          // "5 mL"
        public string kind;           // "Jeringa hipodérmica Luer-Lock"
        public float diameterMm;      // diámetro exterior del cuerpo
        public float barrelMm;        // largo del cuerpo (sin émbolo)
        public float graduationMl;    // mínima división de la escala
        public float majorEveryMl;    // cada cuánto hay marca grande
        public string use;            // uso típico (sólo se muestra en el modo educativo)
        public string whyWrong;       // por qué no sirve para esta práctica

        public string Name => "Jeringa de " + label;
        public string Specs => kind + " · escala cada " + Num(graduationMl) + " mL · Ø " + Num(diameterMm) + " mm";
    }

    [Serializable]
    public struct NeedleSpec
    {
        public string id;
        public int gauge;
        public string lengthInch;     // "1″"
        public float lengthMm;
        public float diameterMm;      // diámetro exterior de la cánula (ISO 9626)
        public Color hub;
        public string colorName;
        public string use;
        public string whyWrong;

        public string Label => gauge + "G × " + lengthInch;
        public string Name => "Aguja " + Label;
        public string Specs => Num(diameterMm) + " × " + Num(lengthMm) + " mm · cono " + colorName;
    }

    public const string CorrectSyringeId = "5";
    public const string CorrectNeedleId = "21G-1";
    public const string CorrectLabel = "jeringa de 5 mL con aguja 21G × 1″";

    public static readonly SyringeSpec[] Syringes =
    {
        new SyringeSpec { id = "1", mL = 1, label = "1 mL", kind = "Jeringa de tuberculina", diameterMm = 8f, barrelMm = 68f, graduationMl = 0.01f, majorEveryMl = 0.1f,
            use = "Dosis muy pequeñas y precisas: pruebas intradérmicas, vacunas, insulina.",
            whyWrong = "Su capacidad (1 mL) no alcanza para el volumen del procedimiento: habría que recargarla varias veces y manipular más al conejo." },
        new SyringeSpec { id = "3", mL = 3, label = "3 mL", kind = "Jeringa hipodérmica Luer-Lock", diameterMm = 10.5f, barrelMm = 58f, graduationMl = 0.1f, majorEveryMl = 0.5f,
            use = "Volúmenes de 0.5 a 3 mL: medicamentos intramusculares o subcutáneos.",
            whyWrong = "Se queda corta (3 mL) para el volumen de trabajo de la práctica." },
        new SyringeSpec { id = "5", mL = 5, label = "5 mL", kind = "Jeringa hipodérmica Luer-Lock", diameterMm = 13.5f, barrelMm = 66f, graduationMl = 0.2f, majorEveryMl = 1f,
            use = "Volúmenes de 1 a 5 mL: inoculación y toma de muestra en animales pequeños como el conejo.",
            whyWrong = "" },
        new SyringeSpec { id = "10", mL = 10, label = "10 mL", kind = "Jeringa hipodérmica Luer-Lock", diameterMm = 16.5f, barrelMm = 80f, graduationMl = 0.2f, majorEveryMl = 1f,
            use = "Volúmenes de 5 a 10 mL: diluciones, lavados, extracciones de mayor volumen.",
            whyWrong = "Es más grande de lo necesario: su escala es menos precisa para medir 5 mL o menos y el émbolo genera más vacío, lo que puede colapsar la vena de la oreja." },
        new SyringeSpec { id = "20", mL = 20, label = "20 mL", kind = "Jeringa hipodérmica Luer-Lock", diameterMm = 20.5f, barrelMm = 95f, graduationMl = 1f, majorEveryMl = 5f,
            use = "Extracciones o irrigaciones de gran volumen.",
            whyWrong = "Demasiado grande: con escala de 1 mL no se mide con precisión y es difícil de controlar al trabajar con el conejo." },
        new SyringeSpec { id = "60", mL = 60, label = "60 mL", kind = "Jeringa de irrigación / alimentación", diameterMm = 29f, barrelMm = 115f, graduationMl = 1f, majorEveryMl = 10f,
            use = "Irrigación de heridas, alimentación enteral, bombas de infusión.",
            whyWrong = "No es una jeringa para inyección en animales pequeños: su volumen y tamaño no tienen sentido para este procedimiento." },
    };

    public static readonly NeedleSpec[] Needles =
    {
        new NeedleSpec { id = "18G-1.5", gauge = 18, lengthInch = "1½″", lengthMm = 38f, diameterMm = 1.2f, hub = new Color(0.96f, 0.56f, 0.72f), colorName = "rosa",
            use = "Cargar o reconstituir soluciones, transfusiones.",
            whyWrong = "Demasiado gruesa: desgarra la vena marginal de la oreja del conejo y provoca hematomas y dolor." },
        new NeedleSpec { id = "20G-1", gauge = 20, lengthInch = "1″", lengthMm = 25f, diameterMm = 0.9f, hub = new Color(1f, 0.84f, 0.1f), colorName = "amarillo",
            use = "Venopunción en personas y animales medianos, vía intramuscular.",
            whyWrong = "Más gruesa de lo que indica el protocolo: daña más la vena de la oreja y aumenta el sangrado." },
        new NeedleSpec { id = "21G-1", gauge = 21, lengthInch = "1″", lengthMm = 25f, diameterMm = 0.8f, hub = new Color(0.12f, 0.62f, 0.32f), colorName = "verde",
            use = "Vena marginal de la oreja y vía intramuscular del conejo: buen flujo sin hemolizar la muestra.",
            whyWrong = "" },
        new NeedleSpec { id = "22G-1.25", gauge = 22, lengthInch = "1¼″", lengthMm = 30f, diameterMm = 0.7f, hub = new Color(0.1f, 0.1f, 0.11f), colorName = "negro",
            use = "Vía intramuscular en adultos, venas finas.",
            whyWrong = "Es más fina y más larga que la del protocolo (21G × 1″): la longitud extra da menos control en la oreja del conejo." },
        new NeedleSpec { id = "23G-1", gauge = 23, lengthInch = "1″", lengthMm = 25f, diameterMm = 0.6f, hub = new Color(0.12f, 0.35f, 0.85f), colorName = "azul",
            use = "Vía subcutánea e intramuscular superficial, venas pequeñas.",
            whyWrong = "Más fina de lo indicado: con 5 mL el flujo es lento y al extraer aumenta el riesgo de hemolizar la muestra." },
        new NeedleSpec { id = "25G-0.625", gauge = 25, lengthInch = "⅝″", lengthMm = 16f, diameterMm = 0.5f, hub = new Color(1f, 0.52f, 0.1f), colorName = "naranja",
            use = "Vía subcutánea o intradérmica.",
            whyWrong = "Muy fina y corta: es para vía subcutánea o intradérmica; con 5 mL el paso del líquido sería muy lento." },
        new NeedleSpec { id = "27G-0.5", gauge = 27, lengthInch = "½″", lengthMm = 13f, diameterMm = 0.4f, hub = new Color(0.62f, 0.64f, 0.67f), colorName = "gris",
            use = "Pruebas intradérmicas, insulina, anestesia local.",
            whyWrong = "Demasiado fina para este procedimiento: se usa en pruebas intradérmicas o insulina." },
    };

    public static int IndexOfNeedle(string id) => Array.FindIndex(Needles, n => n.id == id);

    public static string Num(float v) => v.ToString(v < 0.1f ? "0.00" : v < 1f ? "0.0#" : "0.#", System.Globalization.CultureInfo.InvariantCulture);

    // ------------------------------------------------------------------
    //  Modelos 3D (primitivas). La jeringa queda acostada con el eje largo en Z:
    //  la punta (cono Luer) hacia -Z y el émbolo hacia +Z; descansa sobre y = 0.
    // ------------------------------------------------------------------

    public struct Materials
    {
        public Material barrel, plunger, rubber, steel, ink, card;
        public Material[] hubs; // mismo orden que Needles
    }

    /// <summary>Construye una jeringa. Devuelve el objeto raíz; <paramref name="tipLocal"/> es la punta del cono Luer (local).</summary>
    public static GameObject BuildSyringe(SyringeSpec s, Materials m, Transform parent, out Vector3 tipLocal, bool withMarks = true)
    {
        var root = new GameObject("Jeringa_" + s.label.Replace(" ", ""));
        root.transform.SetParent(parent, false);
        float d = s.diameterMm / 1000f, L = s.barrelMm / 1000f, r = d * 0.5f;
        Quaternion alongZ = Quaternion.Euler(90f, 0f, 0f);

        // Cuerpo translúcido.
        Part(PrimitiveType.Cylinder, root.transform, "Cuerpo", new Vector3(0f, r, 0f), alongZ, new Vector3(d, L * 0.5f, d), m.barrel);
        // Aletas (apoyo de los dedos).
        Part(PrimitiveType.Cube, root.transform, "Aletas", new Vector3(0f, r, L * 0.5f + 0.0012f), Quaternion.identity, new Vector3(d * 2.3f, d * 0.95f, 0.0024f), m.barrel);
        // Cono Luer-Lock (collar) y punta.
        float collarLen = 0.007f, tipLen = 0.008f;
        Part(PrimitiveType.Cylinder, root.transform, "Collar", new Vector3(0f, r, -L * 0.5f - collarLen * 0.5f), alongZ, new Vector3(d * 0.5f, collarLen * 0.5f, d * 0.5f), m.barrel);
        Part(PrimitiveType.Cylinder, root.transform, "PuntaLuer", new Vector3(0f, r, -L * 0.5f - tipLen * 0.5f), alongZ, new Vector3(d * 0.24f, tipLen * 0.5f, d * 0.24f), m.plunger);
        tipLocal = new Vector3(0f, r, -L * 0.5f - tipLen);

        // Tapón de hule (junto a la punta: jeringa vacía) y vástago del émbolo en cruz.
        float stopperLen = Mathf.Max(0.004f, d * 0.4f);
        float stopperZ = -L * 0.5f + stopperLen * 0.5f + 0.0015f;
        Part(PrimitiveType.Cylinder, root.transform, "Tapon", new Vector3(0f, r, stopperZ), alongZ, new Vector3(d * 0.9f, stopperLen * 0.5f, d * 0.9f), m.rubber);
        float rodStart = stopperZ + stopperLen * 0.5f, rodEnd = L * 0.5f + 0.012f + L * 0.08f;
        float rodLen = rodEnd - rodStart, rodMid = (rodStart + rodEnd) * 0.5f;
        Part(PrimitiveType.Cube, root.transform, "Vastago_A", new Vector3(0f, r, rodMid), Quaternion.identity, new Vector3(d * 0.72f, d * 0.12f, rodLen), m.plunger);
        Part(PrimitiveType.Cube, root.transform, "Vastago_B", new Vector3(0f, r, rodMid), Quaternion.identity, new Vector3(d * 0.12f, d * 0.72f, rodLen), m.plunger);
        Part(PrimitiveType.Cylinder, root.transform, "Pulsador", new Vector3(0f, r, rodEnd + 0.0012f), alongZ, new Vector3(d * 0.95f, 0.0012f, d * 0.95f), m.plunger);

        // Escala graduada impresa sobre el cuerpo (marcas negras).
        if (withMarks && m.ink != null)
        {
            float usable = L * 0.86f, z0 = -L * 0.5f + L * 0.07f;
            int minorPerMajor = Mathf.Clamp(Mathf.RoundToInt(s.majorEveryMl / Mathf.Max(0.0001f, s.graduationMl)), 1, 10);
            int majors = Mathf.Max(1, Mathf.RoundToInt(s.mL / s.majorEveryMl));
            int steps = majors * Mathf.Min(minorPerMajor, 5);
            int per = Mathf.Min(minorPerMajor, 5);
            for (int i = 0; i <= steps; i++)
            {
                bool major = i % per == 0;
                float z = z0 + usable * i / steps;
                Part(PrimitiveType.Cube, root.transform, major ? "Marca" : "MarcaChica",
                    new Vector3(-d * 0.12f, d + 0.0002f, z), Quaternion.identity,
                    new Vector3(major ? d * 0.55f : d * 0.3f, 0.0004f, major ? 0.0007f : 0.0005f), m.ink);
            }
        }
        return root;
    }

    /// <summary>
    /// Construye una aguja en su empaque (tarjeta con franja del color del cono). La aguja apunta a -Z.
    /// <paramref name="needle"/> es el grupo cono+cánula, con su origen en la parte trasera del cono.
    /// </summary>
    public static GameObject BuildNeedlePack(NeedleSpec n, int index, Materials m, Transform parent, out Transform needle, bool withCard = true)
    {
        var root = new GameObject("Aguja_" + n.gauge + "G");
        root.transform.SetParent(parent, false);
        Quaternion alongZ = Quaternion.Euler(90f, 0f, 0f);
        Material hubMat = m.hubs != null && index >= 0 && index < m.hubs.Length ? m.hubs[index] : m.plunger;

        float cardLen = 0.082f, cardW = 0.034f, cardH = 0.0016f;
        if (withCard)
        {
            Part(PrimitiveType.Cube, root.transform, "Empaque", new Vector3(0f, cardH * 0.5f, 0f), Quaternion.identity, new Vector3(cardW, cardH, cardLen), m.card);
            Part(PrimitiveType.Cube, root.transform, "Franja", new Vector3(0f, cardH + 0.0002f, cardLen * 0.5f - 0.009f), Quaternion.identity, new Vector3(cardW, 0.0004f, 0.012f), hubMat);
        }

        var g = new GameObject("Aguja");
        g.transform.SetParent(root.transform, false);
        float hubLen = 0.0135f, hubD = 0.0072f;
        float baseY = withCard ? cardH : 0f;
        // El grupo se coloca de modo que la parte trasera del cono quede cerca del extremo +Z de la tarjeta.
        g.transform.localPosition = new Vector3(0f, baseY + hubD * 0.5f, withCard ? cardLen * 0.5f - 0.018f : 0f);
        Part(PrimitiveType.Cylinder, g.transform, "Cono", new Vector3(0f, 0f, -hubLen * 0.5f), alongZ, new Vector3(hubD, hubLen * 0.5f, hubD), hubMat);
        Part(PrimitiveType.Cylinder, g.transform, "Aleta", new Vector3(0f, 0f, -0.0012f), alongZ, new Vector3(hubD * 1.35f, 0.0012f, hubD * 1.35f), hubMat);
        // El grosor se exagera un poco (×1.6) para que se aprecie en pantalla, conservando las proporciones.
        float cd = Mathf.Max(0.0006f, n.diameterMm * 1.6f / 1000f), cl = n.lengthMm / 1000f;
        Part(PrimitiveType.Cylinder, g.transform, "Canula", new Vector3(0f, 0f, -hubLen - cl * 0.5f), alongZ, new Vector3(cd, cl * 0.5f, cd), m.steel);
        needle = g.transform;
        return root;
    }

    static GameObject Part(PrimitiveType type, Transform parent, string name, Vector3 pos, Quaternion rot, Vector3 scale, Material mat)
    {
        var go = GameObject.CreatePrimitive(type);
        go.name = name;
        var col = go.GetComponent<Collider>();
        if (col != null)
        {
            if (Application.isPlaying) UnityEngine.Object.Destroy(col);
            else UnityEngine.Object.DestroyImmediate(col);
        }
        go.transform.SetParent(parent, false);
        go.transform.localPosition = pos;
        go.transform.localRotation = rot;
        go.transform.localScale = scale;
        var r = go.GetComponent<Renderer>();
        if (mat != null) r.sharedMaterial = mat;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        return go;
    }
}
