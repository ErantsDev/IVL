using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// Minijuegos de precisión con el mouse sobre el lomo del conejo (vista desde arriba):
///  - Rasurado: rasurar sólo la zona dorsal de 5 × 5 cm. Se califica lo cubierto y lo que se pasó.
///  - Desinfección: pasar la gasa con alcohol por toda la zona rasurada, del centro hacia afuera.
/// Se pinta sobre un "parche" que se ajusta a la forma del lomo. El parche es una malla con
/// piel (skinning) que usa los mismos huesos y pesos que el conejo, así que se deforma exactamente
/// igual que el pelaje al respirar, temblar o moverse y nunca queda escondido debajo de él.
/// </summary>
public class DorsalMinigame : MonoBehaviour
{
    public enum Kind { Shave, Disinfect }

    public struct Result
    {
        public Kind kind;
        public float grade;
        public string verdict;
        public string detail;
        public float coverage;
        public float overshootCm2;
        public int irritations;
        public float technique;
    }

    [Header("Escena")]
    public Camera minigameCamera;
    public Material patchMaterialTemplate;
    public Material furParticleMaterial;

    [Header("UI")]
    public GameObject panel;
    public TMP_Text titleText;
    public TMP_Text instructionsText;
    public TMP_Text statsText;
    public Button finishButton;
    public RectTransform brushCursor;
    public RectTransform scaleBar;
    public TMP_Text scaleBarLabel;
    public GameObject resultCard;
    public TMP_Text resultTitle;
    public TMP_Text resultGrade;
    public TMP_Text resultVerdict;
    public TMP_Text resultDetail;
    public Button continueButton;

    [Header("Parámetros")]
    public float patchSizeCm = 14f;
    public int texSize = 256;
    public float targetCm = 5f;
    [Tooltip("Margen (cm) que no cuenta como pasarse de la zona.")]
    public float toleranceCm = 0.3f;
    public float shaveBrushCm = 0.6f;
    public float gauzeBrushCm = 1.0f;
    [Tooltip("Velocidad del trazo (cm/s) a partir de la cual el conejo se sobresalta.")]
    public float fastStrokeCmPerSec = 32f;
    public float cameraHeight = 0.24f;
    [Tooltip("Posición del centro de la zona dorsal en coordenadas locales del conejo (z hacia la cabeza).")]
    public float dorsalCenterZ = -0.012f;
    [Tooltip("Separación del parche sobre la piel (m).")]
    public float surfaceOffset = 0.004f;

    public bool IsActive { get; private set; }

    static readonly Color32 Clear = new Color32(0, 0, 0, 0);
    static readonly Color32 Skin = new Color32(236, 168, 160, 255);
    static readonly Color32 SkinWet = new Color32(205, 150, 165, 255);
    static readonly Color32 FurWet = new Color32(205, 214, 232, 110);
    static readonly Color32 Guide = new Color32(255, 214, 64, 235);
    static readonly Color32 GuideCenter = new Color32(90, 200, 255, 230);

    RabbitController rabbit;
    RabbitController patchOwner;
    Kind kind;
    bool educational;
    Action<Result> onDone;

    GameObject patchGO;
    MeshCollider patchCollider;
    Material patchMaterial;
    Texture2D tex;
    Color32[] pixels;
    byte[] shaved;
    byte[] wet;
    byte[] skinShade;
    bool[] guide;
    bool dirty;

    Mesh patchMesh;

    bool finished;
    bool hadLast;
    Vector2 lastUV;
    float lastTime;
    float irritationCooldown;
    int irritations;
    string flashText;
    float flashUntil;
    readonly List<float> radialSamples = new List<float>();
    Vector2 shavedCentroid = new Vector2(0.5f, 0.5f);

    Camera playerCam;
    AudioSource buzz;
    ParticleSystem fur;
    Canvas canvas;

    void Awake()
    {
        if (panel != null) panel.SetActive(false);
        if (minigameCamera != null) minigameCamera.gameObject.SetActive(false);
        if (finishButton != null) finishButton.onClick.AddListener(Finish);
        if (continueButton != null) continueButton.onClick.AddListener(Close);
        canvas = panel != null ? panel.GetComponentInParent<Canvas>() : null;
    }

    // ------------------------------------------------------------------
    //  Inicio / fin
    // ------------------------------------------------------------------

