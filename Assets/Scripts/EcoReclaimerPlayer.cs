using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class EcoReclaimerPlayer : MonoBehaviour
{
    public static EcoReclaimerPlayer Instance { get; private set; }

    [Header("Core Stats & V2.0 Economy")]
    public float HP = 100f;
    public float MaxHP = 100f;
    public float DustTank = 0f;
    public float DustTankMax = 100f;
    public float Heat = 0f;
    public float HeatMax = 100f;
    public bool Overheated = false;
    public int Silica = 0;

    [Header("Equipment & Weapon Upgrades")]
    public int NozzleLevel = 0;
    public int TankLevel = 0;
    public int BlasterLevel = 0;
    public int LanceLevel = 0;
    public int DroneLevel = 0;

    [Header("Weapon Cooldowns")]
    public float ShockCooldown = 0f;
    public float LanceCooldown = 0f;
    public float BlasterCooldown = 0f;

    [Header("Eco-Dash (Hades Evasion)")]
    public bool IsDashing { get; private set; } = false;
    public float DashCooldown = 0f;
    public const float DASH_COOLDOWN_TIME = 0.85f;
    private float _dashTimer = 0f;
    private Vector3 _dashDirection = Vector3.forward;
    private ParticleSystem _dashDustPS;

    [Header("State & Environmental Forces")]
    public bool IsVacuuming { get; private set; }
    public bool StormSlowActive = false;
    public Vector3 StormWindForce = Vector3.zero;
    public Transform MuzzleTransform { get; private set; }

    [Header("Operational Parameters")]
    public float vacuumRange = 10.5f;
    public float vacuumPower = 3.6f;
    public float baseMoveSpeed = 14.5f;

    // Internal References
    private SandGridWorld _world;
    private Transform _charPivot;
    private Transform _leftLegPivot;
    private Transform _rightLegPivot;
    private Transform _leftArmPivot;
    private Transform _rightArmPivot;
    private Transform _dustTankCore;
    private Material _dustTankCoreMat;
    private Light _tankLight;
    private Light _headlight;
    private Transform _nozzle;
    private LineRenderer _vacuumLine;
    private LineRenderer _vacuumReticle;
    private Material _muzzleGlowMat;
    private Animator _animator;
    private float _walkCycle = 0f;

    // Weapon Recoil & Physical Pushback
    private Transform _riflePivot;
    private Vector3 _rifleBaseLocalPos = new Vector3(0.48f, 1.15f, 0.35f);
    private Quaternion _rifleBaseLocalRot = Quaternion.identity;
    private Vector3 _recoilVelocity = Vector3.zero;

    // Astroneer-style closed-loop particle suction
    private ParticleSystem _suctionStreamPS;
    private ParticleSystem.Particle[] _particleBuffer = new ParticleSystem.Particle[512];

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
        if (_world == null)
        {
            SandGridWorld w = FindFirstObjectByType<SandGridWorld>();
            if (w != null) InitializePlayer(w);
        }
    }

    /// <summary>
    /// Initializes player position, world reference, and builds articulated humanoid mesh.
    /// </summary>
    public void InitializePlayer(SandGridWorld world)
    {
        _world = world;
        float startY = _world != null ? _world.GetHeight(0f, 28f) : 0f;
        transform.position = new Vector3(0f, startY + 0.04f, 28f);

        BuildHumanoidHierarchy();
        BuildSuctionParticleSystem();
        BuildDashParticleSystem();
    }

    private void BuildDashParticleSystem()
    {
        Transform old = transform.Find("DashDustPS");
        if (old != null)
        {
            if (Application.isPlaying) SolarisConfig.SafeDestroy(old.gameObject);
            else DestroyImmediate(old.gameObject);
        }

        GameObject psGo = new GameObject("DashDustPS", typeof(ParticleSystem));
        psGo.transform.SetParent(transform, false);
        _dashDustPS = psGo.GetComponent<ParticleSystem>();

        var main = _dashDustPS.main;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 256;
        main.startLifetime = 0.45f;
        main.startSpeed = 8f;
        main.playOnAwake = false;

        var em = _dashDustPS.emission;
        em.enabled = false;

        var rend = psGo.GetComponent<ParticleSystemRenderer>();
        if (rend != null)
        {
            Shader sh = Shader.Find("Particles/Standard Unlit") ?? Shader.Find("Sprites/Default");
            if (sh != null) rend.sharedMaterial = new Material(sh);
        }
    }

    private void EmitDashDust(Vector3 pos, Vector3 backwardDir)
    {
        if (_dashDustPS == null) return;
        for (int i = 0; i < 22; i++)
        {
            Vector3 vel = (backwardDir + Random.insideUnitSphere * 0.45f).normalized * Random.Range(6f, 16f) + Vector3.up * 1.5f;
            Color dustCol = Random.value > 0.4f ? new Color(0.85f, 0.72f, 0.52f, 0.8f) : new Color(0.4f, 0.95f, 1f, 0.7f);
            ParticleSystem.EmitParams ep = new ParticleSystem.EmitParams
            {
                position = pos + Random.insideUnitSphere * 0.3f,
                velocity = vel,
                startSize = Random.Range(0.25f, 0.65f),
                startLifetime = Random.Range(0.35f, 0.55f),
                startColor = dustCol
            };
            _dashDustPS.Emit(ep, 1);
        }
    }

    private void BuildSuctionParticleSystem()
    {
        Transform oldPs = transform.Find("AstroneerSuctionPS");
        if (oldPs != null)
        {
            if (Application.isPlaying) SolarisConfig.SafeDestroy(oldPs.gameObject);
            else DestroyImmediate(oldPs.gameObject);
        }

        GameObject psGo = new GameObject("AstroneerSuctionPS", typeof(ParticleSystem));
        psGo.transform.SetParent(transform, false);
        _suctionStreamPS = psGo.GetComponent<ParticleSystem>();

        var main = _suctionStreamPS.main;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 512;
        main.startLifetime = 0.85f;
        main.startSpeed = 3.2f;
        main.playOnAwake = false;

        var em = _suctionStreamPS.emission;
        em.enabled = false;

        var rend = psGo.GetComponent<ParticleSystemRenderer>();
        if (rend != null)
        {
            Shader sh = Shader.Find("Particles/Standard Unlit") ?? Shader.Find("Sprites/Default");
            if (sh != null) rend.sharedMaterial = new Material(sh);
        }
    }

    private void EmitSuctionParticles(Vector3 hitPoint, float radius)
    {
        if (_suctionStreamPS == null) return;
        int count = UnityEngine.Random.Range(4, 9);
        for (int i = 0; i < count; i++)
        {
            Vector2 offset = UnityEngine.Random.insideUnitCircle * (radius * 0.85f);
            Vector3 spawnPos = hitPoint + new Vector3(offset.x, 0.08f, offset.y);
            Vector3 launchVel = Vector3.up * UnityEngine.Random.Range(2.2f, 3.8f) + UnityEngine.Random.insideUnitSphere * 0.8f;

            Color pColor;
            int colType = UnityEngine.Random.Range(0, 5);
            if (colType == 0) pColor = new Color(0.72f, 0.75f, 0.78f, 0.95f);       // Metallic scrap
            else if (colType == 1) pColor = new Color(0.95f, 0.45f, 0.12f, 0.95f);  // Rust / orange can
            else if (colType == 2) pColor = new Color(0f, 0.90f, 1f, 0.95f);        // Cyan energy spark
            else if (colType == 3) pColor = new Color(0.18f, 0.65f, 0.25f, 0.90f);  // Green bottle shard
            else pColor = new Color(0.32f, 0.26f, 0.20f, 0.90f);                    // Earth / dirt clod

            bool isChunk = (i == 0 || i == 1);
            float pSize = isChunk ? UnityEngine.Random.Range(0.20f, 0.35f) : UnityEngine.Random.Range(0.08f, 0.16f);

            ParticleSystem.EmitParams ep = new ParticleSystem.EmitParams
            {
                position = spawnPos,
                velocity = launchVel,
                startSize = pSize,
                startLifetime = UnityEngine.Random.Range(0.55f, 0.85f),
                startColor = pColor
            };
            _suctionStreamPS.Emit(ep, 1);
        }
    }

    private void SteerSuctionParticles(float dt)
    {
        if (_suctionStreamPS == null || _suctionStreamPS.particleCount == 0) return;
        Vector3 nozzlePos = MuzzleTransform != null ? MuzzleTransform.position : (transform.position + transform.forward * 1.0f + Vector3.up * 1.2f);

        int alive = _suctionStreamPS.GetParticles(_particleBuffer);
        if (alive == 0) return;

        for (int i = 0; i < alive; i++)
        {
            Vector3 pos = _particleBuffer[i].position;
            Vector3 toNozzle = nozzlePos - pos;
            float dist = toNozzle.magnitude;

            if (dist > 0.18f)
            {
                Vector3 desiredVel = toNozzle.normalized * Mathf.Min(26f, dist * 16f);
                _particleBuffer[i].velocity = Vector3.Lerp(_particleBuffer[i].velocity, desiredVel, dt * 14f);
            }
            else
            {
                _particleBuffer[i].remainingLifetime = 0f;
            }
        }
        _suctionStreamPS.SetParticles(_particleBuffer, alive);
    }

    /// <summary>
    /// Builds articulated 3D Humanoid Eco-Reclaimer character hierarchy.
    /// </summary>
    private void BuildHumanoidHierarchy()
    {
        Transform oldPivot = transform.Find("CharPivot");
        if (oldPivot != null)
        {
            if (Application.isPlaying) SolarisConfig.SafeDestroy(oldPivot.gameObject);
            else DestroyImmediate(oldPivot.gameObject);
        }

        GameObject pivotGo = new GameObject("CharPivot");
        pivotGo.transform.SetParent(transform, false);
        pivotGo.transform.localPosition = new Vector3(0f, 0.04f, 0f);
        _charPivot = pivotGo.transform;

        // Shared PBR Materials
        Material armorMat = SolarisConfig.CreateMaterial(new Color(0.14f, 0.16f, 0.20f), 0.7f, 0.35f);
        Material platingMat = SolarisConfig.CreateMaterial(new Color(0.24f, 0.27f, 0.32f), 0.5f, 0.4f);
        Material darkCarbonMat = SolarisConfig.CreateMaterial(new Color(0.08f, 0.09f, 0.11f), 0.4f, 0.2f);
        Material cyanGlowMat = SolarisConfig.CreateMaterial(new Color(0f, 0.9f, 1f), 0.1f, 0.9f, null, new Color(0f, 0.9f, 1f) * 1.5f);
        Material amberGlowMat = SolarisConfig.CreateMaterial(new Color(1f, 0.6f, 0f), 0.1f, 0.8f, null, new Color(1f, 0.5f, 0f) * 1.2f);
        Material tankGlassMat = SolarisConfig.CreateMaterial(new Color(0.25f, 0.15f, 0.05f, 0.7f), 0.2f, 0.8f);

        // 1. Armored Torso, Chest Plate & Heavy Belt
        CreatePrimitivePart(PrimitiveType.Cube, "Torso", _charPivot, new Vector3(0f, 1.25f, 0f), new Vector3(0.8f, 0.95f, 0.45f), Quaternion.identity, armorMat);
        CreatePrimitivePart(PrimitiveType.Cube, "ChestPlate", _charPivot, new Vector3(0f, 1.4f, 0.23f), new Vector3(0.72f, 0.5f, 0.16f), Quaternion.identity, platingMat);
        CreatePrimitivePart(PrimitiveType.Cube, "Belt", _charPivot, new Vector3(0f, 0.82f, 0f), new Vector3(0.74f, 0.16f, 0.44f), Quaternion.identity, darkCarbonMat);

        // 2. Visored Eco-Helmet with Twin Side Respirators
        GameObject headGroup = new GameObject("HeadGroup");
        headGroup.transform.SetParent(_charPivot, false);
        headGroup.transform.localPosition = new Vector3(0f, 1.85f, 0f);

        CreatePrimitivePart(PrimitiveType.Cube, "Helmet", headGroup.transform, Vector3.zero, new Vector3(0.48f, 0.5f, 0.5f), Quaternion.identity, armorMat);
        CreatePrimitivePart(PrimitiveType.Cube, "Visor", headGroup.transform, new Vector3(0f, 0.04f, 0.24f), new Vector3(0.4f, 0.18f, 0.12f), Quaternion.identity, cyanGlowMat);
        CreatePrimitivePart(PrimitiveType.Cylinder, "FilterL", headGroup.transform, new Vector3(-0.25f, -0.12f, 0.14f), new Vector3(0.16f, 0.09f, 0.16f), Quaternion.Euler(0f, 0f, 90f), darkCarbonMat);
        CreatePrimitivePart(PrimitiveType.Cylinder, "FilterR", headGroup.transform, new Vector3(0.25f, -0.12f, 0.14f), new Vector3(0.16f, 0.09f, 0.16f), Quaternion.Euler(0f, 0f, 90f), darkCarbonMat);

        // 3. Articulated Legs (Left & Right Pivots)
        GameObject leftLeg = new GameObject("LeftLegPivot");
        leftLeg.transform.SetParent(_charPivot, false);
        leftLeg.transform.localPosition = new Vector3(-0.24f, 0.75f, 0f);
        _leftLegPivot = leftLeg.transform;
        CreatePrimitivePart(PrimitiveType.Cube, "ThighL", _leftLegPivot, new Vector3(0f, -0.35f, 0f), new Vector3(0.28f, 0.75f, 0.32f), Quaternion.identity, armorMat);
        CreatePrimitivePart(PrimitiveType.Cube, "BootL", _leftLegPivot, new Vector3(0f, -0.65f, 0.06f), new Vector3(0.32f, 0.25f, 0.44f), Quaternion.identity, darkCarbonMat);

        GameObject rightLeg = new GameObject("RightLegPivot");
        rightLeg.transform.SetParent(_charPivot, false);
        rightLeg.transform.localPosition = new Vector3(0.24f, 0.75f, 0f);
        _rightLegPivot = rightLeg.transform;
        CreatePrimitivePart(PrimitiveType.Cube, "ThighR", _rightLegPivot, new Vector3(0f, -0.35f, 0f), new Vector3(0.28f, 0.75f, 0.32f), Quaternion.identity, armorMat);
        CreatePrimitivePart(PrimitiveType.Cube, "BootR", _rightLegPivot, new Vector3(0f, -0.65f, 0.06f), new Vector3(0.32f, 0.25f, 0.44f), Quaternion.identity, darkCarbonMat);

        // 4. Articulated Arms (Left & Right Pivots)
        GameObject leftArm = new GameObject("LeftArmPivot");
        leftArm.transform.SetParent(_charPivot, false);
        leftArm.transform.localPosition = new Vector3(-0.52f, 1.6f, 0f);
        _leftArmPivot = leftArm.transform;
        CreatePrimitivePart(PrimitiveType.Cube, "ArmL", _leftArmPivot, new Vector3(0f, -0.32f, 0f), new Vector3(0.22f, 0.7f, 0.22f), Quaternion.identity, armorMat);

        GameObject rightArm = new GameObject("RightArmPivot");
        rightArm.transform.SetParent(_charPivot, false);
        rightArm.transform.localPosition = new Vector3(0.52f, 1.6f, 0f);
        _rightArmPivot = rightArm.transform;
        CreatePrimitivePart(PrimitiveType.Cube, "ArmR", _rightArmPivot, new Vector3(0f, -0.22f, 0.16f), new Vector3(0.22f, 0.7f, 0.22f), Quaternion.Euler(-45f, 0f, 0f), armorMat);

        // 5. Glowing Backpack Dust Tank
        CreatePrimitivePart(PrimitiveType.Cube, "TankFrame", _charPivot, new Vector3(0f, 1.25f, -0.28f), new Vector3(0.65f, 0.85f, 0.32f), Quaternion.identity, darkCarbonMat);
        CreatePrimitivePart(PrimitiveType.Cylinder, "TankGlass", _charPivot, new Vector3(0f, 1.25f, -0.34f), new Vector3(0.36f, 0.38f, 0.36f), Quaternion.identity, tankGlassMat);

        GameObject coreGo = CreatePrimitivePart(PrimitiveType.Cylinder, "DustTankCore", _charPivot, new Vector3(0f, 1.25f, -0.34f), new Vector3(0.28f, 0.35f, 0.28f), Quaternion.identity, amberGlowMat);
        _dustTankCore = coreGo.transform;
        _dustTankCoreMat = amberGlowMat;

        GameObject tankLightGo = new GameObject("TankLight");
        tankLightGo.transform.SetParent(_charPivot, false);
        tankLightGo.transform.localPosition = new Vector3(0f, 1.25f, -0.45f);
        _tankLight = tankLightGo.AddComponent<Light>();
        _tankLight.type = LightType.Point;
        _tankLight.color = new Color(1f, 0.6f, 0.1f);
        _tankLight.range = 6f;
        _tankLight.intensity = 0.3f;

        // 6. Shoulder Headlight (SpotLight, activates during storms)
        CreatePrimitivePart(PrimitiveType.Cylinder, "HeadlightMount", _charPivot, new Vector3(-0.38f, 1.76f, 0.18f), new Vector3(0.16f, 0.08f, 0.16f), Quaternion.Euler(90f, 0f, 0f), darkCarbonMat);
        CreatePrimitivePart(PrimitiveType.Cylinder, "HeadlightLens", _charPivot, new Vector3(-0.38f, 1.76f, 0.27f), new Vector3(0.18f, 0.04f, 0.18f), Quaternion.Euler(90f, 0f, 0f), cyanGlowMat);

        GameObject hlGo = new GameObject("Headlight");
        hlGo.transform.SetParent(_charPivot, false);
        hlGo.transform.localPosition = new Vector3(-0.38f, 1.76f, 0.3f);
        hlGo.transform.localRotation = Quaternion.identity;
        _headlight = hlGo.AddComponent<Light>();
        _headlight.type = LightType.Spot;
        _headlight.color = new Color(0.7f, 0.9f, 1f);
        _headlight.range = 36f;
        _headlight.spotAngle = 45f;
        _headlight.intensity = 2.5f;
        _headlight.enabled = false;

        // 7. Right-Hand Dual-Mode Cyclone Vacuum / Silica Blaster Rifle
        GameObject rifleGroup = new GameObject("DualModeRifle");
        rifleGroup.transform.SetParent(_charPivot, false);
        rifleGroup.transform.localPosition = new Vector3(0.48f, 1.15f, 0.35f);

        CreatePrimitivePart(PrimitiveType.Cube, "RifleBody", rifleGroup.transform, new Vector3(0f, 0f, 0.3f), new Vector3(0.18f, 0.24f, 1.1f), Quaternion.identity, darkCarbonMat);
        CreatePrimitivePart(PrimitiveType.Cube, "RifleTrim", rifleGroup.transform, new Vector3(0f, 0.05f, 0.25f), new Vector3(0.2f, 0.14f, 0.75f), Quaternion.identity, platingMat);

        GameObject nozzleGo = CreatePrimitivePart(PrimitiveType.Cylinder, "CycloneNozzle", rifleGroup.transform, new Vector3(0f, 0f, 0.95f), new Vector3(0.24f, 0.2f, 0.24f), Quaternion.Euler(90f, 0f, 0f), platingMat);
        _nozzle = nozzleGo.transform;

        _muzzleGlowMat = cyanGlowMat;
        CreatePrimitivePart(PrimitiveType.Cylinder, "MuzzleGlowRing", rifleGroup.transform, new Vector3(0f, 0f, 1.12f), new Vector3(0.26f, 0.03f, 0.26f), Quaternion.Euler(90f, 0f, 0f), _muzzleGlowMat);

        GameObject muzGo = new GameObject("MuzzleTransform");
        muzGo.transform.SetParent(rifleGroup.transform, false);
        muzGo.transform.localPosition = new Vector3(0f, 0f, 1.25f);
        MuzzleTransform = muzGo.transform;

        _vacuumLine = muzGo.AddComponent<LineRenderer>();
        _vacuumLine.useWorldSpace = true;
        _vacuumLine.startWidth = 0.08f;
        _vacuumLine.endWidth = 0.42f;
        _vacuumLine.material = cyanGlowMat;
        _vacuumLine.positionCount = 2;
        _vacuumLine.enabled = false;

        GameObject reticleGo = new GameObject("ExcavationReticle");
        reticleGo.transform.SetParent(transform, false);
        _vacuumReticle = reticleGo.AddComponent<LineRenderer>();
        _vacuumReticle.useWorldSpace = true;
        _vacuumReticle.loop = true;
        _vacuumReticle.startWidth = 0.15f;
        _vacuumReticle.endWidth = 0.15f;
        _vacuumReticle.material = cyanGlowMat;
        _vacuumReticle.positionCount = 36;
        _vacuumReticle.enabled = false;

        _animator = GetComponentInChildren<Animator>();
    }

    private GameObject CreatePrimitivePart(PrimitiveType type, string name, Transform parent, Vector3 localPos, Vector3 localScale, Quaternion localRot, Material mat)
    {
        GameObject go = GameObject.CreatePrimitive(type);
        go.name = name;
        Collider col = go.GetComponent<Collider>();
        if (col != null)
        {
            if (Application.isPlaying) SolarisConfig.SafeDestroy(col);
            else DestroyImmediate(col);
        }
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        go.transform.localScale = localScale;
        go.transform.localRotation = localRot;
        if (mat != null)
        {
            MeshRenderer mr = go.GetComponent<MeshRenderer>();
            if (mr != null) mr.sharedMaterial = mat;
        }
        return go;
    }

    private void Update()
    {
        // 1. Guard check: only process input when game is running and unpaused
        if (SolarisGameManager.Instance != null && (!SolarisGameManager.Instance.GameStarted || SolarisGameManager.Instance.IsPaused))
        {
            if (IsVacuuming) SetVacuumActive(false);
            return;
        }

        float dt = Time.deltaTime;

        // 2. Cooldown decrements
        if (ShockCooldown > 0f) ShockCooldown -= dt;
        if (LanceCooldown > 0f) LanceCooldown -= dt;
        if (BlasterCooldown > 0f) BlasterCooldown -= dt;
        if (DashCooldown > 0f) DashCooldown -= dt;

        if (IsDashing)
        {
            _dashTimer -= dt;
            if (_dashTimer <= 0f) IsDashing = false;
        }

        // 3. Oasis Bio-Field Buffs
        bool inOasis = _world != null && _world.IsInOasis(transform.position);
        if (inOasis)
        {
            HP = Mathf.Min(MaxHP, HP + 8f * dt);
        }

        // 4. Weapon Thermal Cooling
        float coolRate = 18f * (inOasis ? 2.5f : 1.0f);
        if (Heat > 0f)
        {
            Heat -= coolRate * dt;
            if (Heat <= 0f)
            {
                Heat = 0f;
                Overheated = false;
            }
        }
        if (Overheated && Heat < 28f)
        {
            Overheated = false;
        }

        // Update Muzzle Glow Ring based on Heat (Diegetic feedback)
        if (_muzzleGlowMat != null)
        {
            float heatRatio = Mathf.Clamp01(Heat / HeatMax);
            Color glowColor = Overheated
                ? (Mathf.Sin(Time.time * 24f) > 0f ? Color.red * 3.5f : new Color(0.6f, 0.1f, 0f))
                : Color.Lerp(new Color(0f, 0.9f, 1f) * 1.5f, new Color(1f, 0.35f, 0.05f) * 2.5f, heatRatio);
            _muzzleGlowMat.SetColor("_EmissionColor", glowColor);
        }

        // 5. Shoulder Headlight
        if (_headlight != null)
        {
            _headlight.enabled = StormSlowActive;
        }

        // 6. Dynamic Dust Tank Visual Scaling & Amber Glow
        UpdateDustTankVisual();

        // 7. Movement & Aiming
        ProcessMovementAndAim(dt, inOasis);

        // 8. Player Actions & Weapons
        ProcessActions(dt);
    }

    private void UpdateDustTankVisual()
    {
        float ratio = Mathf.Clamp01(DustTank / DustTankMax);
        if (_dustTankCore != null)
        {
            _dustTankCore.localScale = new Vector3(0.28f * (1f + ratio * 0.25f), 0.35f, 0.28f * (1f + ratio * 0.25f));
        }
        if (_tankLight != null)
        {
            _tankLight.intensity = 0.2f + ratio * 1.6f;
        }
        if (_dustTankCoreMat != null)
        {
            Color c = Color.Lerp(new Color(0.45f, 0.2f, 0f), new Color(1f, 0.65f, 0f), ratio);
            _dustTankCoreMat.SetColor("_EmissionColor", c * (0.5f + ratio * 2.0f));
        }
    }

    private void ProcessMovementAndAim(float dt, bool inOasis)
    {
        // Eco-Dash Movement Override
        if (IsDashing)
        {
            Vector3 dashMove = _dashDirection * (36f * dt);
            Vector3 dashTarget = transform.position + dashMove;
            dashTarget.x = Mathf.Clamp(dashTarget.x, -206f, 206f);
            dashTarget.z = Mathf.Clamp(dashTarget.z, -206f, 206f);
            if (_world != null)
            {
                dashTarget = _world.ResolveBuildingCollisions(dashTarget, 1.15f);
                dashTarget.y = _world.GetHeight(dashTarget.x, dashTarget.z) + 0.04f;
            }
            transform.position = dashTarget;

            if (_dashDirection.sqrMagnitude > 0.01f)
            {
                transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(_dashDirection), 20f * dt);
            }
            return;
        }

        float h = Input.GetAxisRaw("Horizontal");
        float v = Input.GetAxisRaw("Vertical");
        Vector3 inputDir = new Vector3(h, 0f, v);
        if (inputDir.sqrMagnitude > 1f) inputDir.Normalize();

        float speed = baseMoveSpeed;
        if (StormSlowActive && !inOasis)
        {
            speed *= 0.65f; // 35% slower outside Oasis during storms
        }

        Vector3 moveDelta = inputDir * speed * dt;
        if (StormSlowActive && !inOasis)
        {
            moveDelta += StormWindForce * dt;
        }

        Vector3 targetPos = transform.position + moveDelta;
        targetPos.x = Mathf.Clamp(targetPos.x, -206f, 206f);
        targetPos.z = Mathf.Clamp(targetPos.z, -206f, 206f);

        if (_world != null)
        {
            targetPos = _world.ResolveBuildingCollisions(targetPos, 1.15f);
            targetPos.y = _world.GetHeight(targetPos.x, targetPos.z) + 0.04f;
        }

        transform.position = targetPos;

        // Aiming toward ground plane mouse hit point
        Ray ray = Camera.main != null ? Camera.main.ScreenPointToRay(Input.mousePosition) : new Ray(transform.position + Vector3.up * 20f, Vector3.down);
        Plane groundPlane = new Plane(Vector3.up, new Vector3(0f, transform.position.y, 0f));
        if (groundPlane.Raycast(ray, out float enter))
        {
            Vector3 aimHit = ray.GetPoint(enter);
            Vector3 aimDir = aimHit - transform.position;
            aimDir.y = 0f;
            if (aimDir.sqrMagnitude > 0.05f)
            {
                Quaternion targetRot = Quaternion.LookRotation(aimDir);
                transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, 14f * dt);
            }
        }

        // Procedural Animation / Walk Swing
        if (_animator != null)
        {
            _animator.SetFloat("Speed", inputDir.magnitude);
        }
        else
        {
            if (inputDir.sqrMagnitude > 0.01f)
            {
                _walkCycle += dt * 14f;
                float legAngle = Mathf.Sin(_walkCycle) * 35f;
                if (_leftLegPivot != null) _leftLegPivot.localRotation = Quaternion.Euler(legAngle, 0f, 0f);
                if (_rightLegPivot != null) _rightLegPivot.localRotation = Quaternion.Euler(-legAngle, 0f, 0f);
                if (_leftArmPivot != null) _leftArmPivot.localRotation = Quaternion.Euler(-legAngle * 0.7f, 0f, 0f);
            }
            else
            {
                _walkCycle = 0f;
                float breath = Mathf.Sin(Time.time * 2.5f) * 2f;
                if (_leftLegPivot != null) _leftLegPivot.localRotation = Quaternion.Slerp(_leftLegPivot.localRotation, Quaternion.identity, 8f * dt);
                if (_rightLegPivot != null) _rightLegPivot.localRotation = Quaternion.Slerp(_rightLegPivot.localRotation, Quaternion.identity, 8f * dt);
                if (_leftArmPivot != null) _leftArmPivot.localRotation = Quaternion.Slerp(_leftArmPivot.localRotation, Quaternion.Euler(breath, 0f, 0f), 8f * dt);
            }
        }
    }

    private void ProcessActions(float dt)
    {
        // A. Left-Click or [E] (Cyclone Vacuum & Scrap Reclamation)
        bool wantVacuum = (Input.GetMouseButton(0) || Input.GetKey(KeyCode.E)) && !Input.GetMouseButton(1);
        if (wantVacuum && !Overheated)
        {
            SetVacuumActive(true);

            // Raycast toward ground to find exact vacuum hit point
            Ray ray = Camera.main != null ? Camera.main.ScreenPointToRay(Input.mousePosition) : new Ray(transform.position + Vector3.up * 20f, Vector3.down);
            Plane groundPlane = new Plane(Vector3.up, new Vector3(0f, transform.position.y, 0f));
            Vector3 targetPoint = transform.position + transform.forward * 4.5f;
            if (groundPlane.Raycast(ray, out float enter))
            {
                targetPoint = ray.GetPoint(enter);
            }

            Vector3 toTarget = targetPoint - transform.position;
            toTarget.y = 0f;
            float dist = toTarget.magnitude;
            if (dist > vacuumRange)
            {
                targetPoint = transform.position + toTarget.normalized * vacuumRange;
            }
            else if (dist < 1.0f)
            {
                targetPoint = transform.position + (toTarget.sqrMagnitude > 0.01f ? toTarget.normalized : transform.forward) * 1.0f;
            }

            if (_world != null)
            {
                targetPoint.y = _world.GetHeight(targetPoint.x, targetPoint.z);
            }

            float brushRadius = 4.8f + NozzleLevel * 0.8f;
            float collected = _world != null ? _world.VacuumAtPoint(targetPoint, transform.position, brushRadius, vacuumPower, dt, this) : 0f;
            DustTank += collected * 16f;

            if (DustTank >= DustTankMax)
            {
                DustTank = DustTankMax;
                SmeltDust(false);
            }

            Vector3 pullDir = (targetPoint - transform.position).normalized;
            OutpostAndCombatManager.Instance?.ApplyVacuumPull(transform.position, pullDir, vacuumRange, dt);

            EmitSuctionParticles(targetPoint, brushRadius);

            Heat += 14f * dt;
            if (Heat >= HeatMax)
            {
                Heat = HeatMax;
                Overheated = true;
            }

            if (_nozzle != null)
            {
                _nozzle.Rotate(Vector3.forward, 900f * dt, Space.Self);
            }

            if (_vacuumLine != null && MuzzleTransform != null)
            {
                _vacuumLine.enabled = true;
                _vacuumLine.SetPosition(0, MuzzleTransform.position);
                _vacuumLine.SetPosition(1, targetPoint + Vector3.up * 0.08f);
            }

            if (_vacuumReticle != null)
            {
                _vacuumReticle.enabled = true;
                float pulse = 1.0f + Mathf.Sin(Time.time * 8f) * 0.05f;
                float r = brushRadius * pulse;
                float rotAngle = Time.time * 75f * Mathf.Deg2Rad;
                for (int p = 0; p < 36; p++)
                {
                    float ang = (float)p / 36f * Mathf.PI * 2f + rotAngle;
                    float px = targetPoint.x + Mathf.Cos(ang) * r;
                    float pz = targetPoint.z + Mathf.Sin(ang) * r;
                    float py = _world != null ? _world.GetHeight(px, pz) + 0.06f : targetPoint.y + 0.06f;
                    _vacuumReticle.SetPosition(p, new Vector3(px, py, pz));
                }
            }
        }
        else
        {
            if (IsVacuuming) SetVacuumActive(false);
            if (_vacuumLine != null) _vacuumLine.enabled = false;
            if (_vacuumReticle != null) _vacuumReticle.enabled = false;
        }

        // Steer all active suction particles in flight towards nozzle
        SteerSuctionParticles(dt);

        // B. Right-Click: Recycled Glass Shard Spreader (Shotgun)
        if (Input.GetMouseButton(1) && !Overheated)
        {
            Vector3 muzPos = MuzzleTransform != null ? MuzzleTransform.position : (transform.position + transform.forward * 1.2f + Vector3.up * 1.2f);
            if (RecycledWeaponSystem.Instance != null)
            {
                RecycledWeaponSystem.Instance.TryFireGlassScatter(muzPos, transform.forward);
            }
            else if (BlasterCooldown <= 0f)
            {
                BlasterCooldown = 0.22f;
                Heat += 8f;
                if (Heat >= HeatMax) { Heat = HeatMax; Overheated = true; }
                float dmg = 22f + BlasterLevel * 8f;
                OutpostAndCombatManager.Instance?.FirePlayerBlaster(muzPos, transform.forward, dmg);
                SolarisGameManager.Instance?.PlaySfx("blaster_shot");
            }
        }

        // C. [SPACE] Eco-Dash (Hades Evasion with i-frames)
        if (Input.GetKeyDown(KeyCode.Space))
        {
            TryTriggerEcoDash();
        }

        // D. [Q] Pneumatic Scrap Piston / Harpoon Slug (Special Attack)
        if (Input.GetKeyDown(KeyCode.Q))
        {
            Vector3 muzPos = MuzzleTransform != null ? MuzzleTransform.position : (transform.position + transform.forward * 1.2f + Vector3.up * 1.2f);
            if (RecycledWeaponSystem.Instance != null)
            {
                RecycledWeaponSystem.Instance.TryFireScrapPiston(muzPos, transform.forward);
            }
            else
            {
                TryFireSolarLance();
            }
        }

        // E. [F] Thermal Slag Vent Blast (Purges 65% Overheat)
        if (Input.GetKeyDown(KeyCode.F))
        {
            if (RecycledWeaponSystem.Instance != null)
            {
                RecycledWeaponSystem.Instance.TryTriggerVentBlast();
            }
        }

        // F. [R] Smelt Dust
        if (Input.GetKeyDown(KeyCode.R))
        {
            SmeltDust();
        }

        // G. [T] Plant Oasis Tree
        if (Input.GetKeyDown(KeyCode.T))
        {
            TryPlantOasisTree();
        }
    }

    /// <summary>
    /// Executes a snappy, i-frame Hades-style evasion dash along movement input or forward direction.
    /// </summary>
    public bool TryTriggerEcoDash()
    {
        if (DashCooldown > 0f || IsDashing) return false;

        float h = Input.GetAxisRaw("Horizontal");
        float v = Input.GetAxisRaw("Vertical");
        Vector3 inputDir = new Vector3(h, 0f, v);
        if (inputDir.sqrMagnitude > 0.05f) _dashDirection = inputDir.normalized;
        else _dashDirection = transform.forward;
        _dashDirection.y = 0f;

        IsDashing = true;
        _dashTimer = 0.22f;
        DashCooldown = DASH_COOLDOWN_TIME;

        EmitDashDust(transform.position, -_dashDirection);
        CombatJuice.Instance?.AddCameraTrauma(0.14f);
        SolarisGameManager.Instance?.PlaySfx("sandstorm_wind");
        return true;
    }

    private void SetVacuumActive(bool active)
    {
        IsVacuuming = active;
        if (_vacuumLine != null && !active)
        {
            _vacuumLine.enabled = false;
        }
        if (_vacuumReticle != null && !active)
        {
            _vacuumReticle.enabled = false;
        }
    }

    /// <summary>
    /// Triggers an explosive seismic shockwave that clears sand and knocks enemies back.
    /// </summary>
    public bool TryTriggerShockwave()
    {
        if (ShockCooldown > 0f) return false;
        ShockCooldown = 6.0f;

        float radius = 14f + NozzleLevel * 3f;
        _world?.CleanCircle(transform.position, radius, 0.35f);
        OutpostAndCombatManager.Instance?.OnPlayerShockwave(transform.position, radius, 35f + BlasterLevel * 12f);

        SpawnShockwaveVisual(transform.position, radius);
        SolarisGameManager.Instance?.PlaySfx("solar_lance");
        return true;
    }

    private void SpawnShockwaveVisual(Vector3 center, float maxRadius)
    {
        GameObject ringGo = new GameObject("ShockwaveRing");
        ringGo.transform.position = center + Vector3.up * 0.25f;

        LineRenderer lr = ringGo.AddComponent<LineRenderer>();
        lr.useWorldSpace = false;
        lr.loop = true;
        int segments = 48;
        lr.positionCount = segments;
        for (int i = 0; i < segments; i++)
        {
            float rad = (i / (float)segments) * Mathf.PI * 2f;
            lr.SetPosition(i, new Vector3(Mathf.Cos(rad), 0f, Mathf.Sin(rad)));
        }
        lr.startWidth = 0.45f;
        lr.endWidth = 0.45f;
        lr.material = SolarisConfig.CreateMaterial(new Color(0f, 1f, 1f, 0.9f), 0f, 1f, null, new Color(0f, 1f, 1f) * 2f);

        StartCoroutine(AnimateShockwaveRing(ringGo, maxRadius));
    }

    private IEnumerator AnimateShockwaveRing(GameObject ringGo, float maxRadius)
    {
        float duration = 0.42f;
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / duration;
            float currentR = Mathf.Lerp(1.2f, maxRadius, t);
            if (ringGo != null)
            {
                ringGo.transform.localScale = new Vector3(currentR, 1f, currentR);
                LineRenderer lr = ringGo.GetComponent<LineRenderer>();
                if (lr != null)
                {
                    lr.startWidth = Mathf.Lerp(0.55f, 0.05f, t);
                    lr.endWidth = lr.startWidth;
                }
            }
            yield return null;
        }
        if (ringGo != null) SolarisConfig.SafeDestroy(ringGo);
    }

    /// <summary>
    /// Fires piercing Solar Plasma Lance beam up to 38m.
    /// </summary>
    public bool TryFireSolarLance()
    {
        if (LanceCooldown > 0f) return false;
        LanceCooldown = 4.0f;

        int restored = _world != null ? _world.SolarPanelsRestored : 0;
        float dmg = 55f + LanceLevel * 25f + (restored * 4f);

        Vector3 muzPos = MuzzleTransform != null ? MuzzleTransform.position : (transform.position + transform.forward * 1.2f + Vector3.up * 1.2f);
        OutpostAndCombatManager.Instance?.FireSolarLance(muzPos, transform.forward, 38f, dmg);
        SolarisGameManager.Instance?.PlaySfx("solar_lance");
        return true;
    }

    /// <summary>
    /// Converts stored dust in dust tank into pure Silica crystals.
    /// </summary>
    public int SmeltDust(bool forceHubBonus = false)
    {
        if (DustTank < 20f)
        {
            SolarisUIManager.Instance?.ShowToast("Toz Tankı yetersiz! (En az 20 toz gerekli)");
            return 0;
        }

        bool nearHub = forceHubBonus || (_world != null && _world.IsNearWorkshop(transform.position)) || transform.position.magnitude <= 16f;
        float rate = nearHub ? 0.06f : 0.04f;
        int gained = Mathf.Max(1, Mathf.RoundToInt(DustTank * rate * (1f + TankLevel * 0.25f)));

        DustTank = 0f;
        Silica += gained;

        SolarisUIManager.Instance?.ShowToast($"Silika Dönüştürüldü: +{gained} 💎! (Verim: %{rate * 100:0})");
        SolarisGameManager.Instance?.PlaySfx("dust_collect");
        return gained;
    }

    /// <summary>
    /// Plants an Oasis Tree that generates a life-saving bio-field.
    /// </summary>
    public bool TryPlantOasisTree()
    {
        int cost = SolarisConfig.TREE_COST;
        if (Silica < cost)
        {
            SolarisUIManager.Instance?.ShowToast($"Vaha Ağacı için {cost} 💎 Silika gerekli!");
            return false;
        }

        if (_world != null && _world.PlantTree(transform.position))
        {
            Silica -= cost;
            SolarisUIManager.Instance?.ShowToast($"Vaha Ağacı Dikildi! (-{cost} 💎)");
            SolarisGameManager.Instance?.PlaySfx("structure_restore");
            return true;
        }
        return false;
    }

    /// <summary>
    /// Reduces HP and triggers respawn if depleted.
    /// </summary>
    public void TakeDamage(float dmg)
    {
        if (IsDashing) return; // Invulnerability i-frame during Eco-Dash!

        HP -= dmg;
        CombatJuice.Instance?.AddCameraTrauma(0.32f);
        SolarisGameManager.Instance?.PlaySfx("enemy_hit");

        if (HP <= 0f)
        {
            HP = MaxHP;
            DustTank = 0f;
            Heat = 0f;
            Overheated = false;
            float respawnY = _world != null ? _world.GetHeight(0f, 28f) : 0f;
            transform.position = new Vector3(0f, respawnY + 0.04f, 28f);
        }
    }

    /// <summary>
    /// Applies upgrade level and recomputes related player attributes.
    /// </summary>
    public void ApplyUpgrade(string key)
    {
        switch (key.ToLower())
        {
            case "nozzle":
                NozzleLevel++;
                vacuumRange = 10.5f + NozzleLevel * 2.2f;
                break;
            case "tank":
                TankLevel++;
                DustTankMax = 100f + TankLevel * 50f;
                break;
            case "blaster":
                BlasterLevel++;
                break;
            case "lance":
                LanceLevel++;
                break;
            case "drone":
                DroneLevel++;
                SweeperDroneManager.Instance?.BuyDrone();
                break;
        }
        SolarisGameManager.Instance?.PlaySfx("upgrade_unlock");
    }
}
