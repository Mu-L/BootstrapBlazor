﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License
// See the LICENSE file in the project root for more information.
// Maintainer: Argo Zhang(argo@live.ca) Website: https://www.blazor.zone

using Microsoft.AspNetCore.Components.Rendering;

namespace BootstrapBlazor.Components;

internal class LookupContent : ComponentBase, ILookup
{
    /// <summary>
    /// <inheritdoc/>
    /// </summary>
    [Parameter]
    public IEnumerable<SelectedItem>? Lookup { get; set; }

    /// <summary>
    /// <inheritdoc/>
    /// </summary>
    [Parameter]
    public ILookupService? LookupService { get; set; }

    /// <summary>
    /// <inheritdoc/>
    /// </summary>
    [Parameter]
    public string? LookupServiceKey { get; set; }

    /// <summary>
    /// <inheritdoc/>
    /// </summary>
    [Parameter]
    public object? LookupServiceData { get; set; }

    /// <summary>
    /// <inheritdoc/>
    /// </summary>
    [Parameter]
    public StringComparison LookupStringComparison { get; set; } = StringComparison.OrdinalIgnoreCase;

    /// <summary>
    /// 获得/设置 显示值
    /// </summary>
    [Parameter]
    public string? Value { get; set; }

    [Inject]
    [NotNull]
    private ILookupService? InjectLookupService { get; set; }

    private string? _content;

    private IEnumerable<SelectedItem>? _items;

    /// <summary>
    /// <inheritdoc/>
    /// </summary>
    /// <returns></returns>
    protected override async Task OnParametersSetAsync()
    {
        await base.OnParametersSetAsync();

        _items ??= await this.GetItemsAsync(InjectLookupService, LookupServiceKey, LookupServiceData) ?? [];
        var item = _items.FirstOrDefault(i => i.Value.Equals(Value, LookupStringComparison));
        _content = item?.Text ?? Value;
    }

    /// <summary>
    /// <inheritdoc/>
    /// </summary>
    /// <param name="builder"></param>
    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        if (!string.IsNullOrEmpty(_content))
        {
            builder.AddContent(0, _content);
        }
    }
}
