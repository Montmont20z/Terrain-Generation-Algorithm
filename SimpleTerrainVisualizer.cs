using UnityEngine;
using System.Collections;

public class SimpleTerrainVisualizer : MonoBehaviour
{
    [Header("Terrain / Perlin")]
    public int width = 128;
    public int height = 128;
    public float scale = 30f;
    public int octaves = 4;
    public float persistence = 0.5f;
    public float lacunarity = 2f;

    [Header("Erosion")]
    public int erosionSteps = 500;
    public float rainAmount = 0.01f;
    public float moveFraction = 0.5f;
    public float erosionRate = 0.02f;
    public float evaporation = 0.02f;

    [Header("Visualization")]
    public int updateEverySteps = 10; // update texture every N steps
    public bool autoPositionCamera = true;
    public bool saveFrames = false;     // saves pngs to persistentDataPath (can be slow)
    public string framePrefix = "frame_";

    float[,] terrain;
    float[,] water;
    float[,] sediment;

    Texture2D tex;
    Renderer quadRenderer;
    int currentStep = 0;

    void Start()
    {
        // allocate arrays
        terrain = new float[width, height];
        water = new float[width, height];
        sediment = new float[width, height];

        // create visual quad and texture
        CreateVisualQuad();

        // generate heightmap
        GeneratePerlinBase();

        // initial draw
        UpdateTextureAndApply();

        // reposition camera if desired
        if (autoPositionCamera && Camera.main != null)
        {
            Camera.main.orthographic = true;
            Camera.main.orthographicSize = Mathf.Max(width, height) * 0.6f;
            Camera.main.transform.position = new Vector3(width / 2f - 0.5f, Mathf.Max(width, height), height / 2f - 0.5f);
            Camera.main.transform.rotation = Quaternion.Euler(90f, 0f, 0f); // look straight down
        }

        // start erosion
        StartCoroutine(RunErosionCoroutine());
    }

    void CreateVisualQuad()
    {
        // Create texture
        tex = new Texture2D(width, height, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Point; // crisp pixels
        tex.wrapMode = TextureWrapMode.Clamp;

        // Create a quad GameObject centered on the map
        GameObject quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        quad.transform.localScale = new Vector3(width, height, 1f);
        quad.transform.position = new Vector3(width / 2f - 0.5f, 0f, height / 2f - 0.5f);
        quad.transform.rotation = Quaternion.Euler(90f, 0f, 0f); // lay flat so we see top-down

        // Make an unlit material and assign the texture
        Material mat = new Material(Shader.Find("Unlit/Texture"));
        mat.mainTexture = tex;
        quadRenderer = quad.GetComponent<Renderer>();
        quadRenderer.sharedMaterial = mat;
    }

    void GeneratePerlinBase()
    {
        System.Random prng = new System.Random(12345);
        float offsetX = prng.Next(-10000, 10000);
        float offsetY = prng.Next(-10000, 10000);

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                float amplitude = 1f;
                float frequency = 1f;
                float noiseHeight = 0f;

                for (int o = 0; o < octaves; o++)
                {
                    float sampleX = (x + offsetX) / scale * frequency;
                    float sampleY = (y + offsetY) / scale * frequency;
                    float perlin = Mathf.PerlinNoise(sampleX, sampleY) * 2f - 1f;
                    noiseHeight += perlin * amplitude;

                    amplitude *= persistence;
                    frequency *= lacunarity;
                }

                terrain[x, y] = Mathf.InverseLerp(-1f, 1f, noiseHeight);
            }
        }
    }

    IEnumerator RunErosionCoroutine()
    {
        for (currentStep = 0; currentStep < erosionSteps; currentStep++)
        {
            RunSimpleErosionStep();

            if (currentStep % updateEverySteps == 0)
            {
                UpdateTextureAndApply();
                if (saveFrames)
                {
                    SaveFrame(currentStep);
                }
            }

            // Don't freeze the editor/play mode - yield occasionally
            if (currentStep % 10 == 0) yield return null;
        }

        // final update
        UpdateTextureAndApply();
        Debug.Log("Erosion finished at step " + currentStep);
    }

    void RunSimpleErosionStep()
    {
        int[] dx = { 1, -1, 0, 0 };
        int[] dy = { 0, 0, 1, -1 };

        // 1) rain
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
                water[x, y] += rainAmount;

        // 2) flow
        float[,] newWater = new float[width, height];
        float[,] newSed = new float[width, height];

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int bestNx = x, bestNy = y;
                float bestH = terrain[x, y] + water[x, y];

                for (int k = 0; k < 4; k++)
                {
                    int nx = x + dx[k], ny = y + dy[k];
                    if (nx < 0 || nx >= width || ny < 0 || ny >= height) continue;
                    float h = terrain[nx, ny] + water[nx, ny];
                    if (h < bestH)
                    {
                        bestH = h;
                        bestNx = nx; bestNy = ny;
                    }
                }

                if (bestNx == x && bestNy == y)
                {
                    newWater[x, y] += water[x, y];
                    newSed[x, y] += sediment[x, y];
                }
                else
                {
                    float move = water[x, y] * moveFraction;
                    float remain = water[x, y] - move;

                    float carriedSedMove = sediment[x, y] * (move / (water[x, y] + 1e-6f));
                    float carriedSedRemain = sediment[x, y] * (remain / (water[x, y] + 1e-6f));

                    newWater[bestNx, bestNy] += move;
                    newSed[bestNx, bestNy] += carriedSedMove;

                    newWater[x, y] += remain;
                    newSed[x, y] += carriedSedRemain;

                    float slope = (terrain[x, y] + water[x, y]) - (terrain[bestNx, bestNy] + water[bestNx, bestNy]);
                    if (slope > 0f)
                    {
                        float erode = Mathf.Min(terrain[x, y], erosionRate * slope * (move + 1e-6f));
                        terrain[x, y] -= erode;
                        newSed[bestNx, bestNy] += erode;
                    }
                }
            }
        }

        // 3) evaporation + deposit
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                water[x, y] = newWater[x, y] * (1f - evaporation);
                sediment[x, y] = newSed[x, y];

                if (water[x, y] < 1e-4f && sediment[x, y] > 0f)
                {
                    float d = Mathf.Min(sediment[x, y], 0.01f);
                    terrain[x, y] += d;
                    sediment[x, y] -= d;
                }
            }
        }
    }

    void UpdateTextureAndApply()
    {
        // Create color array once per update
        Color[] cols = new Color[width * height];

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                float h = Mathf.Clamp01(terrain[x, y]);
                // choose a simple coloring: darker = low, lighter = high
                Color c = new Color(h, h, h, 1f);
                cols[y * width + x] = c;
            }
        }

        tex.SetPixels(cols);
        tex.Apply();
    }

    void SaveFrame(int step)
    {
        byte[] bytes = tex.EncodeToPNG();
        string fileName = $"{framePrefix}{step.ToString("D05")}.png";
        string path = System.IO.Path.Combine(Application.persistentDataPath, fileName);
        System.IO.File.WriteAllBytes(path, bytes);
        Debug.Log("Saved frame to: " + path);
    }
}
