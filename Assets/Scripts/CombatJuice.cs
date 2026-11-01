// ============================================================
// SOLARIS RECLAIM — CombatJuice.cs
// High-Octane Game Feel & Juice Engine (GMTK & Vlambeer Principles)
// Hitstop (Micro-Freeze), Quadratic Trauma Camera Shake,
// Directional Camera Kick, Dynamic FOV Punch, Seismic Ground Waves,
// Emissive Damage Flash, Directional Flinch, Procedural Shard VFX
// Global Namespace — Unity 6 (6000.0.50f1) / C#
// ============================================================

using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class CombatJuice : MonoBehaviour
{
    public static CombatJuice Instance { get; private set; }

    [Header("Camera Trauma Settings")]
    [Range(0f, 1f)] public float Trauma = 0f;
    public float TraumaDecayRate = 1.6f;
    public float MaxShakeTranslation = 0.45f;
    public float MaxShakeRotation = 2.5f;

    [Header("Hitstop Settings")]
    public float LightHitstop = 0.035f;
    public float HeavyHitstop = 0.075f;

    // Directional Camera Punch & FOV Kick (GMTK / Vlambeer)
    private Vector3 _directionalCameraKick = Vector3.zero;
    private float _fovDelta = 0f;

    // Internal Shake State
    private float _perlinSeedX;
    private float _perlinSeedY;
    private float _perlinSeedRot;
    private Vector3 _currentShakeOffset = Vector3.zero;
    private Quaternion _currentShakeRotation = Quaternion.identity;
    private bool _isHitstopping = false;

    // Particle FX
    private ParticleSystem _glassShardsPS;
    private ParticleSystem _scrapSparksPS;
    private ParticleSystem _toxicSplatterPS;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            SolarisConfig.SafeDestroy(gameObject);
            return;
        }
        Instance = this;

        _perlinSeedX = Random.Range(0f, 1000f);
        _perlinSeedY = Random.Range(0f, 1000f);
        _perlinSeedRot = Random.Range(0f, 1000f);

        BuildProceduralParticleSystems();
    }

    private void Update()
    {
        float dt = Time.unscaledDeltaTime;

        // 1. Quadratic Trauma Camera Shake Decay
        if (Trauma > 0f)
        {
            Trauma = Mathf.Max(0f, Trauma - TraumaDecayRate * dt);
            float shakePower = Trauma * Trauma; // Quadratic feel: subtle at low, violent at high

            float time = Time.unscaledTime * 28f;
            float offsetX = (Mathf.PerlinNoise(_perlinSeedX, time) * 2f - 1f) * MaxShakeTranslation * shakePower;
            float offsetY = (Mathf.PerlinNoise(_perlinSeedY, time) * 2f - 1f) * MaxShakeTranslation * shakePower;
            float rotZ = (Mathf.PerlinNoise(_perlinSeedRot, time) * 2f - 1f) * MaxShakeRotation * shakePower;

            _currentShakeOffset = new Vector3(offsetX, offsetY * 0.7f, -offsetX * 0.5f);
            _currentShakeRotation = Quaternion.Euler(offsetY * 3f, offsetX * 3f, rotZ);
        }
        else
        {
            _currentShakeOffset = Vector3.zero;
            _currentShakeRotation = Quaternion.identity;
        }

        // 2. Directional Camera Recoil Punch Decay
        if (_directionalCameraKick.sqrMagnitude > 0.0001f)
        {
            _directionalCameraKick = Vector3.Lerp(_directionalCameraKick, Vector3.zero, dt * 14f);
        }

        // 3. Dynamic FOV Punch Decay
        if (Mathf.Abs(_fovDelta) > 0.01f)
        {
            _fovDelta = Mathf.Lerp(_fovDelta, 0f, dt * 16f);
        }
        else
        {
            _fovDelta = 0f;
        }
    }

    public Vector3 GetShakeOffset() => _currentShakeOffset + _directionalCameraKick;
    public Vector3 GetRecoilKickOffset() => _directionalCameraKick;
    public Quaternion GetShakeRotation() => _currentShakeRotation;
    public float GetFovDelta() => _fovDelta;

    public void EmitMuzzleSparks(Vector3 pos, Vector3 forward, int count) => EmitScrapSparks(pos, forward, count);

    /// <summary>
    /// Adds trauma (0..1) to the camera shake system.
    /// </summary>
    public void AddCameraTrauma(float amount)
    {
        Trauma = Mathf.Clamp01(Trauma + amount);
    }

    /// <summary>
    /// Directional Camera Punch (Vlambeer): pushes the camera opposite to the fire vector for instant physical recoil.
    /// </summary>
    public void AddDirectionalCameraKick(Vector3 kickVector, float force)
    {
        kickVector.y = 0f;
        if (kickVector.sqrMagnitude > 0.01f) kickVector.Normalize();
        _directionalCameraKick += kickVector * force;
        _directionalCameraKick = Vector3.ClampMagnitude(_directionalCameraKick, 1.25f);
    }

    /// <summary>
    /// Micro-zoom FOV kick (GMTK): punches the FOV inwards or outwards for kinetic 3D depth.
    /// </summary>
    public void TriggerFovKick(float delta)
    {
        _fovDelta += delta;
        _fovDelta = Mathf.Clamp(_fovDelta, -6f, 6f);
    }

    /// <summary>
    /// Spawns an expanding seismic ground ripple wave that tears through dust and knocks back entities.
    /// </summary>
    public void SpawnGroundRippleWave(Vector3 center, float maxRadius, float speed, Color color = default)
    {
        if (color == default || color.a <= 0.001f) color = new Color(0.2f, 0.95f, 1f, 0.85f);
        StartCoroutine(GroundRippleRoutine(center, maxRadius, speed, color));
    }

    private IEnumerator GroundRippleRoutine(Vector3 center, float maxRadius, float speed, Color color)
    {
        GameObject waveGo = new GameObject("SeismicGroundWave");
        waveGo.transform.position = center + Vector3.up * 0.12f;

        LineRenderer lr = waveGo.AddComponent<LineRenderer>();
        lr.useWorldSpace = false;
        lr.loop = true;
        int segments = 42;
        lr.positionCount = segments;
        for (int i = 0; i < segments; i++)
        {
            float rad = (i / (float)segments) * Mathf.PI * 2f;
            lr.SetPosition(i, new Vector3(Mathf.Cos(rad), 0f, Mathf.Sin(rad)));
        }
        lr.startWidth = 0.55f;
        lr.endWidth = 0.55f;
        lr.sharedMaterial = SolarisConfig.CreateMaterial(color, 0f, 0.9f, null, color * 2.8f);

        float duration = Mathf.Max(0.2f, maxRadius / speed);
        float elapsed = 0f;

        AddCameraTrauma(0.25f);

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / duration;
            float currentR = Mathf.Lerp(0.5f, maxRadius, t);

            if (waveGo != null)
            {
                waveGo.transform.localScale = new Vector3(currentR, 1f, currentR);
                lr.startWidth = Mathf.Lerp(0.65f, 0.02f, t);
                lr.endWidth = lr.startWidth;

                // Dust lift
                if (Random.value > 0.45f)
                {
                    EmitScrapSparks(center + Random.insideUnitSphere * (currentR * 0.8f), Vector3.up, 2);
                }
            }
            yield return null;
        }

        if (waveGo != null) SolarisConfig.SafeDestroy(waveGo);
    }

    /// <summary>
    /// Full-suite impact feedback: micro-freeze, camera shake, damage flash, directional squash & stretch, impact particles.
    /// </summary>
    public void ApplyHitFeedback(Transform target, Vector3 hitPoint, Vector3 impactDir, bool isHeavy, Color? flashColor = null)
    {
        // 1. Hitstop (Freeze Frame)
        TriggerHitstop(isHeavy ? HeavyHitstop : LightHitstop);

        // 2. Camera Trauma
        AddCameraTrauma(isHeavy ? 0.42f : 0.18f);

        // 3. Emissive Damage Flash
        if (target != null)
        {
            StartCoroutine(DamageFlashRoutine(target, flashColor ?? Color.white));
            StartCoroutine(DirectionalFlinchRoutine(target, impactDir, isHeavy ? 0.35f : 0.18f));
        }

        // 4. Procedural Impact Shards
        if (isHeavy)
        {
            EmitScrapSparks(hitPoint, impactDir, 16);
            EmitGlassShards(hitPoint, impactDir, 20);
        }
        else
        {
            EmitGlassShards(hitPoint, impactDir, 8);
            EmitScrapSparks(hitPoint, impactDir, 6);
        }
    }

    /// <summary>
    /// Freezes time for a few milliseconds, giving weighty resistance to combat hits.
    /// </summary>
    public void TriggerHitstop(float duration)
    {
        if (_isHitstopping || duration <= 0f) return;
        StartCoroutine(HitstopRoutine(duration));
    }

    private IEnumerator HitstopRoutine(float duration)
    {
        _isHitstopping = true;
        float originalScale = Time.timeScale;
        Time.timeScale = 0.02f; // Micro-freeze frame
        yield return new WaitForSecondsRealtime(duration);
        Time.timeScale = originalScale;
        _isHitstopping = false;
    }

    /// <summary>
    /// Flashes all renderers on the target with emission without triggering camera shake or hitstop.
    /// Perfect for DoT ticks (e.g. slag puddle burn).
    /// </summary>
    public void TriggerDamageFlash(Transform target, Color? flashColor = null)
    {
        if (target == null) return;
        StartCoroutine(DamageFlashRoutine(target, flashColor ?? Color.white));
    }

    /// <summary>
    /// Flashes all renderers on the target with pure white/bright emission for 0.06s.
    /// </summary>
    private IEnumerator DamageFlashRoutine(Transform target, Color flashColor)
    {
        if (target == null) yield break;

        Renderer[] renderers = target.GetComponentsInChildren<Renderer>();
        if (renderers == null || renderers.Length == 0) yield break;

        // Store original materials or colors
        List<Material> mats = new List<Material>();
        List<Color> originalColors = new List<Color>();
        List<Color> originalEmissions = new List<Color>();

        for (int i = 0; i < renderers.Length; i++)
        {
            if (renderers[i] == null) continue;
            Material m = renderers[i].material;
            if (m == null) continue;

            mats.Add(m);
            originalColors.Add(m.HasProperty("_Color") ? m.color : Color.white);
            originalEmissions.Add(m.HasProperty("_EmissionColor") ? m.GetColor("_EmissionColor") : Color.black);

            m.EnableKeyword("_EMISSION");
            if (m.HasProperty("_Color")) m.color = Color.white;
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", Color.white);
            if (m.HasProperty("_EmissionColor")) m.SetColor("_EmissionColor", flashColor * 3.5f);
        }

        yield return new WaitForSecondsRealtime(0.065f);

        // Restore original colors
        for (int i = 0; i < mats.Count; i++)
        {
            if (mats[i] != null)
            {
                if (mats[i].HasProperty("_Color")) mats[i].color = originalColors[i];
                if (mats[i].HasProperty("_BaseColor")) mats[i].SetColor("_BaseColor", originalColors[i]);
                if (mats[i].HasProperty("_EmissionColor")) mats[i].SetColor("_EmissionColor", originalEmissions[i]);
            }
        }
    }

    /// <summary>
    /// Directional squash and stretch flinch: momentarily squashes target along impact vector, then springs back.
    /// </summary>
    private IEnumerator DirectionalFlinchRoutine(Transform target, Vector3 impactDir, float intensity)
    {
        if (target == null) yield break;
        Vector3 baseScale = target.localScale;
        impactDir.y = 0f;
        if (impactDir.sqrMagnitude < 0.01f) impactDir = target.forward;
        impactDir.Normalize();

        Vector3 squashed = new Vector3(
            baseScale.x * (1f + intensity * 0.4f),
            baseScale.y * (1f - intensity * 0.6f),
            baseScale.z * (1f + intensity * 0.4f)
        );

        float elapsed = 0f;
        float duration = 0.16f;

        while (elapsed < duration)
        {
            if (target == null) yield break;
            elapsed += Time.unscaledDeltaTime;
            float t = elapsed / duration;

            float spring = Mathf.Sin(t * Mathf.PI) * (1f - t);
            target.localScale = Vector3.Lerp(baseScale, squashed, spring);
            yield return null;
        }

        if (target != null) target.localScale = baseScale;
    }

    // ==========================================
    // PROCEDURAL PARTICLE SYSTEMS
    // ==========================================
    private void BuildProceduralParticleSystems()
    {
        Shader partShader = Shader.Find("Particles/Standard Unlit") ?? Shader.Find("Sprites/Default");
        Material partMat = new Material(partShader);

        // 1. Glass Shards
        GameObject glassGo = new GameObject("FX_GlassShards", typeof(ParticleSystem));
        glassGo.transform.SetParent(transform, false);
        _glassShardsPS = glassGo.GetComponent<ParticleSystem>();
        var m1 = _glassShardsPS.main;
        m1.simulationSpace = ParticleSystemSimulationSpace.World;
        m1.maxParticles = 512;
        m1.startLifetime = 0.45f;
        m1.startSpeed = 16f;
        m1.startSize = 0.22f;
        m1.playOnAwake = false;
        glassGo.GetComponent<ParticleSystemRenderer>().sharedMaterial = partMat;
        var em1 = _glassShardsPS.emission; em1.enabled = false;

        // 2. Scrap Sparks
        GameObject sparkGo = new GameObject("FX_ScrapSparks", typeof(ParticleSystem));
        sparkGo.transform.SetParent(transform, false);
        _scrapSparksPS = sparkGo.GetComponent<ParticleSystem>();
        var m2 = _scrapSparksPS.main;
        m2.simulationSpace = ParticleSystemSimulationSpace.World;
        m2.maxParticles = 512;
        m2.startLifetime = 0.35f;
        m2.startSpeed = 22f;
        m2.startSize = 0.14f;
        m2.playOnAwake = false;
        sparkGo.GetComponent<ParticleSystemRenderer>().sharedMaterial = partMat;
        var em2 = _scrapSparksPS.emission; em2.enabled = false;

        // 3. Toxic Splatter
        GameObject toxicGo = new GameObject("FX_ToxicSplatter", typeof(ParticleSystem));
        toxicGo.transform.SetParent(transform, false);
        _toxicSplatterPS = toxicGo.GetComponent<ParticleSystem>();
        var m3 = _toxicSplatterPS.main;
        m3.simulationSpace = ParticleSystemSimulationSpace.World;
        m3.maxParticles = 512;
        m3.startLifetime = 0.65f;
        m3.startSpeed = 8f;
        m3.startSize = 0.35f;
        m3.playOnAwake = false;
        toxicGo.GetComponent<ParticleSystemRenderer>().sharedMaterial = partMat;
        var em3 = _toxicSplatterPS.emission; em3.enabled = false;
    }

    public void EmitEjectionParticles(Vector3 pos, Vector3 rightDir, bool isGlass)
    {
        if (isGlass)
        {
            EmitGlassShards(pos, rightDir + Vector3.up * 0.35f, 4);
        }
        else
        {
            EmitScrapSparks(pos, rightDir + Vector3.up * 0.35f, 5);
        }
    }

    public void EmitGlassShards(Vector3 pos, Vector3 reflectDir, int count)
    {
        if (_glassShardsPS == null) return;
        for (int i = 0; i < count; i++)
        {
            Vector3 vel = (reflectDir + Random.insideUnitSphere * 0.75f).normalized * Random.Range(10f, 26f);
            Color shardCol = Random.value > 0.3f ? new Color(0.4f, 0.95f, 1f, 0.95f) : new Color(0.9f, 0.98f, 1f, 0.95f);
            ParticleSystem.EmitParams ep = new ParticleSystem.EmitParams
            {
                position = pos + Random.insideUnitSphere * 0.15f,
                velocity = vel,
                startSize = Random.Range(0.12f, 0.28f),
                startLifetime = Random.Range(0.35f, 0.55f),
                startColor = shardCol
            };
            _glassShardsPS.Emit(ep, 1);
        }
    }

    public void EmitScrapSparks(Vector3 pos, Vector3 reflectDir, int count)
    {
        if (_scrapSparksPS == null) return;
        for (int i = 0; i < count; i++)
        {
            Vector3 vel = (reflectDir + Random.insideUnitSphere * 0.85f).normalized * Random.Range(14f, 32f);
            Color sparkCol = Random.value > 0.4f ? new Color(1f, 0.7f, 0.1f, 1f) : new Color(1f, 0.35f, 0.05f, 1f);
            ParticleSystem.EmitParams ep = new ParticleSystem.EmitParams
            {
                position = pos,
                velocity = vel,
                startSize = Random.Range(0.08f, 0.18f),
                startLifetime = Random.Range(0.25f, 0.4f),
                startColor = sparkCol
            };
            _scrapSparksPS.Emit(ep, 1);
        }
    }

    public void EmitToxicSplatter(Vector3 pos, int count)
    {
        if (_toxicSplatterPS == null) return;
        for (int i = 0; i < count; i++)
        {
            Vector3 vel = (Vector3.up * 1.5f + Random.insideUnitSphere * 1.2f).normalized * Random.Range(4f, 12f);
            Color toxCol = Random.value > 0.3f ? new Color(0.2f, 0.9f, 0.3f, 0.85f) : new Color(0.45f, 0.85f, 0.1f, 0.85f);
            ParticleSystem.EmitParams ep = new ParticleSystem.EmitParams
            {
                position = pos,
                velocity = vel,
                startSize = Random.Range(0.25f, 0.55f),
                startLifetime = Random.Range(0.5f, 0.85f),
                startColor = toxCol
            };
            _toxicSplatterPS.Emit(ep, 1);
        }
    }
}
