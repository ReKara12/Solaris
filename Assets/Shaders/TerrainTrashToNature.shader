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
            };

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                o.color = v.color;
                o.worldNormal = UnityObjectToWorldNormal(v.normal);
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                fixed4 trashCol = tex2D(_TrashTex, i.uv);
                fixed4 grassCol = tex2D(_GrassTex, i.uv);
                float trashWeight = saturate(i.color.r * 1.25);
                fixed4 groundCol = lerp(grassCol, trashCol, trashWeight);

                // Gentle diffuse sunlight + warm ambient
                float3 lightDir = normalize(_WorldSpaceLightPos0.xyz);
                float ndotl = max(0.0, dot(normalize(i.worldNormal), lightDir));
                float3 lighting = _LightColor0.rgb * (ndotl * 0.35) + float3(0.78, 0.76, 0.74);
                return fixed4(groundCol.rgb * lighting, 1.0);
            }
            ENDCG
        }
    }
    FallBack "Diffuse"
}
