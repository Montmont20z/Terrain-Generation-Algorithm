# 🏔️ Procedural Terrain Generation
### Perlin Noise + Hydraulic Erosion for a Living, Evolving Voxel World

![Unity](https://img.shields.io/badge/Unity-C%23-black?logo=unity)
![Domain](https://img.shields.io/badge/Domain-Procedural%20Generation-blue)
![Deterministic](https://img.shields.io/badge/Generation-Deterministic%20(seeded)-success)

> Minecraft-style worlds are generated once and then frozen forever. This project makes the terrain **age**: same seed, same reproducible world, but one that is carved by rain, rivers and sediment over time.

<img width="800" height="519" alt="1012-ezgif com-optimize" src="https://github.com/user-attachments/assets/ab0082aa-e2d2-4b09-93a4-0eb3a8b847bf" />

| A | B | C |
| -------- | -------- | -------- |
| <img width="1125" height="681" alt="image" src="https://github.com/user-attachments/assets/2c5b660f-5a0f-4e26-9ca2-fd176e8aba8c" /> |<img width="1207" height="669" alt="image" src="https://github.com/user-attachments/assets/d2be7787-2fd9-410d-8280-199376a32ea7" /> | <img width="1182" height="690" alt="image" src="https://github.com/user-attachments/assets/b64421a4-8ce3-42d0-92e4-6e32948eda65" /> |
| <img width="1130" height="684" alt="image" src="https://github.com/user-attachments/assets/242d67b7-96f7-4629-b603-816c51ee6f43" /> | <img width="1303" height="700" alt="image" src="https://github.com/user-attachments/assets/9d0338b0-d812-495d-9022-f68219ac68fb" /> | <img width="1401" height="659" alt="image" src="https://github.com/user-attachments/assets/352fd9fe-28ff-4579-82a3-4d6f45d677c6" /> |
| <img width="1363" height="712" alt="image" src="https://github.com/user-attachments/assets/90ad9cb5-054e-47e8-8f0d-e093ae40504c" /> | <img width="1413" height="711" alt="image" src="https://github.com/user-attachments/assets/84ec7601-10ef-40f0-8f97-fcdcda237baa" /> | <img width="1359" height="721" alt="image" src="https://github.com/user-attachments/assets/5a5145ee-5250-4ee5-8763-7bde10536cf5" /> |
---

## ✨ Highlights

- **Seeded multi-octave Perlin noise** (fractional Brownian motion) for the base landscape
- **Hydrology-driven rivers & lakes** using D8 flow direction, pit filling and flow accumulation in `O(W·H)`
- **Droplet-based hydraulic erosion** (rain → flow → sediment pickup → deposition → evaporation)
- **Fully deterministic**: one 32-bit master seed reproduces the entire world
- **Two run modes**: precompute full erosion offline, or run cheap incremental erosion in a background thread at runtime
- **Time-lapse capture** of terrain evolution, exported to H.264 video

---

## 🎯 The Problem

Standard Perlin-noise terrain is static. Players on the same seed see identical land no matter how much time passes, and nothing in it resembles real geological processes. Prior work on hydraulic erosion (Mei et al., Štava et al.) is mostly aimed at **offline terrain synthesis**, not at **runtime evolution in persistent game worlds**.

This project targets that gap: **deterministic, time-evolving terrain** that stays reproducible while changing in plausible, gameplay-relevant ways.

### Why it matters for gameplay

| Idea | What it enables |
|---|---|
| **Replayability** | Landscapes that evolve reward repeat visits |
| **Geological realism** | Valleys, river channels, depositional formations (with room to grow toward karst, badlands and coastal features) |
| **Emergent mechanics** | River flooding, sinkhole formation, cave expansion |

---

## 🧠 How It Works

```mermaid
flowchart LR
    S([32-bit Master Seed]) --> A[Multi-octave<br/>Perlin Noise]
    A --> B[Pit Filling]
    B --> C[D8 Flow<br/>Direction]
    C --> D[Flow<br/>Accumulation]
    D --> E[River & Lake<br/>Carving]
    E --> F[Droplet<br/>Hydraulic Erosion]
    F --> G[Time-lapse<br/>Capture]
```

Everything runs on a 2D heightmap `H` of size `W × H` cells (world size `W·R × H·R`, where `R` is the cell resolution in metres).

### Stage 1: Base terrain (Perlin fBm)

Several octaves of seeded Perlin noise are summed, then normalised to the target elevation range:

```
height(x, y) = Σ  A(i) · P(x · f(i), y · f(i))      for i = 0 … n-1
A(i) = persistence^i      (default 0.5)
f(i) = lacunarity^i       (default 2.0)
n    = octaves            (default 6–8)
```

An optional independent seeded Perlin mask `M(x, y) ∈ [0, 1]` allows artistic control in later stages.

### Stage 2: Rivers & lakes (hydrology)

1. **D8 flow direction**: each cell flows to its steepest-descent neighbour among 8 (diagonals weighted by √2), encoded as power-of-two flags.
2. **Sink handling**: lightweight pit filling removes single-cell local minima (a common Perlin artefact). This yields flat lake areas and guarantees every cell has a valid downhill path.
3. **Flow accumulation**: counts how many upstream cells drain through each cell.
4. **River identification & carving**: cells above a threshold (default `100`) become channels, width scales logarithmically with accumulation, and the heightmap is carved up to a maximum depth (default `10 m`).

### Stage 3: Hydraulic erosion (droplet model)

Droplets spawn each tick from a seeded RNG (optionally biased toward high ground or existing rivers). Each carries a position, direction, water volume `w` (starts at 1.0) and sediment `s` (starts at 0).

```
capacity = k_c · w · slope

if s < capacity:   erode   → take  min(k_e · (capacity − s), 0.1)  from terrain, add to s
else:              deposit → drop  k_d · (s − capacity)  onto terrain
                   (also deposits when moving uphill)

then:  w *= (1 − evaporation_rate);  lifetime++
```

Direction is smoothed with inertia, and a droplet dies when its water drops below `0.01` or it hits `max_lifetime` (default `300`).

### 🎞️ Time-lapse visualisation

Every *N* erosion iterations the system copies the heightmap, renders to a 1920×1080 texture, overlays metadata (iteration count, parameters), buffers the compressed frame, and finally encodes everything to **H.264**. You get valley formation, river development, sediment deposition and before/after comparisons.

---

## 🔌 API Design

Modular, deterministic and event-driven, built for three audiences:

- **Engine developers** integrating the system into a game engine
- **Game designers** tuning behaviour through exposed parameters and presets
- **Runtime systems** (weather, events, AI) that modify terrain during gameplay

Supports both **offline precomputation** and **incremental runtime simulation**.


```mermaid
flowchart TD
    Start([Start]) --> Input[/"Input:<br>Seed S, Width W, Height H,<br>Parameters"/]
    Input --> Perlin["Multi-Octave Perlin Noise<br>For i = 0 to n_octaves:<br>A = persistence^i<br>f = lacunarity^i<br>h += A * Perlin(fx, fy, S)"]
    Perlin --> Normalize["Normalize and Scale<br>to Target Elevation Range"]
    Normalize --> Output[/"Output: Heightmap H(x,y)"/]
    Output --> End([End])
```
### Fig. 1. Perlin Noise Flowchart <br> <br>

```mermaid
flowchart TD
    Start([Start]) --> Input[/"Input: Heightmap H(x,y)"/]
    Input --> D8["D8 Flow Direction<br>For each cell:<br>Compute 8 neighbor slopes<br>d(x,y) = steepest downhill neighbor (0–7)<br>If all >= center → sink"]
    D8 --> PitFill["Lightweight Pit Filling<br>Remove single-cell sinks / local minima<br>→ Guarantee valid downhill everywhere"]
    PitFill --> FlowAcc["Flow Accumulation A(x,y)<br>A = 1 + ∑ A(upstream cells)<br>Single-pass topological sort O(W H)"]
    FlowAcc --> Decision{"A(x,y) > river_threshold<br>(default 100)?"}
    
    Decision -- Yes --> River["Mark as River Channel<br>width ≈ w_base + k_scale · log(A)<br>Carve heightmap:<br>H -= min( depth_max, scale_factor )"]
    Decision -- No --> Normal["Normal Terrain Cell"]
    
    River --> Combine["River & Lake Map + Updated H"]
    Normal --> Combine
    
    Combine --> Output[/"Output: Heightmap H(x,y)"/]
    Output --> End([End])
```
### Fig. 2. River Generation Flowchart<br> <br>

```mermaid
flowchart TD
    Start([Start]) --> Input[/"Input: Heightmap H(x,y)"/]
    Input --> Spawn["Spawn droplets (rain rate 500~2000/tick)<br>Seeded RNG<br>State: p(x,y), dir, w=1.0, s=0.0"]
    
    Spawn --> Alive{"Droplet alive?<br>w > 0.01 AND lifetime < max_lifetime<br>(=300)"}
    
    Alive -- No --> More{"More droplets / ticks?"}
    More -- Yes --> Spawn
    More -- No --> FinalOut[/"Final Eroded Heightmap"/] --> End([End])
    
    Alive -- Yes --> Grad["Compute gradient g = bilinear_gradient(H, p)<br>slope = length(g)"]
    Grad --> Dir["dir = lerp(dir, normalize(-g), inertia = 0.3)"]
    Dir --> Pos["p = p + dir * step_size"]
    Pos --> Cap["capacity C = k_c * w * slope<br>(k_c = 4.0, simplified — no velocity)"]
    
    Cap --> CheckCap{"Is s < C?"}
    
    CheckCap -- Yes --> Erode["Erode:<br>ΔE = min( k_e * (C - s), available H,<br>hard limit e.g. 0.1 )<br>H(p) -= ΔE<br>s += ΔE<br>(k_e = 0.7)"]
    CheckCap -- No --> Deposit["Deposit:<br>ΔD = k_d * (s - C)<br>H(p) += ΔD<br>s -= ΔD<br>(k_d = 0.08)"]
    
    Erode --> Evap["Evaporate: w *= (1 - evap_rate = 0.02)"]
    Deposit --> Evap
    
    Evap --> Lifetime["lifetime += 1"]
    Lifetime --> Alive
```
### Fig. 3. River Generation Flowchart<br> <br>


---

## ⚙️ Parameters

<details>
<summary><b>Perlin noise</b></summary>

| Parameter | Type | Default / Range | Effect |
|---|---|---|---|
| Width / Height | int | 100 | Heightmap dimensions |
| Cell Resolution | int | 1 | Units per cell |
| Scale | float | 5.0 | Level of terrain detail |
| Persistence | float | 0 < p < 1 | How fast amplitude falls per octave |
| Lacunarity | float | > 1 | How fast frequency rises per octave |
| Octaves | int | 6 | Number of layered noise functions |

</details>

<details>
<summary><b>Rivers & lakes</b></summary>

| Parameter | Type | Default | Effect |
|---|---|---|---|
| RiverThreshold | int | 100 | Minimum flow accumulation to become a river |
| RiverWidthBase | float | 1 | Base river width |
| RiverWidthScale | float | 1 | Log-scale factor for width |
| RiverCarveDepthMax | float | 10 | Maximum carving depth |
| UsePitFilling | bool | true | Fill local sinks into lakes |

</details>

<details>
<summary><b>Hydraulic erosion</b></summary>

| Parameter | Type | Default | Effect |
|---|---|---|---|
| Rain Rate | int | 1000 (500–2000) | Droplets per tick |
| Erosion Constant `k_e` | float | 0.7 | Erosion rate |
| Deposition Constant `k_d` | float | 0.08 | Deposition rate |
| Capacity Constant `k_c` | float | 4.0 | Sediment capacity |
| Evaporation Rate | float | 0.02 | Water loss per step |
| Inertia | float | 0.3 | Direction smoothing |
| MaxLifetime | int | 300 | Max steps per droplet |
| StepSize | float | 1.0 | Movement per step |
| Simulation Ticks | int | 10 runtime / 100 generation | Number of erosion ticks |
| BiasSpawnToRivers | bool | false | Spawn droplets near rivers |

</details>

---

## 🧱 Scope & Limitations

- **Platforms:** PC and modern consoles; reference implementation in **Unity (C#)** using a heightmap or low-resolution voxel grid
- **Goal is plausibility, not geological accuracy**: visually and mechanically believable behaviour for gameplay
- **Performance:** full erosion can be precomputed offline, or run incrementally and cheaply at runtime

---

## 🚀 Getting Started

## Getting Started

### Requirements

- **Unity Editor:** `6000.3.10f1`

### How to Run

1. Clone or download this repository.
2. Open the project using Unity Editor `6000.3.10f1`.
3. Open the scene containing the terrain generation system.
4. Press the **Play** button in the Unity Editor toolbar to start the simulation.
5. Switch to the **Scene** view to observe and navigate around the generated terrain.

### Scene Navigation

**Note:** The Game view is currently not working, please use the **Scene** view to view and explore the terrain while the simulation is running.

To navigate in the Scene view, use Unity's standard scene navigation controls. For example, hold the right mouse button and use `W`, `A`, `S`, and `D` to move around. Use `Q` and `E` to move vertically, and hold `Shift` to move faster.

### Adjusting Parameters

Terrain generation parameters can be modified through the **Inspector** window.

1. Select the GameObject that contains the terrain generation component.
2. Locate the relevant parameters in the Inspector.
3. Adjust the values to change the terrain generation or erosion settings.
4. Click **Rebuild Terrain** to regenerate the terrain using the updated parameters.

### Available Actions

The terrain generation component provides two main actions:

- **Rebuild Terrain** — Regenerates the terrain using the current parameter settings.
- **Run Erosion** — Applies the erosion algorithm to the generated terrain to modify its shape and produce more eroded terrain features.


---

## 📚 Key References

- Perlin, K. (1985). *An image synthesizer.* ACM SIGGRAPH CGA, 19(3).
- Perlin, K. (2002). *Improving noise.* ACM TOG, 21(3).
- Musgrave, Kolb & Mace (1989). *The synthesis and rendering of eroded fractal terrains.* SIGGRAPH.
- Mei, Decaudin & Hu (2007). *Fast hydraulic erosion simulation and visualization on GPU.* Pacific Graphics.
- Štava, Beneš, Brisbin & Křivánek (2008). *Interactive terrain modeling using hydraulic erosion.* SCA.
- Génevaux et al. (2013). *Terrain generation using procedural models based on hydrology.* ACM TOG, 32(4).
- Cordonnier et al. (2016). *Large scale terrain generation from tectonic uplift and fluvial erosion.* Computer Graphics Forum, 35(2).

Full list in the project proposal.

---

## 👥 Team

Bachelor of Computer Science (Honours) in Interactive Software Technology, Tunku Abdul Rahman University of Management and Technology, Kuala Lumpur, Malaysia.

- **Cheah Hong Lek**
- **Jonathan Wong Rong Wei**
- **Soi Hui Fang**

---

