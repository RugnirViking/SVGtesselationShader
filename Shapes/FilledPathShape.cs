using LibTessDotNet;
using System.Drawing;
using OpenTK.Mathematics;

namespace SVGtesselationShader.Shapes
{
    public class FilledPathShape : PathShape
    {
        private readonly List<ContourVertex[]> tessellatedVertices;

        public FilledPathShape() : base()
        {
            tessellatedVertices = new List<ContourVertex[]>();
        }

        public void Tessellate()
        {
            // Skip tessellation if there's no fill color
            if (!fillColor.HasValue)
            {
                Console.WriteLine("Skipping tessellation for path with no fill");
                tessellatedVertices.Clear();
                return;
            }
            
            var tess = new Tess();
            
            // Add the path as a contour
            var contour = GetContourVertices();
            if (contour.Length > 0)
            {
                tess.AddContour(contour);
            }

            // Tessellate using even-odd fill rule
            tess.Tessellate(WindingRule.EvenOdd, ElementType.Polygons, 3);

            // Convert tessellated vertices to triangles
            tessellatedVertices.Clear();
            var verts = tess.Vertices;
            var elems = tess.Elements;

            // Group vertices into triangles
            for (int i = 0; i < elems.Length; i += 3)
            {
                var triangle = new ContourVertex[3];
                for (int j = 0; j < 3; j++)
                {
                    triangle[j] = verts[elems[i + j]];
                }
                tessellatedVertices.Add(triangle);
            }
            
            Console.WriteLine($"Tessellated path into {tessellatedVertices.Count} triangles");
        }

        public override List<ContourVertex[]> GetTessellatedTriangles()
        {
            return tessellatedVertices;
        }

        public override List<(Point2D Start, Point2D End)> GetStrokeSegments()
        {
            return base.GetStrokeSegments();
        }

        public void TranslateTessellatedVertices(float dx, float dy)
        {
            if (tessellatedVertices == null) return;

            for (int i = 0; i < tessellatedVertices.Count; i++)
            {
                for (int j = 0; j < tessellatedVertices[i].Length; j++)
                {
                    var vertex = tessellatedVertices[i][j];
                    vertex.Position.X += dx;
                    vertex.Position.Y += dy;
                    tessellatedVertices[i][j] = vertex;
                }
            }
        }

        public override ContourVertex[] GetContourVertices()
        {
            // For tessellation, we need to ensure the path is closed
            var contourVertices = new List<ContourVertex>();
            
            if (vertices.Count > 0)
            {
                Console.WriteLine($"Creating contour with {vertices.Count} vertices");
                
                // Add all vertices to the contour
                foreach (var vertex in vertices)
                {
                    contourVertices.Add(new ContourVertex 
                    { 
                        Position = new Vec3(vertex.Point.X, vertex.Point.Y, 0) 
                    });
                }
                
                // Ensure the path is closed by checking if first and last points are the same
                if (contourVertices.Count > 1)
                {
                    var first = contourVertices[0];
                    var last = contourVertices[contourVertices.Count - 1];
                    
                    // Check if the first and last points are different
                    if (Math.Abs(first.Position.X - last.Position.X) > 0.001f || 
                        Math.Abs(first.Position.Y - last.Position.Y) > 0.001f)
                    {
                        Console.WriteLine($"Closing contour by adding first point: {first.Position.X}, {first.Position.Y}");
                        // Add the first point again to close the path
                        contourVertices.Add(first);
                    }
                }
            }
            
            return contourVertices.ToArray();
        }

        public override void Transform(Matrix4 transform)
        {
            base.Transform(transform);
            // Re-tessellate after transformation
            Tessellate();
        }
    }
} 