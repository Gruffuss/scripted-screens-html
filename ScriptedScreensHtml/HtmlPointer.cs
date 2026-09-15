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
    // OnPointerMove exists only under the new input system's module; the game runs the legacy
    // one, which sends enter/exit/down/up and nothing between. While the cursor is inside the
    // page its position is polled each frame instead, so :hover and mousemove follow it.
    private bool _inside;
    private Camera? _cam;
    private Vector2 _last = new(-1f, -1f);

    private void Update()
    {
        if (!_inside || Surface == null || transform is not RectTransform rt)
            return;
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(rt, Input.mousePosition, _cam, out var local))
            return;
        var r = rt.rect;
        if (r.width <= 0f || r.height <= 0f)
            return;
        var f = new Vector2((local.x - r.xMin) / r.width, (r.yMax - local.y) / r.height);
        if ((f - _last).sqrMagnitude < 1e-6f)
            return;
        _last = f;
        Surface.PointerMove(f);
    }

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
    public void OnPointerEnter(PointerEventData e) { _inside = true; _cam = e.enterEventCamera != null ? e.enterEventCamera : e.pressEventCamera; if (Surface != null && Local(e, out var f)) { _last = f; Surface.PointerMove(f); } }
    public void OnPointerExit(PointerEventData e) { _inside = false; _last = new Vector2(-1f, -1f); Surface?.PointerLeave(); }
    public void OnPointerDown(PointerEventData e) { if (Surface != null && Local(e, out var f)) Surface.PointerDown(f); }
    public void OnPointerUp(PointerEventData e) { Surface?.PointerUp(); }
}
