using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using File = Godot.File;
using Vector3 = Godot.Vector3;

public class Main : Node
{
    public int CurrentVersion = 2;

    private List<List<Grid>> Frames = new List<List<Grid>>();

    private File loadedFile = new File();

    private PlaybackWidget _playbackWidget;

    private int _currentFrame = 0;
    public int CurrentFrame
    {
        get
        {
            _currentFrame = (_currentFrame >= 0 && _currentFrame < Frames.Count)  ? _currentFrame : 0;
            return _currentFrame;
        }
        set
        {
            if (value != _currentFrame)
            {
                OnFrameChanged();
            }
            _currentFrame = value;
        }
    }


    private float scrubber;

    [Export] public PackedScene MarkerBlueprint;
    private Dictionary<string, Marker> Markers = new Dictionary<string, Marker>();
    private Dictionary<string, Volume> GridVolumes = new Dictionary<string, Volume>();

    [Export] public SpatialMaterial MarkerMaterialBase;

    private InfoWindow _infoWindow;

    public Dictionary<string, SpatialMaterial> FactionColors = new Dictionary<string, SpatialMaterial>();

    [Export] public Color NeutralColor;
    [Export] public float VoxelSizeMultiplier = 1.0f;

    public override void _Ready()
    {
        GetTree().Connect("files_dropped", this, nameof(GetDroppedFilesPath));

        _playbackWidget = GetNode<PlaybackWidget>("%PlaybackWidget");

        _infoWindow = GetNode<InfoWindow>("%InfoWindow");
        if (_infoWindow == null)
        {
            GD.PrintErr("Error: InfoWindow not found.");
            return;
        }
        
        var optionsMenu  = GetNode("%Options");
        if (optionsMenu != null)
        {
            GD.Print("Main.cs found Options GUI node");
        }
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
                _playbackWidget.SetRecording(Frames.Count, f => scrubber = f);
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

    public void OnFrameChanged()
    {
        _infoWindow.Refresh(ref Frames, CurrentFrame);
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

    public void Refresh()
    {
        if (Frames.Count == 0)
        {
            return;
        }

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
            var markersContainer = GetNode<Spatial>("%Markers");
            foreach (Marker marker in markersContainer.GetChildren())
            {
                marker.Visible = false;
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

                GetNode<Spatial>("%Markers").AddChild(marker);

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

    public override void _Process(float delta)
    {
        secondsSinceLastReadAttempt += delta;
        if (!_playbackWidget.IsSliding)
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
                    SubtractFrameTime(ref scrubber, Frames.Count);
                    ParseSegment(lines.ToArray(), ref Frames, ColumnHeaders); // Starting at line 0 for streaming updates
                }
            }
        }

        if (_playbackWidget.IsPlaying || _playbackWidget.IsSliding)
        {
            Refresh();
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
