using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// A partir del modelo del conejo (Conejo.obj, una sola malla sin esqueleto) construye:
///   - Un esqueleto de 16 huesos (cadera, columna, pecho, cuello, cabeza, orejas en 2 partes,
///     patas delanteras, muslos + patas traseras y cola).
///   - Los pesos de piel (qué tanto mueve cada hueso a cada vértice), con zonas por región.
///   - 8 animaciones: Tranquilo, Olfateando, Nervioso, Forcejeo, Sobresalto, Temblor, Relajado y Salto.
///   - Un Animator Controller con un estado por animación y el prefab "Conejo".
/// </summary>
public static class RabbitBuilder
{
    const string Dir = "Assets/objetos/Conejo";
    const float S = 0.21f;      // escala aplicada al convertir el .glb (unidades del modelo -> metros)
    const float Y0 = 0.955f;    // altura de las patas en el modelo original

    // Coordenadas del modelo original (glTF, normalizadas) -> espacio de Unity en metros.
    // El importador de OBJ invierte X, así que +x del glTF queda del lado IZQUIERDO del conejo (-X en Unity).
    static Vector3 G(float x, float y, float z) => new Vector3(-x * S, (y + Y0) * S, z * S);

    class BoneDef
    {
        public string name, parent;
        public Vector3 head, tail;      // en coordenadas glTF normalizadas
        public int side;                // +1 = izquierda del conejo, -1 = derecha, 0 = centro
        public Transform t;
    }

    static List<BoneDef> Bones()
    {
        var l = new List<BoneDef>();
        void Add(string n, string p, Vector3 h, Vector3 tl, int side = 0) => l.Add(new BoneDef { name = n, parent = p, head = h, tail = tl, side = side });
        Add("Hips", null, new Vector3(0, -0.45f, -0.45f), new Vector3(0, -0.35f, -0.05f));
        Add("Spine", "Hips", new Vector3(0, -0.35f, -0.05f), new Vector3(0, -0.30f, 0.30f));
        Add("Chest", "Spine", new Vector3(0, -0.30f, 0.30f), new Vector3(0, -0.08f, 0.50f));
        Add("Neck", "Chest", new Vector3(0, -0.08f, 0.50f), new Vector3(0, 0.05f, 0.62f));
        Add("Head", "Neck", new Vector3(0, 0.05f, 0.62f), new Vector3(0, -0.10f, 0.97f));
        foreach (int s in new[] { 1, -1 })
        {
            string sfx = s > 0 ? ".L" : ".R";
            Add("Ear1" + sfx, "Head", new Vector3(0.18f * s, 0.28f, 0.42f), new Vector3(0.40f * s, 0.62f, 0.40f), s);
            Add("Ear2" + sfx, "Ear1" + sfx, new Vector3(0.40f * s, 0.62f, 0.40f), new Vector3(0.63f * s, 0.95f, 0.37f), s);
            Add("FrontLeg" + sfx, "Chest", new Vector3(0.20f * s, -0.45f, 0.40f), new Vector3(0.22f * s, -0.92f, 0.55f), s);
            Add("Thigh" + sfx, "Hips", new Vector3(0.36f * s, -0.50f, -0.35f), new Vector3(0.35f * s, -0.85f, -0.50f), s);
            Add("Foot" + sfx, "Thigh" + sfx, new Vector3(0.35f * s, -0.85f, -0.50f), new Vector3(0.30f * s, -0.93f, -0.05f), s);
        }
        Add("Tail", "Hips", new Vector3(0, -0.58f, -0.82f), new Vector3(0, -0.66f, -1.00f));
        return l;
    }

    static bool Gate(BoneDef b, Vector3 g)
    {
        int vs = g.x >= 0 ? 1 : -1;
        if (b.name.StartsWith("Ear")) return g.y > 0.22f && vs == b.side && Mathf.Abs(g.x) > 0.04f;
        if (b.name.StartsWith("FrontLeg")) return g.z > 0.2f && g.y < -0.5f && vs == b.side;
        if (b.name.StartsWith("Thigh") || b.name.StartsWith("Foot")) return g.z < 0.1f && g.y < -0.25f && vs == b.side;
        if (b.name == "Tail") return g.z < -0.7f;
        if (b.name == "Head") return g.z > 0.35f && g.y > -0.35f;
        return true;
    }

