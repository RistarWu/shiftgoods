using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace ShiftGame.ShopLab
{
    public sealed class ShopMaterialSettings : EditorWindow
    {
        public const string ShaderName = ShopMaterialParameters.ShaderName;
        public const string MenuPath = "工具/商店渲染/统一参数";

        static readonly ShopMaterialSection[] Pages =
        {
            ShopMaterialSection.Surface, ShopMaterialSection.Shadow, ShopMaterialSection.Lighting,
            ShopMaterialSection.Outline, ShopMaterialSection.Geometry, ShopMaterialSection.Advanced
        };
        static readonly string[] PageLabels = { "表面颜色", "阴影平滑", "灯光与距离", "描边", "模型曲面", "高级" };
        static readonly string[] ShadowSampling = { "softShadowSampleWidth", "shadowResolution" };
        static readonly string[] CornerFilter = { "corners.enabled", "corners.radiusPixels", "corners.edgeTransition", "corners.relativeSigma", "corners.cutoff" };
        static readonly string[] DepthProtection = { "corners.absoluteDepthTolerance", "corners.relativeDepthTolerance" };
        static readonly string[] OutlineSettings = { "outline.drawLines", "outline.lineWidth", "outline.featherPixels", "outline.drawCreases", "outline.creaseAngle" };

        [SerializeField] Material source;
        [SerializeField] Material draft;
        [SerializeField] Component sceneTarget;
        [SerializeField] Camera setupCamera;
        [SerializeField] ShopLights selectedRig;
        [SerializeField] List<string> selectedProperties = new List<string>();
        [SerializeField] ShopMaterialSection section = ShopMaterialSection.Shadow;
        [SerializeField] bool showDepthProtection;
        [SerializeField] Mesh meshSource;
        [SerializeField] float sharpAngle = ShopMeshPreparation.DefaultSharpAngle;
        [SerializeField] bool joinSplitEdges = true;
        Material[] materials = Array.Empty<Material>();
        string[] materialLabels = Array.Empty<string>();
        Component[] sceneTargets = Array.Empty<Component>();
        Editor materialEditor;
        Editor sceneEditor;
        Vector2 scroll;
        string result;

        [MenuItem(MenuPath)]
        public static void Open() => GetWindow<ShopMaterialSettings>("商店渲染参数");

        [MenuItem("工具/商店渲染/预处理细分网格")]
        static void OpenMeshPreparation()
        {
            var window = GetWindow<ShopMaterialSettings>("商店渲染参数");
            window.section = ShopMaterialSection.Geometry;
            window.scroll = Vector2.zero;
            window.UseSelectedMesh();
        }

        void OnEnable()
        {
            minSize = new Vector2(560, 320);
            Refresh();
            Undo.undoRedoPerformed += RefreshAfterUndo;
            EditorApplication.hierarchyChanged += Refresh;
            EditorApplication.projectChanged += Refresh;
            EditorSceneManager.activeSceneChangedInEditMode += ActiveSceneChanged;
            SceneManager.activeSceneChanged += ActiveSceneChanged;
        }

        void OnDisable()
        {
            Undo.undoRedoPerformed -= RefreshAfterUndo;
            EditorApplication.hierarchyChanged -= Refresh;
            EditorApplication.projectChanged -= Refresh;
            EditorSceneManager.activeSceneChangedInEditMode -= ActiveSceneChanged;
            SceneManager.activeSceneChanged -= ActiveSceneChanged;
            DestroyImmediate(materialEditor);
            DestroyImmediate(sceneEditor);
        }

        void OnDestroy() => DestroyImmediate(draft);

        void ActiveSceneChanged(Scene previous, Scene current) => Refresh();

        void RefreshAfterUndo()
        {
            if (source && selectedProperties.Count == 0)
                SetSource(source);
            result = null;
            Repaint();
        }

        void Refresh()
        {
            materials = FindMaterials();
            materialLabels = materials.Select(MaterialLabel).ToArray();
            Scene scene = SceneManager.GetActiveScene();
            sceneTargets = scene.IsValid() && scene.isLoaded
                ? scene.GetRootGameObjects()
                    .SelectMany(root => root.GetComponentsInChildren<Component>(true))
                    .Where(component => component is Camera || component is ShopLights || component is Light)
                    .ToArray()
                : Array.Empty<Component>();
            if (!sceneTargets.Contains(sceneTarget))
                sceneTarget = sceneTargets.FirstOrDefault();
            Repaint();
        }

        void OnGUI()
        {
            int current = Array.IndexOf(Pages, section);
            int next = GUILayout.Toolbar(Mathf.Max(0, current), PageLabels);
            if (next != current)
            {
                section = Pages[next];
                scroll = Vector2.zero;
                GUIUtility.ExitGUI();
            }

            if (materials.Length > 0 && (!materials.Contains(source) || !draft || draft.shader != source.shader))
                SetSource(materials.Contains(source) ? source : materials[0]);

            using (var view = new EditorGUILayout.ScrollViewScope(scroll))
            {
                scroll = view.scrollPosition;
                switch (section)
                {
                    case ShopMaterialSection.Shadow:
                        DrawShadows();
                        break;
                    case ShopMaterialSection.Outline:
                        DrawOutlines();
                        break;
                    case ShopMaterialSection.Lighting:
                        DrawLighting();
                        break;
                    case ShopMaterialSection.Advanced:
                        DrawSceneWiring();
                        break;
                }
                DrawMaterials();
                if (section == ShopMaterialSection.Geometry)
                    DrawMeshPreparation();
            }
            DrawApplyBar();
        }

        void DrawLighting()
        {
            if (!sceneTargets.OfType<ShopLights>().Any(component => component))
                DrawSceneSetup("当前场景没有逐灯光照组件。");

            var firstLight = sceneTargets.OfType<Light>().FirstOrDefault(light => light);
            using (new EditorGUI.DisabledScope(!firstLight))
            {
                if (GUILayout.Button("调整单盏灯"))
                {
                    if (sceneTarget is not Light)
                        sceneTarget = firstLight;
                    section = ShopMaterialSection.Advanced;
                    scroll = Vector2.zero;
                    GUIUtility.ExitGUI();
                }
            }
        }

        ShopLights DrawRig()
        {
            var rigs = sceneTargets.OfType<ShopLights>().Where(component => component).ToArray();
            if (rigs.Length == 0)
            {
                DrawSceneSetup("当前场景没有商店渲染组件。");
                return null;
            }

            int index = Mathf.Max(0, Array.IndexOf(rigs, selectedRig));
            if (rigs.Length > 1)
                index = EditorGUILayout.Popup("渲染组件", index, rigs.Select(rig => rig.name).ToArray());
            selectedRig = rigs[index];
            return selectedRig;
        }

        void DrawShadows()
        {
            EditorGUILayout.LabelField("当前场景 · 即时生效", EditorStyles.boldLabel);
            var rig = DrawRig();
            if (!rig)
                return;

            using var settings = new SerializedObject(rig);
            EditorGUILayout.LabelField("投影采样", EditorStyles.boldLabel);
            DrawSceneProperties(settings, ShadowSampling);
            EditorGUILayout.LabelField("明暗交界与投影圆角", EditorStyles.boldLabel);
            DrawSceneProperties(settings, CornerFilter);
            showDepthProtection = EditorGUILayout.Foldout(showDepthProtection, "深度保护", true);
            if (showDepthProtection)
            {
                using (new EditorGUI.IndentLevelScope())
                    DrawSceneProperties(settings, DepthProtection);
            }
        }

        void DrawOutlines()
        {
            EditorGUILayout.LabelField("当前场景 · 相机描边 · 即时生效", EditorStyles.boldLabel);
            var rig = DrawRig();
            if (!rig)
                return;

            using var settings = new SerializedObject(rig);
            DrawSceneProperties(settings, OutlineSettings);
        }

        void DrawSceneSetup(string message)
        {
            EditorGUILayout.HelpBox(message, MessageType.Info);
            Scene scene = SceneManager.GetActiveScene();
            if (!setupCamera || setupCamera.gameObject.scene != scene)
            {
                var mainCamera = Camera.main;
                setupCamera = mainCamera && mainCamera.gameObject.scene == scene
                    ? mainCamera
                    : sceneTargets.OfType<Camera>().FirstOrDefault();
            }

            using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode))
            {
                setupCamera = EditorGUILayout.ObjectField("安装到相机", setupCamera, typeof(Camera), true) as Camera;
                using (new EditorGUI.DisabledScope(!setupCamera || setupCamera.gameObject.scene != scene))
                    if (GUILayout.Button("为相机安装商店渲染"))
                    {
                        Camera camera = setupCamera;
                        EditorApplication.delayCall += () =>
                        {
                            if (!camera || camera.gameObject.scene != SceneManager.GetActiveScene() ||
                                EditorApplication.isPlayingOrWillChangePlaymode)
                                return;
                            ShopShaderSetup.Install(camera);
                            Refresh();
                        };
                    }
            }
        }

        void UseSelectedMesh()
        {
            if (Selection.activeObject is Mesh mesh)
                meshSource = mesh;
            else if (Selection.activeGameObject &&
                     Selection.activeGameObject.TryGetComponent(out MeshFilter filter))
                meshSource = filter.sharedMesh;
        }

        void DrawMeshPreparation()
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("预处理细分网格", EditorStyles.boldLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                meshSource = EditorGUILayout.ObjectField("原始网格", meshSource, typeof(Mesh), false) as Mesh;
                if (GUILayout.Button("使用所选网格", GUILayout.ExpandWidth(false)))
                    UseSelectedMesh();
            }
            sharpAngle = EditorGUILayout.Slider(new GUIContent("锐边角度", "相邻面法线的夹角超过此角度时，保留折痕。"),
                sharpAngle, 0f, 180f);
            joinSplitEdges = EditorGUILayout.Toggle(new GUIContent("跨接缝连接",
                "按精确重合的成对反向边识别 UV 接缝。若部件刻意共边但应保持独立，请关闭。"), joinSplitEdges);
            EditorGUILayout.HelpBox("需开启网格读写。工具生成网格副本，并在 UV3、UV4 写入细分控制数据。", MessageType.Info);
            using (new EditorGUI.DisabledScope(!meshSource || EditorApplication.isPlayingOrWillChangePlaymode))
                if (GUILayout.Button("保存预处理网格"))
                    SavePreparedMesh();
        }

        void SavePreparedMesh()
        {
            string path = EditorUtility.SaveFilePanelInProject("保存预处理网格", meshSource.name + "_Subdivision",
                "asset", "在项目中选择网格副本的位置。");
            if (string.IsNullOrEmpty(path))
                return;

            try
            {
                var mesh = ShopMeshPreparation.Prepare(meshSource, sharpAngle, joinSplitEdges);
                AssetDatabase.CreateAsset(mesh, AssetDatabase.GenerateUniqueAssetPath(path));
                AssetDatabase.SaveAssetIfDirty(mesh);
                Selection.activeObject = mesh;
                EditorGUIUtility.PingObject(mesh);
            }
            catch (ArgumentException error)
            {
                EditorUtility.DisplayDialog("无法预处理网格", error.Message, "确定");
            }
        }

        static ShopMaterialSection PropertyPage(string propertyName) => ShopMaterialSections.GetSection(propertyName) switch
        {
            ShopMaterialSection.Shadow => ShopMaterialSection.Surface,
            ShopMaterialSection.Distance => ShopMaterialSection.Lighting,
            ShopMaterialSection.Other => ShopMaterialSection.Advanced,
            var propertySection => propertySection
        };

        void DrawMaterials()
        {
            if (materials.Length == 0)
            {
                if (section != ShopMaterialSection.Shadow)
                    EditorGUILayout.HelpBox("项目中没有可编辑的商店二分材质。", MessageType.Info);
                return;
            }

            Editor.CreateCachedEditor(draft, typeof(MaterialEditor), ref materialEditor);
            MaterialProperty[] properties = MaterialEditor.GetMaterialProperties(new UnityEngine.Object[] { draft })
                .Where(property => PropertyPage(property.name) == section &&
                    IsEditableProperty(draft.shader, draft.shader.FindPropertyIndex(property.name))).ToArray();
            if (properties.Length == 0)
                return;

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("材质草稿 · 应用后生效", EditorStyles.boldLabel);

            int index = Array.IndexOf(materials, source);
            int next;
            bool reload;
            using (new EditorGUILayout.HorizontalScope())
            {
                next = EditorGUILayout.Popup("参数来源", index, materialLabels);
                reload = GUILayout.Button(new GUIContent("重新读取", "丢弃当前草稿和所有勾选，重新读取来源材质。"),
                    GUILayout.ExpandWidth(false));
            }
            if (next != index || reload)
            {
                SetSource(materials[next]);
                GUIUtility.ExitGUI();
            }

            EditorGUILayout.LabelField("修改时自动勾选。应用会写入各分类内所有勾选项。", EditorStyles.wordWrappedMiniLabel);
            foreach (MaterialProperty property in properties)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    bool selected = selectedProperties.Contains(property.name);
                    bool change = EditorGUILayout.ToggleLeft(new GUIContent("", $"应用时写入 {property.displayName}"),
                        selected, GUILayout.Width(20));
                    if (change && !selected)
                        selectedProperties.Add(property.name);
                    else if (!change && selected)
                        selectedProperties.Remove(property.name);

                    using (var changeCheck = new EditorGUI.ChangeCheckScope())
                    {
                        ((MaterialEditor)materialEditor).ShaderProperty(property,
                            new GUIContent(property.displayName, property.name));
                        if (changeCheck.changed && !selectedProperties.Contains(property.name))
                            selectedProperties.Add(property.name);
                    }
                }
            }
        }

        void DrawApplyBar()
        {
            if (!source || materials.Length == 0)
                return;

            int total = selectedProperties.Count;
            int here = selectedProperties.Count(property => PropertyPage(property) == section);
            EditorGUILayout.Space();
            using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField($"待应用 {total} 项（本页 {here} 项）", GUILayout.MinWidth(155));
                using (new EditorGUI.DisabledScope(total == 0))
                {
                    if (GUILayout.Button("清空勾选"))
                    {
                        selectedProperties.Clear();
                        GUIUtility.ExitGUI();
                    }
                    using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode))
                    {
                        if (GUILayout.Button("应用到来源"))
                            Apply(new[] { source });
                        if (GUILayout.Button($"应用到全部 {materials.Length} 个材质"))
                            Apply(materials);
                    }
                }
            }
            if (!string.IsNullOrEmpty(result))
                EditorGUILayout.LabelField(result, EditorStyles.miniLabel);
        }

        static string MaterialLabel(Material material)
        {
            string path = AssetDatabase.GetAssetPath(material);
            return string.IsNullOrEmpty(path) ? material.name : $"{material.name}  ({path})";
        }

        void SetSource(Material material)
        {
            source = material;
            DestroyImmediate(materialEditor);
            DestroyImmediate(draft);
            draft = new Material(material) { name = material.name, hideFlags = HideFlags.HideAndDontSave };
            selectedProperties.Clear();
            result = null;
        }

        void Apply(Material[] targets)
        {
            int count = ApplyProperties(draft, targets, selectedProperties);
            SetSource(source);
            result = $"已更新 {count} 个材质。";
            SceneView.RepaintAll();
        }

        static void DrawSceneProperties(SerializedObject settings, IEnumerable<string> names)
        {
            settings.Update();
            float previousLabelWidth = EditorGUIUtility.labelWidth;
            EditorGUIUtility.labelWidth = Mathf.Max(240, previousLabelWidth);
            try
            {
                foreach (string name in names)
                {
                    using var property = settings.FindProperty(name);
                    if (property != null)
                        EditorGUILayout.PropertyField(property, true);
                }
                if (settings.ApplyModifiedProperties())
                    SceneView.RepaintAll();
            }
            finally
            {
                EditorGUIUtility.labelWidth = previousLabelWidth;
            }
        }

        void DrawSceneWiring()
        {
            EditorGUILayout.LabelField("当前场景 · 单灯、相机与组件 · 即时生效", EditorStyles.boldLabel);
            if (!sceneTargets.OfType<ShopLights>().Any(component => component))
                DrawSceneSetup("当前场景没有逐灯光照组件。");
            if (sceneTargets.Length == 0)
                return;

            int index = Mathf.Max(0, Array.IndexOf(sceneTargets, sceneTarget));
            var labels = sceneTargets.Select(component => component ? ComponentLabel(component) : "").ToArray();
            sceneTarget = sceneTargets[EditorGUILayout.Popup("场景组件", index, labels)];
            if (!sceneTarget)
                return;

            switch (sceneTarget)
            {
                case ShopLights:
                    DrawOtherSceneProperties();
                    break;
                default:
                    Editor.CreateCachedEditor(sceneTarget, null, ref sceneEditor);
                    sceneEditor.OnInspectorGUI();
                    if (sceneTarget is Camera || sceneTarget is Light)
                        DrawTransform(sceneTarget.transform);
                    break;
            }
        }

        void DrawOtherSceneProperties()
        {
            using var settings = new SerializedObject(sceneTarget);
            using var iterator = settings.GetIterator();
            var names = new List<string>();
            var grouped = new HashSet<string>(ShadowSampling.Concat(CornerFilter).Concat(DepthProtection).Concat(OutlineSettings));
            bool enterChildren = true;
            while (iterator.NextVisible(enterChildren))
            {
                enterChildren = iterator.propertyPath is "corners" or "outline";
                if (!enterChildren && iterator.propertyPath != "m_Script" && !grouped.Contains(iterator.propertyPath))
                    names.Add(iterator.propertyPath);
            }
            DrawSceneProperties(settings, names);
        }

        static void DrawTransform(Transform transform)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("位置与方向", EditorStyles.boldLabel);
            using var changes = new EditorGUI.ChangeCheckScope();
            var position = EditorGUILayout.Vector3Field("局部位置", transform.localPosition);
            var rotation = EditorGUILayout.Vector3Field("局部旋转", transform.localEulerAngles);
            if (!changes.changed)
                return;

            Undo.RecordObject(transform, "调整商店相机或灯光");
            transform.localPosition = position;
            transform.localEulerAngles = rotation;
            PrefabUtility.RecordPrefabInstancePropertyModifications(transform);
            SceneView.RepaintAll();
        }

        static string ComponentLabel(Component component)
        {
            string kind = component switch
            {
                Camera => "相机",
                Light => "灯光",
                ShopLights => "逐灯光照",
                _ => component.GetType().Name
            };
            return $"{kind} / {component.name}";
        }

        public static Material[] FindMaterials()
        {
            var materials = new HashSet<Material>();
            foreach (string guid in AssetDatabase.FindAssets("t:Material", new[] { "Assets" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                foreach (var material in AssetDatabase.LoadAllAssetsAtPath(path).OfType<Material>())
                    if (CanEdit(material))
                        materials.Add(material);
            }

            // 同时纳入由当前打开场景持有的材质，包括未启用的物件或渲染器。
            foreach (Renderer renderer in FindObjectsByType<Renderer>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                foreach (Material material in renderer.sharedMaterials)
                    if (CanEdit(material))
                        materials.Add(material);

            return materials.OrderBy(material => material.name, StringComparer.Ordinal)
                .ThenBy(AssetDatabase.GetAssetPath, StringComparer.Ordinal).ToArray();
        }

        static bool CanEdit(Material material)
        {
            if (!material || !material.shader || material.shader.name != ShaderName ||
                (material.hideFlags & (HideFlags.NotEditable | HideFlags.DontSaveInEditor)) != 0)
                return false;
            if (!EditorUtility.IsPersistent(material))
                return true;
            string path = AssetDatabase.GetAssetPath(material);
            return path.StartsWith("Assets/", StringComparison.Ordinal) &&
                !(AssetImporter.GetAtPath(path) is ModelImporter) &&
                AssetDatabase.IsOpenForEdit(material);
        }

        static bool IsEditableProperty(Shader shader, int index)
        {
            if (index < 0 || ShopMaterialSections.IsInternal(shader.GetPropertyName(index)))
                return false;
            return (shader.GetPropertyFlags(index) &
                (ShaderPropertyFlags.HideInInspector | ShaderPropertyFlags.PerRendererData)) == 0;
        }

        // 程序入口使用 ShaderLab 的属性名，例如 "_OutlineColor"，名称与语言界面无关。
        public static int ApplyProperties(Material values, IEnumerable<Material> targets, IEnumerable<string> propertyNames)
        {
            if (!values || !values.shader || values.shader.name != ShaderName)
                throw new ArgumentException("参数来源必须使用商店二分着色器。", nameof(values));

            Shader shader = values.shader;
            var indices = propertyNames.Select(shader.FindPropertyIndex)
                .Where(index => IsEditableProperty(shader, index)).Distinct().ToArray();
            var materials = targets.Where(target => target != values && CanEdit(target) && target.shader == shader)
                .Distinct().ToArray();
            if (indices.Length == 0 || materials.Length == 0)
                return 0;

            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName("应用商店渲染参数");
            Undo.RecordObjects(materials, "应用商店渲染参数");
            foreach (Material material in materials)
            {
                foreach (int index in indices)
                    CopyProperty(values, material, shader, index);
                ShopMaterialParameters.SyncBaseMap(material);
                MaterialEditor.ApplyMaterialPropertyDrawers(material);
                EditorUtility.SetDirty(material);
            }

            Undo.FlushUndoRecordObjects();
            foreach (Material material in materials)
                if (EditorUtility.IsPersistent(material))
                    AssetDatabase.SaveAssetIfDirty(material);
            // 内嵌材质随其所属场景保存。
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (scene.isLoaded && scene.GetRootGameObjects().Any(root =>
                    root.GetComponentsInChildren<Renderer>(true).Any(renderer =>
                        renderer.sharedMaterials.Any(shared =>
                            shared && !EditorUtility.IsPersistent(shared) && materials.Contains(shared)))))
                    EditorSceneManager.MarkSceneDirty(scene);
            }
            return materials.Length;
        }

        static void CopyProperty(Material values, Material target, Shader shader, int index)
        {
            string name = shader.GetPropertyName(index);
            int id = Shader.PropertyToID(name);
            switch (shader.GetPropertyType(index))
            {
                case ShaderPropertyType.Color:
                    target.SetColor(id, values.GetColor(id));
                    break;
                case ShaderPropertyType.Vector:
                    target.SetVector(id, values.GetVector(id));
                    break;
                case ShaderPropertyType.Int:
                    target.SetInteger(id, values.GetInteger(id));
                    break;
                case ShaderPropertyType.Float:
                case ShaderPropertyType.Range:
                    target.SetFloat(id, values.GetFloat(id));
                    break;
                case ShaderPropertyType.Texture:
                    target.SetTexture(id, values.GetTexture(id));
                    if ((shader.GetPropertyFlags(index) & ShaderPropertyFlags.NoScaleOffset) == 0)
                    {
                        target.SetTextureScale(name, values.GetTextureScale(name));
                        target.SetTextureOffset(name, values.GetTextureOffset(name));
                    }
                    break;
            }
        }
    }

    internal enum ShopMaterialSection
    {
        Surface,
        Lighting,
        Shadow,
        Outline,
        Geometry,
        Distance,
        Other,
        Advanced
    }

    // 材质 Inspector 和风格窗口共用分组。未登记的公开属性归到「其他」。
    internal static class ShopMaterialSections
    {
        public static IReadOnlyList<ShopMaterialSection> OrderedSections { get; } =
            Array.AsReadOnly((ShopMaterialSection[])Enum.GetValues(typeof(ShopMaterialSection)));

        public static ShopMaterialSection GetSection(string propertyName) => propertyName switch
        {
            "_BaseColor" or "_BaseMap" => ShopMaterialSection.Surface,
            "_LightColorGain" or "_LightIntensityWeight" or "_IntensityThreshold" or "_DirectionThreshold"
                => ShopMaterialSection.Lighting,
            "_ShadowTint" or "_ShadowTintDepth" => ShopMaterialSection.Shadow,
            "_OutlineEnabled" or "_OutlineColor" or "_OutlineWidthScale" or "_OutlineFeatherScale"
                => ShopMaterialSection.Outline,
            "_TessFactor" or "_TessEdgePixels" or "_Smoothness" => ShopMaterialSection.Geometry,
            "_FadeDistance" => ShopMaterialSection.Distance,
            _ => ShopMaterialSection.Other
        };

        public static string GetLabel(ShopMaterialSection section) => section switch
        {
            ShopMaterialSection.Surface => "固有色",
            ShopMaterialSection.Lighting => "灯光与明暗",
            ShopMaterialSection.Shadow => "阴影",
            ShopMaterialSection.Outline => "描边",
            ShopMaterialSection.Geometry => "几何与细分",
            ShopMaterialSection.Distance => "距离衰减",
            ShopMaterialSection.Other => "其他",
            ShopMaterialSection.Advanced => "高级",
            _ => throw new ArgumentOutOfRangeException(nameof(section))
        };

        public static bool IsInternal(string propertyName) => propertyName is "_ShopObjectId" or "_HasBaseMap";
    }

    public sealed class ShopMaterialInspector : ShaderGUI
    {
        // Unity 在导入材质或 Inspector 修改属性时调用。逐材质同步，也适用于多选编辑。
        public override void ValidateMaterial(Material material) => ShopMaterialParameters.SyncBaseMap(material);

        readonly HashSet<ShopMaterialSection> expanded = new HashSet<ShopMaterialSection>
        {
            ShopMaterialSection.Surface,
            ShopMaterialSection.Lighting,
            ShopMaterialSection.Shadow,
            ShopMaterialSection.Outline
        };

        public override void OnGUI(MaterialEditor materialEditor, MaterialProperty[] properties)
        {
            materialEditor.SetDefaultGUIWidths();
            var sections = properties.Where(IsVisible)
                .ToLookup(property => ShopMaterialSections.GetSection(property.name));
            foreach (ShopMaterialSection section in ShopMaterialSections.OrderedSections)
            {
                bool advanced = section == ShopMaterialSection.Advanced;
                if (!advanced && !sections.Contains(section))
                    continue;

                bool open = EditorGUILayout.BeginFoldoutHeaderGroup(expanded.Contains(section),
                    ShopMaterialSections.GetLabel(section));
                EditorGUILayout.EndFoldoutHeaderGroup();
                if (!open)
                {
                    expanded.Remove(section);
                    continue;
                }

                expanded.Add(section);
                using (new EditorGUI.IndentLevelScope())
                {
                    if (advanced)
                        materialEditor.RenderQueueField();

                    // 使用 Unity 的属性绘制，以保留混合值、撤销、属性抽屉和材质变体支持。
                    foreach (MaterialProperty property in sections[section])
                        materialEditor.ShaderProperty(property, new GUIContent(property.displayName, property.name));
                }
                EditorGUILayout.Space();
            }
        }

        static bool IsVisible(MaterialProperty property) =>
            !ShopMaterialSections.IsInternal(property.name) &&
            (property.propertyFlags & (ShaderPropertyFlags.HideInInspector | ShaderPropertyFlags.PerRendererData)) == 0;
    }

    public static class ShopShaderSetup
    {
        public static void Install(Camera camera)
        {
            if (!camera || EditorUtility.IsPersistent(camera) || !camera.gameObject.scene.isLoaded ||
                EditorApplication.isPlayingOrWillChangePlaymode)
                return;

            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            const string operation = "为相机安装商店渲染";
            Undo.SetCurrentGroupName(operation);
            if (camera.renderingPath != RenderingPath.Forward || !camera.allowHDR)
            {
                Undo.RecordObject(camera, operation);
                camera.renderingPath = RenderingPath.Forward;
                camera.allowHDR = true;
                PrefabUtility.RecordPrefabInstancePropertyModifications(camera);
            }

            var rig = camera.GetComponent<ShopLights>();
            if (!rig)
                rig = Undo.AddComponent<ShopLights>(camera.gameObject);

            using var settings = new SerializedObject(rig);
            using var renderCamera = settings.FindProperty("renderCamera");
            if (renderCamera.objectReferenceValue != camera)
            {
                renderCamera.objectReferenceValue = camera;
                settings.ApplyModifiedProperties();
            }
            Undo.FlushUndoRecordObjects();
            Undo.CollapseUndoOperations(group);
            SceneView.RepaintAll();
        }
    }

    /// <summary>给 GPU 细分准备静态三角网格。处理过程可以拆顶点，但保持原始三角形数量和材质分组。</summary>
    internal static class ShopMeshPreparation
    {
        public const float DefaultSharpAngle = 35f;

        private struct EdgeSide
        {
            public int Start;
            public int End;
        }

        /// <summary>
        /// 生成独立副本。精确重合的反向边可跨 UV 缝连接。
        /// 当模型包含刻意接触的不同部件时，可关闭该选项，仅按源索引识别邻接。
        /// UV3 和 UV4 由 ShopLab 细分着色器使用。
        /// </summary>
        public static Mesh Prepare(Mesh source, float sharpAngle = DefaultSharpAngle, bool joinSplitEdges = true)
        {
            if (source == null)
                throw new ArgumentNullException(nameof(source));
            if (!source.isReadable || source.bindposes.Length > 0 || source.blendShapeCount > 0)
                throw new ArgumentException("预处理需要开启读写且不含蒙皮或混合形状的静态网格。", nameof(source));

            Vector3[] positions = source.vertices;
            var sourceCorners = new List<int>();
            var submeshOffsets = new int[source.subMeshCount + 1];
            for (int i = 0; i < source.subMeshCount; i++)
            {
                if (source.GetTopology(i) != MeshTopology.Triangles)
                    throw new ArgumentException("预处理仅支持三角网格。", nameof(source));
                sourceCorners.AddRange(source.GetTriangles(i));
                submeshOffsets[i + 1] = sourceCorners.Count;
            }

            var positionIds = new int[positions.Length];
            var points = new Dictionary<Vector3, int>();
            for (int i = 0; i < positions.Length; i++)
            {
                if (!joinSplitEdges)
                {
                    positionIds[i] = i;
                    continue;
                }
                if (!points.TryGetValue(positions[i], out int id))
                {
                    id = points.Count;
                    points.Add(positions[i], id);
                }
                positionIds[i] = id;
            }

            int count = sourceCorners.Count;
            var faceNormals = new Vector3[count / 3];
            var weightedNormals = new Vector3[count];
            var parents = new int[count];
            var topologyParents = new int[count];
            var edges = new Dictionary<(int, int), List<EdgeSide>>();
            for (int root = 0; root < count; root += 3)
            {
                Vector3 a = positions[sourceCorners[root]];
                Vector3 b = positions[sourceCorners[root + 1]];
                Vector3 c = positions[sourceCorners[root + 2]];
                Vector3 normal = Vector3.Cross(b - a, c - a).normalized;
                faceNormals[root / 3] = normal;
                for (int offset = 0; offset < 3; offset++)
                {
                    int corner = root + offset;
                    int next = root + (offset + 1) % 3;
                    int prev = root + (offset + 2) % 3;
                    parents[corner] = corner;
                    topologyParents[corner] = corner;
                    Vector3 vertex = positions[sourceCorners[corner]];
                    // 按顶点处的夹角积累法线，避免四边形换三角剖分后法线偏向面积较大的面。
                    float angle = Vector3.Angle(positions[sourceCorners[next]] - vertex,
                                                positions[sourceCorners[prev]] - vertex);
                    weightedNormals[corner] = normal * angle;
                    int from = positionIds[sourceCorners[corner]];
                    int to = positionIds[sourceCorners[next]];
                    if (from == to || normal == Vector3.zero)
                        continue;
                    var key = (Mathf.Min(from, to), Mathf.Max(from, to));
                    if (!edges.TryGetValue(key, out List<EdgeSide> sides))
                    {
                        sides = new List<EdgeSide>(2);
                        edges.Add(key, sides);
                    }
                    sides.Add(new EdgeSide { Start = corner, End = next });
                }
            }

            float limit = Mathf.Cos(Mathf.Clamp(sharpAngle, 0f, 180f) * Mathf.Deg2Rad);
            var hardCorners = new bool[count];
            var irregularCorners = new bool[count];
            var sharpEdges = new List<EdgeSide>();
            foreach (List<EdgeSide> sides in edges.Values)
            {
                // 只在一对反向半边上连接角点。仅共享位置的角点和多面交叉的边不参与焊接。
                EdgeSide first = sides[0];
                if (sides.Count == 2)
                {
                    EdgeSide second = sides[1];
                    bool reversed = positionIds[sourceCorners[first.Start]] == positionIds[sourceCorners[second.End]];
                    float cosine = Vector3.Dot(faceNormals[first.Start / 3], faceNormals[second.Start / 3]);
                    if (reversed)
                    {
                        // 拓扑扇区跨过流形折痕，着色扇区仅跨过软边。两者都按共边连通。
                        Join(topologyParents, first.Start, second.End);
                        Join(topologyParents, first.End, second.Start);
                        if (cosine >= limit - 0.000001f)
                        {
                            Join(parents, first.Start, second.End);
                            Join(parents, first.End, second.Start);
                        }
                        else
                        {
                            sharpEdges.Add(first);
                            hardCorners[first.Start] = hardCorners[first.End] = true;
                            hardCorners[second.Start] = hardCorners[second.End] = true;
                        }
                        continue;
                    }
                }
                foreach (EdgeSide side in sides)
                {
                    irregularCorners[side.Start] = irregularCorners[side.End] = true;
                    if (sides.Count > 1)
                        hardCorners[side.Start] = hardCorners[side.End] = true;
                }
            }

            var smoothNormals = new Vector3[count];
            var linearControl = new bool[count];
            var irregularGroups = new bool[count];
            var sharpDegree = new int[count];
            var sharpFirst = new Vector3[count];
            var sharpSecond = new Vector3[count];
            foreach (EdgeSide edge in sharpEdges)
            {
                Vector3 direction = (positions[sourceCorners[edge.End]] - positions[sourceCorners[edge.Start]]).normalized;
                AddSharpDirection(Find(topologyParents, edge.Start), direction, sharpDegree, sharpFirst, sharpSecond);
                AddSharpDirection(Find(topologyParents, edge.End), -direction, sharpDegree, sharpFirst, sharpSecond);
            }
            for (int i = 0; i < count; i++)
            {
                int group = Find(parents, i);
                smoothNormals[group] += weightedNormals[i];
                linearControl[group] |= hardCorners[i];
                irregularGroups[Find(topologyParents, i)] |= irregularCorners[i];
            }
            var creaseNormals = new Vector3[count];
            for (int i = 0; i < count; i++)
            {
                if (sharpDegree[i] != 2 || irregularGroups[i])
                    continue;
                Vector3 first = sharpFirst[i];
                Vector3 second = sharpSecond[i];
                Vector3 bisector = first + second;
                // 两条折痕围出的折线转角是 -first 与 second 的夹角。仅平滑转向可按角平分线弯曲，
                // 近直线时角平分线数值不稳定，角点或支路交叉时也保留原折痕。
                if (bisector.sqrMagnitude > .000001f && Vector3.Dot(-first, second) >= limit - .000001f)
                    creaseNormals[i] = bisector.normalized;
            }

            var remap = new Dictionary<(int, int), int>();
            var sourceIndices = new List<int>();
            var normals = new List<Vector3>();
            var edgeNormals = new List<Vector3>();
            var marker = new List<Vector2>();
            var indices = new int[count];
            Vector3[] originalNormals = source.normals;
            for (int i = 0; i < count; i++)
            {
                int vertex = sourceCorners[i];
                int group = Find(parents, i);
                if (!remap.TryGetValue((vertex, group), out int newIndex))
                {
                    newIndex = sourceIndices.Count;
                    remap.Add((vertex, group), newIndex);
                    sourceIndices.Add(vertex);
                    Vector3 smooth = smoothNormals[group].normalized;
                    if (smooth == Vector3.zero)
                        smooth = originalNormals.Length == positions.Length ? originalNormals[vertex] : Vector3.up;
                    normals.Add(smooth);
                    // 同一折痕拓扑扇区共用控制法线，着色仍保留各面的独立平滑法线。
                    int topology = Find(topologyParents, i);
                    edgeNormals.Add(sharpDegree[topology] > 0 ? creaseNormals[topology] :
                                    linearControl[group] ? Vector3.zero : smooth);
                    marker.Add(Vector2.right);
                }
                indices[i] = newIndex;
            }

            var result = new Mesh
            {
                name = source.name + " · 细分数据",
                indexFormat = sourceIndices.Count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16,
                subMeshCount = source.subMeshCount
            };
            result.SetVertices(Remap(positions, sourceIndices));
            result.SetNormals(normals);
            Vector4[] tangents = source.tangents;
            if (tangents.Length == positions.Length)
                result.SetTangents(Remap(tangents, sourceIndices));
            Color[] colors = source.colors;
            if (colors.Length == positions.Length)
                result.SetColors(Remap(colors, sourceIndices));
            for (int channel = 0; channel < 8; channel++)
            {
                if (channel == 2 || channel == 3)
                    continue;
                CopyUv(source, result, channel, sourceIndices);
            }
            // UV3.xyz 供几何边插值，UV4.x 告诉着色器这份控制数据已经烘焙。
            result.SetUVs(2, edgeNormals);
            result.SetUVs(3, marker);
            for (int i = 0; i < source.subMeshCount; i++)
                result.SetTriangles(indices, submeshOffsets[i], submeshOffsets[i + 1] - submeshOffsets[i], i, false);
            if (tangents.Length == positions.Length && source.HasVertexAttribute(VertexAttribute.TexCoord0))
                result.RecalculateTangents();
            result.bounds = source.bounds;
            return result;
        }

        // 并查集只连接相邻面上的角点，每个平滑扇区拥有独立法线。
        private static int Find(int[] parents, int index)
        {
            while (parents[index] != index)
            {
                parents[index] = parents[parents[index]];
                index = parents[index];
            }
            return index;
        }

        private static void Join(int[] parents, int a, int b)
        {
            parents[Find(parents, a)] = Find(parents, b);
        }

        private static void AddSharpDirection(int group, Vector3 direction, int[] degree,
                                              Vector3[] first, Vector3[] second)
        {
            if (degree[group] == 0)
                first[group] = direction;
            else if (degree[group] == 1)
                second[group] = direction;
            degree[group]++;
        }

        private static List<T> Remap<T>(IReadOnlyList<T> source, List<int> indices)
        {
            var result = new List<T>(indices.Count);
            foreach (int index in indices)
                result.Add(source[index]);
            return result;
        }

        private static void CopyUv(Mesh source, Mesh destination, int channel, List<int> indices)
        {
            var attribute = (VertexAttribute)((int)VertexAttribute.TexCoord0 + channel);
            if (!source.HasVertexAttribute(attribute))
                return;
            switch (source.GetVertexAttributeDimension(attribute))
            {
                case 4:
                    var uv4 = new List<Vector4>();
                    source.GetUVs(channel, uv4);
                    destination.SetUVs(channel, Remap(uv4, indices));
                    break;
                case 3:
                    var uv3 = new List<Vector3>();
                    source.GetUVs(channel, uv3);
                    destination.SetUVs(channel, Remap(uv3, indices));
                    break;
                default:
                    var uv2 = new List<Vector2>();
                    source.GetUVs(channel, uv2);
                    destination.SetUVs(channel, Remap(uv2, indices));
                    break;
            }
        }
    }
}
