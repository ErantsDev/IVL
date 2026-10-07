using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Construye (o reconstruye) el salón-laboratorio alrededor del Nivel 1:
///   - Piso de losetas cuadriculadas (textura procedural con juntas y relieve)
///   - Paredes blancas con zoclo y moldura, pared norte con dos ventanas esmeriladas
///   - Techo de plafón azul con 4 lámparas de panel (con luces reales) y rejilla de A/C
///   - Puerta con letrero de SALIDA, apagador, extintor, reglamento de bioseguridad,
///     pizarrón con las instrucciones del nivel, reloj que marca la hora real, gabinetes
///     sobre la mesa, botiquín, contactos, contenedor RPBI y bote de basura
///   - Sonda de reflexión + sondas de luz, y ajustes de iluminación para hornear
/// Todo lo generado vive bajo el objeto "Salon", así que se puede volver a ejecutar sin duplicar.
/// </summary>
public static class SalonBuilder
{
    const string Dir = "Assets/Materials/Salon";

    // Rotaciones para cosas pegadas a cada pared: +Z local apunta hacia la pared, -Z hacia el salón,
    // +X local es "la derecha" de quien está viendo esa pared.
    static readonly Quaternion NorthWall = Quaternion.identity;
    static readonly Quaternion SouthWall = Quaternion.Euler(0f, 180f, 0f);
    static readonly Quaternion EastWall = Quaternion.Euler(0f, 90f, 0f);
    static readonly Quaternion WestWall = Quaternion.Euler(0f, -90f, 0f);

    class Mats
    {
        public Material floor, wall, ceiling, baseboard, trim, alu, frosted, sill, lampFrame, lampDiffuser,
            doorFrame, wood, darkGlass, steel, exitGreen, red, black, paper, blueHeader, navy, whiteboard,
            eraser, markerBlue, markerRed, markerBlack, cabinet, cabinetDoor, yellow, redBin, grayBin,
            vent, ventDark, ledRed, plastic;
    }

    [MenuItem("Tools/Inmunologia/Construir salón (techo, luces y detalles)")]
    public static void Build()
    {
        EnsureFolder();
        var M = CreateMaterials();

        var old = GameObject.Find("Salon");
        if (old != null) Object.DestroyImmediate(old);
        var oldNorth = GameObject.Find("Pared_Norte");
        if (oldNorth != null) Object.DestroyImmediate(oldNorth);

        var root = new GameObject("Salon").transform;

        BuildShell(root, M);
        BuildLights(root, M);
        BuildWindows(root, M);
        BuildWestWall(root, M);
        BuildEastWall(root, M);
        BuildNorthWallDetails(root, M);
        BuildSouthWall(root, M);
        BuildFloorProps(root, M);
        BuildCeilingFixtures(root, M);
        MarkFurnitureStatic();
        ConfigureLighting();

        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        Debug.Log("[SalonBuilder] Salón construido. Falta hornear la iluminación (Window > Rendering > Lighting > Generate Lighting).");
    }

    // =====================================================================
    //  Estructura: piso, paredes, techo, zoclos, molduras
    // =====================================================================

    static void BuildShell(Transform root, Mats M)
    {
        var shell = Group("Estructura", root);

        var piso = GameObject.Find("Piso");
        if (piso != null)
        {
            piso.GetComponent<MeshRenderer>().sharedMaterial = M.floor;
            MakeStatic(piso, true);
        }
        foreach (var n in new[] { "Pared_Este", "Pared_Oeste", "Pared_Sur" })
        {
            var w = GameObject.Find(n);
            if (w == null) continue;
            w.GetComponent<MeshRenderer>().sharedMaterial = M.wall;
            MakeStatic(w, true);
        }

        // Losa de techo (solo proyecta sombra: evita que la luz del sol se cuele por arriba).
        var losa = Box("Techo_Losa", shell, new Vector3(0f, 3.1f, 0f), new Vector3(6.3f, 0.2f, 4.3f), M.wall, true, false);
        losa.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.ShadowsOnly;

        // Plafón azul visible desde abajo.
        var plafon = GameObject.CreatePrimitive(PrimitiveType.Plane);
        plafon.name = "Techo_Plafon";
        plafon.transform.SetParent(shell, false);
        plafon.transform.localPosition = new Vector3(0f, 2.995f, 0f);
        plafon.transform.localRotation = Quaternion.Euler(180f, 0f, 0f);
        plafon.transform.localScale = new Vector3(0.6f, 1f, 0.4f);
        Object.DestroyImmediate(plafon.GetComponent<Collider>());
        var pr = plafon.GetComponent<MeshRenderer>();
        pr.sharedMaterial = M.ceiling;
        pr.shadowCastingMode = ShadowCastingMode.Off;
        MakeStatic(plafon, true);

        // Pared norte con dos vanos de ventana (x: -2.3..-0.7 y 0.7..2.3, y: 1.0..2.3).
        var north = Group("Pared_Norte", shell);
        Box("Base", north, new Vector3(0f, 0.5f, 2f), new Vector3(6f, 1f, 0.1f), M.wall, true, true);
        Box("Cabezal", north, new Vector3(0f, 2.65f, 2f), new Vector3(6f, 0.7f, 0.1f), M.wall, true, true);
        Box("Muro_Oeste", north, new Vector3(-2.65f, 1.65f, 2f), new Vector3(0.7f, 1.3f, 0.1f), M.wall, true, true);
        Box("Muro_Centro", north, new Vector3(0f, 1.65f, 2f), new Vector3(1.4f, 1.3f, 0.1f), M.wall, true, true);
        Box("Muro_Este", north, new Vector3(2.65f, 1.65f, 2f), new Vector3(0.7f, 1.3f, 0.1f), M.wall, true, true);

        // Zoclo (10 cm) — en la pared oeste se interrumpe en la puerta (z 0.48..1.52).
        var zoclo = Group("Zoclos", shell);
        Box("Zoclo_Norte", zoclo, new Vector3(0f, 0.05f, 1.944f), new Vector3(5.9f, 0.1f, 0.012f), M.baseboard, false, false);
        Box("Zoclo_Sur", zoclo, new Vector3(0f, 0.05f, -1.944f), new Vector3(5.9f, 0.1f, 0.012f), M.baseboard, false, false);
        Box("Zoclo_Este", zoclo, new Vector3(2.944f, 0.05f, 0f), new Vector3(0.012f, 0.1f, 3.9f), M.baseboard, false, false);
        Box("Zoclo_Oeste_A", zoclo, new Vector3(-2.944f, 0.05f, -0.735f), new Vector3(0.012f, 0.1f, 2.43f), M.baseboard, false, false);
        Box("Zoclo_Oeste_B", zoclo, new Vector3(-2.944f, 0.05f, 1.735f), new Vector3(0.012f, 0.1f, 0.43f), M.baseboard, false, false);

        // Moldura blanca donde la pared se junta con el techo.
        var mold = Group("Molduras", shell);
        Box("Moldura_Norte", mold, new Vector3(0f, 2.972f, 1.93f), new Vector3(5.9f, 0.05f, 0.04f), M.trim, false, false);
        Box("Moldura_Sur", mold, new Vector3(0f, 2.972f, -1.93f), new Vector3(5.9f, 0.05f, 0.04f), M.trim, false, false);
        Box("Moldura_Este", mold, new Vector3(2.93f, 2.972f, 0f), new Vector3(0.04f, 0.05f, 3.9f), M.trim, false, false);
        Box("Moldura_Oeste", mold, new Vector3(-2.93f, 2.972f, 0f), new Vector3(0.04f, 0.05f, 3.9f), M.trim, false, false);
    }

