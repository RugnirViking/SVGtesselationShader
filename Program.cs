using OpenTK.Mathematics;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;

namespace SVGtesselationShader
{
    class Program
    {
        static void Main(string[] args)
        {
            // Load and analyze SVG
            var svgHandler = new SVGHandler();
            Console.WriteLine("Loading SVG file...");
            if (!svgHandler.LoadSVG("signeheart.svg"))
            {
                Console.WriteLine("Failed to load SVG file!");
                return;
            }

            var nativeWindowSettings = new NativeWindowSettings()
            {
                ClientSize = new Vector2i(800, 600),
                Title = "SVG Tessellation Demo",
                WindowBorder = WindowBorder.Hidden,
                WindowState = WindowState.Fullscreen,
                // This is needed to run on macos
                Flags = ContextFlags.ForwardCompatible,
            };

            using (var renderer = new Renderer(GameWindowSettings.Default, nativeWindowSettings, svgHandler))
            {
                renderer.Run();
            }
        }
    }
}
