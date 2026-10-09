using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Prepara el juego para las gafas. Sólo hace algo en Android con un visor activo
/// (<see cref="VRMode.Active"/>); en PC no se crea.
///
/// En cada escena cargada:
///  - Cámara del menú y cámara del jugador: se les pone un padre "..._VROffset" en la posición
///    que tenían y la cámara recibe <see cref="VRHeadPose"/> (sigue la cabeza).
///  - Se apaga el recorrido automático de la cámara del menú (mover la vista sin que el usuario
///    se mueva marea en VR).
///  - Jugador: giro por pasos (VRSnapTurn). Escena: paneles flotantes y mira (VRHud).
///  - PROVISIONAL: como los menús todavía no se pueden usar en VR (falta el láser), se entra directo
///    al nivel en el modo de <see cref="GameSession.Mode"/> (educativo por defecto).
/// </summary>
[DefaultExecutionOrder(-1000)] // lee los controles antes que cualquier otro script
public class VRBootstrap : MonoBehaviour
{
    bool vrReady;

    [Tooltip("Entrar directo al nivel al estar en el menú (mientras no haya menús en VR).")]
    public bool autoStart = true;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Init()
    {
        if (Application.platform != RuntimePlatform.Android) return;
        var go = new GameObject("VRBootstrap");
        DontDestroyOnLoad(go);
        go.AddComponent<VRBootstrap>();
    }

    IEnumerator Start()
    {
        // El sistema XR puede tardar unas tramas en reportar el visor activo.
        float waited = 0f;
        while (!VRMode.Active && waited < 3f)
        {
            waited += Time.unscaledDeltaTime;
            yield return null;
        }
        if (!VRMode.Active) { Destroy(gameObject); yield break; }

        Debug.Log("[VR] Gafas detectadas: activando modo VR.");
        vrReady = true;
        SceneManager.sceneLoaded += OnSceneLoaded;
        yield return SetupScene();
    }

    void Update()
    {
        if (vrReady) VRInput.Sample();
    }

    void OnDestroy() => SceneManager.sceneLoaded -= OnSceneLoaded;

    void OnSceneLoaded(Scene scene, LoadSceneMode mode) => StartCoroutine(SetupScene());

    IEnumerator SetupScene()
    {
        yield return null; // deja que GameFlow.Start decida menú o nivel

        var flow = GameFlow.Instance;
        if (flow != null && flow.menuCamera != null)
        {
            var orbit = flow.menuCamera.GetComponent<MenuCameraOrbit>();
            if (orbit != null) orbit.enabled = false;
            AttachHead(flow.menuCamera.transform, false);
        }

        foreach (var pc in FindObjectsByType<PlayerController>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (pc.cameraTransform != null) AttachHead(pc.cameraTransform, true);
            if (pc.GetComponent<VRSnapTurn>() == null) pc.gameObject.AddComponent<VRSnapTurn>();
        }

        // Paneles flotantes, mira y avisos (un objeto por escena: los Canvas son de la escena).
        if (FindAnyObjectByType<VRHud>() == null) new GameObject("VRHud").AddComponent<VRHud>();

        if (autoStart && flow != null && flow.CurrentState == GameFlow.State.Menu)
            flow.StartGame(GameSession.Mode);
    }

    /// <summary>Mete la cámara dentro de un padre en su posición actual (sólo giro horizontal) y le agrega VRHeadPose.</summary>
    static void AttachHead(Transform cam, bool withControllers)
    {
        if (cam.GetComponent<VRHeadPose>() != null) return;

        var offset = new GameObject(cam.name + "_VROffset").transform;
        offset.SetParent(cam.parent, false);
        offset.SetSiblingIndex(cam.GetSiblingIndex());
        offset.position = cam.position;
        // Sin inclinación: el horizonte de las gafas siempre debe quedar nivelado.
        offset.rotation = Quaternion.Euler(0f, cam.eulerAngles.y, 0f);

        cam.SetParent(offset, false);
        cam.localPosition = Vector3.zero;
        cam.localRotation = Quaternion.identity;
        var head = cam.gameObject.AddComponent<VRHeadPose>();

        // Los controles, en el mismo espacio que la cabeza: se ven y se mueven con las manos.
        // Sólo en el jugador: la cámara del menú se apaga al jugar, pero su soporte no, y sus
        // controles quedaban flotando donde estaba esa cámara (el "duplicado").
        if (withControllers)
        {
            VRControllerPose.Create(offset, UnityEngine.XR.XRNode.LeftHand, head);
            VRControllerPose.Create(offset, UnityEngine.XR.XRNode.RightHand, head);
        }

        var c = cam.GetComponent<Camera>();
        if (c != null) c.cullingMask |= 1 << 5; // capa UI: los paneles flotantes

    }
}
