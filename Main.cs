using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using Godot;
using File = Godot.File;
using Vector3 = Godot.Vector3;

public class Main : Spatial
{
    public int CurrentVersion = 2;

    private List<List<Grid>> Frames = new List<List<Grid>>();

    private File loadedFile = new File();

    private bool _isPlaying;

    public bool IsPlaying
    {
        get => _isPlaying;
        set
        {
            _isPlaying = value;
            PlayButton.Icon = IsPlaying ? IconPause : IconPlay;
        }
    }

    private int _currentFrame = 0;
    public int CurrentFrame
    {
        get => _currentFrame;
        set
        {
            if (value != _currentFrame)
            {
                OnFrameChanged();
            }
            _currentFrame = value;
        }
    }

    public bool isLooping;
    public bool isSliding;
    private float scrubber;

    private Dictionary<string, Marker> Markers = new Dictionary<string, Marker>();
    [Export] public PackedScene MarkerBlueprint;
    [Export] public string AutoloadSCCPath;
    private Dictionary<string, Volume> GridVolumes = new Dictionary<string, Volume>();

    [Export] public SpatialMaterial MarkerMaterialBase;

    [Export] public SpatialMaterial LineMaterial;

    [Export] public NodePath SliderScrubberPath;
    public HSlider SliderScrubber;

    [Export] public NodePath PlayButtonPath;
    public Button PlayButton;
    [Export] public Texture IconPlay;
    [Export] public Texture IconPause;

    [Export] public NodePath SpeedDropdownPath;
    public OptionButton SpeedDropdown;

    public string[] SpeedStrings =
    {
        "Very Fast",
        "Fast",
        "Realtime",
        "Slow",
    };

    public int SpeedIndex = 2;

    public float[] SpeedMultipliers =
    {
        10.0f,
        4.0f,
        1.1f,
        0.5f,
    };

    [Export] public NodePath TimeLabelPath;
    public Label TimeLabel;

    private InfoWindow _infoWindow;

    public Dictionary<string, SpatialMaterial> FactionColors = new Dictionary<string, SpatialMaterial>();


    [Export] public PackedScene CubePrefab; // Prefab for the cube mesh

    [Export] public Color NeutralColor;

    [Export] public float VoxelSizeMultiplier = 1.0f;


    public override void _Ready()
    {
        GetTree().Connect("files_dropped", this, nameof(GetDroppedFilesPath));
        if (AutoloadSCCPath != null)
        {
            File file = new File();
            Error error = file.Open(AutoloadSCCPath, File.ModeFlags.Read);

            if (error != Error.Ok)
            {
                GD.PrintErr("Error loading file: " + AutoloadSCCPath);
                return;
            }

            string fileContents = file.GetAsText();
            GD.Print("File content length: " + fileContents.Length);
            Frames = ParseSCC(fileContents);
            file.Close();
            GD.Print(Frames.Count);
            if (Frames.Count > 0)
            {
                IsPlaying = true;
            }
        }

        SliderScrubber = GetNode(SliderScrubberPath) as HSlider;
        if (SliderScrubber == null)
        {
            GD.PrintErr("Error: SliderScrubber not found.");
            return;
        }
        SliderScrubber.Connect("drag_started", this, nameof(OnSliderDragStarted));
        SliderScrubber.Connect("drag_ended", this, nameof(OnSliderDragEnded));
        SliderScrubber.Connect("value_changed", this, nameof(OnSliderValueChanged));

        PlayButton = GetNode(PlayButtonPath) as Button;
        if (PlayButton == null)
        {
            GD.PrintErr("Error: PlayButton not found.");
            return;
        }
        PlayButton.Connect("pressed", this, nameof(OnPlayButtonPressed));
        PlayButton.Icon = IsPlaying ? IconPause : IconPlay;

        SpeedDropdown = GetNode(SpeedDropdownPath) as OptionButton;
        if (SpeedDropdown == null)
        {
            GD.PrintErr("Error: SpeedDropdown not found.");
            return;
        }

        for (var i = 0; i < SpeedStrings.Length; i++)
        {
            SpeedDropdown.AddItem(SpeedStrings[i]);
        }

        // start at Realtime speed
        SpeedDropdown.Selected = 2;
        SpeedDropdown.Connect("item_selected", this, nameof(OnSpeedDropdownItemSelected));

        TimeLabel = GetNode(TimeLabelPath) as Label;
        if (TimeLabel == null)
        {
            GD.PrintErr("Error: TimeLabel not found.");
            return;
        }

        _infoWindow = GetNode<InfoWindow>("%InfoWindow");
        if (_infoWindow == null)
        {
            GD.PrintErr("Error: InfoWindow not found.");
            return;
        }

        // Initialize the PID controller with gains (you may need to tune these)
        pidController = new PIDController(0.5f, 0.02f, 0.05f);
    }

