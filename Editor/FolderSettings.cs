using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;
using Random = UnityEngine.Random;


namespace FolderTag
{
    public class FolderSettings : SettingsProvider
    {
        #region ============================== Definition ==============================

        private const string mPrefsFile = nameof(FolderTag) + "_Prefs.json";
        private static readonly string mPrefsPath = Path.Combine("ProjectSettings", mPrefsFile);
        private const int mGradientWidth = 16;
        private const string mLegacyPathKey = nameof(FolderTag) + "_FolderTagPath";

        /// <summary>
        /// 配置文件加载状态。Corrupted = 文件存在但读不出来，此时禁止写入，
        /// 避免用内存里的空数据/部分数据覆盖原始文件。
        /// </summary>
        public enum LoadState
        {
            FileMissing,
            Ok,
            Corrupted
        }

        private static LoadState mLoadState = LoadState.Corrupted;
        private static string mLoadError;
        private static string mLoadedContent;
        private static bool mHasUnsavedChanges;
        private static EditorOption<string> mOptFolderTagPath;

        public static LoadState CurrentLoadState { get { return mLoadState; } }
        public static string LoadErrorMessage { get { return mLoadError; } }
        public static bool HasUnsavedChanges { get { return mHasUnsavedChanges; } }

        /// <summary>
        /// 用户实际改动了数据时调用。脏标记权威来源是"用户改过"，
        /// 而不是"写失败了"，这样 Corrupted 拒写、冲突取消等分支也不会漏掉未保存状态。
        /// </summary>
        public static void MarkDirty()
        {
            mHasUnsavedChanges = true;
        }

        private static bool ConfirmBeforeReplacingMemory()
        {
            return !mHasUnsavedChanges || EditorUtility.DisplayDialog(
                "存在未保存的修改", "继续将放弃当前编辑。需要保留时，请先取消并保存副本。",
                "放弃修改并继续", "取消");
        }


        /// <summary>
        /// 保存路径按工程隔离，避免 A 工程的绝对路径被 B 工程继承。
        /// </summary>
        public static EditorOption<string> Opt_FolderTagPath
        {
            get
            {
                if (mOptFolderTagPath == null)
                    mOptFolderTagPath = new EditorOption<string>(GetProjectPathKey(), mPrefsPath);
                return mOptFolderTagPath;
            }
        }

        /// <summary>
        /// 工程身份 = productGUID + 本地工程绝对路径哈希。
        /// 只用 productGUID 时，复制出来的工程（保留同一份 ProjectSettings）会共用同一个键；
        /// 加上路径哈希后，同一台机器上的两个副本不会串，同一份工程在不同机器上也仍能各自独立。
        /// </summary>
        private static string GetProjectIdentity()
        {
            var root = Path.GetFullPath(Path.Combine(Application.dataPath, "..")).Replace('\\', '/');
            if (Environment.OSVersion.Platform == PlatformID.Win32NT)
                root = root.ToLowerInvariant();
            var pathHash = ComputeHash(root).Substring(0, 16);

            try
            {
                var productGuid = PlayerSettings.productGUID;
                if (productGuid != Guid.Empty)
                    return productGuid.ToString("N") + "_" + pathHash;
            }
            catch (Exception)
            {
                // 忽略，只用路径哈希
            }

            return pathHash;
        }

        private static string GetMigrationFlagKey()
        {
            return nameof(FolderTag) + "_PathMigrated_" + GetProjectIdentity();
        }

        private static string GetProjectPathKey()
        {
            return mLegacyPathKey + "_" + GetProjectIdentity();
        }
        public static EditorOption<bool> Opt_EnableSceneTag = new EditorOption<bool>(nameof(FolderTag) + "_EnableSceneTag", true);
        public static EditorOption<bool> Opt_ShowGradient = new EditorOption<bool>(nameof(FolderTag) + "_ShowGradient", true);
        public static EditorOption<bool> Opt_InspectorEdit = new EditorOption<bool>(nameof(FolderTag) + "_InspectorEdit", true);
        public static EditorOption<Color> Opt_SubFoldersTint = new EditorOption<Color>(nameof(FolderTag) + "_SubFoldersTint", new Color(0.7f, 0.7f, 0.7f, 0.7f));
        private static EditorOption<Vector2> Opt_GradientScale = new EditorOption<Vector2>(nameof(FolderTag) + "_GradientScale", new Vector2(0.7f, 1f));
        private static Texture2D texGradient;

        public static Texture Gradient
        {
            get
            {
                if (texGradient == null)
                    UpdateGradient();

                return texGradient;
            }
        }


        public static Color FoldersDescColor { get; private set; }
        private static Color mFoldersDescTint = Color.white;

