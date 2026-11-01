using System.Collections.Generic;
using UnityEngine;

public class SweeperDroneManager : MonoBehaviour
{
    public static SweeperDroneManager Instance { get; private set; }

    [Header("Drone Roster")]
    public List<Transform> Drones = new List<Transform>();
    public int ActiveDroneCount => Drones.Count;

    [System.Serializable]
    public class DroneData
    {
        public Transform Root;
        public Vector3 TargetPos;
        public float Speed = 10.5f;
        public float BobOffset;
        public float RetargetTimer;
        public float CleanTimer;
        public float AttackCooldown;
        public List<Transform> Rotors = new List<Transform>();
        public bool HasTarget;
    }

    private readonly List<DroneData> _droneUnits = new List<DroneData>();
    private SandGridWorld _world;
    private EcoReclaimerPlayer _player;

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
        if (_world == null) _world = FindFirstObjectByType<SandGridWorld>();
        if (_player == null) _player = FindFirstObjectByType<EcoReclaimerPlayer>();
    }

    public void InitializeDrones(SandGridWorld world, EcoReclaimerPlayer player)
    {
        _world = world;
        _player = player;

        // Clear existing drones
        for (int i = 0; i < Drones.Count; i++)
        {
            if (Drones[i] != null)
            {
                if (Application.isPlaying) SolarisConfig.SafeDestroy(Drones[i].gameObject);
                else DestroyImmediate(Drones[i].gameObject);
            }
        }
        Drones.Clear();
        _droneUnits.Clear();
    }

    /// <summary>
    /// Purchases and deploys an autonomous quad-rotor mini sweeper drone (up to 4 drones).
    /// </summary>
    public bool BuyDrone()
    {
        if (Drones.Count >= 4)
        {
            SolarisUIManager.Instance?.ShowToast("Maksimum drone kapasitesine (4) ulaşıldı!");
            return false;
        }

        Vector3 origin = _player != null ? _player.transform.position : Vector3.zero;
        Vector3 spawnPos = origin + new Vector3(Random.Range(-5f, 5f), 2.5f, Random.Range(-5f, 5f));

        GameObject droneGo = new GameObject($"SweeperDrone_{Drones.Count + 1}");
        droneGo.transform.position = spawnPos;

        Material bodyMat = SolarisConfig.CreateMaterial(new Color(0.18f, 0.20f, 0.25f), 0.7f, 0.35f);
        Material cyanMat = SolarisConfig.CreateMaterial(new Color(0f, 0.9f, 1f), 0.1f, 0.9f, null, new Color(0f, 0.9f, 1f) * 1.5f);

        // 1. Chassis Body
        CreatePrimitivePart(PrimitiveType.Cube, "Body", droneGo.transform, new Vector3(0f, 0.2f, 0f), new Vector3(1.2f, 0.35f, 1.2f), Quaternion.identity, bodyMat);

        // 2. Cyan Sensor Dome
        CreatePrimitivePart(PrimitiveType.Sphere, "Dome", droneGo.transform, new Vector3(0f, 0.42f, 0f), new Vector3(0.64f, 0.35f, 0.64f), Quaternion.identity, cyanMat);

        // 3. 4 Quad-Rotors
        List<Transform> rotors = new List<Transform>();
        Vector2[] offsets = {
            new Vector2(-0.5f, 0.5f),
            new Vector2(0.5f, 0.5f),
            new Vector2(-0.5f, -0.5f),
            new Vector2(0.5f, -0.5f)
        };

        for (int i = 0; i < offsets.Length; i++)
        {
            GameObject rotorPivot = new GameObject($"RotorPivot_{i}");
            rotorPivot.transform.SetParent(droneGo.transform, false);
            rotorPivot.transform.localPosition = new Vector3(offsets[i].x, 0.4f, offsets[i].y);

            CreatePrimitivePart(PrimitiveType.Cube, "Blade", rotorPivot.transform, Vector3.zero, new Vector3(0.7f, 0.04f, 0.08f), Quaternion.identity, bodyMat);
            rotors.Add(rotorPivot.transform);
        }

        // 4. Cyan Laser Emitter Eye
        CreatePrimitivePart(PrimitiveType.Cylinder, "LaserEye", droneGo.transform, new Vector3(0f, 0.1f, 0.65f), new Vector3(0.12f, 0.12f, 0.12f), Quaternion.Euler(90f, 0f, 0f), cyanMat);

        // 5. Point Light
        GameObject lightGo = new GameObject("DroneLight");
        lightGo.transform.SetParent(droneGo.transform, false);
        lightGo.transform.localPosition = new Vector3(0f, 0.5f, 0f);
        Light l = lightGo.AddComponent<Light>();
        l.type = LightType.Point;
        l.color = new Color(0f, 0.9f, 1f);
        l.range = 8f;
        l.intensity = 1.2f;

        DroneData data = new DroneData
        {
            Root = droneGo.transform,
            TargetPos = spawnPos,
            Speed = 10.5f,
            BobOffset = Random.Range(0f, Mathf.PI * 2f),
            RetargetTimer = 0f,
            CleanTimer = 0f,
            AttackCooldown = Random.Range(0.2f, 0.8f),
            Rotors = rotors,
            HasTarget = false
        };

        Drones.Add(droneGo.transform);
        _droneUnits.Add(data);

        SolarisUIManager.Instance?.ShowToast($"🤖 Otonom Süpürücü Drone #{Drones.Count} devriyeye başladı!");
        return true;
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
        if (SolarisGameManager.Instance != null && (!SolarisGameManager.Instance.GameStarted || SolarisGameManager.Instance.IsPaused))
            return;

        float dt = Time.deltaTime;

        for (int i = 0; i < _droneUnits.Count; i++)
        {
            DroneData d = _droneUnits[i];
            if (d == null || d.Root == null) continue;

            // 1. Spin Rotors
            for (int r = 0; r < d.Rotors.Count; r++)
            {
                if (d.Rotors[r] != null)
                {
                    float dir = (r % 2 == 0) ? 1200f : -1200f;
                    d.Rotors[r].Rotate(Vector3.up, dir * dt, Space.Self);
                }
            }

            // 2. Sand Dune Patrol & Targeting
            d.RetargetTimer -= dt;
            if (!d.HasTarget || d.RetargetTimer <= 0f)
            {
                FindBestSandTarget(d);
            }

            Vector3 curPos = d.Root.position;
            Vector3 toTarget = d.TargetPos - curPos;
            toTarget.y = 0f;
            float dist = toTarget.magnitude;

            if (dist > 1.2f)
            {
                Vector3 moveDir = toTarget / dist;
                curPos += moveDir * d.Speed * dt;
                Quaternion targetRot = Quaternion.LookRotation(moveDir);
                d.Root.rotation = Quaternion.Slerp(d.Root.rotation, targetRot, 10f * dt);
            }
            else
            {
                // Drone is at the sand dune, cleaning sand
                float cleaned = _world != null ? _world.CleanCircle(curPos, 3.2f, 1.6f * dt) : 0f;
                if (cleaned > 0.01f)
                {
                    d.CleanTimer += dt;
                    if (d.CleanTimer >= 18.0f)
                    {
                        d.CleanTimer -= 18.0f;
                        if (_player != null)
                        {
                            _player.Silica += 1;
                            SolarisUIManager.Instance?.ShowToast("🤖 Otonom Drone +1 💎 Silika topladı!");
                        }
                    }
                }

                if (d.RetargetTimer <= 0f)
                {
                    d.HasTarget = false;
                }
            }

            // Adjust height above terrain + bobbing
            float groundH = _world != null ? _world.GetHeight(curPos.x, curPos.z) : 0f;
            d.BobOffset += dt * 3.5f;
            curPos.y = groundH + 2.2f + Mathf.Sin(d.BobOffset) * 0.25f;
            curPos.x = Mathf.Clamp(curPos.x, -204f, 204f);
            curPos.z = Mathf.Clamp(curPos.z, -204f, 204f);
            d.Root.position = curPos;

            // 3. Cyan Defense Laser (every 1.2s deals 12 damage to closest enemy within 20m)
            d.AttackCooldown -= dt;
            if (d.AttackCooldown <= 0f && OutpostAndCombatManager.Instance != null)
            {
                OutpostAndCombatManager.EnemyUnit nearest = OutpostAndCombatManager.Instance.GetNearestEnemy(curPos, 20.0f);
                if (nearest != null)
                {
                    d.AttackCooldown = 1.2f;
                    OutpostAndCombatManager.Instance.FireDroneLaser(d.Root.position, nearest, 12f);
                }
            }
        }
    }

    private void FindBestSandTarget(DroneData d)
    {
        if (_world != null && _world.Heights != null)
        {
            int cx = SolarisConfig.WorldToGridX(d.Root.position.x);
            int cz = SolarisConfig.WorldToGridZ(d.Root.position.z);
            int reach = 16;
            float bestScore = -1f;
            Vector3 bestTarget = d.Root.position;
            bool found = false;

            for (int dz = -reach; dz <= reach; dz += 2)
            {
                int gz = cz + dz;
                if (gz < 0 || gz >= SolarisConfig.GRID_SIZE) continue;

                for (int dx = -reach; dx <= reach; dx += 2)
                {
                    int gx = cx + dx;
                    if (gx < 0 || gx >= SolarisConfig.GRID_SIZE) continue;

                    int idx = gz * SolarisConfig.GRID_SIZE + gx;
                    float h = _world.Heights[idx];
                    if (h > 0.15f)
                    {
                        float wx = SolarisConfig.GridToWorldX(gx);
                        float wz = SolarisConfig.GridToWorldZ(gz);
                        float dist = Vector2.Distance(new Vector2(wx, wz), new Vector2(d.Root.position.x, d.Root.position.z));
                        float score = h * 10f - dist * 0.4f;
                        if (score > bestScore)
                        {
                            bestScore = score;
                            bestTarget = new Vector3(wx, 0f, wz);
                            found = true;
                        }
                    }
                }
            }

            if (found)
            {
                d.TargetPos = bestTarget;
                d.HasTarget = true;
                d.RetargetTimer = Random.Range(2.0f, 3.5f);
                return;
            }
        }

        // Fallback: roam in area around player
        Vector3 playerPos = _player != null ? _player.transform.position : Vector3.zero;
        Vector2 randCircle = Random.insideUnitCircle * 24f;
        d.TargetPos = playerPos + new Vector3(randCircle.x, 0f, randCircle.y);
        d.HasTarget = true;
        d.RetargetTimer = Random.Range(3.0f, 5.0f);
    }
}
