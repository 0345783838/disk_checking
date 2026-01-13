using System.Drawing;

namespace DiskInspection.Domain
{
    public class CameraInspectResult
    {
        public bool IsOk { get; private set; }
        public Bitmap Origin { get; private set; }
        public Bitmap Result { get; private set; }


        public CameraInspectResult(bool isOk, Bitmap origin, Bitmap result)
        {
            IsOk = isOk;
            Origin = origin;
            Result = result;
        }
    }
}