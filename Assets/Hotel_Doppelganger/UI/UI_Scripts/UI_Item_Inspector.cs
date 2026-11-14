using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using Image = UnityEngine.UI.Image;
using Text = UnityEngine.UI.Text;

// A close look at one item: drag to turn, scroll to zoom, right-drag to move, click outside to go back.
// Its meshes are copied far above the level and filmed by their own camera, so the real object is never touched
public sealed class ItemInspector
{
    private const int TextureSize = 1024;
    private const float ViewSize = 820f;
    private const float FieldOfView = 30f;
    private const float FitPadding = 1.15f;
    private const float TurnDegreesPerPixel = 0.35f;
    private const float ZoomStep = 0.12f;
    private const float ClosestZoom = 0.4f;
    private const float FurthestZoom = 3f;
    private const float FarClip = 100f;
    private const float LightStrength = 2f;

    private static readonly Vector3 StagePosition = new Vector3(0f, 10000f, 0f);
    private static readonly Quaternion StartAngle = Quaternion.Euler(15f, -30f, 0f);
    private static readonly Vector3 LightOffset = new Vector3(-0.6f, 0.8f, 0.2f);

    private readonly GameObject root;
    private readonly Image backdrop;
    private readonly RawImage view;
    private readonly Text title;
    private readonly Text hint;
    private readonly List<Shadow> shadows = new List<Shadow>();

    private GameObject stage;
    private Transform pivot;
    private Camera stageCamera;
    private RenderTexture texture;
    private float fitDistance = 1f;
    private float radius = 0.5f;
    private float zoom = 1f;
    private Vector2 pan;
    private Vector2 lastMouse;
    private bool turning;
    private bool moving;
    private Color viewColor = new Color(0.1f, 0.1f, 0.11f);

    public ItemInspector(Transform canvas, Font font)
    {
        RectTransform rect = RuntimeUI.CreateRect("Item Inspector", canvas);
        RuntimeUI.Stretch(rect);
        root = rect.gameObject;

        backdrop = RuntimeUI.CreateImage("Backdrop", rect, Color.black);
        RuntimeUI.Stretch(backdrop.rectTransform);

        view = RuntimeUI.CreateRect("View", rect).gameObject.AddComponent<RawImage>();
        view.raycastTarget = false;
        RectTransform viewRect = view.rectTransform;
        viewRect.anchorMin = viewRect.anchorMax = viewRect.pivot = new Vector2(0.5f, 0.5f);
        viewRect.sizeDelta = new Vector2(ViewSize, ViewSize);
        viewRect.anchoredPosition = Vector2.zero;

        title = CreateText("Title", rect, font, 40, new Vector2(0.5f, 1f), new Vector2(0f, -56f));
        hint = CreateText("Hint", rect, font, 24, new Vector2(0.5f, 0f), new Vector2(0f, 48f));

        root.SetActive(false);
    }

    public bool IsOpen { get; private set; }

    // Only items with something to draw can be inspected
    public static bool CanInspect(Grabbable item)
    {
        if (item == null) return false;

        foreach (MeshFilter part in item.GetComponentsInChildren<MeshFilter>(true))
        {
            if (part.sharedMesh != null && part.GetComponent<MeshRenderer>() != null) return true;
        }
        return false;
    }

    public void ApplyLook(Font font, Color textColor, Color labelColor, Color shadowColor, Color backdropColor, string highlightTag)
    {
        Font activeFont = font != null ? font : RuntimeUI.DefaultFont;
        title.font = activeFont;
        hint.font = activeFont;
        title.color = textColor;
        hint.color = labelColor;
        foreach (Shadow shadow in shadows) shadow.effectColor = shadowColor;

        backdrop.color = new Color(backdropColor.r, backdropColor.g, backdropColor.b, 0.97f);
        viewColor = backdropColor;
        if (stageCamera != null) stageCamera.backgroundColor = viewColor;

        hint.text = highlightTag + "[Drag]</color>  Turn        "
                    + highlightTag + "[Scroll]</color>  Zoom        "
                    + highlightTag + "[Right Drag]</color>  Move        "
                    + highlightTag + "[Click Outside]</color>  Back";
    }

    public void Open(Grabbable item)
    {
        Close();
        if (!CanInspect(item)) return;

        BuildStage(item);
        title.text = item.DisplayName;
        root.SetActive(true);
        IsOpen = true;

        Mouse mouse = Mouse.current;
        lastMouse = mouse != null ? mouse.position.ReadValue() : Vector2.zero;
        turning = false;
        moving = false;
    }

    public void Close()
    {
        if (stage != null) UnityEngine.Object.Destroy(stage);
        stage = null;
        pivot = null;
        stageCamera = null;

        if (texture != null)
        {
            view.texture = null;
            texture.Release();
            UnityEngine.Object.Destroy(texture);
            texture = null;
        }

        root.SetActive(false);
        IsOpen = false;
    }

