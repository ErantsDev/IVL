using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

/// <summary>
/// Construye todo lo del Nivel 2 (preparación del animal) dentro de la escena del laboratorio:
/// carrito con jaula, caja de guantes, paño, bandeja con rasuradora, gasa y alcohol, pantalla de
/// bitácora, pantalla de la báscula, HUD (barra de estrés y minijuego), cámara del minijuego,
/// el LevelDirector y el selector de nivel del menú. Se puede volver a ejecutar sin duplicar nada.
/// </summary>
public static class Level2Builder
{
    const string MatDir = "Assets/Materials/Nivel2";
    const string MeshDir = "Assets/Materials/Nivel2/Mallas";
    const float TableTop = 0.90f;

    static Sprite rounded;

    [MenuItem("Tools/Inmunologia/Construir nivel 2 (preparación del animal)")]
    public static void Build()
    {
        EnsureFolders();
        rounded = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");

        var rabbitPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/objetos/Conejo/Conejo.prefab");
        if (rabbitPrefab == null || rabbitPrefab.GetComponent<RabbitController>() == null)
        {
            Debug.LogError("[Level2Builder] Falta el prefab del conejo con RabbitController. Ejecuta antes 'Construir conejo'.");
            return;
        }

        Remove("Nivel2");
        Remove("LevelDirector");

        var root = new GameObject("Nivel2");
        var cage = BuildCartAndCage(root.transform, out var door, out var spawn);
        var gloveBox = BuildGloveBox(root.transform);
        var pad = BuildPad(root.transform, out var padPoint);
        BuildToolTray(root.transform, out var clipper, out var gauze, out var bottle);
        var scale = SetupScale(out var weighPoint);
        var monitor = BuildMonitor(root.transform, scale);
        var minigameCam = BuildMinigameCamera(root.transform);

        var hud = GameObject.Find("Canvas");
        var stress = BuildStressBar(hud.transform, out var stressFill, out var stressLabel);
        var minigame = BuildMinigame(root.transform, hud.transform, minigameCam);

        // ---------- Controlador del nivel ----------
        var lm = root.AddComponent<Level2Manager>();
        lm.rabbitPrefab = rabbitPrefab;
        lm.cageSpawnPoint = spawn;
        lm.cageDoor = door;
        lm.cage = cage;
        lm.gloveBox = gloveBox;
        lm.scale = scale;
        lm.pad = pad;
        lm.padPoint = padPoint;
        lm.clipper = clipper;
        lm.gauze = gauze;
        lm.alcoholBottle = bottle;
        lm.minigame = minigame;
        lm.markerMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Nivel1/M_Marcador_Guia.mat");

        var h = hud.transform;
        lm.counterText = Find<TMP_Text>(h, "CounterText");
        lm.toastText = Find<TMP_Text>(h, "ToastText");
        lm.toastGroup = h.Find("ToastText") != null ? h.Find("ToastText").GetComponent<CanvasGroup>() : null;
        lm.modeText = Find<TMP_Text>(h, "ModeText");
        lm.timerText = Find<TMP_Text>(h, "TimerText");
        lm.objectivePanel = h.Find("ObjectivePanel")?.gameObject;
        lm.objectiveText = Find<TMP_Text>(h, "ObjectivePanel/ObjectiveText");
        lm.checklistPanel = h.Find("ChecklistPanel")?.gameObject;
        lm.checklistText = Find<TMP_Text>(h, "ChecklistPanel/ChecklistText");
        lm.stressGroup = stress;
        lm.stressFill = stressFill;
        lm.stressLabel = stressLabel;
        lm.completePanel = h.Find("CompletePanel")?.gameObject;
        lm.completeTitleText = Find<TMP_Text>(h, "CompletePanel/Title");
        lm.completeTimeText = Find<TMP_Text>(h, "CompletePanel/TimeText");
        lm.gradeText = Find<TMP_Text>(h, "CompletePanel/GradeText");
        lm.resultDetailText = h.Find("CompletePanel").GetComponentsInChildren<TMP_Text>(true).FirstOrDefaultNamed("ResultDetailText");
        lm.continueButton = h.Find("CompletePanel/ContinueButton")?.gameObject;
        var l1 = Object.FindAnyObjectByType<Level1Manager>(FindObjectsInactive.Include);
        if (l1 != null)
        {
            // Se reutiliza el mismo HUD que el nivel 1.
            lm.counterText = l1.counterText; lm.toastText = l1.toastText; lm.toastGroup = l1.toastGroup;
            lm.modeText = l1.modeText; lm.timerText = l1.timerText; lm.completePanel = l1.completePanel;
            lm.completeTitleText = l1.completeTitleText; lm.completeTimeText = l1.completeTimeText;
            lm.gradeText = l1.gradeText; lm.resultDetailText = l1.resultDetailText;
            var guide = l1.GetComponent<EducationalGuide>();
            if (guide != null)
            {
                lm.objectivePanel = guide.objectivePanel; lm.objectiveText = guide.objectiveText;
                lm.checklistPanel = guide.checklistPanel; lm.checklistText = guide.checklistText;
                if (guide.markerMaterial != null) lm.markerMaterial = guide.markerMaterial;
            }
        }

        // ---------- LevelDirector, GameFlow, botones de resultados y menú ----------
        var director = BuildDirector(root, scale);
        var flow = Object.FindAnyObjectByType<GameFlow>(FindObjectsInactive.Include);
        if (flow != null)
        {
            flow.director = director;
            EditorUtility.SetDirty(flow);
            RewireResultButtons(h, flow);
        }
        BuildLevelSelector();

        root.SetActive(false);
        EditorUtility.SetDirty(lm);
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(root.scene);
        AssetDatabase.SaveAssets();
        Debug.Log("[Level2Builder] Nivel 2 construido.");
    }

    // =====================================================================
    //  Carrito y jaula
    // =====================================================================

