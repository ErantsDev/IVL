using System.Collections;
using UnityEngine;

/// <summary>Puerta con bisagra de la jaula del conejo.</summary>
public class CageDoor : MonoBehaviour
{
    [Tooltip("Pivote (bisagra) que gira al abrir.")]
    public Transform hinge;
    public Vector3 openEuler = new Vector3(0f, -105f, 0f);
    public float seconds = 0.45f;

    public bool IsOpen { get; private set; }
    Quaternion closedRot;
    Coroutine anim;

    void Awake()
    {
        if (hinge == null) hinge = transform;
        closedRot = hinge.localRotation;
    }

    public void SetOpen(bool open, bool instant = false)
    {
        IsOpen = open;
        if (anim != null) StopCoroutine(anim);
        var target = open ? closedRot * Quaternion.Euler(openEuler) : closedRot;
        if (instant || !isActiveAndEnabled) { hinge.localRotation = target; return; }
        anim = StartCoroutine(Animate(target));
    }

    public void Toggle() => SetOpen(!IsOpen);

    IEnumerator Animate(Quaternion target)
    {
        var from = hinge.localRotation;
        float t = 0f;
        while (t < seconds)
        {
            t += Time.deltaTime;
            hinge.localRotation = Quaternion.Slerp(from, target, Mathf.SmoothStep(0f, 1f, t / seconds));
            yield return null;
        }
        hinge.localRotation = target;
        anim = null;
    }
}
