using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using ShiftGame.ShopLab;

namespace ShiftGame
{
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Camera))]
    [AddComponentMenu("商店渲染/相机渲染")]
    public sealed class ShopLights : MonoBehaviour
    {
        private const int MaxLights = 24;
        private const int Faces = 6;
        private const string ShadowPass = "SHOP_SHADOW";

        private enum ShadowResolution
        {
            [InspectorName("256 像素")] Low = 256,
            [InspectorName("512 像素")] Medium = 512,
            [InspectorName("1024 像素")] High = 1024
        }

        [InspectorName("阴影分辨率"), Tooltip("设定每盏灯的每个投影面的分辨率。")]
        [SerializeField] private ShadowResolution shadowResolution = ShadowResolution.Medium;

        [InspectorName("软投影采样宽度（阴影纹素）"), Range(0f, 8f)]
        [Tooltip("仅影响启用软阴影的灯。增大数值会拉开四个阴影采样点；0 表示不额外偏移。")]
        [SerializeField] private float softShadowSampleWidth = 3f;

        public float SoftShadowSampleWidth
        {
            get => softShadowSampleWidth;
            set => softShadowSampleWidth = Mathf.Clamp(value, 0f, 8f);
        }

        [InspectorName("灯光强度倍率"), Min(0f), Tooltip("统一调整此相机的逐灯光强。")]
        [SerializeField] private float lightIntensityScale = 1f;

        [InspectorName("优先灯光"), Tooltip("按此顺序优先纳入已启用的灯，再自动加入当前场景中的其他灯。前八盏灯启用阴影圆角。")]
        [SerializeField] private Light[] lights = Array.Empty<Light>();

        [InspectorName("额外遮挡物"), Tooltip("使用主材质的渲染器会自动加入。这里可加入使用其他材质的遮挡物。")]
        [SerializeField] private Renderer[] extraCasters = Array.Empty<Renderer>();

        [InspectorName("主摄像机")]
        [SerializeField] private Camera renderCamera;

        [InspectorName("描边"), SerializeField] private ShopOutline outline = new ShopOutline();
        [InspectorName("阴影圆角"), SerializeField] private ShopShadowCorners corners = new ShopShadowCorners();

        public ShopOutline Outline => outline;
        public ShopShadowCorners Corners => corners;

        private static readonly List<ShopLights> ActiveRigs = new List<ShopLights>();
        private Camera attachedEye;

        private static readonly int ShadowVpId = Shader.PropertyToID("_ShopShadowVP");
        private static readonly int ShadowDepthId = Shader.PropertyToID("_ShopShadowDepthParams");
        private static readonly int CountId = Shader.PropertyToID("_ShopLightCount");
        private static readonly int RoundedMaskEnabledId = Shader.PropertyToID("_ShopRoundedLightMaskEnabled");
        private static readonly Vector3[] FaceDirections =
        {
            Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back
        };
        private static readonly Vector3[] FaceUps =
        {
            Vector3.up, Vector3.up, Vector3.back, Vector3.forward, Vector3.up, Vector3.up
        };

        // 方向光和聚光各使用一层，点光使用连续六层，索引取自该盏灯的参数。
        private readonly Vector4[] positions = new Vector4[MaxLights];
        private readonly Vector4[] directions = new Vector4[MaxLights];
        private readonly Vector4[] options = new Vector4[MaxLights];
        private readonly Vector4[] colors = new Vector4[MaxLights];
        private readonly Matrix4x4[] projections = new Matrix4x4[MaxLights * Faces];
        private readonly Vector4[] depthPlanes = new Vector4[MaxLights * Faces];
        private readonly Vector4[] tileOptions = new Vector4[MaxLights * Faces];
        private readonly List<Light> activeLights = new List<Light>();
        private readonly List<Renderer> casters = new List<Renderer>();
        private readonly List<CasterDraw> draws = new List<CasterDraw>();
        private readonly List<Material> materials = new List<Material>();
        private readonly HashSet<Renderer> includedCasters = new HashSet<Renderer>();
        private readonly HashSet<Material> checkedBaseMaps = new HashSet<Material>();
        private readonly Plane[] frustum = new Plane[6];

        private RenderTexture atlas;
        private CommandBuffer shadowCommands;
        private Shader surfaceShader;
        private Material fallbackMaterial;
        private int fallbackPass = -1;
        private Bounds lightingBounds;
        private bool hasLightingBounds;
        private int lastExtraLightCount;
        private bool reportedAtlasFailure;
        private double nextSceneScan;
        private Light[] sceneLights = Array.Empty<Light>();
        private Renderer[] sceneRenderers = Array.Empty<Renderer>();

        public IReadOnlyList<Renderer> ShadowCasters => casters;
        public IReadOnlyList<Renderer> CornerReceivers => sceneRenderers;

        private struct CasterDraw
        {
            public Renderer renderer;
            public Material material;
            public int submesh;
            public int pass;
        }

        // 场景生成器可预设优先灯光、额外遮挡物和负责渲染的相机。
        public void Configure(Light[] lightList, Renderer[] shadowCasters, Camera camera)
        {
            lights = lightList ?? Array.Empty<Light>();
            extraCasters = shadowCasters ?? Array.Empty<Renderer>();
            renderCamera = camera;
            nextSceneScan = 0;
        }

        // 生成或切换房间后，下一次相机渲染时重新扫描场景。
        public void InvalidateSceneCache()
        {
            nextSceneScan = 0;
        }

        private void OnEnable()
        {
            nextSceneScan = 0;
            attachedEye = GetComponent<Camera>();
            ActiveRigs.Add(this);
            Camera.onPreCull += PrepareLighting;
            Camera.onPostRender += FinishLighting;
        }

        private void OnDisable()
        {
            Camera.onPreCull -= PrepareLighting;
            Camera.onPostRender -= FinishLighting;
            ActiveRigs.Remove(this);
            if (preparedCamera)
                FinishLighting(preparedCamera);
            outline.Release();
            corners.Release();
            shadowCommands?.Release();
            shadowCommands = null;
            if (atlas)
            {
                atlas.Release();
                DestroyResource(atlas);
                atlas = null;
            }
            if (fallbackMaterial)
            {
                DestroyResource(fallbackMaterial);
                fallbackMaterial = null;
            }
        }

        internal static void DestroyResource(UnityEngine.Object resource)
        {
            if (Application.isPlaying)
                Destroy(resource);
            else
                DestroyImmediate(resource);
        }

        private void PrepareLighting(Camera camera)
        {
            Camera mainEye = renderCamera ? renderCamera : attachedEye;
            if (camera.cameraType == CameraType.SceneView ? !OwnsSceneView() : camera != mainEye)
                return;
            preparedCamera = camera;
            Shader.SetGlobalFloat("_ShopLightingManaged", 1f);
            Shader.SetGlobalFloat(RoundedMaskEnabledId, 0f);

            // 所有通道共用主眼的细分级别，投影随曲面的实际形状变化。
            Camera tessCamera = mainEye ? mainEye : camera;
            Shader.SetGlobalMatrix("_ShopTessVP", GL.GetGPUProjectionMatrix(tessCamera.projectionMatrix,
                tessCamera.targetTexture != null) * tessCamera.worldToCameraMatrix);
            Shader.SetGlobalVector("_ShopTessCameraSize", new Vector4(tessCamera.scaledPixelWidth,
                tessCamera.scaledPixelHeight, tessCamera.nearClipPlane, tessCamera.orthographic ? -1f : 1f));

            RefreshSceneObjects();
            CollectLights();
            var sceneBounds = CollectCasters();
            Array.Clear(options, 0, options.Length);
            Array.Clear(tileOptions, 0, tileOptions.Length);

            int layerCount = 0;
            int lightCount = Mathf.Min(MaxLights, activeLights.Count);
            bool linearColor = QualitySettings.activeColorSpace == ColorSpace.Linear;
            if (draws.Count > 0)
            {
                for (int i = 0; i < lightCount; i++)
                {
                    Light active = activeLights[i];
                    if (active.shadows != LightShadows.None)
                        layerCount += active.type == LightType.Point ? Faces : 1;
                }
            }

            int resolution = (int)shadowResolution;
            bool canRenderShadows = EnsureAtlas(resolution, layerCount);
            int nextLayer = 0;
            if (canRenderShadows)
                shadowCommands.Clear();

            float intensityScale = Mathf.Max(0f, lightIntensityScale);
            for (int i = 0; i < lightCount; i++)
            {
                Light lightSource = activeLights[i];
                var lightTransform = lightSource.transform;
                bool directional = lightSource.type == LightType.Directional;
                bool spot = lightSource.type == LightType.Spot;
                float lightType = directional ? 0f : spot ? 1f : 2f;
                Vector3 position = lightTransform.position;
                Vector3 forward = lightTransform.forward;
                float range = Mathf.Max(lightSource.range, 0.01f);
                float outer = Mathf.Cos(0.5f * lightSource.spotAngle * Mathf.Deg2Rad);

                float shadowEnabled = canRenderShadows && lightSource.shadows != LightShadows.None ? 1f : 0f;

                positions[i] = new Vector4(position.x, position.y, position.z, lightType);
                directions[i] = new Vector4(forward.x, forward.y, forward.z, range);
                options[i] = new Vector4(lightSource.intensity * intensityScale, outer, nextLayer, shadowEnabled);
                colors[i] = linearColor ? lightSource.color.linear : lightSource.color;
                if (shadowEnabled <= 0f || draws.Count == 0)
                    continue;

                if (directional)
                    RenderDirectional(nextLayer++, lightSource, sceneBounds);
                else if (spot)
                    RenderPerspective(nextLayer++, lightSource, forward, lightTransform.up, lightSource.spotAngle, range);
                else
                {
                    for (int face = 0; face < Faces; face++)
                        RenderPerspective(nextLayer++, lightSource, FaceDirections[face], FaceUps[face], 92f, range);
                }
            }

            if (canRenderShadows)
            {
                Graphics.ExecuteCommandBuffer(shadowCommands);
                Shader.SetGlobalTexture("_ShopShadowAtlas", atlas);
                Shader.SetGlobalFloat("_ShopShadowPixel", 1f / resolution);
            }

            Shader.SetGlobalVectorArray("_ShopLightingPositions", positions);
            Shader.SetGlobalVectorArray("_ShopLightingDirections", directions);
            Shader.SetGlobalVectorArray("_ShopLightingOptions", options);
            Shader.SetGlobalMatrixArray("_ShopShadowProjections", projections);
            Shader.SetGlobalVectorArray("_ShopShadowLinearDepthPlanes", depthPlanes);
            Shader.SetGlobalVectorArray("_ShopShadowTileOptions", tileOptions);
            Shader.SetGlobalVectorArray("_ShopLightingColors", colors);
            Shader.SetGlobalInt(CountId, lightCount);
            corners.PrepareCamera(camera, CornerReceivers, lightCount);
            if (camera == attachedEye)
                outline.PrepareCamera(camera);
        }

        // SceneView 只使用活动场景的一组配置；游戏相机各自使用自己的组件。
        private bool OwnsSceneView()
        {
            var activeScene = SceneManager.GetActiveScene();
            Camera main = Camera.main;
            ShopLights first = null;
            foreach (var rig in ActiveRigs)
            {
                if (!rig || rig.gameObject.scene != activeScene)
                    continue;
                if (!first)
                    first = rig;
                if (main && (rig.renderCamera ? rig.renderCamera : rig.attachedEye) == main)
                    return rig == this;
            }
            return first == this;
        }

        [ImageEffectOpaque]
        private void OnRenderImage(RenderTexture source, RenderTexture destination)
        {
            outline.Render(source, destination);
        }

        private Camera preparedCamera;

        private void FinishLighting(Camera camera)
        {
            if (camera != preparedCamera) return;
            // 结束本相机的管理模式，其他相机可以直接使用 Unity 自身的主灯。
            corners.FinishCamera(camera);
            Shader.SetGlobalFloat("_ShopLightingManaged", 0f);
            Shader.SetGlobalFloat(RoundedMaskEnabledId, 0f);
            Shader.SetGlobalVector("_ShopTessCameraSize", Vector4.zero);
            preparedCamera = null;
        }

        private void RefreshSceneObjects()
        {
            // 降低场景扫描频率。已发现的对象及其灯光参数仍逐帧读取。
            double now = Time.realtimeSinceStartupAsDouble;
            if (now < nextSceneScan)
                return;

            nextSceneScan = now + 0.5;
            sceneLights = FindObjectsByType<Light>(FindObjectsInactive.Include, FindObjectsSortMode.InstanceID);
            sceneRenderers = FindObjectsByType<Renderer>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        }

        private void CollectLights()
        {
            activeLights.Clear();
            foreach (Light lightSource in lights)
                AddLight(lightSource);

            foreach (Light lightSource in sceneLights)
                AddLight(lightSource);

            int excess = Mathf.Max(0, activeLights.Count - MaxLights);
            if (excess > 0 && excess != lastExtraLightCount)
                Debug.LogWarning($"有 {excess} 盏灯超出 {MaxLights} 盏上限，可调整优先灯光。", this);
            lastExtraLightCount = excess;
        }

        private void AddLight(Light lightSource)
        {
            if (!lightSource || !lightSource.isActiveAndEnabled || lightSource.intensity <= 0f
                || lightSource.gameObject.scene != gameObject.scene || activeLights.Contains(lightSource))
                return;
            if (lightSource.type == LightType.Directional || lightSource.type == LightType.Point
                || lightSource.type == LightType.Spot)
                activeLights.Add(lightSource);
        }

        private Bounds CollectCasters()
        {
            if (!surfaceShader)
                surfaceShader = Shader.Find(ShopMaterialParameters.ShaderName);

            includedCasters.Clear();
            checkedBaseMaps.Clear();
            casters.Clear();
            draws.Clear();
            hasLightingBounds = false;
            foreach (Renderer renderer in extraCasters)
                AddCaster(renderer, true);
            foreach (Renderer renderer in sceneRenderers)
                AddCaster(renderer, false);

            return hasLightingBounds ? lightingBounds : new Bounds(transform.position, Vector3.one);
        }

        private void AddCaster(Renderer renderer, bool explicitCaster)
        {
            if (!renderer || !renderer.enabled || !renderer.gameObject.activeInHierarchy
                || renderer.gameObject.scene != gameObject.scene || includedCasters.Contains(renderer))
                return;

            renderer.GetSharedMaterials(materials);
            bool matched = false;
            bool receives = false;
            bool casts = renderer.shadowCastingMode != ShadowCastingMode.Off;
            for (int submesh = 0; submesh < materials.Count; submesh++)
            {
                Material material = materials[submesh];
                bool usesSurface = material && material.shader == surfaceShader;
                // 动态替换或清空材质贴图后，本帧绘制就使用新来源；共享材质每次准备只检查一次。
                if (usesSurface && checkedBaseMaps.Add(material))
                    ShopMaterialParameters.SyncBaseMap(material);
                receives |= usesSurface;
                if (!casts)
                    continue;

                int pass = usesSurface ? material.FindPass(ShadowPass) : -1;
                if (pass < 0 && explicitCaster && EnsureFallback())
                {
                    material = fallbackMaterial;
                    pass = fallbackPass;
                }
                if (pass < 0)
                    continue;

                draws.Add(new CasterDraw { renderer = renderer, material = material, submesh = submesh, pass = pass });
                matched = true;
            }

            if (receives || matched)
            {
                includedCasters.Add(renderer);
                // 关闭投影的接收面仍须落在方向光的覆盖范围中。
                if (hasLightingBounds)
                    lightingBounds.Encapsulate(renderer.bounds);
                else
                    lightingBounds = renderer.bounds;
                hasLightingBounds = true;
            }
            if (matched)
                casters.Add(renderer);
        }

        private bool EnsureFallback()
        {
            if (fallbackMaterial)
                return fallbackPass >= 0;
            if (!surfaceShader)
                return false;
            fallbackMaterial = new Material(surfaceShader) { hideFlags = HideFlags.HideAndDontSave };
            // 额外遮挡物保持原网格轮廓，避免让备用材质改变其形状。
            fallbackMaterial.SetFloat("_TessFactor", 1f);
            fallbackMaterial.SetFloat("_Smoothness", 0f);
            fallbackPass = fallbackMaterial.FindPass(ShadowPass);
            return fallbackPass >= 0;
        }

        private bool EnsureAtlas(int resolution, int layerCount)
        {
            if (atlas && (atlas.width != resolution || atlas.volumeDepth != layerCount))
            {
                atlas.Release();
                DestroyResource(atlas);
                atlas = null;
            }

            if (layerCount == 0)
                return false;

            if (atlas && atlas.IsCreated())
                return true;

            if (!atlas)
            {
                RenderTextureFormat format = SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.RFloat)
                    ? RenderTextureFormat.RFloat : RenderTextureFormat.ARGBFloat;
                atlas = new RenderTexture(resolution, resolution, 24, format, RenderTextureReadWrite.Linear)
                {
                    name = "商店逐灯阴影图集",
                    hideFlags = HideFlags.HideAndDontSave,
                    filterMode = FilterMode.Point,
                    wrapMode = TextureWrapMode.Clamp,
                    dimension = TextureDimension.Tex2DArray,
                    volumeDepth = layerCount,
                    useMipMap = false
                };
            }

            if (atlas.Create())
            {
                shadowCommands ??= new CommandBuffer { name = "商店逐灯阴影" };
                return true;
            }

            if (!reportedAtlasFailure)
                Debug.LogWarning("当前设备无法创建逐灯阴影图集。光源仍参与明暗计算。", this);
            reportedAtlasFailure = true;
            return false;
        }

        private void RenderDirectional(int layer, Light lightSource, Bounds bounds)
        {
            var rotation = lightSource.transform.rotation;
            // 用场景范围包住参与遮挡的物体，方向光不依赖灯的位置。
            float radius = Mathf.Max(1f, bounds.extents.magnitude + 0.5f);
            float far = 2f * radius + 0.5f;
            Vector3 eye = bounds.center - rotation * Vector3.forward * (radius + 0.25f);
            var projection = Matrix4x4.Ortho(-radius, radius, -radius, radius, 0.01f, far);
            RenderTile(layer, lightSource, eye, rotation, projection, far);
        }

        private void RenderPerspective(int layer, Light lightSource, Vector3 forward,
            Vector3 up, float angle, float far)
        {
            float near = Mathf.Clamp(lightSource.shadowNearPlane, 0.001f, far * 0.5f);
            Matrix4x4 projection = Matrix4x4.Perspective(Mathf.Clamp(angle, 1f, 179f), 1f, near, far);
            RenderTile(layer, lightSource, lightSource.transform.position,
                Quaternion.LookRotation(forward, up), projection, far);
        }

        private void RenderTile(int index, Light lightSource, Vector3 eye,
            Quaternion rotation, Matrix4x4 projection, float far)
        {
            Matrix4x4 view = Matrix4x4.Scale(new Vector3(1f, 1f, -1f))
                * Matrix4x4.TRS(eye, rotation, Vector3.one).inverse;
            Matrix4x4 gpuProjection = GL.GetGPUProjectionMatrix(projection, false) * view;
            Vector3 forward = rotation * Vector3.forward;
            Vector4 depthPlane = new Vector4(forward.x, forward.y, forward.z, -Vector3.Dot(forward, eye)) / far;

            projections[index] = gpuProjection;
            depthPlanes[index] = depthPlane;
            tileOptions[index] = new Vector4(
                1f, 0.00025f + 0.002f * lightSource.shadowBias,
                lightSource.shadowNormalBias, lightSource.shadows == LightShadows.Soft ? softShadowSampleWidth : 0f);

            shadowCommands.SetRenderTarget(new RenderTargetIdentifier(atlas), 0, CubemapFace.Unknown, index);
            shadowCommands.ClearRenderTarget(true, true, Color.white);
            shadowCommands.SetGlobalMatrix(ShadowVpId, gpuProjection);
            shadowCommands.SetGlobalVector(ShadowDepthId, depthPlane);

            // 平行光之外只提交落在对应投影面中的遮挡物。
            GeometryUtility.CalculateFrustumPlanes(projection * view, frustum);
            foreach (CasterDraw draw in draws)
            {
                if ((lightSource.cullingMask & (1 << draw.renderer.gameObject.layer)) != 0
                    && GeometryUtility.TestPlanesAABB(frustum, draw.renderer.bounds))
                    shadowCommands.DrawRenderer(draw.renderer, draw.material, draw.submesh, draw.pass);
            }
        }
    }

    // 相机记录每盏灯的分区，再把屏幕中的分区边缘做圆角。
    [Serializable]
    public sealed class ShopShadowCorners
    {
        private const CameraEvent FilterEvent = CameraEvent.BeforeForwardOpaque;
        private const int MaxRadius = 12;
        private static readonly int EnabledId = Shader.PropertyToID("_ShopRoundedLightMaskEnabled");
        private static readonly int ReceiverId = Shader.PropertyToID("_ShopCornerReceiverId");

        [HideInInspector, SerializeField]
        private Shader filterShader = null;

        [InspectorName("圆角采样半径（屏幕像素）"), Range(0, MaxRadius),
         Tooltip("水平和垂直各采样指定像素数。边界圆角随采样半径增大，0 恢复原始二分。"), SerializeField]
        private int radiusPixels = 5;

        [InspectorName("边缘过渡"), Range(0f, 0.4f),
         Tooltip("设置二分门槛两侧过渡区间的总宽度。"), SerializeField]
        private float edgeTransition = 0.14f;

        [InspectorName("圆角二分门槛"), Range(0.35f, 0.65f),
         Tooltip("0.5 保持直边居中；提高门槛会增加暗部，降低门槛会增加亮部。"), SerializeField]
        private float cutoff = 0.5f;

        [InspectorName("高斯宽度（相对半径）"), Range(0.15f, 0.65f),
         Tooltip("设置高斯标准差占采样半径的比例。"), SerializeField]
        private float relativeSigma = 0.4f;

        [InspectorName("深度保护绝对容差（米/像素）"), Range(0f, 0.2f), SerializeField]
        private float absoluteDepthTolerance = 0.025f;

        [InspectorName("深度保护相对容差（每像素）"), Range(0f, 0.05f), SerializeField]
        private float relativeDepthTolerance = 0.012f;

        [InspectorName("启用阴影圆角"), SerializeField] private bool enabled = true;

        public bool Enabled { get => enabled; set => enabled = value; }
        public int RadiusPixels { get => radiusPixels; set => radiusPixels = Mathf.Clamp(value, 0, MaxRadius); }
        public float Cutoff { get => cutoff; set => cutoff = Mathf.Clamp(value, 0.35f, 0.65f); }
        public float EdgeTransition { get => edgeTransition; set => edgeTransition = Mathf.Clamp(value, 0f, 0.4f); }
        public float RelativeSigma { get => relativeSigma; set => relativeSigma = Mathf.Clamp(value, .15f, .65f); }
        public float AbsoluteDepthTolerance { get => absoluteDepthTolerance; set => absoluteDepthTolerance = Mathf.Clamp(value, 0f, .2f); }
        public float RelativeDepthTolerance { get => relativeDepthTolerance; set => relativeDepthTolerance = Mathf.Clamp(value, 0f, .05f); }

        private readonly List<Material> materials = new List<Material>();
        private readonly Plane[] frustum = new Plane[6];
        private readonly RenderTexture[] textures = new RenderTexture[9];
        private readonly RenderTargetIdentifier[] raw = new RenderTargetIdentifier[4];
        private readonly RenderTargetIdentifier[] horizontal = new RenderTargetIdentifier[2];
        private readonly RenderTargetIdentifier[] rounded = new RenderTargetIdentifier[3];
        private CommandBuffer commands;
        private Camera attachedCamera;
        private Material filter;
        private int horizontalPass, verticalPass;

        internal void PrepareCamera(Camera camera, IReadOnlyList<Renderer> receivers, int lightCount)
        {
            if (attachedCamera != camera)
                Detach();
            if (!Enabled || radiusPixels <= 0 || lightCount <= 0 || camera.rect != new Rect(0, 0, 1, 1)
                || camera.actualRenderingPath != RenderingPath.Forward || SystemInfo.supportedRenderTargetCount < 4)
            {
                Detach();
                ReleaseTargets();
                return;
            }

            Shader shader = filterShader ? filterShader : Shader.Find(ShopLab.ShopMaterialParameters.ShaderName);
            if (!shader || !shader.isSupported)
            {
                Detach();
                ReleaseTargets();
                return;
            }
            if (!filter || filter.shader != shader)
            {
                if (filter) ShopLights.DestroyResource(filter);
                filter = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
                horizontalPass = filter.FindPass("SHOP_CORNER_HORIZONTAL");
                verticalPass = filter.FindPass("SHOP_CORNER_VERTICAL");
            }
            if (horizontalPass < 0 || verticalPass < 0) { Detach(); return; }

            int width = camera.scaledPixelWidth;
            int height = camera.scaledPixelHeight;
            if (width < 1 || height < 1 || !EnsureTargets(width, height))
            {
                Detach();
                ReleaseTargets();
                return;
            }

            if (commands == null)
                commands = new CommandBuffer { name = "商店：逐灯阴影圆角" };
            if (!attachedCamera)
            {
                camera.AddCommandBuffer(FilterEvent, commands);
                attachedCamera = camera;
            }

            commands.Clear();
            commands.SetRenderTarget(raw, raw[0]);
            commands.ClearRenderTarget(true, true, Color.clear);
            GeometryUtility.CalculateFrustumPlanes(camera, frustum);
            int receiverId = 0;
            foreach (Renderer receiver in receivers)
            {
                if (!receiver || !receiver.enabled || receiver.forceRenderingOff
                    || !receiver.gameObject.activeInHierarchy || receiver.shadowCastingMode == ShadowCastingMode.ShadowsOnly
                    || (camera.cullingMask & (1 << receiver.gameObject.layer)) == 0
                    || !GeometryUtility.TestPlanesAABB(frustum, receiver.bounds))
                    continue;

                receiver.GetSharedMaterials(materials);
                for (int slot = 0; slot < materials.Count; slot++)
                {
                    Material material = materials[slot];
                    if (!material || material.renderQueue > (int)RenderQueue.GeometryLast)
                        continue;
                    int pass = material.FindPass("SHOP_LIGHT_MASK");
                    if (pass < 0)
                        continue;
                    commands.SetGlobalFloat(ReceiverId, ++receiverId);
                    commands.DrawRenderer(receiver, material, slot, pass);
                }
            }

            // X/Y 使用相同的高斯权重，得到圆对称模糊，再对每盏灯单独二分。
            commands.SetGlobalTexture("_ShopCornerRaw0", raw[0]);
            commands.SetGlobalTexture("_ShopCornerRaw1", raw[1]);
            commands.SetGlobalTexture("_ShopCornerUnionAndId", raw[2]);
            commands.SetGlobalTexture("_ShopCornerGuide", raw[3]);
            commands.SetGlobalVector("_ShopCornerSettings", new Vector4(
                width, height, Mathf.Clamp(radiusPixels, 1, MaxRadius), edgeTransition));
            commands.SetGlobalFloat("_ShopCornerCutoff", cutoff);
            commands.SetGlobalVector("_ShopCornerKernel", new Vector4(
                Mathf.Clamp(relativeSigma, 0.15f, 0.65f), absoluteDepthTolerance, relativeDepthTolerance, 0f));
            commands.SetRenderTarget(horizontal, BuiltinRenderTextureType.None);
            commands.DrawProcedural(Matrix4x4.identity, filter, horizontalPass, MeshTopology.Triangles, 3);
            commands.SetGlobalTexture("_ShopCornerHorizontal0", horizontal[0]);
            commands.SetGlobalTexture("_ShopCornerHorizontal1", horizontal[1]);
            commands.SetRenderTarget(rounded, BuiltinRenderTextureType.None);
            commands.DrawProcedural(Matrix4x4.identity, filter, verticalPass, MeshTopology.Triangles, 3);
            commands.SetGlobalTexture("_ShopRoundedLightMask0", rounded[0]);
            commands.SetGlobalTexture("_ShopRoundedLightMask1", rounded[1]);
            commands.SetGlobalTexture("_ShopRoundedLightUnion", rounded[2]);
            commands.SetGlobalTexture("_ShopRoundedLightGuide", raw[3]);
            commands.SetGlobalFloat(EnabledId, 1f);
            commands.SetRenderTarget(BuiltinRenderTextureType.CameraTarget);
        }

        private bool EnsureTargets(int width, int height)
        {
            if (textures[0] && textures[0].width == width && textures[0].height == height)
            {
                bool ready = true;
                foreach (var texture in textures)
                    ready &= texture && texture.IsCreated();
                if (ready)
                    return true;
            }

            ReleaseTargets();
            for (int i = 0; i < textures.Length; i++)
            {
                var format = i == 2 ? RenderTextureFormat.RGFloat
                    : i == 3 ? RenderTextureFormat.RFloat : i == 8 ? RenderTextureFormat.RHalf : RenderTextureFormat.ARGBHalf;
                if (!SystemInfo.SupportsRenderTextureFormat(format))
                    return false;
                textures[i] = new RenderTexture(width, height, i == 0 ? 24 : 0, format, RenderTextureReadWrite.Linear)
                {
                    name = "阴影圆角数据 " + i,
                    hideFlags = HideFlags.HideAndDontSave,
                    filterMode = FilterMode.Point,
                    wrapMode = TextureWrapMode.Clamp
                };
                if (!textures[i].Create())
                    return false;
            }

            for (int i = 0; i < raw.Length; i++)
                raw[i] = new RenderTargetIdentifier(textures[i]);
            for (int i = 0; i < horizontal.Length; i++)
                horizontal[i] = new RenderTargetIdentifier(textures[4 + i]);
            for (int i = 0; i < rounded.Length; i++)
                rounded[i] = new RenderTargetIdentifier(textures[6 + i]);
            return true;
        }

        private void Detach()
        {
            if (attachedCamera && commands != null)
                attachedCamera.RemoveCommandBuffer(FilterEvent, commands);
            attachedCamera = null;
        }

        private void ReleaseTargets()
        {
            for (int i = 0; i < textures.Length; i++)
            {
                if (!textures[i]) continue;
                textures[i].Release();
                ShopLights.DestroyResource(textures[i]);
                textures[i] = null;
            }
        }

        internal void FinishCamera(Camera camera)
        {
            if (camera != attachedCamera)
                return;
            Detach();
            Shader.SetGlobalFloat(EnabledId, 0f);
        }

        internal void Release()
        {
            Detach();
            commands?.Release();
            commands = null;
            ReleaseTargets();
            if (filter) ShopLights.DestroyResource(filter);
            filter = null;
        }
    }
}