        private static Dictionary<string, FolderData> dicFoldersData;
        private static List<FolderData> listFoldersData;
        private static ReorderableList foldersList, scenesList;
        private static readonly object dataLock = new object(); // 线程安全锁

        #endregion

        #region ============================== Serializable ==============================

        [Serializable]
        private class JsonWrapper
        {
            public Color LabelColor;
            public DictionaryData FoldersData;

            // Unity 2019.4 会忽略自定义泛型字段；使用具体类型并保留既有 JSON 字段名。
            [Serializable]
            public class DictionaryData
            {
                public List<string> Keys;
                public List<FolderData> Values;

                public IEnumerable<KeyValuePair<string, FolderData>> Enumerate()
                {
                    if (Keys == null || Values == null)
                        yield break;

                    for (var n = 0; n < Keys.Count; n++)
                        yield return new KeyValuePair<string, FolderData>(Keys[n], Values[n]);
                }

                public DictionaryData()
                {
                    // 不给缺失字段补空列表，保留 JsonUtility 读取残缺配置时的判定依据。
                }

                public DictionaryData(List<string> keys, List<FolderData> values)
                {
                    Keys = keys;
                    Values = values;
                }

                public DictionaryData(IEnumerable<KeyValuePair<string, FolderData>> data)
                {
                    var pairs = data as KeyValuePair<string, FolderData>[] ?? data.ToArray();
                    Keys = pairs.Select(n => n.Key).ToList();
                    Values = pairs.Select(n => n.Value).ToList();
                }
            }
        }

        [Serializable]
        public class FolderData
        {
            public string _guid;
            public bool _isScene;
            public Color _color;
            public bool _recursive;
            public string _tag = "folder tag";
            public string _desc = "";
        }

        #endregion

        public FolderSettings(string path, SettingsScope scopes, IEnumerable<string> keywords = null) : base(path, scopes, keywords)
        {
        }

        [SettingsProvider]
        public static SettingsProvider CreateSettingsProvider()
        {
            return new FolderSettings("Preferences/Folder Tag", SettingsScope.User);
        }

        [InitializeOnLoadMethod]
        private static void InitializeOnLoad()
        {
            // 初始化默认值
            listFoldersData = new List<FolderData>();
            FoldersDescColor = mFoldersDescTint;

            LoadProjectPrefs();

            dicFoldersData = BuildDictionary(listFoldersData);
            UpdateGradient();
            EditorApplication.projectWindowItemOnGUI += FoldersBrowser.DrawFolderMemos;
        }

        /// <summary>
        /// 按 GUID 去重建索引：重复保留首条并警告，空 GUID 跳过，绝不抛异常。
        /// </summary>
        private static Dictionary<string, FolderData> BuildDictionary(List<FolderData> source)
        {
            var result = new Dictionary<string, FolderData>();
            if (source == null)
                return result;

            foreach (var item in source)
            {
                if (item == null || string.IsNullOrEmpty(item._guid))
                    continue;

                // 与原实现保持一致：全零 GUID 视为无效数据
                if (item._guid == Guid.Empty.ToString())
                    continue;

                if (result.ContainsKey(item._guid))
                {
                    Debug.LogWarning($"[FolderTag] 配置中存在重复的 GUID，已保留首条：{item._guid}");
                    continue;
                }

                result[item._guid] = item;
            }

            return result;
        }

        /// <summary>
        /// 一次加载的完整结果，先在临时对象里算好，成功后才提交到静态状态。
        /// </summary>
        private sealed class PrefsSnapshot
        {
            public LoadState State;
            public string Error;
            public List<FolderData> Folders = new List<FolderData>();
            public Color LabelColor;
            public string Content;
        }

