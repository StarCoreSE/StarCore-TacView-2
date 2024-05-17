using System;
using System.Collections.Generic;
using System.IO;
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

    public bool isLooping;
    public bool isSliding;
    private float scrubber;

    private Dictionary<string, Marker> Markers = new Dictionary<string, Marker>();
    [Export] public PackedScene MarkerBlueprint;
    [Export] public string AutoloadSCCPath;
    private Dictionary<string, MultiMeshInstance> GridVolumes = new Dictionary<string, MultiMeshInstance>();

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
            loadedFile.Open(file, File.ModeFlags.Read);
            var content = loadedFile.GetAsText();
            previousFileLength = loadedFile.GetLen();
            LineNumber = content.Count(c => c == '\n');

            FactionColors.Clear();
            foreach (var volume in GridVolumes)
            {
                volume.Value.QueueFree();
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
    }

    public string SecondsToTime(float e)
    {
        string h = Math.Floor(e / 3600).ToString().PadLeft(2, '0'),
            m = Math.Floor(e % 3600 / 60).ToString().PadLeft(2, '0'),
            s = Math.Floor(e % 60).ToString().PadLeft(2, '0');

        return h + ':' + m + ':' + s;
    }

    public void Update()
    {
        if (Frames.Count == 0)
        {
            GD.PrintErr("Error: No frames to update.");
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

        var currentFrame = Frames[currentIndex];
        var lastFrame = Frames[lastIndex];

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
                    marker.SetMultiMesh(volume.Multimesh);
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
        foreach (var row in rows.Skip(2))
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

        int currentLineNumber = LineNumber;
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
                        GD.PrintErr($"Error: Expected start_block before first grid entry at line {currentLineNumber}.");
                        return;
                    }

                    if (cols.Length != columnHeaders.Count)
                    {
                        GD.PrintErr($"Error: Expected {columnHeaders.Count} columns for tag 'grid', but got {cols.Length} at line {currentLineNumber}.");
                        break;
                    }

                    var grid = new Grid();
                    var stringParts = cols[columnHeaders.IndexOf("position")].Split(' ');
                    var p = Array.ConvertAll(stringParts, float.Parse);
                    grid.Position = new Vector3(p[0], p[1], p[2]);
                    grid.Name = cols[columnHeaders.IndexOf("name")];
                    grid.EntityId = cols[columnHeaders.IndexOf("entityId")];
                    var qParts = cols[columnHeaders.IndexOf("rotation")].Split(' ');
                    var q = Array.ConvertAll(qParts, float.Parse);
                    grid.Orientation = new Quat(q[0], q[1], q[2], q[3]);
                    grid.Faction = cols[columnHeaders.IndexOf("faction")];

                    var fcparts = cols[columnHeaders.IndexOf("factionColor")].Split(' ');
                    var fc = Array.ConvertAll(fcparts, float.Parse);
                    grid.FactionColor = new Vector3(fc[0], fc[1], fc[2]);

                    grid.GridSize = cols[columnHeaders.IndexOf("gridSize")]; // Read the grid size

                    blocks[blocks.Count - 1].Add(grid);
                    break;

                case volumeTag:
                    if (cols.Length != 3)
                    {
                        GD.PrintErr($"Expected three columns for tag 'volume', but got {cols.Length} at line {currentLineNumber}.");
                        break;
                    }

                    string entityId = cols[1];
                    string volume = cols[2];

                    var volumeGridSize = blocks.Last().FirstOrDefault(g => g.EntityId == entityId)?.GridSize;
                    if (volumeGridSize == null)
                    {
                        GD.PrintErr($"Grid size for volume with entity ID {entityId} not found at line {currentLineNumber}.");
                        break;
                    }

                    if (GridVolumes.ContainsKey(entityId))
                    {
                        GridVolumes[entityId] = ConstructVoxelGrid(volume, volumeGridSize);
                    }
                    else
                    {
                        GridVolumes.Add(entityId, ConstructVoxelGrid(volume, volumeGridSize));
                    }
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

    public MultiMeshInstance ConstructVoxelGrid(string base64BinaryVolume, string gridSize)
    {
        MultiMesh multiMesh = new MultiMesh();
        multiMesh.TransformFormat = MultiMesh.TransformFormatEnum.Transform3d;

        Vector3 gridSizeVector = gridSize == "Small" ? new Vector3(0.5f, 0.5f, 0.5f) : new Vector3(2.5f, 2.5f, 2.5f);
        Vector3 gridOffset = Vector3.Zero;

        byte[] compressedData;
        try
        {
            compressedData = Convert.FromBase64String(base64BinaryVolume);
        }
        catch (FormatException ex)
        {
            GD.PrintErr($"ConstructVoxelGrid: Failed to decode Base64 string. Exception: {ex.Message}");
            return null; // Or handle the error as appropriate for your application
        }

        byte[] decompressedData = Decompress(compressedData);
        if (decompressedData == null)
        {
            GD.PrintErr("ConstructVoxelGrid: Failed to decompress data.");
            return null; // Or handle the error as appropriate for your application
        }

        int headerSize = sizeof(int) * 3;
        int width = BitConverter.ToInt32(decompressedData, 0);
        int height = BitConverter.ToInt32(decompressedData, sizeof(int));
        int depth = BitConverter.ToInt32(decompressedData, sizeof(int) * 2);

        byte[] binaryVolume = new byte[decompressedData.Length - headerSize];
        Array.Copy(decompressedData, headerSize, binaryVolume, 0, binaryVolume.Length);

        gridOffset = new Vector3(width, height, depth) * -0.5f * gridSizeVector;
        var buffer = new List<Transform>();

        bool IsBlockPresent(int x, int y, int z)
        {
            if (x < 0 || x >= width || y < 0 || y >= height || z < 0 || z >= depth)
                return false;

            int byteIndex = z * width * height + y * width + x;
            int bytePosition = byteIndex % 8;
            return (binaryVolume[byteIndex / 8] & (1 << (7 - bytePosition))) != 0;
        }

        for (int z = 0; z < depth; z++)
        {
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    if (!IsBlockPresent(x, y, z))
                        continue;

                    bool isSurrounded = IsBlockPresent(x - 1, y, z) && IsBlockPresent(x + 1, y, z)
                        && IsBlockPresent(x, y - 1, z) && IsBlockPresent(x, y + 1, z)
                        && IsBlockPresent(x, y, z - 1) && IsBlockPresent(x, y, z + 1);

                    if (isSurrounded)
                        continue;

                    var transform = new Transform(Basis.Identity, new Vector3(x, y, z) * gridSizeVector + gridOffset);
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

        var cubeMesh = new CubeMesh();
        cubeMesh.Size = gridSizeVector * VoxelSizeMultiplier;
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
}
