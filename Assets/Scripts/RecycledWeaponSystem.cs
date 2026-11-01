// ============================================================
// SOLARIS RECLAIM — RecycledWeaponSystem.cs
// 4-Weapon Recycled Arsenal, 3-Slot Inventory System,
// Physics Gun Recoil, Player Pushback & Ground Ripple Waves
// Global Namespace — Unity 6 (6000.0.50f1) / C#
// ============================================================

using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class RecycledWeaponSystem : MonoBehaviour
{
    public static RecycledWeaponSystem Instance { get; private set; }

    [System.Serializable]
    public class WeaponProfile
    {
        public int id;
        public string name;
        public string shortName;
        public string category;
        public string description;
        public float fireCooldown;
        public float baseDamage;
        public float heatPerShot;
        public float recoilKick;
        public float recoilPitch;
        public float pushbackImpulse;
        public float cameraTrauma;
        public float fovPunch;
        public int costSilica;
        public bool isUnlocked;
        public Color themeColor;
    }

    [Header("Arsenal Profiles")]
    public WeaponProfile[] Weapons;

    [Header("3-Slot Inventory")]
    public int[] EquippedSlots = new int[3] { 0, -1, -1 }; // Slot 0 has default shotgun
    public int ActiveSlotIndex = 0;

    [Header("Weapon Cooldowns")]
    public float ActiveWeaponCooldown = 0f;
    public float VentCooldown = 0f;

    // References
    private EcoReclaimerPlayer _player;
    private Material _matGlassShard;
    private Material _matScrapSlug;
    private Material _matSlagFire;
    private Material _matMortarBomb;
    private ParticleSystem _steamVentPS;

    public WeaponProfile CurrentWeapon
    {
        get
        {
            if (EquippedSlots == null || ActiveSlotIndex < 0 || ActiveSlotIndex >= EquippedSlots.Length)
                return Weapons != null && Weapons.Length > 0 ? Weapons[0] : null;

            int wId = EquippedSlots[ActiveSlotIndex];
            if (wId >= 0 && Weapons != null && wId < Weapons.Length)
                return Weapons[wId];

            return Weapons != null && Weapons.Length > 0 ? Weapons[0] : null;
        }
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            SolarisConfig.SafeDestroy(gameObject);
            return;
        }
        Instance = this;
        InitWeaponProfiles();
    }

    private void Start()
    {
        _player = GetComponent<EcoReclaimerPlayer>() ?? FindFirstObjectByType<EcoReclaimerPlayer>();
        InitMaterials();
        BuildSteamVentParticleSystem();
    }

    private void InitWeaponProfiles()
    {
        Weapons = new WeaponProfile[4];

        // 0: Kristal Kıymık Pompalısı (Glass Shotgun)
        Weapons[0] = new WeaponProfile
        {
            id = 0,
            name = "Kristal Kıymık Pompalısı",
            shortName = "Kıymık Pompalı",
            category = "DAĞILIMLI SAÇILMA",
            description = "Geri dönüştürülmüş keskin silika cam parçacıkları fırlatır. 8 parçalık konik saçılım yakın mesafede ölümcüldür.",
            fireCooldown = 0.34f,
            baseDamage = 16f,
            heatPerShot = 10f,
            recoilKick = 0.28f,
            recoilPitch = 8.5f,
            pushbackImpulse = 4.2f,
            cameraTrauma = 0.20f,
            fovPunch = 2.5f,
            costSilica = 0,
            isUnlocked = true,
            themeColor = new Color(0.2f, 0.95f, 1f)
        };

        // 1: Pnömatik Hurda Zıpkını (Scrap Harpoon)
        Weapons[1] = new WeaponProfile
        {
            id = 1,
            name = "Pnömatik Hurda Zıpkını",
            shortName = "Hurda Zıpkın",
            category = "AĞIR DARBE & SERSEMLETME",
            description = "Ağır sıkıştırılmış metal piston mızrağı. Düşmanları şiddetle geri iter ve duvara çarparlarsa sersemletip ezer.",
            fireCooldown = 0.68f,
            baseDamage = 58f,
            heatPerShot = 18f,
            recoilKick = 0.42f,
            recoilPitch = 12f,
            pushbackImpulse = 6.8f,
            cameraTrauma = 0.34f,
            fovPunch = 4.0f,
            costSilica = 25,
            isUnlocked = false,
            themeColor = new Color(1f, 0.6f, 0.15f)
        };

        // 2: Termal Cüruf & Lav Püskürtücü (Slag Torch)
        Weapons[2] = new WeaponProfile
        {
            id = 2,
            name = "Termal Cüruf Püskürtücü",
            shortName = "Cüruf Lavı",
            category = "ALAN ENGELLEME & YAKICI",
            description = "Akışkan akkor cüruf lavı fırlatır. Yerde 2.5 saniye süren alevli magma göletleri bırakarak geçenleri yakar.",
            fireCooldown = 0.09f,
            baseDamage = 9f,
            heatPerShot = 2.6f,
            recoilKick = 0.08f,
            recoilPitch = 2.5f,
            pushbackImpulse = 1.4f,
            cameraTrauma = 0.06f,
            fovPunch = 0.8f,
            costSilica = 35,
            isUnlocked = false,
            themeColor = new Color(1f, 0.35f, 0.05f)
        };

        // 3: Sismik Moloz Havanı (Seismic Debris Mortar)
        Weapons[3] = new WeaponProfile
        {
            id = 3,
            name = "Sismik Moloz Havanı",
            shortName = "Sismik Havan",
            category = "PARABOLİK TOPÇU & ŞOK DALGASI",
            description = "Yere çarptığında devasa sismik yer dalgası ve şok dalgası yayan ağır moloz patlayıcısı fırlatır.",
            fireCooldown = 1.15f,
            baseDamage = 85f,
            heatPerShot = 26f,
            recoilKick = 0.52f,
            recoilPitch = 16f,
            pushbackImpulse = 8.5f,
            cameraTrauma = 0.46f,
            fovPunch = 5.5f,
            costSilica = 45,
            isUnlocked = false,
            themeColor = new Color(0.9f, 0.8f, 0.2f)
        };
    }

    private void InitMaterials()
    {
        // Sharp translucent cyan glass material
        _matGlassShard = SolarisConfig.CreateMaterial(
            new Color(0.2f, 0.95f, 1f, 0.85f),
            0.1f, 0.95f, null,
            new Color(0.1f, 0.85f, 1f) * 2.4f
        );

        // Heavy dark rusted scrap metal material
        _matScrapSlug = SolarisConfig.CreateMaterial(
            new Color(0.55f, 0.35f, 0.2f),
            0.6f, 0.3f, null,
            new Color(1f, 0.5f, 0.1f) * 0.9f
        );

        // Molten incandescent slag lava material
        _matSlagFire = SolarisConfig.CreateMaterial(
            new Color(1f, 0.45f, 0.05f),
            0.2f, 0.8f, null,
            new Color(1f, 0.35f, 0.05f) * 2.8f
        );

        // Heavy crude seismic bomb material
        _matMortarBomb = SolarisConfig.CreateMaterial(
            new Color(0.35f, 0.32f, 0.28f),
            0.7f, 0.2f, null,
            new Color(1f, 0.8f, 0.1f) * 1.5f
        );
    }

    private void BuildSteamVentParticleSystem()
    {
        GameObject steamGo = new GameObject("FX_SteamVent", typeof(ParticleSystem));
        steamGo.transform.SetParent(transform, false);
        _steamVentPS = steamGo.GetComponent<ParticleSystem>();

        var main = _steamVentPS.main;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 256;
        main.startLifetime = 0.55f;
        main.startSpeed = 16f;
        main.startSize = 0.85f;
        main.playOnAwake = false;

        var em = _steamVentPS.emission;
        em.enabled = false;

        var rend = steamGo.GetComponent<ParticleSystemRenderer>();
        if (rend != null)
        {
            Shader sh = Shader.Find("Particles/Standard Unlit") ?? Shader.Find("Sprites/Default");
            if (sh != null) rend.sharedMaterial = new Material(sh);
        }
    }

    private void Update()
    {
        float dt = Time.deltaTime;
        if (ActiveWeaponCooldown > 0f) ActiveWeaponCooldown -= dt;
        if (VentCooldown > 0f) VentCooldown -= dt;
    }

    // ============================================================
    // INVENTORY MANAGEMENT (3 SLOTS)
    // ============================================================
    public bool SwitchSlot(int slotIndex)
    {
        if (slotIndex < 0 || slotIndex >= EquippedSlots.Length) return false;
        if (EquippedSlots[slotIndex] < 0) return false; // Slot empty

        if (ActiveSlotIndex != slotIndex)
        {
            ActiveSlotIndex = slotIndex;
            SolarisGameManager.Instance?.PlaySfx("blaster_shot");
            CombatJuice.Instance?.TriggerFovKick(0.8f);
            return true;
        }
        return false;
    }

    public void CycleSlot(int direction)
    {
        if (EquippedSlots == null || EquippedSlots.Length == 0) return;
        int count = EquippedSlots.Length;
        for (int i = 1; i <= count; i++)
        {
            int next = (ActiveSlotIndex + direction * i + count * 10) % count;
            if (EquippedSlots[next] >= 0)
            {
                SwitchSlot(next);
                break;
            }
        }
    }

    public bool EquipWeapon(int weaponId, int targetSlot)
    {
        if (targetSlot < 0 || targetSlot >= EquippedSlots.Length) return false;
        if (weaponId < 0 || weaponId >= Weapons.Length) return false;
        if (!Weapons[weaponId].isUnlocked) return false;

        // If weapon is already in another slot, unequip that slot
        for (int i = 0; i < EquippedSlots.Length; i++)
        {
            if (EquippedSlots[i] == weaponId)
            {
                EquippedSlots[i] = -1;
            }
        }

        EquippedSlots[targetSlot] = weaponId;
        ActiveSlotIndex = targetSlot;
        SolarisGameManager.Instance?.PlaySfx("solar_lance");
        return true;
    }

    public bool BuyAndEquipWeapon(int weaponId, int targetSlot)
    {
        if (weaponId < 0 || weaponId >= Weapons.Length) return false;
        var wp = Weapons[weaponId];

        if (!wp.isUnlocked)
        {
            if (_player == null || _player.Silica < wp.costSilica) return false;
            _player.Silica -= wp.costSilica;
            wp.isUnlocked = true;
        }

        return EquipWeapon(weaponId, targetSlot);
    }

    public int GetEquippedSlotForWeapon(int weaponId)
    {
        for (int i = 0; i < EquippedSlots.Length; i++)
        {
            if (EquippedSlots[i] == weaponId) return i;
        }
        return -1;
    }

    // ============================================================
    // WEAPON FIRING DISPATCHER
    // ============================================================
    public bool TryFireActiveWeapon(Vector3 muzzlePos, Vector3 forwardDir)
    {
        if (ActiveWeaponCooldown > 0f || _player == null) return false;
        if (_player.Overheated) return false;

        var wp = CurrentWeapon;
        if (wp == null) return false;

        ActiveWeaponCooldown = wp.fireCooldown;
        _player.Heat += wp.heatPerShot;
        if (_player.Heat >= _player.HeatMax)
        {
            _player.Heat = _player.HeatMax;
            _player.Overheated = true;
        }

        // Apply visual recoil on rifle model and physical player pushback
        _player.TriggerGunRecoil(wp.recoilKick, wp.recoilPitch);
        _player.AddRecoilImpulse(-forwardDir * wp.pushbackImpulse);

        // Camera shake and FOV kick
        CombatJuice.Instance?.AddCameraTrauma(wp.cameraTrauma);
        CombatJuice.Instance?.TriggerFovKick(wp.fovPunch);
        Vector2 screenKick = new Vector2(-forwardDir.x, -forwardDir.z) * (wp.recoilKick * 0.45f);
        CombatJuice.Instance?.AddDirectionalCameraKick(screenKick, wp.recoilKick * 0.35f);

        // Muzzle & ejection juice
        Vector3 rightDir = Vector3.Cross(Vector3.up, forwardDir).normalized;
        CombatJuice.Instance?.EmitMuzzleSparks(muzzlePos, forwardDir, 8);
        CombatJuice.Instance?.EmitEjectionParticles(muzzlePos - forwardDir * 0.25f, rightDir, 3);

        switch (wp.id)
        {
            case 0:
                FireGlassShotgun(muzzlePos, forwardDir, wp);
                break;
            case 1:
                FireScrapHarpoon(muzzlePos, forwardDir, wp);
                break;
            case 2:
                FireSlagTorch(muzzlePos, forwardDir, wp);
                break;
            case 3:
                FireDebrisMortar(muzzlePos, forwardDir, wp);
                break;
            default:
                FireGlassShotgun(muzzlePos, forwardDir, wp);
                break;
        }

        return true;
    }

    // ------------------------------------------------------------
    // Weapon 0: Kristal Kıymık Pompalısı (Glass Shotgun)
    // ------------------------------------------------------------
    private void FireGlassShotgun(Vector3 muzzlePos, Vector3 forwardDir, WeaponProfile wp)
    {
        int shardCount = 8;
        float spreadAngle = 24f;
        float baseDmg = wp.baseDamage + _player.BlasterLevel * 5f;

        for (int i = 0; i < shardCount; i++)
        {
            float angleOffset = Mathf.Lerp(-spreadAngle * 0.5f, spreadAngle * 0.5f, i / (float)(shardCount - 1));
            angleOffset += UnityEngine.Random.Range(-3.5f, 3.5f);
            Quaternion rot = Quaternion.AngleAxis(angleOffset, Vector3.up);
            Vector3 shardDir = rot * forwardDir;

            GameObject shard = GameObject.CreatePrimitive(PrimitiveType.Cube);
            shard.name = "GlassShard";
            shard.transform.position = muzzlePos;
            shard.transform.rotation = Quaternion.LookRotation(shardDir);
            shard.transform.localScale = new Vector3(0.12f, 0.12f, 0.55f);
            shard.GetComponent<Renderer>().sharedMaterial = _matGlassShard;
            SolarisConfig.SafeDestroy(shard.GetComponent<Collider>());

            var p = new OutpostAndCombatManager.ProjectileData
            {
                rootObj = shard,
                position = muzzlePos,
                velocity = shardDir.normalized * 54f,
                damage = baseDmg,
                lifetime = 0f,
                maxLifetime = 0.44f,
                radius = 0.55f,
                isEnemy = false
            };
            OutpostAndCombatManager.Instance?.RegisterExternalProjectile(p);
        }

        SolarisGameManager.Instance?.PlaySfx("blaster_shot");
    }

    // ------------------------------------------------------------
    // Weapon 1: Pnömatik Hurda Zıpkını (Scrap Harpoon)
    // ------------------------------------------------------------
    private void FireScrapHarpoon(Vector3 muzzlePos, Vector3 forwardDir, WeaponProfile wp)
    {
        float dmg = wp.baseDamage + _player.BlasterLevel * 18f;

        GameObject slug = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        slug.name = "ScrapHarpoonSlug";
        slug.transform.position = muzzlePos;
        slug.transform.rotation = Quaternion.LookRotation(forwardDir) * Quaternion.Euler(90f, 0, 0);
        slug.transform.localScale = new Vector3(0.48f, 0.85f, 0.48f);
        slug.GetComponent<Renderer>().sharedMaterial = _matScrapSlug;
        SolarisConfig.SafeDestroy(slug.GetComponent<Collider>());

        var p = new OutpostAndCombatManager.ProjectileData
        {
            rootObj = slug,
            position = muzzlePos,
            velocity = forwardDir.normalized * 44f,
            damage = dmg,
            lifetime = 0f,
            maxLifetime = 1.15f,
            radius = 0.95f,
            isEnemy = false
        };

        OutpostAndCombatManager.Instance?.RegisterExternalProjectile(p, isHeavyKnockback: true);
        SolarisGameManager.Instance?.PlaySfx("solar_lance");
    }

    // ------------------------------------------------------------
    // Weapon 2: Termal Cüruf & Lav Püskürtücü (Slag Torch)
    // ------------------------------------------------------------
    private void FireSlagTorch(Vector3 muzzlePos, Vector3 forwardDir, WeaponProfile wp)
    {
        float dmg = wp.baseDamage + _player.BlasterLevel * 3.5f;

        // Small angular jitter for flamethrower cone
        Quaternion jitterRot = Quaternion.Euler(UnityEngine.Random.Range(-3f, 3f), UnityEngine.Random.Range(-7f, 7f), 0);
        Vector3 sprayDir = jitterRot * forwardDir;

        GameObject slag = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        slag.name = "SlagDroplet";
        slag.transform.position = muzzlePos;
        slag.transform.localScale = new Vector3(0.35f, 0.35f, 0.35f);
        slag.GetComponent<Renderer>().sharedMaterial = _matSlagFire;
        SolarisConfig.SafeDestroy(slag.GetComponent<Collider>());

        var p = new OutpostAndCombatManager.ProjectileData
        {
            rootObj = slag,
            position = muzzlePos,
            velocity = sprayDir.normalized * 32f,
            damage = dmg,
            lifetime = 0f,
            maxLifetime = 0.52f,
            radius = 0.65f,
            isEnemy = false
        };

        OutpostAndCombatManager.Instance?.RegisterExternalProjectile(p);

        // Spawn a burning molten slag puddle on the ground ahead to burn passing enemies
        Vector3 puddlePos = muzzlePos + forwardDir * UnityEngine.Random.Range(4f, 10f);
        OutpostAndCombatManager.Instance?.SpawnSlagPuddle(puddlePos, 2.4f, dmg * 0.4f);

        SolarisGameManager.Instance?.PlaySfx("blaster_shot");
    }

    // ------------------------------------------------------------
    // Weapon 3: Sismik Moloz Havanı (Seismic Debris Mortar)
    // ------------------------------------------------------------
    private void FireDebrisMortar(Vector3 muzzlePos, Vector3 forwardDir, WeaponProfile wp)
    {
        float dmg = wp.baseDamage + _player.BlasterLevel * 24f;

        // Calculate landing target: mouse aim ground pos or forward 16m
        Vector3 targetGround = muzzlePos + forwardDir * 15f;
        Ray ray = Camera.main != null ? Camera.main.ScreenPointToRay(Input.mousePosition) : new Ray(muzzlePos + Vector3.up * 10f, Vector3.down);
        Plane groundPlane = new Plane(Vector3.up, new Vector3(0, muzzlePos.y, 0));
        if (groundPlane.Raycast(ray, out float enter))
        {
            Vector3 hitPoint = ray.GetPoint(enter);
            float dist = Vector3.Distance(hitPoint, muzzlePos);
            dist = Mathf.Clamp(dist, 5f, 26f);
            targetGround = muzzlePos + (hitPoint - muzzlePos).normalized * dist;
        }

        GameObject bomb = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        bomb.name = "SeismicMortarBomb";
        bomb.transform.position = muzzlePos;
        bomb.transform.localScale = new Vector3(0.65f, 0.65f, 0.65f);
        bomb.GetComponent<Renderer>().sharedMaterial = _matMortarBomb;
        SolarisConfig.SafeDestroy(bomb.GetComponent<Collider>());

        // Ground landing warning circle
        GameObject circle = CreateMortarReticle(targetGround, 5.0f);

        var p = new OutpostAndCombatManager.ProjectileData
        {
            rootObj = bomb,
            position = muzzlePos,
            velocity = Vector3.zero,
            damage = dmg,
            lifetime = 0f,
            maxLifetime = 0.85f,
            radius = 1.0f,
            isEnemy = false,
            isMortar = true,
            mortarStart = muzzlePos,
            mortarTarget = targetGround,
            dangerCircle = circle
        };

        OutpostAndCombatManager.Instance?.RegisterExternalProjectile(p);
        SolarisGameManager.Instance?.PlaySfx("sandstorm_wind");
    }

    private GameObject CreateMortarReticle(Vector3 center, float radius)
    {
        GameObject ret = new GameObject("PlayerMortarReticle");
        ret.transform.position = center + Vector3.up * 0.08f;
        LineRenderer lr = ret.AddComponent<LineRenderer>();
        lr.useWorldSpace = false;
        lr.loop = true;
        lr.startWidth = 0.16f;
        lr.endWidth = 0.16f;
        lr.material = _matSlagFire;
        lr.positionCount = 32;

        for (int i = 0; i < 32; i++)
        {
            float rad = (i / 32f) * Mathf.PI * 2f;
            lr.SetPosition(i, new Vector3(Mathf.Cos(rad) * radius, 0f, Mathf.Sin(rad) * radius));
        }
        return ret;
    }

    // ============================================================
    // ABILITY: THERMAL SLAG VENT BLAST (Purges Overheat via Left Shift)
    // ============================================================
    public bool TryTriggerVentBlast()
    {
        if (VentCooldown > 0f || _player == null) return false;
        if (_player.Heat < 20f && !_player.Overheated) return false;

        VentCooldown = 3.2f;

        // Purge heat
        _player.Heat = Mathf.Max(0f, _player.Heat - 65f);
        _player.Overheated = false;

        Vector3 origin = _player.transform.position + Vector3.up * 1.0f;
        Vector3 forward = _player.transform.forward;

        // Emit high-velocity scalding steam particles
        if (_steamVentPS != null)
        {
            for (int i = 0; i < 48; i++)
            {
                Vector3 spreadDir = Quaternion.Euler(UnityEngine.Random.Range(-15f, 15f), UnityEngine.Random.Range(-35f, 35f), 0) * forward;
                ParticleSystem.EmitParams ep = new ParticleSystem.EmitParams
                {
                    position = origin + forward * 0.5f,
                    velocity = spreadDir * UnityEngine.Random.Range(14f, 26f),
                    startSize = UnityEngine.Random.Range(0.6f, 1.4f),
                    startLifetime = UnityEngine.Random.Range(0.35f, 0.6f),
                    startColor = new Color(0.9f, 0.95f, 1f, 0.75f)
                };
                _steamVentPS.Emit(ep, 1);
            }
        }

        // Damage and knock back enemies in 9.5m cone
        OutpostAndCombatManager.Instance?.OnVentBlast(origin, forward, 9.5f, 45f + _player.BlasterLevel * 12f);

        // Ground shockwave ripple
        CombatJuice.Instance?.SpawnGroundRippleWave(origin, 6.0f, 18f);

        // Clean ground in front
        SandGridWorld.Instance?.CleanCircle(origin + forward * 4.5f, 5.5f, 0.45f);

        // Camera trauma & audio
        CombatJuice.Instance?.AddCameraTrauma(0.28f);
        SolarisGameManager.Instance?.PlaySfx("sandstorm_wind");

        return true;
    }

    // Backwards-compatible methods
    public bool TryFireGlassScatter(Vector3 muzzlePos, Vector3 forwardDir)
    {
        return TryFireActiveWeapon(muzzlePos, forwardDir);
    }

    public bool TryFireScrapPiston(Vector3 muzzlePos, Vector3 forwardDir)
    {
        return TryFireActiveWeapon(muzzlePos, forwardDir);
    }
}
