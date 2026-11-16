using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.UI;

namespace VResQ.Core
{
    public class VResqDropdownXR : MonoBehaviour
    {
        private void LateUpdate()
        {
            foreach (var canvas in GetComponentsInChildren<Canvas>())
            {
                var raycaster = canvas.GetComponent<TrackedDeviceGraphicRaycaster>() ?? canvas.gameObject.AddComponent<TrackedDeviceGraphicRaycaster>();
                raycaster.ignoreReversedGraphics = false;
                raycaster.checkFor3DOcclusion = false;
                raycaster.checkFor2DOcclusion = false;
                var mouse = canvas.GetComponent<GraphicRaycaster>();
                if (mouse != null) mouse.ignoreReversedGraphics = false;
            }
        }
    }
}