    public void Begin(Kind k, RabbitController target, bool edu, Action<Result> done)
    {
        rabbit = target;
        kind = k;
        educational = edu;
        onDone = done;
        finished = false;
        hadLast = false;
        irritations = 0;
        radialSamples.Clear();

        if (patchOwner != rabbit || patchGO == null) BuildPatch();
        if (kind == Kind.Disinfect) ComputeShavedCentroid();
        BuildGuide();
        RedrawAll();
        patchCollider.enabled = true;

        rabbit.ForcedMood = kind == Kind.Shave ? "Relajado" : "Temblor";

        // Cámara cenital con la cabeza del conejo hacia arriba de la pantalla.
        Vector3 center = PatchCenterWorld();
        Vector3 fwd = Vector3.ProjectOnPlane(rabbit.transform.forward, Vector3.up).normalized;
        minigameCamera.transform.SetPositionAndRotation(center + Vector3.up * cameraHeight, Quaternion.LookRotation(Vector3.down, fwd));
        minigameCamera.gameObject.SetActive(true);
        playerCam = ObjectGrabber.Instance != null ? ObjectGrabber.Instance.Cam : null;
        if (playerCam != null) playerCam.enabled = false;
        // Con gafas la vista cenital ocupa la vista como en PC; el control derecho mueve el puntero.
        if (VRMode.Active) VRScreen.Show(minigameCamera, panel != null ? (RectTransform)panel.transform : null, PatchCenterWorld());

        EnsureEffects();

        if (panel != null) panel.SetActive(true);
        if (resultCard != null) resultCard.SetActive(false);
        if (finishButton != null) finishButton.gameObject.SetActive(true);
        if (titleText != null)
            titleText.text = kind == Kind.Shave ? "Rasurado de la zona dorsal (5 × 5 cm)" : "Desinfección con alcohol";
        if (instructionsText != null)
            instructionsText.text = kind == Kind.Shave
                ? (educational
                    ? "Mantén el clic izquierdo y pasa la rasuradora <b>sólo dentro del cuadro amarillo</b>. Trazos lentos y cortos: si vas muy rápido el conejo se asusta."
                    : "Mantén el clic izquierdo para rasurar una zona de <b>5 × 5 cm</b> sobre la línea media del lomo. Usa la barra de escala como referencia.")
                : (educational
                    ? "Mantén el clic izquierdo y pasa la gasa con alcohol por <b>toda la piel rasurada</b>. Empieza en el <b>punto azul</b> (centro) y avanza en espiral hacia afuera."
                    : "Mantén el clic izquierdo para desinfectar toda la zona rasurada con la gasa empapada en alcohol, con la técnica correcta.");

        IsActive = true;
        if (GameFlow.Instance != null) GameFlow.Instance.EnterMinigame();
        else { Cursor.lockState = CursorLockMode.None; Cursor.visible = true; }
    }

    void Close()
    {
        if (!IsActive) return;
        IsActive = false;
        if (panel != null) panel.SetActive(false);
        if (VRMode.Active) VRScreen.Hide(); // devuelve la interfaz a su lugar antes de apagar la cámara
        if (minigameCamera != null) minigameCamera.gameObject.SetActive(false);
        if (playerCam != null) playerCam.enabled = true;
        if (patchCollider != null) patchCollider.enabled = false;
        if (buzz != null) buzz.Stop();
        if (rabbit != null) rabbit.ForcedMood = null;
        // Las guías del modo educativo desaparecen; lo rasurado/desinfectado queda en el conejo.
        if (guide != null) { Array.Clear(guide, 0, guide.Length); RedrawAll(); }
        if (GameFlow.Instance != null) GameFlow.Instance.ExitMinigame();
        var r = lastResult;
        onDone?.Invoke(r);
    }

    Result lastResult;

    void Finish()
    {
        if (!IsActive || finished) return;
        finished = true;
        if (buzz != null) buzz.Stop();
        lastResult = kind == Kind.Shave ? EvaluateShave() : EvaluateDisinfect();

        if (finishButton != null) finishButton.gameObject.SetActive(false);
        if (brushCursor != null) brushCursor.gameObject.SetActive(false);
        if (resultCard != null) resultCard.SetActive(true);
        if (resultTitle != null) resultTitle.text = kind == Kind.Shave ? "Resultado del rasurado" : "Resultado de la desinfección";
        if (resultGrade != null)
        {
            resultGrade.text = "Nota: " + lastResult.grade.ToString("0.0") + " / 10";
            resultGrade.color = lastResult.grade >= 8f ? new Color(0.45f, 0.9f, 0.65f) : lastResult.grade >= 6f ? new Color(1f, 0.8f, 0.4f) : new Color(1f, 0.45f, 0.4f);
        }
        if (resultVerdict != null) resultVerdict.text = lastResult.verdict;
        if (resultDetail != null) resultDetail.text = lastResult.detail;
    }

