using System;
using Unity.VisualScripting;
using UnityEngine;

[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class SimpleTerrain : MonoBehaviour
{
    [Header("Perlin input")]
    public float positionX = 0f;
    public float positionY = 0f;
    public float scale = 1f;
    public int octaves = 1;
    public float persistence = 0.5f;
    public float lacunarity = 2.0f;
    public int seed = 1;

    [Header("Mesh")]
    public int meshSize = 50; // grid resolution (meshSize x meshSize quads)
    public float heightScale = 5f; // how tall the hills are
    
    [Header("River / Lake (noise mask carving)")]
    [Tooltip("Only carve rivers below this terrain height (avoid mountain crests)")]


    public float riverMaxTerrainHeight = 3.5f;
    [Tooltip("Distance-from-center threshold in 0..0.5. Typical: 0.02")]

    public float riverWidth = 0.02f; // 0..1
    public float riverDepth = 2.0f;  // max depth carved at river center
    [Tooltip("Low values = longer, smoother rivers. Typical: 0.01 - 0.05")]

    public float riverScale = 0.05f;

    
    
    [Header("Lakes")]
    [Tooltip("Multiply riverWidth to produce broader basins that become lakes if low-lying")]
    public float lakeWidthMultiplier = 3.0f; // lake width depends on river width // ensure > 1.0
    public float lakeMaxHeight = 2.5f;
    [Tooltip("If true, lakes will fill slightly shallower than surrounding to avoid overflow")]
    public float lakeFillAmount = 0.5f;

    public float globalWaterLevel = 1.0f;


    Mesh mesh;
    Vector3[] vertices;
    // offsetX & Y to avoid weird pattern when perlin noise near 0
    float offsetX; 
    float offsetY;
    // water
    float[] lastWater;              // per-vertex final water amount after hydraulic erosion
    GameObject waterObject;         // child object for water mesh


    private void Start()
    {
        // create and store the mesh in the field so other methods can use it
        mesh = new Mesh { name = "PerlinMesh" };

        // assign the mesh to the MeshFilter
        MeshFilter mf = GetComponent<MeshFilter>();
        mf.sharedMesh = mesh;

        CreateGridMesh();
        // Simple material so you can see shading
        var mr = GetComponent<MeshRenderer>();
        if (mr.sharedMaterial == null)
            mr.sharedMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit"));

        // set seeded random offsets to avoid sampling integer lattice points
        System.Random rng = new System.Random(seed);
        offsetX = rng.Next(-100000, 100000);
        offsetY = rng.Next(-100000, 100000);
        
        ApplyPerlinToVertices();
        ApplyRiverAndLakes();
            // upload to mesh so erosion sees correct heights
        mesh.vertices = vertices;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();


        // after ApplyPerlinToVertices() and RunThermalErosion(...)
/*         RunHydraulicErosion(
            iterations: 1000,      // more iterations = deeper channels
            rainPerIt: 0.01f,     // small rain per step
            evaporation: 0.02f,   // fraction of water lost per iteration (0..1)
            capacityFactor: 4.0f, // how much sediment water can carry: tune
            depositSpeed: 0.5f,   // how fast sediment deposits (0..1)
            erodeSpeed: 0.5f      // how fast terrain is eroded (0..1)
        ) */;
        
            // after erosion, update mesh
        mesh.vertices = vertices;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        
        CreateWaterOverlay(threshold: 0.02f, raise: 0.02f, color: new Color(0f, 0.45f, 0.85f, 0.6f));

    }

    private void CreateGridMesh()
    {
        int vertsPerRow = meshSize + 1;
        vertices = new Vector3[vertsPerRow * vertsPerRow]; // create vertices array with vertsPerRow^2 vertices
        Vector2[] uvs = new Vector2[vertices.Length];
        int[] tris = new int[meshSize * meshSize * 6]; // traingle index array (2 triangles per quad, 3 indices per triangle)

        // build vertices and uvs
        for (int y = 0; y <= meshSize; y++)
        {
            for (int x = 0; x <= meshSize; x++)
            {
                int i = y * vertsPerRow + x; // current vertex index
                vertices[i] = new Vector3(x, 0f, y); // flat grid in XZ plane
                uvs[i] = new Vector2((float)x / meshSize, (float)y / meshSize); // convert 0..meshSize to 0..1
            }
        }

        // build triangles
        int triangleIndex = 0;
        for (int y = 0; y < meshSize; y++)
        {
            for (int x = 0; x < meshSize; x++)
            {
                int i = y * vertsPerRow + x; // current vertex index
                // create a quad using two triangles
                // traingle 1
                tris[triangleIndex++] = i;
                tris[triangleIndex++] = i + vertsPerRow;
                tris[triangleIndex++] = i + 1;

                // traingle 2 
                tris[triangleIndex++] = i + 1;
                tris[triangleIndex++] = i + vertsPerRow;
                tris[triangleIndex++] = i + vertsPerRow + 1;
            }
        }

        // assign to mesh (clear then set)
        mesh.Clear();
        mesh.vertices = vertices;
        mesh.triangles = tris;
        mesh.uv = uvs;
        mesh.MarkDynamic();
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
    }

    private void Update()
    {
        // Update vertex heights from Perlin noise each frame (cheap for small meshes)
        ApplyPerlinToVertices();
// ;
        mesh.vertices = vertices;
        mesh.RecalculateNormals();

    }

    void ApplyPerlinToVertices()
    {
        int vertsPerRow = meshSize + 1;

        for (int y = 0; y <= meshSize; y++)
        {
            for (int x = 0; x <= meshSize; x++)
            {
                int i = y * vertsPerRow + x;

                // sample using world-space position + the user offsets
                float sampleX = (x + positionX + offsetX)  / meshSize / scale; // divide by meshSize to constrain to 0..1
                float sampleY = (y + positionY + offsetY)  / meshSize / scale;

                // for multi-octave Perlin
                float noise = 0f;
                float amplitude = 1f;
                float frequency = 1f;
                float maxAmplitude = 0f;
                // iterate over octaves
                for (int o = 0; o < Mathf.Max(1, octaves); o++)
                {
                    float v = Mathf.PerlinNoise(sampleX * frequency, sampleY * frequency) * 2f - 1f; // now -1..1
                    noise += v * amplitude;

                    maxAmplitude += amplitude;
                    amplitude *= persistence; // reduce amplitude for each octave
                    frequency *= lacunarity; // increase frequency for each octave
                }

                // normalize (roughly) from -1..1 to 0..1 and scale height
                float h = Mathf.InverseLerp(-1f, 1f, noise); // convert -1..1 to 0..1
                vertices[i].y = h * heightScale;
            }
        }
    }
    
    
void ApplyRiverAndLakes()
{
    int rows = meshSize + 1;
    lastWater = new float[rows * rows];

    // normalize offsets so Perlin sampling is stable
    float ox = offsetX / meshSize;
    float oy = offsetY / meshSize;

    // defensive: ensure riverScale is positive and sane
    float effectiveRiverScale = Mathf.Max(1e-6f, riverScale);

    int carvedCount = 0;

    // Precompute effective global water
    float effectiveGlobalWaterLevel = globalWaterLevel;
    if (effectiveGlobalWaterLevel <= 0f)
        effectiveGlobalWaterLevel = heightScale * 0.1f;

    for (int y = 0; y <= meshSize; y++)
    {
        for (int x = 0; x <= meshSize; x++)
        {
            int i = y * rows + x;

            // Normalized sample coordinates (0..1) scaled by river frequency.
            // Using normalized coords keeps sampling consistent with ApplyPerlinToVertices.
            float nx = (x + positionX) / (float)meshSize + ox;
            float ny = (y + positionY) / (float)meshSize + oy;

            // scale normalized coordinates by riverScale (small values -> long smooth rivers)
            float sampleXR = nx * effectiveRiverScale;
            float sampleYR = ny * effectiveRiverScale;

            float riverNoise = Mathf.PerlinNoise(sampleXR, sampleYR); // 0..1
            float river = Mathf.Abs(riverNoise - 0.5f); // distance-from-center (0..0.5)

            // carve riverbed if within width and below mountain threshold
            if (river < riverWidth && vertices[i].y < riverMaxTerrainHeight)
            {
                float t = river / Mathf.Max(1e-6f, riverWidth); // 0..1
                float depth = riverDepth * (1f - t); // deepest at center
                vertices[i].y = Mathf.Max(0f, vertices[i].y - depth);
                carvedCount++;
            }

            // simple lakes: wider band + low terrain
            float lakeBand = riverWidth * Mathf.Max(1f, lakeWidthMultiplier);
            if (river < lakeBand && vertices[i].y <= lakeMaxHeight)
            {
                // push down a bit to create basin (adjust multiplier to taste)
                vertices[i].y = vertices[i].y - (lakeFillAmount * 0.1f);
            }

            // Build water mask: global water level OR river centers
            float w = Mathf.Clamp01((effectiveGlobalWaterLevel - vertices[i].y) / Mathf.Max(1e-6f, effectiveGlobalWaterLevel));
            if (river < riverWidth)
                w = Mathf.Max(w, 1f);

            lastWater[i] = w;
        }
    }

    // optional: smooth water mask a little so overlay isn't noisy
    // SmoothWaterMask(iterations: 2);

    Debug.Log($"ApplyRiverAndLakes: carved vertices = {carvedCount}/{rows * rows} (riverWidth={riverWidth}, riverScale={riverScale})");
}

    
    // Minimal hydraulic erosion (grid-based)
    void RunHydraulicErosion(int iterations, float rainPerIt, float evaporation, float capacityFactor, float depositSpeed, float erodeSpeed)
    {
        int rows = meshSize + 1;
        // copy heights to working 2D array
        float[,] h = new float[rows, rows];
        for (int y = 0; y < rows; y++)
            for (int x = 0; x < rows; x++)
                h[y, x] = vertices[y * rows + x].y;

        float[,] water = new float[rows, rows];
        float[,] sediment = new float[rows, rows];

        int[] ox = { 0, 1, 0, -1 };
        int[] oy = { 1, 0, -1, 0 };
        
        for (int it = 0; it < iterations; it++)
        {
            // 1) rain
            for (int y = 0; y < rows; y++)
                for (int x = 0; x < rows; x++)
                    water[y, x] += rainPerIt;

            // prepare flows arrays
            float[,] waterDelta = new float[rows, rows]; // net change of water due to flows (in - out)
            float[,] sedimentDelta = new float[rows, rows];

            // 2) flow calculation and transport
            for (int y = 0; y < rows; y++)
            {
                for (int x = 0; x < rows; x++)
                {
                    float currentHeight = h[y, x] + water[y, x]; // surface height (terrain + water)
                    // compute positive drops to neighbors
                    float totalDrop = 0f;
                    float[] drop = new float[4];

                    for (int n = 0; n < 4; n++)
                    {
                        int nx = x + ox[n];
                        int ny = y + oy[n];
                        if (nx < 0 || nx >= rows || ny < 0 || ny >= rows) { drop[n] = 0f; continue; }

                        float neighSurface = h[ny, nx] + water[ny, nx];
                        float d = currentHeight - neighSurface;
                        drop[n] = Mathf.Max(0f, d);
                        totalDrop += drop[n];
                    }

                    if (totalDrop <= 0f || water[y, x] <= 0f) continue;

                    // amount of water to move out this iteration (keep a safety factor to avoid draining completely)
                    float movable = water[y, x] * 0.5f; // move up to 50% of water each step (stable)
                    for (int n = 0; n < 4; n++)
                    {
                        if (drop[n] <= 0f) continue;
                        int nx = x + ox[n];
                        int ny = y + oy[n];

                        // proportion of movable water
                        float wflow = movable * (drop[n] / totalDrop);

                        // move water
                        waterDelta[y, x] -= wflow;
                        waterDelta[ny, nx] += wflow;

                        // move sediment proportionally with the water (avoid div by zero)
                        float sedimentMove = 0f;
                        if (water[y, x] > 0f)
                            sedimentMove = sediment[y, x] * (wflow / water[y, x]);

                        sedimentDelta[y, x] -= sedimentMove;
                        sedimentDelta[ny, nx] += sedimentMove;
                    }
                }
            }

            // apply water/sediment flow deltas
            for (int y = 0; y < rows; y++)
            {
                for (int x = 0; x < rows; x++)
                {
                    water[y, x] += waterDelta[y, x];
                    sediment[y, x] += sedimentDelta[y, x];
                    // clamp
                    if (water[y, x] < 0f) water[y, x] = 0f;
                    if (sediment[y, x] < 0f) sediment[y, x] = 0f;
                }
            }

            // 3) erosion / deposition
            for (int y = 0; y < rows; y++)
            {
                for (int x = 0; x < rows; x++)
                {
                    // compute local slope: difference between cell height and lowest neighbour surface
                    float surface = h[y, x] + water[y, x];
                    float minNeighbourSurface = surface;
                    for (int n = 0; n < 4; n++)
                    {
                        int nx = x + ox[n];
                        int ny = y + oy[n];
                        if (nx < 0 || nx >= rows || ny < 0 || ny >= rows) continue;
                        float ns = h[ny, nx] + water[ny, nx];
                        if (ns < minNeighbourSurface) minNeighbourSurface = ns;
                    }
                    float slope = Mathf.Max(0f, surface - minNeighbourSurface);

                    // transport capacity (how much sediment water can carry)
                    float capacity = capacityFactor * slope * water[y, x];

                    if (sediment[y, x] > capacity)
                    {
                        // deposit some sediment
                        float deposit = (sediment[y, x] - capacity) * depositSpeed;
                        sediment[y, x] -= deposit;
                        h[y, x] += deposit;
                    }
                    else
                    {
                        // erode terrain up to (capacity - sediment)
                        float amountToErode = Mathf.Min((capacity - sediment[y, x]) * erodeSpeed, h[y, x]);
                        if (amountToErode > 0f)
                        {
                            h[y, x] -= amountToErode;
                            sediment[y, x] += amountToErode;
                        }
                    }
                }
            }

            // 4) evaporation
            for (int y = 0; y < rows; y++)
                for (int x = 0; x < rows; x++)
                {
                    water[y, x] *= Mathf.Max(0f, 1f - evaporation);
                }
        } // end iterations
        
        // copy final water to lastWater so overlay can use it
        lastWater = new float[rows * rows];
        float maxW = 0f;
        for (int y = 0; y < rows; y++)
        {
            for (int x = 0; x < rows; x++)
            {
                float w = water[y, x];
                lastWater[y * rows + x] = w;
                if (w > maxW) maxW = w;
            }
        }

        Debug.Log($"Hydraulic finished. max water per-vertex = {maxW}");

        // write back heights
        for (int y = 0; y < rows; y++)
            for (int x = 0; x < rows; x++)
                vertices[y * rows + x].y = h[y, x];
    }
    
    /// <summary>
    /// Build a simple water overlay mesh from lastWater. Triangles whose average vertex water
    /// exceeds threshold become water triangles. Water mesh is slightly raised to avoid z-fight.
    /// </summary>
    public void CreateWaterOverlay(float threshold = 0.02f, float raise = 0.02f, Color? color = null)
    {
        if (lastWater == null || lastWater.Length == 0) return;

        // cleanup previous
        if (waterObject != null) DestroyImmediate(waterObject);

        int rows = meshSize + 1;
        float raiseAmount = raise;
        Color waterColor = color ?? new Color(0f, 0.45f, 0.85f, 0.6f);

        // find max water (avoid division by zero)
        float maxW = 0f;
        for (int i = 0; i < lastWater.Length; i++)
            if (lastWater[i] > maxW) maxW = lastWater[i];
        if (maxW <= 0f)
        {
            Debug.Log("No water to render (maxW <= 0).");
            return;
        }

        // We'll create a shared-vertex water mesh (grid-like) so normals are smooth.
        // Map original vertex index -> index in waterVerts (only created when needed).
        var indexMap = new System.Collections.Generic.Dictionary<int, int>(rows * rows);
        var waterVerts = new System.Collections.Generic.List<Vector3>();
        var waterTris = new System.Collections.Generic.List<int>();

        int vertsPerRow = rows;
        for (int y = 0; y < meshSize; y++)
        {
            for (int x = 0; x < meshSize; x++)
            {
                int i00 = y * vertsPerRow + x;
                int i01 = y * vertsPerRow + (x + 1);
                int i10 = (y + 1) * vertsPerRow + x;
                int i11 = (y + 1) * vertsPerRow + (x + 1);

                // normalized water values 0..1
                float w00 = lastWater[i00] / maxW;
                float w01 = lastWater[i01] / maxW;
                float w10 = lastWater[i10] / maxW;
                float w11 = lastWater[i11] / maxW;

                // two triangles per quad. Use a stricter test: all three verts must be above threshold
                bool triA = (w00 > threshold) && (w10 > threshold) && (w01 > threshold); // (i00, i10, i01)
                bool triB = (w01 > threshold) && (w10 > threshold) && (w11 > threshold); // (i01, i10, i11)

                // helper to ensure vertex exists in waterVerts and return its index
                int GetOrCreate(int origIdx)
                {
                    if (indexMap.TryGetValue(origIdx, out int mapped)) return mapped;
                    var v = vertices[origIdx] + Vector3.up * raiseAmount;
                    int newIndex = waterVerts.Count;
                    waterVerts.Add(v);
                    indexMap[origIdx] = newIndex;
                    return newIndex;
                }

                if (triA)
                {
                    int a = GetOrCreate(i00);
                    int b = GetOrCreate(i10);
                    int c = GetOrCreate(i01);
                    // ensure consistent winding
                    waterTris.Add(a);
                    waterTris.Add(b);
                    waterTris.Add(c);
                }
                if (triB)
                {
                    int a = GetOrCreate(i01);
                    int b = GetOrCreate(i10);
                    int c = GetOrCreate(i11);
                    waterTris.Add(a);
                    waterTris.Add(b);
                    waterTris.Add(c);
                }
            }
        }

        if (waterVerts.Count == 0)
        {
            Debug.Log("No water triangles created after thresholding.");
            return;
        }

        // build mesh
        Mesh wmesh = new Mesh();
        wmesh.name = "WaterOverlay";
        wmesh.SetVertices(waterVerts);
        wmesh.SetTriangles(waterTris, 0);
        wmesh.RecalculateNormals();
        wmesh.RecalculateBounds();

        // create object
        waterObject = new GameObject("WaterOverlay");
        waterObject.transform.SetParent(transform, false);
        var mf = waterObject.AddComponent<MeshFilter>();
        var mr = waterObject.AddComponent<MeshRenderer>();
        mf.sharedMesh = wmesh;

        // material: URP Unlit transparent if available; fallback to sprite material
        var mat = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
        if (mat != null)
        {
            // Some URP versions expose different property names; attempt common ones
            if (mat.HasProperty("_BaseColor"))
                mat.SetColor("_BaseColor", waterColor);
            else if (mat.HasProperty("_Color"))
                mat.SetColor("_Color", waterColor);

            // Make sure transparency blending is enabled if shader supports it
            // (URP shader graph variants differ; if necessary set the material rendering mode in editor)
            mr.sharedMaterial = mat;
        }
        else
        {
            var fallback = new Material(Shader.Find("Sprites/Default"));
            fallback.SetColor("_Color", waterColor);
            mr.sharedMaterial = fallback;
        }

        Debug.Log($"Water overlay built: verts={waterVerts.Count}, tris={waterTris.Count/3}, maxW={maxW:F4}");
    }

}
