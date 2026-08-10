using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>
/// 配置表管理器。从 CSV/Json 加载配置，提供强类型查询。
/// 注册为 ManagerHub 的 Manager。
/// 
/// 使用方式：
///   var cfg = TableManager.Instance.Get<GunConfig>();
///   var gun = cfg.Find(g => g.id == "rifle_01");
/// </summary>
public class TableManager : ManagerBase
{
    public static TableManager Instance => ManagerHub.Instance.Get<TableManager>();

    private readonly Dictionary<Type, object> _tables = new Dictionary<Type, object>();

    /// <summary>注册一个配置表</summary>
    public void Register<T>(Table<T> table) where T : TableItem, new()
    {
        _tables[typeof(T)] = table;
    }

    /// <summary>获取配置表</summary>
    public Table<T> Get<T>() where T : TableItem, new()
    {
        _tables.TryGetValue(typeof(T), out var t);
        return t as Table<T>;
    }
}

/// <summary>
/// 配置表行数据基类。每行必须有唯一的 id。
/// </summary>
[Serializable]
public abstract class TableItem
{
    public string id;
}

/// <summary>
/// 强类型配置表。支持按 id 查找和条件筛选。
/// </summary>
public class Table<T> where T : TableItem, new()
{
    private readonly List<T> _items = new List<T>();
    private readonly Dictionary<string, T> _byId = new Dictionary<string, T>();

    public IReadOnlyList<T> All => _items;

    /// <summary>从 CSV 文本加载</summary>
    public void LoadCsv(string csvText)
    {
        _items.Clear();
        _byId.Clear();

        var lines = csvText.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length < 2) return; // 需要表头 + 至少一行数据

        var headers = lines[0].Trim().Split(',');
        var fields = typeof(T).GetFields();

        for (int i = 1; i < lines.Length; i++)
        {
            var values = lines[i].Trim().Split(',');
            var item = new T();

            foreach (var field in fields)
            {
                int colIndex = Array.IndexOf(headers, field.Name);
                if (colIndex < 0 || colIndex >= values.Length) continue;

                var strVal = values[colIndex].Trim();
                SetFieldValue(item, field, strVal);
            }

            _items.Add(item);
            if (!string.IsNullOrEmpty(item.id))
                _byId[item.id] = item;
        }
    }

    /// <summary>从 Resources 加载 CSV</summary>
    public void LoadFromResources(string resourcePath)
    {
        var asset = Resources.Load<TextAsset>(resourcePath);
        if (asset != null)
            LoadCsv(asset.text);
    }

    public T Find(string id) => _byId.TryGetValue(id, out var item) ? item : null;
    public T Find(Predicate<T> match) => _items.Find(match);
    public List<T> FindAll(Predicate<T> match) => _items.FindAll(match);

    private void SetFieldValue(T item, System.Reflection.FieldInfo field, string value)
    {
        try
        {
            if (field.FieldType == typeof(int)) field.SetValue(item, int.Parse(value));
            else if (field.FieldType == typeof(float)) field.SetValue(item, float.Parse(value));
            else if (field.FieldType == typeof(bool)) field.SetValue(item, bool.Parse(value));
            else if (field.FieldType == typeof(string)) field.SetValue(item, value);
        }
        catch { /* skip parse errors */ }
    }
}
