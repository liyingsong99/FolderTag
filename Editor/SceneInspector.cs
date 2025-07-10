using UnityEditor;
using UnityEngine;

namespace FolderTag
{
    /// <summary>
    /// 场景检查器，继承自BaseInspector以复用通用功能
    /// </summary>
    [CustomEditor(typeof(SceneAsset))]
    public class SceneInspector : BaseInspector
    {
        /// <summary>
        /// 验证当前选择是否为有效的场景文件
        /// </summary>
        /// <returns>如果是有效场景文件返回true，否则返回false</returns>
        protected override bool IsValidSelection()
        {
            if (Selection.assetGUIDs.Length != 1)
                return false;

            var guid = Selection.assetGUIDs[0];
            var path = AssetDatabase.GUIDToAssetPath(guid);

            return FolderHelper.IsValidScene(path);
        }

        /// <summary>
        /// 获取是否为场景检查器
        /// </summary>
        /// <returns>场景检查器返回true</returns>
        protected override bool IsSceneInspector()
        {
            return true;
        }
    }
}