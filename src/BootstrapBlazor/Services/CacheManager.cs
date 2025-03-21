﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License
// See the LICENSE file in the project root for more information.
// Maintainer: Argo Zhang(argo@live.ca) Website: https://www.blazor.zone

using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using System.ComponentModel;
using System.Globalization;
using System.Linq.Expressions;
using System.Reflection;

#if NET8_0_OR_GREATER
using System.Runtime.CompilerServices;
using System.Collections.Frozen;
#endif

namespace BootstrapBlazor.Components;

/// <summary>
/// 缓存操作类
/// </summary>
internal class CacheManager : ICacheManager
{
    private IMemoryCache Cache { get; set; }

    private IServiceProvider Provider { get; set; }

    [NotNull]
    private static CacheManager? Instance { get; set; }

    [NotNull]
    private static BootstrapBlazorOptions? Options { get; set; }

    private const string CacheKeyPrefix = "BootstrapBlazor";

    /// <summary>
    /// 构造函数
    /// </summary>
    /// <param name="provider"></param>
    /// <param name="memoryCache"></param>
    public CacheManager(IServiceProvider provider, IMemoryCache memoryCache)
    {
        Provider = provider;
        Cache = memoryCache;
        Instance = this;
        Options = Provider.GetRequiredService<IOptions<BootstrapBlazorOptions>>().Value;
    }

    /// <summary>
    /// 获得或者创建指定 Key 缓存项
    /// </summary>
    public TItem GetOrCreate<TItem>(object key, Func<ICacheEntry, TItem> factory) => Cache.GetOrCreate(key, entry =>
    {
        var item = factory(entry);

        entry.SetDefaultSlidingExpiration(Options.CacheManagerOptions.SlidingExpiration);
        return item;
    })!;

    /// <summary>
    /// 获得或者创建指定 Key 缓存项 异步重载方法
    /// </summary>
    public Task<TItem> GetOrCreateAsync<TItem>(object key, Func<ICacheEntry, Task<TItem>> factory) => Cache.GetOrCreateAsync(key, async entry =>
    {
        var item = await factory(entry);

        entry.SetDefaultSlidingExpiration(Options.CacheManagerOptions.SlidingExpiration);
        return item;
    })!;

    /// <summary>
    /// <inheritdoc/>
    /// </summary>
    /// <typeparam name="TItem"></typeparam>
    /// <param name="key"></param>
    /// <param name="value"></param>
    /// <returns></returns>
    public bool TryGetValue<TItem>(object key, [NotNullWhen(true)] out TItem? value)
    {
        var ret = Cache.TryGetValue(key, out var v);
        value = default;
        if (ret && v is TItem val)
        {
            value = val;
        }
        return ret;
    }

    /// <summary>
    /// 清除指定 Key 缓存项
    /// </summary>
    /// <param name="key"></param>
    public void Clear(object? key)
    {
        if (key is "BootstrapBlazor_StartTime")
        {
            return;
        }
        if (key is not null)
        {
            Cache.Remove(key);
        }
        else if (Cache is MemoryCache c)
        {
            c.Compact(100);
        }
    }

    /// <summary>
    /// 设置 App 开始时间
    /// </summary>
    public void SetStartTime() => SetStartTime(DateTimeOffset.Now);

    /// <summary>
    /// 设置 App 开始时间
    /// </summary>
    private void SetStartTime(DateTimeOffset startDateTimeOffset)
    {
        GetOrCreate("BootstrapBlazor_StartTime", entry =>
        {
            entry.Priority = CacheItemPriority.NeverRemove;
            return startDateTimeOffset;
        });
    }

    /// <summary>
    /// 获取 App 开始时间
    /// </summary>
    /// <returns></returns>
    public DateTimeOffset GetStartTime()
    {
        var ret = DateTimeOffset.MinValue;
        if (Cache.TryGetValue("BootstrapBlazor_StartTime", out var v) && v is DateTimeOffset d)
        {
            ret = d;
        }
        return ret;
    }

    /// <summary>
    /// 获得 缓存数量
    /// </summary>
    public long Count
    {
        get
        {
            var count = 0;
            if (Cache is MemoryCache c)
            {
                count = c.Count;
            }
            return count;
        }
    }

#if NET9_0_OR_GREATER
    /// <summary>
    /// 获得 缓存键集合
    /// </summary>
    public IEnumerable<object> Keys
    {
        get
        {
            var keys = Enumerable.Empty<object>();
            if (Cache is MemoryCache c)
            {
                keys = c.Keys;
            }
            return keys;
        }
    }

