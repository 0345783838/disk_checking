using System.Drawing;

namespace DiskInspection.Domain
{
    public sealed class CameraInspectResult
    {
        public bool Ok { get; private set; }
        public Bitmap Origin { get; private set; }
        public Bitmap Result { get; private set; }

        public CameraInspectResult(bool ok, Bitmap origin, Bitmap result)
        {
            Ok = ok;
            Origin = origin;
            Result = result;
        }
    }
}