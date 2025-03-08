using LibTessDotNet;
using System.Drawing;
using OpenTK.Mathematics;

namespace SVGtesselationShader.Shapes
{
    public class BasicShape : IShape
    {
        protected internal List<CurvePoint> vertices;
        protected Color? fillColor;
        protected Color? strokeColor;
        protected float strokeWidth;

        public Color? FillColor => fillColor;
        public Color? StrokeColor => strokeColor;
        public float StrokeWidth => strokeWidth;

        public BasicShape()
        {
            vertices = new List<CurvePoint>();
            strokeWidth = 1.0f;
        }

        protected void NormalizeVerticesToTopLeft()
        {
            if (vertices.Count == 0) return;

            // Find minimum x and y coordinates
            float minX = vertices.Min(v => v.Point.X);
            float minY = vertices.Min(v => v.Point.Y);

            // Offset all vertices by the minimum coordinates
            for (int i = 0; i < vertices.Count; i++)
            {
                var v = vertices[i];
                v.Point = new Point2D(v.Point.X - minX, v.Point.Y - minY);
                
                // Also adjust control points if they exist
                if (v.ControlPoint1.HasValue)
                {
                    v.ControlPoint1 = new Point2D(
                        v.ControlPoint1.Value.X - minX,
                        v.ControlPoint1.Value.Y - minY
                    );
                }
                if (v.ControlPoint2.HasValue)
                {
                    v.ControlPoint2 = new Point2D(
                        v.ControlPoint2.Value.X - minX,
                        v.ControlPoint2.Value.Y - minY
                    );
                }
            }
        }

        public virtual ContourVertex[] GetContourVertices()
        {
            return vertices.Select(v => new ContourVertex { Position = v.Point.ToVec3() }).ToArray();
        }

        public virtual List<ContourVertex[]> GetTessellatedTriangles()
        {
            var results = new List<ContourVertex[]>();
            if (vertices.Count < 3 || fillColor == null || fillColor.Value.A == 0)
                return results;

            var contourVertices = GetContourVertices();
            
            // For simple shapes, we can just triangulate manually (as a fan)
            for (int i = 1; i < contourVertices.Length - 1; i++)
            {
                results.Add(new[] {
                    contourVertices[0],
                    contourVertices[i],
                    contourVertices[i + 1]
                });
            }
            
            return results;
        }

        public virtual List<(Point2D Start, Point2D End)> GetStrokeSegments()
        {
            var results = new List<(Point2D Start, Point2D End)>();
            if (vertices.Count < 2 || strokeColor == null || strokeColor.Value.A == 0 || strokeWidth <= 0)
                return results;
                
            // Create line segments from each vertex to the next
            for (int i = 0; i < vertices.Count - 1; i++)
            {
                results.Add((vertices[i].Point, vertices[i + 1].Point));
            }
            
            return results;
        }

        public virtual void Transform(Matrix4 transform)
        {
            for (int i = 0; i < vertices.Count; i++)
            {
                var v = vertices[i];
                var transformed = transform * new Vector4(v.Point.X, v.Point.Y, 0, 1);
                v.Point = new Point2D(transformed.X, transformed.Y);

                if (v.ControlPoint1.HasValue)
                {
                    var cp1 = transform * new Vector4(v.ControlPoint1.Value.X, v.ControlPoint1.Value.Y, 0, 1);
                    v.ControlPoint1 = new Point2D(cp1.X, cp1.Y);
                }

                if (v.ControlPoint2.HasValue)
                {
                    var cp2 = transform * new Vector4(v.ControlPoint2.Value.X, v.ControlPoint2.Value.Y, 0, 1);
                    v.ControlPoint2 = new Point2D(cp2.X, cp2.Y);
                }
            }
        }

        public static BasicShape CreateRectangle(float x, float y, float width, float height, string? style = null, float opacity = 1.0f)
        {
            var rect = new BasicShape();
            var firstPoint = new Point2D(x, y);
            
            rect.vertices.AddRange(new[]
            {
                new CurvePoint(firstPoint),
                new CurvePoint(new Point2D(x + width, y)),
                new CurvePoint(new Point2D(x + width, y + height)),
                new CurvePoint(new Point2D(x, y + height)),
                new CurvePoint(firstPoint)
            });

            if (style != null)
            {
                var styles = StyleParser.ParseStyle(style);
                rect.fillColor = StyleParser.ParseColor(styles.GetValueOrDefault("fill"), opacity);
                rect.strokeColor = StyleParser.ParseColor(styles.GetValueOrDefault("stroke"), opacity);
                if (float.TryParse(styles.GetValueOrDefault("stroke-width", "1"), out float sw))
                {
                    rect.strokeWidth = sw;
                }
            }

            return rect;
        }

        public static BasicShape CreateCircle(float cx, float cy, float r, int segments = 32, string? style = null, float opacity = 1.0f)
        {
            var circle = new BasicShape();
            
            for (int i = 0; i <= segments; i++)
            {
                float angle = (float)(2 * Math.PI * i / segments);
                circle.vertices.Add(new CurvePoint(new Point2D(
                    cx + r * (float)Math.Cos(angle),
                    cy + r * (float)Math.Sin(angle)
                )));
            }

            if (style != null)
            {
                var styles = StyleParser.ParseStyle(style);
                circle.fillColor = StyleParser.ParseColor(styles.GetValueOrDefault("fill"), opacity);
                circle.strokeColor = StyleParser.ParseColor(styles.GetValueOrDefault("stroke"), opacity);
                if (float.TryParse(styles.GetValueOrDefault("stroke-width", "1"), out float sw))
                {
                    circle.strokeWidth = sw;
                }
            }

            return circle;
        }

        public static BasicShape CreateEllipse(float cx, float cy, float rx, float ry, int segments = 32, string? style = null, float opacity = 1.0f)
        {
            var ellipse = new BasicShape();
            
            for (int i = 0; i <= segments; i++)
            {
                float angle = (float)(2 * Math.PI * i / segments);
                ellipse.vertices.Add(new CurvePoint(new Point2D(
                    cx + rx * (float)Math.Cos(angle),
                    cy + ry * (float)Math.Sin(angle)
                )));
            }

            if (style != null)
            {
                var styles = StyleParser.ParseStyle(style);
                ellipse.fillColor = StyleParser.ParseColor(styles.GetValueOrDefault("fill"), opacity);
                ellipse.strokeColor = StyleParser.ParseColor(styles.GetValueOrDefault("stroke"), opacity);
                if (float.TryParse(styles.GetValueOrDefault("stroke-width", "1"), out float sw))
                {
                    ellipse.strokeWidth = sw;
                }
            }

            return ellipse;
        }
    }
} 