using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

// 進入 Play 模式時自動把場景中的相機掛上 UnityCapture 虛擬相機輸出,
// 不需要修改任何場景。相機依名稱排序對應到 Unity Video Capture #1~#6。
// 渲染尺寸決定方式(優先序):
//   1. 錄影程式寫入的共享設定檔(%TEMP%/gait_unity_capture_config.txt),
//      每秒輪詢、即時調整 → 在 app 裡切換解析度,Unity 自動跟上,不會有黑邊
//   2. 啟動參數 -capw / -caph(例: gait6cam.exe -capw 1280 -caph 720)
//   3. 預設 640x480
// 掛上 targetTexture 後 Game 視窗會變黑,屬正常現象。
// 啟用條件:場景名稱以 "6camera" 開頭,或啟動參數帶 -vcam。
// 其他場景(4M、ground_truth_recording 等)由 MultiCameraStreamer /
// MultiCameraMp4Recorder / GroundTruthRecorder 自己管理 targetTexture,不能被接管。
public static class AutoVirtualCams
{
    const int MaxDevices = 6;
    static int Width = 640, Height = 480;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Setup()
    {
        bool forced = ParseArgs();
        string scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
        if (!forced && !scene.StartsWith("6camera", System.StringComparison.OrdinalIgnoreCase))
        {
            Debug.Log($"[AutoVirtualCams] 場景 {scene} 非 6camera 且未帶 -vcam,略過虛擬相機掛載");
            return;
        }
        var cams = Object.FindObjectsOfType<Camera>()
            .Where(c => c.enabled)
            .OrderBy(c => c.name)
            .Take(MaxDevices)
            .ToArray();

        var watcher = new GameObject("AutoVirtualCamsWatcher").AddComponent<AutoVirtualCamsWatcher>();
        Object.DontDestroyOnLoad(watcher.gameObject);

        for (int i = 0; i < cams.Length; i++)
        {
            var cam = cams[i];
            if (cam.GetComponent<UnityCapture>() != null) continue;

            var rt = new RenderTexture(Width, Height, 24);
            rt.Create();
            cam.targetTexture = rt;

            var cap = cam.gameObject.AddComponent<UnityCapture>();
            cap.CaptureDevice = (UnityCapture.ECaptureDevice)i;
            cap.ResizeMode = UnityCapture.EResizeMode.LinearResize;
            cap.TargetFrameRate = 30;
            cap.HideWarnings = true;
            cap.DoubleBuffering = true;  // 用一幀延遲換吞吐,減輕 GPU 回讀停頓

            watcher.Register(i, cam);
            Debug.Log($"[AutoVirtualCams] {cam.name} -> Unity Video Capture #{i + 1} ({Width}x{Height}@30)");
        }

        if (cams.Length == 0)
            Debug.LogWarning("[AutoVirtualCams] 場景中沒有啟用中的相機");
    }

    // 回傳是否帶了 -vcam(強制啟用)
    static bool ParseArgs()
    {
        bool forced = false;
        var args = System.Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "-vcam") forced = true;
            if (i + 1 >= args.Length) continue;
            int value;
            if (args[i] == "-capw" && int.TryParse(args[i + 1], out value) && value > 0)
                Width = value;
            if (args[i] == "-caph" && int.TryParse(args[i + 1], out value) && value > 0)
                Height = value;
        }
        return forced;
    }
}

// 輪詢錄影程式寫入的解析度設定,對應相機即時重建 RenderTexture。
public class AutoVirtualCamsWatcher : MonoBehaviour
{
    struct Entry
    {
        public int index;
        public Camera cam;
        public int width, height;
    }

    readonly List<Entry> entries = new List<Entry>();
    string configPath;
    float nextPoll;

    void Awake()
    {
        configPath = Path.Combine(Path.GetTempPath(), "gait_unity_capture_config.txt");
    }

    public void Register(int index, Camera cam)
    {
        var rt = cam.targetTexture;
        entries.Add(new Entry { index = index, cam = cam, width = rt.width, height = rt.height });
    }

    static string DeviceName(int index)
    {
        return index == 0 ? "Unity Video Capture" : $"Unity Video Capture #{index + 1}";
    }

    void Update()
    {
        if (Time.unscaledTime < nextPoll) return;
        nextPoll = Time.unscaledTime + 1f;
        if (!File.Exists(configPath)) return;

        string[] lines;
        try { lines = File.ReadAllLines(configPath); }
        catch (IOException) { return; }   // 檔案正被寫入,下一輪再讀

        foreach (var line in lines)
        {
            var parts = line.Split(',');
            if (parts.Length != 3) continue;
            int width, height;
            if (!int.TryParse(parts[1], out width) || !int.TryParse(parts[2], out height)) continue;
            if (width <= 0 || height <= 0) continue;

            for (int i = 0; i < entries.Count; i++)
            {
                var entry = entries[i];
                if (entry.cam == null) continue;
                if (DeviceName(entry.index) != parts[0].Trim()) continue;
                if (entry.width == width && entry.height == height) continue;

                var old = entry.cam.targetTexture;
                var rt = new RenderTexture(width, height, 24);
                rt.Create();
                entry.cam.targetTexture = rt;
                if (old != null) old.Release();

                entry.width = width;
                entry.height = height;
                entries[i] = entry;
                Debug.Log($"[AutoVirtualCams] {entry.cam.name} 依錄影程式設定調整為 {width}x{height}");
            }
        }
    }
}
