using System;
using System.IO;
using System.Linq;

namespace FolderTag
{
    public static class FolderHelper
    {
        /// <summary>
        /// 检查指定目录是否为空
        /// </summary>
        /// <param name="directoryPath">目录路径</param>
        /// <returns>如果目录为空返回true，否则返回false</returns>
        public static bool IsFolderEmpty(string directoryPath)
        {
            if (string.IsNullOrEmpty(directoryPath))
                return true;

            try
            {
                var items = Directory.EnumerateFileSystemEntries(directoryPath);
                using (var en = items.GetEnumerator())
                    return en.MoveNext() == false;
            }
            catch (DirectoryNotFoundException)
            {
                return true; // 目录不存在视为空
            }
            catch (UnauthorizedAccessException)
            {
                return false; // 无权限访问，假设不为空
            }
            catch (System.Exception)
            {
                return false; // 其他异常，假设不为空
            }
        }

        /// <summary>
        /// 检查指定GUID的资源是否被选中
        /// </summary>
        /// <param name="guid">资源GUID</param>
        /// <returns>如果被选中返回true，否则返回false</returns>
        public static bool IsSelected(string guid)
        {
            if (string.IsNullOrEmpty(guid))
                return false;

            return UnityEditor.Selection.assetGUIDs?.Contains(guid) ?? false;
        }

        /// <summary>
        /// 检查指定路径是否为有效的文件夹
        /// </summary>
        /// <param name="path">文件夹路径</param>
        /// <returns>如果是有效文件夹返回true，否则返回false</returns>
        public static bool IsValidFolder(string path)
        {
            if (string.IsNullOrEmpty(path))
                return false;

            return UnityEditor.AssetDatabase.IsValidFolder(path) && !path.Equals("Assets");
        }

        /// <summary>
        /// 检查指定路径是否为有效的场景文件
        /// </summary>
        /// <param name="path">文件路径</param>
        /// <returns>如果是有效场景文件返回true，否则返回false</returns>
        public static bool IsValidScene(string path)
        {
            if (string.IsNullOrEmpty(path))
                return false;

            return path.EndsWith(".unity", System.StringComparison.OrdinalIgnoreCase);
        }
    }
}