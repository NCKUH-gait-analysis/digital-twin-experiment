using UnityEngine;

public class D455CameraConfigurator : MonoBehaviour
{
    public enum D455StreamType
    {
        Depth,
        RGB
    }

    [Header("Cameras")]
    public Camera[] cameras;

    [Header("D455 Preset")]
    public D455StreamType streamType = D455StreamType.Depth;
    public bool applyOnStart = true;
    public bool applyInEditor = true;

    [Header("Display")]
    public int targetDisplay = 0;
    public bool setViewportRect = true;
    public Rect viewportRect = new Rect(0f, 0f, 1f, 1f);

    [Header("Clipping")]
    public float nearClip = 0.6f;
    public float farClip = 6f;

    private const float DepthVerticalFov = 57f;
    private const int DepthWidth = 1280;
    private const int DepthHeight = 720;

    private const float RgbVerticalFov = 65f;
    private const int RgbWidth = 1280;
    private const int RgbHeight = 720;

    private void Start()
    {
        if (applyOnStart)
        {
            ApplySettings();
        }
    }

    private void OnValidate()
    {
        nearClip = Mathf.Max(0.01f, nearClip);
        farClip = Mathf.Max(nearClip + 0.01f, farClip);

        if (applyInEditor)
        {
            ApplySettings();
        }
    }

    [ContextMenu("Apply D455 Settings")]
    public void ApplySettings()
    {
        int width = GetCaptureWidth();
        int height = GetCaptureHeight();
        float aspect = (float)width / height;
        float verticalFov = GetVerticalFov();

        if (cameras == null)
        {
            return;
        }

        foreach (Camera cam in cameras)
        {
            if (cam == null)
            {
                continue;
            }

            cam.orthographic = false;
            cam.fieldOfView = verticalFov;
            cam.aspect = aspect;
            cam.nearClipPlane = nearClip;
            cam.farClipPlane = farClip;
            cam.targetDisplay = targetDisplay;

            if (setViewportRect)
            {
                cam.rect = viewportRect;
            }

            ApplyCaptureSize(cam, width, height);
        }

        Debug.Log($"Applied D455 {streamType} settings: {width}x{height}, vertical FOV {verticalFov}, aspect {aspect:0.###}");
    }

    private float GetVerticalFov()
    {
        return streamType == D455StreamType.Depth ? DepthVerticalFov : RgbVerticalFov;
    }

    private int GetCaptureWidth()
    {
        return streamType == D455StreamType.Depth ? DepthWidth : RgbWidth;
    }

    private int GetCaptureHeight()
    {
        return streamType == D455StreamType.Depth ? DepthHeight : RgbHeight;
    }

    private static void ApplyCaptureSize(Camera cam, int width, int height)
    {
        CameraCapture cameraCapture = cam.GetComponent<CameraCapture>();
        if (cameraCapture != null)
        {
            cameraCapture.captureWidth = width;
            cameraCapture.captureHeight = height;
        }

        MultiCameraCapture multiCameraCapture = cam.GetComponent<MultiCameraCapture>();
        if (multiCameraCapture != null)
        {
            multiCameraCapture.captureWidth = width;
            multiCameraCapture.captureHeight = height;
        }

        foreach (CameraCapture capture in FindObjectsOfType<CameraCapture>())
        {
            if (capture.camera == cam)
            {
                capture.captureWidth = width;
                capture.captureHeight = height;
            }
        }

        foreach (MultiCameraCapture capture in FindObjectsOfType<MultiCameraCapture>())
        {
            if (capture.cameras == null)
            {
                continue;
            }

            foreach (Camera captureCamera in capture.cameras)
            {
                if (captureCamera == cam)
                {
                    capture.captureWidth = width;
                    capture.captureHeight = height;
                    break;
                }
            }
        }
    }
}
