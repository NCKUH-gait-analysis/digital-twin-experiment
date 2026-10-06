using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Collections;
using UnityEngine;
using Debug = UnityEngine.Debug;

public class MultiCameraMp4Recorder : MonoBehaviour
{
    [Header("Cameras")]
    public Camera[] cameras;

    [Header("Output")]
    public string folderName = "recording";
    public string ffmpegPath = "ffmpeg";
    public bool overwriteExistingFiles = true;

    [Header("Capture")]
    public int captureWidth = 1280;
    public int captureHeight = 720;
    public int targetFPS = 30;
    public bool recordOnStart = true;
    public bool stopWhenAnimationEnds = true;
    public Animator motionAnimator;

    [Header("Encoding")]
    public string videoCodec = "libx264";
    public string pixelFormat = "yuv420p";
    public string encoderPreset = "veryfast";
    public int crf = 18;
    public bool flipVertical = true;

    private RenderTexture[] renderTextures;
    private Texture2D[] captureTextures;
    private Process[] ffmpegProcesses;
    private Stream[] ffmpegInputs;
    private float[] previousCameraAspects;
    private Coroutine recordCoroutine;
    private bool isRecording;
    private string outputFolder;
    private int previousTargetFrameRate;

    private void Start()
    {
        if (recordOnStart)
        {
            StartRecording();
        }
    }

    [ContextMenu("Start Recording")]
    public void StartRecording()
    {
        if (isRecording)
        {
            Debug.LogWarning("MultiCameraMp4Recorder is already recording.");
            return;
        }

        if (cameras == null || cameras.Length == 0)
        {
            Debug.LogError("No cameras assigned.");
            return;
        }

        outputFolder = Path.Combine(Application.dataPath, "Captures", folderName);
        Directory.CreateDirectory(outputFolder);

        renderTextures = new RenderTexture[cameras.Length];
        captureTextures = new Texture2D[cameras.Length];
        ffmpegProcesses = new Process[cameras.Length];
        ffmpegInputs = new Stream[cameras.Length];
        previousCameraAspects = new float[cameras.Length];

        for (int i = 0; i < cameras.Length; i++)
        {
            if (cameras[i] == null)
            {
                Debug.LogError($"Camera {i} is not assigned.");
                StopRecording();
                return;
            }

            renderTextures[i] = new RenderTexture(captureWidth, captureHeight, 24, RenderTextureFormat.ARGB32);
            captureTextures[i] = new Texture2D(captureWidth, captureHeight, TextureFormat.RGB24, false);
            previousCameraAspects[i] = cameras[i].aspect;
            cameras[i].aspect = (float)captureWidth / captureHeight;

            string outputPath = Path.Combine(outputFolder, $"camera_{i}.mp4");
            if (File.Exists(outputPath) && !overwriteExistingFiles)
            {
                Debug.LogError($"Output file already exists: {outputPath}");
                StopRecording();
                return;
            }

            if (!StartFfmpeg(i, outputPath))
            {
                StopRecording();
                return;
            }
        }

        isRecording = true;
        previousTargetFrameRate = Application.targetFrameRate;
        Application.targetFrameRate = targetFPS;
        Time.captureFramerate = targetFPS;
        recordCoroutine = StartCoroutine(RecordLoop());
        Debug.Log($"Recording {cameras.Length} cameras to: {outputFolder}");
    }

    [ContextMenu("Stop Recording")]
    public void StopRecording()
    {
        if (recordCoroutine != null)
        {
            StopCoroutine(recordCoroutine);
            recordCoroutine = null;
        }

        isRecording = false;
        Time.captureFramerate = 0;
        Application.targetFrameRate = previousTargetFrameRate;

        CloseFfmpeg();
        RestoreCameraAspects();
        ReleaseCaptureBuffers();

        if (!string.IsNullOrEmpty(outputFolder))
        {
            Debug.Log($"MP4 recording finished: {outputFolder}");
        }
    }

    private IEnumerator RecordLoop()
    {
        WaitForEndOfFrame waitForEndOfFrame = new WaitForEndOfFrame();

        while (isRecording)
        {
            yield return waitForEndOfFrame;

            for (int i = 0; i < cameras.Length; i++)
            {
                CaptureAndWriteFrame(i);
            }

            if (stopWhenAnimationEnds && motionAnimator != null && IsAnimationOver())
            {
                StopRecording();
                yield break;
            }
        }
    }

