using UnityEngine;

public class CameraIntrinsicsLogger : MonoBehaviour
{
    [Header("Cameras")]
    public Camera[] cameras;

    [Header("Image Size")]
    public int imageWidth = 1280;
    public int imageHeight = 720;

    [Header("Options")]
    public bool logOnStart = true;

    private void Start()
    {
        if (logOnStart)
        {
            LogIntrinsics();
        }
    }

    [ContextMenu("Log Intrinsics")]
    public void LogIntrinsics()
    {
        if (cameras == null || cameras.Length == 0)
        {
            Debug.LogWarning("No cameras assigned.");
            return;
        }

        foreach (Camera cam in cameras)
        {
            if (cam == null)
            {
                continue;
            }

            CameraIntrinsics intrinsics = CalculateIntrinsics(cam, imageWidth, imageHeight);
            Debug.Log(
                $"{cam.name} intrinsics ({imageWidth}x{imageHeight})\n" +
                $"fx: {intrinsics.fx:F4}\n" +
                $"fy: {intrinsics.fy:F4}\n" +
                $"cx: {intrinsics.cx:F4}\n" +
                $"cy: {intrinsics.cy:F4}\n" +
                $"K:\n" +
                $"[{intrinsics.fx:F4}, 0, {intrinsics.cx:F4}]\n" +
                $"[0, {intrinsics.fy:F4}, {intrinsics.cy:F4}]\n" +
                $"[0, 0, 1]");
        }
    }

    private static CameraIntrinsics CalculateIntrinsics(Camera cam, int width, int height)
    {
        Matrix4x4 projection = cam.projectionMatrix;

        float fx = projection[0, 0] * width * 0.5f;
        float fy = projection[1, 1] * height * 0.5f;
        float cx = (1f - projection[0, 2]) * width * 0.5f;
        float cy = (1f + projection[1, 2]) * height * 0.5f;

        return new CameraIntrinsics
        {
            fx = fx,
            fy = fy,
            cx = cx,
            cy = cy
        };
    }

    private struct CameraIntrinsics
    {
        public float fx;
        public float fy;
        public float cx;
        public float cy;
    }
}
