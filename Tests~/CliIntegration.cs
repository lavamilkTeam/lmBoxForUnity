using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Temporary test entry, loaded only by run-cli-integration.mjs in the existing demo.
public static class LmuCliIntegration
{
    private const string Root="Assets/LmuCliIntegration";
    private static string[] _before;
    private static DateTime _deadline;
    public static void Run()
    {
        try
        {
            _before=AssetDatabase.FindAssets("",new[]{"Assets"}).Select(AssetDatabase.GUIDToAssetPath).ToArray();
            if(AssetDatabase.IsValidFolder(Root))throw new Exception("Fixture path already exists");
            AssetDatabase.CreateFolder("Assets","LmuCliIntegration");
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            int checks=0;
            var cancelled = JObject.Parse(new Lmbox.AIAgent.Tools.CliTool().ExecuteAsync(new JObject { ["request"] = new JObject { ["version"]=1,["operations"]=new JArray(new JObject{["op"]="objects.add",["name"]="CancelledBatchFirst"},new JObject{["op"]="objects.add",["name"]="CancelledBatchSecond"}) }.ToString() },()=>++checks>1).GetAwaiter().GetResult());
            if(cancelled.Value<bool>("ok") || GameObject.Find("CancelledBatchFirst")==null || GameObject.Find("CancelledBatchSecond")!=null)throw new Exception("Batch cancellation did not stop remaining operations");
            UnityEngine.Object.DestroyImmediate(GameObject.Find("CancelledBatchFirst"));
            for(int i=0;i<2;i++)
            {
                var texture=new Texture2D(2,2);texture.SetPixels(Enumerable.Repeat(i==0?Color.green:Color.red,4).ToArray());texture.Apply();
                File.WriteAllBytes(Root+"/texture"+i+".png",texture.EncodeToPNG());UnityEngine.Object.DestroyImmediate(texture);AssetDatabase.ImportAsset(Root+"/texture"+i+".png");
            }
            var cube=GameObject.CreatePrimitive(PrimitiveType.Cube);cube.name="FixturePrefab";PrefabUtility.SaveAsPrefabAsset(cube,Root+"/Cube.prefab");UnityEngine.Object.DestroyImmediate(cube);
            var settings=new LightingSettings{bakedGI=true,realtimeGI=false,lightmapResolution=1,lightmapMaxSize=32};Lightmapping.lightingSettings=settings;
            EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene(),Root+"/Fixture.unity");
            _deadline=DateTime.UtcNow.AddMinutes(8);
            EditorApplication.update+=Tick;
            File.WriteAllText("Library/LmuCliIntegration.ready","ready");
        }
        catch(Exception e){Debug.LogException(e);EditorApplication.Exit(1);}
    }
    private static void Tick()
    {
        if(DateTime.UtcNow>_deadline){Finish(false,"timeout");return;}
        string path="Library/LmuCliIntegration.stop";if(!File.Exists(path))return;
        bool success=File.ReadAllText(path)=="ok";
        try
        {
            if(success)
            {
                var terrain=GameObject.Find("CliTerrain").GetComponent<Terrain>();
                var weights=terrain.terrainData.GetAlphamaps(0,0,1,1);
                if(Mathf.Abs(weights[0,0,1]-1)>0.001f)throw new Exception("Paint weights differ");
                if(Mathf.Abs(terrain.terrainData.GetHeight(0,0)-2)>0.01f)throw new Exception("Height differs");
                if(GameObject.Find("Placed-1")==null||GameObject.Find("Placed-2")==null)throw new Exception("Prefab placement failed");
                if(GameObject.Find("CliLamp").GetComponent<Light>().intensity!=2)throw new Exception("Light settings differ");
                if(GameObject.Find("CliCube").GetComponent<BoxCollider>().isTrigger!=true)throw new Exception("Component property differs");
                if(File.Exists(Root+"/Denied.txt"))throw new Exception("Headless confirmation bypassed");
                if(terrain.terrainData.detailPrototypes.Length!=1||terrain.terrainData.GetDetailLayer(0,0,1,1,0)[0,0]!=2)throw new Exception("Grass density differs");
                if(terrain.terrainData.treeInstances.Length!=1)throw new Exception("Tree placement failed");
            }
            Finish(success,success?"passed":"runner failed");
        }
        catch(Exception e){Finish(false,e.ToString());}
    }
    private static void Finish(bool success,string message)
    {
        EditorApplication.update-=Tick;
        if(Lightmapping.isRunning)Lightmapping.Cancel();
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
        // Clean only assets created by this fixture/run, leave all pre-existing project files intact.
        var created=AssetDatabase.FindAssets("",new[]{"Assets"}).Select(AssetDatabase.GUIDToAssetPath).Where(p=>!_before.Contains(p)).OrderByDescending(p=>p.Length).ToArray();
        foreach(var p in created)if(p.StartsWith(Root+"/")||p==Root||p.StartsWith("Assets/LmBoxGenerated/"))AssetDatabase.DeleteAsset(p);
        File.WriteAllText("Library/LmuCliIntegration.result.json",new JObject{["ok"]=success,["message"]=message}.ToString());
        Debug.Log("LMU_CLI_INTEGRATION: "+message);EditorApplication.Exit(success?0:1);
    }
}