        /// <summary>
        /// 纯读取：只依赖传入路径，不修改任何静态状态。失败时返回对应状态的快照。
        /// </summary>
        private static PrefsSnapshot ReadPrefs(string path)
        {
            var snapshot = new PrefsSnapshot
            {
                State = LoadState.Corrupted,
                LabelColor = mFoldersDescTint,
                Folders = new List<FolderData>()
            };

            if (string.IsNullOrEmpty(path))
            {
                snapshot.Error = "配置路径为空";
                return snapshot;
            }

            if (!File.Exists(path))
            {
                snapshot.State = LoadState.FileMissing;
                return snapshot;
            }

            try
            {
                string jsonContent;
                using (var file = File.OpenText(path))
                {
                    jsonContent = file.ReadToEnd();
                }

                if (string.IsNullOrWhiteSpace(jsonContent))
                {
                    snapshot.Error = "配置文件内容为空";
                    return snapshot;
                }

                var data = JsonUtility.FromJson<JsonWrapper>(jsonContent);
                if (data == null)
                {
                    snapshot.Error = "配置文件不是合法的 JSON";
                    return snapshot;
                }

                // JsonUtility 会把 null/缺失的容器或列表补成空实例，必须同时核对源 JSON 的字段形状。
                var dictionaryStart = FindJsonMember(jsonContent, jsonContent.IndexOf('{'), "FoldersData");
                if (data.FoldersData == null || dictionaryStart < 0 || jsonContent[dictionaryStart] != '{')
                {
                    snapshot.Error = "配置文件缺少 FoldersData 字段";
                    return snapshot;
                }

                var keys = data.FoldersData.Keys;
                var values = data.FoldersData.Values;
                var keysStart = FindJsonMember(jsonContent, dictionaryStart, "Keys");
                var valuesStart = FindJsonMember(jsonContent, dictionaryStart, "Values");
                if (keysStart < 0 || valuesStart < 0 || jsonContent[keysStart] != '[' || jsonContent[valuesStart] != '[' ||
                    keys == null || values == null || keys.Count != values.Count)
                {
                    snapshot.Error = "配置文件的 Keys 与 Values 数量不一致";
                    return snapshot;
                }

                var loaded = new List<FolderData>();
                var duplicated = 0;
                var emptyGuid = 0;
                var mismatchedGuid = 0;
                var known = new HashSet<string>();

                for (var n = 0; n < values.Count; n++)
                {
                    var value = values[n];
                    if (value == null || string.IsNullOrEmpty(value._guid))
                    {
                        emptyGuid++;
                        continue;
                    }

                    if (!string.Equals(keys[n], value._guid, StringComparison.Ordinal))
                        mismatchedGuid++;

                    if (!known.Add(value._guid))
                    {
                        duplicated++;
                        continue;
                    }

                    loaded.Add(value);
                }

                snapshot.Folders = loaded;
                snapshot.LabelColor = data.LabelColor;
                snapshot.Content = jsonContent;

                // 主配置里有坏条目时不能当成正常加载：
                // 下次保存会把被跳过的条目永久写掉，所以进保护状态等用户明确处理。
                if (emptyGuid > 0 || duplicated > 0 || mismatchedGuid > 0)
                {
                    snapshot.State = LoadState.Corrupted;
                    snapshot.Error = "主配置存在异常条目（空 GUID " + emptyGuid + " 条、重复 GUID " + duplicated + " 条、键值 GUID 不一致 " + mismatchedGuid + " 条），已暂停写入";
                    return snapshot;
                }

                snapshot.State = LoadState.Ok;
                return snapshot;
            }
            catch (FileNotFoundException)
            {
                // 检查与打开之间被删掉：按"不存在"处理，允许重新创建。
                snapshot.State = LoadState.FileMissing;
                return snapshot;
            }
            catch (UnauthorizedAccessException ex)
            {
                snapshot.Error = "无法访问配置文件：" + ex.Message;
            }
            catch (DirectoryNotFoundException ex)
            {
                snapshot.Error = "配置文件目录不存在：" + ex.Message;
            }
            catch (Exception ex)
            {
                snapshot.Error = "读取配置文件失败：" + ex.Message;
            }

            return snapshot;
        }

        public static void LoadProjectPrefs()
        {
            OpenPrefs(Opt_FolderTagPath.Value, false);
        }

        // 重载和打开共用此入口；先读到有效快照，再确认并提交，失败保留当前编辑。
        private static bool OpenPrefs(string path, bool changePath)
        {
            var snapshot = ReadPrefs(path);
            if (snapshot.State == LoadState.Corrupted || (changePath && snapshot.State == LoadState.FileMissing))
            {
                mLoadError = snapshot.Error ?? "目标文件不存在。";
                if (!changePath) mLoadState = LoadState.Corrupted;
                return false;
            }
            if (!ConfirmBeforeReplacingMemory()) return false;
            if (changePath) Opt_FolderTagPath.Value = path;
            mLoadState = snapshot.State;
            mLoadedContent = snapshot.Content;
            mLoadError = null;
            listFoldersData = snapshot.Folders;
            FoldersDescColor = snapshot.LabelColor;
            mHasUnsavedChanges = false;
            LoadResourcePrefs();
            dicFoldersData = BuildDictionary(listFoldersData);
            FoldersBrowser.ClearCache();
            EditorApplication.RepaintProjectWindow();
            return true;
        }