    private object? _coherentStateInstance = null;

    private MethodInfo? _allValuesMethodInfo = null;

    private static readonly FieldInfo _coherentStateFieldInfo = typeof(MemoryCache).GetField("_coherentState", BindingFlags.Instance | BindingFlags.NonPublic)!;

    private static MethodInfo GetAllValuesMethodInfo(Type type) => type.GetMethod("GetAllValues", BindingFlags.Instance | BindingFlags.Public)!;

    /// <summary>
    /// <inheritdoc/>
    /// </summary>
    /// <param name="key"></param>
    /// <param name="entry"></param>
    /// <returns></returns>
    public bool TryGetCacheEntry(object? key, [NotNullWhen(true)] out ICacheEntry? entry)
    {
        entry = null;
        if (key == null)
        {
            return false;
        }

        if (Cache is MemoryCache cache)
        {
            var values = GetAllValues(cache);
            entry = values.Find(e => e.Key == key);
        }
        return entry != null;
    }

    private List<ICacheEntry> GetAllValues(MemoryCache cache)
    {
        _coherentStateInstance = _coherentStateFieldInfo.GetValue(cache)!;
        _allValuesMethodInfo ??= GetAllValuesMethodInfo(_coherentStateInstance.GetType());

        var ret = new List<ICacheEntry>();
        if (_allValuesMethodInfo.Invoke(_coherentStateInstance, null) is IEnumerable<ICacheEntry> values)
        {
            ret.AddRange(values);
        }
        return ret;
    }
#endif

    #region Count
    public static int ElementCount(object? value)
    {
        var ret = 0;
        if (value != null)
        {
            var type = value.GetType();
            var cacheKey = $"{CacheKeyPrefix}-Lambda-Count-{type.GetUniqueTypeName()}";
            var invoker = Instance.GetOrCreate(cacheKey, entry =>
            {
                return LambdaExtensions.CountLambda(type).Compile();
            });
            ret = invoker(value);
        }
        return ret;
    }
    #endregion

    #region Localizer
    /// <summary>
    /// 通过 Type 获得 <see cref="IStringLocalizer"/> 实例
    /// </summary>
    /// <param name="resourceSource"></param>
    /// <returns></returns>
    public static IStringLocalizer? CreateLocalizerByType(Type resourceSource) => resourceSource.Assembly.IsDynamic
        ? null
        : Instance.Provider.GetRequiredService<IStringLocalizerFactory>().Create(resourceSource);

    /// <summary>
    /// 获得 <see cref="JsonLocalizationOptions"/> 值
    /// </summary>
    /// <returns></returns>
    private static JsonLocalizationOptions GetJsonLocalizationOption()
    {
        var localizationOptions = Instance.Provider.GetRequiredService<IOptions<JsonLocalizationOptions>>();
        return localizationOptions.Value;
    }

    /// <summary>
    /// 通过 程序集与类型获得 IStringLocalizer 实例
    /// </summary>
    /// <param name="assembly">Assembly 程序集实例</param>
    /// <param name="typeName">类型名称</param>
    /// <returns></returns>
    public static IStringLocalizer? GetStringLocalizerFromService(Assembly assembly, string typeName)
    {
        if (assembly.IsDynamic)
        {
            return null;
        }
        IStringLocalizer? ret = null;
        var factories = Instance.Provider.GetServices<IStringLocalizerFactory>();
        var factory = factories.LastOrDefault(a => a is not JsonStringLocalizerFactory);
        if (factory != null)
        {
            var type = assembly.GetType(typeName);
            if (type != null)
            {
                ret = factory.Create(type);
            }
        }
        return ret;
    }

    /// <summary>
    /// 获取指定文化本地化资源集合
    /// </summary>
    /// <param name="assembly">Assembly 程序集实例</param>
    /// <param name="typeName">类型名称</param>
    public static IEnumerable<LocalizedString>? GetAllStringsByTypeName(Assembly assembly, string typeName)
        => GetJsonStringByTypeName(GetJsonLocalizationOption(), assembly, typeName, CultureInfo.CurrentUICulture.Name);

