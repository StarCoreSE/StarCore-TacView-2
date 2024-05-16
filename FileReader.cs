using System;
using Godot;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.IO;
using System.Linq;

public class FileReader : Node
{
    private FileProcessor fileProcessor;
    private Queue<Segment> segmentQueue;
    private Queue<ProcessedSegment> processedSegmentQueue;
    private bool isReading;
    private Task<List<Segment>> currentReadingTask;

    private Button _buttonOpen;
    private Button _buttonStep;
    private RichTextLabel _textOutput;
    private FileDialog _dialog;
    private Label _fileLabel;
    private VBoxContainer _segmentContainer;

    [Export] public string DebugDefaultFilePath =
        @"C:\Users\Munashe Dov\AppData\Roaming\SpaceEngineers\Saves\76561198037407710\sccoordwriter\Storage\SCCoordinateOutput_ScCoordWriter\14-05-2024 1040.scc";
    public override void _Ready()
    {
        segmentQueue = new Queue<Segment>();
        processedSegmentQueue = new Queue<ProcessedSegment>();

        try
        {
            _buttonOpen = GetNode<Button>("%Open");
            _buttonStep = GetNode<Button>("%Step");
            _textOutput = GetNode<RichTextLabel>("%TextOutput");
            _dialog = GetNode<FileDialog>("%FileDialog");
            _fileLabel = GetNode<Label>("%FileLabel");
            _segmentContainer = GetNode<VBoxContainer>("%SegmentListContainer");
        }
        catch (InvalidCastException e)
        {
            GD.PrintErr($"Error: Invalid cast - {e.Message}");
        }
        catch (Exception e)
        {
            GD.PrintErr($"Error: Node not found or other issue - {e.Message}");
            return;
        }

        if (_buttonOpen == null)
        {
            GD.PrintErr("Error: Button 'Open' not found or invalid cast!");
            return;
        }
        if (_buttonStep == null)
        {
            GD.PrintErr("Error: Button 'Step' not found or invalid cast!");
            return;
        }
        if (_textOutput == null)
        {
            GD.PrintErr("Error: RichTextLabel 'TextOutput' not found or invalid cast!");
            return;
        }

        if (_dialog == null)
        {
            GD.PrintErr("Error: FileDialog 'FileDialog' not found or invalid cast!");
            return;
        }
        if (_fileLabel == null)
        {
            GD.PrintErr("Error: Label 'FileLabel' not found or invalid cast!");
            return;
        }
        if (_segmentContainer == null)
        {
            GD.PrintErr("Error: VBoxContainer 'SegmentListContainer' not found or invalid cast!");
            return;
        }
        _textOutput.Text = "";
        _buttonOpen.Connect("pressed", this, nameof(OnButtonOpenPressed));
        _dialog.Connect("file_selected", this, nameof(OnDialogFileSelected));

        if (!string.IsNullOrEmpty(DebugDefaultFilePath))
        {
            OnDialogFileSelected(DebugDefaultFilePath);
        }
    }
    public void OnButtonOpenPressed()
    {
        _dialog.PopupCentered();
    }

    public bool OnDialogFileSelected(string path)
    {
        _textOutput.Text = "";
        _fileLabel.Text = path.Split('\\').Last();
        SetTargetFile(path);
        return true;
    }

    public bool SetTargetFile(string filename)
    {
        fileProcessor = new FileProcessor(filename);
        if (fileProcessor != null)
        {
            StartReadingFile();
            return true;
        }
        return false;
    }

    async void StartReadingFile()
    {
        isReading = true;
        while (isReading)
        {
            currentReadingTask = fileProcessor.GetSegmentsAsync();
            var segments = await currentReadingTask;

            foreach (var segment in segments)
            {
                segmentQueue.Enqueue(segment);
            }

            if (segments.Count == 0)
            {
                // Sleep for a short time to prevent busy waiting
                await Task.Delay(500);
            }
        }
    }

    public override void _Process(float delta)
    {
        // Continue processing segments if there are any
        if (segmentQueue.Count > 0)
        {
            var segment = segmentQueue.Dequeue();
            ProcessSegment(segment);
        }

        // Handle any processed segments
        if (processedSegmentQueue.Count > 0)
        {
            var processedSegment = processedSegmentQueue.Dequeue();
            ProcessSegmentInGame(processedSegment);
        }
    }

    private void ProcessSegment(Segment segment)
    {
        // Implement your segment processing logic here
        var processedSegment = new ProcessedSegment();
        foreach (var entry in segment.Entries)
        {
            processedSegment.ProcessedEntries.Add(entry); // Example processing
        }
        processedSegmentQueue.Enqueue(processedSegment);
    }

    private void ProcessSegmentInGame(ProcessedSegment processedSegment)
    {
        // Implement your game-specific logic here
        var blueprint = _segmentContainer.GetChild(0) as PanelContainer;
        blueprint.Visible = false;
        var seg = blueprint.Duplicate() as PanelContainer;
        seg.Visible = true;
        _segmentContainer.AddChild(seg);
        var label = seg.GetNode<Label>("Container/Label");
        label.Text = "";
        foreach (var entry in processedSegment.ProcessedEntries)
        {
            var l = label.Duplicate() as Label;
            l.Text = entry;
            seg.GetNode<VBoxContainer>("Container").AddChild(l);
        }
        label.Visible = false;
    }
}

public class ProcessedSegment
{
    public List<string> ProcessedEntries { get; } = new List<string>();

    // Additional properties and methods for processed segment can be added here
}
