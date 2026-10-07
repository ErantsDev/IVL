using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// Formulario "Datos del alumno" que aparece al presionar Jugar, antes de elegir el modo:
/// nombre, matrícula, correo institucional y sexo. Con el sexo se asigna el avatar, que se
/// muestra en una vista previa 3D (se puede girar arrastrando con el mouse).
/// Los datos se validan, se recuerdan para la próxima vez y se guardan en la base de datos.
/// </summary>
public class StudentFormUI : MonoBehaviour
{
    [Header("Campos")]
    public TMP_InputField nameInput;
    public TMP_InputField idInput;
    public TMP_InputField emailInput;
    public Button maleButton;
    public Button femaleButton;

    [Header("Botones y mensajes")]
    public Button continueButton;
    public Button backButton;
    public TMP_Text errorText;

    [Header("Vista previa del avatar")]
    public RawImage preview;
    public TMP_Text previewCaption;
    public GameObject malePrefab;
    public GameObject femalePrefab;
    [Tooltip("Dónde se arma el \"estudio\" de la vista previa (lejos del laboratorio).")]
    public Vector3 stagePosition = new Vector3(0f, -60f, 0f);

    [Header("Colores")]
    public Color selectedColor = new Color(0.16f, 0.47f, 0.86f, 1f);
    public Color idleColor = new Color(0.18f, 0.24f, 0.34f, 1f);
    public Color fieldNormal = new Color(0.1f, 0.16f, 0.27f, 1f);
    public Color fieldError = new Color(0.45f, 0.13f, 0.16f, 1f);

    /// <summary>Datos válidos confirmados: pasar a la elección de modo.</summary>
    public event Action Continued;
    /// <summary>"Volver" al menú principal.</summary>
    public event Action Cancelled;

    Sexo sexo = Sexo.SinElegir;
    bool wired;

    // Estudio de la vista previa
    Transform stage;
    Camera previewCam;
    RenderTexture previewRT;
    Transform maleInst, femaleInst;
    float yaw;
    float dragVelocity;

    void Awake() => Wire();

    void Wire()
    {
        if (wired) return;
        wired = true;
        if (maleButton) maleButton.onClick.AddListener(() => SelectSex(Sexo.Hombre));
        if (femaleButton) femaleButton.onClick.AddListener(() => SelectSex(Sexo.Mujer));
        if (continueButton) continueButton.onClick.AddListener(TryContinue);
        if (backButton) backButton.onClick.AddListener(() => Cancelled?.Invoke());
        foreach (var f in new[] { nameInput, idInput, emailInput })
            if (f) f.onValueChanged.AddListener(_ => { ClearError(f); });
        if (idInput) idInput.onEndEdit.AddListener(v => idInput.SetTextWithoutNotify(StudentProfile.NormalizeId(v)));
        if (emailInput) emailInput.onEndEdit.AddListener(v => emailInput.SetTextWithoutNotify(StudentProfile.NormalizeEmail(v)));
        if (preview)
        {
            var drag = preview.GetComponent<PreviewDrag>();
            if (drag == null) drag = preview.gameObject.AddComponent<PreviewDrag>();
            drag.form = this;
        }
    }

    // ------------------------------------------------------------------
    //  Abrir / cerrar
    // ------------------------------------------------------------------

    public void Open()
    {
        Wire();
        gameObject.SetActive(true);
        StudentProfile.EnsureLoaded();
        if (nameInput) nameInput.SetTextWithoutNotify(StudentProfile.Nombre);
        if (idInput) idInput.SetTextWithoutNotify(StudentProfile.Matricula);
        if (emailInput) emailInput.SetTextWithoutNotify(StudentProfile.Correo);
        foreach (var f in new[] { nameInput, idInput, emailInput }) ClearError(f);
        SetError(null);
        SelectSex(StudentProfile.Sexo);
        EnsureStage();
        if (stage) stage.gameObject.SetActive(true);
        yaw = 0f;

        // Foco en el primer campo vacío (o en ninguno si ya está todo lleno).
        var first = FirstEmpty();
        if (first != null) Focus(first);
        else if (EventSystem.current) EventSystem.current.SetSelectedGameObject(null);
    }

    public void Close()
    {
        if (stage) stage.gameObject.SetActive(false);
        gameObject.SetActive(false);
    }

    void OnDestroy()
    {
        if (previewRT) { previewRT.Release(); Destroy(previewRT); }
        if (stage) Destroy(stage.gameObject);
    }

    TMP_InputField FirstEmpty()
    {
        foreach (var f in new[] { nameInput, idInput, emailInput })
            if (f && string.IsNullOrWhiteSpace(f.text)) return f;
        return null;
    }