    // =====================================================================
    //  Iluminación: 4 lámparas de panel con luz real + sondas
    // =====================================================================

    static void BuildLights(Transform root, Mats M)
    {
        var g = Group("Iluminacion", root);

        // Alineadas a la retícula del plafón (0.6 m en X, 0.571 m en Z): cada panel ocupa 1x2 placas.
        Vector2[] spots = { new Vector2(-1.5f, -0.857f), new Vector2(1.5f, -0.857f), new Vector2(-1.5f, 0.857f), new Vector2(1.5f, 0.857f) };
        for (int i = 0; i < spots.Length; i++)
        {
            var lamp = Assembly("Lampara_" + (i + 1), g, new Vector3(spots[i].x, 2.995f, spots[i].y), Quaternion.identity);
            Box("Marco", lamp, new Vector3(0f, -0.012f, 0f), new Vector3(0.6f, 0.024f, 1.143f), M.lampFrame, false, false, false);
            Box("Difusor", lamp, new Vector3(0f, -0.0255f, 0f), new Vector3(0.54f, 0.004f, 1.08f), M.lampDiffuser, false, false, false);

            var lgo = new GameObject("Luz");
            lgo.transform.SetParent(lamp, false);
            lgo.transform.localPosition = new Vector3(0f, -0.08f, 0f);
            lgo.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            var L = lgo.AddComponent<Light>();
            L.type = LightType.Spot;
            L.spotAngle = 150f;
            L.innerSpotAngle = 80f;
            L.range = 7f;
            L.intensity = 6f;
            L.color = new Color(1f, 0.985f, 0.96f);
            L.shadows = LightShadows.Soft;
            L.shadowStrength = 0.8f;
            L.shadowNearPlane = 0.1f;
            L.lightmapBakeType = LightmapBakeType.Mixed;
            if (lgo.GetComponent<UniversalAdditionalLightData>() == null) lgo.AddComponent<UniversalAdditionalLightData>();
        }

        var rpGo = new GameObject("Sonda_Reflexion");
        rpGo.transform.SetParent(g, false);
        rpGo.transform.localPosition = new Vector3(0f, 1.5f, 0f);
        var rp = rpGo.AddComponent<ReflectionProbe>();
        rp.mode = ReflectionProbeMode.Baked;
        rp.size = new Vector3(5.9f, 2.99f, 3.9f);
        rp.boxProjection = true;
        rp.resolution = 256;
        rp.importance = 1;

        var lpGo = new GameObject("Sondas_Luz");
        lpGo.transform.SetParent(g, false);
        var lpg = lpGo.AddComponent<LightProbeGroup>();
        var pts = new List<Vector3>();
        foreach (float x in new[] { -2.6f, -1.3f, 0f, 1.3f, 2.6f })
        foreach (float y in new[] { 0.15f, 0.9f, 1.7f, 2.6f })
        foreach (float z in new[] { -1.7f, -0.6f, 0.6f, 1.7f })
            pts.Add(new Vector3(x, y, z));
        lpg.probePositions = pts.ToArray();
    }

    // =====================================================================
    //  Ventanas esmeriladas (pared norte)
    // =====================================================================

    static void BuildWindows(Transform root, Mats M)
    {
        var g = Group("Ventanas", root);
        float y0 = 1.0f, y1 = 2.3f;
        float[,] spans = { { -2.3f, -0.7f }, { 0.7f, 2.3f } };
        for (int i = 0; i < 2; i++)
        {
            float x0 = spans[i, 0], x1 = spans[i, 1], cx = (x0 + x1) * 0.5f, cy = (y0 + y1) * 0.5f, w = x1 - x0, h = y1 - y0;
            var win = Group("Ventana_" + (i + 1), g);
            Box("Marco_Inferior", win, new Vector3(cx, y0 + 0.025f, 2f), new Vector3(w, 0.05f, 0.07f), M.alu, true, false);
            Box("Marco_Superior", win, new Vector3(cx, y1 - 0.025f, 2f), new Vector3(w, 0.05f, 0.07f), M.alu, true, false);
            Box("Marco_Izq", win, new Vector3(x0 + 0.025f, cy, 2f), new Vector3(0.05f, h, 0.07f), M.alu, true, false);
            Box("Marco_Der", win, new Vector3(x1 - 0.025f, cy, 2f), new Vector3(0.05f, h, 0.07f), M.alu, true, false);
            Box("Parteluz", win, new Vector3(cx, cy, 2f), new Vector3(0.04f, h, 0.07f), M.alu, true, false);
            Box("Travesano", win, new Vector3(cx, y0 + 0.42f, 2f), new Vector3(w, 0.035f, 0.06f), M.alu, true, false);

            // Vidrio esmerilado: deja pasar la luz (no proyecta sombra) pero no deja ver hacia afuera.
            var glass = Box("Vidrio", win, new Vector3(cx, cy, 2f), new Vector3(w - 0.1f, h - 0.1f, 0.008f), M.frosted, true, false, false, false);
            glass.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;

            Box("Repisa", win, new Vector3(cx, y0 - 0.015f, 1.92f), new Vector3(w + 0.12f, 0.03f, 0.16f), M.sill, true, true);
        }
    }

    // =====================================================================
    //  Pared oeste: puerta, SALIDA, apagador, extintor, reglamento
    // =====================================================================

