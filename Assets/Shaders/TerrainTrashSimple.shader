Shader "Solaris/TerrainTrashSimple"
{
    Properties
    {
        _MainTex ("Trash / Dust Endless Texture", 2D) = "white" {}
        _Tiling ("Texture Tiling", Float) = 48.0
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 200

        CGPROGRAM
        #pragma surface surf Lambert

        sampler2D _MainTex;
        float _Tiling;

        struct Input
        {
            float2 uv_MainTex;
        };

        void surf (Input IN, inout SurfaceOutput o)
        {
            fixed4 c = tex2D(_MainTex, IN.uv_MainTex * _Tiling);
            o.Albedo = c.rgb;
            o.Alpha = 1.0;
        }
        ENDCG
    }
    FallBack "Diffuse"
}
