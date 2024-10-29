using System.IO;

namespace Wacs.Core
{
    public interface IRenderable
    {
        void RenderText(StreamWriter writer, Module module, string indent);
    }
}