        /// <summary>
        /// 从 Resources 目录收集额外配置。单个文件损坏只跳过该文件，不中断初始化。
        /// </summary>
        public static void LoadResourcePrefs()
        {
            TextAsset[] allTagJsons;
            try
            {
                allTagJsons = Resources.LoadAll<TextAsset>("FolderTag_Prefs");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[FolderTag] 读取 Resources 配置文件失败：{ex.Message}");
                return;
            }

            foreach (var tagJson in allTagJsons)
            {
                if (tagJson == null || string.IsNullOrEmpty(tagJson.text))
                    continue;

                try
                {
                    var data = JsonUtility.FromJson<JsonWrapper>(tagJson.text);
                    if (data == null || data.FoldersData == null)
                    {
                        Debug.LogWarning($"[FolderTag] 跳过无法解析的 Resources 配置：{tagJson.name}");
                        continue;
                    }

                    var keys = data.FoldersData.Keys;
                    var values = data.FoldersData.Values;
                    if (keys == null || values == null || keys.Count != values.Count)
                    {
                        Debug.LogWarning($"[FolderTag] 跳过键值数量不一致的 Resources 配置：{tagJson.name}");
                        continue;
                    }

                    for (var n = 0; n < values.Count; n++)
                    {
                        var item = values[n];
                        if (item == null || string.IsNullOrEmpty(item._guid))
                            continue;

                        if (listFoldersData.Any(existing => existing._guid == item._guid))
                            continue;

                        listFoldersData.Add(item);
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[FolderTag] 跳过损坏的 Resources 配置 {tagJson.name}：{ex.Message}");
                }
            }
        }

        public override void OnGUI(string searchContext)
        {
            DrawMigrationBanner();
            DrawLoadStatePanel();

            // editor prefs variables
            EditorGUI.BeginChangeCheck();

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Data Save Path", Opt_FolderTagPath.Value);
            if (GUILayout.Button("打开配置", GUILayout.Width(80f)))
            {
                var picked = EditorUtility.OpenFilePanel("打开 FolderTag 配置", Application.dataPath, "json");
                if (!string.IsNullOrEmpty(picked)) OpenPrefs(picked, true);
            }
            EditorGUILayout.EndHorizontal();

            var enableSceneTag = EditorGUILayout.Toggle("Enable Scene Tag", Opt_EnableSceneTag.Value);
            if (enableSceneTag != Opt_EnableSceneTag.Value)
            {
                Opt_EnableSceneTag.Value = enableSceneTag;
                EditorApplication.RepaintProjectWindow();
            }

            var showGradient = EditorGUILayout.Toggle("Show Gradient", Opt_ShowGradient.Value);

            var gradientScale = Opt_GradientScale.Value;
            EditorGUILayout.MinMaxSlider("Gradient Scale", ref gradientScale.x, ref gradientScale.y, 0f, 1f);

            var subFoldersTint = EditorGUILayout.ColorField("Sub Folders Tint", Opt_SubFoldersTint.Value);

            if (EditorGUI.EndChangeCheck())
            {
                Opt_ShowGradient.Value = showGradient;
                Opt_GradientScale.Value = gradientScale;
                Opt_SubFoldersTint.Value = subFoldersTint;

                EditorApplication.RepaintProjectWindow();
                UpdateGradient();
            }

            // project prefs variables
            EditorGUI.BeginChangeCheck();

            FoldersDescColor = EditorGUILayout.ColorField("Desc Color", FoldersDescColor);

            if (EditorGUI.EndChangeCheck())
            {
                EditorApplication.RepaintProjectWindow();
                MarkDirty();
                SaveProjectPrefs();
            }

            GetFoldersList().DoLayoutList();
        }

        /// <summary>
        /// 旧全局路径键的迁移入口：只作候选展示，用户确认后才写入本工程的键。
        /// </summary>
        private void DrawMigrationBanner()
        {
            var migratedKey = GetMigrationFlagKey();
            if (EditorPrefs.GetBool(migratedKey, false))
                return;

            if (EditorPrefs.HasKey(GetProjectPathKey()))
                return;

            if (!EditorPrefs.HasKey(mLegacyPathKey))
                return;

            var legacyPath = EditorPrefs.GetString(mLegacyPathKey, string.Empty);
            if (string.IsNullOrEmpty(legacyPath))
                return;

            EditorGUILayout.HelpBox(
                "检测到旧版本的全局保存路径：\n" + legacyPath + "\n它可能是其他工程设置的，是否在本工程使用该路径？",
                MessageType.Info);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("在本工程使用该路径"))
            {
                // 切换流程只确认一次；取消或失败时保留迁移入口。
                if (OpenPrefs(legacyPath, true))
                    EditorPrefs.SetBool(migratedKey, true);
            }
            if (GUILayout.Button("忽略（使用默认路径）"))
            {
                EditorPrefs.SetBool(migratedKey, true);
            }
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.Space(4);
        }

