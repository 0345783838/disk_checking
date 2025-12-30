using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DiskInspection.Models
{
    public class ImageDebugInfo
    {
        public int ID { get; set; }
        public string FilePath { get; set; }
        public Bitmap Crop { get; set; }
        public Bitmap Mask { get; set; }
        public Bitmap Final { get; set; }
        public bool Status { get; set; }
        public ImageDebugInfo(int id, string filePath)
        {
            ID = id;
            FilePath = filePath;
        }
    }
}
