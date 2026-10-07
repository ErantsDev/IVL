#if UNITY_EDITOR
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Arma (o rearma) en la escena todo lo del nivel 3 sobre la mesa de química: centrífuga animada,
/// gradilla con el tubo de sangre y el de balance, frasco de solución salina, vaso de desechos y
/// micropipeta; además lo registra en LevelDirector (nivel 3), en el menú y en el pizarrón.
/// </summary>
public static class Level3Builder
{
    const string MatFolder = "Assets/Materials/Nivel3";
    const float CentriScale = 2.6f;
    const float TubeScale = 1.1f;
    const float TubeLength = 1.6f;
    const float RackHeight = 0.55f;

    [MenuItem("Tools/Inmunologia/Construir Nivel 3")]
    public static void Build()
    {
        var scene = SceneManager.GetActiveScene();
        var old = scene.GetRootGameObjects().FirstOrDefault(g => g.name == "Nivel3");
        if (old != null) Object.DestroyImmediate(old);

        if (!AssetDatabase.IsValidFolder(MatFolder)) AssetDatabase.CreateFolder("Assets/Materials", "Nivel3");
        var liquid = Mat("Liquido", new Color(1f, 1f, 1f, 0.9f), true, 2990, 0.85f);
        var streamMat = Mat("Chorro", new Color(1f, 1f, 1f, 0.7f), true, 2995, 0.9f);
        var screenMat = Mat("Pantalla", new Color(0.02f, 0.05f, 0.04f, 1f), false, -1, 0.7f);
        var labelWhite = Mat("Etiqueta_Muestra", new Color(0.98f, 0.78f, 0.18f, 1f), false, -1, 0.2f);
        var labelBlue = Mat("Etiqueta_Balance", new Color(0.25f, 0.45f, 0.9f, 1f), false, -1, 0.2f);
        var ledMat = Mat("Piloto", new Color(0.25f, 0.5f, 1f, 1f), false, -1, 0.6f);
        ledMat.EnableKeyword("_EMISSION");
        ledMat.SetColor("_EmissionColor", new Color(0.25f, 0.5f, 1f) * 1.5f);
        var tipMat = Mat("Punta_Salina", new Color(0.7f, 0.85f, 1f, 0.55f), true, 2990, 0.9f);
        var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/TextMesh Pro/Resources/Fonts & Materials/LiberationSans SDF.asset");

        var root = new GameObject("Nivel3");
        var mgr = root.AddComponent<Level3Manager>();

        // ---------------- Centrífuga ----------------
        var centriGo = new GameObject("Centrifugadora");
        centriGo.transform.SetParent(root.transform, false);
        centriGo.transform.SetPositionAndRotation(new Vector3(-0.55f, 0.90f, -1.33f), Quaternion.identity);
        var model = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/objetos/Centrifugadora/Centrifugadora.fbx"));
        PrefabUtility.UnpackPrefabInstance(model, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
        model.name = "Modelo";
        model.transform.SetParent(centriGo.transform, false);
        model.transform.localPosition = new Vector3(0f, 0.061f * CentriScale, 0f);
        model.transform.localRotation = Quaternion.identity;
        model.transform.localScale = Vector3.one * CentriScale;
        var body = model.transform.Find("mesh_node");
        var rotorMesh = model.transform.Find("mesh_node.001");
        var lid = model.transform.Find("mesh_node.002");

        var hinge = new GameObject("Bisagra").transform;
        hinge.SetParent(model.transform, false);
        hinge.localPosition = new Vector3(0f, 0.055f, -0.08f);
        lid.SetParent(hinge, true);
        lid.gameObject.AddComponent<BoxCollider>();

        var pivot = new GameObject("Rotor").transform;
        pivot.SetParent(centriGo.transform, false);
        pivot.localPosition = new Vector3(0f, (0.061f + 0.0407f) * CentriScale, 0f);
        rotorMesh.SetParent(pivot, true);

        float rSlot = 0.032f * CentriScale, yTop = (0.055f - 0.0407f) * CentriScale, tilt = 20f, protrude = 0.035f, tubeLen = 0.1f * TubeLength;
        var slots = new Transform[2];
        for (int i = 0; i < 2; i++)
        {
            float th = i * Mathf.PI;
            var p = new Vector3(Mathf.Cos(th) * rSlot, yTop, Mathf.Sin(th) * rSlot);
            var u = new Vector3(-Mathf.Cos(th) * Mathf.Sin(tilt * Mathf.Deg2Rad), Mathf.Cos(tilt * Mathf.Deg2Rad), -Mathf.Sin(th) * Mathf.Sin(tilt * Mathf.Deg2Rad));
            var s = new GameObject("Posicion_" + (i + 1)).transform;
            s.SetParent(pivot, false);
            s.localPosition = p - u * (tubeLen - protrude);
            s.localRotation = Quaternion.FromToRotation(Vector3.up, u);
            slots[i] = s;
        }

        var col = centriGo.AddComponent<BoxCollider>();
        col.center = new Vector3(0f, 0.061f * CentriScale, 0f);
        col.size = new Vector3(0.2f, 0.122f, 0.2f) * CentriScale;

        // Pantalla al frente: se busca la superficie con un rayo.
        var mc = body.gameObject.AddComponent<MeshCollider>();
        Physics.SyncTransforms();
        Vector3 probe = centriGo.transform.TransformPoint(new Vector3(0f, (0.061f - 0.032f) * CentriScale, 0.6f));
        float frontZ = 0.1f * CentriScale;
        if (mc.Raycast(new Ray(probe, -centriGo.transform.forward), out var hit, 2f))
            frontZ = centriGo.transform.InverseTransformPoint(hit.point).z;
        Object.DestroyImmediate(mc);
        float dispY = (0.061f - 0.032f) * CentriScale; // debajo de la perilla: la tapa cerrada tapa la parte de arriba
        var scr = GameObject.CreatePrimitive(PrimitiveType.Cube);
        scr.name = "Pantalla";
        Object.DestroyImmediate(scr.GetComponent<Collider>());
        scr.transform.SetParent(centriGo.transform, false);
        scr.transform.localPosition = new Vector3(0f, dispY, frontZ + 0.004f);
        scr.transform.localScale = new Vector3(0.12f, 0.05f, 0.008f);
        scr.GetComponent<Renderer>().sharedMaterial = screenMat;
        var txtGo = new GameObject("Texto");
        txtGo.transform.SetParent(centriGo.transform, false);
        txtGo.transform.localPosition = new Vector3(0f, dispY, frontZ + 0.0085f);
        txtGo.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
        var tmp = txtGo.AddComponent<TextMeshPro>();
        if (font != null) tmp.font = font;
        tmp.rectTransform.sizeDelta = new Vector2(0.115f, 0.046f);
        tmp.fontSize = 0.18f;
        tmp.enableAutoSizing = true;
        tmp.fontSizeMin = 0.05f;
        tmp.fontSizeMax = 0.2f;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.lineSpacing = -20f;
        tmp.color = new Color(0.45f, 1f, 0.6f);
        tmp.text = "TAPA\nABIERTA";
        var led = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        led.name = "Piloto";
        Object.DestroyImmediate(led.GetComponent<Collider>());
        led.transform.SetParent(centriGo.transform, false);
        led.transform.localPosition = new Vector3(0.075f, dispY, frontZ + 0.002f);
        led.transform.localScale = Vector3.one * 0.012f;
        led.GetComponent<Renderer>().sharedMaterial = ledMat;

        var centri = centriGo.AddComponent<Centrifuge>();
        centri.lidHinge = hinge;
        centri.rotorPivot = pivot;
        centri.slots = slots;
        centri.display = tmp;
        centri.statusLight = led.GetComponent<Renderer>();
        hinge.localRotation = Quaternion.Euler(centri.lidOpenAngle, 0f, 0f);

        // ---------------- Gradilla y tubos ----------------
        var rackGo = new GameObject("Gradilla");
        rackGo.transform.SetParent(root.transform, false);
        rackGo.transform.SetPositionAndRotation(new Vector3(-0.05f, 0.90f, -1.30f), Quaternion.identity);
        var rackModel = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/objetos/tube_rack_lowpoly.fbx"));
        PrefabUtility.UnpackPrefabInstance(rackModel, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
        rackModel.name = "Modelo";
        var rackRot = rackModel.transform.localRotation;
        rackModel.transform.SetParent(rackGo.transform, false);
        rackModel.transform.localPosition = Vector3.zero;
        rackModel.transform.localRotation = rackRot;
        // Gradilla de perfil bajo (≈3.4 cm): así se ve el contenido de los tubos (el eje vertical del modelo es su Z).
        var rs = rackModel.transform.localScale;
        rackModel.transform.localScale = new Vector3(rs.x, rs.y, rs.z * RackHeight);
        var rcol = rackGo.AddComponent<BoxCollider>();
        rcol.center = new Vector3(0f, 0.031f * RackHeight, 0f);
        rcol.size = new Vector3(0.148f, 0.062f * RackHeight, 0.068f);
        var rack = rackGo.AddComponent<Level3Station>();
        rack.kind = Level3Station.Kind.Rack;
        rack.points = new Transform[10];
        float[] xs = { -0.052f, -0.026f, 0f, 0.026f, 0.052f };
        for (int row = 0; row < 2; row++)
            for (int c = 0; c < 5; c++)
            {
                var h = new GameObject("Hueco_" + (row * 5 + c)).transform;
                h.SetParent(rackGo.transform, false);
                h.localPosition = new Vector3(xs[c], 0.008f * RackHeight, row == 0 ? 0.015f : -0.015f);
                rack.points[row * 5 + c] = h;
            }

        var sample = MakeTube(root.transform, "TuboMuestra", false, liquid, labelWhite);
        var balance = MakeTube(root.transform, "TuboBalance", true, liquid, labelBlue);
        sample.transform.SetParent(rack.points[1], false);
        balance.transform.SetParent(rack.points[3], false);

        // ---------------- Frasco de solución salina ----------------
        var bottleGo = new GameObject("Frasco_SolucionSalina");
        bottleGo.transform.SetParent(root.transform, false);
        bottleGo.transform.SetPositionAndRotation(new Vector3(0.22f, 0.90f, -1.42f), Quaternion.identity);
        var bottle = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/objetos/saline_bottle_lowpoly.fbx"));
        PrefabUtility.UnpackPrefabInstance(bottle, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
        bottle.name = "Modelo";
        var bRot = bottle.transform.localRotation;
        bottle.transform.SetParent(bottleGo.transform, false);
        bottle.transform.localPosition = Vector3.zero;
        bottle.transform.localRotation = bRot;
        var cap = bottle.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "Bottle_Cap");
        if (cap != null)
        {
            // La tapa queda destapada sobre la mesa, junto al frasco.
            cap.SetParent(bottleGo.transform, true);
            cap.localPosition = new Vector3(0.075f, 0.0135f, 0.01f);
            cap.localRotation = cap.localRotation * Quaternion.Euler(180f, 0f, 0f);
            var cb = cap.GetComponent<Renderer>().bounds;
            cap.position += Vector3.up * (bottleGo.transform.position.y - cb.min.y);
        }
        var bcol = bottleGo.AddComponent<CapsuleCollider>();
        bcol.center = new Vector3(0f, 0.08f, 0f);
        bcol.radius = 0.034f;
        bcol.height = 0.16f;
        var saline = bottleGo.AddComponent<Level3Station>();
        saline.kind = Level3Station.Kind.Saline;
        var mouth = new GameObject("Boca").transform;
        mouth.SetParent(bottleGo.transform, false);
        mouth.localPosition = new Vector3(0f, 0.152f, 0f);
        saline.points = new[] { mouth };
        AddLabel(bottleGo.transform, "Solución salina\n<b>NaCl 0.9 %</b>", new Vector3(0f, 0.058f, 0.0285f), new Vector2(0.06f, 0.05f), Color.white, font);

        // ---------------- Vaso de desechos ----------------
        var wasteGo = new GameObject("Vaso_Desechos");
        wasteGo.transform.SetParent(root.transform, false);
        wasteGo.transform.SetPositionAndRotation(new Vector3(0.50f, 0.90f, -1.30f), Quaternion.identity);
        var beaker = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/objetos/3D Laboratory Environment with Appratus/Prefabs/Beaker.prefab"));
        PrefabUtility.UnpackPrefabInstance(beaker, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
        beaker.name = "Modelo";
        beaker.transform.SetParent(wasteGo.transform, false);
        beaker.transform.localPosition = Vector3.zero;
        foreach (var c in beaker.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(c);
        var glass = Mat("VidrioVaso", new Color(0.82f, 0.92f, 1f, 0.3f), true, 3000, 0.95f);
        foreach (var r in beaker.GetComponentsInChildren<Renderer>()) r.sharedMaterial = glass;
        var bb = beaker.GetComponentsInChildren<Renderer>().Select(r => r.bounds).Aggregate((a, b) => { a.Encapsulate(b); return a; });
        beaker.transform.position += Vector3.up * (wasteGo.transform.position.y - bb.min.y);
        float beakerH = bb.size.y;
        var wcol = wasteGo.AddComponent<CapsuleCollider>();
        wcol.center = new Vector3(0f, beakerH * 0.5f, 0f);
        wcol.radius = 0.052f;
        wcol.height = beakerH;
        var waste = wasteGo.AddComponent<Level3Station>();
        waste.kind = Level3Station.Kind.Waste;
        var rim = new GameObject("Borde").transform;
        rim.SetParent(wasteGo.transform, false);
        rim.localPosition = new Vector3(0f, beakerH, 0f);
        waste.points = new[] { rim };
        var level = new GameObject("NivelDesecho").transform;
        level.SetParent(wasteGo.transform, false);
        level.localPosition = new Vector3(0f, 0.005f, 0f);
        level.localScale = new Vector3(1f, 0.022f, 1f);
        var wl = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        wl.name = "Liquido";
        Object.DestroyImmediate(wl.GetComponent<Collider>());
        wl.transform.SetParent(level, false);
        wl.transform.localPosition = new Vector3(0f, 0.5f, 0f);
        wl.transform.localScale = new Vector3(0.088f, 0.5f, 0.088f);
        var wlr = wl.GetComponent<Renderer>();
        wlr.sharedMaterial = Mat("Desecho", new Color(0.93f, 0.92f, 0.62f, 0.55f), true, 2990, 0.9f);
        wlr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        AddLabel(wasteGo.transform, "<b>DESECHOS</b>\nbiológicos", new Vector3(0f, beakerH * 0.62f, 0.0505f), new Vector2(0.07f, 0.035f), new Color(1f, 0.35f, 0.3f), font);

        // ---------------- Micropipeta ----------------
        var srcPip = GameObject.Find("Micropipeta");
        var pip = Object.Instantiate(srcPip, root.transform);
        pip.name = "Micropipeta_N3";
        var lm = pip.GetComponent<LabMaterial>();
        if (lm != null) Object.DestroyImmediate(lm);
        pip.transform.SetPositionAndRotation(new Vector3(300f, 0f, 300f), Quaternion.identity);
        var rends = pip.GetComponentsInChildren<Renderer>();
        var tipR = rends.First(r => r.sharedMaterial != null && r.sharedMaterial.name.Contains("Punta"));
        var btnR = rends.First(r => r.sharedMaterial != null && r.sharedMaterial.name.Contains("Boton"));
        var tool = pip.AddComponent<MicropipetteTool>();
        tool.tipLocal = pip.transform.InverseTransformPoint(new Vector3(tipR.bounds.center.x, tipR.bounds.min.y, tipR.bounds.center.z));
        tool.topLocal = pip.transform.InverseTransformPoint(new Vector3(btnR.bounds.center.x, btnR.bounds.max.y, btnR.bounds.center.z));
        var tl = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        tl.name = "LiquidoPunta";
        Object.DestroyImmediate(tl.GetComponent<Collider>());
        tl.transform.SetParent(pip.transform, false);
        tl.transform.localPosition = tool.tipLocal + Vector3.up * 0.016f;
        tl.transform.localScale = new Vector3(0.0038f, 0.012f, 0.0038f);
        tl.GetComponent<Renderer>().sharedMaterial = tipMat;
        tl.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        tool.tipLiquid = tl.GetComponent<Renderer>();
        var prb = pip.GetComponent<Rigidbody>();
        prb.isKinematic = true;
        prb.useGravity = true;

        // Soporte de la micropipeta (la deja vertical, con la punta al aire).
        var standGo = new GameObject("Soporte_Micropipeta");
        standGo.transform.SetParent(root.transform, false);
        standGo.transform.SetPositionAndRotation(new Vector3(0.33f, 0.90f, -1.13f), Quaternion.identity);
        var standMat = Mat("Soporte", new Color(0.22f, 0.25f, 0.3f, 1f), false, -1, 0.45f);
        Part(standGo.transform, PrimitiveType.Cylinder, "Base", new Vector3(0f, 0.006f, -0.01f), new Vector3(0.085f, 0.006f, 0.085f), standMat);
        Part(standGo.transform, PrimitiveType.Cube, "Poste", new Vector3(0f, 0.135f, -0.04f), new Vector3(0.014f, 0.26f, 0.014f), standMat);
        Part(standGo.transform, PrimitiveType.Cube, "Brazo", new Vector3(0f, 0.255f, -0.024f), new Vector3(0.03f, 0.012f, 0.04f), standMat);
        var scol = standGo.AddComponent<BoxCollider>();
        scol.center = new Vector3(0f, 0.135f, -0.02f);
        scol.size = new Vector3(0.085f, 0.27f, 0.085f);
        var stand = standGo.AddComponent<Level3Station>();
        stand.kind = Level3Station.Kind.PipetteStand;
        var seat = new GameObject("Asiento").transform;
        seat.SetParent(standGo.transform, false);
        seat.localPosition = new Vector3(0f, 0.03f, 0f);
        stand.points = new[] { seat };
        var seatRot = tool.UprightRotation(-standGo.transform.forward);
        pip.transform.SetPositionAndRotation(tool.RootForTip(seat.position, seatRot), seatRot);

        // ---------------- Panel de control (UI) ----------------
        var panelGo = new GameObject("PanelCentrifuga");
        panelGo.transform.SetParent(root.transform, false);
        var panel = panelGo.AddComponent<CentrifugePanel>();
        var kit = Object.FindAnyObjectByType<SyringeKitMinigame>(FindObjectsInactive.Include);
        if (kit != null) { panel.panelSprite = kit.panelSprite; panel.font = kit.font; }

        // ---------------- Manager ----------------
        mgr.sample = sample;
        mgr.balance = balance;
        mgr.centrifuge = centri;
        mgr.panel = panel;
        mgr.rack = rack;
        mgr.salineBottle = saline;
        mgr.wasteBeaker = waste;
        mgr.pipetteStand = stand;
        mgr.pipette = tool;
        mgr.wasteLiquid = level;
        mgr.wasteLiquidRenderer = wlr;
        mgr.streamMaterial = streamMat;
        var l2 = Object.FindAnyObjectByType<Level2Manager>(FindObjectsInactive.Include);
        if (l2 != null)
        {
            mgr.markerMaterial = l2.markerMaterial;
            mgr.counterText = l2.counterText;
            mgr.toastText = l2.toastText;
            mgr.toastGroup = l2.toastGroup;
            mgr.modeText = l2.modeText;
            mgr.timerText = l2.timerText;
            mgr.objectivePanel = l2.objectivePanel;
            mgr.objectiveText = l2.objectiveText;
            mgr.checklistPanel = l2.checklistPanel;
            mgr.checklistText = l2.checklistText;
            mgr.mixGroup = l2.stressGroup;
            mgr.mixFill = l2.stressFill;
            mgr.mixLabel = l2.stressLabel;
            mgr.completePanel = l2.completePanel;
            mgr.completeTitleText = l2.completeTitleText;
            mgr.completeTimeText = l2.completeTimeText;
            mgr.gradeText = l2.gradeText;
            mgr.resultDetailText = l2.resultDetailText;
            mgr.continueButton = l2.continueButton;
        }
        sample.looseRoot = root.transform;
        balance.looseRoot = root.transform;

        // ---------------- LevelDirector, menú y pizarrón ----------------
        var dir = Object.FindAnyObjectByType<LevelDirector>(FindObjectsInactive.Include);
        if (dir != null)
        {
            var list = dir.levels.ToList();
            while (list.Count < 3) list.Add(new LevelDirector.LevelSetup());
            var s = list[2];
            s.title = "Nivel 3 · Suspensión de glóbulos rojos";
            s.controller = mgr;
            s.objects = new[] { root };
            s.boardTitle = "Nivel 3 — Suspensión de glóbulos rojos (lavado de eritrocitos)";
            s.boardSteps = "1. Agrega 6 mL de solución salina (NaCl 0.9 %) con la micropipeta.\n" +
                           "2. Resuspende invirtiendo el tubo suavemente.\n" +
                           "3. Coloca muestra y balance opuestos en el rotor; cierra la tapa.\n" +
                           "4. Centrifuga a 2500 rpm durante 5 min.\n" +
                           "5. Decanta el sobrenadante y repite hasta que quede transparente.";
            s.boardNote = "¡Nunca abras la centrífuga en movimiento!";
            dir.levels = list.ToArray();
            EditorUtility.SetDirty(dir);
        }
        root.SetActive(dir != null && dir.AppliedLevel == 3);

        var menu = Object.FindAnyObjectByType<MainMenuUI>(FindObjectsInactive.Include);
        if (menu != null && menu.level2Button != null)
        {
            if (menu.level3Button == null)
            {
                var b3 = Object.Instantiate(menu.level2Button.gameObject, menu.level2Button.transform.parent);
                b3.name = "Nivel3";
                menu.level3Button = b3.GetComponent<UnityEngine.UI.Button>();
            }
            var label = menu.level3Button.GetComponentInChildren<TMP_Text>();
            if (label != null) label.text = "Nivel 3 · Glóbulos rojos";
            var parent = (RectTransform)menu.level3Button.transform.parent;
            parent.sizeDelta = new Vector2(Mathf.Max(parent.sizeDelta.x, 960f), parent.sizeDelta.y);
            EditorUtility.SetDirty(menu);
        }

        EditorSceneManager.MarkSceneDirty(scene);
        Debug.Log("[Nivel3] Construido.");
    }

    static void Part(Transform parent, PrimitiveType type, string name, Vector3 pos, Vector3 scale, Material mat)
    {
        var go = GameObject.CreatePrimitive(type);
        go.name = name;
        Object.DestroyImmediate(go.GetComponent<Collider>());
        go.transform.SetParent(parent, false);
        go.transform.localPosition = pos;
        go.transform.localScale = scale;
        go.GetComponent<Renderer>().sharedMaterial = mat;
    }

    static SampleTube MakeTube(Transform root, string name, bool isBalance, Material liquid, Material label)
    {
        var go = new GameObject(name);
        go.transform.SetParent(root, false);
        var rb = go.AddComponent<Rigidbody>();
        rb.mass = 0.03f;
        rb.isKinematic = true;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
        var cc = go.AddComponent<CapsuleCollider>();
        cc.radius = 0.0086f;
        cc.height = 0.1f * TubeLength;
        cc.center = new Vector3(0f, 0.05f * TubeLength, 0f);
        go.AddComponent<Grabbable>();

        var m = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/objetos/test_tube_lowpoly.fbx"));
        PrefabUtility.UnpackPrefabInstance(m, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
        m.name = "Modelo";
        var rot = m.transform.localRotation;
        var sc = m.transform.localScale;
        m.transform.SetParent(go.transform, false);
        m.transform.localPosition = Vector3.zero;
        m.transform.localRotation = rot;
        m.transform.localScale = new Vector3(sc.x * TubeScale, sc.y * TubeScale, sc.z * TubeLength); // el eje del tubo es Z del modelo
        foreach (var t in m.GetComponentsInChildren<Transform>(true).Where(t => t.name.Contains("Liquid")).ToArray())
            Object.DestroyImmediate(t.gameObject);
        foreach (var c in m.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
        var tubeGlass = Mat("VidrioTubo", new Color(0.9f, 0.96f, 1f, 0.16f), true, 3000, 0.75f);
        foreach (var r in m.GetComponentsInChildren<Renderer>(true)) r.sharedMaterial = tubeGlass;

        var lab = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        lab.name = "Etiqueta";
        Object.DestroyImmediate(lab.GetComponent<Collider>());
        lab.transform.SetParent(go.transform, false);
        lab.transform.localPosition = new Vector3(0f, 0.1f * TubeLength - 0.017f, 0f);
        lab.transform.localScale = new Vector3(0.0182f, 0.0045f, 0.0182f);
        lab.GetComponent<Renderer>().sharedMaterial = label;

        var hb = new GameObject("ZonaDeAgarre");
        hb.transform.SetParent(go.transform, false);
        var hc = hb.AddComponent<CapsuleCollider>();
        hc.isTrigger = true;
        hc.radius = 0.026f;
        hc.height = 0.18f;
        hc.center = new Vector3(0f, 0.08f, 0f);
        hb.AddComponent<GrabHitbox>();

        var tube = go.AddComponent<SampleTube>();
        tube.isBalance = isBalance;
        tube.liquidMaterial = liquid;
        tube.innerRadius = 0.0069f;
        tube.innerBottom = 0.0017f;
        tube.heightPer10Ml = 0.125f;
        tube.bloodMl = 3f;
        tube.balanceMl = 9f;
        return tube;
    }

    static void AddLabel(Transform parent, string text, Vector3 localPos, Vector2 size, Color color, TMP_FontAsset font)
    {
        var go = new GameObject("Rotulo");
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        go.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
        var t = go.AddComponent<TextMeshPro>();
        if (font != null) t.font = font;
        t.rectTransform.sizeDelta = size;
        t.enableAutoSizing = true;
        t.fontSizeMin = 0.02f;
        t.fontSizeMax = 0.12f;
        t.alignment = TextAlignmentOptions.Center;
        t.color = color;
        t.text = text;
    }

    static Material Mat(string name, Color color, bool transparent, int queue, float smooth)
    {
        string path = MatFolder + "/" + name + ".mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null)
        {
            m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            AssetDatabase.CreateAsset(m, path);
        }
        m.SetColor("_BaseColor", color);
        m.SetFloat("_Smoothness", smooth);
        if (transparent)
        {
            m.SetFloat("_Surface", 1f);
            m.SetFloat("_Blend", 0f);
            m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_SrcBlendAlpha", (float)UnityEngine.Rendering.BlendMode.One);
            m.SetFloat("_DstBlendAlpha", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_ZWrite", 0f);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.SetOverrideTag("RenderType", "Transparent");
            m.renderQueue = queue > 0 ? queue : 3000;
        }
        else
        {
            m.SetFloat("_Surface", 0f);
            m.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.SetOverrideTag("RenderType", "Opaque");
            m.renderQueue = -1;
        }
        EditorUtility.SetDirty(m);
        return m;
    }
}
#endif
