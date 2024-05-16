using Godot;
using System;

public class Stand : Spatial
{
    private Spatial _parent;
    private Sprite3D _line;
    private Sprite3D _baseMarker;
    private Camera _camera;

    [Export] public float LineThicknessScalar = 0.004f;
    [Export] public Color Modulate;

    public override void _Ready()
    {
        // Get the parent node, which should be the new root Spatial node
        _parent = GetParent() as Spatial;
        if (_parent == null)
        {
            GD.PrintErr("Stand must be a child of a Spatial node.");
            QueueFree();
            return;
        }

        // Get the Line and Base nodes
        _line = GetNode<Sprite3D>("Line");
        _baseMarker = GetNode<Sprite3D>("Base");

        if (_line == null || _baseMarker == null)
        {
            GD.PrintErr("Stand must have Line and Base nodes as children.");
            QueueFree();
        }

        _camera = GetViewport().GetCamera();
        if (_camera == null)
        {
            GD.PrintErr("Camera node not found");
        }
    }

    public override void _Process(float delta)
    {
        if (_parent != null && _camera != null && _line != null && _baseMarker != null)
        {
            // Get parent's global transform
            Transform parentTransform = _parent.GlobalTransform;
            Vector3 parentPos = parentTransform.origin;

            // Calculate the height between the parent and Y=0
            float height = Math.Abs(parentPos.y);
            if (height < 0.01f)
            {
                height = 0.01f; // Prevent zero height to avoid zero determinant
            }

            // Adjust the scale of the Line
            float distanceToCamera = GlobalTransform.origin.DistanceTo(_camera.GlobalTransform.origin);
            if (float.IsNaN(distanceToCamera) || float.IsInfinity(distanceToCamera))
            {
                GD.PrintErr("Distance to camera is invalid.");
                return;
            }
            float scaledFactor = distanceToCamera * LineThicknessScalar;
            Vector3 currentScale = _line.Scale;
            _line.Scale = new Vector3(scaledFactor, height, currentScale.z);
            
            // Adjust the offset to ensure the Line is correctly positioned
            var textureHeight = _line.Texture.GetHeight();
            _line.PixelSize = 1.0f / textureHeight;
            _line.Offset = new Vector2(0, textureHeight / 2.0f * -(float)Math.Sign(parentPos.y));
            _line.Modulate = Modulate;

            // Position the Base marker at the parent's X and Z, but at Y=0 without changing its rotation
            Vector3 baseMarkerPos = new Vector3(parentPos.x, 0, parentPos.z);
            Transform baseMarkerTransform = new Transform(Basis.Identity, baseMarkerPos);
            _baseMarker.Scale = new Vector3(scaledFactor, 1.0f, scaledFactor);
            _baseMarker.GlobalTransform = baseMarkerTransform;
            _baseMarker.Modulate = Modulate;
        }
        else
        {
            GD.PrintErr("Error: _parent, _line, or _baseMarker is null.");
        }
    }
}
