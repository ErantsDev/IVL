using UnityEngine;

/// <summary>
/// Menú principal en VR como un lobby: la cámara del menú sólo dibuja la interfaz, los controles
/// y un piso sencillo sobre un fondo liso (sin el laboratorio desenfocado de fondo, que en las
/// gafas hacía ver borroso todo el menú). Sólo en VR; la cámara del menú de PC no se toca.
/// </summary>
public static class VRLobby
{
    /// <summary>Capa propia del lobby (libre en el proyecto).</summary>
    public const int Layer = 31;
    public static readonly Color Background = new Color(0.035f, 0.05f, 0.085f, 1f);
    const float EyeHeight = 1.6f;

    public static void Setup(GameFlow flow)
    {
        if (flow == null || flow.menuCamera == null) return;

        // Sin desenfoque de fondo: en VR también desenfocaba el panel del menú.
        if (flow.menuVolume != null) { flow.menuVolume.SetActive(false); flow.menuVolume = null; }

        var cam = flow.menuCamera;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = Background;
        cam.cullingMask = (1 << VRHud.UILayer) | (1 << Layer);

        // Controles y láser del soporte del menú, en la capa del lobby para que se vean.
        Transform rig = cam.transform.parent != null ? cam.transform.parent : cam.transform;
        foreach (var t in rig.GetComponentsInChildren<Transform>(true))
            if (t != cam.transform) t.gameObject.layer = Layer;
        foreach (var laser in rig.GetComponentsInChildren<VRLaser>(true)) laser.SetLayer(Layer);

        if (GameObject.Find("VRLobby") != null) return;
        var root = new GameObject("VRLobby").transform;
        root.position = rig.position + Vector3.down * EyeHeight;
        root.rotation = Quaternion.Euler(0f, rig.eulerAngles.y, 0f);
        root.gameObject.layer = Layer;

        var shader = Shader.Find("Sprites/Default");
        // Anillo claro alrededor y piso oscuro encima: da referencia de dónde está el suelo.
        Disc(root, "Anillo", shader, 3.2f, new Color(0.16f, 0.47f, 0.86f, 0.55f), 2998, -0.004f);
        Disc(root, "Piso", shader, 3.0f, new Color(0.07f, 0.1f, 0.16f, 1f), 2999, 0f);
        Disc(root, "Centro", shader, 0.9f, new Color(0.12f, 0.18f, 0.28f, 1f), 3000, 0.002f);
    }

    static void Disc(Transform parent, string name, Shader shader, float diameter, Color color, int queue, float y)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        Object.Destroy(go.GetComponent<Collider>());
        go.name = name;
        go.layer = Layer;
        go.transform.SetParent(parent, false);
        go.transform.localPosition = new Vector3(0f, y, 0f);
        go.transform.localScale = new Vector3(diameter, 0.001f, diameter);
        var r = go.GetComponent<Renderer>();
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;
        var m = new Material(shader) { color = color, renderQueue = queue };
        r.sharedMaterial = m;
    }
}
