using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

public class SolarisGameManager : MonoBehaviour
{
    public static SolarisGameManager Instance { get; private set; }

    // Core System References
    public SandGridWorld World;
    public EcoReclaimerPlayer Player;
    public SweeperDroneManager Drones;
    public OutpostAndCombatManager Combat;
    public SolarisUIManager UI;

    // Game States
    public bool GameStarted = false;
    public bool IsPaused = false;
    public bool IsMuted = false;
    public bool StormActive = false;
    public bool VictoryAchieved = false;
    public bool FreePlayMode = false;

    // Timers & Durations
    public float StormTimer = 120f;
    public float StormDurationRemaining = 0f;
    private const float STORM_CYCLE_INTERVAL = 120f;
    private const float STORM_CYCLE_DURATION = 45f;

    // Environmental Components
    public ParticleSystem StormParticles;
    public ParticleSystem RainParticles;
    public Light SunLight;
    public Camera MainCam;
    private AudioSource _sfxAudioSource;

    // Procedural Audio Cache
    private readonly Dictionary<string, AudioClip> _sfxClipCache = new Dictionary<string, AudioClip>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AutoBootstrapOnPlay()
    {
        if (Instance == null && Object.FindFirstObjectByType<SolarisGameManager>() == null)
        {
            GameObject rootGo = new GameObject("[AutonomousStudioRoot]");
            rootGo.AddComponent<SolarisGameManager>();
        }
    }

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else if (Instance != this) { SolarisConfig.SafeDestroy(gameObject); return; }

        SolarisUIManager.EnsureEventSystem();