    static void BuildWestWall(Transform root, Mats M)
    {
        var door = Assembly("Puerta", root, new Vector3(-2.95f, 0f, 1.0f), WestWall);
        Box("Jamba_Izq", door, new Vector3(-0.5f, 1.1f, -0.035f), new Vector3(0.1f, 2.2f, 0.07f), M.doorFrame, true, false);
        Box("Jamba_Der", door, new Vector3(0.5f, 1.1f, -0.035f), new Vector3(0.1f, 2.2f, 0.07f), M.doorFrame, true, false);
        Box("Cabezal", door, new Vector3(0f, 2.15f, -0.035f), new Vector3(1.1f, 0.1f, 0.07f), M.doorFrame, true, false);
        Box("Hoja", door, new Vector3(0f, 1.05f, -0.02f), new Vector3(0.9f, 2.1f, 0.04f), M.wood, true, true);
        Box("Mirilla", door, new Vector3(0f, 1.5f, -0.0415f), new Vector3(0.22f, 0.6f, 0.004f), M.darkGlass, false, false);
        Box("Marco_Mirilla", door, new Vector3(0f, 1.5f, -0.041f), new Vector3(0.25f, 0.63f, 0.003f), M.steel, false, false);
        Box("Placa_Patada", door, new Vector3(0f, 0.13f, -0.0415f), new Vector3(0.86f, 0.22f, 0.003f), M.steel, false, false);
        Cyl("Roseta", door, new Vector3(0.36f, 1.0f, -0.045f), Quaternion.Euler(90f, 0f, 0f), new Vector3(0.06f, 0.006f, 0.06f), M.steel);
        Cyl("Cuello", door, new Vector3(0.36f, 1.0f, -0.06f), Quaternion.Euler(90f, 0f, 0f), new Vector3(0.022f, 0.015f, 0.022f), M.steel);
        Box("Manija", door, new Vector3(0.3f, 1.0f, -0.075f), new Vector3(0.13f, 0.022f, 0.022f), M.steel, false, false);

        Box("Letrero_Salida", door, new Vector3(0f, 2.42f, -0.03f), new Vector3(0.38f, 0.15f, 0.06f), M.exitGreen, false, false);
        Text("Texto_Salida", door, new Vector3(0f, 2.42f, -0.062f), new Vector2(0.34f, 0.11f), "SALIDA", Color.white, 1.0f, TextAlignmentOptions.Center, FontStyles.Bold);

        // Apagador a la izquierda de la puerta.
        Box("Apagador_Placa", door, new Vector3(-0.72f, 1.2f, -0.005f), new Vector3(0.08f, 0.12f, 0.01f), M.plastic, false, false);
        Box("Apagador_Tecla", door, new Vector3(-0.72f, 1.2f, -0.012f), new Vector3(0.022f, 0.036f, 0.01f), M.vent, false, false);

        // Extintor a la derecha de la puerta (esquina con la pared norte).
        Box("Extintor_Soporte", door, new Vector3(0.75f, 1.05f, -0.015f), new Vector3(0.06f, 0.2f, 0.03f), M.black, false, false);
        Cyl("Extintor_Cuerpo", door, new Vector3(0.75f, 1.0f, -0.11f), Quaternion.identity, new Vector3(0.16f, 0.26f, 0.16f), M.red, true);
        Cyl("Extintor_Valvula", door, new Vector3(0.75f, 1.3f, -0.11f), Quaternion.identity, new Vector3(0.05f, 0.04f, 0.05f), M.black);
        Box("Extintor_Maneral", door, new Vector3(0.75f, 1.36f, -0.11f), new Vector3(0.03f, 0.02f, 0.13f), M.black, false, false);
        Box("Extintor_Manguera", door, new Vector3(0.835f, 1.1f, -0.11f), new Vector3(0.018f, 0.4f, 0.018f), M.black, false, false);
        Box("Extintor_Letrero", door, new Vector3(0.75f, 1.68f, -0.005f), new Vector3(0.2f, 0.26f, 0.01f), M.red, false, false);
        Text("Extintor_Texto", door, new Vector3(0.75f, 1.68f, -0.011f), new Vector2(0.18f, 0.08f), "EXTINTOR", Color.white, 0.5f, TextAlignmentOptions.Center, FontStyles.Bold);

        // Reglamento de bioseguridad (pared este, junto al pizarrón; en la pared oeste va el espejo).
        var poster = Assembly("Cartel_Bioseguridad", root, new Vector3(2.95f, 0f, 1.62f), EastWall);
        Box("Papel", poster, new Vector3(0f, 1.55f, -0.004f), new Vector3(0.62f, 0.82f, 0.008f), M.paper, false, false);
        Box("Encabezado", poster, new Vector3(0f, 1.905f, -0.0085f), new Vector3(0.62f, 0.11f, 0.002f), M.blueHeader, false, false);
        Text("Titulo", poster, new Vector3(0f, 1.905f, -0.011f), new Vector2(0.58f, 0.09f), "NORMAS DE BIOSEGURIDAD", Color.white, 0.45f, TextAlignmentOptions.Center, FontStyles.Bold);
        Text("Normas", poster, new Vector3(0f, 1.5f, -0.0085f), new Vector2(0.56f, 0.64f),
            "• Usa bata, guantes y lentes de seguridad.\n\n" +
            "• No comas ni bebas dentro del laboratorio.\n\n" +
            "• Esteriliza el material antes de usarlo.\n\n" +
            "• Desecha los residuos biológicos en el contenedor RPBI.\n\n" +
            "• Lávate las manos al entrar y al salir.",
            new Color(0.12f, 0.12f, 0.14f), 0.3f, TextAlignmentOptions.TopLeft, FontStyles.Normal);
    }

    // =====================================================================
    //  Pared este: pizarrón con instrucciones del nivel + reloj
    // =====================================================================

