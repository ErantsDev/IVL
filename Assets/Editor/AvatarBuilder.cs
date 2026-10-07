using System;
using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>
/// Avatares del jugador, animaciones, espejo y registro del alumno:
///   1. Configura los modelos (Avatar_Hombre.fbx / Avatar_Mujer.fbx) como Humanoid y les pone materiales URP.
///   2. Genera las animaciones humanoides (reposo, caminar, correr, en el aire, aterrizar y saludo)
///      a partir de poses definidas por ángulos de cada hueso.
///   3. Crea el Animator Controller (locomoción + capa de gestos) y los prefabs de los avatares.
///   4. Prepara al jugador (capa "Avatar", PlayerAvatar, cámaras) y construye el espejo en la pared oeste.
///   5. Arma el formulario "Datos del alumno" en el menú y las etiquetas con el nombre en HUD/resultados.
/// Se puede volver a ejecutar sin duplicar nada.
/// </summary>
public static class AvatarBuilder
{
    public const string MaleFbx = "Assets/objetos/Avatares/Avatar_Hombre.fbx";
    public const string FemaleFbx = "Assets/objetos/Avatares/Avatar_Mujer.fbx";
    const string MatDir = "Assets/Materials/Avatares";
    const string AnimDir = "Assets/Animaciones/Avatar";
    const string PrefabDir = "Assets/Prefabs/Avatares";
    public const string ControllerPath = AnimDir + "/Avatar.controller";
    public const string MalePrefabPath = PrefabDir + "/Avatar_Hombre.prefab";
    public const string FemalePrefabPath = PrefabDir + "/Avatar_Mujer.prefab";

    /// <summary>Altura de los ojos de los avatares (la cámara del jugador está a 1.60 m).</summary>
    const float EyeHeight = 1.56f;
    const float MaleEyeY = 1.475f, FemaleEyeY = 1.47f;

    [MenuItem("Tools/Inmunologia/Avatares, espejo y registro del alumno")]
    public static void BuildAll()
    {
        ConfigureModels();
        BuildClips();
        BuildController();
        BuildPrefabs();
        SetupPlayer();
        BuildMirror();
        BuildStudentForm();
        AddStudentLabels();
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        AssetDatabase.SaveAssets();
        Debug.Log("[AvatarBuilder] Avatares, animaciones, espejo y formulario del alumno listos.");
    }

    // =====================================================================
    //  1. Modelos y materiales
    // =====================================================================

    struct MatDef { public string name; public Color linear; public float smooth; public float metal; }

    static readonly MatDef[] Mats =
    {
        new MatDef { name = "M_Chem_Blush", linear = new Color(0.92f, 0.55f, 0.52f), smooth = 0.25f },
        new MatDef { name = "M_Chem_Pants", linear = new Color(0.13f, 0.14f, 0.165f), smooth = 0.2f },
        new MatDef { name = "M_Chem_Shirt", linear = new Color(0.18f, 0.22f, 0.28f), smooth = 0.2f },
        new MatDef { name = "M_Chem_Coat", linear = new Color(0.945f, 0.948f, 0.955f), smooth = 0.3f },
        new MatDef { name = "M_Chem_Skin", linear = new Color(0.91f, 0.72f, 0.58f), smooth = 0.3f },
        new MatDef { name = "M_Chem_EyeWhite", linear = new Color(0.97f, 0.97f, 0.965f), smooth = 0.6f },
        new MatDef { name = "M_Chem_EyeDark", linear = new Color(0.05f, 0.045f, 0.05f), smooth = 0.8f },
        new MatDef { name = "M_Chem_Brow", linear = new Color(0.22f, 0.14f, 0.09f), smooth = 0.1f },
        new MatDef { name = "M_Chem_Mouth", linear = new Color(0.55f, 0.24f, 0.24f), smooth = 0.3f },
        new MatDef { name = "M_Chem_LipF", linear = new Color(0.62f, 0.22f, 0.26f), smooth = 0.4f },
        new MatDef { name = "M_Chem_Hair", linear = new Color(0.22f, 0.14f, 0.09f), smooth = 0.15f },
        new MatDef { name = "M_Chem_HairF", linear = new Color(0.33f, 0.15f, 0.11f), smooth = 0.2f },
        new MatDef { name = "M_Chem_HairTie", linear = new Color(0.15f, 0.13f, 0.14f), smooth = 0.3f },
        new MatDef { name = "M_Chem_GoggleFrame", linear = new Color(0.12f, 0.125f, 0.135f), smooth = 0.45f },
        new MatDef { name = "M_Chem_GoggleLens", linear = new Color(0.18f, 0.42f, 0.47f), smooth = 0.9f, metal = 0.1f },
        new MatDef { name = "M_Chem_Shoes", linear = new Color(0.06f, 0.06f, 0.065f), smooth = 0.35f },
    };

