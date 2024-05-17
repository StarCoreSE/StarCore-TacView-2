using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

public class InfoWindow : Container
{
    // Declare member variables here. Examples:
    // private int a = 2;
    // private string b = "text";

    // Called when the node enters the scene tree for the first time.
    private Dictionary<string, Main.Grid> _gridDictionary;
    public override void _Ready()
    {
        
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
                Button newButton = new Button
                {
                    Name = grid.EntityId,
                    Text = grid.Name, // Assuming grids have an EntityName property
                    Visible = true
                };
                newButton.SizeFlagsHorizontal = (int)SizeFlags.ExpandFill;
                newButton.Connect("pressed", this, nameof(OnButtonClicked), new Godot.Collections.Array{newButton});
                list.AddChild(newButton);
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
                        var marker = GetNode<Main>("/root/Node/Spatial").MarkerFromGrid(grid);
                        if (marker != null)
                        {
                            orbitalCamera.TrackedSpatial = marker;
                        }
                    }
                }
            }
        }
    }

//  // Called every frame. 'delta' is the elapsed time since the previous frame.
    //  public override void _Process(float delta)
    //  {
    //      
    //  }
}
