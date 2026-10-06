using UnityEditor;
using UnityEngine;
using System.IO;

// 把 6camera 場景 build 成獨立測試播放器。
// 獨立播放器 + runInBackground 不受視窗焦點節流,適合供錄影程式測試時
// 作為穩定的虛擬相機訊號源(編輯器在背景會被大幅節流,不適合當訊號源)。
public static class BuildTestPlayer
{
    [MenuItem("Tools/Build 6camera 測試播放器")]
    public static void Build()
    {
        PlayerSettings.runInBackground = true;
        var options = new BuildPlayerOptions
        {
            scenes = new[] { "Assets/Scenes/6camera.unity" },
            locationPathName = "Builds/6camera_test/gait6cam.exe",
            target = BuildTarget.StandaloneWindows64,
            options = BuildOptions.None
        };
        var report = BuildPipeline.BuildPlayer(options);
        Debug.Log($"[BuildTestPlayer] {report.summary.result}, 輸出: {options.locationPathName}");
        if (report.summary.result == UnityEditor.Build.Reporting.BuildResult.Succeeded)
        {
            // unity_humanbones 透過 Application.dataPath 讀動作資料,
            // 播放器的 dataPath 是 <exe>_Data,把 Motion Data 一併帶上
            CopyMotionData("Assets/Motion data", "Builds/6camera_test/gait6cam_Data/Motion data");
            EditorUtility.RevealInFinder(Path.GetFullPath("Builds/6camera_test/gait6cam.exe"));
        }
    }

    static void CopyMotionData(string sourceDir, string targetDir)
    {
        Directory.CreateDirectory(targetDir);
        foreach (var file in Directory.GetFiles(sourceDir, "*.txt"))
            File.Copy(file, Path.Combine(targetDir, Path.GetFileName(file)), true);
        Debug.Log($"[BuildTestPlayer] Motion Data 已複製到 {targetDir}");
    }
}
