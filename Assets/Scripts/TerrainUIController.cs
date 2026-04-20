using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class TerrainUIController : MonoBehaviour
{
    [Header("Reference")]
    public SimpleTerrain terrainScript;

    // === MENU PANEL ===
    [Header("Menu Panel")]
    public RectTransform menuPanel;
    public Button toggleMenuButton;
    public GameObject menuBody;

    // === DROPDOWN TOGGLE BUTTONS ===
    [Header("Dropdown Toggle Buttons")]
    public Button terrainDropdownHeader;
    public Button riverDropdownHeader;
    public Button erosionDropdownHeader;

    // === DROPDOWN CONTENT BODIES ===
    [Header("Dropdown Content Bodies")]
    public GameObject terrainContent;
    public GameObject riverContent;
    public GameObject erosionContent;

    // === PERLIN ===
    [Header("Terrain Fields")]
    public TMP_InputField seedField;
    public TMP_InputField scaleField;
    public TMP_InputField octavesField;      // replaces octavesSlider + octavesText
    public TMP_InputField persistenceField;
    public TMP_InputField lacunarityField;
    public TMP_InputField heightScaleField;
    public TMP_InputField meshSizeField;

    // === RIVER ===
    [Header("River Fields")]
    public TMP_InputField riverThresholdField;
    public TMP_InputField riverWidthBaseField;
    public TMP_InputField riverWidthScaleField;
    public TMP_InputField riverCarveDepthMaxField;
    public TMP_InputField riverMaxTerrainHeightField;
    public TMP_InputField globalWaterLevelField;

    // === LAKES ===
    [Header("Lake Fields")]
    public TMP_InputField lakeWidthMultiplierField;
    public TMP_InputField lakeMaxHeightField;
    public TMP_InputField lakeFillAmountField;

    // === EROSION ===
    [Header("Erosion Fields")]
    public TMP_InputField rainRateField;
    public TMP_InputField simulationTicksField;
    public TMP_InputField erosionConstantField;
    public TMP_InputField depositionConstantField;
    public TMP_InputField capacityConstantField;
    public TMP_InputField evaporationRateField;
    public TMP_InputField inertiaField;
    public TMP_InputField dropletStepSizeField;
    public TMP_InputField maxLifetimeField;

    // === ACTION BUTTONS ===
    [Header("Buttons")]
    public Button rebuildButton;
    public Button erosionButton;

    // === INTERNAL STATE ===
    bool menuOpen = false;
    bool terrainOpen = false;
    bool riverOpen = false;
    bool erosionOpen = false;

    void Start()
    {
        // Start with body hidden
        menuBody.SetActive(menuOpen);

        // All dropdowns start collapsed
        terrainContent.SetActive(terrainOpen);
        riverContent.SetActive(riverOpen);
        erosionContent.SetActive(erosionOpen);

        // Hook up menu toggle
        toggleMenuButton.onClick.AddListener(ToggleMenu);

        // Hook up dropdown toggles
        terrainDropdownHeader.onClick.AddListener(ToggleTerrainDropdown);
        riverDropdownHeader.onClick.AddListener(ToggleRiverDropdown);
        erosionDropdownHeader.onClick.AddListener(ToggleErosionDropdown);

        // Populate fields
        seedField.text = terrainScript.seed.ToString();
        scaleField.text = terrainScript.scale.ToString();
        octavesField.text = terrainScript.octaves.ToString();
        persistenceField.text = terrainScript.persistence.ToString();
        lacunarityField.text = terrainScript.lacunarity.ToString();
        heightScaleField.text = terrainScript.heightScale.ToString();
        meshSizeField.text = terrainScript.meshSize.ToString();

        riverThresholdField.text = terrainScript.riverThreshold.ToString();
        riverWidthBaseField.text = terrainScript.riverWidthBase.ToString();
        riverWidthScaleField.text = terrainScript.riverWidthScale.ToString();
        riverCarveDepthMaxField.text = terrainScript.riverCarveDepthMax.ToString();
        riverMaxTerrainHeightField.text = terrainScript.riverMaxTerrainHeight.ToString();
        globalWaterLevelField.text = terrainScript.globalWaterLevel.ToString();

        lakeWidthMultiplierField.text = terrainScript.lakeWidthMultiplier.ToString();
        lakeMaxHeightField.text = terrainScript.lakeMaxHeight.ToString();
        lakeFillAmountField.text = terrainScript.lakeFillAmount.ToString();

        rainRateField.text = terrainScript.rainRate.ToString();
        simulationTicksField.text = terrainScript.simulationTicks.ToString();
        erosionConstantField.text = terrainScript.erosionConstant.ToString();
        depositionConstantField.text = terrainScript.depositionConstant.ToString();
        capacityConstantField.text = terrainScript.capacityConstant.ToString();
        evaporationRateField.text = terrainScript.evaporationRate.ToString();
        inertiaField.text = terrainScript.inertia.ToString();
        dropletStepSizeField.text = terrainScript.dropletStepSize.ToString();
        maxLifetimeField.text = terrainScript.maxLifetime.ToString();

        // Action buttons
        rebuildButton.onClick.AddListener(OnRebuildClicked);
        erosionButton.onClick.AddListener(OnErosionClicked);
    }

    // =========================================================
    //  MENU TOGGLE
    // =========================================================

    void ToggleMenu()
    {
        menuOpen = !menuOpen;
        menuBody.SetActive(menuOpen);

        if (!menuOpen)
        {
            terrainOpen = false;
            riverOpen = false;
            erosionOpen = false;
            terrainContent.SetActive(false);
            riverContent.SetActive(false);
            erosionContent.SetActive(false);
        }
    }

    // =========================================================
    //  DROPDOWN TOGGLES
    // =========================================================

    void ToggleTerrainDropdown()
    {
        terrainOpen = !terrainOpen;
        terrainContent.SetActive(terrainOpen);
    }

    void ToggleRiverDropdown()
    {
        riverOpen = !riverOpen;
        riverContent.SetActive(riverOpen);
    }

    void ToggleErosionDropdown()
    {
        erosionOpen = !erosionOpen;
        erosionContent.SetActive(erosionOpen);
    }

    // =========================================================
    //  ACTION BUTTONS
    // =========================================================

    void OnRebuildClicked()
    {
        ApplyUIToTerrain();
        terrainScript.RebuildAllTerrain();
    }

    void OnErosionClicked()
    {
        ApplyUIToTerrain();
        terrainScript.RunDropletErosionTicks();
    }

    void ApplyUIToTerrain()
    {
        // Terrain / Perlin
        if (int.TryParse(seedField.text, out int seed))
            terrainScript.seed = seed;

        if (float.TryParse(scaleField.text, out float scale))
            terrainScript.scale = Mathf.Max(0.01f, scale);

        if (int.TryParse(octavesField.text, out int octaves))
            terrainScript.octaves = Mathf.Clamp(octaves, 1, 8);

        if (float.TryParse(persistenceField.text, out float persistence))
            terrainScript.persistence = Mathf.Clamp01(persistence);

        if (float.TryParse(lacunarityField.text, out float lacunarity))
            terrainScript.lacunarity = Mathf.Max(1f, lacunarity);

        if (float.TryParse(heightScaleField.text, out float heightScale))
            terrainScript.heightScale = Mathf.Max(0f, heightScale);

        if (int.TryParse(meshSizeField.text, out int meshSize))
            terrainScript.meshSize = Mathf.Max(2, meshSize);

        // River
        if (int.TryParse(riverThresholdField.text, out int riverThreshold))
            terrainScript.riverThreshold = Mathf.Max(0, riverThreshold);

        if (float.TryParse(riverWidthBaseField.text, out float riverWidthBase))
            terrainScript.riverWidthBase = Mathf.Max(0f, riverWidthBase);

        if (float.TryParse(riverWidthScaleField.text, out float riverWidthScale))
            terrainScript.riverWidthScale = Mathf.Max(0f, riverWidthScale);

        if (float.TryParse(riverCarveDepthMaxField.text, out float riverCarveDepthMax))
            terrainScript.riverCarveDepthMax = Mathf.Max(0f, riverCarveDepthMax);

        if (float.TryParse(riverMaxTerrainHeightField.text, out float riverMaxTerrainHeight))
            terrainScript.riverMaxTerrainHeight = Mathf.Max(0f, riverMaxTerrainHeight);

        if (float.TryParse(globalWaterLevelField.text, out float globalWaterLevel))
            terrainScript.globalWaterLevel = globalWaterLevel;

        // Lakes
        if (float.TryParse(lakeWidthMultiplierField.text, out float lakeWidthMultiplier))
            terrainScript.lakeWidthMultiplier = Mathf.Max(0f, lakeWidthMultiplier);

        if (float.TryParse(lakeMaxHeightField.text, out float lakeMaxHeight))
            terrainScript.lakeMaxHeight = Mathf.Max(0f, lakeMaxHeight);

        if (float.TryParse(lakeFillAmountField.text, out float lakeFillAmount))
            terrainScript.lakeFillAmount = Mathf.Clamp01(lakeFillAmount);

        // Erosion
        if (int.TryParse(rainRateField.text, out int rainRate))
            terrainScript.rainRate = Mathf.Max(0, rainRate);

        if (int.TryParse(simulationTicksField.text, out int simulationTicks))
            terrainScript.simulationTicks = Mathf.Max(1, simulationTicks);

        if (float.TryParse(erosionConstantField.text, out float erosionConstant))
            terrainScript.erosionConstant = Mathf.Max(0f, erosionConstant);

        if (float.TryParse(depositionConstantField.text, out float depositionConstant))
            terrainScript.depositionConstant = Mathf.Max(0f, depositionConstant);

        if (float.TryParse(capacityConstantField.text, out float capacityConstant))
            terrainScript.capacityConstant = Mathf.Max(0f, capacityConstant);

        if (float.TryParse(evaporationRateField.text, out float evaporationRate))
            terrainScript.evaporationRate = Mathf.Clamp01(evaporationRate);

        if (float.TryParse(inertiaField.text, out float inertia))
            terrainScript.inertia = Mathf.Clamp01(inertia);

        if (float.TryParse(dropletStepSizeField.text, out float dropletStepSize))
            terrainScript.dropletStepSize = Mathf.Max(0.01f, dropletStepSize);

        if (int.TryParse(maxLifetimeField.text, out int maxLifetime))
            terrainScript.maxLifetime = Mathf.Max(1, maxLifetime);
    }
}