    // ------------------------------------------------------------------
    //  Bucle
    // ------------------------------------------------------------------

    void Update()
    {
        if (!IsActive) return;
        bool paused = GameFlow.Instance != null && GameFlow.Instance.CurrentState != GameFlow.State.Minigame;
        if (paused) { if (buzz != null && buzz.isPlaying) buzz.Pause(); return; }

        if (finished)
        {
            if (ConfirmPressed()) Close();
            FlushTexture();
            return;
        }

        if (ConfirmPressed()) { Finish(); return; }

        Vector2 mouse = MousePosition();
        bool pressing = MouseHeld() && !PointerOverUI();
        // Con gafas, si el láser no está sobre la pantalla del minijuego, no se rasura.
        bool noPointer = VRMode.Active && (VRScreen.Current == null || !VRScreen.Current.HasPointer);
        if (noPointer) pressing = false;
        Ray ray = minigameCamera.ScreenPointToRay(mouse);
        RaycastHit hit = default;
        bool onPatch = !noPointer && patchCollider.Raycast(ray, out hit, 2f);

        UpdateCursor(mouse, onPatch);

        if (pressing && onPatch)
        {
            Vector2 uv = hit.textureCoord;
            float now = Time.time;
            if (hadLast)
            {
                float dt = Mathf.Max(now - lastTime, 0.0001f);
                float cm = Vector2.Distance(uv, lastUV) * patchSizeCm;
                float speed = cm / dt;
                if (kind == Kind.Shave && speed > fastStrokeCmPerSec && cm < 6f && irritationCooldown <= 0f)
                {
                    irritations++;
                    irritationCooldown = 0.9f;
                    rabbit.PlayReaction("Sobresalto", 0.6f);
                    rabbit.AddStress(20f);
                    Flash("¡Muy rápido! El conejo se sobresaltó.");
                }
                StrokeTo(lastUV, uv, hit.point);
            }
            else StrokeTo(uv, uv, hit.point);
            lastUV = uv;
            lastTime = now;
            hadLast = true;
            SetBuzz(kind == Kind.Shave);
        }
        else
        {
            hadLast = false;
            SetBuzz(false);
        }

        irritationCooldown -= Time.deltaTime;
        FlushTexture();
        UpdateStats();
    }

    void StrokeTo(Vector2 from, Vector2 to, Vector3 worldPoint)
    {
        float brushCm = kind == Kind.Shave ? shaveBrushCm : gauzeBrushCm;
        float rUV = brushCm / patchSizeCm;
        float dist = Vector2.Distance(from, to);
        int steps = Mathf.Max(1, Mathf.CeilToInt(dist / (rUV * 0.35f)));
        int newPixels = 0;
        for (int s = 1; s <= steps; s++)
        {
            Vector2 p = Vector2.Lerp(from, to, s / (float)steps);
            newPixels += Stamp(p, rUV);
        }
        if (kind == Kind.Shave && newPixels > 0 && fur != null)
        {
            fur.transform.position = worldPoint + Vector3.up * 0.003f;
            fur.Emit(Mathf.Clamp(newPixels / 6, 1, 12));
        }
    }

    int Stamp(Vector2 uv, float rUV)
    {
        int n = texSize;
        float cx = uv.x * n, cy = uv.y * n, r = rUV * n, r2 = r * r;
        int x0 = Mathf.Max(0, Mathf.FloorToInt(cx - r)), x1 = Mathf.Min(n - 1, Mathf.CeilToInt(cx + r));
        int y0 = Mathf.Max(0, Mathf.FloorToInt(cy - r)), y1 = Mathf.Min(n - 1, Mathf.CeilToInt(cy + r));
        int changed = 0, changedOnSkin = 0;
        for (int y = y0; y <= y1; y++)
        {
            float dy = y + 0.5f - cy;
            for (int x = x0; x <= x1; x++)
            {
                float dx = x + 0.5f - cx;
                if (dx * dx + dy * dy > r2) continue;
                int i = y * n + x;
                if (kind == Kind.Shave)
                {
                    if (shaved[i] != 0) continue;
                    shaved[i] = 1;
                }
                else
                {
                    if (wet[i] != 0) continue;
                    wet[i] = 1;
                    if (shaved[i] != 0) changedOnSkin++;
                }
                pixels[i] = Compose(i);
                changed++;
            }
        }
        if (changed > 0) dirty = true;
        if (kind == Kind.Disinfect && changedOnSkin > 0)
            radialSamples.Add(Vector2.Distance(uv, shavedCentroid) * patchSizeCm);
        return changed;
    }

