Shader "Shift/商店二分"
{
    Properties
    {
        _BaseColor ("固有色（无贴图时）", Color) = (1, 1, 1, 1)
        [MainTexture] _BaseMap ("固有色贴图", 2D) = "white" {}
        [ToggleUI] _HasBaseMap ("使用固有色贴图（仅单文件模式手动设置）", Float) = 0
        _ShadowTint ("阴影乘色", Color) = (0.25, 0.25, 0.25, 1)
        _ShadowTintDepth ("阴影乘色深度", Range(0, 1)) = 1
        _LightColorGain ("灯光颜色增益", Float) = 1
        _LightIntensityWeight ("灯光强度参与亮区权重", Range(0, 1)) = 0
        _OutlineColor ("描边色", Color) = (0.04, 0.04, 0.04, 1)
        [Toggle] _OutlineEnabled ("启用描边", Float) = 1
        _OutlineWidthScale ("材质描边粗细倍率", Range(0, 2)) = 1
        _OutlineFeatherScale ("材质描边边缘平滑倍率", Range(0, 2)) = 1
        _TessFactor ("细分级别上限", Range(1, 32)) = 8
        _TessEdgePixels ("细分目标边长（像素，0 为固定级别）", Float) = 0
        _Smoothness ("几何平滑强度", Range(0, 1)) = 0.5
        _IntensityThreshold ("灯光强度门槛", Float) = 0.5
        _DirectionThreshold ("入射方向门槛", Range(0, 1)) = 0.5
        _FadeDistance ("亮暗差衰减距离（米）", Float) = 100
        [HideInInspector] _MainTex ("相机输入画面", 2D) = "white" {}
        [HideInInspector] _ShopObjectId ("描边对象编号", Float) = 0
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" "DisableBatching"="True" }
        Cull Back
        ZWrite On
        ZTest LEqual

        CGINCLUDE
        #include "UnityCG.cginc"

        float _TessFactor;
        float _TessEdgePixels;
        float _Smoothness;
        float4x4 _ShopTessVP;
        float4 _ShopTessCameraSize;
        float4 _BaseMap_ST;

        struct ShopVertex
        {
            float4 positionOS : POSITION;
            float3 normalOS : NORMAL;
            float2 uv : TEXCOORD0;
            float3 edgeNormalOS : TEXCOORD2;
            float2 edgeData : TEXCOORD3;
        };

        struct ShopControlPoint
        {
            float3 positionOS : INTERNALTESSPOS;
            float3 normalOS : NORMAL;
            float3 edgeNormalOS : TEXCOORD0;
            float2 uv : TEXCOORD1;
            float hasEdgeData : TEXCOORD2;
        };

        struct ShopTessFactors
        {
            float edge[3] : SV_TessFactor;
            float inside : SV_InsideTessFactor;
        };

        struct ShopSurfacePoint
        {
            float3 positionOS;
            float3 normalOS;
        };

        struct ShopVarying
        {
            float4 positionCS : SV_POSITION;
            float3 positionWS : TEXCOORD0;
            float3 normalWS : TEXCOORD1;
            float eyeDepth : TEXCOORD2;
            float4 screenPos : TEXCOORD3;
            float2 uv : TEXCOORD4;
        };

        ShopControlPoint ShopVert(ShopVertex input)
        {
            ShopControlPoint surfacePoint;
            surfacePoint.positionOS = input.positionOS.xyz;
            surfacePoint.normalOS = input.normalOS;
            surfacePoint.uv = input.uv;
            surfacePoint.edgeNormalOS = input.edgeNormalOS;
            surfacePoint.hasEdgeData = all(input.edgeData == float2(1.0, 0.0)) ? 1.0 : 0.0;
            return surfacePoint;
        }

        ShopControlPoint ShopPatchVertex(InputPatch<ShopControlPoint, 3> patch, uint index)
        {
            ShopControlPoint controlPoint = patch[index];
            // 预处理网格的三个顶点都带相同的 UV4 标记。普通模型缺少通道时，
            // Unity 可能复用 UV0；单个纹理坐标碰巧为 (1,0) 时也应保留原有形状。
            if (patch[0].hasEdgeData * patch[1].hasEdgeData * patch[2].hasEdgeData == 0.0)
                controlPoint.edgeNormalOS = 0.0;
            return controlPoint;
        }

        float4 ShopReferenceClip(float3 positionOS)
        {
            float4 positionWS = mul(unity_ObjectToWorld, float4(positionOS, 1.0));
            return mul(_ShopTessVP, positionWS);
        }

        float ShopEdgeFactor(float4 first, float4 second, float limit)
        {
            if (_ShopTessCameraSize.w > 0.0)
            {
                // 透视投影的 w 是眼深。跨越近面时只量可见边，按近端、远端固定顺序求交。
                float nearPlane = max(_ShopTessCameraSize.z, 0.0001);
                if (max(first.w, second.w) <= nearPlane)
                    return 1.0;
                if (min(first.w, second.w) < nearPlane)
                {
                    if (first.w > second.w)
                    {
                        float4 swap = first;
                        first = second;
                        second = swap;
                    }
                    first = lerp(first, second, (nearPlane - first.w) / (second.w - first.w));
                    first.w = nearPlane;
                }
            }
            // 共边只读取两个端点，翻转端点顺序不改变结果。
            float2 delta = (first.xy / first.w - second.xy / second.w) * (_ShopTessCameraSize.xy * 0.5);
            return clamp(ceil(length(delta) / max(_TessEdgePixels, 0.01)), 1.0, limit);
        }

        ShopTessFactors ShopPatchFactors(InputPatch<ShopControlPoint, 3> patch)
        {
            float limit = round(clamp(_TessFactor, 1.0, 32.0));
            ShopTessFactors factors;
            if (_TessEdgePixels <= 0.0 || _ShopTessCameraSize.w == 0.0)
            {
                factors.edge[0] = limit;
                factors.edge[1] = limit;
                factors.edge[2] = limit;
                factors.inside = limit;
                return factors;
            }

            // 所有 Pass 使用同一主相机。D3D 三角形的边索引与对侧顶点索引一致。
            float4 a = ShopReferenceClip(patch[0].positionOS);
            float4 b = ShopReferenceClip(patch[1].positionOS);
            float4 c = ShopReferenceClip(patch[2].positionOS);
            factors.edge[0] = ShopEdgeFactor(b, c, limit);
            factors.edge[1] = ShopEdgeFactor(c, a, limit);
            factors.edge[2] = ShopEdgeFactor(a, b, limit);
            factors.inside = ceil((factors.edge[0] + factors.edge[1] + factors.edge[2]) / 3.0);
            return factors;
        }

        [domain("tri")]
        [partitioning("integer")]
        [outputtopology("triangle_cw")]
        [patchconstantfunc("ShopPatchFactors")]
        [outputcontrolpoints(3)]
        ShopControlPoint ShopHull(InputPatch<ShopControlPoint, 3> patch, uint index : SV_OutputControlPointID)
        {
            return ShopPatchVertex(patch, index);
        }

        float3 ShopTangentOffset(float3 delta, float3 normal)
        {
            // 零向量表示该端点固定。烘焙网格的其余控制法线为单位向量。
            return normal * dot(delta, normal);
        }

        float3 ShopEdgeOffset(ShopControlPoint first, ShopControlPoint second)
        {
            float3 delta = second.positionOS - first.positionOS;
            return ShopTangentOffset(delta, second.edgeNormalOS) - ShopTangentOffset(delta, first.edgeNormalOS);
        }

        ShopSurfacePoint ShopEvaluate(const OutputPatch<ShopControlPoint, 3> patch, float3 barycentric)
        {
            ShopSurfacePoint surfacePoint;
            float3 linearPosition = patch[0].positionOS * barycentric.x
                          + patch[1].positionOS * barycentric.y
                          + patch[2].positionOS * barycentric.z;

            // 将 Phong 投影写成三条边的二次项。每个共边位置只依赖该边的两个端点。
            float3 offset = ShopEdgeOffset(patch[0], patch[1]) * (barycentric.x * barycentric.y)
                          + ShopEdgeOffset(patch[1], patch[2]) * (barycentric.y * barycentric.z)
                          + ShopEdgeOffset(patch[2], patch[0]) * (barycentric.z * barycentric.x);
            surfacePoint.positionOS = linearPosition + offset * saturate(_Smoothness);

            // 分侧的美术法线控制着色，几何连续不抹平硬边。
            surfacePoint.normalOS = patch[0].normalOS * barycentric.x
                           + patch[1].normalOS * barycentric.y
                           + patch[2].normalOS * barycentric.z;
            return surfacePoint;
        }

        [domain("tri")]
        ShopVarying ShopDomain(ShopTessFactors factors, const OutputPatch<ShopControlPoint, 3> patch,
                               float3 barycentric : SV_DomainLocation)
        {
            ShopSurfacePoint surfacePoint = ShopEvaluate(patch, barycentric);
            ShopVarying output;
            output.positionCS = UnityObjectToClipPos(float4(surfacePoint.positionOS, 1.0));
            output.screenPos = ComputeScreenPos(output.positionCS);
            output.positionWS = mul(unity_ObjectToWorld, float4(surfacePoint.positionOS, 1.0)).xyz;
            output.normalWS = UnityObjectToWorldNormal(surfacePoint.normalOS);
            output.eyeDepth = -mul(UNITY_MATRIX_V, float4(output.positionWS, 1.0)).z;
            // 沿原三角形插值 UV0，细分只改变几何形状，仍保留原有的纹理接缝和缩放偏移。
            output.uv = patch[0].uv * barycentric.x + patch[1].uv * barycentric.y + patch[2].uv * barycentric.z;
            return output;
        }

        #define SHOP_MAX_LIGHTS 8
        #define SHOP_SHADOW_TILES 48

        float4 _BaseColor;
        float4 _ShadowTint;
        float _ShadowTintDepth;
        float _LightColorGain;
        float _LightIntensityWeight;
        float _IntensityThreshold;
        float _DirectionThreshold;
        float _FadeDistance;
        float _ShopLightingManaged;

        sampler2D _BaseMap;
        float _HasBaseMap;

        float3 ShopBaseColor(float2 uv)
        {
            // 由材质实际绑定的贴图决定来源；白色、黑色和 1×1 贴图都是有效固有色。
            if (_HasBaseMap > 0.5)
                return tex2D(_BaseMap, TRANSFORM_TEX(uv, _BaseMap)).rgb;
            return _BaseColor.rgb;
        }

        float3 ShopDarkColor(float3 baseColor)
        {
            return baseColor * lerp(1.0, _ShadowTint.rgb, saturate(_ShadowTintDepth));
        }

        float ShopDepthFade(float eyeDepth)
        {
            return saturate(1.0 - eyeDepth / max(_FadeDistance, 0.0001));
        }

        float ShopLightGain(float incident)
        {
            return lerp(1.0, max(0.0, incident), saturate(_LightIntensityWeight));
        }

        // Built-in 备用光照使用同一套光强门槛与方向门槛。
        float ShopBuiltInCoverage(float3 positionWS, float3 normalWS, float3 lightColor, float attenuation)
        {
            float3 toLight = UnityWorldSpaceLightDir(positionWS);
            float cosine = dot(normalWS, normalize(toLight));
            float incident = max(lightColor.r, max(lightColor.g, lightColor.b)) * attenuation;
            float threshold = max(0.0, _IntensityThreshold);
            return step(threshold, incident) * step(threshold * _DirectionThreshold, incident * saturate(cosine))
                * (incident > 0.0 && cosine > 0.0 ? 1.0 : 0.0);
        }

        int _ShopLightCount;
        float4 _ShopLightPositions[SHOP_MAX_LIGHTS];
        float4 _ShopLightDirections[SHOP_MAX_LIGHTS];
        float4 _ShopLightOptions[SHOP_MAX_LIGHTS];
        float4 _ShopLightColors[SHOP_MAX_LIGHTS];
        float4x4 _ShopShadowMatrices[SHOP_SHADOW_TILES];
        float4 _ShopShadowDepthPlanes[SHOP_SHADOW_TILES];
        float4 _ShopShadowTiles[SHOP_SHADOW_TILES];
        UNITY_DECLARE_TEX2DARRAY(_ShopShadowAtlas);
        float _ShopShadowPixel;

        // 点光源按主轴选择朝向表面的一面，顺序与运行时的投影面一致。
        int ShopPointFace(float3 fromLight)
        {
            float3 axis = abs(fromLight);
            if (axis.x >= axis.y && axis.x >= axis.z)
                return fromLight.x >= 0.0 ? 0 : 1;
            if (axis.y >= axis.z)
                return fromLight.y >= 0.0 ? 2 : 3;
            return fromLight.z >= 0.0 ? 4 : 5;
        }

        float ShopFetchShadow(float2 localUv, float2 texelOffset, int layer, float depth, float2 depthSlope)
        {
            float pixel = _ShopShadowPixel;
            float2 uv = clamp(localUv + texelOffset * pixel, 0.5 * pixel, 1.0 - 0.5 * pixel);
            // 比较的是实际采样像素中心在接收面上的深度，避开斜面自遮挡。
            uv = (floor(uv / pixel) + 0.5) * pixel;
            depth += dot(uv - localUv, depthSlope);
            #if UNITY_UV_STARTS_AT_TOP
                uv.y = 1.0 - uv.y;
            #endif
            float stored = UNITY_SAMPLE_TEX2DARRAY_LOD(_ShopShadowAtlas, float3(uv, layer), 0.0).r;
            return step(depth, stored);
        }

        float ShopVisibility(int index, float3 positionWS, float3 normalWS, float cosine, float2 sampleOffset)
        {
            float4 tile = _ShopShadowTiles[index];
            if (tile.x == 0.0)
                return 1.0;

            float4 world = float4(positionWS, 1.0);
            float4 projection = mul(_ShopShadowMatrices[index], world);
            float2 uv = projection.xy / max(projection.w, 1e-6) * 0.5 + 0.5;
            float depth = dot(world, _ShopShadowDepthPlanes[index]);
            if (any(uv < 0.0) || any(uv > 1.0) || depth <= 0.0 || depth >= 1.0)
                return 1.0;

            // 同时补偿深度量化和掠射角；软阴影在当前灯的投影面内取样。
            depth -= tile.y * (1.0 + tile.z * (1.0 - saturate(cosine)));
            // 在接收面上求深度对阴影纹理坐标的斜率，校正硬阴影和软阴影采样。
            float3 helper = abs(normalWS.y) < 0.9 ? float3(0, 1, 0) : float3(1, 0, 0);
            float3 tangent = normalize(cross(helper, normalWS));
            float3 bitangent = cross(normalWS, tangent);
            float4 clipT = mul(_ShopShadowMatrices[index], float4(tangent, 0.0));
            float4 clipB = mul(_ShopShadowMatrices[index], float4(bitangent, 0.0));
            float2 ndc = uv * 2.0 - 1.0;
            float2 duvT = (clipT.xy - ndc * clipT.w) * (0.5 / projection.w);
            float2 duvB = (clipB.xy - ndc * clipB.w) * (0.5 / projection.w);
            float determinant = duvT.x * duvB.y - duvT.y * duvB.x;
            float dT = dot(tangent, _ShopShadowDepthPlanes[index].xyz);
            float dB = dot(bitangent, _ShopShadowDepthPlanes[index].xyz);
            float2 depthSlope = abs(determinant) > 1e-7
                ? float2(dT * duvB.y - dB * duvT.y, dB * duvT.x - dT * duvB.x) / determinant : 0.0;
            depthSlope = clamp(depthSlope, -2.0, 2.0);
            // 软阴影按面板宽度拉开四个采样点，每点仍只判断通过或遮挡。硬阴影的宽度为零。
            float2 filterOffset = tile.w * sampleOffset;
            return ShopFetchShadow(uv, filterOffset, index, depth, depthSlope);
        }

        float ShopIncident(int lightIndex, float3 positionWS, float3 normalWS, out float cosine)
        {
            float4 p = _ShopLightPositions[lightIndex];
            float4 direction = _ShopLightDirections[lightIndex];
            float4 options = _ShopLightOptions[lightIndex];
            float3 fromLight = positionWS - p.xyz;
            float distanceSq = dot(fromLight, fromLight);
            float3 towardsLight = p.w < 0.5 ? -direction.xyz : -fromLight * rsqrt(max(distanceSq, 1e-20));
            cosine = dot(normalWS, towardsLight);

            // 距离曲线在灯源处为一，到作用半径时平滑降为零。
            float rangeFade = saturate(1.0 - sqrt(distanceSq) / direction.w);
            float attenuation = p.w < 0.5 ? 1.0 : rangeFade * rangeFade;
            if (p.w > 0.5 && p.w < 1.5)
            {
                float cone = dot(-towardsLight, direction.xyz);
                attenuation *= saturate((cone - options.y) / max(1.0 - options.y, 1e-6));
            }
            return options.x * attenuation;
        }

        void ShopLightCoverage(float3 positionWS, float3 normalWS, float intensityThreshold, float directionThreshold,
            bool useRoundedMask, out float4 lights0, out float4 lights1, out float unionCoverage,
            out float4 incidents0, out float4 incidents1)
        {
            // 每盏灯都在相同的四点上判断；灯光各自累加，暗色只占没有任何灯通过的采样点。
            const float4 sampleX = float4(-0.375, 0.125, 0.375, -0.125);
            const float4 sampleY = float4(-0.125, -0.375, 0.125, 0.375);
            const float4 sampleWeights = float4(0.25, 0.25, 0.25, 0.25);
            float3 dxPosition = ddx(positionWS);
            float3 dyPosition = ddy(positionWS);
            float4 covered = 0.0;
            lights0 = 0.0;
            lights1 = 0.0;
            incidents0 = 0.0;
            incidents1 = 0.0;
            [loop]
            for (int lightIndex = 0; lightIndex < _ShopLightCount; lightIndex++)
            {
                float4 lane0 = float4(lightIndex == int4(0, 1, 2, 3));
                float4 lane1 = float4(lightIndex == int4(4, 5, 6, 7));
                float cosine;
                float incident = ShopIncident(lightIndex, positionWS, normalWS, cosine);
                incidents0 += lane0 * incident;
                incidents1 += lane1 * incident;
                // 所有像素先计算导数，避免在分支中读取不完整的像素组。
                float4 incidentSamples = incident + ddx(incident) * sampleX + ddy(incident) * sampleY;
                float4 cosineSamples = cosine + ddx(cosine) * sampleX + ddy(cosine) * sampleY;
                [branch]
                if (!useRoundedMask)
                {
                    float4 p = _ShopLightPositions[lightIndex];
                    float4 options = _ShopLightOptions[lightIndex];
                    float4 passed = step(intensityThreshold, incidentSamples)
                        * step(intensityThreshold * directionThreshold, incidentSamples * saturate(cosineSamples));
                    passed *= float4(incidentSamples > 0.0) * float4(cosineSamples > 0.0);
                    if (options.w > 0.0)
                    {
                        [unroll]
                        for (int sampleIndex = 0; sampleIndex < 4; sampleIndex++)
                        {
                            if (passed[sampleIndex] > 0.0)
                            {
                                float2 offset = float2(sampleX[sampleIndex], sampleY[sampleIndex]);
                                float3 samplePosition = positionWS + dxPosition * offset.x + dyPosition * offset.y;
                                int face = p.w > 1.5 ? ShopPointFace(samplePosition - p.xyz) : 0;
                                passed[sampleIndex] *= ShopVisibility((int)options.z + face,
                                    samplePosition, normalWS, cosineSamples[sampleIndex], offset);
                            }
                        }
                    }
                    float coverage = dot(passed, sampleWeights);
                    lights0 += lane0 * coverage;
                    lights1 += lane1 * coverage;
                    covered = max(covered, passed);
                }
            }
            unionCoverage = dot(covered, sampleWeights);
        }

        sampler2D _MainTex, _ShopSceneColor, _Ink, _Surface, _EyeDepth, _Seeds;
        UNITY_DECLARE_DEPTH_TEXTURE(_CameraDepthTexture);
        float4 _MainTex_TexelSize, _Pixel;
        float _Width, _Feather, _StrokeReach, _CreaseCos;
        float4 SurfaceAt(float2 uv)
        {
            return tex2Dlod(_Surface, float4(uv, 0, 0));
        }

        float DepthAt(float2 uv)
        {
            return tex2Dlod(_EyeDepth, float4(uv, 0, 0)).r;
        }

        float SurfaceSlope(float2 uv, float2 step, float id, float depth)
        {
            float2 inside = uv + step * _Pixel.xy;
            if (SurfaceAt(inside).w != id)
                return 0;
            return abs(DepthAt(inside) - depth);
        }

        // 用两侧各自表面的每像素深度变化比较边界，允许接触点落在两个像素中心之间。
        float ContactTolerance(float2 a, float2 b, float stepCount, float za, float zb, float ida, float idb)
        {
            float scale = min(za, zb);
            float absolute = max(0.001, scale * 0.00015);
            float2 towards = sign(b - a);
            float slope = SurfaceSlope(a, -towards, ida, za) + SurfaceSlope(b, towards, idb, zb);
            return absolute + min(slope * stepCount, scale * 0.04);
        }

        // 接触边由启用描边的一方拥有。分离的物体仍由靠近镜头的一方遮挡。
        float Edge(float2 uv, float2 delta, float z, float4 face)
        {
            float2 at = uv + delta * _Pixel.xy;
            float nz = DepthAt(at);
            if (nz <= 0) return 1;
            float4 other = SurfaceAt(at);
            if (face.w != other.w)
            {
                float tie = ContactTolerance(uv, at, 1, z, nz, face.w, other.w);
                if (abs(z - nz) < tie)
                    return face.w < other.w || tex2Dlod(_Ink, float4(at, 0, 0)).a == 0;
                return z < nz;
            }
            if (nz - z > max(0.06, z * 0.04)) return 1;
            return dot(face.xyz, other.xyz) < _CreaseCos && z <= nz;
        }

        Texture2D<float4> _ShopCornerRaw0;
        Texture2D<float4> _ShopCornerRaw1;
        Texture2D<float2> _ShopCornerUnionAndId;
        Texture2D<float> _ShopCornerGuide;
        Texture2D<float4> _ShopCornerHorizontal0;
        Texture2D<float4> _ShopCornerHorizontal1;
        float4 _ShopCornerSettings;
        float _ShopCornerCutoff;
        float4 _ShopCornerKernel;

        struct Masks
        {
            float4 first : SV_Target0;
            float4 second : SV_Target1;
        };

        struct RoundedMasks
        {
            float4 first : SV_Target0;
            float4 second : SV_Target1;
            float coverage : SV_Target2;
        };

        float4 FullScreen(uint vertex : SV_VertexID) : SV_POSITION
        {
            float2 corner = float2((vertex << 1) & 2, vertex & 2);
            return float4(corner * 2.0 - 1.0, 0, 1);
        }

        Masks Gaussian(int2 pixel, int2 axis, float receiver, float depth, bool vertical)
        {
            int2 limit = (int2)_ShopCornerSettings.xy - 1;
            int radius = (int)_ShopCornerSettings.z;
            float invSigma = rcp(_ShopCornerKernel.x * radius);
            Masks center;
            center.first = vertical ? _ShopCornerHorizontal0.Load(int3(pixel, 0)) : _ShopCornerRaw0.Load(int3(pixel, 0));
            center.second = vertical ? _ShopCornerHorizontal1.Load(int3(pixel, 0)) : _ShopCornerRaw1.Load(int3(pixel, 0));

            Masks sum = center;
            float totalWeight = 1.0;
            [loop]
            for (int offset = -radius; offset <= radius; ++offset)
            {
                if (offset == 0)
                    continue;

                int2 tap = clamp(pixel + offset * axis, int2(0, 0), limit);
                float tapReceiver = _ShopCornerUnionAndId.Load(int3(tap, 0)).y;
                float tapDepth = _ShopCornerGuide.Load(int3(tap, 0));
                float nearLimit = (_ShopCornerKernel.y + depth * _ShopCornerKernel.z) * abs(offset);
                bool sameSurface = tapReceiver == receiver && abs(tapDepth - depth) <= nearLimit;
                float4 first = center.first;
                float4 second = center.second;
                if (sameSurface)
                {
                    first = vertical ? _ShopCornerHorizontal0.Load(int3(tap, 0)) : _ShopCornerRaw0.Load(int3(tap, 0));
                    second = vertical ? _ShopCornerHorizontal1.Load(int3(tap, 0)) : _ShopCornerRaw1.Load(int3(tap, 0));
                }

                // 镜头外与其他表面的采样延拓为中心像素的覆盖率，避免轮廓带入邻物的亮暗。
                float distance = offset * invSigma;
                float weight = exp(-0.5 * distance * distance);
                sum.first += first * weight;
                sum.second += second * weight;
                totalWeight += weight;
            }

            sum.first /= totalWeight;
            sum.second /= totalWeight;
            return sum;
        }

        Masks FilterHorizontal(float4 position : SV_POSITION)
        {
            int2 pixel = int2(position.xy);
            float receiver = _ShopCornerUnionAndId.Load(int3(pixel, 0)).y;
            if (receiver == 0.0)
            {
                Masks empty;
                empty.first = empty.second = 0;
                return empty;
            }

            float depth = _ShopCornerGuide.Load(int3(pixel, 0));
            return Gaussian(pixel, int2(1, 0), receiver, depth, false);
        }

        float4 Threshold(float4 blurred, float4 original)
        {
            // 保留未被邻域改变的原始覆盖率，尤其是平直边缘上的亚像素抗锯齿。
            float halfWidth = min(_ShopCornerSettings.w * 0.5, min(_ShopCornerCutoff, 1.0 - _ShopCornerCutoff));
            if (halfWidth <= 0.0)
                return step(_ShopCornerCutoff, blurred);

            float4 rounded = smoothstep(_ShopCornerCutoff - halfWidth, _ShopCornerCutoff + halfWidth, blurred);
            return lerp(original, rounded, step(0.0005, abs(blurred - original)));
        }

        float Largest(float4 first, float4 second)
        {
            float4 both = max(first, second);
            return max(max(both.x, both.y), max(both.z, both.w));
        }

        RoundedMasks FilterVertical(float4 position : SV_POSITION)
        {
            RoundedMasks result;
            int2 pixel = int2(position.xy);
            float2 unionAndId = _ShopCornerUnionAndId.Load(int3(pixel, 0));
            if (unionAndId.y == 0.0)
            {
                result.first = result.second = 0;
                result.coverage = 0;
                return result;
            }

            float depth = _ShopCornerGuide.Load(int3(pixel, 0));
            Masks blurred = Gaussian(pixel, int2(0, 1), unionAndId.y, depth, true);
            float4 original0 = _ShopCornerRaw0.Load(int3(pixel, 0));
            float4 original1 = _ShopCornerRaw1.Load(int3(pixel, 0));
            result.first = Threshold(blurred.first, original0);
            result.second = Threshold(blurred.second, original1);

            // 优先保留原始逐样本并集。当某盏灯的覆盖率变动时，让它贡献相应的增减。
            float originalMax = Largest(original0, original1);
            float roundedMax = Largest(result.first, result.second);
            float roundedSum = dot(result.first + result.second, float4(1.0, 1.0, 1.0, 1.0));
            result.coverage = clamp(unionAndId.x + roundedMax - originalMax, roundedMax, saturate(roundedSum));
            return result;
        }
        ENDCG

        Pass
        {
            // 各灯亮色在线性空间相加；未被任何灯照亮的面积只混入一次暗色。
            Name "SHOP_SURFACE"
            Tags { "LightMode"="ForwardBase" }

            CGPROGRAM
            #pragma only_renderers d3d11
            #pragma target 5.0
            #pragma vertex ShopVert
            #pragma hull ShopHull
            #pragma domain ShopDomain
            #pragma fragment ShopFragment
            #pragma multi_compile_fwdbase nolightmap nodirlightmap nodynlightmap novertexlight

            #include "Lighting.cginc"
            #include "AutoLight.cginc"


            float _ShopRoundedLightMaskEnabled;
            Texture2D<float4> _ShopRoundedLightMask0;
            Texture2D<float4> _ShopRoundedLightMask1;
            Texture2D<float4> _ShopRoundedLightUnion;
            Texture2D<float> _ShopRoundedLightGuide;

            float4 ShopFragment(ShopVarying input) : SV_Target
            {
                float3 normal = normalize(input.normalWS);
                float3 baseColor = ShopBaseColor(input.uv);
                float3 dark = ShopDarkColor(baseColor);
                float fade = ShopDepthFade(input.eyeDepth);
                // 单独导入 Shader 时，使用 Built-in 主方向灯及其屏幕空间实时阴影。
                if (_ShopLightingManaged < 0.5)
                {
                    float3 lightColor = _LightColor0.rgb;
                    float visibility = UnityComputeForwardShadows(float2(0, 0), input.positionWS, input.screenPos);
                    float coverage = ShopBuiltInCoverage(input.positionWS, normal, lightColor, 1.0) * visibility;
                    float incident = max(lightColor.r, max(lightColor.g, lightColor.b));
                    float3 lamp = baseColor * lightColor * ShopLightGain(incident) * max(0.0, _LightColorGain);
                    return float4(lerp(dark, lamp, coverage * fade), 1.0);
                }

                float4 lights0, lights1;
                float unionCoverage;
                float4 incidents0, incidents1;
                int3 pixel = int3(int2(input.positionCS.xy), 0);
                float depthTolerance = max(0.0001, 0.002 * fwidth(input.eyeDepth) + 0.00001 * input.eyeDepth);
                float guideDepth = 0.0;
                if (_ShopRoundedLightMaskEnabled > 0.5)
                    guideDepth = _ShopRoundedLightGuide.Load(pixel);

                bool useRoundedMask = guideDepth > 0.0 && abs(guideDepth - input.eyeDepth) <= depthTolerance;
                ShopLightCoverage(input.positionWS, normal, max(0.0, _IntensityThreshold),
                    _DirectionThreshold, useRoundedMask, lights0, lights1, unionCoverage, incidents0, incidents1);
                if (useRoundedMask)
                {
                    lights0 = _ShopRoundedLightMask0.Load(pixel);
                    lights1 = _ShopRoundedLightMask1.Load(pixel);
                    unionCoverage = _ShopRoundedLightUnion.Load(pixel).r;
                }

                float3 lampSum = 0.0;
                [loop]
                for (int lightIndex = 0; lightIndex < _ShopLightCount; lightIndex++)
                {
                    int channel = lightIndex & 3;
                    float coverage = lightIndex < 4 ? lights0[channel] : lights1[channel];
                    float incident = lightIndex < 4 ? incidents0[channel] : incidents1[channel];
                    float gain = ShopLightGain(incident);
                    lampSum += _ShopLightColors[lightIndex].rgb * (coverage * gain);
                }

                float3 lit = baseColor * lampSum * max(0.0, _LightColorGain);
                return float4(lerp(dark, lit + dark * (1.0 - unionCoverage), fade), 1.0);
            }
            ENDCG
        }

        Pass
        {
            // Built-in 把逐像素点光源放在 ForwardAdd；托管场景已经在主 Pass 收集这些灯。
            Name "SHOP_BUILTIN_POINT"
            Tags { "LightMode"="ForwardAdd" }
            Blend SrcAlpha OneMinusSrcAlpha
            ColorMask RGB
            ZWrite Off

            CGPROGRAM
            #pragma only_renderers d3d11
            #pragma target 5.0
            #pragma vertex ShopVert
            #pragma hull ShopPointHull
            #pragma domain ShopDomain
            #pragma fragment ShopBuiltInPointFragment
            #pragma multi_compile_fwdadd_fullshadows

            #include "Lighting.cginc"
            #include "AutoLight.cginc"

            ShopTessFactors ShopPointPatchFactors(InputPatch<ShopControlPoint, 3> patch)
            {
            #if defined(POINT) || defined(POINT_COOKIE)
                if (_ShopLightingManaged < 0.5)
                    return ShopPatchFactors(patch);
            #endif
                // 将外侧细分因子置零，托管光照和非点光跳过这个备用 Pass 的几何绘制。
                ShopTessFactors culled;
                culled.edge[0] = culled.edge[1] = culled.edge[2] = culled.inside = 0.0;
                return culled;
            }

            [domain("tri")]
            [partitioning("integer")]
            [outputtopology("triangle_cw")]
            [patchconstantfunc("ShopPointPatchFactors")]
            [outputcontrolpoints(3)]
            ShopControlPoint ShopPointHull(InputPatch<ShopControlPoint, 3> patch, uint index : SV_OutputControlPointID)
            {
                return ShopPatchVertex(patch, index);
            }

            float4 ShopBuiltInPointFragment(ShopVarying input) : SV_Target
            {
            #if defined(POINT) || defined(POINT_COOKIE)
                if (_ShopLightingManaged > 0.5)
                    return 0.0;

                float3 lightCoord = mul(unity_WorldToLight, float4(input.positionWS, 1.0)).xyz;
                float distanceSquared = dot(lightCoord, lightCoord);
            #if defined(POINT_COOKIE)
                float attenuation = tex2D(_LightTextureB0, distanceSquared.rr).r * texCUBE(_LightTexture0, lightCoord).a;
            #else
                float attenuation = tex2D(_LightTexture0, distanceSquared.rr).r;
            #endif
                float3 lightColor = _LightColor0.rgb;
                float visibility = UnityComputeForwardShadows(float2(0, 0), input.positionWS, input.screenPos);
                float coverage = ShopBuiltInCoverage(input.positionWS, normalize(input.normalWS), lightColor, attenuation);
                float incident = max(lightColor.r, max(lightColor.g, lightColor.b)) * attenuation;
                float3 lamp = ShopBaseColor(input.uv) * lightColor * ShopLightGain(incident) * max(0.0, _LightColorGain);
                return float4(lamp, coverage * visibility * ShopDepthFade(input.eyeDepth));
            #else
                return 0.0;
            #endif
            }
            ENDCG
        }

        Pass
        {
            // 屏幕空间圆角读取逐灯原始覆盖率、精确暗区并集和接收面信息。
            Name "SHOP_LIGHT_MASK"
            Tags { "LightMode"="ShopLightMask" }

            CGPROGRAM
            #pragma only_renderers d3d11
            #pragma target 5.0
            #pragma vertex ShopVert
            #pragma hull ShopHull
            #pragma domain ShopDomain
            #pragma fragment ShopMaskFragment


            float _ShopCornerReceiverId;

            struct ShopMaskOutput
            {
                float4 lights0 : SV_Target0;
                float4 lights1 : SV_Target1;
                float4 unionAndId : SV_Target2;
                float guide : SV_Target3;
            };

            ShopMaskOutput ShopMaskFragment(ShopVarying input)
            {
                ShopMaskOutput output;
                float unionCoverage;
                float4 incidents0, incidents1;
                float3 normal = normalize(input.normalWS);
                ShopLightCoverage(input.positionWS, normal, max(0.0, _IntensityThreshold),
                    _DirectionThreshold, false, output.lights0, output.lights1, unionCoverage, incidents0, incidents1);
                output.unionAndId = float4(unionCoverage, _ShopCornerReceiverId, 0.0, 0.0);
                output.guide = input.eyeDepth;
                return output;
            }
            ENDCG
        }

        Pass
        {
            // 逐灯命令缓冲绘制线性灯光深度。
            Name "SHOP_SHADOW"
            Tags { "LightMode"="ShopShadow" }

            CGPROGRAM
            #pragma only_renderers d3d11
            #pragma target 5.0
            #pragma vertex ShopVert
            #pragma hull ShopHull
            #pragma domain ShopShadowDomain
            #pragma fragment ShopShadowFragment


            float4x4 _ShopShadowVP;
            float4 _ShopShadowDepthParams;

            struct ShopShadowVarying
            {
                float4 positionCS : SV_POSITION;
                float depth : TEXCOORD0;
            };

            [domain("tri")]
            ShopShadowVarying ShopShadowDomain(ShopTessFactors factors,
                const OutputPatch<ShopControlPoint, 3> patch, float3 barycentric : SV_DomainLocation)
            {
                ShopSurfacePoint surfacePoint = ShopEvaluate(patch, barycentric);
                float4 worldPosition = mul(unity_ObjectToWorld, float4(surfacePoint.positionOS, 1.0));
                ShopShadowVarying output;
                output.positionCS = mul(_ShopShadowVP, worldPosition);
                output.depth = dot(worldPosition, _ShopShadowDepthParams);
                return output;
            }

            float4 ShopShadowFragment(ShopShadowVarying input) : SV_Target
            {
                return float4(input.depth, 0, 0, 1);
            }
            ENDCG
        }

        Pass
        {
            // 不描边的表面也传递深度和法线，供屏幕空间检测处理遮挡。
            Name "SHOP_OUTLINE_DATA"
            Tags { "LightMode"="ShopOutlineData" }

            CGPROGRAM
            #pragma only_renderers d3d11
            #pragma target 5.0
            #pragma vertex ShopVert
            #pragma hull ShopHull
            #pragma domain ShopDomain
            #pragma fragment ShopOutlineFragment


            float4 _OutlineColor;
            float _OutlineEnabled;
            float _OutlineWidthScale;
            float _OutlineFeatherScale;
            float _ShopObjectId;

            struct ShopOutlineOutput
            {
                float4 colorAndEnabled : SV_Target0;
                float4 normalAndId : SV_Target1;
                float2 eyeDepth : SV_Target2;
            };

            ShopOutlineOutput ShopOutlineFragment(ShopVarying input)
            {
                ShopOutlineOutput output;
                float feather = (1.0 + round(saturate(_OutlineFeatherScale * 0.5) * 254.0)) / 255.0;
                float enabled = _OutlineEnabled > 0.5 && _OutlineWidthScale > 0.0 ? feather : 0.0;
                output.colorAndEnabled = float4(_OutlineColor.rgb, enabled);
                float3 viewNormal = normalize(mul((float3x3)UNITY_MATRIX_V, input.normalWS));
                output.normalAndId = float4(viewNormal, _ShopObjectId);
                output.eyeDepth = float2(input.eyeDepth, clamp(_OutlineWidthScale, 0.0, 2.0));
                return output;
            }
            ENDCG
        }

        Pass
        {
            // Built-in 灯光照射其他材质时也能使用细分后的投影轮廓。
            Name "SHADOWCASTER"
            Tags { "LightMode"="ShadowCaster" }

            CGPROGRAM
            #pragma only_renderers d3d11
            #pragma target 5.0
            #pragma vertex ShopVert
            #pragma hull ShopHull
            #pragma domain ShopBuiltInShadowDomain
            #pragma fragment ShopBuiltInShadowFragment
            #pragma multi_compile_shadowcaster


            struct ShopBuiltInShadowVarying
            {
                V2F_SHADOW_CASTER;
            };

            [domain("tri")]
            ShopBuiltInShadowVarying ShopBuiltInShadowDomain(ShopTessFactors factors,
                const OutputPatch<ShopControlPoint, 3> patch, float3 barycentric : SV_DomainLocation)
            {
                ShopSurfacePoint surfacePoint = ShopEvaluate(patch, barycentric);
                ShopBuiltInShadowVarying output;
                float4 positionOS = float4(surfacePoint.positionOS, 1.0);
            #if defined(SHADOWS_CUBE) && !defined(SHADOWS_CUBE_IN_DEPTH_TEX)
                output.vec = mul(unity_ObjectToWorld, positionOS).xyz - _LightPositionRange.xyz;
                output.pos = UnityObjectToClipPos(positionOS);
            #else
                output.pos = UnityApplyLinearShadowBias(UnityClipSpaceShadowCasterPos(positionOS, surfacePoint.normalOS));
            #endif
                return output;
            }

            float4 ShopBuiltInShadowFragment(ShopBuiltInShadowVarying input) : SV_Target
            {
                SHADOW_CASTER_FRAGMENT(input)
            }
            ENDCG
        }

        Pass
        {
            Name "FIND_EDGE"
            Tags { "LightMode"="ShopFullscreen" }
            Cull Off ZWrite Off ZTest Always

            CGPROGRAM
            #pragma only_renderers d3d11
            #pragma target 5.0
            #pragma vertex vert_img
            #pragma fragment FindEdge
            float4 FindEdge(v2f_img i) : SV_Target
            {
                float2 uv = i.uv;
                if (tex2D(_Ink, uv).a == 0) return 0;
                float z = tex2D(_EyeDepth,uv).r;
                float4 face = SurfaceAt(uv);
                float right = Edge(uv, float2(1,0), z, face);
                float left  = Edge(uv, float2(-1,0), z, face);
                float up    = Edge(uv, float2(0,1), z, face);
                float down  = Edge(uv, float2(0,-1), z, face);
                if (right+left+up+down < 0.5) return 0;

                // 用八邻域占用值拟合边界法线，再在内外采样位置之间求亚像素中心。
                float outside[8];
                const float2 taps[8] = {
                    float2(1,0), float2(-1,0), float2(0,1), float2(0,-1),
                    float2(1,1), float2(1,-1), float2(-1,1), float2(-1,-1)
                };
                float2 gradient = 0;
                [unroll] for (int tap = 0; tap < 8; tap++)
                {
                    outside[tap] = SurfaceAt(uv + taps[tap] * _Pixel.xy).w != face.w;
                    gradient += taps[tap] * outside[tap] * (tap < 4 ? 2.0 : 1.0);
                }
                float2 ownedGradient = float2(right-left, up-down);
                if (dot(gradient, gradient) < 0.1 || dot(gradient, ownedGradient) <= 0.0)
                {
                    // 内折线或过细特征无法拟合占用边界时，沿当前已认领的边绘制。
                    float len = length(ownedGradient);
                    return float4(ownedGradient / max(1e-5, len), len > 0 ? 0.5 : 0.0, 1);
                }
                float2 n = normalize(gradient);
                float farthestInside = 0, nearestOutside = 2;
                [unroll] for (int tap = 0; tap < 8; tap++)
                {
                    float projection = dot(taps[tap], n);
                    if (outside[tap] > 0.5)
                        nearestOutside = min(nearestOutside, projection);
                    else
                        farthestInside = max(farthestInside, projection);
                }
                float center = clamp((nearestOutside + farthestInside) * 0.5, 0.15, 0.72);
                return float4(n, center, 1);
            }
            ENDCG
        }
        Pass
        {
            Name "ROUND_PEN"
            Tags { "LightMode"="ShopFullscreen" }
            Cull Off ZWrite Off ZTest Always

            CGPROGRAM
            #pragma only_renderers d3d11
            #pragma target 5.0
            #pragma vertex vert_img
            #pragma fragment DrawPen
            float4 DrawPen(v2f_img i) : SV_Target
            {
                float2 uv = i.uv;
                float4 base = tex2D(_ShopSceneColor,uv);
                #if UNITY_UV_STARTS_AT_TOP
                if (_MainTex_TexelSize.y < 0) uv.y = 1 - uv.y;
                #endif
                float4 dest = tex2D(_Surface,uv);
                float z = tex2D(_EyeDepth,uv).r;
                float rawDepth = SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, uv);
                #if defined(UNITY_REVERSED_Z)
                float orthoDepth = lerp(_ProjectionParams.z, _ProjectionParams.y, rawDepth);
                #else
                float orthoDepth = lerp(_ProjectionParams.y, _ProjectionParams.z, rawDepth);
                #endif
                float cameraZ = lerp(LinearEyeDepth(rawDepth), orthoDepth, unity_OrthoParams.w);
                int extent = (int)ceil(_StrokeReach + 0.5);
                float bestCoverage = 0;
                float bestDistance = 1e8;
                float3 color = 0;
                // 在有限半径内测到短线段的欧氏距离，折角与端点自然得到圆形笔帽。
                [loop] for (int y=-extent; y<=extent; ++y)
                [loop] for (int x=-extent; x<=extent; ++x)
                {
                    float2 delta = float2(x,y);
                    float2 at = uv + delta*_Pixel.xy;
                    float4 seed = tex2Dlod(_Seeds,float4(at,0,0));
                    if (seed.a < 0.5) continue;
                    float2 n = seed.xy, tangent = float2(-n.y,n.x);
                    float2 toLine = -delta - n * seed.z;
                    float halfLength = 0.64;
                    float d = length(toLine - tangent * clamp(dot(toLine,tangent), -halfLength, halfLength));
                    float2 ownerStyle = tex2Dlod(_EyeDepth,float4(at,0,0)).rg;
                    float4 ink = tex2Dlod(_Ink,float4(at,0,0));
                    float radius = _Width * ownerStyle.g * 0.5;
                    // 墨色纹理的零透明度留给关闭描边；其余 255 档编码该材质的边缘平滑倍率。
                    float feather = _Feather * max(0, (round(ink.a * 255) - 1) / 127.0);
                    float coverage = feather > 0 ? saturate(0.5 + (radius - d) / feather) : step(d, radius);
                    if (coverage <= 0 || coverage < bestCoverage || (coverage == bestCoverage && d >= bestDistance)) continue;
                    float sz = ownerStyle.r;
                    float owner = tex2Dlod(_Surface,float4(at,0,0)).w;
                    // 外笔迹可覆盖背景。本像素若有更靠近的其他物体，则把笔迹裁断。
                    if (owner != dest.w)
                    {
                        float distanceInPixels = max(abs(delta.x), abs(delta.y));
                        float tolerance = z > 0
                            ? ContactTolerance(at, uv, max(1, distanceInPixels), sz, z, owner, dest.w)
                            : max(0.002, sz * 0.0002);
                        if ((z > 0 ? z : cameraZ) + tolerance < sz) continue;
                    }
                    bestCoverage = coverage;
                    bestDistance = d;
                    color = ink.rgb;
                }
                return float4(lerp(base.rgb,color,bestCoverage),base.a);
            }
            ENDCG
        }

        Pass
        {
            Name "SHOP_CORNER_HORIZONTAL"
            Tags { "LightMode"="ShopFullscreen" }
            Cull Off ZWrite Off ZTest Always

            CGPROGRAM
            #pragma only_renderers d3d11
            #pragma target 5.0
            #pragma vertex FullScreen
            #pragma fragment FilterHorizontal
            ENDCG
        }

        Pass
        {
            Name "SHOP_CORNER_VERTICAL"
            Tags { "LightMode"="ShopFullscreen" }
            Cull Off ZWrite Off ZTest Always

            CGPROGRAM
            #pragma only_renderers d3d11
            #pragma target 5.0
            #pragma vertex FullScreen
            #pragma fragment FilterVertical
            ENDCG
        }
    }

    CustomEditor "ShiftGame.ShopLab.ShopMaterialInspector"
    Fallback Off
}
