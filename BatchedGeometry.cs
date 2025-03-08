using System.Drawing;
using LibTessDotNet;
using OpenTK.Mathematics;
using SVGtesselationShader.Shapes;

namespace SVGtesselationShader
{
    /// <summary>
    /// Stores pre-tessellated geometry for batch rendering
    /// </summary>
    public class BatchedGeometry
    {
        // Holds all vertex data for filled shapes (position + color)
        public List<float> FillVertices { get; private set; } = new List<float>();
        
        // Holds all vertex data for stroked paths (position + color)
        public List<float> StrokeVertices { get; private set; } = new List<float>();
        
        // Count of vertices in the fill buffer
        public int FillVertexCount => FillVertices.Count / 7;
        
        // Count of vertices in the stroke buffer
        public int StrokeVertexCount => StrokeVertices.Count / 7;

        public BatchedGeometry()
        {
        }

        /// <summary>
        /// Processes all shapes from the SVG and batches them into a single vertex list
        /// </summary>
        public void ProcessShapes(IEnumerable<IShape> shapes)
        {
            FillVertices.Clear();
            StrokeVertices.Clear();

            // Process each shape and add its vertices to the appropriate buffer
            foreach (var shape in shapes)
            {
                ProcessFilledShape(shape);
                ProcessStrokedShape(shape);
            }
        }

        private void ProcessFilledShape(IShape shape)
        {
            // Skip if no fill color or fully transparent
            if (!shape.FillColor.HasValue || shape.FillColor.Value.A == 0)
                return;

            Color fill = shape.FillColor.Value;
            float r = fill.R / 255f;
            float g = fill.G / 255f;
            float b = fill.B / 255f;
            float a = fill.A / 255f;

            // Get tessellated triangles
            var tessellatedTriangles = shape.GetTessellatedTriangles();
            if (tessellatedTriangles == null || tessellatedTriangles.Count == 0)
                return;

            // Add all triangles to the vertex buffer
            foreach (var triangle in tessellatedTriangles)
            {
                foreach (var vertex in triangle)
                {
                    // Position (x,y,z)
                    FillVertices.Add(vertex.Position.X);
                    FillVertices.Add(vertex.Position.Y);
                    FillVertices.Add(vertex.Position.Z);
                    
                    // Color (r,g,b,a)
                    FillVertices.Add(r);
                    FillVertices.Add(g);
                    FillVertices.Add(b);
                    FillVertices.Add(a);
                }
            }
        }

        private void ProcessStrokedShape(IShape shape)
        {
            // Skip if no stroke color or zero width
            if (!shape.StrokeColor.HasValue || shape.StrokeWidth <= 0 || shape.StrokeColor.Value.A == 0)
                return;

            Color stroke = shape.StrokeColor.Value;
            float r = stroke.R / 255f;
            float g = stroke.G / 255f;
            float b = stroke.B / 255f;
            float a = stroke.A / 255f;
            float width = shape.StrokeWidth;

            // Get stroke lines
            var lines = shape.GetStrokeSegments();
            if (lines == null || lines.Count == 0)
                return;

            // Add all line segments to the stroke buffer
            foreach (var line in lines)
            {
                // Start vertex of the line
                StrokeVertices.Add(line.Start.X);
                StrokeVertices.Add(line.Start.Y);
                StrokeVertices.Add(0); // Z
                StrokeVertices.Add(r);
                StrokeVertices.Add(g);
                StrokeVertices.Add(b);
                StrokeVertices.Add(a);
                
                // End vertex of the line
                StrokeVertices.Add(line.End.X);
                StrokeVertices.Add(line.End.Y);
                StrokeVertices.Add(0); // Z
                StrokeVertices.Add(r);
                StrokeVertices.Add(g);
                StrokeVertices.Add(b);
                StrokeVertices.Add(a);
            }
        }
    }
} 