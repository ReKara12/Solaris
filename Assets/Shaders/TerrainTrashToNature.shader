Shader "Solaris/TerrainTrashToNature"
{
    Properties
    {
        _TrashTex ("Trash & Scrap Endless Texture", 2D) = "white" {}
        _GrassTex ("Lush Grass & Flowers Texture", 2D) = "white" {}
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        LOD 100
        Cull Off

        Pass
        {
            Tags { "LightMode"="ForwardBase" }
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            #include "Lighting.cginc"

            sampler2D _TrashTex;
            sampler2D _GrassTex;

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
                float3 worldNormal : TEXCOORD1;
                float3 worldPos : TEXCOORD2;
            };

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                o.color = v.color;
                o.worldNormal = UnityObjectToWorldNormal(v.normal);
                o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                float3 N = normalize(i.worldNormal);
                float3 L = normalize(_WorldSpaceLightPos0.xyz);

                // Sample terrain textures
                fixed4 trashCol = tex2D(_TrashTex, i.uv);
                fixed4 grassCol = tex2D(_GrassTex, i.uv);

                float trashWeight = saturate(i.color.r * 1.25);
                fixed4 groundCol = lerp(grassCol, trashCol, trashWeight);

                // Slope steepness (0.0 on flat ground, 0.4-0.8 on crater walls)
                float slope = saturate(1.0 - N.y);

                // Excavated Trench Cut-Edge: earthy soil cross-section along the crater slope
                float cutBand = smoothstep(0.04, 0.35, trashWeight) * smoothstep(0.96, 0.65, trashWeight);
                fixed3 dirtTrenchCol = fixed3(0.22, 0.15, 0.10); // Damp exposed earth cut
                groundCol.rgb = lerp(groundCol.rgb, dirtTrenchCol, cutBand * saturate(slope * 2.4) * 0.70);

                // Contact shadow inside the cut perimeter
                groundCol.rgb *= (1.0 - cutBand * 0.30);

                // Directional 3D lighting with clear sunny/shadowed crater walls
                float ndotl = dot(N, L);
                float halfLambert = saturate(ndotl * 0.55 + 0.45);
                float3 sunLight = _LightColor0.rgb * halfLambert * 0.95;

                // Cavity Ambient Occlusion in crater depressions
                float cavityAO = 1.0 - slope * 0.45;
                float3 ambient = float3(0.38, 0.36, 0.35) * cavityAO;

                // Sun-facing rim highlight on the crater crest
                float rimLight = saturate(dot(N, float3(-L.x, 0.45, -L.z))) * slope * 0.25;

                float3 totalLighting = (sunLight + ambient + rimLight);
                return fixed4(groundCol.rgb * totalLighting, 1.0);
            }
            ENDCG
        }
    }
    FallBack "Diffuse"
}
