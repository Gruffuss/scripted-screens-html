using UnityEngine;
using UnityEngine.EventSystems;

namespace ScriptedScreensHtml;

/// <summary>
/// Lives on the page's host object. UGUI hands pointer events to the first handler up the
/// hierarchy from the object under the pointer (the vector surface child, which handles
/// clicks and drags only), so moves, enters, exits, downs and ups arrive here. They are
/// converted to a fraction of the host rect and handed to the surface, which knows the
/// page's boxes: that is what :hover, :active, mouse events and click coordinates are made of.
/// </summary>
internal sealed class HtmlPointer : MonoBehaviour, IPointerMoveHandler, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler
{
    internal HtmlSurface? Surface;

    private bool Local(PointerEventData e, out Vector2 fraction)
    {
        fraction = default;
        if (transform is not RectTransform rt)
            return false;
        var cam = e.enterEventCamera != null ? e.enterEventCamera : e.pressEventCamera;
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(rt, e.position, cam, out var local))
            return false;
        var r = rt.rect;
        if (r.width <= 0f || r.height <= 0f)
            return false;
        fraction = new Vector2((local.x - r.xMin) / r.width, (r.yMax - local.y) / r.height);
        return true;
    }

    public void OnPointerMove(PointerEventData e) { if (Surface != null && Local(e, out var f)) Surface.PointerMove(f); }
    public void OnPointerEnter(PointerEventData e) { if (Surface != null && Local(e, out var f)) Surface.PointerMove(f); }
    public void OnPointerExit(PointerEventData e) { Surface?.PointerLeave(); }
    public void OnPointerDown(PointerEventData e) { if (Surface != null && Local(e, out var f)) Surface.PointerDown(f); }
    public void OnPointerUp(PointerEventData e) { Surface?.PointerUp(); }
}
