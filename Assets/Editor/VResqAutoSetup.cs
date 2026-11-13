using System;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.XR.Interaction.Toolkit.UI;
using VResQ.Core;
using VResQ.Control;
using VResQ.Video;

public static class VResqAutoSetup
{
    private static T Ensure<T>(GameObject root) where T : Component
    {
        return root.GetComponent<T>() ?? Undo.AddComponent<T>(root);
    }

    [MenuItem("V-ResQ/Auto Setup Components and Video Controls", false, 3)]
    public static void RepairActiveScene()
    {
        var hud = UnityEngine.Object.FindFirstObjectByType<RescueRobotVRHUD>();
        if (hud == null)
        {
            HUDBuilderEditor.RebuildSceneCleanly();
            hud = UnityEngine.Object.FindFirstObjectByType<RescueRobotVRHUD>();
        }
        GameObject root = hud.gameObject;
        var config = Ensure<VResqFrameworkConfig>(root);
        Ensure<VResqAutoDiscovery>(root);
        Ensure<VResqClient>(root);
        var receiver = Ensure<VResqCameraReceiver>(root);
        var surround = Ensure<VResqSurroundDisplay>(root);
        var controller = Ensure<VResqTeleopController>(root);
        var follow = Ensure<TacticalHUDFollow>(root);
        var canvas = Ensure<Canvas>(root);
        canvas.renderMode = RenderMode.WorldSpace;
        Ensure<CanvasScaler>(root);
        Ensure<GraphicRaycaster>(root);
        Ensure<TrackedDeviceGraphicRaycaster>(root);
        var events = UnityEngine.Object.FindFirstObjectByType<EventSystem>();
        if (events == null)
        {
            var eventRoot = new GameObject("EventSystem");
            Undo.RegisterCreatedObjectUndo(eventRoot, "Create VResQ EventSystem");
            events = eventRoot.AddComponent<EventSystem>();
        }
        if (events.GetComponent<BaseInputModule>() == null) Ensure<XRUIInputModule>(events.gameObject);
        var presentation = Ensure<VResqPresentationControls>(root);
        receiver.targetRawImage = hud.centerVideoScreen;
        if (receiver.targetRawImage == null) throw new InvalidOperationException("Center robot RawImage is missing");
        receiver.targetRawImage.color = Color.white;
        hud.surroundDisplay = surround;
        if (surround.externalWorldRoot == null)
            foreach (var sceneRoot in root.scene.GetRootGameObjects())
                if (sceneRoot.name == "External_3D_World_Environment") surround.externalWorldRoot = sceneRoot;
        if (Camera.main != null)
        {
            canvas.worldCamera = Camera.main;
            follow.targetCamera = Camera.main.transform;
            surround.targetCamera = Camera.main.transform;
            controller.cameraRigTransform = Camera.main.transform.parent ?? Camera.main.transform;
        }
        presentation.cameraShader = AssetDatabase.LoadAssetAtPath<Shader>("Assets/VResQ/Video/VResqCameraDisplay.shader");
        Transform old = root.transform.Find("Video_Data_Controls");
        if (old != null) Undo.DestroyObjectImmediate(old.gameObject);
        var panel = Rect("Video_Data_Controls", root.transform, new Vector2(0, -580), new Vector2(1740, 84));
        panel.gameObject.AddComponent<Image>().color = new Color(.02f, .055f, .08f, .97f);
        presentation.backgroundToggle = MakeToggle(panel, "VR background", -730);
        presentation.monitorToggle = MakeToggle(panel, "Robot monitor", -470);
        Label(panel, "Brightness", new Vector2(-235, 0), new Vector2(130, 32));
        var sliderRect = Rect("Brightness", panel, new Vector2(-50, 0), new Vector2(220, 30));
        var rail = Rect("Rail", sliderRect, Vector2.zero, new Vector2(220, 6));
        rail.gameObject.AddComponent<Image>().color = new Color(.2f, .4f, .45f);
        var handle = Rect("Handle", sliderRect, Vector2.zero, new Vector2(20, 30));
        var handleImage = handle.gameObject.AddComponent<Image>();
        handleImage.color = new Color(.2f, 1f, .8f);
        var slider = sliderRect.gameObject.AddComponent<Slider>();
        slider.minValue = .5f;
        slider.maxValue = 2f;
        slider.handleRect = handle;
        slider.targetGraphic = handleImage;
        slider.value = config.Profile.cameraBrightness;
        presentation.brightnessSlider = slider;
        presentation.statusText = Label(panel, "Frame -- | Telemetry waiting", new Vector2(400, 0), new Vector2(660, 66));
        presentation.statusText.fontSize = 14;
        var reset = Rect("Reset diagnostics", panel, new Vector2(790, 0), new Vector2(130, 36));
        var resetImage = reset.gameObject.AddComponent<Image>();
        resetImage.color = new Color(.08f, .25f, .28f);
        presentation.resetDiagnostics = reset.gameObject.AddComponent<Button>();
        presentation.resetDiagnostics.targetGraphic = resetImage;
        Label(reset, "Reset counters", Vector2.zero, new Vector2(120, 32)).fontSize = 14;
        presentation.backgroundToggle.SetIsOnWithoutNotify(config.Profile.backgroundVisible);
        presentation.monitorToggle.SetIsOnWithoutNotify(config.Profile.robotMonitorVisible);
        EditorUtility.SetDirty(presentation);
        EditorUtility.SetDirty(receiver);
        EditorUtility.SetDirty(surround);
        EditorUtility.SetDirty(hud);
        EditorSceneManager.MarkSceneDirty(root.scene);
        EditorSceneManager.SaveScene(root.scene);
        Debug.Log("[VRESQ_SETUP] Components and video controls wired successfully");
    }