        /// <summary>
        /// 更新渐变纹理，用于文件夹背景显示
        /// 根据GradientScale设置生成不同的透明度渐变效果
        /// </summary>
        private static void UpdateGradient()
        {
            // 释放之前的纹理资源以防止内存泄漏
            if (texGradient != null)
            {
                if (Application.isPlaying)
                    UnityEngine.Object.Destroy(texGradient);
                else
                    UnityEngine.Object.DestroyImmediate(texGradient);
            }

            // 创建新的渐变纹理
            texGradient = new Texture2D(mGradientWidth, 1);
            texGradient.wrapMode = TextureWrapMode.Clamp;
            var range = Opt_GradientScale.Value;

            // 如果范围是完整的0-1，则创建完全不透明的纹理
            if (range == new Vector2(0, 1))
            {
                for (var x = 0; x < mGradientWidth; x++)
                    texGradient.SetPixel(x, 0, new Color(1, 1, 1, 1));
            }
            else
            {
                // 根据GradientScale范围创建渐变效果
                for (var x = 0; x < mGradientWidth; x++)
                    texGradient.SetPixel(x, 0, new Color(1, 1, 1, _getAlpha(x)));

                /// <summary>
                /// 计算指定像素位置的透明度值
                /// 在range范围内为完全不透明，范围外渐变到透明
                /// </summary>
                /// <param name="xPixel">像素的X坐标</param>
                /// <returns>透明度值(0-1)</returns>
                float _getAlpha(int xPixel)
                {
                    // 将像素坐标转换为0-1的比例
                    var xScale = xPixel / (mGradientWidth - 1f);

                    // 如果在设定范围内，返回完全不透明
                    if (xScale >= range.x && xScale <= range.y)
                        return 1f;

                    // 计算距离最近边界的距离，并应用渐变衰减
                    var distance = xScale < range.x ? range.x - xScale : xScale - range.y;
                    return Mathf.Clamp01(1f - distance * 3f); // 3f是衰减速度系数
                }
            }

            // 应用像素修改到纹理
            texGradient.Apply();
        }

        public static void AddFoldersList(FolderData folderData)
        {
            lock (dataLock)
            {
                if (listFoldersData == null)
                {
                    listFoldersData = new List<FolderData>();
                }
                
                listFoldersData.Add(folderData);
                
                // 更新字典
                if (dicFoldersData == null)
                {
                    dicFoldersData = new Dictionary<string, FolderData>();
                }
                
                if (!string.IsNullOrEmpty(folderData._guid))
                {
                    dicFoldersData[folderData._guid] = folderData;
                }
                
                GetFoldersList(true);
            }
        }

