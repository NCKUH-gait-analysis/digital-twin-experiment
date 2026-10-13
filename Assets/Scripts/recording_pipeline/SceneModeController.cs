using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

// 6camera 測試場景的「場景模式」切換,對應錄影程式的三個階段:
//   Chessboard:每台相機正前方各掛一張棋盤(6x8 格 → 5x7 內角點,對齊錄影程式的內參校正),
//              用 layer 讓每台相機只看得到自己的那張;人物與 ArUco 全部隱藏。
//   Aruco:     只留地面的 ArUco,人物隱藏(外參校正)。
//   Recording: 只留 Amber(錄影);GT、ArUco、棋盤隱藏。
//   None:      還原成場景原本的狀態。
//
// 模式來源有三:面板按鈕、快捷鍵、以及錄影程式寫的 %TEMP%/gait_unity_scene_mode.txt
// (內容為 chessboard / aruco / recording / none,每秒輪詢,內容變了才套用;
//  錄影程式在進入內參校正頁、外參校正頁、錄影頁與回主頁時寫入,關閉時刪除)。
public class SceneModeController : MonoBehaviour
{
    public enum Mode { None, Chessboard, Aruco, Recording }

    public string amberName = "Amber";
    public string gtName = "GT";
    public string arucoName = "Aruco";

    [Header("棋盤")]
    public float boardDistance = 1.0f;   // 棋盤距相機距離(m)
    public float boardHeight = 0.65f;    // 棋盤高度(m);60° FOV 下約佔畫面高 56%
    public bool boardSway = true;        // 緩慢擺動,讓校正取得不同姿態而不是 N 張一樣的圖
    public float swayAngle = 12f;        // 擺動最大角度(度)
    public const int FirstBoardLayer = 24;   // layer 24~29 給 6 張棋盤;專案裡這些 layer 未命名、未使用

    public Mode Current { get; private set; } = Mode.None;
    public string Source { get; private set; } = "";   // 最近一次切換的來源:app / 面板 / 快捷鍵
    public IReadOnlyList<Camera> Cameras { get { return boards.Select(b => b.cam).ToList(); } }

    public static readonly string ModePath = Path.Combine(Path.GetTempPath(), "gait_unity_scene_mode.txt");

    class Board
    {
        public Camera cam;
        public GameObject go;
        public int origMask;
        public float phase;
    }

    GameObject amber, gt, aruco;
    bool amberInit, gtInit, arucoInit;
    readonly List<Board> boards = new List<Board>();
    bool boardsBuilt;
    float nextPoll;
    string lastFileContent;

    void Awake()
    {
        amber = FindRoot(amberName);
        gt = FindRoot(gtName);
        aruco = FindRoot(arucoName);
        amberInit = amber != null && amber.activeSelf;
        gtInit = gt != null && gt.activeSelf;
        arucoInit = aruco != null && aruco.activeSelf;
    }

    // GameObject.Find 找不到 inactive 物件,所以掃 root 物件
    static GameObject FindRoot(string name)
    {
        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        return scene.GetRootGameObjects().FirstOrDefault(g => g.name == name);
    }

    void Start()
    {
        // 相機的 targetTexture 由 AutoVirtualCams 在 AfterSceneLoad 指定,到 Start 時已經就緒
        BuildBoards();
        nextPoll = 0f;   // 第一幀就讀一次設定檔:錄影程式若已在某個階段,Unity 直接跟上
    }

    void Update()
    {
        PollModeFile();
        if (Current == Mode.Chessboard && boardSway) SwayBoards();
    }

    // ---- 模式切換 ----

    public static bool TryParse(string text, out Mode mode)
    {
        switch ((text ?? "").Trim().ToLowerInvariant())
        {
            case "chessboard": mode = Mode.Chessboard; return true;
            case "aruco": mode = Mode.Aruco; return true;
            case "recording": mode = Mode.Recording; return true;
            case "none": case "original": mode = Mode.None; return true;
        }
        mode = Mode.None;
        return false;
    }

    public void SetMode(Mode mode, string source)
    {
        Current = mode;
        Source = source;

        bool showBoards = mode == Mode.Chessboard;
        SetActive(amber, mode == Mode.Recording || (mode == Mode.None && amberInit));
        SetActive(gt, mode == Mode.None && gtInit);
        SetActive(aruco, mode == Mode.Aruco || (mode == Mode.None && arucoInit));

        int allBoardLayers = 0;
        for (int i = 0; i < boards.Count; i++) allBoardLayers |= 1 << (FirstBoardLayer + i);
        for (int i = 0; i < boards.Count; i++)
        {
            var b = boards[i];
            if (b.go != null) b.go.SetActive(showBoards);
            if (b.cam == null) continue;
            // 棋盤模式:每台相機只看自己的棋盤;其他模式:還原原本的 cullingMask
            b.cam.cullingMask = showBoards
                ? (b.origMask & ~allBoardLayers) | (1 << (FirstBoardLayer + i))
                : b.origMask;
        }
        Debug.Log($"[SceneMode] -> {mode} (來源:{source})");
    }

