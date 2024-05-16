using Godot;
using System;

public class Marker : Spatial
{
    private MultiMeshInstance _visual;
    private Label3D _label;
    private Sprite3D _stand;

    public SpatialMaterial Material
    {
        set
        {
            if (_visual != null)
            {
                _visual.MaterialOverride = value;
            }
        }
        get => _visual?.MaterialOverride as SpatialMaterial;
    }

    public void SetMultiMesh(MultiMesh mesh)
    {
        if (mesh == null)
        {
            GD.Print("Failed to set multimesh");
            return;
        }
        _visual.Multimesh = mesh;

        var blockCountThreshold = 20;
        if (_visual.Multimesh.InstanceCount < blockCountThreshold)
        {
            _label.Visible = false;
            _stand.Visible = false;
        }
    }

    public void SetGridSize(string gridSize)
    {
        if (_visual != null)
        {
            Vector3 size = gridSize == "Small" ? new Vector3(0.5f, 0.5f, 0.5f) : new Vector3(2.5f, 2.5f, 2.5f);
            _visual.Scale = size;
        }
    }

    public override void _Ready()
    {
        _visual = GetNode<MultiMeshInstance>("%Volume");
        if (_visual == null)
        {
            GD.PrintErr("Error: _visual (MultiMeshInstance) not found.");
            return;
        }

        _label = GetNode<Label3D>("%Label");
        if (_label == null)
        {
            GD.PrintErr("Error: _label (Label3D) not found.");
            return;
        }

        _stand = GetNode<Sprite3D>("%Stand");
        if (_stand == null)
        {
            GD.PrintErr("Error: _stand (Sprite3D) not found.");
            return;
        }
    }
}