    static Transform BuildCartAndCage(Transform root, out CageDoor door, out Transform spawn)
    {
        var metal = Mat("M_Carrito_Metal", new Color(0.72f, 0.74f, 0.76f), 0.6f, 0.8f);
        var dark = Mat("M_Carrito_Ruedas", new Color(0.1f, 0.1f, 0.11f), 0.3f, 0f);
        var wire = Mat("M_Jaula_Alambre", new Color(0.78f, 0.8f, 0.82f), 0.7f, 0.9f);
        var tray = Mat("M_Jaula_Bandeja", new Color(0.33f, 0.45f, 0.58f), 0.45f, 0f);
        var bedding = Mat("M_Jaula_Viruta", new Color(0.86f, 0.74f, 0.52f), 0.1f, 0f);
        var blue = Mat("M_Plastico_Azul", new Color(0.15f, 0.4f, 0.8f), 0.5f, 0f);
        var clear = Mat("M_Plastico_Claro", new Color(0.85f, 0.92f, 0.95f), 0.8f, 0f);

        var cart = new GameObject("Carrito_Jaula").transform;
        cart.SetParent(root, false);
        // Lejos de la mesa de química (glovebox/báscula/paño ~ x:-0.9..0.6, z:-1.2..-1.35):
        // así cargar al conejo implica un trayecto real donde el estrés por movimiento brusco importa.
        cart.position = new Vector3(0.3f, 0f, 1.45f);
        const float W = 0.62f, D = 0.5f, H = 0.76f;
        Prim(PrimitiveType.Cube, "Cubierta", cart, new Vector3(0, H - 0.015f, 0), new Vector3(W, 0.03f, D), metal);
        Prim(PrimitiveType.Cube, "Repisa", cart, new Vector3(0, 0.2f, 0), new Vector3(W - 0.02f, 0.02f, D - 0.02f), metal);
        foreach (var sx in new[] { -1, 1 })
            foreach (var sz in new[] { -1, 1 })
            {
                var p = new Vector3(sx * (W / 2 - 0.02f), 0f, sz * (D / 2 - 0.02f));
                Prim(PrimitiveType.Cylinder, "Pata", cart, p + Vector3.up * (H / 2 + 0.02f), new Vector3(0.025f, (H - 0.05f) / 2f, 0.025f), metal);
                Prim(PrimitiveType.Sphere, "Rueda", cart, p + Vector3.up * 0.025f, Vector3.one * 0.05f, dark);
            }
        var cartCol = cart.gameObject.AddComponent<BoxCollider>();
        cartCol.center = new Vector3(0, H / 2, 0);
        cartCol.size = new Vector3(W, H, D);

        // ---- Jaula ----
        var cage = new GameObject("Jaula").transform;
        cage.SetParent(cart, false);
        cage.localPosition = new Vector3(0f, H, 0f);
        const float CX = 0.42f, CZ = 0.56f, CH = 0.52f, B = 0.06f;
        Prim(PrimitiveType.Cube, "Bandeja", cage, new Vector3(0, B / 2, 0), new Vector3(CX + 0.02f, B, CZ + 0.02f), tray, true);
        Prim(PrimitiveType.Cube, "Viruta", cage, new Vector3(0, B + 0.004f, 0), new Vector3(CX - 0.02f, 0.012f, CZ - 0.02f), bedding);

        var bars = new List<(Vector3 pos, Vector3 size)>();
        const float t = 0.004f, step = 0.028f;
        float y0 = B, y1 = CH;
        float hx = CX / 2f, hz = CZ / 2f;
        // pared trasera (-X) y laterales (±Z)
        for (float z = -hz; z <= hz + 0.001f; z += step) bars.Add((new Vector3(-hx, (y0 + y1) / 2, z), new Vector3(t, y1 - y0, t)));
        for (float x = -hx; x <= hx + 0.001f; x += step)
        {
            bars.Add((new Vector3(x, (y0 + y1) / 2, -hz), new Vector3(t, y1 - y0, t)));
            bars.Add((new Vector3(x, (y0 + y1) / 2, hz), new Vector3(t, y1 - y0, t)));
            bars.Add((new Vector3(x, y1, 0), new Vector3(t, t, CZ)));        // techo
        }
        for (float z = -hz; z <= hz + 0.001f; z += step * 2) bars.Add((new Vector3(0, y1, z), new Vector3(CX, t, t)));
        // rieles horizontales
        foreach (float yy in new[] { y0 + 0.01f, (y0 + y1) / 2, y1 })
        {
            bars.Add((new Vector3(-hx, yy, 0), new Vector3(t * 1.5f, t * 1.5f, CZ)));
            bars.Add((new Vector3(0, yy, -hz), new Vector3(CX, t * 1.5f, t * 1.5f)));
            bars.Add((new Vector3(0, yy, hz), new Vector3(CX, t * 1.5f, t * 1.5f)));
        }
        // frente (+X) con hueco para la puerta
        const float dz0 = -0.145f, dz1 = 0.145f, dy0 = 0.075f, dy1 = 0.44f;
        for (float z = -hz; z <= hz + 0.001f; z += step)
        {
            if (z > dz0 - 0.005f && z < dz1 + 0.005f)
            {
                bars.Add((new Vector3(hx, (y0 + dy0) / 2, z), new Vector3(t, dy0 - y0, t)));
                bars.Add((new Vector3(hx, (dy1 + y1) / 2, z), new Vector3(t, y1 - dy1, t)));
            }
            else bars.Add((new Vector3(hx, (y0 + y1) / 2, z), new Vector3(t, y1 - y0, t)));
        }
        bars.Add((new Vector3(hx, y1, 0), new Vector3(t * 1.5f, t * 1.5f, CZ)));
        bars.Add((new Vector3(hx, y0 + 0.01f, 0), new Vector3(t * 1.5f, t * 1.5f, CZ)));
        bars.Add((new Vector3(hx, dy0, 0), new Vector3(t * 1.5f, t * 1.5f, dz1 - dz0 + 0.02f)));
        bars.Add((new Vector3(hx, dy1, 0), new Vector3(t * 1.5f, t * 1.5f, dz1 - dz0 + 0.02f)));
        var wireGo = new GameObject("Alambre");
        wireGo.transform.SetParent(cage, false);
        wireGo.AddComponent<MeshFilter>().sharedMesh = BoxesMesh("Jaula_Alambre", bars);
        wireGo.AddComponent<MeshRenderer>().sharedMaterial = wire;

        // colisionadores invisibles de las paredes (el frente sólo alrededor de la puerta)
        AddBox(cage, "Col_Trasera", new Vector3(-hx, CH / 2, 0), new Vector3(0.01f, CH, CZ));
        AddBox(cage, "Col_Izq", new Vector3(0, CH / 2, -hz), new Vector3(CX, CH, 0.01f));
        AddBox(cage, "Col_Der", new Vector3(0, CH / 2, hz), new Vector3(CX, CH, 0.01f));
        AddBox(cage, "Col_Techo", new Vector3(0, CH, 0), new Vector3(CX, 0.01f, CZ));
        AddBox(cage, "Col_FrenteA", new Vector3(hx, CH / 2, (-hz + dz0) / 2), new Vector3(0.01f, CH, dz0 + hz));
        AddBox(cage, "Col_FrenteB", new Vector3(hx, CH / 2, (hz + dz1) / 2), new Vector3(0.01f, CH, hz - dz1));
        AddBox(cage, "Col_FrenteArriba", new Vector3(hx, (dy1 + CH) / 2, 0), new Vector3(0.01f, CH - dy1, dz1 - dz0));

        // puerta con bisagra vertical en su borde -Z
        var hinge = new GameObject("Bisagra_Puerta").transform;
        hinge.SetParent(cage, false);
        hinge.localPosition = new Vector3(hx + 0.006f, 0, dz0);
        var doorBars = new List<(Vector3, Vector3)>();
        float dw = dz1 - dz0, dh = dy1 - dy0;
        for (float z = 0.012f; z < dw - 0.005f; z += step * 0.9f) doorBars.Add((new Vector3(0, dy0 + dh / 2, z), new Vector3(t, dh, t)));
        doorBars.Add((new Vector3(0, dy0, dw / 2), new Vector3(t * 2, t * 2, dw)));
        doorBars.Add((new Vector3(0, dy1, dw / 2), new Vector3(t * 2, t * 2, dw)));
        doorBars.Add((new Vector3(0, dy0 + dh / 2, 0), new Vector3(t * 2, dh, t * 2)));
        doorBars.Add((new Vector3(0, dy0 + dh / 2, dw), new Vector3(t * 2, dh, t * 2)));
        doorBars.Add((new Vector3(0.008f, dy0 + dh * 0.55f, dw - 0.01f), new Vector3(0.012f, 0.03f, 0.02f))); // pestillo
        var doorGo = new GameObject("Puerta");
        doorGo.transform.SetParent(hinge, false);
        doorGo.AddComponent<MeshFilter>().sharedMesh = BoxesMesh("Jaula_Puerta", doorBars);
        doorGo.AddComponent<MeshRenderer>().sharedMaterial = wire;
        var dc = doorGo.AddComponent<BoxCollider>();
        dc.center = new Vector3(0, dy0 + dh / 2, dw / 2);
        dc.size = new Vector3(0.012f, dh, dw);
        door = cage.gameObject.AddComponent<CageDoor>();
        door.hinge = hinge;
        door.openEuler = new Vector3(0f, 105f, 0f);

        // bebedero y comedero
        var bottle = new GameObject("Bebedero").transform;
        bottle.SetParent(cage, false);
        bottle.localPosition = new Vector3(-0.08f, 0.22f, hz + 0.03f);
        Prim(PrimitiveType.Cylinder, "Botella", bottle, Vector3.zero, new Vector3(0.05f, 0.07f, 0.05f), clear);
        Prim(PrimitiveType.Cylinder, "Tapa", bottle, new Vector3(0, -0.075f, 0), new Vector3(0.03f, 0.008f, 0.03f), blue);
        var spout = Prim(PrimitiveType.Cylinder, "Tubo", bottle, new Vector3(0, -0.1f, -0.03f), new Vector3(0.008f, 0.035f, 0.008f), wire);
        spout.transform.localRotation = Quaternion.Euler(-40f, 0, 0);
        Prim(PrimitiveType.Cylinder, "Comedero", cage, new Vector3(-0.12f, B + 0.025f, -0.17f), new Vector3(0.1f, 0.02f, 0.1f), blue);

        spawn = new GameObject("PuntoConejo").transform;
        spawn.SetParent(cage, false);
        spawn.localPosition = new Vector3(0.02f, B + 0.01f, 0.02f);
        spawn.localRotation = Quaternion.Euler(0f, 0f, 0f);

        var st = cage.gameObject.AddComponent<Level2Station>();
        st.kind = Level2Station.Kind.Cage;

        // cartel
        var label = Tmp3D("Etiqueta", cage, "CONEJO · Oryctolagus cuniculus", 0.12f, new Color(0.1f, 0.12f, 0.16f), new Vector2(0.3f, 0.05f));
        label.transform.localPosition = new Vector3(hx + 0.012f, B / 2, 0f);
        label.transform.localRotation = Quaternion.Euler(0f, -90f, 0f);
        return cage;
    }

