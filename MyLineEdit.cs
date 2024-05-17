using Godot;

public class MyLineEdit : LineEdit
{
    public override void _Input(InputEvent @event)
    {
        if (HasFocus() && @event is InputEventMouseButton mouseEvent)
        {
            var globalMousePos = mouseEvent.GlobalPosition;
            var lineEditRect = GetGlobalRect();

            if (!lineEditRect.HasPoint(globalMousePos))
            {
                ReleaseFocus();
            }
        }
    }
}