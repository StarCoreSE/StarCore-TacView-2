using Godot;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using Vector3 = Godot.Vector3;

public class Main : Spatial
{
	public int CurrentVersion = 1;

	private List<List<Grid>> Frames = new List<List<Grid>>();

	private File loadedFile = new File();

	public bool isPlaying;
	public bool isLooping;
	public bool isSliding;
	private float scrubber;

	private Dictionary<string, Spatial> Markers = new Dictionary<string, Spatial>();
	[Export] public PackedScene MarkerBlueprint;
	[Export] public string AutoloadSCCPath;

    [Export] public SpatialMaterial LineMaterial;

    [Export] public NodePath SliderScrubberPath;
    public HSlider SliderScrubber;
    
    [Export] public NodePath PlayButtonPath;
    public Button PlayButton;

    [Export] public NodePath SpeedDropdownPath;
    public OptionButton SpeedDropdown;

    public string[] SpeedStrings = {
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
                isPlaying = true;
            }
        }
		SliderScrubber = GetNode(SliderScrubberPath) as HSlider;
        if (SliderScrubber == null) return;
        SliderScrubber.Connect("drag_started", this, nameof(OnSliderDragStarted));
        SliderScrubber.Connect("drag_ended", this, nameof(OnSliderDragEnded));
        SliderScrubber.Connect("value_changed", this, nameof(OnSliderValueChanged));

        PlayButton = GetNode(PlayButtonPath) as Button;
		if (PlayButton == null) return;
        PlayButton.Connect("toggled", this, nameof(OnPlayButtonToggled));

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

    public void OnPlayButtonToggled(bool toggle)
    {
        isPlaying = toggle;
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
			var result = ParseSCC(content);
            if (result.Count > 0)
            {
                Frames = result;
                isPlaying = true;
                scrubber = 0;
            }
            else
            {
                GD.Print("Failed to load SCC "+file);
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
        TimeLabel.Text = SecondsToTime((float)Math.Floor(scrubber * (Frames.Count))) + "/" + SecondsToTime((float)Frames.Count);
        //if (isPlaying)
        {
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
					GetNode<Spatial>("Markers").AddChild(marker);
                }
                if (marker == null) continue;

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
                cylinderInstance.Translation = new Vector3(position.x, position.y/2, position.z);
                cylinderInstance.MaterialOverride = LineMaterial;
                GetNode("LineContainer").AddChild(cylinderInstance);

                var partial = grid.Orientation.Normalized().Slerp(nextOrientation.Normalized(), (float)t);
                marker.Rotation = partial.GetEuler();
            }
		}
	}

    public override void _Process(float delta)
	{
		if (isPlaying && !isSliding)
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
		Console.WriteLine("scc length " + scc.Length);
		var blocks = new List<List<Grid>>();
		var rows = scc.Split("\n");

		if (!System.Text.RegularExpressions.Regex.IsMatch(rows.First(), $"version {CurrentVersion}"))
		{
			{ return blocks; }
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
					blocks[blocks.Count-1].Add(grid);
					break;
			}
		}
		return blocks;
	}
	public class Grid
	{
		public string Name = "";
		public string EntityId = "";
		public Vector3 Position;
        public Quat Orientation;
    }

    public Vector3 Lerp(Vector3 a, Vector3 b, float t)
    {
        return a + (b - a) * t;
    }
}
