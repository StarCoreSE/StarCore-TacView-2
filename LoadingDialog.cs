using Godot;
using System;

public class LoadingDialog : PanelContainer
{
    private Button _cancel;
    private ProgressBar _progress;
    private Label _title;

    public Action Cancelled;

    public override void _Ready()
    {
        _cancel = GetNode<Button>("%Cancel");
        _progress = GetNode<ProgressBar>("%ProgressBar");
        _title = GetNode<Label>("%Title");

        _cancel.Connect("button_down", this, nameof(OnCancel));
    }

    private void OnCancel()
    {
        Cancelled?.Invoke();
        _progress.Value = 0;
        Visible = false;
    }

    public void SetTitle(string title)
    {
        _title.Text = title;
        _progress.Value = 0;
        Visible = true;
    }

    public void SetProgress(float value)
    {
        _progress.Value = value * 100.0f;
        if (value >= 1.0f)
        {
            _progress.Value = 0;
            Visible = false;
        }
    }
}