        private static ReorderableList scenePreviewList, folderPreviewList;
        public static ReorderableList GetFoldersList(bool forceNew = false, bool isScene = false)
        {
            var currentPreviewList = isScene ? scenePreviewList : folderPreviewList;
            if (currentPreviewList != null && !forceNew)
                return currentPreviewList;

            float lineHeight = EditorGUIUtility.singleLineHeight;
            var listPreview = listFoldersData.Where(n => n._isScene == isScene).ToList();

            currentPreviewList = new ReorderableList(listPreview, typeof(FolderData), true, true, true, true);
            currentPreviewList.drawElementCallback = (rect, index, isActive, isFocused) =>
            {
                var element = listPreview[index];

                var refRect = new Rect(rect.position + new Vector2(0f, 1f),
                    new Vector2(rect.size.x * .5f - EditorGUIUtility.standardVerticalSpacing, lineHeight));
                var colorRect = new Rect(rect.position + new Vector2(rect.size.x * .5f, 1f),
                    new Vector2(rect.size.x * .5f - 50f - EditorGUIUtility.standardVerticalSpacing, lineHeight));
                var recRect = new Rect(rect.position + new Vector2(rect.size.x - 50f, 1f), new Vector2(18f, lineHeight));
                var tagRect = new Rect(rect.position + new Vector2(0f, lineHeight + 2f), new Vector2(rect.size.x, lineHeight));

                var deleteRect = new Rect(rect.position + new Vector2(rect.size.x - 18f, 1f), new Vector2(18f, lineHeight));
                if (GUI.Button(deleteRect, "x"))
                {
                    listFoldersData.Remove(element);
                    dicFoldersData.Remove(element._guid);
                    
                    // 清除被删除项的缓存
                    FoldersBrowser.ClearSpecificCache(element._guid);
                    
                    MarkDirty();
                    SaveProjectPrefs();
                    EditorApplication.RepaintProjectWindow();
                }

                EditorGUI.BeginChangeCheck();

                UnityEngine.Object preview = null;
                Type type = typeof(DefaultAsset);
                if (isScene)
                {
                    preview = AssetDatabase.LoadAssetAtPath<SceneAsset>(AssetDatabase.GUIDToAssetPath(element._guid));
                    type = typeof(SceneAsset);
                }
                else
                {
                    preview = AssetDatabase.LoadAssetAtPath<DefaultAsset>(AssetDatabase.GUIDToAssetPath(element._guid));
                }

                var folder = EditorGUI.ObjectField(refRect,
                    GUIContent.none,
                    preview,
                    type,
                    false);

                // 单独记录 ObjectField 本身的变化，避免把"资源暂时解析不到"误判成用户清空
                var objectFieldChanged = EditorGUI.EndChangeCheck();
                EditorGUI.BeginChangeCheck();

                element._color = EditorGUI.ColorField(colorRect, GUIContent.none, element._color);
                element._recursive = EditorGUI.Toggle(recRect, GUIContent.none, element._recursive);
                var strTag = EditorGUI.TextField(tagRect, GUIContent.none, element._tag);

                // Limit tag length to 50 characters
                if (strTag.Length > 50)
                {
                    element._tag = strTag.Substring(0, 50);
                }
                else
                {
                    element._tag = strTag;
                }

                // 在任何提前返回或短路判断前结束检查，保持 GUI 检查栈配对。
                var fieldsChanged = EditorGUI.EndChangeCheck();
                var guidChanged = false;

                if (objectFieldChanged)
                {
                    // 只有 ObjectField 本身变了才处理 GUID 关联，tag/color 的编辑不会碰到 GUID
                    var path = AssetDatabase.GetAssetPath(folder);
                    var candidateGuid = string.IsNullOrEmpty(path) ? string.Empty : AssetDatabase.AssetPathToGUID(path);

                    if (folder == null)
                    {
                        // 用户确实把关联项拖空：允许解除关联
                        if (!string.IsNullOrEmpty(element._guid))
                        {
                            element._guid = string.Empty;
                            guidChanged = true;
                        }
                    }
                    else
                    {
                        var isDirectory = File.GetAttributes(path).HasFlag(FileAttributes.Directory);
                        var validType = isDirectory || FolderHelper.IsValidScene(path);
                        var alreadyUsed = listFoldersData.Any(n => n != element && n._guid == candidateGuid);

                        if (!validType || alreadyUsed)
                        {
                            // 校验不通过：保留原 GUID 与原有记录，不改动数据
                            Debug.LogWarning($"[FolderTag] 已忽略无效或重复的资源拖入：{path}");
                            EditorApplication.RepaintProjectWindow();
                            return;
                        }

                        if (element._guid != candidateGuid)
                        {
                            element._guid = candidateGuid;
                            guidChanged = true;
                        }
                    }
                }

                // 对象换了/清空了，或 color/tag 改了，任一成立都要置脏并保存
                if (guidChanged || fieldsChanged)
                {
                    MarkDirty();

                    if (guidChanged)
                        dicFoldersData = BuildDictionary(listFoldersData);

                    // 清除当前GUID的缓存，确保立即刷新显示
                    FoldersBrowser.ClearSpecificCache(element._guid);
                    
                    SaveProjectPrefs();

                    EditorApplication.RepaintProjectWindow();
                }
            };
            currentPreviewList.elementHeight = lineHeight * 2 + 3;
            currentPreviewList.onRemoveCallback = data =>
            {
                var folderData = listPreview[data.index];
                listFoldersData.Remove(folderData);
                dicFoldersData.Remove(folderData._guid);
                
                // 清除被删除项的缓存
                FoldersBrowser.ClearSpecificCache(folderData._guid);
                
                MarkDirty();
                SaveProjectPrefs();
                EditorApplication.RepaintProjectWindow();
            };
            currentPreviewList.onAddCallback = data =>
            {
                listFoldersData.Add(CreateFolderData(isScene));
                MarkDirty();
            };
            currentPreviewList.drawHeaderCallback = rect => { EditorGUI.LabelField(rect, new GUIContent(isScene ? "Scenes" : "Folders", "")); };

            return currentPreviewList;
        }

        public static FolderData CreateFolderData(bool isScene = false)
        {
            var color = Color.HSVToRGB(Random.value, 0.7f, 0.7f);
            color.a = 0.7f;
            return new FolderData() { _color = color, _isScene = isScene };
        }

        /// <summary>
        /// 为"继承父目录标签的子目录"创建独立记录。
        /// 复制父记录的 color/tag/desc 作为初始值（显示上不变样），并关闭 _recursive，
        /// 避免一编辑就把父目录的继承关系改掉。
        /// </summary>
        public static FolderData CreateOverride(string guid, bool isScene, FolderData inherited)
        {
            var data = new FolderData()
            {
                _guid = guid,
                _isScene = isScene,
                _recursive = false
            };

            if (inherited != null)
            {
                data._color = inherited._color;
                data._tag = inherited._tag;
                data._desc = inherited._desc;
            }

            return data;
        }

