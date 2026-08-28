using Godot;

namespace LX.UI.Components;

/// <summary>只创建可见行附近节点的纵向虚拟列表，适合大型配置和背包列表。</summary>
[GlobalClass]
public partial class VirtualListView : ScrollContainer
{
    private readonly List<Control> _pool = [];
    private Control? _content;
    private Func<Control>? _factory;
    private Action<Control, int>? _binder;
    private int _itemCount;

    /// <summary>每个列表项占用的固定高度。</summary>
    [Export(PropertyHint.Range, "16,256,1")]
    public float ItemHeight { get; set; } = 38;

    /// <summary>在可见区域外额外保留的行数，用来减少快速滚动时的空白。</summary>
    [Export(PropertyHint.Range, "0,8,1")]
    public int BufferItems { get; set; } = 2;

    /// <inheritdoc />
    public override void _Ready()
    {
        HorizontalScrollMode = ScrollMode.Disabled;
        _content = new Control { Name = "VirtualContent", MouseFilter = MouseFilterEnum.Ignore };
        AddChild(_content);
        GetVScrollBar().ValueChanged += _ => RefreshVisibleItems();
    }

    /// <summary>设置数据量、节点工厂和绑定回调；已有池节点会被安全重建。</summary>
    public void Configure(int itemCount, Func<Control> factory, Action<Control, int> binder)
    {
        if (itemCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(itemCount));
        }
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentNullException.ThrowIfNull(binder);
        if (_content is null)
        {
            throw new InvalidOperationException("VirtualListView must be inside the scene tree before use.");
        }

        foreach (var item in _pool)
        {
            item.QueueFree();
        }
        _pool.Clear();
        _itemCount = itemCount;
        _factory = factory;
        _binder = binder;
        _content.CustomMinimumSize = new Vector2(0, _itemCount * ItemHeight);
        var poolSize = Math.Min(
            itemCount,
            Math.Max(8, Mathf.CeilToInt(Size.Y / Math.Max(1, ItemHeight)) + BufferItems * 2));
        for (var index = 0; index < poolSize; index++)
        {
            var item = factory() ?? throw new InvalidOperationException("Virtual list factory returned null.");
            _pool.Add(item);
            _content.AddChild(item);
        }
        RefreshVisibleItems();
    }

    /// <inheritdoc />
    public override void _Notification(int what)
    {
        if (what == NotificationResized)
        {
            RefreshVisibleItems();
        }
    }

    private void RefreshVisibleItems()
    {
        if (_content is null || _factory is null || _binder is null || ItemHeight <= 0)
        {
            return;
        }
        var first = Math.Max(0, Mathf.FloorToInt((float)GetVScrollBar().Value / ItemHeight) - BufferItems);
        for (var poolIndex = 0; poolIndex < _pool.Count; poolIndex++)
        {
            var itemIndex = first + poolIndex;
            var item = _pool[poolIndex];
            item.Visible = itemIndex < _itemCount;
            if (!item.Visible)
            {
                continue;
            }
            item.Position = new Vector2(0, itemIndex * ItemHeight);
            item.Size = new Vector2(Math.Max(0, Size.X - GetVScrollBar().Size.X), ItemHeight);
            _binder(item, itemIndex);
        }
    }
}
