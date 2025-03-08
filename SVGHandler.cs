using System.Xml.Linq;
using LibTessDotNet;
using SVGtesselationShader.Shapes;
using System.Drawing;
using OpenTK.Mathematics;

namespace SVGtesselationShader
{
    public class SVGHandler
    {
        private XDocument? svgDocument;
        private readonly XNamespace svgNs = "http://www.w3.org/2000/svg";
        private readonly XNamespace inkscapeNs = "http://www.inkscape.org/namespaces/inkscape";
        private List<IShape> shapes;

        public SVGHandler()
        {
            shapes = new List<IShape>();
        }

        public bool LoadSVG(string filePath)
        {
            try
            {
                svgDocument = XDocument.Load(filePath);
                ParseShapes();
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading SVG: {ex.Message}");
                return false;
            }
        }

        private void ParseShapes()
        {
            shapes.Clear();
            
            try
            {
                if (svgDocument?.Root == null) return;
                var svg = svgDocument.Root;
                
                // Get all shape elements in the order they appear in the SVG
                var shapeElements = svg.Descendants()
                    .Where(e => 
                        e.Name.LocalName == "rect" || 
                        e.Name.LocalName == "path" || 
                        e.Name.LocalName == "circle" || 
                        e.Name.LocalName == "ellipse")
                    .ToList();
                
                // Process each shape in order
                foreach (var element in shapeElements)
                {
                    try
                    {
                        string? style = element.Attribute("style")?.Value;
                        string elementType = element.Name.LocalName;
                        string? id = element.Attribute("id")?.Value;
                        
                        switch (elementType)
                        {
                            case "rect":
                                ParseRectangle(element, style);
                                break;
                            case "path":
                                ParsePath(element, style);
                                break;
                            case "circle":
                                ParseCircle(element, style);
                                break;
                            case "ellipse":
                                ParseEllipse(element, style);
                                break;
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error processing element {element.Name.LocalName}: {ex.Message}");
                    }
                }

                // After all shapes are parsed, normalize them relative to the global minimum point
                NormalizeAllShapesToTopLeft();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error parsing shapes: {ex.Message}");
            }
        }
        
        private void ParseRectangle(XElement rect, string? style)
        {
            float x = float.Parse(rect.Attribute("x")?.Value ?? "0");
            float y = float.Parse(rect.Attribute("y")?.Value ?? "0");
            float width = float.Parse(rect.Attribute("width")?.Value ?? "0");
            float height = float.Parse(rect.Attribute("height")?.Value ?? "0");
            
            // Parse opacity attributes
            float opacity = 1.0f;
            if (float.TryParse(rect.Attribute("opacity")?.Value ?? "1", out float elementOpacity))
            {
                opacity = elementOpacity;
            }

            var styles = StyleParser.ParseStyle(style);
            opacity *= StyleParser.ParseOpacity(styles);
            
            var shape = BasicShape.CreateRectangle(x, y, width, height, style, opacity);
            shapes.Add(shape);
        }
        
        private void ParsePath(XElement path, string? style)
        {
            string? pathData = path.Attribute("d")?.Value;
            if (string.IsNullOrEmpty(pathData))
            {
                Console.WriteLine("Path has no data attribute!");
                return;
            }
            
            Console.WriteLine($"Parsing path: id={path.Attribute("id")?.Value}");
            Console.WriteLine($"Path data: {pathData[..Math.Min(20, pathData.Length)]}...");
            
            // Parse opacity attributes
            float opacity = 1.0f;
            if (float.TryParse(path.Attribute("opacity")?.Value ?? "1", out float elementOpacity))
            {
                opacity = elementOpacity;
            }

            var styles = StyleParser.ParseStyle(style);
            opacity *= StyleParser.ParseOpacity(styles);
            
            // Check if the path should be filled
            bool shouldFill = true;
            Color? fillColor = null;
            
            if (style != null)
            {
                // Check if fill is explicitly set to "none"
                if (styles.TryGetValue("fill", out string? fillValue))
                {
                    shouldFill = fillValue.Trim().ToLowerInvariant() != "none";
                    fillColor = StyleParser.ParseColor(fillValue, opacity);
                }
            }
            
            if (shouldFill && fillColor != null)
            {
                Console.WriteLine($"Creating filled path with color: {fillColor}");
                var filledPath = new FilledPathShape();
                filledPath.ParsePathData(pathData, style, opacity);
                filledPath.Tessellate();
                shapes.Add(filledPath);
            }
            else
            {
                Console.WriteLine("Creating stroked path (no fill)");
                var strokedPath = new PathShape();
                strokedPath.ParsePathData(pathData, style, opacity);
                shapes.Add(strokedPath);
            }
        }
        
        private void ParseCircle(XElement circle, string? style)
        {
            float cx = float.Parse(circle.Attribute("cx")?.Value ?? "0");
            float cy = float.Parse(circle.Attribute("cy")?.Value ?? "0");
            float r = float.Parse(circle.Attribute("r")?.Value ?? "0");
            
            // Parse opacity attributes
            float opacity = 1.0f;
            if (float.TryParse(circle.Attribute("opacity")?.Value ?? "1", out float elementOpacity))
            {
                opacity = elementOpacity;
            }

            var styles = StyleParser.ParseStyle(style);
            opacity *= StyleParser.ParseOpacity(styles);
            
            var shape = BasicShape.CreateCircle(cx, cy, r, 32, style, opacity);
            shapes.Add(shape);
        }
        
        private void ParseEllipse(XElement ellipse, string? style)
        {
            float cx = float.Parse(ellipse.Attribute("cx")?.Value ?? "0");
            float cy = float.Parse(ellipse.Attribute("cy")?.Value ?? "0");
            float rx = float.Parse(ellipse.Attribute("rx")?.Value ?? "0");
            float ry = float.Parse(ellipse.Attribute("ry")?.Value ?? "0");
            
            // Parse opacity attributes
            float opacity = 1.0f;
            if (float.TryParse(ellipse.Attribute("opacity")?.Value ?? "1", out float elementOpacity))
            {
                opacity = elementOpacity;
            }

            var styles = StyleParser.ParseStyle(style);
            opacity *= StyleParser.ParseOpacity(styles);
            
            var shape = BasicShape.CreateEllipse(cx, cy, rx, ry, 32, style, opacity);
            shapes.Add(shape);
        }

        private void NormalizeAllShapesToTopLeft()
        {
            if (!shapes.Any()) return;

            // Find the global minimum x and y coordinates across all shapes
            float minX = float.MaxValue;
            float minY = float.MaxValue;

            // First pass: find global minimum coordinates
            foreach (var shape in shapes)
            {
                if (shape is BasicShape basicShape)
                {
                    foreach (var vertex in basicShape.vertices)
                    {
                        minX = Math.Min(minX, vertex.Point.X);
                        minY = Math.Min(minY, vertex.Point.Y);
                        
                        // Also check control points
                        if (vertex.ControlPoint1.HasValue)
                        {
                            minX = Math.Min(minX, vertex.ControlPoint1.Value.X);
                            minY = Math.Min(minY, vertex.ControlPoint1.Value.Y);
                        }
                        if (vertex.ControlPoint2.HasValue)
                        {
                            minX = Math.Min(minX, vertex.ControlPoint2.Value.X);
                            minY = Math.Min(minY, vertex.ControlPoint2.Value.Y);
                        }
                    }

                    // Also check tessellated vertices if it's a filled path
                    if (shape is FilledPathShape filledPath)
                    {
                        foreach (var vertex in filledPath.GetTessellatedTriangles().SelectMany(t => t))
                        {
                            minX = Math.Min(minX, vertex.Position.X);
                            minY = Math.Min(minY, vertex.Position.Y);
                        }
                    }
                }
            }

            Console.WriteLine($"Found minimum point: ({minX}, {minY})");

            // Second pass: translate all points
            foreach (var shape in shapes)
            {
                if (shape is BasicShape basicShape)
                {
                    foreach (var vertex in basicShape.vertices)
                    {
                        vertex.Point = new Point2D(
                            vertex.Point.X - minX,
                            vertex.Point.Y - minY
                        );
                        
                        // Also translate control points
                        if (vertex.ControlPoint1.HasValue)
                        {
                            vertex.ControlPoint1 = new Point2D(
                                vertex.ControlPoint1.Value.X - minX,
                                vertex.ControlPoint1.Value.Y - minY
                            );
                        }
                        if (vertex.ControlPoint2.HasValue)
                        {
                            vertex.ControlPoint2 = new Point2D(
                                vertex.ControlPoint2.Value.X - minX,
                                vertex.ControlPoint2.Value.Y - minY
                            );
                        }
                    }

                    // Also translate tessellated vertices if it's a filled path
                    if (shape is FilledPathShape filledPath)
                    {
                        filledPath.TranslateTessellatedVertices(-minX, -minY);
                    }
                }
            }
        }

        public IEnumerable<IShape> GetShapes()
        {
            return shapes;
        }

        public (float width, float height) GetDimensions()
        {
            if (svgDocument?.Root == null) return (800, 600); // Default dimensions
            
            var svg = svgDocument.Root;
            if (svg == null) return (0, 0);

            string widthStr = svg.Attribute("width")?.Value ?? "0";
            string heightStr = svg.Attribute("height")?.Value ?? "0";

            // Convert common units to pixels (approximate)
            float ParseWithUnits(string value)
            {
                value = value.Trim().ToLower();
                if (value.EndsWith("px")) return float.Parse(value[..^2]);
                if (value.EndsWith("mm")) return float.Parse(value[..^2]) * 3.779528f; // Convert mm to pixels (96 DPI)
                if (value.EndsWith("cm")) return float.Parse(value[..^2]) * 37.79528f; // Convert cm to pixels
                if (value.EndsWith("in")) return float.Parse(value[..^2]) * 96f; // Convert inches to pixels
                if (value.EndsWith("pt")) return float.Parse(value[..^2]) * 1.333333f; // Convert points to pixels
                // For numbers without units, assume pixels
                return float.Parse(new string(value.TakeWhile(c => char.IsDigit(c) || c == '.' || c == '-').ToArray()));
            }

            try
            {
                return (
                    ParseWithUnits(widthStr),
                    ParseWithUnits(heightStr)
                );
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error parsing dimensions: {ex.Message}");
                return (800, 600); // Default fallback size
            }
        }

        public (float x, float y, float width, float height) GetViewBox()
        {
            if (svgDocument?.Root == null) return (0, 0, 800, 600); // Default viewBox
            
            var svg = svgDocument.Root;

            // If no viewBox is specified, try to use width and height
            string? viewBox = svg.Attribute("viewBox")?.Value;
            if (string.IsNullOrEmpty(viewBox))
            {
                var (width, height) = GetDimensions();
                return (0, 0, width, height);
            }

            try
            {
                var values = viewBox.Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(v => float.Parse(v.Trim()))
                    .ToArray();

                if (values.Length >= 4)
                {
                    return (values[0], values[1], values[2], values[3]);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error parsing viewBox: {ex.Message}");
            }

            // Fallback to dimensions if viewBox parsing fails
            var dims = GetDimensions();
            return (0, 0, dims.width, dims.height);
        }
    }
} 