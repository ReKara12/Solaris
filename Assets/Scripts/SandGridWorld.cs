using System;
using System.Collections.Generic;
using UnityEngine;

public class SandGridWorld : MonoBehaviour
{
    public static SandGridWorld Instance { get; private set; }

    [Header("World Arrays (160x160)")]
    public float[] Heights;
    public float[] BaseHeights;
    public float[] TrashDensity;
    public float[] Grass;
    public byte[] IsRoad;
    public byte[] OasisProtected;

    [Header("World Entities & Stats")]
    public List<Bounds> BuildingColliders = new List<Bounds>();
    public List<Vector3> OasisTrees = new List<Vector3>();
    public List<Vector3> StructurePositions = new List<Vector3>();
    public int SolarPanelsRestored { get; private set; }
    public float RestorationPercent { get; private set; }

    // Internal Buried Structure representation
    private class BuriedBuilding
    {
        public GameObject Root;
        public Vector3 BasePos;
        public float BuriedY;
        public float CurrentY;
        public int GridX;
        public int GridZ;
        public bool IsRestored;
        public bool HasSolarPanels;
        public float RiseSpeed;
    }

    private readonly List<BuriedBuilding> _buriedBuildings = new List<BuriedBuilding>();

    // Mesh & Graphics
    private MeshFilter _meshFilter;
    private MeshRenderer _meshRenderer;
    private MeshCollider _meshCollider;
    private Mesh _terrainMesh;
    private Material _terrainMat;
    private Texture2D _splatMap;

    private Vector3[] _vertices;
    private Vector2[] _uvs;
    private Color[] _colorBuffer;
    private int[] _triangles;

    private bool _dirty = false;
    private bool _colliderDirty = false;
    private float _colliderTimer = 0f;
    private float _restorationCalcTimer = 0f;

    // Workshop Animations
    private Transform _workshopRadarDish;
    private Transform _workshopCrystal;
    private Vector3 _crystalBasePos;

    // Spire Crown Animation
    private Transform _spireEnergyCore;