namespace ShiftGame.ShopLab
{
    // 相机先记录可见商品的线色、编号、法线与深度，然后在屏幕上用圆笔描边。
    [Serializable]
    public sealed class ShopOutline
    {
        [HideInInspector] public Shader outlineShader;
        [InspectorName("笔迹总宽（屏幕像素）"), Range(0.5f, 8f)] public float lineWidth = 2.5f;
        [InspectorName("描边边缘平滑宽度（像素）"), Range(0f, 2f)] public float featherPixels = 1f;
        [InspectorName("绘制同一物体内部的折痕")] public bool drawCreases;
        [InspectorName("内部折痕角度（度）"), Range(1, 179)] public float creaseAngle = 55f;
        [InspectorName("启用描边")] public bool drawLines = true;
        public bool Enabled { get => drawLines; set => drawLines = value; }

        Camera eye;
        Material pen;
        int findEdgePass, roundPenPass;
        CommandBuffer dataCommands;
        RenderTexture ink, surface, eyeDepth, seeds;
        readonly RenderTargetIdentifier[] targets = new RenderTargetIdentifier[3];
        readonly List<Material> materials = new List<Material>();
        readonly Dictionary<Renderer, int> objectIds = new Dictionary<Renderer, int>();
        MaterialPropertyBlock values;
        Renderer[] objects;
        int nextId;
        double refreshAfter;
        static readonly int Id = Shader.PropertyToID("_ShopObjectId");
        static readonly int WidthScale = Shader.PropertyToID("_OutlineWidthScale");
        static readonly int FeatherScale = Shader.PropertyToID("_OutlineFeatherScale");
        float largestStrokeReach;
        bool readyForComposite;

