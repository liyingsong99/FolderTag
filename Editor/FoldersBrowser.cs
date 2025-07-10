using UnityEditor;
using UnityEngine;
using System.Collections.Generic;

namespace FolderTag
{
    public static class FoldersBrowser
    {
        private static GUIStyle s_labelNormal;
        private static GUIStyle s_labelSelected;
        private static readonly Dictionary<string, CachedFolderData> folderDataCache = new Dictionary<string, CachedFolderData>();
        private static int lastFrameCount = -1;

        // 缓存数据结构
        private struct CachedFolderData
        {
            public FolderSettings.FolderData Data;
            public bool IsSubFolder;
            public int FrameCount;
            public bool IsValid;
        }

        public static void DrawFolderMemos(string guid, Rect rect)
        {
            // 避免在同一帧内重复处理相同的GUID
            if (lastFrameCount == Time.frameCount && folderDataCache.ContainsKey(guid))
            {
                var cachedData = folderDataCache[guid];
                if (cachedData.IsValid)
                {
                    DrawFolderTagCached(guid, rect, cachedData.Data, cachedData.IsSubFolder);
                }
                return;
            }

            DrawFolderTag(guid, rect);
            lastFrameCount = Time.frameCount;
        }

        private static void DrawFolderTag(string guid, Rect rect)
        {
            if (rect.width < rect.height)
                return;

            var path = AssetDatabase.GUIDToAssetPath(guid);
            if (!FolderHelper.IsValidFolder(path) && !FolderHelper.IsValidScene(path))
                return;

            if (!FolderSettings.Opt_EnableSceneTag.Value && FolderHelper.IsValidScene(path))
                return;

            var data = FolderSettings.GetFolderData(guid, path, out var isSubFolder);
            if (data == null)
            {
                // 缓存无效数据，避免重复查询
                folderDataCache[guid] = new CachedFolderData
                {
                    Data = null,
                    IsSubFolder = false,
                    FrameCount = Time.frameCount,
                    IsValid = false
                };
                return;
            }

            // 缓存有效数据
            folderDataCache[guid] = new CachedFolderData
            {
                Data = data,
                IsSubFolder = isSubFolder,
                FrameCount = Time.frameCount,
                IsValid = true
            };

            DrawFolderTagCached(guid, rect, data, isSubFolder);
        }

        private static void DrawFolderTagCached(string guid, Rect rect, FolderSettings.FolderData data, bool isSubFolder)
        {
            bool curIsTreeView = (rect.x - 16) % 14 == 0;
            if (!curIsTreeView)
                rect.xMin += 3;

            Color tagColor = FolderSettings.FoldersDescColor;
            if (FolderSettings.Opt_ShowGradient.Value)
            {
                // draw background
                GUI.color = isSubFolder ? data._color * FolderSettings.Opt_SubFoldersTint.Value : data._color;
                GUI.DrawTexture(rect, FolderSettings.Gradient, ScaleMode.ScaleAndCrop);
            }
            else if (FolderSettings.FoldersDescColor == Color.white)
            {
                // use gradient color
                tagColor = EditorGUIUtility.isProSkin ? data._color * 1.5f : data._color;
            }

            // draw tag
            if (!string.IsNullOrEmpty(data._tag))
            {
                GUI.color = tagColor;
                GUIStyle style = GUI.skin.label;
                Vector2 englishSize = style.CalcSize(new GUIContent(data._tag));

                int x = rect.xMax - englishSize.x > 0 ? (int)(rect.xMax - englishSize.x) : 0;
                GUI.Label(new Rect(x, rect.y - 1, englishSize.x, englishSize.y), data._tag, _labelSkin());
            }

            GUI.color = Color.white;

            GUIStyle _labelSkin()
            {
                if (s_labelSelected == null || s_labelSelected.normal.textColor != FolderSettings.FoldersDescColor)
                    SetLabelTint();

                return FolderHelper.IsSelected(guid) ? s_labelSelected : s_labelNormal;
            }
        }

        /// <summary>
        /// 清除所有缓存数据，强制下次绘制时重新获取folder data
        /// </summary>
        public static void ClearCache()
        {
            folderDataCache.Clear();
            lastFrameCount = -1;
        }

        /// <summary>
        /// 清除特定GUID的缓存数据
        /// </summary>
        /// <param name="guid">要清除缓存的GUID</param>
        public static void ClearSpecificCache(string guid)
        {
            if (!string.IsNullOrEmpty(guid) && folderDataCache.ContainsKey(guid))
            {
                folderDataCache.Remove(guid);
            }
        }

        private static void SetLabelTint()
        {
            s_labelSelected = new GUIStyle("Label");
            s_labelSelected.normal.textColor = FolderSettings.FoldersDescColor;
            s_labelSelected.hover.textColor = s_labelSelected.normal.textColor;

            s_labelNormal = new GUIStyle("Label");
            s_labelNormal.normal.textColor = new Color32(210, 210, 210, 255) * FolderSettings.FoldersDescColor;
            s_labelNormal.hover.textColor = s_labelNormal.normal.textColor;
        }
    }
}