// Vertex-colored, simply lit, double-sided. Used for every bridge member, joint and fill in one mesh.
// uv.x = distance along a member (room meters), uv.y = utilization; with _StripeAmount > 0, members get
// wide stripes that grow denser as utilization rises (the spec's second, non-color cue).
Shader "BridgeBuilder/VertexColor"
{
    Properties
    {
        _StripeAmount ("Stripe Amount", Range(0, 1)) = 0
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }
        Pass
        {
            Name "Forward"
            Tags { "LightMode" = "UniversalForward" }
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _StripeAmount;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float4 color : COLOR;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS : TEXCOORD0;
                float2 uv : TEXCOORD1;
                half4 color : COLOR;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.uv = input.uv;
                output.color = input.color;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                Light light = GetMainLight();
                float diffuse = abs(dot(normalize(input.normalWS), light.direction));
                half3 color = input.color.rgb * (0.55 + 0.45 * diffuse);

                float utilization = saturate(input.uv.y);
                float frequency = lerp(3.0, 25.0, utilization);
                float stripe = step(0.5, frac(input.uv.x * frequency));
                float amount = _StripeAmount * step(0.001, input.uv.y);
                color *= lerp(1.0, lerp(0.7, 1.0, stripe), amount);
                return half4(color, 1.0);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
