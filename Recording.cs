using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using static Main;

public class Recording : Node
{
    private File _file = new File();
    private Dictionary<string, Marker> Markers = new Dictionary<string, Marker>();
    private Dictionary<string, Volume> GridVolumes = new Dictionary<string, Volume>();
    public Dictionary<string, SpatialMaterial> FactionColors = new Dictionary<string, SpatialMaterial>();
    public List<List<Grid>> Frames = new List<List<Grid>>();

    private float _scrubber;
    private int _currentFrame = 0;

    private float _secondsSinceLastReadAttempt = 0;
    private ulong _previousFileLength = 0;
    private int _lineNumber = 0;

    public List<string> ColumnHeaders = new List<string> { "kind", "name", "owner", "faction", "factionColor", "entityId", "health", "position", "rotation", "gridSize" };

    public int CurrentFrame
    {
        get
        {
            _currentFrame = (_currentFrame >= 0 && _currentFrame < Frames.Count) ? _currentFrame : 0;
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

    public void OnFrameChanged()
    {
        Info?.Refresh(ref Frames, CurrentFrame);
    }

    public Color NeutralColor { get; set; }

    public Spatial MarkerRoot;
    public PackedScene MarkerBlueprint { get; set; }
    public SpatialMaterial MarkerMaterialBase { get; set; }
    public OptionsWindow Options { get; set; }
    public InfoWindow Info;
    private LoadingDialog _loadingDialog;

    public void Deinit()
	{
        GD.Print("Deinit called");

        _file.Close();

        FactionColors.Clear();

        foreach (var volume in GridVolumes.Values)
        {
            volume.Deinit();
        }
        GridVolumes.Clear();

        foreach (var marker in Markers.Values)
        {
            marker.QueueFree();
        }
        Markers.Clear();

        Frames.Clear();
    }

    public static Task<Recording> Create(string filename, LoadingDialog loadingDialog = null)
    {
        var rec = new Recording();
        rec.LoadFile(filename, loadingDialog);
        return Task.FromResult(rec);
    }

    private void LoadFile(string filename, LoadingDialog loadingDialog = null)
    {
        _file.Open(filename, File.ModeFlags.Read);
        var content = _file.GetAsText();
        _previousFileLength = _file.GetLen();
        _lineNumber = 1;
        
        if (loadingDialog != null)
        {
            _loadingDialog = loadingDialog;
            _loadingDialog.SetTitle($"Loading {filename.Split("\\").LastOrDefault()}...");
        }

        Frames = ParseSCC(content);
    }

    public void SetScrubber(float value)
    {
        _scrubber = value;
    }

    public int ScrubberToFrameIndex(float scrubberLocal, int frameCount)
    {
        scrubberLocal = Mathf.Clamp(scrubberLocal, 0, 1);

        var proportion = 1.0 / (frameCount - 1);
        var remapped = scrubberLocal / proportion;

        var currentIndex = (int)remapped;

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
        var remapped = _scrubber / proportion;
        var currentIndex = (int)remapped;
        var lastIndex = currentIndex - 1;

        if (lastIndex < 0)
        {
            lastIndex = 0;
        }

        CurrentFrame = ScrubberToFrameIndex(_scrubber, Frames.Count);

        var currentFrame = Frames[CurrentFrame];
        var lastFrame = CurrentFrame > 0 ? Frames[CurrentFrame - 1] : currentFrame;

        try
        {
            var markersContainer = MarkerRoot;
            foreach (Marker marker in markersContainer.GetChildren())
            {
                marker.Visible = false;
                if (Options != null)
                {
                    marker.SetNameplateVisibility(Options.ShowNames);
                    marker.SetStandVisibility(Options.ShowStands);
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
            Marker marker;
            if (Markers.TryGetValue(grid.EntityId, out marker))
            {
                marker.Visible = true;
                if (FactionColors.TryGetValue(grid.EntityId, out var color))
                {
                    if (marker.Material != color)
                    {
                        marker.Material = color;
                    }
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

                MarkerRoot.AddChild(marker);

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

    public void MaybeReadNextSegmentFromFile(float delta)
    {
        _secondsSinceLastReadAttempt += delta;
        if (_file.IsOpen() && _secondsSinceLastReadAttempt > 0.050f && _previousFileLength != _file.GetLen())
        {
            _file.Seek((long)_previousFileLength);
            _previousFileLength = _file.GetLen();

            _secondsSinceLastReadAttempt = 0;

            var lines = new List<string>();
            string line;
            while ((line = _file.GetLine()) != "")
            {
                lines.Add(line);
            }
            if (lines.Count > 0)
            {
                SubtractFrameTime(ref _scrubber, Frames.Count);
                ParseSegment(lines.ToArray(), ref Frames, ColumnHeaders);
            }
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

    private List<List<Grid>> ParseSCC(string scc)
    {
        const int currentVersion = 2;
        const string startTag = "start_block";
        var blocks = new List<List<Grid>>();
        var rows = scc.Split("\n");

        if (!System.Text.RegularExpressions.Regex.IsMatch(rows.First(), $"version {currentVersion}"))
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
        _lineNumber += headerLineCount;
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

            if (_loadingDialog != null)
            {
                var progress = (float)_lineNumber / (rows.Length + 1);
                _loadingDialog.SetProgress(progress);
            }
        }


            // Parse the last segment if any
            if (segment.Count > 0)
        {
            ParseSegment(segment.ToArray(), ref blocks, columnHeaders);
        }

        _loadingDialog?.SetProgress(1);

        return blocks;
    }

    private void ParseSegment(string[] segment, ref List<List<Grid>> blocks, List<string> columnHeaders)
    {
        const string gridTag = "grid";
        const string volumeTag = "volume";
        const string blockRemovalTag = "v-";

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
                        GD.PrintErr($"Error: Expected start_block before first grid entry at line {_lineNumber}.");
                        return;
                    }

                    if (cols.Length != columnHeaders.Count)
                    {
                        GD.PrintErr($"Error: Expected {columnHeaders.Count} columns for tag 'grid', but got {cols.Length} at line {_lineNumber}.");
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
                        GD.PrintErr($"Error processing grid entry at line {_lineNumber}: {ex.Message}");
                    }
                    break;


                case volumeTag:
                    var volume = new Volume(row, _lineNumber);
                    if (!volume.Ok)
                    {
                        volume.Deinit();
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
                        GD.PrintErr($"Grid size for volume with entity ID {volume.EntityId} not found at line {_lineNumber}.");
                        break;
                    }
                    volume.GridSize = gridSizeString == "Small" ? 0.5f : 2.5f;
                    volume.VisualNode = volume.BuildMesh();
                    GridVolumes[volume.EntityId] = volume;
                    break;
                case blockRemovalTag:
                    string[] columns = { "tag", "gridId", "blockCount", "Vector3I[blockCount]" };
                    if (cols.Length != columns.Length)
                    {
                        GD.PrintErr($"BlockRemoval: expected {columns.Length} columns but got {cols.Length}");
                        break;
                    }

                    break;
            }
            _lineNumber++;
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
        public string GridSize;
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

        public Vector3 CenterOfMass;

        public float GridSize = 2.5f;

        public void Deinit()
        {
            VisualNode?.QueueFree();
        }

        public MeshInstance VisualNode;

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

        public MeshInstance BuildMesh()
        {
            if (!Ok)
            {
                GD.PrintErr($"ConstructVoxelGrid: Volume with EntityId {EntityId} is not OK.");
                return new MeshInstance();
            }

            var gridOffset = new Vector3(Width, Height, Depth) * -0.5f * GridSize - Vector3.Forward * GridSize;

            var vertices = new List<Vector3>();
            var indices = new List<int>();
            var normals = new List<Vector3>();

            bool IsBlockPresent(int x, int y, int z)
            {
                if (x < 0 || x >= Width || y < 0 || y >= Height || z < 0 || z >= Depth)
                    return false;

                int byteIndex = z * Width * Height + y * Width + x;
                int bytePosition = byteIndex % 8;
                return (BinaryVolume[byteIndex / 8] & (1 << (7 - bytePosition))) != 0;
            }

            void AddQuad(Vector3[] quadVertices, Vector3 normal)
            {
                int startIndex = vertices.Count;
                vertices.AddRange(quadVertices);
                normals.AddRange(new Vector3[] { normal, normal, normal, normal });
                indices.AddRange(new int[] { startIndex, startIndex + 1, startIndex + 2, startIndex, startIndex + 2, startIndex + 3 });
            }

            void AddFace(Vector3 basePos, Vector3[] offsets, Vector3 normal)
            {
                Vector3[] quadVertices = new Vector3[4];
                for (int i = 0; i < 4; i++)
                {
                    quadVertices[i] = basePos + offsets[i] * GridSize;
                }
                AddQuad(quadVertices, normal);
            }

            Vector3[] leftFaceOffsets = { Vector3.Forward, Vector3.Up + Vector3.Forward, Vector3.Up, Vector3.Zero };
            Vector3[] rightFaceOffsets = { Vector3.Right + Vector3.Up, Vector3.Right + Vector3.Forward + Vector3.Up, Vector3.Right + Vector3.Forward, Vector3.Right };
            Vector3[] bottomFaceOffsets = { Vector3.Right, Vector3.Forward + Vector3.Right, Vector3.Forward, Vector3.Zero };
            Vector3[] topFaceOffsets = { Vector3.Up + Vector3.Forward, Vector3.Up + Vector3.Right + Vector3.Forward, Vector3.Up + Vector3.Right, Vector3.Up };
            Vector3[] frontFaceOffsets = { Vector3.Up, Vector3.Right + Vector3.Up, Vector3.Right, Vector3.Zero };
            Vector3[] backFaceOffsets = { Vector3.Forward + Vector3.Right, Vector3.Forward + Vector3.Right + Vector3.Up, Vector3.Forward + Vector3.Up, Vector3.Forward };

            var totalPosition = new Vector3();
            int blockCount = 0;

            for (int z = 0; z < Depth; z++)
            {
                for (int y = 0; y < Height; y++)
                {
                    for (int x = 0; x < Width; x++)
                    {
                        if (!IsBlockPresent(x, y, z))
                            continue;

                        Vector3 basePos = new Vector3(x, y, z) * GridSize + gridOffset;
                        totalPosition += basePos;
                        blockCount++;

                        if (!IsBlockPresent(x - 1, y, z)) // Left face
                            AddFace(basePos, leftFaceOffsets, Vector3.Left);

                        if (!IsBlockPresent(x + 1, y, z)) // Right face
                            AddFace(basePos, rightFaceOffsets, Vector3.Right);

                        if (!IsBlockPresent(x, y - 1, z)) // Bottom face
                            AddFace(basePos, bottomFaceOffsets, Vector3.Down);

                        if (!IsBlockPresent(x, y + 1, z)) // Top face
                            AddFace(basePos, topFaceOffsets, Vector3.Up);

                        if (!IsBlockPresent(x, y, z + 1)) // Front face
                            AddFace(basePos, frontFaceOffsets, Vector3.Forward);

                        if (!IsBlockPresent(x, y, z - 1)) // Back face
                            AddFace(basePos, backFaceOffsets, Vector3.Back);
                    }
                }
            }

            if (blockCount > 0)
            {
                CenterOfMass = totalPosition / (blockCount * GridSize);
            }

            // Create the mesh
            var arrayMesh = new ArrayMesh();
            var arrays = new Godot.Collections.Array();
            arrays.Resize((int)ArrayMesh.ArrayType.Max);

            arrays[(int)ArrayMesh.ArrayType.Vertex] = vertices.ToArray();
            arrays[(int)ArrayMesh.ArrayType.Index] = indices.ToArray();
            arrays[(int)ArrayMesh.ArrayType.Normal] = normals.ToArray();

            arrayMesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);

            // Create the MeshInstance
            var meshInstance = new MeshInstance();
            meshInstance.Mesh = arrayMesh;

            return meshInstance;
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

    

    public Marker MarkerFromGrid(Grid grid)
    {
        return Markers.TryGetValue(grid.EntityId, out var fromGrid) ? fromGrid : null;
    }

    public Vector3 Lerp(Vector3 a, Vector3 b, float t)
    {
        return a + (b - a) * t;
    }
}
