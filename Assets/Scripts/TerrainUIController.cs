using UnityEngine;
using UnityEngine.UI; // Required for Button, InputField, etc.
using TMPro;           // Recommended if using TextMeshPro

public class TerrainUIController : MonoBehaviour
{
    [Header("References")]
    public SimpleTerrain terrainScript;

    [Header("Perlin Inputs")]
    public TMP_InputField seedField;
    public TMP_InputField scaleField;

    [Header("Buttons")]
    public Button rebuildButton;
    public Button erosionButton;

    void Start()
    {
        // 1. Initialize UI with current values from the script
        // seedField.text = terrainScript.seed.ToString();
        // scaleField.text = terrainScript.scale.ToString();

        // // 2. Setup Listeners for Fields (Updates data when you type)
        // seedField.onEndEdit.AddListener(val => {
        //     if (int.TryParse(val, out int result)) terrainScript.seed = result;
        // });

        // scaleField.onEndEdit.AddListener(val => {
        //     if (float.TryParse(val, out float result)) terrainScript.scale = result;
        // });

        // 3. Setup Button Clicks
        rebuildButton.onClick.AddListener(() => terrainScript.RebuildAllTerrain());
        erosionButton.onClick.AddListener(() => terrainScript.RunDropletErosionTicks());
    }
}