    static void BuildEastWall(Transform root, Mats M)
    {
        var wb = Assembly("Pizarron", root, new Vector3(2.95f, 0f, 0.2f), EastWall);
        Box("Superficie", wb, new Vector3(0f, 1.7f, -0.01f), new Vector3(1.98f, 1.08f, 0.02f), M.whiteboard, true, true);
        Box("Marco_Sup", wb, new Vector3(0f, 2.25f, -0.02f), new Vector3(2.04f, 0.04f, 0.04f), M.alu, false, false);
        Box("Marco_Inf", wb, new Vector3(0f, 1.15f, -0.02f), new Vector3(2.04f, 0.04f, 0.04f), M.alu, false, false);
        Box("Marco_Izq", wb, new Vector3(-1.0f, 1.7f, -0.02f), new Vector3(0.04f, 1.14f, 0.04f), M.alu, false, false);
        Box("Marco_Der", wb, new Vector3(1.0f, 1.7f, -0.02f), new Vector3(0.04f, 1.14f, 0.04f), M.alu, false, false);
        Box("Charola", wb, new Vector3(0f, 1.12f, -0.06f), new Vector3(1.2f, 0.02f, 0.09f), M.alu, true, false);

        var mk = Quaternion.Euler(0f, 0f, 90f);
        Cyl("Plumon_Azul", wb, new Vector3(-0.35f, 1.14f, -0.07f), mk, new Vector3(0.018f, 0.065f, 0.018f), M.markerBlue);
        Cyl("Plumon_Rojo", wb, new Vector3(-0.2f, 1.14f, -0.07f), mk, new Vector3(0.018f, 0.065f, 0.018f), M.markerRed);
        Cyl("Plumon_Negro", wb, new Vector3(-0.05f, 1.14f, -0.07f), mk, new Vector3(0.018f, 0.065f, 0.018f), M.markerBlack);
        Box("Borrador", wb, new Vector3(0.3f, 1.145f, -0.065f), new Vector3(0.14f, 0.03f, 0.05f), M.eraser, false, false);

        var ink = new Color(0.08f, 0.2f, 0.62f);
        Text("Titulo", wb, new Vector3(0f, 2.1f, -0.0215f), new Vector2(1.8f, 0.14f), "Nivel 1 — Preparación y esterilización del material", ink, 0.9f, TextAlignmentOptions.Center, FontStyles.Bold | FontStyles.Underline);
        Text("Pasos", wb, new Vector3(-0.02f, 1.6f, -0.0215f), new Vector2(1.76f, 0.72f),
            "1. Toma solo el material correcto para la práctica.\n" +
            "2. Si el material no está estéril, llévalo al autoclave.\n" +
            "3. Espera a que termine el ciclo y retíralo.\n" +
            "4. Coloca todo el material en la mesa de química.",
            ink, 0.7f, TextAlignmentOptions.TopLeft, FontStyles.Normal);
        Text("Nota", wb, new Vector3(0.45f, 1.24f, -0.0215f), new Vector2(0.95f, 0.1f), "¡Todo el material debe estar estéril!", new Color(0.78f, 0.08f, 0.08f), 0.5f, TextAlignmentOptions.Right, FontStyles.Italic);

        // Reloj sobre el pizarrón.
        var clock = Assembly("Reloj", wb, new Vector3(0f, 2.62f, 0f), Quaternion.identity);
        Cyl("Aro", clock, new Vector3(0f, 0f, -0.02f), Quaternion.Euler(90f, 0f, 0f), new Vector3(0.3f, 0.02f, 0.3f), M.black);
        Cyl("Caratula", clock, new Vector3(0f, 0f, -0.03f), Quaternion.Euler(90f, 0f, 0f), new Vector3(0.27f, 0.012f, 0.27f), M.paper);
        for (int k = 0; k < 12; k++)
        {
            float a = k * 30f * Mathf.Deg2Rad;
            var mark = Box("Marca_" + k, clock, new Vector3(Mathf.Sin(a) * 0.112f, Mathf.Cos(a) * 0.112f, -0.0435f),
                new Vector3(0.006f, k % 3 == 0 ? 0.026f : 0.014f, 0.002f), M.black, false, false, false);
            mark.transform.localRotation = Quaternion.Euler(0f, 0f, -k * 30f);
        }
        var hourPivot = Pivot("Hora", clock, new Vector3(0f, 0f, -0.045f));
        Box("Manecilla", hourPivot, new Vector3(0f, 0.032f, 0f), new Vector3(0.011f, 0.072f, 0.003f), M.black, false, false, false, false);
        var minPivot = Pivot("Minuto", clock, new Vector3(0f, 0f, -0.047f));
        Box("Manecilla", minPivot, new Vector3(0f, 0.047f, 0f), new Vector3(0.007f, 0.104f, 0.002f), M.black, false, false, false, false);
        var secPivot = Pivot("Segundo", clock, new Vector3(0f, 0f, -0.049f));
        Box("Manecilla", secPivot, new Vector3(0f, 0.042f, 0f), new Vector3(0.0025f, 0.12f, 0.0015f), M.red, false, false, false, false);
        Cyl("Centro", clock, new Vector3(0f, 0f, -0.05f), Quaternion.Euler(90f, 0f, 0f), new Vector3(0.012f, 0.004f, 0.012f), M.red, false, false);
        var wc = clock.gameObject.AddComponent<WallClock>();
        wc.hourHand = hourPivot;
        wc.minuteHand = minPivot;
        wc.secondHand = secPivot;
    }

    // =====================================================================
    //  Pared norte: letrero del laboratorio, botiquín, contactos
    // =====================================================================

    static void BuildNorthWallDetails(Transform root, Mats M)
    {
        var n = Assembly("Pared_Norte_Detalles", root, new Vector3(0f, 0f, 1.95f), NorthWall);

        Box("Placa_Laboratorio", n, new Vector3(0f, 2.62f, -0.01f), new Vector3(1.9f, 0.26f, 0.02f), M.navy, false, false);
        Text("Texto_Laboratorio", n, new Vector3(0f, 2.62f, -0.0215f), new Vector2(1.8f, 0.2f), "LABORATORIO DE INMUNOLOGÍA", Color.white, 1.2f, TextAlignmentOptions.Center, FontStyles.Bold);

        Box("Botiquin", n, new Vector3(0f, 1.55f, -0.06f), new Vector3(0.34f, 0.4f, 0.12f), M.plastic, true, false);
        Box("Cruz_V", n, new Vector3(0f, 1.6f, -0.1205f), new Vector3(0.05f, 0.16f, 0.002f), M.red, false, false, false);
        Box("Cruz_H", n, new Vector3(0f, 1.6f, -0.1205f), new Vector3(0.16f, 0.05f, 0.002f), M.red, false, false, false);
        Text("Texto_Botiquin", n, new Vector3(0f, 1.43f, -0.1215f), new Vector2(0.3f, 0.06f), "BOTIQUÍN", new Color(0.78f, 0.06f, 0.06f), 0.4f, TextAlignmentOptions.Center, FontStyles.Bold);

        foreach (float x in new[] { -1.5f, 1.5f })
            Outlet(n, M, new Vector3(x, 0.35f, 0f));
    }