    // ------------------------------------------------------------------
    //  Evaluación
    // ------------------------------------------------------------------

    void Measure(out int inside, out int targetTotal, out int outsideTol)
    {
        int n = texSize;
        float half = targetCm * 0.5f / patchSizeCm * n;
        float halfTol = (targetCm * 0.5f + toleranceCm) / patchSizeCm * n;
        float c = n * 0.5f;
        inside = 0; targetTotal = 0; outsideTol = 0;
        for (int y = 0; y < n; y++)
        {
            float dy = Mathf.Abs(y + 0.5f - c);
            for (int x = 0; x < n; x++)
            {
                float dx = Mathf.Abs(x + 0.5f - c);
                int i = y * n + x;
                bool inTarget = dx <= half && dy <= half;
                if (inTarget) { targetTotal++; if (shaved[i] != 0) inside++; }
                else if (shaved[i] != 0 && (dx > halfTol || dy > halfTol)) outsideTol++;
            }
        }
    }

    float PixelAreaCm2 => (patchSizeCm / texSize) * (patchSizeCm / texSize);

    Result EvaluateShave()
    {
        Measure(out int inside, out int total, out int outside);
        float cov = total > 0 ? inside / (float)total : 0f;
        float over = outside * PixelAreaCm2;
        float grade = 10f * Mathf.Min(1f, cov / 0.95f) - 0.5f * over - 0.5f * Mathf.Min(irritations, 4);
        grade = Mathf.Clamp(Mathf.Round(grade * 10f) / 10f, 0f, 10f);

        string verdict;
        if (cov < 0.85f && over > 3f) verdict = "Incompleto y te pasaste: quedó pelo dentro de la zona y rasuraste fuera de ella.";
        else if (cov < 0.85f) verdict = "Incompleto: quedó pelo dentro de la zona de 5 × 5 cm.";
        else if (over > 3f) verdict = "Te pasaste: rasuraste fuera de la zona de 5 × 5 cm.";
        else if (grade >= 9f) verdict = "¡Excelente! Rasurado preciso y completo.";
        else if (grade >= 7f) verdict = "Bien hecho: zona rasurada correctamente.";
        else verdict = "Regular: intenta ser más preciso y cuidadoso.";

        string detail = "Zona de 5 × 5 cm rasurada: " + Mathf.RoundToInt(cov * 100f) + "%\n" +
                        "Rasurado fuera de la zona: " + over.ToString("0.0") + " cm²" + (over <= 0.05f ? " (¡nada!)" : "") + "\n" +
                        "Sobresaltos del conejo por trazos rápidos: " + irritations;
        return new Result { kind = Kind.Shave, grade = grade, verdict = verdict, detail = detail, coverage = cov, overshootCm2 = over, irritations = irritations };
    }

    Result EvaluateDisinfect()
    {
        int shavedCount = 0, covered = 0;
        for (int i = 0; i < shaved.Length; i++)
        {
            if (shaved[i] == 0) continue;
            shavedCount++;
            if (wet[i] != 0) covered++;
        }
        float cov = shavedCount > 0 ? covered / (float)shavedCount : 0f;
        float corr = Correlation(radialSamples);
        float technique = radialSamples.Count < 4 ? 0f : Mathf.Clamp01((corr + 0.1f) / 0.8f);
        float grade = 10f * (0.7f * Mathf.Min(1f, cov / 0.9f) + 0.3f * technique);
        grade = Mathf.Clamp(Mathf.Round(grade * 10f) / 10f, 0f, 10f);

        string verdict;
        if (cov < 0.9f) verdict = "Faltó desinfectar parte de la piel rasurada.";
        else if (technique < 0.5f) verdict = "Cubriste la zona, pero la técnica correcta es del centro hacia afuera.";
        else verdict = "¡Bien! Desinfección completa y con buena técnica.";

        string detail = "Piel rasurada desinfectada: " + Mathf.RoundToInt(cov * 100f) + "%\n" +
                        "Técnica (del centro hacia afuera): " + Mathf.RoundToInt(technique * 100f) + "%";
        return new Result { kind = Kind.Disinfect, grade = grade, verdict = verdict, detail = detail, coverage = cov, technique = technique };
    }