        void Attach(Camera camera)
        {
            eye = camera;
            eye.depthTextureMode |= DepthTextureMode.Depth;
            values ??= new MaterialPropertyBlock();
            dataCommands = new CommandBuffer { name = "便利店：记录描边所用的表面" };
            eye.AddCommandBuffer(CameraEvent.BeforeImageEffectsOpaque, dataCommands);
        }

        // 添加商品后可立即调用。定时刷新也允许直接把材质拖到任意新物体上。
        public void RefreshObjects()
        {
            objects = UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            refreshAfter = Time.realtimeSinceStartupAsDouble + 1.0;
            var gone = new List<Renderer>();
            foreach (var item in objectIds) if (!item.Key) gone.Add(item.Key);
            foreach (var item in gone) objectIds.Remove(item);
        }

        // 程序入口：保留其他程序已经设置的材质参数。共享材质的商品可以独立改色。
        public static void SetOutline(Renderer item, Color color, bool enabled)
        {
            if (!item) return;
            var block = new MaterialPropertyBlock();
            item.GetPropertyBlock(block);
            SetOutlineValues(block, color, enabled);
            item.SetPropertyBlock(block);

            // 材质槽的参数块优先于整物件参数块，因此只同步已经有覆盖的槽。
            int slotCount = item.sharedMaterials.Length;
            for (int slot = 0; slot < slotCount; slot++)
            {
                item.GetPropertyBlock(block, slot);
                if (block.isEmpty) continue;
                SetOutlineValues(block, color, enabled);
                item.SetPropertyBlock(block, slot);
            }
        }