    public static void BuildConfiguredScenes()
    {
        foreach (string path in new[] { "Assets/Scenes/BasicScene.unity", "Assets/Scenes/SampleScene.unity" })
        {
            if (!System.IO.File.Exists(path)) continue;
            EditorSceneManager.OpenScene(path);
            RepairActiveScene();
        }
    }

    public static void RecoverVideoAndBuild()
    {
        BuildConfiguredScenes();
        foreach (string path in new[] { "Assets/Scenes/BasicScene.unity", "Assets/Scenes/SampleScene.unity" })
        {
            if (!System.IO.File.Exists(path)) continue;
            EditorSceneManager.OpenScene(path);
            var config = UnityEngine.Object.FindFirstObjectByType<VResqFrameworkConfig>();
            config.Profile.backgroundVisible = true;
            config.Profile.robotMonitorVisible = true;
            config.Profile.viewMode = SurroundViewMode.Surround360;
            config.NotifyConfigChanged();
            EditorSceneManager.MarkSceneDirty(config.gameObject.scene);
            EditorSceneManager.SaveScene(config.gameObject.scene);
        }
        VResqVideoRecoveryChecks.VerifyLiveCamera();
        BuildWindows();
    }

    [MenuItem("V-ResQ/Auto Build Windows Operator", false, 4)]
    public static void BuildWindows()
    {
        BuildConfiguredScenes();
        VResqRegressionChecks.VerifyScene();
        EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");
        var target = UnityEditor.Build.NamedBuildTarget.Standalone;
        var previousBackend = PlayerSettings.GetScriptingBackend(target);
        try
        {
            PlayerSettings.SetScriptingBackend(target, ScriptingImplementation.Mono2x);
            var result = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { "Assets/Scenes/SampleScene.unity" },
                locationPathName = "Builds/Windows/VResQ-Operator.exe",
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None
            });
            if (result.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded)
                throw new InvalidOperationException("Windows build failed");
        }
        finally { PlayerSettings.SetScriptingBackend(target, previousBackend); }
    }

    private static RectTransform Rect(string name, Transform parent, Vector2 position, Vector2 size)
    {
        var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
        return rect;
    }

    private static TextMeshProUGUI Label(Transform parent, string text, Vector2 position, Vector2 size)
    {
        var label = Rect(text, parent, position, size).gameObject.AddComponent<TextMeshProUGUI>();
        label.text = text;
        label.fontSize = 18;
        label.color = Color.white;
        label.alignment = TextAlignmentOptions.MidlineLeft;
        label.textWrappingMode = TextWrappingModes.NoWrap;
        label.overflowMode = TextOverflowModes.Ellipsis;
        label.raycastTarget = false;
        return label;
    }

    private static Toggle MakeToggle(Transform parent, string name, float x)
    {
        var root = Rect(name, parent, new Vector2(x, 0), new Vector2(240, 36));
        var box = Rect("Box", root, new Vector2(-105, 0), new Vector2(26, 26)).gameObject.AddComponent<Image>();
        box.color = new Color(.2f, .35f, .4f);
        var mark = Rect("Check", box.transform, Vector2.zero, new Vector2(16, 16)).gameObject.AddComponent<Image>();
        mark.color = new Color(.2f, 1f, .8f);
        Label(root, name, new Vector2(20, 0), new Vector2(195, 36));
        var toggle = root.gameObject.AddComponent<Toggle>();
        toggle.targetGraphic = box;
        toggle.graphic = mark;
        return toggle;
    }
}
