using Godot;
using System;

public class Marker : Spatial
{
    private MultiMeshInstance _visual;
    private Label3D _label;
    private Sprite3D _stand;

    public SpatialMaterial Material
    {
        set {
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

        var blockCountThreshold = 10;
        if (_visual.Multimesh.InstanceCount < blockCountThreshold)
        {
            _label.Visible = false;
            _stand.Visible = false;
        }
    }

    public override void _Ready()
    {
        _visual = GetNode<MultiMeshInstance>("%Volume");
        _label = GetNode<Label3D>("%Label");
        _stand = GetNode<Sprite3D>("%Stand");
    }
}