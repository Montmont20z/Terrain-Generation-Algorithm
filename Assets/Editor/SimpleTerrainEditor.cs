using UnityEngine;
using UnityEditor;

// This tells Unity this script is the UI for SimpleTerrain
[CustomEditor(typeof(SimpleTerrain))] 
public class SimpleTerrainEditor : Editor
{
    public override void OnInspectorGUI()
    {
        // 1. Draw all the fields you already have (Seed, Octaves, etc.)
        DrawDefaultInspector();

        // 2. Reference your actual script
        SimpleTerrain script = (SimpleTerrain)target;

        // 3. Add some spacing and the Button
        GUILayout.Space(10); 
        if (GUILayout.Button("Rebuild Terrain", GUILayout.Height(30)))
        {
            script.RebuildAllTerrain();
        }
        
        GUILayout.Space(10); 
        if (GUILayout.Button("Run Erosion", GUILayout.Height(30)))
        {
            // This calls your function!
            script.RunDropletErosionTicks();
        }
    }
}