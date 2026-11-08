// create_gameobject：创建空物体或基础几何体，支持父节点、位置设置和撤销。
using System;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Lmbox.AIAgent.Tools
{
    /// <summary>Creates a GameObject, optionally a primitive, under an optional parent path.</summary>
    public class CreateGameObjectTool : AgentTool
    {
        public override string Name => "create_gameobject";
        public override string Description =>
            "在场景中创建一个 GameObject。可指定父节点路径、基础几何体类型和位置。创建后可被撤销。";

        public override string ParametersSchemaJson => @"{
            ""type"": ""object"",
            ""properties"": {
                ""name"": { ""type"": ""string"", ""description"": ""GameObject 名称"" },
                ""parent"": { ""type"": ""string"", ""description"": ""父节点层级路径，如 'Canvas/Panel'，留空则为根节点"" },
                ""primitiveType"": { ""type"": ""string"", ""enum"": [""Cube"",""Sphere"",""Capsule"",""Cylinder"",""Plane"",""Quad""], ""description"": ""可选，创建 Unity 基础几何体"" },
                ""position"": {
                    ""type"": ""object"",
                    ""properties"": {
                        ""x"": { ""type"": ""number"" },
                        ""y"": { ""type"": ""number"" },
                        ""z"": { ""type"": ""number"" }
                    }
                }
            },
            ""required"": [""name""]
        }";

        public override Task<string> ExecuteAsync(JObject args)
        {
            var name = args?["name"]?.ToString();
            if (string.IsNullOrEmpty(name))
                throw new ArgumentException("缺少参数 name");

            GameObject go;
            var primitive = args?["primitiveType"]?.ToString();
            if (!string.IsNullOrEmpty(primitive) &&
                Enum.TryParse(primitive, true, out PrimitiveType pt))
            {
                go = GameObject.CreatePrimitive(pt);
                go.name = name;
            }
            else
            {
                go = new GameObject(name);
            }

            var parentPath = args?["parent"]?.ToString();
            if (!string.IsNullOrEmpty(parentPath))
            {
                var parent = ToolUtil.FindByPath(parentPath);
                if (parent == null)
                {
                    UnityEngine.Object.DestroyImmediate(go);
                    throw new ArgumentException($"找不到父节点: {parentPath}");
                }
                go.transform.SetParent(parent, false);
            }

            var pos = args?["position"] as JObject;
            if (pos != null)
            {
                go.transform.position = new Vector3(
                    pos["x"]?.Value<float>() ?? 0f,
                    pos["y"]?.Value<float>() ?? 0f,
                    pos["z"]?.Value<float>() ?? 0f);
            }

            Undo.RegisterCreatedObjectUndo(go, "AI Create GameObject");
            EditorSceneManager.MarkSceneDirty(go.scene);
            Selection.activeGameObject = go;

            return Task.FromResult($"已创建: {ToolUtil.GetPath(go.transform)}");
        }
    }

    internal static class ToolUtil
    {
        public static string GetPath(Transform t)
        {
            var sb = new StringBuilder(t.name);
            while (t.parent != null)
            {
                t = t.parent;
                sb.Insert(0, t.name + "/");
            }
            return sb.ToString();
        }

        public static Transform FindByPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            var go = GameObject.Find(path);
            return go != null ? go.transform : null;
        }
    }
}
