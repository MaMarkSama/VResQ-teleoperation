using System;
using UnityEditor;
using UnityEngine;
using Unity.XR.CoreUtils;
using UnityEngine.EventSystems;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.Interaction.Toolkit.UI;
using UnityEngine.XR.OpenXR;
using UnityEngine.XR.OpenXR.Features;
using UnityEditor.XR.Management;
using UnityEditor.XR.Management.Metadata;
using System.Linq;
using VResQ.Core;

public static class VResqXRSetup
{
    public static void ConfigureRig(GameObject hud)
    {
        var rig = GameObject.Find("VResQ XR Hands Rig");
        if (rig == null)
        {
            const string path = "Assets/VRTemplateAssets/Prefabs/Setup/Complete XR Origin Set Up Hands Variant.prefab";
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null) throw new InvalidOperationException("XR Hands rig prefab is missing");
            rig = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            rig.name = "VResQ XR Hands Rig";
            Undo.RegisterCreatedObjectUndo(rig, "Install VResQ hands rig");
            foreach (var origin in UnityEngine.Object.FindObjectsByType<XROrigin>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (origin.transform.IsChildOf(rig.transform)) continue;
                if (hud.transform.IsChildOf(origin.transform)) hud.transform.SetParent(null, true);
                origin.gameObject.SetActive(false);
                EditorUtility.SetDirty(origin.gameObject);
            }
        }
        var xrOrigin = rig.GetComponentInChildren<XROrigin>(true);
        if (xrOrigin == null || xrOrigin.Camera == null) throw new InvalidOperationException("Hands rig has no XR camera");
        xrOrigin.Camera.tag = "MainCamera";
        foreach (var behaviour in rig.GetComponentsInChildren<MonoBehaviour>(true))
            if (behaviour != null && (behaviour.GetType().Namespace ?? "").Contains(".Locomotion.")) behaviour.enabled = false;
        var camera = xrOrigin.Camera;
        var manager = camera.GetComponent<ARCameraManager>() ?? camera.gameObject.AddComponent<ARCameraManager>();
        manager.enabled = true;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = Color.clear;
        camera.allowHDR = false;
        camera.GetUniversalAdditionalCameraData().renderPostProcessing = false;
        if (UnityEngine.Object.FindFirstObjectByType<ARSession>() == null)
            Undo.RegisterCreatedObjectUndo(new GameObject("VResQ AR Session", typeof(ARSession)), "Create AR Session");
        var general = XRGeneralSettingsPerBuildTarget.XRGeneralSettingsForBuildTarget(BuildTargetGroup.Standalone);
        if (general == null || general.Manager == null) throw new InvalidOperationException("Standalone XR settings are missing");
        general.InitManagerOnStart = true;
        foreach (var loader in general.Manager.activeLoaders.ToArray())
            if (loader.GetType().Name.Contains("Simulation")) general.Manager.TryRemoveLoader(loader);
        if (!general.Manager.activeLoaders.Any(loader => loader is OpenXRLoader))
            if (!XRPackageMetadataStore.AssignLoader(general.Manager, typeof(OpenXRLoader).FullName, BuildTargetGroup.Standalone))
                throw new InvalidOperationException("Cannot configure OpenXR for Quest Link");
        EditorUtility.SetDirty(general);
        EditorUtility.SetDirty(general.Manager);
        foreach (var group in new[] { BuildTargetGroup.Standalone })
        {
            var settings = OpenXRSettings.GetSettingsForBuildTargetGroup(group);
            if (settings == null) continue;
            foreach (var feature in settings.GetFeatures<OpenXRFeature>())
            {
                string ns = feature.GetType().Namespace ?? "";
                string name = feature.GetType().Name;
                bool metaFeature = ns.Contains("Features.Meta") && (name == "ARCameraFeature" || name == "ARSessionFeature");
                bool handsFeature = name == "HandTracking" || name == "MetaHandTrackingAim" ||
                    name == "HandInteractionPoses" || name == "HandInteractionProfile" || name == "MetaQuestFeature";
                if (metaFeature || handsFeature)
                {
                    feature.enabled = true;
                    EditorUtility.SetDirty(feature);
                }
            }
        }
        var events = UnityEngine.Object.FindFirstObjectByType<EventSystem>();
        if (events == null) events = new GameObject("EventSystem", typeof(EventSystem)).GetComponent<EventSystem>();
        events.enabled = true;
        foreach (var other in UnityEngine.Object.FindObjectsByType<EventSystem>(FindObjectsSortMode.None))
            if (other != events)
            {
                other.enabled = false;
                foreach (var module in other.GetComponents<BaseInputModule>()) module.enabled = false;
                EditorUtility.SetDirty(other);
            }
        foreach (var module in events.GetComponents<BaseInputModule>())
            if (!(module is XRUIInputModule)) module.enabled = false;
        var xrUi = events.GetComponent<XRUIInputModule>() ?? events.gameObject.AddComponent<XRUIInputModule>();
        xrUi.enabled = true;
        xrUi.enableXRInput = true;
        xrUi.enableMouseInput = true;
        xrUi.enableTouchInput = true;
        xrUi.enableGamepadInput = true;
        EditorUtility.SetDirty(xrUi);
        var raycaster = hud.GetComponent<TrackedDeviceGraphicRaycaster>() ?? hud.AddComponent<TrackedDeviceGraphicRaycaster>();
        raycaster.checkFor3DOcclusion = false;
        raycaster.checkFor2DOcclusion = false;
        raycaster.ignoreReversedGraphics = false;
        var mouseRaycaster = hud.GetComponent<GraphicRaycaster>() ?? hud.AddComponent<GraphicRaycaster>();
        mouseRaycaster.ignoreReversedGraphics = false;
        mouseRaycaster.blockingObjects = GraphicRaycaster.BlockingObjects.None;
        EditorUtility.SetDirty(mouseRaycaster);
        EditorUtility.SetDirty(raycaster);
        foreach (var graphic in hud.GetComponentsInChildren<Graphic>(true))
            if (graphic.GetComponentInParent<Selectable>() == null)
            {
                graphic.raycastTarget = graphic.gameObject == hud.GetComponent<RescueRobotVRHUD>().settingsModalRoot;
                EditorUtility.SetDirty(graphic);
            }
        var profile = hud.GetComponent<VResqFrameworkConfig>().Profile;
        profile.headsetPassthrough = true;
        profile.backgroundVisible = false;
        EditorUtility.SetDirty(manager);
        EditorUtility.SetDirty(camera);
        Debug.Log("[VRESQ_XR_SETUP] Hands rig, passthrough and UI input configured");
    }
}
