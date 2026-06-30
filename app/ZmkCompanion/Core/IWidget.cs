using System.Drawing;

namespace ZmkCompanion.Core;

// A widget knows how to render itself into a region of the 68×160 canvas.
// Firing Invalidated asks the compositor to re-render and send a new frame.
interface IWidget : IDisposable
{
    // Region of the 68×160 canvas this widget occupies.
    Rectangle Bounds { get; set; }

    // Fired on the UI thread when the widget's content has changed.
    event Action? Invalidated;

    void Start();
    void Stop();

    // Renders into `g` clipped to Bounds. Background is already black.
    void Render(Graphics g);
}