    /// <summary>
    /// 通过指定程序集获取所有本地化信息键值集合
    /// </summary>
    /// <param name="option">JsonLocalizationOptions 实例</param>
    /// <param name="assembly">Assembly 程序集实例</param>
    /// <param name="typeName">类型名称</param>
    /// <param name="cultureName">cultureName 未空时使用 CultureInfo.CurrentUICulture.Name</param>
    /// <param name="forceLoad">默认 false 使用缓存值 设置 true 时内部强制重新加载</param>
    /// <returns></returns>
    public static IEnumerable<LocalizedString>? GetJsonStringByTypeName(JsonLocalizationOptions option, Assembly assembly, string typeName, string? cultureName = null, bool forceLoad = false)
    {
        if (assembly.IsDynamic)
        {
            return null;
        }

        cultureName ??= CultureInfo.CurrentUICulture.Name;
        if (string.IsNullOrEmpty(cultureName))
        {
            return [];
        }

        var key = $"{CacheKeyPrefix}-{nameof(GetJsonStringByTypeName)}-{assembly.GetUniqueName()}-{cultureName}";
        if (forceLoad)
        {
            Instance.Cache.Remove(key);
        }

        var localizedItems = Instance.GetOrCreate(key, entry =>
        {
            var sections = option.GetJsonStringFromAssembly(assembly, cultureName);
            var items = sections.SelectMany(section => section.GetChildren().Select(kv =>
            {
                var value = kv.Value;
                if (value == null && option.UseKeyWhenValueIsNull == true)
                {
                    value = kv.Key;
                }
                return new LocalizedString(kv.Key, value ?? "", false, section.Key);
            }));
#if NET8_0_OR_GREATER
            return items.ToFrozenSet();
#else
            return items.ToHashSet();
#endif
        });
        return localizedItems.Where(item => item.SearchedLocation!.Equals(typeName, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// 通过 ILocalizationResolve 接口实现类获得本地化键值集合
    /// </summary>
    /// <param name="typeName"></param>
    /// <param name="includeParentCultures"></param>
    /// <returns></returns>
    public static IEnumerable<LocalizedString> GetTypeStringsFromResolve(string typeName, bool includeParentCultures = true) => Instance.Provider.GetRequiredService<ILocalizationResolve>().GetAllStringsByType(typeName, includeParentCultures);
    #endregion

    #region DisplayName
    /// <summary>
    /// 获得类型属性的描述信息
    /// </summary>
    /// <param name="modelType"></param>
    /// <param name="fieldName"></param>
    /// <returns></returns>
    public static string GetDisplayName(Type modelType, string fieldName)
    {
        string? dn = null;
        // 显示名称为空时通过资源文件查找 FieldName 项
        var localizer = modelType.Assembly.IsDynamic ? null : CreateLocalizerByType(modelType);
        var stringLocalizer = localizer?[fieldName];
        if (stringLocalizer is { ResourceNotFound: false })
        {
            dn = stringLocalizer.Value;
        }
        else if (modelType.IsEnum)
        {
            var info = modelType.GetFieldByName(fieldName);
            if (info != null)
            {
                dn = FindDisplayAttribute(modelType, info);
            }
        }
        else if (TryGetProperty(modelType, fieldName, out var propertyInfo))
        {
            dn = FindDisplayAttribute(modelType, propertyInfo);
        }
        return dn ?? fieldName;
    }

    private static string? FindDisplayAttribute(Type modelType, MemberInfo memberInfo)
    {
        // 回退查找 Display 标签
        var dn = memberInfo.GetCustomAttribute<DisplayAttribute>(true)?.Name
            ?? memberInfo.GetCustomAttribute<DisplayNameAttribute>(true)?.DisplayName
            ?? memberInfo.GetCustomAttribute<DescriptionAttribute>(true)?.Description;

        // 回退查找资源文件通过 dn 查找匹配项 用于支持 Validation
        if (!modelType.Assembly.IsDynamic && !string.IsNullOrEmpty(dn))
        {
            dn = GetLocalizerValueFromResourceManager(dn);
        }
        return dn;
    }

    public static List<SelectedItem> GetNullableBoolItems(Type modelType, string fieldName)
    {
        var cacheKey = $"{CacheKeyPrefix}-{nameof(GetNullableBoolItems)}-{modelType.GetUniqueTypeName()}-{fieldName}-{CultureInfo.CurrentUICulture.Name}";
        return Instance.GetOrCreate(cacheKey, entry =>
        {
            var items = new List<SelectedItem>();
            var localizer = modelType.Assembly.IsDynamic ? null : CreateLocalizerByType(modelType);
            IStringLocalizer? localizerAttr = null;
            items.Add(new SelectedItem("", FindDisplayText(nameof(NullableBoolItemsAttribute.NullValueDisplayText), attr => attr.NullValueDisplayText)));
            items.Add(new SelectedItem("True", FindDisplayText(nameof(NullableBoolItemsAttribute.TrueValueDisplayText), attr => attr.TrueValueDisplayText)));
            items.Add(new SelectedItem("False", FindDisplayText(nameof(NullableBoolItemsAttribute.FalseValueDisplayText), attr => attr.FalseValueDisplayText)));
            return items;

            string FindDisplayText(string itemName, Func<NullableBoolItemsAttribute, string?> callback)
            {
                string? dn = null;

                // 优先读取资源文件配置
                var stringLocalizer = localizer?[$"{fieldName}-{itemName}"];
                if (stringLocalizer is { ResourceNotFound: false })
                {
                    dn = stringLocalizer.Value;
                }
                else if (TryGetProperty(modelType, fieldName, out var propertyInfo))
                {
                    // 类资源文件未找到 回落查找标签
                    var attr = propertyInfo.GetCustomAttribute<NullableBoolItemsAttribute>(true);
                    if (attr != null && !modelType.Assembly.IsDynamic)
                    {
                        dn = callback(attr);
                    }
                }

                // 回落读取 NullableBoolItemsAttribute 资源文件配置
                return dn ?? FindDisplayTextByItemName(itemName);
            }

            string FindDisplayTextByItemName(string itemName)
            {
                localizerAttr ??= CreateLocalizerByType(typeof(NullableBoolItemsAttribute));
                var stringLocalizer = localizerAttr![itemName];
                return stringLocalizer.Value;
            }
        });
    }

    /// <summary>
    /// 通过指定 Key 获取资源文件中的键值
    /// </summary>
    /// <param name="key"></param>
    /// <returns></returns>
    private static string? GetLocalizerValueFromResourceManager(string key)
    {
        string? dn = null;
        var options = GetJsonLocalizationOption();
        if (options.ResourceManagerStringLocalizerType != null)
        {
            var localizer = CreateLocalizerByType(options.ResourceManagerStringLocalizerType);
            dn = GetValueByKey(localizer);
        }
        return dn ?? key;

        [ExcludeFromCodeCoverage]
        string? GetValueByKey(IStringLocalizer? l)
        {
            string? val = null;
            var stringLocalizer = l?[key];
            if (stringLocalizer is { ResourceNotFound: false })
            {
                val = stringLocalizer.Value;
            }
            return val;
        }
    }
    #endregion

    #region Placeholder
    public static string? GetPlaceholder(Type modelType, string fieldName)
    {
        // 通过资源文件查找 FieldName 项
        string? ret = null;
        var localizer = CreateLocalizerByType(modelType);
        if (localizer != null)
        {
            var stringLocalizer = localizer[$"{fieldName}.PlaceHolder"];
            if (!stringLocalizer.ResourceNotFound)
            {
                ret = stringLocalizer.Value;
            }
            else if (TryGetProperty(modelType, fieldName, out var propertyInfo))
            {
                var placeHolderAttribute = propertyInfo.GetCustomAttribute<PlaceHolderAttribute>(true);
                if (placeHolderAttribute != null)
                {
                    ret = placeHolderAttribute.Text;
                }
            }
        }
        return ret;
    }
    #endregion

    #region Lambda Property
    public static bool TryGetProperty(Type modelType, string fieldName, [NotNullWhen(true)] out PropertyInfo? propertyInfo)
    {
        var props = modelType.GetRuntimeProperties();

        // 支持 MetadataType
        var metadataType = modelType.GetCustomAttribute<MetadataTypeAttribute>(false);
        if (metadataType != null)
        {
            props = props.Concat(metadataType.MetadataClassType.GetRuntimeProperties());
        }

        propertyInfo = props.FirstOrDefault(p => p.Name == fieldName);
        return propertyInfo != null;
    }

    public static TResult GetPropertyValue<TModel, TResult>(TModel model, string fieldName) => (model is IDynamicColumnsObject d)
        ? (TResult)d.GetValue(fieldName)!
        : GetValue<TModel, TResult>(model, fieldName);

    private static TResult GetValue<TModel, TResult>(TModel model, string fieldName)
    {
        if (model == null)
        {
            throw new ArgumentNullException(nameof(model));
        }

        var type = model.GetType();
        var cacheKey = $"{CacheKeyPrefix}-Lambda-Get-{type.GetUniqueTypeName()}-{typeof(TModel)}-{fieldName}-{typeof(TResult)}";
        var invoker = Instance.GetOrCreate(cacheKey, entry =>
        {
            if (type.Assembly.IsDynamic)
            {
                entry.SetAbsoluteExpiration(Options.CacheManagerOptions.AbsoluteExpiration);
            }

            return LambdaExtensions.GetPropertyValueLambda<TModel, TResult>(model, fieldName).Compile();
        });
        return invoker(model);
    }

    public static void SetPropertyValue<TModel, TValue>(TModel model, string fieldName, TValue value)
    {
        if (model is IDynamicColumnsObject d)
        {
            d.SetValue(fieldName, value);
        }
        else
        {
            if (model == null)
            {
                throw new ArgumentNullException(nameof(model));
            }

            var type = model.GetType();
            var cacheKey = $"{CacheKeyPrefix}-Lambda-Set-{type.GetUniqueTypeName()}-{typeof(TModel)}-{fieldName}-{typeof(TValue)}";
            var invoker = Instance.GetOrCreate(cacheKey, entry =>
            {
                if (type.Assembly.IsDynamic)
                {
                    entry.SetAbsoluteExpiration(Options.CacheManagerOptions.AbsoluteExpiration);
                }
                return LambdaExtensions.SetPropertyValueLambda<TModel, TValue>(model, fieldName).Compile();
            });
            invoker(model, value);
        }
    }

    /// <summary>
    /// 获得 指定模型标记 <see cref="KeyAttribute"/> 的属性值
    /// </summary>
    /// <typeparam name="TModel"></typeparam>
    /// <typeparam name="TValue"></typeparam>
    /// <param name="model"></param>
    /// <param name="customAttribute"></param>
    /// <returns></returns>
    public static TValue? GetKeyValue<TModel, TValue>(TModel model, Type? customAttribute = null)
    {
        var ret = default(TValue);
        if (model != null)
        {
            var type = model.GetType();
            var cacheKey = $"{CacheKeyPrefix}-Lambda-{nameof(GetKeyValue)}-{type.GetUniqueTypeName()}-{typeof(TModel)}-{customAttribute?.GetUniqueTypeName()}";
            var invoker = Instance.GetOrCreate(cacheKey, entry => LambdaExtensions.GetKeyValue<TModel, TValue>(customAttribute).Compile());
            ret = invoker(model);
        }
        return ret;
    }
    #endregion

    #region Lambda Sort
    public static Func<IEnumerable<T>, string, SortOrder, IEnumerable<T>> GetSortFunc<T>()
    {
        var cacheKey = $"{CacheKeyPrefix}-Lambda-{nameof(GetSortFunc)}-{typeof(T).GetUniqueTypeName()}";
        return Instance.GetOrCreate(cacheKey, entry => LambdaExtensions.GetSortLambda<T>().Compile());
    }

    public static Func<IEnumerable<T>, List<string>, IEnumerable<T>> GetSortListFunc<T>()
    {
        var cacheKey = $"{CacheKeyPrefix}-Lambda-{nameof(GetSortListFunc)}-{typeof(T).GetUniqueTypeName()}";
        return Instance.GetOrCreate(cacheKey, entry => LambdaExtensions.GetSortListLambda<T>().Compile());
    }
    #endregion

    #region Lambda ConvertTo
    public static Func<object, IEnumerable<string?>> CreateConverterInvoker(Type type)
    {
        var cacheKey = $"{CacheKeyPrefix}-Lambda-{nameof(CreateConverterInvoker)}-{type.GetUniqueTypeName()}";
        return Instance.GetOrCreate(cacheKey, entry =>
        {
            var method = typeof(CacheManager)
                .GetMethod(nameof(ConvertToString), BindingFlags.NonPublic | BindingFlags.Static)!
                .MakeGenericMethod(type);

            var para_exp = Expression.Parameter(typeof(object));
            var convert = Expression.Convert(para_exp, typeof(List<>).MakeGenericType(type));
            var body = Expression.Call(method, convert);
            return Expression.Lambda<Func<object, IEnumerable<string?>>>(body, para_exp).Compile();
        });
    }

    private static IEnumerable<string?> ConvertToString<TSource>(List<TSource> source) => source is List<SelectedItem> list
        ? list.Select(i => i.Value)
        : source.Select(i => i?.ToString());
    #endregion

    #region OnValueChanged Lambda
    /// <summary>
    /// 创建 OnValueChanged 回调委托
    /// </summary>
    /// <typeparam name="TModel"></typeparam>
    /// <param name="fieldType"></param>
    /// <returns></returns>
    public static Func<TModel, ITableColumn, Func<TModel, ITableColumn, object?, Task>, object> GetOnValueChangedInvoke<TModel>(Type fieldType)
    {
        var cacheKey = $"{CacheKeyPrefix}-Lambda-{nameof(GetOnValueChangedInvoke)}-{typeof(TModel).GetUniqueTypeName()}-{fieldType.GetUniqueTypeName()}";
        return Instance.GetOrCreate(cacheKey, entry => Utility.CreateOnValueChanged<TModel>(fieldType).Compile());
    }
    #endregion

    #region Format
    public static Func<object, string, IFormatProvider?, string> GetFormatInvoker(Type type)
    {
        var cacheKey = $"{CacheKeyPrefix}-Lambda-{nameof(GetFormatInvoker)}-{type.GetUniqueTypeName()}";
        return Instance.GetOrCreate(cacheKey, entry => GetFormatLambda(type).Compile());

        static Expression<Func<object, string, IFormatProvider?, string>> GetFormatLambda(Type type)
        {
            var exp_p1 = Expression.Parameter(typeof(object));
            var exp_p2 = Expression.Parameter(typeof(string));
            var exp_p3 = Expression.Parameter(typeof(IFormatProvider));
            Expression? body = null;
            if (type.IsAssignableTo(typeof(IFormattable)))
            {
                // 通过 IFormattable 接口格式化
                var mi = type.GetMethod("ToString", [typeof(string), typeof(IFormatProvider)]);
                if (mi != null)
                {
                    body = Expression.Call(Expression.Convert(exp_p1, type), mi, exp_p2, exp_p3);
                }
            }
            else
            {
                // 通过 ToString() 方法格式化
                var mi = type.GetMethod("ToString", []);
                if (mi != null)
                {
                    body = Expression.Call(Expression.Convert(exp_p1, type), mi);
                }
            }
            return BuildExpression();

            [ExcludeFromCodeCoverage]
            Expression<Func<object, string, IFormatProvider?, string>> BuildExpression() => body == null
                ? (s, f, provider) => s.ToString() ?? ""
                : Expression.Lambda<Func<object, string, IFormatProvider?, string>>(body, exp_p1, exp_p2, exp_p3);
        }
    }

    public static Func<object, IFormatProvider?, string> GetFormatProviderInvoker(Type type)
    {
        var cacheKey = $"{CacheKeyPrefix}-Lambda-{nameof(GetFormatProviderInvoker)}-{type.GetUniqueTypeName()}";
        return Instance.GetOrCreate(cacheKey, entry => GetFormatProviderLambda(type).Compile());

        static Expression<Func<object, IFormatProvider?, string>> GetFormatProviderLambda(Type type)
        {
            var exp_p1 = Expression.Parameter(typeof(object));
            var exp_p2 = Expression.Parameter(typeof(IFormatProvider));
            Expression? body;

            var mi = type.GetMethod("ToString", [typeof(IFormatProvider)]);
            if (mi != null)
            {
                // 通过 ToString(IFormatProvider? provider) 接口格式化
                body = Expression.Call(Expression.Convert(exp_p1, type), mi, exp_p2);
            }
            else
            {
                // 通过 ToString() 方法格式化
                mi = type.GetMethod("ToString");
                body = Expression.Call(Expression.Convert(exp_p1, type), mi!);
            }
            return Expression.Lambda<Func<object, IFormatProvider?, string>>(body, exp_p1, exp_p2);
        }
    }

    public static object GetFormatterInvoker(Type type, Func<object, Task<string?>> formatter)
    {
        var cacheKey = $"{CacheKeyPrefix}-Lambda-{nameof(GetFormatterInvoker)}-{type.GetUniqueTypeName()}";
        var invoker = Instance.GetOrCreate(cacheKey, entry => GetFormatterInvokerLambda(type).Compile());
        return invoker(formatter);

        static Expression<Func<Func<object, Task<string?>>, object>> GetFormatterInvokerLambda(Type type)
        {
            var method = typeof(CacheManager).GetMethod(nameof(InvokeFormatterAsync), BindingFlags.Static | BindingFlags.NonPublic)!.MakeGenericMethod(type);
            var exp_p1 = Expression.Parameter(typeof(Func<object?, Task<string?>>));
            var body = Expression.Call(null, method, exp_p1);
            return Expression.Lambda<Func<Func<object, Task<string?>>, object>>(body, exp_p1);
        }
    }

    private static Func<TType, Task<string?>> InvokeFormatterAsync<TType>(Func<object?, Task<string?>> formatter) => new(v => formatter(v));
    #endregion
}