    static float Correlation(List<float> r)
    {
        int n = r.Count;
        if (n < 3) return 0f;
        double mx = (n - 1) * 0.5, my = 0;
        for (int i = 0; i < n; i++) my += r[i];
        my /= n;
        double sxy = 0, sxx = 0, syy = 0;
        for (int i = 0; i < n; i++)
        {
            double dx = i - mx, dy = r[i] - my;
            sxy += dx * dy; sxx += dx * dx; syy += dy * dy;
        }
        if (sxx <= 0 || syy <= 1e-9) return 0f;
        return (float)(sxy / Math.Sqrt(sxx * syy));
    }

    void UpdateStats()
    {
        if (statsText == null) return;
        string s;
        if (kind == Kind.Shave)
        {
            if (educational)
            {
                Measure(out int inside, out int total, out int outside);
                float cov = total > 0 ? inside / (float)total : 0f;
                s = "Zona cubierta: <b>" + Mathf.RoundToInt(cov * 100f) + "%</b>     Fuera de la zona: <b>" + (outside * PixelAreaCm2).ToString("0.0") + " cm²</b>     Sobresaltos: <b>" + irritations + "</b>";
            }
            else s = "Enter o botón «Terminar» cuando acabes.";
        }
        else
        {
            if (educational)
            {
                int sh = 0, cv = 0;
                for (int i = 0; i < shaved.Length; i++) if (shaved[i] != 0) { sh++; if (wet[i] != 0) cv++; }
                s = "Piel rasurada desinfectada: <b>" + (sh > 0 ? Mathf.RoundToInt(100f * cv / sh) : 0) + "%</b>   (necesitas 90% o más)";
            }
            else s = "Enter o botón «Terminar» cuando acabes.";
        }
        if (Time.time < flashUntil) s += "\n<color=#FF9C8C><b>" + flashText + "</b></color>";
        statsText.text = s;
    }

    void Flash(string text)
    {
        flashText = text;
        flashUntil = Time.time + 1.6f;
    }

    // ------------------------------------------------------------------
    //  Parche ajustado al lomo
    // ------------------------------------------------------------------

    Vector3 PatchCenterWorld()
    {
        return patchGO != null ? patchGO.transform.TransformPoint(patchCenterLocal) : rabbit.transform.position;
    }

    Vector3 patchCenterLocal;