    static float SegDist(Vector3 p, Vector3 a, Vector3 b)
    {
        Vector3 ab = b - a;
        float t = Mathf.Clamp01(Vector3.Dot(p - a, ab) / Vector3.Dot(ab, ab));
        return Vector3.Distance(p, a + t * ab);
    }

    [MenuItem("Tools/Inmunologia/Construir conejo (esqueleto y animaciones)")]
    public static void Build()
    {
        var srcMesh = AssetDatabase.LoadAssetAtPath<Mesh>(Dir + "/Conejo.obj");
        if (srcMesh == null) { Debug.LogError("[RabbitBuilder] No encontré " + Dir + "/Conejo.obj"); return; }

        var mat = CreateMaterial();

        // ---------- Jerarquía ----------
        var root = new GameObject("Conejo");
        var model = new GameObject("Modelo");
        model.transform.SetParent(root.transform, false);
        var armature = new GameObject("Armature");
        armature.transform.SetParent(root.transform, false);

        var defs = Bones();
        var byName = new Dictionary<string, BoneDef>();
        foreach (var d in defs)
        {
            var go = new GameObject(d.name);
            d.t = go.transform;
            byName[d.name] = d;
        }
        foreach (var d in defs)
        {
            d.t.SetParent(d.parent == null ? armature.transform : byName[d.parent].t, false);
            d.t.position = G(d.head.x, d.head.y, d.head.z);
            d.t.rotation = Quaternion.identity;
        }
        // Punta de la nariz (sin peso; útil como referencia)
        var nose = new GameObject("Nariz").transform;
        nose.SetParent(byName["Head"].t, false);
        nose.position = G(0, -0.1f, 0.97f);

        // ---------- Pesos ----------
        var verts = srcMesh.vertices;
        var weights = new BoneWeight[verts.Length];
        var dist = new float[defs.Count];
        for (int i = 0; i < verts.Length; i++)
        {
            var u = verts[i];
            var g = new Vector3(-u.x / S, u.y / S - Y0, u.z / S);
            for (int b = 0; b < defs.Count; b++)
            {
                float d = Gate(defs[b], g) ? SegDist(g, defs[b].head, defs[b].tail) : 1000f;
                dist[b] = 1f / Mathf.Pow(d + 0.02f, 4f);
            }
            // 4 mayores
            int[] idx = { 0, 0, 0, 0 };
            float[] w = { -1, -1, -1, -1 };
            for (int b = 0; b < defs.Count; b++)
            {
                float v = dist[b];
                for (int k = 0; k < 4; k++)
                {
                    if (v > w[k])
                    {
                        for (int m = 3; m > k; m--) { w[m] = w[m - 1]; idx[m] = idx[m - 1]; }
                        w[k] = v; idx[k] = b;
                        break;
                    }
                }
            }
            float sum = w[0] + w[1] + w[2] + w[3];
            weights[i] = new BoneWeight
            {
                boneIndex0 = idx[0], weight0 = w[0] / sum,
                boneIndex1 = idx[1], weight1 = w[1] / sum,
                boneIndex2 = idx[2], weight2 = w[2] / sum,
                boneIndex3 = idx[3], weight3 = w[3] / sum,
            };
        }

        var bones = new Transform[defs.Count];
        var bindposes = new Matrix4x4[defs.Count];
        for (int b = 0; b < defs.Count; b++)
        {
            bones[b] = defs[b].t;
            bindposes[b] = defs[b].t.worldToLocalMatrix * model.transform.localToWorldMatrix;
        }

        string meshPath = Dir + "/Conejo_Skinned.asset";
        var mesh = UnityEngine.Object.Instantiate(srcMesh);
        mesh.name = "Conejo_Skinned";
        mesh.boneWeights = weights;
        mesh.bindposes = bindposes;
        mesh.RecalculateBounds();
        var oldMesh = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
        if (oldMesh != null) { EditorUtility.CopySerialized(mesh, oldMesh); mesh = oldMesh; }
        else AssetDatabase.CreateAsset(mesh, meshPath);

        var smr = model.AddComponent<SkinnedMeshRenderer>();
        smr.sharedMesh = mesh;
        smr.bones = bones;
        smr.rootBone = byName["Hips"].t;
        smr.sharedMaterial = mat;
        smr.shadowCastingMode = ShadowCastingMode.On;
        smr.localBounds = new Bounds(new Vector3(0f, 0.08f, 0.08f), new Vector3(0.6f, 0.7f, 0.7f));
        smr.updateWhenOffscreen = false;

        // ---------- Física / interacción ----------
        var rb = root.AddComponent<Rigidbody>();
        rb.isKinematic = true;
        rb.useGravity = false;
        rb.mass = 3f;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        var body = root.AddComponent<CapsuleCollider>();
        body.direction = 2; // eje Z
        body.center = new Vector3(0f, 0.095f, -0.01f);
        body.radius = 0.085f;
        body.height = 0.3f;
        var head = root.AddComponent<SphereCollider>();
        head.center = G(0, 0.02f, 0.66f);
        head.radius = 0.058f;
        var g2 = root.AddComponent<Grabbable>();
        g2.displayName = "Conejo";

        // ---------- Animaciones ----------
        var clips = BuildClips(root.transform, byName);
        var controller = BuildController(clips);
        var animator = root.AddComponent<Animator>();
        animator.runtimeAnimatorController = controller;
        animator.applyRootMotion = false;
        animator.cullingMode = AnimatorCullingMode.CullUpdateTransforms;
        var rc = root.AddComponent<RabbitController>();
        rc.animator = animator;
        rc.skin = smr;

        string prefabPath = Dir + "/Conejo.prefab";
        PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
        UnityEngine.Object.DestroyImmediate(root);
        AssetDatabase.SaveAssets();
        Debug.Log("[RabbitBuilder] Conejo listo: " + prefabPath + " (" + defs.Count + " huesos, " + clips.Count + " animaciones).");
    }

