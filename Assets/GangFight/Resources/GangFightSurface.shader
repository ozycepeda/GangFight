Shader "GangFight/Surface"
{
    Properties { _Color ("Color", Color) = (1, 1, 1, 1) }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            fixed4 _Color;
            struct Vertex { float4 position : POSITION; float3 normal : NORMAL; };
            struct Pixel { float4 clipPosition : SV_POSITION; half3 normal : TEXCOORD0; float3 world : TEXCOORD1; };
            Pixel vert(Vertex input)
            {
                Pixel output;
                output.clipPosition = UnityObjectToClipPos(input.position);
                output.normal = UnityObjectToWorldNormal(input.normal);
                output.world = mul(unity_ObjectToWorld, input.position).xyz;
                return output;
            }
            fixed4 frag(Pixel input) : SV_Target
            {
                half3 normal = normalize(input.normal);
                half light = saturate(dot(normal, normalize(half3(-.55, 1, .45))));
                half rim = pow(1 - saturate(dot(normal, normalize(_WorldSpaceCameraPos - input.world))), 3);
                return fixed4(_Color.rgb * (.50 + .50 * light) + rim * .11, 1);
            }
            ENDCG
        }
    }
}
