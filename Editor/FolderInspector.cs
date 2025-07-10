using UnityEditor;
using UnityEngine;

namespace FolderTag
{
    /// <summary>
    /// 文件夹检查器，继承自BaseInspector以复用通用功能
    /// </summary>
    [CustomEditor(typeof(DefaultAsset))]
    public class FolderInspector : BaseInspector
    {
        /// <summary>
        /// 验证当前选择是否为有效的文件夹
        /// </summary>
        /// <returns>如果是有效文件夹返回true，否则返回false</returns>
        protected override bool IsValidSelection()
        {
            if (Selection.assetGUIDs.Length != 1)
                return false;

            var guid = Selection.assetGUIDs[0];
            var path = AssetDatabase.GUIDToAssetPath(guid);

            return FolderHelper.IsValidFolder(path);
        }

        /// <summary>
        /// 获取是否为场景检查器
        /// </summary>
        /// <returns>文件夹检查器返回false</returns>
        protected override bool IsSceneInspector()
        {
            return false;
        }
    }
}