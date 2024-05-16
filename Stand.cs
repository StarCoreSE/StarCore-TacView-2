using Godot;
using System;

public class Stand : Sprite3D
{
    private Spatial _parent;
    private Sprite3D _baseMarker;

    public override void _Ready()
    {
        var parentNode = GetParent();
        if (parentNode != null)
        {
            if (parentNode is Spatial node)
            {
                _parent = node;
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
            Transform parentTransform = _parent.GlobalTransform;
            Vector3 parentPos = parentTransform.origin;

            float height = Math.Abs(parentPos.y);
            if (height < 0.01f)
            {
                height = 0.01f;
            }

            var texHeight = Texture.GetHeight();
            PixelSize = 1.0f / texHeight;

            Vector3 currentScale = Scale;
            Scale = new Vector3(currentScale.x, height, currentScale.z);

            Offset = new Vector2(0, texHeight / 2.0f * -(float)Math.Sign(parentPos.y));

            Vector3 baseMarkerPos = new Vector3(parentPos.x, 0, parentPos.z);
            Transform baseMarkerTransform = new Transform(Basis.Identity, baseMarkerPos);
            _baseMarker.GlobalTransform = baseMarkerTransform;
        }
        else
        {
            GD.PrintErr("Error: _parent or _baseMarker is null.");
        }
    }
}