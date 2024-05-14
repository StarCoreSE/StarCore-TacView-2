using Godot;

public class OrbitalCamera : Camera
{
    [Export]
    public float sensitivity = 0.01f;
    [Export]
    public float distanceFromTarget = 5.0f;
    private Vector2 _mouseDelta;
    private Vector2 rotationOffset = Vector2.Zero;
    private bool isDragging;
    public Spatial Pivot;
    public Spatial TrackedSpatial;

    public float zoomSpeed => 1f;

    public override void _Ready()
    {
        Pivot = GetParent() as Spatial;
        Translate(Vector3.Back * distanceFromTarget);
    }

    public override void _Input(InputEvent @event)
    {
        if (@event is InputEventMouseButton buttonEvent1 && buttonEvent1.ButtonIndex == (int)ButtonList.Right && buttonEvent1.Pressed)
        {
            Input.MouseMode = Input.MouseModeEnum.Captured;
        }
        else if (@event is InputEventMouseButton buttonEvent2 && buttonEvent2.ButtonIndex == (int)ButtonList.Right && !buttonEvent2.Pressed)
        {
            Input.MouseMode = Input.MouseModeEnum.Visible;
        }

        isDragging = Input.IsMouseButtonPressed((int)ButtonList.Right);

        if (@event is InputEventMouseButton buttonEvent)
        {
            float dynamicZoomSpeed = zoomSpeed * (distanceFromTarget / 10.0f); // Adjust this factor as needed

            switch ((ButtonList)buttonEvent.ButtonIndex)
            {
                case ButtonList.Right:
                    isDragging = true;
                    break;
                case ButtonList.WheelUp:
                    Translate(Vector3.Back * -dynamicZoomSpeed);
                    distanceFromTarget = this.GlobalTransform.origin.DistanceTo(Pivot.GlobalTransform.origin);
                    break;
                case ButtonList.WheelDown:
                    Translate(Vector3.Back * dynamicZoomSpeed);
                    distanceFromTarget = this.GlobalTransform.origin.DistanceTo(Pivot.GlobalTransform.origin);
                    break;
                default:
                    break;
            }
        }
        else if (@event is InputEventMouseMotion motion)
        {
            if (isDragging)
            {
                _mouseDelta = motion.Relative;
            }
        }
    }

    public override void _Process(float delta)
    {
        RotateCamera();

        if (Input.IsActionJustPressed("ui_select"))
        {
            Camera camera = GetViewport().GetCamera() as Camera;
            Vector3 rayOrigin = camera.ProjectRayOrigin(GetViewport().GetMousePosition());
            Vector3 rayNormal = camera.ProjectRayNormal(GetViewport().GetMousePosition());
            PhysicsDirectSpaceState spaceState = GetWorld().DirectSpaceState;
            var result = spaceState.IntersectRay(rayOrigin, rayOrigin + rayNormal * 100000);

            if (result != null)
            {
                if (!result.Contains("collider")) return;
                Spatial collider = result["collider"] as Spatial;
                if (collider == null) return;

                GD.Print("3D object under cursor:", collider);
                TrackedSpatial = collider;
            }
        }

        if (Input.IsActionJustPressed("ui_cancel"))
        {
            TrackedSpatial = null;
            Pivot.Translation = Vector3.Zero;
        }

        if (TrackedSpatial != null)
        {
            Pivot.Translation = TrackedSpatial.GlobalTranslation;
        }
    }

    private void RotateCamera()
    {
        if (Input.MouseMode == Input.MouseModeEnum.Captured)
        {
            rotationOffset.x += -_mouseDelta.y * sensitivity;
            rotationOffset.y += -_mouseDelta.x * sensitivity;
            _mouseDelta = Vector2.Zero;
        }

        Pivot.RotationDegrees = Vector3.Zero;
        Pivot.RotateX(rotationOffset.x);
        Pivot.RotateY(rotationOffset.y);
    }

    public override void _PhysicsProcess(float delta)
    {
        UpdateCameraPosition();
    }

    private void UpdateCameraPosition()
    {
        // Implement any additional camera position updates here
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        // Implement any additional unhandled input logic here
    }
}
