using Godot;
using System;

public class RingMeshGenerator : MeshInstance
{
    [Export]
    public float Radius = 1.0f;
    [Export]
    public float Thickness = 0.1f;
    [Export]
    public int Segments = 32;
    [Export]
    public float FixedSize = 0.1f; // The apparent fixed size of the thickness

    private Camera _camera;

    private float _previousRadius;
    private float _previousThickness;
    private int _previousSegments;

    public override void _Ready()
    {
        base._Ready();
        _camera = GetViewport().GetCamera();
        GenerateRingMesh();
        StorePreviousValues();
    }

    public override void _Process(float delta)
    {
        base._Process(delta);

        if (_camera != null)
        {
            UpdateThicknessBasedOnDistance();
        }

        if (HasPropertiesChanged())
        {
            GenerateRingMesh();
            StorePreviousValues();
        }
    }

    private void UpdateThicknessBasedOnDistance()
    {
        float distance = GlobalTransform.origin.DistanceTo(_camera.GlobalTransform.origin);
        Thickness = FixedSize * distance;
    }

    private void GenerateRingMesh()
    {
        // Validate parameters
        if (Segments < 3)
        {
            GD.PrintErr("Segments must be at least 3.");
            return;
        }

        // Create a new array mesh
        ArrayMesh ringMesh = new ArrayMesh();

        // Create arrays for vertices, normals, and uvs
        Vector3[] vertices = new Vector3[Segments * 2];
        Vector3[] normals = new Vector3[Segments * 2];
        Vector2[] uvs = new Vector2[Segments * 2];
        int[] indices = new int[Segments * 6];

        float angleStep = Mathf.Tau / Segments;

        for (int i = 0; i < Segments; i++)
        {
            float angle = i * angleStep;
            float nextAngle = (i + 1) % Segments * angleStep;

            Vector3 innerVertex = new Vector3(Mathf.Cos(angle) * Radius, 0, Mathf.Sin(angle) * Radius);
            Vector3 outerVertex = new Vector3(Mathf.Cos(angle) * (Radius + Thickness), 0, Mathf.Sin(angle) * (Radius + Thickness));
            Vector3 nextInnerVertex = new Vector3(Mathf.Cos(nextAngle) * Radius, 0, Mathf.Sin(nextAngle) * Radius);
            Vector3 nextOuterVertex = new Vector3(Mathf.Cos(nextAngle) * (Radius + Thickness), 0, Mathf.Sin(nextAngle) * (Radius + Thickness));

            // Set vertices
            vertices[i * 2] = innerVertex;
            vertices[i * 2 + 1] = outerVertex;

            // Set normals
            normals[i * 2] = Vector3.Up;
            normals[i * 2 + 1] = Vector3.Up;

            // Set UVs
            uvs[i * 2] = new Vector2(i / (float)Segments, 0);
            uvs[i * 2 + 1] = new Vector2(i / (float)Segments, 1);

            // Set indices
            int baseIndex = i * 6;
            indices[baseIndex] = i * 2;
            indices[baseIndex + 1] = (i * 2 + 2) % (Segments * 2);
            indices[baseIndex + 2] = i * 2 + 1;

            indices[baseIndex + 3] = i * 2 + 1;
            indices[baseIndex + 4] = (i * 2 + 2) % (Segments * 2);
            indices[baseIndex + 5] = (i * 2 + 3) % (Segments * 2);
        }

        // Create the surface arrays
        SurfaceTool st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        for (int i = 0; i < vertices.Length; i++)
        {
            st.AddNormal(normals[i]);
            st.AddUv(uvs[i]);
            st.AddVertex(vertices[i]);
        }
        for (int i = 0; i < indices.Length; i++)
        {
            st.AddIndex(indices[i]);
        }
        st.GenerateNormals();

        // Commit the surface to the mesh
        st.Commit(ringMesh);

        // Set the mesh to the MeshInstance
        this.Mesh = ringMesh;
    }

    private void StorePreviousValues()
    {
        _previousRadius = Radius;
        _previousThickness = Thickness;
        _previousSegments = Segments;
    }

    private bool HasPropertiesChanged()
    {
        return _previousRadius != Radius || _previousThickness != Thickness || _previousSegments != Segments;
    }
}