    void BuildPatch()
    {
        if (patchGO != null) Destroy(patchGO);
        if (patchMesh != null) Destroy(patchMesh);
        patchOwner = rabbit;
        var smr = rabbit.skin;

        // Malla del cuerpo en la pose actual, sin cabeza/orejas/patas delanteras (para que los rayos no las toquen).
        // Importante: BakeMesh(.., true) devuelve la malla en el espacio local del renderer, SIN la escala
        // del conejo (cada conejo tiene un tamaño distinto según su peso). Antes el objeto temporal no
        // llevaba esa escala, así que en los conejos grandes la superficie medida quedaba ~1 cm por debajo
        // de la real y el parche (con el cuadro guía y lo rasurado) quedaba escondido bajo el pelaje.
        var baked = new Mesh();
        smr.BakeMesh(baked, true);
        var src = smr.sharedMesh;
        var bw = src.boneWeights;
        var bones = smr.bones;
        var excluded = new bool[bones.Length];
        for (int b = 0; b < bones.Length; b++)
        {
            string nm = bones[b].name;
            excluded[b] = nm.StartsWith("Ear") || nm == "Head" || nm == "Neck" || nm.StartsWith("FrontLeg");
        }
        var tris = baked.triangles;
        var keep = new List<int>(tris.Length);
        for (int t = 0; t < tris.Length; t += 3)
        {
            if (excluded[bw[tris[t]].boneIndex0] || excluded[bw[tris[t + 1]].boneIndex0] || excluded[bw[tris[t + 2]].boneIndex0]) continue;
            keep.Add(tris[t]); keep.Add(tris[t + 1]); keep.Add(tris[t + 2]);
        }
        var body = new Mesh { indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
        body.vertices = baked.vertices;
        body.triangles = keep.ToArray();
        var tmp = new GameObject("_CuerpoTemporal");
        tmp.transform.SetPositionAndRotation(smr.transform.position, smr.transform.rotation);
        tmp.transform.localScale = smr.transform.lossyScale;
        var bodyCol = tmp.AddComponent<MeshCollider>();
        bodyCol.sharedMesh = body;
        Physics.SyncTransforms();

        Transform root = rabbit.transform;
        Vector3 up = root.up, right = root.right, fwd = root.forward;
        Vector3 c0 = root.position + fwd * (dorsalCenterZ * root.lossyScale.z);
        Vector3 center = c0 + up * 0.2f;
        if (bodyCol.Raycast(new Ray(c0 + up * 0.5f, -up), out var ch, 1f)) center = ch.point;

        const int N = 40;
        float size = patchSizeCm / 100f;
        var verts = new Vector3[(N + 1) * (N + 1)];
        var uvs = new Vector2[verts.Length];
        var valid = new bool[verts.Length];
        var weights = new BoneWeight[verts.Length];
        var keepTris = body.triangles;
        // El parche vive en el mismo espacio que la malla del conejo y lleva sus mismos pesos de huesos.
        Transform parent = smr.transform;
        int spineIndex = Mathf.Max(0, Array.IndexOf(bones, rabbit.Spine));
        for (int j = 0; j <= N; j++)
        {
            for (int i = 0; i <= N; i++)
            {
                float u = i / (float)N, v = j / (float)N;
                Vector3 p = center + right * ((u - 0.5f) * size) + fwd * ((v - 0.5f) * size);
                int k = j * (N + 1) + i;
                uvs[k] = new Vector2(u, v);
                if (bodyCol.Raycast(new Ray(p + up * 0.25f, -up), out var h, 0.6f))
                {
                    verts[k] = parent.InverseTransformPoint(h.point + h.normal * surfaceOffset);
                    weights[k] = BlendWeights(bw, keepTris, h.triangleIndex, h.barycentricCoordinate);
                    valid[k] = true;
                }
                else
                {
                    verts[k] = parent.InverseTransformPoint(p);
                    weights[k] = new BoneWeight { boneIndex0 = spineIndex, weight0 = 1f };
                }
            }
        }
        var indices = new List<int>();
        for (int j = 0; j < N; j++)
        {
            for (int i = 0; i < N; i++)
            {
                int a = j * (N + 1) + i, b = a + 1, c = a + N + 1, d = c + 1;
                if (!(valid[a] && valid[b] && valid[c] && valid[d])) continue;
                indices.Add(a); indices.Add(c); indices.Add(b);
                indices.Add(b); indices.Add(c); indices.Add(d);
            }
        }
        Destroy(tmp);
        Destroy(body);
        Destroy(baked);

        patchGO = new GameObject("ParcheDorsal");
        patchGO.transform.SetParent(parent, false);

        var mesh = new Mesh { name = "ParcheDorsal" };
        mesh.vertices = verts;
        mesh.uv = uvs;
        mesh.SetTriangles(indices, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        // Poses de enlace = pose actual de los huesos: en este cuadro el parche queda exactamente donde
        // se midió y de aquí en adelante se deforma con el conejo (respiración, temblor, etc.).
        mesh.boneWeights = weights;
        var bindposes = new Matrix4x4[bones.Length];
        for (int b = 0; b < bones.Length; b++)
            bindposes[b] = bones[b].worldToLocalMatrix * patchGO.transform.localToWorldMatrix;
        mesh.bindposes = bindposes;
        patchMesh = mesh;

        var sk = patchGO.AddComponent<SkinnedMeshRenderer>();
        sk.sharedMesh = mesh;
        sk.bones = bones;
        sk.rootBone = smr.rootBone;
        sk.updateWhenOffscreen = true;
        sk.quality = SkinQuality.Bone4;
        sk.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        sk.receiveShadows = true;
        patchMaterial = new Material(patchMaterialTemplate);
        sk.sharedMaterial = patchMaterial;
        patchCollider = patchGO.AddComponent<MeshCollider>();
        patchCollider.sharedMesh = mesh;
        patchCollider.enabled = false;
        patchCenterLocal = patchGO.transform.InverseTransformPoint(center);

        int n = texSize;
        tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, name = "ParcheDorsal" };
        pixels = new Color32[n * n];
        shaved = new byte[n * n];
        wet = new byte[n * n];
        skinShade = new byte[n * n];
        guide = new bool[n * n];
        var rng = new System.Random(rabbit.Code.GetHashCode());
        for (int i = 0; i < skinShade.Length; i++) skinShade[i] = (byte)rng.Next(0, 22);
        patchMaterial.SetTexture("_BaseMap", tex);
        patchMaterial.mainTexture = tex;
    }

    /// <summary>
    /// Pesos de huesos en un punto de un triángulo del conejo: mezcla de los pesos de sus tres
    /// vértices según las coordenadas baricéntricas (se quedan los 4 más fuertes).
    /// </summary>
    static BoneWeight BlendWeights(BoneWeight[] bw, int[] tris, int triIndex, Vector3 bary)
    {
        var acc = new Dictionary<int, float>(8);
        void Add(BoneWeight w, float f)
        {
            if (w.weight0 > 0f) acc[w.boneIndex0] = (acc.TryGetValue(w.boneIndex0, out var a0) ? a0 : 0f) + w.weight0 * f;
            if (w.weight1 > 0f) acc[w.boneIndex1] = (acc.TryGetValue(w.boneIndex1, out var a1) ? a1 : 0f) + w.weight1 * f;
            if (w.weight2 > 0f) acc[w.boneIndex2] = (acc.TryGetValue(w.boneIndex2, out var a2) ? a2 : 0f) + w.weight2 * f;
            if (w.weight3 > 0f) acc[w.boneIndex3] = (acc.TryGetValue(w.boneIndex3, out var a3) ? a3 : 0f) + w.weight3 * f;
        }
        int t = triIndex * 3;
        Add(bw[tris[t]], bary.x);
        Add(bw[tris[t + 1]], bary.y);
        Add(bw[tris[t + 2]], bary.z);

        var list = new List<KeyValuePair<int, float>>(acc);
        list.Sort((x, y) => y.Value.CompareTo(x.Value));
        float sum = 0f;
        for (int i = 0; i < list.Count && i < 4; i++) sum += list[i].Value;
        if (sum <= 0f) return new BoneWeight { boneIndex0 = bw[tris[t]].boneIndex0, weight0 = 1f };
        var r = new BoneWeight();
        if (list.Count > 0) { r.boneIndex0 = list[0].Key; r.weight0 = list[0].Value / sum; }
        if (list.Count > 1) { r.boneIndex1 = list[1].Key; r.weight1 = list[1].Value / sum; }
        if (list.Count > 2) { r.boneIndex2 = list[2].Key; r.weight2 = list[2].Value / sum; }
        if (list.Count > 3) { r.boneIndex3 = list[3].Key; r.weight3 = list[3].Value / sum; }
        return r;
    }

    void ComputeShavedCentroid()
    {
        int n = texSize;
        double sx = 0, sy = 0; int cnt = 0;
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
                if (shaved[y * n + x] != 0) { sx += x + 0.5; sy += y + 0.5; cnt++; }
        shavedCentroid = cnt > 0 ? new Vector2((float)(sx / cnt / n), (float)(sy / cnt / n)) : new Vector2(0.5f, 0.5f);
    }

    void BuildGuide()
    {
        int n = texSize;
        Array.Clear(guide, 0, guide.Length);
        if (!educational) return;
        if (kind == Kind.Shave)
        {
            float half = targetCm * 0.5f / patchSizeCm * n;
            float c = n * 0.5f;
            int lo = Mathf.RoundToInt(c - half), hi = Mathf.RoundToInt(c + half);
            for (int t = lo - 1; t <= hi + 1; t++)
            {
                bool dash = ((t - lo) / 6) % 2 == 0;
                if (!dash) continue;
                for (int w = -1; w <= 0; w++)
                {
                    SetGuide(t, lo + w); SetGuide(t, hi - w);
                    SetGuide(lo + w, t); SetGuide(hi - w, t);
                }
            }
        }
        else
        {
            float cx = shavedCentroid.x * n, cy = shavedCentroid.y * n;
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float d = Mathf.Sqrt((x + 0.5f - cx) * (x + 0.5f - cx) + (y + 0.5f - cy) * (y + 0.5f - cy));
                    if (d < 4.5f) guide[y * n + x] = true;
                }
        }
    }