        static void SetOutlineValues(MaterialPropertyBlock block, Color color, bool enabled)
        {
            block.SetColor("_OutlineColor", color);
            block.SetFloat("_OutlineEnabled", enabled ? 1 : 0);
        }

        internal void PrepareCamera(Camera camera)
        {
            readyForComposite = false;
            if (!Enabled)
            {
                Release();
                return;
            }

            if (eye != camera)
                Release();
            if (dataCommands != null)
                dataCommands.Clear();

            Shader shader = outlineShader ? outlineShader : Shader.Find(ShopMaterialParameters.ShaderName);
            if (!shader || !shader.isSupported)
                return;
            if (!pen || pen.shader != shader)
            {
                if (pen) ShopLights.DestroyResource(pen);
                // 几何和屏幕通道共享同一个着色器，用名称绑定以免修改顺序后串用。
                pen = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
                findEdgePass = pen.FindPass("FIND_EDGE");
                roundPenPass = pen.FindPass("ROUND_PEN");
            }
            if (findEdgePass < 0 || roundPenPass < 0)
                return;
            if (dataCommands == null)
                Attach(camera);
            if (objects == null || Time.realtimeSinceStartupAsDouble >= refreshAfter) RefreshObjects();
            if (!PrepareTargets(Mathf.Max(1, eye.scaledPixelWidth), Mathf.Max(1, eye.scaledPixelHeight)))
                return;
            dataCommands.SetRenderTarget(targets, new RenderTargetIdentifier(ink));
            dataCommands.ClearRenderTarget(true, true, Color.clear);
            largestStrokeReach = 0f;
            foreach (var obj in objects)
            {
                if (!obj || !obj.enabled || obj.forceRenderingOff || !obj.gameObject.activeInHierarchy ||
                    (eye.cullingMask & (1 << obj.gameObject.layer)) == 0) continue;
                obj.GetSharedMaterials(materials);
                for (int slot = 0; slot < materials.Count; ++slot)
                {
                    var mat = materials[slot];
                    if (!mat) continue;
                    int pass = mat.FindPass("SHOP_OUTLINE_DATA");
                    if (pass < 0) continue;
                    if (!objectIds.TryGetValue(obj, out int objectId)) objectIds[obj] = objectId = ++nextId;
                    // 若调用者有材质槽专属参数块，Unity 会优先使用它，编号也要写进去。
                    obj.GetPropertyBlock(values, slot);
                    bool perSlot = !values.isEmpty;
                    if (!perSlot) obj.GetPropertyBlock(values);
                    // 取本机位可能画到的最远距离，保持宽线末端完整，窄线场景也能少采样。
                    float width = values.HasFloat(WidthScale) ? values.GetFloat(WidthScale) : mat.GetFloat(WidthScale);
                    float feather = values.HasFloat(FeatherScale) ? values.GetFloat(FeatherScale) : mat.GetFloat(FeatherScale);
                    largestStrokeReach = Mathf.Max(largestStrokeReach,
                        0.5f * (lineWidth * Mathf.Clamp(width, 0f, 2f) + featherPixels * Mathf.Clamp(feather, 0f, 2f)));
                    if (values.GetFloat(Id) != objectId)
                    {
                        values.SetFloat(Id, objectId);
                        if (perSlot) obj.SetPropertyBlock(values, slot);
                        else obj.SetPropertyBlock(values);
                    }
                    // 关闭自身描边的商品仍要写深度，它可以遮住后方商品的线。
                    dataCommands.DrawRenderer(obj, mat, slot, pass);
                }
            }
            dataCommands.SetRenderTarget(BuiltinRenderTextureType.CameraTarget);
            readyForComposite = true;
        }