    // =====================================================================
    //  Mesa: guantes, paño y bandeja de instrumentos
    // =====================================================================

    static Transform BuildGloveBox(Transform root)
    {
        var box = Mat("M_Caja_Guantes", new Color(0.93f, 0.95f, 0.98f), 0.25f, 0f);
        var band = Mat("M_Caja_Guantes_Franja", new Color(0.2f, 0.45f, 0.85f), 0.3f, 0f);
        var nitrile = Mat("M_Guante_Nitrilo", new Color(0.45f, 0.38f, 0.85f), 0.45f, 0f);
        var slot = Mat("M_Caja_Ranura", new Color(0.15f, 0.15f, 0.18f), 0.1f, 0f);

        var g = new GameObject("Caja_Guantes").transform;
        g.SetParent(root, false);
        g.position = new Vector3(-0.87f, TableTop, -1.2f);
        Prim(PrimitiveType.Cube, "Caja", g, new Vector3(0, 0.05f, 0), new Vector3(0.24f, 0.1f, 0.125f), box);
        Prim(PrimitiveType.Cube, "Franja", g, new Vector3(0, 0.03f, 0), new Vector3(0.242f, 0.025f, 0.127f), band);
        Prim(PrimitiveType.Cube, "Ranura", g, new Vector3(0, 0.1f, 0), new Vector3(0.12f, 0.002f, 0.04f), slot);
        var glove = Prim(PrimitiveType.Sphere, "Guante", g, new Vector3(0.01f, 0.115f, 0), new Vector3(0.07f, 0.035f, 0.045f), nitrile);
        glove.transform.localRotation = Quaternion.Euler(0, 20f, 12f);
        Prim(PrimitiveType.Capsule, "Dedo", g, new Vector3(0.05f, 0.125f, 0.01f), new Vector3(0.016f, 0.025f, 0.016f), nitrile).transform.localRotation = Quaternion.Euler(0, 0, 70f);
        var label = Tmp3D("Texto", g, "<b>GUANTES DE NITRILO</b>\nTalla M · 100 piezas", 0.12f, new Color(0.1f, 0.2f, 0.45f), new Vector2(0.22f, 0.05f));
        label.transform.localPosition = new Vector3(0, 0.065f, 0.0635f);
        label.transform.localRotation = Quaternion.Euler(0, 180f, 0);
        var col = g.gameObject.AddComponent<BoxCollider>();
        col.center = new Vector3(0, 0.065f, 0);
        col.size = new Vector3(0.24f, 0.13f, 0.125f);
        g.gameObject.AddComponent<Level2Station>().kind = Level2Station.Kind.GloveBox;
        return g;
    }

    static Transform BuildPad(Transform root, out Transform padPoint)
    {
        var cloth = Mat("M_Pano_Quirurgico", new Color(0.2f, 0.45f, 0.62f), 0.1f, 0f);
        var p = new GameObject("Pano").transform;
        p.SetParent(root, false);
        p.position = new Vector3(0.02f, TableTop, -1.25f);
        Prim(PrimitiveType.Cube, "Tela", p, new Vector3(0, 0.002f, 0), new Vector3(0.44f, 0.004f, 0.34f), cloth);
        var col = p.gameObject.AddComponent<BoxCollider>();
        col.center = new Vector3(0, 0.01f, 0);
        col.size = new Vector3(0.44f, 0.02f, 0.34f);
        p.gameObject.AddComponent<Level2Station>().kind = Level2Station.Kind.Pad;

        // regla de 15 cm junto al conejo
        var ruler = Prim(PrimitiveType.Cube, "Regla", p, new Vector3(0.0f, 0.005f, 0.145f), new Vector3(0.16f, 0.002f, 0.03f), RulerMaterial());
        ruler.name = "Regla_15cm";

        padPoint = new GameObject("PuntoConejo").transform;
        padPoint.SetParent(p, false);
        padPoint.localPosition = new Vector3(0f, 0.004f, -0.01f);
        padPoint.localRotation = Quaternion.Euler(0f, -90f, 0f);
        return p;
    }

