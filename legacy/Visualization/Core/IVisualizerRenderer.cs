using System.Drawing;

namespace Spectralis;

internal interface IVisualizerRenderer
{
    void Draw(Graphics graphics, Rectangle bounds, VisualizerScene scene);
}
