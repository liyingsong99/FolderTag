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
        private const string mPrefsPath = "ProjectSettings\\" + mPrefsFile;
        private const int mGradientWidth = 16;

        public static EditorOption<string> Opt_FolderTagPath = new EditorOption<string>(nameof(FolderTag) + "_FolderTagPath", mPrefsPath);
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
            public DictionaryData<string, FolderData> FoldersData;

            [Serializable]
            public class DictionaryData<TKey, TValue>
            {
                public List<TKey> Keys;
                public List<TValue> Values;

                public IEnumerable<KeyValuePair<TKey, TValue>> Enumerate()
                {
                    if (Keys == null || Values == null)
                        yield break;

                    for (var n = 0; n < Keys.Count; n++)
                        yield return new KeyValuePair<TKey, TValue>(Keys[n], Values[n]);
                }

                public DictionaryData()
                    : this(new List<TKey>(), new List<TValue>())
                {
                }

                public DictionaryData(List<TKey> keys, List<TValue> values)
                {
                    Keys = keys;
                    Values = values;
                }

                public DictionaryData(IEnumerable<KeyValuePair<TKey, TValue>> data)
                {
                    var pairs = data as KeyValuePair<TKey, TValue>[] ?? data.ToArray();
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

            if (File.Exists(Opt_FolderTagPath.Value))
            {
                try
                {
                    using var file = File.OpenText(Opt_FolderTagPath.Value);
                    var jsonContent = file.ReadToEnd();
                    
                    if (!string.IsNullOrWhiteSpace(jsonContent))
                    {
                        var data = JsonUtility.FromJson<JsonWrapper>(jsonContent);
                        
                        if (data?.FoldersData != null)
                        {
                            listFoldersData = data.FoldersData
                                .Enumerate()
                                .Where(n => n.Value != null && !string.IsNullOrEmpty(n.Value._guid))
                                .Select(n => n.Value)
                                .ToList();
                        }

                        if (data != null)
                        {
                            FoldersDescColor = data.LabelColor;
                        }
                    }
                }
                catch (UnauthorizedAccessException ex)
                {
                    Debug.LogError($"[FolderTag] 无法访问配置文件：{ex.Message}");
                }
                catch (DirectoryNotFoundException ex)
                {
                    Debug.LogError($"[FolderTag] 配置文件目录不存在：{ex.Message}");
                }
                catch (FileNotFoundException ex)
                {
                    Debug.LogWarning($"[FolderTag] 配置文件不存在，将使用默认设置：{ex.Message}");
                }
                catch (System.ArgumentException ex)
                {
                    Debug.LogError($"[FolderTag] 配置文件JSON格式错误：{ex.Message}");
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[FolderTag] 加载配置文件时发生未知错误：{ex.Message}");
                }
            }

            //从Resources目录中加载所有命名为FolderTag.json 的额外数据文件 
            var allTagJsons = Resources.LoadAll<TextAsset>("FolderTag_Prefs");
            foreach (var tagJson in allTagJsons)
            {
                var data = JsonUtility.FromJson<JsonWrapper>(tagJson.text);
                var list = data.FoldersData
                    .Enumerate()
                    .Select(n => n.Value)
                    .ToList();

                foreach (var item in list)
                {
                    if (listFoldersData.Any(n => n._guid == item._guid))
                        continue;

                    listFoldersData.Add(item);
                }
            }

            dicFoldersData = listFoldersData.ToDictionary(n => n._guid, n => n);

            UpdateGradient();
            EditorApplication.projectWindowItemOnGUI += FoldersBrowser.DrawFolderMemos;
        }

        public override void OnGUI(string searchContext)
        {
            // editor prefs variables
            EditorGUI.BeginChangeCheck();

            var folderTagPath = EditorGUILayout.TextField("Data Save Path", Opt_FolderTagPath.Value);
            if (folderTagPath != Opt_FolderTagPath.Value)
            {
                Opt_FolderTagPath.Value = folderTagPath;
                EditorApplication.RepaintProjectWindow();
            }

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
                SaveProjectPrefs();
            }

            GetFoldersList().DoLayoutList();
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

                if (EditorGUI.EndChangeCheck())
                {
                    var fodlerGuid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(folder));

                    if (element._guid != fodlerGuid)
                    {
                        // ignore non directory files
                        if (folder != null && !File.GetAttributes(AssetDatabase.GetAssetPath(folder)).HasFlag(FileAttributes.Directory)
                            && !FolderHelper.IsValidScene(AssetDatabase.GetAssetPath(folder)))
                        {
                            folder = null;
                        }

                        // ignore if already contains
                        if (folder != null && listFoldersData.Any(n => n._guid == fodlerGuid))
                            folder = null;
                    }

                    element._guid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(folder));
                    
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
                
                SaveProjectPrefs();
                EditorApplication.RepaintProjectWindow();
            };
            currentPreviewList.onAddCallback = data => { listFoldersData.Add(CreateFolderData(isScene)); };
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
                    var searchGuid = AssetDatabase.GUIDFromAssetPath(searchPath).ToString();

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

        public static void SaveProjectPrefs()
        {
            try
            {
                // 确保数据有效性
                if (listFoldersData == null)
                {
                    listFoldersData = new List<FolderData>();
                }

                dicFoldersData = listFoldersData
                    .Where(n => n != null && !string.IsNullOrEmpty(n._guid) && n._guid != Guid.Empty.ToString())
                    .ToDictionary(n => n._guid, n => n);

                var json = new JsonWrapper()
                {
                    LabelColor = FoldersDescColor,
                    FoldersData = new JsonWrapper.DictionaryData<string, FolderData>(dicFoldersData
                        .Values
                        .Select(n => new KeyValuePair<string, FolderData>(n._guid, n)))
                };

                var jsonString = JsonUtility.ToJson(json, true);
                
                // 确保目录存在
                var directory = Path.GetDirectoryName(Opt_FolderTagPath.Value);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                File.WriteAllText(Opt_FolderTagPath.Value, jsonString);
                
                // 清除缓存，确保下次绘制时显示最新数据
                FoldersBrowser.ClearCache();
            }
            catch (UnauthorizedAccessException ex)
            {
                Debug.LogError($"[FolderTag] 无法写入配置文件，权限不足：{ex.Message}");
            }
            catch (DirectoryNotFoundException ex)
            {
                Debug.LogError($"[FolderTag] 配置文件目录不存在：{ex.Message}");
            }
            catch (IOException ex)
            {
                Debug.LogError($"[FolderTag] 写入配置文件时发生IO错误：{ex.Message}");
            }
            catch (System.ArgumentException ex)
            {
                Debug.LogError($"[FolderTag] 序列化配置数据时发生错误：{ex.Message}");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[FolderTag] 保存配置文件时发生未知错误：{ex.Message}");
            }
        }
    }
}