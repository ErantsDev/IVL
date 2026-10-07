using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Menú: Tools > Inmunologia > ...
///   Configurar jugador            -> crea el Player (primera persona) con la Main Camera.
///   Hacer agarrables (selección)  -> agrega Rigidbody + Collider + Grabbable a lo seleccionado.
///   Crear objetos de prueba       -> mesa y objetos simples agarrables dentro del aula.
/// </summary>
public static class PlayerSetup
{
    [MenuItem("Tools/Inmunologia/Configurar jugador")]
    public static string SetupPlayer()
    {
        var player = GameObject.Find("Player");
        if (player == null)
        {
            player = new GameObject("Player");
            Undo.RegisterCreatedObjectUndo(player, "Crear Player");
        }
        player.transform.SetPositionAndRotation(new Vector3(0f, 0.05f, -1f), Quaternion.identity);

        var cc = player.GetComponent<CharacterController>();
        if (cc == null) cc = player.AddComponent<CharacterController>();
        cc.height = 1.8f;
        cc.radius = 0.3f;
        cc.center = new Vector3(0f, 0.9f, 0f);
        cc.stepOffset = 0.3f;

        var cam = Camera.main;
        if (cam == null)
        {
            var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
            cam = camGo.AddComponent<Camera>();
            camGo.AddComponent<AudioListener>();
        }
        cam.transform.SetParent(player.transform, false);
        cam.transform.localPosition = new Vector3(0f, 1.6f, 0f);
        cam.transform.localRotation = Quaternion.identity;
        cam.nearClipPlane = 0.05f;

        var pc = player.GetComponent<PlayerController>();
        if (pc == null) pc = player.AddComponent<PlayerController>();
        pc.cameraTransform = cam.transform;

        if (cam.GetComponent<ObjectGrabber>() == null)
            cam.gameObject.AddComponent<ObjectGrabber>();

        EditorSceneManager.MarkSceneDirty(player.scene);
        return "Player listo en " + player.transform.position;
    }

    [MenuItem("Tools/Inmunologia/Hacer agarrables (selección)")]
    public static void MakeSelectionGrabbable()
    {
        foreach (var go in Selection.gameObjects)
            MakeGrabbable(go, 1f);
    }

    public static void MakeGrabbable(GameObject go, float mass)
    {
        if (go.GetComponentInChildren<Collider>() == null)
        {
            var mf = go.GetComponentInChildren<MeshFilter>();
            if (mf != null && mf.sharedMesh != null)
            {
                var mc = mf.gameObject.AddComponent<MeshCollider>();
                mc.convex = true;
            }
            else
            {
                go.AddComponent<BoxCollider>();
            }
        }
        else
        {
            // Rigidbodies no cinemáticos requieren MeshColliders convexos.
            foreach (var mc in go.GetComponentsInChildren<MeshCollider>())
                mc.convex = true;
        }

        var rb = go.GetComponent<Rigidbody>();
        if (rb == null) rb = go.AddComponent<Rigidbody>();
        rb.mass = mass;
        rb.isKinematic = false;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

        if (go.GetComponent<Grabbable>() == null)
            go.AddComponent<Grabbable>();

        go.isStatic = false;
        EditorUtility.SetDirty(go);
        EditorSceneManager.MarkSceneDirty(go.scene);
    }

    [MenuItem("Tools/Inmunologia/Crear objetos de prueba")]
    public static string CreateTestObjects()
    {
        var root = GameObject.Find("ObjetosAgarrables") ?? new GameObject("ObjetosAgarrables");

        var mesa = GameObject.Find("Mesa");
        if (mesa == null)
        {
            mesa = GameObject.CreatePrimitive(PrimitiveType.Cube);
            mesa.name = "Mesa";
        }
        mesa.transform.position = new Vector3(0f, 0.4f, 1f);
        mesa.transform.localScale = new Vector3(1.6f, 0.8f, 0.8f);

        CreateItem(root, PrimitiveType.Cube, "Caja", new Vector3(-0.5f, 0.95f, 1f), Vector3.one * 0.2f, new Color(0.8f, 0.3f, 0.2f));
        CreateItem(root, PrimitiveType.Sphere, "Esfera", new Vector3(0f, 0.95f, 1f), Vector3.one * 0.2f, new Color(0.2f, 0.5f, 0.9f));
        CreateItem(root, PrimitiveType.Cylinder, "Tubo de ensayo", new Vector3(0.5f, 1.0f, 1f), new Vector3(0.08f, 0.12f, 0.08f), new Color(0.3f, 0.8f, 0.4f));

        EditorSceneManager.MarkSceneDirty(root.scene);
        return "Objetos de prueba creados";
    }

    static void CreateItem(GameObject parent, PrimitiveType type, string name, Vector3 pos, Vector3 scale, Color color)
    {
        var existing = parent.transform.Find(name);
        if (existing != null) return;

        var go = GameObject.CreatePrimitive(type);
        go.name = name;
        go.transform.SetParent(parent.transform, false);
        go.transform.position = pos;
        go.transform.localScale = scale;

        var shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader != null)
        {
            var mat = new Material(shader);
            mat.SetColor("_BaseColor", color);
            go.GetComponent<Renderer>().sharedMaterial = mat;
        }

        // Los primitivos traen su collider; se reemplaza el MeshCollider del cilindro por uno convexo.
        MakeGrabbable(go, 0.5f);
    }
}