        bool PrepareTargets(int w, int h)
        {
            if (ink && ink.width == w && ink.height == h && ink.IsCreated() &&
                surface.IsCreated() && eyeDepth.IsCreated() && seeds.IsCreated())
                return true;
            ReleaseTargets();
            ink = Make("描边颜色与开关", w, h, RenderTextureFormat.ARGB32, 24);
            surface = Make("表面法线与商品编号", w, h, RenderTextureFormat.ARGBFloat, 0);
            eyeDepth = Make("眼深与材质笔宽倍率", w, h, RenderTextureFormat.RGFloat, 0);
            seeds = Make("圆笔中心线", w, h, RenderTextureFormat.ARGBHalf, 0);
            if (!ink.IsCreated() || !surface.IsCreated() || !eyeDepth.IsCreated() || !seeds.IsCreated())
            {
                ReleaseTargets();
                return false;
            }
            targets[0] = ink; targets[1] = surface; targets[2] = eyeDepth;
            return true;
        }

        static RenderTexture Make(string label, int w, int h, RenderTextureFormat fmt, int depth)
        {
            var rt = new RenderTexture(w, h, depth, fmt, RenderTextureReadWrite.Linear) {
                name = label, hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };
            rt.Create();
            return rt;
        }

        internal void Render(RenderTexture source, RenderTexture destination)
        {
            if (!Enabled || !readyForComposite || !pen || !ink) { Graphics.Blit(source, destination); return; }
            pen.SetTexture("_Ink", ink);
            pen.SetTexture("_Surface", surface);
            pen.SetTexture("_EyeDepth", eyeDepth);
            pen.SetVector("_Pixel", new Vector4(1f / ink.width, 1f / ink.height, ink.width, ink.height));
            pen.SetFloat("_Width", lineWidth);
            pen.SetFloat("_Feather", featherPixels);
            pen.SetFloat("_StrokeReach", largestStrokeReach);
            pen.SetFloat("_CreaseCos", drawCreases ? Mathf.Cos(creaseAngle * Mathf.Deg2Rad) : -2f);
            Graphics.Blit(ink, seeds, pen, findEdgePass);
            pen.SetTexture("_Seeds", seeds);
            pen.SetTexture("_ShopSceneColor", source);
            Graphics.Blit(source, destination, pen, roundPenPass);
        }

