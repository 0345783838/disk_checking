using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DiskInspection.Models
{
    enum FileStatus
    {
        OK = 0,
        NG = 1,
        NOT_DONE = 2
    }
    public class ImageDebugInfo
    {
        public int ID { get; set; }
        public string FilePath { get; set; }
        public Bitmap Crop { get; set; }
        public Bitmap Mask { get; set; }
        public Bitmap Final { get; set; }
        public int Status { get; set; }
        public ImageDebugInfo(int id, string filePath)
        {
            ID = id;
            FilePath = filePath;
            Status = (int)FileStatus.NOT_DONE;
        }
    }
}
