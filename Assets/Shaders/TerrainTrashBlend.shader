Shader "Solaris/TerrainTrashBlend"
{
    Properties
    {
        _TrashTex ("Trash & Scrap Texture", 2D) = "white" {}
        _RoadTex ("Asphalt Road Texture", 2D) = "white" {}
        _GrassTex ("Restored Grass Texture", 2D) = "white" {}
        _GroundTex ("Base Concrete / Ground", 2D) = "white" {}
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 200

        CGPROGRAM
        #pragma surface surf Lambert

        sampler2D _TrashTex;
        sampler2D _RoadTex;
        sampler2D _GrassTex;
        sampler2D _GroundTex;

        struct Input
        {
            float2 uv_TrashTex;
            float4 color : COLOR;
        };

        void surf (Input IN, inout SurfaceOutput o)
        {
            float2 uv = IN.uv_TrashTex;

            float4 trashCol = tex2D(_TrashTex, uv);
            float4 roadCol = tex2D(_RoadTex, uv);
            float4 grassCol = tex2D(_GrassTex, uv);
            float4 groundCol = tex2D(_GroundTex, uv);

            // IN.color: R=Trash, G=Grass, B=Road
            float trashWeight = saturate(IN.color.r * 1.35);
            float grassWeight = saturate(IN.color.g);
            float roadWeight = saturate(IN.color.b);

            // 1. Base clean ground (road on boulevards, concrete elsewhere)
            float3 baseClean = lerp(groundCol.rgb, roadCol.rgb, roadWeight);

            // 2. Restored lush grass
            float3 cleanWithGrass = lerp(baseClean, grassCol.rgb, grassWeight);

            // 3. Top waste / trash dune layer
            float3 finalAlbedo = lerp(cleanWithGrass, trashCol.rgb, trashWeight);

            o.Albedo = finalAlbedo;
            o.Alpha = 1.0;
        }
        ENDCG
    }
    FallBack "Diffuse"
}