    static void BuildToolTray(Transform root, out Grabbable clipper, out GauzePad gauze, out Transform bottle)
    {
        var steel = Mat("M_Acero_Bandeja", new Color(0.8f, 0.81f, 0.82f), 0.75f, 0.9f);
        var tray = new GameObject("Bandeja_Instrumentos").transform;
        tray.SetParent(root, false);
        tray.position = new Vector3(0.47f, TableTop, -1.22f);
        Prim(PrimitiveType.Cube, "Base", tray, new Vector3(0, 0.004f, 0), new Vector3(0.28f, 0.008f, 0.36f), steel, true);
        foreach (var s in new[] { -1, 1 })
        {
            Prim(PrimitiveType.Cube, "Borde", tray, new Vector3(s * 0.14f, 0.012f, 0), new Vector3(0.006f, 0.02f, 0.36f), steel);
            Prim(PrimitiveType.Cube, "Borde", tray, new Vector3(0, 0.012f, s * 0.18f), new Vector3(0.28f, 0.02f, 0.006f), steel);
        }
        float top = TableTop + 0.008f;

        // Rasuradora eléctrica
        var bodyMat = Mat("M_Rasuradora", new Color(0.16f, 0.17f, 0.2f), 0.55f, 0f);
        var bladeMat = Mat("M_Rasuradora_Cuchilla", new Color(0.85f, 0.86f, 0.88f), 0.85f, 1f);
        var accent = Mat("M_Rasuradora_Boton", new Color(0.2f, 0.75f, 0.45f), 0.5f, 0f);
        var c = new GameObject("Rasuradora").transform;
        c.SetParent(root, false);
        c.position = new Vector3(0.42f, top + 0.018f, -1.1f);
        var body = Prim(PrimitiveType.Capsule, "Cuerpo", c, Vector3.zero, new Vector3(0.034f, 0.07f, 0.03f), bodyMat);
        body.transform.localRotation = Quaternion.Euler(0, 0, 90f);
        Prim(PrimitiveType.Cube, "Cabezal", c, new Vector3(-0.078f, 0.002f, 0), new Vector3(0.018f, 0.014f, 0.038f), bladeMat);
        Prim(PrimitiveType.Cube, "Dientes", c, new Vector3(-0.089f, 0.002f, 0), new Vector3(0.006f, 0.006f, 0.036f), bladeMat);
        Prim(PrimitiveType.Cube, "Boton", c, new Vector3(-0.01f, 0.016f, 0), new Vector3(0.018f, 0.005f, 0.012f), accent);
        Prim(PrimitiveType.Cylinder, "Cable", c, new Vector3(0.085f, 0f, 0), new Vector3(0.008f, 0.012f, 0.008f), bodyMat).transform.localRotation = Quaternion.Euler(0, 0, 90f);
        var cc = c.gameObject.AddComponent<CapsuleCollider>();
        cc.direction = 0;
        cc.radius = 0.018f;
        cc.height = 0.19f;
        cc.center = new Vector3(-0.005f, 0, 0);
        var rb = c.gameObject.AddComponent<Rigidbody>();
        rb.mass = 0.35f;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
        clipper = c.gameObject.AddComponent<Grabbable>();
        clipper.displayName = "Rasuradora eléctrica";

        // Gasa estéril
        var dry = Mat("M_Gasa", new Color(0.97f, 0.97f, 0.95f), 0.05f, 0f);
        var wet = Mat("M_Gasa_Alcohol", new Color(0.78f, 0.84f, 0.9f), 0.55f, 0f);
        var gz = new GameObject("Gasa").transform;
        gz.SetParent(root, false);
        gz.position = new Vector3(0.52f, top + 0.004f, -1.33f);
        var gzr = Prim(PrimitiveType.Cube, "Tela", gz, Vector3.zero, new Vector3(0.075f, 0.007f, 0.075f), dry);
        var gcol = gz.gameObject.AddComponent<BoxCollider>();
        gcol.size = new Vector3(0.075f, 0.012f, 0.075f);
        var grb = gz.gameObject.AddComponent<Rigidbody>();
        grb.mass = 0.05f;
        grb.interpolation = RigidbodyInterpolation.Interpolate;
        grb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
        gz.gameObject.AddComponent<Grabbable>().displayName = "Gasa estéril";
        gauze = gz.gameObject.AddComponent<GauzePad>();
        gauze.renderers = new Renderer[] { gzr.GetComponent<Renderer>() };
        gauze.dryMaterial = dry;
        gauze.wetMaterial = wet;
        // paquete abierto de gasas (decoración)
        Prim(PrimitiveType.Cube, "Paquete_Gasas", tray, new Vector3(-0.07f, 0.014f, -0.12f), new Vector3(0.09f, 0.012f, 0.09f), dry);

        // Botella de alcohol (fija)
        var bottleMat = Mat("M_Botella_Alcohol", new Color(0.93f, 0.95f, 0.97f), 0.7f, 0f);
        var capMat = Mat("M_Tapa_Azul", new Color(0.12f, 0.35f, 0.8f), 0.5f, 0f);
        var b = new GameObject("Botella_Alcohol").transform;
        b.SetParent(root, false);
        b.position = new Vector3(0.56f, top, -1.13f);
        Prim(PrimitiveType.Cylinder, "Botella", b, new Vector3(0, 0.075f, 0), new Vector3(0.062f, 0.075f, 0.062f), bottleMat);
        Prim(PrimitiveType.Cylinder, "Hombro", b, new Vector3(0, 0.155f, 0), new Vector3(0.045f, 0.008f, 0.045f), bottleMat);
        Prim(PrimitiveType.Cylinder, "Tapa", b, new Vector3(0, 0.175f, 0), new Vector3(0.03f, 0.014f, 0.03f), capMat);
        Prim(PrimitiveType.Cylinder, "Pico", b, new Vector3(0, 0.198f, 0), new Vector3(0.01f, 0.01f, 0.01f), capMat);
        var lbl = Tmp3D("Etiqueta", b, "<b>ALCOHOL</b>\nETÍLICO 70 %", 0.1f, new Color(0.1f, 0.25f, 0.6f), new Vector2(0.05f, 0.045f));
        lbl.transform.localPosition = new Vector3(0, 0.08f, 0.0325f);
        lbl.transform.localRotation = Quaternion.Euler(0, 180f, 0);
        var bcol = b.gameObject.AddComponent<CapsuleCollider>();
        bcol.center = new Vector3(0, 0.1f, 0);
        bcol.radius = 0.033f;
        bcol.height = 0.21f;
        b.gameObject.AddComponent<Level2Station>().kind = Level2Station.Kind.AlcoholBottle;
        bottle = b;
    }

    // =====================================================================
    //  Báscula
    // =====================================================================