    static void SetActive(GameObject go, bool active)
    {
        if (go != null && go.activeSelf != active) go.SetActive(active);
    }

    // ---- 棋盤 ----

    void BuildBoards()
    {
        if (boardsBuilt) return;
        boardsBuilt = true;

        var cams = FindObjectsOfType<Camera>()
            .Where(c => c.enabled && c.targetTexture != null)
            .OrderBy(c => c.name)
            .Take(6)
            .ToArray();
        if (cams.Length == 0)
        {
            Debug.LogWarning("[SceneMode] 找不到有 targetTexture 的相機,棋盤模式無法使用");
            return;
        }

        var mat = LoadBoardMaterial();
        float aspect = 1040f / 1280f;   // chessboard_6x8.png 的寬高比
        if (mat != null && mat.mainTexture != null)
            aspect = (float)mat.mainTexture.width / mat.mainTexture.height;

        for (int i = 0; i < cams.Length; i++)
        {
            var cam = cams[i];
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name = $"CalibBoard_{cam.name}";
            var col = go.GetComponent<Collider>();
            if (col != null) Destroy(col);
            // Quad 的正面朝向自己的 -Z;掛在相機底下、local rotation 為零,正面就正對相機
            go.transform.SetParent(cam.transform, false);
            go.transform.localPosition = new Vector3(0f, 0f, boardDistance);
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = new Vector3(boardHeight * aspect, boardHeight, 1f);
            go.layer = FirstBoardLayer + i;
            var r = go.GetComponent<Renderer>();
            if (mat != null) r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            go.SetActive(false);

            boards.Add(new Board { cam = cam, go = go, origMask = cam.cullingMask, phase = i * 1.3f });
        }
    }

    // Resources/Calib/chessboard_6x8.mat(Unlit,無光照漸層);找不到就退回場景裡那張 chessboard 的材質
    static Material LoadBoardMaterial()
    {
        var mat = Resources.Load<Material>("Calib/chessboard_6x8");
        if (mat != null) return mat;
        Debug.LogWarning("[SceneMode] 找不到 Resources/Calib/chessboard_6x8.mat,改用場景 chessboard 的材質");
        var legacy = FindRoot("chessboard");
        var r = legacy != null ? legacy.GetComponent<Renderer>() : null;
        return r != null ? r.sharedMaterial : null;
    }

    void SwayBoards()
    {
        float t = Time.time;
        foreach (var b in boards)
        {
            if (b.go == null) continue;
            float p = b.phase;
            var rot = Quaternion.Euler(
                Mathf.Sin(t * 0.50f + p) * swayAngle,
                Mathf.Sin(t * 0.37f + p * 2f) * swayAngle,
                Mathf.Sin(t * 0.23f + p * 3f) * swayAngle * 0.5f);
            var offset = new Vector3(Mathf.Sin(t * 0.31f + p) * 0.10f, Mathf.Sin(t * 0.27f + p * 2f) * 0.08f, 0f);
            b.go.transform.localRotation = rot;
            b.go.transform.localPosition = new Vector3(0f, 0f, boardDistance) + offset;
        }
    }

    // ---- ArUco 可見性:四個角都落在該相機的畫面內才算 ----

    public bool IsArucoFullyVisible(Camera cam, out int visibleCorners)
    {
        visibleCorners = 0;
        if (cam == null || aruco == null) return false;
        var r = aruco.GetComponentInChildren<Renderer>();
        if (r == null) return false;
        var bnd = r.bounds;
        float y = bnd.center.y;
        var corners = new[]
        {
            new Vector3(bnd.min.x, y, bnd.min.z), new Vector3(bnd.max.x, y, bnd.min.z),
            new Vector3(bnd.min.x, y, bnd.max.z), new Vector3(bnd.max.x, y, bnd.max.z),
        };
        foreach (var c in corners)
        {
            var v = cam.WorldToViewportPoint(c);
            if (v.z > 0f && v.x >= 0f && v.x <= 1f && v.y >= 0f && v.y <= 1f) visibleCorners++;
        }
        return visibleCorners == 4;
    }

    // ---- 錄影程式寫的設定檔 ----

    void PollModeFile()
    {
        if (Time.unscaledTime < nextPoll) return;
        nextPoll = Time.unscaledTime + 1f;
        if (!File.Exists(ModePath)) return;

        string content;
        try { content = File.ReadAllText(ModePath).Trim(); }
        catch (IOException) { return; }   // 正在被寫入,下一輪再讀

        if (content == lastFileContent) return;
        lastFileContent = content;

        Mode mode;
        if (TryParse(content, out mode)) SetMode(mode, "app");
        else Debug.LogWarning($"[SceneMode] 設定檔內容無法辨識:{content}");
    }
}
