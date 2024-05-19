using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

public class InfoWindow : Container
{
    private Dictionary<string, Main.Grid> _gridDictionary;
    private LineEdit _search;
    private Dictionary<Button, bool> _initialVisibility = new Dictionary<Button, bool>();

    public override void _Ready()
    {
        _search = GetNode<LineEdit>("%SearchFilter");
        if (_search == null)
        {
            GD.PrintErr("InfoWindow failed to find SearchFilter node");
        }
        else
        {
            _search.Connect("text_changed", this, nameof(OnSearchFilterChanged));
        }
    }

    public void Refresh(ref List<List<Main.Grid>> frames, int currentFrame)
    {
        var grids = frames[currentFrame];
        // Convert the list of grids to a dictionary for quick lookup
        _gridDictionary = grids.ToDictionary(grid => grid.EntityId);

        // Get the VBoxContainer that holds the buttons
        var list = GetNode<VBoxContainer>("%ItemList");

        // Get the current children of the VBoxContainer
        var children = list.GetChildren();

        // Cache visibility toggling for existing buttons
        foreach (var child in children)
        {
            if (child is Button button)
            {
                button.Visible = false; // Hide all buttons initially

                // If the button's name matches an EntityId in the dictionary, make it visible
                if (_gridDictionary.ContainsKey(button.Name))
                {
                    if (!_gridDictionary[button.Name].Name.StartsWith("Large Grid"))
                    {
                        button.Visible = true;
                    }
                }
            }
        }

        // Add new buttons for any grids that don't have an existing button
        foreach (var grid in grids)
        {
            bool buttonExists = false;

            // Check if a button for this grid already exists
            foreach (var child in children)
            {
                if (child is Button button && button.Name == grid.EntityId)
                {
                    buttonExists = true;
                    break;
                }
            }

            // If no button exists for this grid, create a new one
            if (!buttonExists)
            {
                var shouldShowButton = (!grid.Name.StartsWith("Large Grid"));
                Button newButton = new Button
                {
                    Name = grid.EntityId,
                    Text = grid.Name, // Assuming grids have an EntityName property
                    Visible = shouldShowButton,
                    Align = Button.TextAlign.Left
                };
                newButton.SizeFlagsHorizontal = (int)SizeFlags.ExpandFill;
                newButton.ClipText = true;
                newButton.Connect("pressed", this, nameof(OnButtonClicked), new Godot.Collections.Array { newButton });
                list.AddChild(newButton);
            }
        }

        // Cache the initial visibility state of each button
        _initialVisibility.Clear();
        foreach (Node child in list.GetChildren())
        {
            if (child is Button button)
            {
                _initialVisibility[button] = button.Visible;
            }
        }

        // Re-apply the search filter
        ReapplySearchFilter();
    }

    private void ReapplySearchFilter()
    {
        var filter = _search.Text.ToLower();
        var list = GetNode<VBoxContainer>("%ItemList");
        foreach (Node child in list.GetChildren())
        {
            if (child is Button button)
            {
                if (string.IsNullOrEmpty(filter))
                {
                    // Restore initial visibility
                    button.Visible = _initialVisibility[button];
                }
                else
                {
                    // Filter based on text
                    button.Visible = button.Text.ToLower().Contains(filter);
                }
            }
        }
    }

    public void OnButtonClicked(Button sender)
    {
        if (sender != null)
        {
            var camera = GetViewport().GetCamera();
            if (camera != null)
            {
                if (camera is OrbitalCamera orbitalCamera)
                {
                    if (_gridDictionary.TryGetValue(sender.Name, out var grid))
                    {
                        var marker = GetNode<Main>("/root/Main").MarkerFromGrid(grid);
                        if (marker != null)
                        {
                            orbitalCamera.TrackedSpatial = marker;
                        }
                    }
                }
            }
        }
    }

    public void OnSearchFilterChanged(string filter)
    {
        ReapplySearchFilter();
    }
}
