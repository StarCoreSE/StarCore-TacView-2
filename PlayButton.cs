using Godot;
using System;

public class PlayButton : Button
{
    [Export] public Texture IconPlay;
    [Export] public Texture IconPause;

    private bool _isPlaying;
    public bool IsPlaying
    {
        get => _isPlaying;
        set
        {
            _isPlaying = value;
            Icon = IsPlaying ? IconPause : IconPlay;
        }
    }
}
