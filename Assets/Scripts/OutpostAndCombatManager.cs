// ============================================================
// SOLARIS RECLAIM v2.0 — OutpostAndCombatManager.cs
// 12 Enemy Outposts (4 Smog Factories + 8 Toxic Scrap Mounds)
// Smart Proximity Spawner (Cap 14, Despawn >90m, Storm Hunter)
// 3 Distinct Animated 3D Enemies (Crawler, Golem, Wraith)
// Weapons Combat: Blaster, Solar Lance, Drone Laser, Shockwave, Vacuum Pull
// Global Namespace — Unity 6 (6000.0.50f1) / C#
// ============================================================

using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class OutpostAndCombatManager : MonoBehaviour
{
    // ============================================================
    // Singleton & State
    // ============================================================
    public static OutpostAndCombatManager Instance { get; private set; }

    public List<OutpostData> Outposts = new List<OutpostData>();
    public List<EnemyUnit> ActiveEnemies = new List<EnemyUnit>();
    public int TotalOutposts => Outposts.Count;
    public int DestroyedOutpostsCount { get; private set; }
    public bool StormActive = false;

    // ============================================================
    // Helper Data Classes (Nested to prevent duplicate collisions)
    // ============================================================
    public class VentData
    {
        public string id;
        public Vector3 position;
        public float hp = 50f;
        public float maxHp = 50f;
        public float radius = 2.6f;
        public bool destroyed = false;
        public GameObject rootObj;
        public Transform fanTransform;
        public Light exhaustLight;
        public Renderer fanRenderer;
        public WorldHealthBar healthBar;
        public OutpostData parentOutpost;
    }

    public class OutpostData
    {
        public string id;
        public string type; // "factory" or "mound"
        public Vector3 position;
        public float hp;
        public float maxHp;
        public float radius;
        public bool destroyed = false;
        public bool shielded = false;
        public GameObject rootObj;
        public GameObject shieldDomeObj;
        public ParticleSystem smokeParticles;
        public List<VentData> vents = new List<VentData>();
        public float spawnTimer = 0f;
        public int activeCrawlers = 0;
        public int activeGolems = 0;
        public WorldHealthBar healthBar;
        public Light coreLight;
    }

    public class EnemyUnit
    {
        public string id;
        public string type; // "crawler", "golem", "wraith"
        public Vector3 position;
        public float hp;
        public float maxHp;
        public float speed;
        public float radius;
        public bool isDead = false;
        public GameObject rootObj;
        public OutpostData sourceOutpost;
        public float attackTimer = 0f;
        public float animTimer = 0f;
        public Vector3 knockbackVelocity = Vector3.zero;
        public WorldHealthBar healthBar;
        public float healthBarFadeTimer = 0f;

        // Visual animation pivots
        public Transform[] legPivots;
        public Transform torso;
        public Transform[] armPivots;
        public Transform[] energyRings;
        public Light coreLight;

        // Hades-style telegraphed combat state machine
        public enum AIState { Chase, Telegraph, Attacking, Stunned, Recovering }
        public AIState state = AIState.Chase;
        public float stateTimer = 0f;
        public Vector3 chargeDirection = Vector3.forward;
        public GameObject telegraphVisual;
        public bool isKnockedByPiston = false;
    }

    public class ProjectileData
    {
        public GameObject rootObj;
        public Vector3 position;
        public Vector3 velocity;
        public float damage;
        public float lifetime;
        public float maxLifetime;
        public float radius;
        public bool isEnemy;
        public bool isHeavyKnockback;
        public bool isMortar;
        public Vector3 mortarStart;
        public Vector3 mortarTarget;
        public GameObject dangerCircle;
    }

    public class WorldHealthBar
    {
        public GameObject rootObj;
        public Transform fillTransform;
        public Renderer fillRenderer;
        public float heightOffset;
        public float maxHp;
        public float currentHp;
        public float baseWidth = 2.4f;
    }

    public class LaserBeamData
    {
        public GameObject rootObj;
        public LineRenderer line;
        public float timer;
        public float duration;
        public float initialWidth;
    }

    public class ShockwaveVisualData
    {
        public GameObject rootObj;
        public Material mat;
        public float timer;
        public float duration;
        public float maxRadius;
        public Vector3 center;
    }

    // ============================================================
    // Private Fields
    // ============================================================
    private SandGridWorld _world;
    private EcoReclaimerPlayer _player;

    private readonly List<ProjectileData> _projectiles = new List<ProjectileData>();
    private readonly List<LaserBeamData> _laserBeams = new List<LaserBeamData>();
    private readonly List<ShockwaveVisualData> _shockwaves = new List<ShockwaveVisualData>();

    private float _stormSpawnTimer = 0f;
    private int _idCounter = 1;

    // Shared Materials
    private Material _matDarkMetal;
    private Material _matRust;
    private Material _matToxicSludge;
    private Material _matReactorCore;
    private Material _matVentOrange;
    private Material _matShieldRed;
    private Material _matCrawlerMetal;
    private Material _matWraithCrimson;
    private Material _matCyanProjectile;
    private Material _matEnemyBolt;
    private Material _matHealthBg;
    private Material _matHealthFg;
    private Material _matTelegraphRed;

    // Outpost spawn coordinates
    private static readonly Vector3[] FACTORY_POSITIONS = new Vector3[]
    {
        new Vector3(125f, 0f, 125f),
        new Vector3(-125f, 0f, 125f),
        new Vector3(125f, 0f, -125f),
        new Vector3(-125f, 0f, -125f)
    };

    private static readonly Vector3[] MOUND_POSITIONS = new Vector3[]
    {
        new Vector3(65f, 0f, 45f),
        new Vector3(-65f, 0f, 45f),
        new Vector3(65f, 0f, -45f),
        new Vector3(-65f, 0f, -45f),
        new Vector3(45f, 0f, 85f),
        new Vector3(-45f, 0f, -85f),
        new Vector3(95f, 0f, -25f),
        new Vector3(-95f, 0f, 25f)
    };

    // ============================================================
    // Unity Lifecycle
    // ============================================================
    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            SolarisConfig.SafeDestroy(gameObject);
            return;
        }
        Instance = this;
        InitSharedMaterials();
    }

    private void Start()
    {
        if (Outposts.Count == 0)
        {
            SandGridWorld w = _world != null ? _world : SandGridWorld.Instance;
            EcoReclaimerPlayer p = _player != null ? _player : EcoReclaimerPlayer.Instance;
            if (w != null && p != null)
            {
                InitializeCombat(w, p);
            }
        }
    }

    private void Update()
    {
        float dt = Time.deltaTime;
        if (dt <= 0f) return;

        if (_world == null) _world = SandGridWorld.Instance;
        if (_player == null) _player = EcoReclaimerPlayer.Instance;

        Vector3 playerPos = _player != null ? _player.transform.position : Vector3.zero;

        // 1. Update Outposts (Vent fan spin, re-contamination, proximity spawning)
        UpdateOutposts(dt, playerPos);

        // 2. Update Active Enemies (AI, movement, attacks, procedural animation, despawn)
        UpdateEnemies(dt, playerPos);

        // 3. Update Projectiles
        UpdateProjectiles(dt);

        // 4. Update Laser Beams
        UpdateLaserBeams(dt);

        // 5. Update Shockwave Visuals
        UpdateShockwaves(dt);

        // 6. Blood Moon Sandstorm Hunter Spawns
        UpdateStormSpawning(dt, playerPos);
    }

    private void LateUpdate()
    {
        Camera cam = Camera.main;
        if (cam == null) return;
        Quaternion camRot = cam.transform.rotation;

        // Billboard all active health bars
        for (int i = 0; i < Outposts.Count; i++)
        {
            var op = Outposts[i];
            if (!op.destroyed && op.healthBar != null && op.healthBar.rootObj != null)
            {
                UpdateHealthBarTransform(op.healthBar, op.position, camRot);
            }
            if (!op.destroyed)
            {
                for (int v = 0; v < op.vents.Count; v++)
                {
                    var vt = op.vents[v];
                    if (!vt.destroyed && vt.healthBar != null && vt.healthBar.rootObj != null)
                    {
                        UpdateHealthBarTransform(vt.healthBar, vt.position, camRot);
                    }
                }
            }
        }

        for (int i = 0; i < ActiveEnemies.Count; i++)
        {
            var e = ActiveEnemies[i];
            if (!e.isDead && e.healthBar != null && e.healthBar.rootObj != null)
            {
                bool showBar = e.healthBarFadeTimer > 0f && e.hp < e.maxHp;
                if (e.healthBar.rootObj.activeSelf != showBar)
                {
                    e.healthBar.rootObj.SetActive(showBar);
                }
                if (showBar)
                {
                    UpdateHealthBarTransform(e.healthBar, e.position, camRot);
                }
            }
        }
    }

    private void UpdateHealthBarTransform(WorldHealthBar bar, Vector3 entityPos, Quaternion camRot)
    {
        bar.rootObj.transform.position = entityPos + Vector3.up * bar.heightOffset;
        bar.rootObj.transform.rotation = camRot;
    }

    // ============================================================
    // 1. Combat Initialization (12 Enemy Outposts)
    // ============================================================
    public void InitializeCombat(SandGridWorld world, EcoReclaimerPlayer player)
    {
        _world = world;
        _player = player;

        // Clean up any existing children / outposts / enemies
        while (transform.childCount > 0)
        {
            DestroyImmediate(transform.GetChild(0).gameObject);
        }

        Outposts.Clear();
        ActiveEnemies.Clear();
        _projectiles.Clear();
        _laserBeams.Clear();
        _shockwaves.Clear();
        DestroyedOutpostsCount = 0;
        _stormSpawnTimer = 0f;

        InitSharedMaterials();

        // 1. Build 4 Smoking Factories (SmogFactory)
        for (int i = 0; i < FACTORY_POSITIONS.Length; i++)
        {
            Vector3 basePos = FACTORY_POSITIONS[i];
            float groundY = _world != null ? _world.GetHeight(basePos.x, basePos.z) : 0f;
            Vector3 worldPos = new Vector3(basePos.x, groundY, basePos.z);

            OutpostData factory = BuildSmogFactory(i, worldPos);
            Outposts.Add(factory);

            // Add factory footprint to world building colliders
            if (_world != null && _world.BuildingColliders != null)
            {
                _world.BuildingColliders.Add(new Bounds(new Vector3(worldPos.x, worldPos.y + 6f, worldPos.z), new Vector3(18f, 14f, 18f)));
            }
        }

        // 2. Build 8 Toxic Scrap Mounds (ScrapMound)
        for (int i = 0; i < MOUND_POSITIONS.Length; i++)
        {
            Vector3 basePos = MOUND_POSITIONS[i];
            float groundY = _world != null ? _world.GetHeight(basePos.x, basePos.z) : 0f;
            Vector3 worldPos = new Vector3(basePos.x, groundY, basePos.z);

            OutpostData mound = BuildScrapMound(i, worldPos);
            Outposts.Add(mound);

            // Add scrap mound footprint to world building colliders
            if (_world != null && _world.BuildingColliders != null)
            {
                _world.BuildingColliders.Add(new Bounds(new Vector3(worldPos.x, worldPos.y + 2.5f, worldPos.z), new Vector3(8f, 6f, 8f)));
            }
        }
    }

    // ============================================================
    // 2. Outpost & Vent Builders
    // ============================================================
    private OutpostData BuildSmogFactory(int index, Vector3 pos)
    {
        GameObject factoryGroup = new GameObject($"SmogFactory_{index}");
        factoryGroup.transform.SetParent(transform, true);
        factoryGroup.transform.position = pos;

        OutpostData op = new OutpostData
        {
            id = $"factory_{index}",
            type = "factory",
            position = pos,
            hp = 240f,
            maxHp = 240f,
            radius = 9.5f,
            shielded = true,
            destroyed = false,
            rootObj = factoryGroup
        };

        // 1. Heavy industrial bunker fortress (16m x 12m x 16m)
        GameObject bunker = GameObject.CreatePrimitive(PrimitiveType.Cube);
        bunker.name = "Bunker";
        bunker.transform.SetParent(factoryGroup.transform, false);
        bunker.transform.localPosition = new Vector3(0f, 6f, 0f);
        bunker.transform.localScale = new Vector3(16f, 12f, 16f);

        Texture2D faceTex = Resources.Load<Texture2D>("Textures/BuildingFaces/face_factory");
        if (faceTex == null)
        {
            faceTex = SandGridWorld.LoadBuildingTexture("face_factory");
        }
        Material bunkerMat = SolarisConfig.CreateMaterial(new Color(0.25f, 0.28f, 0.32f), 0.5f, 0.35f, faceTex);
        bunker.GetComponent<Renderer>().sharedMaterial = bunkerMat;

        // 2. Twin 22m tall smokestacks with dark rising smoke puffs
        ParticleSystem smokePs = null;
        for (int c = -1; c <= 1; c += 2)
        {
            GameObject stack = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            stack.name = $"Smokestack_{c}";
            stack.transform.SetParent(factoryGroup.transform, false);
            // Unity cylinder height = 2 units, so scale.y = 11 creates a 22m tall chimney
            stack.transform.localPosition = new Vector3(c * 4.2f, 11f, -4f);
            stack.transform.localScale = new Vector3(2.4f, 11f, 2.4f);
            stack.GetComponent<Renderer>().sharedMaterial = _matDarkMetal;

            // Smoke particle system on top of chimney
            GameObject smokeObj = new GameObject("SmokeEmitter");
            smokeObj.transform.SetParent(stack.transform, false);
            smokeObj.transform.localPosition = new Vector3(0f, 1f, 0f);

            ParticleSystem ps = smokeObj.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.startSpeed = 4.2f;
            main.startSize = 1.8f;
            main.startLifetime = 3.5f;
            main.startColor = new Color(0.12f, 0.12f, 0.14f, 0.75f);
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 80;

            var emission = ps.emission;
            emission.rateOverTime = 12f;

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 15f;
            shape.radius = 0.8f;

            var sizeOverLifetime = ps.sizeOverLifetime;
            sizeOverLifetime.enabled = true;
            AnimationCurve curve = new AnimationCurve();
            curve.AddKey(0f, 0.6f);
            curve.AddKey(1f, 3.2f);
            sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1f, curve);

            var psRenderer = smokeObj.GetComponent<ParticleSystemRenderer>();
            psRenderer.sharedMaterial = _matHealthBg;

            if (smokePs == null) smokePs = ps;
        }
        op.smokeParticles = smokePs;

        // 3. Central Glowing Red Reactor Core (240 HP, radius = 9.5f)
        GameObject core = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        core.name = "ReactorCore";
        core.transform.SetParent(factoryGroup.transform, false);
        core.transform.localPosition = new Vector3(0f, 13.5f, 0f);
        core.transform.localScale = new Vector3(4.2f, 4.2f, 4.2f);
        core.GetComponent<Renderer>().sharedMaterial = _matReactorCore;

        GameObject coreLightObj = new GameObject("ReactorCoreLight");
        coreLightObj.transform.SetParent(core.transform, false);
        coreLightObj.transform.localPosition = Vector3.zero;
        Light coreLight = coreLightObj.AddComponent<Light>();
        coreLight.type = LightType.Point;
        coreLight.color = new Color(1f, 0.12f, 0.12f);
        coreLight.range = 22f;
        coreLight.intensity = 2.8f;
        op.coreLight = coreLight;

        // 4. Translucent Red Shield Dome (15m radius = 30m diameter)
        GameObject shieldDome = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        shieldDome.name = "ShieldDome";
        shieldDome.transform.SetParent(factoryGroup.transform, false);
        shieldDome.transform.localPosition = new Vector3(0f, 0.5f, 0f);
        shieldDome.transform.localScale = new Vector3(30f, 30f, 30f);
        shieldDome.GetComponent<Renderer>().sharedMaterial = _matShieldRed;

        // Remove default sphere collider to avoid blocking physics rays
        SolarisConfig.SafeDestroy(shieldDome.GetComponent<Collider>());
        op.shieldDomeObj = shieldDome;

        // 5. 3 Destructible Cooling Vents positioned at 12.5m radius around factory
        for (int v = 0; v < 3; v++)
        {
            float ang = (v / 3f) * Mathf.PI * 2f;
            float vx = pos.x + Mathf.Cos(ang) * 12.5f;
            float vz = pos.z + Mathf.Sin(ang) * 12.5f;
            float vy = _world != null ? _world.GetHeight(vx, vz) : pos.y;
            Vector3 ventPos = new Vector3(vx, vy, vz);

            VentData vent = BuildCoolingVent(index, v, ventPos, op);
            op.vents.Add(vent);
        }

        // 6. World-space health bar for factory
        op.healthBar = CreateWorldHealthBar($"HealthBar_Factory_{index}", factoryGroup.transform, 17f, 240f, 4.2f);

        return op;
    }

    private VentData BuildCoolingVent(int factoryIndex, int ventIndex, Vector3 pos, OutpostData parentOp)
    {
        GameObject ventGroup = new GameObject($"Vent_{factoryIndex}_{ventIndex}");
        ventGroup.transform.SetParent(transform, true);
        ventGroup.transform.position = pos;

        VentData vent = new VentData
        {
            id = $"factory_{factoryIndex}_vent_{ventIndex}",
            position = pos,
            hp = 50f,
            maxHp = 50f,
            radius = 2.6f,
            destroyed = false,
            rootObj = ventGroup,
            parentOutpost = parentOp
        };

        // Vent base housing (radius ~1.3m, height 2.6m)
        GameObject baseCyl = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        baseCyl.name = "VentHousing";
        baseCyl.transform.SetParent(ventGroup.transform, false);
        baseCyl.transform.localPosition = new Vector3(0f, 1.3f, 0f);
        baseCyl.transform.localScale = new Vector3(2.6f, 1.3f, 2.6f);
        baseCyl.GetComponent<Renderer>().sharedMaterial = _matDarkMetal;

        // Spinning fan rotor on top
        GameObject fanPivot = new GameObject("FanRotor");
        fanPivot.transform.SetParent(ventGroup.transform, false);
        fanPivot.transform.localPosition = new Vector3(0f, 2.65f, 0f);

        GameObject blade1 = GameObject.CreatePrimitive(PrimitiveType.Cube);
        blade1.transform.SetParent(fanPivot.transform, false);
        blade1.transform.localScale = new Vector3(2.2f, 0.08f, 0.35f);
        blade1.GetComponent<Renderer>().sharedMaterial = _matVentOrange;
        SolarisConfig.SafeDestroy(blade1.GetComponent<Collider>());

        GameObject blade2 = GameObject.CreatePrimitive(PrimitiveType.Cube);
        blade2.transform.SetParent(fanPivot.transform, false);
        blade2.transform.localScale = new Vector3(0.35f, 0.08f, 2.2f);
        blade2.GetComponent<Renderer>().sharedMaterial = _matVentOrange;
        SolarisConfig.SafeDestroy(blade2.GetComponent<Collider>());

        vent.fanTransform = fanPivot.transform;
        vent.fanRenderer = blade1.GetComponent<Renderer>();

        // Orange exhaust glow
        GameObject lightObj = new GameObject("ExhaustLight");
        lightObj.transform.SetParent(ventGroup.transform, false);
        lightObj.transform.localPosition = new Vector3(0f, 2.8f, 0f);
        Light exhaustLight = lightObj.AddComponent<Light>();
        exhaustLight.type = LightType.Point;
        exhaustLight.color = new Color(1f, 0.55f, 0.1f);
        exhaustLight.range = 8.5f;
        exhaustLight.intensity = 1.8f;
        vent.exhaustLight = exhaustLight;

        // World health bar for vent
        vent.healthBar = CreateWorldHealthBar($"HealthBar_Vent_{factoryIndex}_{ventIndex}", ventGroup.transform, 3.4f, 50f, 2.0f);

        return vent;
    }

    private OutpostData BuildScrapMound(int index, Vector3 pos)
    {
        GameObject moundGroup = new GameObject($"ScrapMound_{index}");
        moundGroup.transform.SetParent(transform, true);
        moundGroup.transform.position = pos;

        OutpostData op = new OutpostData
        {
            id = $"mound_{index}",
            type = "mound",
            position = pos,
            hp = 120f,
            maxHp = 120f,
            radius = 4.5f,
            shielded = false,
            destroyed = false,
            rootObj = moundGroup
        };

        // Jagged rusted debris mound base
        GameObject baseCone = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        baseCone.name = "ScrapBase";
        baseCone.transform.SetParent(moundGroup.transform, false);
        baseCone.transform.localPosition = new Vector3(0f, 1.1f, 0f);
        baseCone.transform.localScale = new Vector3(6.8f, 1.1f, 6.8f);
        baseCone.GetComponent<Renderer>().sharedMaterial = _matRust;

        // Protruding twisted beams and rusted pipes
        for (int p = 0; p < 5; p++)
        {
            GameObject beam = GameObject.CreatePrimitive(PrimitiveType.Cube);
            beam.name = $"ScrapBeam_{p}";
            beam.transform.SetParent(moundGroup.transform, false);
            float ang = p * 1.25f;
            beam.transform.localPosition = new Vector3(Mathf.Cos(ang) * 1.8f, 1.8f + (p % 2) * 0.4f, Mathf.Sin(ang) * 1.8f);
            beam.transform.localRotation = Quaternion.Euler(p % 2 == 0 ? 25f : -25f, p * 45f, p > 2 ? 30f : -30f);
            beam.transform.localScale = new Vector3(0.4f, 3.2f, 0.4f);
            beam.GetComponent<Renderer>().sharedMaterial = _matRust;
        }

        // Central Glowing Toxic Sludge Core
        GameObject sludge = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        sludge.name = "ToxicCore";
        sludge.transform.SetParent(moundGroup.transform, false);
        sludge.transform.localPosition = new Vector3(0f, 2.1f, 0f);
        sludge.transform.localScale = new Vector3(2.2f, 2.2f, 2.2f);
        sludge.GetComponent<Renderer>().sharedMaterial = _matToxicSludge;

        GameObject sludgeLightObj = new GameObject("SludgeLight");
        sludgeLightObj.transform.SetParent(sludge.transform, false);
        sludgeLightObj.transform.localPosition = Vector3.zero;
        Light sludgeLight = sludgeLightObj.AddComponent<Light>();
        sludgeLight.type = LightType.Point;
        sludgeLight.color = new Color(0.2f, 1f, 0.3f);
        sludgeLight.range = 10f;
        sludgeLight.intensity = 2.0f;
        op.coreLight = sludgeLight;

        // Warning Hazard Ring around base
        GameObject ring = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        ring.name = "HazardRing";
        ring.transform.SetParent(moundGroup.transform, false);
        ring.transform.localPosition = new Vector3(0f, 0.05f, 0f);
        ring.transform.localScale = new Vector3(8.5f, 0.05f, 8.5f);
        ring.GetComponent<Renderer>().sharedMaterial = _matVentOrange;
        SolarisConfig.SafeDestroy(ring.GetComponent<Collider>());

        // World health bar for scrap mound
        op.healthBar = CreateWorldHealthBar($"HealthBar_Mound_{index}", moundGroup.transform, 4.4f, 120f, 2.8f);

        return op;
    }

    // ============================================================
    // 3. Smart Proximity Spawner & Outpost Updates
    // ============================================================
    private void UpdateOutposts(float dt, Vector3 playerPos)
    {
        for (int i = 0; i < Outposts.Count; i++)
        {
            var op = Outposts[i];
            if (op.destroyed) continue;

            float distToPlayer = Vector3.Distance(op.position, playerPos);

            // 1. Smog Factory specific logic
            if (op.type == "factory")
            {
                // Spin cooling vent rotor blades
                for (int v = 0; v < op.vents.Count; v++)
                {
                    var vt = op.vents[v];
                    if (!vt.destroyed && vt.fanTransform != null)
                    {
                        vt.fanTransform.Rotate(Vector3.up, 420f * dt);
                    }
                }

                // Undestroyed factories slowly re-contaminate sand within 45m
                if (_world != null)
                {
                    _world.RecontaminateCircle(op.position, 45f, 0.015f, dt);
                }

                // Proximity spawn: player within 68m, max 3 enemies per factory (2 crawlers + 1 golem), 11s interval
                if (distToPlayer <= 68f && ActiveEnemies.Count < SolarisConfig.MAX_ACTIVE_ENEMIES)
                {
                    op.spawnTimer += dt;
                    if (op.spawnTimer >= 11f)
                    {
                        op.spawnTimer = 0f;
                        if (op.activeCrawlers + op.activeGolems < 3)
                        {
                            string spawnType = (op.activeGolems < 1 && UnityEngine.Random.value < 0.45f) ? "golem" : "crawler";
                            Vector3 spawnPos = op.position + Quaternion.Euler(0, UnityEngine.Random.Range(0f, 360f), 0) * (Vector3.forward * UnityEngine.Random.Range(10f, 14f));
                            if (_world != null) spawnPos.y = _world.GetHeight(spawnPos.x, spawnPos.z);

                            SpawnEnemy(spawnType, spawnPos, op);
                        }
                    }
                }
            }
            // 2. Scrap Mound specific logic
            else if (op.type == "mound")
            {
                // Proximity spawn: player within 48m, max 2 crawlers per mound, 9s interval
                if (distToPlayer <= 48f && ActiveEnemies.Count < SolarisConfig.MAX_ACTIVE_ENEMIES)
                {
                    op.spawnTimer += dt;
                    if (op.spawnTimer >= 9f)
                    {
                        op.spawnTimer = 0f;
                        if (op.activeCrawlers < 2)
                        {
                            Vector3 spawnPos = op.position + Quaternion.Euler(0, UnityEngine.Random.Range(0f, 360f), 0) * (Vector3.forward * UnityEngine.Random.Range(4.5f, 7f));
                            if (_world != null) spawnPos.y = _world.GetHeight(spawnPos.x, spawnPos.z);

                            SpawnEnemy("crawler", spawnPos, op);
                        }
                    }
                }
            }
        }
    }

    private void UpdateStormSpawning(float dt, Vector3 playerPos)
    {
        if (!StormActive || _player == null) return;

        _stormSpawnTimer += dt;
        if (_stormSpawnTimer >= 7.0f)
        {
            _stormSpawnTimer = 0f;

            int activeWraiths = 0;
            for (int i = 0; i < ActiveEnemies.Count; i++)
            {
                if (ActiveEnemies[i].type == "wraith" && !ActiveEnemies[i].isDead) activeWraiths++;
            }

            // Max 5 active storm wraiths, global cap 14
            if (activeWraiths < 5 && ActiveEnemies.Count < SolarisConfig.MAX_ACTIVE_ENEMIES)
            {
                float ang = UnityEngine.Random.Range(0f, Mathf.PI * 2f);
                float dist = UnityEngine.Random.Range(28f, 36f);
                Vector3 spawnPos = playerPos + new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang)) * dist;
                float groundY = _world != null ? _world.GetHeight(spawnPos.x, spawnPos.z) : playerPos.y;
                spawnPos.y = groundY + 2.2f;

                SpawnEnemy("wraith", spawnPos, null);
            }
        }
    }

    // ============================================================
    // 4. Enemy Spawning & Procedural 3D Animated Models
    // ============================================================
    public EnemyUnit SpawnEnemy(string type, Vector3 pos, OutpostData source = null)
    {
        if (ActiveEnemies.Count >= SolarisConfig.MAX_ACTIVE_ENEMIES) return null;

        int id = _idCounter++;
        GameObject enemyObj = new GameObject($"Enemy_{type}_{id}");
        enemyObj.transform.SetParent(transform, true);
        enemyObj.transform.position = pos;

        EnemyUnit enemy = new EnemyUnit
        {
            id = $"{type}_{id}",
            type = type,
            position = pos,
            isDead = false,
            rootObj = enemyObj,
            sourceOutpost = source
        };

        if (source != null)
        {
            if (type == "crawler") source.activeCrawlers++;
            else if (type == "golem") source.activeGolems++;
        }

        switch (type)
        {
            case "crawler":
                enemy.hp = 45f;
                enemy.maxHp = 45f;
                enemy.speed = 9.2f;
                enemy.radius = 1.3f;
                BuildCrawlerVisuals(enemyObj, enemy);
                enemy.healthBar = CreateWorldHealthBar($"HealthBar_Crawler_{id}", enemyObj.transform, 1.4f, 45f, 1.6f);
                break;

            case "golem":
                enemy.hp = 150f;
                enemy.maxHp = 150f;
                enemy.speed = 5.2f;
                enemy.radius = 2.4f;
                BuildGolemVisuals(enemyObj, enemy);
                enemy.healthBar = CreateWorldHealthBar($"HealthBar_Golem_{id}", enemyObj.transform, 3.4f, 150f, 2.6f);
                break;

            case "wraith":
                enemy.hp = 65f;
                enemy.maxHp = 65f;
                enemy.speed = 10.8f;
                enemy.radius = 1.5f;
                BuildWraithVisuals(enemyObj, enemy);
                enemy.healthBar = CreateWorldHealthBar($"HealthBar_Wraith_{id}", enemyObj.transform, 1.8f, 65f, 1.8f);
                break;
        }

        ActiveEnemies.Add(enemy);
        return enemy;
    }

    private void BuildCrawlerVisuals(GameObject root, EnemyUnit enemy)
    {
        // Central spider chassis (slate titanium)
        GameObject chassis = GameObject.CreatePrimitive(PrimitiveType.Cube);
        chassis.name = "Chassis";
        chassis.transform.SetParent(root.transform, false);
        chassis.transform.localPosition = new Vector3(0f, 0.45f, 0f);
        chassis.transform.localScale = new Vector3(1.3f, 0.45f, 1.5f);
        chassis.GetComponent<Renderer>().sharedMaterial = _matCrawlerMetal;
        SolarisConfig.SafeDestroy(chassis.GetComponent<Collider>());

        // 2 Glowing red eye sensors in front
        for (int eye = -1; eye <= 1; eye += 2)
        {
            GameObject eyeObj = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            eyeObj.transform.SetParent(chassis.transform, false);
            eyeObj.transform.localPosition = new Vector3(eye * 0.32f, 0.1f, 0.55f);
            eyeObj.transform.localScale = new Vector3(0.2f, 0.2f, 0.2f);
            eyeObj.GetComponent<Renderer>().sharedMaterial = _matReactorCore;
            SolarisConfig.SafeDestroy(eyeObj.GetComponent<Collider>());
        }

        // 6 Articulated robotic legs (3 left, 3 right)
        enemy.legPivots = new Transform[6];
        int legIdx = 0;
        for (int side = -1; side <= 1; side += 2)
        {
            for (int z = -1; z <= 1; z++)
            {
                GameObject pivot = new GameObject($"LegPivot_{legIdx}");
                pivot.transform.SetParent(root.transform, false);
                pivot.transform.localPosition = new Vector3(side * 0.65f, 0.45f, z * 0.5f);

                // Upper leg segment angled outward/upward
                GameObject upper = GameObject.CreatePrimitive(PrimitiveType.Cube);
                upper.transform.SetParent(pivot.transform, false);
                upper.transform.localPosition = new Vector3(side * 0.4f, 0.15f, 0f);
                upper.transform.localRotation = Quaternion.Euler(0f, 0f, -side * 30f);
                upper.transform.localScale = new Vector3(0.7f, 0.14f, 0.14f);
                upper.GetComponent<Renderer>().sharedMaterial = _matDarkMetal;
                SolarisConfig.SafeDestroy(upper.GetComponent<Collider>());

                // Lower leg segment angled down to terrain
                GameObject lower = GameObject.CreatePrimitive(PrimitiveType.Cube);
                lower.transform.SetParent(upper.transform, false);
                lower.transform.localPosition = new Vector3(side * 0.35f, -0.3f, 0f);
                lower.transform.localRotation = Quaternion.Euler(0f, 0f, side * 55f);
                lower.transform.localScale = new Vector3(0.6f, 0.12f, 0.12f);
                lower.GetComponent<Renderer>().sharedMaterial = _matRust;
                SolarisConfig.SafeDestroy(lower.GetComponent<Collider>());

                enemy.legPivots[legIdx++] = pivot.transform;
            }
        }
    }

    private void BuildGolemVisuals(GameObject root, EnemyUnit enemy)
    {
        // Heavy industrial torso cabin
        GameObject torso = GameObject.CreatePrimitive(PrimitiveType.Cube);
        torso.name = "GolemTorso";
        torso.transform.SetParent(root.transform, false);
        torso.transform.localPosition = new Vector3(0f, 2.0f, 0f);
        torso.transform.localScale = new Vector3(2.2f, 2.2f, 1.8f);
        torso.GetComponent<Renderer>().sharedMaterial = _matRust;
        SolarisConfig.SafeDestroy(torso.GetComponent<Collider>());
        enemy.torso = torso.transform;

        // Sludge core in chest
        GameObject core = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        core.transform.SetParent(torso.transform, false);
        core.transform.localPosition = new Vector3(0f, 0.1f, 0.52f);
        core.transform.localScale = new Vector3(0.6f, 0.6f, 0.4f);
        core.GetComponent<Renderer>().sharedMaterial = _matToxicSludge;
        SolarisConfig.SafeDestroy(core.GetComponent<Collider>());

        // 2 Shoulder exhaust chimneys emitting toxic smoke
        for (int c = -1; c <= 1; c += 2)
        {
            GameObject chimney = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            chimney.transform.SetParent(torso.transform, false);
            chimney.transform.localPosition = new Vector3(c * 0.9f, 1.1f, -0.5f);
            chimney.transform.localScale = new Vector3(0.35f, 0.5f, 0.35f);
            chimney.GetComponent<Renderer>().sharedMaterial = _matDarkMetal;
            SolarisConfig.SafeDestroy(chimney.GetComponent<Collider>());
        }

        // 2 Heavy hydraulic walker legs
        enemy.legPivots = new Transform[2];
        for (int l = 0; l < 2; l++)
        {
            int side = (l == 0) ? -1 : 1;
            GameObject hip = new GameObject($"Hip_{side}");
            hip.transform.SetParent(root.transform, false);
            hip.transform.localPosition = new Vector3(side * 0.75f, 1.0f, 0f);

            GameObject legMesh = GameObject.CreatePrimitive(PrimitiveType.Cube);
            legMesh.transform.SetParent(hip.transform, false);
            legMesh.transform.localPosition = new Vector3(0f, -0.45f, 0f);
            legMesh.transform.localScale = new Vector3(0.55f, 1.0f, 0.7f);
            legMesh.GetComponent<Renderer>().sharedMaterial = _matDarkMetal;
            SolarisConfig.SafeDestroy(legMesh.GetComponent<Collider>());

            enemy.legPivots[l] = hip.transform;
        }

        // 2 Heavy hydraulic arm fists
        enemy.armPivots = new Transform[2];
        for (int a = 0; a < 2; a++)
        {
            int side = (a == 0) ? -1 : 1;
            GameObject shoulder = new GameObject($"Shoulder_{side}");
            shoulder.transform.SetParent(torso.transform, false);
            shoulder.transform.localPosition = new Vector3(side * 1.3f, 0.4f, 0f);

            GameObject fist = GameObject.CreatePrimitive(PrimitiveType.Cube);
            fist.transform.SetParent(shoulder.transform, false);
            fist.transform.localPosition = new Vector3(0f, -0.6f, 0.3f);
            fist.transform.localScale = new Vector3(0.6f, 1.1f, 0.7f);
            fist.GetComponent<Renderer>().sharedMaterial = _matDarkMetal;
            SolarisConfig.SafeDestroy(fist.GetComponent<Collider>());

            enemy.armPivots[a] = shoulder.transform;
        }
    }

    private void BuildWraithVisuals(GameObject root, EnemyUnit enemy)
    {
        // Central floating obsidian/crimson core
        GameObject core = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        core.name = "WraithCore";
        core.transform.SetParent(root.transform, false);
        core.transform.localPosition = Vector3.zero;
        core.transform.localScale = new Vector3(1.2f, 1.6f, 1.2f);
        core.GetComponent<Renderer>().sharedMaterial = _matWraithCrimson;
        SolarisConfig.SafeDestroy(core.GetComponent<Collider>());

        GameObject lightObj = new GameObject("WraithCoreLight");
        lightObj.transform.SetParent(core.transform, false);
        Light coreLight = lightObj.AddComponent<Light>();
        coreLight.type = LightType.Point;
        coreLight.color = new Color(1f, 0.1f, 0.15f);
        coreLight.range = 8f;
        coreLight.intensity = 2.2f;
        enemy.coreLight = coreLight;

        // 3 Concentric crimson energy rings rotating on tilted axes
        enemy.energyRings = new Transform[3];
        for (int r = 0; r < 3; r++)
        {
            GameObject ring = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            ring.name = $"EnergyRing_{r}";
            ring.transform.SetParent(root.transform, false);
            ring.transform.localPosition = Vector3.zero;
            ring.transform.localRotation = Quaternion.Euler(r * 45f, r * 60f, r * 30f);
            ring.transform.localScale = new Vector3(2.0f + r * 0.5f, 0.04f, 2.0f + r * 0.5f);
            ring.GetComponent<Renderer>().sharedMaterial = _matEnemyBolt;
            SolarisConfig.SafeDestroy(ring.GetComponent<Collider>());

            enemy.energyRings[r] = ring.transform;
        }
    }

    // ============================================================
    // 5. Enemy Updates (AI, Movement, Attacks & Despawn)
    // ============================================================
    private void UpdateEnemies(float dt, Vector3 playerPos)
    {
        for (int i = ActiveEnemies.Count - 1; i >= 0; i--)
        {
            var e = ActiveEnemies[i];
            if (e.isDead || e.rootObj == null)
            {
                ActiveEnemies.RemoveAt(i);
                continue;
            }

            // Despawn rule: Any enemy > 90m from player is immediately despawned
            float distToPlayer = Vector3.Distance(e.position, playerPos);
            if (distToPlayer > 90f)
            {
                DespawnEnemy(e);
                ActiveEnemies.RemoveAt(i);
                continue;
            }

            e.animTimer += dt;

            // Oasis Slow Effect: 40% slower inside Oasis
            bool insideOasis = (_world != null && _world.IsInOasis(e.position));
            float speedMult = insideOasis ? 0.60f : 1.0f;
            float currentSpeed = e.speed * speedMult;

            // Apply residual knockback velocity & check wall slam
            if (e.knockbackVelocity.sqrMagnitude > 0.01f)
            {
                Vector3 prevPos = e.position;
                e.position += e.knockbackVelocity * dt;

                if (e.isKnockedByPiston && _world != null)
                {
                    Vector3 resolved = _world.ResolveBuildingCollisions(e.position, e.radius);
                    if (Vector3.Distance(resolved, e.position) > 0.15f)
                    {
                        // Wall-Slam occurred!
                        e.position = resolved;
                        e.knockbackVelocity = Vector3.zero;
                        e.isKnockedByPiston = false;
                        e.state = EnemyUnit.AIState.Stunned;
                        e.stateTimer = 1.4f;
                        DamageEnemy(e, 42f, (resolved - prevPos).normalized, isHeavy: true);
                        CombatJuice.Instance?.EmitScrapSparks(e.position, -(e.position - prevPos).normalized, 22);
                        SolarisGameManager.Instance?.PlaySfx("outpost_explode");
                    }
                }

                e.knockbackVelocity = Vector3.Lerp(e.knockbackVelocity, Vector3.zero, 8f * dt);
            }

            Vector3 toPlayer = (playerPos - e.position);
            toPlayer.y = 0f;
            Vector3 moveDir = toPlayer.normalized;

            // 1. Crawler AI ("Scrap Charger" - Hades Bruiser with Telegraph Charge)
            if (e.type == "crawler")
            {
                // Stunned State
                if (e.state == EnemyUnit.AIState.Stunned)
                {
                    e.stateTimer -= dt;
                    if (e.rootObj != null)
                    {
                        e.rootObj.transform.localRotation = Quaternion.Euler(0, 0, Mathf.Sin(e.animTimer * 20f) * 14f);
                    }
                    if (e.stateTimer <= 0f)
                    {
                        e.state = EnemyUnit.AIState.Chase;
                        e.attackTimer = 2.0f;
                    }
                }
                // Telegraph State (Winding up & drawing ground line)
                else if (e.state == EnemyUnit.AIState.Telegraph)
                {
                    e.stateTimer -= dt;
                    if (toPlayer.magnitude > 0.1f)
                    {
                        e.chargeDirection = toPlayer.normalized;
                        e.rootObj.transform.rotation = Quaternion.LookRotation(e.chargeDirection);
                    }

                    // Vibrate body in anticipation
                    if (e.torso != null)
                    {
                        e.torso.localPosition = new Vector3(Mathf.Sin(e.animTimer * 50f) * 0.08f, 0.4f, 0);
                    }

                    if (e.stateTimer <= 0f)
                    {
                        e.state = EnemyUnit.AIState.Attacking;
                        e.stateTimer = 0.48f; // 0.48s rush
                        if (e.telegraphVisual != null)
                        {
                            SolarisConfig.SafeDestroy(e.telegraphVisual);
                            e.telegraphVisual = null;
                        }
                    }
                }
                // Attacking State (High-Speed Dash Rush)
                else if (e.state == EnemyUnit.AIState.Attacking)
                {
                    e.stateTimer -= dt;
                    e.position += e.chargeDirection * (currentSpeed * 3.4f) * dt;

                    // Hit player?
                    if (distToPlayer <= 2.2f)
                    {
                        _player?.TakeDamage(18f);
                        CombatJuice.Instance?.AddCameraTrauma(0.35f);
                        e.state = EnemyUnit.AIState.Chase;
                        e.attackTimer = 2.4f;
                    }

                    // Hit obstacle / building wall?
                    if (_world != null)
                    {
                        Vector3 resolved = _world.ResolveBuildingCollisions(e.position, e.radius);
                        if (Vector3.Distance(resolved, e.position) > 0.12f)
                        {
                            // Stun from missing dash and hitting wall!
                            e.position = resolved;
                            e.state = EnemyUnit.AIState.Stunned;
                            e.stateTimer = 1.5f;
                            DamageEnemy(e, 25f, -e.chargeDirection, isHeavy: true);
                            CombatJuice.Instance?.EmitScrapSparks(e.position, -e.chargeDirection, 20);
                            SolarisGameManager.Instance?.PlaySfx("outpost_explode");
                        }
                    }

                    if (e.stateTimer <= 0f)
                    {
                        e.state = EnemyUnit.AIState.Chase;
                        e.attackTimer = 2.2f;
                    }
                }
                // Chase State
                else
                {
                    if (e.attackTimer > 0f) e.attackTimer -= dt;

                    if (toPlayer.magnitude > 0.1f)
                    {
                        e.rootObj.transform.rotation = Quaternion.Slerp(e.rootObj.transform.rotation, Quaternion.LookRotation(moveDir), 12f * dt);
                    }

                    // Trigger Telegraph when in sweet charge range (4m to 14m)
                    if (distToPlayer <= 14f && distToPlayer >= 3.8f && e.attackTimer <= 0f)
                    {
                        e.state = EnemyUnit.AIState.Telegraph;
                        e.stateTimer = 0.72f;
                        e.chargeDirection = moveDir;
                        if (e.telegraphVisual != null) SolarisConfig.SafeDestroy(e.telegraphVisual);
                        e.telegraphVisual = CreateTelegraphLine(e.position, e.position + e.chargeDirection * 15f, 0.40f);
                    }
                    else if (distToPlayer > 2.0f)
                    {
                        e.position += moveDir * currentSpeed * dt;
                    }
                    else
                    {
                        // Melee bite
                        e.attackTimer += dt;
                        if (e.attackTimer >= 1.0f)
                        {
                            e.attackTimer = 0f;
                            _player?.TakeDamage(8f);
                            SolarisGameManager.Instance?.PlaySfx("enemy_hit");
                        }
                    }
                }

                // Snap y to ground
                if (_world != null) e.position.y = _world.GetHeight(e.position.x, e.position.z);
                e.rootObj.transform.position = e.position;

                // Animate 6 scuttling legs with dynamic gait speed
                if (e.legPivots != null)
                {
                    float legSpeed = e.state == EnemyUnit.AIState.Attacking ? 32f : 15f;
                    for (int l = 0; l < e.legPivots.Length; l++)
                    {
                        if (e.legPivots[l] == null) continue;
                        float phase = (l % 2 == 0) ? 0f : Mathf.PI;
                        float swing = Mathf.Sin(e.animTimer * legSpeed + phase) * 26f;
                        float lift = Mathf.Cos(e.animTimer * legSpeed + phase) * 15f;
                        e.legPivots[l].localRotation = Quaternion.Euler(swing, lift, 0f);
                    }
                }
            }
            // 2. Golem AI ("Smog Bloater" - Area Denial with Vacuum Deflation Counterplay)
            else if (e.type == "golem")
            {
                if (toPlayer.magnitude > 0.1f && e.state != EnemyUnit.AIState.Telegraph)
                {
                    e.rootObj.transform.rotation = Quaternion.Slerp(e.rootObj.transform.rotation, Quaternion.LookRotation(moveDir), 5f * dt);
                }

                // Telegraph Detonation Countdown State
                if (e.state == EnemyUnit.AIState.Telegraph)
                {
                    e.stateTimer -= dt;

                    // Torso expands and pulses violently
                    if (e.torso != null)
                    {
                        float pulse = 1.0f + Mathf.Abs(Mathf.Sin(e.animTimer * 18f)) * 0.45f;
                        e.torso.localScale = new Vector3(pulse, pulse, pulse);
                    }

                    // Vacuum Counterplay: player can suck the toxic gas to defuse explosion!
                    if (_player != null && _player.IsVacuuming && distToPlayer <= _player.vacuumRange)
                    {
                        Vector3 toEnemy = (e.position - _player.transform.position).normalized;
                        if (Vector3.Dot(_player.transform.forward, toEnemy) > 0.60f)
                        {
                            DamageEnemy(e, 45f * dt, -toEnemy, isHeavy: false);
                            _player.DustTank = Mathf.Min(_player.DustTankMax, _player.DustTank + 30f * dt);
                            if (e.isDead || e.hp <= 0f)
                            {
                                if (e.telegraphVisual != null) SolarisConfig.SafeDestroy(e.telegraphVisual);
                                continue;
                            }
                        }
                    }

                    // Fuse expired: Detonate!
                    if (e.stateTimer <= 0f)
                    {
                        if (distToPlayer <= 5.8f)
                        {
                            _player?.TakeDamage(25f);
                        }
                        if (_world != null)
                        {
                            _world.RecontaminateCircle(e.position, 5.8f, 0.45f, 1f);
                        }
                        CombatJuice.Instance?.EmitToxicSplatter(e.position, 32);
                        CombatJuice.Instance?.AddCameraTrauma(0.45f);
                        SolarisGameManager.Instance?.PlaySfx("outpost_explode");

                        if (e.telegraphVisual != null) SolarisConfig.SafeDestroy(e.telegraphVisual);
                        KillEnemy(e, false);
                        continue;
                    }
                }
                // Chase State
                else
                {
                    // Trigger Detonation Telegraph if near player or critically wounded (< 35% HP)
                    if (distToPlayer <= 4.2f || (e.hp <= e.maxHp * 0.35f && distToPlayer <= 8.5f))
                    {
                        e.state = EnemyUnit.AIState.Telegraph;
                        e.stateTimer = 1.15f;
                        if (e.telegraphVisual != null) SolarisConfig.SafeDestroy(e.telegraphVisual);
                        e.telegraphVisual = CreateTelegraphCircle(e.position, 5.5f);
                    }
                    else if (distToPlayer > 3.6f)
                    {
                        e.position += moveDir * currentSpeed * dt;
                    }
                }

                // Snap y to ground
                if (_world != null) e.position.y = _world.GetHeight(e.position.x, e.position.z);
                e.rootObj.transform.position = e.position;

                // Heavy walker animation
                if (e.legPivots != null && e.legPivots.Length >= 2 && e.state != EnemyUnit.AIState.Telegraph)
                {
                    if (e.legPivots[0] != null) e.legPivots[0].localRotation = Quaternion.Euler(Mathf.Sin(e.animTimer * 4.5f) * 26f, 0, 0);
                    if (e.legPivots[1] != null) e.legPivots[1].localRotation = Quaternion.Euler(-Mathf.Sin(e.animTimer * 4.5f) * 26f, 0, 0);
                }
            }
            // 3. Wraith AI ("Slag Mortar Drone" - Artillery with Lobbed Mortar Shell)
            else if (e.type == "wraith")
            {
                // Hover 2.2m above ground plane with smooth oscillation
                float groundY = _world != null ? _world.GetHeight(e.position.x, e.position.z) : playerPos.y;
                e.position.y = groundY + 2.2f + Mathf.Sin(e.animTimer * 3.2f) * 0.35f;

                // Standoff distance around ~12m from player
                if (distToPlayer > 15f)
                {
                    e.position += moveDir * currentSpeed * dt;
                }
                else if (distToPlayer < 9.0f)
                {
                    e.position -= moveDir * (currentSpeed * 0.85f) * dt;
                }
                else
                {
                    // Circle/strafe around player
                    Vector3 strafe = Vector3.Cross(moveDir, Vector3.up) * (e.id.GetHashCode() % 2 == 0 ? 1f : -1f);
                    e.position += strafe * (currentSpeed * 0.75f) * dt;
                }

                e.rootObj.transform.position = e.position;
                if (toPlayer.magnitude > 0.1f)
                {
                    e.rootObj.transform.rotation = Quaternion.Slerp(e.rootObj.transform.rotation, Quaternion.LookRotation(moveDir), 8f * dt);
                }

                // Rotate crimson energy rings
                if (e.energyRings != null)
                {
                    if (e.energyRings.Length > 0 && e.energyRings[0] != null) e.energyRings[0].Rotate(Vector3.up, 180f * dt);
                    if (e.energyRings.Length > 1 && e.energyRings[1] != null) e.energyRings[1].Rotate(Vector3.forward, 240f * dt);
                    if (e.energyRings.Length > 2 && e.energyRings[2] != null) e.energyRings[2].Rotate(Vector3.right, 300f * dt);
                }

                // Fire lobbed toxic mortar with 1.25s ground danger circle
                e.attackTimer += dt;
                if (e.attackTimer >= 3.0f && distToPlayer <= 22f)
                {
                    e.attackTimer = 0f;
                    SpawnEnemyMortar(e.position, playerPos, 14f);
                    SolarisGameManager.Instance?.PlaySfx("sandstorm_wind");
                }
            }
        }
    }

    private void DespawnEnemy(EnemyUnit enemy)
    {
        if (enemy.telegraphVisual != null)
        {
            SolarisConfig.SafeDestroy(enemy.telegraphVisual);
            enemy.telegraphVisual = null;
        }
        if (enemy.sourceOutpost != null)
        {
            if (enemy.type == "crawler") enemy.sourceOutpost.activeCrawlers = Mathf.Max(0, enemy.sourceOutpost.activeCrawlers - 1);
            else if (enemy.type == "golem") enemy.sourceOutpost.activeGolems = Mathf.Max(0, enemy.sourceOutpost.activeGolems - 1);
        }
        if (enemy.healthBar != null && enemy.healthBar.rootObj != null)
        {
            SolarisConfig.SafeDestroy(enemy.healthBar.rootObj);
        }
        if (enemy.rootObj != null)
        {
            SolarisConfig.SafeDestroy(enemy.rootObj);
        }
    }

    // ============================================================
    // 6. Weapons & Combat Methods
    // ============================================================
    public void FirePlayerBlaster(Vector3 muzzlePos, Vector3 dir, float damage)
    {
        GameObject bolt = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        bolt.name = "BlasterShard";
        bolt.transform.position = muzzlePos;
        bolt.transform.rotation = Quaternion.LookRotation(dir);
        bolt.transform.localScale = new Vector3(0.24f, 0.45f, 0.24f);
        bolt.GetComponent<Renderer>().sharedMaterial = _matCyanProjectile;
        SolarisConfig.SafeDestroy(bolt.GetComponent<Collider>());

        GameObject lightObj = new GameObject("BoltLight");
        lightObj.transform.SetParent(bolt.transform, false);
        Light boltLight = lightObj.AddComponent<Light>();
        boltLight.type = LightType.Point;
        boltLight.color = new Color(0.2f, 1f, 1f);
        boltLight.range = 5f;
        boltLight.intensity = 2f;

        ProjectileData p = new ProjectileData
        {
            rootObj = bolt,
            position = muzzlePos,
            velocity = dir.normalized * 46f,
            damage = damage,
            lifetime = 0f,
            maxLifetime = 1.6f,
            radius = 0.55f,
            isEnemy = false
        };
        _projectiles.Add(p);
    }

    private void SpawnEnemyBolt(Vector3 startPos, Vector3 dir, float damage)
    {
        GameObject bolt = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        bolt.name = "WraithBolt";
        bolt.transform.position = startPos;
        bolt.transform.rotation = Quaternion.LookRotation(dir);
        bolt.transform.localScale = new Vector3(0.55f, 0.55f, 0.55f);
        bolt.GetComponent<Renderer>().sharedMaterial = _matEnemyBolt;
        SolarisConfig.SafeDestroy(bolt.GetComponent<Collider>());

        GameObject lightObj = new GameObject("BoltLight");
        lightObj.transform.SetParent(bolt.transform, false);
        Light boltLight = lightObj.AddComponent<Light>();
        boltLight.type = LightType.Point;
        boltLight.color = new Color(1f, 0.15f, 0.15f);
        boltLight.range = 6f;
        boltLight.intensity = 2.2f;

        ProjectileData p = new ProjectileData
        {
            rootObj = bolt,
            position = startPos,
            velocity = dir.normalized * 28f,
            damage = damage,
            lifetime = 0f,
            maxLifetime = 2.0f,
            radius = 0.65f,
            isEnemy = true
        };
        _projectiles.Add(p);
    }

    public void FireSolarLance(Vector3 muzzlePos, Vector3 dir, float maxRange, float damage)
    {
        Vector3 endPos = muzzlePos + dir.normalized * maxRange;

        // Visual Piercing Beam
        GameObject beamObj = new GameObject("SolarLanceBeam");
        LineRenderer line = beamObj.AddComponent<LineRenderer>();
        line.positionCount = 2;
        line.SetPosition(0, muzzlePos);
        line.SetPosition(1, endPos);
        line.startWidth = 0.42f;
        line.endWidth = 0.28f;
        line.sharedMaterial = _matCyanProjectile;
        line.startColor = new Color(1f, 0.9f, 0.2f, 1f);
        line.endColor = new Color(0.2f, 1f, 1f, 0.8f);

        _laserBeams.Add(new LaserBeamData
        {
            rootObj = beamObj,
            line = line,
            timer = 0f,
            duration = 0.35f,
            initialWidth = 0.42f
        });

        SolarisGameManager.Instance?.PlaySfx("solar_lance");

        // Piercing Raycast / Segment Collision on ALL entities along ray
        for (int i = ActiveEnemies.Count - 1; i >= 0; i--)
        {
            var e = ActiveEnemies[i];
            if (e.isDead) continue;
            if (DistToSegment(e.position, muzzlePos, endPos) <= e.radius + 1.2f)
            {
                DamageEnemy(e, damage);
                SpawnHitSparks(e.position, Color.cyan);
            }
        }

        for (int i = 0; i < Outposts.Count; i++)
        {
            var op = Outposts[i];
            if (op.destroyed) continue;

            // Vents along ray
            for (int v = 0; v < op.vents.Count; v++)
            {
                var vt = op.vents[v];
                if (!vt.destroyed && DistToSegment(vt.position, muzzlePos, endPos) <= vt.radius + 1.2f)
                {
                    DamageVent(vt, damage);
                    SpawnHitSparks(vt.position, Color.yellow);
                }
            }

            // Mound along ray
            if (op.type == "mound" && DistToSegment(op.position, muzzlePos, endPos) <= op.radius + 1.2f)
            {
                DamageOutpost(op, damage);
                SpawnHitSparks(op.position, Color.green);
            }
            // Factory Core along ray
            else if (op.type == "factory" && DistToSegment(op.position, muzzlePos, endPos) <= op.radius + 1.2f)
            {
                if (op.shielded)
                {
                    SolarisUIManager.Instance?.ShowToast("🛡️ KALKAN AKTİF! ÖNCE 3 SOĞUTMA VENTİNİ VURUN");
                    SpawnHitSparks(op.position + Vector3.up * 6f, Color.red);
                }
                else
                {
                    DamageOutpost(op, damage);
                    SpawnHitSparks(op.position + Vector3.up * 13.5f, Color.red);
                }
            }
        }
    }

    public void FireDroneLaser(Vector3 startPos, EnemyUnit target, float damage)
    {
        if (target == null || target.isDead || target.rootObj == null) return;

        Vector3 targetPos = target.position + Vector3.up * 0.6f;
        GameObject beamObj = new GameObject("DroneLaserBeam");
        LineRenderer line = beamObj.AddComponent<LineRenderer>();
        line.positionCount = 2;
        line.SetPosition(0, startPos);
        line.SetPosition(1, targetPos);
        line.startWidth = 0.18f;
        line.endWidth = 0.12f;
        line.sharedMaterial = _matCyanProjectile;
        line.startColor = new Color(0.2f, 1f, 1f, 1f);
        line.endColor = new Color(0.2f, 0.8f, 1f, 0.7f);

        _laserBeams.Add(new LaserBeamData
        {
            rootObj = beamObj,
            line = line,
            timer = 0f,
            duration = 0.15f,
            initialWidth = 0.18f
        });

        DamageEnemy(target, damage);
        SpawnHitSparks(targetPos, Color.cyan);
    }

    public void OnPlayerShockwave(Vector3 center, float radius, float damage)
    {
        SpawnShockwaveVisual(center, radius, new Color(0.2f, 1f, 1f, 0.85f), 0.45f);

        // AoE damage and knockback to enemies within radius
        for (int i = ActiveEnemies.Count - 1; i >= 0; i--)
        {
            var e = ActiveEnemies[i];
            if (e.isDead) continue;
            float dist = Vector3.Distance(center, e.position);
            if (dist <= radius)
            {
                DamageEnemy(e, damage);
                Vector3 knockDir = (e.position - center);
                knockDir.y = 0f;
                if (knockDir.sqrMagnitude < 0.001f) knockDir = Vector3.forward;
                e.knockbackVelocity = knockDir.normalized * 18f;
                SpawnHitSparks(e.position, Color.cyan);
            }
        }

        // Damage vents and scrap mounds within radius
        for (int i = 0; i < Outposts.Count; i++)
        {
            var op = Outposts[i];
            if (op.destroyed) continue;

            for (int v = 0; v < op.vents.Count; v++)
            {
                var vt = op.vents[v];
                if (!vt.destroyed && Vector3.Distance(center, vt.position) <= radius)
                {
                    DamageVent(vt, damage);
                }
            }

            if (op.type == "mound" && Vector3.Distance(center, op.position) <= radius)
            {
                DamageOutpost(op, damage);
            }
        }
    }

    public void ApplyVacuumPull(Vector3 origin, Vector3 forward, float range, float dt)
    {
        for (int i = ActiveEnemies.Count - 1; i >= 0; i--)
        {
            var e = ActiveEnemies[i];
            if (e.isDead || e.type != "crawler") continue;

            Vector3 toEnemy = e.position - origin;
            float dist = toEnemy.magnitude;
            if (dist <= range && Vector3.Angle(forward, toEnemy) <= 45f)
            {
                // Pull toward nozzle
                e.position = Vector3.MoveTowards(e.position, origin, 13.5f * dt);
                if (_world != null) e.position.y = _world.GetHeight(e.position.x, e.position.z);
                if (e.rootObj != null) e.rootObj.transform.position = e.position;

                // Weakened crawler shredding: hp < 35% and within 3m
                if (e.hp < e.maxHp * 0.35f && dist <= 3.0f)
                {
                    KillEnemy(e, shred: true);
                    if (_player != null)
                    {
                        _player.Silica += 2;
                        _player.DustTank = Mathf.Min(_player.DustTank + 12f, _player.DustTankMax);
                    }
                    SolarisUIManager.Instance?.ShowToast("🌀 CRAWLER PARÇALANDI! +2 💎 SİLİKA, +12 TOZ");
                    SolarisGameManager.Instance?.PlaySfx("dust_collect");
                }
            }
        }
    }

    public EnemyUnit GetNearestEnemy(Vector3 pos, float maxDist)
    {
        EnemyUnit closest = null;
        float minDist = maxDist;

        for (int i = 0; i < ActiveEnemies.Count; i++)
        {
            var e = ActiveEnemies[i];
            if (e.isDead || e.rootObj == null) continue;
            float d = Vector3.Distance(pos, e.position);
            if (d < minDist)
            {
                minDist = d;
                closest = e;
            }
        }
        return closest;
    }

    // ============================================================
    // 7. Damage, Destruction & Health Bar Handling
    // ============================================================
    public void DamageEnemy(EnemyUnit enemy, float dmg, Vector3? impactDir = null, bool isHeavy = false)
    {
        if (enemy.isDead) return;

        enemy.hp -= dmg;
        enemy.healthBarFadeTimer = 1.6f;
        UpdateHealthBarFill(enemy.healthBar, enemy.hp, enemy.maxHp);

        Vector3 dir = impactDir ?? (enemy.position - (_player != null ? _player.transform.position : Vector3.zero)).normalized;
        CombatJuice.Instance?.ApplyHitFeedback(enemy.rootObj != null ? enemy.rootObj.transform : null, enemy.position, dir, isHeavy);

        if (enemy.hp <= 0f)
        {
            KillEnemy(enemy, shred: false, impactDir: dir);
        }
    }

    private void KillEnemy(EnemyUnit enemy, bool shred, Vector3? impactDir = null)
    {
        enemy.isDead = true;
        enemy.hp = 0f;

        if (enemy.telegraphVisual != null)
        {
            SolarisConfig.SafeDestroy(enemy.telegraphVisual);
            enemy.telegraphVisual = null;
        }

        Vector3 dir = impactDir ?? Vector3.up;
        CombatJuice.Instance?.EmitScrapSparks(enemy.position, dir, 22);
        CombatJuice.Instance?.EmitGlassShards(enemy.position, dir, 18);
        if (enemy.type == "golem") CombatJuice.Instance?.EmitToxicSplatter(enemy.position, 24);
        CombatJuice.Instance?.AddCameraTrauma(enemy.type == "golem" ? 0.42f : 0.22f);
        CombatJuice.Instance?.TriggerHitstop(0.06f);

        // Replenish ammo to RecycledWeaponSystem
        RecycledWeaponSystem.Instance?.ReplenishFromTrash(enemy.type == "golem" ? 14f : 6f);

        // Silica drop rewards
        int silicaReward = 0;
        if (enemy.type == "crawler") silicaReward = 2;
        else if (enemy.type == "golem") silicaReward = 6;
        else if (enemy.type == "wraith") silicaReward = 4;

        if (_player != null && !shred)
        {
            _player.Silica += silicaReward;
        }

        if (enemy.sourceOutpost != null)
        {
            if (enemy.type == "crawler") enemy.sourceOutpost.activeCrawlers = Mathf.Max(0, enemy.sourceOutpost.activeCrawlers - 1);
            else if (enemy.type == "golem") enemy.sourceOutpost.activeGolems = Mathf.Max(0, enemy.sourceOutpost.activeGolems - 1);
        }

        SolarisGameManager.Instance?.PlaySfx("enemy_hit");

        if (enemy.healthBar != null && enemy.healthBar.rootObj != null)
        {
            SolarisConfig.SafeDestroy(enemy.healthBar.rootObj);
        }
        if (enemy.rootObj != null)
        {
            SolarisConfig.SafeDestroy(enemy.rootObj);
        }
    }

    public void DamageVent(VentData vent, float dmg)
    {
        if (vent.destroyed) return;

        vent.hp -= dmg;
        UpdateHealthBarFill(vent.healthBar, vent.hp, vent.maxHp);

        if (vent.hp <= 0f)
        {
            DestroyVent(vent);
        }
    }

    private void DestroyVent(VentData vent)
    {
        vent.destroyed = true;
        vent.hp = 0f;
        if (vent.exhaustLight != null) vent.exhaustLight.enabled = false;
        if (vent.fanRenderer != null) vent.fanRenderer.sharedMaterial = _matDarkMetal;

        SpawnHitSparks(vent.position, Color.yellow);
        SolarisGameManager.Instance?.PlaySfx("outpost_explode");

        if (vent.healthBar != null && vent.healthBar.rootObj != null)
        {
            SolarisConfig.SafeDestroy(vent.healthBar.rootObj);
        }

        // Check if all 3 vents on parent factory are destroyed
        if (vent.parentOutpost != null)
        {
            bool anyAlive = false;
            for (int i = 0; i < vent.parentOutpost.vents.Count; i++)
            {
                if (!vent.parentOutpost.vents[i].destroyed)
                {
                    anyAlive = true;
                    break;
                }
            }

            if (!anyAlive && vent.parentOutpost.shielded)
            {
                vent.parentOutpost.shielded = false;
                if (vent.parentOutpost.shieldDomeObj != null)
                {
                    vent.parentOutpost.shieldDomeObj.SetActive(false);
                }
                SolarisUIManager.Instance?.ShowToast("⚡ SOĞUTMA VENTLERİ İMHA EDİLDİ! REAKTÖR KALKANI ÇÖKTÜ!");
                SolarisGameManager.Instance?.PlaySfx("upgrade_unlock");
            }
        }
    }

    public void DamageOutpost(OutpostData op, float dmg)
    {
        if (op.destroyed) return;

        if (op.type == "factory" && op.shielded)
        {
            SolarisUIManager.Instance?.ShowToast("🛡️ KALKAN AKTİF! ÖNCE 3 SOĞUTMA VENTİNİ VURUN");
            SpawnHitSparks(op.position + Vector3.up * 6f, Color.red);
            return;
        }

        op.hp -= dmg;
        UpdateHealthBarFill(op.healthBar, op.hp, op.maxHp);

        if (op.hp <= 0f)
        {
            DestroyOutpost(op);
        }
    }

    private void DestroyOutpost(OutpostData op)
    {
        op.destroyed = true;
        op.hp = 0f;
        DestroyedOutpostsCount++;

        if (op.coreLight != null) op.coreLight.enabled = false;
        if (op.smokeParticles != null) op.smokeParticles.Stop();
        if (op.healthBar != null && op.healthBar.rootObj != null) SolarisConfig.SafeDestroy(op.healthBar.rootObj);

        SolarisGameManager.Instance?.PlaySfx("outpost_explode");

        // Factory Destruction Rewards & Cleanup
        if (op.type == "factory")
        {
            if (_player != null) _player.Silica += 50;

            if (_world != null)
            {
                _world.CleanCircle(op.position, 28f, 3.8f);
                _world.SpawnOasisCluster(op.position);
            }

            SolarisUIManager.Instance?.ShowToast("🏭 FABRİKA İMHA EDİLDİ! +50 💎 SİLİKA");
            SpawnShockwaveVisual(op.position, 28f, new Color(1f, 0.4f, 0.1f, 0.9f), 0.8f);
        }
        // Scrap Mound Destruction Rewards & Cleanup
        else if (op.type == "mound")
        {
            if (_player != null) _player.Silica += 15;

            if (_world != null)
            {
                _world.CleanCircle(op.position, 20f, 3.2f);
            }

            SolarisUIManager.Instance?.ShowToast("♻️ HURDA YIĞINI TEMİZLENDİ! +15 💎 SİLİKA");
            SpawnShockwaveVisual(op.position, 20f, new Color(0.2f, 1f, 0.4f, 0.9f), 0.6f);
        }
    }

    // ============================================================
    // 8. Projectiles & Visual Effects Updates
    // ============================================================
    private void UpdateProjectiles(float dt)
    {
        for (int i = _projectiles.Count - 1; i >= 0; i--)
        {
            var p = _projectiles[i];
            p.lifetime += dt;

            if (p.lifetime >= p.maxLifetime || p.rootObj == null)
            {
                if (p.dangerCircle != null) SolarisConfig.SafeDestroy(p.dangerCircle);
                if (p.rootObj != null) SolarisConfig.SafeDestroy(p.rootObj);
                _projectiles.RemoveAt(i);
                continue;
            }

            // Lobbed mortar projectile handling
            if (p.isMortar)
            {
                float t = Mathf.Clamp01(p.lifetime / p.maxLifetime);
                Vector3 current = Vector3.Lerp(p.mortarStart, p.mortarTarget, t);
                current.y += Mathf.Sin(t * Mathf.PI) * 11f;
                p.position = current;
                if (p.rootObj != null) p.rootObj.transform.position = p.position;

                if (t >= 1f)
                {
                    if (_player != null && Vector3.Distance(p.mortarTarget, _player.transform.position) <= 3.4f)
                    {
                        _player.TakeDamage(p.damage);
                    }
                    CombatJuice.Instance?.EmitToxicSplatter(p.mortarTarget, 22);
                    CombatJuice.Instance?.AddCameraTrauma(0.28f);
                    SolarisGameManager.Instance?.PlaySfx("enemy_hit");

                    if (p.dangerCircle != null) SolarisConfig.SafeDestroy(p.dangerCircle);
                    if (p.rootObj != null) SolarisConfig.SafeDestroy(p.rootObj);
                    _projectiles.RemoveAt(i);
                    continue;
                }
                continue;
            }

            Vector3 nextPos = p.position + p.velocity * dt;

            // Player projectile collisions
            if (!p.isEnemy)
            {
                bool hit = false;

                // 1. Check Enemies
                for (int e = ActiveEnemies.Count - 1; e >= 0; e--)
                {
                    var enemy = ActiveEnemies[e];
                    if (enemy.isDead) continue;
                    if (Vector3.Distance(nextPos, enemy.position) <= (p.radius + enemy.radius))
                    {
                        Vector3 impactDir = p.velocity.normalized;
                        if (p.isHeavyKnockback)
                        {
                            enemy.isKnockedByPiston = true;
                            enemy.knockbackVelocity += impactDir * 28f;
                        }
                        else
                        {
                            enemy.knockbackVelocity += impactDir * 12f;
                        }

                        DamageEnemy(enemy, p.damage, impactDir, p.isHeavyKnockback);
                        hit = true;
                        break;
                    }
                }

                // 2. Check Cooling Vents
                if (!hit)
                {
                    for (int o = 0; o < Outposts.Count && !hit; o++)
                    {
                        var op = Outposts[o];
                        if (op.destroyed) continue;

                        for (int v = 0; v < op.vents.Count; v++)
                        {
                            var vt = op.vents[v];
                            if (!vt.destroyed && Vector3.Distance(nextPos, vt.position) <= (p.radius + vt.radius))
                            {
                                DamageVent(vt, p.damage);
                                SpawnHitSparks(nextPos, Color.yellow);
                                hit = true;
                                break;
                            }
                        }

                        if (hit) break;

                        // 3. Check Scrap Mound
                        if (op.type == "mound" && Vector3.Distance(nextPos, op.position) <= (p.radius + op.radius))
                        {
                            DamageOutpost(op, p.damage);
                            SpawnHitSparks(nextPos, Color.green);
                            hit = true;
                            break;
                        }
                        // 4. Check Factory Reactor Core
                        else if (op.type == "factory" && Vector3.Distance(nextPos, op.position) <= (p.radius + op.radius))
                        {
                            if (op.shielded)
                            {
                                SolarisUIManager.Instance?.ShowToast("🛡️ KALKAN AKTİF! ÖNCE 3 SOĞUTMA VENTİNİ VURUN");
                                SpawnHitSparks(nextPos, Color.red);
                            }
                            else
                            {
                                DamageOutpost(op, p.damage);
                                SpawnHitSparks(nextPos, Color.red);
                            }
                            hit = true;
                            break;
                        }
                    }
                }

                if (hit)
                {
                    if (p.rootObj != null) SolarisConfig.SafeDestroy(p.rootObj);
                    _projectiles.RemoveAt(i);
                    continue;
                }
            }
            // Enemy projectile collisions (against Player)
            else
            {
                if (_player != null && Vector3.Distance(nextPos, _player.transform.position) <= (p.radius + 1.15f))
                {
                    _player.TakeDamage(p.damage);
                    SpawnHitSparks(nextPos, Color.red);
                    SolarisGameManager.Instance?.PlaySfx("enemy_hit");

                    if (p.rootObj != null) SolarisConfig.SafeDestroy(p.rootObj);
                    _projectiles.RemoveAt(i);
                    continue;
                }
            }

            p.position = nextPos;
            if (p.rootObj != null) p.rootObj.transform.position = p.position;
        }
    }

    private void UpdateLaserBeams(float dt)
    {
        for (int i = _laserBeams.Count - 1; i >= 0; i--)
        {
            var lb = _laserBeams[i];
            lb.timer += dt;
            if (lb.timer >= lb.duration || lb.line == null || lb.rootObj == null)
            {
                if (lb.rootObj != null) SolarisConfig.SafeDestroy(lb.rootObj);
                _laserBeams.RemoveAt(i);
                continue;
            }

            float t = 1f - (lb.timer / lb.duration);
            lb.line.startWidth = lb.initialWidth * t;
            lb.line.endWidth = lb.initialWidth * 0.7f * t;
        }
    }

    private void UpdateShockwaves(float dt)
    {
        for (int i = _shockwaves.Count - 1; i >= 0; i--)
        {
            var sw = _shockwaves[i];
            sw.timer += dt;
            if (sw.timer >= sw.duration || sw.rootObj == null)
            {
                if (sw.rootObj != null) SolarisConfig.SafeDestroy(sw.rootObj);
                _shockwaves.RemoveAt(i);
                continue;
            }

            float progress = sw.timer / sw.duration;
            float currentR = Mathf.Lerp(1f, sw.maxRadius, progress);
            sw.rootObj.transform.localScale = new Vector3(currentR * 2f, 0.05f, currentR * 2f);

            if (sw.mat != null && sw.mat.HasProperty("_Color"))
            {
                Color c = sw.mat.color;
                c.a = (1f - progress) * 0.8f;
                sw.mat.color = c;
            }
        }
    }

    private void SpawnShockwaveVisual(Vector3 center, float maxRadius, Color color, float duration)
    {
        GameObject ring = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        ring.name = "ShockwaveRing";
        ring.transform.position = new Vector3(center.x, center.y + 0.1f, center.z);
        ring.transform.localScale = new Vector3(2f, 0.05f, 2f);
        SolarisConfig.SafeDestroy(ring.GetComponent<Collider>());

        Material ringMat = SolarisConfig.CreateMaterial(color, 0f, 0.8f, null, color);
        ring.GetComponent<Renderer>().sharedMaterial = ringMat;

        _shockwaves.Add(new ShockwaveVisualData
        {
            rootObj = ring,
            mat = ringMat,
            timer = 0f,
            duration = duration,
            maxRadius = maxRadius,
            center = center
        });
    }

    private void SpawnHitSparks(Vector3 pos, Color color)
    {
        GameObject spark = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        spark.name = "HitSpark";
        spark.transform.position = pos;
        spark.transform.localScale = new Vector3(0.6f, 0.6f, 0.6f);
        SolarisConfig.SafeDestroy(spark.GetComponent<Collider>());

        Material sparkMat = SolarisConfig.CreateMaterial(color, 0f, 0.9f, null, color * 2f);
        spark.GetComponent<Renderer>().sharedMaterial = sparkMat;

        SolarisConfig.SafeDestroy(spark, 0.16f);
    }

    // ============================================================
    // 9. World-Space Billboard Health Bars
    // ============================================================
    private WorldHealthBar CreateWorldHealthBar(string name, Transform parent, float yOffset, float maxHp, float baseWidth)
    {
        GameObject barGroup = new GameObject(name);
        barGroup.transform.SetParent(parent, false);
        barGroup.transform.localPosition = new Vector3(0f, yOffset, 0f);

        // Background dark quad
        GameObject bg = GameObject.CreatePrimitive(PrimitiveType.Quad);
        bg.name = "Bar_BG";
        bg.transform.SetParent(barGroup.transform, false);
        bg.transform.localPosition = Vector3.zero;
        bg.transform.localScale = new Vector3(baseWidth, 0.28f, 1f);
        bg.GetComponent<Renderer>().sharedMaterial = _matHealthBg;
        SolarisConfig.SafeDestroy(bg.GetComponent<Collider>());

        // Foreground fill quad
        GameObject fg = GameObject.CreatePrimitive(PrimitiveType.Quad);
        fg.name = "Bar_Fill";
        fg.transform.SetParent(barGroup.transform, false);
        fg.transform.localPosition = new Vector3(0f, 0f, -0.01f);
        fg.transform.localScale = new Vector3(baseWidth * 0.96f, 0.22f, 1f);
        fg.GetComponent<Renderer>().sharedMaterial = _matHealthFg;
        SolarisConfig.SafeDestroy(fg.GetComponent<Collider>());

        WorldHealthBar bar = new WorldHealthBar
        {
            rootObj = barGroup,
            fillTransform = fg.transform,
            fillRenderer = fg.GetComponent<Renderer>(),
            heightOffset = yOffset,
            maxHp = maxHp,
            currentHp = maxHp,
            baseWidth = baseWidth
        };

        // Initially hidden until entity takes damage
        barGroup.SetActive(false);
        return bar;
    }

    private void UpdateHealthBarFill(WorldHealthBar bar, float currentHp, float maxHp)
    {
        if (bar == null || bar.rootObj == null) return;

        bar.currentHp = Mathf.Max(0f, currentHp);
        bar.maxHp = maxHp;
        float ratio = Mathf.Clamp01(bar.currentHp / bar.maxHp);

        if (!bar.rootObj.activeSelf && ratio < 0.999f && ratio > 0f)
        {
            bar.rootObj.SetActive(true);
        }

        if (bar.fillTransform != null)
        {
            float totalW = bar.baseWidth * 0.96f;
            float currentW = totalW * ratio;
            bar.fillTransform.localScale = new Vector3(currentW, 0.22f, 1f);
            bar.fillTransform.localPosition = new Vector3(-totalW * 0.5f + currentW * 0.5f, 0f, -0.01f);
        }

        // Color coding: Green -> Amber -> Red
        if (bar.fillRenderer != null)
        {
            Color col = (ratio > 0.55f) ? Color.green : ((ratio > 0.25f) ? Color.yellow : Color.red);
            bar.fillRenderer.sharedMaterial.color = col;
        }

        if (ratio <= 0f)
        {
            bar.rootObj.SetActive(false);
        }
    }

    // ============================================================
    // 10. Math & Geometry Helpers
    // ============================================================
    private float DistToSegment(Vector3 p, Vector3 a, Vector3 b)
    {
        Vector3 ab = b - a;
        float lenSq = ab.sqrMagnitude;
        if (lenSq < 0.0001f) return Vector3.Distance(p, a);
        float t = Mathf.Clamp01(Vector3.Dot(p - a, ab) / lenSq);
        Vector3 proj = a + ab * t;
        return Vector3.Distance(p, proj);
    }

    private void InitSharedMaterials()
    {
        if (_matDarkMetal != null) return;

        _matDarkMetal = SolarisConfig.CreateMaterial(new Color(0.18f, 0.20f, 0.23f), 0.7f, 0.45f);
        _matRust = SolarisConfig.CreateMaterial(new Color(0.48f, 0.28f, 0.16f), 0.3f, 0.25f);
        _matToxicSludge = SolarisConfig.CreateMaterial(new Color(0.18f, 0.85f, 0.25f), 0.1f, 0.8f, null, new Color(0.2f, 1f, 0.3f) * 1.5f);
        _matReactorCore = SolarisConfig.CreateMaterial(new Color(1f, 0.15f, 0.15f), 0.2f, 0.8f, null, new Color(1f, 0.1f, 0.1f) * 2.5f);
        _matVentOrange = SolarisConfig.CreateMaterial(new Color(1f, 0.55f, 0.1f), 0.2f, 0.8f, null, new Color(1f, 0.45f, 0.05f) * 2f);
        _matCrawlerMetal = SolarisConfig.CreateMaterial(new Color(0.16f, 0.17f, 0.20f), 0.75f, 0.5f);
        _matWraithCrimson = SolarisConfig.CreateMaterial(new Color(0.9f, 0.1f, 0.15f), 0.3f, 0.9f, null, new Color(1f, 0.1f, 0.2f) * 2.2f);
        _matCyanProjectile = SolarisConfig.CreateMaterial(new Color(0.15f, 0.95f, 1f), 0.1f, 0.9f, null, new Color(0.2f, 1f, 1f) * 2.5f);
        _matEnemyBolt = SolarisConfig.CreateMaterial(new Color(1f, 0.2f, 0.2f), 0.1f, 0.9f, null, new Color(1f, 0.1f, 0.1f) * 2.5f);

        // Shield Dome Material (Translucent Red)
        Shader litShader = SolarisConfig.GetLitShader();
        _matShieldRed = new Material(litShader);
        Color shieldCol = new Color(1f, 0.15f, 0.2f, 0.35f);
        if (_matShieldRed.HasProperty("_Color")) _matShieldRed.SetColor("_Color", shieldCol);
        if (_matShieldRed.HasProperty("_BaseColor")) _matShieldRed.SetColor("_BaseColor", shieldCol);
        if (_matShieldRed.HasProperty("_EmissionColor"))
        {
            _matShieldRed.EnableKeyword("_EMISSION");
            _matShieldRed.SetColor("_EmissionColor", new Color(0.8f, 0.1f, 0.15f) * 0.8f);
        }
        _matShieldRed.SetFloat("_Mode", 3);
        _matShieldRed.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        _matShieldRed.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        _matShieldRed.SetInt("_ZWrite", 0);
        _matShieldRed.renderQueue = 3000;

        // Health bar materials
        Shader unlitShader = Shader.Find("Universal Render Pipeline/Unlit");
        if (unlitShader == null) unlitShader = Shader.Find("Sprites/Default");
        if (unlitShader == null) unlitShader = Shader.Find("Unlit/Color");

        _matHealthBg = new Material(unlitShader);
        _matHealthBg.color = new Color(0.08f, 0.09f, 0.12f, 0.85f);

        _matHealthFg = new Material(unlitShader);
        _matHealthFg.color = new Color(0.2f, 0.9f, 0.4f, 0.95f);

        _matTelegraphRed = SolarisConfig.CreateMaterial(new Color(1f, 0.15f, 0.15f, 0.85f), 0f, 0.9f, null, new Color(1f, 0.15f, 0.15f) * 2.8f);
    }

    public void RegisterExternalProjectile(ProjectileData p, bool isHeavyKnockback = false)
    {
        p.isHeavyKnockback = isHeavyKnockback;
        _projectiles.Add(p);
    }

    public void OnVentBlast(Vector3 origin, Vector3 forward, float range, float damage)
    {
        for (int i = ActiveEnemies.Count - 1; i >= 0; i--)
        {
            var e = ActiveEnemies[i];
            if (e.isDead) continue;
            Vector3 toEnemy = e.position - origin;
            float dist = toEnemy.magnitude;
            if (dist <= range)
            {
                float dot = Vector3.Dot(forward, toEnemy.normalized);
                if (dot > 0.45f) // Wide forward cone
                {
                    Vector3 knockDir = (toEnemy.normalized + Vector3.up * 0.15f).normalized;
                    e.knockbackVelocity += knockDir * 24f;
                    DamageEnemy(e, damage, knockDir, isHeavy: true);
                }
            }
        }
    }

    public GameObject CreateTelegraphLine(Vector3 start, Vector3 end, float width = 0.35f)
    {
        GameObject go = new GameObject("TelegraphLine");
        LineRenderer lr = go.AddComponent<LineRenderer>();
        lr.useWorldSpace = true;
        lr.positionCount = 2;
        lr.SetPosition(0, start + Vector3.up * 0.12f);
        lr.SetPosition(1, end + Vector3.up * 0.12f);
        lr.startWidth = width;
        lr.endWidth = width;
        if (_matTelegraphRed != null) lr.sharedMaterial = _matTelegraphRed;
        return go;
    }

    public GameObject CreateTelegraphCircle(Vector3 center, float radius)
    {
        GameObject go = new GameObject("TelegraphCircle");
        go.transform.position = center + Vector3.up * 0.12f;
        LineRenderer lr = go.AddComponent<LineRenderer>();
        lr.useWorldSpace = false;
        lr.loop = true;
        int segments = 32;
        lr.positionCount = segments;
        for (int i = 0; i < segments; i++)
        {
            float rad = (i / (float)segments) * Mathf.PI * 2f;
            lr.SetPosition(i, new Vector3(Mathf.Cos(rad) * radius, 0f, Mathf.Sin(rad) * radius));
        }
        lr.startWidth = 0.28f;
        lr.endWidth = 0.28f;
        if (_matTelegraphRed != null) lr.sharedMaterial = _matTelegraphRed;
        return go;
    }

    public void SpawnEnemyMortar(Vector3 startPos, Vector3 targetPos, float damage)
    {
        GameObject circle = CreateTelegraphCircle(targetPos, 3.4f);

        GameObject bolt = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        bolt.name = "WraithMortar";
        bolt.transform.position = startPos;
        bolt.transform.localScale = new Vector3(0.65f, 0.65f, 0.65f);
        bolt.GetComponent<Renderer>().sharedMaterial = _matToxicSludge;
        SolarisConfig.SafeDestroy(bolt.GetComponent<Collider>());

        ProjectileData p = new ProjectileData
        {
            rootObj = bolt,
            position = startPos,
            mortarStart = startPos,
            mortarTarget = targetPos,
            velocity = Vector3.zero,
            damage = damage,
            lifetime = 0f,
            maxLifetime = 1.25f,
            radius = 0.75f,
            isEnemy = true,
            isMortar = true,
            dangerCircle = circle
        };
        _projectiles.Add(p);
    }
}
