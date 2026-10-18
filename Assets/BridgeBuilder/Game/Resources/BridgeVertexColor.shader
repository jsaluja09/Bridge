// Vertex-colored, simply lit, double-sided. Used for every bridge member, joint and fill in one mesh.
// uv.x = distance along a member (room meters), uv.y = utilization; with _StripeAmount > 0, members get
// wide stripes that grow denser as utilization rises (the spec's second, non-color cue).
// uv1 = (u, v, slot, tint): slot 0/1/2 picks the wood/cable/concrete texture, and the vertex color covers it by
// tint (0 = pure material); slot -1 is plain vertex color. _*Color multiplies each texture (a flat stand-in
// color when the texture is missing).
Shader "BridgeBuilder/VertexColor"
{
    Properties
    {
        _StripeAmount ("Stripe Amount", Range(0, 1)) = 0
        _WoodTex ("Wood", 2D) = "white" {}
        _CableTex ("Steel Cable", 2D) = "white" {}
        _ConcreteTex ("Concrete", 2D) = "white" {}
        _WoodColor ("Wood Color", Color) = (1, 1, 1, 1)
        _CableColor ("Steel Cable Color", Color) = (1, 1, 1, 1)
        _ConcreteColor ("Concrete Color", Color) = (1, 1, 1, 1)
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

            TEXTURE2D(_WoodTex);
            SAMPLER(sampler_WoodTex);
            TEXTURE2D(_CableTex);
            SAMPLER(sampler_CableTex);
            TEXTURE2D(_ConcreteTex);
            SAMPLER(sampler_ConcreteTex);

            CBUFFER_START(UnityPerMaterial)
                float _StripeAmount;
                half4 _WoodColor;
                half4 _CableColor;
                half4 _ConcreteColor;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float4 color : COLOR;
                float2 uv : TEXCOORD0;
                float4 mat : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS : TEXCOORD0;
                float2 uv : TEXCOORD1;
                float4 mat : TEXCOORD2;
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
                output.mat = input.mat;
                output.color = input.color;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                // Sample all three and pick by weight: sampling inside a branch breaks mip selection.
                float2 texUV = input.mat.xy;
                float slot = input.mat.z;
                half3 wood = SAMPLE_TEXTURE2D(_WoodTex, sampler_WoodTex, texUV).rgb * _WoodColor.rgb;
                half3 cable = SAMPLE_TEXTURE2D(_CableTex, sampler_CableTex, texUV).rgb * _CableColor.rgb;
                half3 concrete = SAMPLE_TEXTURE2D(_ConcreteTex, sampler_ConcreteTex, texUV).rgb * _ConcreteColor.rgb;
                half isWood = step(-0.5, slot) * step(slot, 0.5);
                half isCable = step(0.5, slot) * step(slot, 1.5);
                half isConcrete = step(1.5, slot);
                half3 surface = wood * isWood + cable * isCable + concrete * isConcrete;
                half textured = step(-0.5, slot);
                half3 albedo = lerp(input.color.rgb, lerp(surface, input.color.rgb, saturate(input.mat.w)), textured);

                Light light = GetMainLight();
                float diffuse = abs(dot(normalize(input.normalWS), light.direction));
                half3 color = albedo * (0.55 + 0.45 * diffuse);

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
