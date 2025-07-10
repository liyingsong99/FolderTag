using UnityEditor;
using UnityEngine;
using System.Text.RegularExpressions;

namespace FolderTag
{
    /// <summary>
    /// 基础检查器类，提供文件夹和场景标签编辑的通用功能
    /// </summary>
    public abstract class BaseInspector : Editor
    {
        private static bool showPreview;

        /// <summary>
        /// 验证当前选择是否适用于此检查器
        /// </summary>
        /// <returns>如果适用返回true，否则返回false</returns>
        protected abstract bool IsValidSelection();

        /// <summary>
        /// 获取是否为场景检查器
        /// </summary>
        /// <returns>如果是场景检查器返回true，否则返回false</returns>
        protected abstract bool IsSceneInspector();

        public override void OnInspectorGUI()
        {
            if (Selection.assetGUIDs.Length != 1)
                return;

            // 场景检查器需要额外检查是否启用场景标签
            if (IsSceneInspector() && !FolderSettings.Opt_EnableSceneTag.Value)
                return;

            if (!IsValidSelection())
                return;

            var guid = Selection.assetGUIDs[0];
            var path = AssetDatabase.GUIDToAssetPath(guid);

            var folderData = FolderSettings.GetFolderData(guid, path, out bool subFolder);

            GUI.enabled = true;
            bool create = folderData == null;
            if (create)
            {
                folderData = FolderSettings.CreateFolderData(IsSceneInspector());
                folderData._guid = guid;
                folderData._tag = string.Empty;
                folderData._desc = string.Empty;
            }

            EditorGUI.BeginChangeCheck();

            // 标签输入和验证
            DrawTagField(folderData);

            GUILayout.Space(5);

            // 描述输入
            EditorGUILayout.LabelField("Description");
            folderData._desc = EditorGUILayout.TextArea(folderData._desc, EditorStyles.textArea, GUILayout.MinHeight(300));

            if (EditorGUI.EndChangeCheck())
            {
                if (create) 
                    FolderSettings.AddFoldersList(folderData);

                // 清除当前GUID的缓存，确保立即刷新显示
                FoldersBrowser.ClearSpecificCache(guid);
                
                EditorApplication.RepaintProjectWindow();
                FolderSettings.SaveProjectPrefs();
            }

            GUILayout.Space(10);

            // 清理按钮
            if (GUILayout.Button("Clean Empty Data"))
            {
                FolderSettings.CleanEmptyData();
            }

            GUILayout.Space(10);

            // 预览折叠面板
            DrawPreviewSection();

            GUI.enabled = false;
        }

        /// <summary>
        /// 绘制标签输入字段并进行验证
        /// </summary>
        /// <param name="folderData">文件夹数据</param>
        private void DrawTagField(FolderSettings.FolderData folderData)
        {
            EditorGUILayout.LabelField("Tag");
            var strTag = EditorGUILayout.TextField(folderData._tag, EditorStyles.textField);

            // 输入验证和清理：限制标签长度为50个字符，并移除无效字符
            if (!string.IsNullOrEmpty(strTag))
            {
                // 移除控制字符和多余空白
                strTag = System.Text.RegularExpressions.Regex.Replace(strTag, @"[\x00-\x1F\x7F]", "");
                strTag = strTag.Trim();
                
                // 限制长度
                if (strTag.Length > 50)
                {
                    strTag = strTag.Substring(0, 50);
                    EditorGUILayout.HelpBox("标签已被截断至50个字符", MessageType.Warning);
                }
            }
            
            folderData._tag = strTag ?? string.Empty;
        }

        /// <summary>
        /// 绘制预览部分
        /// </summary>
        private void DrawPreviewSection()
        {
            string title = showPreview ? " ∧ Hide Tags Preview" : " ∨ Show Tags Preview";
            showPreview = EditorGUILayout.BeginFoldoutHeaderGroup(showPreview, title);
            if (showPreview)
            {
                FolderSettings.GetFoldersList(isScene: IsSceneInspector()).DoLayoutList();
            }
        }
    }
} 