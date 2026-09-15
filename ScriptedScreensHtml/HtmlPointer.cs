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
    // The game's input module delivers presses and releases to a world console and nothing
    // else: no moves, no enter, no exit. So the cursor is polled against the page rect every
    // frame, through the canvas camera, and that is what :hover and mousemove follow. With the
    // cursor locked to the crosshair this is the crosshair, which is what a player expects.
    private bool _inside;
    private Camera? _cam;
    private float _camCheckedAt = -10f;
    private Vector2 _last = new(-1f, -1f);

    private Camera? CameraFor()
    {
        if (_cam == null || Time.unscaledTime - _camCheckedAt > 2f)
        {
            _camCheckedAt = Time.unscaledTime;
            var canvas = GetComponentInParent<Canvas>();
            _cam = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? (canvas.worldCamera != null ? canvas.worldCamera : Camera.main) : null;
        }
        return _cam;
    }

    private void Update()
    {
        if (Surface == null || transform is not RectTransform rt)
            return;
        var cam = CameraFor();
        var r = rt.rect;
        var local = Vector2.zero;
        var inside = r.width > 0f && r.height > 0f
                     && RectTransformUtility.ScreenPointToLocalPointInRectangle(rt, Input.mousePosition, cam, out local)
                     && r.Contains(local);
        if (!inside)
        {
            if (_inside) { _inside = false; _last = new Vector2(-1f, -1f); Surface.PointerLeave(); }
            return;
        }
        _inside = true;
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
    public void OnPointerEnter(PointerEventData e) { if (Surface != null && Local(e, out var f)) { _last = f; Surface.PointerMove(f); } }
    public void OnPointerExit(PointerEventData e) { }
    public void OnPointerDown(PointerEventData e) { if (Surface != null && Local(e, out var f)) Surface.PointerDown(f); }
    public void OnPointerUp(PointerEventData e) { Surface?.PointerUp(); }
}
