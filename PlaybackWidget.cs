using System;
using Godot;

public class PlaybackWidget : PanelContainer
{
    private int _frameCount;

    private bool _isPlaying;

    private PlayButton _playButton;

    private Action<float> _setScrubber = f => { };
    public bool IsLooping;
    public bool IsSliding;

    private float _scrubber;

    public HSlider SliderScrubber;


    public OptionButton SpeedDropdown;

    public int SpeedIndex = 2;

    public float[] SpeedMultipliers =
    {
        10.0f,
        4.0f,
        1.1f,
        0.5f
    };

    public string[] SpeedStrings =
    {
        "Very Fast",
        "Fast",
        "Realtime",
        "Slow"
    };

    public Label TimeLabel;

    public bool IsPlaying
    {
        get => _isPlaying;
        set
        {
            _isPlaying = value;
            _playButton.IsPlaying = value;
        }
    }

    public bool IsStreaming { get; set; }

    public override void _Ready()
    {
        SliderScrubber = GetNode("%SliderScrubber") as HSlider;
        if (SliderScrubber == null)
        {
            GD.PrintErr("Error: SliderScrubber not found.");
            return;
        }

        SliderScrubber.Connect("drag_started", this, nameof(OnSliderDragStarted));
        SliderScrubber.Connect("drag_ended", this, nameof(OnSliderDragEnded));
        SliderScrubber.Connect("value_changed", this, nameof(OnSliderValueChanged));

        _playButton = GetNode("%PlayButton") as PlayButton;
        if (_playButton == null)
        {
            GD.PrintErr("Error: PlayButton not found.");
            return;
        }

        _playButton.Connect("pressed", this, nameof(OnPlayButtonPressed));

        SpeedDropdown = GetNode("%SpeedDropdown") as OptionButton;
        if (SpeedDropdown == null)
        {
            GD.PrintErr("Error: SpeedDropdown not found.");
            return;
        }

        foreach (var t in SpeedStrings) SpeedDropdown.AddItem(t);

        // start at Realtime speed
        SpeedDropdown.Selected = 2;
        SpeedDropdown.Connect("item_selected", this, nameof(OnSpeedDropdownItemSelected));

        TimeLabel = GetNode("%TimeLabel") as Label;
        if (TimeLabel == null)
        {
            GD.PrintErr("Error: TimeLabel not found.");
            return;
        }

        SliderScrubber.Editable = false;
        _playButton.Disabled = true;
    }

    public override void _Process(float delta)
    {
        if (_frameCount > 0)
        {
            if (IsPlaying || IsSliding)
                TimeLabel.Text = SecondsToTime((float)Math.Floor(_scrubber * _frameCount)) + "/" +
                                 SecondsToTime(_frameCount);
            if (IsPlaying && !IsSliding)
            {
                _scrubber += delta / (_frameCount / SpeedMultipliers[SpeedIndex]);
                SliderScrubber.Value = _scrubber * 100;
            }

            if (_scrubber > 1.0f) _scrubber = IsLooping ? 0 : 1;

            _setScrubber(_scrubber);
        }

        if (IsStreaming)
        {
            var si = GetNode("%StreamingIndicator") as CanvasItem;
            si.Modulate = Color.FromHsv(0f, .9f, .8f);
        }
        else
        {
            var si = GetNode("%StreamingIndicator") as CanvasItem;
            si.Modulate = Color.FromHsv(0f, .0f, .7f);
        }
    }

    public void SetRecording(int frameCount, Action<float> setScrubber)
    {
        _setScrubber = setScrubber;
        _scrubber = 0;
        setScrubber(_scrubber);
        _frameCount = frameCount;
        if (_frameCount > 0)
        {
            IsPlaying = true;
            SliderScrubber.Editable = true;
            TimeLabel.Text = SecondsToTime((float)Math.Floor(_scrubber * _frameCount)) + "/" +
                             SecondsToTime(_frameCount);
        }

        _playButton.Disabled = !(_frameCount > 0);
    }

    public void OnSliderDragStarted()
    {
        IsSliding = true;
    }

    public void OnSliderDragEnded(bool valueChanged)
    {
        IsSliding = false;
    }

    public void OnSliderValueChanged(float value)
    {
        _scrubber = value / 100;
        _setScrubber(_scrubber);
    }

    public void OnPlayButtonPressed()
    {
        IsPlaying = !IsPlaying;
    }

    public void OnSpeedDropdownItemSelected(int index)
    {
        SpeedIndex = index;
    }

    public string SecondsToTime(float e)
    {
        string h = Math.Floor(e / 3600).ToString().PadLeft(2, '0'),
            m = Math.Floor(e % 3600 / 60).ToString().PadLeft(2, '0'),
            s = Math.Floor(e % 60).ToString().PadLeft(2, '0');

        return h + ':' + m + ':' + s;
    }
}