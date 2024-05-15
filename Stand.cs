using Godot;
using System;

public class Stand : Sprite3D
{
    private Spatial _parent;
    private Sprite3D _baseMarker;

    // Called when the node enters the scene tree for the first time.
    public override void _Ready()
    {
        var parentNode = GetParent();
        if (parentNode != null)
        {
            if (parentNode is Spatial node)
            {
                _parent = node;
                // Ensure the baseMarker is the first child
                if (GetChild(0) is Sprite3D marker)
                {
                    _baseMarker = marker;
                }
                else
                {
                    GD.PrintErr("Stand's first child must be a Sprite3D node.");
                    QueueFree();
                }
            }
            else
            {
                GD.PrintErr("Stand is child of non-Spatial node");
                QueueFree();
            }
        }
        else
        {
            GD.PrintErr("Stand must not be root node of scene");
            QueueFree();
        }
    }

    public override void _Process(float delta)
    {
        if (_parent != null && _baseMarker != null)
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

            // Ensure 1 unit height to allow for accurate scaling
            var texHeight = Texture.GetHeight();
            PixelSize = 1.0f / texHeight;

            // Adjust the scale of the stand
            Vector3 currentScale = Scale;
            Scale = new Vector3(currentScale.x, height, currentScale.z);

            // Adjust the offset to ensure the stand is correctly positioned
            Offset = new Vector2(0, texHeight / 2.0f * -(float)Math.Sign(parentPos.y));

            // Position the baseMarker at the parent's X and Z, but at Y=0 without changing its rotation
            Vector3 baseMarkerPos = new Vector3(parentPos.x, 0, parentPos.z);
            Transform baseMarkerTransform = new Transform(Basis.Identity, baseMarkerPos);
            _baseMarker.GlobalTransform = baseMarkerTransform;
        }
    }
}
