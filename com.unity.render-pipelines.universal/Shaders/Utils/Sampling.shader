Shader "Hidden/Universal Render Pipeline/Sampling"
{
    HLSLINCLUDE
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

    struct Attributes
    {
        float4 positionOS   : POSITION;
        float2 texcoord     : TEXCOORD0;
        UNITY_VERTEX_INPUT_INSTANCE_ID
    };

    struct VertQuadAttributes
    {
        uint vertexID : SV_VertexID;
        UNITY_VERTEX_INPUT_INSTANCE_ID
    };

    struct Varyings
    {
        float4  positionCS  : SV_POSITION;
        float2  uv          : TEXCOORD0;
        UNITY_VERTEX_INPUT_INSTANCE_ID
        UNITY_VERTEX_OUTPUT_STEREO
    };

    uniform float4 _BlitScaleBias;
    uniform float4 _BlitScaleBiasRt;

    Varyings VertQuad(VertQuadAttributes input)
    {
        Varyings output;
        UNITY_SETUP_INSTANCE_ID(input);
        UNITY_TRANSFER_INSTANCE_ID(input, output);
        UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

        output.positionCS = GetQuadVertexPosition(input.vertexID) * float4(_BlitScaleBiasRt.x, _BlitScaleBiasRt.y, 1, 1) + float4(_BlitScaleBiasRt.z, _BlitScaleBiasRt.w, 0, 0);
        output.positionCS.xy = output.positionCS.xy * float2(2.0f, -2.0f) + float2(-1.0f, 1.0f); //convert to -1..1
        output.uv = GetQuadTexCoord(input.vertexID) * _BlitScaleBias.xy + _BlitScaleBias.zw;
        return output;
    }

    Varyings Vertex(Attributes input)
    {
        Varyings output;
        UNITY_SETUP_INSTANCE_ID(input);
        UNITY_TRANSFER_INSTANCE_ID(input, output);
        UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

        output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
        output.uv = UnityStereoTransformScreenSpaceTex(input.texcoord);
        return output;
    }

    half4 DownsampleBox4Tap(TEXTURE2D_X_PARAM(tex, samplerTex), float2 uv, float2 texelSize, float amount)
    {
        float4 d = texelSize.xyxy * float4(-amount, -amount, amount, amount);

        half4 s;
        s =  (SAMPLE_TEXTURE2D_X(tex, samplerTex, uv + d.xy));
        s += (SAMPLE_TEXTURE2D_X(tex, samplerTex, uv + d.zy));
        s += (SAMPLE_TEXTURE2D_X(tex, samplerTex, uv + d.xw));
        s += (SAMPLE_TEXTURE2D_X(tex, samplerTex, uv + d.zw));

        return s * 0.25h;
    }

    ENDHLSL

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline"}
        LOD 100

        // 0 - Downsample - Box filtering
        Pass
        {
            Name "Default"
            ZTest Always
            ZWrite Off

            HLSLPROGRAM
            // Required to compile gles 2.0 with standard srp library
            #pragma prefer_hlslcc gles
            #pragma vertex VertQuad
            #pragma fragment FragBoxDownsample

            TEXTURE2D_X(_BlitTex);
            SAMPLER(sampler_BlitTex);
            float4 _BlitTex_TexelSize;

            float _SampleOffset;

            half4 FragBoxDownsample(Varyings input) : SV_Target
            {
                half4 col = DownsampleBox4Tap(TEXTURE2D_X_ARGS(_BlitTex, sampler_BlitTex), input.uv, _BlitTex_TexelSize.xy, _SampleOffset);
                return half4(col.rgb, 1);
            }
            ENDHLSL
        }
    }
}
