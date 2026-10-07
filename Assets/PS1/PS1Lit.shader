// PS1 look for URP: vertex snapping (jitter), affine texture mapping, point-filtered textures,
// per-vertex (Gouraud) lighting, 15-bit color with ordered dithering, fog. Works with single-pass VR.
Shader "PS1/Lit"
{
    Properties
    {
        _BaseMap ("Texture", 2D) = "white" {}
        _BaseColor ("Color", Color) = (1,1,1,1)
        _SnapResolution ("Vertex Snap Resolution", Vector) = (320, 240, 0, 0)
        _AffineAmount ("Affine Texture Warp", Range(0, 1)) = 0.6
        _ColorDepth ("Color Levels", Float) = 32
        _DitherStrength ("Dither Strength", Range(0, 1)) = 1
        _AmbientBoost ("Ambient Boost", Range(0, 2)) = 1
        _Cutoff ("Alpha Cutoff", Range(0, 1)) = 0.5
        [Toggle] _AlphaClip ("Alpha Clip", Float) = 0
        [Toggle] _WorldUV ("World Space UV (box projection)", Float) = 0
        _WorldUVScale ("World UV Scale (tiles per meter)", Float) = 1
        _WorldUVOffset ("World UV Offset", Vector) = (0, 0, 0, 0)
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull", Float) = 2
        _OffsetFactor ("Depth Offset Factor (decals: -1)", Float) = 0
        _OffsetUnits ("Depth Offset Units (decals: -1)", Float) = 0
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }
        LOD 100

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Cull [_Cull]
            Offset [_OffsetFactor], [_OffsetUnits]

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_point_repeat);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _BaseColor;
                float4 _SnapResolution;
                half _AffineAmount;
                half _ColorDepth;
                half _DitherStrength;
                half _AmbientBoost;
                half _Cutoff;
                half _AlphaClip;
                half _WorldUV;
                float _WorldUVScale;
                float4 _WorldUVOffset;
            CBUFFER_END

            // Scene lights uploaded by PS1LightManager (URP additional lights are disabled on this project).
            #define PS1_MAX_POINTS 8
            float4 _PS1PointPos[PS1_MAX_POINTS];   // xyz position, w range
            float4 _PS1PointColor[PS1_MAX_POINTS]; // rgb color * intensity
            float _PS1PointCount;
            float4 _PS1SpotPos;   // xyz position, w range
            float4 _PS1SpotDir;   // xyz direction, w enabled
            float4 _PS1SpotColor; // rgb color * intensity
            float4 _PS1SpotCone;  // x cos(outer), y cos(inner)
            float4 _PS1Ambient;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                noperspective float2 uvAffine : TEXCOORD1;
                half3 lighting : TEXCOORD2;
                float fogCoord : TEXCOORD3;
                float3 positionWS : TEXCOORD4;
                half3 normalWS : TEXCOORD5;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            half Attenuation(float dist, float range)
            {
                half x = saturate(1.0 - (dist * dist) / max(range * range, 0.0001));
                return x * x;
            }

            half3 SceneLights(float3 positionWS, half3 normalWS)
            {
                half3 sum = 0;
                int count = (int)_PS1PointCount;
                for (int i = 0; i < PS1_MAX_POINTS; i++)
                {
                    if (i >= count) break;
                    float3 toLight = _PS1PointPos[i].xyz - positionWS;
                    float dist = length(toLight);
                    half ndotl = saturate(dot(normalWS, toLight / max(dist, 0.0001)) * 0.8 + 0.2);
                    sum += _PS1PointColor[i].rgb * ndotl * Attenuation(dist, _PS1PointPos[i].w);
                }
                if (_PS1SpotDir.w > 0.5)
                {
                    float3 toLight = _PS1SpotPos.xyz - positionWS;
                    float dist = length(toLight);
                    float3 l = toLight / max(dist, 0.0001);
                    half cone = smoothstep(_PS1SpotCone.x, _PS1SpotCone.y, dot(-l, _PS1SpotDir.xyz));
                    half ndotl = saturate(dot(normalWS, l) * 0.8 + 0.2);
                    sum += _PS1SpotColor.rgb * ndotl * cone * Attenuation(dist, _PS1SpotPos.w);
                }
                return sum;
            }

            Varyings vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                float4 positionCS = TransformWorldToHClip(positionWS);

                // Snap to a coarse screen grid: the classic PS1 vertex wobble (0 = off, steadier in VR).
                if (_SnapResolution.x > 0.5)
                {
                    float2 res = _SnapResolution.xy;
                    float2 ndc = positionCS.xy / positionCS.w;
                    ndc = round(ndc * res * 0.5) / (res * 0.5);
                    positionCS.xy = ndc * positionCS.w;
                }
                output.positionCS = positionCS;

                float2 uv = TRANSFORM_TEX(input.uv, _BaseMap);
                output.uv = uv;
                output.uvAffine = uv;

                // Gouraud lighting: main light + ambient, evaluated per vertex.
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
                Light mainLight = GetMainLight();
                half ndotl = saturate(dot(normalWS, mainLight.direction));
                // Flat ambient set by PS1Style/DarkAmbience (baked light probes of the template are too bright).
                half3 ambient = _PS1Ambient.rgb * _AmbientBoost;
                output.lighting = ambient + mainLight.color * ndotl;
                output.positionWS = positionWS;
                output.normalWS = normalWS;

                output.fogCoord = ComputeFogFactor(positionCS.z);
                return output;
            }

            static const half bayer4[16] =
            {
                0.0 / 16.0,  8.0 / 16.0,  2.0 / 16.0, 10.0 / 16.0,
                12.0 / 16.0, 4.0 / 16.0, 14.0 / 16.0,  6.0 / 16.0,
                3.0 / 16.0, 11.0 / 16.0,  1.0 / 16.0,  9.0 / 16.0,
                15.0 / 16.0, 7.0 / 16.0, 13.0 / 16.0,  5.0 / 16.0
            };

            half4 frag(Varyings input, FRONT_FACE_TYPE facing : FRONT_FACE_SEMANTIC) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float2 uv = lerp(input.uv, input.uvAffine, _AffineAmount);
                if (_WorldUV > 0.5)
                {
                    // Box projection from world position: textures tile at a constant size
                    // whatever the mesh UVs (the imported map has stretched or missing UVs).
                    float3 n = abs(input.normalWS);
                    float3 p = input.positionWS * _WorldUVScale;
                    uv = (n.x > n.y && n.x > n.z) ? p.zy : ((n.y > n.z) ? p.xz : p.xy);
                    uv += _WorldUVOffset.xy;
                }
                half4 tex = SAMPLE_TEXTURE2D(_BaseMap, sampler_point_repeat, uv);
                half4 color = tex * _BaseColor;

                if (_AlphaClip > 0.5)
                    clip(color.a - _Cutoff);

                // Materials are double-sided: flip the normal on back faces, otherwise walls whose
                // normals point outward stay black under the lights.
                half3 normalWS = normalize(input.normalWS) * (IS_FRONT_VFACE(facing, 1.0, -1.0));
                color.rgb *= input.lighting + SceneLights(input.positionWS, normalWS);
                color.rgb = MixFog(color.rgb, input.fogCoord);

                // Reduce color depth with ordered dithering.
                uint2 p = (uint2)input.positionCS.xy % 4;
                half dither = (bayer4[p.y * 4 + p.x] - 0.5) * _DitherStrength;
                half levels = max(_ColorDepth, 2.0);
                color.rgb = floor(color.rgb * levels + dither + 0.5) / levels;

                return half4(saturate(color.rgb), 1.0);
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask R
            Cull [_Cull]
            Offset [_OffsetFactor], [_OffsetUnits]

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _BaseColor;
                float4 _SnapResolution;
                half _AffineAmount;
                half _ColorDepth;
                half _DitherStrength;
                half _AmbientBoost;
                half _Cutoff;
                half _AlphaClip;
                half _WorldUV;
                float _WorldUVScale;
                float4 _WorldUVOffset;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                float4 positionCS = TransformObjectToHClip(input.positionOS.xyz);
                if (_SnapResolution.x > 0.5)
                {
                    float2 res = _SnapResolution.xy;
                    float2 ndc = positionCS.xy / positionCS.w;
                    ndc = round(ndc * res * 0.5) / (res * 0.5);
                    positionCS.xy = ndc * positionCS.w;
                }
                output.positionCS = positionCS;
                return output;
            }

            half frag(Varyings input) : SV_Target
            {
                return input.positionCS.z;
            }
            ENDHLSL
        }
    }
    FallBack Off
}