    static void Outlet(Transform parent, Mats M, Vector3 p)
    {
        var o = Assembly("Contacto", parent, p, Quaternion.identity);
        Box("Placa", o, new Vector3(0f, 0f, -0.005f), new Vector3(0.07f, 0.115f, 0.01f), M.plastic, false, false, false);
        foreach (float y in new[] { 0.025f, -0.025f })
        foreach (float x in new[] { -0.008f, 0.008f })
            Box("Ranura", o, new Vector3(x, y, -0.0105f), new Vector3(0.004f, 0.014f, 0.002f), M.black, false, false, false);
    }

    // =====================================================================
    //  Pared sur: gabinetes sobre la mesa + aviso junto al autoclave
    // =====================================================================

    static void BuildSouthWall(Transform root, Mats M)
    {
        var s = Assembly("Pared_Sur_Detalles", root, new Vector3(0f, 0f, -1.95f), SouthWall);

        // Gabinetes altos: mundo x -0.9..1.2  ->  local x -1.2..0.9 (en esta pared +X local = -X mundo).
        Box("Gabinete", s, new Vector3(-0.15f, 2.1f, -0.175f), new Vector3(2.1f, 0.7f, 0.35f), M.cabinet, true, true);
        for (int i = 0; i < 3; i++)
        {
            float cx = -0.85f + 0.7f * i;
            Box("Puerta_" + (i + 1), s, new Vector3(cx, 2.1f, -0.36f), new Vector3(0.68f, 0.68f, 0.02f), M.cabinetDoor, false, false);
            Box("Vidrio_" + (i + 1), s, new Vector3(cx, 2.12f, -0.371f), new Vector3(0.52f, 0.5f, 0.004f), M.darkGlass, false, false, false);
            Box("Jaladera_" + (i + 1), s, new Vector3(cx, 1.83f, -0.38f), new Vector3(0.12f, 0.012f, 0.015f), M.steel, false, false, false);
        }

        // Aviso sobre el autoclave (mundo x ≈ 2.1).
        Box("Aviso_Caliente", s, new Vector3(-2.1f, 2.38f, -0.005f), new Vector3(0.36f, 0.22f, 0.01f), M.yellow, false, false);
        Box("Aviso_Borde", s, new Vector3(-2.1f, 2.38f, -0.0035f), new Vector3(0.38f, 0.24f, 0.007f), M.black, false, false);
        Text("Aviso_Texto", s, new Vector3(-2.1f, 2.38f, -0.011f), new Vector2(0.32f, 0.18f), "PRECAUCIÓN\nSUPERFICIE CALIENTE", Color.black, 0.45f, TextAlignmentOptions.Center, FontStyles.Bold);
    }

    // =====================================================================
    //  Piso: contenedor RPBI y bote de basura
    // =====================================================================

    static void BuildFloorProps(Transform root, Mats M)
    {
        var g = Group("Botes", root);

        var rpbi = Assembly("Contenedor_RPBI", g, new Vector3(-1.25f, 0f, -1.72f), Quaternion.identity);
        Cyl("Cuerpo", rpbi, new Vector3(0f, 0.22f, 0f), Quaternion.identity, new Vector3(0.3f, 0.22f, 0.3f), M.redBin, true);
        Cyl("Tapa", rpbi, new Vector3(0f, 0.452f, 0f), Quaternion.identity, new Vector3(0.32f, 0.014f, 0.32f), M.redBin);
        Box("Etiqueta", rpbi, new Vector3(0f, 0.27f, 0.151f), new Vector3(0.17f, 0.13f, 0.004f), M.paper, false, false, false);
        var back = Quaternion.Euler(0f, 180f, 0f);
        TextRot("RPBI", rpbi, new Vector3(0f, 0.3f, 0.154f), back, new Vector2(0.15f, 0.05f), "RPBI", new Color(0.72f, 0.04f, 0.05f), 0.5f, FontStyles.Bold);
        TextRot("RPBI_Leyenda", rpbi, new Vector3(0f, 0.245f, 0.154f), back, new Vector2(0.15f, 0.05f), "Residuos peligrosos\nbiológico-infecciosos", Color.black, 0.15f, FontStyles.Normal);

        var trash = Assembly("Bote_Basura", g, new Vector3(-2.7f, 0f, 0.05f), Quaternion.identity);
        Cyl("Cuerpo", trash, new Vector3(0f, 0.2f, 0f), Quaternion.identity, new Vector3(0.28f, 0.2f, 0.28f), M.grayBin, true);
        Cyl("Borde", trash, new Vector3(0f, 0.405f, 0f), Quaternion.identity, new Vector3(0.29f, 0.008f, 0.29f), M.black);
    }

    // =====================================================================
    //  Techo: rejilla de aire acondicionado y detector de humo
    // =====================================================================

    static void BuildCeilingFixtures(Transform root, Mats M)
    {
        var g = Group("Instalaciones_Techo", root);

        var vent = Assembly("Rejilla_AC", g, new Vector3(0.3f, 2.995f, 0f), Quaternion.identity);
        Box("Marco", vent, new Vector3(0f, -0.006f, 0f), new Vector3(0.56f, 0.012f, 0.53f), M.vent, false, false, false);
        Box("Hueco_1", vent, new Vector3(0f, -0.0125f, 0f), new Vector3(0.44f, 0.002f, 0.41f), M.ventDark, false, false, false);
        Box("Anillo", vent, new Vector3(0f, -0.016f, 0f), new Vector3(0.34f, 0.006f, 0.31f), M.vent, false, false, false);
        Box("Hueco_2", vent, new Vector3(0f, -0.0195f, 0f), new Vector3(0.24f, 0.002f, 0.21f), M.ventDark, false, false, false);
        Box("Centro", vent, new Vector3(0f, -0.023f, 0f), new Vector3(0.14f, 0.006f, 0.12f), M.vent, false, false, false);

        var smoke = Assembly("Detector_Humo", g, new Vector3(-0.9f, 2.995f, 0f), Quaternion.identity);
        Cyl("Cuerpo", smoke, new Vector3(0f, -0.014f, 0f), Quaternion.identity, new Vector3(0.12f, 0.014f, 0.12f), M.plastic);
        Box("LED", smoke, new Vector3(0.03f, -0.029f, 0f), new Vector3(0.007f, 0.003f, 0.007f), M.ledRed, false, false, false);
    }

    // =====================================================================
    //  Estáticos de muebles existentes + ajustes de iluminación
    // =====================================================================