    void SetGuide(int x, int y)
    {
        if (x < 0 || y < 0 || x >= texSize || y >= texSize) return;
        guide[y * texSize + x] = true;
    }

    Color32 Compose(int i)
    {
        if (shaved[i] != 0)
        {
            var c = wet[i] != 0 ? SkinWet : Skin;
            byte d = skinShade[i];
            return new Color32((byte)(c.r - d), (byte)(c.g - d), (byte)(c.b - d / 2), 255);
        }
        if (guide[i]) return kind == Kind.Shave ? Guide : GuideCenter;
        if (wet[i] != 0) return FurWet;
        return Clear;
    }

    void RedrawAll()
    {
        for (int i = 0; i < pixels.Length; i++)
        {
            // En el modo desinfección el punto guía se dibuja encima de la piel.
            if (kind == Kind.Disinfect && guide[i] && wet[i] == 0) { pixels[i] = GuideCenter; continue; }
            pixels[i] = Compose(i);
        }
        dirty = true;
        FlushTexture();
    }

    void FlushTexture()
    {
        if (!dirty || tex == null) return;
        dirty = false;
        tex.SetPixels32(pixels);
        tex.Apply(false);
    }

    // ------------------------------------------------------------------
    //  Efectos, cursor e interfaz
    // ------------------------------------------------------------------