    static ScaleDevice SetupScale(out Transform weighPoint)
    {
        var sc = GameObject.Find("PetScale_LowPoly").transform;
        foreach (var n in new[] { "PantallaLCD", "EstadoLCD", "PuntoPesaje" })
        {
            var old = sc.Find(n);
            if (old != null) Object.DestroyImmediate(old.gameObject);
        }
        var bodyT = sc.Find("SCALE_Body");
        var trayT = sc.Find("SCALE_Tray");
        var mesh = bodyT.GetComponent<MeshFilter>().sharedMesh;
        Bounds Sub(int s)
        {
            var v = mesh.vertices;
            var idx = mesh.GetIndices(s);
            var b = new Bounds(bodyT.TransformPoint(v[idx[0]]), Vector3.zero);
            foreach (var i in idx) b.Encapsulate(bodyT.TransformPoint(v[i]));
            return b;
        }
        var lcd = Sub(3);
        var decal = Sub(4);
        var lcdColor = new Color(0.55f, 1f, 0.78f);

        var lcdText = Tmp3D("PantallaLCD", null, "0.00 kg", 0.3f, lcdColor, new Vector2(lcd.size.x * 0.95f, lcd.size.z * 0.95f));
        lcdText.alignment = TextAlignmentOptions.Right;
        lcdText.enableAutoSizing = true;
        lcdText.fontSizeMin = 0.05f;
        lcdText.fontSizeMax = 0.3f;
        lcdText.fontStyle = FontStyles.Bold;
        lcdText.transform.SetPositionAndRotation(new Vector3(lcd.center.x, lcd.max.y + 0.0015f, lcd.center.z), Quaternion.Euler(90f, 180f, 0f));
        lcdText.transform.SetParent(sc, true);

        var status = Tmp3D("EstadoLCD", null, "CERO", 0.1f, new Color(0.12f, 0.16f, 0.22f), new Vector2(decal.size.x * 0.95f, decal.size.z * 0.9f));
        status.enableAutoSizing = true;
        status.fontSizeMin = 0.03f;
        status.fontSizeMax = 0.1f;
        status.fontStyle = FontStyles.Bold;
        status.transform.SetPositionAndRotation(new Vector3(decal.center.x, decal.max.y + 0.0015f, decal.center.z), Quaternion.Euler(90f, 180f, 0f));
        status.transform.SetParent(sc, true);

        var trayB = trayT.GetComponent<Renderer>().bounds;
        weighPoint = new GameObject("PuntoPesaje").transform;
        weighPoint.SetPositionAndRotation(new Vector3(trayB.center.x, trayB.max.y, trayB.center.z), Quaternion.Euler(0f, -90f, 0f));
        weighPoint.SetParent(sc, true);

        var dev = sc.GetComponent<ScaleDevice>();
        if (dev == null) dev = sc.gameObject.AddComponent<ScaleDevice>();
        dev.tray = trayT;
        dev.lcdText = lcdText;
        dev.statusText = status;
        dev.idText = null;
        dev.weighPoint = weighPoint;
        dev.residualKg = 0f;
        var st = sc.GetComponent<Level2Station>();
        if (st == null) st = sc.gameObject.AddComponent<Level2Station>();
        st.kind = Level2Station.Kind.Scale;
        EditorUtility.SetDirty(dev);
        return dev;
    }

    // =====================================================================
    //  Bitácora (pantalla en la pared sur)
    // =====================================================================

    static BitacoraMonitor BuildMonitor(Transform root, ScaleDevice scale)
    {
        var frame = Mat("M_Monitor_Marco", new Color(0.08f, 0.09f, 0.1f), 0.5f, 0.2f);
        var screen = Mat("M_Monitor_Pantalla", new Color(0.02f, 0.05f, 0.1f), 0.8f, 0f);
        screen.EnableKeyword("_EMISSION");
        screen.SetColor("_EmissionColor", new Color(0.02f, 0.06f, 0.14f));
        screen.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;

        float wallZ = -1.95f;
        var wall = GameObject.Find("Pared_Sur");
        if (wall != null) wallZ = wall.GetComponent<Renderer>().bounds.max.z;

        var m = new GameObject("Bitacora_Pantalla").transform;
        m.SetParent(root, false);
        m.position = new Vector3(-1.95f, 2.02f, wallZ + 0.02f);
        m.rotation = Quaternion.Euler(0f, 180f, 0f); // el frente mira hacia +Z (al salón)
        Prim(PrimitiveType.Cube, "Marco", m, Vector3.zero, new Vector3(1.04f, 0.64f, 0.03f), frame, true);
        Prim(PrimitiveType.Cube, "Pantalla", m, new Vector3(0, 0, -0.0155f), new Vector3(1.0f, 0.6f, 0.002f), screen);

        var title = Tmp3D("Titulo", m, "<b>BITÁCORA DE PESAJES · BIOTERIO</b>", 0.46f, new Color(0.55f, 0.8f, 1f), new Vector2(0.94f, 0.06f));
        title.alignment = TextAlignmentOptions.Left;
        title.enableAutoSizing = false; title.textWrappingMode = TextWrappingModes.NoWrap;
        title.transform.localPosition = new Vector3(0, 0.25f, -0.018f);
        title.transform.localRotation = Quaternion.identity;

        var live = Tmp3D("Lectura", m, "", 0.32f, new Color(0.92f, 0.95f, 1f), new Vector2(0.94f, 0.05f));
        live.alignment = TextAlignmentOptions.Left;
        live.enableAutoSizing = false; live.textWrappingMode = TextWrappingModes.NoWrap;
        live.transform.localPosition = new Vector3(0, 0.195f, -0.018f);
        live.transform.localRotation = Quaternion.identity;

        var body = Tmp3D("Registros", m, "", 0.3f, new Color(0.88f, 0.92f, 0.98f), new Vector2(0.94f, 0.36f));
        body.alignment = TextAlignmentOptions.TopLeft;
        body.enableAutoSizing = false; body.textWrappingMode = TextWrappingModes.NoWrap;
        body.lineSpacing = 8f;
        body.transform.localPosition = new Vector3(0, -0.01f, -0.018f);
        body.transform.localRotation = Quaternion.identity;

        var foot = Tmp3D("Pie", m, "", 0.22f, new Color(0.6f, 0.68f, 0.8f), new Vector2(0.94f, 0.04f));
        foot.alignment = TextAlignmentOptions.Left;
        foot.enableAutoSizing = false; foot.textWrappingMode = TextWrappingModes.NoWrap;
        foot.transform.localPosition = new Vector3(0, -0.265f, -0.018f);
        foot.transform.localRotation = Quaternion.identity;

        var mon = m.gameObject.AddComponent<BitacoraMonitor>();
        mon.bodyText = body;
        mon.footerText = foot;
        mon.liveText = live;
        mon.scale = scale;
        return mon;
    }

    // =====================================================================
    //  Cámara y UI del minijuego, barra de estrés
    // =====================================================================

    static Camera BuildMinigameCamera(Transform root)
    {
        var go = new GameObject("CamaraMinijuego");
        go.transform.SetParent(root, false);
        var cam = go.AddComponent<Camera>();
        cam.fieldOfView = 40f;
        cam.nearClipPlane = 0.01f;
        cam.farClipPlane = 20f;
        cam.depth = 5f;
        var data = go.AddComponent<UniversalAdditionalCameraData>();
        data.renderPostProcessing = true;
        data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
        go.SetActive(false);
        return cam;
    }