    static void MarkFurnitureStatic()
    {
        foreach (var n in new[] { "ChemLabTable_LowPoly", "rack", "chair", "PetScale_LowPoly", "soporte" })
        {
            var go = GameObject.Find(n);
            if (go == null) continue;
            foreach (var r in go.GetComponentsInChildren<MeshRenderer>(true))
                MakeStatic(r.gameObject, false, false);
        }
        // Solo el cuerpo del autoclave: la tapa y la rueda se animan.
        var ac = GameObject.Find("autoclave_lowpoly");
        if (ac != null) MakeStatic(ac, false, false);
    }

    static void ConfigureLighting()
    {
        var sunGo = GameObject.Find("Directional Light");
        if (sunGo != null)
        {
            var sun = sunGo.GetComponent<Light>();
            // Sol que entra por las ventanas del norte y cae en diagonal sobre el piso.
            sunGo.transform.rotation = Quaternion.Euler(38f, 160f, 0f);
            sun.lightmapBakeType = LightmapBakeType.Mixed;
            sun.intensity = 1.8f;
            sun.color = new Color(1f, 0.97f, 0.93f);
            sun.shadows = LightShadows.Soft;
        }

        RenderSettings.ambientIntensity = 1f;
        RenderSettings.reflectionIntensity = 1f;

        var ls = Lightmapping.lightingSettings;
        if (ls != null)
        {
            ls.bakedGI = true;
            ls.realtimeGI = false;
            ls.mixedBakeMode = MixedLightingMode.IndirectOnly; // luz directa y sombras en tiempo real, rebote horneado
            ls.lightmapResolution = 16f;
            ls.lightmapMaxSize = 1024;
            ls.ao = true;
            ls.aoMaxDistance = 0.5f;
            EditorUtility.SetDirty(ls);
        }

        var urp = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
        if (urp != null)
        {
            urp.shadowDistance = 20f;       // escena interior: sombras más nítidas
            urp.maxAdditionalLightsCount = 8;
            EditorUtility.SetDirty(urp);
        }
    }

    // =====================================================================
    //  Materiales y texturas
    // =====================================================================

    static Mats CreateMaterials()
    {
        var M = new Mats();

        MakeFloorTextures(out var fAlb, out var fNrm, out var fMask);
        M.floor = Lit("M_Piso_Losetas", Color.white, 1f);
        M.floor.SetTexture("_BaseMap", fAlb);
        M.floor.SetTexture("_BumpMap", fNrm);
        M.floor.SetFloat("_BumpScale", 1f);
        M.floor.EnableKeyword("_NORMALMAP");
        M.floor.SetTexture("_MetallicGlossMap", fMask);
        M.floor.SetFloat("_SmoothnessTextureChannel", 0f);
        M.floor.EnableKeyword("_METALLICSPECGLOSSMAP");
        M.floor.SetTextureScale("_BaseMap", new Vector2(7.5f, 5f)); // 6x4 m -> losetas de 40 cm

        MakeCeilingTextures(out var cAlb, out var cNrm);
        M.ceiling = Lit("M_Techo_Plafon_Azul", Color.white, 0.12f);
        M.ceiling.SetTexture("_BaseMap", cAlb);
        M.ceiling.SetTexture("_BumpMap", cNrm);
        M.ceiling.SetFloat("_BumpScale", 1f);
        M.ceiling.EnableKeyword("_NORMALMAP");
        M.ceiling.SetTextureScale("_BaseMap", new Vector2(10f, 7f)); // placas de 60 x 57 cm

        M.wall = Lit("M_Pared_Blanca", new Color(0.95f, 0.955f, 0.955f), 0.12f);
        M.baseboard = Lit("M_Zoclo", new Color(0.33f, 0.35f, 0.38f), 0.35f);
        M.trim = Lit("M_Moldura", new Color(0.96f, 0.96f, 0.95f), 0.2f);
        M.alu = Lit("M_Aluminio", new Color(0.72f, 0.74f, 0.76f), 0.55f, 0.8f);
        M.sill = Lit("M_Repisa_Ventana", new Color(0.84f, 0.84f, 0.82f), 0.5f);
        M.frosted = Transparent(Emissive("M_Vidrio_Esmerilado", new Color(0.9f, 0.94f, 0.97f), new Color(0.78f, 0.85f, 0.93f), 0.6f), 0.75f);
        M.frosted.SetFloat("_Smoothness", 0.6f);
        M.lampFrame = Lit("M_Lampara_Marco", new Color(0.92f, 0.92f, 0.92f), 0.5f, 0.2f);
        M.lampDiffuser = Emissive("M_Lampara_Difusor", Color.white, new Color(1f, 0.99f, 0.97f), 2.2f);
        M.doorFrame = Lit("M_Puerta_Marco", new Color(0.3f, 0.38f, 0.47f), 0.45f, 0.3f);
        M.wood = Lit("M_Puerta_Madera", new Color(0.55f, 0.38f, 0.24f), 0.35f);
        M.darkGlass = Lit("M_Vidrio_Oscuro", new Color(0.12f, 0.16f, 0.19f), 0.92f, 0.1f);
        M.steel = Lit("M_Acero", new Color(0.78f, 0.79f, 0.8f), 0.7f, 0.9f);
        M.exitGreen = Emissive("M_Salida_Verde", new Color(0.05f, 0.55f, 0.2f), new Color(0.1f, 0.9f, 0.3f), 1.3f);
        M.red = Lit("M_Rojo", new Color(0.78f, 0.06f, 0.06f), 0.6f);
        M.black = Lit("M_Negro", new Color(0.04f, 0.04f, 0.045f), 0.35f);
        M.paper = Lit("M_Papel", new Color(0.96f, 0.96f, 0.94f), 0.1f);
        M.blueHeader = Lit("M_Azul_Encabezado", new Color(0.1f, 0.32f, 0.62f), 0.3f);
        M.navy = Lit("M_Placa_Marino", new Color(0.07f, 0.18f, 0.4f), 0.5f, 0.1f);
        M.whiteboard = Lit("M_Pizarron_Blanco", new Color(0.98f, 0.98f, 0.98f), 0.85f);
        M.eraser = Lit("M_Borrador", new Color(0.2f, 0.2f, 0.22f), 0.2f);
        M.markerBlue = Lit("M_Plumon_Azul", new Color(0.1f, 0.25f, 0.75f), 0.5f);
        M.markerRed = Lit("M_Plumon_Rojo", new Color(0.8f, 0.1f, 0.1f), 0.5f);
        M.markerBlack = Lit("M_Plumon_Negro", new Color(0.05f, 0.05f, 0.05f), 0.5f);
        M.cabinet = Lit("M_Gabinete", new Color(0.9f, 0.9f, 0.88f), 0.35f);
        M.cabinetDoor = Lit("M_Gabinete_Puerta", new Color(0.84f, 0.85f, 0.85f), 0.4f);
        M.yellow = Lit("M_Amarillo_Aviso", new Color(0.98f, 0.78f, 0.05f), 0.45f);
        M.redBin = Lit("M_Contenedor_RPBI", new Color(0.72f, 0.04f, 0.05f), 0.45f);
        M.grayBin = Lit("M_Bote_Gris", new Color(0.45f, 0.47f, 0.5f), 0.4f);
        M.vent = Lit("M_Rejilla", new Color(0.9f, 0.9f, 0.9f), 0.3f);
        M.ventDark = Lit("M_Rejilla_Oscura", new Color(0.22f, 0.23f, 0.25f), 0.2f);
        M.ledRed = Emissive("M_LED_Rojo", new Color(0.6f, 0f, 0f), new Color(1f, 0.05f, 0.05f), 2f);
        M.plastic = Lit("M_Plastico_Blanco", new Color(0.94f, 0.94f, 0.92f), 0.45f);

        AssetDatabase.SaveAssets();
        return M;
    }

