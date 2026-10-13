using System.Linq;
using UnityEngine;

// Play 之後的執行期監看/控制面板(IMGUI)。
//
// 背景:AutoVirtualCams 把所有相機的 targetTexture 接走後,沒有相機渲染到螢幕,
// Game 視窗只剩黑底和「Display 1 No cameras rendering」。這個面板:
//   1. 生成一台不畫場景的監看相機(cullingMask = 0)渲染到 Display 1,Unity 就不再顯示那行字;
//      再把選中相機的 RenderTexture 依比例鋪滿 Game 視窗(預設 Camera0,可切換)。
//   2. 場景模式切換(棋盤 / ArUco / 錄影 / 原始),實際切換邏輯在 SceneModeController。
//   3. 控制 targetName(預設 Amber)的動畫:重頭播放 / 暫停 / 速度。
//
// 用 IMGUI 而不是 Canvas:不需要 EventSystem、不依賴場景相機。
// 快捷鍵:F1~F4 場景模式、1~6 監看相機、R 重頭播放、Space 暫停/繼續、H 收合面板。
// 由 AutoVirtualCams.Setup 在 6camera 場景自動生成;也可手動掛到任何 GameObject 上。
public class RuntimeControlPanel : MonoBehaviour
{
    [Tooltip("要控制的人物 GameObject 名稱(需帶 Animator)。找不到時退回場景中第一個有 controller 的 Animator。")]
    public string targetName = "Amber";

    [Tooltip("Game 視窗預設顯示哪台相機的畫面(需有 targetTexture)。找不到時退回名稱排序第一台。")]
    public string viewCameraName = "Camera0";

    const float PanelWidth = 340f;
    const float Margin = 10f;

    Animator target;
    Vector3 startPos;
    Quaternion startRot;
    float speed = 1f;      // 使用者設定的速度;暫停時 animator.speed 為 0,原值保留在這裡
    bool paused;

    Camera[] viewCams = new Camera[0];
    int selected;
    Camera monitorCam;
    SceneModeController modes;

    Rect windowRect = new Rect(Margin, Margin, PanelWidth, 10f);
    bool collapsed;
    GUIStyle smallLabel;
    GUIStyle okLabel;
    GUIStyle badLabel;

    void Start()
    {
        modes = GetComponent<SceneModeController>() ?? gameObject.AddComponent<SceneModeController>();
        FindTarget();
        FindViewCams();
        EnsureMonitorCamera();
    }

    void FindTarget()
    {
        var animators = FindObjectsOfType<Animator>(true)
            .Where(a => a.runtimeAnimatorController != null)
            .OrderBy(a => a.name)
            .ToArray();
        target = animators.FirstOrDefault(a => a.name == targetName) ?? animators.FirstOrDefault();
        if (target == null)
        {
            Debug.LogWarning($"[RuntimeControlPanel] 場景中找不到帶 controller 的 Animator(targetName={targetName})");
            return;
        }
        if (target.name != targetName)
            Debug.LogWarning($"[RuntimeControlPanel] 找不到 {targetName},改控制 {target.name}");
        startPos = target.transform.position;
        startRot = target.transform.rotation;
        speed = target.speed > 0f ? target.speed : 1f;
        paused = target.speed == 0f;
    }

    void FindViewCams()
    {
        viewCams = FindObjectsOfType<Camera>()
            .Where(c => c.enabled && c.targetTexture != null)
            .OrderBy(c => c.name)
            .ToArray();
        int idx = System.Array.FindIndex(viewCams, c => c.name == viewCameraName);
        if (idx < 0 && viewCams.Length > 0)
            Debug.LogWarning($"[RuntimeControlPanel] 找不到 {viewCameraName},Game 視窗改顯示 {viewCams[0].name}");
        selected = Mathf.Max(0, idx);
    }

    // 若沒有任何相機渲染到螢幕,補一台只負責清成黑色的監看相機,
    // 消掉 Unity 的「No cameras rendering」提示;真正的畫面由 OnGUI 畫上去。
    void EnsureMonitorCamera()
    {
        bool anyOnScreen = FindObjectsOfType<Camera>().Any(c => c.enabled && c.targetTexture == null);
        if (anyOnScreen) return;
        var go = new GameObject("RuntimeControlPanel_Monitor");
        go.transform.SetParent(transform, false);
        monitorCam = go.AddComponent<Camera>();
        monitorCam.cullingMask = 0;
        monitorCam.clearFlags = CameraClearFlags.SolidColor;
        monitorCam.backgroundColor = Color.black;
        monitorCam.depth = 100f;
        monitorCam.targetDisplay = 0;
        monitorCam.allowHDR = false;
        monitorCam.allowMSAA = false;
    }

