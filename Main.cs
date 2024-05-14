using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
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

    private Dictionary<string, Spatial> Markers = new Dictionary<string, Spatial>();
    [Export] public PackedScene MarkerBlueprint;
    [Export] public string AutoloadSCCPath;
    private Dictionary<string, Spatial> GridVolumes = new Dictionary<string, Spatial>();

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
        1.0f,
        0.5f,
    };

    [Export] public NodePath TimeLabelPath;
    public Label TimeLabel;

    public Dictionary<string, SpatialMaterial> FactionColors = new Dictionary<string, SpatialMaterial>();


    [Export] public PackedScene CubePrefab; // Prefab for the cube mesh

    [Export] public Color NeutralColor;

    public override void _Ready()
    {
        GetTree().Connect("files_dropped", this, nameof(GetDroppedFilesPath));
        if (AutoloadSCCPath != null)
        {
            File file = new File();
            Error error = file.Open(AutoloadSCCPath, File.ModeFlags.Read);

            if (error != Error.Ok)
            {
                GD.Print("Error loading file: " + AutoloadSCCPath);
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
        if (SliderScrubber == null) return;
        SliderScrubber.Connect("drag_started", this, nameof(OnSliderDragStarted));
        SliderScrubber.Connect("drag_ended", this, nameof(OnSliderDragEnded));
        SliderScrubber.Connect("value_changed", this, nameof(OnSliderValueChanged));

        PlayButton = GetNode(PlayButtonPath) as Button;
        if (PlayButton == null) return;
        PlayButton.Connect("pressed", this, nameof(OnPlayButtonPressed));
        PlayButton.Icon = IsPlaying ? IconPause : IconPlay;

        SpeedDropdown = GetNode(SpeedDropdownPath) as OptionButton;
        if (SpeedDropdown == null) return;

        for (var i = 0; i < SpeedStrings.Length; i++)
        {
            SpeedDropdown.AddItem(SpeedStrings[i]);
        }

        // start at Realtime speed
        SpeedDropdown.Selected = 2;
        SpeedDropdown.Connect("item_selected", this, nameof(OnSpeedDropdownItemSelected));

        TimeLabel = GetNode(TimeLabelPath) as Label;
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
            return;
        }

        var file = files[0];
        if (file.EndsWith(".scc"))
        {
            loadedFile.Open(file, File.ModeFlags.Read);
            var content = loadedFile.GetAsText();

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
                GD.Print("Failed to load SCC " + file);
            }
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
        TimeLabel.Text = SecondsToTime((float)Math.Floor(scrubber * (Frames.Count))) + "/" +
                         SecondsToTime((float)Frames.Count);
        if (Frames.Count == 0)
        {
            return;
        }

        var proportion = 1.0 / (Frames.Count - 1);
        var remapped = scrubber / proportion;
        var currentIndex = (int)remapped;
        var nextIndex = currentIndex + 1;

        if (nextIndex == Frames.Count)
        {
            nextIndex = currentIndex;
        }

        var currentFrame = Frames[currentIndex];
        var nextFrame = Frames[nextIndex];

        foreach (var marker in GetNode<Spatial>("Markers").GetChildren())
        {
            if (marker is Spatial spatial)
            {
                spatial.Visible = false;
            }
        }

        foreach (Node child in GetNode("LineContainer").GetChildren())
        {
            child.QueueFree();
        }

        foreach (var grid in currentFrame)
        {
            Spatial marker;
            if (Markers.TryGetValue(grid.EntityId, out marker))
            {
                marker.Visible = true;
            }
            else
            {
                marker = MarkerBlueprint.Instance() as Spatial;
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

                if (GridVolumes.ContainsKey(grid.EntityId))
                {
                    Spatial volume;
                    if (GridVolumes.TryGetValue(grid.EntityId, out volume))
                    {
                        volume.Name = "Cube";
                        var currentVisual = marker.GetNode<MeshInstance>("Cube");
                        if (currentVisual != volume)
                        {
                            marker.RemoveChild(currentVisual);
                            marker.AddChild(volume);
                        }
                    }
                }

                var cubeNode = marker.GetNode("Cube");
                switch (cubeNode)
                {
                    case MeshInstance _:
                        marker.GetNode<MeshInstance>("Cube").MaterialOverride = factionColor;
                        break;
                    case Spatial _:
                        cubeNode.GetChild<MultiMeshInstance>(0).MaterialOverlay = factionColor;
                        break;
                }

                GetNode<Spatial>("Markers").AddChild(marker);
            }

            if (marker == null) continue;

            // update the color for this faction if the grid's color doesn't match, and it's a unique color
            if (grid.Faction != "Unowned" && FactionColors.ContainsKey(grid.Faction) && FactionColors[grid.Faction] != MarkerMaterialBase)
            {
                FactionColors[grid.Faction].AlbedoColor = Color.FromHsv(grid.FactionColor.x, 0.95f, 0.2f);
            }

            var next = nextFrame.Find(e => e.EntityId == grid.EntityId);
            var t = 0.0;
            var nextPosition = Vector3.Zero;
            var nextOrientation = Quat.Identity;
            if (next != null)
            {
                nextPosition = next.Position;
                nextOrientation = next.Orientation;
                t = remapped - (int)remapped;
            }

            var position = Lerp(grid.Position, nextPosition, (float)t);
            marker.Translation = position;

            float radius = 3;
            var cylinder = new CylinderMesh
            {
                RadialSegments = 6,
                BottomRadius = radius,
                TopRadius = radius,
            };

            cylinder.Height = Math.Abs(position.y);
            var cylinderInstance = new MeshInstance();
            cylinderInstance.Mesh = cylinder;
            cylinderInstance.Translation = new Vector3(position.x, position.y / 2, position.z);
            cylinderInstance.MaterialOverride = LineMaterial;
            GetNode("LineContainer").AddChild(cylinderInstance);

            var partial = grid.Orientation.Normalized().Slerp(nextOrientation.Normalized(), (float)t);
            marker.Rotation = partial.GetEuler();
        }
    }

    public override void _Process(float delta)
    {
        if (IsPlaying && !isSliding)
        {
            scrubber += delta / (Frames.Count / SpeedMultipliers[SpeedIndex]);
            if (scrubber > 1.0)
            {
                scrubber = isLooping ? 0 : 1;
            }

            SliderScrubber.Value = scrubber * 100;
            Update();
        }
    }

    private List<List<Grid>> ParseSCC(string scc)
    {
        const string startTag = "start_block";
        const string gridTag = "grid";
        const string volumeTag = "volume";
        Console.WriteLine("scc length " + scc.Length);
        var blocks = new List<List<Grid>>();
        var rows = scc.Split("\n");

        if (!System.Text.RegularExpressions.Regex.IsMatch(rows.First(), $"version {CurrentVersion}"))
        {
            {
                return blocks;
            }
        }

        var columnHeaders = rows[1].Split(",").ToList();
        foreach (var row in rows)
        {
            var cols = row.Split(",");
            var entryKind = cols[0];
            switch (entryKind)
            {
                case startTag:
                    blocks.Add(new List<Grid>());
                    break;

                case gridTag:
                    if (blocks.Count <= 0)
                    {
                        Console.WriteLine("error: expected start_block before first grid entry");
                        return blocks;
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

                    blocks[blocks.Count - 1].Add(grid);
                    break;
                case volumeTag:
                    if (cols.Length != 3)
                    {
                        GD.Print($"Expected three columns for tag 'volume', but got {cols.Length}");
                        break;
                    }
                    string entityId = cols[1];
                    string volume = cols[2];
                    GridVolumes.Add(entityId, ConstructVoxelGrid(volume));
                    break;
            }
        }

        return blocks;
    }

    public class Grid
    {
        public string Name = "";
        public string EntityId = "";
        public string Faction = "";
        public Vector3 FactionColor;
        public Vector3 Position;
        public Quat Orientation;
    }

    public Vector3 Lerp(Vector3 a, Vector3 b, float t)
    {
        return a + (b - a) * t;
    }

    public Spatial ConstructVoxelGrid(string base64BinaryVolume)
    {
        MultiMesh multiMesh = new MultiMesh();
        multiMesh.TransformFormat = MultiMesh.TransformFormatEnum.Transform3d;
        var result = new Spatial();

        Vector3 GridSize = new Vector3(2.5f, 2.5f, 2.5f);
        Vector3 GridOffset = Vector3.Zero;
        byte[] compressedData = Convert.FromBase64String(base64BinaryVolume);
        byte[] decompressedData = Decompress(compressedData);

        int headerSize = sizeof(int) * 3;
        int width = BitConverter.ToInt32(decompressedData, 0);
        int height = BitConverter.ToInt32(decompressedData, sizeof(int));
        int depth = BitConverter.ToInt32(decompressedData, sizeof(int) * 2);
        GD.Print($"Volume Header: {width}, {height}, {depth}");

        byte[] binaryVolume = new byte[decompressedData.Length - headerSize];
        Array.Copy(decompressedData, headerSize, binaryVolume, 0, binaryVolume.Length);

        GridOffset = new Vector3(width, height, depth) * -0.5f * GridSize;
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

                    var transform = new Transform(Basis.Identity, new Vector3(x, y, z) * GridSize + GridOffset);
                    buffer.Add(transform);
                }
            }
        }

        multiMesh.InstanceCount = buffer.Count;

        for (var i = 0; i < buffer.Count; ++i)
        {
            multiMesh.SetInstanceTransform(i, buffer[i]);
        }

        MeshInstance cube = CubePrefab?.Instance() as MeshInstance;
        if (cube == null)
        {
            GD.PrintErr("CubePrefab instance is null. Please check the CubePrefab assignment.");
            return result;
        }

        multiMesh.Mesh = cube.Mesh;
        var instance = new MultiMeshInstance();
        instance.Multimesh = multiMesh;
        instance.MaterialOverlay = MarkerMaterialBase;

        result.AddChild(instance);
        return result;
    }

    private static byte[] Decompress(byte[] data)
    {
        List<byte> decompressedData = new List<byte>();

        for (int i = 0; i < data.Length; i += 2)
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
