using UnityEngine;

public static class SolarisConfig
{
    public const float WORLD_SIZE = 420f;
    public const int GRID_SIZE = 160;
    public const float HALF_WORLD = 210f;
    public const float CELL_SIZE = WORLD_SIZE / (GRID_SIZE - 1);
    public const float MAX_SAND_HEIGHT = 3.8f;
    public const float RESTORE_THRESHOLD = 0.42f;
    public const int START_SILICA = 0;
    public const int TREE_COST = 8;
    public const float OASIS_RADIUS = 18f;
    public const float STORM_INTERVAL = 120f;
    public const float STORM_DURATION = 45f;
    public const float VICTORY_THRESHOLD = 80f;
    public const int MAX_ACTIVE_ENEMIES = 14;

    public static readonly Vector3 WORKSHOP_POS = new Vector3(18f, 0f, 14f);
    public static readonly int[] COST_NOZZLE = { 15, 40, 90 };
    public static readonly int[] COST_TANK = { 15, 40, 90 };
    public static readonly int[] COST_BLASTER = { 200, 500, 1100 }; // +1000% increased cost tier
    public static readonly int[] COST_LANCE = { 300, 700, 1400 };   // +1000% increased cost tier
    public static readonly int[] COST_DRONE = { 35, 75, 130, 200 };

    public static Shader GetLitShader()
    {
        Shader s = Shader.Find("Universal Render Pipeline/Lit");
        if (s == null) s = Shader.Find("Standard");
        if (s == null) s = Shader.Find("Diffuse");
        return s;
    }

    public static Material CreateMaterial(Color color, float metallic = 0.1f, float smoothness = 0.3f, Texture2D mainTex = null, Color? emission = null)
    {
        Shader shader = GetLitShader();
        Material mat = new Material(shader);

        if (mat.HasProperty("_Color")) mat.SetColor("_Color", color);
        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);

        if (mainTex != null)
        {
            if (mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", mainTex);
            if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", mainTex);
        }

        if (mat.HasProperty("_Metallic")) mat.SetFloat("_Metallic", metallic);
        if (mat.HasProperty("_Glossiness")) mat.SetFloat("_Glossiness", smoothness);
        if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", smoothness);

        if (emission.HasValue)
        {
            mat.EnableKeyword("_EMISSION");
            if (mat.HasProperty("_EmissionColor")) mat.SetColor("_EmissionColor", emission.Value);
        }

        return mat;
    }

    public static int WorldToGridX(float x)
    {
        return Mathf.Clamp(Mathf.RoundToInt((x + HALF_WORLD) / CELL_SIZE), 0, GRID_SIZE - 1);
    }

    public static int WorldToGridZ(float z)
    {
        return Mathf.Clamp(Mathf.RoundToInt((z + HALF_WORLD) / CELL_SIZE), 0, GRID_SIZE - 1);
    }

    public static float GridToWorldX(int gx)
    {
        return gx * CELL_SIZE - HALF_WORLD;
    }

    public static float GridToWorldZ(int gz)
    {
        return gz * CELL_SIZE - HALF_WORLD;
    }

    public static void SafeDestroy(UnityEngine.Object obj)
    {
        if (obj == null) return;
        if (Application.isPlaying)
            UnityEngine.Object.Destroy(obj);
        else
            UnityEngine.Object.DestroyImmediate(obj);
    }

    public static void SafeDestroy(UnityEngine.Object obj, float delay)
    {
        if (obj == null) return;
        if (Application.isPlaying)
            UnityEngine.Object.Destroy(obj, delay);
        else
            UnityEngine.Object.DestroyImmediate(obj);
    }
}