    private void CaptureAndWriteFrame(int index)
    {
        Camera cam = cameras[index];
        RenderTexture previousTarget = cam.targetTexture;
        RenderTexture previousActive = RenderTexture.active;

        cam.targetTexture = renderTextures[index];
        cam.Render();

        RenderTexture.active = renderTextures[index];
        captureTextures[index].ReadPixels(new Rect(0, 0, captureWidth, captureHeight), 0, 0);
        captureTextures[index].Apply();

        cam.targetTexture = previousTarget;
        RenderTexture.active = previousActive;

        byte[] frameBytes = captureTextures[index].GetRawTextureData();
        ffmpegInputs[index].Write(frameBytes, 0, frameBytes.Length);
    }

    private bool StartFfmpeg(int cameraIndex, string outputPath)
    {
        try
        {
            ProcessStartInfo psi = new ProcessStartInfo
            {
                FileName = ffmpegPath,
                Arguments = BuildFfmpegArguments(outputPath),
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            Process process = new Process { StartInfo = psi };
            process.Start();

            ffmpegProcesses[cameraIndex] = process;
            ffmpegInputs[cameraIndex] = process.StandardInput.BaseStream;
            return true;
        }
        catch (Exception e)
        {
            Debug.LogError($"Failed to start ffmpeg for camera {cameraIndex}. Set ffmpegPath or install ffmpeg. Error: {e.Message}");
            return false;
        }
    }

    private string BuildFfmpegArguments(string outputPath)
    {
        StringBuilder args = new StringBuilder();
        args.Append(overwriteExistingFiles ? "-y " : "-n ");
        args.Append("-f rawvideo ");
        args.Append("-vcodec rawvideo ");
        args.Append("-pixel_format rgb24 ");
        args.Append($"-video_size {captureWidth}x{captureHeight} ");
        args.Append($"-framerate {targetFPS} ");
        args.Append("-i - ");
        args.Append("-an ");
        args.Append($"-c:v {videoCodec} ");
        args.Append($"-preset {encoderPreset} ");
        args.Append($"-crf {crf} ");
        if (flipVertical)
        {
            args.Append("-vf vflip ");
        }
        args.Append($"-pix_fmt {pixelFormat} ");
        args.Append($"\"{outputPath}\"");
        return args.ToString();
    }

    private void CloseFfmpeg()
    {
        if (ffmpegInputs != null)
        {
            for (int i = 0; i < ffmpegInputs.Length; i++)
            {
                try
                {
                    ffmpegInputs[i]?.Flush();
                    ffmpegInputs[i]?.Close();
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"Failed to close ffmpeg input {i}: {e.Message}");
                }
            }
        }

        if (ffmpegProcesses != null)
        {
            for (int i = 0; i < ffmpegProcesses.Length; i++)
            {
                Process process = ffmpegProcesses[i];
                if (process == null)
                {
                    continue;
                }

                try
                {
                    string errors = process.StandardError.ReadToEnd();
                    process.WaitForExit(3000);
                    if (process.ExitCode != 0)
                    {
                        Debug.LogWarning($"ffmpeg camera {i} exited with code {process.ExitCode}: {errors}");
                    }
                    process.Dispose();
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"Failed to finish ffmpeg camera {i}: {e.Message}");
                }
            }
        }
    }

    private void ReleaseCaptureBuffers()
    {
        if (renderTextures != null)
        {
            foreach (RenderTexture rt in renderTextures)
            {
                if (rt == null)
                {
                    continue;
                }

                rt.Release();
                Destroy(rt);
            }
        }

        if (captureTextures != null)
        {
            foreach (Texture2D texture in captureTextures)
            {
                if (texture != null)
                {
                    Destroy(texture);
                }
            }
        }

        RenderTexture.active = null;
    }

    private void RestoreCameraAspects()
    {
        if (cameras == null || previousCameraAspects == null)
        {
            return;
        }

        int count = Mathf.Min(cameras.Length, previousCameraAspects.Length);
        for (int i = 0; i < count; i++)
        {
            if (cameras[i] != null)
            {
                cameras[i].aspect = previousCameraAspects[i];
            }
        }
    }

    private bool IsAnimationOver()
    {
        AnimatorStateInfo stateInfo = motionAnimator.GetCurrentAnimatorStateInfo(0);
        return stateInfo.normalizedTime >= 1.0f;
    }

    private void OnApplicationQuit()
    {
        StopRecording();
    }
}
