using LibTessDotNet;
using System.Drawing;
using OpenTK.Mathematics;

namespace SVGtesselationShader.Shapes
{
    public interface IShape
    {
        // Get vertices for tessellation
        ContourVertex[] GetContourVertices();
        
        // Get fill color if any
        Color? FillColor { get; }
        
        // Get stroke color and width if any
        Color? StrokeColor { get; }
        float StrokeWidth { get; }
        
        // Transform the shape
        void Transform(Matrix4 transform);

        // Get tessellated triangles for batch rendering
        List<ContourVertex[]> GetTessellatedTriangles();
        
        // Get stroke segments for batch rendering
        List<(Point2D Start, Point2D End)> GetStrokeSegments();
    }

    // Helper struct to store a 2D point
    public struct Point2D
    {
        public float X { get; set; }
        public float Y { get; set; }

        public Point2D(float x, float y)
        {
            X = x;
            Y = y;
        }

        public Vec3 ToVec3()
        {
            return new Vec3(X, Y, 0);
        }

        public static bool operator ==(Point2D a, Point2D b)
        {
            return a.X == b.X && a.Y == b.Y;
        }

        public static bool operator !=(Point2D a, Point2D b)
        {
            return !(a == b);
        }

        public override bool Equals(object? obj)
        {
            if (obj is Point2D other)
            {
                return this == other;
            }
            return false;
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(X, Y);
        }
    }

    // Separate class for curve control points to avoid struct cycle
    public class CurvePoint
    {
        public Point2D Point { get; set; }
        public Point2D? ControlPoint1 { get; set; }
        public Point2D? ControlPoint2 { get; set; }

        public CurvePoint(Point2D point)
        {
            Point = point;
            ControlPoint1 = null;
            ControlPoint2 = null;
        }

        public CurvePoint(Point2D point, Point2D? cp1, Point2D? cp2)
        {
            Point = point;
            ControlPoint1 = cp1;
            ControlPoint2 = cp2;
        }
    }

    // Helper class to parse SVG style attributes
    public static class StyleParser
    {
        public static Color? ParseColor(string? colorStr, float opacity = 1.0f)
        {
            if (string.IsNullOrEmpty(colorStr)) return null;
            
            // Handle "none" value explicitly
            if (colorStr.Trim().ToLowerInvariant() == "none")
            {
                return null;
            }

            // Handle hex colors
            if (colorStr.StartsWith("#"))
            {
                var hex = colorStr.TrimStart('#');
                if (int.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out int argb))
                {
                    return Color.FromArgb(
                        (int)(opacity * 255),  // Apply opacity to alpha channel
                        (argb >> 16) & 0xFF,
                        (argb >> 8) & 0xFF,
                        argb & 0xFF
                    );
                }
            }

            // Handle named colors
            try
            {
                var color = Color.FromName(colorStr);
                if (opacity < 1.0f)
                {
                    // Create a new color with the specified opacity
                    return Color.FromArgb((int)(opacity * 255), color);
                }
                return color;
            }
            catch
            {
                return null;
            }
        }

        public static float ParseOpacity(Dictionary<string, string> styles)
        {
            float opacity = 1.0f;
            
            // Check for general opacity
            if (styles.TryGetValue("opacity", out string? opacityStr))
            {
                float.TryParse(opacityStr, out opacity);
            }
            
            return opacity;
        }

        public static Dictionary<string, string> ParseStyle(string? styleStr)
        {
            var result = new Dictionary<string, string>();
            if (string.IsNullOrEmpty(styleStr)) return result;

            var pairs = styleStr.Split(';', StringSplitOptions.RemoveEmptyEntries);
            foreach (var pair in pairs)
            {
                var parts = pair.Split(':', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 2)
                {
                    result[parts[0].Trim()] = parts[1].Trim();
                }
            }
            return result;
        }
    }
} 