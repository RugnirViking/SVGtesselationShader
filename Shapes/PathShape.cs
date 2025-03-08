using LibTessDotNet;
using System.Drawing;
using System.Text.RegularExpressions;
using OpenTK.Mathematics;

namespace SVGtesselationShader.Shapes
{
    public class PathShape : BasicShape
    {
        private const int CURVE_SEGMENTS = 32;
        private bool isRelative;
        private Point2D currentPoint;

        public PathShape() : base()
        {
            currentPoint = new Point2D(0, 0);
        }

        public void ParsePathData(string pathData, string? style = null, float opacity = 1.0f)
        {
            if (string.IsNullOrEmpty(pathData))
            {
                Console.WriteLine("Warning: Empty path data provided");
                return;
            }
            
            Console.WriteLine($"Parsing path data: {pathData[..Math.Min(20, pathData.Length)]}...");
            
            if (style != null)
            {
                var styles = StyleParser.ParseStyle(style);
                
                // Handle fill color
                string? fillValue = styles.GetValueOrDefault("fill");
                fillColor = StyleParser.ParseColor(fillValue, opacity);
                
                // If fill is "none", explicitly set fillColor to null
                if (fillValue?.Trim().ToLowerInvariant() == "none")
                {
                    fillColor = null;
                }
                
                // Handle stroke color and width
                strokeColor = StyleParser.ParseColor(styles.GetValueOrDefault("stroke"), opacity);
                if (float.TryParse(styles.GetValueOrDefault("stroke-width", "1"), out float sw))
                {
                    strokeWidth = sw;
                }
            }

            var commands = Regex.Matches(pathData, @"([MmZzLlHhVvCcSsQqTtAa])([^MmZzLlHhVvCcSsQqTtAa]*)")
                .Cast<Match>()
                .Select(m => new {
                    Command = m.Groups[1].Value,
                    Args = ParseArguments(m.Groups[2].Value)
                }).ToList();

            Console.WriteLine($"Found {commands.Count} commands in path");
            
            vertices.Clear();
            currentPoint = new Point2D(0, 0);

            foreach (var cmd in commands)
            {
                isRelative = char.IsLower(cmd.Command[0]);
                Console.WriteLine($"Processing command: {cmd.Command} (relative: {isRelative})");
                ExecutePathCommand(cmd.Command, cmd.Args);
            }
            
            Console.WriteLine($"Path parsing complete. Total vertices: {vertices.Count}");
            if (vertices.Count > 0)
            {
                Console.WriteLine($"First vertex: {vertices[0].Point.X}, {vertices[0].Point.Y}");
                Console.WriteLine($"Last vertex: {vertices[^1].Point.X}, {vertices[^1].Point.Y}");
            }
        }

        private float[] ParseArguments(string argsStr)
        {
            try
            {
                Console.WriteLine($"Parsing arguments: '{argsStr}'");
                
                string normalizedStr = argsStr.Replace(",", " , ").Replace("  ", " ").Trim();
                var parts = normalizedStr.Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries);
                
                // Parse each part as a float
                var numbers = new List<float>();
                foreach (var part in parts)
                {
                    if (float.TryParse(part, System.Globalization.NumberStyles.Float, 
                                      System.Globalization.CultureInfo.InvariantCulture, out float value))
                    {
                        numbers.Add(value);
                    }
                    else
                    {
                        Console.WriteLine($"Warning: Could not parse '{part}' as a float");
                    }
                }
                
                return numbers.ToArray();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error parsing arguments '{argsStr}': {ex.Message}");
                return Array.Empty<float>();
            }
        }