    static Material CreateMaterial()
    {
        string path = Dir + "/M_Conejo.mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        var sh = Shader.Find("Universal Render Pipeline/Lit");
        if (m == null) { m = new Material(sh); AssetDatabase.CreateAsset(m, path); }
        m.shader = sh;
        m.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(Dir + "/Conejo_BaseColor.jpg"));
        m.SetColor("_BaseColor", Color.white);
        m.SetTexture("_BumpMap", AssetDatabase.LoadAssetAtPath<Texture2D>(Dir + "/Conejo_Normal.jpg"));
        m.SetFloat("_BumpScale", 0.8f);
        m.EnableKeyword("_NORMALMAP");
        m.SetTexture("_MetallicGlossMap", AssetDatabase.LoadAssetAtPath<Texture2D>(Dir + "/Conejo_MetallicSmoothness.png"));
        m.EnableKeyword("_METALLICSPECGLOSSMAP");
        m.SetFloat("_SmoothnessTextureChannel", 0f);
        m.SetFloat("_Smoothness", 0.4f); // el pelaje debe verse mate
        EditorUtility.SetDirty(m);
        return m;
    }

    // =====================================================================
    //  Animaciones (poses procedurales horneadas a clips)
    // =====================================================================

    class Pose
    {
        public readonly Dictionary<string, Vector3> rot = new Dictionary<string, Vector3>();
        public Vector3 hipsOffset;
        public Vector3 chestScale = Vector3.one;
        public void R(string bone, float x, float y, float z)
        {
            rot.TryGetValue(bone, out var v);
            rot[bone] = v + new Vector3(x, y, z);
        }
    }

    const float TAU = Mathf.PI * 2f;
    static float Sn(float cycles, float t, float len, float phase = 0f) => Mathf.Sin(TAU * cycles * t / len + phase);
    static float Bell(float t, float center, float width) => Mathf.Exp(-((t - center) * (t - center)) / (2f * width * width));
    static float Smooth(float x) { x = Mathf.Clamp01(x); return x * x * (3f - 2f * x); }

    static readonly string[] AnimatedBones =
    {
        "Hips", "Spine", "Chest", "Neck", "Head", "Ear1.L", "Ear2.L", "Ear1.R", "Ear2.R",
        "FrontLeg.L", "FrontLeg.R", "Thigh.L", "Foot.L", "Thigh.R", "Foot.R", "Tail"
    };

    static void Breath(Pose p, float t, float len, float cycles, float amount)
    {
        float b = Sn(cycles, t, len);
        p.chestScale = new Vector3(1f + 0.012f * amount * b, 1f + 0.02f * amount * b, 1f + 0.01f * amount * b);
        p.R("Spine", 0.8f * amount * b, 0, 0);
    }

    static Dictionary<string, (float len, bool loop, Func<float, Pose> fn)> Definitions()
    {
        var d = new Dictionary<string, (float, bool, Func<float, Pose>)>();

        d["Tranquilo"] = (4f, true, t =>
        {
            float L = 4f; var p = new Pose();
            Breath(p, t, L, 3, 1f);
            p.R("Ear1.L", 2f * Sn(1, t, L), 0, 3f + 2f * Sn(2, t, L, 1f));
            p.R("Ear1.R", 2f * Sn(1, t, L, 0.7f), 0, -3f - 2f * Sn(2, t, L));
            float tw = Bell(t, 2.6f, 0.07f);   // tic de la oreja izquierda
            p.R("Ear1.L", -16f * tw, 0, 8f * tw);
            p.R("Ear2.L", -10f * tw, 0, 0);
            p.R("Head", 1.5f * Sn(2, t, L), 4f * Sn(1, t, L), 0);
            p.R("Tail", 0, 3f * Sn(2, t, L), 0);
            return p;
        });

        d["Olfateando"] = (3f, true, t =>
        {
            float L = 3f; var p = new Pose();
            Breath(p, t, L, 4, 1.1f);
            p.R("Chest", 4f, 0, 0);
            p.R("Neck", 12f + 3f * Sn(1, t, L), 0, 0);
            p.R("Head", 1.4f * Sn(21, t, L), 18f * Sn(1, t, L), 3f * Sn(1, t, L, 1.2f));
            p.R("Ear1.L", 10f + 3f * Sn(2, t, L), 0, 2f);
            p.R("Ear1.R", 10f + 3f * Sn(2, t, L, 1f), 0, -2f);
            p.R("Ear2.L", 4f * Sn(3, t, L), 0, 0);
            p.R("Ear2.R", 4f * Sn(3, t, L, 1.5f), 0, 0);
            return p;
        });

        d["Nervioso"] = (2f, true, t =>
        {
            float L = 2f; var p = new Pose();
            Breath(p, t, L, 6, 1.7f);
            p.hipsOffset = new Vector3(0, -0.012f, 0);
            p.R("Chest", 6f, 0, 0);
            p.R("Neck", 10f, 0, 0);
            p.R("Head", 8f, 7f * Sn(1, t, L), 0);
            p.R("Ear1.L", -45f, 0, -8f); p.R("Ear2.L", -15f, 0, 0);
            p.R("Ear1.R", -45f, 0, 8f); p.R("Ear2.R", -15f, 0, 0);
            p.R("Spine", 0.9f * Sn(24, t, L), 0.7f * Sn(26, t, L), 0);
            p.R("Tail", -10f, 0, 0);
            return p;
        });

        d["Forcejeo"] = (1.2f, true, t =>
        {
            float L = 1.2f; var p = new Pose();
            float k = Sn(3, t, L);
            p.R("Thigh.L", 35f * Mathf.Max(0f, k) - 10f, 0, 0);
            p.R("Thigh.R", 35f * Mathf.Max(0f, -k) - 10f, 0, 0);
            p.R("Foot.L", 25f * Mathf.Max(0f, k), 0, 0);
            p.R("Foot.R", 25f * Mathf.Max(0f, -k), 0, 0);
            p.R("FrontLeg.L", 25f * Sn(3, t, L, Mathf.PI * 0.5f), 0, 0);
            p.R("FrontLeg.R", -25f * Sn(3, t, L, Mathf.PI * 0.5f), 0, 0);
            p.R("Spine", 0, 14f * Sn(2, t, L), 0);
            p.R("Hips", 0, 0, 8f * Sn(2, t, L, 1f));
            p.R("Chest", 0, 0, -6f * Sn(2, t, L, 1f));
            p.R("Neck", 0, 20f * Sn(2, t, L, 0.5f), 0);
            p.R("Head", 10f * Sn(4, t, L), 0, 0);
            p.R("Ear1.L", -20f + 20f * Sn(4, t, L), 0, 6f * Sn(4, t, L, 1f));
            p.R("Ear1.R", -20f + 20f * Sn(4, t, L, 0.8f), 0, -6f * Sn(4, t, L, 1f));
            p.R("Ear2.L", 15f * Sn(4, t, L, 1f), 0, 0);
            p.R("Ear2.R", 15f * Sn(4, t, L, 1.8f), 0, 0);
            p.hipsOffset = new Vector3(0, 0.006f * Mathf.Abs(k), 0);
            return p;
        });

        d["Sobresalto"] = (0.6f, false, t =>
        {
            var p = new Pose();
            float e = t < 0.08f ? t / 0.08f : Mathf.Exp(-(t - 0.08f) * 7f);
            p.hipsOffset = new Vector3(0, 0.02f * e, 0);
            p.R("Ear1.L", -25f * e, 0, 10f * e); p.R("Ear1.R", -25f * e, 0, -10f * e);
            p.R("Head", -12f * e, 0, 0);
            p.R("Chest", -8f * e, 0, 0);
            p.R("Thigh.L", 15f * e, 0, 0); p.R("Thigh.R", 15f * e, 0, 0);
            p.R("Tail", 20f * e, 0, 0);
            return p;
        });

        d["Temblor"] = (1.5f, true, t =>
        {
            float L = 1.5f; var p = new Pose();
            Breath(p, t, L, 3, 1.4f);
            p.hipsOffset = new Vector3(0, -0.004f, 0);
            p.R("Spine", 0, 0, 1.8f * Sn(21, t, L));
            p.R("Chest", 0, 0, -1.5f * Sn(21, t, L, 1f));
            p.R("Neck", 0, 0, 1.2f * Sn(21, t, L, 2f));
            p.R("Head", 0, 1.2f * Sn(20, t, L), 0);
            p.R("Ear1.L", -15f + 3f * Sn(20, t, L), 0, 0);
            p.R("Ear1.R", -15f + 3f * Sn(20, t, L, 1f), 0, 0);
            return p;
        });

        d["Relajado"] = (5f, true, t =>
        {
            float L = 5f; var p = new Pose();
            Breath(p, t, L, 3, 1.3f);
            p.R("Ear1.L", -25f + 2f * Sn(1, t, L), 0, 15f);
            p.R("Ear1.R", -25f + 2f * Sn(1, t, L, 1f), 0, -15f);
            p.R("Ear2.L", -10f, 0, 0); p.R("Ear2.R", -10f, 0, 0);
            p.R("Head", 8f + 1.5f * Sn(1, t, L), 2f * Sn(1, t, L, 2f), 0);
            p.R("Neck", 5f, 0, 0);
            p.hipsOffset = new Vector3(0, -0.005f, 0);
            return p;
        });

        d["Salto"] = (0.7f, false, t =>
        {
            var p = new Pose();
            float crouch = t < 0.18f ? Smooth(t / 0.18f) : 1f - Smooth((t - 0.18f) / 0.1f);
            float push = t < 0.18f ? 0f : t < 0.35f ? Smooth((t - 0.18f) / 0.17f) : 1f - Smooth((t - 0.35f) / 0.2f);
            float land = t < 0.5f ? 0f : t < 0.6f ? Smooth((t - 0.5f) / 0.1f) : 1f - Smooth((t - 0.6f) / 0.1f);
            p.hipsOffset = new Vector3(0, -0.015f * crouch - 0.012f * land, 0);
            p.R("Chest", 10f * crouch - 16f * push + 6f * land, 0, 0);
            p.R("Head", 8f * crouch - 6f * push, 0, 0);
            p.R("Thigh.L", -10f * crouch + 55f * push, 0, 0); p.R("Thigh.R", -10f * crouch + 55f * push, 0, 0);
            p.R("Foot.L", 35f * push, 0, 0); p.R("Foot.R", 35f * push, 0, 0);
            p.R("FrontLeg.L", -40f * push + 10f * land, 0, 0); p.R("FrontLeg.R", -40f * push + 10f * land, 0, 0);
            p.R("Ear1.L", -22f * push, 0, 0); p.R("Ear1.R", -22f * push, 0, 0);
            p.R("Tail", 15f * push, 0, 0);
            return p;
        });

        return d;
    }

    static List<AnimationClip> BuildClips(Transform root, Dictionary<string, BoneDef> byName)
    {
        var result = new List<AnimationClip>();
        const float fps = 30f;
        foreach (var kv in Definitions())
        {
            string name = kv.Key;
            var (len, loop, fn) = kv.Value;
            var clip = new AnimationClip { name = "Conejo_" + name, frameRate = fps };

            int frames = Mathf.RoundToInt(len * fps);
            var rotCurves = new Dictionary<string, AnimationCurve[]>();
            foreach (var b in AnimatedBones) rotCurves[b] = new[] { new AnimationCurve(), new AnimationCurve(), new AnimationCurve(), new AnimationCurve() };
            var hipsPos = new[] { new AnimationCurve(), new AnimationCurve(), new AnimationCurve() };
            var chestScl = new[] { new AnimationCurve(), new AnimationCurve(), new AnimationCurve() };
            Vector3 hipsBase = byName["Hips"].t.localPosition;

            for (int f = 0; f <= frames; f++)
            {
                float t = f / fps;
                float tt = loop && f == frames ? 0f : t; // cierre exacto del ciclo
                var pose = fn(tt);
                foreach (var b in AnimatedBones)
                {
                    pose.rot.TryGetValue(b, out var e);
                    var q = Quaternion.Euler(e);
                    var c = rotCurves[b];
                    c[0].AddKey(t, q.x); c[1].AddKey(t, q.y); c[2].AddKey(t, q.z); c[3].AddKey(t, q.w);
                }
                var hp = hipsBase + pose.hipsOffset;
                hipsPos[0].AddKey(t, hp.x); hipsPos[1].AddKey(t, hp.y); hipsPos[2].AddKey(t, hp.z);
                chestScl[0].AddKey(t, pose.chestScale.x); chestScl[1].AddKey(t, pose.chestScale.y); chestScl[2].AddKey(t, pose.chestScale.z);
            }

            foreach (var b in AnimatedBones)
            {
                string path = AnimationUtility.CalculateTransformPath(byName[b].t, root);
                var c = rotCurves[b];
                clip.SetCurve(path, typeof(Transform), "m_LocalRotation.x", c[0]);
                clip.SetCurve(path, typeof(Transform), "m_LocalRotation.y", c[1]);
                clip.SetCurve(path, typeof(Transform), "m_LocalRotation.z", c[2]);
                clip.SetCurve(path, typeof(Transform), "m_LocalRotation.w", c[3]);
            }
            string hipsPath = AnimationUtility.CalculateTransformPath(byName["Hips"].t, root);
            clip.SetCurve(hipsPath, typeof(Transform), "m_LocalPosition.x", hipsPos[0]);
            clip.SetCurve(hipsPath, typeof(Transform), "m_LocalPosition.y", hipsPos[1]);
            clip.SetCurve(hipsPath, typeof(Transform), "m_LocalPosition.z", hipsPos[2]);
            string chestPath = AnimationUtility.CalculateTransformPath(byName["Chest"].t, root);
            clip.SetCurve(chestPath, typeof(Transform), "m_LocalScale.x", chestScl[0]);
            clip.SetCurve(chestPath, typeof(Transform), "m_LocalScale.y", chestScl[1]);
            clip.SetCurve(chestPath, typeof(Transform), "m_LocalScale.z", chestScl[2]);
            clip.EnsureQuaternionContinuity();

            var settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.loopTime = loop;
            AnimationUtility.SetAnimationClipSettings(clip, settings);

            string path2 = Dir + "/Animaciones/Conejo_" + name + ".anim";
            if (!AssetDatabase.IsValidFolder(Dir + "/Animaciones")) AssetDatabase.CreateFolder(Dir, "Animaciones");
            var existing = AssetDatabase.LoadAssetAtPath<AnimationClip>(path2);
            if (existing != null) { EditorUtility.CopySerialized(clip, existing); clip = existing; }
            else AssetDatabase.CreateAsset(clip, path2);
            result.Add(clip);
        }
        return result;
    }

    static AnimatorController BuildController(List<AnimationClip> clips)
    {
        string path = Dir + "/Conejo.controller";
        if (AssetDatabase.LoadAssetAtPath<AnimatorController>(path) != null) AssetDatabase.DeleteAsset(path);
        var ctrl = AnimatorController.CreateAnimatorControllerAtPath(path);
        var sm = ctrl.layers[0].stateMachine;
        AnimatorState first = null;
        int i = 0;
        foreach (var c in clips)
        {
            var st = sm.AddState(c.name.Replace("Conejo_", ""), new Vector3(260f + (i % 3) * 230f, 60f + (i / 3) * 80f, 0f));
            st.motion = c;
            if (st.name == "Tranquilo") first = st;
            i++;
        }
        if (first != null) sm.defaultState = first;
        EditorUtility.SetDirty(ctrl);
        return ctrl;
    }
}
