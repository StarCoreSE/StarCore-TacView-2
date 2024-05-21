using Godot;
using System;
using static Main;

public class Marker : Spatial
{
    private MeshInstance _visual;
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

    public void UpdateVolume(Main.Volume volume)
    {
        // Set the MultiMesh for the visual node
        _visual.Mesh = volume.VisualNode.Mesh;

        // Get the collision shape node
        var collision = GetNode<CollisionShape>("%CollisionShape");

        // Create a new BoxShape with the extents based on the volume dimensions
        var box = new BoxShape();
        box.Extents = new Vector3(volume.Width, volume.Height, volume.Depth) * volume.GridSize / 2;
        collision.Shape = box;

        _visual.Transform = new Transform(Basis.Identity, -volume.CenterOfMass);
        collision.Translation = _visual.Translation; //- (volume.GridSize * Vector3.One * 0.5f);

        // Create a CubeMesh for LOD visualization
        var cube = new CubeMesh();
        cube.Size = box.Extents * 2.0f * BoundingBoxLodRatio;

        // Set the mesh to the LOD node
        _lod.Mesh = cube;

        // Adjust the transform of the LOD node to center it around the volume's center of mass
        _lod.Transform = new Transform(Basis.Identity, -volume.CenterOfMass);
    }



    public override void _Ready()
    {
        _visual = GetNode<MeshInstance>("%Volume");
        if (_visual == null)
        {
            GD.PrintErr("Error: _visual (MeshInstance) not found.");
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

        //if (_visual != null && _visual.Mesh != null)
        //{
        //    var blockCountThreshold = 20;
        //    if (_visual.Multimesh.InstanceCount < blockCountThreshold)
        //    {
        //        _label.Visible = false;
        //        _stand.Visible = false;
        //    }
        //}

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