        private void ExecutePathCommand(string command, float[] args)
        {
            try
            {
                string normalizedCommand = command.ToUpper();

                switch (normalizedCommand)
                {
                    case "M": // Move to
                        MoveTo(args);
                        break;
                    case "L": // Line to
                        LineTo(args);
                        break;
                    case "H": // Horizontal line
                        HorizontalLineTo(args);
                        break;
                    case "V": // Vertical line
                        VerticalLineTo(args);
                        break;
                    case "C": // Cubic Bézier curve
                        CubicBezierTo(args);
                        break;
                    case "S": // Smooth cubic Bézier
                        SmoothCubicBezierTo(args);
                        break;
                    case "Q": // Quadratic Bézier curve
                        QuadraticBezierTo(args);
                        break;
                    case "T": // Smooth quadratic Bézier
                        SmoothQuadraticBezierTo(args);
                        break;
                    case "A": // Elliptical arc
                        ArcTo(args);
                        break;
                    case "Z": // Close path
                        ClosePath();
                        break;
                    default:
                        Console.WriteLine($"Unknown command: {command} - ignoring");
                        break;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error executing command {command}: {ex.Message}");
                Console.WriteLine($"Arguments: [{string.Join(", ", args)}]");
            }
        }

        private Point2D GetAbsolutePoint(float x, float y)
        {
            if (isRelative)
            {
                return new Point2D(currentPoint.X + x, currentPoint.Y + y);
            }
            return new Point2D(x, y);
        }

        private void MoveTo(float[] args)
        {
            if (args.Length < 2)
            {
                Console.WriteLine($"Warning: MoveTo command requires at least 2 arguments but got {args.Length}");
                return;
            }
            
            for (int i = 0; i < args.Length; i += 2)
            {
                if (i + 1 >= args.Length) break;
                
                var point = GetAbsolutePoint(args[i], args[i + 1]);
                Console.WriteLine($"MoveTo: {point.X}, {point.Y}");
                
                // Always add the point to vertices, even if it's the first point
                vertices.Add(new CurvePoint(point));
                currentPoint = point;
            }
        }

        private void LineTo(float[] args)
        {
            
            if (args.Length < 2)
            {
                Console.WriteLine($"WARNING: LineTo command requires 2 arguments but got {args.Length}");
                
                return;
            }
            
            for (int i = 0; i < args.Length; i += 2)
            {
                if (i + 1 >= args.Length)
                {
                    Console.WriteLine($"WARNING: Incomplete coordinate pair at index {i}");
                    break;
                }
                
                var point = GetAbsolutePoint(args[i], args[i + 1]);
                Console.WriteLine($"Adding LineTo point: {point.X}, {point.Y}");
                vertices.Add(new CurvePoint(point));
                currentPoint = point;
            }
        }

        private void HorizontalLineTo(float[] args)
        {
            Console.WriteLine($"HorizontalLineTo called with {args.Length} arguments: [{string.Join(", ", args)}]");
            
            foreach (var x in args)
            {
                var newX = isRelative ? currentPoint.X + x : x;
                var point = new Point2D(newX, currentPoint.Y);
                Console.WriteLine($"Adding HorizontalLineTo point: {point.X}, {point.Y}");
                vertices.Add(new CurvePoint(point));
                currentPoint = point;
            }
        }

        private void VerticalLineTo(float[] args)
        {
            foreach (var y in args)
            {
                var newY = isRelative ? currentPoint.Y + y : y;
                var point = new Point2D(currentPoint.X, newY);
                vertices.Add(new CurvePoint(point));
                currentPoint = point;
            }
        }

        private void CubicBezierTo(float[] args)
        {
            for (int i = 0; i < args.Length; i += 6)
            {
                var cp1 = GetAbsolutePoint(args[i], args[i + 1]);
                var cp2 = GetAbsolutePoint(args[i + 2], args[i + 3]);
                var end = GetAbsolutePoint(args[i + 4], args[i + 5]);

                AddCubicBezierCurve(currentPoint, cp1, cp2, end);
                currentPoint = end;
            }
        }

        private void SmoothCubicBezierTo(float[] args)
        {
            for (int i = 0; i < args.Length; i += 4)
            {
                // Reflect previous control point
                var cp1 = currentPoint;
                if (vertices.Count > 0 && vertices[^1].ControlPoint2 != null)
                {
                    var prev = vertices[^1].ControlPoint2.Value;
                    cp1 = new Point2D(
                        2 * currentPoint.X - prev.X,
                        2 * currentPoint.Y - prev.Y
                    );
                }

                var cp2 = GetAbsolutePoint(args[i], args[i + 1]);
                var end = GetAbsolutePoint(args[i + 2], args[i + 3]);

                AddCubicBezierCurve(currentPoint, cp1, cp2, end);
                currentPoint = end;
            }
        }

        private void QuadraticBezierTo(float[] args)
        {
            for (int i = 0; i < args.Length; i += 4)
            {
                var cp = GetAbsolutePoint(args[i], args[i + 1]);
                var end = GetAbsolutePoint(args[i + 2], args[i + 3]);

                AddQuadraticBezierCurve(currentPoint, cp, end);
                currentPoint = end;
            }
        }

        private void SmoothQuadraticBezierTo(float[] args)
        {
            for (int i = 0; i < args.Length; i += 2)
            {
                // Reflect previous control point
                var cp = currentPoint;
                if (vertices.Count > 0 && vertices[^1].ControlPoint1 != null)
                {
                    var prev = vertices[^1].ControlPoint1.Value;
                    cp = new Point2D(
                        2 * currentPoint.X - prev.X,
                        2 * currentPoint.Y - prev.Y
                    );
                }

                var end = GetAbsolutePoint(args[i], args[i + 1]);
                
                AddQuadraticBezierCurve(currentPoint, cp, end);
                currentPoint = end;
            }
        }

        private void ArcTo(float[] args)
        {
            // Simplified arc implementation - converts to a series of line segments
            for (int i = 0; i < args.Length; i += 7)
            {
                var rx = args[i];
                var ry = args[i + 1];
                var xAxisRotation = args[i + 2];
                var largeArcFlag = args[i + 3] != 0;
                var sweepFlag = args[i + 4] != 0;
                var end = GetAbsolutePoint(args[i + 5], args[i + 6]);

                AddArcCurve(currentPoint, end, rx, ry, xAxisRotation, largeArcFlag, sweepFlag);
                currentPoint = end;
            }
        }

        private void ClosePath()
        {
            try
            {
                // Find the first point of the current subpath
                if (vertices.Count > 0)
                {
                    // Get the first point of the current subpath
                    // This is typically the last MoveTo point
                    Point2D firstPoint = new Point2D(0, 0);
                    bool foundFirstPoint = false;
                    
                    // Search backwards for the last MoveTo command
                    for (int i = vertices.Count - 1; i >= 0; i--)
                    {
                        // If we find a vertex that was created by a MoveTo, use that as our first point
                        if (vertices[i].ControlPoint1 == null && vertices[i].ControlPoint2 == null)
                        {
                            firstPoint = vertices[i].Point;
                            foundFirstPoint = true;
                            break;
                        }
                    }
                    
                    if (!foundFirstPoint && vertices.Count > 0)
                    {
                        // If we couldn't find a specific MoveTo, use the first vertex
                        firstPoint = vertices[0].Point;
                        foundFirstPoint = true;
                    }
                    
                    if (foundFirstPoint)
                    {
                        // Only add a closing point if the current point is different from the first point
                        if (!currentPoint.Equals(firstPoint))
                        {
                            Console.WriteLine($"Closing path from {currentPoint.X},{currentPoint.Y} to {firstPoint.X},{firstPoint.Y}");
                            vertices.Add(new CurvePoint(firstPoint));
                            currentPoint = firstPoint;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in ClosePath: {ex.Message}");
            }
        }

        private void AddCubicBezierCurve(Point2D start, Point2D cp1, Point2D cp2, Point2D end)
        {
            for (int i = 0; i <= CURVE_SEGMENTS; i++)
            {
                float t = i / (float)CURVE_SEGMENTS;
                float u = 1 - t;
                float tt = t * t;
                float uu = u * u;
                float uuu = uu * u;
                float ttt = tt * t;

                float x = uuu * start.X +
                         3 * uu * t * cp1.X +
                         3 * u * tt * cp2.X +
                         ttt * end.X;

                float y = uuu * start.Y +
                         3 * uu * t * cp1.Y +
                         3 * u * tt * cp2.Y +
                         ttt * end.Y;

                var point = new Point2D(x, y);
                var curvePoint = new CurvePoint(point);
                if (i == CURVE_SEGMENTS)
                {
                    curvePoint.ControlPoint1 = cp1;
                    curvePoint.ControlPoint2 = cp2;
                }
                vertices.Add(curvePoint);
            }
        }

        private void AddQuadraticBezierCurve(Point2D start, Point2D cp, Point2D end)
        {
            for (int i = 0; i <= CURVE_SEGMENTS; i++)
            {
                float t = i / (float)CURVE_SEGMENTS;
                float u = 1 - t;
                float tt = t * t;
                float uu = u * u;

                float x = uu * start.X +
                         2 * u * t * cp.X +
                         tt * end.X;

                float y = uu * start.Y +
                         2 * u * t * cp.Y +
                         tt * end.Y;

                var point = new Point2D(x, y);
                var curvePoint = new CurvePoint(point);
                if (i == CURVE_SEGMENTS)
                {
                    curvePoint.ControlPoint1 = cp;
                }
                vertices.Add(curvePoint);
            }
        }

        private void AddArcCurve(Point2D start, Point2D end, float rx, float ry, float angle,
            bool largeArcFlag, bool sweepFlag)
        {
            // Simplified arc implementation - converts to line segments
            // This is a basic implementation and could be improved for better arc rendering
            var center = new Point2D(
                (start.X + end.X) / 2,
                (start.Y + end.Y) / 2
            );

            for (int i = 0; i <= CURVE_SEGMENTS; i++)
            {
                float t = i / (float)CURVE_SEGMENTS;
                float angleRad = (float)(t * Math.PI * (sweepFlag ? 1 : -1));
                
                var point = new Point2D(
                    center.X + rx * (float)Math.Cos(angleRad),
                    center.Y + ry * (float)Math.Sin(angleRad)
                );
                
                vertices.Add(new CurvePoint(point));
            }
        }
    }
} 