        internal void Release()
        {
            readyForComposite = false;
            if (dataCommands != null)
            {
                if (eye) eye.RemoveCommandBuffer(CameraEvent.BeforeImageEffectsOpaque, dataCommands);
                dataCommands.Release(); dataCommands = null;
            }
            ReleaseTargets();
            if (pen) ShopLights.DestroyResource(pen);
            pen = null;
            eye = null;
        }

        void ReleaseTargets()
        {
            foreach (var rt in new[] { ink, surface, eyeDepth, seeds })
                if (rt) { rt.Release(); ShopLights.DestroyResource(rt); }
            ink = surface = eyeDepth = seeds = null;
        }
    }

    public static class ShopMaterialParameters
    {
        public const string ShaderName = "Shift/商店二分";
        private static readonly int BaseMap = Shader.PropertyToID("_BaseMap");
        private static readonly int HasBaseMap = Shader.PropertyToID("_HasBaseMap");

        // 材质 Inspector、批量写入以及运行时使用同一个判定，空槽恢复材质原先保存的纯色。
        public static void SyncBaseMap(Material material)
        {
            if (!material || !material.HasProperty(HasBaseMap)) return;
            float assigned = material.GetTexture(BaseMap) ? 1f : 0f;
            if (material.GetFloat(HasBaseMap) != assigned)
                material.SetFloat(HasBaseMap, assigned);
        }

