using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace VResQ.Core
{
    public class VResqUIFeedback : MonoBehaviour
    {
        public TextMeshProUGUI output;
        private float expires;

        private IEnumerator Start()
        {
            yield return null;
            foreach (var button in GetComponentsInChildren<Button>(true))
            {
                Button target = button;
                target.onClick.AddListener(() => Report(target));
            }
        }

        public void Report(Button button)
        {
            var label = button.GetComponentInChildren<TextMeshProUGUI>();
            string name = label != null ? label.text : button.name;
            var client = GetComponent<VResqClient>();
            string state = "UI received";
            bool remoteCommand = button.name.Contains("APPROACH") || button.name.Contains("BACK") ||
                button.name.Contains("SURVEY") || button.name.Contains("RTH") || button.name.Contains("SCAN");
            if (remoteCommand && client != null)
                state = !client.IsConnected ? "Robot link unavailable" : !client.ControlAuthenticated
                    ? "Operator authentication unavailable" : client.LatestState == null || !client.LatestState.motion_authorized ? "Motion not authorized" : "Command requested";
            output.text = name + " | " + state;
            output.color = state == "UI received" ? new Color(.5f, 1f, .85f) : new Color(1, .8f, .3f);
            expires = Time.unscaledTime + 5;
        }

        private void Update()
        {
            if (output != null && Time.unscaledTime > expires)
            {
                var client = GetComponent<VResqClient>();
                var controller = GetComponent<VResQ.Control.VResqTeleopController>();
                output.text = $"{controller?.DriveStatus} | WS sent {client.OperatorCommandsSent} | ESP32 cmd RX {client.LastReportedCommandRx} | {client.LastCommandStatus}";
            }
        }
    }
}