    public static void ConfigureModels()
    {
        EnsureFolder(MatDir);
        var lit = Shader.Find("Universal Render Pipeline/Lit");
        var created = new Dictionary<string, Material>();
        foreach (var d in Mats)
        {
            string path = MatDir + "/" + d.name.Replace("M_Chem_", "Avatar_") + ".mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(lit); AssetDatabase.CreateAsset(m, path); }
            m.shader = lit;
            // Los colores del glTF están en espacio lineal; SetColor espera sRGB.
            m.SetColor("_BaseColor", d.linear.gamma);
            m.SetFloat("_Smoothness", d.smooth);
            m.SetFloat("_Metallic", d.metal);
            EditorUtility.SetDirty(m);
            created[d.name] = m;
        }

        foreach (var p in new[] { MaleFbx, FemaleFbx })
        {
            var imp = (ModelImporter)AssetImporter.GetAtPath(p);
            if (imp == null) { Debug.LogError("[AvatarBuilder] No se encontró " + p); continue; }
            imp.bakeAxisConversion = true;
            imp.animationType = ModelImporterAnimationType.Human;
            imp.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            imp.importBlendShapes = false;
            imp.importCameras = false;
            imp.importLights = false;
            imp.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;

            // El saludo del modelo (brazo izquierdo en pose T) se reemplaza por uno generado (ver WaveGesture).
            imp.importAnimation = false;
            foreach (var kv in created)
                imp.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), kv.Key), kv.Value);
            imp.SaveAndReimport();
        }
    }

    // =====================================================================
    //  2. Animaciones humanoides generadas a partir de poses
    // =====================================================================

    /// <summary>Esqueleto de referencia en pose T, con el que se "posa" cada cuadro.</summary>
    class Rig
    {
        public GameObject root;
        public Animator animator;
        public HumanPoseHandler handler;
        public readonly Dictionary<Transform, Quaternion> restLocal = new Dictionary<Transform, Quaternion>();
        public readonly Dictionary<Transform, Quaternion> restWorld = new Dictionary<Transform, Quaternion>();
        public Transform hips;
        public Vector3 hipsRestLocalPos, hipsRestWorld;
        public Transform lFoot, rFoot, lToes, rToes;
        public float footRestY, toesRestY;
        public Transform this[HumanBodyBones b] => animator.GetBoneTransform(b);
    }

    class Pose
    {
        public readonly Dictionary<HumanBodyBones, Quaternion> rot = new Dictionary<HumanBodyBones, Quaternion>();
        public Vector3 hipsOffset;
        public bool ground = true;
        public float lift; // se suma después de apoyar los pies (fase de vuelo al correr)

        public void Add(HumanBodyBones b, Quaternion q)
        {
            rot[b] = rot.TryGetValue(b, out var prev) ? q * prev : q;
        }
    }

    static Quaternion Q(Vector3 axis, float deg) => Quaternion.AngleAxis(deg, axis);
    static readonly Vector3 X = Vector3.right, Y = Vector3.up, Z = Vector3.forward;

    // Lado: izquierdo = -1 (el personaje mira a +Z, su izquierda está en -X), derecho = +1.
    static void Leg(Pose p, int side, float swing, float knee, float ankle, float spread = 0f)
    {
        bool left = side < 0;
        p.Add(left ? HumanBodyBones.LeftUpperLeg : HumanBodyBones.RightUpperLeg, Q(Z, side * spread) * Q(X, -swing));
        p.Add(left ? HumanBodyBones.LeftLowerLeg : HumanBodyBones.RightLowerLeg, Q(X, knee));
        p.Add(left ? HumanBodyBones.LeftFoot : HumanBodyBones.RightFoot, Q(X, ankle));
    }

    // down: grados que baja el brazo desde la pose T; forward: balanceo hacia adelante; elbow: flexión del codo.
    static void Arm(Pose p, int side, float down, float forward, float elbow)
    {
        bool left = side < 0;
        p.Add(left ? HumanBodyBones.LeftUpperArm : HumanBodyBones.RightUpperArm, Q(X, -forward) * Q(Z, -side * down));
        p.Add(left ? HumanBodyBones.LeftLowerArm : HumanBodyBones.RightLowerArm, Q(Y, -side * elbow));
    }

    static void Torso(Pose p, float spineLean, float chestLean, float chestTwist, float headPitch, float hipsYaw = 0f, float hipsRoll = 0f)
    {
        if (hipsYaw != 0f || hipsRoll != 0f) p.Add(HumanBodyBones.Hips, Q(Y, hipsYaw) * Q(Z, hipsRoll));
        p.Add(HumanBodyBones.Spine, Q(X, spineLean) * Q(Y, -hipsYaw * 0.5f));
        p.Add(HumanBodyBones.Chest, Q(Y, chestTwist) * Q(X, chestLean));
        p.Add(HumanBodyBones.Neck, Q(X, -(spineLean + chestLean) * 0.35f));
        p.Add(HumanBodyBones.Head, Q(X, headPitch - (spineLean + chestLean) * 0.3f));
    }

    static float Pos(float v) => v > 0f ? v : 0f;
    static float Pow(float v, float e) => Mathf.Pow(Pos(v), e);

    static Pose Idle(float t, float len)
    {
        float a = 2f * Mathf.PI * t / len;
        float b = Mathf.Sin(a);
        var p = new Pose();
        Torso(p, 1.5f + 0.6f * b, 0.8f + 0.7f * b, 0f, 0.8f * Mathf.Sin(a + 1f), 0f, 1.2f * Mathf.Sin(a + 0.4f));
        p.hipsOffset = new Vector3(0.008f * Mathf.Sin(a + 0.4f), 0f, 0f);
        Leg(p, -1, 1f, 3f, -2f, 3f);
        Leg(p, 1, 1f, 3f, -2f, 3f);
        Arm(p, -1, 79f + 1.2f * b, 5f, 12f + 2f * b);
        Arm(p, 1, 79f + 1.2f * b, 5f, 12f + 2f * b);
        return p;
    }

    static Pose Walk(float t, float len)
    {
        float a = 2f * Mathf.PI * t / len;
        var p = new Pose();
        for (int side = -1; side <= 1; side += 2)
        {
            float ph = side < 0 ? a : a + Mathf.PI;
            float s = Mathf.Sin(ph), c = Mathf.Cos(ph);
            float swing = 25f * s;
            float knee = 5f + 46f * Pow(c, 1.5f);
            float ankle = (swing - knee) * 0.85f - 7f * Pos(c);
            Leg(p, side, swing, knee, ankle, 2f);
            // Brazos al revés que las piernas.
            float fwd = -19f * s;
            Arm(p, side, 78f, fwd, 14f + 14f * Pos(fwd / 19f));
        }
        float sa = Mathf.Sin(a);
        Torso(p, 4f, 2f, -7f * sa, 0f, 5f * sa, 0f);
        return p;
    }

    static Pose Run(float t, float len)
    {
        float a = 2f * Mathf.PI * t / len;
        var p = new Pose();
        for (int side = -1; side <= 1; side += 2)
        {
            float ph = side < 0 ? a : a + Mathf.PI;
            float s = Mathf.Sin(ph), c = Mathf.Cos(ph);
            float swing = 10f + 38f * s;
            float knee = 20f + 70f * Pow(c, 1.2f);
            float ankle = (swing - knee) * 0.5f + 12f;
            Leg(p, side, swing, knee, ankle, 1.5f);
            float fwd = 6f - 36f * s;
            Arm(p, side, 70f, fwd, 82f + 8f * s);
        }
        float sa = Mathf.Sin(a);
        Torso(p, 9f, 5f, -10f * sa, -2f, 7f * sa, 0f);
        p.lift = 0.035f * Pos(-Mathf.Cos(2f * a));
        return p;
    }

    static Pose Air(float t, float len)
    {
        float a = 2f * Mathf.PI * t / len;
        float w = Mathf.Sin(a);
        var p = new Pose { ground = false };
        Leg(p, -1, 42f + 3f * w, 72f, 22f, 4f);
        Leg(p, 1, 30f - 3f * w, 58f, 25f, 4f);
        Arm(p, -1, 28f + 4f * w, 28f, 35f);
        Arm(p, 1, 28f - 4f * w, 24f, 35f);
        Torso(p, 7f, 3f, 0f, -6f);
        p.hipsOffset = new Vector3(0f, 0.02f, 0f);
        return p;
    }

    static Pose Land(float t, float len)
    {
        float k = Mathf.Pow(Mathf.Max(0f, Mathf.Sin(Mathf.PI * Mathf.Clamp01(t / len))), 0.8f);
        var p = new Pose();
        Leg(p, -1, 32f * k, 64f * k, -32f * k, 3f);
        Leg(p, 1, 28f * k, 58f * k, -30f * k, 3f);
        Arm(p, -1, 78f - 14f * k, 10f * k, 12f + 22f * k);
        Arm(p, 1, 78f - 14f * k, 10f * k, 12f + 22f * k);
        Torso(p, 4f + 12f * k, 1f + 5f * k, 0f, 4f * k);
        return p;
    }

    /// <summary>Saludo con la mano derecha: sube el brazo, agita el antebrazo tres veces y lo baja.</summary>
    static Pose WaveGesture(float t, float len)
    {
        float up = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / 0.3f)) * Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((len - t) / 0.35f));
        float wobble = Mathf.Sin((t - 0.3f) / (len - 0.65f) * Mathf.PI * 6f) * 22f * up;
        var p = Idle(t, 3.2f);
        p.rot.Remove(HumanBodyBones.RightUpperArm);
        p.rot.Remove(HumanBodyBones.RightLowerArm);
        // Brazo derecho: de colgando (79° abajo) a 20° arriba de la horizontal, un poco al frente,
        // con rotación externa del hombro para que al doblar el codo el antebrazo quede vertical.
        // El "adiós" es el codo abriendo y cerrando: el antebrazo se mece de lado a lado.
        float down = Mathf.Lerp(79f, -20f, up);
        float twist = Mathf.Lerp(0f, -85f, up);
        p.Add(HumanBodyBones.RightUpperArm, Q(Y, -15f * up) * Q(X, -5f * (1f - up)) * Q(Z, -down) * Q(X, twist));
        p.Add(HumanBodyBones.RightLowerArm, Q(Y, -(Mathf.Lerp(12f, 72f, up) + wobble)));
        p.Add(HumanBodyBones.RightHand, Q(Z, wobble * 0.3f));
        p.Add(HumanBodyBones.Head, Q(Z, -4f * up) * Q(Y, 6f * up));
        return p;
    }

    static Rig CreateRig()
    {
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(MaleFbx);
        var go = (GameObject)Object.Instantiate(model);
        go.name = "_RigTemporal";
        go.hideFlags = HideFlags.HideAndDontSave;
        go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        go.transform.localScale = Vector3.one;
        var rig = new Rig { root = go, animator = go.GetComponent<Animator>() };
        foreach (var t in go.GetComponentsInChildren<Transform>(true))
        {
            rig.restLocal[t] = t.localRotation;
            rig.restWorld[t] = t.rotation;
        }
        rig.hips = rig[HumanBodyBones.Hips];
        rig.hipsRestLocalPos = rig.hips.localPosition;
        rig.hipsRestWorld = rig.hips.position;
        rig.lFoot = rig[HumanBodyBones.LeftFoot]; rig.rFoot = rig[HumanBodyBones.RightFoot];
        rig.lToes = rig[HumanBodyBones.LeftToes]; rig.rToes = rig[HumanBodyBones.RightToes];
        rig.footRestY = rig.lFoot.position.y;
        rig.toesRestY = rig.lToes.position.y;
        rig.handler = new HumanPoseHandler(rig.animator.avatar, go.transform);
        return rig;
    }

    static void ApplyPose(Rig rig, Pose p)
    {
        foreach (var kv in rig.restLocal) kv.Key.localRotation = kv.Value;
        rig.hips.localPosition = rig.hipsRestLocalPos;
        foreach (var kv in p.rot)
        {
            var t = rig[kv.Key];
            if (t == null) continue;
            // D se expresa en el marco del personaje en pose T: local = (rotación de reposo del padre)^-1 * D * (rotación de reposo del hueso).
            t.localRotation = Quaternion.Inverse(rig.restWorld[t.parent]) * kv.Value * rig.restWorld[t];
        }
        rig.hips.position = rig.hipsRestWorld + p.hipsOffset;
        if (p.ground)
        {
            float dy = Mathf.Min(
                Mathf.Min(rig.lFoot.position.y - rig.footRestY, rig.rFoot.position.y - rig.footRestY),
                Mathf.Min(rig.lToes.position.y - rig.toesRestY, rig.rToes.position.y - rig.toesRestY));
            rig.hips.position -= Vector3.up * dy;
        }
        rig.hips.position += Vector3.up * p.lift;
    }

    static bool IsFingerMuscle(string n) =>
        n.Contains("Thumb") || n.Contains("Index") || n.Contains("Middle") || n.Contains("Ring") || n.Contains("Little");

    static AnimationClip Bake(Rig rig, string name, float length, bool loop, Func<float, float, Pose> poseAt, float fps = 30f)
    {
        EnsureFolder(AnimDir);
        string path = AnimDir + "/" + name + ".anim";
        var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
        if (clip == null) { clip = new AnimationClip(); AssetDatabase.CreateAsset(clip, path); }
        clip.ClearCurves();
        clip.frameRate = fps;

        int muscles = HumanTrait.MuscleCount;
        var curves = new AnimationCurve[muscles];
        for (int i = 0; i < muscles; i++) curves[i] = new AnimationCurve();
        var rootT = new[] { new AnimationCurve(), new AnimationCurve(), new AnimationCurve() };
        var rootQ = new[] { new AnimationCurve(), new AnimationCurve(), new AnimationCurve(), new AnimationCurve() };

        int frames = Mathf.Max(2, Mathf.RoundToInt(length * fps));
        var hp = new HumanPose();
        Quaternion prevQ = Quaternion.identity;
        for (int f = 0; f <= frames; f++)
        {
            float t = length * f / frames;
            // En los ciclos el último cuadro es idéntico al primero.
            ApplyPose(rig, poseAt(loop && f == frames ? 0f : t, length));
            rig.handler.GetHumanPose(ref hp);
            for (int i = 0; i < muscles; i++)
            {
                float v = hp.muscles[i];
                if (float.IsNaN(v) || float.IsInfinity(v)) v = curves[i].length > 0 ? curves[i].keys[curves[i].length - 1].value : 0f;
                curves[i].AddKey(t, v);
            }
            rootT[0].AddKey(t, hp.bodyPosition.x);
            rootT[1].AddKey(t, hp.bodyPosition.y);
            rootT[2].AddKey(t, hp.bodyPosition.z);
            var q = hp.bodyRotation;
            if (f > 0 && Quaternion.Dot(prevQ, q) < 0f) q = new Quaternion(-q.x, -q.y, -q.z, -q.w);
            prevQ = q;
            rootQ[0].AddKey(t, q.x); rootQ[1].AddKey(t, q.y); rootQ[2].AddKey(t, q.z); rootQ[3].AddKey(t, q.w);
        }

        for (int i = 0; i < muscles; i++)
        {
            string mn = HumanTrait.MuscleName[i];
            if (IsFingerMuscle(mn)) continue;
            AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("", typeof(Animator), mn), curves[i]);
        }
        string[] tn = { "RootT.x", "RootT.y", "RootT.z" };
        for (int i = 0; i < 3; i++) AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("", typeof(Animator), tn[i]), rootT[i]);
        string[] qn = { "RootQ.x", "RootQ.y", "RootQ.z", "RootQ.w" };
        for (int i = 0; i < 4; i++) AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("", typeof(Animator), qn[i]), rootQ[i]);

        var s = AnimationUtility.GetAnimationClipSettings(clip);
        s.loopTime = loop;
        s.loopBlend = false;
        s.loopBlendOrientation = true;
        s.loopBlendPositionY = true;
        s.loopBlendPositionXZ = true;
        s.keepOriginalOrientation = true;
        s.keepOriginalPositionY = true;
        s.keepOriginalPositionXZ = true;
        s.heightFromFeet = false;
        AnimationUtility.SetAnimationClipSettings(clip, s);
        EditorUtility.SetDirty(clip);
        return clip;
    }

    public static void BuildClips()
    {
        var rig = CreateRig();
        try
        {
            Bake(rig, "Reposo", 3.2f, true, Idle);
            Bake(rig, "Caminar", 0.8f, true, Walk);
            Bake(rig, "Correr", 0.62f, true, Run);
            Bake(rig, "EnElAire", 0.6f, true, Air);
            Bake(rig, "Aterrizar", 0.4f, false, Land);
            Bake(rig, "Saludo", 1.8f, false, WaveGesture);
        }
        finally
        {
            rig.handler.Dispose();
            Object.DestroyImmediate(rig.root);
        }
        AssetDatabase.SaveAssets();
    }

    // =====================================================================
    //  2b. Emotes (rueda de la tecla B): bailes de cuerpo completo
    // =====================================================================

    /// <summary>Estados de la capa "Emotes" del Animator (el nombre del estado = nombre del clip).</summary>
    public static readonly string[] EmoteClips = { "Emote_SixSeven", "Emote_PassinhoJamal" };

    [MenuItem("Tools/Inmunologia/Emotes (hornear bailes y capa del Animator)")]
    public static void BuildEmotes()
    {
        var rig = CreateRig();
        try
        {
            Bake(rig, "Emote_SixSeven", SixSevenLength, true, SixSeven);
            Bake(rig, "Emote_PassinhoJamal", JamalBeat * 8f, true, PassinhoJamal);
        }
        finally
        {
            rig.handler.Dispose();
            Object.DestroyImmediate(rig.root);
        }
        AssetDatabase.SaveAssets();
        AddEmoteLayer();
        Debug.Log("[AvatarBuilder] Emotes horneados y capa 'Emotes' lista.");
    }

    // Brazo con giro del antebrazo sobre su propio eje (roll > 0 = palma hacia arriba / supinación).
    static void ArmRoll(Pose p, int side, float down, float forward, float elbow, float roll)
    {
        bool left = side < 0;
        p.Add(left ? HumanBodyBones.LeftUpperArm : HumanBodyBones.RightUpperArm, Q(X, -forward) * Q(Z, -side * down));
        p.Add(left ? HumanBodyBones.LeftLowerArm : HumanBodyBones.RightLowerArm, Q(Y, -side * elbow) * Q(X, roll));
    }

    // ---------- "Six Seven" (6-7): palmas hacia arriba, manos que suben y bajan alternadas ----------
    const float SixSevenLength = 2.0f;   // 4 vaivenes completos
    const float SixSevenPeriod = 0.5f;

    static Pose SixSeven(float t, float len)
    {
        float a = 2f * Mathf.PI * t / SixSevenPeriod;
        float s = Mathf.Sin(a);                       // +1: mano derecha arriba, izquierda abajo
        float bounce = Mathf.Abs(Mathf.Sin(a));       // rebote en cada media vuelta
        var p = new Pose();
        for (int side = -1; side <= 1; side += 2)
        {
            float k = side > 0 ? s : -s;              // cada mano al revés de la otra
            // Codos pegados al cuerpo y antebrazos al frente (casi horizontales), un poco hacia adentro,
            // con las palmas hacia arriba como una balanza: una mano sube mientras la otra baja.
            ArmRoll(p, side, 74f - 3f * k, 24f + 6f * k, 84f + 19f * k, 80f);
            p.Add(side < 0 ? HumanBodyBones.LeftUpperArm : HumanBodyBones.RightUpperArm, Q(Y, -side * 14f));
            p.Add(side < 0 ? HumanBodyBones.LeftShoulder : HumanBodyBones.RightShoulder, Q(Z, -side * 6f * Pos(k)));
            p.Add(side < 0 ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand, Q(Z, side * 14f));
            Leg(p, side, 3f, 9f + 7f * bounce, -5f - 4f * bounce, 4f);
        }
        Torso(p, 4f + 2f * bounce, 3f, 5f * s, 3f - 5f * bounce, -3f * s, 0f);
        p.Add(HumanBodyBones.Head, Q(Z, -4f * s));
        p.hipsOffset = new Vector3(0.01f * s, 0f, 0f);
        return p;
    }

    // ---------- Passinho do Jamal ----------
    // "Bate tres veces con el pie y anda para el lado": tres golpecitos con el pie derecho y un paso a la
    // derecha, luego lo mismo con el izquierdo. La cadera va al lado contrario del pie que pisa y los
    // brazos van y vienen sueltos, al ritmo de las piernas. Movimiento elástico, sin fuerza.
    const float JamalBeat = 0.36f;

    static Pose PassinhoJamal(float t, float len)
    {
        float beats = t / JamalBeat;
        int beat = Mathf.FloorToInt(beats) % 8;
        float f = beats - Mathf.Floor(beats);              // 0..1 dentro del tiempo
        int active = beat < 4 ? 1 : -1;                    // pie que marca: derecho y luego izquierdo
        bool step = beat % 4 == 3;                         // 4º tiempo: paso al lado
        float lift = Mathf.Sin(Mathf.PI * f);              // el pie sube y vuelve a golpear en el tiempo
        float groove = 1f - Mathf.Abs(Mathf.Cos(Mathf.PI * f)); // rebote de rodillas en cada tiempo

        // Cadera: se va al lado contrario del pie que marca; en el paso se cambia de lado (suave).
        float sway = -active * 0.07f;
        if (step) sway = Mathf.Lerp(-active * 0.07f, active * 0.07f, Mathf.SmoothStep(0f, 1f, f));
        // Transición suave al cambiar de pie (del tiempo 3 al 4 y del 7 al 0 ya está en el paso).
        var p = new Pose();
        for (int side = -1; side <= 1; side += 2)
        {
            if (side == active && !step)
            {
                // Golpecitos: rodilla arriba y abajo, con la punta del pie.
                Leg(p, side, 22f * lift, 50f * lift + 8f, -14f * lift - 4f, 6f);
            }
            else if (side == active && step)
            {
                // Paso al lado: la pierna se abre y apoya.
                float open = Mathf.Sin(Mathf.PI * Mathf.Min(1f, f * 1.3f));
                Leg(p, side, 8f * open, 22f * open + 8f, -8f * open, 6f + 10f * Mathf.SmoothStep(0f, 1f, f));
            }
            else
            {
                // Pierna de apoyo: flexión elástica en cada tiempo.
                Leg(p, side, 2f, 10f + 10f * groove, -6f - 6f * groove, 6f);
            }

            // Brazos sueltos que van y vienen: el del lado contrario al pie que marca va al frente.
            float swing = Mathf.Sin(Mathf.PI * beats) * (side == active ? -1f : 1f);
            float fwd = 30f * swing + 4f;
            ArmRoll(p, side, 74f + 5f * Mathf.Abs(swing), fwd, 16f + 26f * Pos(swing), 10f);
            p.Add(side < 0 ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand, Q(X, 18f * swing));
        }
        float twist = 11f * Mathf.Sin(Mathf.PI * beats) * active;
        Torso(p, 6f + 3f * groove, 2f, twist, 2f - 4f * groove, active * 3f, active * 5f * (step ? Mathf.Cos(Mathf.PI * f) : 1f));
        p.Add(HumanBodyBones.Head, Q(Z, active * 3f) * Q(Y, -twist * 0.5f));
        p.hipsOffset = new Vector3(sway, -0.012f * groove, 0f);
        return p;
    }

    static void AddEmoteLayer()
    {
        var ctrl = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        if (ctrl == null) { Debug.LogError("[AvatarBuilder] No se encontró " + ControllerPath); return; }
        for (int li = ctrl.layers.Length - 1; li >= 0; li--)
            if (ctrl.layers[li].name == "Emotes") ctrl.RemoveLayer(li);

        ctrl.AddLayer("Emotes");
        var all = ctrl.layers;
        var layer = all[all.Length - 1];
        layer.blendingMode = AnimatorLayerBlendingMode.Override;
        layer.defaultWeight = 0f;
        layer.avatarMask = null; // cuerpo completo
        var sm = layer.stateMachine;
        var empty = sm.AddState("Vacio", new Vector3(300, 60, 0));
        empty.motion = Clip("Reposo");
        sm.defaultState = empty;
        int i = 0;
        foreach (var name in EmoteClips)
        {
            var st = sm.AddState(name, new Vector3(560, 60 + 90 * i++, 0));
            st.motion = Clip(name);
        }
        all[all.Length - 1] = layer;
        ctrl.layers = all;
        EditorUtility.SetDirty(ctrl);
        AssetDatabase.SaveAssets();
    }

    // =====================================================================
    //  3. Animator Controller y prefabs
    // =====================================================================

    static AnimationClip Clip(string name) => AssetDatabase.LoadAssetAtPath<AnimationClip>(AnimDir + "/" + name + ".anim");

    public static void BuildController()
    {
        EnsureFolder(AnimDir);
        AssetDatabase.DeleteAsset(ControllerPath);
        var ctrl = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
        ctrl.AddParameter("Velocidad", AnimatorControllerParameterType.Float);
        ctrl.AddParameter(new AnimatorControllerParameter { name = "Sentido", type = AnimatorControllerParameterType.Float, defaultFloat = 1f });
        ctrl.AddParameter(new AnimatorControllerParameter { name = "EnSuelo", type = AnimatorControllerParameterType.Bool, defaultBool = true });
        ctrl.AddParameter("Saludar", AnimatorControllerParameterType.Trigger);

        // ---- Capa base: locomoción, salto y aterrizaje ----
        var sm = ctrl.layers[0].stateMachine;
        var loco = ctrl.CreateBlendTreeInController("Locomocion", out var tree, 0);
        tree.blendType = BlendTreeType.Simple1D;
        tree.blendParameter = "Velocidad";
        tree.useAutomaticThresholds = false;
        tree.AddChild(Clip("Reposo"), 0f);
        tree.AddChild(Clip("Caminar"), 3f);
        tree.AddChild(Clip("Correr"), 5.5f);
        loco.speedParameterActive = true;
        loco.speedParameter = "Sentido";

        var air = sm.AddState("EnElAire", new Vector3(300, 200, 0));
        air.motion = Clip("EnElAire");
        var land = sm.AddState("Aterrizar", new Vector3(300, 320, 0));
        land.motion = Clip("Aterrizar");
        sm.defaultState = loco;

        var t1 = loco.AddTransition(air);
        t1.hasExitTime = false; t1.duration = 0.12f;
        t1.AddCondition(AnimatorConditionMode.IfNot, 0, "EnSuelo");
        var t2 = air.AddTransition(land);
        t2.hasExitTime = false; t2.duration = 0.06f;
        t2.AddCondition(AnimatorConditionMode.If, 0, "EnSuelo");
        var t3 = land.AddTransition(loco);
        t3.hasExitTime = true; t3.exitTime = 0.65f; t3.duration = 0.15f;
        var t4 = land.AddTransition(air);
        t4.hasExitTime = false; t4.duration = 0.1f;
        t4.AddCondition(AnimatorConditionMode.IfNot, 0, "EnSuelo");

        // ---- Capa de gestos (brazos y cabeza): saludo ----
        var mask = BuildUpperBodyMask();
        ctrl.AddLayer("Gestos");
        var layers = ctrl.layers;
        layers[0].iKPass = true;
        layers[1].avatarMask = mask;
        layers[1].blendingMode = AnimatorLayerBlendingMode.Override;
        layers[1].defaultWeight = 0f;
        var gsm = layers[1].stateMachine;
        var idleG = gsm.AddState("Vacio", new Vector3(300, 100, 0));
        idleG.writeDefaultValues = false;
        idleG.motion = Clip("Reposo");
        var wave = gsm.AddState("Saludo", new Vector3(300, 220, 0));
        wave.motion = Clip("Saludo");
        wave.speed = 1f;
        gsm.defaultState = idleG;
        var w1 = idleG.AddTransition(wave);
        w1.hasExitTime = false; w1.duration = 0.15f;
        w1.AddCondition(AnimatorConditionMode.If, 0, "Saludar");
        var w2 = wave.AddTransition(idleG);
        w2.hasExitTime = true; w2.exitTime = 0.95f; w2.duration = 0.2f;
        ctrl.layers = layers;

        EditorUtility.SetDirty(ctrl);
        AssetDatabase.SaveAssets();
    }

    static AvatarMask BuildUpperBodyMask()
    {
        string path = AnimDir + "/MascaraBrazos.mask";
        var mask = AssetDatabase.LoadAssetAtPath<AvatarMask>(path);
        if (mask == null) { mask = new AvatarMask(); AssetDatabase.CreateAsset(mask, path); }
        for (int i = 0; i < (int)AvatarMaskBodyPart.LastBodyPart; i++)
        {
            var part = (AvatarMaskBodyPart)i;
            // Sólo el brazo derecho y la cabeza: se puede saludar mientras se camina.
            bool on = part == AvatarMaskBodyPart.Head || part == AvatarMaskBodyPart.RightArm || part == AvatarMaskBodyPart.RightFingers;
            mask.SetHumanoidBodyPartActive(part, on);
        }
        EditorUtility.SetDirty(mask);
        return mask;
    }

    public static void BuildPrefabs()
    {
        EnsureFolder(PrefabDir);
        var ctrl = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(ControllerPath);
        MakePrefab(MaleFbx, MalePrefabPath, "Avatar_Hombre", EyeHeight / MaleEyeY, ctrl);
        MakePrefab(FemaleFbx, FemalePrefabPath, "Avatar_Mujer", EyeHeight / FemaleEyeY, ctrl);
        AssetDatabase.SaveAssets();
    }

    static void MakePrefab(string fbx, string path, string name, float scale, RuntimeAnimatorController ctrl)
    {
        var root = new GameObject(name);
        root.transform.localScale = Vector3.one * scale;
        var model = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(fbx));
        model.name = "Modelo";
        model.transform.SetParent(root.transform, false);
        var anim = model.GetComponent<Animator>();
        anim.runtimeAnimatorController = ctrl;
        anim.applyRootMotion = false;
        anim.cullingMode = AnimatorCullingMode.CullUpdateTransforms;
        anim.updateMode = AnimatorUpdateMode.Normal;
        if (model.GetComponent<AvatarAnimatorDriver>() == null) model.AddComponent<AvatarAnimatorDriver>();
        foreach (var smr in model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            smr.updateWhenOffscreen = false;
            smr.skinnedMotionVectors = false;
        }
        PrefabUtility.SaveAsPrefabAsset(root, path);
        Object.DestroyImmediate(root);
    }

    // =====================================================================
    //  4. Jugador y espejo
    // =====================================================================

    public static int EnsureAvatarLayer()
    {
        int layer = LayerMask.NameToLayer(PlayerAvatar.LayerName);
        if (layer >= 0) return layer;
        var tm = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
        var layers = tm.FindProperty("layers");
        for (int i = 6; i < 32; i++)
        {
            var el = layers.GetArrayElementAtIndex(i);
            if (string.IsNullOrEmpty(el.stringValue))
            {
                el.stringValue = PlayerAvatar.LayerName;
                tm.ApplyModifiedProperties();
                return i;
            }
        }
        Debug.LogError("[AvatarBuilder] No hay capas libres para \"Avatar\".");
        return -1;
    }

    static T FindInScene<T>(string name) where T : Component
    {
        foreach (var c in Resources.FindObjectsOfTypeAll<T>())
            if (c.gameObject.scene.IsValid() && c.name == name) return c;
        return null;
    }

    public static void SetupPlayer()
    {
        int layer = EnsureAvatarLayer();
        var player = GameObject.Find("Player");
        if (player == null) { Debug.LogError("[AvatarBuilder] No hay Player en la escena."); return; }
        var pa = player.GetComponent<PlayerAvatar>();
        if (pa == null) pa = player.AddComponent<PlayerAvatar>();
        pa.malePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(MalePrefabPath);
        pa.femalePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(FemalePrefabPath);
        EditorUtility.SetDirty(pa);

        if (layer >= 0)
        {
            // La cámara del jugador y la del minijuego no dibujan al avatar (primera persona).
            var main = player.GetComponentInChildren<Camera>(true);
            if (main != null) { main.cullingMask &= ~(1 << layer); EditorUtility.SetDirty(main); }
            var mg = FindInScene<Camera>("CamaraMinijuego");
            if (mg != null) { mg.cullingMask &= ~(1 << layer); EditorUtility.SetDirty(mg); }
        }
    }

    public static void BuildMirror()
    {
        EnsureFolder(MatDir);
        // El reglamento de bioseguridad se muda a la pared este (junto al pizarrón) para dejar lugar al espejo.
        var poster = GameObject.Find("Salon/Cartel_Bioseguridad");
        if (poster != null)
        {
            poster.transform.SetPositionAndRotation(new Vector3(2.95f, 0f, 1.62f), Quaternion.Euler(0f, 90f, 0f));
            EditorUtility.SetDirty(poster.transform);
        }

        var old = GameObject.Find("Espejo");
        if (old != null) Object.DestroyImmediate(old);

        var lit = Shader.Find("Universal Render Pipeline/Lit");
        var frameMat = LoadOrCreate(MatDir + "/Espejo_Marco.mat", lit);
        frameMat.SetColor("_BaseColor", new Color(0.72f, 0.74f, 0.78f));
        frameMat.SetFloat("_Metallic", 0.85f);
        frameMat.SetFloat("_Smoothness", 0.7f);
        var backMat = LoadOrCreate(MatDir + "/Espejo_Respaldo.mat", lit);
        backMat.SetColor("_BaseColor", new Color(0.12f, 0.13f, 0.15f));
        backMat.SetFloat("_Smoothness", 0.3f);
        var glassMat = LoadOrCreate(MatDir + "/Espejo_Vidrio.mat", Shader.Find("Universal Render Pipeline/Unlit"));
        glassMat.SetColor("_BaseColor", new Color(0.93f, 0.95f, 0.97f));
        var headerMat = LoadOrCreate(MatDir + "/Espejo_Letrero.mat", lit);
        headerMat.SetColor("_BaseColor", new Color(0.1f, 0.3f, 0.6f));
        headerMat.SetFloat("_Smoothness", 0.4f);

        const float W = 1.2f, H = 1.9f, Bottom = 0.5f, F = 0.045f;
        float cy = Bottom + H * 0.5f;

        // Pared oeste (x = -2.95), entre el estante y la puerta. +Z local apunta hacia el salón.
        var root = new GameObject("Espejo");
        root.transform.SetPositionAndRotation(new Vector3(-2.95f, 0f, -0.5f), Quaternion.Euler(0f, 90f, 0f));

        Box(root.transform, "Respaldo", new Vector3(0f, cy, 0.006f), new Vector3(W + 0.04f, H + 0.04f, 0.012f), backMat);

        var glass = new GameObject("Vidrio");
        glass.transform.SetParent(root.transform, false);
        glass.transform.localPosition = new Vector3(0f, cy, 0.014f);
        glass.layer = 1; // TransparentFX: la cámara del espejo no se ve a sí misma
        var mf = glass.AddComponent<MeshFilter>();
        mf.sharedMesh = MirrorMesh(W, H);
        var mr = glass.AddComponent<MeshRenderer>();
        mr.sharedMaterial = glassMat;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;
        var mirror = glass.AddComponent<PlanarMirror>();
        mirror.size = new Vector2(W, H);
        mirror.glass = mr;
        mirror.reflectLayers = ~((1 << 1) | (1 << 5)); // todo menos TransparentFX y UI

        // Marco de aluminio.
        Box(root.transform, "Marco_Sup", new Vector3(0f, Bottom + H + F * 0.5f, 0.02f), new Vector3(W + 2 * F, F, 0.04f), frameMat);
        Box(root.transform, "Marco_Inf", new Vector3(0f, Bottom - F * 0.5f, 0.02f), new Vector3(W + 2 * F, F, 0.04f), frameMat);
        Box(root.transform, "Marco_Izq", new Vector3(-(W + F) * 0.5f, cy, 0.02f), new Vector3(F, H, 0.04f), frameMat);
        Box(root.transform, "Marco_Der", new Vector3((W + F) * 0.5f, cy, 0.02f), new Vector3(F, H, 0.04f), frameMat);
        Box(root.transform, "Repisa", new Vector3(0f, Bottom - F - 0.012f, 0.06f), new Vector3(W + 2 * F, 0.024f, 0.12f), frameMat);

        // Letrero arriba del espejo.
        Box(root.transform, "Letrero", new Vector3(0f, Bottom + H + F + 0.1f, 0.008f), new Vector3(1.0f, 0.13f, 0.012f), headerMat);
        var label = new GameObject("Letrero_Texto", typeof(RectTransform));
        label.transform.SetParent(root.transform, false);
        var lrt = (RectTransform)label.transform;
        lrt.localPosition = new Vector3(0f, Bottom + H + F + 0.1f, 0.0155f);
        lrt.localRotation = Quaternion.Euler(0f, 180f, 0f);
        lrt.sizeDelta = new Vector2(0.96f, 0.11f);
        var tmp = label.AddComponent<TextMeshPro>();
        if (TMP_Settings.defaultFontAsset != null) tmp.font = TMP_Settings.defaultFontAsset;
        tmp.text = "<b>¿LISTO PARA EL LABORATORIO?</b>\n<size=70%>Revisa tu bata, lentes y guantes</size>";
        tmp.fontSize = 0.42f;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = Color.white;
        tmp.textWrappingMode = TextWrappingModes.NoWrap;

        // Zona para apuntar (E: saludar).
        var col = root.AddComponent<BoxCollider>();
        col.center = new Vector3(0f, cy, 0.03f);
        col.size = new Vector3(W + 0.1f, H + 0.1f, 0.06f);
        root.AddComponent<MirrorInteractable>();

        EditorUtility.SetDirty(frameMat); EditorUtility.SetDirty(backMat); EditorUtility.SetDirty(glassMat); EditorUtility.SetDirty(headerMat);
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
    }

    static Material LoadOrCreate(string path, Shader shader)
    {
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null) { m = new Material(shader); AssetDatabase.CreateAsset(m, path); }
        m.shader = shader;
        return m;
    }

    static GameObject Box(Transform parent, string name, Vector3 pos, Vector3 size, Material m)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        Object.DestroyImmediate(go.GetComponent<Collider>());
        go.transform.SetParent(parent, false);
        go.transform.localPosition = pos;
        go.transform.localScale = size;
        go.GetComponent<Renderer>().sharedMaterial = m;
        return go;
    }

    /// <summary>Rectángulo en XY mirando a +Z, con U creciendo hacia +X local (coincide con la cámara del espejo).</summary>
    static Mesh MirrorMesh(float w, float h)
    {
        string path = MatDir + "/Espejo_Malla.asset";
        var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (mesh == null) { mesh = new Mesh(); AssetDatabase.CreateAsset(mesh, path); }
        mesh.Clear();
        mesh.name = "Espejo";
        mesh.vertices = new[] { new Vector3(-w / 2, -h / 2, 0), new Vector3(w / 2, -h / 2, 0), new Vector3(-w / 2, h / 2, 0), new Vector3(w / 2, h / 2, 0) };
        mesh.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 1), new Vector2(1, 1) };
        mesh.triangles = new[] { 0, 1, 2, 1, 3, 2 };
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        EditorUtility.SetDirty(mesh);
        return mesh;
    }

    // =====================================================================
    //  5. Formulario del alumno y etiquetas
    // =====================================================================

    static readonly Color Card = new Color(0.05f, 0.1f, 0.2f, 0.96f);
    static readonly Color Primary = new Color(0.16f, 0.47f, 0.86f, 1f);
    static readonly Color Secondary = new Color(0.18f, 0.24f, 0.34f, 1f);
    static readonly Color Green = new Color(0.2f, 0.72f, 0.48f, 1f);
    static readonly Color Muted = new Color(0.72f, 0.78f, 0.87f, 1f);
    static readonly Color Accent = new Color(0.55f, 0.78f, 1f, 1f);
    static readonly Color FieldColor = new Color(0.1f, 0.16f, 0.27f, 1f);
    static Sprite rounded;

    public static void BuildStudentForm()
    {
        rounded = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
        var menu = Object.FindAnyObjectByType<MainMenuUI>(FindObjectsInactive.Include);
        if (menu == null) { Debug.LogError("[AvatarBuilder] No hay MainMenuUI."); return; }
        var screen = menu.transform.Find("PantallaMenu");
        var old = screen.Find("PanelAlumno");
        if (old != null) Object.DestroyImmediate(old.gameObject);

        var panel = UI("PanelAlumno", screen);
        Stretch(panel);
        var form = panel.gameObject.AddComponent<StudentFormUI>();

        var card = UI("Tarjeta", panel);
        Place(card, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1240, 880));
        Img(card, Card, true, true);
        var bar = UI("Acento", card);
        Place(bar, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1), Vector2.zero, new Vector2(0, 10));
        Img(bar, Primary, false);

        var title = Txt(UI("Titulo", card), "DATOS DEL ALUMNO", 50, Color.white, TextAlignmentOptions.Left, FontStyles.Bold);
        TopLeft(title.rectTransform, 64, -46, 760, 66);
        title.characterSpacing = 3f;
        var sub = Txt(UI("Subtitulo", card), "Antes de elegir el modo de juego registra tus datos. Se guardan junto con tus resultados.", 22, Muted, TextAlignmentOptions.TopLeft);
        TopLeft(sub.rectTransform, 64, -116, 720, 64);

        float y = -196;
        form.nameInput = Field(card, "Nombre", "Nombre completo", "Ej. Diego Pineda Gumeta", ref y, TMP_InputField.ContentType.Name, 80);
        form.idInput = Field(card, "Matricula", "Matrícula", "Ej. A210123", ref y, TMP_InputField.ContentType.Alphanumeric, 12);
        form.emailInput = Field(card, "Correo", "Correo institucional", "nombre@unach.mx", ref y, TMP_InputField.ContentType.EmailAddress, 80);

        var sexLabel = Txt(UI("Etiqueta_Sexo", card), "Sexo", 24, Accent, TextAlignmentOptions.Left, FontStyles.Bold);
        TopLeft(sexLabel.rectTransform, 64, y, 700, 34);
        y -= 40;
        form.maleButton = SexButton(card, "Hombre", "Hombre", 64, y);
        form.femaleButton = SexButton(card, "Mujer", "Mujer", 64 + 360, y);
        y -= 84;

        form.errorText = Txt(UI("Error", card), "", 22, new Color(1f, 0.55f, 0.5f), TextAlignmentOptions.TopLeft, FontStyles.Bold);
        TopLeft(form.errorText.rectTransform, 64, y, 700, 60);
        form.errorText.gameObject.SetActive(false);

        // Vista previa del avatar (columna derecha).
        var frame = UI("MarcoVista", card);
        Place(frame, new Vector2(1, 1), new Vector2(1, 1), new Vector2(1, 1), new Vector2(-64, -46), new Vector2(380, 560));
        Img(frame, new Color(0.14f, 0.22f, 0.36f, 1f), true);
        var raw = UI("VistaAvatar", frame);
        Stretch(raw, 6, 6, 6, 6);
        var ri = raw.gameObject.AddComponent<UnityEngine.UI.RawImage>();
        ri.color = Color.white;
        ri.raycastTarget = true;
        form.preview = ri;
        form.previewCaption = Txt(UI("Leyenda", card), "Elige tu sexo para asignarte un avatar", 24, Accent, TextAlignmentOptions.Center, FontStyles.Bold);
        Place(form.previewCaption.rectTransform, new Vector2(1, 1), new Vector2(1, 1), new Vector2(1, 1), new Vector2(-64, -616), new Vector2(380, 40));
        var hint = Txt(UI("Pista", card), "Arrastra con el mouse para girarlo", 18, Muted, TextAlignmentOptions.Center);
        Place(hint.rectTransform, new Vector2(1, 1), new Vector2(1, 1), new Vector2(1, 1), new Vector2(-64, -656), new Vector2(380, 30));

        // Botones.
        form.backButton = Btn(card, "Volver", "Volver", Secondary, 28);
        Place((RectTransform)form.backButton.transform, new Vector2(0, 0), new Vector2(0, 0), new Vector2(0, 0), new Vector2(64, 44), new Vector2(260, 68));
        form.continueButton = Btn(card, "Continuar", "Continuar  →", Green, 30);
        Place((RectTransform)form.continueButton.transform, new Vector2(1, 0), new Vector2(1, 0), new Vector2(1, 0), new Vector2(-64, 44), new Vector2(340, 68));
        var keys = Txt(UI("Teclas", card), "Tab: siguiente campo   ·   Enter: continuar", 18, new Color(0.55f, 0.62f, 0.72f), TextAlignmentOptions.Center);
        Place(keys.rectTransform, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(-30, 66), new Vector2(460, 30));

        form.malePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(MalePrefabPath);
        form.femalePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(FemalePrefabPath);
        form.fieldNormal = FieldColor;
        panel.SetSiblingIndex(menu.modePanel != null ? menu.modePanel.transform.GetSiblingIndex() : screen.childCount - 1);
        panel.gameObject.SetActive(false);

        menu.studentForm = form;
        EditorUtility.SetDirty(form);
        EditorUtility.SetDirty(menu);

        // En la elección de modo se ve quién está jugando.
        if (menu.modePanel != null)
        {
            var mp = menu.modePanel.transform;
            var prev = mp.Find("Alumno");
            if (prev != null) Object.DestroyImmediate(prev.gameObject);
            var who = Txt(UI("Alumno", mp), "", 24, Accent, TextAlignmentOptions.Right, FontStyles.Bold);
            Place(who.rectTransform, new Vector2(1, 1), new Vector2(1, 1), new Vector2(1, 1), new Vector2(-60, -36), new Vector2(900, 40));
            var sl = who.gameObject.AddComponent<StudentLabel>();
            sl.format = "Alumno: {0}  ·  {1}";
        }
    }

    static TMP_InputField Field(Transform card, string name, string label, string placeholder, ref float y, TMP_InputField.ContentType type, int limit)
    {
        var l = Txt(UI("Etiqueta_" + name, card), label, 24, Accent, TextAlignmentOptions.Left, FontStyles.Bold);
        TopLeft(l.rectTransform, 64, y, 700, 34);
        y -= 40;

        var res = new TMP_DefaultControls.Resources { standard = rounded, inputField = rounded, background = rounded };
        var go = TMP_DefaultControls.CreateInputField(res);
        go.name = "Campo_" + name;
        go.layer = 5;
        foreach (var t in go.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = 5;
        go.transform.SetParent(card, false);
        var rt = (RectTransform)go.transform;
        TopLeft(rt, 64, y, 700, 62);
        var img = go.GetComponent<UnityEngine.UI.Image>();
        img.sprite = rounded;
        img.type = UnityEngine.UI.Image.Type.Sliced;
        img.color = FieldColor;

        var input = go.GetComponent<TMP_InputField>();
        input.contentType = type;
        input.lineType = TMP_InputField.LineType.SingleLine;
        input.characterLimit = limit;
        input.caretColor = Color.white;
        input.customCaretColor = true;
        input.caretWidth = 2;
        input.selectionColor = new Color(0.35f, 0.6f, 1f, 0.45f);
        var cb = input.colors;
        cb.normalColor = Color.white;
        cb.highlightedColor = new Color(1.12f, 1.12f, 1.2f, 1f);
        cb.selectedColor = new Color(1.2f, 1.25f, 1.4f, 1f);
        cb.pressedColor = Color.white;
        input.colors = cb;

        var area = (RectTransform)go.transform.Find("Text Area");
        area.offsetMin = new Vector2(20, 6);
        area.offsetMax = new Vector2(-20, -6);
        var text = (TextMeshProUGUI)input.textComponent;
        if (TMP_Settings.defaultFontAsset != null) text.font = TMP_Settings.defaultFontAsset;
        text.fontSize = 28;
        text.color = Color.white;
        text.alignment = TextAlignmentOptions.Left;
        var ph = (TextMeshProUGUI)input.placeholder;
        if (TMP_Settings.defaultFontAsset != null) ph.font = TMP_Settings.defaultFontAsset;
        ph.text = placeholder;
        ph.fontSize = 26;
        ph.fontStyle = FontStyles.Italic;
        ph.color = new Color(0.62f, 0.68f, 0.78f, 0.55f);
        ph.alignment = TextAlignmentOptions.Left;
        input.pointSize = 28;
        y -= 84;
        return input;
    }

    static UnityEngine.UI.Button SexButton(Transform card, string name, string label, float x, float y)
    {
        var b = Btn(card, "Sexo_" + name, label, Secondary, 28);
        TopLeft((RectTransform)b.transform, x, y, 340, 64);
        // Punto blanco que indica la opción elegida.
        var mark = UI("Marca", b.transform);
        Place(mark, new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(-24, 0), new Vector2(20, 20));
        var dot = mark.gameObject.AddComponent<UnityEngine.UI.Image>();
        dot.sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Knob.psd");
        dot.color = Color.white;
        dot.raycastTarget = false;
        mark.gameObject.SetActive(false);
        return b;
    }

    public static void AddStudentLabels()
    {
        var hud = GameObject.Find("Canvas");
        if (hud == null) return;
        var h = hud.transform;

        var mode = h.Find("ModeText") as RectTransform;
        if (mode != null)
        {
            var prev = h.Find("AlumnoText");
            if (prev != null) Object.DestroyImmediate(prev.gameObject);
            var t = Txt(UI("AlumnoText", h), "", 18, Muted, TextAlignmentOptions.Left);
            TopLeft(t.rectTransform, 38, -106, 400, 24);
            t.textWrappingMode = TextWrappingModes.NoWrap;
            t.overflowMode = TextOverflowModes.Ellipsis;
            t.transform.SetSiblingIndex(mode.GetSiblingIndex() + 1);
            t.gameObject.AddComponent<StudentLabel>().format = "{0}  ·  {1}";
            var bg = h.Find("HudFondo") as RectTransform;
            if (bg != null) bg.sizeDelta = new Vector2(bg.sizeDelta.x, 120);
            // El panel del objetivo (debajo) se recorre para no encimarse.
            var obj = h.Find("ObjectivePanel") as RectTransform;
            if (obj != null) obj.anchoredPosition = new Vector2(obj.anchoredPosition.x, -150f);
        }

        var complete = h.Find("CompletePanel");
        if (complete != null)
        {
            var prev = complete.Find("AlumnoResultado");
            if (prev != null) Object.DestroyImmediate(prev.gameObject);
            var t = Txt(UI("AlumnoResultado", complete), "", 26, Accent, TextAlignmentOptions.Center, FontStyles.Bold);
            Place(t.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -352), new Vector2(1200, 40));
            t.gameObject.AddComponent<StudentLabel>().format = "Alumno: {0}  ·  Matrícula {1}";
        }
        EditorUtility.SetDirty(hud);
    }

    // =====================================================================
    //  Utilidades de UI (mismo estilo que MenuBuilder)
    // =====================================================================

    static RectTransform UI(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.layer = 5;
        go.transform.SetParent(parent, false);
        return (RectTransform)go.transform;
    }

    static void Place(RectTransform rt, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 pos, Vector2 size)
    {
        rt.anchorMin = anchorMin; rt.anchorMax = anchorMax; rt.pivot = pivot;
        rt.sizeDelta = size; rt.anchoredPosition = pos;
    }

    static void TopLeft(RectTransform rt, float x, float y, float w, float h) =>
        Place(rt, new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1), new Vector2(x, y), new Vector2(w, h));

    static void Stretch(RectTransform rt, float left = 0, float right = 0, float top = 0, float bottom = 0)
    {
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.pivot = new Vector2(0.5f, 0.5f);
        rt.offsetMin = new Vector2(left, bottom); rt.offsetMax = new Vector2(-right, -top);
    }

    static UnityEngine.UI.Image Img(RectTransform rt, Color c, bool roundedSprite = true, bool raycast = false)
    {
        var img = rt.gameObject.AddComponent<UnityEngine.UI.Image>();
        if (roundedSprite) { img.sprite = rounded; img.type = UnityEngine.UI.Image.Type.Sliced; }
        img.color = c;
        img.raycastTarget = raycast;
        return img;
    }

    static TextMeshProUGUI Txt(RectTransform rt, string text, float size, Color c, TextAlignmentOptions align, FontStyles style = FontStyles.Normal)
    {
        var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
        if (TMP_Settings.defaultFontAsset != null) t.font = TMP_Settings.defaultFontAsset;
        t.text = text; t.fontSize = size; t.color = c; t.alignment = align; t.fontStyle = style;
        t.richText = true; t.raycastTarget = false;
        t.textWrappingMode = TextWrappingModes.Normal;
        return t;
    }

    static UnityEngine.UI.ColorBlock Colors(Color baseColor)
    {
        var cb = UnityEngine.UI.ColorBlock.defaultColorBlock;
        cb.normalColor = baseColor;
        cb.highlightedColor = Color.Lerp(baseColor, Color.white, 0.22f);
        cb.pressedColor = Color.Lerp(baseColor, Color.black, 0.25f);
        cb.selectedColor = baseColor;
        cb.disabledColor = new Color(0.3f, 0.3f, 0.3f, 0.6f);
        cb.colorMultiplier = 1f;
        cb.fadeDuration = 0.08f;
        return cb;
    }

    static UnityEngine.UI.Button Btn(Transform parent, string name, string label, Color baseColor, float fontSize)
    {
        var rt = UI(name, parent);
        rt.sizeDelta = new Vector2(300, 72);
        var img = Img(rt, Color.white, true, true);
        var b = rt.gameObject.AddComponent<UnityEngine.UI.Button>();
        b.targetGraphic = img;
        b.colors = Colors(baseColor);
        var t = Txt(UI("Label", rt), label, fontSize, Color.white, TextAlignmentOptions.Center, FontStyles.Bold);
        Stretch(t.rectTransform);
        return b;
    }

    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = Path.GetDirectoryName(path).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
    }
}