    // Procedural Texture Cache
    private static readonly Dictionary<string, Texture2D> _textureCache = new Dictionary<string, Texture2D>();

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            SolarisConfig.SafeDestroy(gameObject);
            return;
        }
        Instance = this;
    }

    private void Start()
    {
        if (Heights == null || Heights.Length == 0)
        {
            InitializeWorld();
        }
    }

    // Public aliases for StudioBridge reflection invoke
    public void BuildScene() => InitializeWorld();
    public void SetupScene() => InitializeWorld();
    public void Initialize() => InitializeWorld();

    /// <summary>
    /// Builds the procedural terrain, road network, Climate Spire, Workshop, and 65+ city buildings.
    /// Idempotent: safely destroys previous children if called multiple times.
    /// </summary>
    public void InitializeWorld()
    {
        // 1. Clean existing children and lists
        while (transform.childCount > 0)
        {
            DestroyImmediate(transform.GetChild(0).gameObject);
        }

        BuildingColliders.Clear();
        OasisTrees.Clear();
        StructurePositions.Clear();
        _buriedBuildings.Clear();
        SolarPanelsRestored = 0;
        RestorationPercent = 0f;

        // 2. Allocate 160x160 arrays
        int totalCells = SolarisConfig.GRID_SIZE * SolarisConfig.GRID_SIZE;
        if (Heights == null || Heights.Length != totalCells) Heights = new float[totalCells];
        if (BaseHeights == null || BaseHeights.Length != totalCells) BaseHeights = new float[totalCells];
        if (TrashDensity == null || TrashDensity.Length != totalCells) TrashDensity = new float[totalCells];
        if (Grass == null || Grass.Length != totalCells) Grass = new float[totalCells];
        if (IsRoad == null || IsRoad.Length != totalCells) IsRoad = new byte[totalCells];
        if (OasisProtected == null || OasisProtected.Length != totalCells) OasisProtected = new byte[totalCells];

        // 3. Generate city pavement baseline and recyclable trash/debris heaps
        int N = SolarisConfig.GRID_SIZE;
        for (int z = 0; z < N; z++)
        {
            for (int x = 0; x < N; x++)
            {
                int idx = z * N + x;
                float wx = SolarisConfig.GridToWorldX(x);
                float wz = SolarisConfig.GridToWorldZ(z);

                // Multi-octave Perlin variation for trash density
                float n1 = Mathf.PerlinNoise(wx * 0.022f + 140f, wz * 0.022f + 140f);
                float n2 = Mathf.PerlinNoise(wx * 0.065f + 320f, wz * 0.065f + 320f);

                // Base ground: solid flat city pavement at y = 0.0f
                float baseGround = 0f;
                float distFromCenter = Mathf.Max(Mathf.Abs(wx), Mathf.Abs(wz));
                if (distFromCenter > 170f)
                {
                    // Natural rolling dunes only outside the city boundaries
                    float edgeFactor = Mathf.Clamp01((distFromCenter - 170f) / 40f);
                    baseGround = ((n1 * 0.7f + n2 * 0.3f) * 2.5f) * edgeFactor;
                }
                BaseHeights[idx] = baseGround;

                // Boulevard check (North-South & East-West avenues)
                bool isRoad = (Mathf.Abs(wx) <= 7.2f || Mathf.Abs(wz) <= 7.2f);
                IsRoad[idx] = (byte)(isRoad ? 1 : 0);

                // Initial Trash & Scrap Debris density (0.0 = clean pavement, 1.0 = heavy scrap pile)
                float initialTrash = Mathf.Clamp01(0.70f + n1 * 0.30f);

                // Clearings around central structures
                float distToSpire = Mathf.Sqrt(wx * wx + wz * wz);
                if (distToSpire < 13f)
                {
                    initialTrash = Mathf.Lerp(0.02f, initialTrash, Mathf.Clamp01(distToSpire / 13f));
                }

                float distToWorkshop = Vector2.Distance(new Vector2(wx, wz), new Vector2(18f, 14f));
                if (distToWorkshop < 10f)
                {
                    initialTrash = Mathf.Lerp(0.02f, initialTrash, Mathf.Clamp01(distToWorkshop / 10f));
                }

                TrashDensity[idx] = initialTrash;
                // Surface height is base pavement + trash layer thickness (up to 0.65m)
                Heights[idx] = baseGround + initialTrash * 0.65f;
                Grass[idx] = 0f;
                OasisProtected[idx] = 0;
            }
        }

        // 4. Construct Terrain Mesh & Material
        _BuildTerrainMesh();

        // 5. Construct Boulevards & Streetlamps
        _BuildRoadNetwork();

        // 6. Construct Monumental Climate Spire
        _BuildClimateSpire();

        // 7. Construct Eco-Hub Workshop
        _BuildWorkshop();

        // 8. Construct 65+ City Buildings
        _BuildCityMetropolis();

        _dirty = true;
        _UpdateMeshAndColors();
    }

    #region Terrain Mesh Construction
    private void _BuildTerrainMesh()
    {
        _meshFilter = GetComponent<MeshFilter>();
        if (_meshFilter == null) _meshFilter = gameObject.AddComponent<MeshFilter>();

        _meshRenderer = GetComponent<MeshRenderer>();
        if (_meshRenderer == null) _meshRenderer = gameObject.AddComponent<MeshRenderer>();

        _meshCollider = GetComponent<MeshCollider>();
        if (_meshCollider == null) _meshCollider = gameObject.AddComponent<MeshCollider>();

        int N = SolarisConfig.GRID_SIZE;
        int totalVerts = N * N;
        int totalQuads = (N - 1) * (N - 1);

        _vertices = new Vector3[totalVerts];
        _uvs = new Vector2[totalVerts];
        _colorBuffer = new Color[totalVerts];
        _triangles = new int[totalQuads * 6];

        for (int z = 0; z < N; z++)
        {
            for (int x = 0; x < N; x++)
            {
                int idx = z * N + x;
                float wx = SolarisConfig.GridToWorldX(x);
                float wz = SolarisConfig.GridToWorldZ(z);
                _vertices[idx] = new Vector3(wx, Heights[idx], wz);
                _uvs[idx] = new Vector2((float)x / (N - 1), (float)z / (N - 1));
            }
        }

        int t = 0;
        for (int z = 0; z < N - 1; z++)
        {
            for (int x = 0; x < N - 1; x++)
            {
                int i0 = z * N + x;
                int i1 = i0 + 1;
                int i2 = (z + 1) * N + x;
                int i3 = i2 + 1;

                // Clockwise winding (viewed from +Y, normal points up towards the sky)
                _triangles[t++] = i0;
                _triangles[t++] = i1;
                _triangles[t++] = i2;

                _triangles[t++] = i1;
                _triangles[t++] = i3;
                _triangles[t++] = i2;
            }
        }

        _terrainMesh = new Mesh();
        _terrainMesh.name = "Solaris_Procedural_Terrain";
        _terrainMesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        _terrainMesh.vertices = _vertices;
        _terrainMesh.uv = _uvs;
        _terrainMesh.triangles = _triangles;

        _splatMap = new Texture2D(N, N, TextureFormat.RGBA32, false);
        _splatMap.name = "Solaris_Dynamic_SplatMap";
        _splatMap.filterMode = FilterMode.Bilinear;
        _splatMap.wrapMode = TextureWrapMode.Clamp;

        _UpdateColormapPixels();

        _terrainMesh.colors = _colorBuffer;
        _terrainMesh.RecalculateNormals();
        _terrainMesh.RecalculateBounds();

        _meshFilter.sharedMesh = _terrainMesh;
        _meshCollider.sharedMesh = _terrainMesh;

        // Standard Tiled Terrain Material
        Shader stdShader = Shader.Find("Standard") ?? Shader.Find("Diffuse");
        _terrainMat = new Material(stdShader);

        Texture2D trashTex = null;
#if UNITY_EDITOR
        string texPath = "Assets/Textures/TrashDebris_Seamless.png";
        trashTex = UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>(texPath);
#endif
        if (trashTex == null)
        {
            trashTex = _GenerateTrashTexture(256);
        }
        trashTex.wrapMode = TextureWrapMode.Repeat;
        trashTex.filterMode = FilterMode.Bilinear;

        _terrainMat.mainTexture = trashTex;
        _terrainMat.mainTextureScale = new Vector2(48f, 48f);
        _meshRenderer.sharedMaterial = _terrainMat;
    }

    private void _UpdateColormapPixels()
    {
        int N = SolarisConfig.GRID_SIZE;
        for (int z = 0; z < N; z++)
        {
            for (int x = 0; x < N; x++)
            {
                int idx = z * N + x;
                float trash = TrashDensity != null ? TrashDensity[idx] : Mathf.Clamp01(Heights[idx] / 1.5f);
                float grass = Grass != null ? Grass[idx] : 0f;
                float road = (IsRoad != null && IsRoad[idx] == 1) ? 1f : 0f;

                // R = Trash, G = Grass, B = Road, A = 1
                _colorBuffer[idx] = new Color(trash, grass, road, 1f);
            }
        }

        if (_splatMap != null)
        {
            _splatMap.SetPixels(_colorBuffer);
            _splatMap.Apply(false);
        }
    }

    private void _UpdateMeshAndColors()
    {
        if (_terrainMesh == null) return;

        int total = SolarisConfig.GRID_SIZE * SolarisConfig.GRID_SIZE;
        for (int i = 0; i < total; i++)
        {
            _vertices[i].y = Heights[i];
        }

        _terrainMesh.vertices = _vertices;
        _terrainMesh.RecalculateNormals();

        _UpdateColormapPixels();
        _terrainMesh.colors = _colorBuffer;

        _dirty = false;
        _colliderDirty = true;
    }
    #endregion

    #region Road Network & Streetlamps
    private void _BuildRoadNetwork()
    {
        var roadRoot = new GameObject("Boulevard_Streetlamps");
        roadRoot.transform.SetParent(transform, false);

        Material poleMat = SolarisConfig.CreateMaterial(new Color(0.20f, 0.20f, 0.22f), 0.4f, 0.6f);
        Material lampHeadMat = SolarisConfig.CreateMaterial(new Color(1f, 0.95f, 0.8f), 0.1f, 0.9f, null, new Color(1f, 0.88f, 0.45f) * 2.8f);

        float[] lampCoords = { -180f, -150f, -120f, -90f, -60f, -32f, 32f, 60f, 90f, 120f, 150f, 180f };

        // North-South Boulevard lamps
        foreach (float z in lampCoords)
        {
            _CreateStreetlamp(roadRoot.transform, new Vector3(7.8f, 0f, z), 90f, poleMat, lampHeadMat);
            _CreateStreetlamp(roadRoot.transform, new Vector3(-7.8f, 0f, z), -90f, poleMat, lampHeadMat);
        }

        // East-West Boulevard lamps
        foreach (float x in lampCoords)
        {
            _CreateStreetlamp(roadRoot.transform, new Vector3(x, 0f, 7.8f), 0f, poleMat, lampHeadMat);
            _CreateStreetlamp(roadRoot.transform, new Vector3(x, 0f, -7.8f), 180f, poleMat, lampHeadMat);
        }
    }

    private void _CreateStreetlamp(Transform parent, Vector3 basePos, float yRot, Material poleMat, Material lampMat)
    {
        basePos.y = GetHeight(basePos.x, basePos.z);

        var lampGo = new GameObject("Streetlamp");
        lampGo.transform.SetParent(parent, false);
        lampGo.transform.position = basePos;
        lampGo.transform.rotation = Quaternion.Euler(0f, yRot, 0f);

        // Vertical Mast
        var pole = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        pole.name = "Pole";
        pole.transform.SetParent(lampGo.transform, false);
        pole.transform.localPosition = new Vector3(0f, 2.8f, 0f);
        pole.transform.localScale = new Vector3(0.24f, 2.8f, 0.24f);
        pole.GetComponent<MeshRenderer>().sharedMaterial = poleMat;

        // Curved Overhang Arm
        var arm = GameObject.CreatePrimitive(PrimitiveType.Cube);
        arm.name = "Arm";
        arm.transform.SetParent(lampGo.transform, false);
        arm.transform.localPosition = new Vector3(0f, 5.6f, -0.8f);
        arm.transform.localScale = new Vector3(0.18f, 0.16f, 1.6f);
        arm.GetComponent<MeshRenderer>().sharedMaterial = poleMat;

        // Glowing Lamp Fixture
        var head = GameObject.CreatePrimitive(PrimitiveType.Cube);
        head.name = "LampFixture";
        head.transform.SetParent(lampGo.transform, false);
        head.transform.localPosition = new Vector3(0f, 5.48f, -1.45f);
        head.transform.localScale = new Vector3(0.42f, 0.18f, 0.42f);
        head.GetComponent<MeshRenderer>().sharedMaterial = lampMat;

        // Point Light
        var lightGo = new GameObject("LampLight");
        lightGo.transform.SetParent(head.transform, false);
        lightGo.transform.localPosition = new Vector3(0f, -0.2f, 0f);
        var pt = lightGo.AddComponent<Light>();
        pt.type = LightType.Point;
        pt.range = 14f;
        pt.intensity = 1.6f;
        pt.color = new Color(1f, 0.92f, 0.68f);

        // Register collider bounds
        BuildingColliders.Add(new Bounds(basePos + Vector3.up * 3f, new Vector3(1.2f, 6f, 1.2f)));
    }
    #endregion

    #region Central Climate Spire
    private void _BuildClimateSpire()
    {
        var spireRoot = new GameObject("ClimateSpire");
        spireRoot.transform.SetParent(transform, false);
        spireRoot.transform.position = Vector3.zero;

        Material metalMat = SolarisConfig.CreateMaterial(new Color(0.16f, 0.18f, 0.22f), 0.5f, 0.5f);
        Material conduitGlow = SolarisConfig.CreateMaterial(new Color(0f, 0.85f, 1f), 0.1f, 0.9f, null, new Color(0f, 0.9f, 1f) * 3f);
        Material solarMat = SolarisConfig.CreateMaterial(new Color(0.08f, 0.18f, 0.42f), 0.4f, 0.8f);

        // 1. Monumental Base Platform (Radius 14m, Height 2.5m)
        var basePlat = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        basePlat.name = "SpireBasePlatform";
        basePlat.transform.SetParent(spireRoot.transform, false);
        basePlat.transform.localPosition = new Vector3(0f, 1.25f, 0f);
        basePlat.transform.localScale = new Vector3(28f, 1.25f, 28f);
        basePlat.GetComponent<MeshRenderer>().sharedMaterial = metalMat;

        // 2. Multi-tier Tower Structure
        // Tier 1
        var t1 = GameObject.CreatePrimitive(PrimitiveType.Cube);
        t1.name = "Tower_Tier1";
        t1.transform.SetParent(spireRoot.transform, false);
        t1.transform.localPosition = new Vector3(0f, 7.5f, 0f);
        t1.transform.localScale = new Vector3(8f, 10f, 8f);
        t1.GetComponent<MeshRenderer>().sharedMaterial = metalMat;

        // Tier 2
        var t2 = GameObject.CreatePrimitive(PrimitiveType.Cube);
        t2.name = "Tower_Tier2";
        t2.transform.SetParent(spireRoot.transform, false);
        t2.transform.localPosition = new Vector3(0f, 17f, 0f);
        t2.transform.localScale = new Vector3(5.5f, 9f, 5.5f);
        t2.GetComponent<MeshRenderer>().sharedMaterial = metalMat;

        // Spire Needle
        var needle = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        needle.name = "SpireNeedle";
        needle.transform.SetParent(spireRoot.transform, false);
        needle.transform.localPosition = new Vector3(0f, 28.5f, 0f);
        needle.transform.localScale = new Vector3(1.4f, 7f, 1.4f);
        needle.GetComponent<MeshRenderer>().sharedMaterial = metalMat;

        // 3. 4 Cyber Pylons with Glowing Conduit Rings
        Vector3[] pylonOffsets = {
            new Vector3(8.5f, 0f, 8.5f),
            new Vector3(-8.5f, 0f, 8.5f),
            new Vector3(-8.5f, 0f, -8.5f),
            new Vector3(8.5f, 0f, -8.5f)
        };

        for (int i = 0; i < pylonOffsets.Length; i++)
        {
            var pylon = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            pylon.name = $"Pylon_{i + 1}";
            pylon.transform.SetParent(spireRoot.transform, false);
            pylon.transform.localPosition = pylonOffsets[i] + Vector3.up * 6f;
            pylon.transform.localScale = new Vector3(1.2f, 6f, 1.2f);
            pylon.GetComponent<MeshRenderer>().sharedMaterial = metalMat;

            // Glowing conduit ring
            var ring = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            ring.name = "EnergyRing";
            ring.transform.SetParent(pylon.transform, false);
            ring.transform.localPosition = new Vector3(0f, 0.4f, 0f);
            ring.transform.localScale = new Vector3(1.4f, 0.15f, 1.4f);
            ring.GetComponent<MeshRenderer>().sharedMaterial = conduitGlow;
        }

        // 4. Rotating Energy Crown at Spire Tip
        var crownGo = new GameObject("EnergyCrown");
        crownGo.transform.SetParent(spireRoot.transform, false);
        crownGo.transform.localPosition = new Vector3(0f, 36.5f, 0f);
        _spireEnergyCore = crownGo.transform;

        var coreCrystal = GameObject.CreatePrimitive(PrimitiveType.Cube);
        coreCrystal.name = "CoreCrystal";
        coreCrystal.transform.SetParent(crownGo.transform, false);
        coreCrystal.transform.localScale = new Vector3(1.8f, 2.6f, 1.8f);
        coreCrystal.transform.rotation = Quaternion.Euler(45f, 45f, 0f);
        coreCrystal.GetComponent<MeshRenderer>().sharedMaterial = conduitGlow;

        var spireLight = crownGo.AddComponent<Light>();
        spireLight.type = LightType.Point;
        spireLight.range = 38f;
        spireLight.intensity = 3.5f;
        spireLight.color = new Color(0.2f, 0.9f, 1f);

        // 5. 4 Massive Solar Collector Wings
        for (int i = 0; i < 4; i++)
        {
            float angle = i * 90f + 45f;
            var wing = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wing.name = $"SolarWing_{i + 1}";
            wing.transform.SetParent(spireRoot.transform, false);
            wing.transform.localRotation = Quaternion.Euler(30f, angle, 0f);
            wing.transform.localPosition = Quaternion.Euler(0f, angle, 0f) * new Vector3(0f, 14f, 7f);
            wing.transform.localScale = new Vector3(4.5f, 0.3f, 8f);
            wing.GetComponent<MeshRenderer>().sharedMaterial = solarMat;
        }

        // Register Colliders & Structure Pos
        BuildingColliders.Add(new Bounds(new Vector3(0f, 14f, 0f), new Vector3(26f, 30f, 26f)));
        StructurePositions.Add(Vector3.zero);
    }
    #endregion

    #region Eco-Hub Workshop
    private void _BuildWorkshop()
    {
        Vector3 wPos = SolarisConfig.WORKSHOP_POS;
        wPos.y = GetHeight(wPos.x, wPos.z);

        var wsGo = new GameObject("EcoHub_Workshop");
        wsGo.transform.SetParent(transform, false);
        wsGo.transform.position = wPos;

        Texture2D wsTex = LoadBuildingTexture("face_workshop");
        Material wsMat = SolarisConfig.CreateMaterial(Color.white, 0.2f, 0.5f, wsTex);
        Material trimMat = SolarisConfig.CreateMaterial(new Color(0.18f, 0.72f, 0.65f), 0.3f, 0.7f);
        Material crystalMat = SolarisConfig.CreateMaterial(new Color(0.2f, 1f, 0.85f), 0.1f, 0.9f, null, new Color(0.2f, 1f, 0.85f) * 3.2f);

        // Main Modular Building Body
        var body = GameObject.CreatePrimitive(PrimitiveType.Cube);
        body.name = "WorkshopBuilding";
        body.transform.SetParent(wsGo.transform, false);
        body.transform.localPosition = new Vector3(0f, 3.25f, 0f);
        body.transform.localScale = new Vector3(13f, 6.5f, 10f);
        body.GetComponent<MeshRenderer>().sharedMaterial = wsMat;

        // Solar / Industrial Roof Plate
        var roof = GameObject.CreatePrimitive(PrimitiveType.Cube);
        roof.name = "WorkshopRoof";
        roof.transform.SetParent(wsGo.transform, false);
        roof.transform.localPosition = new Vector3(0f, 6.6f, 0f);
        roof.transform.localScale = new Vector3(13.8f, 0.4f, 10.8f);
        roof.GetComponent<MeshRenderer>().sharedMaterial = trimMat;

        // Overhang Entrance Canopy
        var canopy = GameObject.CreatePrimitive(PrimitiveType.Cube);
        canopy.name = "EntranceCanopy";
        canopy.transform.SetParent(wsGo.transform, false);
        canopy.transform.localPosition = new Vector3(0f, 2.4f, 5.6f);
        canopy.transform.localScale = new Vector3(6f, 0.25f, 2.4f);
        canopy.GetComponent<MeshRenderer>().sharedMaterial = trimMat;

        // Rotating Turquoise Radar Dish
        var dishMount = new GameObject("RadarDish_Mount");
        dishMount.transform.SetParent(wsGo.transform, false);
        dishMount.transform.localPosition = new Vector3(-3.8f, 6.8f, -2.5f);
        _workshopRadarDish = dishMount.transform;

        var dishPole = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        dishPole.name = "DishMast";
        dishPole.transform.SetParent(dishMount.transform, false);
        dishPole.transform.localPosition = new Vector3(0f, 0.8f, 0f);
        dishPole.transform.localScale = new Vector3(0.2f, 0.8f, 0.2f);
        dishPole.GetComponent<MeshRenderer>().sharedMaterial = trimMat;

        var dishPlate = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        dishPlate.name = "ParabolicDish";
        dishPlate.transform.SetParent(dishMount.transform, false);
        dishPlate.transform.localPosition = new Vector3(0f, 1.8f, 0f);
        dishPlate.transform.localRotation = Quaternion.Euler(38f, 0f, 0f);
        dishPlate.transform.localScale = new Vector3(2.4f, 0.12f, 2.4f);
        dishPlate.GetComponent<MeshRenderer>().sharedMaterial = trimMat;

        // Floating & Pulsing Energy Crystal
        var crystalGo = new GameObject("WorkshopEnergyCrystal");
        crystalGo.transform.SetParent(wsGo.transform, false);
        _crystalBasePos = new Vector3(0f, 8.8f, 0f);
        crystalGo.transform.localPosition = _crystalBasePos;
        _workshopCrystal = crystalGo.transform;

        var crystalMesh = GameObject.CreatePrimitive(PrimitiveType.Cube);
        crystalMesh.name = "CrystalMesh";
        crystalMesh.transform.SetParent(crystalGo.transform, false);
        crystalMesh.transform.localScale = new Vector3(1.2f, 1.8f, 1.2f);
        crystalMesh.transform.localRotation = Quaternion.Euler(45f, 45f, 45f);
        crystalMesh.GetComponent<MeshRenderer>().sharedMaterial = crystalMat;

        var crystalLight = crystalGo.AddComponent<Light>();
        crystalLight.type = LightType.Point;
        crystalLight.range = 16f;
        crystalLight.intensity = 2.4f;
        crystalLight.color = new Color(0.2f, 1f, 0.85f);

        // Register bounds and position
        BuildingColliders.Add(new Bounds(wPos + Vector3.up * 3.5f, new Vector3(14.5f, 7.5f, 11.5f)));
        StructurePositions.Add(wPos);
    }
    #endregion

    #region 65+ City Buildings
    private void _BuildCityMetropolis()
    {
        var cityRoot = new GameObject("City_Buildings");
        cityRoot.transform.SetParent(transform, false);

        // 4 Quadrants layout: 18 buildings per quadrant = 72 detailed buildings
        // NE Quadrant (+X, +Z)
        _BuildQuadrant(cityRoot.transform, 1, 1f, 1f);
        // NW Quadrant (-X, +Z)
        _BuildQuadrant(cityRoot.transform, 2, -1f, 1f);
        // SW Quadrant (-X, -Z)
        _BuildQuadrant(cityRoot.transform, 3, -1f, -1f);
        // SE Quadrant (+X, -Z)
        _BuildQuadrant(cityRoot.transform, 4, 1f, -1f);
    }

    private void _BuildQuadrant(Transform parent, int quadId, float signX, float signZ)
    {
        float[] xSlots = { 28f, 62f, 98f, 134f, 170f };
        float[] zSlots = { 28f, 62f, 98f, 134f, 170f };

        string[] faceTextures = {
            "face_office_1", "face_office_2",
            "face_apartment_1", "face_apartment_2",
            "face_shop_1", "face_factory"
        };

        Material roofMat = SolarisConfig.CreateMaterial(new Color(0.22f, 0.22f, 0.25f), 0.3f, 0.4f, LoadBuildingTexture("roof_industrial"));
        Material acMat = SolarisConfig.CreateMaterial(new Color(0.35f, 0.35f, 0.38f), 0.5f, 0.5f);
        Material antennaMat = SolarisConfig.CreateMaterial(new Color(0.18f, 0.18f, 0.20f), 0.7f, 0.6f);
        Material beaconGlow = SolarisConfig.CreateMaterial(Color.red, 0.1f, 0.9f, null, Color.red * 3.5f);
        Material solarMat = SolarisConfig.CreateMaterial(new Color(0.08f, 0.22f, 0.5f), 0.4f, 0.8f);

        int count = 0;
        for (int row = 0; row < zSlots.Length; row++)
        {
            for (int col = 0; col < xSlots.Length; col++)
            {
                if (count >= 18) break;

                // Deterministic pseudo-random seed per building
                int seed = quadId * 100 + row * 10 + col;
                float jitterX = Mathf.Sin(seed * 1.7f) * 4.5f;
                float jitterZ = Mathf.Cos(seed * 2.3f) * 4.5f;

                float bx = (xSlots[col] + jitterX) * signX;
                float bz = (zSlots[row] + jitterZ) * signZ;

                // Avoid collision with Workshop at (18, 14)
                if (Vector2.Distance(new Vector2(bx, bz), new Vector2(18f, 14f)) < 16f)
                    continue;

                // Avoid collision with Spire at (0, 0)
                if (Mathf.Abs(bx) < 18f && Mathf.Abs(bz) < 18f)
                    continue;

                count++;

                int typeIdx = (seed % faceTextures.Length + faceTextures.Length) % faceTextures.Length;
                string faceName = faceTextures[typeIdx];

                float width = 11f + (seed % 5) * 1.4f;
                float depth = 11f + ((seed * 3) % 5) * 1.4f;
                float height = 14f + ((seed * 7) % 18) * 1.5f; // 14m to 40m

                Vector3 basePos = new Vector3(bx, GetHeight(bx, bz), bz);

                var bGo = new GameObject($"Building_Q{quadId}_{count}_{faceName}");
                bGo.transform.SetParent(parent, false);
                bGo.transform.position = basePos;

                // Building Body
                var body = GameObject.CreatePrimitive(PrimitiveType.Cube);
                body.name = "Body";
                body.transform.SetParent(bGo.transform, false);
                body.transform.localPosition = new Vector3(0f, height * 0.5f, 0f);
                body.transform.localScale = new Vector3(width, height, depth);

                Texture2D fTex = LoadBuildingTexture(faceName);
                Material bodyMat = SolarisConfig.CreateMaterial(Color.white, 0.2f, 0.45f, fTex);
                bodyMat.mainTextureScale = new Vector2(width / 5.5f, height / 4f);
                body.GetComponent<MeshRenderer>().sharedMaterial = bodyMat;

                // Roof Slab
                var roof = GameObject.CreatePrimitive(PrimitiveType.Cube);
                roof.name = "Roof";
                roof.transform.SetParent(bGo.transform, false);
                roof.transform.localPosition = new Vector3(0f, height + 0.2f, 0f);
                roof.transform.localScale = new Vector3(width + 0.4f, 0.4f, depth + 0.4f);
                roof.GetComponent<MeshRenderer>().sharedMaterial = roofMat;

                // Rooftop AC Unit
                var ac = GameObject.CreatePrimitive(PrimitiveType.Cube);
                ac.name = "RooftopAC";
                ac.transform.SetParent(bGo.transform, false);
                ac.transform.localPosition = new Vector3(width * 0.2f, height + 0.9f, depth * 0.2f);
                ac.transform.localScale = new Vector3(2.4f, 1.2f, 2.0f);
                ac.GetComponent<MeshRenderer>().sharedMaterial = acMat;

                // Rooftop Antenna Mast
                var antenna = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                antenna.name = "AntennaMast";
                antenna.transform.SetParent(bGo.transform, false);
                antenna.transform.localPosition = new Vector3(-width * 0.25f, height + 2.5f, -depth * 0.25f);
                antenna.transform.localScale = new Vector3(0.18f, 2.5f, 0.18f);
                antenna.GetComponent<MeshRenderer>().sharedMaterial = antennaMat;

                // Red Warning Beacon
                var beacon = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                beacon.name = "WarningBeacon";
                beacon.transform.SetParent(antenna.transform, false);
                beacon.transform.localPosition = new Vector3(0f, 1.05f, 0f);
                beacon.transform.localScale = new Vector3(0.35f, 0.35f, 0.35f);
                beacon.GetComponent<MeshRenderer>().sharedMaterial = beaconGlow;

                // Tilted Solar Panel Array on rooftop
                var solar = GameObject.CreatePrimitive(PrimitiveType.Cube);
                solar.name = "RooftopSolarArray";
                solar.transform.SetParent(bGo.transform, false);
                solar.transform.localPosition = new Vector3(-width * 0.15f, height + 0.8f, depth * 0.2f);
                solar.transform.localRotation = Quaternion.Euler(25f, 0f, 0f);
                solar.transform.localScale = new Vector3(3.8f, 0.15f, 2.5f);
                solar.GetComponent<MeshRenderer>().sharedMaterial = solarMat;

                Vector3 solarWorldPos = bGo.transform.TransformPoint(solar.transform.localPosition);
                StructurePositions.Add(solarWorldPos);

                // Buried state: ~50% of buildings start partially/heavily buried
                bool isBuried = (count % 2 == 1);
                int gx = SolarisConfig.WorldToGridX(bx);
                int gz = SolarisConfig.WorldToGridZ(bz);

                if (isBuried)
                {
                    float buriedDepth = -UnityEngine.Random.Range(2.2f, 4.0f);
                    bGo.transform.position = basePos + Vector3.up * buriedDepth;

                    _buriedBuildings.Add(new BuriedBuilding
                    {
                        Root = bGo,
                        BasePos = basePos,
                        BuriedY = buriedDepth,
                        CurrentY = buriedDepth,
                        GridX = gx,
                        GridZ = gz,
                        IsRestored = false,
                        HasSolarPanels = true,
                        RiseSpeed = UnityEngine.Random.Range(2.5f, 4.0f)
                    });
                }
                else
                {
                    SolarPanelsRestored++;
                }

                // Register Building Collider
                BuildingColliders.Add(new Bounds(basePos + Vector3.up * (height * 0.5f), new Vector3(width, height, depth)));
            }
        }
    }
    #endregion

    #region Texture Loading & Fallback Generator
    public static Texture2D LoadBuildingTexture(string name)
    {
        if (_textureCache.TryGetValue(name, out var cached) && cached != null)
            return cached;

        Texture2D loaded = Resources.Load<Texture2D>("Textures/BuildingFaces/" + name);
        if (loaded != null)
        {
            _textureCache[name] = loaded;
            return loaded;
        }

        Texture2D procedural = _GenerateProceduralTexture(name);
        _textureCache[name] = procedural;
        return procedural;
    }

    private static Texture2D _GenerateProceduralTexture(string name)
    {
        int size = 128;
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.name = "Proc_" + name;
        tex.wrapMode = TextureWrapMode.Repeat;
        tex.filterMode = FilterMode.Bilinear;

        Color[] pixels = new Color[size * size];

        Color bg = new Color(0.20f, 0.22f, 0.26f);
        Color windowLit = new Color(0.95f, 0.85f, 0.55f);
        Color windowDim = new Color(0.12f, 0.24f, 0.35f);
        Color accent = new Color(0.15f, 0.75f, 0.70f);

        if (name.Contains("apartment"))
        {
            bg = new Color(0.38f, 0.30f, 0.25f);
            windowLit = new Color(1.0f, 0.90f, 0.65f);
        }
        else if (name.Contains("workshop"))
        {
            bg = new Color(0.18f, 0.25f, 0.28f);
            accent = new Color(0.12f, 0.92f, 0.78f);
        }
        else if (name.Contains("factory") || name.Contains("roof"))
        {
            bg = new Color(0.16f, 0.16f, 0.18f);
        }

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                Color c = bg;
                if (name.Contains("workshop") && (y < 24 || (x + y) % 32 < 6))
                {
                    c = accent;
                }
                else if (name.Contains("office") || name.Contains("apartment") || name.Contains("shop"))
                {
                    // Window grid pattern
                    int cellX = x % 16;
                    int cellY = y % 16;
                    if (cellX >= 4 && cellX <= 12 && cellY >= 4 && cellY <= 12)
                    {
                        bool isLit = ((x / 16 + y / 16 * 7) % 3) != 0;
                        c = isLit ? windowLit : windowDim;
                    }
                }
                else if (name.Contains("road"))
                {
                    c = new Color(0.18f, 0.18f, 0.20f);
                }

                pixels[y * size + x] = c;
            }
        }

        tex.SetPixels(pixels);
        tex.Apply(false);
        return tex;
    }

    private Texture2D _GenerateTrashTexture(int size)
    {
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.name = "Proc_TrashDebris";
        tex.wrapMode = TextureWrapMode.Repeat;
        tex.filterMode = FilterMode.Bilinear;

        Color cardboardBrown = new Color(0.58f, 0.44f, 0.28f);
        Color scrapSilver = new Color(0.78f, 0.82f, 0.88f);
        Color rustOrange = new Color(0.72f, 0.36f, 0.14f);
        Color plasticRed = new Color(0.88f, 0.22f, 0.18f);
        Color plasticYellow = new Color(0.96f, 0.82f, 0.15f);
        Color plasticCyan = new Color(0.05f, 0.82f, 0.85f);
        Color circuitGreen = new Color(0.12f, 0.65f, 0.35f);
        Color darkGrime = new Color(0.24f, 0.22f, 0.20f);

        Color[] pix = new Color[size * size];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float n1 = Mathf.PerlinNoise(x * 0.08f, y * 0.08f);
                float n2 = Mathf.PerlinNoise(x * 0.22f + 40f, y * 0.22f + 40f);
                float fine = Mathf.PerlinNoise(x * 0.65f + 120f, y * 0.65f + 120f);

                // Base crumpled waste / cardboard and dust grime
                Color c = Color.Lerp(darkGrime, cardboardBrown, n1 * 0.85f);

                // Rust & scrap metal sheets
                if (n2 > 0.60f)
                {
                    c = Color.Lerp(c, rustOrange, (n2 - 0.60f) * 2.5f);
                }

                // Shiny aluminum cans & tin foil
                if (fine > 0.80f)
                {
                    c = Color.Lerp(c, scrapSilver, 0.90f);
                }
                // Recycled plastic packaging & chips
                else if (fine < 0.25f)
                {
                    int h = (x * 47 + y * 73) % 100;
                    if (h < 22) c = plasticRed;
                    else if (h < 45) c = plasticYellow;
                    else if (h < 68) c = plasticCyan;
                    else if (h < 85) c = circuitGreen;
                }

                pix[y * size + x] = c;
            }
        }
        tex.SetPixels(pix);
        tex.Apply(false);
        return tex;
    }

    private Texture2D _GenerateRoadTexture(int size)
    {
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.name = "Proc_RoadAsphalt";
        tex.wrapMode = TextureWrapMode.Repeat;
        tex.filterMode = FilterMode.Bilinear;

        Color asphalt = new Color(0.14f, 0.14f, 0.16f);
        Color yellowStripe = new Color(0.96f, 0.76f, 0.12f);
        Color curbEdge = new Color(0.32f, 0.33f, 0.36f);

        Color[] pix = new Color[size * size];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float grain = Mathf.PerlinNoise(x * 0.5f, y * 0.5f) * 0.06f;
                Color c = new Color(asphalt.r + grain, asphalt.g + grain, asphalt.b + grain);

                int midX = size / 2;
                bool isStripeBand = (x >= midX - 8 && x <= midX - 3) || (x >= midX + 3 && x <= midX + 8);
                bool isDash = (y % 32 < 20);
                if (isStripeBand && isDash)
                {
                    c = yellowStripe;
                }

                if (x == 4 || x == 5 || x == size - 5 || x == size - 6)
                {
                    c = Color.white * 0.85f;
                }
                else if (x < 4 || x > size - 5)
                {
                    c = curbEdge;
                }

                pix[y * size + x] = c;
            }
        }
        tex.SetPixels(pix);
        tex.Apply(false);
        return tex;
    }

    private Texture2D _GenerateGrassTexture(int size)
    {
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.name = "Proc_RestoredGrass";
        tex.wrapMode = TextureWrapMode.Repeat;
        tex.filterMode = FilterMode.Bilinear;

        Color grassDark = new Color(0.12f, 0.48f, 0.20f);
        Color grassLight = new Color(0.22f, 0.74f, 0.32f);
        Color flowerYellow = new Color(0.98f, 0.90f, 0.35f);

        Color[] pix = new Color[size * size];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float n = Mathf.PerlinNoise(x * 0.2f, y * 0.2f);
                Color c = Color.Lerp(grassDark, grassLight, n);

                int h = (x * 43 + y * 71) % 400;
                if (h == 25) c = flowerYellow;

                pix[y * size + x] = c;
            }
        }
        tex.SetPixels(pix);
        tex.Apply(false);
        return tex;
    }

    private Texture2D _GenerateGroundTexture(int size)
    {
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.name = "Proc_ConcreteGround";
        tex.wrapMode = TextureWrapMode.Repeat;
        tex.filterMode = FilterMode.Bilinear;

        Color concrete = new Color(0.32f, 0.33f, 0.35f);
        Color joint = new Color(0.20f, 0.21f, 0.23f);

        Color[] pix = new Color[size * size];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float grain = Mathf.PerlinNoise(x * 0.35f, y * 0.35f) * 0.04f;
                Color c = new Color(concrete.r + grain, concrete.g + grain, concrete.b + grain);

                if (x % 32 == 0 || y % 32 == 0)
                {
                    c = joint;
                }

                pix[y * size + x] = c;
            }
        }
        tex.SetPixels(pix);
        tex.Apply(false);
        return tex;
    }
    #endregion

    #region Public Simulation & Interaction Methods
    public float GetHeight(float x, float z)
    {
        float gx = (x + SolarisConfig.HALF_WORLD) / SolarisConfig.CELL_SIZE;
        float gz = (z + SolarisConfig.HALF_WORLD) / SolarisConfig.CELL_SIZE;

        int x0 = Mathf.Clamp(Mathf.FloorToInt(gx), 0, SolarisConfig.GRID_SIZE - 2);
        int z0 = Mathf.Clamp(Mathf.FloorToInt(gz), 0, SolarisConfig.GRID_SIZE - 2);

        float tx = Mathf.Clamp01(gx - x0);
        float tz = Mathf.Clamp01(gz - z0);

        int N = SolarisConfig.GRID_SIZE;
        float h00 = Heights[z0 * N + x0];
        float h10 = Heights[z0 * N + (x0 + 1)];
        float h01 = Heights[(z0 + 1) * N + x0];
        float h11 = Heights[(z0 + 1) * N + (x0 + 1)];

        float h0 = Mathf.Lerp(h00, h10, tx);
        float h1 = Mathf.Lerp(h01, h11, tx);
        return Mathf.Lerp(h0, h1, tz);
    }

    public float VacuumAtPoint(Vector3 hitPoint, Vector3 playerPos, float brushRadius, float power, float dt, EcoReclaimerPlayer player = null)
    {
        int minX = SolarisConfig.WorldToGridX(hitPoint.x - brushRadius);
        int maxX = SolarisConfig.WorldToGridX(hitPoint.x + brushRadius);
        int minZ = SolarisConfig.WorldToGridZ(hitPoint.z - brushRadius);
        int maxZ = SolarisConfig.WorldToGridZ(hitPoint.z + brushRadius);

        float totalRemoved = 0f;
        int N = SolarisConfig.GRID_SIZE;

        for (int z = minZ; z <= maxZ; z++)
        {
            for (int x = minX; x <= maxX; x++)
            {
                int idx = z * N + x;
                float wx = SolarisConfig.GridToWorldX(x);
                float wz = SolarisConfig.GridToWorldZ(z);

                float dist = Vector2.Distance(new Vector2(wx, wz), new Vector2(hitPoint.x, hitPoint.z));
                if (dist <= brushRadius)
                {
                    float falloff = Mathf.SmoothStep(1f, 0f, dist / brushRadius);
                    float removeAmount = power * dt * falloff;

                    if (TrashDensity != null && TrashDensity[idx] > 0.001f)
                    {
                        float actual = Mathf.Min(TrashDensity[idx], removeAmount);
                        TrashDensity[idx] -= actual;
                        Heights[idx] = BaseHeights[idx] + TrashDensity[idx] * 0.65f;
                        totalRemoved += actual;

                        if (TrashDensity[idx] < 0.35f)
                        {
                            Grass[idx] = Mathf.Clamp01(Grass[idx] + actual * 0.45f);
                        }
                        _dirty = true;
                    }
                }
            }
        }

        return totalRemoved;
    }

    public float VacuumAt(Vector3 origin, Vector3 forward, float range, float power, float dt)
    {
        Vector3 aimPoint = origin + forward * (range * 0.65f);
        aimPoint.y = GetHeight(aimPoint.x, aimPoint.z);
        return VacuumAtPoint(aimPoint, origin, range * 0.45f, power, dt, null);
    }

    public float CleanCircle(Vector3 center, float radius, float amount)
    {
        int minX = SolarisConfig.WorldToGridX(center.x - radius);
        int maxX = SolarisConfig.WorldToGridX(center.x + radius);
        int minZ = SolarisConfig.WorldToGridZ(center.z - radius);
        int maxZ = SolarisConfig.WorldToGridZ(center.z + radius);

        float totalRemoved = 0f;
        int N = SolarisConfig.GRID_SIZE;

        for (int z = minZ; z <= maxZ; z++)
        {
            for (int x = minX; x <= maxX; x++)
            {
                int idx = z * N + x;
                float wx = SolarisConfig.GridToWorldX(x);
                float wz = SolarisConfig.GridToWorldZ(z);

                float dist = Vector2.Distance(new Vector2(wx, wz), new Vector2(center.x, center.z));
                if (dist <= radius)
                {
                    float falloff = Mathf.SmoothStep(1f, 0f, dist / radius);
                    float cellAmount = amount * falloff;

                    if (TrashDensity != null && TrashDensity[idx] > 0.001f)
                    {
                        float actual = Mathf.Min(TrashDensity[idx], cellAmount);
                        TrashDensity[idx] -= actual;
                        Heights[idx] = BaseHeights[idx] + TrashDensity[idx] * 0.65f;
                        totalRemoved += actual;

                        if (TrashDensity[idx] < 0.35f)
                        {
                            Grass[idx] = Mathf.Clamp01(Grass[idx] + falloff * 0.5f);
                        }
                        _dirty = true;
                    }
                }
            }
        }

        return totalRemoved;
    }

    public void RecontaminateCircle(Vector3 center, float radius, float rate, float dt)
    {
        int minX = SolarisConfig.WorldToGridX(center.x - radius);
        int maxX = SolarisConfig.WorldToGridX(center.x + radius);
        int minZ = SolarisConfig.WorldToGridZ(center.z - radius);
        int maxZ = SolarisConfig.WorldToGridZ(center.z + radius);

        int N = SolarisConfig.GRID_SIZE;

        for (int z = minZ; z <= maxZ; z++)
        {
            for (int x = minX; x <= maxX; x++)
            {
                int idx = z * N + x;
                if (OasisProtected[idx] == 1) continue;

                float wx = SolarisConfig.GridToWorldX(x);
                float wz = SolarisConfig.GridToWorldZ(z);

                float dist = Vector2.Distance(new Vector2(wx, wz), new Vector2(center.x, center.z));
                if (dist <= radius)
                {
                    float falloff = 1f - (dist / radius);
                    float add = rate * dt * falloff;

                    if (TrashDensity != null && TrashDensity[idx] < 1f)
                    {
                        TrashDensity[idx] = Mathf.Min(1f, TrashDensity[idx] + add);
                        Heights[idx] = BaseHeights[idx] + TrashDensity[idx] * 0.65f;
                        Grass[idx] = Mathf.Max(0f, Grass[idx] - rate * 0.1f * dt);
                        _dirty = true;
                    }
                }
            }
        }
    }

    public bool PlantTree(Vector3 pos)
    {
        if (OasisTrees.Count >= 120) return false;

        pos.y = GetHeight(pos.x, pos.z);

        Transform oasisRoot = transform.Find("Oasis_Trees_Root");
        if (oasisRoot == null)
        {
            var go = new GameObject("Oasis_Trees_Root");
            go.transform.SetParent(transform, false);
            oasisRoot = go.transform;
        }

        var treeGo = new GameObject($"OasisTree_{OasisTrees.Count + 1}");
        treeGo.transform.SetParent(oasisRoot, false);
        treeGo.transform.position = pos;

        Material barkMat = SolarisConfig.CreateMaterial(new Color(0.38f, 0.24f, 0.14f), 0.1f, 0.2f);
        Material foliageMat = SolarisConfig.CreateMaterial(new Color(0.12f, 0.72f, 0.25f), 0.05f, 0.35f, null, new Color(0.04f, 0.25f, 0.08f));
        Material bioRingMat = SolarisConfig.CreateMaterial(new Color(0.1f, 0.95f, 0.45f, 0.4f), 0.05f, 0.2f, null, new Color(0.15f, 0.90f, 0.45f) * 2.2f);

        // 3D Trunk
        var trunk = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        trunk.name = "Trunk";
        trunk.transform.SetParent(treeGo.transform, false);
        trunk.transform.localPosition = new Vector3(0f, 2.0f, 0f);
        trunk.transform.localScale = new Vector3(0.7f, 2.0f, 0.7f);
        trunk.GetComponent<MeshRenderer>().sharedMaterial = barkMat;

        // 3-Tier Lush Foliage
        Vector3[] foliagePos = {
            new Vector3(0f, 3.8f, 0f),
            new Vector3(0f, 5.2f, 0f),
            new Vector3(0f, 6.4f, 0f)
        };
        Vector3[] foliageScale = {
            new Vector3(3.6f, 2.6f, 3.6f),
            new Vector3(2.8f, 2.2f, 2.8f),
            new Vector3(1.8f, 1.8f, 1.8f)
        };

        for (int i = 0; i < 3; i++)
        {
            var fol = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            fol.name = $"FoliageTier_{i + 1}";
            fol.transform.SetParent(treeGo.transform, false);
            fol.transform.localPosition = foliagePos[i];
            fol.transform.localScale = foliageScale[i];
            fol.GetComponent<MeshRenderer>().sharedMaterial = foliageMat;
            SolarisConfig.SafeDestroy(fol.GetComponent<Collider>());
        }

        // Glowing 18m Emerald Bio-Field Ring on the Ground
        var ring = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        ring.name = "BioField_Ring";
        ring.transform.SetParent(treeGo.transform, false);
        ring.transform.localPosition = new Vector3(0f, 0.06f, 0f);
        ring.transform.localScale = new Vector3(SolarisConfig.OASIS_RADIUS * 2f, 0.02f, SolarisConfig.OASIS_RADIUS * 2f);
        ring.GetComponent<MeshRenderer>().sharedMaterial = bioRingMat;
        SolarisConfig.SafeDestroy(ring.GetComponent<Collider>());

        var ringLight = ring.AddComponent<Light>();
        ringLight.type = LightType.Point;
        ringLight.range = SolarisConfig.OASIS_RADIUS;
        ringLight.intensity = 1.2f;
        ringLight.color = new Color(0.2f, 0.95f, 0.45f);

        // Register Tree & Protect Oasis Area
        OasisTrees.Add(pos);

        int minX = SolarisConfig.WorldToGridX(pos.x - SolarisConfig.OASIS_RADIUS);
        int maxX = SolarisConfig.WorldToGridX(pos.x + SolarisConfig.OASIS_RADIUS);
        int minZ = SolarisConfig.WorldToGridZ(pos.z - SolarisConfig.OASIS_RADIUS);
        int maxZ = SolarisConfig.WorldToGridZ(pos.z + SolarisConfig.OASIS_RADIUS);

        int N = SolarisConfig.GRID_SIZE;
        for (int z = minZ; z <= maxZ; z++)
        {
            for (int x = minX; x <= maxX; x++)
            {
                float wx = SolarisConfig.GridToWorldX(x);
                float wz = SolarisConfig.GridToWorldZ(z);
                if (Vector2.Distance(new Vector2(wx, wz), new Vector2(pos.x, pos.z)) <= SolarisConfig.OASIS_RADIUS)
                {
                    OasisProtected[z * N + x] = 1;
                }
            }
        }

        CleanCircle(pos, 7.5f, 0.45f);
        _dirty = true;
        return true;
    }

    public void SpawnOasisCluster(Vector3 center)
    {
        PlantTree(center + new Vector3(2.5f, 0f, 1.2f));
        PlantTree(center + new Vector3(-2.2f, 0f, 2.0f));
        PlantTree(center + new Vector3(-1.0f, 0f, -2.5f));
        CleanCircle(center, 18f, 0.65f);
    }

    public bool IsInOasis(Vector3 pos)
    {
        Vector2 p = new Vector2(pos.x, pos.z);
        for (int i = 0; i < OasisTrees.Count; i++)
        {
            Vector2 t = new Vector2(OasisTrees[i].x, OasisTrees[i].z);
            if (Vector2.Distance(p, t) <= SolarisConfig.OASIS_RADIUS)
                return true;
        }
        return false;
    }

    public bool IsNearWorkshop(Vector3 pos)
    {
        return Vector2.Distance(new Vector2(pos.x, pos.z), new Vector2(18f, 14f)) <= 14.5f;
    }

    public Vector3 ResolveBuildingCollisions(Vector3 pos, float radius)
    {
        Vector3 resolved = pos;
        for (int i = 0; i < BuildingColliders.Count; i++)
        {
            Bounds b = BuildingColliders[i];
            float minX = b.min.x - radius;
            float maxX = b.max.x + radius;
            float minZ = b.min.z - radius;
            float maxZ = b.max.z + radius;

            if (resolved.x >= minX && resolved.x <= maxX && resolved.z >= minZ && resolved.z <= maxZ)
            {
                float distLeft = resolved.x - minX;
                float distRight = maxX - resolved.x;
                float distBack = resolved.z - minZ;
                float distFront = maxZ - resolved.z;

                float minDist = Mathf.Min(Mathf.Min(distLeft, distRight), Mathf.Min(distBack, distFront));

                if (Mathf.Approximately(minDist, distLeft)) resolved.x = minX;
                else if (Mathf.Approximately(minDist, distRight)) resolved.x = maxX;
                else if (Mathf.Approximately(minDist, distBack)) resolved.z = minZ;
                else resolved.z = maxZ;
            }
        }
        return resolved;
    }
    #endregion

    #region Update Loop & Dynamics
    private void Update()
    {
        // 1. Animate Workshop dish and crystal
        if (_workshopRadarDish != null)
        {
            _workshopRadarDish.Rotate(Vector3.up, 42f * Time.deltaTime, Space.World);
        }

        if (_workshopCrystal != null)
        {
            _workshopCrystal.Rotate(new Vector3(0.5f, 1f, 0.3f), 55f * Time.deltaTime, Space.Self);
            _workshopCrystal.localPosition = _crystalBasePos + Vector3.up * (Mathf.Sin(Time.time * 2.5f) * 0.28f);
        }

        // 2. Animate Spire Energy Core
        if (_spireEnergyCore != null)
        {
            _spireEnergyCore.Rotate(Vector3.up, 30f * Time.deltaTime, Space.World);
        }

        // 3. Check Buried Buildings Unearthing
        for (int i = 0; i < _buriedBuildings.Count; i++)
        {
            var bb = _buriedBuildings[i];
            if (bb.IsRestored) continue;

            int idx = bb.GridZ * SolarisConfig.GRID_SIZE + bb.GridX;
            if (Heights[idx] < SolarisConfig.RESTORE_THRESHOLD)
            {
                bb.CurrentY = Mathf.MoveTowards(bb.CurrentY, 0f, bb.RiseSpeed * Time.deltaTime);
                if (bb.Root != null)
                {
                    bb.Root.transform.position = bb.BasePos + Vector3.up * bb.CurrentY;
                }

                if (bb.CurrentY >= 0f)
                {
                    bb.IsRestored = true;
                    if (bb.HasSolarPanels)
                    {
                        SolarPanelsRestored++;
                    }
                }
            }
        }

        // 4. Periodically Recompute RestorationPercent
        _restorationCalcTimer -= Time.deltaTime;
        if (_restorationCalcTimer <= 0f)
        {
            _restorationCalcTimer = 0.5f;
            int sampleCount = 0;
            int cleanCount = 0;
            for (int z = 0; z < SolarisConfig.GRID_SIZE; z += 4)
            {
                for (int x = 0; x < SolarisConfig.GRID_SIZE; x += 4)
                {
                    sampleCount++;
                    int idx = z * SolarisConfig.GRID_SIZE + x;
                    if (Heights[idx] < SolarisConfig.RESTORE_THRESHOLD)
                        cleanCount++;
                }
            }

            float sandCleanPercent = (float)cleanCount / sampleCount * 100f;
            RestorationPercent = Mathf.Clamp(sandCleanPercent * 0.85f + (SolarPanelsRestored * 1.5f), 0f, 100f);
        }

        // 5. Update mesh and colors if dirty
        if (_dirty)
        {
            _UpdateMeshAndColors();
        }

        // 6. Throttled MeshCollider Update
        if (_colliderDirty)
        {
            _colliderTimer -= Time.deltaTime;
            if (_colliderTimer <= 0f)
            {
                _colliderTimer = 0.35f;
                _colliderDirty = false;
                if (_meshCollider != null && _terrainMesh != null)
                {
                    _meshCollider.sharedMesh = null;
                    _meshCollider.sharedMesh = _terrainMesh;
                }
            }
        }
    }
    #endregion
}
