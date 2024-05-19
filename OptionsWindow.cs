using Godot;
using System;

public class OptionsWindow : PanelContainer
{
    private Control _titlebar;
    private Control _content;
    private Button _toggleButton;
    private bool _isExpanded;
    [Export] public Vector2 ExpandedSize = new Vector2(200.0f, 0.0f);

    public bool ShowNames;
    public bool ShowStands;

    private CheckBox _showNames;
    private CheckBox _showStands;

    public override void _Ready()
    {
        _titlebar = (Control)GetNode("%TitleBar");
        _content = (Control)GetNode("%Content");
        _toggleButton = (Button)GetNode("%TitleBar");
        GD.Print($"title   size {_titlebar.RectSize}");
        GD.Print($"content size {_content.RectSize}");

        _toggleButton.Connect("pressed", this, nameof(OnToggleButtonPressed));

        _showNames = GetNode<CheckBox>("%ShowNames");
        _showNames.Connect("toggled", this, nameof(OnShowNamesToggled));
        ShowNames = _showNames.Pressed;

        _showStands = GetNode<CheckBox>("%ShowStands");
        _showStands.Connect("toggled", this, nameof(OnShowStandsToggled));
        ShowStands = _showStands.Pressed;
    }

    private void OnToggleButtonPressed()
    {
        _isExpanded = !_isExpanded;
        _content.Visible = _isExpanded;
        _content.Visible = _isExpanded;
        _titlebar.RectMinSize = _isExpanded ? ExpandedSize : Vector2.Zero;
    }

    public void OnShowNamesToggled(bool state)
    {
        ShowNames = state;
    }

    public void OnShowStandsToggled(bool state)
    {
        ShowStands = state;
    }
}
