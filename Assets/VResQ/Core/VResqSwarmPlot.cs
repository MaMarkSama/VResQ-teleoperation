using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace VResQ.Core
{
    [RequireComponent(typeof(CanvasRenderer))]
    public class VResqSwarmPlot : MaskableGraphic
    {
        private JToken snapshot;
        public void SetSnapshot(JToken value) { snapshot = value as JObject; SetVerticesDirty(); }

        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear();
            var bounds = snapshot?["arena_bounds"] as JObject;
            var robots = snapshot?["robots"] as JArray;
            if (bounds == null || robots == null) return;
            float minX = Number(bounds["min_x"]), maxX = Number(bounds["max_x"]);
            float minY = Number(bounds["min_y"]), maxY = Number(bounds["max_y"]);
            if (float.IsNaN(minX) || float.IsNaN(maxX) || float.IsNaN(minY) || float.IsNaN(maxY) || float.IsInfinity(minX) || float.IsInfinity(maxX) || float.IsInfinity(minY) || float.IsInfinity(maxY)) return;
            if (maxX <= minX || maxY <= minY) return;
            var rect = rectTransform.rect;
            Vector2 Project(float x, float y) => new Vector2(Mathf.Lerp(rect.xMin + 10, rect.xMax - 10, Mathf.Clamp01((x - minX) / (maxX - minX))), Mathf.Lerp(rect.yMin + 10, rect.yMax - 10, Mathf.Clamp01((y - minY) / (maxY - minY))));
            if (snapshot["arena_walls"] is JArray walls)
                foreach (var wall in walls)
                {
                    if (!(wall is JObject)) continue;
                    var a = Project(Number(wall["x1"]), Number(wall["y1"]));
                    var b = Project(Number(wall["x2"]), Number(wall["y2"]));
                    Line(mesh, a, b, new Color(.65f, .8f, .85f));
                }
            if (snapshot["relay_links"] is JArray links)
                foreach (var link in links)
                {
                    if (!(link is JObject)) continue;
                    if (!(link["from_pose"] is JObject a) || !(link["to_pose"] is JObject b)) continue;
                    Line(mesh, Project(Number(a["x"]), Number(a["y"])), Project(Number(b["x"]), Number(b["y"])), new Color(.1f, .5f, .65f));
                }
            foreach (var robot in robots)
            {
                if (!(robot is JObject)) continue;
                var pose = robot["pose"] as JObject;
                if (pose?["x"] == null || pose["y"] == null) continue;
                float x = Number(pose["x"]), y = Number(pose["y"]);
                if (float.IsNaN(x) || float.IsNaN(y) || float.IsInfinity(x) || float.IsInfinity(y)) continue;
                var point = Project(x, y);
                var tint = (string)robot["hw_mode"] == "LIVE-HW" ? new Color(.2f, 1f, .7f) : new Color(1f, .75f, .25f);
                Quad(mesh, point, tint);
            }
        }

        private static float Number(JToken value) => float.TryParse(value?.ToString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float number) ? number : float.NaN;
        private static void Line(VertexHelper mesh, Vector2 a, Vector2 b, Color tint)
        {
            if (float.IsNaN(a.x + a.y + b.x + b.y) || float.IsInfinity(a.x + a.y + b.x + b.y) || (b - a).sqrMagnitude < .01f) return;
            Vector2 normal = new Vector2(a.y - b.y, b.x - a.x).normalized;
            int i = mesh.currentVertCount;
            mesh.AddVert(a - normal, tint, Vector2.zero); mesh.AddVert(a + normal, tint, Vector2.zero);
            mesh.AddVert(b + normal, tint, Vector2.zero); mesh.AddVert(b - normal, tint, Vector2.zero);
            mesh.AddTriangle(i, i + 1, i + 2); mesh.AddTriangle(i, i + 2, i + 3);
        }
        private static void Quad(VertexHelper mesh, Vector2 point, Color tint)
        {
            int i = mesh.currentVertCount;
            mesh.AddVert(point + new Vector2(-5, -5), tint, Vector2.zero);
            mesh.AddVert(point + new Vector2(-5, 5), tint, Vector2.zero);
            mesh.AddVert(point + new Vector2(5, 5), tint, Vector2.zero);
            mesh.AddVert(point + new Vector2(5, -5), tint, Vector2.zero);
            mesh.AddTriangle(i, i + 1, i + 2); mesh.AddTriangle(i, i + 2, i + 3);
        }
    }
}
