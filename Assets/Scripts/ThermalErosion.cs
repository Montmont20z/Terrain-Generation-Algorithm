using UnityEngine;

public class ThermalErosion
{

    public int meshSize = 50; // grid resolution (meshSize x meshSize quads)
    Vector3[] vertices;

    // inside SimpleTerrain class

    /// <summary>
    /// Run thermal erosion on the current vertces[] height values.
    /// meshSize = number of quads; rows = meshSize+1
    /// </summary>
    /// Example usage
    /// RunThermalErosion(iterations: 20, talus: 0.5f, erosionFactor: 0.5f);
    public void RunThermalErosion(int iterations, float talus, float erosionFactor)
    {
        int rows = meshSize + 1;
        // build a 2D heights array so it's easy to index neighbours
        float[,] h = new float[rows, rows];
        for (int y = 0; y < rows; y++)
            for (int x = 0; x < rows; x++)
                h[y, x] = vertices[y * rows + x].y;

        // 8-neighbour offsets
        int[] ox = { -1, 0, 1, -1, 1, -1, 0, 1 };
        int[] oy = { -1, -1, -1, 0, 0, 1, 1, 1 };

        for (int it = 0; it < iterations; it++)
        {
            // accumulate changes then apply to avoid order bias
            float[,] delta = new float[rows, rows];

            for (int y = 0; y < rows; y++)
            {
                for (int x = 0; x < rows; x++)
                {
                    float current = h[y, x];

                    // gather total amount above talus for eligible neighbors
                    float totalExcess = 0f;
                    float[] excess = new float[8];

                    for (int n = 0; n < 8; n++)
                    {
                        int nx = x + ox[n];
                        int ny = y + oy[n];
                        if (nx < 0 || nx >= rows || ny < 0 || ny >= rows) continue;

                        float neigh = h[ny, nx];
                        float dh = current - neigh;
                        if (dh > talus)
                        {
                            // amount above threshold for that neighbor
                            float e = dh - talus;
                            excess[n] = e;
                            totalExcess += e;
                        }
                        else
                        {
                            excess[n] = 0f;
                        }
                    }

                    if (totalExcess <= 0f) continue;

                    // total material to move out of this cell this iteration
                    // scale by erosionFactor so behavior is controllable
                    float moveAmount = totalExcess * erosionFactor;

                    // distribute moveAmount to neighbors proportional to their excess
                    for (int n = 0; n < 8; n++)
                    {
                        int nx = x + ox[n];
                        int ny = y + oy[n];
                        if (nx < 0 || nx >= rows || ny < 0 || ny >= rows) continue;
                        if (excess[n] <= 0f) continue;

                        float portion = (excess[n] / totalExcess) * moveAmount;
                        delta[y, x] -= portion;
                        delta[ny, nx] += portion;
                    }
                }
            }

            // apply deltas
            for (int y = 0; y < rows; y++)
                for (int x = 0; x < rows; x++)
                    h[y, x] += delta[y, x];
        }

        // write back to vertices
        for (int y = 0; y < rows; y++)
            for (int x = 0; x < rows; x++)
                vertices[y * rows + x].y = h[y, x];
    }

}
