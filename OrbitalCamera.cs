using Godot;


public class OrbitalCamera : Camera
{
    [Export]
    public float sensitivity = 0.01f;

    [Export]
    public float distanceFromTarget = 5.0f;

    public float zoomSpeed => 100;
    private Vector2 _mouseDelta;
    private Vector2 rotationOffset = Vector2.Zero;
    private bool isDragging;
    public Spatial Pivot;
    public Spatial TrackedSpatial;
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
            switch ((ButtonList)buttonEvent.ButtonIndex)
            {
                case ButtonList.Right:
                    isDragging = true;
                    break;
                case ButtonList.WheelUp:
                    Translate(Vector3.Back * -zoomSpeed);
                    distanceFromTarget = this.GlobalTransform.origin.DistanceTo(Pivot.GlobalTransform.origin);
                    break;
                case ButtonList.WheelDown:
                    Translate(Vector3.Back * zoomSpeed);
                    distanceFromTarget = this.GlobalTransform.origin.DistanceTo(Pivot.GlobalTransform.origin);
                    break;
                case ButtonList.Left:
                case ButtonList.Middle:
                case ButtonList.Xbutton1:
                case ButtonList.Xbutton2:
                case ButtonList.WheelLeft:
                case ButtonList.WheelRight:
                case ButtonList.MaskXbutton1:
                case ButtonList.MaskXbutton2:
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

        {
            if (Input.IsActionJustPressed("ui_select"))
            {
                // Get the current camera
                Camera camera = GetViewport().GetCamera() as Camera;

                // Get the ray from the cursor into the scene
                Vector3 rayOrigin = camera.ProjectRayOrigin(GetViewport().GetMousePosition());
                Vector3 rayNormal = camera.ProjectRayNormal(GetViewport().GetMousePosition());

                // Perform a collision test with objects in the scene
                PhysicsDirectSpaceState spaceState = GetWorld().DirectSpaceState;
                var result = spaceState.IntersectRay(rayOrigin, rayOrigin + rayNormal * 100000); // Adjust the distance as needed

                if (result != null)
                {
                    if (!result.Contains("collider")) return;
                    Spatial collider = result["collider"] as Spatial;
                    if (collider == null) return;
                    GD.Print("3D object under cursor:", collider);
                    //Pivot.Translate(collider.GlobalTransform.origin);
                    TrackedSpatial = collider;
                    //Pivot.GlobalTransform = new Transform(collider.GlobalTransform.basis, collider.GlobalTransform.origin);
                }
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
            //Pivot.GlobalTransform = new Transform(TrackedSpatial.GlobalTransform.basis, TrackedSpatial.GlobalTransform.origin);
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
    }

    public override void _UnhandledInput(InputEvent @event)
    {

    }
}

