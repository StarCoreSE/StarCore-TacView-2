using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Godot;

public class FileProcessor
{
    private readonly string filename;
    private long lastPosition;

    public FileProcessor(string filename)
    {
        this.filename = filename;
        lastPosition = 0;
    }

    public async Task<List<Segment>> GetSegmentsAsync()
    {
        var segments = new List<Segment>();

        try
        {
            // If it's the first time reading, read the entire file
            if (lastPosition == 0)
            {
                await ReadFromFileAsync(segments, 0, true);
            }
            else
            {
                // Read only new segments since last position
                await ReadFromFileAsync(segments, lastPosition, false);
            }
        }
        catch (Exception e)
        {
            GD.PrintErr($"Error reading file: {e.Message}");
        }

        return segments;
    }

    private async Task ReadFromFileAsync(List<Segment> segments, long startPosition, bool skipHeader)
    {
        using (var stream = new FileStream(filename, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        {
            stream.Seek(startPosition, SeekOrigin.Begin);
            using (var reader = new StreamReader(stream))
            {
                string line;
                if (skipHeader)
                {
                    // Read and validate the header lines
                    var expectedHeaders = new List<string>
                    {
                        "version 2",
                        "kind,name,owner,faction,factionColor,entityId,health,position,rotation"
                    };

                    for (int i = 0; i < expectedHeaders.Count; i++)
                    {
                        line = await reader.ReadLineAsync();
                        //if (line != expectedHeaders[i])
                        //{
                        //    GD.PrintErr($"Error: expected '{expectedHeaders[i]}', got '{line}'");
                        //}
                    }
                }

                Segment segment = null;
                while ((line = await reader.ReadLineAsync()) != null)
                {
                    if (line.StartsWith("start_block"))
                    {
                        if (segment != null)
                        {
                            segments.Add(segment);
                        }
                        segment = new Segment();
                    }
                    segment?.Entries.Add(ProcessLine(line));
                }
                if (segment != null)
                {
                    segments.Add(segment);
                }
                lastPosition = stream.Position;
            }
        }
    }

    private string ProcessLine(string line)
    {
        // Implement your line processing logic here
        return line;
    }
}

public class Segment
{
    public List<string> Entries { get; } = new List<string>();

    // Additional properties and methods for segment processing can be added here
}
