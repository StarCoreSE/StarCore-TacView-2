using Godot;
using System;
using static Main;

public class Marker : Spatial
{
    private MultiMeshInstance _visual;
    private Label3D _label;
    private Stand _stand;
    private Camera _camera;
    private MeshInstance _lod;
    [Export] public float ThresholdLOD = 2500.0f;
    [Export] public float BoundingBoxLodRatio = 0.8f;
    public SpatialMaterial Material
    {
        set
        {
            if (_visual != null)
            {
                _visual.MaterialOverride = value;
                if (_lod != null)
                {
                    _lod.MaterialOverride = value;
                }
            }
        }
        get => _visual?.MaterialOverride as SpatialMaterial;
    }

    public void SetNameplateVisibility(bool visible)
    {
        _label.Visible = visible;
    }

    public void SetStandVisibility(bool visible)
    {
        _stand.Visible = visible;
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

    public void UpdateVolume(Main.Volume volume)
    {
        SetMultiMesh(volume.VisualNode.Multimesh);
        var collision = GetNode<CollisionShape>("%CollisionShape");
        var box = new BoxShape();
        box.Extents = new Vector3(volume.Width, volume.Height, volume.Depth) * volume.GridSize / 2;
        collision.Shape = box;

        var cube = new CubeMesh();
        cube.Size = box.Extents * 2.0f * BoundingBoxLodRatio;
        _lod.Mesh = cube;
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

        _stand = GetNode<Stand>("%Stand");
        if (_stand == null)
        {
            GD.PrintErr("Error: _stand (Sprite3D) not found.");
            return;
        }

        _lod = GetNode<MeshInstance>("%LOD");
        if (_lod == null)
        {
            GD.PrintErr("Error: _lod (MeshInstance) not found.");
            return;
        }

        _camera = GetViewport().GetCamera();
        if (_camera == null)
        {
            GD.PrintErr("Camera node not found");
        }
    }

    public override void _Process(float delta)
    {
        if (!Visible) return;

        float distanceToCamera = 100.0f;
        if (_camera != null)
        {
            distanceToCamera = GlobalTransform.origin.DistanceTo(_camera.GlobalTransform.origin);
        }
        if (float.IsNaN(distanceToCamera) || float.IsInfinity(distanceToCamera))
        {
            GD.PrintErr("Distance to camera is invalid.");
            return;
        }

        if (_visual != null && _visual.Multimesh != null)
        {
            var blockCountThreshold = 20;
            if (_visual.Multimesh.InstanceCount < blockCountThreshold)
            {
                _label.Visible = false;
                _stand.Visible = false;
            }
        }

        if (distanceToCamera >= ThresholdLOD)
        {
            _visual.Visible = false;
            _lod.Visible = true;
        }
        else
        {
            _visual.Visible = true;
            _lod.Visible = false;
        }
    }
}