    public void OnSliderDragStarted()
    {
        isSliding = true;
    }

    public void OnSliderDragEnded(bool valueChanged)
    {
        isSliding = false;
    }

    public void OnSliderValueChanged(float value)
    {
        scrubber = value / 100;
        Update();
    }

    public void OnPlayButtonPressed()
    {
        IsPlaying = !IsPlaying;
    }

    public void OnSpeedDropdownItemSelected(int index)
    {
        SpeedIndex = index;
    }

    public void GetDroppedFilesPath(string[] files, int screen)
    {
        if (files.Length == 0)
        {
            GD.PrintErr("Error: No files dropped.");
            return;
        }

        var file = files[0];
        if (file.EndsWith(".scc"))
        {
            // Clear camera focus, because the object won't be in the new recording
            (GetNode("%Camera") as OrbitalCamera).TrackedSpatial = null;


            loadedFile.Open(file, File.ModeFlags.Read);
            var content = loadedFile.GetAsText();
            previousFileLength = loadedFile.GetLen();
            LineNumber = 1;
            //LineNumber = content.Count(c => c == '\n');

            FactionColors.Clear();
            foreach (var kv in GridVolumes)
            {
                if (kv.Value.VisualNode != null)
                {
                    kv.Value.VisualNode.QueueFree();
                }
            }
            GridVolumes.Clear();
            foreach (var marker in Markers)
            {
                marker.Value.QueueFree();
            }
            Markers.Clear();

            var result = ParseSCC(content);
            if (result.Count > 0)
            {
                Frames = result;
                IsPlaying = true;
                scrubber = 0;
            }
            else
            {
                GD.PrintErr("Failed to load SCC " + file);
            }
        }
        else
        {
            GD.PrintErr("Error: Dropped file is not an SCC file.");
        }

        SliderScrubber.Editable = Frames.Count > 0;
        PlayButton.Disabled = !(Frames.Count > 0);
    }

    public void OnFrameChanged()
    {
        _infoWindow.Refresh(ref Frames, CurrentFrame);
    }

    public string SecondsToTime(float e)
    {
        string h = Math.Floor(e / 3600).ToString().PadLeft(2, '0'),
            m = Math.Floor(e % 3600 / 60).ToString().PadLeft(2, '0'),
            s = Math.Floor(e % 60).ToString().PadLeft(2, '0');

        return h + ':' + m + ':' + s;
    }

    public int ScrubberToFrameIndex(float scrubberLocal, int frameCount)
    {
        // Ensure scrubber value is within the valid range [0, 1]
        scrubberLocal = Mathf.Clamp(scrubberLocal, 0, 1);

        // Calculate the proportion and remap the scrubber value
        var proportion = 1.0 / (frameCount - 1);
        var remapped = scrubberLocal / proportion;

        // Convert the remapped value to an integer index
        var currentIndex = (int)remapped;

        // Ensure the index is within valid range
        if (currentIndex >= frameCount)
        {
            currentIndex = frameCount - 1;
        }

        if (currentIndex < 0)
        {
            currentIndex = 0;
        }

        return currentIndex;
    }