        /// <summary>
        /// 获取指定GUID和路径的文件夹数据
        /// 如果直接匹配不到，会递归向上查找父文件夹的数据（如果启用了递归选项）
        /// </summary>
        /// <param name="guid">资源的GUID</param>
        /// <param name="path">资源的路径</param>
        /// <param name="subFolder">输出参数，表示是否为子文件夹（通过父文件夹继承的数据）</param>
        /// <returns>找到的文件夹数据，如果没有找到返回null</returns>
        public static FolderData GetFolderData(string guid, string path, out bool subFolder)
        {
            lock (dataLock)
            {
                subFolder = false;
                
                // 确保数据字典已初始化
                if (dicFoldersData == null)
                {
                    dicFoldersData = new Dictionary<string, FolderData>();
                    return null;
                }

                // 首先尝试直接匹配GUID
                if (dicFoldersData.TryGetValue(guid, out var folderData))
                    return folderData;

                // 如果直接匹配失败，开始递归向上查找父文件夹
                subFolder = true;
                var searchPath = path;
                while (folderData == null)
                {
                    // 获取父目录路径
                    searchPath = Path.GetDirectoryName(searchPath);
                    if (string.IsNullOrEmpty(searchPath))
                        return null; // 已经到达根目录，没有找到

                    // 获取父目录的GUID并查找对应的数据
                    var searchGuid = AssetDatabase.AssetPathToGUID(searchPath);

                    dicFoldersData.TryGetValue(searchGuid, out folderData);
                    
                    // 如果找到了数据但没有启用递归，则不应该继承给子文件夹
                    if (folderData != null && !folderData._recursive)
                        return null;
                }

                return folderData;
            }
        }

        public static void CleanEmptyData()
        {
            MarkDirty();
            lock (dataLock)
            {
                if (dicFoldersData == null || listFoldersData == null)
                    return;

                // 移除dicFoldersData中guid指向的资源不存在的数据
                var itemsToRemove = new List<FolderData>();
                
                foreach (var item in dicFoldersData.Values.ToArray())
                {
                    if (item == null || string.IsNullOrEmpty(item._guid))
                    {
                        itemsToRemove.Add(item);
                        continue;
                    }

                    var path = AssetDatabase.GUIDToAssetPath(item._guid);
                    if (string.IsNullOrEmpty(path))
                    {
                        itemsToRemove.Add(item);
                        continue;
                    }

                    // 检查资源是否仍然存在
                    var asset = AssetDatabase.LoadAssetAtPath<DefaultAsset>(path);
                    if (asset == null && !FolderHelper.IsValidScene(path))
                    {
                        itemsToRemove.Add(item);
                    }
                }

                // 批量移除无效数据
                foreach (var item in itemsToRemove)
                {
                    if (item != null && !string.IsNullOrEmpty(item._guid))
                    {
                        dicFoldersData.Remove(item._guid);
                    }
                    listFoldersData.Remove(item);
                }

                SaveProjectPrefs();
            }
        }

        private void DrawLoadStatePanel()
        {
            if (!string.IsNullOrEmpty(mLoadError))
                EditorGUILayout.HelpBox(mLoadError, MessageType.Warning);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("重新加载")) LoadProjectPrefs();
            if (GUILayout.Button("保存副本"))
            {
                var path = EditorUtility.SaveFilePanel("新建 FolderTag 副本", Application.dataPath, "FolderTag_Prefs_copy.json", "json");
                if (!string.IsNullOrEmpty(path)) SaveCopy(path);
            }
            EditorGUILayout.EndHorizontal();
        }

        private static string SerializeCurrentPrefs()
        {
            var data = BuildDictionary(listFoldersData);
            return JsonUtility.ToJson(new JsonWrapper
            {
                LabelColor = FoldersDescColor,
                FoldersData = new JsonWrapper.DictionaryData(data)
            }, true);
        }

