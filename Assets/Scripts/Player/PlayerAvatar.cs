using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Va en el jugador. Coloca el avatar que corresponde al sexo registrado en el formulario
/// (químico o química) a los pies del CharacterController.
///
/// Como el juego es en primera persona, el avatar vive en la capa "Avatar", que la cámara del
/// jugador no dibuja (así no se ve el interior de la cabeza); sí lo dibuja la cámara del espejo.
/// Además se agrega una copia "sólo sombra" en la capa normal para que el jugador vea su sombra.
/// </summary>
[DefaultExecutionOrder(-50)]
public class PlayerAvatar : MonoBehaviour
{
    public const string LayerName = "Avatar";

    public GameObject malePrefab;
    public GameObject femalePrefab;
    [Tooltip("Proyecta la sombra del avatar también en la vista en primera persona.")]
    public bool firstPersonShadow = true;
    [Tooltip("Material de los guantes de nitrilo (se ven en las manos al ponérselos en el nivel 2).")]
    public Material gloveMaterial;

    bool glovesOn;
    public AvatarHands Hands { get; private set; }

    /// <summary>Pone (o quita) los guantes en las manos del avatar; se conserva si cambia el avatar.</summary>
    public void SetGloves(bool on)
    {
        glovesOn = on;
        if (Hands != null) Hands.SetGloves(on);
    }

    public bool GlovesOn => glovesOn;

    public static PlayerAvatar Instance { get; private set; }

    public GameObject Current { get; private set; }
    public AvatarAnimatorDriver Driver { get; private set; }
    public Sexo CurrentSex { get; private set; } = Sexo.SinElegir;

    void Awake()
    {
        Instance = this;
        StudentProfile.EnsureLoaded();
        StudentProfile.Changed += Refresh;
        Refresh();
    }

    void OnDestroy()
    {
        StudentProfile.Changed -= Refresh;
        if (Instance == this) Instance = null;
    }

    /// <summary>Crea (o cambia) el avatar según el sexo registrado. Sin datos: químico.</summary>
    public void Refresh()
    {
        var sex = StudentProfile.Sexo == Sexo.Mujer ? Sexo.Mujer : Sexo.Hombre;
        if (Current != null && sex == CurrentSex) return;
        if (Current != null) Destroy(Current);
        groundOffset = float.NaN;

        var prefab = sex == Sexo.Mujer ? femalePrefab : malePrefab;
        if (prefab == null) return;
        CurrentSex = sex;

        Current = Instantiate(prefab, transform);
        Current.name = "Avatar";
        var cc = GetComponent<CharacterController>();
        float feet = cc != null ? cc.center.y - cc.height * 0.5f - cc.skinWidth : 0f;
        Current.transform.localPosition = new Vector3(0f, feet, 0f);
        Current.transform.localRotation = Quaternion.identity;

        int layer = LayerMask.NameToLayer(LayerName);
        if (layer >= 0)
            foreach (var t in Current.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = layer;

        var bodySmr = Current.GetComponentInChildren<SkinnedMeshRenderer>();
        Hands = Current.AddComponent<AvatarHands>();
        if (bodySmr != null) Hands.Init(bodySmr, gloveMaterial);
        Hands.SetGloves(glovesOn);

        if (firstPersonShadow) AddShadowCasters(Current);
        Driver = Current.GetComponentInChildren<AvatarAnimatorDriver>();
    }

    float groundOffset = float.NaN;

    void LateUpdate()
    {
        // Pies sobre el piso: el CharacterController "flota" unos centímetros (skinWidth),
        // así que se mide la distancia real al suelo mientras está apoyado.
        if (Current == null) return;
        var cc = GetComponent<CharacterController>();
        if (cc == null || !cc.enabled) return;
        if (cc.isGrounded && Physics.Raycast(transform.position + Vector3.up * 0.3f, Vector3.down, out var hit, 0.6f, ~((1 << 2) | (1 << Mathf.Max(0, LayerMask.NameToLayer(LayerName)))), QueryTriggerInteraction.Ignore))
        {
            float target = Mathf.Clamp(hit.point.y - transform.position.y, -0.25f, 0.05f);
            groundOffset = float.IsNaN(groundOffset) ? target : Mathf.Lerp(groundOffset, target, 1f - Mathf.Exp(-15f * Time.deltaTime));
        }
        if (!float.IsNaN(groundOffset))
            Current.transform.localPosition = new Vector3(0f, groundOffset, 0f);
    }

    /// <summary>Copia de cada malla del avatar que sólo proyecta sombra, en la capa normal.</summary>
    static void AddShadowCasters(GameObject avatar)
    {
        foreach (var smr in avatar.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            if (smr.gameObject.name == "BrazosPrimeraPersona") continue;
            var go = new GameObject(smr.name + "_Sombra");
            go.layer = 0;
            go.transform.SetParent(smr.transform.parent, false);
            go.transform.localPosition = smr.transform.localPosition;
            go.transform.localRotation = smr.transform.localRotation;
            go.transform.localScale = smr.transform.localScale;
            var copy = go.AddComponent<SkinnedMeshRenderer>();
            copy.sharedMesh = smr.sharedMesh;
            copy.bones = smr.bones;
            copy.rootBone = smr.rootBone;
            copy.sharedMaterials = smr.sharedMaterials;
            copy.localBounds = smr.localBounds;
            copy.updateWhenOffscreen = false;
            copy.shadowCastingMode = ShadowCastingMode.ShadowsOnly;
            copy.receiveShadows = false;
            copy.lightProbeUsage = LightProbeUsage.Off;
            copy.reflectionProbeUsage = ReflectionProbeUsage.Off;
        }
    }

    /// <summary>Saluda (lo usa el espejo con la tecla E).</summary>
    public void Wave()
    {
        if (Driver != null) Driver.Wave();
    }
}