    public void Update()
    {
        if (Frames.Count == 0)
        {
            return;
        }

        TimeLabel.Text = SecondsToTime((float)Math.Floor(scrubber * (Frames.Count))) + "/" +
                         SecondsToTime((float)Frames.Count);

        var proportion = 1.0 / (Frames.Count - 1);
        var remapped = scrubber / proportion;
        var currentIndex = (int)remapped;
        var lastIndex = currentIndex - 1;

        if (lastIndex < 0)
        {
            lastIndex = 0;
        }

        CurrentFrame = ScrubberToFrameIndex(scrubber, Frames.Count);
        
        var currentFrame = Frames[CurrentFrame];
        var lastFrame = CurrentFrame > 0 ? Frames[CurrentFrame-1] : currentFrame;

        try
        {
            var markersNode = GetNode<Node>("Markers");
            foreach (Node markerNode in markersNode.GetChildren())
            {
                if (markerNode is Marker marker)
                {
                    marker.Visible = false;
                }
                else
                {
                    GD.PrintErr($"Node '{markerNode.Name}' is not of type Marker. Actual type: {markerNode.GetType().Name}");
                }
            }
        }
        catch (InvalidCastException ex)
        {
            GD.PrintErr($"Invalid cast exception occurred: {ex.Message}\n{ex.StackTrace}");
        }
        catch (Exception ex)
        {
            GD.PrintErr($"Exception occurred: {ex.Message}\n{ex.StackTrace}");
        }

        foreach (var grid in currentFrame)
        {
            if (grid.Faction != "Unowned" && FactionColors.ContainsKey(grid.Faction) && FactionColors[grid.Faction] != MarkerMaterialBase)
            {
                FactionColors[grid.Faction].AlbedoColor = Color.FromHsv(grid.FactionColor.x, 0.9f, 0.15f);
            }

            Marker marker;
            if (Markers.TryGetValue(grid.EntityId, out marker))
            {
                marker.Visible = true;
                if (FactionColors.TryGetValue(grid.EntityId, out var color))
                {
                    marker.Material = color;
                }
            }
            else
            {
                marker = MarkerBlueprint.Instance<Marker>();
                if (marker == null)
                {
                    GD.PrintErr($"Failed to instantiate marker for grid.EntityId {grid.EntityId}");
                    continue;
                }
                Markers[grid.EntityId] = marker;
                marker.GetNode<Label3D>("Label").Text = grid.Name;

                if (!FactionColors.ContainsKey(grid.Faction))
                {
                    var material = MarkerMaterialBase.Duplicate() as SpatialMaterial;
                    material.AlbedoColor = grid.Faction == "Unowned" ? NeutralColor : Color.FromHsv(grid.FactionColor.x, 0.95f, 0.2f);
                    FactionColors.Add(grid.Faction, material);
                }

                if (!FactionColors.TryGetValue(grid.Faction, out var factionColor))
                {
                    factionColor = MarkerMaterialBase;
                }

                GetNode<Spatial>("Markers").AddChild(marker);

                if (GridVolumes.TryGetValue(grid.EntityId, out var volume))
                {
                    marker.UpdateVolume(volume);
                    marker.Material = factionColor;
                }
            }

            var last = lastFrame.Find(e => e.EntityId == grid.EntityId);
            var t = 0.0;
            var lastPosition = grid.Position;
            var lastOrientation = grid.Orientation;

            if (last != null && currentIndex != 0)
            {
                lastPosition = last.Position;
                lastOrientation = last.Orientation;
                t = remapped - (int)remapped;
            }

            var position = lastPosition.LinearInterpolate(grid.Position, (float)t);
            marker.Translation = position;

            var partial = lastOrientation.Normalized().Slerp(grid.Orientation.Normalized(), (float)t);
            marker.Rotation = partial.GetEuler();
        }
    }