    static void Focus(TMP_InputField f)
    {
        if (f == null) return;
        if (EventSystem.current) EventSystem.current.SetSelectedGameObject(f.gameObject);
        f.ActivateInputField();
    }

    // ------------------------------------------------------------------
    //  Sexo y avatar
    // ------------------------------------------------------------------

    public void SelectSex(Sexo s)
    {
        sexo = s;
        Paint(maleButton, s == Sexo.Hombre);
        Paint(femaleButton, s == Sexo.Mujer);
        if (s != Sexo.SinElegir && errorText && errorText.text == StudentProfile.ValidateSex(Sexo.SinElegir)) SetError(null);
        UpdatePreviewLayout();
    }

    void Paint(Button b, bool selected)
    {
        if (!b) return;
        var cb = b.colors;
        var c = selected ? selectedColor : idleColor;
        cb.normalColor = c;
        cb.selectedColor = c;
        cb.highlightedColor = Color.Lerp(c, Color.white, 0.2f);
        b.colors = cb;
        var check = b.transform.Find("Marca");
        if (check) check.gameObject.SetActive(selected);
    }

    void UpdatePreviewLayout()
    {
        if (previewCaption)
            previewCaption.text = sexo == Sexo.Hombre ? "Tu avatar: químico"
                : sexo == Sexo.Mujer ? "Tu avatar: química"
                : "Elige tu sexo para asignarte un avatar";
        if (maleInst == null || femaleInst == null) return;

        // Sin elegir: los dos lado a lado. Elegido: sólo ese, al centro.
        maleInst.gameObject.SetActive(sexo != Sexo.Mujer);
        femaleInst.gameObject.SetActive(sexo != Sexo.Hombre);
        float sep = sexo == Sexo.SinElegir ? 0.48f : 0f;
        // La cámara mira hacia -Z: +X queda a la izquierda de la imagen (igual que el botón "Hombre").
        maleInst.localPosition = new Vector3(sep, 0f, 0f);
        femaleInst.localPosition = new Vector3(-sep, 0f, 0f);
        if (previewCam)
        {
            previewCam.transform.localPosition = sexo == Sexo.SinElegir ? new Vector3(0f, 1.0f, 4.9f) : new Vector3(0f, 1.0f, 4.15f);
            previewCam.transform.localRotation = Quaternion.LookRotation(new Vector3(0f, 0.9f, 0f) - previewCam.transform.localPosition);
        }
    }

    void EnsureStage()
    {
        if (stage != null || preview == null) return;
        stage = new GameObject("EstudioAvatar").transform;
        stage.position = stagePosition;

        // Cámara que dibuja la vista previa en una textura.
        previewRT = new RenderTexture(560, 760, 24, RenderTextureFormat.ARGB32) { name = "VistaAvatar", antiAliasing = 4 };
        var camGo = new GameObject("CamaraVistaAvatar");
        camGo.transform.SetParent(stage, false);
        previewCam = camGo.AddComponent<Camera>();
        previewCam.targetTexture = previewRT;
        previewCam.clearFlags = CameraClearFlags.SolidColor;
        previewCam.backgroundColor = new Color(0.07f, 0.12f, 0.22f, 1f);
        previewCam.fieldOfView = 30f;
        previewCam.nearClipPlane = 0.1f;
        previewCam.farClipPlane = 15f;
        previewCam.depth = -20;
        previewCam.useOcclusionCulling = false;
        preview.texture = previewRT;

        // Luces propias (no alcanzan al laboratorio, que está 60 m arriba).
        AddLight("LuzClave", new Vector3(1.4f, 2.4f, 2.4f), 2.2f, new Color(1f, 0.96f, 0.9f));
        AddLight("LuzRelleno", new Vector3(-1.8f, 1.4f, 1.8f), 0.9f, new Color(0.75f, 0.85f, 1f));
        AddLight("LuzContra", new Vector3(0f, 2.6f, -1.6f), 2.5f, new Color(0.6f, 0.8f, 1f));

        // Podio.
        var podium = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        podium.name = "Podio";
        Destroy(podium.GetComponent<Collider>());
        podium.transform.SetParent(stage, false);
        podium.transform.localPosition = new Vector3(0f, -0.025f, 0f);
        podium.transform.localScale = new Vector3(1.9f, 0.025f, 1.2f);
        var pr = podium.GetComponent<Renderer>();
        var sh = Shader.Find("Universal Render Pipeline/Lit");
        if (sh != null)
        {
            var m = new Material(sh);
            m.SetColor("_BaseColor", new Color(0.16f, 0.24f, 0.38f));
            m.SetFloat("_Smoothness", 0.6f);
            pr.sharedMaterial = m;
        }

        maleInst = SpawnPreviewAvatar(malePrefab, "Hombre");
        femaleInst = SpawnPreviewAvatar(femalePrefab, "Mujer");
        UpdatePreviewLayout();
    }