        // 也适用于没有挂 ShopLights 的相机；传 null 会清空固有色贴图。
        public static void SetBaseMap(Material material, Texture texture)
        {
            if (!IsShopMaterial(material)) return;
            material.SetTexture(BaseMap, texture);
            SyncBaseMap(material);
        }

        // 在主线程调用。回调只设置需要更新的 Shader 属性，参数块由本方法复用。
        // 返回本次覆盖的商店材质槽数量，包含关闭的房间和停用渲染器。
        public static int Apply(Scene scene, Action<MaterialPropertyBlock> edit)
        {
            if (edit == null) throw new ArgumentNullException(nameof(edit));
            if (!scene.IsValid() || !scene.isLoaded) return 0;

            var block = new MaterialPropertyBlock();
            var renderers = new List<Renderer>();
            var materials = new List<Material>();
            int count = 0;
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                root.GetComponentsInChildren(true, renderers);
                foreach (Renderer renderer in renderers)
                {
                    renderer.GetSharedMaterials(materials);
                    if (materials.Count == 0)
                        continue;

                    // 全部槽都是商店材质时，未单独覆写的槽继续继承整物件的参数块。
                    bool allShop = materials.TrueForAll(IsShopMaterial);
                    bool editedRendererBlock = false;
                    for (int slot = 0; slot < materials.Count; slot++)
                    {
                        if (!IsShopMaterial(materials[slot]))
                            continue;
                        count++;
                        renderer.GetPropertyBlock(block, slot);
                        if (!block.isEmpty)
                        {
                            edit(block);
                            renderer.SetPropertyBlock(block, slot);
                        }
                        else if (!editedRendererBlock)
                        {
                            // 混用着色器时，复制已有整物件值，只把新设置写入商店材质槽。
                            renderer.GetPropertyBlock(block);
                            edit(block);
                            if (allShop)
                            {
                                renderer.SetPropertyBlock(block);
                                editedRendererBlock = true;
                            }
                            else
                            {
                                renderer.SetPropertyBlock(block, slot);
                            }
                        }
                    }
                }
            }
            return count;
        }

        static bool IsShopMaterial(Material material)
        {
            return material && material.shader && material.shader.name == ShaderName;
        }
    }
}
