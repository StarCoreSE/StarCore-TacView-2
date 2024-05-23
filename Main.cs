using System.Threading;
using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Godot;
public class Main : Node
{
    private PlaybackWidget _playbackWidget;
    private InfoWindow _infoWindow;
    private OptionsWindow _optionsWindow;
    private Recording _recording;
    private LoadingDialog _loadingDialog;

    private CancellationTokenSource _cancellationTokenSource;

    [Export] public PackedScene MarkerBlueprint;
    [Export] public SpatialMaterial MarkerMaterialBase;
    [Export] public Color NeutralColor;
    
    public override void _Ready()
    {
        GetTree().Connect("files_dropped", this, nameof(GetDroppedFilesPath));

        _playbackWidget = GetNode<PlaybackWidget>("%PlaybackWidget");
        if (_playbackWidget == null)
        {
            GD.PrintErr("Error: PlaybackWidget not found.");
            return;
        }

        _infoWindow = GetNode<InfoWindow>("%InfoWindow");
        if (_infoWindow == null)
        {
            GD.PrintErr("Error: InfoWindow not found.");
            return;
        }
        
        _optionsWindow = GetNode<OptionsWindow>("%OptionsWindow");
        if (_optionsWindow == null)
        {
            GD.PrintErr("Error: OptionsWindow not found.");
        }

        _loadingDialog = GetNode<LoadingDialog>("%LoadingDialog");
        if (_loadingDialog == null)
        {
            GD.PrintErr("Error: LoadingDialog not found.");
        }
    }

    public async void GetDroppedFilesPath(string[] files, int screen)
    {
        // Cancel any ongoing operation before starting a new one
        _cancellationTokenSource?.Cancel();
        _cancellationTokenSource = new CancellationTokenSource();
        var cancellationToken = _cancellationTokenSource.Token;

        if (files.Length == 0)
        {
            GD.PrintErr("Error: No files dropped.");
            return;
        }

        var filename = files[0];
        if (filename.EndsWith(".scc"))
        {
            try
            {
                // Use Task.Run to offload the synchronous work to a background thread
                _loadingDialog.Cancelled = () => _cancellationTokenSource.Cancel();

                // Cannot use multithreading in HTML export
                Recording nextRecording;
                if (!OS.HasFeature("web"))
                {
                    nextRecording = await Task.Run(() => Recording.Create(filename, _loadingDialog), cancellationToken);
                }
                else
                {
                    nextRecording = Recording.Create(filename, _loadingDialog).Result;
                }

                if (cancellationToken.IsCancellationRequested)
                {
                    nextRecording.Deinit();
                    nextRecording.QueueFree();
                    return; // Check for cancellation
                }

                if (nextRecording != null)
                {
                    if (GetNode("%Camera") is OrbitalCamera cameraNode)
                    {
                        cameraNode.TrackedSpatial = null;
                    }
                    else
                    {
                        GD.PrintErr("Error: Camera node not found or incorrect type.");
                    }
                    _recording?.Deinit();
                    _recording?.QueueFree();
                    _recording = nextRecording;
                    _playbackWidget.SetRecording(_recording);
                    _infoWindow.SetRecording(_recording);

                    _recording.NeutralColor = NeutralColor;
                    _recording.MarkerBlueprint = MarkerBlueprint;
                    _recording.MarkerMaterialBase = MarkerMaterialBase;
                    _recording.Options = _optionsWindow;
                    _recording.Info = _infoWindow;
                    _recording.MarkerRoot = GetNode<Spatial>("%Markers");
                }
                else
                {
                    GD.Print($"Failed to load recording from {filename}");
                }
            }
            catch (OperationCanceledException)
            {
                GD.Print("Operation was canceled.");
            }
            catch (Exception ex)
            {
                GD.PrintErr($"Error while loading recording: {ex.Message}");
            }
        }
        else
        {
            GD.PrintErr("Error: Dropped file is not an SCC file.");
        }
    }

    public override void _Process(float delta)
    {
        if (_recording != null && (_playbackWidget.IsPlaying || _playbackWidget.IsSliding))
        {
            if (!_playbackWidget.IsSliding)
            {
                _recording.MaybeReadNextSegmentFromFile(delta);
            }
            _recording.Refresh();
        }
    }
}