    void AddLight(string name, Vector3 pos, float intensity, Color c)
    {
        var go = new GameObject(name);
        go.transform.SetParent(stage, false);
        go.transform.localPosition = pos;
        var l = go.AddComponent<Light>();
        l.type = LightType.Point;
        l.range = 7f;
        l.intensity = intensity;
        l.color = c;
        l.shadows = LightShadows.None;
    }

    Transform SpawnPreviewAvatar(GameObject prefab, string label)
    {
        if (prefab == null) return null;
        var go = Instantiate(prefab, stage);
        go.name = "Vista_" + label;
        go.transform.localPosition = Vector3.zero;
        go.transform.localRotation = Quaternion.identity;
        return go.transform;
    }

    void Update()
    {
        // Giro lento del avatar (o con inercia después de arrastrarlo).
        if (Mathf.Abs(dragVelocity) > 0.5f) { yaw += dragVelocity * Time.unscaledDeltaTime; dragVelocity = Mathf.Lerp(dragVelocity, 0f, 1f - Mathf.Exp(-3f * Time.unscaledDeltaTime)); }
        else if (sexo != Sexo.SinElegir) yaw += 22f * Time.unscaledDeltaTime;
        else yaw = Mathf.LerpAngle(yaw, 0f, 1f - Mathf.Exp(-4f * Time.unscaledDeltaTime));
        var rot = Quaternion.Euler(0f, yaw, 0f);
        if (maleInst) maleInst.localRotation = rot;
        if (femaleInst) femaleInst.localRotation = rot;

        HandleKeys();
    }

    internal void Drag(float dx)
    {
        yaw -= dx * 0.4f;
        dragVelocity = -dx * 18f;
    }

    // ------------------------------------------------------------------
    //  Teclado: Tab para cambiar de campo, Enter para continuar
    // ------------------------------------------------------------------

    void HandleKeys()
    {
#if ENABLE_INPUT_SYSTEM
        var k = Keyboard.current;
        if (k == null) return;
        bool tab = k.tabKey.wasPressedThisFrame;
        bool back = k.leftShiftKey.isPressed || k.rightShiftKey.isPressed;
        bool enter = k.enterKey.wasPressedThisFrame || k.numpadEnterKey.wasPressedThisFrame;
#else
        bool tab = Input.GetKeyDown(KeyCode.Tab);
        bool back = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
        bool enter = Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter);
#endif
        if (tab)
        {
            var fields = new[] { nameInput, idInput, emailInput };
            var current = EventSystem.current ? EventSystem.current.currentSelectedGameObject : null;
            int i = Array.FindIndex(fields, f => f && f.gameObject == current);
            int next = i < 0 ? 0 : (i + (back ? fields.Length - 1 : 1)) % fields.Length;
            Focus(fields[next]);
        }
        else if (enter)
        {
            TryContinue();
        }
    }

    // ------------------------------------------------------------------
    //  Validación
    // ------------------------------------------------------------------

    public void TryContinue()
    {
        string n = nameInput ? nameInput.text : "";
        string id = idInput ? idInput.text : "";
        string em = emailInput ? emailInput.text : "";

        string err;
        if ((err = StudentProfile.ValidateName(n)) != null) { Fail(nameInput, err); return; }
        if ((err = StudentProfile.ValidateId(id)) != null) { Fail(idInput, err); return; }
        if ((err = StudentProfile.ValidateEmail(em)) != null) { Fail(emailInput, err); return; }
        if ((err = StudentProfile.ValidateSex(sexo)) != null) { Fail(null, err); return; }

        StudentProfile.Set(n, id, em, sexo);
        SetError(null);
        Continued?.Invoke();
    }

    void Fail(TMP_InputField field, string message)
    {
        SetError(message);
        if (field != null)
        {
            var img = field.GetComponent<Image>();
            if (img) img.color = fieldError;
            Focus(field);
        }
    }

    void ClearError(TMP_InputField field)
    {
        if (field == null) return;
        var img = field.GetComponent<Image>();
        if (img) img.color = fieldNormal;
    }

    void SetError(string message)
    {
        if (!errorText) return;
        errorText.text = message ?? "";
        errorText.gameObject.SetActive(!string.IsNullOrEmpty(message));
    }

    /// <summary>Para pruebas automáticas: llena el formulario y lo confirma.</summary>
    public void DebugFill(string nombre, string matricula, string correo, Sexo s)
    {
        if (nameInput) nameInput.text = nombre;
        if (idInput) idInput.text = matricula;
        if (emailInput) emailInput.text = correo;
        SelectSex(s);
        TryContinue();
    }

    public string CurrentError => errorText && errorText.gameObject.activeSelf ? errorText.text : null;
}