        if (World == null)
        {
            BuildScene();
        }
        else
        {
            if (Player == null) Player = Object.FindFirstObjectByType<EcoReclaimerPlayer>();
            if (Drones == null) Drones = GetComponentInChildren<SweeperDroneManager>() ?? Object.FindFirstObjectByType<SweeperDroneManager>();
            if (Combat == null) Combat = GetComponentInChildren<OutpostAndCombatManager>() ?? Object.FindFirstObjectByType<OutpostAndCombatManager>();
            if (UI == null) UI = GetComponentInChildren<SolarisUIManager>() ?? Object.FindFirstObjectByType<SolarisUIManager>();

            if (UI != null)
            {
                UI.InitializeUI(World, Player, Drones, Combat, this);
            }
        }
    }

    private void Start()
    {
        if (!GameStarted && UI != null)
        {
            UI.SetMainMenuVisible(true);
        }
    }

    // ==========================================
    // IDEMPOTENT SCENE BUILDER & INITIALIZER
    // ==========================================
    public void BuildScene()
    {
        // 1. Environment & Fog Configuration
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.ExponentialSquared;
        RenderSettings.fogColor = new Color(0.16f, 0.08f, 0.03f); // Atmospheric desert haze
        RenderSettings.fogDensity = 0.010f;
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.38f, 0.32f, 0.26f);

        // 2. Directional Sun Light
        SunLight = Object.FindFirstObjectByType<Light>();
        if (SunLight == null || SunLight.type != LightType.Directional)
        {
            GameObject lightGo = new GameObject("Directional Sun Light");
            SunLight = lightGo.AddComponent<Light>();
            SunLight.type = LightType.Directional;
            lightGo.transform.rotation = Quaternion.Euler(50f, -32f, 0f);
        }
        SunLight.intensity = 1.35f;
        SunLight.color = new Color(1.0f, 0.94f, 0.85f);
        SunLight.shadows = LightShadows.Soft;

        // 3. Main Camera Configuration
        MainCam = Camera.main;
        if (MainCam == null)
        {
            MainCam = Object.FindFirstObjectByType<Camera>();
        }
        if (MainCam == null)
        {
            GameObject camGo = new GameObject("Main Camera");
            camGo.tag = "MainCamera";
            MainCam = camGo.AddComponent<Camera>();
            camGo.AddComponent<AudioListener>();
        }
        MainCam.farClipPlane = 600f;
        MainCam.fieldOfView = 58f;
        MainCam.clearFlags = CameraClearFlags.Skybox;
        MainCam.backgroundColor = new Color(0.16f, 0.08f, 0.03f);

        // 4. Setup SFX AudioSource
        _sfxAudioSource = GetComponent<AudioSource>();
        if (_sfxAudioSource == null) _sfxAudioSource = gameObject.AddComponent<AudioSource>();
        _sfxAudioSource.playOnAwake = false;
        _sfxAudioSource.spatialBlend = 0f; // 2D crisp UI / Master sound

        // 5. Create / Attach Core Systems in Order
        GameObject root = gameObject;

        // A. SandGridWorld
        World = root.GetComponentInChildren<SandGridWorld>();
        if (World == null)
        {
            GameObject worldGo = new GameObject("SandGridWorld");
            worldGo.transform.SetParent(root.transform, false);
            World = worldGo.AddComponent<SandGridWorld>();
        }
        World.InitializeWorld();

        // B. EcoReclaimerPlayer
        Player = Object.FindFirstObjectByType<EcoReclaimerPlayer>();
        if (Player == null)
        {
            GameObject playerGo = new GameObject("EcoReclaimerPlayer");
            playerGo.transform.SetParent(root.transform, false);
            Player = playerGo.AddComponent<EcoReclaimerPlayer>();
        }
        Player.InitializePlayer(World);

        // C. SweeperDroneManager
        Drones = root.GetComponentInChildren<SweeperDroneManager>();
        if (Drones == null)
        {
            GameObject droneGo = new GameObject("SweeperDroneManager");
            droneGo.transform.SetParent(root.transform, false);
            Drones = droneGo.AddComponent<SweeperDroneManager>();
        }
        Drones.InitializeDrones(World, Player);

        // D. OutpostAndCombatManager
        Combat = root.GetComponentInChildren<OutpostAndCombatManager>();
        if (Combat == null)
        {
            GameObject combatGo = new GameObject("OutpostAndCombatManager");
            combatGo.transform.SetParent(root.transform, false);
            Combat = combatGo.AddComponent<OutpostAndCombatManager>();
        }
        Combat.InitializeCombat(World, Player);

        // E. SolarisUIManager
        UI = root.GetComponentInChildren<SolarisUIManager>();
        if (UI == null)
        {
            GameObject uiGo = new GameObject("SolarisUIManager");
            uiGo.transform.SetParent(root.transform, false);
            UI = uiGo.AddComponent<SolarisUIManager>();
        }
        SolarisUIManager.EnsureEventSystem();
        UI.InitializeUI(World, Player, Drones, Combat, this);

        // 6. Build Storm Particle System
        BuildStormParticleSystem();

        // 7. Build Rain Particle System
        BuildRainParticleSystem();
    }

    private void BuildStormParticleSystem()
    {
        Transform existing = transform.Find("CrimsonStormParticles");
        if (existing != null) DestroyImmediate(existing.gameObject);

        GameObject stormGo = new GameObject("CrimsonStormParticles", typeof(ParticleSystem));
        stormGo.transform.SetParent(transform, false);
        StormParticles = stormGo.GetComponent<ParticleSystem>();

        var main = StormParticles.main;
        main.loop = true;
        main.startLifetime = 3.2f;
        main.startSpeed = 26f;
        main.startSize = 0.5f;
        main.startColor = new Color(0.85f, 0.20f, 0.10f, 0.45f);
        main.simulationSpace = ParticleSystemSimulationSpace.World;

        var em = StormParticles.emission;
        em.rateOverTime = 650f;
        em.enabled = false;

        var shape = StormParticles.shape;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = new Vector3(260f, 40f, 260f);

        var vel = StormParticles.velocityOverLifetime;
        vel.enabled = true;
        vel.x = new ParticleSystem.MinMaxCurve(18f, 26f);
        vel.y = new ParticleSystem.MinMaxCurve(-3f, -1f);
        vel.z = new ParticleSystem.MinMaxCurve(6f, 12f);

        var rend = stormGo.GetComponent<ParticleSystemRenderer>();
        if (rend != null)
        {
            Shader sh = Shader.Find("Particles/Standard Unlit") ?? Shader.Find("Sprites/Default");
            if (sh != null) rend.sharedMaterial = new Material(sh);
        }
    }

    private void BuildRainParticleSystem()
    {
        Transform existing = transform.Find("RestorationRainParticles");
        if (existing != null) DestroyImmediate(existing.gameObject);

        GameObject rainGo = new GameObject("RestorationRainParticles", typeof(ParticleSystem));
        rainGo.transform.SetParent(transform, false);
        rainGo.transform.position = new Vector3(0f, 42f, 0f);
        RainParticles = rainGo.GetComponent<ParticleSystem>();

        var main = RainParticles.main;
        main.loop = true;
        main.startLifetime = 1.6f;
        main.startSpeed = 30f;
        main.startSize = 0.35f;
        main.startColor = new Color(0.40f, 0.85f, 1.0f, 0.70f);
        main.simulationSpace = ParticleSystemSimulationSpace.World;

        var em = RainParticles.emission;
        em.rateOverTime = 1200f;
        em.enabled = false;

        var shape = RainParticles.shape;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = new Vector3(320f, 10f, 320f);

        var vel = RainParticles.velocityOverLifetime;
        vel.enabled = true;
        vel.y = new ParticleSystem.MinMaxCurve(-32f, -26f);

        var rend = rainGo.GetComponent<ParticleSystemRenderer>();
        if (rend != null)
        {
            Shader sh = Shader.Find("Particles/Standard Unlit") ?? Shader.Find("Sprites/Default");
            if (sh != null) rend.sharedMaterial = new Material(sh);
        }
    }

    // ==========================================
    // GAME CONTROL METHODS
    // ==========================================
    public void StartGame()
    {
        GameStarted = true;
        IsPaused = false;
        Time.timeScale = 1f;

        if (MainCam != null && Player != null)
        {
            MainCam.transform.position = Player.transform.position + new Vector3(0f, 20f, -13f);
            MainCam.transform.LookAt(Player.transform.position + Vector3.up * 1.5f);
        }

        if (UI != null)
        {
            UI.SetMainMenuVisible(false);
            UI.SetPauseMenuVisible(false);
            UI.ShowToast("SOLARIS RECLAIM v2.0 BAŞLADI — ÇÖLÜ GERİ ALIN!");
        }
    }

    public void RestartGame()
    {
        GameStarted = false;
        IsPaused = false;
        StormActive = false;
        VictoryAchieved = false;
        FreePlayMode = false;
        StormTimer = STORM_CYCLE_INTERVAL;
        Time.timeScale = 1f;

        BuildScene();
        StartGame();
    }

    public void TogglePause()
    {
        if (!GameStarted) return;
        IsPaused = !IsPaused;
        Time.timeScale = IsPaused ? 0f : 1f;

        if (UI != null)
        {
            UI.SetPauseMenuVisible(IsPaused);
        }
    }

    public bool ToggleMute()
    {
        IsMuted = !IsMuted;
        if (_sfxAudioSource != null) _sfxAudioSource.mute = IsMuted;
        return IsMuted;
    }

    public void EnableFreePlay()
    {
        FreePlayMode = true;
        if (UI != null)
        {
            UI.HideVictoryModal();
            UI.ShowToast("🌿 SERBEST MOD AKTİF: ŞEHRİ GELİŞTİRMEYE DEVAM EDİN!");
        }
    }

    // ==========================================
    // MAIN UPDATE LOOP & STATE MACHINE
    // ==========================================
    private void Update()
    {
        // 1. Menu Orbit Camera (while game has not started)
        if (!GameStarted)
        {
            if (MainCam != null)
            {
                float orbitAngle = Time.time * 6.5f * Mathf.Deg2Rad;
                float orbitRadius = 95f;
                MainCam.transform.position = new Vector3(Mathf.Sin(orbitAngle) * orbitRadius, 36f, Mathf.Cos(orbitAngle) * orbitRadius);
                MainCam.transform.LookAt(new Vector3(0f, 6f, 0f));
            }

            if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.Space))
            {
                StartGame();
            }
            return;
        }

        // 2. Smooth Camera Follow Player (in-game)
        if (MainCam != null && Player != null)
        {
            Vector3 targetCamPos = Player.transform.position + new Vector3(0f, 20f, -13f);
            MainCam.transform.position = Vector3.Lerp(MainCam.transform.position, targetCamPos, Time.deltaTime * 6f);
            MainCam.transform.LookAt(Player.transform.position + Vector3.up * 1.5f);
        }

        // 3. ESC Handling
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            if (UI != null && UI.IsControlsOpen)
            {
                UI.SetControlsVisible(false);
            }
            else if (UI != null && UI.IsWorkshopOpen)
            {
                UI.SetWorkshopVisible(false);
            }
            else
            {
                TogglePause();
            }
        }

        if (IsPaused) return;

        // 4. [F] Key Handling: Workshop Modal Toggle
        if (Input.GetKeyDown(KeyCode.F))
        {
            if (World != null && Player != null)
            {
                bool near = World.IsNearWorkshop(Player.transform.position);
                if (near)
                {
                    if (UI != null) UI.SetWorkshopVisible(!UI.IsWorkshopOpen);
                }
                else
                {
                    if (UI != null) UI.ShowToast("ATÖLYE İÇİN MERKEZİ ECO-HUB BİNASINA (18, 14) YAKLAŞIN");
                }
            }
        }

        // 5. Automatic Dust Smelting near Eco-Hub Workshop
        if (World != null && Player != null && World.IsNearWorkshop(Player.transform.position))
        {
            if (Player.DustTank >= 25f)
            {
                int gained = Player.SmeltDust(true);
                if (gained > 0)
                {
                    PlaySfx("dust_collect");
                }
            }
        }

        // 6. Blood Moon Sandstorm State Machine
        UpdateSandstormStateMachine(Time.deltaTime);

        // 7. 80% Restoration Rain & Victory Celebration
        UpdateRestorationCheck();
    }

    private void UpdateSandstormStateMachine(float dt)
    {
        if (!StormActive)
        {
            StormTimer -= dt;
            if (StormTimer <= 0f)
            {
                // Trigger Blood Moon Sandstorm
                StormActive = true;
                StormDurationRemaining = STORM_CYCLE_DURATION;

                if (Combat != null) Combat.StormActive = true;
                if (Player != null)
                {
                    Player.StormSlowActive = true;
                    Player.StormWindForce = new Vector3(4.5f, 0f, 1.8f);
                }

                if (StormParticles != null)
                {
                    var em = StormParticles.emission;
                    em.enabled = true;
                }

                PlaySfx("storm_siren");
                if (UI != null) UI.ShowToast("⚠️ DİKKAT: KANLI FIRTINA KOPTU! GÖRÜŞ DÜŞÜYOR");
            }
        }
        else
        {
            StormDurationRemaining -= dt;

            // Deep crimson fog shift & visibility restriction
            RenderSettings.fogColor = Color.Lerp(RenderSettings.fogColor, new Color(0.32f, 0.06f, 0.04f), dt * 1.8f);
            RenderSettings.fogDensity = Mathf.Lerp(RenderSettings.fogDensity, 0.036f, dt * 1.8f);

            // Recontaminate unprotected sand cells
            if (World != null)
            {
                World.RecontaminateCircle(Vector3.zero, 210f, 0.008f, dt);
            }

            if (StormDurationRemaining <= 0f)
            {
                // Storm Clears
                StormActive = false;
                StormTimer = STORM_CYCLE_INTERVAL;

                if (Combat != null) Combat.StormActive = false;
                if (Player != null)
                {
                    Player.StormSlowActive = false;
                    Player.StormWindForce = Vector3.zero;
                }

                if (StormParticles != null)
                {
                    var em = StormParticles.emission;
                    em.enabled = false;
                }

                // Restore atmospheric fog
                RenderSettings.fogColor = VictoryAchieved ? new Color(0.12f, 0.28f, 0.35f) : new Color(0.16f, 0.08f, 0.03f);
                RenderSettings.fogDensity = VictoryAchieved ? 0.006f : 0.010f;

                if (UI != null) UI.ShowToast("🌪️ FIRTINA DİNDİ! GÖKYÜZÜ AÇILIYOR");
            }
        }
    }

    private void UpdateRestorationCheck()
    {
        if (World == null || VictoryAchieved) return;

        if (World.RestorationPercent >= SolarisConfig.VICTORY_THRESHOLD)
        {
            VictoryAchieved = true;

            // Enable Rain
            if (RainParticles != null)
            {
                var em = RainParticles.emission;
                em.enabled = true;
            }

            // Shift Sky / Fog to clear Cyan-Blue
            RenderSettings.fogColor = new Color(0.12f, 0.28f, 0.35f);
            RenderSettings.fogDensity = 0.006f;

            PlaySfx("rain_finale");

            if (UI != null)
            {
                UI.ShowVictoryModal();
            }
        }
    }

    // ==========================================
    // AUDIO SYNTHESIZER & SOUND MANAGER
    // ==========================================
    public void PlaySfx(string name)
    {
        if (IsMuted || _sfxAudioSource == null || string.IsNullOrEmpty(name)) return;

        // 1. Try local audio clip cache
        if (_sfxClipCache.TryGetValue(name, out AudioClip cachedClip) && cachedClip != null)
        {
            _sfxAudioSource.PlayOneShot(cachedClip);
            return;
        }

        // 2. Try loading from Resources/Audio/
        AudioClip loadedClip = Resources.Load<AudioClip>("Audio/" + name);
        if (loadedClip != null)
        {
            _sfxClipCache[name] = loadedClip;
            _sfxAudioSource.PlayOneShot(loadedClip);
            return;
        }

        // 3. Synthesize Procedural PCM AudioClip fallback
        AudioClip synthClip = GenerateProceduralClip(name);
        if (synthClip != null)
        {
            _sfxClipCache[name] = synthClip;
            _sfxAudioSource.PlayOneShot(synthClip);
        }
    }

    private AudioClip GenerateProceduralClip(string name)
    {
        int sampleRate = 44100;
        float duration = 0.25f;
        float[] samples;

        switch (name)
        {
            case "blaster_shot":
                duration = 0.16f;
                samples = new float[Mathf.RoundToInt(sampleRate * duration)];
                for (int i = 0; i < samples.Length; i++)
                {
                    float t = (float)i / samples.Length;
                    float freq = Mathf.Lerp(920f, 240f, t);
                    float sin = Mathf.Sin(2f * Mathf.PI * freq * (i / (float)sampleRate));
                    float noise = (Random.value * 2f - 1f) * 0.15f;
                    samples[i] = (sin + noise) * (1f - t) * 0.45f;
                }
                break;

            case "solar_lance":
                duration = 0.38f;
                samples = new float[Mathf.RoundToInt(sampleRate * duration)];
                for (int i = 0; i < samples.Length; i++)
                {
                    float t = (float)i / samples.Length;
                    float freq = Mathf.Lerp(380f, 720f, t);
                    float square = Mathf.Sign(Mathf.Sin(2f * Mathf.PI * freq * (i / (float)sampleRate))) * 0.35f;
                    float hum = Mathf.Sin(2f * Mathf.PI * 110f * (i / (float)sampleRate)) * 0.25f;
                    samples[i] = (square + hum) * (1f - t * 0.6f) * 0.45f;
                }
                break;

            case "dust_collect":
                duration = 0.22f;
                samples = new float[Mathf.RoundToInt(sampleRate * duration)];
                for (int i = 0; i < samples.Length; i++)
                {
                    float t = (float)i / samples.Length;
                    float freq = t < 0.5f ? 523.25f : 659.25f; // C5 to E5
                    float wave = Mathf.Sin(2f * Mathf.PI * freq * (i / (float)sampleRate));
                    samples[i] = wave * (1f - t) * 0.4f;
                }
                break;

            case "upgrade_buy":
                duration = 0.35f;
                samples = new float[Mathf.RoundToInt(sampleRate * duration)];
                for (int i = 0; i < samples.Length; i++)
                {
                    float t = (float)i / samples.Length;
                    float freq = t < 0.33f ? 523.25f : (t < 0.66f ? 659.25f : 783.99f); // C5-E5-G5
                    float wave = Mathf.Sin(2f * Mathf.PI * freq * (i / (float)sampleRate));
                    samples[i] = wave * (1f - t * 0.5f) * 0.5f;
                }
                break;

            case "structure_restore":
                duration = 0.65f;
                samples = new float[Mathf.RoundToInt(sampleRate * duration)];
                for (int i = 0; i < samples.Length; i++)
                {
                    float t = (float)i / samples.Length;
                    float wave1 = Mathf.Sin(2f * Mathf.PI * 440f * (i / (float)sampleRate));
                    float wave2 = Mathf.Sin(2f * Mathf.PI * 554.37f * (i / (float)sampleRate));
                    float wave3 = Mathf.Sin(2f * Mathf.PI * 659.25f * (i / (float)sampleRate));
                    samples[i] = ((wave1 + wave2 + wave3) / 3f) * (1f - t) * 0.5f;
                }
                break;

            case "storm_siren":
                duration = 1.1f;
                samples = new float[Mathf.RoundToInt(sampleRate * duration)];
                for (int i = 0; i < samples.Length; i++)
                {
                    float t = (float)i / samples.Length;
                    float lfo = Mathf.Sin(2f * Mathf.PI * 3f * t); // 3 Hz modulation
                    float freq = 340f + lfo * 110f;
                    float wave = Mathf.Sin(2f * Mathf.PI * freq * (i / (float)sampleRate));
                    samples[i] = wave * (1f - t * 0.2f) * 0.5f;
                }
                break;

            default: // Generic chime
                duration = 0.20f;
                samples = new float[Mathf.RoundToInt(sampleRate * duration)];
                for (int i = 0; i < samples.Length; i++)
                {
                    float t = (float)i / samples.Length;
                    samples[i] = Mathf.Sin(2f * Mathf.PI * 440f * (i / (float)sampleRate)) * (1f - t) * 0.4f;
                }
                break;
        }

        AudioClip clip = AudioClip.Create("Procedural_" + name, samples.Length, 1, sampleRate, false);
        clip.SetData(samples, 0);
        return clip;
    }
}