    void EnsureEffects()
    {
        if (buzz == null)
        {
            buzz = gameObject.AddComponent<AudioSource>();
            buzz.clip = ProceduralAudio.ClipperBuzz();
            buzz.loop = true;
            buzz.playOnAwake = false;
            buzz.spatialBlend = 0f;
            buzz.volume = 0.25f;
        }
        if (fur == null && furParticleMaterial != null)
        {
            var go = new GameObject("PelusaRasurado");
            go.transform.SetParent(transform, false);
            fur = go.AddComponent<ParticleSystem>();
            fur.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = fur.main;
            main.playOnAwake = false;
            main.loop = false;
            main.duration = 1f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.5f, 1.1f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.05f, 0.22f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.0025f, 0.006f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 1f, 1f, 0.95f), new Color(0.93f, 0.9f, 0.86f, 0.9f));
            main.gravityModifier = 0.35f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 600;
            var em = fur.emission;
            em.enabled = false;
            var shape = fur.shape;
            shape.shapeType = ParticleSystemShapeType.Hemisphere;
            shape.radius = 0.004f;
            shape.rotation = new Vector3(-90f, 0f, 0f);
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = furParticleMaterial;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }
    }

    void SetBuzz(bool on)
    {
        if (buzz == null) return;
        if (on && !buzz.isPlaying) buzz.Play();
        else if (!on && buzz.isPlaying) buzz.Stop();
    }

    void UpdateCursor(Vector2 mouse, bool onPatch)
    {
        if (canvas == null) return;
        // Lienzo donde está el cursor (en VR, el de la pantalla flotante, con su cámara).
        var cv = brushCursor != null ? brushCursor.GetComponentInParent<Canvas>() : canvas;
        if (cv == null) cv = canvas;
        cv = cv.rootCanvas;
        Camera uiCam = cv.renderMode == RenderMode.ScreenSpaceOverlay ? null : cv.worldCamera;
        float scaleFactor = cv.scaleFactor > 0f ? cv.scaleFactor : 1f;

        // Píxeles de pantalla por centímetro a la altura del lomo.
        Vector3 c = PatchCenterWorld();
        Vector3 a = minigameCamera.WorldToScreenPoint(c);
        Vector3 b = minigameCamera.WorldToScreenPoint(c + minigameCamera.transform.right * 0.01f);
        float pxPerCm = Vector2.Distance(a, b);

        if (brushCursor != null)
        {
            brushCursor.gameObject.SetActive(!PointerOverUI());
            float brushCm = kind == Kind.Shave ? shaveBrushCm : gauzeBrushCm;
            float d = 2f * brushCm * pxPerCm / scaleFactor;
            brushCursor.sizeDelta = new Vector2(d, d);
            var parentRt = (RectTransform)brushCursor.parent;
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(parentRt, mouse, uiCam, out var local))
                brushCursor.localPosition = local;
            var img = brushCursor.GetComponent<Graphic>();
            if (img != null) img.color = onPatch ? new Color(1f, 1f, 1f, 0.95f) : new Color(1f, 1f, 1f, 0.35f);
        }
        if (scaleBar != null)
        {
            scaleBar.sizeDelta = new Vector2(pxPerCm / scaleFactor, scaleBar.sizeDelta.y);
            if (scaleBarLabel != null) scaleBarLabel.text = "1 cm";
        }
    }

    static bool PointerOverUI() => VRMode.Active ? VRLaser.OverUI : EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();

    static Vector2 MousePosition() => GameInput.PointerPosition();

    static bool MouseHeld() => GameInput.PrimaryHeld();

    static bool ConfirmPressed() => GameInput.ConfirmPressed();

    // ------------------------------------------------------------------
    //  Para pruebas automáticas
    // ------------------------------------------------------------------

    /// <summary>Pinta un trazo en coordenadas de la zona (cm desde el centro; x a la derecha, y hacia la cabeza).</summary>
    public void DebugStroke(Vector2 fromCm, Vector2 toCm)
    {
        Vector2 f = new Vector2(0.5f + fromCm.x / patchSizeCm, 0.5f + fromCm.y / patchSizeCm);
        Vector2 t = new Vector2(0.5f + toCm.x / patchSizeCm, 0.5f + toCm.y / patchSizeCm);
        StrokeTo(f, t, PatchCenterWorld());
        FlushTexture();
    }

    public void DebugFinish() => Finish();
    public void DebugClose() => Close();
}
