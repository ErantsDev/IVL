using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>Arrastrar sobre la vista previa gira al avatar.</summary>
public class PreviewDrag : MonoBehaviour, IDragHandler
{
    public StudentFormUI form;
    public void OnDrag(PointerEventData e) { if (form) form.Drag(e.delta.x); }
}