    // Returns false once the player has clicked outside to go back
    public bool Update(Mouse mouse)
    {
        if (!IsOpen) return false;
        if (mouse == null) return true;

        Vector2 position = mouse.position.ReadValue();
        Vector2 delta = position - lastMouse;
        lastMouse = position;
        bool overView = RuntimeUI.Contains(view.rectTransform, position);

        if (mouse.leftButton.wasPressedThisFrame)
        {
            if (!overView)
            {
                Close();
                return false;
            }
            turning = true;
        }
        if (mouse.rightButton.wasPressedThisFrame && overView) moving = true;
        if (!mouse.leftButton.isPressed) turning = false;
        if (!mouse.rightButton.isPressed) moving = false;

        Transform cameraTransform = stageCamera.transform;
        if (turning && delta != Vector2.zero)
        {
            // Around the camera's axes, so it always spins the way the mouse moves
            pivot.rotation = Quaternion.AngleAxis(-delta.x * TurnDegreesPerPixel, cameraTransform.up)
                             * Quaternion.AngleAxis(delta.y * TurnDegreesPerPixel, cameraTransform.right)
                             * pivot.rotation;
        }

        float distance = fitDistance * zoom;
        if (moving && delta != Vector2.zero)
        {
            // Pixels to meters at the item, so it stays under the mouse
            float viewPixels = view.rectTransform.rect.height * view.canvas.scaleFactor;
            float metersPerPixel = 2f * distance * Mathf.Tan(FieldOfView * 0.5f * Mathf.Deg2Rad) / Mathf.Max(1f, viewPixels);
            pan = Vector2.ClampMagnitude(pan + delta * metersPerPixel, radius * 2f);
        }

        float wheel = mouse.scroll.ReadValue().y;
        if (wheel != 0f && overView) zoom = Mathf.Clamp(zoom * (1f - Mathf.Sign(wheel) * ZoomStep), ClosestZoom, FurthestZoom);

        cameraTransform.position = StagePosition + new Vector3(-pan.x, -pan.y, -fitDistance * zoom);
        return true;
    }

    // Copies the item's meshes onto a pivot, with a camera far enough back to fit them
    private void BuildStage(Grabbable item)
    {
        stage = new GameObject("Item Inspect Stage");
        stage.transform.position = StagePosition;

        pivot = new GameObject("Pivot").transform;
        pivot.SetParent(stage.transform, false);
        Transform model = new GameObject("Model").transform;
        model.SetParent(pivot, false);

        Transform source = item.transform;

        foreach (MeshFilter part in item.GetComponentsInChildren<MeshFilter>(true))
        {
            if (part.sharedMesh == null || !part.TryGetComponent(out MeshRenderer partRenderer)) continue;

            Transform copy = new GameObject(part.name).transform;
            copy.SetParent(model, false);
            copy.localPosition = source.InverseTransformPoint(part.transform.position);
            copy.localRotation = Quaternion.Inverse(source.rotation) * part.transform.rotation;
            copy.localScale = Divide(part.transform.lossyScale, source.lossyScale);
            copy.gameObject.AddComponent<MeshFilter>().sharedMesh = part.sharedMesh;
            copy.gameObject.AddComponent<MeshRenderer>().sharedMaterials = partRenderer.sharedMaterials;
        }

        model.localScale = source.lossyScale;
        RecenterModel(model);

        pivot.localRotation = StartAngle;
        zoom = 1f;
        pan = Vector2.zero;

        GameObject cameraObject = new GameObject("Item Inspect Camera");
        cameraObject.transform.SetParent(stage.transform, false);
        stageCamera = cameraObject.AddComponent<Camera>();
        stageCamera.clearFlags = CameraClearFlags.SolidColor;
        stageCamera.backgroundColor = viewColor;
        stageCamera.fieldOfView = FieldOfView;
        stageCamera.nearClipPlane = 0.01f;
        stageCamera.farClipPlane = FarClip;
        stageCamera.transform.position = StagePosition + Vector3.back * fitDistance;
        stageCamera.transform.rotation = Quaternion.identity;

        texture = new RenderTexture(TextureSize, TextureSize, 24);
        stageCamera.targetTexture = texture;
        view.texture = texture;

        // Rides with the camera, up and to the left, so the item is lit however it's turned
        var lightObject = new GameObject("Item Inspect Light");
        lightObject.transform.SetParent(cameraObject.transform, false);
        lightObject.transform.localPosition = LightOffset * fitDistance;
        Light keyLight = lightObject.AddComponent<Light>();
        keyLight.type = LightType.Point;
        keyLight.range = fitDistance * 6f;

        // Scaled by distance squared so big and small items are equally bright
        keyLight.intensity = Mathf.Max(1f, LightStrength * fitDistance * fitDistance);
    }

    private void RecenterModel(Transform model)
    {
        var bounds = new Bounds();
        bool first = true;
        foreach (Renderer part in model.GetComponentsInChildren<Renderer>())
        {
            if (first) bounds = part.bounds;
            else bounds.Encapsulate(part.bounds);
            first = false;
        }

        model.position += pivot.position - bounds.center;
        radius = Mathf.Max(0.01f, bounds.extents.magnitude);
        fitDistance = radius / Mathf.Sin(FieldOfView * 0.5f * Mathf.Deg2Rad) * FitPadding;
    }

    private static Vector3 Divide(Vector3 a, Vector3 b) => new Vector3(
        b.x != 0f ? a.x / b.x : a.x,
        b.y != 0f ? a.y / b.y : a.y,
        b.z != 0f ? a.z / b.z : a.z);

    private Text CreateText(string name, RectTransform parent, Font font, int size, Vector2 anchor, Vector2 position)
    {
        Text text = RuntimeUI.CreateText(name, parent, font, size, Color.white, TextAnchor.MiddleCenter, Color.black, out Shadow shadow);
        text.supportRichText = true;
        shadows.Add(shadow);

        RectTransform rect = text.rectTransform;
        rect.anchorMin = rect.anchorMax = rect.pivot = anchor;
        rect.anchoredPosition = position;
        rect.sizeDelta = new Vector2(1600f, 60f);
        return text;
    }
}