using System;
using TMPro;
using UnityEngine;

/// <summary>Lo implementa el controlador de cada nivel para que GameFlow pueda arrancarlo.</summary>
public interface ILevelController
{
    void BeginLevel(GameMode mode);
    bool IsRunning { get; }
}

/// <summary>
/// Prepara la escena del laboratorio para el nivel elegido (GameSession.Level) antes que cualquier
/// otro script: enciende/apaga los objetos de cada nivel, "congela" como decoración el material del
/// nivel 1 cuando se juega el nivel 2, mueve la báscula a la mesa y cambia lo escrito en el pizarrón.
/// </summary>
[DefaultExecutionOrder(-1000)]
public class LevelDirector : MonoBehaviour
{
    [Serializable]
    public class LevelSetup
    {
        public string title;
        public MonoBehaviour controller;
        [Tooltip("Objetos que sólo existen en este nivel.")]
        public GameObject[] objects;
        [TextArea(2, 6)] public string boardTitle;
        [TextArea(3, 12)] public string boardSteps;
        [TextArea(1, 4)] public string boardNote;
    }

    public static LevelDirector Instance { get; private set; }

    public LevelSetup[] levels = new LevelSetup[0];

    [Header("Material del nivel 1 que en otros niveles queda sólo como decoración")]
    public Transform[] level1Props;

    [Header("Báscula (en el nivel 2 se usa sobre la mesa)")]
    public Transform scale;
    public Vector3 scaleLevel1Position;
    public Vector3 scaleLevel1Euler;
    public Vector3 scaleLevel2Position;
    public Vector3 scaleLevel2Euler;

    [Header("Pizarrón")]
    public TMP_Text boardTitle;
    public TMP_Text boardSteps;
    public TMP_Text boardNote;

    public int AppliedLevel { get; private set; }

    public ILevelController Current
    {
        get
        {
            int i = AppliedLevel - 1;
            if (i < 0 || i >= levels.Length) return null;
            return levels[i].controller as ILevelController;
        }
    }

    public string CurrentTitle
    {
        get
        {
            int i = AppliedLevel - 1;
            return i >= 0 && i < levels.Length ? levels[i].title : "";
        }
    }

    void Awake()
    {
        Instance = this;
        Apply(Mathf.Clamp(GameSession.Level, 1, Mathf.Max(1, levels.Length)));
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    void Apply(int level)
    {
        AppliedLevel = level;

        // Primero se apaga lo que no corresponde (así sus Awake nunca corren) y luego se enciende lo del nivel.
        for (int i = 0; i < levels.Length; i++)
        {
            if (i == level - 1) continue;
            SetObjects(levels[i], false);
        }
        if (level - 1 < levels.Length) SetObjects(levels[level - 1], true);

        if (level != 1 && level1Props != null)
        {
            foreach (var root in level1Props)
            {
                if (root == null) continue;
                foreach (var g in root.GetComponentsInChildren<Grabbable>(true)) g.enabled = false;
                foreach (var rb in root.GetComponentsInChildren<Rigidbody>(true)) { rb.isKinematic = true; rb.useGravity = false; }
                foreach (var lm in root.GetComponentsInChildren<LabMaterial>(true)) lm.enabled = false;
            }
        }

        if (scale != null)
        {
            if (level == 2) scale.SetPositionAndRotation(scaleLevel2Position, Quaternion.Euler(scaleLevel2Euler));
            else scale.SetPositionAndRotation(scaleLevel1Position, Quaternion.Euler(scaleLevel1Euler));
        }

        if (level - 1 < levels.Length)
        {
            var s = levels[level - 1];
            if (boardTitle != null && !string.IsNullOrEmpty(s.boardTitle)) boardTitle.text = s.boardTitle;
            if (boardSteps != null && !string.IsNullOrEmpty(s.boardSteps)) boardSteps.text = s.boardSteps;
            if (boardNote != null && s.boardNote != null) boardNote.text = s.boardNote;
        }
    }

    static void SetObjects(LevelSetup s, bool on)
    {
        if (s == null || s.objects == null) return;
        foreach (var go in s.objects)
            if (go != null && go.activeSelf != on) go.SetActive(on);
    }
}