        // 副本只新建，不切路径、不清脏，也不允许覆盖已有文件。
        private static bool SaveCopy(string path)
        {
            try
            {
                var content = SerializeCurrentPrefs();
                using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write))
                using (var writer = new StreamWriter(stream)) writer.Write(content);
                return true;
            }
            catch (Exception ex)
            {
                mLoadError = "保存副本失败（已有文件不会覆盖）：" + ex.Message;
                return false;
            }
        }

        // 单编辑器普通文件：外部变化只拒写，不强制覆盖，不提供跨进程同步协议。
        public static bool SaveProjectPrefs()
        {
            if (mLoadState == LoadState.Corrupted) return false;
            try
            {
                var path = Opt_FolderTagPath.Value;
                var diskContent = File.Exists(path) ? File.ReadAllText(path) : null;
                if (!string.Equals(diskContent, mLoadedContent, StringComparison.Ordinal))
                {
                    mLoadError = "配置已在外部改变，未保存当前编辑。请先保存副本或重新加载。";
                    mHasUnsavedChanges = true;
                    return false;
                }
                var content = SerializeCurrentPrefs();
                if (!WriteAtomically(path, content, mLoadedContent == null))
                {
                    mHasUnsavedChanges = true;
                    return false;
                }
                mLoadedContent = content;
                mLoadState = LoadState.Ok;
                mLoadError = null;
                mHasUnsavedChanges = false;
                dicFoldersData = BuildDictionary(listFoldersData);
                FoldersBrowser.ClearCache();
                return true;
            }
            catch (Exception ex)
            {
                mLoadError = "保存失败，当前编辑尚未写入：" + ex.Message;
                mHasUnsavedChanges = true;
                return false;
            }
        }

        // 只定位指定对象的直属字段，跳过字符串和嵌套值，避免说明文本中的字段名误命中。
        // JSON 的值解析仍交给 JsonUtility；这里只保留它会丢失的“字段是否存在”信息。
        private static int FindJsonMember(string json, int objectStart, string name)
        {
            if (objectStart < 0 || json[objectStart] != '{') return -1;
            var depth = 0;
            for (var i = objectStart; i < json.Length; i++)
            {
                var c = json[i];
                if (c == '"')
                {
                    var start = ++i;
                    while (i < json.Length && json[i] != '"')
                    {
                        if (json[i] == '\\') i++;
                        i++;
                    }
                    if (i >= json.Length) return -1;
                    var end = i;
                    var next = i + 1;
                    while (next < json.Length && char.IsWhiteSpace(json[next])) next++;
                    if (depth == 1 && next < json.Length && json[next] == ':')
                    {
                        var field = System.Text.RegularExpressions.Regex.Unescape(json.Substring(start, end - start));
                        if (field == name)
                        {
                            do { next++; } while (next < json.Length && char.IsWhiteSpace(json[next]));
                            return next < json.Length ? next : -1;
                        }
                    }
                }
                else if (c == '{' || c == '[') depth++;
                else if (c == '}' || c == ']')
                {
                    depth--;
                    if (depth == 0) return -1;
                }
            }
            return -1;
        }

        private static string ComputeHash(string content)
        {
            using (var md5 = System.Security.Cryptography.MD5.Create())
            {
                var hash = md5.ComputeHash(System.Text.Encoding.UTF8.GetBytes(content ?? string.Empty));
                return BitConverter.ToString(hash).Replace("-", string.Empty).ToLowerInvariant();
            }
        }

        /// <summary>
        /// 原子写入：先写同目录临时文件（. 前缀 + .tmp~ 后缀，Unity 不 import），
        /// 关闭后再替换目标。替换失败只报错，原文件保持不动，绝不先删后移。
        /// </summary>
        private static bool WriteAtomically(string targetPath, string content, bool expectNotExist)
        {
            var directory = Path.GetDirectoryName(targetPath);
            if (string.IsNullOrEmpty(directory))
                directory = ".";

            var fileName = Path.GetFileName(targetPath);
            var tempPath = Path.Combine(directory, "." + fileName + "." + Guid.NewGuid().ToString("N") + ".tmp~");

            try
            {
                Directory.CreateDirectory(directory);
                File.WriteAllText(tempPath, content);

                if (expectNotExist)
                {
                    // 首次创建：直接落位
                    File.Move(tempPath, targetPath);
                }
                else
                {
                    var backupDir = Path.Combine("Library", nameof(FolderTag));
                    Directory.CreateDirectory(backupDir);
                    // 每个来源只保留上一版；不以无限备份扩展成历史版本管理。
                    var backupPath = Path.Combine(backupDir, ComputeHash(Path.GetFullPath(targetPath)) + ".bak");
                    File.Replace(tempPath, targetPath, backupPath);
                }

                return true;
            }
            catch (Exception ex)
            {
                mLoadError = "写入失败，原文件与当前编辑已保留：" + ex.Message;
                TryDelete(tempPath);
                return false;
            }
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch (Exception ex)
            {
                // 删除失败不威胁数据，但要留痕，避免临时文件无限堆积
                Debug.LogWarning("[FolderTag] 清理临时文件失败，可能残留：" + path + "（" + ex.Message + "）");
            }
        }
    }
}