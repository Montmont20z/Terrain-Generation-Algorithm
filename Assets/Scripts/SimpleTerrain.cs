using System.Collections;
using UnityEngine;

[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class SimpleTerrain : MonoBehaviour
{
    [Header("Perlin input")]
    public float positionX = 0f;
    public float positionY = 0f;
    public float scale = 0.5f;
    public int octaves = 8;
    public float persistence = 0.5f;
    public float lacunarity = 2.0f;
    public int seed = 1;

    [Header("Mesh")]
    public int meshSize = 250; // grid resolution (meshSize x meshSize quads)
    public float heightScale = 50f; // how tall the hills are

    [Header("River / Lake (flow-based hydrology)")]
    [Tooltip("Only carve rivers below this terrain height (avoid mountain crests)")]
    public float riverMaxTerrainHeight = 30.0f;

    [Tooltip("Minimum flow accumulation to be considered a river channel")]
    public int riverThreshold = 100;

    [Tooltip("Base river width (adds flat-bottom carving around channel)")]
    public float riverWidthBase = 1f;

    [Tooltip("Log scale factor — higher = wider rivers with more accumulation")]
    public float riverWidthScale = 1f;

    [Tooltip("Max depth carved at the main river channel")]
    public float riverCarveDepthMax = 10f;

    [Tooltip("Remove single-cell sinks so flow can propagate correctly")]
    public bool usePitFilling = true;

    [Header("Lakes")]
    [Tooltip("Multiply river threshold to produce broader basins that become lakes if low-lying")]
    public float lakeWidthMultiplier = 3.0f; // lake width depends on river width // ensure > 1.0
    public float lakeMaxHeight = 2.5f;
    [Tooltip("If true, lakes will fill slightly shallower than surrounding to avoid overflow")]
    public float lakeFillAmount = 0.5f;

    public float globalWaterLevel = 1.0f;
    [Header("Droplet Erosion")]
    public int rainRate = 1000;          // droplets per tick
    public int simulationTicks = 10;     // ticks to run on manual trigger
    public float erosionConstant = 0.7f;    // k_e
    public float depositionConstant = 0.08f; // k_d
    public float capacityConstant = 4.0f;   // k_c
    public float evaporationRate = 0.02f;
    public float inertia = 0.3f;
    public float dropletStepSize = 1.0f;
    public int maxLifetime = 300;
    public bool biasSpawnToRivers = false;

    // === FLOW-BASED RIVER SYSTEM===
    private readonly int[] dx8 = { 1, 1, 0, -1, -1, -1, 0, 1 };
    private readonly int[] dy8 = { 0, 1, 1, 1, 0, -1, -1, -1 };
    private bool _terrainDirty = false;

    // cache the last values that affect river topology
    private float _lastScale, _lastHeightScale, _lastPersistence, _lastLacunarity;
    private int _lastOctaves, _lastSeed;

    Mesh mesh;
    Vector3[] vertices;
    // offsetX & Y to avoid weird pattern when perlin noise near 0
    float offsetX;
    float offsetY;
    // water
    float[] lastWater;              // per-vertex final water amount after hydraulic erosion
    private Mesh _waterMesh;
    private GameObject _waterObject;
    private Material _waterMat;



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


        // after erosion, update mesh
        mesh.vertices = vertices;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        RebuildWaterOverlay(threshold: 0.02f, raise: 0.02f, color: new Color(0f, 0.45f, 0.85f, 0.6f));

    }
    
    private void OnValidate()
    {
        /// Clamp values
        // === Perlin ===
        scale        = Mathf.Max(0.01f, scale);
        octaves      = Mathf.Clamp(octaves, 1, 12);
        persistence  = Mathf.Clamp(persistence, 0.1f, 0.9f);
        lacunarity   = Mathf.Max(1.01f, lacunarity);
        heightScale  = Mathf.Max(1f, heightScale);
        meshSize     = Mathf.Clamp(meshSize, 10, 512);
        
        // === River ===
        riverThreshold      = Mathf.Max(1, riverThreshold);
        riverWidthBase      = Mathf.Max(0.1f, riverWidthBase);
        riverWidthScale     = Mathf.Max(0f, riverWidthScale);
        riverCarveDepthMax  = Mathf.Clamp(riverCarveDepthMax, 0.1f, heightScale);
        riverMaxTerrainHeight = Mathf.Clamp(riverMaxTerrainHeight, 0f, heightScale);
        globalWaterLevel    = Mathf.Max(0f, globalWaterLevel);

        // === Lakes ===
        lakeWidthMultiplier = Mathf.Max(1.01f, lakeWidthMultiplier);
        lakeMaxHeight       = Mathf.Clamp(lakeMaxHeight, 0f, heightScale * 0.5f);
        lakeFillAmount      = Mathf.Clamp(lakeFillAmount, 0f, 1f);

        // === Droplet Erosion ===
        rainRate            = Mathf.Clamp(rainRate, 1, 5000);
        simulationTicks     = Mathf.Clamp(simulationTicks, 1, 200);
        erosionConstant     = Mathf.Clamp(erosionConstant, 0.01f, 1f);
        depositionConstant  = Mathf.Clamp(depositionConstant, 0.01f, 1f);
        capacityConstant    = Mathf.Clamp(capacityConstant, 0.1f, 20f);
        evaporationRate     = Mathf.Clamp(evaporationRate, 0.001f, 0.5f);
        inertia             = Mathf.Clamp(inertia, 0f, 0.99f);
        dropletStepSize     = Mathf.Clamp(dropletStepSize, 0.1f, 5f);
        maxLifetime         = Mathf.Clamp(maxLifetime, 10, 1000);

        // reset terrain flag
        _terrainDirty = true;
    }

    private void Update()
    {
        if (_terrainDirty)
        {
            _terrainDirty = false;
            RebuildTerrain();
        }
    }

    void RebuildTerrain()
    {
        if (mesh == null) return;
        // Re-generate everything when positionX/Y changes
        ApplyPerlinToVertices();
        
        if (isRiverParamsDirty())
        {
            ApplyRiverAndLakes();           // ← rivers & lakes now follow the shifted Perlin terrain
            CacheRiverParams();
            RebuildWaterOverlay();
            // CreateWaterOverlay(threshold: 0.02f, raise: 0.02f, color: new Color(0f, 0.45f, 0.85f, 0.6f));
        }
        mesh.vertices = vertices;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        // RunHydraulicErosion(5, 5.0f, 5.0f, 5.0f, 5.0f, 5.0f);
    }
    
    [ContextMenu("Rebuild All Terrain")]
    public void RebuildAllTerrain()
    {
        if (mesh == null) return;
        // Re-generate everything when positionX/Y changes
        ApplyPerlinToVertices();
        
        ApplyRiverAndLakes();           // ← rivers & lakes now follow the shifted Perlin terrain
        RebuildWaterOverlay();
        // CreateWaterOverlay(threshold: 0.02f, raise: 0.02f, color: new Color(0f, 0.45f, 0.85f, 0.6f));
        mesh.vertices = vertices;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
    }
    
    private bool isRiverParamsDirty()
    {
        return scale != _lastScale || heightScale != _lastHeightScale ||
               persistence != _lastPersistence || lacunarity != _lastLacunarity ||
               octaves != _lastOctaves || seed != _lastSeed;
    }
    
    private void CacheRiverParams()
    {
        _lastScale = scale; _lastHeightScale = heightScale;
        _lastPersistence = persistence; _lastLacunarity = lacunarity;
        _lastOctaves = octaves; _lastSeed = seed;
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
    
    void ApplyPerlinToVertices()
    {
        int vertsPerRow = meshSize + 1;

        for (int y = 0; y <= meshSize; y++)
        {
            for (int x = 0; x <= meshSize; x++)
            {
                int i = y * vertsPerRow + x;

                // sample using world-space position + the user offsets
                float sampleX = (x + positionX + offsetX) / meshSize / scale; // divide by meshSize to constrain to 0..1
                float sampleY = (y + positionY + offsetY) / meshSize / scale;

                // for multi-octave Perlin
                float noise = 0f;
                float amplitude = 1f;
                float frequency = 1f;
                float maxAmplitude = 0f;
                // iterate over octaves
                for (int o = 0; o < Mathf.Max(1, octaves); o++)
                {
                    float v = Mathf.PerlinNoise(sampleX * frequency, sampleY * frequency) * 2f - 1f; // now -1..1
                    // float v = Mathf.PerlinNoise((sampleX + Mathf.PerlinNoise(sampleX,sampleY)) * frequency, (y + Mathf.PerlinNoise(sampleX,sampleY)) * frequency) * 2f - 1f;
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

    /// <summary>
    /// D8 flow direction + pit filling + flow accumulation.
    /// Rivers follow natural valleys created by your Perlin noise.
    /// Changing positionX/Y instantly updates rivers/lakes because they are derived from the terrain itself.
    /// Lakes are created as broader low-lying basins along the river network.
    /// </summary>
    void ApplyRiverAndLakes()
    {
        int rows = meshSize + 1;
        lastWater = new float[rows * rows];

        float[,] originalHeight = new float[rows, rows];
        for (int y = 0; y < rows; y++)
            for (int x = 0; x < rows; x++)
                originalHeight[y, x] = vertices[y * rows + x].y;

        float[,] flowHeight = new float[rows, rows];
        System.Array.Copy(originalHeight, flowHeight, rows * rows);

        // UsePitFilling is now togglable per spec
        if (usePitFilling)
            ApplyPitFilling(flowHeight, rows);

        int[,] flowDir = ComputeFlowDirections(flowHeight, rows);
        float[,] accumulation = ComputeFlowAccumulation(flowHeight, flowDir, rows);

        // Precompute max log accumulation for normalization
        float maxLogAcc = 0f;
        for (int y = 0; y < rows; y++)
            for (int x = 0; x < rows; x++)
            {
                float logAcc = Mathf.Log(accumulation[y, x] + 1f);
                if (logAcc > maxLogAcc) maxLogAcc = logAcc;
            }
        if (maxLogAcc <= 0f) maxLogAcc = 1f;

        float effectiveGlobalWaterLevel = globalWaterLevel > 0f ? globalWaterLevel : heightScale * 0.1f;
        int carvedCount = 0;

        for (int y = 0; y < rows; y++)
        {
            for (int x = 0; x < rows; x++)
            {
                int i = y * rows + x;
                float acc = accumulation[y, x];
                float origH = originalHeight[y, x];

                // River width from spec: W(x,y) = RiverWidthBase + RiverWidthScale * log(A)
                float logAcc = Mathf.Log(acc + 1f);
                float riverWidth = riverWidthBase + riverWidthScale * logAcc;

                // A cell is a river channel if accumulation exceeds threshold
                bool isRiverChannel = (acc > riverThreshold) && (origH < riverMaxTerrainHeight);

                // A cell is in the river WIDTH band if any neighbor within riverWidth
                // is a channel — approximated here by checking if acc exceeds a
                // width-scaled threshold (efficient single-pass approximation)
                float widthThreshold = riverThreshold / Mathf.Max(1f, riverWidth);
                bool isRiverWidth = (acc > widthThreshold) && (origH < riverMaxTerrainHeight);

                if (isRiverChannel)
                {
                    // Carve depth: D(x,y) = RiverCarveDepthMax * log(A) / maxLogAcc
                    float norm  = logAcc / maxLogAcc;
                    float depth = riverCarveDepthMax * norm;
                    vertices[i].y = Mathf.Max(0f, origH - depth);
                    carvedCount++;
                }
                else if (isRiverWidth)
                {
                    // River bank — carve shallower based on distance from channel
                    // approximated as half depth scaled by width factor
                    float norm        = logAcc / maxLogAcc;
                    float bankDepth   = riverCarveDepthMax * norm * 0.3f;
                    vertices[i].y     = Mathf.Max(0f, origH - bankDepth);
                }
                else
                {
                    vertices[i].y = origH;
                }

                // === LAKE BASINS ===
                float lakeThreshold = riverThreshold / Mathf.Max(1f, lakeWidthMultiplier);
                if (origH <= lakeMaxHeight && acc > lakeThreshold)
                    vertices[i].y = Mathf.Max(0f, vertices[i].y - lakeFillAmount * 0.5f);

                // === WATER MASK ===
                float w = Mathf.Clamp01((effectiveGlobalWaterLevel - vertices[i].y) / effectiveGlobalWaterLevel);
                if (isRiverChannel) w = 1f;
                if (isRiverWidth)   w = Mathf.Max(w, 0.6f);
                if (origH <= lakeMaxHeight && acc > lakeThreshold) w = Mathf.Max(w, lakeFillAmount);

                lastWater[i] = w;
            }
        }

        // Dilate water mask
        float[] dilated = new float[lastWater.Length];
        for (int y = 0; y < rows; y++)
            for (int x = 0; x < rows; x++)
            {
                float max = lastWater[y * rows + x];
                for (int d = 0; d < 8; d++)
                {
                    int nx2 = x + dx8[d], ny2 = y + dy8[d];
                    if (nx2 >= 0 && nx2 < rows && ny2 >= 0 && ny2 < rows)
                        max = Mathf.Max(max, lastWater[ny2 * rows + nx2]);
                }
                dilated[y * rows + x] = max;
            }
        lastWater = dilated;

        Debug.Log($"ApplyRiverAndLakes: carved {carvedCount} channel cells | riverWidth formula: {riverWidthBase} + {riverWidthScale} * log(A)");
    }

    // ====================== HELPER METHODS (new) ======================
    private int[,] ComputeFlowDirections(float[,] h, int size)
    {
        int[,] dir = new int[size, size];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                dir[y, x] = -1; // default = sink
                float maxSlope = 0f;
                for (int d = 0; d < 8; d++)
                {
                    int nx = x + dx8[d];
                    int ny = y + dy8[d];
                    if (nx >= 0 && nx < size && ny >= 0 && ny < size)
                    {
                        float dist = (Mathf.Abs(dx8[d]) + Mathf.Abs(dy8[d]) == 1) ? 1f : 1.414213562f;
                        float slope = (h[y, x] - h[ny, nx]) / dist;
                        if (slope > maxSlope)
                        {
                            maxSlope = slope;
                            dir[y, x] = d;
                        }
                    }
                }
            }
        }
        return dir;
    }

    private void ApplyPitFilling(float[,] h, int size)
    {
        int maxIter = size * 2;
        int iter = 0;
        bool changed;
        do
        {
            changed = false;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float myH = h[y, x];
                    float minNeighH = float.MaxValue;
                    bool hasLower = false;

                    for (int d = 0; d < 8; d++)
                    {
                        int nx = x + dx8[d];
                        int ny = y + dy8[d];
                        if (nx >= 0 && nx < size && ny >= 0 && ny < size)
                        {
                            float nh = h[ny, nx];
                            if (nh < minNeighH) minNeighH = nh;
                            if (nh < myH) hasLower = true;
                        }
                    }

                    if (!hasLower && minNeighH < float.MaxValue)
                    {
                        h[y, x] = minNeighH + 0.01f; // small raise to let water flow out
                        changed = true;
                    }
                }
            }
            iter++;
        } while (changed && iter < maxIter);

        // Debug.Log($"PitFilling completed in {iter} iterations");
    }

    private float[,] ComputeFlowAccumulation(float[,] h, int[,] flowDir, int size)
    {
        float[,] acc = new float[size, size];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
                acc[y, x] = 1f; // every cell contributes itself

        // Build list of cells and sort by height DESCENDING (high → low)
        var cells = new System.Collections.Generic.List<System.Tuple<int, int, float>>(size * size);
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
                cells.Add(System.Tuple.Create(x, y, h[y, x]));

        cells.Sort((a, b) => b.Item3.CompareTo(a.Item3));

        // Propagate accumulation downstream
        for (int i = 0; i < cells.Count; i++)
        {
            int x = cells[i].Item1;
            int y = cells[i].Item2;
            int d = flowDir[y, x];
            if (d == -1) continue; // sink

            int nx = x + dx8[d];
            int ny = y + dy8[d];
            if (nx >= 0 && nx < size && ny >= 0 && ny < size)
            {
                acc[ny, nx] += acc[y, x];
            }
        }
        return acc;
    }

    // Minimal hydraulic erosion (grid-based) - unchanged

    /// <summary>
    /// Build a simple water overlay mesh from lastWater. Triangles whose average vertex water
    /// exceeds threshold become water triangles. Water mesh is slightly raised to avoid z-fight.
    /// </summary>
    public void RebuildWaterOverlay(float threshold = 0.02f, float raise = 0.02f, Color? color = null)
    {
        if (lastWater == null || lastWater.Length == 0) return;

        Color waterColor = color ?? new Color(0f, 0.45f, 0.85f, 0.6f);

        // --- One-time setup ---
        if (_waterObject == null)
        {
            _waterObject = new GameObject("WaterOverlay");
            _waterObject.transform.SetParent(transform, false);

            _waterMesh = new Mesh { name = "WaterOverlay" };
            _waterObject.AddComponent<MeshFilter>().sharedMesh = _waterMesh;

            var mr = _waterObject.AddComponent<MeshRenderer>();
            _waterMat = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            mr.sharedMaterial = _waterMat;
        }

        // Update material color
        if (_waterMat.HasProperty("_BaseColor")) _waterMat.SetColor("_BaseColor", waterColor);
        else if (_waterMat.HasProperty("_Color"))  _waterMat.SetColor("_Color",     waterColor);

        // --- Rebuild mesh data ---
        float maxW = 0f;
        for (int i = 0; i < lastWater.Length; i++)
            if (lastWater[i] > maxW) maxW = lastWater[i];
        if (maxW <= 0f) { _waterMesh.Clear(); return; }

        int rows = meshSize + 1;
        var indexMap  = new System.Collections.Generic.Dictionary<int, int>(rows * rows);
        var waterVerts = new System.Collections.Generic.List<Vector3>();
        var waterTris  = new System.Collections.Generic.List<int>();

        int GetOrCreate(int origIdx)
        {
            if (indexMap.TryGetValue(origIdx, out int mapped)) return mapped;
            int newIndex = waterVerts.Count;
            waterVerts.Add(vertices[origIdx] + Vector3.up * raise);
            indexMap[origIdx] = newIndex;
            return newIndex;
        }

        for (int y = 0; y < meshSize; y++)
        {
            for (int x = 0; x < meshSize; x++)
            {
                int i00 = y * rows + x,       i01 = y * rows + (x + 1);
                int i10 = (y + 1) * rows + x, i11 = (y + 1) * rows + (x + 1);

                float w00 = lastWater[i00] / maxW, w01 = lastWater[i01] / maxW;
                float w10 = lastWater[i10] / maxW, w11 = lastWater[i11] / maxW;

                if (w00 > threshold && w10 > threshold && w01 > threshold)
                {
                    waterTris.Add(GetOrCreate(i00));
                    waterTris.Add(GetOrCreate(i10));
                    waterTris.Add(GetOrCreate(i01));
                }
                if (w01 > threshold && w10 > threshold && w11 > threshold)
                {
                    waterTris.Add(GetOrCreate(i01));
                    waterTris.Add(GetOrCreate(i10));
                    waterTris.Add(GetOrCreate(i11));
                }
            }
        }

        // --- Upload to existing mesh (no new allocations) ---
        _waterMesh.Clear();
        _waterMesh.SetVertices(waterVerts);
        _waterMesh.SetTriangles(waterTris, 0);
        _waterMesh.RecalculateNormals();
        _waterMesh.RecalculateBounds();
    }
    
    void RunHydraulicErosion(int iterations, float rainPerIt, float evaporation, float capacityFactor, float depositSpeed, float erodeSpeed)
    {
        // ... (your original method - left untouched)
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
    /// Droplet-based hydraulic erosion as per proposal.
    /// Each droplet: spawns, rolls downhill via bilinear gradient,
    /// erodes/deposits sediment, evaporates. Modifies vertices[].y directly.
    /// Call RunDropletErosionTicks() to run multiple ticks at once.
    /// </summary>
    void RunDropletErosionTick()
    {
        int rows = meshSize + 1;
        System.Random rng = new System.Random(seed);

        // Build a flat working heightmap from current vertices
        // (droplets use float positions, so we need random access by [y,x])
        float[] h = new float[rows * rows];
        for (int i = 0; i < h.Length; i++)
            h[i] = vertices[i].y;

        // Helper: bilinear sample height at float position (px, py)
        float SampleHeight(float px, float py)
        {
            int x0 = Mathf.Clamp((int)px, 0, rows - 2);
            int y0 = Mathf.Clamp((int)py, 0, rows - 2);
            float fx = px - x0;
            float fy = py - y0;

            float h00 = h[y0 * rows + x0];
            float h10 = h[y0 * rows + (x0 + 1)];
            float h01 = h[(y0 + 1) * rows + x0];
            float h11 = h[(y0 + 1) * rows + (x0 + 1)];

            return h00 * (1 - fx) * (1 - fy)
                 + h10 * fx       * (1 - fy)
                 + h01 * (1 - fx) * fy
                 + h11 * fx       * fy;
        }

        // Helper: bilinear gradient at float position — returns (dH/dx, dH/dy)
        Vector2 SampleGradient(float px, float py)
        {
            int x0 = Mathf.Clamp((int)px, 0, rows - 2);
            int y0 = Mathf.Clamp((int)py, 0, rows - 2);
            float fx = px - x0;
            float fy = py - y0;

            float h00 = h[y0 * rows + x0];
            float h10 = h[y0 * rows + (x0 + 1)];
            float h01 = h[(y0 + 1) * rows + x0];
            float h11 = h[(y0 + 1) * rows + (x0 + 1)];

            // partial derivatives via bilinear interpolation
            float dHdx = (h10 - h00) * (1 - fy) + (h11 - h01) * fy;
            float dHdy = (h01 - h00) * (1 - fx) + (h11 - h10) * fx;

            return new Vector2(dHdx, dHdy);
        }

        // Helper: deposit/erode at float position using bilinear weights
        void ModifyHeight(float px, float py, float delta)
        {
            int x0 = Mathf.Clamp((int)px, 0, rows - 2);
            int y0 = Mathf.Clamp((int)py, 0, rows - 2);
            float fx = px - x0;
            float fy = py - y0;

            // distribute delta across the 4 surrounding vertices by bilinear weights
            h[y0 * rows + x0]           += delta * (1 - fx) * (1 - fy);
            h[y0 * rows + (x0 + 1)]     += delta * fx       * (1 - fy);
            h[(y0 + 1) * rows + x0]     += delta * (1 - fx) * fy;
            h[(y0 + 1) * rows + (x0 + 1)] += delta * fx     * fy;
        }

        // --- Spawn and simulate droplets ---
        for (int d = 0; d < rainRate; d++)
        {
            // Spawn position
            float px, py;
            if (biasSpawnToRivers && lastWater != null)
            {
                // bias: pick a random river cell weighted by lastWater
                // simple rejection sample — try up to 10 times, take highest water
                int bestIdx = rng.Next(0, rows * rows);
                for (int attempt = 0; attempt < 10; attempt++)
                {
                    int idx = rng.Next(0, rows * rows);
                    if (lastWater[idx] > lastWater[bestIdx]) bestIdx = idx;
                }
                px = bestIdx % rows;
                py = bestIdx / rows;
            }
            else
            {
                px = (float)(rng.NextDouble() * (rows - 2));
                py = (float)(rng.NextDouble() * (rows - 2));
            }

            // Droplet state
            float dirX = 0f, dirY = 0f;
            float water  = 1.0f;
            float sediment = 0.0f;

            for (int step = 0; step < maxLifetime; step++)
            {
                if (water < 0.01f) break;

                // Out of bounds check
                if (px < 0 || px >= rows - 1 || py < 0 || py >= rows - 1) break;

                // 1. Sample gradient at current position
                Vector2 grad = SampleGradient(px, py);
                float slope = grad.magnitude;

                // 2. Update direction with inertia (smooth the path)
                if (slope > 0.0001f)
                {
                    // steepest descent = -gradient
                    dirX = dirX * inertia + (-grad.x / slope) * (1f - inertia);
                    dirY = dirY * inertia + (-grad.y / slope) * (1f - inertia);
                }
                else
                {
                    // flat area: nudge in a random direction to avoid infinite loop
                    float angle = (float)(rng.NextDouble() * Mathf.PI * 2f);
                    dirX = Mathf.Cos(angle);
                    dirY = Mathf.Sin(angle);
                }

                // normalize direction
                float dirLen = Mathf.Sqrt(dirX * dirX + dirY * dirY);
                if (dirLen > 0.0001f) { dirX /= dirLen; dirY /= dirLen; }

                float oldPx = px, oldPy = py;
                float oldH  = SampleHeight(px, py);

                // 3. Move droplet
                px += dirX * dropletStepSize;
                py += dirY * dropletStepSize;

                if (px < 0 || px >= rows - 1 || py < 0 || py >= rows - 1) break;

                float newH = SampleHeight(px, py);
                float heightDiff = newH - oldH; // positive = moved uphill

                // 4. Sediment capacity
                float capacity = Mathf.Max(-heightDiff, 0.001f) * water * capacityConstant;
                // use slope = max(-heightDiff, min_slope) so flat areas still have capacity

                // 5. Erode or deposit
                if (sediment > capacity || heightDiff > 0f)
                {
                    // Deposit: overfull or moving uphill
                    float depositAmount = (heightDiff > 0f)
                        ? Mathf.Min(sediment, heightDiff)          // fill the uphill pit
                        : (sediment - capacity) * depositionConstant;

                    sediment -= depositAmount;
                    ModifyHeight(oldPx, oldPy, depositAmount);
                }
                else
                {
                    // Erode
                    float erodeAmount = Mathf.Min(
                        (capacity - sediment) * erosionConstant,
                        -heightDiff + 0.001f   // don't erode more than the drop height
                        // 0.5f // max crave
                    );
                    erodeAmount = Mathf.Max(0f, erodeAmount);
                    sediment += erodeAmount;
                    ModifyHeight(oldPx, oldPy, -erodeAmount);
                }

                // 6. Evaporate
                water *= (1f - evaporationRate);
            }

            // Remaining sediment deposits at final position
            if (sediment > 0f)
                ModifyHeight(px, py, sediment);
        }

        // Write back to vertices
        for (int i = 0; i < h.Length; i++)
            vertices[i].y = Mathf.Max(0f, h[i]);
    }

    [ContextMenu("Run Droplet Erosion")]
    public void RunDropletErosionTicks()
    {
        for (int t = 0; t < simulationTicks; t++)
            RunDropletErosionTick();

        mesh.vertices = vertices;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        RebuildWaterOverlay();
        Debug.Log($"Droplet erosion done: {simulationTicks} ticks × {rainRate} droplets = {simulationTicks * rainRate} total droplets");
    }
    
    public IEnumerator RunErosionTimeLapse(int totalTicks)
    {
        for (int t = 0; t < totalTicks; t++)
        {
            RunDropletErosionTick();
            mesh.vertices = vertices;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            RebuildWaterOverlay();
            Debug.Log("current tick" + t);
            yield return null; // one tick per frame — visible evolution
        }
    }


}
