using UnityEditor;
using UnityEngine;


namespace FolderTag
{
    public class EditorOption<T>
    {
        private readonly string _key;
        private T _val;
        private bool _loaded;

        public EditorOption(string key, T val)
        {
            _key = key;
            _val = val;
        }

        public T Value
        {
            get
            {
                if (!_loaded)
                {
                    Get();
                    _loaded = true;
                }

                return _val;
            }
            set => Set(value);
        }

        private void Get()
        {
            if (!EditorPrefs.HasKey(_key)) return;
            
            try
            {
                var type = typeof(T);

                if (type == typeof(bool))
                {
                    _val = (T)(object)EditorPrefs.GetBool(_key);
                    return;
                }
                if (type == typeof(int))
                {
                    _val = (T)(object)EditorPrefs.GetInt(_key);
                    return;
                }
                if (type == typeof(float))
                {
                    _val = (T)(object)EditorPrefs.GetFloat(_key);
                    return;
                }
                if (type == typeof(string))
                {
                    _val = (T)(object)EditorPrefs.GetString(_key);
                    return;
                }

                // 对于复杂类型，使用JSON反序列化
                var jsonString = EditorPrefs.GetString(_key);
                if (!string.IsNullOrEmpty(jsonString))
                {
                    _val = JsonUtility.FromJson<T>(jsonString);
                }
            }
            catch (System.ArgumentException ex)
            {
                Debug.LogWarning($"[FolderTag] EditorPrefs参数错误或JSON反序列化失败 '{_key}': {ex.Message}");
            }
            catch (System.FormatException ex)
            {
                Debug.LogWarning($"[FolderTag] EditorPrefs值格式错误 '{_key}': {ex.Message}");
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[FolderTag] 读取EditorPrefs时发生未知错误 '{_key}': {ex.Message}");
            }
        }

        private void Set(T val)
        {
            try
            {
                var type = typeof(T);
                _val = val;

                if (type == typeof(bool))
                {
                    EditorPrefs.SetBool(_key, _val.Equals(true));
                }
                else if (type == typeof(int))
                {
                    EditorPrefs.SetInt(_key, (int)(object)_val);
                }
                else if (type == typeof(string))
                {
                    var stringValue = (string)(object)_val;
                    EditorPrefs.SetString(_key, stringValue ?? string.Empty);
                }
                else if (type == typeof(float))
                {
                    EditorPrefs.SetFloat(_key, (float)(object)_val);
                }
                else
                {
                    // 对于复杂类型，使用JSON序列化
                    var jsonString = JsonUtility.ToJson(val);
                    EditorPrefs.SetString(_key, jsonString);
                }
            }
            catch (System.InvalidCastException ex)
            {
                Debug.LogError($"[FolderTag] 类型转换失败 '{_key}': {ex.Message}");
            }
            catch (System.ArgumentException ex)
            {
                Debug.LogError($"[FolderTag] JSON序列化失败 '{_key}': {ex.Message}");
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[FolderTag] 设置EditorPrefs时发生未知错误 '{_key}': {ex.Message}");
            }
        }
    }
}