    public void SubtractFrameTime(ref float scrubber, int totalFrames)
    {
        if (totalFrames <= 1)
        {
            GD.PrintErr("Error: Not enough frames to adjust scrubber.");
            return;
        }

        var proportion = 1.0f / (totalFrames - 1);
        scrubber -= proportion;

        // Ensure the scrubber does not go below 0
        if (scrubber < 0)
        {
            scrubber = 0;
        }
    }

    public float secondsSinceLastReadAttempt = 0;
    public ulong previousFileLength = 0;
    public int LineNumber = 0;
    public List<string> ColumnHeaders = new List<string> { "kind", "name", "owner", "faction", "factionColor", "entityId", "health", "position", "rotation", "gridSize" };

    private PIDController pidController;
    private float targetBuffer = 1.0f; // Target buffer size in frames
    private float playbackSpeed = 1.0f; // Initial playback speed
    private bool isStreaming = false; // Flag to indicate if we are in streaming mode

    public override void _Process(float delta)
    {
        secondsSinceLastReadAttempt += delta;
        if (!isSliding)
        {
            if (loadedFile.IsOpen() && secondsSinceLastReadAttempt > 0.050f && previousFileLength != loadedFile.GetLen())
            {
                loadedFile.Seek((long)previousFileLength);
                previousFileLength = loadedFile.GetLen();

                secondsSinceLastReadAttempt = 0;

                var lines = new List<string>();
                string line;
                while ((line = loadedFile.GetLine()) != "")
                {
                    lines.Add(line);
                }
                if (lines.Count > 0)
                {
                    // Check if we should enter streaming mode
                    bool wasAtEnd = scrubber >= 1.0f;

                    SubtractFrameTime(ref scrubber, Frames.Count);
                    ParseSegment(lines.ToArray(), ref Frames, ColumnHeaders); // Starting at line 0 for streaming updates

                    // Enable streaming mode if we were at the end when new data arrived
                    if (wasAtEnd)
                    {
                        isStreaming = true;
                    }
                }
            }
        }

        if (IsPlaying && !isSliding)
        {
            if (isStreaming)
            {
                // Calculate the current buffer size
                float currentBuffer = Frames.Count - (scrubber * Frames.Count);

                // Update the playback speed using the PID controller
                float speedAdjustment = -1.0f * pidController.Update(targetBuffer, currentBuffer, delta);
                playbackSpeed = 1.0f + speedAdjustment;
                playbackSpeed = Mathf.Clamp(playbackSpeed, 0.25f, 4.0f); // Clamp speed to reasonable values
                                                                         //GD.Print($"Playback speed: {playbackSpeed}, Current buffer: {currentBuffer}, Target buffer: {targetBuffer}");
            }
            else
            {
                playbackSpeed = 1.0f; // Normal playback speed
            }

            scrubber += (delta / (Frames.Count / SpeedMultipliers[SpeedIndex])) * playbackSpeed;
            if (scrubber > 1.0f)
            {
                scrubber = isLooping ? 0 : 1;
            }

            SliderScrubber.Value = scrubber * 100;
            Update();
        }

        // Disable streaming mode if user interacts with the scrubber
        if (isSliding)
        {
            isStreaming = false;
        }

        if (isStreaming)
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


    private List<List<Grid>> ParseSCC(string scc)
    {
        const string startTag = "start_block";
        var blocks = new List<List<Grid>>();
        var rows = scc.Split("\n");

        if (!System.Text.RegularExpressions.Regex.IsMatch(rows.First(), $"version {CurrentVersion}"))
        {
            GD.PrintErr("Error: Unsupported version or outdated replay file.");
            return blocks; // Return an empty list
        }

        var columnHeaders = rows[1].Split(",").ToList();
        var expectedColumns = new List<string> { "kind", "name", "owner", "faction", "factionColor", "entityId", "health", "position", "rotation", "gridSize" };

        if (!expectedColumns.All(columnHeaders.Contains))
        {
            var missingColumns = expectedColumns.Except(columnHeaders).ToList();
            var extraColumns = columnHeaders.Except(expectedColumns).ToList();
            GD.PrintErr("Error: The replay file does not contain the expected columns.");
            GD.PrintErr($"Expected columns: {string.Join(", ", expectedColumns)}");
            GD.PrintErr($"Actual columns: {string.Join(", ", columnHeaders)}");
            if (missingColumns.Count > 0)
            {
                GD.PrintErr($"Missing columns: {string.Join(", ", missingColumns)}");
            }
            if (extraColumns.Count > 0)
            {
                GD.PrintErr($"Unexpected columns: {string.Join(", ", extraColumns)}");
            }
            return blocks; // Return an empty list
        }

        // Split the input into segments
        var segment = new List<string>();

        // Account for skipped header lines
        const int headerLineCount = 2;
        LineNumber += headerLineCount;
        foreach (var row in rows.Skip(headerLineCount))
        {
            if (row.StartsWith(startTag))
            {
                if (segment.Count > 0)
                {
                    ParseSegment(segment.ToArray(), ref blocks, columnHeaders);
                    segment.Clear();
                }
            }
            segment.Add(row);
        }
        

        // Parse the last segment if any
        if (segment.Count > 0)
        {
            ParseSegment(segment.ToArray(), ref blocks, columnHeaders);
        }

        return blocks;
    }

    private void ParseSegment(string[] segment, ref List<List<Grid>> blocks, List<string> columnHeaders)
    {
        const string gridTag = "grid";
        const string volumeTag = "volume";

        foreach (var row in segment)
        {
            var cols = row.Split(",");
            var entryKind = cols[0];
            switch (entryKind)
            {
                case "start_block":
                    blocks.Add(new List<Grid>());
                    break;

                case gridTag:
                    if (blocks.Count <= 0)
                    {
                        GD.PrintErr($"Error: Expected start_block before first grid entry at line {LineNumber}.");
                        return;
                    }

                    if (cols.Length != columnHeaders.Count)
                    {
                        GD.PrintErr($"Error: Expected {columnHeaders.Count} columns for tag 'grid', but got {cols.Length} at line {LineNumber}.");
                        break;
                    }

                    try
                    {
                        var grid = new Grid();

                        // Utility function to get column value by header name
                        string GetColumnValue(string header)
                        {
                            int index = columnHeaders.IndexOf(header);
                            if (index == -1 || index >= cols.Length)
                                throw new Exception($"'{header}' column not found or out of bounds.");
                            return cols[index];
                        }

                        // Utility function to parse a float array from a space-separated string
                        float[] ParseFloatArray(string input, int expectedLength, string columnName)
                        {
                            var parts = input.Split(' ');
                            if (parts.Length != expectedLength)
                                throw new Exception($"Column '{columnName}' expected {expectedLength} components but got {parts.Length}.");
                            return Array.ConvertAll(parts, float.Parse);
                        }

                        // Helper function to create Vector3
                        Vector3 ToVector3(float[] array, string columnName)
                        {
                            if (array.Length != 3)
                                throw new Exception($"Column '{columnName}' array length {array.Length} does not match Vector3 requirements.");
                            return new Vector3(array[0], array[1], array[2]);
                        }

                        // Helper function to create Quat
                        Quat ToQuat(float[] array, string columnName)
                        {
                            if (array.Length != 4)
                                throw new Exception($"Column '{columnName}' array length {array.Length} does not match Quat requirements.");
                            return new Quat(array[0], array[1], array[2], array[3]);
                        }

                        // Parse values
                        grid.Position = ToVector3(ParseFloatArray(GetColumnValue("position"), 3, "position"), "position");
                        grid.Name = GetColumnValue("name");
                        grid.EntityId = GetColumnValue("entityId");
                        grid.Orientation = ToQuat(ParseFloatArray(GetColumnValue("rotation"), 4, "rotation"), "rotation");
                        grid.Faction = GetColumnValue("faction");
                        grid.FactionColor = ToVector3(ParseFloatArray(GetColumnValue("factionColor"), 3, "factionColor"), "factionColor");
                        grid.GridSize = GetColumnValue("gridSize");

                        blocks[blocks.Count - 1].Add(grid);
                    }
                    catch (Exception ex)
                    {
                        GD.PrintErr($"Error processing grid entry at line {LineNumber}: {ex.Message}");
                    }
                    break;


                case volumeTag:
                    var volume = new Volume(row, LineNumber);
                    if (!volume.Ok)
                    {
                        break;
                    }

                    if (GridVolumes.ContainsKey(volume.EntityId))
                    {
                        GD.Print($"Volume: already have volume with this EntityId {volume.EntityId}");
                        break;
                    }

                    var gridList = blocks.LastOrDefault();
                    var gridSizeString = gridList?.FirstOrDefault(g => g.EntityId == volume.EntityId)?.GridSize;
                    if (gridSizeString == null)
                    {
                        GD.PrintErr($"Grid size for volume with entity ID {volume.EntityId} not found at line {LineNumber}.");
                        break;
                    }
                    volume.GridSize = gridSizeString == "Small" ? 0.5f : 2.5f;
                    volume.VisualNode = ConstructVoxelGrid(volume, gridSizeString);
                    GridVolumes[volume.EntityId] = volume;
                    break;
            }
            LineNumber++;
        }
    }

    public class Grid
    {
        public string Name = "";
        public string EntityId = "";
        public string Faction = "";
        public Vector3 FactionColor;
        public Vector3 Position;
        public Quat Orientation;
        public string GridSize; // Add this property
    }

    public Vector3 Lerp(Vector3 a, Vector3 b, float t)
    {
        return a + (b - a) * t;
    }

    public MultiMeshInstance ConstructVoxelGrid(Volume volume, string gridSize)
    {
        if (!volume.Ok)
        {
            GD.PrintErr($"ConstructVoxelGrid: Volume with EntityId {volume.EntityId} is not OK.");
            return new MultiMeshInstance();
        }

        MultiMesh multiMesh = new MultiMesh();
        multiMesh.TransformFormat = MultiMesh.TransformFormatEnum.Transform3d;

        //Vector3 gridSizeVector = gridSize == "Small" ? new Vector3(0.5f, 0.5f, 0.5f) : new Vector3(2.5f, 2.5f, 2.5f);
        Vector3 gridOffset = new Vector3(volume.Width, volume.Height, volume.Depth) * -0.5f * volume.GridSize;

        var buffer = new List<Transform>();

        bool IsBlockPresent(int x, int y, int z)
        {
            if (x < 0 || x >= volume.Width || y < 0 || y >= volume.Height || z < 0 || z >= volume.Depth)
                return false;

            int byteIndex = z * volume.Width * volume.Height + y * volume.Width + x;
            int bytePosition = byteIndex % 8;
            return (volume.BinaryVolume[byteIndex / 8] & (1 << (7 - bytePosition))) != 0;
        }

        for (int z = 0; z < volume.Depth; z++)
        {
            for (int y = 0; y < volume.Height; y++)
            {
                for (int x = 0; x < volume.Width; x++)
                {
                    if (!IsBlockPresent(x, y, z))
                        continue;

                    bool isSurrounded = IsBlockPresent(x - 1, y, z) && IsBlockPresent(x + 1, y, z)
                        && IsBlockPresent(x, y - 1, z) && IsBlockPresent(x, y + 1, z)
                        && IsBlockPresent(x, y, z - 1) && IsBlockPresent(x, y, z + 1);

                    if (isSurrounded)
                        continue;

                    var transform = new Transform(Basis.Identity, new Vector3(x, y, z) * volume.GridSize + gridOffset);
                    buffer.Add(transform);
                }
            }
        }

        multiMesh.InstanceCount = buffer.Count;

        for (var i = 0; i < buffer.Count; ++i)
        {
            multiMesh.SetInstanceTransform(i, buffer[i]);
        }

        var instance = new MultiMeshInstance();
        MeshInstance cube = CubePrefab.Instance() as MeshInstance;
        if (cube == null)
        {
            GD.PrintErr("CubePrefab instance is null. Please check the CubePrefab assignment.");
            return instance;
        }

        var cubeMesh = new CubeMesh
        {
            Size = new Vector3(volume.GridSize, volume.GridSize, volume.GridSize) * VoxelSizeMultiplier
        };
        multiMesh.Mesh = cubeMesh;

        instance.Multimesh = multiMesh;
        instance.MaterialOverride = MarkerMaterialBase;

        return instance;
    }


    private static byte[] Decompress(byte[] data)
    {
        List<byte> decompressedData = new List<byte>();

        if (data.Length % 2 != 0)
        {
            GD.PrintErr("Decompress: Data length is odd, this might indicate an issue with the input data.");
        }

        for (int i = 0; i < data.Length - 1; i += 2)
        {
            byte b = data[i];
            byte count = data[i + 1];

            for (int j = 0; j < count; j++)
            {
                decompressedData.Add(b);
            }
        }

        return decompressedData.ToArray();
    }

    public class Volume
    {
        public string EntityId { get; private set; }
        public int Width { get; private set; }
        public int Height { get; private set; }
        public int Depth { get; private set; }
        public string Base64String { get; private set; }
        public long FileLineNumber { get; private set; }
        public bool Ok { get; private set; }
        public byte[] BinaryVolume { get; private set; }

        public float GridSize = 2.5f;

        public MultiMeshInstance VisualNode;

        public Volume(string volumeEntry, long lineNumber)
        {
            FileLineNumber = lineNumber;
            Ok = false;

            var cols = volumeEntry.Split(",");
            if (cols.Length != 3)
            {
                GD.PrintErr($"Expected three columns for tag 'volume', but got {cols.Length} at line {lineNumber}.");
                return;
            }

            EntityId = cols[1];
            Base64String = cols[2];

            byte[] compressedData;
            try
            {
                compressedData = Convert.FromBase64String(Base64String);
            }
            catch (FormatException ex)
            {
                GD.PrintErr($"Volume: Failed to decode Base64 string at line {lineNumber}. Exception: {ex.Message}");
                return;
            }

            byte[] decompressedData = Decompress(compressedData);
            if (decompressedData == null)
            {
                GD.PrintErr($"Volume: Failed to decompress data at line {lineNumber}.");
                return;
            }

            if (decompressedData.Length < sizeof(int) * 3)
            {
                GD.PrintErr($"Volume: Decompressed data is too short at line {lineNumber}.");
                return;
            }

            Width = BitConverter.ToInt32(decompressedData, 0);
            Height = BitConverter.ToInt32(decompressedData, sizeof(int));
            Depth = BitConverter.ToInt32(decompressedData, sizeof(int) * 2);

            const int headerSize = sizeof(int) * 3;
            BinaryVolume = new byte[decompressedData.Length - headerSize];
            Array.Copy(decompressedData, headerSize, BinaryVolume, 0, BinaryVolume.Length);

            int expectedLength = (Width * Height * Depth + 7) / 8;
            if (BinaryVolume.Length != expectedLength)
            {
                GD.PrintErr($"Volume: Expected {expectedLength} bytes for BinaryVolume, but got {BinaryVolume.Length} at line {lineNumber}.");
                return;
            }

            Ok = true;
        }

    }

    public Marker MarkerFromGrid(Grid grid)
    {
        return Markers.TryGetValue(grid.EntityId, out var fromGrid) ? fromGrid : null;
    }
}
