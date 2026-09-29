using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace FactorySim.Client;

/// <summary>
/// A button that opens a searchable, bounded popup list: a search field on top, then at most
/// <see cref="VisibleRows"/> rows that scroll when there are more, so the popup is the same size
/// whether it holds five entries or two hundred. Some entries can be pinned above the search
/// results (for choices like "Anything" that are not part of what is being searched).
/// Reusable anywhere a long flat list needs picking, not just the splitter sort filter.
/// </summary>
public sealed class SearchablePicker
{
    /// <summary>Rows visible before the list scrolls. For scripted tests.</summary>
    public const int VisibleRows = 10;

    private const int RowHeight = 32;
    private const int PopupWidth = 280;

    public readonly record struct Entry(string? Key, string Label, Texture2D? Icon, string Tooltip = "");

    public readonly Button Button;

    private readonly Action<string?> _onPick;
    private readonly PopupPanel _popup;
    private readonly LineEdit _search;
    private readonly ItemList _list;

    private List<Entry> _pinned = new();
    private List<Entry> _items = new();
    private List<Entry> _shown = new();

    public SearchablePicker(Action<string?> onPick)
    {
        _onPick = onPick;

        Button = new Button
        {
            FocusMode = Control.FocusModeEnum.None,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            ClipText = true,
            Alignment = HorizontalAlignment.Left,
        };
        Button.Pressed += TogglePopup;

        _popup = new PopupPanel { Theme = UiTheme.Create() };
        var body = new VBoxContainer();
        body.AddThemeConstantOverride("separation", 4);

        _search = new LineEdit { PlaceholderText = "Search...", CustomMinimumSize = new Vector2(PopupWidth - 16, 0) };
        _search.TextChanged += _ => RenderList();
        _search.TextSubmitted += _ => PickCurrent();
        _search.GuiInput += OnSearchInput;
        body.AddChild(_search);

        _list = new ItemList
        {
            CustomMinimumSize = new Vector2(PopupWidth - 16, RowHeight * VisibleRows),
            SameColumnWidth = true,
            MaxColumns = 1,
            FocusMode = Control.FocusModeEnum.None,
            FixedIconSize = new Vector2I(20, 20),
        };
        _list.ItemSelected += index => Choose(_shown[(int)index].Key);
        body.AddChild(_list);

        _popup.AddChild(body);
        Button.AddChild(_popup);
        _popup.PopupHide += () => _search.Text = "";
    }

    /// <summary>Pinned entries, always shown above the search results (for example "Anything").</summary>
    public void SetPinned(IReadOnlyList<Entry> pinned)
    {
        _pinned = pinned.ToList();
        RenderList();
    }

    /// <summary>The searchable entries (for example every unlocked item).</summary>
    public void SetItems(IReadOnlyList<Entry> items)
    {
        _items = items.ToList();
        RenderList();
    }

    /// <summary>Backfills icons that were not ready when the entries were set (they can arrive a
    /// frame later from the thumbnail cache).</summary>
    public void RefreshIcons(Func<string, Texture2D?> lookup)
    {
        bool changed = false;
        for (int i = 0; i < _items.Count; i++)
        {
            if (_items[i].Icon != null || _items[i].Key == null) continue;
            if (lookup(_items[i].Key!) is not { } tex) continue;
            _items[i] = _items[i] with { Icon = tex };
            changed = true;
        }
        if (changed) RenderList();
        UpdateButtonIcon(lookup);
    }

    private string? _selectedKey;
    private bool _mixed;

    /// <summary>The current choice shown on the button; blank when a mixed selection has no single value.</summary>
    public void SetSelected(string? key, bool mixed)
    {
        _selectedKey = key;
        _mixed = mixed;
        UpdateButtonIcon(_ => null);
    }

    private void UpdateButtonIcon(Func<string, Texture2D?> lookup)
    {
        if (_mixed)
        {
            Button.Text = "";
            Button.Icon = null;
            Button.TooltipText = "";
            return;
        }
        Entry? found = null;
        foreach (var e in _pinned) if (e.Key == _selectedKey) { found = e; break; }
        if (found == null) foreach (var e in _items) if (e.Key == _selectedKey) { found = e; break; }
        Button.Text = found?.Label ?? "";
        Button.Icon = found?.Icon ?? (_selectedKey != null ? lookup(_selectedKey) : null);
        Button.TooltipText = found?.Tooltip ?? "";
    }

    /// <summary>Closes the popup without picking anything. Called when the Manage window hides or
    /// the selection changes, so a stale popup doesn't linger over the next thing shown.</summary>
    public void Close()
    {
        if (_popup.Visible) _popup.Hide();
    }

    private void TogglePopup()
    {
        if (_popup.Visible) { _popup.Hide(); return; }
        RenderList();
        _search.GrabFocus();

        var rect = Button.GetGlobalRect();
        var screen = Button.GetWindow()?.Size ?? new Vector2I(1920, 1080);
        int width = PopupWidth;
        int height = (int)_popup.GetContentsMinimumSize().Y;
        if (height <= 0) height = RowHeight * VisibleRows + 48;

        int x = (int)rect.Position.X;
        int y = (int)(rect.Position.Y + rect.Size.Y);
        if (x + width > screen.X) x = Math.Max(0, screen.X - width);
        if (y + height > screen.Y) y = (int)Math.Max(0, rect.Position.Y - height);

        _popup.Popup(new Rect2I(new Vector2I(x, y), new Vector2I(width, height)));
    }

    private void RenderList()
    {
        string query = _search.Text.Trim();
        var filtered = query.Length == 0
            ? _items
            : _items.Where(e => e.Label.Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();

        _shown = _pinned.Concat(filtered).ToList();
        _list.Clear();
        foreach (var entry in _shown) _list.AddItem(entry.Label, entry.Icon);

        if (filtered.Count == 0 && _items.Count > 0)
        {
            int idx = _list.AddItem("No matching items");
            _list.SetItemSelectable(idx, false);
            _list.SetItemDisabled(idx, true);
        }
    }

    private void MoveSelection(int delta)
    {
        int count = _list.ItemCount;
        if (count == 0) return;
        var current = _list.GetSelectedItems();
        int next = current.Length == 0 ? (delta > 0 ? 0 : count - 1) : Mathf.Clamp(current[0] + delta, 0, count - 1);
        while (next >= 0 && next < count && !_list.IsItemSelectable(next)) next += delta > 0 ? 1 : -1;
        if (next < 0 || next >= count) return;
        _list.Select(next);
        _list.EnsureCurrentIsVisible();
    }

    private void PickCurrent()
    {
        var selected = _list.GetSelectedItems();
        if (selected.Length == 0)
        {
            // Nothing highlighted yet: Enter picks the first real (pinned or matched) row.
            if (_shown.Count == 0) return;
            Choose(_shown[0].Key);
            return;
        }
        int idx = selected[0];
        if (idx < 0 || idx >= _shown.Count || !_list.IsItemSelectable(idx)) return;
        Choose(_shown[idx].Key);
    }

    private void Choose(string? key)
    {
        _popup.Hide();
        _onPick(key);
    }

    private void OnSearchInput(InputEvent ev)
    {
        if (ev is not InputEventKey { Pressed: true, Echo: false } key) return;
        switch (key.Keycode)
        {
            case Key.Down:
                MoveSelection(1);
                _search.AcceptEvent();
                break;
            case Key.Up:
                MoveSelection(-1);
                _search.AcceptEvent();
                break;
            case Key.Escape:
                Close();
                _search.AcceptEvent();
                break;
        }
    }
}
