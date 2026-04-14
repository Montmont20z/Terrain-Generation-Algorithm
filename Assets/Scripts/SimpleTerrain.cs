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

    [Tooltip("Accumulation threshold for river cells (upstream cell count). Lower = more rivers on small meshes (try 10-30 for 51x51 grid)")]
    public float riverAccumulationThreshold = 50f;

    [Tooltip("Max depth carved at main river channel (deeper for bigger rivers via log scaling)")]
    public float riverDepth = 2.0f;

    [Header("Lakes")]
    [Tooltip("Multiply river threshold to produce broader basins that become lakes if low-lying")]
    public float lakeWidthMultiplier = 3.0f; // lake width depends on river width // ensure > 1.0
    public float lakeMaxHeight = 2.5f;
    [Tooltip("If true, lakes will fill slightly shallower than surrounding to avoid overflow")]
    public float lakeFillAmount = 0.5f;

    public float globalWaterLevel = 1.0f;

    // === FLOW-BASED RIVER SYSTEM===
    private readonly int[] dx8 = { 1, 1, 0, -1, -1, -1, 0, 1 };
    private readonly int[] dy8 = { 0, 1, 1, 1, 0, -1, -1, -1 };

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

    private void OnValidate()
    {
        // Re-generate everything when positionX/Y changes (fixes problem 1)
        ApplyPerlinToVertices();
        ApplyRiverAndLakes();           // ← rivers & lakes now follow the shifted Perlin terrain
        mesh.vertices = vertices;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        CreateWaterOverlay(threshold: 0.02f, raise: 0.02f, color: new Color(0f, 0.45f, 0.85f, 0.6f));
        
        // RunHydraulicErosion(5, 5.0f, 5.0f, 5.0f, 5.0f, 5.0f);
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

        // 1. Copy current Perlin heights (original terrain before any carving)
        float[,] originalHeight = new float[rows, rows];
        for (int y = 0; y < rows; y++)
            for (int x = 0; x < rows; x++)
                originalHeight[y, x] = vertices[y * rows + x].y;

        // 2. Make a working copy for flow simulation (we will pit-fill this copy)
        float[,] flowHeight = new float[rows, rows];
        System.Array.Copy(originalHeight, flowHeight, rows * rows);

        // 3. Lightweight pit filling (removes single-cell sinks so flow can propagate)
        ApplyPitFilling(flowHeight, rows);

        // 4. Compute flow directions on the pit-filled heightmap
        int[,] flowDir = ComputeFlowDirections(flowHeight, rows);

        // 5. Compute flow accumulation (how many cells drain into each cell)
        float[,] accumulation = ComputeFlowAccumulation(flowHeight, flowDir, rows);

        // 6. Carve rivers + create lakes + build water mask
        int carvedCount = 0;
        float maxLogAcc = 0f;
        for (int y = 0; y < rows; y++)
            for (int x = 0; x < rows; x++)
            {
                float logAcc = Mathf.Log(accumulation[y, x] + 1f);
                if (logAcc > maxLogAcc) maxLogAcc = logAcc;
            }
        if (maxLogAcc <= 0f) maxLogAcc = 1f;

        float effectiveGlobalWaterLevel = globalWaterLevel > 0f ? globalWaterLevel : heightScale * 0.1f;

        for (int y = 0; y < rows; y++)
        {
            for (int x = 0; x < rows; x++)
            {
                int i = y * rows + x;
                float acc = accumulation[y, x];
                float origH = originalHeight[y, x];

                bool isRiver = (acc > riverAccumulationThreshold) && (origH < riverMaxTerrainHeight);

                // === RIVER CARVING (main channel) ===
                if (isRiver)
                {
                    float norm = Mathf.Log(acc + 1f) / maxLogAcc; // logarithmic scaling = wider/deeper rivers naturally
                    float depth = riverDepth * norm;
                    vertices[i].y = Mathf.Max(0f, origH - depth);
                    carvedCount++;
                }
                else
                {
                    vertices[i].y = origH;
                }

                // === LAKE BASINS (wider low-lying areas near rivers) ===
                float lakeThreshold = riverAccumulationThreshold / Mathf.Max(1f, lakeWidthMultiplier);
                if (origH <= lakeMaxHeight && acc > lakeThreshold)
                {
                    // shallow basin carve for lake look
                    vertices[i].y = Mathf.Max(0f, vertices[i].y - lakeFillAmount * 0.5f);
                }

                // === WATER MASK (for overlay) ===
                float w = Mathf.Clamp01((effectiveGlobalWaterLevel - vertices[i].y) / effectiveGlobalWaterLevel);
                if (isRiver) w = 1f;                                 // rivers are always full water
                if (origH <= lakeMaxHeight && acc > lakeThreshold) w = Mathf.Max(w, lakeFillAmount); // lakes get water too

                lastWater[i] = w;
            }
        }
        
        // After setting lastWater in ApplyRiverAndLakes, blur/dilate it
        float[] dilated = new float[lastWater.Length];
        for (int y = 0; y < rows; y++)
            for (int x = 0; x < rows; x++) {
                float max = lastWater[y * rows + x];
                for (int d = 0; d < 8; d++) {
                    int nx2 = x + dx8[d], ny2 = y + dy8[d];
                    if (nx2 >= 0 && nx2 < rows && ny2 >= 0 && ny2 < rows)
                        max = Mathf.Max(max, lastWater[ny2 * rows + nx2]);
                }
                dilated[y * rows + x] = max;
            }
        lastWater = dilated;

        Debug.Log($"ApplyRiverAndLakes (flow-based): carved {carvedCount} river cells | max acc ≈ {accumulation[rows / 2, rows / 2]:F0}");
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

        // Debug.Log($"Water overlay built: verts={waterVerts.Count}, tris={waterTris.Count/3}, maxW={maxW:F4}");
    }

}