    static GameObject BuildStressBar(Transform hud, out Image fill, out TMP_Text label)
    {
        var old = hud.Find("BarraEstres");
        if (old != null) Object.DestroyImmediate(old.gameObject);
        var rt = UI("BarraEstres", hud);
        Place(rt, new Vector2(0, 0), new Vector2(0, 0), new Vector2(0, 0), new Vector2(24, 24), new Vector2(440, 64));
        Img(rt, new Color(0.03f, 0.07f, 0.14f, 0.82f));
        label = Txt(UI("Texto", rt), "Conejo tranquilo", 20, Color.white, TextAlignmentOptions.Center, FontStyles.Bold);
        Place(label.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1), new Vector2(0, -4), new Vector2(-20, 28));
        var back = UI("Fondo", rt);
        Place(back, new Vector2(0, 0), new Vector2(1, 0), new Vector2(0.5f, 0), new Vector2(0, 10), new Vector2(-28, 16));
        Img(back, new Color(1f, 1f, 1f, 0.15f));
        var f = UI("Relleno", back);
        Stretch(f);
        fill = f.gameObject.AddComponent<Image>();
        fill.sprite = rounded;
        fill.type = Image.Type.Filled;
        fill.fillMethod = Image.FillMethod.Horizontal;
        fill.fillAmount = 0.2f;
        fill.color = new Color(0.35f, 0.85f, 0.55f);
        fill.raycastTarget = false;
        var cap = Txt(UI("Titulo", rt), "ESTRÉS", 13, new Color(0.75f, 0.82f, 0.9f), TextAlignmentOptions.Left, FontStyles.Bold);
        Place(cap.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1), new Vector2(14, -8), new Vector2(80, 20));
        rt.gameObject.SetActive(false);
        return rt.gameObject;
    }

    static DorsalMinigame BuildMinigame(Transform root, Transform hud, Camera cam)
    {
        var old = hud.Find("MinijuegoPanel");
        if (old != null) Object.DestroyImmediate(old.gameObject);

        var panel = UI("MinijuegoPanel", hud);
        Stretch(panel);
        var pause = hud.Find("PausePanel");
        if (pause != null) panel.SetSiblingIndex(pause.GetSiblingIndex());

        var top = UI("Encabezado", panel);
        Place(top, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -18), new Vector2(880, 132));
        Img(top, new Color(0.03f, 0.07f, 0.14f, 0.86f));
        var title = Txt(UI("Titulo", top), "Rasurado", 32, Color.white, TextAlignmentOptions.Center, FontStyles.Bold);
        Place(title.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1), new Vector2(0, -10), new Vector2(-40, 44));
        var instr = Txt(UI("Instrucciones", top), "", 20, new Color(0.85f, 0.9f, 0.97f), TextAlignmentOptions.Center);
        Place(instr.rectTransform, new Vector2(0, 0), new Vector2(1, 1), new Vector2(0.5f, 0.5f), new Vector2(0, -22), new Vector2(-50, -56));

        var statsBg = UI("Estadisticas", panel);
        Place(statsBg, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -158), new Vector2(880, 64));
        Img(statsBg, new Color(0.03f, 0.07f, 0.14f, 0.72f));
        var stats = Txt(UI("Texto", statsBg), "", 21, Color.white, TextAlignmentOptions.Center);
        Stretch(stats.rectTransform, 16, 16, 4, 4);

        var finish = Btn(panel, "BotonTerminar", "Terminar (Enter)", new Color(0.2f, 0.72f, 0.48f), 26);
        Place((RectTransform)finish.transform, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 40), new Vector2(340, 70));

        var scaleRoot = UI("Escala", panel);
        Place(scaleRoot, new Vector2(0, 0), new Vector2(0, 0), new Vector2(0, 0), new Vector2(50, 50), new Vector2(200, 60));
        var sbar = UI("Barra", scaleRoot);
        Place(sbar, new Vector2(0, 0), new Vector2(0, 0), new Vector2(0, 0), new Vector2(0, 0), new Vector2(60, 10));
        Img(sbar, Color.white, false);
        var sLabel = Txt(UI("Texto", scaleRoot), "1 cm", 22, Color.white, TextAlignmentOptions.BottomLeft, FontStyles.Bold);
        Place(sLabel.rectTransform, new Vector2(0, 0), new Vector2(0, 0), new Vector2(0, 0), new Vector2(0, 14), new Vector2(160, 32));
        foreach (var t in new[] { sLabel }) { t.outlineWidth = 0.2f; t.outlineColor = Color.black; }

        var brush = UI("Pincel", panel);
        Place(brush, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(24, 24));
        var bimg = brush.gameObject.AddComponent<Image>();
        bimg.sprite = RingSprite();
        bimg.raycastTarget = false;

        var card = UI("Resultado", panel);
        Place(card, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -20), new Vector2(780, 440));
        Img(card, new Color(0.04f, 0.09f, 0.18f, 0.96f), true, true);
        var rTitle = Txt(UI("Titulo", card), "Resultado", 30, Color.white, TextAlignmentOptions.Center, FontStyles.Bold);
        Place(rTitle.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1), new Vector2(0, -24), new Vector2(-40, 44));
        var rGrade = Txt(UI("Nota", card), "Nota: 10.0 / 10", 46, new Color(0.45f, 0.9f, 0.65f), TextAlignmentOptions.Center, FontStyles.Bold);
        Place(rGrade.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1), new Vector2(0, -78), new Vector2(-40, 60));
        var rVerdict = Txt(UI("Veredicto", card), "", 24, new Color(1f, 0.92f, 0.7f), TextAlignmentOptions.Center, FontStyles.Bold);
        Place(rVerdict.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1), new Vector2(0, -146), new Vector2(-60, 64));
        var rDetail = Txt(UI("Detalle", card), "", 21, new Color(0.85f, 0.9f, 0.97f), TextAlignmentOptions.Center);
        Place(rDetail.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1), new Vector2(0, -216), new Vector2(-60, 100));
        var cont = Btn(card, "BotonContinuar", "Continuar (Enter)", new Color(0.16f, 0.47f, 0.86f), 26);
        Place((RectTransform)cont.transform, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 28), new Vector2(340, 70));
        card.gameObject.SetActive(false);

        panel.gameObject.SetActive(false);

        var mgGo = new GameObject("Minijuego");
        mgGo.transform.SetParent(root, false);
        var mg = mgGo.AddComponent<DorsalMinigame>();
        mg.minigameCamera = cam;
        mg.patchMaterialTemplate = PatchMaterial();
        mg.furParticleMaterial = FurMaterial();
        mg.panel = panel.gameObject;
        mg.titleText = title;
        mg.instructionsText = instr;
        mg.statsText = stats;
        mg.finishButton = finish;
        mg.brushCursor = brush;
        mg.scaleBar = sbar;
        mg.scaleBarLabel = sLabel;
        mg.resultCard = card.gameObject;
        mg.resultTitle = rTitle;
        mg.resultGrade = rGrade;
        mg.resultVerdict = rVerdict;
        mg.resultDetail = rDetail;
        mg.continueButton = cont;
        return mg;
    }

    // =====================================================================
    //  LevelDirector, botones y selector de nivel
    // =====================================================================

    static LevelDirector BuildDirector(GameObject level2Root, ScaleDevice scale)
    {
        var go = new GameObject("LevelDirector");
        var d = go.AddComponent<LevelDirector>();
        var l1 = Object.FindAnyObjectByType<Level1Manager>(FindObjectsInactive.Include);
        var l2 = level2Root.GetComponent<Level2Manager>();

        var piz = GameObject.Find("Salon/Pizarron");
        TMP_Text bt = null, bs = null, bn = null;
        if (piz != null)
        {
            bt = piz.transform.Find("Titulo")?.GetComponent<TMP_Text>();
            bs = piz.transform.Find("Pasos")?.GetComponent<TMP_Text>();
            bn = piz.transform.Find("Nota")?.GetComponent<TMP_Text>();
        }
        d.boardTitle = bt; d.boardSteps = bs; d.boardNote = bn;

        d.levels = new[]
        {
            new LevelDirector.LevelSetup
            {
                title = "Nivel 1 · Preparación de material estéril",
                controller = l1,
                objects = l1 != null ? new[] { l1.gameObject } : new GameObject[0],
                boardTitle = "Nivel 1 — Preparación y esterilización del material",
                boardSteps = "1. Toma solo el material correcto para la práctica.\n2. Si el material no está estéril, llévalo al autoclave.\n3. Espera a que termine el ciclo y retíralo.\n4. Coloca todo el material en la mesa de química.",
                boardNote = "¡Todo el material debe estar estéril!"
            },
            new LevelDirector.LevelSetup
            {
                title = "Nivel 2 · Preparación del animal",
                controller = l2,
                objects = new[] { level2Root },
                boardTitle = "Nivel 2 — Preparación del animal (conejo)",
                boardSteps = "1. Ponte guantes y saca al conejo de la jaula con cuidado.\n2. Tara la báscula y pesa al conejo: rango válido 2.5 – 4.0 kg.\n   Fuera de rango: repórtalo como NO APTO (se trae otro).\n3. Colócalo sobre el paño y rasura la zona dorsal (7 × 5 cm).\n4. Desinfecta con alcohol al 70 %, del centro hacia afuera.",
                boardNote = "¡Muévete despacio: el estrés altera al animal!"
            }
        };

        var matN1 = GameObject.Find("Mat_N1");
        var matInc = GameObject.Find("Mat_incorrectos");
        d.level1Props = new[] { matN1 != null ? matN1.transform : null, matInc != null ? matInc.transform : null };

        d.scale = scale.transform;
        d.scaleLevel1Position = new Vector3(-1.734f, 1.077f, -1.582f);
        d.scaleLevel1Euler = new Vector3(270f, 0f, 0f);
        d.scaleLevel2Position = new Vector3(-0.5f, TableTop, -1.25f);
        d.scaleLevel2Euler = new Vector3(270f, 0f, 0f);
        // La báscula se deja en la escena con la pose del nivel 1.
        scale.transform.SetPositionAndRotation(d.scaleLevel1Position, Quaternion.Euler(d.scaleLevel1Euler));
        return d;
    }

    static void RewireResultButtons(Transform hud, GameFlow flow)
    {
        void Wire(string path, UnityEngine.Events.UnityAction action)
        {
            var b = hud.Find(path)?.GetComponent<Button>();
            if (b == null) return;
            for (int i = b.onClick.GetPersistentEventCount() - 1; i >= 0; i--) UnityEventTools.RemovePersistentListener(b.onClick, i);
            UnityEventTools.AddPersistentListener(b.onClick, action);
            EditorUtility.SetDirty(b);
        }
        Wire("CompletePanel/RetryButton", flow.RestartLevel);
        Wire("CompletePanel/ContinueButton", flow.ContinueToNextLevel);
        Wire("CompletePanel/MenuButton", flow.GoToMainMenu);
    }

    static void BuildLevelSelector()
    {
        var menu = Object.FindAnyObjectByType<MainMenuUI>(FindObjectsInactive.Include);
        if (menu == null || menu.modePanel == null) return;
        var mp = menu.modePanel.transform;
        var old = mp.Find("SelectorNivel");
        if (old != null) Object.DestroyImmediate(old.gameObject);
        var oldD = mp.Find("DescripcionNivel");
        if (oldD != null) Object.DestroyImmediate(oldD.gameObject);

        var sub = mp.Find("Subtitulo")?.GetComponent<TMP_Text>();
        if (sub != null) sub.text = "Elige el nivel y el modo de juego.";

        var row = UI("SelectorNivel", mp);
        Place(row, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -228), new Vector2(900, 58));
        var hl = row.gameObject.AddComponent<HorizontalLayoutGroup>();
        hl.spacing = 20;
        hl.childControlWidth = true; hl.childControlHeight = true;
        hl.childForceExpandWidth = true; hl.childForceExpandHeight = true;
        var b1 = Btn(row, "Nivel1", "Nivel 1 · Material estéril", new Color(0.16f, 0.47f, 0.86f), 22);
        var b2 = Btn(row, "Nivel2", "Nivel 2 · Preparación del animal", new Color(0.18f, 0.24f, 0.34f), 22);

        var desc = Txt(UI("DescripcionNivel", mp), "", 19, new Color(0.8f, 0.86f, 0.95f), TextAlignmentOptions.Center);
        Place(desc.rectTransform, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -296), new Vector2(1180, 64));

        foreach (var n in new[] { "TarjetaEducativo", "TarjetaEvaluacion" })
        {
            var c = mp.Find(n) as RectTransform;
            if (c == null) continue;
            c.anchoredPosition = new Vector2(c.anchoredPosition.x, -92f);
            c.sizeDelta = new Vector2(c.sizeDelta.x, 430f);
        }

        menu.level1Button = b1;
        menu.level2Button = b2;
        menu.levelDescription = desc;
        EditorUtility.SetDirty(menu);
    }

    // =====================================================================
    //  Materiales y texturas
    // =====================================================================

    static Material Mat(string name, Color c, float smooth, float metal)
    {
        string path = MatDir + "/" + name + ".mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        var sh = Shader.Find("Universal Render Pipeline/Lit");
        if (m == null) { m = new Material(sh); AssetDatabase.CreateAsset(m, path); }
        m.shader = sh;
        m.SetColor("_BaseColor", c);
        m.SetFloat("_Smoothness", smooth);
        m.SetFloat("_Metallic", metal);
        EditorUtility.SetDirty(m);
        return m;
    }

    static Material PatchMaterial()
    {
        string path = MatDir + "/M_ParcheDorsal.mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        var sh = Shader.Find("Universal Render Pipeline/Lit");
        if (m == null) { m = new Material(sh); AssetDatabase.CreateAsset(m, path); }
        m.shader = sh;
        m.SetColor("_BaseColor", Color.white);
        m.SetFloat("_Smoothness", 0.3f);
        m.SetFloat("_Metallic", 0f);
        m.SetFloat("_Surface", 1f);
        m.SetFloat("_Blend", 0f);
        m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
        m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
        m.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
        m.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
        m.SetFloat("_ZWrite", 0f);
        m.SetFloat("_Cull", 2f);
        m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        m.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        m.SetOverrideTag("RenderType", "Transparent");
        m.renderQueue = (int)RenderQueue.Transparent;
        m.SetShaderPassEnabled("ShadowCaster", false);
        m.SetShaderPassEnabled("DepthOnly", false);
        EditorUtility.SetDirty(m);
        return m;
    }

    static Material FurMaterial()
    {
        string path = MatDir + "/M_Pelusa.mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        var sh = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        if (m == null) { m = new Material(sh); AssetDatabase.CreateAsset(m, path); }
        m.shader = sh;
        m.SetColor("_BaseColor", new Color(0.96f, 0.95f, 0.93f));
        EditorUtility.SetDirty(m);
        return m;
    }

    static Material RulerMaterial()
    {
        string texPath = MatDir + "/T_Regla.png";
        const int W = 1024, H = 192;
        var tex = new Texture2D(W, H, TextureFormat.RGBA32, false);
        var px = new Color32[W * H];
        var bg = new Color32(245, 236, 190, 255);
        var ink = new Color32(30, 30, 30, 255);
        for (int i = 0; i < px.Length; i++) px[i] = bg;
        // 16 cm de regla -> 15 cm marcados (margen de 0.5 cm por lado)
        float pxPerCm = W / 16f;
        for (int mm = 0; mm <= 150; mm++)
        {
            int x = Mathf.RoundToInt((0.5f + mm / 10f) * pxPerCm);
            int len = mm % 10 == 0 ? 110 : mm % 5 == 0 ? 75 : 45;
            int w = mm % 10 == 0 ? 9 : mm % 5 == 0 ? 6 : 4;
            for (int y = H - len; y < H; y++)
                for (int dx = 0; dx < w; dx++)
                {
                    int xx = Mathf.Clamp(x + dx - w / 2, 0, W - 1);
                    px[y * W + xx] = ink;
                }
        }
        tex.SetPixels32(px);
        tex.Apply();
        File.WriteAllBytes(texPath, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
        AssetDatabase.ImportAsset(texPath);
        var t = AssetDatabase.LoadAssetAtPath<Texture2D>(texPath);
        var m = Mat("M_Regla", Color.white, 0.4f, 0f);
        m.SetTexture("_BaseMap", t);
        return m;
    }

    static Sprite RingSprite()
    {
        string path = MatDir + "/T_Anillo.png";
        if (!File.Exists(path))
        {
            const int N = 128;
            var tex = new Texture2D(N, N, TextureFormat.RGBA32, false);
            var px = new Color32[N * N];
            float c = (N - 1) / 2f;
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float d = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c));
                    float a = Mathf.Clamp01(1f - Mathf.Abs(d - (c - 5f)) / 3f);
                    float inner = Mathf.Clamp01(1f - Mathf.Abs(d - (c - 9f)) / 2f) * 0.6f;
                    byte v = (byte)(a > inner ? 255 : 20);
                    px[y * N + x] = new Color32(v, v, v, (byte)(Mathf.Max(a, inner) * 255));
                }
            tex.SetPixels32(px);
            tex.Apply();
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path);
            var imp = (TextureImporter)AssetImporter.GetAtPath(path);
            imp.textureType = TextureImporterType.Sprite;
            imp.alphaIsTransparency = true;
            imp.mipmapEnabled = false;
            imp.SaveAndReimport();
        }
        return AssetDatabase.LoadAssetAtPath<Sprite>(path);
    }

    // =====================================================================
    //  Utilidades
    // =====================================================================

    static void EnsureFolders()
    {
        if (!AssetDatabase.IsValidFolder("Assets/Materials")) AssetDatabase.CreateFolder("Assets", "Materials");
        if (!AssetDatabase.IsValidFolder(MatDir)) AssetDatabase.CreateFolder("Assets/Materials", "Nivel2");
        if (!AssetDatabase.IsValidFolder(MeshDir)) AssetDatabase.CreateFolder(MatDir, "Mallas");
    }

    static void Remove(string rootName)
    {
        foreach (var go in UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects())
            if (go.name == rootName) Object.DestroyImmediate(go);
    }

    static GameObject Prim(PrimitiveType type, string name, Transform parent, Vector3 localPos, Vector3 localScale, Material mat, bool keepCollider = false)
    {
        var go = GameObject.CreatePrimitive(type);
        go.name = name;
        if (!keepCollider) Object.DestroyImmediate(go.GetComponent<Collider>());
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        go.transform.localScale = localScale;
        go.GetComponent<Renderer>().sharedMaterial = mat;
        return go;
    }

    static void AddBox(Transform parent, string name, Vector3 center, Vector3 size)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var b = go.AddComponent<BoxCollider>();
        b.center = center;
        b.size = size;
    }

    static Mesh BoxesMesh(string name, List<(Vector3 pos, Vector3 size)> boxes)
    {
        var cube = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
        var combine = new CombineInstance[boxes.Count];
        for (int i = 0; i < boxes.Count; i++)
            combine[i] = new CombineInstance { mesh = cube, transform = Matrix4x4.TRS(boxes[i].pos, Quaternion.identity, boxes[i].size) };
        var mesh = new Mesh { name = name, indexFormat = IndexFormat.UInt32 };
        mesh.CombineMeshes(combine, true, true);
        mesh.RecalculateBounds();
        string path = MeshDir + "/" + name + ".asset";
        var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (existing != null) { EditorUtility.CopySerialized(mesh, existing); Object.DestroyImmediate(mesh); return existing; }
        AssetDatabase.CreateAsset(mesh, path);
        return mesh;
    }

    static TextMeshPro Tmp3D(string name, Transform parent, string text, float size, Color color, Vector2 rect)
    {
        var go = new GameObject(name);
        if (parent != null) go.transform.SetParent(parent, false);
        var t = go.AddComponent<TextMeshPro>();
        if (TMP_Settings.defaultFontAsset != null) t.font = TMP_Settings.defaultFontAsset;
        t.text = text;
        t.fontSize = size;
        t.color = color;
        t.alignment = TextAlignmentOptions.Center;
        t.textWrappingMode = TextWrappingModes.Normal;
        t.richText = true;
        t.rectTransform.sizeDelta = rect;
        t.enableAutoSizing = true;
        t.fontSizeMin = size * 0.3f;
        t.fontSizeMax = size;
        return t;
    }

    static T Find<T>(Transform root, string path) where T : Component
    {
        var t = root.Find(path);
        return t != null ? t.GetComponent<T>() : null;
    }

    static TMP_Text FirstOrDefaultNamed(this TMP_Text[] arr, string name)
    {
        foreach (var t in arr) if (t.name == name) return t;
        return null;
    }

    static RectTransform UI(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.layer = 5;
        go.transform.SetParent(parent, false);
        return (RectTransform)go.transform;
    }

    static void Place(RectTransform rt, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 pos, Vector2 size)
    {
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.pivot = pivot;
        rt.sizeDelta = size;
        rt.anchoredPosition = pos;
    }

    static void Stretch(RectTransform rt, float left = 0, float right = 0, float top = 0, float bottom = 0)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.offsetMin = new Vector2(left, bottom);
        rt.offsetMax = new Vector2(-right, -top);
    }

    static Image Img(RectTransform rt, Color c, bool roundedSprite = true, bool raycast = false)
    {
        var img = rt.gameObject.AddComponent<Image>();
        if (roundedSprite) { img.sprite = rounded; img.type = Image.Type.Sliced; }
        img.color = c;
        img.raycastTarget = raycast;
        return img;
    }

    static TextMeshProUGUI Txt(RectTransform rt, string text, float size, Color c, TextAlignmentOptions align, FontStyles style = FontStyles.Normal)
    {
        var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
        if (TMP_Settings.defaultFontAsset != null) t.font = TMP_Settings.defaultFontAsset;
        t.text = text;
        t.fontSize = size;
        t.color = c;
        t.alignment = align;
        t.fontStyle = style;
        t.richText = true;
        t.raycastTarget = false;
        t.textWrappingMode = TextWrappingModes.Normal;
        return t;
    }

    static ColorBlock Colors(Color baseColor)
    {
        var cb = ColorBlock.defaultColorBlock;
        cb.normalColor = baseColor;
        cb.highlightedColor = Color.Lerp(baseColor, Color.white, 0.22f);
        cb.pressedColor = Color.Lerp(baseColor, Color.black, 0.25f);
        cb.selectedColor = baseColor;
        cb.disabledColor = new Color(0.3f, 0.3f, 0.3f, 0.6f);
        cb.colorMultiplier = 1f;
        cb.fadeDuration = 0.08f;
        return cb;
    }

    static Button Btn(Transform parent, string name, string label, Color baseColor, float fontSize)
    {
        var rt = UI(name, parent);
        rt.sizeDelta = new Vector2(300, 72);
        var img = Img(rt, Color.white, true, true);
        var b = rt.gameObject.AddComponent<Button>();
        b.targetGraphic = img;
        b.colors = Colors(baseColor);
        var t = Txt(UI("Label", rt), label, fontSize, Color.white, TextAlignmentOptions.Center, FontStyles.Bold);
        Stretch(t.rectTransform);
        return b;
    }
}
