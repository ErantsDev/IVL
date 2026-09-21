using System.Collections;
using UnityEngine;

/// <summary>
/// Apertura y cierre de la tapa del autoclave (low poly).
///
/// Jerarquía esperada al importar "autoclave_lowpoly.fbx":
///   Autoclave_Body        (gabinete + cámara hueca + panel + postes + ruedas)
///     ├─ Autoclave_Lid      (tapa + arco + tornillo; pivote en la base del poste izquierdo)
///     │    └─ Autoclave_Wheel  (volante negro; gira sobre su propio eje)
///     └─ LoadPoint          (marcador en el piso de la cámara, donde va el material)
///
/// El FBX ya trae la animación horneada (110 frames a 24 fps): el volante da dos
/// vueltas para destrabar, la tapa sube 13 cm y luego gira 105° sobre el poste
/// izquierdo, dejando libre la boca de la cámara. Si prefieres usar esa clip,
/// mete el modelo en un Animator y reprodúcela; para cerrar, con speed = -1.
///
/// Este script es la alternativa por código: hace el mismo movimiento pero
/// controlable desde el juego (al interactuar con el equipo), y además permite
/// meter y sacar objetos de la cámara.
///
/// NOTA sobre el sentido del giro: Blender y Unity usan sistemas de coordenadas
/// de distinta mano, así que si al probar la tapa gira hacia el lado contrario,
/// invierte el signo de "swingAngle" en el inspector. Es lo único que puede
/// cambiar de sentido; la subida y el giro del volante se ven igual.
/// </summary>
[DisallowMultipleComponent]
public class AutoclaveLid : MonoBehaviour
{
    [Header("Referencias (se buscan solas si se dejan vacías)")]
    public Transform lid;
    public Transform wheel;
    public Transform loadPoint;

    [Header("Movimiento (mismos valores que la animación del FBX)")]
    public float liftHeight = 0.13f;      // metros que sube la tapa
    public float swingAngle = -105f;      // grados que gira sobre el poste izquierdo
    public float wheelTurns = -720f;      // grados que gira el volante al destrabar

    [Header("Tiempos, en segundos")]
    public float unscrewDuration = 1.0f;
    public float liftDuration = 1.1f;
    public float swingDuration = 1.7f;

    public bool IsOpen { get; private set; }
    public bool IsMoving { get; private set; }

    private Vector3 _lidClosedPos;
    private Quaternion _lidClosedRot;
    private Quaternion _wheelClosedRot;

    void Awake()
    {
        if (lid == null) lid = FindChildByName("Autoclave_Lid");
        if (wheel == null) wheel = FindChildByName("Autoclave_Wheel");
        if (loadPoint == null) loadPoint = FindChildByName("LoadPoint");

        if (lid != null)
        {
            _lidClosedPos = lid.localPosition;
            _lidClosedRot = lid.localRotation;
        }
        if (wheel != null) _wheelClosedRot = wheel.localRotation;
    }

    private Transform FindChildByName(string n)
    {
        foreach (Transform t in GetComponentsInChildren<Transform>(true))
            if (t.name == n) return t;
        return null;
    }

    /// <summary>Abre la tapa: destraba el volante, la sube y la gira a un lado.</summary>
    public void Open()
    {
        if (!IsMoving && !IsOpen) StartCoroutine(OpenRoutine());
    }

    /// <summary>Cierra la tapa haciendo el recorrido inverso.</summary>
    public void Close()
    {
        if (!IsMoving && IsOpen) StartCoroutine(CloseRoutine());
    }

    /// <summary>Alterna abrir/cerrar (para engancharlo a un botón o a un raycast de interacción).</summary>
    public void Toggle()
    {
        if (IsOpen) Close(); else Open();
    }

    /// <summary>
    /// Mete un objeto en la cámara: lo coloca en el LoadPoint y lo deja como hijo
    /// del autoclave para que se mueva con él. Sólo funciona con la tapa abierta.
    /// </summary>
    public bool LoadItem(Transform item)
    {
        if (item == null || loadPoint == null || !IsOpen) return false;
        item.SetParent(loadPoint, true);
        item.localPosition = Vector3.zero;
        item.localRotation = Quaternion.identity;
        return true;
    }

    /// <summary>Saca un objeto de la cámara y lo suelta en la escena.</summary>
    public bool UnloadItem(Transform item)
    {
        if (item == null || !IsOpen) return false;
        item.SetParent(null, true);
        return true;
    }

    private IEnumerator OpenRoutine()
    {
        IsMoving = true;
        yield return SpinY(wheel, _wheelClosedRot, 0f, wheelTurns, unscrewDuration);
        yield return MoveTo(lid, _lidClosedPos, _lidClosedPos + Vector3.up * liftHeight, liftDuration);
        yield return SpinY(lid, _lidClosedRot, 0f, swingAngle, swingDuration);
        IsOpen = true;
        IsMoving = false;
    }

    private IEnumerator CloseRoutine()
    {
        IsMoving = true;
        yield return SpinY(lid, _lidClosedRot, swingAngle, 0f, swingDuration);
        yield return MoveTo(lid, _lidClosedPos + Vector3.up * liftHeight, _lidClosedPos, liftDuration);
        yield return SpinY(wheel, _wheelClosedRot, wheelTurns, 0f, unscrewDuration);
        IsOpen = false;
        IsMoving = false;
    }

    private IEnumerator MoveTo(Transform t, Vector3 from, Vector3 to, float duration)
    {
        if (t == null) yield break;
        for (float e = 0f; e < duration; e += Time.deltaTime)
        {
            t.localPosition = Vector3.Lerp(from, to, Mathf.SmoothStep(0f, 1f, e / duration));
            yield return null;
        }
        t.localPosition = to;
    }

    /// <summary>
    /// Gira sobre el eje Y local interpolando el ÁNGULO, no el quaternion: así las
    /// vueltas completas del volante (720°) sí se ven girar. Interpolando
    /// quaternions no se movería nada, porque 720° y 0° son la misma orientación.
    /// </summary>
    private IEnumerator SpinY(Transform t, Quaternion baseRot, float fromAngle, float toAngle, float duration)
    {
        if (t == null) yield break;
        for (float e = 0f; e < duration; e += Time.deltaTime)
        {
            float a = Mathf.Lerp(fromAngle, toAngle, Mathf.SmoothStep(0f, 1f, e / duration));
            t.localRotation = baseRot * Quaternion.Euler(0f, a, 0f);
            yield return null;
        }
        t.localRotation = baseRot * Quaternion.Euler(0f, toAngle, 0f);
    }
}