    static Material Lit(string name, Color c, float smooth, float metal = 0f)
    {
        string path = Dir + "/" + name + ".mat";
        var sh = Shader.Find("Universal Render Pipeline/Lit");
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null) { m = new Material(sh); AssetDatabase.CreateAsset(m, path); }
        m.shader = sh;
        m.SetColor("_BaseColor", c);
        m.SetFloat("_Smoothness", smooth);
        m.SetFloat("_Metallic", metal);
        m.DisableKeyword("_EMISSION");
        m.SetColor("_EmissionColor", Color.black);
        m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.EmissiveIsBlack;
        EditorUtility.SetDirty(m);
        return m;
    }

    static Material Emissive(string name, Color baseColor, Color emission, float intensity)
    {
        var m = Lit(name, baseColor, 0.5f);
        m.EnableKeyword("_EMISSION");
        m.SetColor("_EmissionColor", emission * intensity);
        m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.BakedEmissive;
        EditorUtility.SetDirty(m);
        return m;
    }

    static Material Transparent(Material m, float alpha)
    {
        var c = m.GetColor("_BaseColor");
        c.a = alpha;
        m.SetColor("_BaseColor", c);
        m.SetFloat("_Surface", 1f);
        m.SetFloat("_Blend", 0f);
        m.SetOverrideTag("RenderType", "Transparent");
        m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
        m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
        m.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
        m.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
        m.SetFloat("_ZWrite", 0f);
        m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        m.renderQueue = (int)RenderQueue.Transparent;
        m.SetShaderPassEnabled("DepthOnly", false);
        m.SetShaderPassEnabled("ShadowCaster", false);
        EditorUtility.SetDirty(m);
        return m;
    }

    static void MakeFloorTextures(out Texture2D albedo, out Texture2D normal, out Texture2D mask)
    {
        const int N = 1024;
        int tile = N / 2;                  // 2x2 losetas por textura
        float grout = tile * 0.016f;       // media junta
        float bevel = tile * 0.022f;       // bisel de la loseta
        var col = new Color[N * N];
        var hgt = new float[N * N];
        var msk = new Color[N * N];
        Color A = new Color(0.91f, 0.9f, 0.87f);   // blanco hueso
        Color B = new Color(0.6f, 0.66f, 0.72f);   // gris azulado
        Color G = new Color(0.5f, 0.5f, 0.48f);    // junta

        for (int y = 0; y < N; y++)
        for (int x = 0; x < N; x++)
        {
            int tx = x / tile, ty = y / tile, lx = x % tile, ly = y % tile;
            float d = Mathf.Min(Mathf.Min(lx, tile - 1 - lx), Mathf.Min(ly, tile - 1 - ly));
            bool isA = ((tx + ty) & 1) == 0;
            float cloud = (Mathf.PerlinNoise(x * 0.012f + tx * 31.7f, y * 0.012f + ty * 17.3f) - 0.5f) * 0.07f;
            float fine = (Hash(x, y, 1) - 0.5f) * 0.035f;
            float speck = Hash(x, y, 2) > 0.992f ? (isA ? -0.18f : 0.14f) : 0f;

            Color c; float h; float s;
            if (d < grout)
            {
                c = G * (1f + fine); h = 0f; s = 0.12f;
            }
            else
            {
                c = (isA ? A : B) * (1f + cloud + fine + speck);
                float t = Mathf.Clamp01((d - grout) / bevel);
                h = t * t * (3f - 2f * t);
                s = (isA ? 0.62f : 0.58f) - Mathf.Abs(cloud) * 0.5f;
            }
            c.a = 1f;
            int i = y * N + x;
            col[i] = c; hgt[i] = h; msk[i] = new Color(0f, 0f, 0f, s);
        }

        albedo = SaveTexture("T_Piso_Losetas_Albedo", col, N, false, true);
        normal = SaveTexture("T_Piso_Losetas_Normal", HeightToNormal(hgt, N, 2f), N, true, false);
        mask = SaveTexture("T_Piso_Losetas_Mascara", msk, N, false, false);
    }

    static void MakeCeilingTextures(out Texture2D albedo, out Texture2D normal)
    {
        const int N = 512;                 // una placa por textura
        float bar = N * 0.02f;             // media perfil "T"
        float bevel = N * 0.018f;
        var col = new Color[N * N];
        var hgt = new float[N * N];
        Color tileC = new Color(0.3f, 0.52f, 0.78f);   // azul
        Color barC = new Color(0.93f, 0.93f, 0.92f);   // perfil blanco

        for (int y = 0; y < N; y++)
        for (int x = 0; x < N; x++)
        {
            float d = Mathf.Min(Mathf.Min(x, N - 1 - x), Mathf.Min(y, N - 1 - y));
            float fine = (Hash(x, y, 3) - 0.5f) * 0.05f;
            float cloud = (Mathf.PerlinNoise(x * 0.02f, y * 0.02f) - 0.5f) * 0.06f;
            Color c; float h;
            if (d < bar)
            {
                c = barC * (1f + fine * 0.3f); h = 1f;
            }
            else
            {
                float t = Mathf.Clamp01((d - bar) / bevel);
                h = Mathf.Lerp(0.55f, 0f, t * t * (3f - 2f * t));
                c = tileC * (1f + cloud + fine);
                if (Hash(x, y, 4) > 0.985f) { c *= 0.72f; h -= 0.15f; } // perforaciones acústicas
            }
            c.a = 1f;
            int i = y * N + x;
            col[i] = c; hgt[i] = h;
        }

        albedo = SaveTexture("T_Techo_Plafon_Albedo", col, N, false, true);
        normal = SaveTexture("T_Techo_Plafon_Normal", HeightToNormal(hgt, N, 1.5f), N, true, false);
    }

    static Color[] HeightToNormal(float[] h, int n, float strength)
    {
        var o = new Color[n * n];
        for (int y = 0; y < n; y++)
        for (int x = 0; x < n; x++)
        {
            float l = h[y * n + (x - 1 + n) % n], r = h[y * n + (x + 1) % n];
            float dn = h[((y - 1 + n) % n) * n + x], up = h[((y + 1) % n) * n + x];
            var v = new Vector3((l - r) * strength, (dn - up) * strength, 1f).normalized;
            o[y * n + x] = new Color(v.x * 0.5f + 0.5f, v.y * 0.5f + 0.5f, v.z * 0.5f + 0.5f, 1f);
        }
        return o;
    }

    static Texture2D SaveTexture(string name, Color[] px, int size, bool isNormal, bool sRGB)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false, !sRGB);
        tex.SetPixels(px);
        tex.Apply();
        string path = Dir + "/" + name + ".png";
        File.WriteAllBytes(path, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);

        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        var imp = (TextureImporter)AssetImporter.GetAtPath(path);
        imp.textureType = isNormal ? TextureImporterType.NormalMap : TextureImporterType.Default;
        imp.sRGBTexture = sRGB;
        imp.wrapMode = TextureWrapMode.Repeat;
        imp.anisoLevel = 8;
        imp.mipmapEnabled = true;
        imp.maxTextureSize = size;
        imp.textureCompression = TextureImporterCompression.CompressedHQ;
        imp.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    static float Hash(int x, int y, int seed)
    {
        unchecked
        {
            uint h = (uint)(x * 374761393 + y * 668265263 + seed * 982451653);
            h = (h ^ (h >> 13)) * 1274126177u;
            h ^= h >> 16;
            return (h & 0xFFFFFF) / (float)0xFFFFFF;
        }
    }

    // =====================================================================
    //  Utilidades de construcción
    // =====================================================================

    static void EnsureFolder()
    {
        if (!AssetDatabase.IsValidFolder("Assets/Materials")) AssetDatabase.CreateFolder("Assets", "Materials");
        if (!AssetDatabase.IsValidFolder(Dir)) AssetDatabase.CreateFolder("Assets/Materials", "Salon");
    }

    static Transform Group(string name, Transform parent)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        return go.transform;
    }

    static Transform Assembly(string name, Transform parent, Vector3 localPos, Quaternion localRot)
    {
        var t = Group(name, parent);
        t.localPosition = localPos;
        t.localRotation = localRot;
        return t;
    }

    static Transform Pivot(string name, Transform parent, Vector3 localPos) => Assembly(name, parent, localPos, Quaternion.identity);

    static GameObject Box(string name, Transform parent, Vector3 pos, Vector3 size, Material m,
        bool collider = true, bool lightmapped = false, bool castShadows = true, bool isStatic = true)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.localPosition = pos;
        go.transform.localRotation = Quaternion.identity;
        go.transform.localScale = size;
        var mr = go.GetComponent<MeshRenderer>();
        mr.sharedMaterial = m;
        mr.shadowCastingMode = castShadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
        if (!collider) Object.DestroyImmediate(go.GetComponent<Collider>());
        if (isStatic) MakeStatic(go, lightmapped);
        return go;
    }

    static GameObject Cyl(string name, Transform parent, Vector3 pos, Quaternion rot, Vector3 scale, Material m,
        bool collider = false, bool isStatic = true)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.localPosition = pos;
        go.transform.localRotation = rot;
        go.transform.localScale = scale;
        go.GetComponent<MeshRenderer>().sharedMaterial = m;
        if (!collider) Object.DestroyImmediate(go.GetComponent<Collider>());
        if (isStatic) MakeStatic(go, false);
        return go;
    }

    static TextMeshPro Text(string name, Transform parent, Vector3 pos, Vector2 size, string text, Color color,
        float maxFontSize, TextAlignmentOptions align, FontStyles style)
        => TextRot(name, parent, pos, Quaternion.identity, size, text, color, maxFontSize, style, align);

    static TextMeshPro TextRot(string name, Transform parent, Vector3 pos, Quaternion rot, Vector2 size, string text,
        Color color, float maxFontSize, FontStyles style, TextAlignmentOptions align = TextAlignmentOptions.Center)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = pos;
        go.transform.localRotation = rot;
        var tmp = go.AddComponent<TextMeshPro>();
        if (TMP_Settings.defaultFontAsset != null) tmp.font = TMP_Settings.defaultFontAsset;
        tmp.rectTransform.sizeDelta = size;
        tmp.text = text;
        tmp.color = color;
        tmp.alignment = align;
        tmp.fontStyle = style;
        tmp.enableAutoSizing = true;
        tmp.fontSizeMin = 0.05f;
        tmp.fontSizeMax = maxFontSize;
        tmp.textWrappingMode = TextWrappingModes.Normal;
        var mr = go.GetComponent<MeshRenderer>();
        if (mr != null) mr.shadowCastingMode = ShadowCastingMode.Off;
        return tmp;
    }

    static void MakeStatic(GameObject go, bool lightmapped, bool batching = true)
    {
        var flags = StaticEditorFlags.ContributeGI | StaticEditorFlags.OccluderStatic |
                    StaticEditorFlags.OccludeeStatic | StaticEditorFlags.ReflectionProbeStatic;
        if (batching) flags |= StaticEditorFlags.BatchingStatic;
        GameObjectUtility.SetStaticEditorFlags(go, flags);

        var mr = go.GetComponent<MeshRenderer>();
        if (mr == null) return;
        var mf = go.GetComponent<MeshFilter>();
        bool hasUv2 = mf != null && mf.sharedMesh != null && mf.sharedMesh.uv2 != null && mf.sharedMesh.uv2.Length > 0;
        mr.receiveGI = lightmapped && hasUv2 ? ReceiveGI.Lightmaps : ReceiveGI.LightProbes;
    }
}