    // ---- 動畫控制 ----

    // 回到第 0 幀:Rebind 讓所有 layer 回到預設狀態、時間歸零;Update(0) 立刻套用姿勢。
    // 有 root motion 的話人物會越走越遠,所以連初始位置一起還原。
    public void Restart()
    {
        if (target == null) return;
        target.transform.SetPositionAndRotation(startPos, startRot);
        target.Rebind();
        target.Update(0f);
        ApplySpeed();
    }

    public void TogglePause()
    {
        paused = !paused;
        ApplySpeed();
    }

    void ApplySpeed()
    {
        if (target != null) target.speed = paused ? 0f : speed;
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.R)) Restart();
        if (Input.GetKeyDown(KeyCode.Space)) TogglePause();
        if (Input.GetKeyDown(KeyCode.H)) collapsed = !collapsed;
        if (Input.GetKeyDown(KeyCode.F1)) modes.SetMode(SceneModeController.Mode.Chessboard, "快捷鍵");
        if (Input.GetKeyDown(KeyCode.F2)) modes.SetMode(SceneModeController.Mode.Aruco, "快捷鍵");
        if (Input.GetKeyDown(KeyCode.F3)) modes.SetMode(SceneModeController.Mode.Recording, "快捷鍵");
        if (Input.GetKeyDown(KeyCode.F4)) modes.SetMode(SceneModeController.Mode.None, "快捷鍵");
        for (int i = 0; i < Mathf.Min(viewCams.Length, 9); i++)
            if (Input.GetKeyDown(KeyCode.Alpha1 + i)) selected = i;
    }

    // ---- 繪製 ----

    void OnGUI()
    {
        if (smallLabel == null)
        {
            smallLabel = new GUIStyle(GUI.skin.label) { fontSize = 11 };
            smallLabel.normal.textColor = new Color(0.8f, 0.8f, 0.8f);
            okLabel = new GUIStyle(smallLabel);
            okLabel.normal.textColor = new Color(0.4f, 0.9f, 0.5f);
            badLabel = new GUIStyle(smallLabel);
            badLabel.normal.textColor = new Color(1f, 0.5f, 0.4f);
        }

        DrawMainView();                 // 先畫背景,面板疊在上面

        windowRect.width = PanelWidth;
        windowRect = GUILayout.Window(GetInstanceID(), windowRect, DrawWindow,
            collapsed ? "控制面板 (H 展開)" : "控制面板");
    }

    static Rect FitRect(Rect area, float w, float h)
    {
        float s = Mathf.Min(area.width / w, area.height / h);
        float fw = w * s, fh = h * s;
        return new Rect(area.x + (area.width - fw) / 2f, area.y + (area.height - fh) / 2f, fw, fh);
    }

    void DrawMainView()
    {
        if (viewCams.Length == 0) { FindViewCams(); return; }
        selected = Mathf.Clamp(selected, 0, viewCams.Length - 1);
        var cam = viewCams[selected];
        if (cam == null || cam.targetTexture == null) { FindViewCams(); return; }
        var rt = cam.targetTexture;
        GUI.DrawTexture(FitRect(new Rect(0f, 0f, Screen.width, Screen.height), rt.width, rt.height),
            rt, ScaleMode.StretchToFill, false);
        GUI.Label(new Rect(Screen.width - 200f, Margin, 190f, 20f),
            $"{cam.name}  {rt.width}x{rt.height}", smallLabel);
    }

    static string ModeLabel(SceneModeController.Mode m)
    {
        switch (m)
        {
            case SceneModeController.Mode.Chessboard: return "棋盤";
            case SceneModeController.Mode.Aruco: return "ArUco";
            case SceneModeController.Mode.Recording: return "錄影";
            default: return "原始";
        }
    }

    void ModeButton(SceneModeController.Mode m, string label)
    {
        bool on = modes.Current == m;
        if (GUILayout.Toggle(on, label, GUI.skin.button) && !on) modes.SetMode(m, "面板");
    }

    void DrawWindow(int id)
    {
        if (collapsed)
        {
            if (GUILayout.Button("展開")) collapsed = false;
            GUI.DragWindow();
            return;
        }

        // ---- 場景模式 ----
        GUILayout.Label("場景模式", smallLabel);
        GUILayout.BeginHorizontal();
        ModeButton(SceneModeController.Mode.Chessboard, "棋盤 F1");
        ModeButton(SceneModeController.Mode.Aruco, "ArUco F2");
        ModeButton(SceneModeController.Mode.Recording, "錄影 F3");
        ModeButton(SceneModeController.Mode.None, "原始 F4");
        GUILayout.EndHorizontal();
        GUILayout.Label(
            $"目前:{ModeLabel(modes.Current)}" + (string.IsNullOrEmpty(modes.Source) ? "" : $"  (來源:{modes.Source})"),
            smallLabel);

        if (modes.Current == SceneModeController.Mode.Chessboard)
        {
            modes.boardSway = GUILayout.Toggle(modes.boardSway, " 棋盤緩慢擺動(校正取得不同姿態)");
        }
        else if (modes.Current == SceneModeController.Mode.Aruco)
        {
            // 每台相機是否完整拍得到 ArUco(四個角都在畫面內)
            GUILayout.BeginHorizontal();
            foreach (var cam in viewCams)
            {
                int n;
                bool ok = modes.IsArucoFullyVisible(cam, out n);
                GUILayout.Label($"{cam.name.Replace("Camera", "C")}{(ok ? " ✓" : $" ✗{n}/4")}", ok ? okLabel : badLabel);
            }
            GUILayout.EndHorizontal();
        }

        GUILayout.Space(6f);

        // ---- 監看相機 ----
        GUILayout.Label("Game 視窗顯示的相機 (1~6)", smallLabel);
        GUILayout.BeginHorizontal();
        for (int i = 0; i < viewCams.Length; i++)
        {
            if (viewCams[i] == null) continue;
            bool on = i == selected;
            if (GUILayout.Toggle(on, viewCams[i].name.Replace("Camera", "C"), GUI.skin.button) && !on) selected = i;
        }
        GUILayout.EndHorizontal();

        GUILayout.Space(6f);

        // ---- 人物動畫 ----
        if (target == null)
        {
            GUILayout.Label($"找不到 {targetName} 的 Animator", smallLabel);
            if (GUILayout.Button("重新掃描")) { FindTarget(); FindViewCams(); }
            GUI.DragWindow();
            return;
        }

        var state = target.GetCurrentAnimatorStateInfo(0);
        var clips = target.GetCurrentAnimatorClipInfo(0);
        string clipName = clips.Length > 0 && clips[0].clip != null ? clips[0].clip.name : "-";
        float t = state.length > 0f ? Mathf.Repeat(state.normalizedTime, 1f) : 0f;
        GUILayout.Label(target.name + (target.gameObject.activeInHierarchy ? "" : "  (目前模式已隱藏)"));
        GUILayout.Label($"{clipName}  {t * state.length:0.00}s / {state.length:0.00}s  (x{target.speed:0.00})", smallLabel);

        var bar = GUILayoutUtility.GetRect(10f, 6f, GUILayout.ExpandWidth(true));
        GUI.Box(bar, GUIContent.none);
        GUI.color = new Color(0.3f, 0.8f, 0.4f);
        GUI.DrawTexture(new Rect(bar.x, bar.y, bar.width * t, bar.height), Texture2D.whiteTexture);
        GUI.color = Color.white;

        GUILayout.Space(4f);
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("重頭播放 (R)")) Restart();
        if (GUILayout.Button(paused ? "繼續 (Space)" : "暫停 (Space)")) TogglePause();
        GUILayout.EndHorizontal();

        GUILayout.BeginHorizontal();
        GUILayout.Label("速度", GUILayout.Width(30f));
        float newSpeed = GUILayout.HorizontalSlider(speed, 0.1f, 2f);
        if (!Mathf.Approximately(newSpeed, speed)) { speed = newSpeed; ApplySpeed(); }
        if (GUILayout.Button("1x", GUILayout.Width(30f))) { speed = 1f; ApplySpeed(); }
        GUILayout.EndHorizontal();

        GUILayout.Space(4f);
        GUILayout.BeginHorizontal();
        GUILayout.FlexibleSpace();
        if (GUILayout.Button("收合 (H)", GUILayout.Width(70f))) collapsed = true;
        GUILayout.EndHorizontal();

        GUI.DragWindow(new Rect(0f, 0f, 10000f, 20f));
    }
}
