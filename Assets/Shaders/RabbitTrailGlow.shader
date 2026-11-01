Shader "Grove/Rabbit Trail Glow"
{
    Properties
    {
        _BaseMap ("Footprint Mask", 2D) = "white" {}
        [HDR] _GlowColor ("Blue Glow", Color) = (0.08, 0.65, 5.5, 1)
        _UseTexture ("Footprint (1) / Wisp (0)", Float) = 1
        _PulseAmount ("Pulse Amount", Range(0, 0.5)) = 0.15
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Blend One One
            ZWrite Off
            Cull Off
            Offset -1, -1
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _GlowColor;
                float _UseTexture;
                float _PulseAmount;
            CBUFFER_END
            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                half4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                half4 color : COLOR;
                float phase : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };
            Varyings Vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                float3 world = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(world);
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                output.color = input.color;
                output.phase = world.x * 0.6 + world.z * 0.35;
                return output;
            }
            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                half4 tex = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv);
                // The supplied image has a faint horizontal scan line outside the two paw shapes.
                half footprint = tex.r * tex.a * (1 - smoothstep(0.27, 0.32, abs(input.uv.y - 0.5)));
                float radius = length(input.uv * 2 - 1);
                half wisp = pow(saturate(1 - radius), 2);
                half mask = lerp(wisp, footprint, _UseTexture);
                half pulse = 1 - _PulseAmount + _PulseAmount * sin(_Time.y * 2 + input.phase);
                half4 tint = lerp(input.color, half4(1, 1, 1, 1), _UseTexture);
                return half4(_GlowColor.rgb * tint.rgb * tint.a * mask * pulse, 0);
            }
            ENDHLSL
        }
    }
}
